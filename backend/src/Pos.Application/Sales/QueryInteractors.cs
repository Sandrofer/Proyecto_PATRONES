using Pos.Application.Ports;
using Pos.Shared;

namespace Pos.Application.Sales;

public interface IQuoteSaleInputPort
{
    Task<SaleQuoteDto> HandleAsync(SaleRequest request, CancellationToken ct);
}

// Calculates the authoritative totals (Odoo prices, taxes and authorized
// discount) without registering anything.
public sealed class QuoteSaleInteractor(SaleBuilder saleBuilder) : IQuoteSaleInputPort
{
    public async Task<SaleQuoteDto> HandleAsync(SaleRequest request, CancellationToken ct)
    {
        var sale = await saleBuilder.BuildAsync(Guid.NewGuid(), request, ct);
        var totals = sale.CalculateTotals();

        return new SaleQuoteDto(
            totals.Lines.Select(l => new SaleLineQuoteDto(
                l.ProductId, l.ProductName, l.Quantity, l.UnitPrice,
                l.DiscountPercent, l.SubtotalWithoutTax, l.SubtotalWithTax)).ToList(),
            sale.DiscountPercent,
            totals.AmountUntaxed,
            totals.AmountTax,
            totals.AmountTotal);
    }
}

public interface IGetSalesHistoryInputPort
{
    Task<IReadOnlyList<SaleSummaryDto>> HandleAsync(CancellationToken ct);
}

public sealed class GetSalesHistoryInteractor(IPosGateway gateway) : IGetSalesHistoryInputPort
{
    const int HistorySize = 50;

    public Task<IReadOnlyList<SaleSummaryDto>> HandleAsync(CancellationToken ct) =>
        gateway.GetRecentOrdersAsync(HistorySize, ct);
}

public interface IGetSaleDetailInputPort
{
    Task<SaleDetailDto> HandleAsync(int orderId, CancellationToken ct);
}

public sealed class GetSaleDetailInteractor(IPosGateway gateway) : IGetSaleDetailInputPort
{
    public async Task<SaleDetailDto> HandleAsync(int orderId, CancellationToken ct) =>
        await gateway.GetOrderAsync(orderId, ct)
        ?? throw new NotFoundException($"No existe la venta {orderId} en Odoo.");
}
