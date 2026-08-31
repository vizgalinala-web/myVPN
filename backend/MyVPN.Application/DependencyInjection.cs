using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using MyVPN.Application.Services;
using MyVPN.Application.Validation;

namespace MyVPN.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();
        services.AddScoped<AuthService>();
        services.AddScoped<DeviceService>();
        services.AddScoped<UserQueryService>();
        services.AddScoped<VpnServerService>();
        return services;
    }
}
