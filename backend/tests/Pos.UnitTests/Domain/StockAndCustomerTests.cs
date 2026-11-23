using Pos.Application;
using Pos.Domain;
using Pos.Domain.Catalog;
using Pos.Domain.Customers;
using Pos.Domain.Sales;
using Pos.Shared;

namespace Pos.UnitTests.Domain;

// RN-09: stock reported by Odoo.
public class StockTests
{
    static CatalogProduct WithStock(decimal? stock) =>
        new(20, "Croissant", "CRO-01", 1.80m, TestData.Iva15, true, stock);

    [Fact]
    public void Can_sell_up_to_the_available_stock()
    {
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(WithStock(3), 3);
        Assert.Equal(3, sale.Lines[0].Quantity);
    }

    [Fact]
    public void Cannot_sell_more_than_the_available_stock()
    {
        var sale = new Sale(Guid.NewGuid());
        var ex = Assert.Throws<DomainException>(() => sale.AddProduct(WithStock(3), 4));
        Assert.Contains("se piden 4 y hay 3", ex.Message);
    }

    [Fact]
    public void Repeated_lines_are_added_before_checking_stock()
    {
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(WithStock(3), 2);
        Assert.Throws<DomainException>(() => sale.AddProduct(WithStock(3), 2));
        Assert.Equal(2, sale.Lines[0].Quantity);
    }

    [Fact]
    public void Out_of_stock_product_cannot_be_sold()
    {
        var ex = Assert.Throws<DomainException>(() => new Sale(Guid.NewGuid()).AddProduct(WithStock(0), 1));
        Assert.Contains("agotado", ex.Message);
    }

    [Fact]
    public void Products_without_stock_control_are_not_limited()
    {
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(WithStock(null), 500);
        Assert.Single(sale.Lines);
    }

    [Fact]
    public async Task Api_rejects_a_sale_beyond_stock_before_calling_Odoo()
    {
        var gateway = TestData.Gateway();
        var request = new SaleRequest([new SaleLineRequest(TestData.Coffee.Id, 101)], null, null, TestData.Cash.Id);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            TestData.Builder(gateway).BuildAsync(Guid.NewGuid(), request, default));
        Assert.Contains("Stock insuficiente", ex.Errors["sale"][0]);
    }
}

// RN-10: customer identified by a valid Ecuadorian cedula.
public class CustomerTests
{
    [Theory]
    [InlineData("1710034065")]   // Pichincha
    [InlineData("0926687856")]   // Guayas
    [InlineData("1804567897")]   // Tungurahua
    [InlineData("3004567891")]   // province 30 (abroad)
    public void Valid_cedulas_are_accepted(string cedula) => Assert.True(Cedula.IsValid(cedula));

    [Theory]
    [InlineData("1710034066")]   // wrong check digit
    [InlineData("171003406")]    // 9 digits
    [InlineData("17100340651")]  // 11 digits
    [InlineData("17100A4065")]   // letters
    [InlineData("2510034065")]   // province 25 does not exist (check digit is right)
    [InlineData("1760034064")]   // third digit 6, not a person (check digit is right)
    [InlineData("")]
    [InlineData(null)]
    public void Invalid_cedulas_are_rejected(string? cedula) => Assert.False(Cedula.IsValid(cedula));

    [Fact]
    public void Customer_name_is_normalized()
    {
        var c = Customer.Create(" 1710034065 ", "  María   José  Pérez ");
        Assert.Equal("1710034065", c.Identification.Value);
        Assert.Equal("María José Pérez", c.Name);
    }

    [Theory]
    [InlineData("Jo")]
    [InlineData("Juan123")]
    [InlineData("")]
    public void Invalid_names_are_rejected(string name) =>
        Assert.Throws<DomainException>(() => Customer.Create("1710034065", name));

    [Fact]
    public async Task Api_rejects_a_sale_with_an_invalid_cedula()
    {
        var request = TestData.TwoProducts() with { Customer = new SaleCustomerRequest("1234567890", "Juan Pérez") };

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            TestData.Builder(TestData.Gateway()).BuildAsync(Guid.NewGuid(), request, default));
        Assert.Contains("no es válida", ex.Errors["customer"][0]);
    }

    [Fact]
    public async Task Sale_without_customer_is_consumidor_final()
    {
        var sale = await TestData.Builder(TestData.Gateway()).BuildAsync(Guid.NewGuid(), TestData.TwoProducts(), default);
        Assert.Null(sale.Customer);
    }
}
