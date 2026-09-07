using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MyVPN.Application.DTOs;
using MyVPN.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace MyVPN.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _postgres;
    public WebApplicationFactory<Program>? Factory { get; private set; }
    public bool DockerAvailable { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            _postgres = new PostgreSqlBuilder()
                .WithImage("postgres:16-alpine")
                .WithDatabase("myvpn_test")
                .WithUsername("myvpn")
                .WithPassword("myvpn_test_password")
                .Build();

            await _postgres.StartAsync();
            DockerAvailable = true;

            var connectionString = _postgres.GetConnectionString();
            Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Default"] = connectionString,
                        ["Jwt:Issuer"] = "myvpn-api-test",
                        ["Jwt:Audience"] = "myvpn-clients-test",
                        ["Jwt:SigningKey"] = "INTEGRATION_TEST_SIGNING_KEY_32CHARS_OK!",
                        ["Jwt:AccessTokenLifetimeMinutes"] = "15",
                        ["RefreshTokens:LifetimeDays"] = "30",
                        ["RefreshTokens:HashPepper"] = "INTEGRATION_TEST_REFRESH_PEPPER_32OK",
                        ["Cors:AllowedOrigins:0"] = "http://localhost:3000",
                        ["RateLimiting:Register:PermitLimit"] = "1000",
                        ["RateLimiting:Login:PermitLimit"] = "1000",
                        ["RateLimiting:Refresh:PermitLimit"] = "1000",
                        ["RateLimiting:Logout:PermitLimit"] = "1000",
                        ["RateLimiting:AccountDelete:PermitLimit"] = "1000"
                    });
                });
            });

            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MyVpnDbContext>();
            await db.Database.MigrateAsync();
        }
        catch (Exception)
        {
            DockerAvailable = false;
            Factory = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (_postgres is not null)
        {
            await _postgres.DisposeAsync();
        }
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;

[Collection("api")]
public sealed class AuthAndApiIntegrationTests
{
    private readonly ApiFixture _fixture;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public AuthAndApiIntegrationTests(ApiFixture fixture) => _fixture = fixture;

    private void RequireDocker()
    {
        Skip.If(!_fixture.DockerAvailable || _fixture.Factory is null, "Docker/Testcontainers PostgreSQL unavailable.");
    }

    [SkippableFact]
    public async Task HealthEndpoints_Work()
    {
        RequireDocker();
        var client = _fixture.Factory!.CreateClient();

        var live = await client.GetAsync("/health");
        live.StatusCode.Should().Be(HttpStatusCode.OK);
        var liveBody = await live.Content.ReadAsStringAsync();
        liveBody.Should().Contain("Healthy");
        liveBody.Should().NotContain("Password");

        var ready = await client.GetAsync("/health/ready");
        ready.StatusCode.Should().Be(HttpStatusCode.OK);
        var readyBody = await ready.Content.ReadAsStringAsync();
        readyBody.Should().Contain("Healthy");
        readyBody.Should().NotContain("Password");
        readyBody.Should().NotContain( "myvpn_test_password");
    }

    [SkippableFact]
    public async Task Auth_Me_Servers_Devices_Ownership_Flow()
    {
        RequireDocker();
        var client = _fixture.Factory!.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var register = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"user1_{suffix}@example.com",
            password = "CorrectHorseBatteryStaple!"
        });
        register.StatusCode.Should().Be(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"user1_{suffix}@example.com",
            password = "CorrectHorseBatteryStaple!"
        });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        tokens.Should().NotBeNull();

        var authClient = _fixture.Factory!.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var me = await authClient.GetAsync("/api/me");
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var meBody = await me.Content.ReadAsStringAsync();
        meBody.Should().Contain($"user1_{suffix}@example.com");
        meBody.Should().NotContain("PasswordHash");

        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        refresh.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = await refresh.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        rotated!.RefreshToken.Should().NotBe(tokens.RefreshToken);

        var replay = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = tokens.RefreshToken });
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = rotated.RefreshToken });
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var logoutAgain = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = rotated.RefreshToken });
        logoutAgain.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Re-login for device tests
        var login2 = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"user1_{suffix}@example.com",
            password = "CorrectHorseBatteryStaple!"
        });
        var tokens2 = await login2.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens2!.AccessToken);

        var serversAnon = await client.GetAsync("/api/servers");
        serversAnon.StatusCode.Should().Be(HttpStatusCode.OK);
        var serversBody = await serversAnon.Content.ReadAsStringAsync();
        serversBody.Should().Contain("Frankfurt");
        serversBody.Should().NotContain("VpnNetwork");
        serversBody.Should().NotContain("PublicKey");

        var createDevice = await authClient.PostAsJsonAsync("/api/devices", new
        {
            name = "My iPhone",
            platform = "iOS",
            publicKey = "ERERERERERERERERERERERERERERERERERERERERERE="
        });
        createDevice.StatusCode.Should().Be(HttpStatusCode.Created);
        var device = await createDevice.Content.ReadFromJsonAsync<DeviceResponse>(JsonOptions);
        device!.VpnAddress.Should().BeNull();

        var list = await authClient.GetAsync("/api/devices");
        list.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second user cannot delete first user's device
        var register2 = await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"user2_{suffix}@example.com",
            password = "CorrectHorseBatteryStaple!"
        });
        register2.EnsureSuccessStatusCode();
        var loginUser2 = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"user2_{suffix}@example.com",
            password = "CorrectHorseBatteryStaple!"
        });
        var tokensUser2 = await loginUser2.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        var client2 = _fixture.Factory!.CreateClient();
        client2.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokensUser2!.AccessToken);

        var foreignDelete = await client2.DeleteAsync($"/api/devices/{device.Id}");
        foreignDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var foreignList = await client2.GetAsync("/api/devices");
        var foreignDevices = await foreignList.Content.ReadFromJsonAsync<DevicesResponse>(JsonOptions);
        foreignDevices!.Devices.Should().BeEmpty();

        var deleteOwn = await authClient.DeleteAsync($"/api/devices/{device.Id}");
        deleteOwn.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var unauthorized = await client.GetAsync("/api/me");
        unauthorized.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [SkippableFact]
    public async Task Login_UnknownAndWrongPassword_SameErrorShape()
    {
        RequireDocker();
        var client = _fixture.Factory!.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        await client.PostAsJsonAsync("/api/auth/register", new
        {
            email = $"known_{suffix}@example.com",
            password = "CorrectHorseBatteryStaple!"
        });

        var unknown = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"missing_{suffix}@example.com",
            password = "CorrectHorseBatteryStaple!"
        });
        var wrong = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = $"known_{suffix}@example.com",
            password = "WrongPasswordValue!"
        });

        unknown.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        wrong.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var unknownBody = await unknown.Content.ReadAsStringAsync();
        var wrongBody = await wrong.Content.ReadAsStringAsync();
        unknownBody.Should().Contain("INVALID_CREDENTIALS");
        wrongBody.Should().Contain("INVALID_CREDENTIALS");
        unknownBody.Should().Contain("traceId");
    }

    [SkippableFact]
    public async Task DeleteAccount_ErasesUserAndAllowsReregister()
    {
        RequireDocker();
        var client = _fixture.Factory!.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"erase_{suffix}@example.com";
        const string password = "CorrectHorseBatteryStaple!";

        (await client.PostAsJsonAsync("/api/auth/register", new { email, password }))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);
        var authClient = _fixture.Factory!.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var createDevice = await authClient.PostAsJsonAsync("/api/devices", new
        {
            name = "To erase",
            platform = "Windows",
            publicKey = "ERERERERERERERERERERERERERERERERERERERERERE="
        });
        createDevice.StatusCode.Should().Be(HttpStatusCode.Created);

        var wrong = new HttpRequestMessage(HttpMethod.Delete, "/api/account")
        {
            Content = JsonContent.Create(new { password = "WrongPasswordValue!" })
        };
        var wrongResponse = await authClient.SendAsync(wrong);
        wrongResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await wrongResponse.Content.ReadAsStringAsync()).Should().Contain("INVALID_CREDENTIALS");

        var erase = new HttpRequestMessage(HttpMethod.Delete, "/api/account")
        {
            Content = JsonContent.Create(new { password })
        };
        var erased = await authClient.SendAsync(erase);
        erased.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var me = await authClient.GetAsync("/api/me");
        me.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var loginGone = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        loginGone.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var reregister = await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        reregister.StatusCode.Should().Be(HttpStatusCode.Created);
    }
}

