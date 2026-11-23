using NSubstitute;
using Pos.Frontend.Application;
using Pos.Frontend.Domain;
using Pos.Shared;

namespace Pos.Frontend.UnitTests;

// The cashier sees why a discount is not allowed before calling the API.
public class DiscountValidationTests
{
    static readonly DiscountPolicyDto[] Policies =
    [
        new("NONE", "Sin descuento", false, null),
        new("MANUAL", "Manual", true, 10m),
        new("STUDENT", "Estudiante", false, 10m, RequiresIdentifiedCustomer: true),
        new("VOLUME", "Volumen", false, 5m, MinimumAmount: 100m)
    ];

    static CheckoutUseCase Checkout(decimal coffees = 1)
    {
        var c = new CheckoutUseCase(Substitute.For<IPosApi>()) { Policies = Policies };
        c.Cart.Add(new CartProduct(1, "Café", null, 2.50m, 15m, false), coffees);
        return c;
    }

    [Fact]
    public async Task Volume_discount_below_100_is_blocked_before_calling_the_API()
    {
        var api = Substitute.For<IPosApi>();
        var c = new CheckoutUseCase(api) { Policies = Policies, DiscountPolicyCode = "VOLUME" };
        c.Cart.Add(new CartProduct(1, "Café", null, 2.50m, 15m, false));

        Assert.Contains("compra mínima de $ 100", c.ValidateDiscount());
        Assert.Equal(0m, c.ProvisionalDiscountPercent);
        var result = await c.ConfirmAsync(1);
        Assert.False(result.Success);
        await api.DidNotReceiveWithAnyArgs().RegisterSaleAsync(default, default!);
    }

    [Fact]
    public void Volume_discount_from_100_is_shown_in_the_provisional_total()
    {
        var c = Checkout(35);   // 100.63
        c.DiscountPolicyCode = "VOLUME";
        Assert.Null(c.ValidateDiscount());
        Assert.Equal(5m, c.ProvisionalDiscountPercent);
    }

    [Fact]
    public void Student_discount_needs_a_customer_with_cedula()
    {
        var c = Checkout();
        c.DiscountPolicyCode = "STUDENT";
        Assert.Contains("cédula", c.ValidateDiscount());

        c.CustomerRequired = true;
        c.CustomerIdentification = "1710034065";
        c.CustomerName = "Ana Torres";
        Assert.Null(c.ValidateDiscount());
    }

    [Theory]
    [InlineData(null, "Indique el porcentaje")]
    [InlineData(0.0, "mayor que cero")]
    [InlineData(12.0, "máximo autorizado es 10")]
    [InlineData(5.555, "2 decimales")]
    public void Manual_percent_is_validated(double? percent, string expected)
    {
        var c = Checkout();
        c.DiscountPolicyCode = "MANUAL";
        c.DiscountPercent = percent is null ? null : (decimal)percent.Value;
        Assert.Contains(expected, c.ValidateDiscount());
    }

    [Fact]
    public void Con_cedula_selected_but_empty_is_not_sent_as_consumidor_final()
    {
        var c = Checkout();
        c.CustomerRequired = true;
        Assert.Contains("cédula", c.ValidateBeforeSending(1));
    }

    [Theory]
    [InlineData("Juan123")]
    [InlineData("!\"#*")]
    [InlineData("Jo")]
    public void Customer_name_must_be_letters(string name)
    {
        var c = Checkout();
        c.CustomerRequired = true;
        c.CustomerIdentification = "1710034065";
        c.CustomerName = name;
        Assert.NotNull(c.ValidateCustomer());
    }

    [Fact]
    public void Cart_limits_quantity_per_line() =>
        Assert.ThrowsAny<ArgumentException>(() => new Cart().Add(new CartProduct(1, "Café", null, 1m, 15m, false), 1000));
}
