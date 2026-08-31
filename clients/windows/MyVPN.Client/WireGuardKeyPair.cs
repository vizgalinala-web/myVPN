using NSec.Cryptography;

namespace MyVPN.Client;

/// <summary>
/// Generates WireGuard-compatible Curve25519 key pairs.
/// Private keys must stay on the device and are never sent to the API.
/// </summary>
public static class WireGuardKeyPair
{
    public static (string PrivateKey, string PublicKey) Generate()
    {
        var algorithm = new X25519();
        using var key = Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
        var privateBytes = key.Export(KeyBlobFormat.RawPrivateKey);
        // WireGuard private keys are clamped Curve25519 scalars; NSec already produces valid keys.
        var publicBytes = key.PublicKey.Export(KeyBlobFormat.RawPublicKey);
        return (Convert.ToBase64String(privateBytes), Convert.ToBase64String(publicBytes));
    }

    public static bool IsValidPublicKey(string publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey) || publicKey.Length != 44 || publicKey.Any(char.IsWhiteSpace))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[64];
        return Convert.TryFromBase64String(publicKey, buffer, out var written) && written == 32;
    }
}
