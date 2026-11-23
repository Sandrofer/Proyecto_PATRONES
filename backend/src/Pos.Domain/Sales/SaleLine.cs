using Pos.Domain.Catalog;

namespace Pos.Domain.Sales;

public sealed class SaleLine
{
    internal SaleLine(CatalogProduct product, decimal quantity)
    {
        Product = product;
        Quantity = quantity;
    }

    public CatalogProduct Product { get; }
    public decimal Quantity { get; }

    // Same rule Odoo applies to a POS line: the discount is a percentage of the
    // unit price and taxes are computed per line, rounded to the currency.
    internal SaleLineAmounts Calculate(decimal discountPercent)
    {
        decimal discountedAmount = Product.UnitPrice * (1 - discountPercent / 100m) * Quantity;
        TaxBreakdown taxes = Product.Taxes.Apply(discountedAmount);

        return new SaleLineAmounts(
            Product.Id,
            Product.Name,
            Quantity,
            Product.UnitPrice,
            discountPercent,
            taxes.Untaxed,
            taxes.ExactTax,
            taxes.Total);
    }
}

public sealed record SaleLineAmounts(
    int ProductId,
    string ProductName,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountPercent,
    decimal SubtotalWithoutTax,
    decimal ExactTax,
    decimal SubtotalWithTax);
