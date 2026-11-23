using Pos.Application.Ports;
using Pos.Shared;

namespace Pos.Application.Auth;

public interface ILoginInputPort
{
    Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct);
}

// Cashiers log in with their Odoo user. The use case does not know that the
// directory is Odoo: it only depends on IOperatorDirectory (DIP).
public sealed class LoginInteractor(IOperatorDirectory operators, ITokenIssuer tokens) : ILoginInputPort
{
    public async Task<LoginResponse> HandleAsync(LoginRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request?.Username) || string.IsNullOrWhiteSpace(request.Password))
            throw ValidationException.For("credentials", "Ingrese usuario y contraseña.");
        if (InputRules.ValidateUsername(request.Username) is string error)
            throw ValidationException.For("username", error);
        if (request.Password.Length > 100)
            throw ValidationException.For("password", "La contraseña es demasiado larga.");

        var op = await operators.ValidateCredentialsAsync(request.Username.Trim(), request.Password, ct)
            ?? throw new AuthenticationFailedException();

        return tokens.Issue(op);
    }
}
