using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVPN.Application.Abstractions;
using MyVPN.Application.Options;
using MyVPN.Infrastructure.Persistence;
using MyVPN.Infrastructure.Security;
using MyVPN.Infrastructure.Vpn;

namespace MyVPN.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is required.");

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.Configure<RefreshTokenOptions>(configuration.GetSection(RefreshTokenOptions.SectionName));
        services.Configure<CorsOptions>(configuration.GetSection(CorsOptions.SectionName));
        services.Configure<RateLimitOptions>(configuration.GetSection(RateLimitOptions.SectionName));
        services.Configure<VpnOptions>(configuration.GetSection(VpnOptions.SectionName));

        services.AddDbContext<MyVpnDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsAssembly(typeof(MyVpnDbContext).Assembly.FullName);
            }));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IDeviceRepository, DeviceRepository>();
        services.AddScoped<IVpnServerRepository, VpnServerRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IDeviceConnectionEventRepository, DeviceConnectionEventRepository>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Argon2PasswordHasher>();
        services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        services.AddSingleton<IWireGuardPublicKeyValidator, WireGuardPublicKeyValidator>();
        services.AddSingleton<IWireGuardPeerProvisioner, InMemoryWireGuardPeerProvisioner>();
        services.AddScoped<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
