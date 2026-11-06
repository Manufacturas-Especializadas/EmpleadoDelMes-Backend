using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
namespace EmpleadoDelMes.Application.Opinions;

public sealed record SupervisorCampaign(Guid Id, DateOnly EvaluationMonth, bool IsArchived);
public sealed record SupervisorCampaignPage(IReadOnlyList<SupervisorCampaign> Items, int Total, int PageNumber, int PageSize);
public sealed record EvaluatorLink(Guid CampaignId, Guid CandidateEmployeeId, Guid? SponsorEmployeeId, bool IsPending);
public interface ISupervisorRepository
{
    Task<SupervisorCampaignPage> CampaignsAsync(Guid userId, int page, int size, CancellationToken token);
    Task<OpinionPage> CandidatesAsync(Guid userId, Guid campaignId, string? term, bool? hasOpinion, int page, int size, CancellationToken token);
    Task<NominationResult<OpinionStatus>> GetAsync(Guid userId, Guid campaignId, Guid candidateId, CancellationToken token);
    Task<NominationResult<EvaluatorLink>> LinkAsync(Guid campaignId, Guid candidateId, CancellationToken token);
}
public sealed class SupervisorService(ISupervisorRepository repository, IOpinionRepository opinions, TimeProvider clock)
{
    private static bool InvalidPage(int page, int size) => page < 1 || size < 1 || size > 100 || ((long)page - 1) * size > int.MaxValue;
    public Task<NominationResult<SupervisorCampaignPage>> CampaignsAsync(Guid user, int page, int size, CancellationToken token) =>
        InvalidPage(page, size) ? Task.FromResult(new NominationResult<SupervisorCampaignPage>(null, NominationError.Validation, "Indica una página válida y tamaño entre 1 y 100."))
        : Execute(async () => new NominationResult<SupervisorCampaignPage>(await repository.CampaignsAsync(user, page, size, token)));
    public Task<NominationResult<OpinionPage>> CandidatesAsync(Guid user, Guid campaign, string? term, bool? completed, int page, int size, CancellationToken token) =>
        campaign == Guid.Empty || InvalidPage(page, size) || term?.Length > 100
            ? Task.FromResult(new NominationResult<OpinionPage>(null, NominationError.Validation, "Revisa la convocatoria, paginación y búsqueda de hasta 100 caracteres."))
            : Execute(async () => new NominationResult<OpinionPage>(await repository.CandidatesAsync(user, campaign, term?.Trim(), completed, page, size, token)));
    public Task<NominationResult<OpinionStatus>> GetAsync(Guid user, Guid campaign, Guid candidate, CancellationToken token) =>
        campaign == Guid.Empty || candidate == Guid.Empty
            ? Task.FromResult(new NominationResult<OpinionStatus>(null, NominationError.Validation, "Selecciona una convocatoria y candidato válidos."))
            : Execute(() => repository.GetAsync(user, campaign, candidate, token));
    public Task<NominationResult<EvaluatorLink>> LinkAsync(Guid campaign, Guid candidate, CancellationToken token) =>
        campaign == Guid.Empty || candidate == Guid.Empty ? Task.FromResult(new NominationResult<EvaluatorLink>(null, NominationError.Validation, "Selecciona una convocatoria y candidato válidos."))
        : Execute(() => repository.LinkAsync(campaign, candidate, token));
    public Task<NominationResult<OpinionSaved>> SaveAsync(Guid user, Guid campaign, Guid candidate, string? content, string? version, CancellationToken token)
    {
        if (user == Guid.Empty || campaign == Guid.Empty || candidate == Guid.Empty)
            return Task.FromResult(new NominationResult<OpinionSaved>(null, NominationError.Validation, "Selecciona una convocatoria y candidato válidos."));
        byte[]? bytes = null;
        if (version is not null)
        {
            bytes = new byte[8];
            if (!Convert.TryFromBase64String(version, bytes, out var count) || count != 8)
                return Task.FromResult(new NominationResult<OpinionSaved>(null, NominationError.Validation, "La versión de la opinión no es válida."));
        }
        return Execute(() => opinions.SaveAsync(campaign, candidate, OpinionSource.ImmediateSupervisor, bytes, state =>
        {
            if (state.Campaign is null || !state.CandidateExists) return new(null, NominationError.NotFound, "No encontramos la convocatoria o el candidato.");
            var now = clock.GetUtcNow();
            if (!state.Eligible || !state.Campaign.CanAcceptOpinions(now))
                return new(null, NominationError.Concurrency, "Se requiere preselección activa, una postulación válida y un periodo de opiniones abierto.");
            try
            {
                if (state.Existing is null) return new(new CandidateOpinion(Guid.NewGuid(), campaign, candidate, OpinionSource.ImmediateSupervisor, content!, user, now));
                if (state.Existing.Content != content?.Trim()) state.Existing.EditContent(content!, now);
                return new(state.Existing);
            }
            catch (ArgumentException) { return new(null, NominationError.Validation, "Escribe una opinión obligatoria de hasta 500 caracteres."); }
        }, token, user));
    }
    private static async Task<NominationResult<T>> Execute<T>(Func<Task<NominationResult<T>>> action)
    {
        try { return await action(); }
        catch (NominationPersistenceException ex)
        { return new(default, ex.Error, ex.Error == NominationError.Concurrency ? "La información cambió o existe una opinión histórica sin evaluador. Revisa la vinculación." : "No pudimos completar la consulta de evaluadores. Inténtalo nuevamente."); }
    }
}
