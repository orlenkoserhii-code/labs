using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class Lab04AuthorizationTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>
{
    private static string Password => Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set SeedUsers__Password outside Git.");
    private static Guid InternalId => Guid.Parse("30000000-0000-0000-0000-000000000003");
    private Task<HttpClient> Login(string name) => factory.LoginAsync(name, Password);

    private sealed record State(Guid Owner, string Title, string Description,
        IncidentSeverity Severity, IncidentStatus Status, DateTimeOffset Updated);

    private async Task<State?> Snapshot(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var x = await db.Incidents.AsNoTracking().SingleOrDefaultAsync(i => i.Id == id);
        return x is null ? null : new(x.OwnerUserId, x.Title, x.Description,
            x.Severity, x.Status, x.UpdatedAtUtc);
    }

    private async Task<Guid> Create(HttpClient owner)
    {
        using var response = await owner.PostAsJsonAsync("/api/incidents", new
        {
            title = "LR04-" + Guid.NewGuid().ToString("N"),
            description = "Навчальний інцидент для перевірки авторизації.",
            severity = "Low", occurredAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1)
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task Cleanup(Guid id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>()
            .Incidents.Where(x => x.Id == id).ExecuteDeleteAsync();
    }

    [Theory]
    [InlineData("GET", "/api/incidents")]
    [InlineData("GET", "/api/incidents/20000000-0000-0000-0000-000000000001")]
    [InlineData("PUT", "/api/incidents/20000000-0000-0000-0000-000000000001")]
    [InlineData("DELETE", "/api/incidents/20000000-0000-0000-0000-000000000001")]
    [InlineData("POST", "/api/incidents/20000000-0000-0000-0000-000000000001/triage")]
    [InlineData("GET", "/api/incident-comments/30000000-0000-0000-0000-000000000001")]
    public async Task A01_AnonymousGets401(string method, string path)
    {
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        { BaseAddress = new Uri("https://localhost"), AllowAutoRedirect = false, HandleCookies = false });
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT") request.Content = JsonContent.Create(new { title = "test", description = "test" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.NotEqual("text/html", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A02_OwnerReadsUpdatesAndDeletesOwnNewIncident()
    {
        using var alice = await Login("alice");
        var id = await Create(alice);
        try
        {
            using var read = await alice.GetAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            using var update = await alice.PutAsJsonAsync($"/api/incidents/{id}",
                new { title = "LR04-edit-" + Guid.NewGuid(), description = "Змінений власний опис." });
            Assert.Equal(HttpStatusCode.NoContent, update.StatusCode);
            Assert.Equal("Змінений власний опис.", (await Snapshot(id))!.Description);
            using var delete = await alice.DeleteAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
            Assert.Null(await Snapshot(id));
        }
        finally { await Cleanup(id); }
    }

    [Fact]
    public async Task A03_NonOwnerReadDoesNotExposeForeignDetails()
    {
        using var alice = await Login("alice");
        using var response = await alice.GetAsync($"/api/incidents/{DbSeeder.BobIncidentId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("id", out _));
        Assert.False(body.TryGetProperty("description", out _));
        Assert.False(body.TryGetProperty("comments", out _));
    }

    [Fact]
    public async Task A03R_AnalystReadsAliceDetails()
    {
        using var morgan = await Login("morgan");
        using var response = await morgan.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(DbSeeder.AliceIncidentId, body.GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task A04_ForeignUpdateAndDeleteDoNotChangeState()
    {
        using var alice = await Login("alice");
        var id = DbSeeder.BobIncidentId;
        var before = await Snapshot(id);
        using var update = await alice.PutAsJsonAsync($"/api/incidents/{id}",
            new { title = "forbidden", description = "Чужі дані змінювати не можна." });
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        Assert.Equal(before, await Snapshot(id));
        using var delete = await alice.DeleteAsync($"/api/incidents/{id}");
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Equal(before, await Snapshot(id));
    }

    [Fact]
    public async Task A05_ListContainsOnlyAllowedOwners()
    {
        using var alice = await Login("alice");
        var list = await alice.GetFromJsonAsync<JsonElement>("/api/incidents");
        var ids = list.EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
        await using var scope = factory.Services.CreateAsyncScope();
        var allowed = await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>()
            .Incidents.Where(x => x.OwnerUserId == DbSeeder.AliceId).Select(x => x.Id).ToListAsync();
        Assert.Contains(DbSeeder.AliceIncidentId, ids);
        Assert.All(ids, id => Assert.Contains(id, allowed));
        Assert.DoesNotContain(DbSeeder.BobIncidentId, ids);
    }

    [Fact]
    public async Task A06_ReporterCannotTriage_AndStateIsUnchanged()
    {
        using var alice = await Login("alice");
        var id = await Create(alice);
        try
        {
            var before = await Snapshot(id);
            using var response = await alice.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            Assert.Equal(before, await Snapshot(id));
        }
        finally { await Cleanup(id); }
    }

    [Fact]
    public async Task A07_AnalystTriagesOnlyNew_WithoutContentUpdateRights()
    {
        using var alice = await Login("alice");
        using var morgan = await Login("morgan");
        var id = await Create(alice);
        try
        {
            var before = await Snapshot(id);
            using var response = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            var after = await Snapshot(id);
            Assert.Equal(IncidentStatus.Triaged, after!.Status);
            Assert.Equal(before!.Owner, after.Owner);
            Assert.Equal(before.Description, after.Description);
            using var repeat = await morgan.PostAsync($"/api/incidents/{id}/triage", null);
            Assert.Equal(HttpStatusCode.Forbidden, repeat.StatusCode);
            Assert.Equal(after, await Snapshot(id));
            using var edit = await morgan.PutAsJsonAsync($"/api/incidents/{id}",
                new { title = "forbidden", description = "Аналітик не редагує довільний вміст." });
            Assert.Equal(HttpStatusCode.Forbidden, edit.StatusCode);
            Assert.Equal(after, await Snapshot(id));
            using var delete = await morgan.DeleteAsync($"/api/incidents/{id}");
            Assert.Equal(HttpStatusCode.Forbidden, delete.StatusCode);
            Assert.Equal(after, await Snapshot(id));
            await using var scope = factory.Services.CreateAsyncScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<SecureLabDbContext>()
                .IncidentStatusHistory.CountAsync(x => x.IncidentId == id
                    && x.ChangedByUserId == DbSeeder.MorganId
                    && x.OldStatus == IncidentStatus.New && x.NewStatus == IncidentStatus.Triaged));
        }
        finally { await Cleanup(id); }
    }

    [Theory]
    [InlineData("ownerUserId", "10000000-0000-0000-0000-000000000002")]
    [InlineData("role", "Analyst")]
    [InlineData("status", "Triaged")]
    [InlineData("severity", "Critical")]
    [InlineData("id", "20000000-0000-0000-0000-000000000002")]
    [InlineData("createdAtUtc", "2026-01-01T00:00:00Z")]
    public async Task A08b_OverpostingIsRejected_ProtectedStateAndRolesUnchanged(string field, string value)
    {
        using var alice = await Login("alice");
        var id = await Create(alice);
        try
        {
            var before = await Snapshot(id);
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
            var rolesBefore = await db.UserRoles.Where(x => x.UserId == DbSeeder.AliceId)
                .OrderBy(x => x.RoleId).Select(x => x.RoleId).ToArrayAsync();
            using var response = await alice.PutAsJsonAsync($"/api/incidents/{id}",
                new Dictionary<string, string> { ["title"] = "forged", ["description"] = "forged",
                    [field] = value });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(before, await Snapshot(id));
            var rolesAfter = await db.UserRoles.Where(x => x.UserId == DbSeeder.AliceId)
                .OrderBy(x => x.RoleId).Select(x => x.RoleId).ToArrayAsync();
            Assert.Equal(rolesBefore, rolesAfter);
        }
        finally { await Cleanup(id); }
    }

    [Theory]
    [InlineData("GET", false)]
    [InlineData("PUT", false)]
    [InlineData("DELETE", false)]
    [InlineData("POST", false)]
    [InlineData("GET", true)]
    public async Task A10_ForeignAndMissingHaveSame404(string method, bool child)
    {
        using var alice = await Login("alice");
        var prefix = child ? "/api/incident-comments/" : "/api/incidents/";
        var foreignId = child ? Guid.Parse("30000000-0000-0000-0000-000000000002") : DbSeeder.BobIncidentId;
        var suffix = method == "POST" ? "/triage" : "";
        async Task<HttpResponseMessage> Send(Guid id)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), prefix + id + suffix);
            if (method == "PUT") request.Content = JsonContent.Create(new { title = "test", description = "test" });
            return await alice.SendAsync(request);
        }
        using var foreign = await Send(foreignId);
        using var missing = await Send(Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(foreign.StatusCode, missing.StatusCode);
        Assert.Equal(foreign.Content.Headers.ContentType?.ToString(),
            missing.Content.Headers.ContentType?.ToString());
        var a = await foreign.Content.ReadFromJsonAsync<JsonObject>();
        var b = await missing.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(a); Assert.NotNull(b);
        foreach (var key in new[] { "traceId", "requestId" }) { a.Remove(key); b.Remove(key); }
        Assert.True(JsonNode.DeepEquals(a, b));
    }

    [Fact]
    public async Task A12_ChildAndInternalVisibilityFollowParentPolicy()
    {
        using var alice = await Login("alice");
        using var morgan = await Login("morgan");
        using var foreign = await alice.GetAsync("/api/incident-comments/30000000-0000-0000-0000-000000000002");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var hidden = await alice.GetAsync($"/api/incident-comments/{InternalId}");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var allowed = await morgan.GetAsync($"/api/incident-comments/{InternalId}");
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var reporterDetails = await alice.GetFromJsonAsync<JsonElement>($"/api/incidents/{DbSeeder.AliceIncidentId}");
        var analystDetails = await morgan.GetFromJsonAsync<JsonElement>($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.DoesNotContain(reporterDetails.GetProperty("comments").EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == InternalId);
        Assert.Contains(analystDetails.GetProperty("comments").EnumerateArray(),
            x => x.GetProperty("id").GetGuid() == InternalId);
    }

    [Fact]
    public async Task BobCannotReadAlice_AndFakeHeaderDoesNotChangeSubject()
    {
        using var bob = await Login("bob");
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"/api/incidents/{DbSeeder.AliceIncidentId}");
        request.Headers.Add("X-Demo-UserId", DbSeeder.AliceId.ToString());
        using var response = await bob.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdministratorDoesNotBypassForeignReadPolicy()
    {
        using var admin = await Login("admin");
        using var response = await admin.GetAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OwnerCannotDeleteNonNewIncident_AndStateDoesNotChange()
    {
        using var alice = await Login("alice");
        var before = await Snapshot(DbSeeder.AliceIncidentId);
        Assert.NotEqual(IncidentStatus.New, before!.Status);
        using var response = await alice.DeleteAsync($"/api/incidents/{DbSeeder.AliceIncidentId}");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(before, await Snapshot(DbSeeder.AliceIncidentId));
    }

    [Fact]
    public async Task AnalystCanReadPublicCommentOfBob()
    {
        using var morgan = await Login("morgan");
        using var response = await morgan.GetAsync(
            "/api/incident-comments/30000000-0000-0000-0000-000000000002");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
