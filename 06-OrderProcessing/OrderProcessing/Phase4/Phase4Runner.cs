using System.Collections.Concurrent;
using System.Diagnostics;
using OrderProcessing.Phase3;
using OrderProcessing.Shared;

namespace OrderProcessing.Phase4;

public sealed class Phase4Runner(JsonFileStore store, DataSeeder seeder)
{
    public async Task RunAsync(CancellationToken ct)
    {
        // 10.1: checkout completion is streamed as each task finishes.
        await new Phase3Runner(store,seeder).RunAsync(ct,completionOrder:true);
        await DashboardAsync(3,ct);
        await DashboardAsync(99,ct);

        var orders=await store.ReadAsync<Order>(DataFiles.Orders,ct);
        var quoteService=new ShippingQuoteService();
        quoteService.ResetObservedThreads();
        int shippingPoolBefore=ThreadPool.ThreadCount;
        var shippingWatch=Stopwatch.StartNew();
        for(int id=1;id<=5;id++)
        {
            Order order=orders.Single(x=>x.Id==id);var q=await quoteService.GetFastestQuoteAsync(order,ct);
            Console.WriteLine($"Order #{id}: {q.Carrier} {q.Price:F2} in {q.ElapsedMs} ms");
        }
        shippingWatch.Stop();
        Console.WriteLine($"Shipping quotes #1-#5: elapsed={shippingWatch.ElapsedMilliseconds} ms; distinct threads observed={quoteService.ObservedThreadIds.Count}; ThreadPool.ThreadCount before={shippingPoolBefore}, after={ThreadPool.ThreadCount}");
        try{await quoteService.GetFastestQuoteAsync(orders.Single(x=>x.Id==6),ct,new HashSet<int>{6});}
        catch(ShippingProviderUnavailableException ex){Console.WriteLine($"All-carriers-failed demo: {ex.Message}");}

        await FraudScoreAsync(ct);
        await ResendEmailsAsync(ct);
    }

    private async Task DashboardAsync(int customerId,CancellationToken ct)
    {
        var service=new CustomerDashboardService(store, customerId==99?new HashSet<int>{99}:new HashSet<int>());
        int poolBefore=ThreadPool.ThreadCount;
        var watch=Stopwatch.StartNew();
        try
        {
            CustomerDashboard dashboard=await service.LoadAsync(customerId,ct);watch.Stop();
            string orderSummary=string.Join(", ",dashboard.Orders.OrderBy(x=>x.Id).Select(x=>$"#{x.Id} {x.Status}"));
            string paymentSummary=string.Join(", ",dashboard.Payments.OrderBy(x=>x.OrderId).Select(x=>$"#{x.OrderId} {x.Status}"));
            Console.WriteLine($"Customer #{customerId} {dashboard.Customer?.Name} — {dashboard.Orders.Count} orders ({orderSummary}), {dashboard.Payments.Count} payments ({paymentSummary}), {dashboard.Emails.Count} emails in {watch.ElapsedMilliseconds} ms");
            Console.WriteLine($"Dashboard #{customerId}: distinct threads observed={service.ObservedThreadIds.Count}; ThreadPool.ThreadCount before={poolBefore}, after={ThreadPool.ThreadCount}");
        }
        catch(AggregateException ex)
        {
            watch.Stop();foreach(Exception error in ex.Flatten().InnerExceptions)Console.WriteLine($"Dashboard error: {error.Message}");
            Console.WriteLine($"Dashboard loads finished in {watch.ElapsedMilliseconds} ms");
            Console.WriteLine($"Dashboard #{customerId}: distinct threads observed={service.ObservedThreadIds.Count}; ThreadPool.ThreadCount before={poolBefore}, after={ThreadPool.ThreadCount}");
        }
    }

    private static int Score(int orderId,CancellationToken ct)
    {
        long h=orderId;
        for(int i=0;i<100_000;i++){if((i&1023)==0)ct.ThrowIfCancellationRequested();h=(h*31+i)%1_000_000_007;}
        return (int)(h%100);
    }

    private static async Task FraudScoreAsync(CancellationToken ct)
    {
        var sequentialWatch=Stopwatch.StartNew();var sequential=new List<int>();
        for(int id=1;id<=10_000;id++)if(Score(id,ct)>=90)sequential.Add(id);
        sequentialWatch.Stop();
        var parallelWatch=Stopwatch.StartNew();var parallel=new ConcurrentBag<int>();var threadIds=new ConcurrentDictionary<int,byte>();
        await Parallel.ForEachAsync(Enumerable.Range(1,10_000),new ParallelOptions{MaxDegreeOfParallelism=Environment.ProcessorCount,CancellationToken=ct},(id,token)=>
        {
            threadIds.TryAdd(Environment.CurrentManagedThreadId,0);if(Score(id,token)>=90)parallel.Add(id);return ValueTask.CompletedTask;
        });
        parallelWatch.Stop();var sorted=parallel.Order().ToArray();
        bool sameIds=sequential.SequenceEqual(sorted);
        Console.WriteLine($"Fraud: sequential={sequential.Count}, parallel={sorted.Length}, sameIds={sameIds}, first={string.Join(", ",sorted.Take(5))}");
        if(sequential.Count!=1005||!sameIds||!sorted.Take(5).SequenceEqual(new[]{4,8,12,29,33}))throw new InvalidOperationException("Fraud scoring results did not match the assignment values.");
        Console.WriteLine($"Sequential={sequentialWatch.ElapsedMilliseconds} ms; parallel={parallelWatch.ElapsedMilliseconds} ms; speed-up={(double)sequentialWatch.ElapsedMilliseconds/Math.Max(1,parallelWatch.ElapsedMilliseconds):F2}x; worker threads={threadIds.Count}; processors={Environment.ProcessorCount}; ThreadPool.ThreadCount={ThreadPool.ThreadCount}");
    }

