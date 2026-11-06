namespace EmpleadoDelMes.Domain.Entities;

// Immutable binding to an assignment's historical identity, never its current active state.
public sealed class CampaignEvaluator
{
    private CampaignEvaluator() { }
    public CampaignEvaluator(Guid campaignId, Guid candidateEmployeeId, Guid assignmentId, DateTimeOffset linkedAt)
    {
        CampaignId = DomainGuard.RequiredId(campaignId, nameof(campaignId));
        CandidateEmployeeId = DomainGuard.RequiredId(candidateEmployeeId, nameof(candidateEmployeeId));
        AssignmentId = DomainGuard.RequiredId(assignmentId, nameof(assignmentId));
        LinkedAtUtc = linkedAt.ToUniversalTime();
    }
    public Guid CampaignId { get; }
    public Guid CandidateEmployeeId { get; }
    public Guid AssignmentId { get; }
    public DateTimeOffset LinkedAtUtc { get; }
}
