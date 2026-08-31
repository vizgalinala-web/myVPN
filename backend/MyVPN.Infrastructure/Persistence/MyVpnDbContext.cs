using Microsoft.EntityFrameworkCore;
using MyVPN.Domain.Entities;

namespace MyVPN.Infrastructure.Persistence;

public sealed class MyVpnDbContext : DbContext
{
    public MyVpnDbContext(DbContextOptions<MyVpnDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<VpnServer> VpnServers => Set<VpnServer>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<DeviceConnectionEvent> DeviceConnectionEvents => Set<DeviceConnectionEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MyVpnDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
