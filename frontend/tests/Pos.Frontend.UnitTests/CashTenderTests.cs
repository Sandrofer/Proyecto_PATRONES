using NSubstitute;
using Pos.Frontend.Application;
using Pos.Frontend.Domain;
using Pos.Shared;

namespace Pos.Frontend.UnitTests;

// The cashier sees the change at once and cannot charge with too little cash.
public class CashTenderTests
{
    static CheckoutUseCase Checkout(IPosApi? api = null)
    {
        var c = new CheckoutUseCase(api ?? Substitute.For<IPosApi>())
        {
            Policies = [new DiscountPolicyDto("NONE", "Sin descuento", false, null)]
        };
        c.Cart.Add(new CartProduct(1, "Café", null, 2.50m, 15m, false));   // 2.88
        return c;
    }

    [Fact]
    public void Shows_the_change()
    {
        var c = Checkout();
        c.AmountTendered = 5m;
        Assert.Null(c.ValidateCash());
        Assert.Equal(5m - c.AmountDue, c.Change);
    }

    [Fact]
    public async Task Too_little_cash_is_blocked_before_calling_the_API()
    {
        var api = Substitute.For<IPosApi>();
        var c = Checkout(api);
        c.AmountTendered = 1m;

        Assert.Contains("no cubre", c.ValidateBeforeSending(1));
        Assert.False((await c.ConfirmAsync(1)).Success);
        await api.DidNotReceiveWithAnyArgs().RegisterSaleAsync(default, default!);
    }

    [Theory]
    [InlineData(500.0, "vuelto no puede superar")]
    [InlineData(5.555, "2 decimales")]
    [InlineData(0.0, "mayor que cero")]
    public void Invalid_amounts_are_explained(double tendered, string expected)
    {
        var c = Checkout();
        c.AmountTendered = (decimal)tendered;
        Assert.Contains(expected, c.ValidateCash());
    }

    [Fact]
    public void The_amount_is_sent_to_the_API()
    {
        var c = Checkout();
        c.AmountTendered = 10m;
        Assert.Equal(10m, c.BuildRequest(1).AmountTendered);
    }

    [Fact]
    public void Suggests_bills_that_cover_the_total() =>
        Assert.Equal([5m, 10m, 20m], CashTender.SuggestedBills(2.88m));
}
