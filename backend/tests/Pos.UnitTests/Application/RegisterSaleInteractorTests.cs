using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pos.Application;
using Pos.Application.Ports;
using Pos.Application.Sales;
using Pos.Infrastructure.Idempotency;
using Pos.Shared;

namespace Pos.UnitTests.Application;

public class RegisterSaleInteractorTests
{
    readonly IIdempotencyStore _store = new InMemoryIdempotencyStore();
    readonly ICurrentOperator _operator = Substitute.For<ICurrentOperator>();

    RegisterSaleInteractor Create(IPosGateway gateway) =>
        new(gateway, TestData.Builder(gateway), _store, _operator);

    static readonly RegisteredOrder Order = new(77, "Shop/0001", "paid", 10.35m);

    [Fact]
    public async Task Registers_the_sale_when_Odoo_confirms_it()
    {
        var gateway = TestData.Gateway();
        gateway.SubmitPaidOrderAsync(Arg.Any<OrderSubmission>(), Arg.Any<CancellationToken>()).Returns(Order);

        var result = await Create(gateway).HandleAsync(Guid.NewGuid(), TestData.TwoProducts(), default);

        Assert.Equal(SaleRegistrationOutcome.Registered, result.Outcome);
        Assert.Equal(77, result.OrderId);
        await gateway.Received(1).SubmitPaidOrderAsync(
            Arg.Is<OrderSubmission>(s => s.Totals.AmountTotal == 10.35m && s.PaymentMethod.IsCash),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task P01_Rejects_a_sale_without_products_without_calling_Odoo()
    {
        var gateway = TestData.Gateway();
        var request = new SaleRequest([], null, null, TestData.Cash.Id);

        await Assert.ThrowsAsync<ValidationException>(() => Create(gateway).HandleAsync(Guid.NewGuid(), request, default));
        await gateway.DidNotReceiveWithAnyArgs().SubmitPaidOrderAsync(default!, default);
    }

    [Fact]
    public async Task P02_Rejects_zero_quantity()
    {
        var gateway = TestData.Gateway();
        var request = new SaleRequest([new SaleLineRequest(TestData.Coffee.Id, 0)], null, null, TestData.Cash.Id);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Create(gateway).HandleAsync(Guid.NewGuid(), request, default));
        Assert.Contains("lines[0].quantity", ex.Errors.Keys);
        await gateway.DidNotReceiveWithAnyArgs().SubmitPaidOrderAsync(default!, default);
    }

    [Theory]
    [InlineData("closing_control")]
    [InlineData("closed")]
    public async Task P03_Rejects_a_closed_POS_session(string state)
    {
        var gateway = TestData.Gateway(TestData.OpenSession with { State = state });

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            Create(gateway).HandleAsync(Guid.NewGuid(), TestData.TwoProducts(), default));
        Assert.Contains("session", ex.Errors.Keys);
        await gateway.DidNotReceiveWithAnyArgs().SubmitPaidOrderAsync(default!, default);
    }

    [Fact]
    public async Task P03_Rejects_when_there_is_no_session()
    {
        var gateway = TestData.Gateway();
        gateway.GetActiveSessionAsync(Arg.Any<CancellationToken>()).Returns((PosSession?)null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            Create(gateway).HandleAsync(Guid.NewGuid(), TestData.TwoProducts(), default));
    }

