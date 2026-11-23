using Pos.Application.Ports;
using Pos.Domain.Customers;
using Pos.Shared;

namespace Pos.Application.Customers;

public interface IFindCustomerInputPort
{
    Task<CustomerDto> HandleAsync(string identification, CancellationToken ct);
}

// Looks up a customer in Odoo by cedula so the cashier does not retype the name.
public sealed class FindCustomerInteractor(IPosGateway gateway) : IFindCustomerInputPort
{
    public async Task<CustomerDto> HandleAsync(string identification, CancellationToken ct)
    {
        string id = (identification ?? "").Trim();
        if (!Cedula.IsValid(id))
            throw ValidationException.For("identification", $"La cédula '{id}' no es válida.");

        return await gateway.FindCustomerAsync(id, ct)
            ?? throw new NotFoundException($"No hay un cliente con la cédula {id} en Odoo.");
    }
}
