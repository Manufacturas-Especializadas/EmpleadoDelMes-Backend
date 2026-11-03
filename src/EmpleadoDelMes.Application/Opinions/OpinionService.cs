using EmpleadoDelMes.Application.Accounts;
using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Application.Opinions;

public sealed record OpinionActor(Guid UserId, IReadOnlyCollection<string> Roles);
public sealed record OpinionCandidate(Guid CandidateEmployeeId, string CandidatePayrollNumber,
    string CandidateFullName, Guid CampaignId, bool HasOpinion);
public sealed record OpinionPage(IReadOnlyList<OpinionCandidate> Items, int Total, int PageNumber, int PageSize);
public sealed record OpinionDto(Guid Id, Guid CampaignId, Guid CandidateEmployeeId, string Content,
    DateTimeOffset CreatedAtUtc, DateTimeOffset LastModifiedAtUtc, string Version);
public sealed record OpinionStatus(bool HasOpinion, OpinionDto? Opinion, string? Message = null);
public sealed record OpinionSaved(OpinionDto Opinion, bool Created);
public sealed record OpinionWriteContext(Campaign? Campaign, bool CandidateExists, bool Eligible, CandidateOpinion? Existing);

public interface IOpinionRepository
{
    Task<NominationResult<OpinionPage>> SearchAsync(Guid campaignId, OpinionSource source, string? term,
        int pageNumber, int pageSize, CancellationToken token);
    Task<NominationResult<OpinionStatus>> GetAsync(Guid campaignId, Guid candidateId, OpinionSource source, CancellationToken token);
    Task<NominationResult<OpinionSaved>> SaveAsync(Guid campaignId, Guid candidateId, OpinionSource source, byte[]? version,
        Func<OpinionWriteContext, NominationResult<CandidateOpinion>> change, CancellationToken token);
}

public sealed class OpinionService(IOpinionRepository repository, TimeProvider clock)
{
    private static NominationResult<OpinionSource> Department(OpinionActor actor)
    {
        if (actor.UserId == Guid.Empty)
            return new(default, NominationError.Unauthorized, "Tu sesión no es válida. Inicia sesión nuevamente.");
        var sources = new List<OpinionSource>();
        if (actor.Roles.Contains(AccountRoles.HumanResources)) sources.Add(OpinionSource.HumanResources);
        if (actor.Roles.Contains(AccountRoles.Safety)) sources.Add(OpinionSource.Safety);
        if (actor.Roles.Contains(AccountRoles.Quality)) sources.Add(OpinionSource.Quality);
        if (sources.Count != 1 || actor.Roles.Contains(AccountRoles.Administrator) || actor.Roles.Contains(AccountRoles.Sponsor))
            return new(default, NominationError.Forbidden, "Se requiere una cuenta con un único rol departamental, sin roles de administrador o patrocinador.");
        return new(sources[0]);
    }

    private static NominationResult<T> Failure<T>(NominationError? error, string? message) => new(default, error, message);
    private static NominationResult<T> Persistence<T>(NominationPersistenceException ex) =>
        new(default, ex.Error == NominationError.Concurrency ? NominationError.Concurrency : NominationError.Persistence,
            ex.Error == NominationError.Concurrency ? "La opinión cambió durante la operación. Vuelve a consultarla."
                : "No pudimos completar la operación de opiniones. Inténtalo nuevamente.");

    public async Task<NominationResult<OpinionPage>> SearchAsync(OpinionActor actor, Guid? campaignId, string? term,
        int pageNumber, int pageSize, CancellationToken token)
    {
        var department = Department(actor);
        if (!department.IsSuccess) return Failure<OpinionPage>(department.Error, department.Message);
        if (!campaignId.HasValue || campaignId == Guid.Empty)
            return Failure<OpinionPage>(NominationError.Validation, "Selecciona una convocatoria válida.");
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100 || ((long)pageNumber - 1) * pageSize > int.MaxValue || term?.Length > 100)
            return Failure<OpinionPage>(NominationError.Validation, "La página debe ser positiva, el tamaño entre 1 y 100 y la búsqueda de hasta 100 caracteres.");
        try { return await repository.SearchAsync(campaignId.Value, department.Value, term?.Trim(), pageNumber, pageSize, token); }
        catch (NominationPersistenceException ex) { return Persistence<OpinionPage>(ex); }
    }

    public async Task<NominationResult<OpinionStatus>> GetAsync(OpinionActor actor, Guid campaignId, Guid candidateId, CancellationToken token)
    {
        var department = Department(actor);
        if (!department.IsSuccess) return Failure<OpinionStatus>(department.Error, department.Message);
        if (campaignId == Guid.Empty || candidateId == Guid.Empty)
            return Failure<OpinionStatus>(NominationError.Validation, "Selecciona una convocatoria y un candidato válidos.");
        try { return await repository.GetAsync(campaignId, candidateId, department.Value, token); }
        catch (NominationPersistenceException ex) { return Persistence<OpinionStatus>(ex); }
    }

    public async Task<NominationResult<OpinionSaved>> SaveAsync(OpinionActor actor, Guid campaignId, Guid candidateId,
        string? content, string? version, CancellationToken token)
    {
        var department = Department(actor);
        if (!department.IsSuccess) return Failure<OpinionSaved>(department.Error, department.Message);
        if (campaignId == Guid.Empty || candidateId == Guid.Empty)
            return Failure<OpinionSaved>(NominationError.Validation, "Selecciona una convocatoria y un candidato válidos.");
        byte[]? bytes = null;
        if (version is not null)
        {
            bytes = new byte[8];
            if (!Convert.TryFromBase64String(version, bytes, out var length) || length != 8)
                return Failure<OpinionSaved>(NominationError.Validation, "La versión de la opinión no es válida.");
        }
        try
        {
            return await repository.SaveAsync(campaignId, candidateId, department.Value, bytes, state =>
            {
                if (state.Campaign is null) return Failure<CandidateOpinion>(NominationError.NotFound, "No encontramos la convocatoria.");
                if (!state.CandidateExists) return Failure<CandidateOpinion>(NominationError.NotFound, "No encontramos al candidato.");
                var now = clock.GetUtcNow();
                if (!state.Campaign.CanAcceptOpinions(now))
                    return Failure<CandidateOpinion>(NominationError.Concurrency, "La convocatoria no admite opiniones en este momento.");
                if (!state.Eligible)
                    return Failure<CandidateOpinion>(NominationError.Concurrency, "El candidato debe estar preseleccionado y conservar al menos una postulación válida.");
                try
                {
                    if (state.Existing is null)
                        return new(new CandidateOpinion(Guid.NewGuid(), campaignId, candidateId, department.Value, content!, actor.UserId, now));
                    if (state.Existing.Content != content?.Trim()) state.Existing.EditContent(content!, now);
                    return new(state.Existing);
                }
                catch (ArgumentException)
                { return Failure<CandidateOpinion>(NominationError.Validation, "Escribe una opinión de hasta 500 caracteres; no puede estar vacía."); }
            }, token);
        }
        catch (NominationPersistenceException ex) { return Persistence<OpinionSaved>(ex); }
    }
}
