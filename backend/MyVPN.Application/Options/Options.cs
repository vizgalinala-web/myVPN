namespace MyVPN.Application.Options;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public string SigningKey { get; set; } = string.Empty;
    public int AccessTokenLifetimeMinutes { get; set; } = 15;
}

public sealed class RefreshTokenOptions
{
    public const string SectionName = "RefreshTokens";

    public int LifetimeDays { get; set; } = 30;
    /// <summary>Optional HMAC pepper for hashing refresh tokens. If empty, SHA-256 of raw token is used.</summary>
    public string HashPepper { get; set; } = string.Empty;
}

public sealed class CorsOptions
{
    public const string SectionName = "Cors";

    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public RateLimitPolicyOptions Register { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };
    public RateLimitPolicyOptions Login { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60 };
    public RateLimitPolicyOptions Refresh { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 };
    public RateLimitPolicyOptions Logout { get; set; } = new() { PermitLimit = 20, WindowSeconds = 60 };
    public RateLimitPolicyOptions DeviceConfiguration { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
    public RateLimitPolicyOptions DeviceDisconnect { get; set; } = new() { PermitLimit = 30, WindowSeconds = 60 };
    public RateLimitPolicyOptions AccountDelete { get; set; } = new() { PermitLimit = 5, WindowSeconds = 60 };
}

public sealed class RateLimitPolicyOptions
{
    public int PermitLimit { get; set; } = 10;
    public int WindowSeconds { get; set; } = 60;
}

public sealed class VpnOptions
{
    public const string SectionName = "Vpn";

    /// <summary>Comma-separated DNS servers embedded in client configs.</summary>
    public string DnsServers { get; set; } = "1.1.1.1";

    /// <summary>AllowedIPs for full-tunnel MVP.</summary>
    public string AllowedIps { get; set; } = "0.0.0.0/0, ::/0";

    public int PersistentKeepaliveSeconds { get; set; } = 25;

    /// <summary>Host offset reserved for the WireGuard server inside each VpnNetwork CIDR (typically .1).</summary>
    public int ReservedServerHostOffset { get; set; } = 1;

    /// <summary>Maximum active devices a single user may register.</summary>
    public int MaxDevicesPerUser { get; set; } = 5;

    /// <summary>InMemory or File. File writes peer JSON for an external wg sync agent.</summary>
    public string PeerProvisioner { get; set; } = "InMemory";

    /// <summary>Directory used when PeerProvisioner=File.</summary>
    public string PeerStateDirectory { get; set; } = "peer-state";
}

public sealed class RefreshTokenCleanupOptions
{
    public const string SectionName = "RefreshTokenCleanup";

    public bool Enabled { get; set; } = true;
    public int IntervalHours { get; set; } = 24;
    /// <summary>Delete expired/revoked tokens older than this many days.</summary>
    public int RetentionDays { get; set; } = 7;
}
