namespace Pos.Frontend.Web;

// Presentation helpers (formatting only, no business rules).
public static class Ui
{
    public static string Money(decimal value) => "$ " + value.ToString("N2");
    public static string Money(decimal? value) => value is null ? "—" : Money(value.Value);
    public static string Qty(decimal value) => value.ToString("0.##");

    public static string StatusLabel(string? state) => state switch
    {
        "paid" => "Pagada",
        "done" => "Cerrada",
        "invoiced" => "Facturada",
        "draft" => "Borrador",
        "cancel" => "Anulada",
        _ => state ?? "—"
    };

    public static string StatusClass(string? state) => state switch
    {
        "paid" or "done" or "invoiced" => "ok",
        "cancel" => "error",
        _ => "neutral"
    };

    public static string Initials(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => char.ToUpperInvariant(w[0])));
}
