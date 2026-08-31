using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;
using Microsoft.Extensions.Options;
using MyVPN.Application.Abstractions;
using MyVPN.Application.Options;
using MyVPN.Domain.Entities;

namespace MyVPN.Infrastructure.Security;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class Argon2PasswordHasher : IPasswordHasher
{
    // Reasonable interactive defaults for Argon2id.
    private const int DegreeOfParallelism = 2;
    private const int MemorySizeKb = 65536;
    private const int Iterations = 3;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = HashPassword(password, salt);
        return $"argon2id$v=19$m={MemorySizeKb},t={Iterations},p={DegreeOfParallelism}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public bool Verify(string password, string passwordHash)
    {
        try
        {
            var parts = passwordHash.Split('$', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 5 || parts[0] != "argon2id")
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = HashPassword(password, salt);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch
        {
            return false;
        }
    }

    private static byte[] HashPassword(string password, byte[] salt)
    {
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySizeKb,
            Iterations = Iterations
        };

        return argon2.GetBytes(HashSize);
    }
}

public sealed class JwtTokenService : IJwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public int AccessTokenLifetimeSeconds => Math.Max(60, _options.AccessTokenLifetimeMinutes * 60);

    public (string Token, DateTimeOffset ExpiresAt, string Jti) CreateAccessToken(User user)
    {
        var jti = Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddSeconds(AccessTokenLifetimeSeconds);

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("sub", user.Id.ToString()),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, jti),
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), System.Security.Claims.ClaimValueTypes.Integer64)
        };

        var key = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new Microsoft.IdentityModel.Tokens.SigningCredentials(key, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.HmacSha256);
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: credentials);

        var encoded = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
        return (encoded, expires, jti);
    }
}

public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly RefreshTokenOptions _options;

    public RefreshTokenService(IOptions<RefreshTokenOptions> options)
    {
        _options = options.Value;
    }

    public (string RawToken, string TokenHash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var raw = Base64UrlEncode(bytes);
        return (raw, Hash(raw));
    }

    public string Hash(string rawToken)
    {
        if (!string.IsNullOrEmpty(_options.HashPepper))
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_options.HashPepper));
            return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(rawToken))).ToLowerInvariant();
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>
/// Validates WireGuard Curve25519 public keys: strict Base64 encoding of exactly 32 bytes.
/// </summary>
public sealed class WireGuardPublicKeyValidator : IWireGuardPublicKeyValidator
{
    public bool IsValid(string publicKey)
    {
        if (string.IsNullOrWhiteSpace(publicKey))
        {
            return false;
        }

        if (publicKey.Length != 44)
        {
            return false;
        }

        if (publicKey.Any(char.IsWhiteSpace))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[64];
        if (!Convert.TryFromBase64String(publicKey, buffer, out var bytesWritten))
        {
            return false;
        }

        return bytesWritten == 32;
    }
}
