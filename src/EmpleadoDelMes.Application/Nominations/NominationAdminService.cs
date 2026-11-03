
using System.Text.Json;
using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Application.Nominations;

public sealed class NominationAdminService(
    INominationAdminRepository repository, TimeProvider clock)
{
    public async Task<NominationResult<NominationCandidatePage>> SearchCandidatesAsync(
        Guid? campaignId, string? term, int pageNumber = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!campaignId.HasValue || campaignId.Value == Guid.Empty)
            return new(null, NominationError.Validation, "Selecciona una convocatoria válida; la convocatoria es obligatoria.");
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100 ||
            ((long)pageNumber - 1) * pageSize > int.MaxValue)
            return new(null, NominationError.Validation,
                "La paginación no es válida. El tamaño de página debe estar entre 1 y 100.");
        if (term?.Length > 100)
            return new(null, NominationError.Validation, "La búsqueda no puede superar 100 caracteres.");

        var search = new NominationCandidateSearch(campaignId.Value,
            string.IsNullOrWhiteSpace(term) ? null : term.Trim(), pageNumber, pageSize);
        try
        {
            var page = await repository.SearchCandidatesAsync(search, cancellationToken);
            return page is null
                ? new(null, NominationError.NotFound, "No encontramos la convocatoria.")
                : new(page);
        }
        catch (NominationPersistenceException)
        {
            return new(null, NominationError.Persistence, "No pudimos consultar los candidatos. Inténtalo nuevamente.");
        }
    }

    public async Task<NominationResult<NominationAdminDetail>> CancelAsync(Guid id, Guid actorUserId,
        string? version, string? reason, CancellationToken token = default)
    {
        if (actorUserId == Guid.Empty)
            return new(null, NominationError.Unauthorized, "Tu sesión no es válida. Inicia sesión nuevamente.");
        if (id == Guid.Empty)
            return new(null, NominationError.Validation, "Selecciona una postulación válida.");
        var bytes = new byte[8];
        if (version is null || !Convert.TryFromBase64String(version, bytes, out var length) || length != 8)
            return new(null, NominationError.Validation, "Consulta la postulación e incluye una versión válida antes de anularla.");
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 1000)
            return new(null, NominationError.Validation, "Indica un motivo de anulación de entre 1 y 1000 caracteres.");

        try
        {
            return await repository.CancelAsync(id, bytes, (nomination, isArchived) =>
            {
                if (nomination.IsCancelled)
                    return new(null, NominationError.Concurrency, "La postulación ya está anulada.");
                if (isArchived)
                    return new(null, NominationError.Concurrency, "No se puede anular una postulación de una convocatoria archivada.");

                nomination.Cancel();
                return new(new NominationAuditEntry(Guid.NewGuid(), nomination.Id, actorUserId,
                    NominationAuditAction.Cancellation, clock.GetUtcNow(), normalizedReason));
            }, token);
        }
        catch (NominationPersistenceException ex)
        {
            return new(null, ex.Error == NominationError.Concurrency ? NominationError.Concurrency : NominationError.Persistence,
                ex.Error == NominationError.Concurrency
                    ? "La postulación cambió durante la operación. Vuelve a consultarla antes de anularla."
                    : "No pudimos guardar la anulación. Inténtalo nuevamente.");
        }
    }

    public async Task<NominationResult<NominationAdminDetail>> GetByIdAsync(Guid id, CancellationToken token = default)
    {
        if (id == Guid.Empty) return new(null, NominationError.Validation, "Selecciona una postulación válida.");
        try
        {
            var detail = await repository.GetByIdAsync(id, token);
            return detail is null ? new(null, NominationError.NotFound, "No encontramos la postulación.") : new(detail);
        }
        catch (NominationPersistenceException)
        {
            return new(null, NominationError.Persistence, "No pudimos consultar la postulación. Inténtalo nuevamente.");
        }
    }

    public async Task<NominationResult<NominationCorrectionResult>> CorrectAsync(Guid id, Guid actorUserId, string? version,
        NominationQualities qualities, string? justification, string? reason, CancellationToken token = default)
    {
        if (actorUserId == Guid.Empty) return new(null, NominationError.Unauthorized, "Tu sesión no es válida. Inicia sesión nuevamente.");
        if (id == Guid.Empty) return new(null, NominationError.Validation, "Selecciona una postulación válida.");
        var bytes = new byte[8];
        if (version is null || !Convert.TryFromBase64String(version, bytes, out var length) || length != 8)
            return new(null, NominationError.Validation, "Consulta la postulación e incluye una versión válida antes de corregirla.");
        var normalizedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedReason) || normalizedReason.Length > 1000)
            return new(null, NominationError.Validation, "Indica un motivo de corrección de entre 1 y 1000 caracteres.");
        try
        {
            return await repository.CorrectAsync(id, bytes, (nomination, isArchived) =>
            {
                if (nomination.IsCancelled)
                    return new(null, NominationError.Concurrency, "No se puede corregir una postulación anulada.");
                if (isArchived)
                    return new(null, NominationError.Concurrency, "No se puede corregir una postulación de una convocatoria archivada.");
                var beforeQualities = nomination.Qualities;
                var beforeJustification = nomination.Justification;
                try { nomination.Correct(qualities, justification!); }
                catch (ArgumentException ex)
                {
                    return new(null, NominationError.Validation, ex.ParamName == "qualities"
                        ? "Selecciona al menos una de las cuatro cualidades disponibles."
                        : "Escribe una justificación de máximo 500 caracteres; no puede estar vacía.");
                }
                if (beforeQualities == nomination.Qualities && beforeJustification == nomination.Justification)
                    return new(null); // Successful no-op, not a fictitious audit event.
                var details = JsonSerializer.Serialize(new
                {
                    Before = new { Qualities = (int)beforeQualities, Justification = beforeJustification },
                    After = new { Qualities = (int)nomination.Qualities, nomination.Justification }
                });
                return new(new NominationAuditEntry(Guid.NewGuid(), nomination.Id, actorUserId,
                    NominationAuditAction.Correction, clock.GetUtcNow(), normalizedReason, details));
            }, token);
        }
        catch (NominationPersistenceException ex)
        {
            return new(null, ex.Error == NominationError.Concurrency ? NominationError.Concurrency : NominationError.Persistence,
                ex.Error == NominationError.Concurrency
                    ? "La postulación cambió durante la operación. Vuelve a consultarla antes de corregirla."
                    : "No pudimos guardar la corrección. Inténtalo nuevamente.");
        }
    }

    public async Task<NominationResult<NominationAdminPage>> SearchAsync(
        Guid? campaignId,
        string? term,
        bool? isCancelled,
        int pageNumber = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (campaignId == Guid.Empty)
        {
            return new(
                null,
                NominationError.Validation,
                "Selecciona una convocatoria válida.");
        }

        if (pageNumber < 1 || pageSize < 1 || pageSize > 100 ||
            ((long)pageNumber - 1) * pageSize > int.MaxValue)
        {
            return new(
                null,
                NominationError.Validation,
                "La paginación no es válida. " +
                "El tamaño de página debe estar entre 1 y 100.");
        }

        var normalizedTerm = term?.Trim();

        if (normalizedTerm?.Length > 100)
        {
            return new(
                null,
                NominationError.Validation,
                "La búsqueda no puede superar 100 caracteres.");
        }

        var search = new NominationAdminSearch(
            campaignId,
            string.IsNullOrWhiteSpace(normalizedTerm)
                ? null
                : normalizedTerm,
            isCancelled,
            pageNumber,
            pageSize);

        try
        {
            var page = await repository.SearchAsync(
                search,
                cancellationToken);

            return new(page);
        }
        catch (NominationPersistenceException)
        {
            return new(
                null,
                NominationError.Persistence,
                "No pudimos consultar las postulaciones. " +
                "Inténtalo nuevamente.");
        }
    }
}
