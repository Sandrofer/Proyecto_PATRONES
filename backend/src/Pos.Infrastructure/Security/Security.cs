using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
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

public sealed class OperatorAccount
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    // Format: PBKDF2$<iterations>$<salt base64>$<hash base64> (see PasswordHasher).
    public string PasswordHash { get; set; } = "";
}

public sealed class OperatorsOptions
{
    public const string Section = "Operators";
    public List<OperatorAccount> Accounts { get; set; } = [];
}

public static class PasswordHasher
{
    const int Iterations = 100_000;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"PBKDF2${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2" || !int.TryParse(parts[1], out int iterations))
            return false;

        byte[] salt = Convert.FromBase64String(parts[2]);
        byte[] expected = Convert.FromBase64String(parts[3]);
        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}

// Operators are the POS users of this API. They are configured outside the
// repository (only password hashes are stored).
public sealed class ConfiguredOperatorDirectory(IOptions<OperatorsOptions> options) : IOperatorDirectory
{
    public Operator? ValidateCredentials(string username, string password)
    {
        var account = options.Value.Accounts.FirstOrDefault(a =>
            string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase));

        return account is not null && PasswordHasher.Verify(password, account.PasswordHash)
            ? new Operator(account.Username, string.IsNullOrWhiteSpace(account.DisplayName) ? account.Username : account.DisplayName)
            : null;
    }
}

public sealed class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : ITokenIssuer
{
    public LoginResponse Issue(Operator op)
    {
        var jwt = options.Value;
        DateTime expires = clock.GetUtcNow().UtcDateTime.AddMinutes(jwt.ExpirationMinutes);

        var token = new JwtSecurityToken(
            jwt.Issuer,
            jwt.Audience,
            [new Claim(ClaimTypes.Name, op.Username), new Claim("display_name", op.DisplayName)],
            expires: expires,
            signingCredentials: new SigningCredentials(SigningKey(jwt), SecurityAlgorithms.HmacSha256));

        return new LoginResponse(new JwtSecurityTokenHandler().WriteToken(token), expires, op.DisplayName);
    }

    public static SymmetricSecurityKey SigningKey(JwtOptions jwt) => new(Encoding.UTF8.GetBytes(jwt.SigningKey));
}

public sealed class HttpContextCurrentOperator(IHttpContextAccessor accessor) : ICurrentOperator
{
    public string Name => accessor.HttpContext?.User.FindFirstValue("display_name")
        ?? accessor.HttpContext?.User.Identity?.Name
        ?? "desconocido";
}
