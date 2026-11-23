using Pos.Application;
using Pos.Application.Sales;
using Pos.Domain.Discounts;

namespace Pos.UnitTests.Application;

// P10: a new discount rule is added by writing one class and registering it.
// Sale, SaleBuilder, the use cases and the other policies are not modified.
public class DiscountExtensibilityTests
{
    // Example of a requirement change: "10 % for employees".
    sealed class EmployeeDiscountPolicy : IDiscountPolicy
    {
        public string Code => "EMPLOYEE";
        public string Description => "Descuento de empleado (10 %)";
        public bool RequiresPercent => false;
        public decimal? MaxPercent => 10m;
        public DiscountDecision Evaluate(DiscountContext context) => DiscountDecision.Approve(10m);
    }

    [Fact]
    public async Task P10_A_new_policy_works_without_changing_existing_components()
    {
        var gateway = TestData.Gateway();
        var builder = new SaleBuilder(gateway, TestData.Policies(new EmployeeDiscountPolicy()));

        var sale = await builder.BuildAsync(Guid.NewGuid(), TestData.TwoProducts("EMPLOYEE"), default);

        Assert.Equal(10m, sale.DiscountPercent);
        Assert.Equal("EMPLOYEE", sale.DiscountPolicyCode);
    }

    [Fact]
    public async Task An_unregistered_policy_is_rejected()
    {
        var builder = TestData.Builder(TestData.Gateway());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            builder.BuildAsync(Guid.NewGuid(), TestData.TwoProducts("EMPLOYEE"), default));
        Assert.Contains("no está habilitada", ex.Errors["discountPolicyCode"][0]);
    }

    [Fact]
    public async Task Volume_discount_requires_the_minimum_amount()
    {
        var builder = TestData.Builder(TestData.Gateway());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            builder.BuildAsync(Guid.NewGuid(), TestData.TwoProducts(VolumeDiscountPolicy.PolicyCode), default));
        Assert.Contains("compra mínima", ex.Errors["sale"][0]);
    }

    [Fact]
    public async Task Manual_discount_above_the_limit_is_rejected()
    {
        var builder = TestData.Builder(TestData.Gateway());

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            builder.BuildAsync(Guid.NewGuid(), TestData.TwoProducts(ManualPercentageDiscountPolicy.PolicyCode, 50m), default));
        Assert.Contains("no está autorizado", ex.Errors["sale"][0]);
    }
}
