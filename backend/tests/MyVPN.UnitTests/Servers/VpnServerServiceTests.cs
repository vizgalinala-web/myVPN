using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using MyVPN.Application.Services;
using MyVPN.Domain.Entities;
using MyVPN.Infrastructure.Persistence;
using MyVPN.Infrastructure.Persistence.Seed;

namespace MyVPN.UnitTests.Servers;

public sealed class VpnServerServiceTests
{
    [Fact]
    public async Task ListEnabled_ReturnsOnlyEnabled_AndNoSecrets()
    {
        await using var db = TestHelpers.CreateDb();
        db.VpnServers.AddRange(
            new VpnServer
            {
                Id = Guid.NewGuid(),
                Name = "On",
                Country = "DE",
                City = "Frankfurt",
                Hostname = "on.example.com",
                Endpoint = "on.example.com:51820",
                PublicKey = TestHelpers.ValidPublicKeyA,
                VpnNetwork = "10.8.0.0/24",
                Enabled = true,
                CreatedAt = DateTimeOffset.UtcNow
            },
            new VpnServer
            {
                Id = Guid.NewGuid(),
                Name = "Off",
                Country = "NL",
                City = "Amsterdam",
                Hostname = "off.example.com",
                Endpoint = "off.example.com:51820",
                PublicKey = TestHelpers.ValidPublicKeyB,
                VpnNetwork = "10.8.1.0/24",
                Enabled = false,
                CreatedAt = DateTimeOffset.UtcNow
            });
        await db.SaveChangesAsync();

        var service = new VpnServerService(new VpnServerRepository(db));
        var result = await service.ListEnabledAsync();

        result.Servers.Should().ContainSingle().Which.Name.Should().Be("On");
        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().NotContain("PublicKey");
        json.Should().NotContain("VpnNetwork");
        json.Should().NotContain(TestHelpers.ValidPublicKeyA);
    }

    [Fact]
    public async Task Seed_IsIdempotent()
    {
        await using var db = TestHelpers.CreateDb();
        var logger = NullLogger.Instance;

        await DevelopmentVpnServerSeeder.EnsureSeedDataAsync(db, logger);
        await DevelopmentVpnServerSeeder.EnsureSeedDataAsync(db, logger);

        (await db.VpnServers.CountAsync()).Should().Be(4);
    }
}