    private async Task ResendEmailsAsync(CancellationToken ct)
    {
        var ids=Enumerable.Range(1001,30).ToArray();int active=0,max=0;
        IEmailService emailService=new EmailService(store,new Dictionary<int,int>());
        var threadIds=new ConcurrentDictionary<int,byte>();
        int poolBefore=ThreadPool.ThreadCount;
        var watch=Stopwatch.StartNew();
        await Parallel.ForEachAsync(ids,new ParallelOptions{MaxDegreeOfParallelism=4,CancellationToken=ct},async (orderId,token)=>
        {
            int running=Interlocked.Increment(ref active);int seen;
            do{seen=max;if(running<=seen)break;}while(Interlocked.CompareExchange(ref max,running,seen)!=seen);
            try
            {
                threadIds.TryAdd(Environment.CurrentManagedThreadId,0);
                var testOrder=new Order{Id=orderId,CustomerId=0};
                var testCustomer=new Customer{Id=0,Name="Resend Test",Email=$"test.order{orderId}@example.com",IsActive=true};
                await emailService.SendConfirmationAsync(testOrder,testCustomer,token);
                threadIds.TryAdd(Environment.CurrentManagedThreadId,0);
            }
            finally{Interlocked.Decrement(ref active);}
        });
        watch.Stop();var saved=await store.ReadAsync<EmailMessage>(DataFiles.Emails,ct);var matches=saved.Where(e=>e.OrderId is >=1001 and <=1030).ToList();
        Console.WriteLine($"Email resend: {matches.Count}/30 records, {matches.Select(x=>x.OrderId).Distinct().Count()}/30 distinct orders, max simultaneous={max}, time={watch.Elapsed.TotalSeconds:F1}s, distinct threads observed={threadIds.Count}, ThreadPool.ThreadCount before={poolBefore}, after={ThreadPool.ThreadCount}");
    }
}

public sealed class CustomerDashboardService(JsonFileStore store,IReadOnlySet<int> failPaymentsForCustomerIds)
{
    public ConcurrentDictionary<int,byte> ObservedThreadIds { get; } = new();
    private void TrackThread()=>ObservedThreadIds.TryAdd(Environment.CurrentManagedThreadId,0);

    public async Task<CustomerDashboard> LoadAsync(int customerId,CancellationToken ct)
    {
        Task<Customer> profile=Load(async()=>
        {
            var xs=await store.ReadAsync<Customer>(DataFiles.Customers,ct);return xs.FirstOrDefault(x=>x.Id==customerId)??throw new CustomerNotFoundException($"Customer #{customerId} not found");
        },ct);
        Task<List<Order>> orders=Load(async()=> (await store.ReadAsync<Order>(DataFiles.Orders,ct)).Where(x=>x.CustomerId==customerId).ToList(),ct);
        Task<List<PaymentTransaction>> payments=Load(async()=>
        {
            if(failPaymentsForCustomerIds.Contains(customerId))throw new TimeoutException("Payments store timeout");
            return await store.ReadAsync<PaymentTransaction>(DataFiles.Payments,ct);
        },ct);
        Task<List<EmailMessage>> emails=Load(async()=> (await store.ReadAsync<EmailMessage>(DataFiles.Emails,ct)).ToList(),ct);
        Task all=Task.WhenAll(profile,orders,payments,emails);
        try{await all;}
        catch
        {
            if(ct.IsCancellationRequested)throw new OperationCanceledException(ct);
            var errors=all.Exception?.Flatten().InnerExceptions;
            if(errors is not null)throw new AggregateException(errors);
            throw;
        }
        var orderList=await orders;var paymentsAll=await payments;var emailAll=await emails;
        var customer=await profile;
        var orderIds=orderList.Select(x=>x.Id).ToHashSet();
        return new CustomerDashboard{Customer=customer,Orders=orderList,Payments=paymentsAll.Where(x=>orderIds.Contains(x.OrderId)).ToList(),Emails=emailAll.Where(x=>orderIds.Contains(x.OrderId)).ToList()};

        async Task<T> Load<T>(Func<Task<T>> work,CancellationToken token){TrackThread();await Task.Delay(200,token);TrackThread();await Task.Delay(300,token);TrackThread();T result=await work();TrackThread();return result;}
    }
}

