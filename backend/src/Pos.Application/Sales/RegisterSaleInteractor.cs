using Pos.Application.Ports;
using Pos.Domain;
using Pos.Shared;

namespace Pos.Application.Sales;

public interface IRegisterSaleInputPort
{
    Task<SaleRegistrationDto> HandleAsync(Guid idempotencyKey, SaleRequest request, CancellationToken ct);
}

// Registers a sale with its cash payment in Odoo.
// Guarantees: no success is reported without Odoo's confirmation (RN-07)
// and a repeated request never creates a second order (RN-08).
public sealed class RegisterSaleInteractor(
    IPosGateway gateway,
    SaleBuilder saleBuilder,
    IIdempotencyStore idempotencyStore,
    ICurrentOperator currentOperator) : IRegisterSaleInputPort
{
    public async Task<SaleRegistrationDto> HandleAsync(Guid idempotencyKey, SaleRequest request, CancellationToken ct)
    {
        if (idempotencyKey == Guid.Empty)
            throw ValidationException.For(ApiRoutes.IdempotencyHeader,
                "Falta el identificador único de la operación (Idempotency-Key).");

        var claim = idempotencyStore.Claim(idempotencyKey);
        if (claim.Status == IdempotencyClaimStatus.Completed)
            return claim.CompletedResult! with { Outcome = SaleRegistrationOutcome.AlreadyRegistered };
        if (claim.Status == IdempotencyClaimStatus.InProgress)
            throw new OperationInProgressException();

        try
        {
            var result = await RegisterAsync(idempotencyKey, request, ct);
            if (result.Outcome == SaleRegistrationOutcome.Uncertain)
                idempotencyStore.Release(idempotencyKey);
            else
                idempotencyStore.Complete(idempotencyKey, result);
            return result;
        }
        catch
        {
            idempotencyStore.Release(idempotencyKey);
            throw;
        }
    }

    async Task<SaleRegistrationDto> RegisterAsync(Guid reference, SaleRequest request, CancellationToken ct)
    {
        // A previous attempt (maybe uncertain, maybe before a restart) could
        // already have created the order: Odoo is the source of truth.
        var existing = await gateway.FindOrderByReferenceAsync(reference, ct);
        if (existing is not null)
            return Registered(existing, SaleRegistrationOutcome.AlreadyRegistered,
                "La venta ya estaba registrada en Odoo; no se creó otra.");

        var session = await gateway.GetActiveSessionAsync(ct);
        if (session is null || !session.IsOpen)
            throw ValidationException.For("session",
                "No hay una sesión POS abierta en Odoo. Abra una sesión antes de vender.");

        var paymentMethod = session.PaymentMethods.FirstOrDefault(m => m.Id == request.PaymentMethodId);
        if (paymentMethod is null)
            throw ValidationException.For("paymentMethodId",
                "El método de pago no está configurado en la sesión POS abierta.");
        if (!paymentMethod.IsCash)
            throw ValidationException.For("paymentMethodId",
                "Solo se admite el pago en efectivo en esta versión del POS.");

        var sale = await saleBuilder.BuildAsync(reference, request, ct);
        try
        {
            sale.Confirm();
        }
        catch (DomainException ex)
        {
            throw ValidationException.For("sale", ex.Message);
        }

        var submission = new OrderSubmission(sale, sale.CalculateTotals(), session, paymentMethod,
            currentOperator.Name, currentOperator.OdooUserId);

        try
        {
            var order = await gateway.SubmitPaidOrderAsync(submission, ct);
            return Registered(order, SaleRegistrationOutcome.Registered, "Venta y pago registrados en Odoo.");
        }
        catch (ExternalServiceUnavailableException)
        {
            return await VerifyAfterCommunicationFailureAsync(reference, ct);
        }
    }

    // The request may or may not have reached Odoo. Ask Odoo once; if it still
    // cannot answer, report an uncertain result instead of success (RN-07).
    async Task<SaleRegistrationDto> VerifyAfterCommunicationFailureAsync(Guid reference, CancellationToken ct)
    {
        try
        {
            var order = await gateway.FindOrderByReferenceAsync(reference, ct);
            if (order is not null)
                return Registered(order, SaleRegistrationOutcome.Registered,
                    "Venta y pago registrados en Odoo (confirmado después de un fallo de comunicación).");
        }
        catch (ExternalServiceUnavailableException)
        {
            // Odoo still unreachable: fall through to the uncertain result.
        }

        return new SaleRegistrationDto(
            SaleRegistrationOutcome.Uncertain,
            "No se pudo confirmar el registro en Odoo. NO cobre de nuevo: reintente la consulta con la misma operación.",
            null, null, null, null);
    }

    static SaleRegistrationDto Registered(RegisteredOrder order, SaleRegistrationOutcome outcome, string message) =>
        new(outcome, message, order.Id, order.Name, order.State, order.AmountTotal);
}
