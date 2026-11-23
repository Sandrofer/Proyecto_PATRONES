using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pos.Application.Ports;
using Pos.Domain.Discounts;
using Pos.Infrastructure.Idempotency;
using Pos.Infrastructure.Odoo;
using Pos.Infrastructure.Security;

namespace Pos.Infrastructure;

public sealed class DiscountOptions
{
    public const string Section = "Discounts";
    public decimal ManualMaxPercent { get; set; } = 10m;
    public decimal VolumeMinimumAmount { get; set; } = 100m;
    public decimal VolumePercent { get; set; } = 5m;
    public decimal StudentPercent { get; set; } = 10m;
}

public static class DependencyContainer
{
    public static IServiceCollection AddPosInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<OdooOptions>().Bind(configuration.GetSection(OdooOptions.Section))
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey), "Odoo:ApiKey no está configurada (use user-secrets o variables de entorno).")
            .Validate(o => o.PosConfigId > 0, "Odoo:PosConfigId no está configurado.")
            .ValidateOnStart();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection(JwtOptions.Section))
            .Validate(o => o.SigningKey.Length >= 32, "Jwt:SigningKey debe tener al menos 32 caracteres.")
            .ValidateOnStart();
        services.Configure<OperatorsOptions>(configuration.GetSection(OperatorsOptions.Section));
        services.Configure<DiscountOptions>(configuration.GetSection(DiscountOptions.Section));

        services.AddHttpClient<OdooJson2Client>((sp, http) =>
        {
            var odoo = sp.GetRequiredService<IOptions<OdooOptions>>().Value;
            http.BaseAddress = new Uri(odoo.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = TimeSpan.FromSeconds(odoo.TimeoutSeconds);
        });
        services.AddScoped<IPosGateway, OdooPosGateway>();
        services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

        // Discount strategies. Registering a new IDiscountPolicy is the only
        // change needed to offer a new discount (P10).
        services.AddSingleton<IDiscountPolicy, NoDiscountPolicy>();
        services.AddSingleton<IDiscountPolicy>(sp =>
            new ManualPercentageDiscountPolicy(sp.GetRequiredService<IOptions<DiscountOptions>>().Value.ManualMaxPercent));
        services.AddSingleton<IDiscountPolicy>(sp =>
            new StudentDiscountPolicy(sp.GetRequiredService<IOptions<DiscountOptions>>().Value.StudentPercent));
        services.AddSingleton<IDiscountPolicy>(sp =>
        {
            var d = sp.GetRequiredService<IOptions<DiscountOptions>>().Value;
            return new VolumeDiscountPolicy(d.VolumeMinimumAmount, d.VolumePercent);
        });

        services.AddHttpContextAccessor();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IOperatorDirectory, ConfiguredOperatorDirectory>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddScoped<ICurrentOperator, HttpContextCurrentOperator>();
        return services;
    }
}
