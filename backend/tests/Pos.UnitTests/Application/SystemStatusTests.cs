using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pos.Application;
using Pos.Application.Ports;
using Pos.Application.Status;

namespace Pos.UnitTests.Application;

// GET /api/status: "Odoo is down" is an answer, never an exception.
public class SystemStatusTests
{
    [Fact]
    public async Task Reports_Odoo_down_without_failing()
    {
        var gateway = Substitute.For<IPosGateway>();
        gateway.GetActiveSessionAsync(default).ThrowsAsyncForAnyArgs(new ExternalServiceUnavailableException("No se pudo comunicar con Odoo."));

        var status = await new GetSystemStatusInteractor(gateway).HandleAsync(default);

        Assert.False(status.OdooAvailable);
        Assert.Contains("Odoo", status.Message);
    }

    [Fact]
    public async Task Reports_a_closed_cash_session()
    {
        var gateway = Substitute.For<IPosGateway>();
        gateway.GetActiveSessionAsync(default).ReturnsForAnyArgs((PosSession?)null);

        var status = await new GetSystemStatusInteractor(gateway).HandleAsync(default);

        Assert.True(status.OdooAvailable);
        Assert.False(status.SessionOpen);
    }

    [Fact]
    public async Task Reports_online_with_the_open_session()
    {
        var gateway = TestData.Gateway();

        var status = await new GetSystemStatusInteractor(gateway).HandleAsync(default);

        Assert.True(status.OdooAvailable);
        Assert.True(status.SessionOpen);
        Assert.NotNull(status.SessionName);
    }
}
