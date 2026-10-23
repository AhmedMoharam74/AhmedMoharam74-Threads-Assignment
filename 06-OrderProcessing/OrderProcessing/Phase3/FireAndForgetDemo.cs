using OrderProcessing.Phase1;
using OrderProcessing.Shared;

namespace OrderProcessing.Phase3;

public sealed class FireAndForgetDemo(OrderRepository orderRepository,IEmailService emailService)
{
    // Intentionally broken: kept for the required investigation demo.
#pragma warning disable CS4014
    public async Task ProcessBrokenAsync(Order order,Customer customer,CancellationToken ct)
    {
        await orderRepository.SaveAsync(order,ct);
        emailService.SendConfirmationAsync(order,customer,ct);
        Console.WriteLine("Order processing finished");
    }
#pragma warning restore CS4014

    public async Task ProcessFixedAsync(Order order,Customer customer,CancellationToken ct)
    {
        await orderRepository.SaveAsync(order,ct);
        await emailService.SendConfirmationAsync(order,customer,ct);
        Console.WriteLine("Order processing finished");
    }

    public async Task RunBothDemosAsync(Order order, Customer customer, CancellationToken ct)
    {
        Console.WriteLine("=== Broken fire-and-forget ===");
        order.Status = OrderStatus.Completed;
        await ProcessBrokenAsync(order, customer, ct);
        Console.WriteLine("Broken method returned; waiting to see the email result...");
        await Task.Delay(700, ct);

        Console.WriteLine("=== Fixed: await the email ===");
        order.Status = OrderStatus.Completed;
        try
        {
            await ProcessFixedAsync(order, customer, ct);
        }
        catch (SmtpUnavailableException ex)
        {
            Console.WriteLine($"Caller observed email failure: {ex.Message}");
        }
    }
}

public sealed class DemonstrationFailingEmailService : IEmailService
{
    public async Task SendConfirmationAsync(Order order, Customer customer, CancellationToken ct)
    {
        await Task.Delay(500, ct);
        Console.WriteLine($"Email operation for Order #{order.Id} failed: SMTP unavailable");
        throw new SmtpUnavailableException("SMTP unavailable");
    }
}
