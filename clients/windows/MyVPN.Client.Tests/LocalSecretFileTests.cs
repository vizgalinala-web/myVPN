using FluentAssertions;
using MyVPN.Client;

namespace MyVPN.Client.Tests;

public sealed class LocalSecretFileTests
{
    [Fact]
    public void WriteAllText_CreatesFile_AndRestrictsUnixMode()
    {
        var dir = Path.Combine(Path.GetTempPath(), "myvpn-secret-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(dir, "myvpn.conf");
        try
        {
            LocalSecretFile.WriteAllText(path, "[Interface]\nPrivateKey = PLACEHOLDER\n");
            File.ReadAllText(path).Should().Contain("PLACEHOLDER");
            if (!OperatingSystem.IsWindows())
            {
                File.GetUnixFileMode(path).Should().Be(LocalSecretFile.OwnerReadWrite);
            }
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
