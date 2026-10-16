namespace AccessRequestHub.Domain;

public sealed class ApiException(
    int statusCode,
    string code,
    string message,
    object? details = null) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
    public string Code { get; } = code;
    public object? Details { get; } = details;
}

public sealed record CreateRequestCommand(
    string? ClientRequestId,
    string? ApplicationId,
    string? Environment,
    string? AccessLevel,
    string? Justification);

public sealed record DecisionCommand(int Version, string? Reason);

public sealed record ErrorDetails(string Code, string Message, object? Details = null);

public sealed record ErrorEnvelope(ErrorDetails Error, string RequestId);

public sealed record CreateOutcome(RequestView Request, bool Replayed);

public sealed record UserView(
    string Id,
    string Name,
    string Email,
    string Role,
    string? ManagerId);

public sealed record ApplicationView(string Id, string Name, string OwnerId);

public sealed record BootstrapView(
    IReadOnlyList<UserView> Users,
    IReadOnlyList<ApplicationView> Applications,
    IReadOnlyList<string> Environments,
    IReadOnlyList<string> AccessLevels,
    string PolicyVersion);

public sealed record AuditEventView(
    long Id,
    string RequestId,
    string ActorId,
    string ActorName,
    string Action,
    string? FromStatus,
    string ToStatus,
    string? Reason,
    int RequestVersion,
    string PolicyVersion,
    string OccurredAt);

public sealed record RequestView(
    string Id,
    string ClientRequestId,
    string RequesterId,
    string ManagerId,
    string OwnerId,
    string ApplicationId,
    string ApplicationName,
    string RequesterName,
    string RequesterEmail,
    string ManagerName,
    string OwnerName,
    string Environment,
    string AccessLevel,
    string Justification,
    string Status,
    string PolicyVersion,
    int Version,
    string CreatedAt,
    string UpdatedAt,
    bool HighRisk,
    bool CanAct,
    string? AssignedApprover,
    IReadOnlyList<AuditEventView>? Audit = null);
