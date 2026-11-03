using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Application.Candidates;

public sealed record CandidateReviewItem(Guid CampaignId, Guid CandidateEmployeeId, string CandidatePayrollNumber,
    string CandidateFullName, int ValidNominationsCount, bool HumanResourcesHasOpinion, bool SafetyHasOpinion, bool QualityHasOpinion);
public sealed record CandidateReviewPage(IReadOnlyList<CandidateReviewItem> Items, int Total, int PageNumber, int PageSize);
public sealed record ReviewNomination(Guid Id, string SponsorPayrollNumber, string SponsorFullName,
    NominationQualities Qualities, string Justification, DateTimeOffset RegisteredAtUtc);
public sealed record ReviewOpinion(OpinionSource OpinionSource, bool HasOpinion, string? Content,
    DateTimeOffset? CreatedAtUtc, DateTimeOffset? LastModifiedAtUtc);
public sealed record CandidateReviewDetail(Guid CampaignId, DateOnly EvaluationMonth, Guid CandidateEmployeeId,
    string CandidatePayrollNumber, string CandidateFullName, bool IsPreselected, int ValidNominationsCount,
    IReadOnlyList<ReviewNomination> Nominations, IReadOnlyList<ReviewOpinion> Opinions);

public interface ICandidateReviewRepository
{
    Task<NominationResult<CandidateReviewPage>> SearchAsync(NominationCandidateSearch search, CancellationToken token);
    Task<NominationResult<CandidateReviewDetail>> GetAsync(Guid campaignId, Guid candidateId, CancellationToken token);
}

public sealed class CandidateReviewService(ICandidateReviewRepository repository)
{
    public async Task<NominationResult<CandidateReviewPage>> SearchAsync(Guid? campaignId, string? term,
        int pageNumber, int pageSize, CancellationToken token)
    {
        if (!campaignId.HasValue || campaignId == Guid.Empty)
            return new(null, NominationError.Validation, "Selecciona una convocatoria válida; la convocatoria es obligatoria.");
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100 || ((long)pageNumber - 1) * pageSize > int.MaxValue)
            return new(null, NominationError.Validation, "La página debe ser positiva y el tamaño de página debe estar entre 1 y 100.");
        if (term?.Length > 100)
            return new(null, NominationError.Validation, "La búsqueda no puede superar 100 caracteres.");
        try
        {
            return await repository.SearchAsync(new(campaignId.Value,
                string.IsNullOrWhiteSpace(term) ? null : term.Trim(), pageNumber, pageSize), token);
        }
        catch (NominationPersistenceException)
        { return new(null, NominationError.Persistence, "No pudimos consultar los candidatos. Inténtalo nuevamente."); }
    }

    public async Task<NominationResult<CandidateReviewDetail>> GetAsync(Guid campaignId, Guid candidateId, CancellationToken token)
    {
        if (campaignId == Guid.Empty || candidateId == Guid.Empty)
            return new(null, NominationError.Validation, "Selecciona una convocatoria y un candidato válidos.");
        try { return await repository.GetAsync(campaignId, candidateId, token); }
        catch (NominationPersistenceException)
        { return new(null, NominationError.Persistence, "No pudimos consultar el detalle del candidato. Inténtalo nuevamente."); }
    }
}
