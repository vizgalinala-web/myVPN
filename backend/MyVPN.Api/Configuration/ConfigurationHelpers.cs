using System.Security.Claims;
using MyVPN.Application.Abstractions;
using MyVPN.Application.Options;

namespace MyVPN.Api.Configuration;

public sealed class HttpCurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpCurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid? UserId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User?.FindFirstValue("sub")
                ?? _httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}

public static class ConfigurationGuard
{
    private static readonly string[] ForbiddenSigningKeyFragments =
    {
        "change_me",
        "placeholder",
        "dev_only",
        "dev-only",
        "example",
        "secret",
        "password"
    };

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("ConnectionStrings__Default is required.");
        }

        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? throw new InvalidOperationException("Jwt configuration section is required.");

        if (string.IsNullOrWhiteSpace(jwt.Issuer))
        {
            throw new InvalidOperationException("Jwt__Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(jwt.Audience))
        {
            throw new InvalidOperationException("Jwt__Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(jwt.SigningKey))
        {
            throw new InvalidOperationException("Jwt__SigningKey is required.");
        }

        if (environment.IsProduction())
        {
            if (jwt.SigningKey.Length < 32)
            {
                throw new InvalidOperationException("Jwt__SigningKey must be at least 32 characters in Production.");
            }

            var lower = jwt.SigningKey.ToLowerInvariant();
            if (ForbiddenSigningKeyFragments.Any(f => lower.Contains(f, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Jwt__SigningKey appears to be a placeholder and is not allowed in Production.");
            }
        }
        else if (jwt.SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Jwt__SigningKey must be at least 32 characters.");
        }
    }
}
