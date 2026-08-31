using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MyVPN.Application.Abstractions;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Application.Options;
using MyVPN.Domain.Entities;

namespace MyVPN.Application.Services;

public sealed class AuthService
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwt;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IClock _clock;
    private readonly RefreshTokenOptions _refreshOptions;
    private readonly IValidator<RegisterRequest> _registerValidator;
    private readonly IValidator<LoginRequest> _loginValidator;
    private readonly IValidator<RefreshRequest> _refreshValidator;
    private readonly IValidator<LogoutRequest> _logoutValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwt,
        IRefreshTokenService refreshTokenService,
        IClock clock,
        IOptions<RefreshTokenOptions> refreshOptions,
        IValidator<RegisterRequest> registerValidator,
        IValidator<LoginRequest> loginValidator,
        IValidator<RefreshRequest> refreshValidator,
        IValidator<LogoutRequest> logoutValidator,
        IValidator<ChangePasswordRequest> changePasswordValidator,
        ILogger<AuthService> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _passwordHasher = passwordHasher;
        _jwt = jwt;
        _refreshTokenService = refreshTokenService;
        _clock = clock;
        _refreshOptions = refreshOptions.Value;
        _registerValidator = registerValidator;
        _loginValidator = loginValidator;
        _refreshValidator = refreshValidator;
        _logoutValidator = logoutValidator;
        _changePasswordValidator = changePasswordValidator;
        _logger = logger;
    }

    public async Task<RegisterResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(_registerValidator, request, cancellationToken);

        var email = User.NormalizeEmail(request.Email);
        if (await _users.EmailExistsAsync(email, cancellationToken))
        {
            // Explicit conflict for register; existence is revealed by successful registration anyway.
            throw new AppException(
                ErrorCodes.EmailConflict,
                "Email conflict",
                "Unable to register with the provided email.",
                StatusCodes.Conflict);
        }

        var now = _clock.UtcNow;
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            CreatedAt = now,
            UpdatedAt = now,
            IsActive = true
        };

        await _users.AddAsync(user, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User registered. UserId={UserId}", user.Id);
        return new RegisterResponse(user.Id, user.Email, user.CreatedAt);
    }

    public async Task<TokenResponse> LoginAsync(LoginRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(_loginValidator, request, cancellationToken);

        var email = User.NormalizeEmail(request.Email);
        var user = await _users.FindByEmailAsync(email, cancellationToken);

        // Constant-ish path: always verify against a real hash when possible.
        var passwordValid = user is not null && _passwordHasher.Verify(request.Password, user.PasswordHash);
        if (user is null || !passwordValid)
        {
            throw new AppException(
                ErrorCodes.InvalidCredentials,
                "Invalid credentials",
                "Invalid email or password.",
                StatusCodes.Unauthorized);
        }

        if (!user.IsActive)
        {
            throw new AppException(
                ErrorCodes.UserInactive,
                "User inactive",
                "This account is inactive.",
                StatusCodes.Forbidden);
        }

        return await IssueTokensAsync(user, Guid.NewGuid(), clientIp, cancellationToken);
    }

    public async Task<TokenResponse> RefreshAsync(RefreshRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(_refreshValidator, request, cancellationToken);

        var hash = _refreshTokenService.Hash(request.RefreshToken);
        var existing = await _refreshTokens.FindByHashAsync(hash, cancellationToken);
        if (existing is null)
        {
            throw new AppException(
                ErrorCodes.InvalidRefreshToken,
                "Invalid refresh token",
                "The refresh token is invalid.",
                StatusCodes.Unauthorized);
        }

        var now = _clock.UtcNow;

        if (existing.IsRevoked)
        {
            // Replay protection: revoke the entire token family.
            await _refreshTokens.RevokeFamilyAsync(existing.TokenFamilyId, now, clientIp, cancellationToken);
            await _refreshTokens.SaveChangesAsync(cancellationToken);
            _logger.LogWarning(
                "Refresh token reuse detected. UserId={UserId} TokenFamilyId={TokenFamilyId}",
                existing.UserId,
                existing.TokenFamilyId);

            throw new AppException(
                ErrorCodes.RefreshTokenReused,
                "Refresh token reused",
                "The refresh token was already used and has been revoked.",
                StatusCodes.Unauthorized);
        }

        if (existing.IsExpired(now))
        {
            throw new AppException(
                ErrorCodes.InvalidRefreshToken,
                "Invalid refresh token",
                "The refresh token is invalid.",
                StatusCodes.Unauthorized);
        }

        var user = await _users.FindByIdAsync(existing.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new AppException(
                ErrorCodes.UserInactive,
                "User inactive",
                "This account is inactive.",
                StatusCodes.Forbidden);
        }

        existing.RevokedAt = now;
        existing.RevokedByIp = clientIp;

        var response = await IssueTokensAsync(user, existing.TokenFamilyId, clientIp, cancellationToken, existing);
        return response;
    }

    public async Task LogoutAsync(LogoutRequest request, string? clientIp, CancellationToken cancellationToken = default)
    {
        await ValidateAsync(_logoutValidator, request, cancellationToken);

        var hash = _refreshTokenService.Hash(request.RefreshToken);
        var existing = await _refreshTokens.FindByHashAsync(hash, cancellationToken);

        // Idempotent: unknown or already-revoked tokens succeed silently.
        if (existing is not null && !existing.IsRevoked)
        {
            existing.RevokedAt = _clock.UtcNow;
            existing.RevokedByIp = clientIp;
            await _refreshTokens.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Refresh token revoked on logout. UserId={UserId}", existing.UserId);
        }
    }

    public async Task ChangePasswordAsync(
        Guid userId,
        ChangePasswordRequest request,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        await ValidateAsync(_changePasswordValidator, request, cancellationToken);

        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new AppException(ErrorCodes.Unauthorized, "Unauthorized", "Authentication is required.", 401);
        }

        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            throw new AppException(
                ErrorCodes.InvalidCredentials,
                "Invalid credentials",
                "Current password is incorrect.",
                401);
        }

        user.PasswordHash = _passwordHasher.Hash(request.NewPassword);
        user.UpdatedAt = _clock.UtcNow;
        await _refreshTokens.RevokeAllForUserAsync(userId, _clock.UtcNow, clientIp, cancellationToken);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Password changed and refresh tokens revoked. UserId={UserId}", userId);
    }

    private async Task<TokenResponse> IssueTokensAsync(
        User user,
        Guid tokenFamilyId,
        string? clientIp,
        CancellationToken cancellationToken,
        RefreshToken? replaced = null)
    {
        var (accessToken, _, _) = _jwt.CreateAccessToken(user);
        var (rawRefresh, refreshHash) = _refreshTokenService.Generate();
        var now = _clock.UtcNow;

        var refreshEntity = new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenFamilyId = tokenFamilyId,
            TokenHash = refreshHash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_refreshOptions.LifetimeDays),
            CreatedByIp = clientIp
        };

        if (replaced is not null)
        {
            replaced.ReplacedByTokenId = refreshEntity.Id;
        }

        await _refreshTokens.AddAsync(refreshEntity, cancellationToken);
        await _refreshTokens.SaveChangesAsync(cancellationToken);

        return new TokenResponse(
            accessToken,
            rawRefresh,
            _jwt.AccessTokenLifetimeSeconds);
    }

    private static async Task ValidateAsync<T>(IValidator<T> validator, T instance, CancellationToken cancellationToken)
    {
        var result = await validator.ValidateAsync(instance, cancellationToken);
        if (result.IsValid)
        {
            return;
        }

        var errors = result.Errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

        throw new AppException(
            ErrorCodes.ValidationFailed,
            "Validation failed",
            "One or more validation errors occurred.",
            StatusCodes.BadRequest,
            errors: errors);
    }
}

internal static class StatusCodes
{
    public const int BadRequest = 400;
    public const int Unauthorized = 401;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int Conflict = 409;
}