public sealed class ShippingQuoteService
{
    public ConcurrentDictionary<int,byte> ObservedThreadIds { get; } = new();
    public void ResetObservedThreads()=>ObservedThreadIds.Clear();
    private void TrackThread()=>ObservedThreadIds.TryAdd(Environment.CurrentManagedThreadId,0);

    public async Task<ShippingQuote> GetFastestQuoteAsync(Order order,CancellationToken ct,IReadOnlySet<int>? failForOrderIds=null)
    {
        TrackThread();
        var watch=Stopwatch.StartNew();using var run=CancellationTokenSource.CreateLinkedTokenSource(ct);run.CancelAfter(1200);
        IShippingCarrier[] carriers=[new FastShipCarrier(failForOrderIds,TrackThread),new CairoExpressCarrier(failForOrderIds,TrackThread),new NileCourierCarrier(failForOrderIds,TrackThread)];
        var pending=carriers.Select(carrier=>(Name:carrier.Name,Task:carrier.GetQuoteAsync(order,run.Token))).ToList();
        Task budget=Task.Delay(1200,ct);
        while(pending.Count>0)
        {
            ct.ThrowIfCancellationRequested();
            Task<Task<decimal>> any=Task.WhenAny(pending.Select(x=>x.Task));
            Task winner=await Task.WhenAny(any,budget);TrackThread();
            if(winner==budget){run.Cancel();await Observe(CancellationToken.None);ct.ThrowIfCancellationRequested();watch.Stop();return new ShippingQuote("Flat rate",25.00m,watch.ElapsedMilliseconds);}
            Task<decimal> completed=await any;var item=pending.First(x=>ReferenceEquals(x.Task,completed));pending.Remove(item);
            try
            {
                decimal price=await completed;run.Cancel();await Observe(CancellationToken.None);watch.Stop();return new ShippingQuote(item.Name,price,watch.ElapsedMilliseconds);
            }
            catch(OperationCanceledException) when(!ct.IsCancellationRequested){if(run.IsCancellationRequested){await Observe(CancellationToken.None);watch.Stop();return new ShippingQuote("Flat rate",25m,watch.ElapsedMilliseconds);}}
            catch(ShippingProviderUnavailableException){ }
        }
        watch.Stop();throw new ShippingProviderUnavailableException("All carriers failed");

        async Task Observe(CancellationToken token){token.ThrowIfCancellationRequested();foreach(var p in pending){try{await p.Task;}catch{}}}
    }
}


public interface IShippingCarrier
{
    string Name { get; }
    Task<decimal> GetQuoteAsync(Order order,CancellationToken ct);
}

public abstract class ShippingCarrierBase(IReadOnlySet<int>? failForOrderIds,Action? trackThread) : IShippingCarrier
{
    protected abstract decimal Factor { get; }
    protected abstract int MinDelayMs { get; }
    protected abstract int MaxDelayMs { get; }
    public abstract string Name { get; }
    protected virtual bool BuiltInFailure(Order order)=>false;

    public async Task<decimal> GetQuoteAsync(Order order,CancellationToken ct)
    {
        try
        {
            trackThread?.Invoke();await Task.Delay(150,ct);trackThread?.Invoke();
            if(failForOrderIds?.Contains(order.Id)==true||BuiltInFailure(order))throw new ShippingProviderUnavailableException($"{Name} unavailable");
            await Task.Delay(Random.Shared.Next(MinDelayMs,MaxDelayMs+1),ct);trackThread?.Invoke();
            return Math.Round(order.ShippingCost*Factor,2,MidpointRounding.AwayFromZero);
        }
        catch(OperationCanceledException){Console.WriteLine($"[{Name}] cancelled");throw;}
    }
}

public sealed class FastShipCarrier(IReadOnlySet<int>? failures=null,Action? trackThread=null):ShippingCarrierBase(failures,trackThread)
{
    public override string Name=>"FastShip";protected override decimal Factor=>1.00m;protected override int MinDelayMs=>500;protected override int MaxDelayMs=>1500;
}
public sealed class CairoExpressCarrier(IReadOnlySet<int>? failures=null,Action? trackThread=null):ShippingCarrierBase(failures,trackThread)
{
    public override string Name=>"CairoExpress";protected override decimal Factor=>.95m;protected override int MinDelayMs=>300;protected override int MaxDelayMs=>2000;protected override bool BuiltInFailure(Order order)=>order.Id%2==0;
}
public sealed class NileCourierCarrier(IReadOnlySet<int>? failures=null,Action? trackThread=null):ShippingCarrierBase(failures,trackThread)
{
    public override string Name=>"NileCourier";protected override decimal Factor=>1.10m;protected override int MinDelayMs=>800;protected override int MaxDelayMs=>1200;
}
