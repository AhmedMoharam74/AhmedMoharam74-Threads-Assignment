using OrderProcessing.Phase1;
using OrderProcessing.Phase2;
using OrderProcessing.Phase3;
using OrderProcessing.Phase4;
using OrderProcessing.Shared;

public static class Program
{
    public static async Task Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        string phase = args.FirstOrDefault()?.ToLowerInvariant() ?? "menu";
        int cancelIndex = Array.IndexOf(args, "--cancel-after");
        if (cancelIndex >= 0 && cancelIndex + 1 < args.Length && int.TryParse(args[cancelIndex + 1], out int ms))
            cancellation.CancelAfter(ms);

        var store = new JsonFileStore();
        var seeder = new DataSeeder(store);
        try
        {
            if (phase == "phase3" && args.Contains("--comparison", StringComparer.OrdinalIgnoreCase))
            {
                await new Phase3Runner(store, seeder).RunComparisonAsync(cancellation.Token);
                return;
            }
            if (phase == "phase3" && args.Contains("--fireforget", StringComparer.OrdinalIgnoreCase))
            {
                await seeder.ResetAsync(cancellation.Token);
                Order order = (await store.ReadAsync<Order>(DataFiles.IncomingOrders, cancellation.Token)).First(x => x.Id == 1);
                Customer customer = (await store.ReadAsync<Customer>(DataFiles.Customers, cancellation.Token)).First(x => x.Id == order.CustomerId);
                var demo = new FireAndForgetDemo(new OrderRepository(store), new DemonstrationFailingEmailService());
                await demo.RunBothDemosAsync(order, customer, cancellation.Token);
                return;
            }
            if (phase == "menu")
            {
                Console.WriteLine("1. Phase 1 — async data access\n2. Phase 2 — concurrent processing\n3. Phase 3 — checkout\n4. Phase 4 — scale-up\n5. All phases");
                Console.Write("Choose a phase: ");
                phase = Console.ReadLine() switch { "1" => "phase1", "2" => "phase2", "3" => "phase3", "4" => "phase4", "5" => "all", _ => "menu" };
            }

            switch (phase)
            {
                case "phase1": await new Phase1Runner(store, seeder).RunAsync(cancellation.Token); break;
                case "phase2": await new Phase2Runner(store, seeder).RunAsync(cancellation.Token); break;
                case "phase3": await new Phase3Runner(store, seeder).RunAsync(cancellation.Token); break;
                case "phase4": await new Phase4Runner(store, seeder).RunAsync(cancellation.Token); break;
                case "all": await RunAllAsync(store, seeder, cancellation.Token); break;
                default: Console.WriteLine("Usage: dotnet run -- phase1|phase2|phase3|phase4|all [--cancel-after <ms>] (Phase 3 options: --comparison, --fireforget)"); break;
            }
        }
        catch (OperationCanceledException) { Console.WriteLine("Phase cancelled."); }
    }

    private static async Task RunAllAsync(JsonFileStore store, DataSeeder seeder, CancellationToken ct)
    {
        await new Phase1Runner(store, seeder).RunAsync(ct);
        await new Phase2Runner(store, seeder).RunAsync(ct);
        await new Phase3Runner(store, seeder).RunAsync(ct);
        await new Phase4Runner(store, seeder).RunAsync(ct);
    }
}
