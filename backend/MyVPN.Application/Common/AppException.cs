namespace MyVPN.Application.Common;

public sealed class AppException : Exception
{
    public string Code { get; }
    public string Title { get; }
    public int Status { get; }
    public string Type { get; }
    public IDictionary<string, string[]>? Errors { get; }

    public AppException(
        string code,
        string title,
        string detail,
        int status,
        string? type = null,
        IDictionary<string, string[]>? errors = null)
        : base(detail)
    {
        Code = code;
        Title = title;
        Status = status;
        Type = type ?? $"https://api.myvpn.example/errors/{code.ToLowerInvariant().Replace('_', '-')}";
        Errors = errors;
    }
}

public static class ErrorCodes
{
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string EmailConflict = "EMAIL_CONFLICT";
    public const string WeakPassword = "WEAK_PASSWORD";
    public const string UserInactive = "USER_INACTIVE";
    public const string InvalidRefreshToken = "INVALID_REFRESH_TOKEN";
    public const string RefreshTokenReused = "REFRESH_TOKEN_REUSED";
    public const string Unauthorized = "UNAUTHORIZED";
    public const string NotFound = "NOT_FOUND";
    public const string Conflict = "CONFLICT";
    public const string RateLimited = "RATE_LIMITED";
}
