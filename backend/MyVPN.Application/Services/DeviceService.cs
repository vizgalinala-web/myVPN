using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVPN.Application.Abstractions;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Application.Options;
using MyVPN.Domain.Entities;
using MyVPN.Domain.Enums;

namespace MyVPN.Application.Services;

public sealed class DeviceService
{
    private readonly IDeviceRepository _devices;
    private readonly IUserRepository _users;
    private readonly IDeviceConnectionEventRepository _connectionEvents;
    private readonly IWireGuardPublicKeyValidator _publicKeyValidator;
    private readonly VpnConfigurationService _vpnConfiguration;
    private readonly IClock _clock;
    private readonly VpnOptions _vpnOptions;
    private readonly IValidator<CreateDeviceRequest> _createValidator;
    private readonly ILogger<DeviceService> _logger;

    public DeviceService(
        IDeviceRepository devices,
        IUserRepository users,
        IDeviceConnectionEventRepository connectionEvents,
        IWireGuardPublicKeyValidator publicKeyValidator,
        VpnConfigurationService vpnConfiguration,
        IClock clock,
        IOptions<VpnOptions> vpnOptions,
        IValidator<CreateDeviceRequest> createValidator,
        ILogger<DeviceService> logger)
    {
        _devices = devices;
        _users = users;
        _connectionEvents = connectionEvents;
        _publicKeyValidator = publicKeyValidator;
        _vpnConfiguration = vpnConfiguration;
        _clock = clock;
        _vpnOptions = vpnOptions.Value;
        _createValidator = createValidator;
        _logger = logger;
    }

    public async Task<DevicesResponse> ListAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await EnsureActiveUserAsync(userId, cancellationToken);
        var devices = await _devices.ListByUserAsync(userId, cancellationToken);
        return new DevicesResponse(devices.Select(Map).ToList());
    }

    public async Task<DeviceResponse> GetAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        await EnsureActiveUserAsync(userId, cancellationToken);
        var device = await _devices.FindByIdForUserAsync(deviceId, userId, cancellationToken);
        if (device is null)
        {
            throw new AppException(ErrorCodes.NotFound, "Not found", "Device not found.", 404);
        }

        return Map(device);
    }

    public async Task<DeviceConnectionEventsResponse> ListConnectionEventsAsync(
        Guid userId,
        Guid deviceId,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        await EnsureActiveUserAsync(userId, cancellationToken);
        var device = await _devices.FindByIdForUserAsync(deviceId, userId, cancellationToken);
        if (device is null)
        {
            throw new AppException(ErrorCodes.NotFound, "Not found", "Device not found.", 404);
        }

        var events = await _connectionEvents.ListByDeviceForUserAsync(deviceId, userId, take, cancellationToken);
        return new DeviceConnectionEventsResponse(events.Select(e => new DeviceConnectionEventResponse(
            e.Id,
            e.ServerId,
            e.EventType.ToString(),
            e.VpnAddress,
            e.CreatedAt)).ToList());
    }

    public async Task<DeviceResponse> CreateAsync(Guid userId, CreateDeviceRequest request, CancellationToken cancellationToken = default)
    {
        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
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

        if (!_publicKeyValidator.IsValid(request.PublicKey))
        {
            throw new AppException(
                ErrorCodes.ValidationFailed,
                "Validation failed",
                "Public key format is invalid.",
                400,
                errors: new Dictionary<string, string[]>
                {
                    ["PublicKey"] = ["Public key must be a valid WireGuard public key (32-byte Base64)."]
                });
        }

        await EnsureActiveUserAsync(userId, cancellationToken);

        var deviceCount = await _devices.CountByUserAsync(userId, cancellationToken);
        if (deviceCount >= _vpnOptions.MaxDevicesPerUser)
        {
            throw new AppException(
                ErrorCodes.Conflict,
                "Device limit reached",
                $"A maximum of {_vpnOptions.MaxDevicesPerUser} devices is allowed per account.",
                409);
        }

        if (await _devices.PublicKeyExistsAsync(request.PublicKey, cancellationToken))
        {
            throw new AppException(
                ErrorCodes.Conflict,
                "Conflict",
                "A device with this public key already exists.",
                409);
        }

        if (!Enum.TryParse<DevicePlatform>(request.Platform, ignoreCase: true, out var platform))
        {
            throw new AppException(
                ErrorCodes.ValidationFailed,
                "Validation failed",
                "Platform must be iOS or Windows.",
                400);
        }

        var device = new Device
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name.Trim(),
            Platform = platform,
            PublicKey = request.PublicKey.Trim(),
            VpnAddress = null,
            CreatedAt = _clock.UtcNow,
            LastSeenAt = null,
            IsActive = true
        };

        await _devices.AddAsync(device, cancellationToken);
        await _devices.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Device created. UserId={UserId} DeviceId={DeviceId}", userId, device.Id);
        return Map(device);
    }

    public async Task DeleteAsync(Guid userId, Guid deviceId, CancellationToken cancellationToken = default)
    {
        await EnsureActiveUserAsync(userId, cancellationToken);

        var device = await _devices.FindByIdForUserAsync(deviceId, userId, cancellationToken);
        if (device is null)
        {
            throw new AppException(
                ErrorCodes.NotFound,
                "Not found",
                "Device not found.",
                404);
        }

        await _vpnConfiguration.DeprovisionDeviceAsync(device, cancellationToken);
        _devices.Remove(device);
        await _devices.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Device deleted. UserId={UserId} DeviceId={DeviceId}", userId, deviceId);
    }

    private async Task EnsureActiveUserAsync(Guid userId, CancellationToken cancellationToken)
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
    }

    private static DeviceResponse Map(Device device) => new(
        device.Id,
        device.Name,
        device.Platform.ToString(),
        device.PublicKey,
        device.VpnAddress,
        device.LastConnectedServerId,
        device.ConnectedAt,
        device.IsConnected,
        device.CreatedAt,
        device.LastSeenAt,
        device.IsActive);
}
