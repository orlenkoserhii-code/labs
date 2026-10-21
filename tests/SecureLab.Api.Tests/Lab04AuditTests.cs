using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SecureLab.Api.Data;
using Xunit.Abstractions;

namespace SecureLab.Api.Tests;

public sealed class Lab04AuditTests(SecureLabApiFactory factory, ITestOutputHelper output)
    : IClassFixture<SecureLabApiFactory>
{
    [Fact]
    public async Task A09_DeniedTriage_RecordsSafeEventWithResponseTrace()
    {
        var password = Environment.GetEnvironmentVariable("SeedUsers__Password")
            ?? throw new InvalidOperationException("Set SeedUsers__Password outside Git.");
        using var capture = new AuditCapture();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddSingleton<ILoggerProvider>(capture)));
        using var client = host.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var before = await db.Incidents.AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.AliceIncidentId);
        using var response = await client.PostAsync(
            $"/api/incidents/{DbSeeder.AliceIncidentId}/triage", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var trace = body.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(trace));

        var audit = Assert.Single(capture.Events, x => Equals(x["TraceId"], trace));
        Assert.Equal("authorization.denied", audit["EventName"]);
        Assert.Equal(DbSeeder.AliceId, audit["SubjectId"]);
        Assert.Equal(DbSeeder.AliceIncidentId, audit["ResourceId"]);
        Assert.Equal("triage", audit["Action"]);
        Assert.Equal("denied", audit["Result"]);
        Assert.IsType<DateTimeOffset>(audit["OccurredAtUtc"]);
        var allowed = new[] { "EventName", "SubjectId", "ResourceId", "Action",
            "Result", "OccurredAtUtc", "TraceId" };
        Assert.All(audit.Keys, key => Assert.Contains(key, allowed));
        var after = await db.Incidents.AsNoTracking()
            .SingleAsync(x => x.Id == DbSeeder.AliceIncidentId);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.UpdatedAtUtc, after.UpdatedAtUtc);
        output.WriteLine("LR04_AUDIT " + JsonSerializer.Serialize(audit));
    }

    private sealed class AuditCapture : ILoggerProvider
    {
        public ConcurrentQueue<Dictionary<string, object?>> Events { get; } = new();
        public ILogger CreateLogger(string categoryName) =>
            categoryName == "SecureLab.Authorization" ? new AuditLogger(this) : NullLogger.Instance;
        public void Dispose() { }

        private sealed class AuditLogger(AuditCapture capture) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel level) => true;
            public void Log<TState>(LogLevel level, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (state is not IEnumerable<KeyValuePair<string, object?>> fields) return;
                var data = fields.Where(x => x.Key != "{OriginalFormat}")
                    .ToDictionary(x => x.Key, x => x.Value);
                if (data.TryGetValue("EventName", out var name)
                    && Equals(name, "authorization.denied")) capture.Events.Enqueue(data);
            }
        }
    }
}
