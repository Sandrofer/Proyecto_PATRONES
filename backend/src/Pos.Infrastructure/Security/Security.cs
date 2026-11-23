using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pos.Application.Ports;
using Pos.Shared;

namespace Pos.Infrastructure.Security;

public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public string Issuer { get; set; } = "pos-odoo-api";
    public string Audience { get; set; } = "pos-odoo-frontend";
    // Secret (>= 32 chars): user-secrets or environment variables.
    public string SigningKey { get; set; } = "";
    public int ExpirationMinutes { get; set; } = 480;
}

// Issues the token the browser uses for the whole shift. It carries the
// cashier's Odoo user id so each sale is registered in their name.
public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : ITokenIssuer
{
    public const string OdooUserIdClaim = "odoo_uid";
    public const string DisplayNameClaim = "display_name";

    public LoginResponse Issue(Operator op)
    {
        var jwt = options.Value;
        DateTime expires = clock.GetUtcNow().UtcDateTime.AddMinutes(jwt.ExpirationMinutes);

        var token = new JwtSecurityToken(
            jwt.Issuer,
            jwt.Audience,
            [
                new Claim(ClaimTypes.Name, op.Username),
                new Claim(DisplayNameClaim, op.DisplayName),
                new Claim(OdooUserIdClaim, op.OdooUserId.ToString())
            ],
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey(jwt), SecurityAlgorithms.HmacSha256));

        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, op.DisplayName);
    }

    public static SymmetricSecurityKey SigningKey(JwtOptions jwt) => new(Encoding.UTF8.GetBytes(jwt.SigningKey));
}

public sealed class HttpContextCurrentOperator(IHttpContextAccessor accessor) : ICurrentOperator
{
    ClaimsPrincipal? User => accessor.HttpContext?.User;

    public string Name => User?.FindFirstValue(JwtTokenIssuer.DisplayNameClaim)
        ?? User?.Identity?.Name
        ?? "desconocido";

    public int? OdooUserId =>
        int.TryParse(User?.FindFirstValue(JwtTokenIssuer.OdooUserIdClaim), out int id) ? id : null;
}
