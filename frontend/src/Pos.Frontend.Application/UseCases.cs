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
    public Guid? PendingOperation { get; private set; }

    // Customer: both empty = consumidor final.
    public string CustomerIdentification { get; set; } = "";
    public string CustomerName { get; set; } = "";
    public bool HasCustomer => !string.IsNullOrWhiteSpace(CustomerIdentification) || !string.IsNullOrWhiteSpace(CustomerName);

    // Returns an error message or null when the customer data is acceptable.
    public string? ValidateCustomer()
    {
        if (!HasCustomer)
            return null;
        if (!Cedula.IsValid(CustomerIdentification.Trim()))
            return "La cédula no es válida (10 dígitos, provincia y dígito verificador).";
        if (CustomerName.Trim().Length < 3)
            return "Ingrese el nombre del cliente (mínimo 3 letras).";
        return null;
    }

    public SaleRequest BuildRequest(int paymentMethodId) => new(
        Cart.Lines.Select(l => new SaleLineRequest(l.Product.Id, l.Quantity)).ToList(),
        DiscountPolicyCode,
        DiscountPercent,
        paymentMethodId,
        HasCustomer ? new SaleCustomerRequest(CustomerIdentification.Trim(), CustomerName.Trim()) : null);

    public async Task<ApiResult<SaleQuoteDto>> QuoteAsync(int paymentMethodId)
    {
        if (Cart.IsEmpty)
            return ApiResult<SaleQuoteDto>.Fail("Agregue al menos un producto.");
        return await api.QuoteAsync(BuildRequest(paymentMethodId));
    }

    public async Task<CheckoutResult> ConfirmAsync(int paymentMethodId)
    {
        if (Cart.IsEmpty)
            return new CheckoutResult(false, false, "Agregue al menos un producto antes de confirmar.", null);
        if (paymentMethodId <= 0)
            return new CheckoutResult(false, false, "Seleccione un método de pago.", null);
        if (ValidateCustomer() is string customerError)
            return new CheckoutResult(false, false, customerError, null);

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
        CustomerIdentification = "";
        CustomerName = "";
        return new CheckoutResult(true, false, registration.Message, registration);
    }

    public void Reset()
    {
        PendingOperation = null;
        Cart.Clear();
        DiscountPolicyCode = "NONE";
        DiscountPercent = null;
        CustomerIdentification = "";
        CustomerName = "";
    }
}
