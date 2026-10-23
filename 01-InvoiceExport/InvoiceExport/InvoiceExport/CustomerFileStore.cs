namespace InvoiceExport;

public sealed class CustomerFileStore
{
    readonly string _folder;
    public CustomerFileStore(string folder) => _folder = folder;

    public async Task<CustomerProfile> ReadProfileAsync(int customerId, CancellationToken ct = default)
    {
        string path = Path.Combine(_folder, $"customer-{customerId}.txt");
        await Task.Delay(300, ct);
        string text = await File.ReadAllTextAsync(path, ct);
        string[] parts = text.Split('|');
        return new CustomerProfile(int.Parse(parts[0]), parts[1], parts[2]);
    }
}
