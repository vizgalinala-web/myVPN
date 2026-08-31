using Microsoft.EntityFrameworkCore;
using MyVPN.Application.Abstractions;
using MyVPN.Domain.Entities;

namespace MyVPN.Infrastructure.Persistence;

public sealed class UserRepository : IUserRepository
{
    private readonly MyVpnDbContext _db;

    public UserRepository(MyVpnDbContext db) => _db = db;

    public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => _db.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail, cancellationToken);

    public Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken = default)
        => _db.Users.AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
        => await _db.Users.AddAsync(user, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

public sealed class DeviceRepository : IDeviceRepository
{
    private readonly MyVpnDbContext _db;

    public DeviceRepository(MyVpnDbContext db) => _db = db;

    public async Task<IReadOnlyList<Device>> ListByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => await _db.Devices.AsNoTracking()
            .Where(d => d.UserId == userId)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(cancellationToken);

    public Task<Device?> FindByIdForUserAsync(Guid deviceId, Guid userId, CancellationToken cancellationToken = default)
        => _db.Devices.FirstOrDefaultAsync(d => d.Id == deviceId && d.UserId == userId, cancellationToken);

    public Task<bool> PublicKeyExistsAsync(string publicKey, CancellationToken cancellationToken = default)
        => _db.Devices.AnyAsync(d => d.PublicKey == publicKey, cancellationToken);

    public async Task<IReadOnlyList<string>> ListAssignedVpnAddressesAsync(CancellationToken cancellationToken = default)
        => await _db.Devices.AsNoTracking()
            .Where(d => d.VpnAddress != null && d.VpnAddress != "")
            .Select(d => d.VpnAddress!)
            .ToListAsync(cancellationToken);

    public Task<int> CountByUserAsync(Guid userId, CancellationToken cancellationToken = default)
        => _db.Devices.CountAsync(d => d.UserId == userId, cancellationToken);

    public async Task AddAsync(Device device, CancellationToken cancellationToken = default)
        => await _db.Devices.AddAsync(device, cancellationToken);

    public void Remove(Device device) => _db.Devices.Remove(device);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

public sealed class VpnServerRepository : IVpnServerRepository
{
    private readonly MyVpnDbContext _db;

    public VpnServerRepository(MyVpnDbContext db) => _db = db;

    public async Task<IReadOnlyList<VpnServer>> ListEnabledAsync(CancellationToken cancellationToken = default)
        => await _db.VpnServers.AsNoTracking()
            .Where(s => s.Enabled)
            .OrderBy(s => s.Country).ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);

    public Task<VpnServer?> FindEnabledByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => _db.VpnServers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id && s.Enabled, cancellationToken);
}

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly MyVpnDbContext _db;

    public RefreshTokenRepository(MyVpnDbContext db) => _db = db;

    public Task<RefreshToken?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        => _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task AddAsync(RefreshToken token, CancellationToken cancellationToken = default)
        => await _db.RefreshTokens.AddAsync(token, cancellationToken);

    public async Task RevokeFamilyAsync(Guid tokenFamilyId, DateTimeOffset revokedAt, string? revokedByIp, CancellationToken cancellationToken = default)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.TokenFamilyId == tokenFamilyId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = revokedAt;
            token.RevokedByIp = revokedByIp;
        }
    }

    public async Task RevokeAllForUserAsync(Guid userId, DateTimeOffset revokedAt, string? revokedByIp, CancellationToken cancellationToken = default)
    {
        var tokens = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
        {
            token.RevokedAt = revokedAt;
            token.RevokedByIp = revokedByIp;
        }
    }

    public async Task<int> DeleteExpiredOrRevokedAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default)
    {
        var stale = await _db.RefreshTokens
            .Where(t =>
                (t.ExpiresAt < olderThan) ||
                (t.RevokedAt != null && t.RevokedAt < olderThan))
            .ToListAsync(cancellationToken);

        if (stale.Count == 0)
        {
            return 0;
        }

        _db.RefreshTokens.RemoveRange(stale);
        await _db.SaveChangesAsync(cancellationToken);
        return stale.Count;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}

public sealed class DeviceConnectionEventRepository : IDeviceConnectionEventRepository
{
    private readonly MyVpnDbContext _db;

    public DeviceConnectionEventRepository(MyVpnDbContext db) => _db = db;

    public async Task AddAsync(DeviceConnectionEvent connectionEvent, CancellationToken cancellationToken = default)
        => await _db.DeviceConnectionEvents.AddAsync(connectionEvent, cancellationToken);

    public async Task<IReadOnlyList<DeviceConnectionEvent>> ListByDeviceForUserAsync(
        Guid deviceId,
        Guid userId,
        int take,
        CancellationToken cancellationToken = default)
        => await _db.DeviceConnectionEvents.AsNoTracking()
            .Where(e => e.DeviceId == deviceId && e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .ThenByDescending(e => e.Id)
            .Take(Math.Clamp(take, 1, 100))
            .ToListAsync(cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);
}
