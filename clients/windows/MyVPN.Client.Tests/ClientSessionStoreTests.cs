using FluentAssertions;
using MyVPN.Client;

namespace MyVPN.Client.Tests;

public sealed class ClientSessionStoreTests
{
    [Fact]
    public void SaveLoad_RoundTripsWithoutSecrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "myvpn-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "session.json");
        try
        {
            var session = new ClientSession(
                Api: "https://api.example.com/",
                Email: "user@example.com",
                DeviceId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                ServerId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                ServerName: "Germany 1",
                ConfigPath: Path.Combine(dir, "myvpn.conf"),
                SavedAt: DateTimeOffset.Parse("2026-09-07T12:00:00Z"));

            ClientSessionStore.Save(session, path);
            var loaded = ClientSessionStore.Load(path);

            loaded.Should().BeEquivalentTo(session);
            var json = File.ReadAllText(path);
            json.Should().Contain("user@example.com");
            json.Should().NotContain("password");
            json.Should().NotContain("PrivateKey");
            json.Should().NotContain("refreshToken");
            json.Should().NotContain("Bearer");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Load_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(Path.GetTempPath(), "missing-session-" + Guid.NewGuid().ToString("N") + ".json");
        ClientSessionStore.Load(path).Should().BeNull();
    }

    [Fact]
    public void Delete_RemovesFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "myvpn-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "session.json");
        try
        {
            ClientSessionStore.Save(
                new ClientSession(
                    "https://api.example.com/",
                    "a@example.com",
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "NL",
                    Path.Combine(dir, "myvpn.conf"),
                    DateTimeOffset.UtcNow),
                path);

            ClientSessionStore.Delete(path).Should().BeTrue();
            File.Exists(path).Should().BeFalse();
            ClientSessionStore.Delete(path).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
