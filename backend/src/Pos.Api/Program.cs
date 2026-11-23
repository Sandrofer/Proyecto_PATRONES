using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Pos.Api;
using Pos.Application;
using Pos.Infrastructure;
using Pos.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

// Composition root: the only place that knows every layer.
builder.Services.AddPosApplication();
builder.Services.AddPosInfrastructure(builder.Configuration);

var jwt = builder.Configuration.GetSection(JwtOptions.Section).Get<JwtOptions>() ?? new JwtOptions();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = jwt.Issuer,
        ValidAudience = jwt.Audience,
        IssuerSigningKey = JwtTokenIssuer.SigningKey(jwt),
        ValidateIssuerSigningKey = true,
        ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) }));
});

// The frontend runs on another computer, so the browser needs CORS.
// Cors:AllowedOrigins lists the frontend URLs; "*" allows any origin
// (acceptable here because authentication uses bearer tokens, not cookies).
string[] origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
{
    if (origins.Contains("*")) p.AllowAnyOrigin();
    else p.WithOrigins(origins);
    p.AllowAnyHeader().AllowAnyMethod();
}));

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapPosEndpoints();

app.Run();

// Exposed for WebApplicationFactory in tests.
public partial class Program;
