using System.Diagnostics;
using System.Collections.Concurrent;
using OrderProcessing.Phase1;
using OrderProcessing.Phase2;
using OrderProcessing.Shared;

namespace OrderProcessing.Phase3;

public sealed class Phase3Runner
{
    private readonly JsonFileStore store;
    private readonly DataSeeder seeder;
    private readonly IPaymentGateway paymentGateway;
    private readonly IEmailService emailService;
    private readonly OrderRepository orderRepository;

    public Phase3Runner(JsonFileStore store,DataSeeder seeder,IPaymentGateway? paymentGateway=null,IEmailService? emailService=null,OrderRepository? orderRepository=null,bool disableFailures=false)
    {
        this.store=store;this.seeder=seeder;
        this.paymentGateway=paymentGateway??new PaymentGateway(store,disableFailures?new HashSet<int>():new HashSet<int>{5},disableFailures?new HashSet<int>():new HashSet<int>{8});
        this.emailService=emailService??new EmailService(store,disableFailures?new Dictionary<int,int>():new Dictionary<int,int>{{3,2},{17,int.MaxValue}});
        this.orderRepository=orderRepository??new OrderRepository(store,disableFailures?new HashSet<int>():new HashSet<int>{13});
    }

    public async Task RunAsync(CancellationToken ct, bool completionOrder = false, bool comparisonMode = false)
    {
        await seeder.ResetAsync(ct);
        var pricing = new PricingService(store, failOrder7Shipping:false);
        List<Order> incoming = await store.ReadAsync<Order>(DataFiles.IncomingOrders,ct);
        if (comparisonMode) incoming = incoming.Take(10).ToList();
        IPaymentGateway activePaymentGateway = comparisonMode
            ? new PaymentGateway(store, new HashSet<int>(), new HashSet<int>()) : paymentGateway;
        IEmailService activeEmailService = comparisonMode
            ? new EmailService(store, new Dictionary<int, int>()) : emailService;
        OrderRepository activeOrderRepository = comparisonMode
            ? new OrderRepository(store) : orderRepository;
        var tasks = new List<Task<bool>>();
        var taskOrders = new Dictionary<Task<bool>, Order>();
        var threadIds = new ConcurrentDictionary<int, byte>();
        int threadPoolBefore = ThreadPool.ThreadCount;
        var runWatch=Stopwatch.StartNew();
        int startCount=0;
        foreach(Order order in incoming)
        {
            if(ct.IsCancellationRequested) break;
            threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
            Task<bool> task = ProcessAsync(order,ct);
            tasks.Add(task);
            taskOrders.Add(task, order);
            startCount++;
        }
        int finished=0,cancelled=0;
        if (completionOrder)
        {
            int line = 0;
            await foreach (Task<bool> task in Task.WhenEach(tasks))
            {
                try { await task; finished++; }
                catch (OperationCanceledException) { cancelled++; }
                catch (Exception ex) { Console.WriteLine($"Unexpected order error: {ex.Message}"); }
                Order order = taskOrders[task];
                Console.WriteLine($"[{++line,2}/20] Order #{order.Id} -> {order.Status} ({order.FailureReason ?? "completed"}) after {runWatch.Elapsed.TotalSeconds:F1} s");
            }
        }
        else
        {
            foreach(Task<bool> task in tasks)
            {
                try{await task;finished++;}
                catch(OperationCanceledException){cancelled++;}
                catch(Exception ex){Console.WriteLine($"Unexpected order error: {ex.Message}");}
            }
        }
        if(ct.IsCancellationRequested)
        {
            foreach(Order notStarted in incoming.Skip(startCount))
            {
                notStarted.Status=OrderStatus.Pending; notStarted.FailureReason="Cancelled";
                await new OrderRepository(store).SaveAsync(notStarted,CancellationToken.None);
                cancelled++;
            }
        }
        List<Order> result=await store.ReadAsync<Order>(DataFiles.Orders,CancellationToken.None);
        threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
        if (comparisonMode)
            Console.WriteLine($"Async comparison: {runWatch.ElapsedMilliseconds} ms; distinct threads observed={threadIds.Count}; ThreadPool.ThreadCount before={threadPoolBefore}, after={ThreadPool.ThreadCount}");
        if(ct.IsCancellationRequested)Console.WriteLine($"Run cancelled — {finished} orders finished, {cancelled} cancelled");
        Console.WriteLine("Order | Status | Total | Reason");
        foreach(var o in result.OrderBy(x=>x.Id))Console.WriteLine($"{o.Id,2} | {o.Status,-13} | {o.TotalAmount,8:F2} | {o.FailureReason}");
        Console.WriteLine($"Completed={result.Count(x=>x.Status==OrderStatus.Completed)}, Rejected={result.Count(x=>x.Status==OrderStatus.Rejected)}, PaymentFailed={result.Count(x=>x.Status==OrderStatus.PaymentFailed)}, Failed={result.Count(x=>x.Status==OrderStatus.Failed)}");

        async Task<bool> ProcessAsync(Order order,CancellationToken token)
        {
            threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
            var timer=Stopwatch.StartNew();
            var reserved=new List<OrderItem>();
            string? transaction=null;
            try
            {
                Log.Info($"Order #{order.Id} -> Step 1/6 customer lookup");
                await Task.Delay(200,token);await Task.Delay(300,token);threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                Customer? customer=(await store.ReadAsync<Customer>(DataFiles.Customers,token)).FirstOrDefault(x=>x.Id==order.CustomerId);
                threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                if(customer is null)return await Reject($"Customer #{order.CustomerId} not found",token);
                if(!customer.IsActive)return await Reject("Customer is inactive",token);

                foreach(OrderItem item in order.Items)
                {
                    var acquired=await store.UpdateAsync<Product,bool>(DataFiles.Products,list=>{var p=list.FirstOrDefault(x=>x.Id==item.ProductId);if(p is null||p.StockQuantity<item.Quantity)return false;p.StockQuantity-=item.Quantity;return true;},token);
                    threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                    if(!acquired){Product? p=(await store.ReadAsync<Product>(DataFiles.Products,token)).FirstOrDefault(x=>x.Id==item.ProductId);await RollbackStock(token);return await Reject($"Insufficient stock for '{p?.Name??"unknown"}'",token);}
                    reserved.Add(item);
                }
                await pricing.PriceAsync(order,token);
                threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                PaymentResult payment=await activePaymentGateway.ProcessPaymentAsync(order.Id,order.TotalAmount,token);
                threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                if(!payment.Success)
                {
                    await RollbackStock(token);order.Status=OrderStatus.PaymentFailed;order.FailureReason=payment.Message;await Save(order,token);return false;
                }
                transaction=payment.TransactionId;
                order.TransactionId=transaction;order.Status=OrderStatus.Completed;
                await Save(order,token);
                threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                await activeEmailService.SendConfirmationAsync(order,customer,token);
                threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                timer.Stop();Log.Success($"Order #{order.Id} -> completed in {timer.ElapsedMilliseconds} ms");return true;
            }
            catch(OperationCanceledException)
            {
                await RollbackStock(CancellationToken.None);
                if(transaction is not null)await activePaymentGateway.RefundAsync(transaction,CancellationToken.None);
                order.Status=OrderStatus.Pending;order.FailureReason="Cancelled";await Save(order,CancellationToken.None);throw;
            }
            catch(DatabaseWriteException ex)
            {
                if(transaction is not null)await activePaymentGateway.RefundAsync(transaction,CancellationToken.None);
                await RollbackStock(CancellationToken.None);order.Status=OrderStatus.Failed;order.FailureReason=ex.Message;await Save(order,CancellationToken.None);return false;
            }
            catch(InvalidCouponException ex)
            {
                await RollbackStock(token);return await Reject(ex.Message,token);
            }

            async Task<bool> Reject(string reason,CancellationToken rejectToken){order.Status=OrderStatus.Rejected;order.FailureReason=reason;await Save(order,rejectToken);return false;}
            async Task Save(Order o,CancellationToken saveToken){await activeOrderRepository.SaveAsync(o,saveToken);}
            async Task RollbackStock(CancellationToken cleanupToken){foreach(var item in reserved){await store.UpdateAsync<Product,bool>(DataFiles.Products,list=>{var p=list.First(x=>x.Id==item.ProductId);p.StockQuantity+=item.Quantity;return true;},cleanupToken);}reserved.Clear();}
        }
    }

