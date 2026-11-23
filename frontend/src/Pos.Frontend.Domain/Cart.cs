namespace Pos.Frontend.Domain;

// Product data the cart needs. Prices are the ones Odoo reported through the API.
// QuantityAvailable: stock reported by Odoo; null = no stock control.
public sealed record CartProduct(int Id, string Name, string? Code, decimal UnitPrice, decimal TaxPercent, bool TaxIncluded,
    decimal? QuantityAvailable = null)
{
    public bool IsOutOfStock => QuantityAvailable is <= 0;
}

public sealed record CartLine(CartProduct Product, decimal Quantity)
{
    public decimal Subtotal => Taxes(Product, Product.UnitPrice * Quantity).Total;

    internal static (decimal Untaxed, decimal Tax, decimal Total) Taxes(CartProduct product, decimal amount)
    {
        decimal untaxed = product.TaxIncluded ? amount / (1 + product.TaxPercent / 100m) : amount;
        decimal tax = Math.Round(untaxed * product.TaxPercent / 100m, 2, MidpointRounding.AwayFromZero);
        untaxed = Math.Round(untaxed, 2, MidpointRounding.AwayFromZero);
        return (untaxed, tax, untaxed + tax);
    }
}

public sealed record CartTotals(decimal Untaxed, decimal Tax, decimal Total);

// Cart of a sale in preparation. Pure C#: no Blazor, no HTTP (RNF-02).
// The total is provisional; the API recalculates it with Odoo's data.
public sealed class Cart
{
    readonly List<CartLine> _lines = [];

    public IReadOnlyList<CartLine> Lines => _lines;
    public bool IsEmpty => _lines.Count == 0;

    public event Action? Changed;

    public void Add(CartProduct product, decimal quantity = 1)
    {
        EnsureValidQuantity(quantity);
        int index = _lines.FindIndex(l => l.Product.Id == product.Id);
        EnsureStock(product, quantity + (index >= 0 ? _lines[index].Quantity : 0));
        if (index >= 0)
            _lines[index] = _lines[index] with { Quantity = _lines[index].Quantity + quantity };
        else
            _lines.Add(new CartLine(product, quantity));
        Changed?.Invoke();
    }

    public void SetQuantity(int productId, decimal quantity)
    {
        EnsureValidQuantity(quantity);
        int index = _lines.FindIndex(l => l.Product.Id == productId);
        if (index < 0)
            throw new InvalidOperationException("El producto no está en el carrito.");
        EnsureStock(_lines[index].Product, quantity);
        _lines[index] = _lines[index] with { Quantity = quantity };
        Changed?.Invoke();
    }

    public void Remove(int productId)
    {
        if (_lines.RemoveAll(l => l.Product.Id == productId) > 0)
            Changed?.Invoke();
    }

    public void Clear()
    {
        _lines.Clear();
        Changed?.Invoke();
    }

    public CartTotals CalculateTotals(decimal discountPercent = 0m)
    {
        decimal untaxed = 0, tax = 0;
        foreach (var line in _lines)
        {
            var t = CartLine.Taxes(line.Product, line.Product.UnitPrice * (1 - discountPercent / 100m) * line.Quantity);
            untaxed += t.Untaxed;
            tax += t.Tax;
        }
        return new CartTotals(untaxed, tax, untaxed + tax);
    }

    public static decimal? RemainingStock(CartProduct product, decimal inCart) =>
        product.QuantityAvailable is decimal available ? available - inCart : null;

    // Same rule as the API (RN-09); the API checks it again with fresh stock.
    static void EnsureStock(CartProduct product, decimal quantity)
    {
        if (product.QuantityAvailable is not decimal available || quantity <= available)
            return;
        throw new InvalidOperationException(available <= 0
            ? $"'{product.Name}' está agotado."
            : $"Solo hay {available:0.##} unidades de '{product.Name}'.");
    }

    // Products are sold by unit (RN-11): whole numbers greater than zero.
    static void EnsureValidQuantity(decimal quantity)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(null, "La cantidad debe ser mayor que cero.");
        if (quantity != decimal.Truncate(quantity))
            throw new ArgumentOutOfRangeException(null, "La cantidad debe ser un número entero.");
    }
}
