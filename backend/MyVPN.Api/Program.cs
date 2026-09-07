using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MyVPN.Api.Configuration;
using MyVPN.Api.Middleware;
using MyVPN.Application;
using MyVPN.Application.Common;
using MyVPN.Application.Options;
using MyVPN.Infrastructure;
using MyVPN.Infrastructure.Persistence;
using MyVPN.Infrastructure.Persistence.Seed;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

ConfigurationGuard.Validate(builder.Configuration, builder.Environment);

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<MyVPN.Application.Abstractions.ICurrentUser, HttpCurrentUser>();

builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState
            .Where(kvp => kvp.Value?.Errors.Count > 0)
            .ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value!.Errors.Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value." : e.ErrorMessage).ToArray());

        var problem = new ProblemDetails
        {
            Type = "https://api.myvpn.example/errors/validation-failed",
            Title = "Validation failed",
            Status = StatusCodes.Status400BadRequest,
            Detail = "One or more validation errors occurred.",
            Extensions =
            {
                ["code"] = ErrorCodes.ValidationFailed,
                ["traceId"] = context.HttpContext.TraceIdentifier,
                ["errors"] = errors
            }
        };

        return new BadRequestObjectResult(problem);
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MyVPN API",
        Version = "v1",
        Description = "MyVPN Backend API"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("Jwt configuration is required.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub"
        };
    });

builder.Services.AddAuthorization();

var rateLimitOptions = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
    ?? new RateLimitOptions();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "https://api.myvpn.example/errors/rate-limited",
            title = "Too many requests",
            status = 429,
            code = ErrorCodes.RateLimited,
            detail = "Rate limit exceeded. Try again later.",
            traceId = context.HttpContext.TraceIdentifier
        };
        await context.HttpContext.Response.WriteAsync(JsonSerializer.Serialize(problem), token);
    };

    options.AddPolicy("auth-register", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.Register.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.Register.WindowSeconds),
                QueueLimit = 0
            }));

    options.AddPolicy("auth-login", httpContext =>
    {
        var ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var emailHint = httpContext.Request.Headers["X-RateLimit-Email"].FirstOrDefault() ?? string.Empty;
        var key = string.IsNullOrWhiteSpace(emailHint)
            ? ip
            : $"{ip}:{emailHint.Trim().ToLowerInvariant()}";

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: key,
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.Login.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.Login.WindowSeconds),
                QueueLimit = 0
            });
    });

    options.AddPolicy("auth-refresh", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.Refresh.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.Refresh.WindowSeconds),
                QueueLimit = 0
            }));

    options.AddPolicy("auth-logout", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.Logout.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.Logout.WindowSeconds),
                QueueLimit = 0
            }));

    options.AddPolicy("device-configuration", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User?.FindFirst("sub")?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.DeviceConfiguration.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.DeviceConfiguration.WindowSeconds),
                QueueLimit = 0
            }));

    options.AddPolicy("device-disconnect", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User?.FindFirst("sub")?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.DeviceDisconnect.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.DeviceDisconnect.WindowSeconds),
                QueueLimit = 0
            }));

    options.AddPolicy("account-delete", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.User?.FindFirst("sub")?.Value
                ?? httpContext.Connection.RemoteIpAddress?.ToString()
                ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = rateLimitOptions.AccountDelete.PermitLimit,
                Window = TimeSpan.FromSeconds(rateLimitOptions.AccountDelete.WindowSeconds),
                QueueLimit = 0
            }));
});

var corsOptions = builder.Configuration.GetSection(CorsOptions.SectionName).Get<CorsOptions>() ?? new CorsOptions();
builder.Services.AddCors(options =>
{
    options.AddPolicy("ConfiguredCors", policy =>
    {
        if (corsOptions.AllowedOrigins.Length == 0)
        {
            // No browser clients configured — deny cross-origin by default.
            policy.SetIsOriginAllowed(_ => false);
            return;
        }

        policy.WithOrigins(corsOptions.AllowedOrigins)
            .AllowAnyHeader()
            .WithMethods("GET", "POST", "DELETE", "OPTIONS");
    });
});

builder.Services.AddHealthChecks()
    .AddNpgSql(
        builder.Configuration.GetConnectionString("Default")!,
        name: "postgresql",
        tags: new[] { "ready" });

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.TimestampFormat = "O";
});

var app = builder.Build();

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<RequestLoggingMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

if (corsOptions.AllowedOrigins.Length > 0)
{
    app.UseCors("ConfiguredCors");
}

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = async (context, _) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { status = "Healthy" }));
    }
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var payload = new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString()
            })
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
});

app.MapControllers();

if (app.Environment.IsDevelopment())
{
    // Non-destructive: apply pending EF migrations only in Development.
    // Production must apply migrations via explicit release/ops process.
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<MyVpnDbContext>();
        await db.Database.MigrateAsync();
    }

    await DevelopmentVpnServerSeeder.SeedAsync(app.Services, app.Environment);
}

app.Run();

public partial class Program;
