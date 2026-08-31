using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MyVPN.Application.Options;
using MyVPN.Application.Services;
using MyVPN.Application.Validation;
using MyVPN.Domain.Entities;
using MyVPN.Infrastructure.Persistence;
using MyVPN.Infrastructure.Security;
using MyVPN.Infrastructure.Vpn;

namespace MyVPN.UnitTests;

internal sealed class TestClock : MyVPN.Application.Abstractions.IClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2025-01-01T00:00:00Z");
}

internal static class TestHelpers
{
    public static MyVpnDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MyVpnDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new MyVpnDbContext(options);
    }

    public static AuthService CreateAuthService(MyVpnDbContext db, TestClock? clock = null)
    {
        clock ??= new TestClock();
        var refreshOptions = Options.Create(new RefreshTokenOptions
        {
            LifetimeDays = 30,
            HashPepper = "unit-test-pepper-value-32chars!!"
        });
        var jwtOptions = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = "unit-test-signing-key-32chars-min!",
            AccessTokenLifetimeMinutes = 15
        });

        return new AuthService(
            new UserRepository(db),
            new RefreshTokenRepository(db),
            new Argon2PasswordHasher(),
            new JwtTokenService(jwtOptions),
            new RefreshTokenService(refreshOptions),
            clock,
            refreshOptions,
            new RegisterRequestValidator(),
            new LoginRequestValidator(),
            new RefreshRequestValidator(),
            new LogoutRequestValidator(),
            NullLogger<AuthService>.Instance);
    }

    public static DeviceService CreateDeviceService(MyVpnDbContext db, TestClock? clock = null)
        => CreateDeviceServiceWithOptions(db, clock, Options.Create(new VpnOptions { MaxDevicesPerUser = 5 }));

    public static DeviceService CreateDeviceServiceWithOptions(
        MyVpnDbContext db,
        TestClock? clock,
        Microsoft.Extensions.Options.IOptions<VpnOptions> vpnOptions)
    {
        clock ??= new TestClock();
        var provisioner = new InMemoryWireGuardPeerProvisioner(NullLogger<InMemoryWireGuardPeerProvisioner>.Instance);
        var events = new DeviceConnectionEventRepository(db);
        var vpnConfiguration = new VpnConfigurationService(
            new DeviceRepository(db),
            new UserRepository(db),
            new VpnServerRepository(db),
            events,
            new VpnIpAllocator(),
            provisioner,
            clock,
            vpnOptions,
            NullLogger<VpnConfigurationService>.Instance);

        return new DeviceService(
            new DeviceRepository(db),
            new UserRepository(db),
            events,
            new WireGuardPublicKeyValidator(),
            vpnConfiguration,
            clock,
            vpnOptions,
            new CreateDeviceRequestValidator(),
            NullLogger<DeviceService>.Instance);
    }

    public const string ValidPublicKeyA = "ERERERERERERERERERERERERERERERERERERERERERE=";
    public const string ValidPublicKeyB = "IiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiIiI=";
    public const string ValidPassword = "CorrectHorseBatteryStaple!";
}
