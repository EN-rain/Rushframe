namespace Rushframe.Domain;

public sealed record AgentAuditRecord(
    DateTimeOffset TimestampUtc,
    ProjectId ProjectId,
    long ProjectRevision,
    string Action,
    string Summary,
    bool Success,
    string? Error,
    string SessionId);
