using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyVPN.Application.Common;
using MyVPN.Application.Options;
using MyVPN.Application.Services;
using MyVPN.Domain.Entities;
using MyVPN.Infrastructure.Persistence;
using MyVPN.Infrastructure.Vpn;

namespace MyVPN.UnitTests.Vpn;

public sealed class VpnProvisioningTests
{
    [Fact]
    public void Allocator_SkipsNetworkBroadcastAndServerHost()
    {
        var allocator = new VpnIpAllocator();
        var first = allocator.Allocate("10.8.0.0/24", Array.Empty<string>());
        first.Should().Be("10.8.0.2/32");

        var second = allocator.Allocate("10.8.0.0/24", new[] { first });
        second.Should().Be("10.8.0.3/32");
    }

    [Fact]
    public void Allocator_ExhaustedPool_ThrowsConflict()
    {
        var allocator = new VpnIpAllocator();
        // /30 => network, .1 server, .2 only client, .3 broadcast => one client max
        var first = allocator.Allocate("10.9.0.0/30", Array.Empty<string>());
        first.Should().Be("10.9.0.2/32");

        var act = () => allocator.Allocate("10.9.0.0/30", new[] { first });
        act.Should().Throw<AppException>().Which.Status.Should().Be(409);
    }

    [Fact]
    public async Task Configuration_AssignsAddress_WithoutPrivateKey()
    {
        await using var db = TestHelpers.CreateDb();
        var clock = new TestClock();
        var user = await SeedUserAsync(db);
        var server = await SeedServerAsync(db, "10.8.0.0/24");
        var device = await SeedDeviceAsync(db, user.Id, TestHelpers.ValidPublicKeyA);

        var service = CreateConfigService(db, clock);
        var config = await service.GetConfigurationAsync(user.Id, device.Id, server.Id);

        config.Address.Should().Be("10.8.0.2/32");
        config.Peer.PublicKey.Should().Be(server.PublicKey);
        config.Peer.Endpoint.Should().Be(server.Endpoint);
        config.WireGuardQuickConfig.Should().Contain("[Interface]");
        config.WireGuardQuickConfig.Should().Contain("Address = 10.8.0.2/32");
        config.WireGuardQuickConfig.Should().NotContain("PrivateKey =");
        config.WireGuardQuickConfig.ToLowerInvariant().Should().NotContain("privatekey =");

        var stored = await db.Devices.FindAsync(device.Id);
        stored!.VpnAddress.Should().Be("10.8.0.2/32");
        stored.LastSeenAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Configuration_ReusesAddress_ForSameNetwork()
    {
        await using var db = TestHelpers.CreateDb();
        var user = await SeedUserAsync(db);
        var server = await SeedServerAsync(db, "10.8.0.0/24");
        var device = await SeedDeviceAsync(db, user.Id, TestHelpers.ValidPublicKeyA);
        var service = CreateConfigService(db, new TestClock());

        var first = await service.GetConfigurationAsync(user.Id, device.Id, server.Id);
        var second = await service.GetConfigurationAsync(user.Id, device.Id, server.Id);

        second.Address.Should().Be(first.Address);
    }

    [Fact]
    public async Task Configuration_ForeignDevice_ReturnsNotFound()
    {
        await using var db = TestHelpers.CreateDb();
        var owner = await SeedUserAsync(db, "owner@example.com");
        var other = await SeedUserAsync(db, "other@example.com");
        var server = await SeedServerAsync(db, "10.8.0.0/24");
        var device = await SeedDeviceAsync(db, owner.Id, TestHelpers.ValidPublicKeyA);
        var service = CreateConfigService(db, new TestClock());

        var act = () => service.GetConfigurationAsync(other.Id, device.Id, server.Id);
        (await act.Should().ThrowAsync<AppException>()).Which.Status.Should().Be(404);
    }

    [Fact]
    public async Task Configuration_DisabledServer_NotFound()
    {
        await using var db = TestHelpers.CreateDb();
        var user = await SeedUserAsync(db);
        var server = await SeedServerAsync(db, "10.8.0.0/24", enabled: false);
        var device = await SeedDeviceAsync(db, user.Id, TestHelpers.ValidPublicKeyA);
        var service = CreateConfigService(db, new TestClock());

        var act = () => service.GetConfigurationAsync(user.Id, device.Id, server.Id);
        (await act.Should().ThrowAsync<AppException>()).Which.Status.Should().Be(404);
    }

    [Fact]
    public async Task Disconnect_ClearsConnectionState_Idempotent()
    {
        await using var db = TestHelpers.CreateDb();
        var clock = new TestClock();
        var user = await SeedUserAsync(db);
        var server = await SeedServerAsync(db, "10.8.0.0/24");
        var device = await SeedDeviceAsync(db, user.Id, TestHelpers.ValidPublicKeyA);
        var service = CreateConfigService(db, clock);

        await service.GetConfigurationAsync(user.Id, device.Id, server.Id);
        (await db.Devices.FindAsync(device.Id))!.IsConnected.Should().BeTrue();

        await service.DisconnectAsync(user.Id, device.Id);
        await service.DisconnectAsync(user.Id, device.Id);

        var stored = await db.Devices.FindAsync(device.Id);
        stored!.ConnectedAt.Should().BeNull();
        stored.LastConnectedServerId.Should().BeNull();
        stored.IsConnected.Should().BeFalse();
        stored.VpnAddress.Should().Be("10.8.0.2/32");
    }

    private static VpnConfigurationService CreateConfigService(MyVpnDbContext db, TestClock clock)
        => new(
            new DeviceRepository(db),
            new UserRepository(db),
            new VpnServerRepository(db),
            new DeviceConnectionEventRepository(db),
            new VpnIpAllocator(),
            new InMemoryWireGuardPeerProvisioner(NullLogger<InMemoryWireGuardPeerProvisioner>.Instance),
            clock,
            Options.Create(new VpnOptions()),
            NullLogger<VpnConfigurationService>.Instance);

    private static async Task<User> SeedUserAsync(MyVpnDbContext db, string email = "user@example.com")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = User.NormalizeEmail(email),
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<VpnServer> SeedServerAsync(MyVpnDbContext db, string network, bool enabled = true)
    {
        var server = new VpnServer
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Country = "DE",
            City = "Frankfurt",
            Hostname = $"host-{Guid.NewGuid():N}.example.com",
            Endpoint = "host.example.com:51820",
            PublicKey = TestHelpers.ValidPublicKeyB,
            VpnNetwork = network,
            Enabled = enabled,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.VpnServers.Add(server);
        await db.SaveChangesAsync();
        return server;
    }

    private static async Task<Device> SeedDeviceAsync(MyVpnDbContext db, Guid userId, string publicKey)
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = "Phone",
            Platform = MyVPN.Domain.Enums.DevicePlatform.iOS,
            PublicKey = publicKey,
            VpnAddress = null,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return device;
    }
}
