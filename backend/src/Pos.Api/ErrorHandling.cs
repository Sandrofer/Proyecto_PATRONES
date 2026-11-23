using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Pos.Application;

namespace Pos.Api;

// Translates Application exceptions into consistent ProblemDetails responses
// (RNF-08). Controllers/endpoints never build error responses by hand.
public sealed class ProblemDetailsExceptionHandler(IProblemDetailsService problemDetails, ILogger<ProblemDetailsExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Datos no válidos"),
            AuthenticationFailedException => (StatusCodes.Status401Unauthorized, "Acceso denegado"),
            OperatorNotAuthorizedException => (StatusCodes.Status403Forbidden, "Sin permiso de Punto de venta"),
            NotFoundException => (StatusCodes.Status404NotFound, "No encontrado"),
            OperationInProgressException => (StatusCodes.Status409Conflict, "Operación en curso"),
            ExternalServiceRejectedException => (StatusCodes.Status502BadGateway, "Odoo rechazó la operación"),
            ExternalServiceUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Odoo no disponible"),
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Solicitud mal formada"),
            _ => (StatusCodes.Status500InternalServerError, "Error inesperado")
        };

        if (status == StatusCodes.Status500InternalServerError)
            logger.LogError(exception, "Unhandled error");

        var details = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status500InternalServerError
                ? "Ocurrió un error inesperado en el servidor."
                : exception.Message
        };
        if (exception is ValidationException validation)
            details.Extensions["errors"] = validation.Errors;

        context.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = details,
            Exception = exception
        });
    }
}
