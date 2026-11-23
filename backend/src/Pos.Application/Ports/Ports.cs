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
    Task<CustomerDto?> FindCustomerAsync(string identification, CancellationToken ct);
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
    string OperatorName,
    int? OperatorUserId,
    CashTender? Cash = null);

public sealed record RegisteredOrder(int Id, string Name, string State, decimal AmountTotal, decimal AmountReturn = 0);

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
    // Id of the cashier's user in Odoo; the sale is registered in their name.
    int? OdooUserId { get; }
}

// A cashier. Cashiers are Odoo users with Point of Sale rights.
public sealed record Operator(int OdooUserId, string Username, string DisplayName);

// Where cashiers are registered and validated (Odoo in this project).
public interface IOperatorDirectory
{
    // Returns null when the credentials are wrong.
    // Throws OperatorNotAuthorizedException when they are right but the user
    // has no Point of Sale rights.
    Task<Operator?> ValidateCredentialsAsync(string username, string password, CancellationToken ct);
}

public interface ITokenIssuer
{
    LoginResponse Issue(Operator op);
}
