using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyVPN.Client;

/// <summary>
/// Last successful local connect metadata. Never stores passwords, tokens, or private keys.
/// </summary>
public sealed record ClientSession(
    string Api,
    string Email,
    Guid DeviceId,
    Guid ServerId,
    string ServerName,
    string ConfigPath,
    DateTimeOffset SavedAt);

public static class ClientSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public static string DefaultDirectory()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return OperatingSystem.IsWindows()
            ? Path.Combine(home, "MyVPN")
            : Path.Combine(home, ".myvpn");
    }

    public static string DefaultPath() => Path.Combine(DefaultDirectory(), "session.json");

    public static void Save(ClientSession session, string? path = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (string.IsNullOrWhiteSpace(session.Api) || string.IsNullOrWhiteSpace(session.Email))
        {
            throw new ArgumentException("Session must include Api and Email.");
        }

        var full = Path.GetFullPath(path ?? DefaultPath());
        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? DefaultDirectory());
        var json = JsonSerializer.Serialize(session, JsonOptions);
        LocalSecretFile.WriteAllText(full, json);
    }

    public static ClientSession? Load(string? path = null)
    {
        var full = Path.GetFullPath(path ?? DefaultPath());
        if (!File.Exists(full))
        {
            return null;
        }

        var json = File.ReadAllText(full);
        return JsonSerializer.Deserialize<ClientSession>(json, JsonOptions);
    }

    public static bool Delete(string? path = null)
    {
        var full = Path.GetFullPath(path ?? DefaultPath());
        if (!File.Exists(full))
        {
            return false;
        }

        File.Delete(full);
        return true;
    }
}
