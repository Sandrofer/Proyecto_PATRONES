using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Pos.Frontend.Application;
using Pos.Shared;

namespace Pos.Frontend.Infrastructure;

// Adapter from IPosApi to the .NET REST API. It is the only class of the
// frontend that knows URLs, HTTP status codes and ProblemDetails.
public sealed class HttpPosApi(HttpClient http, IAuthSession session) : IPosApi
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<ApiResult<LoginResponse>> LoginAsync(LoginRequest request) =>
        SendAsync<LoginResponse>(HttpMethod.Post, ApiRoutes.Login, request, authenticated: false);

    public Task<ApiResult<IReadOnlyList<ProductDto>>> SearchProductsAsync(string? search) =>
        SendAsync<IReadOnlyList<ProductDto>>(HttpMethod.Get,
            $"{ApiRoutes.Products}?search={Uri.EscapeDataString(search ?? "")}");

    public Task<ApiResult<PosSessionDto>> GetActiveSessionAsync() =>
        SendAsync<PosSessionDto>(HttpMethod.Get, ApiRoutes.ActiveSession);

    public Task<ApiResult<IReadOnlyList<DiscountPolicyDto>>> GetDiscountPoliciesAsync() =>
        SendAsync<IReadOnlyList<DiscountPolicyDto>>(HttpMethod.Get, ApiRoutes.DiscountPolicies);

    public Task<ApiResult<SaleQuoteDto>> QuoteAsync(SaleRequest request) =>
        SendAsync<SaleQuoteDto>(HttpMethod.Post, ApiRoutes.SaleQuote, request);

    public Task<ApiResult<SaleRegistrationDto>> RegisterSaleAsync(Guid idempotencyKey, SaleRequest request) =>
        SendAsync<SaleRegistrationDto>(HttpMethod.Post, ApiRoutes.Sales, request,
            headers: r => r.Headers.Add(ApiRoutes.IdempotencyHeader, idempotencyKey.ToString()));

    public Task<ApiResult<IReadOnlyList<SaleSummaryDto>>> GetSalesAsync() =>
        SendAsync<IReadOnlyList<SaleSummaryDto>>(HttpMethod.Get, ApiRoutes.Sales);

    public Task<ApiResult<SaleDetailDto>> GetSaleAsync(int id) =>
        SendAsync<SaleDetailDto>(HttpMethod.Get, $"{ApiRoutes.Sales}/{id}");

    async Task<ApiResult<T>> SendAsync<T>(HttpMethod method, string url, object? body = null,
        bool authenticated = true, Action<HttpRequestMessage>? headers = null)
    {
        using var request = new HttpRequestMessage(method, url.TrimStart('/'));
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        if (authenticated && session.AccessToken is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        headers?.Invoke(request);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return ApiResult<T>.Fail("No se pudo conectar con el servidor del POS. Verifique la red y que la API esté en ejecución.");
        }

        using (response)
        {
            int status = (int)response.StatusCode;
            if (response.IsSuccessStatusCode)
            {
                try
                {
                    return ApiResult<T>.Ok((await response.Content.ReadFromJsonAsync<T>(Json))!, status);
                }
                catch (JsonException)
                {
                    return ApiResult<T>.Fail("El servidor devolvió una respuesta que no se pudo interpretar.", statusCode: status);
                }
            }

            if (response.StatusCode == HttpStatusCode.Unauthorized && authenticated)
            {
                await session.SignOutAsync();
                return ApiResult<T>.Fail("Su sesión expiró. Inicie sesión nuevamente.", statusCode: status);
            }

            var problem = await ReadProblemAsync(response);
            return ApiResult<T>.Fail(
                problem?.Detail ?? problem?.Title ?? DefaultMessage(response.StatusCode),
                problem?.Errors,
                status);
        }
    }

    static async Task<Problem?> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<Problem>(Json);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            return null;
        }
    }

    static string DefaultMessage(HttpStatusCode status) => status switch
    {
        HttpStatusCode.TooManyRequests => "Demasiados intentos. Espere un minuto.",
        HttpStatusCode.NotFound => "El recurso solicitado no existe.",
        >= HttpStatusCode.InternalServerError => "El servidor del POS tuvo un error. Intente de nuevo.",
        _ => $"La solicitud falló ({(int)status})."
    };

    sealed record Problem(string? Title, string? Detail, int? Status, Dictionary<string, string[]>? Errors);
}
