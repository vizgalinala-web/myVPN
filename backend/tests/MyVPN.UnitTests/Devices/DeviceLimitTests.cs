using FluentAssertions;
using Microsoft.Extensions.Options;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Application.Options;
using MyVPN.Application.Services;
using MyVPN.Domain.Entities;
using MyVPN.Infrastructure.Persistence;

namespace MyVPN.UnitTests.Devices;

public sealed class DeviceLimitTests
{
    [Fact]
    public async Task Create_ExceedsMaxDevices_ReturnsDeviceLimitReached()
    {
        await using var db = TestHelpers.CreateDb();
        var clock = new TestClock();
        var user = await SeedUserAsync(db);
        var options = Options.Create(new VpnOptions { MaxDevicesPerUser = 1 });
        var service = TestHelpers.CreateDeviceServiceWithOptions(db, clock, options);

        await service.CreateAsync(user.Id, new CreateDeviceRequest("One", "iOS", TestHelpers.ValidPublicKeyA));
        var act = () => service.CreateAsync(user.Id, new CreateDeviceRequest("Two", "Windows", TestHelpers.ValidPublicKeyB));
        var ex = (await act.Should().ThrowAsync<AppException>()).Which;
        ex.Status.Should().Be(409);
        ex.Code.Should().Be(ErrorCodes.DeviceLimitReached);
    }

    [Fact]
    public async Task Create_InactiveDeviceStillCountsTowardLimit()
    {
        await using var db = TestHelpers.CreateDb();
        var clock = new TestClock();
        var user = await SeedUserAsync(db);
        var options = Options.Create(new VpnOptions { MaxDevicesPerUser = 1 });
        var service = TestHelpers.CreateDeviceServiceWithOptions(db, clock, options);

        var created = await service.CreateAsync(user.Id, new CreateDeviceRequest("One", "iOS", TestHelpers.ValidPublicKeyA));
        var device = await db.Devices.FindAsync(created.Id);
        device!.IsActive = false;
        await db.SaveChangesAsync();

        var act = () => service.CreateAsync(user.Id, new CreateDeviceRequest("Two", "Windows", TestHelpers.ValidPublicKeyB));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.DeviceLimitReached);
    }

    [Fact]
    public async Task Delete_Device_FreesSlotForNewRegistration()
    {
        await using var db = TestHelpers.CreateDb();
        var clock = new TestClock();
        var user = await SeedUserAsync(db);
        var options = Options.Create(new VpnOptions { MaxDevicesPerUser = 1 });
        var service = TestHelpers.CreateDeviceServiceWithOptions(db, clock, options);

        var first = await service.CreateAsync(user.Id, new CreateDeviceRequest("One", "iOS", TestHelpers.ValidPublicKeyA));
        await service.DeleteAsync(user.Id, first.Id);

        var second = await service.CreateAsync(user.Id, new CreateDeviceRequest("Two", "Windows", TestHelpers.ValidPublicKeyB));
        second.Id.Should().NotBe(first.Id);
    }

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
