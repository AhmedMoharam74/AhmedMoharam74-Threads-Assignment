using System.Diagnostics;

namespace InvoiceExport;

public sealed class InvoiceExportService
{
    readonly OrderDatabase _db;
    readonly CustomerFileStore _customers;
    readonly InvoiceFileWriter _invoices;

    public InvoiceExportService(OrderDatabase db, CustomerFileStore customers, InvoiceFileWriter invoices)
        => (_db, _customers, _invoices) = (db, customers, invoices);

    public async Task ExportAllPendingAsync(CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        List<Order> orders = await _db.GetPendingOrdersAsync(ct);

        // Start every independent order pipeline first, then observe each task individually.
        List<Task<string>> exports = orders.Select(ExportOneAsync).ToList();
        foreach (Task<string> export in exports)
            Console.WriteLine($"Wrote {await export}");

        Console.WriteLine($"Export finished in {watch.Elapsed.TotalSeconds:F1} s");

        async Task<string> ExportOneAsync(Order order)
        {
            Order fresh = await _db.GetOrderByIdAsync(order.Id, ct)
                ?? throw new InvalidOperationException($"Order {order.Id} missing");
            CustomerProfile customer = await _customers.ReadProfileAsync(fresh.CustomerId, ct);
            return await _invoices.WriteInvoiceAsync(fresh, customer, ct);
        }
    }
}
