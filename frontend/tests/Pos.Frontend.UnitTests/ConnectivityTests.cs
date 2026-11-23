using NSubstitute;
using Pos.Frontend.Application;
using Pos.Shared;

namespace Pos.Frontend.UnitTests;

// The POS notices at once when Odoo or the API goes down, blocks charging,
// and recovers by itself.
public class ConnectivityTests
{
    static SystemStatusDto Status(bool odoo, bool session) =>
        new(odoo, session, session ? "POS/00001" : null, odoo ? "ok" : "Odoo no está disponible.", DateTime.UtcNow);

    [Fact]
    public void A_503_from_any_call_marks_Odoo_down_and_blocks_charging()
    {
        var state = new ConnectivityState();
        state.Observe(503, failed: true, touchesOdoo: true);

        Assert.Equal(ConnectionStatus.OdooDown, state.Status);
        Assert.False(state.CanCharge);
        Assert.Contains("Odoo", state.ChargeBlockedReason);
        Assert.NotNull(state.DownSinceUtc);
    }

    [Fact]
    public void A_network_failure_marks_the_API_down()
    {
        var state = new ConnectivityState();
        state.Observe(null, failed: true, touchesOdoo: true);
        Assert.Equal(ConnectionStatus.ApiDown, state.Status);
    }

    [Fact]
    public void Recovers_when_the_status_check_says_Odoo_is_back()
    {
        var state = new ConnectivityState();
        int notifications = 0;
        state.Changed += () => notifications++;

        state.Observe(503, true, true);
        state.Apply(Status(odoo: true, session: true));

        Assert.Equal(ConnectionStatus.Online, state.Status);
        Assert.True(state.CanCharge);
        Assert.True(state.JustRecovered);
        Assert.Null(state.DownSinceUtc);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void A_call_that_does_not_need_Odoo_does_not_hide_the_outage()
    {
        var state = new ConnectivityState();
        state.Observe(503, true, true);
        state.Observe(200, failed: false, touchesOdoo: false);   // e.g. discount policies
        Assert.Equal(ConnectionStatus.OdooDown, state.Status);
    }

    [Fact]
    public void Closed_cash_session_blocks_charging()
    {
        var state = new ConnectivityState();
        state.Apply(Status(odoo: true, session: false));
        Assert.Equal(ConnectionStatus.NoSession, state.Status);
        Assert.False(state.CanCharge);
    }

    [Fact]
    public async Task Checks_more_often_while_down()
    {
        var api = Substitute.For<IPosApi>();
        api.GetSystemStatusAsync().Returns(ApiResult<SystemStatusDto>.Ok(Status(odoo: false, session: false)));
        var state = new ConnectivityState();
        var useCase = new SystemStatusUseCase(api, state);

        await useCase.CheckAsync();

        Assert.Equal(ConnectionStatus.OdooDown, state.Status);
        Assert.Equal(SystemStatusUseCase.DownInterval, useCase.NextInterval);
    }
}
