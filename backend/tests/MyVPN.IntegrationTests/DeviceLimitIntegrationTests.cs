using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MyVPN.Application.DTOs;
using MyVPN.Infrastructure.Persistence;
using Xunit;

namespace MyVPN.IntegrationTests;

[Collection("api")]
public sealed class DeviceLimitIntegrationTests
{
    private readonly ApiFixture _fixture;

    public DeviceLimitIntegrationTests(ApiFixture fixture) => _fixture = fixture;

    [SkippableFact]
    public async Task CreateDevices_Concurrent_DoesNotExceedFivePerUser()
    {
        Skip.If(!_fixture.DockerAvailable || _fixture.Factory is null, "Docker/Testcontainers PostgreSQL unavailable.");

        var client = _fixture.Factory!.CreateClient();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"limit_{suffix}@example.com";
        const string password = "CorrectHorseBatteryStaple!";

        await client.PostAsJsonAsync("/api/auth/register", new { email, password });
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var tokens = await login.Content.ReadFromJsonAsync<TokenResponse>(new System.Text.Json.JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        tokens.Should().NotBeNull();

        var authClient = _fixture.Factory.CreateClient();
        authClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens!.AccessToken);

        var tasks = Enumerable.Range(0, 10).Select(async i =>
        {
            var body = new
            {
                name = $"Device-{i}",
                platform = "Windows",
                publicKey = MakePublicKey(i)
            };
            var response = await authClient.PostAsJsonAsync("/api/devices", body);
            var text = await response.Content.ReadAsStringAsync();
            return (response.StatusCode, text);
        });

        var results = await Task.WhenAll(tasks);
        results.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(5);
        results.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(5);
        results.Where(r => r.StatusCode == HttpStatusCode.Conflict)
            .Should()
            .OnlyContain(r => r.text.Contains("DEVICE_LIMIT_REACHED", StringComparison.Ordinal));

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MyVpnDbContext>();
        var userId = await db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync();
        (await db.Devices.CountAsync(d => d.UserId == userId)).Should().Be(5);
    }

    private static string MakePublicKey(int seed)
    {
        var bytes = new byte[32];
        bytes[0] = (byte)seed;
        bytes[1] = (byte)(seed >> 8);
        bytes[2] = 0xAB;
        return Convert.ToBase64String(bytes);
    }
}
