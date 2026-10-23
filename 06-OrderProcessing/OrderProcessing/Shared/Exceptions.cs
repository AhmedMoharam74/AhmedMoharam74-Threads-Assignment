namespace OrderProcessing.Shared;

public sealed class CustomerNotFoundException(string message) : Exception(message) { }
public sealed class InvalidCouponException(string message) : Exception(message) { }
public sealed class ShippingProviderUnavailableException(string message) : Exception(message) { }
public sealed class DatabaseWriteException(string message) : Exception(message) { }
public sealed class SmtpUnavailableException(string message) : Exception(message) { }
public sealed class DataFileCorruptedException(string fileName, Exception inner) : Exception($"Data file '{fileName}' is corrupted.", inner) { }
