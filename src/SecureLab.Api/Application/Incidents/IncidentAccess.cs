using System.Security.Claims;
using SecureLab.Api.Data;
using SecureLab.Api.Data.Entities;

namespace SecureLab.Api.Application.Incidents;

public readonly record struct IncidentSubject(Guid Id, bool Reporter, bool Analyst)
{
    public static bool TryRead(ClaimsPrincipal user, out IncidentSubject subject)
    {
        subject = default;
        if (user.Identity?.IsAuthenticated != true ||
            !Guid.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            return false;
        subject = new(id, user.IsInRole(DbSeeder.ReporterRole),
            user.IsInRole(DbSeeder.AnalystRole));
        return true;
    }
}

public static class IncidentAccess
{
    public static IQueryable<Incident> Visible(
        IQueryable<Incident> query, IncidentSubject subject)
    {
        if (subject.Analyst) return query;
        if (subject.Reporter)
            return query.Where(item => item.OwnerUserId == subject.Id);
        return query.Where(_ => false);
    }

    public static bool Read(IncidentSubject subject, Incident item) =>
        subject.Analyst || (subject.Reporter && item.OwnerUserId == subject.Id);

    public static bool Update(IncidentSubject subject, Incident item) =>
        subject.Reporter && item.OwnerUserId == subject.Id;

    public static bool Delete(IncidentSubject subject, Incident item) =>
        Update(subject, item) && item.Status == IncidentStatus.New;

    public static bool Triage(IncidentSubject subject, Incident item) =>
        subject.Analyst && item.Status == IncidentStatus.New;
}
