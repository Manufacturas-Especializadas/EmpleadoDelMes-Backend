using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Domain.Entities;

public sealed class CandidateOpinion
{
    public CandidateOpinion(Guid id, Guid campaignId, Guid candidateEmployeeId,
        OpinionSource opinionSource, string content, Guid createdByUserId, DateTimeOffset createdAt)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        CampaignId = DomainGuard.RequiredId(campaignId, nameof(campaignId));
        CandidateEmployeeId = DomainGuard.RequiredId(candidateEmployeeId, nameof(candidateEmployeeId));
        CreatedByUserId = DomainGuard.RequiredId(createdByUserId, nameof(createdByUserId));
        if (!Enum.IsDefined(opinionSource))
        {
            throw new ArgumentOutOfRangeException(nameof(opinionSource));
        }

        OpinionSource = opinionSource;
        Content = DomainGuard.RequiredText(content, nameof(content), 500);
        CreatedAtUtc = createdAt.ToUniversalTime();
        LastModifiedAtUtc = CreatedAtUtc;
    }

    public Guid Id { get; }
    public Guid CampaignId { get; }
    public Guid CandidateEmployeeId { get; }
    public OpinionSource OpinionSource { get; }
    public string Content { get; private set; }
    public Guid CreatedByUserId { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset LastModifiedAtUtc { get; private set; }

    public void EditContent(string content, DateTimeOffset modifiedAt)
    {
        Content = DomainGuard.RequiredText(content, nameof(content), 500);
        LastModifiedAtUtc = modifiedAt.ToUniversalTime();
    }
}
