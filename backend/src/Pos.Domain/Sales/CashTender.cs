namespace Pos.Domain.Sales;

// Cash handed over by the customer and the change returned (RF-11, cash
// payment). Value object: it can only exist with valid amounts.
public sealed record CashTender
{
    // A counter sale never returns more than one $100 bill of change; a bigger
    // amount is almost always a typing mistake (e.g. 500 instead of 5.00).
    public const decimal MaxChange = 100m;

    public decimal AmountDue { get; }
    public decimal AmountTendered { get; }
    public decimal Change => AmountTendered - AmountDue;

    CashTender(decimal due, decimal tendered)
    {
        AmountDue = due;
        AmountTendered = tendered;
    }

    // tendered null = the customer paid the exact amount.
    public static CashTender Create(decimal amountDue, decimal? tendered)
    {
        if (amountDue < 0)
            throw new DomainException("El total a cobrar no puede ser negativo.");
        if (tendered is not decimal amount)
            return new CashTender(amountDue, amountDue);
        if (amount <= 0)
            throw new DomainException("El efectivo recibido debe ser mayor que cero.");
        if (decimal.Round(amount, 2) != amount)
            throw new DomainException("El efectivo recibido admite como máximo 2 decimales (centavos).");
        if (amount < amountDue)
            throw new DomainException($"El efectivo recibido ($ {amount:N2}) no cubre el total ($ {amountDue:N2}).");
        if (amount - amountDue > MaxChange)
            throw new DomainException($"El vuelto no puede superar $ {MaxChange:N2}: revise el efectivo recibido.");
        return new CashTender(amountDue, amount);
    }
}
