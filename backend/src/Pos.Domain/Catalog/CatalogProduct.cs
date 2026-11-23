namespace Pos.Domain.Catalog;

// Snapshot of a product as Odoo reports it. Prices always come from Odoo,
// never from the browser (RN-03).
public sealed record CatalogProduct(
    int Id,
    string Name,
    string? Code,
    decimal UnitPrice,
    TaxSet Taxes,
    bool AvailableInPos,
    decimal? QuantityAvailable,
    string? ImageBase64 = null);

// Percentage tax as configured in Odoo (account.tax with amount_type = percent).
public sealed record TaxRule(int Id, decimal Percent, bool PriceIncluded);

// Untaxed is rounded to the currency; ExactTax is not rounded yet, because
// the rounding depends on the company setting (see TaxRoundingMode).
public sealed record TaxBreakdown(decimal Untaxed, decimal ExactTax)
{
    public decimal RoundedTax => Money.Round(ExactTax);
    public decimal Total => Untaxed + RoundedTax;
}

// Odoo company setting "tax_calculation_rounding_method".
public enum TaxRoundingMode
{
    // round_per_line: each line's tax is rounded, then summed.
    PerLine,
    // round_globally: taxes are summed unrounded and rounded once.
    Global
}

public static class Money
{
    public static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}

public sealed class TaxSet
{
    public static readonly TaxSet None = new([]);

    public TaxSet(IEnumerable<TaxRule> rules) => Rules = rules.ToList();

    public IReadOnlyList<TaxRule> Rules { get; }

    public decimal TotalPercent => Rules.Sum(r => r.Percent);
    public bool PriceIncluded => Rules.Any(r => r.PriceIncluded);

    // Applies the taxes to an amount (unit price x quantity, already discounted).
    public TaxBreakdown Apply(decimal amount)
    {
        decimal includedPercent = Rules.Where(r => r.PriceIncluded).Sum(r => r.Percent);
        decimal excludedPercent = Rules.Where(r => !r.PriceIncluded).Sum(r => r.Percent);

        decimal untaxed = Money.Round(amount / (1 + includedPercent / 100m));
        decimal exactTax = untaxed * (includedPercent + excludedPercent) / 100m;
        return new TaxBreakdown(untaxed, exactTax);
    }
}
