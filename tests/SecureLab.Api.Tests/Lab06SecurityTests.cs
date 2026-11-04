using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Scaffolding;
using Xunit.Abstractions;

namespace SecureLab.Api.Tests;

public sealed class Lab06SecurityTests(SecureLabApiFactory factory, ITestOutputHelper output)
    : IClassFixture<SecureLabApiFactory>
{
    private static string Password => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set SeedUsers__Password outside the repository.");

    private static WebApplicationFactoryClientOptions ClientOptions => new()
    {
        BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = true
    };

    private async Task<Guid> CreateFixtureAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var item = new Incident
        {
            Id = Guid.NewGuid(), OwnerUserId = DbSeeder.AliceId,
            Title = "LR06-" + Guid.NewGuid(), Description = "Original study description",
            Severity = IncidentSeverity.Low, Status = IncidentStatus.New,
            OccurredAtUtc = DateTimeOffset.UtcNow, CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        db.Incidents.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    private async Task RemoveFixtureAsync(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>()
            .Incidents.Where(item => item.Id == id).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task REG_CSRF_01_DenialPreservesState_ValidPairAllowsUpdate()
    {
        using var client = await factory.LoginAsync("alice", Password, withAntiforgery: false);
        var id = await CreateFixtureAsync();
        var path = $"/api/incidents/{id}";
        try
        {
            var before = await client.GetStringAsync(path);
            var change = new { title = "LR06-updated-" + id, description = "Updated study description" };
            using var missing = await client.PutAsJsonAsync(path, change);
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
            Assert.Equal(before, await client.GetStringAsync(path));

            client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", "invalid-study-token");
            using var invalid = await client.PutAsJsonAsync(path, change);
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            Assert.Equal(before, await client.GetStringAsync(path));

            await client.UseAntiforgeryTokenAsync();
            using var allowed = await client.PutAsJsonAsync(path, change);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
            var after = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(change.description, after.GetProperty("description").GetString());
            Assert.Equal(change.title, after.GetProperty("title").GetString());
            output.WriteLine(JsonSerializer.Serialize(new
            {
                scenario = "REG-CSRF-01", missing = (int)missing.StatusCode,
                invalid = (int)invalid.StatusCode, valid = (int)allowed.StatusCode,
                unchangedAfterDenial = true, changedAfterSuccess = true
            }));
        }
        finally { await RemoveFixtureAsync(id); }
    }

    [Fact]
    public async Task REG_HEADERS_01_ProductionErrorRetainsHeadersAndTrace()
    {
        using var capture = new LogCapture();
        using var host = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Database:ApplyMigrationsOnStartup", "false");
            builder.UseSetting("ConnectionStrings:SecureLab",
                "Host=127.0.0.1;Port=54399;Database=securelab;Username=securelab;"
                + "Password=unreachable-study-value;Timeout=1;Include Error Detail=false");
            builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(capture));
        });
        Assert.Equal("Production", host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var client = host.CreateClient(ClientOptions);
        using var response = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = "NotARealPassword1!" });
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var trace = body.GetProperty("traceId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(trace));
        Assert.Contains(capture.Entries, entry => entry.Trace == trace
            && entry.Category == "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware");
        Assert.All(body.EnumerateObject(), property =>
            Assert.Contains(property.Name, new[] { "type", "title", "status", "traceId" }));
        AssertHeaders(response);
        Assert.DoesNotContain(capture.Entries, entry =>
            entry.Text.Contains("NotARealPassword1!", StringComparison.Ordinal)
            || entry.Text.Contains("unreachable-study-value", StringComparison.Ordinal));
        output.WriteLine(JsonSerializer.Serialize(new
        {
            scenario = "REG-HEADERS-01", environment = "Production",
            status = (int)response.StatusCode, responseTrace = trace,
            logTrace = capture.Entries.First(entry => entry.Trace == trace
                && entry.Category == "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware").Trace,
            sensitiveMatches = 0, securityHeadersPresent = true
        }));
    }

    private static void AssertHeaders(HttpResponseMessage response)
    {
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal("no-referrer", Assert.Single(response.Headers.GetValues("Referrer-Policy")));
        Assert.Contains("frame-ancestors 'none'",
            Assert.Single(response.Headers.GetValues("Content-Security-Policy")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POST", "/api/incidents", "alice")]
    [InlineData("DELETE", "/api/incidents/{id}", "alice")]
    [InlineData("POST", "/api/incidents/{id}/triage", "morgan")]
    [InlineData("POST", "/api/auth/logout", "alice")]
    public async Task CSRF_Perimeter_DenialPreservesDatabaseAndSession(
        string method, string route, string user)
    {
        using var client = await factory.LoginAsync(user, Password, withAntiforgery: false);
        var id = await CreateFixtureAsync();
        try
        {
            var before = await client.GetStringAsync($"/api/incidents/{id}");
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var count = await db.Incidents.CountAsync();
            using var request = new HttpRequestMessage(new HttpMethod(method), route.Replace("{id}", id.ToString()));
            request.Content = JsonContent.Create(new
            {
                title = "LR06-missing-" + id, description = "Study description", severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow
            });
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, await client.GetStringAsync($"/api/incidents/{id}"));
            Assert.Equal(count, await db.Incidents.CountAsync());
            using var me = await client.GetAsync("/api/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
        finally { await RemoveFixtureAsync(id); }
    }

    [Fact]
    public async Task CSRF_IdentityChange_RejectsOldTokenAndAcceptsFreshToken()
    {
        using var client = await factory.LoginAsync("alice", Password);
        using var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var login = await client.PostAsJsonAsync("/api/auth/login",
            new { userName = "bob", password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        using var stale = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.BadRequest, stale.StatusCode);
        using var stillSignedIn = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, stillSignedIn.StatusCode);
        await client.UseAntiforgeryTokenAsync();
        using var fresh = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, fresh.StatusCode);
        using var after = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task SearchLimiter_IsolatesTwoSubjects_RejectsThenRecovers()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.PostConfigure<SearchRateLimitOptions>(options =>
                {
                    options.PermitLimit = 2;
                    options.WindowSeconds = 3;
                })));
        using var alice = await host.LoginAsync("alice", Password);
        using var bob = await host.LoginAsync("bob", Password);
        const string search = "/api/incidents/search?q=навчальн&sortBy=createdAtUtc";
        using var first = await alice.GetAsync(search);
        using var second = await alice.GetAsync(search);
        using var limited = await alice.GetAsync(search);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var independent = await bob.GetAsync(search);
        Assert.Equal(HttpStatusCode.OK, independent.StatusCode);
        alice.DefaultRequestHeaders.Add("X-Forwarded-For", "192.0.2.10");
        using var spoof = await alice.GetAsync(search);
        Assert.Equal(HttpStatusCode.TooManyRequests, spoof.StatusCode);
        using var unauthorizedResource = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        Assert.Equal(HttpStatusCode.NotFound, unauthorizedResource.StatusCode);
        await Task.Delay(TimeSpan.FromMilliseconds(3300));
        using var recovered = await alice.GetAsync(search);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Theory]
    [InlineData("http://localhost:4173", true)]
    [InlineData("http://localhost:4174", false)]
    public async Task REG_CORS_01_ExactOriginAndPreflight(string origin, bool allowed)
    {
        using var client = await factory.LoginAsync("alice", Password);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("Origin", origin);
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal(origin, Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
            Assert.Equal("true", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Credentials")));
        }
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/me");
        preflight.Headers.Add("Origin", origin);
        preflight.Headers.Add("Access-Control-Request-Method", "GET");
        preflight.Headers.Add("Access-Control-Request-Headers", "X-Lab-Cors-Probe");
        using var options = await client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, options.StatusCode);
        Assert.Equal(allowed, options.Headers.Contains("Access-Control-Allow-Origin"));
        if (allowed)
        {
            Assert.Equal("GET", Assert.Single(options.Headers.GetValues("Access-Control-Allow-Methods")));
            Assert.Equal("X-Lab-Cors-Probe", Assert.Single(options.Headers.GetValues("Access-Control-Allow-Headers")));
        }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public async Task HeadersAndCookies_NormalAndDeniedResponses(string environment)
    {
        using var host = factory.WithWebHostBuilder(builder => builder.UseEnvironment(environment));
        Assert.Equal(environment, host.Services.GetRequiredService<IHostEnvironment>().EnvironmentName);
        using var anonymous = host.CreateClient(ClientOptions);
        foreach (var route in new[] { "/", "/auth-lab.html", "/browser-lab.html", "/csrf.js" })
        {
            using var page = await anonymous.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            AssertHeaders(page);
        }
        using var denied = await anonymous.GetAsync("/api/auth/csrf-token");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        AssertHeaders(denied);
        using var login = await anonymous.PostAsJsonAsync("/api/auth/login",
            new { userName = "alice", password = Password });
        Assert.Equal(HttpStatusCode.NoContent, login.StatusCode);
        AssertCookieAttributes(login, "samesite=lax");
        using var token = await anonymous.GetAsync("/api/auth/csrf-token");
        Assert.Equal(HttpStatusCode.OK, token.StatusCode);
        Assert.True(token.Headers.CacheControl?.NoStore);
        AssertCookieAttributes(token, "samesite=strict");
        using var invalid = await anonymous.GetAsync("/api/incidents?status=invalid");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        AssertHeaders(invalid);
        using var missing = await anonymous.GetAsync($"/api/incidents/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        AssertHeaders(missing);
        using var normal = await anonymous.GetAsync("/api/incidents");
        Assert.Equal(HttpStatusCode.OK, normal.StatusCode);
        AssertHeaders(normal);
        if (environment == "Production")
        {
            using var cors = new HttpRequestMessage(HttpMethod.Get, "/api/me");
            cors.Headers.Add("Origin", "http://localhost:4173");
            using var response = await anonymous.SendAsync(cors);
            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }
    }

    private static void AssertCookieAttributes(HttpResponseMessage response, string sameSite)
    {
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        var attributes = cookie.Split(';').Skip(1).Select(value => value.Trim().ToLowerInvariant()).ToArray();
        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains(sameSite, attributes);
    }

    [Fact]
    public async Task Audit_AllowedAndDeniedTriage_ContainsOnlyRequiredFields()
    {
        using var capture = new LogCapture();
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILoggerProvider>(capture)));
        using var alice = await host.LoginAsync("alice", Password);
        using var morgan = await host.LoginAsync("morgan", Password);
        var id = await CreateFixtureAsync();
        try
        {
            var before = await alice.GetStringAsync($"/api/incidents/{id}");
            using var denied = await alice.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            Assert.Equal(before, await alice.GetStringAsync($"/api/incidents/{id}"));
            using var allowed = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
            var after = await alice.GetFromJsonAsync<JsonElement>($"/api/incidents/{id}");
            Assert.Equal("Triaged", after.GetProperty("status").GetString());
            foreach (var result in new[] { "denied", "allowed" })
            {
                var entry = Assert.Single(capture.Entries, item =>
                    item.Category == "SecureLab.Authorization"
                    && item.Fields.TryGetValue("Result", out var value) && Equals(value, result));
                Assert.Equal(id, entry.Fields["ResourceId"]);
                Assert.Equal(result == "denied" ? DbSeeder.AliceId : DbSeeder.MorganId, entry.Fields["SubjectId"]);
                Assert.Equal("triage", entry.Fields["Action"]);
                Assert.IsType<DateTimeOffset>(entry.Fields["OccurredAtUtc"]);
                Assert.All(entry.Fields.Keys, key => Assert.Contains(key,
                    new[] { "EventName", "SubjectId", "Action", "ResourceId", "Result", "OccurredAtUtc", "TraceId" }));
                output.WriteLine(JsonSerializer.Serialize(entry.Fields));
            }
            using var invalid = await alice.PutAsJsonAsync($"/api/incidents/{id}", new { title = "", description = "" });
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using var failedLogin = await alice.PostAsJsonAsync("/api/auth/login",
                new { userName = "alice", password = "NotARealPassword1!" });
            Assert.Equal(HttpStatusCode.Unauthorized, failedLogin.StatusCode);
            Assert.DoesNotContain(capture.Entries, entry => entry.Text.Contains(Password, StringComparison.Ordinal)
                || entry.Text.Contains("NotARealPassword1!", StringComparison.Ordinal)
                || entry.Text.Contains("X-CSRF-TOKEN", StringComparison.OrdinalIgnoreCase)
                || entry.Text.Contains("Set-Cookie", StringComparison.OrdinalIgnoreCase));
            output.WriteLine("LOG-REVIEW login-success, login-failure, denied-action, validation, privileged-transition: sensitive matches 0.");
        }
        finally { await RemoveFixtureAsync(id); }
    }

    private sealed record LogEntry(string Category, string? Trace, string Text,
        IReadOnlyDictionary<string, object?> Fields);

    private sealed class LogCapture : ILoggerProvider
    {
        public ConcurrentQueue<LogEntry> Entries { get; } = new();
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(this, categoryName);
        public void Dispose() { }

        private sealed class CaptureLogger(LogCapture capture, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var fields = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values.Where(value => value.Key != "{OriginalFormat}")
                        .ToDictionary(value => value.Key, value => value.Value)
                    : new Dictionary<string, object?>();
                capture.Entries.Enqueue(new(category, Activity.Current?.Id,
                    formatter(state, exception) + exception?.ToString(), fields));
            }
        }
    }
}
