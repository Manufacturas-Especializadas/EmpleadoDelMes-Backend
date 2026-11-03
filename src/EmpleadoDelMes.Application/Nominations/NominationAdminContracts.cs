
using EmpleadoDelMes.Domain.Enums;
using EmpleadoDelMes.Domain.Entities;

namespace EmpleadoDelMes.Application.Nominations;

public sealed record NominationAdminSearch(
    Guid? CampaignId,
    string? Term,
    bool? IsCancelled,
    int PageNumber,
    int PageSize);

public sealed record NominationAdminDto(
    Guid Id,
    Guid CampaignId,
    DateOnly EvaluationMonth,
    Guid SponsorEmployeeId,
    string SponsorPayrollNumber,
    string SponsorFullName,
    Guid CandidateEmployeeId,
    string CandidatePayrollNumber,
    string CandidateFullName,
    NominationQualities Qualities,
    DateTimeOffset RegisteredAtUtc,
    bool IsCancelled);

public sealed record NominationAdminPage(
    IReadOnlyList<NominationAdminDto> Items,
    int Total,
    int PageNumber,
    int PageSize);

public sealed record NominationAdminDetail(
    NominationAdminDto Nomination,
    Guid CreatedByUserId,
    string Justification,
    string Version);

public sealed record NominationCorrectionResult(NominationAdminDetail Detail, bool Changed, string Message);

public sealed record NominationCandidateSearch(Guid CampaignId, string? Term, int PageNumber, int PageSize);

public sealed record NominationCandidateDto(
    Guid CampaignId,
    Guid CandidateEmployeeId,
    string CandidatePayrollNumber,
    string CandidateFullName,
    int ValidNominationsCount,
    bool IsPreselected = false,
    string? PreselectionVersion = null);

public sealed record NominationCandidatePage(
    IReadOnlyList<NominationCandidateDto> Items, int Total, int PageNumber, int PageSize);

public interface INominationAdminRepository
{
    // Null means the campaign does not exist; an existing empty campaign returns an empty page.
    Task<NominationCandidatePage?> SearchCandidatesAsync(NominationCandidateSearch search, CancellationToken cancellationToken);
    Task<NominationAdminDetail?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<NominationResult<NominationAdminDetail>> CancelAsync(Guid id, byte[] expectedVersion,
        Func<Nomination, bool, NominationResult<NominationAuditEntry>> cancel, CancellationToken cancellationToken);
    Task<NominationResult<NominationCorrectionResult>> CorrectAsync(Guid id, byte[] expectedVersion,
        Func<Nomination, bool, NominationResult<NominationAuditEntry>> correct, CancellationToken cancellationToken);
    Task<NominationAdminPage> SearchAsync(
        NominationAdminSearch search,
        CancellationToken cancellationToken);
}
