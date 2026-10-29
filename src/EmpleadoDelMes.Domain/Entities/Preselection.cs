namespace EmpleadoDelMes.Domain.Entities;

public sealed class Preselection
{
    public Preselection(Guid campaignId, Guid candidateEmployeeId, Guid actorUserId, DateTimeOffset changedAt)
    {
        CampaignId = DomainGuard.RequiredId(campaignId, nameof(campaignId));
        CandidateEmployeeId = DomainGuard.RequiredId(candidateEmployeeId, nameof(candidateEmployeeId));
        Select(actorUserId, changedAt);
    }

    public Guid CampaignId { get; }
    public Guid CandidateEmployeeId { get; }
    public bool IsSelected { get; private set; }
    public Guid LastChangedByUserId { get; private set; }
    public DateTimeOffset LastChangedAtUtc { get; private set; }

    public void Select(Guid actorUserId, DateTimeOffset changedAt) => ChangeSelection(true, actorUserId, changedAt);
    public void Withdraw(Guid actorUserId, DateTimeOffset changedAt) => ChangeSelection(false, actorUserId, changedAt);

    private void ChangeSelection(bool selected, Guid actorUserId, DateTimeOffset changedAt)
    {
        var actor = DomainGuard.RequiredId(actorUserId, nameof(actorUserId));
        IsSelected = selected;
        LastChangedByUserId = actor;
        LastChangedAtUtc = changedAt.ToUniversalTime();
    }
}
