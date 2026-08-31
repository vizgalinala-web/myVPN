using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MyVPN.Domain.Entities;
using MyVPN.Domain.Enums;

namespace MyVPN.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.HasIndex(x => x.Email).IsUnique();
        builder.HasMany(x => x.Devices).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.RefreshTokens).WithOne(x => x.User).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.ToTable("devices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Platform).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(x => x.PublicKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.VpnAddress).HasMaxLength(64);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.PublicKey).IsUnique();
        builder.HasIndex(x => x.LastConnectedServerId);
        builder.HasOne<VpnServer>()
            .WithMany()
            .HasForeignKey(x => x.LastConnectedServerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}

public sealed class VpnServerConfiguration : IEntityTypeConfiguration<VpnServer>
{
    public void Configure(EntityTypeBuilder<VpnServer> builder)
    {
        builder.ToTable("vpn_servers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Country).HasMaxLength(2).IsRequired();
        builder.Property(x => x.City).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Hostname).HasMaxLength(253).IsRequired();
        builder.Property(x => x.Endpoint).HasMaxLength(253).IsRequired();
        builder.Property(x => x.PublicKey).HasMaxLength(64).IsRequired();
        builder.Property(x => x.VpnNetwork).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Enabled).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.HasIndex(x => x.Enabled);
        builder.HasIndex(x => x.Hostname).IsUnique();
    }
}

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.TokenFamilyId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ExpiresAt);
        builder.HasIndex(x => x.TokenFamilyId);
        builder.HasOne(x => x.ReplacedByToken)
            .WithMany()
            .HasForeignKey(x => x.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
