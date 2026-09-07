using FluentValidation;
using Microsoft.Extensions.Logging;
using MyVPN.Application.Abstractions;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;

namespace MyVPN.Application.Services;

/// <summary>
/// Account lifecycle. Erasure removes the user, devices, refresh tokens, and WireGuard peers.
/// </summary>
public sealed class AccountService
{
    private readonly IUserRepository _users;
    private readonly IDeviceRepository _devices;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly VpnConfigurationService _vpnConfiguration;
    private readonly IValidator<DeleteAccountRequest> _deleteValidator;
    private readonly ILogger<AccountService> _logger;

    public AccountService(
        IUserRepository users,
        IDeviceRepository devices,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        VpnConfigurationService vpnConfiguration,
        IValidator<DeleteAccountRequest> deleteValidator,
        ILogger<AccountService> logger)
    {
        _users = users;
        _devices = devices;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _vpnConfiguration = vpnConfiguration;
        _deleteValidator = deleteValidator;
        _logger = logger;
    }

    /// <summary>
    /// Permanently deletes the authenticated account after password confirmation.
    /// Wrong password uses the same 401 shape as login so callers cannot probe lock state.
    /// </summary>
    public async Task DeleteAccountAsync(
        Guid userId,
        DeleteAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await _deleteValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            var errors = validation.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            throw new AppException(
                ErrorCodes.ValidationFailed,
                "Validation failed",
                "One or more validation errors occurred.",
                400,
                errors: errors);
        }

        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new AppException(ErrorCodes.Unauthorized, "Unauthorized", "Authentication is required.", 401);
        }

        if (!_passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            throw new AppException(
                ErrorCodes.InvalidCredentials,
                "Invalid credentials",
                "Invalid email or password.",
                401);
        }

        var devices = await _devices.ListByUserTrackedAsync(userId, cancellationToken);
        foreach (var device in devices)
        {
            await _vpnConfiguration.DeprovisionDeviceAsync(device, cancellationToken);
            _devices.Remove(device);
        }

        await _refreshTokens.DeleteAllForUserAsync(userId, cancellationToken);
        _users.Remove(user);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Account erased. UserId={UserId} DeviceCount={DeviceCount}", userId, devices.Count);
    }
}
