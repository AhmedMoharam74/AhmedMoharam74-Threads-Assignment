using System.Diagnostics;
using System.Collections.Concurrent;
using System.Text.Json;
using OrderProcessing.Phase1;
using OrderProcessing.Shared;

namespace OrderProcessing.Phase2;

public sealed class Phase2Runner(JsonFileStore store, DataSeeder seeder)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await seeder.ResetAsync(ct);
        await RunUnsafeRaceAsync(ct);
        await seeder.ResetAsync(ct);
        await RunSafeRaceAsync(ct);
        await seeder.ResetAsync(ct);

        List<Order> incoming = await store.ReadAsync<Order>(DataFiles.IncomingOrders,ct);
        var validator=new Phase1Runner(store,seeder);
        List<Task> validationTasks=incoming.Select(order=>validator.ValidateAsync(order,ct)).ToList();
        foreach(Task validation in validationTasks)await validation;
        List<Order> orders = await store.ReadAsync<Order>(DataFiles.Orders, ct);
        Order first = orders.Single(x => x.Id == 1);
        var pricing = new PricingService(store);
        int sequentialPoolBefore = ThreadPool.ThreadCount;
        var sequential = Stopwatch.StartNew();
        await pricing.PriceSequentialAsync(first, ct);
        sequential.Stop();
        Console.WriteLine($"Pricing sequential threads={pricing.ObservedThreadIds.Count}; ThreadPool.ThreadCount before={sequentialPoolBefore}, after={ThreadPool.ThreadCount}");
        pricing.ResetObservedThreads();
        first = orders.Single(x => x.Id == 1);
        int concurrentPoolBefore = ThreadPool.ThreadCount;
        var concurrent = Stopwatch.StartNew();
        await pricing.PriceAsync(first, ct);
        concurrent.Stop();
        Console.WriteLine($"Pricing concurrent threads={pricing.ObservedThreadIds.Count}; ThreadPool.ThreadCount before={concurrentPoolBefore}, after={ThreadPool.ThreadCount}");
        Console.WriteLine($"Pricing order #1: {first.SubTotal:F2} / {first.Discount:F2} / {first.ShippingCost:F2} / {first.TotalAmount:F2}");
        Console.WriteLine($"Pricing sequential={sequential.ElapsedMilliseconds} ms; concurrent={concurrent.ElapsedMilliseconds} ms");

        orders = await store.ReadAsync<Order>(DataFiles.Orders, ct);
        List<Order> validated = orders.Where(x => x.Status == OrderStatus.Validated).ToList();
        var watch = Stopwatch.StartNew();
        var processingThreadIds = new ConcurrentDictionary<int, byte>();
        int processingPoolBefore = ThreadPool.ThreadCount;
        List<Task<bool>> tasks = validated.Select(x => ProcessAsync(x, ct)).ToList();
        if (tasks.Count > 0) Console.WriteLine($"First task immediately: {tasks[0].Status}");
        await Task.Delay(1000, ct);
        foreach (Task<bool> task in tasks) Console.WriteLine($"Task while running: {task.Status}");
        foreach (Task<bool> task in tasks)
        {
            try { await task; }
            catch (Exception ex) { Console.WriteLine($"Processing task failed: {ex.Message}"); }
        }
        watch.Stop();
        List<Order> final = await store.ReadAsync<Order>(DataFiles.Orders, ct);
        Console.WriteLine($"Concurrent processing took {watch.Elapsed.TotalSeconds:F1}s; orders.json={final.Count} records");
        Console.WriteLine($"Processing distinct threads observed={processingThreadIds.Count}; ThreadPool.ThreadCount before={processingPoolBefore}, after={ThreadPool.ThreadCount}");
        Console.WriteLine($"Completed={final.Count(x=>x.Status==OrderStatus.Completed)}, Rejected={final.Count(x=>x.Status==OrderStatus.Rejected)}, Failed={final.Count(x=>x.Status==OrderStatus.Failed)}");
        Task<Report> reportTask=GenerateReportAsync(final,ct);
        Console.WriteLine("Generating report...");
        Report report=await reportTask;
        Console.WriteLine($"Report: {report.TotalOrders} orders; revenue={report.TotalRevenue:F2}; top={report.TopSellingProduct} x{report.TopSellingQuantity}");

        async Task<Report> GenerateReportAsync(List<Order> ordersForReport,CancellationToken token)
        {
            await Task.Delay(200,token);await Task.Delay(400,token);
            var completed=ordersForReport.Where(x=>x.Status==OrderStatus.Completed).ToList();
            var productsForReport=await store.ReadAsync<Product>(DataFiles.Products,token);
            var quantities=completed.SelectMany(o=>o.Items).GroupBy(x=>x.ProductId).Select(g=>new{Id=g.Key,Qty=g.Sum(x=>x.Quantity)}).OrderByDescending(x=>x.Qty).ThenBy(x=>x.Id).FirstOrDefault();
            var result=new Report{TotalOrders=ordersForReport.Count,CompletedCount=completed.Count,RejectedCount=ordersForReport.Count(x=>x.Status==OrderStatus.Rejected),FailedCount=ordersForReport.Count(x=>x.Status==OrderStatus.Failed),PaymentFailedCount=ordersForReport.Count(x=>x.Status==OrderStatus.PaymentFailed),TotalRevenue=completed.Sum(x=>x.TotalAmount),AverageOrderValue=completed.Count==0?0:completed.Average(x=>x.TotalAmount),TopSellingProduct=productsForReport.FirstOrDefault(x=>x.Id==quantities?.Id)?.Name??"",TopSellingQuantity=quantities?.Qty??0,GeneratedAt=DateTime.Now};
            Directory.CreateDirectory(Path.Combine(DataFiles.Root,"reports"));
            string reportPath=Path.Combine("reports",$"report_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            await File.WriteAllTextAsync(DataFiles.PathFor(reportPath),JsonSerializer.Serialize(result,JsonFileStore.JsonOptions),token);
            return result;
        }

        async Task<bool> ProcessAsync(Order order, CancellationToken token)
        {
            processingThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
            var reserved = new List<Product>();
            try
            {
            foreach (OrderItem item in order.Items)
            {
                Product? p = await Reserve(item.ProductId, item.Quantity, token);
                if (p is null)
                {
                    foreach (Product held in reserved) await Release(held.Id, order.Items.Single(x=>x.ProductId==held.Id).Quantity, token);
                    Product? name = (await store.ReadAsync<Product>(DataFiles.Products, token)).FirstOrDefault(x=>x.Id==item.ProductId);
                    order.Status=OrderStatus.Rejected; order.FailureReason=$"Insufficient stock for '{name?.Name ?? "unknown"}'";
                    await Save(order, token); return false;
                }
                reserved.Add(p);
            }
                await pricing.PriceAsync(order, token);
                processingThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                if (order.Id == 7) throw new ShippingProviderUnavailableException("Shipping provider unavailable");
                await Task.Run(async () =>
                {
                    long value=0; for (int i=0;i<20_000_000;i++){if((i&65535)==0)token.ThrowIfCancellationRequested();value=(value+i)%97;}
                    Directory.CreateDirectory(Path.Combine(DataFiles.Root,"invoices"));
                    await File.WriteAllTextAsync(Path.Combine(DataFiles.Root,"invoices",$"invoice_{order.Id}.txt"),$"Order {order.Id}; Total {order.TotalAmount:F2}; checksum {value}",token);
                }, token);
                processingThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                order.Status=OrderStatus.Completed;
                await Save(order,token);
                processingThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
                return true;
            }
            catch (OperationCanceledException)
            {
                foreach (Product held in reserved) await Release(held.Id,order.Items.Single(x=>x.ProductId==held.Id).Quantity,CancellationToken.None);
                order.Status=OrderStatus.Pending;order.FailureReason="Cancelled";await Save(order,CancellationToken.None);throw;
            }
            catch (Exception ex)
            {
                foreach (Product held in reserved) await Release(held.Id,order.Items.Single(x=>x.ProductId==held.Id).Quantity,token);
                order.Status=OrderStatus.Failed; order.FailureReason=ex.Message;
                await Save(order,token); throw;
            }
        }

        async Task<Product?> Reserve(int id,int qty,CancellationToken token)
        {
            await Task.Delay(200,token); await Task.Delay(100,token); await Task.Delay(400,token); await Task.Delay(200,token);
            return await store.UpdateAsync<Product,Product?>(DataFiles.Products, list => { Product? p=list.FirstOrDefault(x=>x.Id==id); if(p is null||p.StockQuantity<qty)return null; p.StockQuantity-=qty; return p; },token);
        }
        async Task Release(int id,int qty,CancellationToken token)
        {
            await Task.Delay(200,token); await Task.Delay(100,token); await Task.Delay(400,token); await Task.Delay(200,token);
            await store.UpdateAsync<Product,bool>(DataFiles.Products,list=>{var p=list.First(x=>x.Id==id);p.StockQuantity+=qty;return true;},token);
        }
        async Task Save(Order order,CancellationToken token) => await new OrderRepository(store).SaveAsync(order,token);
    }

    private async Task RunUnsafeRaceAsync(CancellationToken ct)
    {
        var products=await store.ReadAsync<Product>(DataFiles.Products,ct); Product keyboard=products.Single(x=>x.Id==4); keyboard.StockQuantity=1; await store.WriteAsync(DataFiles.Products,products,ct);
        async Task<bool> Unsafe(CancellationToken token){var before=(await store.ReadAsync<Product>(DataFiles.Products,token)).Single(x=>x.Id==4).StockQuantity;await Task.Delay(700,token);if(before<1)return false;var list=await store.ReadAsync<Product>(DataFiles.Products,token);list.Single(x=>x.Id==4).StockQuantity=before-1;await store.WriteAsync(DataFiles.Products,list,token);return true;}
        List<Task<bool>> tasks=[Unsafe(ct),Unsafe(ct)]; bool a=await tasks[0];bool b=await tasks[1];Console.WriteLine($"Unsafe keyboard race: {a}, {b}; stock={(await store.ReadAsync<Product>(DataFiles.Products,ct)).Single(x=>x.Id==4).StockQuantity}");
    }
    private async Task RunSafeRaceAsync(CancellationToken ct)
    {
        var products=await store.ReadAsync<Product>(DataFiles.Products,ct);products.Single(x=>x.Id==4).StockQuantity=1;await store.WriteAsync(DataFiles.Products,products,ct);
        async Task<bool> Safe(CancellationToken token){await Task.Delay(700,token);return await store.UpdateAsync<Product,bool>(DataFiles.Products,list=>{var p=list.Single(x=>x.Id==4);if(p.StockQuantity<1)return false;p.StockQuantity--;return true;},token);}
        List<Task<bool>> tasks=[Safe(ct),Safe(ct)];bool a=await tasks[0];bool b=await tasks[1];Console.WriteLine($"Safe keyboard race: {a}, {b}; stock={(await store.ReadAsync<Product>(DataFiles.Products,ct)).Single(x=>x.Id==4).StockQuantity}");
    }
}

