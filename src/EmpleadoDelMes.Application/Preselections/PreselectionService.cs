using EmpleadoDelMes.Application.Nominations;
using EmpleadoDelMes.Domain.Entities;

namespace EmpleadoDelMes.Application.Preselections;

public sealed record PreselectionDto(Guid CampaignId, Guid CandidateEmployeeId, bool IsSelected,
    Guid LastChangedByUserId, DateTimeOffset LastChangedAtUtc, string Version);
public sealed record PreselectionChangeContext(bool? IsArchived, bool CandidateExists,
    bool HasValidNomination, Preselection? Existing);

public interface IPreselectionRepository
{
    Task<NominationResult<PreselectionDto>> ChangeAsync(Guid campaignId, Guid candidateId, byte[]? version,
        Func<PreselectionChangeContext, NominationResult<Preselection>> change, CancellationToken token);
}

public sealed class PreselectionService(IPreselectionRepository repository, TimeProvider clock)
{
    public async Task<NominationResult<PreselectionDto>> ChangeAsync(Guid campaignId, Guid candidateId,
        Guid actorId, bool select, string? version, CancellationToken token = default)
    {
        if (actorId == Guid.Empty)
            return new(null, NominationError.Unauthorized, "Tu sesión no es válida. Inicia sesión nuevamente.");
        if (campaignId == Guid.Empty || candidateId == Guid.Empty)
            return new(null, NominationError.Validation, "Selecciona una convocatoria y un candidato válidos.");
        byte[]? bytes = null;
        if (version is not null)
        {
            bytes = new byte[8];
            if (!Convert.TryFromBase64String(version, bytes, out var length) || length != 8)
                return new(null, NominationError.Validation, "La versión de la preselección no es válida.");
        }
        try
        {
            return await repository.ChangeAsync(campaignId, candidateId, bytes, state =>
            {
                if (state.IsArchived is null)
                    return new(null, NominationError.NotFound, "No encontramos la convocatoria.");
                if (state.IsArchived.Value)
                    return new(null, NominationError.Concurrency, "No se puede modificar la preselección de una convocatoria archivada.");
                if (!state.CandidateExists)
                    return new(null, NominationError.NotFound, "No encontramos al candidato.");
                if (select && !state.HasValidNomination)
                    return new(null, NominationError.Concurrency, "El candidato necesita al menos una postulación válida en esta convocatoria.");
                var existing = state.Existing;
                if (existing is null)
                    return select
                        ? new(new Preselection(campaignId, candidateId, actorId, clock.GetUtcNow()))
                        : new(null, NominationError.NotFound, "No encontramos la preselección.");
                if (existing.IsSelected == select) return new(existing);
                if (bytes is null)
                    return new(null, NominationError.Validation, "Incluye la versión actual de la preselección para modificarla.");
                if (select) existing.Select(actorId, clock.GetUtcNow());
                else existing.Withdraw(actorId, clock.GetUtcNow());
                return new(existing);
            }, token);
        }
        catch (NominationPersistenceException ex)
        {
            return ex.Error == NominationError.Concurrency
                ? new(null, NominationError.Concurrency, "La preselección cambió durante la operación. Vuelve a consultarla.")
                : new(null, NominationError.Persistence, "No pudimos guardar la preselección. Inténtalo nuevamente.");
        }
    }
}
