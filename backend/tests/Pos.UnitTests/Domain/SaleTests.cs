using Pos.Domain;
using Pos.Domain.Catalog;
using Pos.Domain.Discounts;
using Pos.Domain.Sales;

namespace Pos.UnitTests.Domain;

public class SaleTests
{
    [Fact]
    public void P01_A_sale_without_products_cannot_be_confirmed()
    {
        var sale = new Sale(Guid.NewGuid());

        var ex = Assert.Throws<DomainException>(sale.Confirm);
        Assert.Contains("al menos un producto", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void P02_Zero_or_negative_quantities_are_rejected(decimal quantity)
    {
        var sale = new Sale(Guid.NewGuid());

        Assert.Throws<DomainException>(() => sale.AddProduct(TestData.Coffee, quantity));
        Assert.Empty(sale.Lines);
    }

    [Fact]
    public void P04_A_product_not_enabled_for_the_POS_is_rejected()
    {
        var sale = new Sale(Guid.NewGuid());

        Assert.Throws<DomainException>(() => sale.AddProduct(TestData.Disabled, 1));
    }

    [Fact]
    public void Adding_the_same_product_twice_merges_the_quantities()
    {
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(TestData.Coffee, 1);
        sale.AddProduct(TestData.Coffee, 2);

        var line = Assert.Single(sale.Lines);
        Assert.Equal(3, line.Quantity);
    }

    [Fact]
    public void Totals_add_the_excluded_tax_per_line()
    {
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(TestData.Coffee, 2);   // 5.00 + 0.75
        sale.AddProduct(TestData.Sandwich, 1); // 4.00 + 0.60

        var totals = sale.CalculateTotals();

        Assert.Equal(9.00m, totals.AmountUntaxed);
        Assert.Equal(1.35m, totals.AmountTax);
        Assert.Equal(10.35m, totals.AmountTotal);
    }

    // Real case found against Odoo: per-line rounding gives 4.32, but a company
    // configured with round_globally charges 4.31.
    [Theory]
    [InlineData(TaxRoundingMode.PerLine, 0.57, 4.32)]
    [InlineData(TaxRoundingMode.Global, 0.56, 4.31)]
    public void Tax_rounding_follows_the_Odoo_company_setting(TaxRoundingMode mode, decimal tax, decimal total)
    {
        var sale = new Sale(Guid.NewGuid(), mode);
        sale.AddProduct(TestData.Coffee with { UnitPrice = 1.50m }, 1); // tax 0.225
        sale.AddProduct(TestData.Sandwich with { UnitPrice = 2.25m }, 1); // tax 0.3375

        var totals = sale.CalculateTotals();

        Assert.Equal(3.75m, totals.AmountUntaxed);
        Assert.Equal(tax, totals.AmountTax);
        Assert.Equal(total, totals.AmountTotal);
    }

    [Fact]
    public void Included_tax_is_extracted_from_the_price()
    {
        var product = TestData.Coffee with { UnitPrice = 11.50m, Taxes = new TaxSet([new TaxRule(1, 15m, true)]) };
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(product, 1);

        var totals = sale.CalculateTotals();

        Assert.Equal(10.00m, totals.AmountUntaxed);
        Assert.Equal(1.50m, totals.AmountTax);
        Assert.Equal(11.50m, totals.AmountTotal);
    }

    [Fact]
    public void An_authorized_discount_is_applied_to_every_line()
    {
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(TestData.Coffee, 2);
        sale.AddProduct(TestData.Sandwich, 1);

        sale.ApplyDiscount(new ManualPercentageDiscountPolicy(10m), 10m);

        var totals = sale.CalculateTotals();
        Assert.Equal(10m, sale.DiscountPercent);
        Assert.Equal(8.10m, totals.AmountUntaxed);
        Assert.Equal(9.32m, totals.AmountTotal);
    }

    [Fact]
    public void A_discount_above_the_authorized_maximum_is_rejected_with_a_clear_message()
    {
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(TestData.Coffee, 1);

        var ex = Assert.Throws<DomainException>(() => sale.ApplyDiscount(new ManualPercentageDiscountPolicy(10m), 25m));

        Assert.Contains("no está autorizado", ex.Message);
        Assert.Equal(0m, sale.DiscountPercent);
    }

    [Fact]
    public void A_confirmed_sale_cannot_be_edited()
    {
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(TestData.Coffee, 1);
        sale.Confirm();

        Assert.Throws<DomainException>(() => sale.AddProduct(TestData.Sandwich, 1));
        Assert.Throws<DomainException>(() => sale.ApplyDiscount(new NoDiscountPolicy(), null));
        Assert.Equal(SaleStatus.Confirmed, sale.Status);
    }
}
