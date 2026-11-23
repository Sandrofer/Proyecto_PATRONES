namespace Pos.Domain.Discounts;

// Strategy: every discount rule is an interchangeable policy. Adding a new
// rule means adding a class, without touching Sale or the use cases (RF-07).
public interface IDiscountPolicy
{
    string Code { get; }
    string Description { get; }
    bool RequiresPercent { get; }
    decimal? MaxPercent { get; }
    DiscountDecision Evaluate(DiscountContext context);
}

public sealed record DiscountContext(decimal AmountBeforeDiscount, decimal? RequestedPercent);

public sealed record DiscountDecision(bool IsApproved, decimal Percent, string? RejectionReason)
{
    public static DiscountDecision Approve(decimal percent) => new(true, percent, null);
    public static DiscountDecision Reject(string reason) => new(false, 0m, reason);
}

public sealed class NoDiscountPolicy : IDiscountPolicy
{
    public const string PolicyCode = "NONE";
    public string Code => PolicyCode;
    public string Description => "Sin descuento";
    public bool RequiresPercent => false;
    public decimal? MaxPercent => null;

    public DiscountDecision Evaluate(DiscountContext context) => DiscountDecision.Approve(0m);
}

// The operator types a percentage, limited by the authorized maximum.
public sealed class ManualPercentageDiscountPolicy(decimal maxPercent) : IDiscountPolicy
{
    public const string PolicyCode = "MANUAL";
    public string Code => PolicyCode;
    public string Description => $"Descuento manual (máximo {maxPercent:0.##} %)";
    public bool RequiresPercent => true;
    public decimal? MaxPercent => maxPercent;

    public DiscountDecision Evaluate(DiscountContext context)
    {
        if (context.RequestedPercent is not { } percent)
            return DiscountDecision.Reject("Indique el porcentaje del descuento manual.");
        if (percent <= 0)
            return DiscountDecision.Reject("El porcentaje de descuento debe ser mayor que cero.");
        if (percent > maxPercent)
            return DiscountDecision.Reject(
                $"El descuento de {percent:0.##} % no está autorizado: el máximo permitido es {maxPercent:0.##} %.");

        return DiscountDecision.Approve(percent);
    }
}

// Fixed discount for students (the operator checks the student ID card).
public sealed class StudentDiscountPolicy(decimal percent) : IDiscountPolicy
{
    public const string PolicyCode = "STUDENT";
    public string Code => PolicyCode;
    public string Description => $"Estudiante ({percent:0.##} %)";
    public bool RequiresPercent => false;
    public decimal? MaxPercent => percent;

    public DiscountDecision Evaluate(DiscountContext context) => DiscountDecision.Approve(percent);
}

// Automatic discount when the purchase reaches a minimum amount.
public sealed class VolumeDiscountPolicy(decimal minimumAmount, decimal percent) : IDiscountPolicy
{
    public const string PolicyCode = "VOLUME";
    public string Code => PolicyCode;
    public string Description => $"{percent:0.##} % por compras desde {minimumAmount:0.00}";
    public bool RequiresPercent => false;
    public decimal? MaxPercent => percent;

    public DiscountDecision Evaluate(DiscountContext context) =>
        context.AmountBeforeDiscount >= minimumAmount
            ? DiscountDecision.Approve(percent)
            : DiscountDecision.Reject(
                $"El descuento por volumen requiere una compra mínima de {minimumAmount:0.00} " +
                $"(total actual {context.AmountBeforeDiscount:0.00}).");
}
