using NSubstitute;
using Pos.Application.Discounts;
using Pos.Application.Ports;
using Pos.Application.Sales;
using Pos.Domain.Catalog;
using Pos.Domain.Discounts;
using Pos.Shared;

namespace Pos.UnitTests;

static class TestData
{
    public static readonly TaxSet Iva15 = new([new TaxRule(1, 15m, PriceIncluded: false)]);

    public static CatalogProduct Coffee => new(10, "Café", "CAF-01", 2.50m, Iva15, true, 100);
    public static CatalogProduct Sandwich => new(11, "Sánduche", "SAN-01", 4.00m, Iva15, true, null);
    public static CatalogProduct Disabled => new(12, "Producto retirado", "RET-01", 9.99m, Iva15, false, 0);

    public static PaymentMethod Cash => new(1, "Efectivo", true);
    public static PaymentMethod Card => new(2, "Tarjeta", false);

    public static PosSession OpenSession => new(5, "POS/00005", "opened", 1, "Caja principal", [Cash, Card]);

    public static SaleRequest TwoProducts(string? policy = null, decimal? percent = null) =>
        new([new SaleLineRequest(Coffee.Id, 2), new SaleLineRequest(Sandwich.Id, 1)], policy, percent, Cash.Id);

    public static IDiscountPolicyCatalog Policies(params IDiscountPolicy[] extra) =>
        new DiscountPolicyCatalog(new IDiscountPolicy[]
        {
            new NoDiscountPolicy(),
            new ManualPercentageDiscountPolicy(10m),
            new VolumeDiscountPolicy(100m, 5m)
        }.Concat(extra));

    // Gateway double with an open session and the three catalog products.
    public static IPosGateway Gateway(PosSession? session = null)
    {
        var gateway = Substitute.For<IPosGateway>();
        var all = new[] { Coffee, Sandwich, Disabled };
        gateway.GetProductsAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => all.Where(p => ci.Arg<IReadOnlyCollection<int>>().Contains(p.Id)).ToList());
        gateway.GetActiveSessionAsync(Arg.Any<CancellationToken>()).Returns(session ?? OpenSession);
        return gateway;
    }

    public static SaleBuilder Builder(IPosGateway gateway, IDiscountPolicyCatalog? policies = null) =>
        new(gateway, policies ?? Policies());
}