public sealed class PricingService(JsonFileStore store, bool failOrder7Shipping = true)
{
    public ConcurrentDictionary<int, byte> ObservedThreadIds { get; } = new();
    public void ResetObservedThreads() => ObservedThreadIds.Clear();
    private void TrackThread() => ObservedThreadIds.TryAdd(Environment.CurrentManagedThreadId, 0);

    public async Task PriceSequentialAsync(Order order,CancellationToken ct)
    {
        TrackThread(); order.SubTotal=await Subtotal(order,ct); TrackThread(); order.Discount=await Discount(order,ct); TrackThread(); order.ShippingCost=await Shipping(order,ct); TrackThread(); order.TotalAmount=Math.Round(order.SubTotal-order.Discount+order.ShippingCost,2,MidpointRounding.AwayFromZero);
    }
    public async Task PriceAsync(Order order,CancellationToken ct)
    {
        TrackThread(); order.SubTotal=await Subtotal(order,ct); TrackThread();
        Task<decimal> discount=Discount(order,ct); Task<decimal> shipping=Shipping(order,ct);
        order.Discount=await discount; TrackThread(); order.ShippingCost=await shipping; TrackThread();
        order.TotalAmount=Math.Round(order.SubTotal-order.Discount+order.ShippingCost,2,MidpointRounding.AwayFromZero);
    }
    private async Task<decimal> Subtotal(Order order,CancellationToken ct)
    {
        TrackThread(); await Task.Delay(200,ct);TrackThread();await Task.Delay(400,ct);TrackThread();var products=await store.ReadAsync<Product>(DataFiles.Products,ct);TrackThread();decimal sum=0;
        foreach(var item in order.Items){var p=products.First(x=>x.Id==item.ProductId);item.UnitPrice=p.Price;sum+=p.Price*item.Quantity;}order.SubTotal=Round(sum);return order.SubTotal;
    }
    private async Task<decimal> Discount(Order order,CancellationToken ct)
    {
        TrackThread();await Task.Delay(150,ct);TrackThread();await Task.Delay(300,ct);TrackThread();await Task.Delay(Random.Shared.Next(500,1501),ct);TrackThread();
        var customer=(await store.ReadAsync<Customer>(DataFiles.Customers,ct)).FirstOrDefault(x=>x.Id==order.CustomerId);int loyalty=customer?.LoyaltyTier switch{LoyaltyTier.Gold=>10,LoyaltyTier.Silver=>5,_=>0};int coupon=0;
        if(order.CouponCode is not null){var c=(await store.ReadAsync<Coupon>(DataFiles.Coupons,ct)).FirstOrDefault(x=>x.Code.Equals(order.CouponCode,StringComparison.OrdinalIgnoreCase));if(c is null)throw new InvalidCouponException($"Coupon '{order.CouponCode}' does not exist");if(c.ExpiryDate.Date<DateTime.Today)throw new InvalidCouponException($"Coupon '{order.CouponCode}' has expired");coupon=c.DiscountPercent;}
        return Round(order.SubTotal*Math.Min(30,loyalty+coupon)/100m);
    }
    private async Task<decimal> Shipping(Order order,CancellationToken ct)
    {
        TrackThread();await Task.Delay(150,ct);TrackThread();if(failOrder7Shipping && order.Id==7)throw new ShippingProviderUnavailableException("Shipping provider unavailable");
        await Task.Delay(300,ct);TrackThread();await Task.Delay(Random.Shared.Next(500,1501),ct);TrackThread();
        if(order.SubTotal>=1000)return 0;var products=await store.ReadAsync<Product>(DataFiles.Products,ct);int quantity=order.Items.Sum(x=>x.Quantity);decimal weight=order.Items.Sum(x=>products.Single(p=>p.Id==x.ProductId).WeightKg*x.Quantity);return Round(5m+1.5m*quantity+2m*weight);
    }
    private static decimal Round(decimal x)=>Math.Round(x,2,MidpointRounding.AwayFromZero);
}
