using Microsoft.EntityFrameworkCore;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Scaffolding;

// Навчальний старт ЛР 02. Запускати лише з локальними штучними даними.
public static class Lab02Endpoints
{
    public static void MapLab02Endpoints(this WebApplication app)
    {
        app.MapGet("/api/incidents/search", async (string? q, string? sortBy, SecureLabDbContext db, CancellationToken ct) =>
        {
            var order = sortBy switch
            {
                null or "" or "createdAtUtc" => "created_at_utc DESC",
                "severity" => "severity", "status" => "status", _ => sortBy
            };
            var sql = "SELECT * FROM incidents WHERE title ILIKE '%" + (q ?? "")
                + "%' OR description ILIKE '%" + (q ?? "") + "%' ORDER BY " + order + " LIMIT 50";
            var rows = await db.Incidents.FromSqlRaw(sql).AsNoTracking().ToListAsync(ct);
            return Results.Ok(rows.Select(row => new
            {
                row.Id, row.Title, row.Description,
                Severity = row.Severity.ToString(), Status = row.Status.ToString(), row.CreatedAtUtc
            }));
        });
        app.MapPost("/api/incidents", async (CreateIncidentRequest request, SecureLabDbContext db, CancellationToken ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Title))
                errors["title"] = ["Заголовок обов'язковий."];
            else if (request.Title.Length > 160)
                errors["title"] = ["Не довше 160 символів."];

            if (string.IsNullOrWhiteSpace(request.Description))
                errors["description"] = ["Опис обов'язковий."];
            else if (request.Description.Length > 4000)
                errors["description"] = ["Не довше 4000 символів."];

            var severityOk = Enum.TryParse<IncidentSeverity>(request.Severity, true, out var severity)
                            && Enum.IsDefined(severity);
            if (!severityOk)
                errors["severity"] = ["Допустимі значення: Low, Medium, High, Critical."];

            if (request.OccurredAtUtc is null)
                errors["occurredAtUtc"] = ["Обов'язкове поле."];
            else if (request.OccurredAtUtc > now.AddMinutes(5))
                errors["occurredAtUtc"] = ["Дата не може бути більш ніж на 5 хвилин у майбутньому."];

            var title = request.Title?.Trim() ?? "";
            var description = request.Description?.Trim() ?? "";

            if (severityOk && severity is IncidentSeverity.High or IncidentSeverity.Critical
                && description.Length < 40)
                errors["description"] = ["Для High або Critical потрібно щонайменше 40 символів після Trim()."];

            if (errors.Count > 0)
                return Results.ValidationProblem(errors);

            if (await db.Incidents.AnyAsync(item => item.Title == title
                && item.Status != IncidentStatus.Closed, ct))
                return Results.Problem(
                    title: "Конфлікт інциденту",
                    detail: "Інцидент із таким заголовком уже існує та ще не закритий.",
                    statusCode: StatusCodes.Status409Conflict);

            var incident = new Incident
            {
                Id = Guid.NewGuid(),
                OwnerUserId = DbSeeder.AliceId,
                Title = title,
                Description = description,
                Severity = severity,
                Status = IncidentStatus.New,
                OccurredAtUtc = request.OccurredAtUtc!.Value.ToUniversalTime(),
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.Incidents.Add(incident);
            await db.SaveChangesAsync(ct);

            return Results.Created(
                $"/api/incidents/{incident.Id}",
                new CreatedIncidentResponse(
                    incident.Id, incident.Title, incident.Severity.ToString(),
                    incident.Status.ToString(), incident.CreatedAtUtc));
        });
    }
}

public sealed record CreateIncidentRequest(
    string? Title, string? Description, string? Severity, DateTimeOffset? OccurredAtUtc);
public sealed record CreatedIncidentResponse(
    Guid Id, string Title, string Severity, string Status, DateTimeOffset CreatedAtUtc);
