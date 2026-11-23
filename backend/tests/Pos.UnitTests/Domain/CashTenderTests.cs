using NSubstitute;
using Pos.Application;
using Pos.Application.Ports;
using Pos.Application.Sales;
using Pos.Domain;
using Pos.Domain.Sales;
using Pos.Infrastructure.Idempotency;

namespace Pos.UnitTests.Domain;

// RF-11: cash received and change.
public class CashTenderTests
{
    [Fact]
    public void Without_amount_the_customer_pays_exactly()
    {
        var cash = CashTender.Create(10.35m, null);
        Assert.Equal(10.35m, cash.AmountTendered);
        Assert.Equal(0m, cash.Change);
    }

    [Fact]
    public void Change_is_the_difference()
    {
        var cash = CashTender.Create(10.35m, 20m);
        Assert.Equal(9.65m, cash.Change);
    }

    [Theory]
    [InlineData(0.0, "mayor que cero")]
    [InlineData(-5.0, "mayor que cero")]
    [InlineData(10.355, "2 decimales")]
    [InlineData(10.0, "no cubre el total")]
    [InlineData(500.0, "vuelto no puede superar")]
    public void Invalid_amounts_are_rejected(double tendered, string expected)
    {
        var ex = Assert.Throws<DomainException>(() => CashTender.Create(10.35m, (decimal)tendered));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public async Task Api_rejects_cash_that_does_not_cover_the_total_before_calling_Odoo()
    {
        var gateway = TestData.Gateway();
        var interactor = new RegisterSaleInteractor(gateway, TestData.Builder(gateway),
            new InMemoryIdempotencyStore(), Substitute.For<ICurrentOperator>());
        var request = TestData.TwoProducts() with { AmountTendered = 5m };   // total 10.35

        var ex = await Assert.ThrowsAsync<ValidationException>(() => interactor.HandleAsync(Guid.NewGuid(), request, default));

        Assert.Contains("no cubre", ex.Errors["amountTendered"][0]);
        await gateway.DidNotReceiveWithAnyArgs().SubmitPaidOrderAsync(default!, default);
    }

    [Fact]
    public async Task Odoo_receives_the_cash_received_and_the_change()
    {
        var gateway = TestData.Gateway();
        gateway.SubmitPaidOrderAsync(Arg.Any<OrderSubmission>(), Arg.Any<CancellationToken>())
            .Returns(new RegisteredOrder(77, "Shop/0001", "paid", 10.35m, 9.65m));
        var interactor = new RegisterSaleInteractor(gateway, TestData.Builder(gateway),
            new InMemoryIdempotencyStore(), Substitute.For<ICurrentOperator>());

        var result = await interactor.HandleAsync(Guid.NewGuid(), TestData.TwoProducts() with { AmountTendered = 20m }, default);

        Assert.Equal(9.65m, result.Change);
        await gateway.Received(1).SubmitPaidOrderAsync(
            Arg.Is<OrderSubmission>(s => s.Cash!.AmountTendered == 20m && s.Cash.Change == 9.65m),
            Arg.Any<CancellationToken>());
    }
}
