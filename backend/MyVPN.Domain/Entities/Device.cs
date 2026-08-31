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
    public Guid? LastConnectedServerId { get; set; }
    public DateTimeOffset? ConnectedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
    public bool IsActive { get; set; } = true;

    public bool IsConnected => ConnectedAt is not null && LastConnectedServerId is not null;

    public User User { get; set; } = null!;
}