    [Fact]
    public async Task P04_Rejects_a_product_not_enabled_and_does_not_send_the_sale()
    {
        var gateway = TestData.Gateway();
        var request = new SaleRequest([new SaleLineRequest(TestData.Disabled.Id, 1)], null, null, TestData.Cash.Id);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Create(gateway).HandleAsync(Guid.NewGuid(), request, default));
        Assert.Contains("no está habilitado", ex.Errors["sale"][0]);
        await gateway.DidNotReceiveWithAnyArgs().SubmitPaidOrderAsync(default!, default);
    }

    [Fact]
    public async Task P04_Rejects_an_unknown_product()
    {
        var gateway = TestData.Gateway();
        var request = new SaleRequest([new SaleLineRequest(999, 1)], null, null, TestData.Cash.Id);

        await Assert.ThrowsAsync<ValidationException>(() => Create(gateway).HandleAsync(Guid.NewGuid(), request, default));
        await gateway.DidNotReceiveWithAnyArgs().SubmitPaidOrderAsync(default!, default);
    }

    [Fact]
    public async Task Rejects_a_payment_method_that_is_not_cash()
    {
        var gateway = TestData.Gateway();
        var request = TestData.TwoProducts() with { PaymentMethodId = TestData.Card.Id };

        await Assert.ThrowsAsync<ValidationException>(() => Create(gateway).HandleAsync(Guid.NewGuid(), request, default));
    }

    [Fact]
    public async Task P05_Timeout_does_not_report_success()
    {
        var gateway = TestData.Gateway();
        gateway.SubmitPaidOrderAsync(Arg.Any<OrderSubmission>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExternalServiceUnavailableException("timeout"));
        gateway.FindOrderByReferenceAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((RegisteredOrder?)null, (RegisteredOrder?)null);

        var result = await Create(gateway).HandleAsync(Guid.NewGuid(), TestData.TwoProducts(), default);

        Assert.Equal(SaleRegistrationOutcome.Uncertain, result.Outcome);
        Assert.Null(result.OrderId);
    }

    [Fact]
    public async Task P05_An_Odoo_rejection_is_propagated_as_an_error()
    {
        var gateway = TestData.Gateway();
        gateway.SubmitPaidOrderAsync(Arg.Any<OrderSubmission>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExternalServiceRejectedException("Odoo rechazó la operación"));

        await Assert.ThrowsAsync<ExternalServiceRejectedException>(() =>
            Create(gateway).HandleAsync(Guid.NewGuid(), TestData.TwoProducts(), default));
    }

    [Fact]
    public async Task P05_After_a_timeout_the_order_found_in_Odoo_is_reported_as_registered()
    {
        var key = Guid.NewGuid();
        var gateway = TestData.Gateway();
        gateway.SubmitPaidOrderAsync(Arg.Any<OrderSubmission>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ExternalServiceUnavailableException("timeout"));
        gateway.FindOrderByReferenceAsync(key, Arg.Any<CancellationToken>())
            .Returns((RegisteredOrder?)null, Order);

        var result = await Create(gateway).HandleAsync(key, TestData.TwoProducts(), default);

        Assert.Equal(SaleRegistrationOutcome.Registered, result.Outcome);
        Assert.Equal(77, result.OrderId);
    }

    [Fact]
    public async Task P06_A_repeated_request_does_not_create_a_second_order()
    {
        var key = Guid.NewGuid();
        var gateway = TestData.Gateway();
        gateway.SubmitPaidOrderAsync(Arg.Any<OrderSubmission>(), Arg.Any<CancellationToken>()).Returns(Order);
        var useCase = Create(gateway);

        var first = await useCase.HandleAsync(key, TestData.TwoProducts(), default);
        var second = await useCase.HandleAsync(key, TestData.TwoProducts(), default);

        Assert.Equal(SaleRegistrationOutcome.Registered, first.Outcome);
        Assert.Equal(SaleRegistrationOutcome.AlreadyRegistered, second.Outcome);
        Assert.Equal(first.OrderId, second.OrderId);
        await gateway.Received(1).SubmitPaidOrderAsync(Arg.Any<OrderSubmission>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task P06_A_retry_after_an_uncertain_result_finds_the_existing_order_in_Odoo()
    {
        var key = Guid.NewGuid();
        var gateway = TestData.Gateway();
        // Simulates an API restart: the order already exists in Odoo with this uuid.
        gateway.FindOrderByReferenceAsync(key, Arg.Any<CancellationToken>()).Returns(Order);

        var result = await Create(gateway).HandleAsync(key, TestData.TwoProducts(), default);

        Assert.Equal(SaleRegistrationOutcome.AlreadyRegistered, result.Outcome);
        await gateway.DidNotReceiveWithAnyArgs().SubmitPaidOrderAsync(default!, default);
    }

    [Fact]
    public async Task P06_A_concurrent_request_with_the_same_key_is_rejected()
    {
        var key = Guid.NewGuid();
        var gateway = TestData.Gateway();
        var pending = new TaskCompletionSource<RegisteredOrder>();
        gateway.SubmitPaidOrderAsync(Arg.Any<OrderSubmission>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var useCase = Create(gateway);

        var first = useCase.HandleAsync(key, TestData.TwoProducts(), default);
        await Assert.ThrowsAsync<OperationInProgressException>(() => useCase.HandleAsync(key, TestData.TwoProducts(), default));

        pending.SetResult(Order);
        Assert.Equal(SaleRegistrationOutcome.Registered, (await first).Outcome);
        await gateway.Received(1).SubmitPaidOrderAsync(Arg.Any<OrderSubmission>(), Arg.Any<CancellationToken>());
    }
}
