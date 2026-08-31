using FluentAssertions;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Domain.Entities;
using MyVPN.Infrastructure.Persistence;
using MyVPN.Infrastructure.Vpn;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyVPN.Application.Options;

namespace MyVPN.UnitTests.Authentication;

public sealed class ChangePasswordAndCleanupTests
{
    [Fact]
    public async Task ChangePassword_RevokesRefreshTokens_AndRejectsOldPassword()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        await auth.RegisterAsync(new RegisterRequest("pw@example.com", TestHelpers.ValidPassword));
        var login = await auth.LoginAsync(new LoginRequest("pw@example.com", TestHelpers.ValidPassword), null);
        var userId = db.Users.Single().Id;

        await auth.ChangePasswordAsync(
            userId,
            new ChangePasswordRequest(TestHelpers.ValidPassword, "AnotherStrongPassphrase!"),
            "127.0.0.1");

        db.RefreshTokens.All(t => t.RevokedAt != null).Should().BeTrue();

        var actOld = () => auth.LoginAsync(new LoginRequest("pw@example.com", TestHelpers.ValidPassword), null);
        await actOld.Should().ThrowAsync<AppException>();

        var actRefresh = () => auth.RefreshAsync(new RefreshRequest(login.RefreshToken), null);
        await actRefresh.Should().ThrowAsync<AppException>();

        var neu = await auth.LoginAsync(new LoginRequest("pw@example.com", "AnotherStrongPassphrase!"), null);
        neu.AccessToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RefreshTokenCleanup_DeletesOldRevokedRows()
    {
        await using var db = TestHelpers.CreateDb();
        var clock = new TestClock { UtcNow = DateTimeOffset.Parse("2025-01-01T00:00:00Z") };
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = "clean@example.com",
            PasswordHash = "hash",
            CreatedAt = clock.UtcNow,
            UpdatedAt = clock.UtcNow,
            IsActive = true
        };
        db.Users.Add(user);
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenFamilyId = Guid.NewGuid(),
            TokenHash = "abc",
            CreatedAt = clock.UtcNow.AddDays(-40),
            ExpiresAt = clock.UtcNow.AddDays(-30),
            RevokedAt = clock.UtcNow.AddDays(-30)
        });
        await db.SaveChangesAsync();

        var repo = new RefreshTokenRepository(db);
        var deleted = await repo.DeleteExpiredOrRevokedAsync(clock.UtcNow.AddDays(-7));
        deleted.Should().Be(1);
        db.RefreshTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task FileProvisioner_WritesAndRemovesPeerFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "myvpn-peers-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var provisioner = new FileSystemWireGuardPeerProvisioner(
                Options.Create(new VpnOptions { PeerStateDirectory = dir }),
                NullLogger<FileSystemWireGuardPeerProvisioner>.Instance);

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
            var device = new Device
            {
                Id = Guid.NewGuid(),
                UserId = Guid.NewGuid(),
                Name = "Phone",
                Platform = MyVPN.Domain.Enums.DevicePlatform.iOS,
                PublicKey = TestHelpers.ValidPublicKeyA,
                VpnAddress = "10.8.0.2/32",
                CreatedAt = DateTimeOffset.UtcNow,
                IsActive = true
            };

            await provisioner.AddOrUpdatePeerAsync(server, device);
            Directory.GetFiles(dir, "*.json").Should().ContainSingle();
            await provisioner.RemovePeerAsync(device.PublicKey);
            Directory.GetFiles(dir, "*.json").Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
