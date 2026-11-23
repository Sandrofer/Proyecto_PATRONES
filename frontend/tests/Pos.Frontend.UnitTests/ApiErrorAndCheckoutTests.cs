using System.Net;
using System.Text;
using NSubstitute;
using Pos.Frontend.Application;
using Pos.Frontend.Domain;
using Pos.Frontend.Infrastructure;
using Pos.Shared;

namespace Pos.Frontend.UnitTests;

// P08: understandable errors when the .NET API fails.
public class ApiErrorAndCheckoutTests
{
    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    static HttpPosApi Api(HttpMessageHandler handler)
    {
        var session = Substitute.For<IAuthSession>();
        session.AccessToken.Returns("token");
        return new HttpPosApi(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }, session);
    }

    static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task P08_Connection_failure_shows_a_clear_message()
    {
        var api = Api(new FakeHandler(_ => throw new HttpRequestException("refused")));

        var result = await api.SearchProductsAsync("café");

        Assert.False(result.IsSuccess);
        Assert.True(result.IsCommunicationFailure);
        Assert.Contains("No se pudo conectar con el servidor", result.Error);
    }

    [Fact]
    public async Task P08_ProblemDetails_from_the_API_are_shown_to_the_operator()
    {
        var api = Api(new FakeHandler(_ => Json(HttpStatusCode.BadRequest,
            """{"title":"Datos no válidos","detail":"Los datos enviados no son válidos.","status":400,"errors":{"sale":["El descuento de 50 % no está autorizado: el máximo permitido es 10 %."]}}""")));

        var result = await api.QuoteAsync(new SaleRequest([new SaleLineRequest(1, 1)], "MANUAL", 50, 1));

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.StatusCode);
        Assert.Contains("no está autorizado", result.FullError);
    }

    [Fact]
    public async Task P08_Unexpected_server_error_has_a_friendly_message()
    {
        var api = Api(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var result = await api.GetSalesAsync();

        Assert.Equal("El servidor del POS tuvo un error. Intente de nuevo.", result.Error);
    }

    [Fact]
    public async Task Register_sends_the_idempotency_key_header()
    {
        var key = Guid.NewGuid();
        var handler = new FakeHandler(_ => Json(HttpStatusCode.Created,
            """{"outcome":0,"message":"ok","orderId":5,"orderName":"Shop/0005","state":"paid","amountTotal":10.35}"""));

        await Api(handler).RegisterSaleAsync(key, new SaleRequest([new SaleLineRequest(1, 1)], null, null, 1));

        Assert.Equal(key.ToString(), handler.Requests[0].Headers.GetValues(ApiRoutes.IdempotencyHeader).Single());
        Assert.Equal("Bearer", handler.Requests[0].Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task Checkout_keeps_the_same_key_after_a_communication_failure_and_clears_after_success()
    {
        var api = Substitute.For<IPosApi>();
        var keys = new List<Guid>();
        api.RegisterSaleAsync(Arg.Do<Guid>(keys.Add), Arg.Any<SaleRequest>()).Returns(
            ApiResult<SaleRegistrationDto>.Fail("No se pudo conectar con el servidor del POS."),
            ApiResult<SaleRegistrationDto>.Ok(new SaleRegistrationDto(SaleRegistrationOutcome.Registered, "ok", 5, "Shop/0005", "paid", 10m)));
        var checkout = new CheckoutUseCase(api);
        checkout.Cart.Add(new CartProduct(1, "Café", null, 2.5m, 15m, false));

        var first = await checkout.ConfirmAsync(1);
        var second = await checkout.ConfirmAsync(1);

        Assert.True(first.Uncertain);
        Assert.True(second.Success);
        Assert.Equal(keys[0], keys[1]);
        Assert.Null(checkout.PendingOperation);
        Assert.True(checkout.Cart.IsEmpty);
    }

    [Fact]
    public async Task Checkout_with_an_empty_cart_does_not_call_the_API()
    {
        var api = Substitute.For<IPosApi>();

        var result = await new CheckoutUseCase(api).ConfirmAsync(1);

        Assert.False(result.Success);
        await api.DidNotReceiveWithAnyArgs().RegisterSaleAsync(default, default!);
    }
}
