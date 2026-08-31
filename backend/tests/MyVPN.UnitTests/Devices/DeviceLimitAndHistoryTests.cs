using FluentAssertions;
using Microsoft.Extensions.Options;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Application.Options;
using MyVPN.Application.Services;
using MyVPN.Domain.Entities;
using MyVPN.Infrastructure.Persistence;

namespace MyVPN.UnitTests.Devices;

public sealed class DeviceLimitAndHistoryTests
{
    [Fact]
    public async Task Create_ExceedsMaxDevices_Conflict()
    {
        await using var db = TestHelpers.CreateDb();
        var clock = new TestClock();
        var user = await SeedUserAsync(db);
        var options = Options.Create(new VpnOptions { MaxDevicesPerUser = 1 });
        var service = CreateDeviceService(db, clock, options);

        await service.CreateAsync(user.Id, new CreateDeviceRequest("One", "iOS", TestHelpers.ValidPublicKeyA));
        var act = () => service.CreateAsync(user.Id, new CreateDeviceRequest("Two", "Windows", TestHelpers.ValidPublicKeyB));
        (await act.Should().ThrowAsync<AppException>()).Which.Status.Should().Be(409);
    }

    [Fact]
    public async Task ConnectionHistory_RecordsConnectAndDisconnect()
    {
        await using var db = TestHelpers.CreateDb();
        var clock = new TestClock();
        var user = await SeedUserAsync(db);
        var server = new VpnServer
        {
            Id = Guid.NewGuid(),
            Name = "DE",
            Country = "DE",
            City = "Frankfurt",
            Hostname = "de.example.com",
            Endpoint = "de.example.com:51820",
            PublicKey = TestHelpers.ValidPublicKeyB,
            VpnNetwork = "10.8.0.0/24",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.VpnServers.Add(server);
        await db.SaveChangesAsync();

        var devices = CreateDeviceService(db, clock, Options.Create(new VpnOptions()));
        var created = await devices.CreateAsync(user.Id, new CreateDeviceRequest("Phone", "iOS", TestHelpers.ValidPublicKeyA));

        var cfg = new VpnConfigurationService(
            new DeviceRepository(db),
            new UserRepository(db),
            new VpnServerRepository(db),
            new DeviceConnectionEventRepository(db),
            new MyVPN.Application.Services.VpnIpAllocator(),
            new MyVPN.Infrastructure.Vpn.InMemoryWireGuardPeerProvisioner(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<MyVPN.Infrastructure.Vpn.InMemoryWireGuardPeerProvisioner>.Instance),
            clock,
            Options.Create(new VpnOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<VpnConfigurationService>.Instance);

        await cfg.GetConfigurationAsync(user.Id, created.Id, server.Id);
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await cfg.DisconnectAsync(user.Id, created.Id);

        var history = await devices.ListConnectionEventsAsync(user.Id, created.Id);
        history.Events.Should().HaveCount(2);
        history.Events[0].EventType.Should().Be("Disconnected");
        history.Events[1].EventType.Should().Be("Connected");
    }

    private static DeviceService CreateDeviceService(MyVpnDbContext db, TestClock clock, IOptions<VpnOptions> options)
        => TestHelpers.CreateDeviceServiceWithOptions(db, clock, options);

    private static async Task<User> SeedUserAsync(MyVpnDbContext db)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "limit@example.com",
            PasswordHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsActive = true
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }
}
