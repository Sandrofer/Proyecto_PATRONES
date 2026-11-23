using Pos.Domain.Catalog;
using Pos.Domain.Sales;
using Pos.Shared;

namespace Pos.Application.Ports;

// Output ports: Application defines what it needs and Infrastructure
// implements it (Dependency Inversion). Nothing here knows about HTTP or Odoo JSON.

public interface IPosGateway
{
    Task<IReadOnlyList<CatalogProduct>> SearchProductsAsync(string? term, int limit, CancellationToken ct);
    Task<IReadOnlyList<CatalogProduct>> GetProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken ct);
    Task<PosSession?> GetActiveSessionAsync(CancellationToken ct);
    Task<TaxRoundingMode> GetTaxRoundingModeAsync(CancellationToken ct);
    Task<RegisteredOrder?> FindOrderByReferenceAsync(Guid reference, CancellationToken ct);
    Task<RegisteredOrder> SubmitPaidOrderAsync(OrderSubmission submission, CancellationToken ct);
    Task<IReadOnlyList<SaleSummaryDto>> GetRecentOrdersAsync(int limit, CancellationToken ct);
    Task<SaleDetailDto?> GetOrderAsync(int orderId, CancellationToken ct);
}

public sealed record PaymentMethod(int Id, string Name, bool IsCash);

public sealed record PosSession(
    int Id,
    string Name,
    string State,
    int ConfigId,
    string ConfigName,
    IReadOnlyList<PaymentMethod> PaymentMethods)
{
    public bool IsOpen => State == "opened";
}

public sealed record OrderSubmission(
    Sale Sale,
    SaleTotals Totals,
    PosSession Session,
    PaymentMethod PaymentMethod,
    string OperatorName);

public sealed record RegisteredOrder(int Id, string Name, string State, decimal AmountTotal);

public enum IdempotencyClaimStatus { Acquired, InProgress, Completed }

public sealed record IdempotencyClaim(IdempotencyClaimStatus Status, SaleRegistrationDto? CompletedResult = null);

public interface IIdempotencyStore
{
    IdempotencyClaim Claim(Guid key);
    void Complete(Guid key, SaleRegistrationDto result);
    void Release(Guid key);
}

public interface ICurrentOperator
{
    string Name { get; }
}

public sealed record Operator(string Username, string DisplayName);

public interface IOperatorDirectory
{
    Operator? ValidateCredentials(string username, string password);
}

public interface ITokenIssuer
{
    LoginResponse Issue(Operator op);
}
