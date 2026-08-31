using MyVPN.Domain.Entities;
using MyVPN.Domain.Enums;

namespace MyVPN.Application.Abstractions;

public interface IUserRepository
{
    Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IDeviceRepository
{
    Task<IReadOnlyList<Device>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default);
    Task<Device?> FindByIdForUserAsync(Guid deviceId, Guid userId, CancellationToken cancellationToken = default);
    Task<bool> PublicKeyExistsAsync(string publicKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListAssignedVpnAddressesAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Device device, CancellationToken cancellationToken = default);
    void Remove(Device device);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IVpnServerRepository
{
    Task<IReadOnlyList<VpnServer>> ListEnabledAsync(CancellationToken cancellationToken = default);
    Task<VpnServer?> FindEnabledByIdAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default);
    Task RevokeFamilyAsync(Guid tokenFamilyId, DateTimeOffset revokedAt, string? revokedByIp, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}

public interface IJwtTokenService
{
    (string Token, DateTimeOffset ExpiresAt, string Jti) CreateAccessToken(User user);
    int AccessTokenLifetimeSeconds { get; }
}

public interface IRefreshTokenService
{
    (string RawToken, string TokenHash) Generate();
    string Hash(string rawToken);
}

public interface IWireGuardPublicKeyValidator
{
    bool IsValid(string publicKey);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public interface ICurrentUser
{
    Guid? UserId { get; }
}
