using Pos.Application;
using Pos.Application.Auth;
using Pos.Application.Catalog;
using Pos.Application.Discounts;
using Pos.Application.Sales;
using Pos.Shared;

namespace Pos.Api;

// Presentation layer: endpoints only translate HTTP <-> input ports.
// No business rules live here (CA-02).
public static class Endpoints
{
    public static WebApplication MapPosEndpoints(this WebApplication app)
    {
        app.MapPost(ApiRoutes.Login, async (LoginRequest request, ILoginInputPort login, CancellationToken ct) =>
            Results.Ok(await login.HandleAsync(request, ct)))
            .RequireRateLimiting("login");

        var api = app.MapGroup("").RequireAuthorization();

        api.MapGet(ApiRoutes.Products, async (string? search, ISearchProductsInputPort useCase, CancellationToken ct) =>
            Results.Ok(await useCase.HandleAsync(search, ct)));

        api.MapGet(ApiRoutes.Customers + "/{identification}", async (string identification,
                Pos.Application.Customers.IFindCustomerInputPort useCase, CancellationToken ct) =>
            Results.Ok(await useCase.HandleAsync(identification, ct)));

        api.MapGet(ApiRoutes.ActiveSession, async (IGetActiveSessionInputPort useCase, CancellationToken ct) =>
            Results.Ok(await useCase.HandleAsync(ct)));

        api.MapGet(ApiRoutes.DiscountPolicies, (IGetDiscountPoliciesInputPort useCase) =>
            Results.Ok(useCase.Handle()));

        api.MapPost(ApiRoutes.SaleQuote, async (SaleRequest request, IQuoteSaleInputPort useCase, CancellationToken ct) =>
            Results.Ok(await useCase.HandleAsync(request, ct)));

        api.MapPost(ApiRoutes.Sales, async (HttpRequest http, SaleRequest request, IRegisterSaleInputPort useCase) =>
        {
            if (!Guid.TryParse(http.Headers[ApiRoutes.IdempotencyHeader], out Guid key))
                throw ValidationException.For(ApiRoutes.IdempotencyHeader,
                    "Envíe la cabecera Idempotency-Key con un GUID único por venta.");

            // The registration must not be abandoned halfway if the browser
            // disconnects, so it does not use the request cancellation token.
            var result = await useCase.HandleAsync(key, request, CancellationToken.None);
            return result.Outcome switch
            {
                SaleRegistrationOutcome.Registered => Results.Created($"{ApiRoutes.Sales}/{result.OrderId}", result),
                SaleRegistrationOutcome.AlreadyRegistered => Results.Ok(result),
                _ => Results.Accepted(value: result)
            };
        });

        api.MapGet(ApiRoutes.Sales, async (IGetSalesHistoryInputPort useCase, CancellationToken ct) =>
            Results.Ok(await useCase.HandleAsync(ct)));

        api.MapGet(ApiRoutes.Sales + "/{id:int}", async (int id, IGetSaleDetailInputPort useCase, CancellationToken ct) =>
            Results.Ok(await useCase.HandleAsync(id, ct)));

        return app;
    }
}
