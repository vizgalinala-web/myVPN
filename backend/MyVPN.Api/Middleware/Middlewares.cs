using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using MyVPN.Application.Common;

namespace MyVPN.Api.Middleware;

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            _logger.LogWarning(
                "Application error Code={Code} Status={Status} TraceId={TraceId} Path={Path}",
                ex.Code,
                ex.Status,
                context.TraceIdentifier,
                context.Request.Path);

            await WriteProblemAsync(context, ex.Status, ex.Type, ex.Title, ex.Message, ex.Code, ex.Errors);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception TraceId={TraceId} Path={Path}", context.TraceIdentifier, context.Request.Path);

            var detail = _environment.IsDevelopment()
                ? "An unexpected error occurred."
                : "An unexpected error occurred.";

            await WriteProblemAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "https://api.myvpn.example/errors/internal",
                "Internal server error",
                detail,
                "INTERNAL_ERROR");
        }
    }

    private static async Task WriteProblemAsync(
        HttpContext context,
        int status,
        string type,
        string title,
        string detail,
        string code,
        IDictionary<string, string[]>? errors = null)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        var problem = new ProblemDetails
        {
            Type = type,
            Title = title,
            Status = status,
            Detail = detail,
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = context.TraceIdentifier
            }
        };

        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }));
    }
}

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["X-XSS-Protection"] = "0";
            headers["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });

        await _next(context);
    }
}

public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var started = DateTimeOffset.UtcNow;
        try
        {
            await _next(context);
        }
        finally
        {
            var durationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds;
            var userId = context.User?.FindFirst("sub")?.Value;
            _logger.LogInformation(
                "HTTP {Method} {Path} => {StatusCode} in {DurationMs}ms TraceId={TraceId} UserId={UserId}",
                context.Request.Method,
                context.Request.Path.Value,
                context.Response.StatusCode,
                (int)durationMs,
                context.TraceIdentifier,
                userId);
        }
    }
}
