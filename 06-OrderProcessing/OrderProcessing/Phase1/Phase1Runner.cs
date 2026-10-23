using System.Diagnostics;
using System.Collections.Concurrent;
using OrderProcessing.Shared;

namespace OrderProcessing.Phase1;

public sealed class Phase1Runner(JsonFileStore store, DataSeeder seeder)
{
    public async Task RunAsync(CancellationToken ct)
    {
        await seeder.ResetAsync(ct);
        List<Order> source = await store.ReadAsync<Order>(DataFiles.IncomingOrders, ct);
        var syncThreads = new ConcurrentDictionary<int, byte>();
        int syncPoolBefore = ThreadPool.ThreadCount;
        var syncWatch = Stopwatch.StartNew();
        foreach (Order order in source) ValidateSync(order, syncThreads);
        syncWatch.Stop();

        await seeder.ResetAsync(ct);
        source = await store.ReadAsync<Order>(DataFiles.IncomingOrders, ct);
        var asyncThreads = new ConcurrentDictionary<int, byte>();
        int asyncPoolBefore = ThreadPool.ThreadCount;
        var asyncTotalWatch = Stopwatch.StartNew();
        var asyncWatch = Stopwatch.StartNew();
        foreach (Order order in source) await ValidateAsync(order, ct, saveResult: false, threadIds: asyncThreads);
        asyncWatch.Stop();

        var saveWatch = Stopwatch.StartNew();
        var orderRepository = new OrderRepository(store);
        var saveThreads = new ConcurrentDictionary<int, byte>();
        int savePoolBefore = ThreadPool.ThreadCount;
        foreach (Order order in source)
        {
            saveThreads.TryAdd(Environment.CurrentManagedThreadId, 0);
            await orderRepository.SaveAsync(order, ct);
            saveThreads.TryAdd(Environment.CurrentManagedThreadId, 0);
        }
        saveWatch.Stop();
        asyncTotalWatch.Stop();

        List<Order> saved = await store.ReadAsync<Order>(DataFiles.Orders, ct);
        int validated = (await orderRepository.GetByStatusAsync(OrderStatus.Validated, ct)).Count;
        int rejected = (await orderRepository.GetByStatusAsync(OrderStatus.Rejected, ct)).Count;
        Console.WriteLine("===== Validation Summary =====");
        Console.WriteLine($"Total Orders: {saved.Count}");
        Console.WriteLine($"Validated: {validated}");
        Console.WriteLine($"Rejected: {rejected}");
        foreach (Order order in saved.Where(x => x.Status == OrderStatus.Rejected)) Console.WriteLine($"- Order #{order.Id}: {order.FailureReason}");
        Console.WriteLine($"Sync Time: {syncWatch.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"Sync distinct threads: {syncThreads.Count}; ThreadPool.ThreadCount before={syncPoolBefore}, after={ThreadPool.ThreadCount}");
        Console.WriteLine($"Async Time: {asyncWatch.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"Async validation distinct threads: {asyncThreads.Count}; ThreadPool.ThreadCount before={asyncPoolBefore}, after={ThreadPool.ThreadCount}");
        Console.WriteLine("(Async Time measures sequential validation only, so it is comparable to Sync Time.)");
        Console.WriteLine($"Async Save Time: {saveWatch.Elapsed.TotalSeconds:F1} seconds");
        Console.WriteLine($"Async save distinct threads observed: {saveThreads.Count}; ThreadPool.ThreadCount before={savePoolBefore}, after={ThreadPool.ThreadCount}");
        Console.WriteLine($"Async Total Time: {asyncTotalWatch.Elapsed.TotalSeconds:F1} seconds (validation + save)");
        Console.WriteLine($"orders.json now contains {saved.Count} records.");
    }

    private void ValidateSync(Order order, ConcurrentDictionary<int, byte> threadIds)
    {
        threadIds.TryAdd(Environment.CurrentManagedThreadId, 0);
        Thread.Sleep(200);
        Customer? customer = store.Read<Customer>(DataFiles.Customers).FirstOrDefault(x => x.Id == order.CustomerId);
        if (customer is null) { Reject("Customer #" + order.CustomerId + " not found"); return; }
        if (!customer.IsActive) { Reject("Customer is inactive"); return; }
        List<Product> products = store.Read<Product>(DataFiles.Products);
        foreach (OrderItem item in order.Items)
        {
            Thread.Sleep(300);
            Product? product = products.FirstOrDefault(x => x.Id == item.ProductId);
            if (product is null) { Reject($"Product #{item.ProductId} not found"); return; }
            if (product.StockQuantity < item.Quantity) { Reject($"Insufficient stock for '{product.Name}'"); return; }
        }
        if (order.CouponCode is not null)
        {
            Thread.Sleep(300);
            Coupon? coupon = store.Read<Coupon>(DataFiles.Coupons).FirstOrDefault(x => x.Code.Equals(order.CouponCode, StringComparison.OrdinalIgnoreCase));
            if (coupon is null) { Reject($"Coupon '{order.CouponCode}' does not exist"); return; }
            if (coupon.ExpiryDate.Date < DateTime.Today) { Reject($"Coupon '{order.CouponCode}' has expired"); return; }
        }
        order.Status = OrderStatus.Validated;
        return;
        void Reject(string reason) { order.Status = OrderStatus.Rejected; order.FailureReason = reason; }
    }

    public async Task ValidateAsync(Order order, CancellationToken ct, bool saveResult = true, ConcurrentDictionary<int, byte>? threadIds = null)
    {
        threadIds?.TryAdd(Environment.CurrentManagedThreadId, 0);
        await Task.Delay(200, ct);
        threadIds?.TryAdd(Environment.CurrentManagedThreadId, 0);
        Customer? customer = (await store.ReadAsync<Customer>(DataFiles.Customers, ct)).FirstOrDefault(x => x.Id == order.CustomerId);
        threadIds?.TryAdd(Environment.CurrentManagedThreadId, 0);
        if (customer is null) { Reject("Customer #" + order.CustomerId + " not found"); await SaveIfRequested(ct); return; }
        if (!customer.IsActive) { Reject("Customer is inactive"); await SaveIfRequested(ct); return; }
        List<Product> products = await store.ReadAsync<Product>(DataFiles.Products, ct);
        foreach (OrderItem item in order.Items)
        {
            await Task.Delay(300, ct);
            threadIds?.TryAdd(Environment.CurrentManagedThreadId, 0);
            Product? product = products.FirstOrDefault(x => x.Id == item.ProductId);
            if (product is null) { Reject($"Product #{item.ProductId} not found"); await SaveIfRequested(ct); return; }
            if (product.StockQuantity < item.Quantity) { Reject($"Insufficient stock for '{product.Name}'"); await SaveIfRequested(ct); return; }
        }
        if (order.CouponCode is not null)
        {
            await Task.Delay(300, ct);
            threadIds?.TryAdd(Environment.CurrentManagedThreadId, 0);
            Coupon? coupon = (await store.ReadAsync<Coupon>(DataFiles.Coupons, ct)).FirstOrDefault(x => x.Code.Equals(order.CouponCode, StringComparison.OrdinalIgnoreCase));
            if (coupon is null) { Reject($"Coupon '{order.CouponCode}' does not exist"); await SaveIfRequested(ct); return; }
            if (coupon.ExpiryDate.Date < DateTime.Today) { Reject($"Coupon '{order.CouponCode}' has expired"); await SaveIfRequested(ct); return; }
        }
        order.Status = OrderStatus.Validated;
        await SaveIfRequested(ct);
        return;
        void Reject(string reason) { order.Status = OrderStatus.Rejected; order.FailureReason = reason; }
        async Task SaveIfRequested(CancellationToken saveToken)
        {
            if (saveResult) await new OrderRepository(store).SaveAsync(order, saveToken);
        }
    }
}
