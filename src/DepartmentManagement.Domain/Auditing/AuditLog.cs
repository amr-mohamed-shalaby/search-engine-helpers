namespace DepartmentManagement.Domain.Auditing;

/// <summary>Represents an immutable application audit event. Persistence is added in a later phase.</summary>
public sealed class AuditLog
{
    public long Id { get; init; }
    public string? UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public string Module { get; init; } = string.Empty;
    public string? EntityType { get; init; }
    public string? EntityId { get; init; }
    public DateTimeOffset OccurredAtUtc { get; init; }
    public bool Succeeded { get; init; }
    public string? DetailsJson { get; init; }
    public string? ClientMachine { get; init; }
    public string? ClientIp { get; init; }
    public string? CorrelationId { get; init; }
}
