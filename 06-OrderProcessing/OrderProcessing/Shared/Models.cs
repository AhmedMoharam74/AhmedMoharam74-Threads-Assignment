namespace OrderProcessing.Shared;

public enum LoyaltyTier { Bronze, Silver, Gold }
public enum OrderStatus { Pending, Validated, Rejected, PaymentFailed, Completed, Failed }
public enum PaymentStatus { Captured, Declined, Refunded }

public sealed class Customer { public int Id { get; set; } public string Name { get; set; } = ""; public string Email { get; set; } = ""; public bool IsActive { get; set; } public LoyaltyTier LoyaltyTier { get; set; } }
public sealed class Product { public int Id { get; set; } public string Name { get; set; } = ""; public decimal Price { get; set; } public int StockQuantity { get; set; } public decimal WeightKg { get; set; } }
public sealed class Coupon { public string Code { get; set; } = ""; public int DiscountPercent { get; set; } public DateTime ExpiryDate { get; set; } }
public sealed class OrderItem { public int ProductId { get; set; } public int Quantity { get; set; } public decimal UnitPrice { get; set; } }
public sealed class Order { public int Id { get; set; } public int CustomerId { get; set; } public List<OrderItem> Items { get; set; } = []; public string? CouponCode { get; set; } public decimal SubTotal { get; set; } public decimal Discount { get; set; } public decimal ShippingCost { get; set; } public decimal TotalAmount { get; set; } public OrderStatus Status { get; set; } = OrderStatus.Pending; public DateTime CreatedAt { get; set; } public string? FailureReason { get; set; } public string? TransactionId { get; set; } }
public sealed class PaymentTransaction { public int OrderId { get; set; } public decimal Amount { get; set; } public PaymentStatus Status { get; set; } public string? TransactionId { get; set; } public DateTime CreatedAt { get; set; } }
public sealed class PaymentResult { public bool Success { get; set; } public string? TransactionId { get; set; } public PaymentStatus Status { get; set; } public string? Message { get; set; } }
public sealed class EmailMessage { public Guid Id { get; set; } public int OrderId { get; set; } public string To { get; set; } = ""; public string Subject { get; set; } = ""; public string Body { get; set; } = ""; public DateTime SentAt { get; set; } public int Attempts { get; set; } public string? LastError { get; set; } }
public sealed class Report { public int TotalOrders { get; set; } public decimal TotalRevenue { get; set; } public int CompletedCount { get; set; } public int RejectedCount { get; set; } public int FailedCount { get; set; } public int PaymentFailedCount { get; set; } public string TopSellingProduct { get; set; } = ""; public int TopSellingQuantity { get; set; } public decimal AverageOrderValue { get; set; } public DateTime GeneratedAt { get; set; } }
public sealed class CustomerDashboard { public Customer? Customer { get; set; } public List<Order> Orders { get; set; } = []; public List<PaymentTransaction> Payments { get; set; } = []; public List<EmailMessage> Emails { get; set; } = []; }
public sealed record ShippingQuote(string Carrier, decimal Price, long ElapsedMs);
