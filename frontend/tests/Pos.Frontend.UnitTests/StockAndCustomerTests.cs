using NSubstitute;
using Pos.Frontend.Application;
using Pos.Frontend.Domain;
using Pos.Shared;

namespace Pos.Frontend.UnitTests;

public class CartStockTests
{
    static CartProduct Croissant(decimal? stock) => new(4, "Croissant", "CRO-01", 1.80m, 15m, false, stock);

    [Fact]
    public void Cart_does_not_exceed_stock_when_adding()
    {
        var cart = new Cart();
        cart.Add(Croissant(2));
        cart.Add(Croissant(2));
        var ex = Assert.Throws<InvalidOperationException>(() => cart.Add(Croissant(2)));
        Assert.Contains("Solo hay 2", ex.Message);
        Assert.Equal(2, cart.Lines[0].Quantity);
    }

    [Fact]
    public void Cart_does_not_exceed_stock_when_typing_a_quantity()
    {
        var cart = new Cart();
        cart.Add(Croissant(3));
        Assert.Throws<InvalidOperationException>(() => cart.SetQuantity(4, 10));
        Assert.Equal(1, cart.Lines[0].Quantity);
    }

    [Fact]
    public void Out_of_stock_products_cannot_be_added()
    {
        Assert.True(Croissant(0).IsOutOfStock);
        Assert.Throws<InvalidOperationException>(() => new Cart().Add(Croissant(0)));
    }

    [Fact]
    public void Products_without_stock_control_have_no_limit()
    {
        var cart = new Cart();
        cart.Add(Croissant(null), 999);
        Assert.Null(Cart.RemainingStock(cart.Lines[0].Product, 999));
    }
}

public class CheckoutCustomerTests
{
    [Theory]
    [InlineData("1710034065", true)]
    [InlineData("1710034066", false)]
    [InlineData("abc", false)]
    public void Cedula_is_checked_in_the_browser(string value, bool valid) => Assert.Equal(valid, Cedula.IsValid(value));

    [Fact]
    public void Without_customer_the_request_is_consumidor_final()
    {
        var checkout = new CheckoutUseCase(Substitute.For<IPosApi>());
        Assert.Null(checkout.BuildRequest(1).Customer);
        Assert.Null(checkout.ValidateCustomer());
    }

    [Fact]
    public async Task Invalid_cedula_blocks_the_sale_before_calling_the_API()
    {
        var api = Substitute.For<IPosApi>();
        var checkout = new CheckoutUseCase(api) { CustomerIdentification = "1234567890", CustomerName = "Juan Pérez" };
        checkout.Cart.Add(new CartProduct(1, "Café", null, 1.5m, 15m, false));

        var result = await checkout.ConfirmAsync(1);

        Assert.False(result.Success);
        Assert.Contains("cédula", result.Message);
        await api.DidNotReceiveWithAnyArgs().RegisterSaleAsync(default, default!);
    }

    [Fact]
    public void Valid_customer_is_sent_to_the_API()
    {
        var checkout = new CheckoutUseCase(Substitute.For<IPosApi>())
        {
            CustomerIdentification = " 1710034065 ",
            CustomerName = " Ana Torres "
        };

        Assert.Equal(new SaleCustomerRequest("1710034065", "Ana Torres"), checkout.BuildRequest(1).Customer);
    }
}

public class CartWholeQuantityTests
{
    static readonly CartProduct Coffee = new(1, "Café", "CAF-01", 1.50m, 15m, false, 10);

    [Fact]
    public void Cart_rejects_fractional_quantities_with_a_clean_message()
    {
        var cart = new Cart();
        cart.Add(Coffee);

        var ex = Assert.ThrowsAny<ArgumentException>(() => cart.SetQuantity(Coffee.Id, 1.5m));

        Assert.Equal("La cantidad debe ser un número entero.", ex.Message);
        Assert.Equal(1, cart.Lines[0].Quantity);
    }

    [Fact]
    public void Cart_rejects_adding_a_fractional_quantity() =>
        Assert.ThrowsAny<ArgumentException>(() => new Cart().Add(Coffee, 0.5m));
}

public class LoginInputTests
{
    [Fact]
    public async Task Login_with_special_characters_is_rejected_in_the_browser()
    {
        var api = Substitute.For<IPosApi>();
        var error = await new LoginUseCase(api, Substitute.For<IAuthSession>()).ExecuteAsync("!\"#*", "x");

        Assert.Equal(InputRules.UsernameCharactersMessage, error);
        await api.DidNotReceiveWithAnyArgs().LoginAsync(default!);
    }
}
