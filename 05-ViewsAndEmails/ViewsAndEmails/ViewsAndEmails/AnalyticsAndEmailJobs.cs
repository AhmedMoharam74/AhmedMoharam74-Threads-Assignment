using System.Collections.Concurrent;

namespace ViewsAndEmails;

public sealed class AnalyticsAndEmailJobs
{
    public readonly ConcurrentDictionary<string, int> Views = new();
    public readonly ConcurrentQueue<string> EmailJobs = new();
    public readonly ConcurrentBag<string> SentEmails = new();
    public volatile bool ProducersDone;

    public void RecordView(string product) => Views.AddOrUpdate(product, 1, (_, count) => count + 1);

    public void PlaceOrder(int orderId) => EmailJobs.Enqueue($"Email: order {orderId}");

    public void EmailWorker()
    {
        while (true)
        {
            if (EmailJobs.TryDequeue(out string? job))
            {
                SentEmails.Add(job);
                continue;
            }

            if (ProducersDone && EmailJobs.IsEmpty)
                return;

            Thread.Yield();
        }
    }
}
