using Pos.Frontend.Domain;
using Pos.Shared;

namespace Pos.Frontend.Application;

public sealed class LoginUseCase(IPosApi api, IAuthSession session)
{
    public async Task<string?> ExecuteAsync(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            return "Ingrese usuario y contraseña.";
        if (InputRules.ValidateUsername(username) is string usernameError)
            return usernameError;

        var result = await api.LoginAsync(new LoginRequest(username.Trim(), password));
        if (!result.IsSuccess)
            return result.Error;

        await session.SignInAsync(result.Value!);
        return null;
    }
}

public static class ProductMapping
{
    public static CartProduct ToCartProduct(this ProductDto p) =>
        new(p.Id, p.Name, p.Code, p.UnitPrice, p.TaxPercent, p.TaxIncludedInPrice, p.QuantityAvailable);
}

public sealed record CheckoutResult(bool Success, bool Uncertain, string Message, SaleRegistrationDto? Registration);

// Coordinates the sale in preparation: the cart, the discount and the
// confirmation. It keeps the same Idempotency-Key until Odoo gives a
// definitive answer, so retrying never registers a second sale.
public sealed class CheckoutUseCase(IPosApi api)
{
    public Cart Cart { get; } = new();
    public string DiscountPolicyCode { get; set; } = "NONE";
    public decimal? DiscountPercent { get; set; }
    // Cash handed over by the customer; null = exact amount.
    public decimal? AmountTendered { get; set; }

    // Total to collect with the discount the cashier selected (if valid).
    public decimal AmountDue => Cart.CalculateTotals(ProvisionalDiscountPercent).Total;
    public decimal Change => CashTender.Change(AmountDue, AmountTendered);
    public string? ValidateCash() => Cart.IsEmpty ? null : CashTender.Validate(AmountDue, AmountTendered);
    public Guid? PendingOperation { get; private set; }

    // Policies published by the API, with their conditions (minimum amount,
    // identified customer, maximum percent).
    public IReadOnlyList<DiscountPolicyDto> Policies { get; set; } = [];
    public DiscountPolicyDto? SelectedPolicy =>
        Policies.FirstOrDefault(p => string.Equals(p.Code, DiscountPolicyCode, StringComparison.OrdinalIgnoreCase));

