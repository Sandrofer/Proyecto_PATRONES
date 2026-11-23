using Pos.Domain.Catalog;
using Pos.Domain.Discounts;

namespace Pos.Domain.Sales;

public enum SaleStatus
{
    InPreparation,
    Confirmed
}

// Aggregate root of a POS sale. It protects the business rules RN-01, RN-02,
// RN-04, RN-05 and RN-06; nothing outside can change its lines directly.
public sealed class Sale
{
    readonly List<SaleLine> _lines = [];

    public Sale(Guid reference, TaxRoundingMode taxRounding = TaxRoundingMode.PerLine)
    {
        if (reference == Guid.Empty)
            throw new DomainException("La venta necesita una referencia única.");
        Reference = reference;
        TaxRounding = taxRounding;
    }

    // Same rounding as the Odoo company, so our total equals Odoo's to the cent.
    public TaxRoundingMode TaxRounding { get; }

    // Unique reference of the operation; it is also the order uuid in Odoo,
    // which lets us detect repeated requests (RN-08).
    public Guid Reference { get; }
    public SaleStatus Status { get; private set; } = SaleStatus.InPreparation;
    public decimal DiscountPercent { get; private set; }
    public string? DiscountPolicyCode { get; private set; }
    public IReadOnlyList<SaleLine> Lines => _lines;

    public void AddProduct(CatalogProduct product, decimal quantity)
    {
        EnsureInPreparation();
        ArgumentNullException.ThrowIfNull(product);

        if (!product.AvailableInPos)
            throw new DomainException($"El producto '{product.Name}' no está habilitado para la venta en el POS.");
        if (quantity <= 0)
            throw new DomainException($"La cantidad de '{product.Name}' debe ser mayor que cero.");

        var existing = _lines.FirstOrDefault(l => l.Product.Id == product.Id);
        if (existing is not null)
        {
            _lines.Remove(existing);
            quantity += existing.Quantity;
        }

        _lines.Add(new SaleLine(product, quantity));
    }

    // RN-04: the discount is decided by a policy (Strategy); the sale only
    // accepts approved decisions.
    public void ApplyDiscount(IDiscountPolicy policy, decimal? requestedPercent)
    {
        EnsureInPreparation();
        ArgumentNullException.ThrowIfNull(policy);

        var decision = policy.Evaluate(new DiscountContext(CalculateTotals(0m).AmountTotal, requestedPercent));
        if (!decision.IsApproved)
            throw new DomainException(decision.RejectionReason!);

        DiscountPercent = decision.Percent;
        DiscountPolicyCode = policy.Code;
    }

    // RN-01 and RN-05: an empty or inconsistent sale cannot be confirmed.
    public void Confirm()
    {
        EnsureInPreparation();
        if (_lines.Count == 0)
            throw new DomainException("La venta debe contener al menos un producto.");
        if (CalculateTotals().AmountTotal < 0)
            throw new DomainException("El total de la venta no puede ser negativo.");

        Status = SaleStatus.Confirmed;
    }

    public SaleTotals CalculateTotals() => CalculateTotals(DiscountPercent);

    SaleTotals CalculateTotals(decimal discountPercent)
    {
        var lines = _lines.Select(l => l.Calculate(discountPercent)).ToList();
        decimal untaxed = lines.Sum(l => l.SubtotalWithoutTax);
        decimal tax = TaxRounding == TaxRoundingMode.Global
            ? Money.Round(lines.Sum(l => l.ExactTax))
            : lines.Sum(l => Money.Round(l.ExactTax));

        return new SaleTotals(lines, untaxed, tax, untaxed + tax);
    }

    // RN-06: a confirmed sale is no longer edited as a sale in preparation.
    void EnsureInPreparation()
    {
        if (Status != SaleStatus.InPreparation)
            throw new DomainException("La venta ya fue confirmada y no puede modificarse.");
    }
}

public sealed record SaleTotals(
    IReadOnlyList<SaleLineAmounts> Lines,
    decimal AmountUntaxed,
    decimal AmountTax,
    decimal AmountTotal);
