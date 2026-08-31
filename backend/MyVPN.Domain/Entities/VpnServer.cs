namespace MyVPN.Domain.Entities;

public class VpnServer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Hostname { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public string VpnNetwork { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}
