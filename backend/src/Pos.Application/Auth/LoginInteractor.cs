using Pos.Application.Ports;
using Pos.Shared;

namespace Pos.Application.Auth;

public interface ILoginInputPort
{
    LoginResponse Handle(LoginRequest request);
}

public sealed class LoginInteractor(IOperatorDirectory operators, ITokenIssuer tokens) : ILoginInputPort
{
    public LoginResponse Handle(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request?.Username) || string.IsNullOrWhiteSpace(request.Password))
            throw ValidationException.For("credentials", "Ingrese usuario y contraseña.");

        var op = operators.ValidateCredentials(request.Username.Trim(), request.Password)
            ?? throw new AuthenticationFailedException();

        return tokens.Issue(op);
    }
}
