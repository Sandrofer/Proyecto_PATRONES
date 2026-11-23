namespace Pos.Frontend.Domain;

// Same rule as the API's CashTender (RF-11, cash payment): the browser uses it
// to show the change and warn at once; the API checks it again.
public static class CashTender
{
    public const decimal MaxChange = 100m;

    // Bills a customer usually hands over (Ecuador, USD).
    static readonly decimal[] Bills = [1m, 5m, 10m, 20m, 50m, 100m];

    // tendered null = exact amount. Returns an error message or null.
    public static string? Validate(decimal amountDue, decimal? tendered)
    {
        if (tendered is not decimal amount)
            return null;
        if (amount <= 0)
            return "El efectivo recibido debe ser mayor que cero.";
        if (decimal.Round(amount, 2) != amount)
            return "El efectivo recibido admite como máximo 2 decimales (centavos).";
        if (amount < amountDue)
            return $"El efectivo recibido ($ {amount:N2}) no cubre el total ($ {amountDue:N2}).";
        if (amount - amountDue > MaxChange)
            return $"El vuelto no puede superar $ {MaxChange:N2}: revise el efectivo recibido.";
        return null;
    }

    public static decimal Change(decimal amountDue, decimal? tendered) =>
        tendered is decimal amount && amount > amountDue ? amount - amountDue : 0m;

    // Up to three bills that cover the total, for one-click entry.
    public static IReadOnlyList<decimal> SuggestedBills(decimal amountDue) =>
        Bills.Where(b => b > amountDue && b - amountDue <= MaxChange).Take(3).ToList();
}
