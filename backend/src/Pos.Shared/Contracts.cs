namespace Pos.Shared;

// Contracts shared by the .NET API and the Blazor frontend.
// They only describe data that travels over HTTP; they contain no behavior.

public static class ApiRoutes
{
    public const string Login = "/api/auth/login";
    public const string Products = "/api/products";
    public const string ActiveSession = "/api/pos/sessions/active";
    public const string DiscountPolicies = "/api/discount-policies";
    public const string SaleQuote = "/api/sales/quote";
    public const string Sales = "/api/sales";
    public const string IdempotencyHeader = "Idempotency-Key";
}

public sealed record LoginRequest(string Username, string Password);

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAtUtc, string OperatorName);

public sealed record ProductDto(
    int Id,
    string Name,
    string? Code,
    decimal UnitPrice,
    decimal TaxPercent,
    bool TaxIncludedInPrice,
    decimal? QuantityAvailable,
    string? ImageDataUrl);

public sealed record PaymentMethodDto(int Id, string Name, bool IsCash);

public sealed record PosSessionDto(
    int Id,
    string Name,
    string State,
    int ConfigId,
    string ConfigName,
    IReadOnlyList<PaymentMethodDto> PaymentMethods);

public sealed record DiscountPolicyDto(string Code, string Description, bool RequiresPercent, decimal? MaxPercent);

public sealed record SaleLineRequest(int ProductId, decimal Quantity);

public sealed record SaleRequest(
    IReadOnlyList<SaleLineRequest> Lines,
    string? DiscountPolicyCode,
    decimal? DiscountPercent,
    int PaymentMethodId);

public sealed record SaleLineQuoteDto(
    int ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal SubtotalWithoutTax,
    decimal SubtotalWithTax);

public sealed record SaleQuoteDto(
    IReadOnlyList<SaleLineQuoteDto> Lines,
    decimal DiscountPercent,
    decimal AmountUntaxed,
    decimal AmountTax,
    decimal AmountTotal);

public enum SaleRegistrationOutcome
{
    // Odoo confirmed the order and payment.
    Registered,
    // The same Idempotency-Key was already registered; no new order was created.
    AlreadyRegistered,
    // Odoo did not answer in time: the result is unknown, retry with the same key.
    Uncertain
}

public sealed record SaleRegistrationDto(
    SaleRegistrationOutcome Outcome,
    string Message,
    int? OrderId,
    string? OrderName,
    string? State,
    decimal? AmountTotal);

public sealed record SaleSummaryDto(
    int Id,
    string Name,
    DateTime DateOrderUtc,
    string State,
    decimal AmountTotal,
    string? SessionName,
    string? CashierName);

public sealed record SaleDetailLineDto(
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal SubtotalWithoutTax,
    decimal SubtotalWithTax);

public sealed record SalePaymentDto(string PaymentMethodName, decimal Amount);

public sealed record SaleDetailDto(
    int Id,
    string Name,
    DateTime DateOrderUtc,
    string State,
    string? SessionName,
    string? CashierName,
    decimal AmountTax,
    decimal AmountTotal,
    decimal AmountPaid,
    IReadOnlyList<SaleDetailLineDto> Lines,
    IReadOnlyList<SalePaymentDto> Payments);
