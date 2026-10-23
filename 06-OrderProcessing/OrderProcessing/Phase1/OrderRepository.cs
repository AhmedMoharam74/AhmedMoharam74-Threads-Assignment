using OrderProcessing.Shared;

namespace OrderProcessing.Phase1;

public sealed class OrderRepository
{
    private readonly JsonFileStore store;
    private readonly IReadOnlySet<int> failSaveForOrderIds;

    public OrderRepository(JsonFileStore store,IReadOnlySet<int>? failSaveForOrderIds=null)
    {
        this.store=store;this.failSaveForOrderIds=failSaveForOrderIds??new HashSet<int>();
    }

    public async Task SaveAsync(Order order, CancellationToken ct)
    {
        await Task.Delay(200, ct);
        await Task.Delay(100, ct);
        await Task.Delay(400,ct);
        if(order.Status==OrderStatus.Completed&&failSaveForOrderIds.Contains(order.Id))
            throw new DatabaseWriteException("Connection lost during commit");
        await store.UpdateAsync<Order, bool>(DataFiles.Orders, orders =>
        {
            int index = orders.FindIndex(x => x.Id == order.Id);
            if (index < 0) orders.Add(order); else orders[index] = order;
            return true;
        }, ct);
        await Task.Delay(200,ct);
    }

    public async Task<List<Order>> GetByStatusAsync(OrderStatus status, CancellationToken ct)
    {
        await Task.Delay(200, ct);
        await Task.Delay(300, ct);
        return (await store.ReadAsync<Order>(DataFiles.Orders, ct)).Where(x => x.Status == status).ToList();
    }
}
