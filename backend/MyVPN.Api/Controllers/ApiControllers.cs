using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Application.Services;

namespace MyVPN.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly AuthService _auth;

    public AuthController(AuthService auth) => _auth = auth;

    /// <summary>Register a new user. Duplicate email returns 409 without revealing extra details.</summary>
    [HttpPost("register")]
    [EnableRateLimiting("auth-register")]
    [ProducesResponseType(typeof(RegisterResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<RegisterResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.RegisterAsync(request, cancellationToken);
        return Created($"/api/me", result);
    }

    [HttpPost("login")]
    [EnableRateLimiting("auth-login")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TokenResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.LoginAsync(request, GetClientIp(), cancellationToken);
        return Ok(result);
    }

    [HttpPost("refresh")]
    [EnableRateLimiting("auth-refresh")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<TokenResponse>> Refresh([FromBody] RefreshRequest request, CancellationToken cancellationToken)
    {
        var result = await _auth.RefreshAsync(request, GetClientIp(), cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Revokes the provided refresh token. Idempotent. Does not require a Bearer access token.
    /// Access tokens remain valid until expiry (no server-side blacklist in Phase 2).
    /// </summary>
    [HttpPost("logout")]
    [EnableRateLimiting("auth-logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
    {
        await _auth.LogoutAsync(request, GetClientIp(), cancellationToken);
        return NoContent();
    }

    private string? GetClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();
}

[ApiController]
[Route("api/me")]
[Authorize]
public sealed class MeController : ControllerBase
{
    private readonly UserQueryService _users;

    public MeController(UserQueryService users) => _users = users;

    [HttpGet]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<CurrentUserResponse>> Get(CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        return Ok(await _users.GetCurrentAsync(userId, cancellationToken));
    }

    private Guid RequireUserId()
    {
        var value = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var id))
        {
            throw new AppException(ErrorCodes.Unauthorized, "Unauthorized", "Authentication is required.", 401);
        }

        return id;
    }
}

[ApiController]
[Route("api/servers")]
public sealed class ServersController : ControllerBase
{
    private readonly VpnServerService _servers;

    public ServersController(VpnServerService servers) => _servers = servers;

    /// <summary>Public MVP endpoint — returns only Enabled servers. No authentication required.</summary>
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ServersResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ServersResponse>> List(CancellationToken cancellationToken)
        => Ok(await _servers.ListEnabledAsync(cancellationToken));
}

[ApiController]
[Route("api/devices")]
[Authorize]
public sealed class DevicesController : ControllerBase
{
    private readonly DeviceService _devices;

    public DevicesController(DeviceService devices) => _devices = devices;

    [HttpGet]
    [ProducesResponseType(typeof(DevicesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DevicesResponse>> List(CancellationToken cancellationToken)
        => Ok(await _devices.ListAsync(RequireUserId(), cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(DeviceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DeviceResponse>> Create([FromBody] CreateDeviceRequest request, CancellationToken cancellationToken)
    {
        var created = await _devices.CreateAsync(RequireUserId(), request, cancellationToken);
        return Created($"/api/devices/{created.Id}", created);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _devices.DeleteAsync(RequireUserId(), id, cancellationToken);
        return NoContent();
    }

    private Guid RequireUserId()
    {
        var value = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var id))
        {
            throw new AppException(ErrorCodes.Unauthorized, "Unauthorized", "Authentication is required.", 401);
        }

        return id;
    }
}
