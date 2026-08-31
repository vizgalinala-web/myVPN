using MyVPN.Domain.Enums;

namespace MyVPN.Domain.Entities;

public class Device
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DevicePlatform Platform { get; set; }
    public string PublicKey { get; set; } = string.Empty;
    public string? VpnAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool IsActive { get; set; } = true;

    public User User { get; set; } = null!;
}
