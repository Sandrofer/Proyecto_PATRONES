using Pos.Shared;

namespace Pos.Frontend.Application;

// Result of a call to the API: either a value or an error the UI can show.
public sealed record ApiResult<T>(T? Value, string? Error, IReadOnlyDictionary<string, string[]>? FieldErrors = null, int? StatusCode = null)
{
    public bool IsSuccess => Error is null;

    // True when the request may not have reached the server or its answer was lost.
    public bool IsCommunicationFailure => StatusCode is null && Error is not null;

    public static ApiResult<T> Ok(T value, int? statusCode = 200) => new(value, null, null, statusCode);
    public static ApiResult<T> Fail(string error, IReadOnlyDictionary<string, string[]>? fields = null, int? statusCode = null) =>
        new(default, error, fields, statusCode);

    public string FullError => FieldErrors is { Count: > 0 }
        ? $"{Error} {string.Join(" ", FieldErrors.Values.SelectMany(v => v))}"
        : Error ?? "";
}

// Output port to the .NET API. Implemented over HTTP in Infrastructure; the
// use cases do not know HttpClient exists.
public interface IPosApi
{
    Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request);
    Task<ApiResult<IReadOnlyList<ProductDto>>> SearchProductsAsync(string? search);
    Task<ApiResult<PosSessionDto>> GetActiveSessionAsync();
    Task<ApiResult<IReadOnlyList<DiscountPolicyDto>>> GetDiscountPoliciesAsync();
    Task<ApiResult<SaleQuoteDto>> QuoteAsync(SaleRequest request);
    Task<ApiResult<SaleRegistrationDto>> RegisterSaleAsync(Guid idempotencyKey, SaleRequest request);
    Task<ApiResult<IReadOnlyList<SaleSummaryDto>>> GetSalesAsync();
    Task<ApiResult<SaleDetailDto>> GetSaleAsync(int id);
}

public interface IAuthSession
{
    string? AccessToken { get; }
    string? OperatorName { get; }
    bool IsAuthenticated { get; }
    Task InitializeAsync();
    Task SignInAsync(LoginResponse response);
    Task SignOutAsync();
}
