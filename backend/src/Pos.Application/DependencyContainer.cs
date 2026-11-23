using Microsoft.Extensions.DependencyInjection;
using Pos.Application.Auth;
using Pos.Application.Catalog;
using Pos.Application.Discounts;
using Pos.Application.Sales;

namespace Pos.Application;

public static class DependencyContainer
{
    public static IServiceCollection AddPosApplication(this IServiceCollection services)
    {
        services.AddScoped<SaleBuilder>();
        services.AddSingleton<IDiscountPolicyCatalog, DiscountPolicyCatalog>();

        services.AddScoped<ILoginInputPort, LoginInteractor>();
        services.AddScoped<ISearchProductsInputPort, SearchProductsInteractor>();
        services.AddScoped<IGetActiveSessionInputPort, GetActiveSessionInteractor>();
        services.AddScoped<IGetDiscountPoliciesInputPort, GetDiscountPoliciesInteractor>();
        services.AddScoped<IQuoteSaleInputPort, QuoteSaleInteractor>();
        services.AddScoped<IRegisterSaleInputPort, RegisterSaleInteractor>();
        services.AddScoped<IGetSalesHistoryInputPort, GetSalesHistoryInteractor>();
        services.AddScoped<IGetSaleDetailInputPort, GetSaleDetailInteractor>();
        return services;
    }
}
