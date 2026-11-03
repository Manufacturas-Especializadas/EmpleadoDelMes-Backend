using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Application.Nominations;

public enum NominationError { Validation, Unauthorized, Forbidden, NotFound, NoActiveCampaign, Duplicate, Concurrency, Persistence }
public sealed record NominationResult<T>(T? Value, NominationError? Error = null, string? Message = null)
{
    public bool IsSuccess => Error is null;
}
public sealed class NominationPersistenceException(NominationError error, Exception? inner = null)
    : Exception("Nomination persistence operation failed.", inner)
{
    public NominationError Error { get; } = error;
}
public sealed record CandidateDto(Guid Id, string PayrollNumber, string FullName);
public sealed record CandidatePage(IReadOnlyList<CandidateDto> Items, int Total, int PageNumber, int PageSize);
public sealed record ActiveNominationCampaign(Guid Id, DateOnly EvaluationMonth);
public sealed record NominationConfirmation(Guid Id, Guid CampaignId, Guid CreatedByUserId,
    CandidateDto Sponsor, CandidateDto Candidate, NominationQualities Qualities,
    string Justification, DateTimeOffset RegisteredAtUtc, bool IsCancelled)
{
    public static NominationConfirmation FromDomain(Nomination n) => new(n.Id, n.CampaignId, n.CreatedByUserId,
        new(n.SponsorEmployeeId, n.SponsorPayrollNumber, n.SponsorFullName),
        new(n.CandidateEmployeeId, n.CandidatePayrollNumber, n.CandidateFullName),
        n.Qualities, n.Justification, n.RegisteredAtUtc, n.IsCancelled);
}
public sealed record NominationActor(Guid UserId, Employee? Employee, bool IsActive, bool MustChangePassword, bool IsSponsor, bool IsLockedOut);
public sealed record NominationCreationContext(NominationActor? Actor, Employee? Candidate, IReadOnlyList<Campaign> Campaigns);

public interface INominationRepository
{
    Task<NominationActor?> GetActorAsync(Guid userId, CancellationToken cancellationToken);
    Task<IReadOnlyList<Campaign>> GetPotentialCampaignsAsync(CancellationToken cancellationToken);
    // Resolve account, employees and campaigns after taking the SAME transaction lock as campaign administration.
    Task<NominationResult<NominationConfirmation>> CreateAsync(Guid userId, Guid candidateId,
        Func<NominationCreationContext, NominationResult<Nomination>> create, CancellationToken cancellationToken);
}
