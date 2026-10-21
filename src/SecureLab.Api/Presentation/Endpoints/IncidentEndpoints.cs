using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Application.Incidents;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;
using SecureLab.Api.Presentation.Contracts;

namespace SecureLab.Api.Presentation.Endpoints;

public static class IncidentEndpoints
{
    public static IEndpointRouteBuilder MapIncidentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/incidents")
            .RequireAuthorization().WithTags("Incidents");
        group.MapGet("/", List).WithName("GetIncidents")
            .Produces<IReadOnlyList<IncidentListItemResponse>>().ProducesValidationProblem();
        group.MapGet("/{id:guid}", Details).WithName("GetIncidentDetails")
            .Produces<IncidentDetailsResponse>().ProducesProblem(404);
        group.MapPut("/{id:guid}", Update);
        group.MapDelete("/{id:guid}", Delete);
        group.MapPost("/{id:guid}/triage", Triage);
        group.MapGet("/severity-summary", Summary).WithName("GetIncidentSeveritySummary")
            .Produces<IReadOnlyList<IncidentSeveritySummaryResponse>>();
        endpoints.MapGet("/api/incident-comments/{id:guid}", Comment)
            .RequireAuthorization();
        return endpoints;
    }

    private static IResult Hidden() => Results.Problem(
        title: "Інцидент не знайдено", statusCode: 404);

    private static IResult Denied() => Results.Problem(
        title: "Доступ заборонено", statusCode: 403);

    private static bool ParseStatus(string? value, out IncidentStatus? status)
    {
        status = null;
        if (value is null) return true;
        if (!Enum.GetNames<IncidentStatus>().Contains(value, StringComparer.OrdinalIgnoreCase))
            return false;
        status = Enum.Parse<IncidentStatus>(value, true);
        return true;
    }

    private static IResult InvalidStatus() => Results.ValidationProblem(
        new Dictionary<string, string[]>
        {
            ["status"] = ["Допустимі значення: New, Triaged, InProgress, Resolved, Closed."]
        });

    private static async Task<IResult> List(string? status, HttpContext http,
        IncidentQueries queries, CancellationToken ct)
    {
        if (!IncidentSubject.TryRead(http.User, out var subject)) return Results.Unauthorized();
        if (!ParseStatus(status, out var parsed)) return InvalidStatus();
        return Results.Ok(await queries.GetListAsync(parsed, subject, ct));
    }

    private static async Task<IResult> Details(Guid id, HttpContext http,
        IncidentQueries queries, CancellationToken ct)
    {
        if (!IncidentSubject.TryRead(http.User, out var subject)) return Results.Unauthorized();
        var response = await queries.GetDetailsAsync(id, subject, ct);
        return response is null ? Hidden() : Results.Ok(response);
    }

    private static async Task<IResult> Summary(string? status,
        IncidentQueries queries, CancellationToken ct)
    {
        if (!ParseStatus(status, out var parsed)) return InvalidStatus();
        return Results.Ok(await queries.GetSeveritySummaryAsync(parsed, ct));
    }

    private static async Task<IResult> Update(Guid id, UpdateIncidentRequest request,
        HttpContext http, SecureLabDbContext db, CancellationToken ct)
    {
        if (!IncidentSubject.TryRead(http.User, out var subject)) return Results.Unauthorized();
        var item = await db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null) return Hidden();
        // Controlled local IDOR: no resource permission before content update.

        var title = request.Title?.Trim() ?? "";
        var description = request.Description?.Trim() ?? "";
        var errors = new Dictionary<string, string[]>();
        if (title.Length == 0 || request.Title!.Length > 160)
            errors["title"] = ["Обов'язкове поле, до 160 символів."];
        if (description.Length == 0 || request.Description!.Length > 4000)
            errors["description"] = ["Обов'язкове поле, до 4000 символів."];
        if (item.Severity is IncidentSeverity.High or IncidentSeverity.Critical
            && description.Length < 40)
            errors["description"] = ["Для High/Critical потрібно від 40 символів після Trim."];
        if (errors.Count > 0) return Results.ValidationProblem(errors);
        if (await db.Incidents.AnyAsync(x => x.Id != id && x.Title == title
            && x.Status != IncidentStatus.Closed, ct))
            return Results.Problem(title: "Конфлікт заголовка", statusCode: 409);

        item.Title = title;
        item.Description = description;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Delete(Guid id, HttpContext http,
        SecureLabDbContext db, CancellationToken ct)
    {
        if (!IncidentSubject.TryRead(http.User, out var subject)) return Results.Unauthorized();
        var item = await db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || !IncidentAccess.Read(subject, item)) return Hidden();
        if (!IncidentAccess.Delete(subject, item)) return Denied();
        db.Incidents.Remove(item);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> Comment(Guid id, HttpContext http,
        SecureLabDbContext db, CancellationToken ct)
    {
        if (!IncidentSubject.TryRead(http.User, out var subject)) return Results.Unauthorized();
        var item = await db.IncidentComments.AsNoTracking()
            .Include(x => x.Incident).Include(x => x.Author)
            .SingleOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || !IncidentAccess.Read(subject, item.Incident)
            || (item.IsInternal && !subject.Analyst)) return Hidden();
        return Results.Ok(new IncidentCommentResponse(item.Id,
            item.Author.DisplayName, item.Text, item.CreatedAtUtc));
    }

    private static async Task<IResult> Triage(Guid id, HttpContext http,
        SecureLabDbContext db, ILoggerFactory logs, CancellationToken ct)
    {
        if (!IncidentSubject.TryRead(http.User, out var subject)) return Results.Unauthorized();
        var item = await db.Incidents.SingleOrDefaultAsync(x => x.Id == id, ct);
        var visible = item is not null && IncidentAccess.Read(subject, item);
        if (!visible || !IncidentAccess.Triage(subject, item!))
        {
            var trace = Activity.Current?.Id ?? http.TraceIdentifier;
            logs.CreateLogger("SecureLab.Authorization").LogWarning(
                "{EventName} subject={SubjectId} action={Action} resource={ResourceId} result={Result} at={OccurredAtUtc} trace={TraceId}",
                "authorization.denied", subject.Id, "triage", id,
                "denied", DateTimeOffset.UtcNow, trace);
            return Results.Problem(
                title: visible ? "Доступ заборонено" : "Інцидент не знайдено",
                statusCode: visible ? 403 : 404,
                extensions: new Dictionary<string, object?> { ["traceId"] = trace });
        }

        item!.Status = IncidentStatus.Triaged;
        item.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.IncidentStatusHistory.Add(new IncidentStatusHistory
        {
            Id = Guid.NewGuid(), IncidentId = id, ChangedByUserId = subject.Id,
            OldStatus = IncidentStatus.New, NewStatus = IncidentStatus.Triaged,
            Note = "Первинна класифікація аналітиком.",
            CreatedAtUtc = item.UpdatedAtUtc
        });
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
