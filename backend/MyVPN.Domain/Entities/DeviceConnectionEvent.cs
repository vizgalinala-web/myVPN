using MyVPN.Domain.Enums;

namespace MyVPN.Domain.Entities;

public class DeviceConnectionEvent
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Guid UserId { get; set; }
    public Guid? ServerId { get; set; }
    public DeviceConnectionEventType EventType { get; set; }
    public string? VpnAddress { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public Device Device { get; set; } = null!;
}
