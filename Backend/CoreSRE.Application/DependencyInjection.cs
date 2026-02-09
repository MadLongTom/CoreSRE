using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace CoreSRE.Application;

/// <summary>
/// Application 层依赖注入配置
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(assembly));
        services.AddAutoMapper(assembly);
        services.AddValidatorsFromAssembly(assembly);

        return services;
    }
}
