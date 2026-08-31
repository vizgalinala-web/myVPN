using FluentAssertions;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Domain.Entities;
using MyVPN.Domain.Enums;
using MyVPN.Infrastructure.Security;

namespace MyVPN.UnitTests.Devices;

public sealed class DeviceServiceTests
{
    [Fact]
    public async Task CreateDevice_Succeeds_WithNullVpnAddress()
    {
        await using var db = TestHelpers.CreateDb();
        var user = await SeedUserAsync(db);
        var service = TestHelpers.CreateDeviceService(db);

        var device = await service.CreateAsync(user.Id, new CreateDeviceRequest("My iPhone", "iOS", TestHelpers.ValidPublicKeyA));

        device.VpnAddress.Should().BeNull();
        device.PublicKey.Should().Be(TestHelpers.ValidPublicKeyA);
        device.Platform.Should().Be("iOS");
    }

    [Fact]
    public async Task List_ReturnsOnlyOwnDevices()
    {
        await using var db = TestHelpers.CreateDb();
        var a = await SeedUserAsync(db, "a@example.com");
        var b = await SeedUserAsync(db, "b@example.com");
        var service = TestHelpers.CreateDeviceService(db);
        await service.CreateAsync(a.Id, new CreateDeviceRequest("A", "iOS", TestHelpers.ValidPublicKeyA));
        await service.CreateAsync(b.Id, new CreateDeviceRequest("B", "Windows", TestHelpers.ValidPublicKeyB));

        var list = await service.ListAsync(a.Id);
        list.Devices.Should().ContainSingle().Which.Name.Should().Be("A");
    }

    [Fact]
    public async Task Delete_OwnDevice_Succeeds()
    {
        await using var db = TestHelpers.CreateDb();
        var user = await SeedUserAsync(db);
        var service = TestHelpers.CreateDeviceService(db);
        var created = await service.CreateAsync(user.Id, new CreateDeviceRequest("Phone", "iOS", TestHelpers.ValidPublicKeyA));

        await service.DeleteAsync(user.Id, created.Id);
        (await service.ListAsync(user.Id)).Devices.Should().BeEmpty();
    }

    [Fact]
    public async Task Delete_ForeignDevice_ReturnsNotFound()
    {
        await using var db = TestHelpers.CreateDb();
        var a = await SeedUserAsync(db, "a@example.com");
        var b = await SeedUserAsync(db, "b@example.com");
        var service = TestHelpers.CreateDeviceService(db);
        var created = await service.CreateAsync(a.Id, new CreateDeviceRequest("Phone", "iOS", TestHelpers.ValidPublicKeyA));

        var act = () => service.DeleteAsync(b.Id, created.Id);
        (await act.Should().ThrowAsync<AppException>()).Which.Status.Should().Be(404);
    }

    [Fact]
    public async Task Create_DuplicatePublicKey_Conflict()
    {
        await using var db = TestHelpers.CreateDb();
        var user = await SeedUserAsync(db);
        var service = TestHelpers.CreateDeviceService(db);
        await service.CreateAsync(user.Id, new CreateDeviceRequest("One", "iOS", TestHelpers.ValidPublicKeyA));

        var act = () => service.CreateAsync(user.Id, new CreateDeviceRequest("Two", "Windows", TestHelpers.ValidPublicKeyA));
        (await act.Should().ThrowAsync<AppException>()).Which.Status.Should().Be(409);
    }

    [Fact]
    public async Task Create_InvalidPublicKey_Rejected()
    {
        await using var db = TestHelpers.CreateDb();
        var user = await SeedUserAsync(db);
        var service = TestHelpers.CreateDeviceService(db);

        var act = () => service.CreateAsync(user.Id, new CreateDeviceRequest("Phone", "iOS", "not-a-key"));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Theory]
    [InlineData("ERERERERERERERERERERERERERERERERERERERERERE=", true)]
    [InlineData("not valid", false)]
    [InlineData("ERERERERERERERERERERERERERERERERERERERERERE= ", false)]
    [InlineData("AAAA", false)]
    public void WireGuardPublicKeyValidator_ChecksFormat(string key, bool expected)
    {
        new WireGuardPublicKeyValidator().IsValid(key).Should().Be(expected);
    }

    private static async Task<User> SeedUserAsync(MyVPN.Infrastructure.Persistence.MyVpnDbContext db, string email = "user@example.com")
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
}
