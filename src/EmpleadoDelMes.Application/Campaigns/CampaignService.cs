using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Application.Campaigns;

public sealed class CampaignService(ICampaignRepository repository, TimeProvider clock)
{
    public const int MaximumPageSize = 100;

    public Task<CampaignResult<CampaignDto>> ChangeAsync(Guid id, string? version, CampaignChange change,
        DateTimeOffset? openingAt = null, DateTimeOffset? closingAt = null, CancellationToken cancellationToken = default)
    {
        var bytes = new byte[8];
        if (id == Guid.Empty || !Enum.IsDefined(change))
            return Task.FromResult(Invalid<CampaignDto>("Selecciona una convocatoria y una operación válidas."));
        if (version is null || !Convert.TryFromBase64String(version, bytes, out var length) || length != 8)
            return Task.FromResult(Invalid<CampaignDto>("Vuelve a consultar la convocatoria e incluye su versión antes de guardar."));
        if (change is CampaignChange.ScheduleNominations or CampaignChange.ScheduleOpinions &&
            (openingAt is null || closingAt is null || openingAt >= closingAt))
            return Task.FromResult(Invalid<CampaignDto>("Indica la apertura y el cierre; la apertura debe ser anterior al cierre."));
        return Execute(() => repository.UpdateAsync(id, bytes, (campaign, others) =>
        {
            try
            {
                switch (change)
                {
                    case CampaignChange.ScheduleNominations: campaign.ScheduleNominations(openingAt!.Value, closingAt!.Value); break;
                    case CampaignChange.ScheduleOpinions: campaign.ScheduleOpinions(openingAt!.Value, closingAt!.Value); break;
                    case CampaignChange.OpenNominations: campaign.OpenNominations(); break;
                    case CampaignChange.CloseNominations: campaign.CloseNominations(); break;
                    case CampaignChange.AutomaticNominations: campaign.EnableAutomaticNominations(); break;
                    case CampaignChange.OpenOpinions: campaign.OpenOpinions(); break;
                    case CampaignChange.CloseOpinions: campaign.CloseOpinions(); break;
                    case CampaignChange.AutomaticOpinions: campaign.EnableAutomaticOpinions(); break;
                    case CampaignChange.Archive: if (!campaign.IsArchived) campaign.Archive(); break;
                    case CampaignChange.Reopen: if (campaign.IsArchived) campaign.Reopen(); break;
                }
            }
            catch (InvalidOperationException ex) { return new(false, CampaignError.Validation, ex.Message); }
            catch (ArgumentException) { return new(false, CampaignError.Validation, "La apertura debe ser anterior al cierre."); }

            // Closing/archiving and opinion-only changes never introduce nomination conflicts.
            if (change is CampaignChange.ScheduleNominations or CampaignChange.OpenNominations or CampaignChange.AutomaticNominations)
            {
                var now = clock.GetUtcNow();
                if (others.Any(other => CampaignNominationPolicy.Conflicts(campaign, other, now)))
                    return new(false, CampaignError.Concurrency,
                        "Las postulaciones entrarían en conflicto con otra convocatoria. Cierra su periodo o ajusta las fechas antes de continuar.");
            }
            return new(true);
        }, cancellationToken));
    }

    // The caller supplies the plant's local date. UTC is used only for creation timestamps.
    public Task<CampaignResult<CampaignDto>> EnsurePreviousMonthAsync(DateOnly localReferenceDate,
        CancellationToken cancellationToken = default)
    {
        if (localReferenceDate.Year == 1 && localReferenceDate.Month == 1)
            return Task.FromResult(Invalid<CampaignDto>("La fecha de referencia debe permitir calcular el mes anterior."));
        var month = FirstDay(localReferenceDate).AddMonths(-1);
        return Execute(async () =>
        {
            var existing = await repository.GetByMonthAsync(month, cancellationToken);
            if (existing is not null) return new CampaignResult<CampaignDto>(existing);
            var campaign = new Campaign(Guid.NewGuid(), month, clock.GetUtcNow());
            try { return new(await repository.CreateAsync(campaign, cancellationToken)); }
            catch (CampaignPersistenceException ex) when (ex.Error == CampaignError.Duplicate)
            {
                // Another instance won the unique-index race. Read its record without updating it.
                existing = await repository.GetByMonthAsync(month, cancellationToken);
                return existing is not null ? new(existing) : new(null, CampaignError.Concurrency,
                    "La información cambió durante la operación. Inténtalo nuevamente.");
            }
        });
    }

    public Task<CampaignResult<CampaignDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        id == Guid.Empty ? Task.FromResult(Invalid<CampaignDto>("Selecciona una convocatoria válida."))
            : Find(() => repository.GetByIdAsync(id, cancellationToken));

    public Task<CampaignResult<CampaignDto>> GetByMonthAsync(DateOnly evaluationMonth, CancellationToken cancellationToken = default) =>
        Find(() => repository.GetByMonthAsync(FirstDay(evaluationMonth), cancellationToken));

    public Task<CampaignResult<CampaignPage>> ListAsync(int pageNumber = 1, int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > MaximumPageSize || (long)(pageNumber - 1) * pageSize > int.MaxValue)
            return Task.FromResult(Invalid<CampaignPage>("Indica una página válida y un tamaño entre 1 y 100."));
        return Execute(async () => new CampaignResult<CampaignPage>(await repository.ListAsync(pageNumber, pageSize, cancellationToken)));
    }

    private static DateOnly FirstDay(DateOnly date) => new(date.Year, date.Month, 1);
    private static CampaignResult<T> Invalid<T>(string message) => new(default, CampaignError.Validation, message);
    private static Task<CampaignResult<CampaignDto>> Find(Func<Task<CampaignDto?>> query) => Execute(async () =>
    {
        var campaign = await query();
        return campaign is null ? new CampaignResult<CampaignDto>(null, CampaignError.NotFound, "No encontramos la convocatoria.") : new(campaign);
    });
    private static async Task<CampaignResult<T>> Execute<T>(Func<Task<CampaignResult<T>>> operation)
    {
        try { return await operation(); }
        catch (CampaignPersistenceException ex)
        {
            return new(default, ex.Error, ex.Error is CampaignError.Concurrency or CampaignError.Duplicate
                ? "La información cambió durante la operación. Inténtalo nuevamente."
                : "No pudimos completar la operación de convocatorias. Inténtalo nuevamente.");
        }
    }
}
