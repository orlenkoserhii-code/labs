using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Tests;

public sealed class Lab02ContractTests(SecureLabApiFactory factory)
    : IClassFixture<SecureLabApiFactory>, IAsyncLifetime
{
    private readonly HttpClient _client = factory.CreateClient();
    private readonly string _prefix = $"LR02-{Guid.NewGuid():N}-";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Title.StartsWith(_prefix)).ExecuteDeleteAsync();
        _client.Dispose();
    }

    private Dictionary<string, object?> ValidBody(string suffix = "valid") => new()
    {
        ["title"] = _prefix + suffix,
        ["description"] = "Навчальний опис інциденту з достатньою кількістю символів.",
        ["severity"] = "Low",
        ["occurredAtUtc"] = "2026-09-01T13:00:00+03:00"
    };

    private static async Task<JsonElement> ReadJson(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task AssertProblem(HttpResponseMessage response, HttpStatusCode status, string? field = null)
    {
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var json = await ReadJson(response);
        Assert.Equal((int)status, json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("title").GetString()));
        if (field is not null) Assert.True(json.GetProperty("errors").TryGetProperty(field, out _));
        foreach (var forbidden in new[] { "Exception", "SELECT ", "Npgsql", "Password=", "SecureLab.Api" })
            Assert.DoesNotContain(forbidden, json.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_UsesServerValues_NormalizesUtc_AndReturnsOnlyContract()
    {
        var body = ValidBody();
        body["title"] = "  " + body["title"] + "  ";
        body["description"] = "  trimmed description  ";
        var suppliedId = Guid.NewGuid();
        body["id"] = suppliedId;
        body["ownerUserId"] = DbSeeder.BobId;
        body["status"] = "Closed";
        body["createdAtUtc"] = "2000-01-01T00:00:00Z";
        using var response = await _client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var json = await ReadJson(response);
        Assert.Equal(new[] { "createdAtUtc", "id", "severity", "status", "title" },
            json.EnumerateObject().Select(p => p.Name).OrderBy(name => name).ToArray());
        var id = json.GetProperty("id").GetGuid();
        Assert.NotEqual(suppliedId, id);
        Assert.Equal(_prefix + "valid", json.GetProperty("title").GetString());
        Assert.Equal("New", json.GetProperty("status").GetString());
        Assert.Equal($"/api/incidents/{id}", response.Headers.Location?.OriginalString);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var stored = await db.Incidents.AsNoTracking().SingleAsync(item => item.Id == id);
        Assert.Equal(DbSeeder.AliceId, stored.OwnerUserId);
        Assert.Equal("trimmed description", stored.Description);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 10, 0, 0, TimeSpan.Zero), stored.OccurredAtUtc);
        Assert.Equal(TimeSpan.Zero, stored.OccurredAtUtc.Offset);
        Assert.Equal(stored.CreatedAtUtc, stored.UpdatedAtUtc);
        Assert.True(stored.CreatedAtUtc > new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Theory]
    [InlineData("title", "")]
    [InlineData("title", "   ")]
    [InlineData("title", null)]
    [InlineData("description", "")]
    [InlineData("description", null)]
    [InlineData("severity", "7")]
    [InlineData("severity", "Unknown")]
    [InlineData("severity", null)]
    [InlineData("occurredAtUtc", null)]
    [InlineData("occurredAtUtc", "2099-01-01T00:00:00Z")]
    public async Task InvalidDto_ReturnsFieldProblem_WithoutInsert(string field, string? value)
    {
        var body = ValidBody();
        body[field] = value;
        using var response = await _client.PostAsJsonAsync("/api/incidents", body);
        await AssertProblem(response, HttpStatusCode.BadRequest, field);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        Assert.False(await db.Incidents.AnyAsync(item => item.Title.StartsWith(_prefix)));
    }

    [Theory]
    [InlineData("title", 160)]
    [InlineData("description", 4000)]
    public async Task LengthLimit_IsMeasuredBeforeTrim(string field, int limit)
    {
        var body = ValidBody();
        body[field] = field == "title" ? _prefix + new string('x', limit - _prefix.Length) : new string('x', limit);
        using var accepted = await _client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        body[field] = body[field] + " ";
        using var rejected = await _client.PostAsJsonAsync("/api/incidents", body);
        await AssertProblem(rejected, HttpStatusCode.BadRequest, field);
    }

    [Theory]
    [InlineData("High", 39, false)]
    [InlineData("High", 40, true)]
    [InlineData("Critical", 39, false)]
    [InlineData("Critical", 40, true)]
    public async Task CrossField_ChecksTrimmedDescription(string severity, int length, bool allowed)
    {
        var body = ValidBody();
        body["severity"] = severity;
        body["description"] = "  " + new string('x', length) + "  ";
        using var response = await _client.PostAsJsonAsync("/api/incidents", body);
        if (allowed) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        else await AssertProblem(response, HttpStatusCode.BadRequest, "description");
    }

    [Theory]
    [InlineData(IncidentStatus.New)]
    [InlineData(IncidentStatus.Triaged)]
    [InlineData(IncidentStatus.InProgress)]
    [InlineData(IncidentStatus.Resolved)]
    [InlineData(IncidentStatus.Closed)]
    public async Task DuplicateTitle_ConflictsUnlessClosed_AndIsCaseSensitive(IncidentStatus status)
    {
        var body = ValidBody("Alert");
        using var first = await _client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var id = (await ReadJson(first)).GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        await db.Incidents.Where(item => item.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(item => item.Status, status));
        body["title"] = "  " + _prefix + "Alert  ";
        using var duplicate = await _client.PostAsJsonAsync("/api/incidents", body);
        if (status == IncidentStatus.Closed) Assert.Equal(HttpStatusCode.Created, duplicate.StatusCode);
        else await AssertProblem(duplicate, HttpStatusCode.Conflict);
        body["title"] = _prefix + "alert";
        using var differentCase = await _client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Created, differentCase.StatusCode);
    }

    [Theory]
    [InlineData("USB", "20000000-0000-0000-0000-000000000005")]
    [InlineData("O'Brien", "20000000-0000-0000-0000-000000000004")]
    [InlineData("zz-no-match", null)]
    [InlineData("zz-no-match' OR TRUE -- ", null)]
    public async Task Search_Regression_ReturnsExactSet(string q, string? expectedId)
    {
        using var response = await _client.GetAsync("/api/incidents/search?q=" + Uri.EscapeDataString(q));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadJson(response);
        var ids = result.EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(expectedId is null ? Array.Empty<Guid>() : new[] { Guid.Parse(expectedId) }, ids);
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("\\")]
    public async Task Search_Metacharacters_AreLiteral_AndDescriptionIsSearched(string symbol)
    {
        var body = ValidBody("literal");
        body["description"] = _prefix + symbol + "needle";
        using var created = await _client.PostAsJsonAsync("/api/incidents", body);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var expected = (await ReadJson(created)).GetProperty("id").GetGuid();
        var decoy = ValidBody("decoy");
        decoy["description"] = _prefix + "Xneedle";
        using var other = await _client.PostAsJsonAsync("/api/incidents", decoy);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        using var result = await _client.GetAsync("/api/incidents/search?q=" + Uri.EscapeDataString(_prefix + symbol + "needle"));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var rows = (await ReadJson(result)).EnumerateArray().ToArray();
        Assert.Equal(expected, Assert.Single(rows).GetProperty("id").GetGuid());
    }

    [Theory]
    [InlineData("price")]
    [InlineData("created_at_utc")]
    [InlineData("Severity")]
    public async Task UnknownSort_ReturnsValidationProblem(string sort)
    {
        using var response = await _client.GetAsync("/api/incidents/search?sortBy=" + Uri.EscapeDataString(sort));
        await AssertProblem(response, HttpStatusCode.BadRequest, "sortBy");
    }

    [Fact]
    public async Task MissingIncident_UsesSafeProblemDetails()
    {
        using var response = await _client.GetAsync("/api/incidents/99999999-9999-9999-9999-999999999999");
        await AssertProblem(response, HttpStatusCode.NotFound);
        Assert.Equal("Інцидент не знайдено", (await ReadJson(response)).GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("createdAtUtc")]
    [InlineData("severity")]
    [InlineData("status")]
    public async Task Search_SortsByBusinessRank_ThenId_AndLimitsAfterSorting(string? sort)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SecureLabDbContext>();
        var now = DateTimeOffset.UtcNow;
        var fixtures = Enumerable.Range(0, 55).Select(n => new Incident
        {
            Id = Guid.Parse($"30000000-0000-0000-0000-{n:000000000000}"),
            OwnerUserId = DbSeeder.AliceId, Title = _prefix + n,
            Description = "Sorting fixture", Severity = (IncidentSeverity)(n % 4),
            Status = (IncidentStatus)(n % 5), OccurredAtUtc = now,
            CreatedAtUtc = now.AddMinutes(n % 3), UpdatedAtUtc = now
        }).ToList();
        db.Incidents.AddRange(fixtures);
        await db.SaveChangesAsync();
        var ordered = sort switch
        {
            "severity" => fixtures.OrderByDescending(item => item.Severity),
            "status" => fixtures.OrderBy(item => item.Status),
            _ => fixtures.OrderByDescending(item => item.CreatedAtUtc)
        };
        var expected = ordered.ThenBy(item => item.Id).Take(50).Select(item => item.Id).ToArray();
        var url = "/api/incidents/search?q=" + Uri.EscapeDataString(_prefix);
        if (sort is not null) url += "&sortBy=" + sort;
        using var response = await _client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var actual = (await ReadJson(response)).EnumerateArray().Select(item => item.GetProperty("id").GetGuid()).ToArray();
        Assert.Equal(expected, actual);
    }
}
