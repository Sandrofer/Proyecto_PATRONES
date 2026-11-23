using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pos.Application.Discounts;
using Pos.Application.Ports;
using Pos.Application.Sales;
using Pos.Domain.Discounts;
using Pos.Infrastructure.Idempotency;
using Pos.Infrastructure.Odoo;
using Pos.Shared;

namespace Pos.IntegrationTests;

// Runs only when POS_RUN_INTEGRATION=1, because it creates a real sale in Odoo.
public sealed class OdooFactAttribute : FactAttribute
{
    public OdooFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("POS_RUN_INTEGRATION") != "1")
            Skip = "Prueba con Odoo real: ejecute con POS_RUN_INTEGRATION=1 y Odoo en marcha.";
    }
}

// P09: register and verify in Odoo a sale of two products paid in cash,
// going through the real use case and the real Odoo adapter (no doubles).
public class OdooSaleIntegrationTests
{
    sealed class TestOperator : ICurrentOperator
    {
        public string Name => "prueba-integracion";
    }

    static OdooPosGateway CreateGateway()
    {
        var config = new ConfigurationBuilder()
            .AddUserSecrets("pos-odoo-api-2026")   // same secrets as Pos.Api
            .AddEnvironmentVariables()
            .Build();
        var odoo = new OdooOptions();
        config.GetSection(OdooOptions.Section).Bind(odoo);
        var options = Options.Create(odoo);

        var http = new HttpClient
        {
            BaseAddress = new Uri(odoo.BaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(odoo.TimeoutSeconds)
        };
        return new OdooPosGateway(new OdooJson2Client(http, options, NullLogger<OdooJson2Client>.Instance), options);
    }

    [OdooFact]
    public async Task P09_Registers_a_two_product_cash_sale_in_Odoo_and_does_not_duplicate_it()
    {
        var gateway = CreateGateway();
        var session = await gateway.GetActiveSessionAsync(default);
        Assert.NotNull(session);
        Assert.True(session.IsOpen, "La sesión POS debe estar abierta.");
        var cash = session.PaymentMethods.First(m => m.IsCash);

        var products = (await gateway.SearchProductsAsync(null, 10, default)).Take(2).ToList();
        Assert.Equal(2, products.Count);

        var useCase = new RegisterSaleInteractor(
            gateway,
            new SaleBuilder(gateway, new DiscountPolicyCatalog([new NoDiscountPolicy()])),
            new InMemoryIdempotencyStore(),
            new TestOperator());
        var key = Guid.NewGuid();
        var request = new SaleRequest(products.Select(p => new SaleLineRequest(p.Id, 1)).ToList(), null, null, cash.Id);

        var quote = await new QuoteSaleInteractor(new SaleBuilder(gateway, new DiscountPolicyCatalog([new NoDiscountPolicy()])))
            .HandleAsync(request, default);
        var result = await useCase.HandleAsync(key, request, default);

        Assert.Equal(SaleRegistrationOutcome.Registered, result.Outcome);
        Assert.NotNull(result.OrderId);

        // Verify in Odoo what was really stored.
        var detail = await gateway.GetOrderAsync(result.OrderId!.Value, default);
        Assert.NotNull(detail);
        Assert.Equal("paid", detail.State);
        Assert.Equal(2, detail.Lines.Count);
        Assert.Equal(quote.AmountTotal, detail.AmountTotal);
        Assert.Equal(detail.AmountTotal, detail.AmountPaid);
        Assert.Contains(detail.Payments, p => p.Amount == detail.AmountTotal);

        // The same operation again: Odoo already has the uuid, no second order.
        var again = await new RegisterSaleInteractor(
            gateway,
            new SaleBuilder(gateway, new DiscountPolicyCatalog([new NoDiscountPolicy()])),
            new InMemoryIdempotencyStore(),
            new TestOperator()).HandleAsync(key, request, default);
        Assert.Equal(SaleRegistrationOutcome.AlreadyRegistered, again.Outcome);
        Assert.Equal(result.OrderId, again.OrderId);
    }
}
