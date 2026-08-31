using FluentAssertions;
using MyVPN.Application.Common;
using MyVPN.Application.DTOs;
using MyVPN.Domain.Entities;

namespace MyVPN.UnitTests.Authentication;

public sealed class AuthServiceTests
{
    [Fact]
    public async Task Register_ValidUser_Succeeds()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);

        var result = await auth.RegisterAsync(new RegisterRequest("User@Example.com", TestHelpers.ValidPassword));

        result.Email.Should().Be("user@example.com");
        result.Id.Should().NotBeEmpty();
        var stored = db.Users.Single();
        stored.PasswordHash.Should().NotBe(TestHelpers.ValidPassword);
        stored.PasswordHash.Should().StartWith("argon2id$");
        stored.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    [InlineData("a@b")]
    public async Task Register_InvalidEmail_Rejected(string email)
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);

        var act = () => auth.RegisterAsync(new RegisterRequest(email, TestHelpers.ValidPassword));
        var ex = await act.Should().ThrowAsync<AppException>();
        ex.Which.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Register_ShortPassword_Rejected()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        var act = () => auth.RegisterAsync(new RegisterRequest("a@example.com", "short"));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Register_LongPassword_Rejected()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        var act = () => auth.RegisterAsync(new RegisterRequest("a@example.com", new string('a', 129)));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Register_WeakPassword_Rejected()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        var act = () => auth.RegisterAsync(new RegisterRequest("a@example.com", "password1234"));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Conflict()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        await auth.RegisterAsync(new RegisterRequest("dup@example.com", TestHelpers.ValidPassword));

        var act = () => auth.RegisterAsync(new RegisterRequest("DUP@example.com", TestHelpers.ValidPassword));
        (await act.Should().ThrowAsync<AppException>()).Which.Status.Should().Be(409);
    }

    [Fact]
    public async Task Login_Success_ReturnsTokens()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        await auth.RegisterAsync(new RegisterRequest("login@example.com", TestHelpers.ValidPassword));

        var tokens = await auth.LoginAsync(new LoginRequest("login@example.com", TestHelpers.ValidPassword));

        tokens.AccessToken.Should().NotBeNullOrWhiteSpace();
        tokens.RefreshToken.Should().NotBeNullOrWhiteSpace();
        tokens.ExpiresIn.Should().Be(900);
        tokens.TokenType.Should().Be("Bearer");
        db.RefreshTokens.Should().ContainSingle();
        db.RefreshTokens.Single().TokenHash.Should().NotBe(tokens.RefreshToken);
    }

    [Fact]
    public async Task Login_WrongPassword_NeutralError()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        await auth.RegisterAsync(new RegisterRequest("login@example.com", TestHelpers.ValidPassword));

        var act = () => auth.LoginAsync(new LoginRequest("login@example.com", "WrongPassword!!!"));
        var ex = await act.Should().ThrowAsync<AppException>();
        ex.Which.Code.Should().Be(ErrorCodes.InvalidCredentials);
        ex.Which.Message.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Login_UnknownEmail_SameNeutralError()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);

        var act = () => auth.LoginAsync(new LoginRequest("missing@example.com", TestHelpers.ValidPassword));
        var ex = await act.Should().ThrowAsync<AppException>();
        ex.Which.Code.Should().Be(ErrorCodes.InvalidCredentials);
        ex.Which.Message.Should().Be("Invalid email or password.");
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndRevokesOld()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        await auth.RegisterAsync(new RegisterRequest("r@example.com", TestHelpers.ValidPassword));
        var first = await auth.LoginAsync(new LoginRequest("r@example.com", TestHelpers.ValidPassword));

        var second = await auth.RefreshAsync(new RefreshRequest(first.RefreshToken));

        second.RefreshToken.Should().NotBe(first.RefreshToken);
        var old = db.RefreshTokens.OrderBy(t => t.CreatedAt).First();
        old.RevokedAt.Should().NotBeNull();
        old.ReplacedByTokenId.Should().NotBeNull();
    }

    [Fact]
    public async Task Refresh_ExpiredToken_Rejected()
    {
        await using var db = TestHelpers.CreateDb();
        var clock = new TestClock();
        var auth = TestHelpers.CreateAuthService(db, clock);
        await auth.RegisterAsync(new RegisterRequest("e@example.com", TestHelpers.ValidPassword));
        var tokens = await auth.LoginAsync(new LoginRequest("e@example.com", TestHelpers.ValidPassword));

        clock.UtcNow = clock.UtcNow.AddDays(40);
        var act = () => auth.RefreshAsync(new RefreshRequest(tokens.RefreshToken));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.InvalidRefreshToken);
    }

    [Fact]
    public async Task Refresh_RevokedTokenReuse_RevokesFamily()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        await auth.RegisterAsync(new RegisterRequest("reuse@example.com", TestHelpers.ValidPassword));
        var first = await auth.LoginAsync(new LoginRequest("reuse@example.com", TestHelpers.ValidPassword));
        var second = await auth.RefreshAsync(new RefreshRequest(first.RefreshToken));

        var act = () => auth.RefreshAsync(new RefreshRequest(first.RefreshToken));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.RefreshTokenReused);

        db.RefreshTokens.All(t => t.RevokedAt != null).Should().BeTrue();

        var act2 = () => auth.RefreshAsync(new RefreshRequest(second.RefreshToken));
        await act2.Should().ThrowAsync<AppException>();
    }

    [Fact]
    public async Task Logout_IsIdempotent()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        await auth.RegisterAsync(new RegisterRequest("out@example.com", TestHelpers.ValidPassword));
        var tokens = await auth.LoginAsync(new LoginRequest("out@example.com", TestHelpers.ValidPassword));

        await auth.LogoutAsync(new LogoutRequest(tokens.RefreshToken));
        await auth.LogoutAsync(new LogoutRequest(tokens.RefreshToken));

        db.RefreshTokens.Single().RevokedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Login_InactiveUser_Forbidden()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        await auth.RegisterAsync(new RegisterRequest("blocked@example.com", TestHelpers.ValidPassword));
        db.Users.Single().IsActive = false;
        await db.SaveChangesAsync();

        var act = () => auth.LoginAsync(new LoginRequest("blocked@example.com", TestHelpers.ValidPassword));
        (await act.Should().ThrowAsync<AppException>()).Which.Code.Should().Be(ErrorCodes.UserInactive);
    }

    [Fact]
    public async Task Jwt_DoesNotContainSensitiveClaims()
    {
        await using var db = TestHelpers.CreateDb();
        var auth = TestHelpers.CreateAuthService(db);
        await auth.RegisterAsync(new RegisterRequest("jwt@example.com", TestHelpers.ValidPassword));
        var tokens = await auth.LoginAsync(new LoginRequest("jwt@example.com", TestHelpers.ValidPassword));

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(tokens.AccessToken);
        jwt.Claims.Select(c => c.Type).Should().Contain(new[] { "sub", "jti", "iat", "iss", "aud", "exp" });
        string.Join(',', jwt.Claims.Select(c => c.Value)).Should().NotContain(TestHelpers.ValidPassword);
        string.Join(',', jwt.Claims.Select(c => c.Value)).Should().NotContain("argon2id");
    }
}
