using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Domain.Entities;

public sealed class NominationAuditEntry
{
    public NominationAuditEntry(Guid id, Guid nominationId, Guid actorUserId,
        NominationAuditAction action, DateTimeOffset occurredAt, string reason, string? details = null)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        NominationId = DomainGuard.RequiredId(nominationId, nameof(nominationId));
        ActorUserId = DomainGuard.RequiredId(actorUserId, nameof(actorUserId));
        if (!Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        Action = action;
        OccurredAtUtc = occurredAt.ToUniversalTime();
        Reason = DomainGuard.RequiredText(reason, nameof(reason));
        Details = action == NominationAuditAction.Correction
            ? DomainGuard.RequiredText(details!, nameof(details))
            : string.IsNullOrWhiteSpace(details) ? null : details.Trim();
    }

    public Guid Id { get; }
    public Guid NominationId { get; }
    public Guid ActorUserId { get; }
    public NominationAuditAction Action { get; }
    public DateTimeOffset OccurredAtUtc { get; }
    public string Reason { get; }
    public string? Details { get; }
}
