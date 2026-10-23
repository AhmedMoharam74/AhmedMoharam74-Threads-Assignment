using OrderProcessing.Shared;

namespace OrderProcessing.Phase3;

public interface IPaymentGateway
{
    Task<PaymentResult> ProcessPaymentAsync(int orderId,decimal amount,CancellationToken ct);
    Task RefundAsync(string transactionId,CancellationToken ct);
}

public interface IEmailService
{
    Task SendConfirmationAsync(Order order,Customer customer,CancellationToken ct);
}

public sealed class PaymentGateway(JsonFileStore store,IReadOnlySet<int> timeoutOrderIds,IReadOnlySet<int> declineOrderIds) : IPaymentGateway
{
    public async Task<PaymentResult> ProcessPaymentAsync(int orderId,decimal amount,CancellationToken ct)
    {
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);
        if(timeoutOrderIds.Contains(orderId))timeout.CancelAfter(3000);
        try
        {
            await Task.Delay(800,timeout.Token);await Task.Delay(500,timeout.Token);
            if(declineOrderIds.Contains(orderId))
            {
                var declined=new PaymentTransaction{OrderId=orderId,Amount=amount,Status=PaymentStatus.Declined,CreatedAt=DateTime.Now};
                await store.UpdateAsync<PaymentTransaction,bool>(DataFiles.Payments,list=>{list.Add(declined);return true;},ct);
                return new PaymentResult{Success=false,Status=PaymentStatus.Declined,Message="Card declined by issuer"};
            }
            if(timeoutOrderIds.Contains(orderId))await Task.Delay(5000,timeout.Token);
            else await Task.Delay(700,timeout.Token);
            string tx="TXN-"+Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            var payment=new PaymentTransaction{OrderId=orderId,Amount=amount,Status=PaymentStatus.Captured,TransactionId=tx,CreatedAt=DateTime.Now};
            await store.UpdateAsync<PaymentTransaction,bool>(DataFiles.Payments,list=>{list.Add(payment);return true;},ct);
            return new PaymentResult{Success=true,Status=PaymentStatus.Captured,TransactionId=tx};
        }
        catch(OperationCanceledException) when(!ct.IsCancellationRequested)
        {
            return new PaymentResult{Success=false,Status=PaymentStatus.Declined,Message="Payment gateway timeout (3s)"};
        }
    }

    public async Task RefundAsync(string transactionId,CancellationToken ct)
    {
        await Task.Delay(150,ct);await Task.Delay(300,ct);
        await store.UpdateAsync<PaymentTransaction,bool>(DataFiles.Payments,list=>{var payment=list.FirstOrDefault(x=>x.TransactionId==transactionId);if(payment is not null)payment.Status=PaymentStatus.Refunded;return true;},ct);
    }
}

public sealed class EmailService(JsonFileStore store,IReadOnlyDictionary<int,int> failAttempts) : IEmailService
{
    public async Task SendConfirmationAsync(Order order,Customer customer,CancellationToken ct)
    {
        for(int attempt=1;attempt<=4;attempt++)
        {
            if(attempt>1)await Task.Delay(500*(1<<(attempt-2)),ct);
            try
            {
                await Task.Delay(200,ct);await Task.Delay(300,ct);await Task.Delay(500,ct);
                if(failAttempts.TryGetValue(order.Id,out int failures)&&attempt<=failures)throw new SmtpUnavailableException("SMTP unavailable");
                var message=new EmailMessage{Id=Guid.NewGuid(),OrderId=order.Id,To=customer.Email,Subject=$"Order #{order.Id} confirmed",Body=$"Your order #{order.Id} is confirmed.",SentAt=DateTime.Now,Attempts=attempt};
                await store.UpdateAsync<EmailMessage,bool>(DataFiles.Emails,list=>{list.Add(message);return true;},ct);return;
            }
            catch(SmtpUnavailableException ex)
            {
                Log.Error($"Order #{order.Id} email attempt {attempt} failed: {ex.Message}");
                if(attempt==4)
                {
                    var failed=new EmailMessage{Id=Guid.NewGuid(),OrderId=order.Id,To=customer.Email,Subject=$"Order #{order.Id} confirmation",Body=$"Order #{order.Id}",SentAt=DateTime.Now,Attempts=attempt,LastError=ex.Message};
                    await store.UpdateAsync<EmailMessage,bool>(DataFiles.EmailRetry,list=>{list.Add(failed);return true;},ct);
                    Console.WriteLine($"Email for Order #{order.Id} queued for retry later");
                }
            }
        }
    }
}
