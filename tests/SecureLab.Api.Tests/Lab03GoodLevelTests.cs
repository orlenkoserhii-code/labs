using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Scaffolding;

namespace SecureLab.Api.Tests;

public sealed class Lab03GoodLevelTests(
    SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private readonly string _prefix =
        $"lr03good{Guid.NewGuid():N}";

    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException(
            "Set SeedUsers__Password outside Git.");

    private static WebApplicationFactoryClientOptions ClientOptions =>
        new()
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        };

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope =
            factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        await db.Incidents
            .Where(item => item.Title.StartsWith(_prefix))
            .ExecuteDeleteAsync();

        await db.Users
            .Where(user => user.UserName == _prefix)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task A03_ForeignHeader_DoesNotReplaceAlice()
    {
        using var client = await factory.LoginAsync(
            "alice", StudyPassword);

        using var request = new HttpRequestMessage(
            HttpMethod.Get, "/api/me");

        request.Headers.Add(
            "X-Demo-UserId",
            DbSeeder.BobId.ToString());

        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile = await response.Content
            .ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            DbSeeder.AliceId,
            profile.GetProperty("id").GetGuid());

        Assert.Equal(
            "alice",
            profile.GetProperty("userName").GetString());
    }

    [Fact]
    public async Task A04_CreateWithoutOwner_StoresBobAsOwner()
    {
        using var client = await factory.LoginAsync(
            "bob", StudyPassword);

        var title = _prefix + "-incident";

        using var response = await client.PostAsJsonAsync(
            "/api/incidents",
            new
            {
                title,
                description = "Навчальний опис інциденту.",
                severity = "Low",
                occurredAtUtc =
                    DateTimeOffset.UtcNow.AddMinutes(-1)
            });

        Assert.Equal(
            HttpStatusCode.Created,
            response.StatusCode);

        var body = await response.Content
            .ReadFromJsonAsync<JsonElement>();

        var id = body.GetProperty("id").GetGuid();

        await using var scope =
            factory.Services.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<SecureLabDbContext>();

        var stored = await db.Incidents
            .AsNoTracking()
            .SingleAsync(item => item.Id == id);

        Assert.Equal(title, stored.Title);
        Assert.Equal(DbSeeder.BobId, stored.OwnerUserId);
    }

    [Fact]
    public async Task A06_Logout_SameClientLosesAccess()
    {
        using var client = await factory.LoginAsync(
            "alice", StudyPassword);

        using var before = await client.GetAsync("/api/me");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var logout = await client.PostAsync(
            "/api/auth/logout", null);

        Assert.Equal(
            HttpStatusCode.NoContent,
            logout.StatusCode);

        using var after = await client.GetAsync("/api/me");

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            after.StatusCode);

        Assert.Null(after.Headers.Location);
        Assert.Empty(after.Headers.WwwAuthenticate);
    }

    [Fact]
    public async Task A07_UnknownUserAndWrongPassword_HaveSameFailure()
    {
        using var client = factory.CreateClient(ClientOptions);

        using var unknown = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "missing" + Guid.NewGuid().ToString("N"),
                password = StudyPassword
            });

        using var wrongPassword = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "alice",
                password = StudyPassword + "-wrong"
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            unknown.StatusCode);

        Assert.Equal(
            unknown.StatusCode,
            wrongPassword.StatusCode);

        Assert.Equal(
            "application/problem+json",
            unknown.Content.Headers.ContentType?.MediaType);

        Assert.Equal(
            unknown.Content.Headers.ContentType?.MediaType,
            wrongPassword.Content.Headers.ContentType?.MediaType);

        Assert.Equal(
            await StableErrorAsync(unknown),
            await StableErrorAsync(wrongPassword));
    }

    private static async Task<string> StableErrorAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content
            .ReadFromJsonAsync<JsonElement>();

        if (body.TryGetProperty("traceId", out var trace))
        {
            Assert.False(
                string.IsNullOrWhiteSpace(trace.GetString()));
        }

        var stable = body.EnumerateObject()
            .Where(property => !property.Name.Equals(
                "traceId",
                StringComparison.OrdinalIgnoreCase))
            .OrderBy(
                property => property.Name,
                StringComparer.Ordinal)
            .ToDictionary(
                property => property.Name,
                property => property.Value.Clone());

        return JsonSerializer.Serialize(stable);
    }

    [Fact]
    public async Task DuplicateRegistration_ReturnsSafe400()
    {
        using var client = factory.CreateClient(ClientOptions);

        var request = new
        {
            userName = _prefix,
            email = $"{_prefix}@example.test",
            displayName = "Тест реєстрації",
            password = StudyPassword
        };

        using var first = await client.PostAsJsonAsync(
            "/api/auth/register", request);

        Assert.Equal(
            HttpStatusCode.Created,
            first.StatusCode);

        var registered = await first.Content
            .ReadFromJsonAsync<JsonElement>();

        var fields = registered.EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "displayName", "id", "userName" },
            fields);

        using var duplicate = await client.PostAsJsonAsync(
            "/api/auth/register", request);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            duplicate.StatusCode);

        Assert.Equal(
            "application/json",
            duplicate.Content.Headers.ContentType?.MediaType);

        var failure = await duplicate.Content
            .ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            new[] { "message" },
            failure.EnumerateObject()
                .Select(property => property.Name)
                .ToArray());

        Assert.Equal(
            "Registration failed",
            failure.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CookieFlags_HttpsResponse_HasRequiredAttributes()
    {
        using var client = factory.CreateClient(ClientOptions);

        using var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "alice",
                password = StudyPassword
            });

        Assert.Equal(
            HttpStatusCode.NoContent,
            response.StatusCode);

        const string cookieName =
            ".AspNetCore.Identity.Application=";

        var matching = response.Headers.GetValues("Set-Cookie")
            .Where(value => value.StartsWith(
                cookieName,
                StringComparison.Ordinal))
            .ToArray();

        Assert.True(
            matching.Length == 1,
            "Expected one application cookie; values omitted.");

        var attributes = matching[0]
            .Split(';')
            .Skip(1)
            .Select(value => value.Trim().ToLowerInvariant())
            .ToArray();

        Assert.Contains("httponly", attributes);
        Assert.Contains("secure", attributes);
        Assert.Contains("samesite=lax", attributes);
    }

    [Fact]
    public async Task LoginRateLimit_SharesQuota_ThenRecovers()
    {
        using var host = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<LoginRateLimitOptions>(
                    options =>
                    {
                        options.PermitLimit = 2;
                        options.WindowSeconds = 3;
                    });
            }));

        using var first = host.CreateClient(ClientOptions);
        using var second = host.CreateClient(ClientOptions);

        using var successful = await first.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "alice",
                password = StudyPassword
            });

        Assert.Equal(
            HttpStatusCode.NoContent,
            successful.StatusCode);

        using var failed = await second.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "missing" + Guid.NewGuid().ToString("N"),
                password = StudyPassword
            });

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            failed.StatusCode);

        using var denied = await second.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "alice",
                password = StudyPassword
            });

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            denied.StatusCode);

        using var unknownDenied = await first.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "missing" + Guid.NewGuid().ToString("N"),
                password = StudyPassword
            });

        Assert.Equal(
            HttpStatusCode.TooManyRequests,
            unknownDenied.StatusCode);

        await Task.Delay(TimeSpan.FromMilliseconds(3300));

        using var recovered = await second.PostAsJsonAsync(
            "/api/auth/login",
            new
            {
                userName = "alice",
                password = StudyPassword
            });

        Assert.Equal(
            HttpStatusCode.NoContent,
            recovered.StatusCode);
    }
}