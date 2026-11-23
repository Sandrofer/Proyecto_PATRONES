using Pos.Application.Ports;
using Pos.Shared;

namespace Pos.Application.Status;

public interface IGetSystemStatusInputPort
{
    Task<SystemStatusDto> HandleAsync(CancellationToken ct);
}

// Tells the browser whether Odoo answers and whether the cash register
// session is open, so the POS can warn and block charging before the cashier
// tries (RN-07: never a false success). It never throws for Odoo failures:
// "Odoo is down" is a valid answer, not an error of this endpoint.
public sealed class GetSystemStatusInteractor(IPosGateway gateway) : IGetSystemStatusInputPort
{
    public async Task<SystemStatusDto> HandleAsync(CancellationToken ct)
    {
        try
        {
            var session = await gateway.GetActiveSessionAsync(ct);
            return session is { IsOpen: true }
                ? new SystemStatusDto(true, true, session.Name, "Odoo conectado.", DateTime.UtcNow)
                : new SystemStatusDto(true, false, null,
                    "Odoo responde, pero no hay una sesión de caja abierta. Ábrala en Odoo (Punto de venta → Abrir caja).",
                    DateTime.UtcNow);
        }
        catch (ExternalServiceUnavailableException ex)
        {
            return new SystemStatusDto(false, false, null, ex.Message, DateTime.UtcNow);
        }
        catch (ExternalServiceRejectedException)
        {
            return new SystemStatusDto(false, false, null,
                "Odoo rechazó la conexión de la API (revise la API key del usuario de integración).", DateTime.UtcNow);
        }
    }
}
