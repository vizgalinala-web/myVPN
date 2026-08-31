using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MyVPN.Infrastructure.Persistence;

public sealed class MyVpnDbContextFactory : IDesignTimeDbContextFactory<MyVpnDbContext>
{
    public MyVpnDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "MyVPN.Api");
        if (!Directory.Exists(basePath))
        {
            basePath = Directory.GetCurrentDirectory();
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=myvpn;Username=myvpn;Password=myvpn_dev_password";

        var options = new DbContextOptionsBuilder<MyVpnDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new MyVpnDbContext(options);
    }
}
