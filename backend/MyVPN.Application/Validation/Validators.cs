using System.Text.RegularExpressions;
using FluentValidation;
using MyVPN.Application.DTOs;
using MyVPN.Domain.Enums;

namespace MyVPN.Application.Validation;

public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password123", "password1234", "password12345", "password123456",
        "123456789012", "1234567890123", "qwertyuiopas", "qwertyuiopasd",
        "iloveyou1234", "adminadmin12", "letmeinletmein", "welcome12345",
        "changeme1234", "football1234", "baseball1234", "monkeymonkey1",
        "abcdefghijkl", "aaaaaaaaaaaa", "111111111111"
    };

    public static bool IsTooCommon(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return true;
        }

        if (CommonPasswords.Contains(password))
        {
            return true;
        }

        var compact = Regex.Replace(password, @"\s+", string.Empty);
        return CommonPasswords.Contains(compact);
    }
}

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    private static readonly Regex ControlChars = new(@"[\p{C}]", RegexOptions.Compiled);
    private static readonly Regex EmailFormat = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .MaximumLength(320)
            .Must(e => EmailFormat.IsMatch(e))
            .WithMessage("Email format is invalid.")
            .EmailAddress()
            .Must(e => !ControlChars.IsMatch(e))
            .WithMessage("Email must not contain control characters.");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength)
            .MaximumLength(PasswordPolicy.MaxLength)
            .Must(p => !PasswordPolicy.IsTooCommon(p))
            .WithMessage("Password is too common or weak.")
            .Must(p => !ControlChars.IsMatch(p))
            .WithMessage("Password must not contain control characters.");
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(320);
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
    }
}

public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken).NotEmpty().MaximumLength(512);
    }
}

public sealed class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
{
    private static readonly Regex ControlChars = new(@"[\p{C}]", RegexOptions.Compiled);

    public ChangePasswordRequestValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(PasswordPolicy.MinLength)
            .MaximumLength(PasswordPolicy.MaxLength)
            .Must(p => !PasswordPolicy.IsTooCommon(p))
            .WithMessage("Password is too common or weak.")
            .Must(p => !ControlChars.IsMatch(p))
            .WithMessage("Password must not contain control characters.")
            .Must((req, neu) => neu != req.CurrentPassword)
            .WithMessage("New password must be different from the current password.");
    }
}

public sealed class DeleteAccountRequestValidator : AbstractValidator<DeleteAccountRequest>
{
    public DeleteAccountRequestValidator()
    {
        RuleFor(x => x.Password).NotEmpty().MaximumLength(PasswordPolicy.MaxLength);
    }
}

public sealed class CreateDeviceRequestValidator : AbstractValidator<CreateDeviceRequest>
{
    private static readonly Regex ControlChars = new(@"[\p{C}]", RegexOptions.Compiled);

    public CreateDeviceRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100)
            .Must(n => !ControlChars.IsMatch(n))
            .WithMessage("Name must not contain control characters.");

        RuleFor(x => x.Platform)
            .NotEmpty()
            .Must(BeSupportedPlatform)
            .WithMessage("Platform must be iOS or Windows.");

        RuleFor(x => x.PublicKey)
            .NotEmpty()
            .MaximumLength(64);
    }

    private static bool BeSupportedPlatform(string platform)
        => Enum.TryParse<DevicePlatform>(platform, ignoreCase: true, out _);
}
