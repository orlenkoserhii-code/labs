using System.Text.Json.Serialization;

namespace SecureLab.Api.Presentation.Contracts;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record UpdateIncidentRequest(string? Title, string? Description);
