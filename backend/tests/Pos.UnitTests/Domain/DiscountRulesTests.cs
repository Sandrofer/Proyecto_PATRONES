using Pos.Application;
using Pos.Application.Discounts;
using Pos.Domain;
using Pos.Domain.Customers;
using Pos.Domain.Discounts;
using Pos.Domain.Sales;
using Pos.Shared;

namespace Pos.UnitTests.Domain;

// RN-04 / RF-08: every discount condition, and the sale limits.
public class DiscountRulesTests
{
    static Sale SaleOf(decimal quantity)
    {
        var sale = new Sale(Guid.NewGuid());
        sale.AddProduct(TestData.Coffee, quantity);   // 2.50 + 15 % = 2.875 each
        return sale;
    }

    [Fact]
    public void Volume_discount_is_rejected_below_the_minimum()
    {
        var ex = Assert.Throws<DomainException>(() => SaleOf(1).ApplyDiscount(new VolumeDiscountPolicy(100m, 5m), null));
        Assert.Contains("compra mínima de 100", ex.Message);
    }

    [Fact]
    public void Volume_discount_is_applied_from_the_minimum()
    {
        var sale = SaleOf(35);                       // 35 x 2.875 = 100.63
        sale.ApplyDiscount(new VolumeDiscountPolicy(100m, 5m), null);
        Assert.Equal(5m, sale.DiscountPercent);
    }

    [Fact]
    public void Student_discount_requires_an_identified_customer()
    {
        var ex = Assert.Throws<DomainException>(() => SaleOf(1).ApplyDiscount(new StudentDiscountPolicy(10m), null));
        Assert.Contains("cédula", ex.Message);
    }

    [Fact]
    public void Student_discount_is_applied_to_a_customer_with_cedula()
    {
        var sale = SaleOf(1);
        sale.AssignCustomer(Customer.Create("1710034065", "Ana Torres"));
        sale.ApplyDiscount(new StudentDiscountPolicy(10m), null);
        Assert.Equal(10m, sale.DiscountPercent);
    }

    [Fact]
    public void Customer_cannot_change_after_the_discount_was_decided()
    {
        var sale = SaleOf(1);
        sale.AssignCustomer(Customer.Create("1710034065", "Ana Torres"));
        sale.ApplyDiscount(new StudentDiscountPolicy(10m), null);
        Assert.Throws<DomainException>(() => sale.AssignCustomer(null));
    }

    [Theory]
    [InlineData(null, "Indique el porcentaje")]
    [InlineData(0.0, "mayor que cero")]
    [InlineData(-5.0, "mayor que cero")]
    [InlineData(10.5, "máximo permitido es 10")]
    public void Manual_discount_rejects_invalid_percents(double? percent, string expected)
    {
        var decision = new ManualPercentageDiscountPolicy(10m)
            .Evaluate(new DiscountContext(50m, percent is null ? null : (decimal)percent.Value));
        Assert.False(decision.IsApproved);
        Assert.Contains(expected, decision.RejectionReason);
    }

    [Fact]
    public void Policies_publish_their_conditions_to_the_browser()
    {
        var dtos = new GetDiscountPoliciesInteractor(new DiscountPolicyCatalog(
            [new NoDiscountPolicy(), new StudentDiscountPolicy(10m), new VolumeDiscountPolicy(100m, 5m)])).Handle();

        Assert.True(dtos.Single(d => d.Code == "STUDENT").RequiresIdentifiedCustomer);
        Assert.Equal(100m, dtos.Single(d => d.Code == "VOLUME").MinimumAmount);
        Assert.Null(dtos.Single(d => d.Code == "NONE").MinimumAmount);
    }

    [Fact]
    public void A_line_cannot_exceed_999_units() =>
        Assert.Throws<DomainException>(() => new Sale(Guid.NewGuid()).AddProduct(TestData.Sandwich, 1000));

    [Fact]
    public async Task Api_rejects_more_than_999_units_per_line()
    {
        var request = new SaleRequest([new SaleLineRequest(TestData.Sandwich.Id, 1000)], null, null, TestData.Cash.Id);
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            TestData.Builder(TestData.Gateway()).BuildAsync(Guid.NewGuid(), request, default));
        Assert.Contains("999", ex.Errors["lines[0].quantity"][0]);
    }

    [Fact]
    public async Task Api_rejects_more_than_50_lines()
    {
        var lines = Enumerable.Range(1, 51).Select(i => new SaleLineRequest(i, 1)).ToList();
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            TestData.Builder(TestData.Gateway()).BuildAsync(Guid.NewGuid(), new SaleRequest(lines, null, null, 1), default));
        Assert.Contains("50", ex.Errors["lines"][0]);
    }
}
