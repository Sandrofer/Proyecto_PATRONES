namespace Pos.Application;

// Invalid input data (400). Errors are grouped by field.
public sealed class ValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Los datos enviados no son válidos.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationException For(string field, string message) =>
        new(new Dictionary<string, string[]> { [field] = [message] });
}

public sealed class NotFoundException(string message) : Exception(message);

public sealed class AuthenticationFailedException() : Exception("Usuario o contraseña incorrectos.");

// Another request with the same Idempotency-Key is still running (409).
public sealed class OperationInProgressException()
    : Exception("Esta venta ya se está procesando. Espere unos segundos y consulte el resultado.");

// Odoo could not be reached or did not answer in time. For writes the
// outcome is unknown: callers must not assume success nor failure.
public sealed class ExternalServiceUnavailableException(string message, Exception? inner = null)
    : Exception(message, inner);

// Odoo answered and rejected the operation (nothing was registered).
public sealed class ExternalServiceRejectedException(string message, Exception? inner = null)
    : Exception(message, inner);
