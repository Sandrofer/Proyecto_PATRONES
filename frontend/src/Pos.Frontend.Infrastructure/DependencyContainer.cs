using Microsoft.Extensions.DependencyInjection;
using Pos.Frontend.Application;

namespace Pos.Frontend.Infrastructure;

public static class DependencyContainer
{
    public static IServiceCollection AddPosFrontend(this IServiceCollection services, Uri apiBaseUrl)
    {
        services.AddSingleton<BrowserAuthSession>();
        services.AddSingleton<IAuthSession>(sp => sp.GetRequiredService<BrowserAuthSession>());
        services.AddSingleton<ConnectivityState>();
        services.AddSingleton<IPosApi>(sp => new HttpPosApi(
            new HttpClient { BaseAddress = apiBaseUrl, Timeout = TimeSpan.FromSeconds(30) },
            sp.GetRequiredService<IAuthSession>(),
            sp.GetRequiredService<ConnectivityState>()));
        services.AddSingleton<SystemStatusUseCase>();

        services.AddSingleton<LoginUseCase>();
        services.AddSingleton<CheckoutUseCase>();
        return services;
    }
}
