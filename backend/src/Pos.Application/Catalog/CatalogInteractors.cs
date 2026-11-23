using Pos.Application.Ports;
using Pos.Domain.Catalog;
using Pos.Shared;

namespace Pos.Application.Catalog;

public interface ISearchProductsInputPort
{
    Task<IReadOnlyList<ProductDto>> HandleAsync(string? search, CancellationToken ct);
}

public sealed class SearchProductsInteractor(IPosGateway gateway) : ISearchProductsInputPort
{
    const int MaxResults = 40;
    const int MaxSearchLength = 100;

    public async Task<IReadOnlyList<ProductDto>> HandleAsync(string? search, CancellationToken ct)
    {
        string? term = search?.Trim();
        if (term?.Length > MaxSearchLength)
            throw ValidationException.For("search", $"La búsqueda admite como máximo {MaxSearchLength} caracteres.");

        var products = await gateway.SearchProductsAsync(string.IsNullOrEmpty(term) ? null : term, MaxResults, ct);
        return products.Where(p => p.AvailableInPos).Select(ToDto).ToList();
    }

    static ProductDto ToDto(CatalogProduct p) =>
        new(p.Id, p.Name, p.Code, p.UnitPrice, p.Taxes.TotalPercent, p.Taxes.PriceIncluded, p.QuantityAvailable,
            p.ImageBase64 is null ? null : $"data:image/png;base64,{p.ImageBase64}");
}

public interface IGetActiveSessionInputPort
{
    Task<PosSessionDto> HandleAsync(CancellationToken ct);
}

public sealed class GetActiveSessionInteractor(IPosGateway gateway) : IGetActiveSessionInputPort
{
    public async Task<PosSessionDto> HandleAsync(CancellationToken ct)
    {
        var session = await gateway.GetActiveSessionAsync(ct);
        if (session is null || !session.IsOpen)
            throw new NotFoundException("No hay una sesión POS abierta en Odoo.");

        return new PosSessionDto(session.Id, session.Name, session.State, session.ConfigId, session.ConfigName,
            session.PaymentMethods.Select(m => new PaymentMethodDto(m.Id, m.Name, m.IsCash)).ToList());
    }
}
