using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MyVPN.Domain.Entities;

namespace MyVPN.Infrastructure.Persistence.Seed;

public static class DevelopmentVpnServerSeeder
{
    public static async Task SeedAsync(IServiceProvider services, IHostEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (!environment.IsDevelopment())
        {
            return;
        }

        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyVpnDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("VpnServerSeed");

        await EnsureSeedDataAsync(db, logger, cancellationToken);
    }

    public static async Task EnsureSeedDataAsync(MyVpnDbContext db, ILogger logger, CancellationToken cancellationToken = default)
    {
        var seeds = GetDevelopmentServers();
        foreach (var seed in seeds)
        {
            var exists = await db.VpnServers.AnyAsync(s => s.Hostname == seed.Hostname, cancellationToken);
            if (exists)
            {
                continue;
            }

            db.VpnServers.Add(seed);
        }

        var added = await db.SaveChangesAsync(cancellationToken);
        if (added > 0)
        {
            logger.LogInformation("Seeded {Count} development VPN servers.", added);
        }
    }

    public static IReadOnlyList<VpnServer> GetDevelopmentServers()
    {
        var now = DateTimeOffset.UtcNow;
        return new[]
        {
            Create("Germany 1", "DE", "Frankfurt", "de1.example.com", "de1.example.com:51820", "ERERERERERERERERERERERERERERERERERERERERERE=", "10.8.0.0/24", now),
            Create("Netherlands 1", "NL", "Amsterdam", "nl1.example.com", "nl1.example.com:51820", "IiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiI=", "10.8.1.0/24", now),
            Create("United Kingdom 1", "GB", "London", "uk1.example.com", "uk1.example.com:51820", "MzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzMzM=", "10.8.2.0/24", now),
            Create("USA 1", "US", "New York", "us1.example.com", "us1.example.com:51820", "REREREREREREREREREREREREREREREREREREREREREQ=", "10.8.3.0/24", now)
        };
    }

    private static VpnServer Create(
        string name,
        string country,
        string city,
        string hostname,
        string endpoint,
        string publicKey,
        string vpnNetwork,
        DateTimeOffset createdAt)
        => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            Country = country,
            City = city,
            Hostname = hostname,
            Endpoint = endpoint,
            PublicKey = publicKey,
            VpnNetwork = vpnNetwork,
            Enabled = true,
            CreatedAt = createdAt
        };
}
