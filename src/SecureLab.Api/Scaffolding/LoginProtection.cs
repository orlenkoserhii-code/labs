using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace SecureLab.Api.Scaffolding;

public sealed class LoginRateLimitOptions
{
    public int PermitLimit { get; set; } = 5;
    public int WindowSeconds { get; set; } = 60;
}

public static class LoginProtection
{
    public const string PolicyName = "auth-login";

    public static void AddLoginProtection(
        this WebApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<LoginRateLimitOptions>()
            .Bind(builder.Configuration.GetSection("LoginRateLimit"))
            .Validate(
                options =>
                    options.PermitLimit > 0 &&
                    options.WindowSeconds > 0,
                "Login rate-limit settings must be positive.")
            .ValidateOnStart();

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode =
                StatusCodes.Status429TooManyRequests;

            options.AddPolicy(PolicyName, context =>
            {
                var settings = context.RequestServices
                    .GetRequiredService<IOptions<LoginRateLimitOptions>>()
                    .Value;

                // Не довіряємо довільним X-Forwarded-For заголовкам.
                var key = context.Connection.RemoteIpAddress?.ToString()
                    ?? "unknown";

                return RateLimitPartition.GetFixedWindowLimiter(
                    key,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = settings.PermitLimit,
                        Window = TimeSpan.FromSeconds(
                            settings.WindowSeconds),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });

            options.OnRejected = async (context, _) =>
            {
                Record(context.HttpContext, "login", "limited");

                await Results.Problem(
                    title: "Too many login attempts",
                    statusCode: StatusCodes.Status429TooManyRequests)
                    .ExecuteAsync(context.HttpContext);
            };
        });
    }

    public static void Record(
        HttpContext context,
        string eventType,
        string outcome)
    {
        var logger = context.RequestServices
            .GetRequiredService<ILoggerFactory>()
            .CreateLogger("SecureLab.Authentication");

        logger.LogInformation(
            "Authentication {EventType}: {Outcome}; trace {TraceId}",
            eventType,
            outcome,
            context.TraceIdentifier);
    }
}