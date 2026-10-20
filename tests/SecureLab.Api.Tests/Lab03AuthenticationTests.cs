using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;

namespace SecureLab.Api.Tests;

public sealed class Lab03AuthenticationTests(
    SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string StudyPassword =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException(
            "Set SeedUsers__Password outside Git.");

    [Theory]
    [InlineData("/api/me")]
    [InlineData("/api/incidents")]
    public async Task A01_AnonymousRequest_Returns401(
        string endpoint)
    {
        using var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost"),
                AllowAutoRedirect = false,
                HandleCookies = false
            });

        using var request = new HttpRequestMessage(
            endpoint == "/api/me" ? HttpMethod.Get : HttpMethod.Post,
            endpoint);

        request.Headers.Add(
            "X-Demo-UserId",
            DbSeeder.AliceId.ToString());

        if (request.Method == HttpMethod.Post)
        {
            request.Content = JsonContent.Create(new
            {
                title = $"LR03-anonymous-{Guid.NewGuid():N}",
                description = "Навчальний опис інциденту.",
                severity = "Low",
                occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
            });
        }

        using var response = await client.SendAsync(request);

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            response.StatusCode);

        Assert.Null(response.Headers.Location);

        Assert.NotEqual(
            "text/html",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Empty(response.Headers.WwwAuthenticate);

        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A02_LoginThenMe_ReturnsVerifiedAlice()
    {
        using var client = await factory.LoginAsync(
            "alice",
            StudyPassword);

        using var response = await client.GetAsync("/api/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var profile =
            await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(
            DbSeeder.AliceId,
            profile.GetProperty("id").GetGuid());

        Assert.Equal(
            "alice",
            profile.GetProperty("userName").GetString());

        var properties = profile
            .EnumerateObject()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "id", "userName" },
            properties);
    }

    [Fact]
    public async Task A05_ForgedAliceOwner_StoresBobAsOwner()
    {
        using var client = await factory.LoginAsync(
            "bob",
            StudyPassword);

        var title = $"LR03-owner-{Guid.NewGuid():N}";

        try
        {
            using var response = await client.PostAsJsonAsync(
                "/api/incidents",
                new
                {
                    title,
                    description = "Навчальний опис інциденту.",
                    severity = "Low",
                    occurredAtUtc =
                        DateTimeOffset.UtcNow.AddMinutes(-1),

                    ownerUserId = DbSeeder.AliceId
                });

            Assert.Equal(
                HttpStatusCode.Created,
                response.StatusCode);

            var created =
                await response.Content.ReadFromJsonAsync<JsonElement>();

            var incidentId =
                created.GetProperty("id").GetGuid();

            await using var scope =
                factory.Services.CreateAsyncScope();

            var db = scope.ServiceProvider
                .GetRequiredService<SecureLabDbContext>();

            var stored = await db.Incidents
                .AsNoTracking()
                .SingleAsync(incident => incident.Id == incidentId);

            Assert.Equal(title, stored.Title);

            Assert.Equal(DbSeeder.BobId, stored.OwnerUserId);
        }
        finally
        {
            await using var scope =
                factory.Services.CreateAsyncScope();

            var db = scope.ServiceProvider
                .GetRequiredService<SecureLabDbContext>();

            await db.Incidents
                .Where(incident => incident.Title == title)
                .ExecuteDeleteAsync();
        }
    }
}