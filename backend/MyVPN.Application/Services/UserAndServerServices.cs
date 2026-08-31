using MyVPN.Application.Abstractions;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;

namespace MyVPN.Application.Services;

public sealed class UserQueryService
{
    private readonly IUserRepository _users;

    public UserQueryService(IUserRepository users)
    {
        _users = users;
    }

    public async Task<CurrentUserResponse> GetCurrentAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new AppException(
                ErrorCodes.Unauthorized,
                "Unauthorized",
                "Authentication is required.",
                401);
        }

        return new CurrentUserResponse(user.Id, user.Email, user.CreatedAt);
    }
}

public sealed class VpnServerService
{
    private readonly IVpnServerRepository _servers;

    public VpnServerService(IVpnServerRepository servers)
    {
        _servers = servers;
    }

    /// <summary>
    /// Returns only enabled servers. Endpoint is public for MVP — no auth required.
    /// </summary>
    public async Task<ServersResponse> ListEnabledAsync(CancellationToken cancellationToken = default)
    {
        var servers = await _servers.ListEnabledAsync(cancellationToken);
        var dtos = servers.Select(s => new VpnServerDto(
            s.Id,
            s.Name,
            s.Country,
            s.City,
            s.Hostname,
            s.Endpoint)).ToList();

        return new ServersResponse(dtos);
    }
}
