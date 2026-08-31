using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MyVPN.Api.Configuration;

namespace MyVPN.UnitTests.Authentication;

public sealed class ConfigurationGuardTests
{
    [Fact]
    public void Production_RejectsPlaceholderSigningKey()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=localhost;Database=x;Username=x;Password=x",
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience",
            ["Jwt:SigningKey"] = "DEV_ONLY_CHANGE_ME_32chars_min_key!!"
        }).Build();

        var act = () => ConfigurationGuard.Validate(config, new FakeEnv(Environments.Production));
        act.Should().Throw<InvalidOperationException>().WithMessage("*placeholder*");
    }

    [Fact]
    public void Production_RejectsMissingConnectionString()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "issuer",
            ["Jwt:Audience"] = "audience",
            ["Jwt:SigningKey"] = "PRODUCTION_SAFE_SIGNING_KEY_VALUE_32_CHARS_OK"
        }).Build();

        var act = () => ConfigurationGuard.Validate(config, new FakeEnv(Environments.Production));
        act.Should().Throw<InvalidOperationException>().WithMessage("*ConnectionStrings__Default*");
    }

    private sealed class FakeEnv(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "MyVPN.Api";
        public string ContentRootPath { get; set; } = "/tmp";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
