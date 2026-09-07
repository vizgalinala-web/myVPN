using FluentAssertions;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Domain.Entities;

namespace MyVPN.UnitTests.Authentication;

public sealed class AccountServiceTests
{
    [Fact]
    public async Task DeleteAccount_WrongPassword_DoesNotErase()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        var registered = await auth.RegisterAsync(new RegisterRequest("erase@example.com", TestHelpers.ValidPassword));
        var (account, _, _) = TestHelpers.CreateAccountStack(db);

        var act = () => account.DeleteAccountAsync(registered.Id, new DeleteAccountRequest("WrongPassword!!"));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.InvalidCredentials);

        db.Users.Should().ContainSingle(u => u.Id == registered.Id);
    }

    [Fact]
    public async Task DeleteAccount_EmptyPassword_ValidationFailed()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        var registered = await auth.RegisterAsync(new RegisterRequest("erase@example.com", TestHelpers.ValidPassword));
        var (account, _, _) = TestHelpers.CreateAccountStack(db);

        var act = () => account.DeleteAccountAsync(registered.Id, new DeleteAccountRequest(""));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.ValidationFailed);
        db.Users.Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteAccount_RemovesUserDevicesTokensAndPeers()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        var registered = await auth.RegisterAsync(new RegisterRequest("erase@example.com", TestHelpers.ValidPassword));
        await auth.LoginAsync(new LoginRequest("erase@example.com", TestHelpers.ValidPassword));

        var server = new VpnServer
        {
            Id = Guid.NewGuid(),
            Name = "DE",
            Country = "DE",
            City = "Frankfurt",
            Hostname = "de1.example.com",
            Endpoint = "de1.example.com:51820",
            PublicKey = TestHelpers.ValidPublicKeyB,
            VpnNetwork = "10.8.0.0/24",
            Enabled = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.VpnServers.Add(server);
        await db.SaveChangesAsync();

        var devices = TestHelpers.CreateDeviceService(db);
        var device = await devices.CreateAsync(
            registered.Id,
            new CreateDeviceRequest("Phone", "iOS", TestHelpers.ValidPublicKeyA));

        var (account, provisioner, vpn) = TestHelpers.CreateAccountStack(db);
        await vpn.GetConfigurationAsync(registered.Id, device.Id, server.Id);
        provisioner.Snapshot().Should().ContainSingle();

        await account.DeleteAccountAsync(registered.Id, new DeleteAccountRequest(TestHelpers.ValidPassword));

        db.Users.Should().BeEmpty();
        db.Devices.Should().BeEmpty();
        db.RefreshTokens.Should().BeEmpty();
        provisioner.Snapshot().Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAccount_FreesEmailForReregister()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        var registered = await auth.RegisterAsync(new RegisterRequest("reuse@example.com", TestHelpers.ValidPassword));
        var (account, _, _) = TestHelpers.CreateAccountStack(db);

        await account.DeleteAccountAsync(registered.Id, new DeleteAccountRequest(TestHelpers.ValidPassword));

        var again = await auth.RegisterAsync(new RegisterRequest("reuse@example.com", TestHelpers.ValidPassword));
        again.Email.Should().Be("reuse@example.com");
        again.Id.Should().NotBe(registered.Id);
    }

    [Fact]
    public async Task DeleteAccount_UnknownUser_Unauthorized()
    {
        await using var db = TestHelpers.CreateDb();
        var (account, _, _) = TestHelpers.CreateAccountStack(db);
        var act = () => account.DeleteAccountAsync(Guid.NewGuid(), new DeleteAccountRequest(TestHelpers.ValidPassword));
        (await act.Should().ThrowAsync<AppException>()).Which.Status.Should().Be(401);
    }
}