    public async Task RunComparisonAsync(CancellationToken ct)
    {
        await seeder.ResetAsync(ct);
        List<Order> incoming = (await store.ReadAsync<Order>(DataFiles.IncomingOrders, ct)).Take(10).ToList();
        var customers = store.Read<Customer>(DataFiles.Customers);
        var products = store.Read<Product>(DataFiles.Products);
        var coupons = store.Read<Coupon>(DataFiles.Coupons);
        var stockGate = new object();
        var storeGate = new object();
        var syncThreadIds = new ConcurrentDictionary<int, byte>();
        int poolBefore = ThreadPool.ThreadCount;
        var syncWatch = Stopwatch.StartNew();
        var workers = incoming.Select(order => new Thread(() =>
        {
            syncThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
            ProcessSynchronously(order);
        })).ToList();
        foreach (Thread worker in workers) worker.Start();
        foreach (Thread worker in workers) worker.Join();
        syncWatch.Stop();
        Console.WriteLine($"Sync comparison: {syncWatch.ElapsedMilliseconds} ms; distinct threads={syncThreadIds.Count}; ThreadPool.ThreadCount before={poolBefore}, after={ThreadPool.ThreadCount}");
        PrintSummary(store.Read<Order>(DataFiles.Orders));

        Console.WriteLine("--- Async comparison: 10 tasks ---");
        await new Phase3Runner(store, seeder, disableFailures: true).RunAsync(ct, comparisonMode: true);

        void ProcessSynchronously(Order order)
        {
            Thread.Sleep(200);
            Thread.Sleep(Random.Shared.Next(300, 601));
            Customer? customer = customers.FirstOrDefault(x => x.Id == order.CustomerId);
            if (customer is null) { Reject($"Customer #{order.CustomerId} not found"); return; }
            if (!customer.IsActive) { Reject("Customer is inactive"); return; }

            var reserved = new List<OrderItem>();
            lock (stockGate)
            {
                foreach (OrderItem item in order.Items)
                {
                    Thread.Sleep(200); Thread.Sleep(100); Thread.Sleep(400); Thread.Sleep(200);
                    Product? product = products.FirstOrDefault(x => x.Id == item.ProductId);
                    if (product is null || product.StockQuantity < item.Quantity)
                    {
                        foreach (OrderItem held in reserved) products.Single(x => x.Id == held.ProductId).StockQuantity += held.Quantity;
                        Reject($"Insufficient stock for '{product?.Name ?? "unknown"}'");
                        return;
                    }
                    product.StockQuantity -= item.Quantity;
                    reserved.Add(item);
                }
            }

            try
            {
                Thread.Sleep(200); Thread.Sleep(400);
                decimal subtotal = 0;
                foreach (OrderItem item in order.Items)
                {
                    Product product = products.Single(x => x.Id == item.ProductId);
                    item.UnitPrice = product.Price;
                    subtotal += product.Price * item.Quantity;
                }
                order.SubTotal = Math.Round(subtotal, 2, MidpointRounding.AwayFromZero);
                Thread.Sleep(150); Thread.Sleep(300); Thread.Sleep(Random.Shared.Next(500, 1501));
                int loyalty = customer.LoyaltyTier switch { LoyaltyTier.Gold => 10, LoyaltyTier.Silver => 5, _ => 0 };
                int couponPercent = 0;
                if (order.CouponCode is not null)
                {
                    Coupon? coupon = coupons.FirstOrDefault(x => x.Code.Equals(order.CouponCode, StringComparison.OrdinalIgnoreCase));
                    if (coupon is null || coupon.ExpiryDate.Date < DateTime.Today)
                    {
                        foreach (OrderItem held in reserved) products.Single(x => x.Id == held.ProductId).StockQuantity += held.Quantity;
                        Reject(coupon is null ? $"Coupon '{order.CouponCode}' does not exist" : $"Coupon '{order.CouponCode}' has expired");
                        return;
                    }
                    couponPercent = coupon.DiscountPercent;
                }
                order.Discount = Math.Round(order.SubTotal * Math.Min(30, loyalty + couponPercent) / 100m, 2, MidpointRounding.AwayFromZero);
                Thread.Sleep(150); Thread.Sleep(300); Thread.Sleep(Random.Shared.Next(500, 1501));
                int quantity = order.Items.Sum(x => x.Quantity);
                decimal weight = order.Items.Sum(x => products.Single(p => p.Id == x.ProductId).WeightKg * x.Quantity);
                order.ShippingCost = order.SubTotal >= 1000 ? 0 : Math.Round(5m + 1.5m * quantity + 2m * weight, 2, MidpointRounding.AwayFromZero);
                order.TotalAmount = Math.Round(order.SubTotal - order.Discount + order.ShippingCost, 2, MidpointRounding.AwayFromZero);

                Thread.Sleep(800); Thread.Sleep(500); Thread.Sleep(700);
                string transactionId = "TXN-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
                order.TransactionId = transactionId;
                lock (storeGate)
                {
                    List<PaymentTransaction> payments = store.Read<PaymentTransaction>(DataFiles.Payments);
                    payments.Add(new PaymentTransaction { OrderId = order.Id, Amount = order.TotalAmount, Status = PaymentStatus.Captured, TransactionId = transactionId, CreatedAt = DateTime.Now });
                    store.Write(DataFiles.Payments, payments);
                }
                order.Status = OrderStatus.Completed;
                Thread.Sleep(200); Thread.Sleep(100); Thread.Sleep(400); Thread.Sleep(200);
                Save(order);
                Thread.Sleep(200); Thread.Sleep(300); Thread.Sleep(500);
                lock (storeGate)
                {
                    List<EmailMessage> emails = store.Read<EmailMessage>(DataFiles.Emails);
                    emails.Add(new EmailMessage { Id = Guid.NewGuid(), OrderId = order.Id, To = customer.Email, Subject = $"Order #{order.Id} confirmed", Body = $"Your order #{order.Id} is confirmed.", SentAt = DateTime.Now, Attempts = 1 });
                    store.Write(DataFiles.Emails, emails);
                }
            }
            catch
            {
                lock (stockGate)
                    foreach (OrderItem held in reserved) products.Single(x => x.Id == held.ProductId).StockQuantity += held.Quantity;
                throw;
            }

            void Reject(string reason) { order.Status = OrderStatus.Rejected; order.FailureReason = reason; Save(order); }
            void Save(Order value)
            {
                lock (storeGate)
                {
                    List<Order> saved = store.Read<Order>(DataFiles.Orders);
                    int index = saved.FindIndex(x => x.Id == value.Id);
                    if (index < 0) saved.Add(value); else saved[index] = value;
                    store.Write(DataFiles.Orders, saved);
                    store.Write(DataFiles.Products, products);
                }
            }
        }

        static void PrintSummary(List<Order> orders)
        {
            Console.WriteLine($"Sync comparison result: Completed={orders.Count(x => x.Status == OrderStatus.Completed)}, Rejected={orders.Count(x => x.Status == OrderStatus.Rejected)} (expected 8 and 2)");
        }
    }
}