    // Customer: CustomerRequired = "Con cédula" selected; otherwise consumidor final.
    public bool CustomerRequired { get; set; }
    public string CustomerIdentification { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public bool HasCustomer => !string.IsNullOrWhiteSpace(CustomerIdentification) || !string.IsNullOrWhiteSpace(CustomerName);
    bool HasValidCustomer => (CustomerRequired || HasCustomer) && ValidateCustomer() is null;

    // Returns an error message or null when the customer data is acceptable.
    public string? ValidateCustomer()
    {
        if (!CustomerRequired && !HasCustomer)
            return null;
        if (string.IsNullOrWhiteSpace(CustomerIdentification))
            return "Ingrese la cédula del cliente o elija «Consumidor final».";
        if (!Cedula.IsValid(CustomerIdentification.Trim()))
            return "La cédula no es válida (10 dígitos, provincia y dígito verificador).";
        return InputRules.ValidatePersonName(CustomerName);
    }

    // Same conditions the API applies (RN-04, RF-08), checked before calling it
    // so the cashier sees the reason at once. The API checks them again.
    public string? ValidateDiscount()
    {
        if (string.Equals(DiscountPolicyCode, "NONE", StringComparison.OrdinalIgnoreCase))
            return null;
        var policy = SelectedPolicy;
        if (policy is null)
            return "La política de descuento seleccionada no está disponible.";

        if (policy.RequiresPercent)
        {
            if (DiscountPercent is not decimal percent)
                return "Indique el porcentaje del descuento.";
            if (percent <= 0)
                return "El porcentaje de descuento debe ser mayor que cero.";
            if (!InputRules.HasValidDecimals(percent))
                return "El porcentaje de descuento admite como máximo 2 decimales.";
            if (policy.MaxPercent is decimal max && percent > max)
                return $"El descuento máximo autorizado es {max:0.##} %.";
        }

        decimal amount = Cart.CalculateTotals().Total;
        if (policy.MinimumAmount is decimal minimum && amount < minimum)
            return $"Este descuento requiere una compra mínima de $ {minimum:N2} (total actual $ {amount:N2}).";

        if (policy.RequiresIdentifiedCustomer && !HasValidCustomer)
            return "Este descuento requiere identificar al cliente con su cédula (elija «Con cédula»).";

        return null;
    }

    // Percent used for the provisional total: only when the discount is valid.
    public decimal ProvisionalDiscountPercent =>
        Cart.IsEmpty || ValidateDiscount() is not null || SelectedPolicy is not { } p ? 0m
        : p.RequiresPercent ? DiscountPercent ?? 0m
        : p.MaxPercent ?? 0m;

    // First reason why the sale cannot be sent yet, or null.
    public string? ValidateBeforeSending(int paymentMethodId) =>
        Cart.IsEmpty ? "Agregue al menos un producto."
        : paymentMethodId <= 0 ? "Seleccione un método de pago."
        : ValidateCustomer() ?? ValidateDiscount() ?? ValidateCash();

    public SaleRequest BuildRequest(int paymentMethodId) => new(
        Cart.Lines.Select(l => new SaleLineRequest(l.Product.Id, l.Quantity)).ToList(),
        DiscountPolicyCode,
        DiscountPercent,
        paymentMethodId,
        HasCustomer ? new SaleCustomerRequest(CustomerIdentification.Trim(), CustomerName.Trim()) : null,
        AmountTendered);

    public async Task<ApiResult<SaleQuoteDto>> QuoteAsync(int paymentMethodId)
    {
        if (ValidateBeforeSending(paymentMethodId) is string error)
            return ApiResult<SaleQuoteDto>.Fail(error);
        return await api.QuoteAsync(BuildRequest(paymentMethodId));
    }

    public async Task<CheckoutResult> ConfirmAsync(int paymentMethodId)
    {
        // A pending operation is retried exactly as it was sent.
        if (PendingOperation is null && ValidateBeforeSending(paymentMethodId) is string error)
            return new CheckoutResult(false, false, error, null);

        PendingOperation ??= Guid.NewGuid();
        var result = await api.RegisterSaleAsync(PendingOperation.Value, BuildRequest(paymentMethodId));

        if (!result.IsSuccess)
        {
            // No answer or a server error: we cannot know if Odoo registered
            // it, so the key is kept and a retry cannot duplicate the sale.
            if (result.IsCommunicationFailure || result.StatusCode >= 500)
                return new CheckoutResult(false, true,
                    $"{result.Error} No se sabe con certeza si la venta se registró. Pulse «Reintentar»: no se duplicará.", null);

            // Definitive rejection (4xx): the next attempt is a new operation.
            PendingOperation = null;
            return new CheckoutResult(false, false, result.FullError, null);
        }

        var registration = result.Value!;
        if (registration.Outcome == SaleRegistrationOutcome.Uncertain)
            return new CheckoutResult(false, true, registration.Message, registration);

        PendingOperation = null;
        Cart.Clear();
        DiscountPolicyCode = "NONE";
        DiscountPercent = null;
        AmountTendered = null;
        CustomerIdentification = "";
        CustomerName = "";
        CustomerRequired = false;
        return new CheckoutResult(true, false, registration.Message, registration);
    }

    public void Reset()
    {
        PendingOperation = null;
        Cart.Clear();
        DiscountPolicyCode = "NONE";
        DiscountPercent = null;
        AmountTendered = null;
        CustomerIdentification = "";
        CustomerName = "";
        CustomerRequired = false;
    }
}
