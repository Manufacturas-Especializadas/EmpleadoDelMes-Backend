using System.Text.Json;
using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;

namespace EmpleadoDelMes.Application.CampaignAutomation;

public sealed class CampaignAutomationService(ICampaignAutomationStore store, ICampaignRepository repository,
    CampaignService campaigns, AutomationTechnicalSettings technical, TimeProvider clock, CampaignAutomationStatus status)
{
    public async Task<AutomationState> GetAsync(CancellationToken token)
    {
        var c = await store.ReadAsync(token);
        return new(ToDto(c), technical.Enabled, technical.AllowsGeneration && c.IsEnabled,
            technical.LegacyScheduleDetected, technical.TimeZone.Id, technical.FirstEvaluationMonth, status.Read());
    }

    public async Task<AutomationState> UpdateAsync(AutomationUpdateRequest request, Guid actor, CancellationToken token)
    {
        byte[] version;
        try { version = Convert.FromBase64String(request.Version ?? ""); }
        catch (FormatException) { throw InvalidVersion(); }
        if (version.Length != 8) throw InvalidVersion();
        var calendar = AutomationCalendarValidation.Parse(request.Calendar);
        var schedule = AutomationCalendarValidation.Schedule(calendar, technical.TimeZone);
        AutomationCalendarValidation.Validate(schedule, FirstMonth());
        await store.WithLockAsync(async () =>
        {
            var current = await store.ReadAsync(token);
            if (!current.RowVersion.SequenceEqual(version)) throw Conflict();
            if (request.IsEnabled && technical.LegacyScheduleDetected)
                throw new AutomationException(CampaignError.Validation,
                    "Retira todas las variables CampaignAutomation:Schedule antes de activar el calendario SQL.");
            // Validate against persisted campaigns inside the same lock used by campaign administration.
            var preview = request.IsEnabled ? await PreviewCoreAsync(schedule, 24, token) : null;
            if (preview is not null && preview.Months.Any(m => m.Conflicts.Count > 0))
                throw new AutomationException(CampaignError.Concurrency,
                    "El calendario entra en conflicto con convocatorias existentes. Consulta la vista previa y ajusta las fechas.");
            var previous = AuditValues(current);
            current.Configure(request.IsEnabled, calendar, actor, clock.GetUtcNow());
            await store.SaveAsync(current, version, new()
            {
                Id = Guid.NewGuid(), AdministratorId = actor, ChangedAtUtc = clock.GetUtcNow(),
                PreviousValues = previous, NewValues = AuditValues(current)
            }, token);
            return true;
        }, token);
        return await GetAsync(token);
    }

    public async Task<AutomationPreview> PreviewAsync(AutomationPreviewRequest request, CancellationToken token)
    {
        if (request.UpcomingMonths is < 1 or > 24)
            throw new AutomationException(CampaignError.Validation, "Solicita entre 1 y 24 meses próximos.");
        var schedule = AutomationCalendarValidation.Schedule(AutomationCalendarValidation.Parse(request.Calendar), technical.TimeZone);
        AutomationCalendarValidation.Validate(schedule, FirstMonth());
        return await PreviewCoreAsync(schedule, request.UpcomingMonths, token);
    }

    private async Task<AutomationPreview> PreviewCoreAsync(CampaignSchedule schedule, int upcoming, CancellationToken token)
    {
        var existing = await store.ReadCampaignsAsync(token);
        var byMonth = existing.ToDictionary(c => c.EvaluationMonth);
        var currentMonth = LocalMonth();
        if (currentMonth.Year > 9997 || (9997 - currentMonth.Year) * 12 + 12 - currentMonth.Month < upcoming - 1)
            throw new AutomationException(CampaignError.Validation, "El calendario excede el rango admitido.");
        var last = currentMonth.AddMonths(upcoming - 1);
        // Include far-future manually created records as conflict candidates as well.
        if (existing.Count > 0 && existing.Max(c => c.EvaluationMonth) > last)
            last = existing.Max(c => c.EvaluationMonth);
        if (last.Year > 9997) throw new AutomationException(CampaignError.Validation, "El calendario excede el rango admitido.");
        var months = new List<AutomationPreviewMonth>();
        for (var month = FirstMonth(); month <= last; month = month.AddMonths(1))
        {
            token.ThrowIfCancellationRequested();
            var dates = schedule.Calculate(month);
            byMonth.TryGetValue(month, out var found);
            var conflicts = new List<string>();
            if (found is null)
            {
                var candidate = CampaignService.BuildCampaign(month, clock.GetUtcNow(), schedule);
                foreach (var other in existing)
                    if (CampaignNominationPolicy.Conflicts(candidate, other, clock.GetUtcNow()))
                        conflicts.Add($"Postulaciones en conflicto con {other.EvaluationMonth:yyyy-MM}.");
            }
            months.Add(new(month, dates, found is not null, found?.Id, found?.IsArchived ?? false,
                found is null && month <= currentMonth, conflicts, found is null ? null : CampaignDto.FromDomain(found, now: clock.GetUtcNow())));
        }
        return new(technical.TimeZone.Id, months, months.Where(m => m.IsPending).Select(m => m.EvaluationMonth).ToArray());
    }

    public Task<AutomationGeneration> GenerateAsync(DateOnly month, CancellationToken token)
    {
        if (!technical.AllowsGeneration) return Task.FromResult(new AutomationGeneration(false, false, month));
        return store.WithLockAsync(async () =>
        {
            // Re-read under the cross-instance lock: disabling cannot race a later insert.
            var config = await store.ReadAsync(token);
            if (!config.IsEnabled) return new AutomationGeneration(false, false, month);
            if (month.Day != 1 || month < technical.FirstEvaluationMonth || month > LocalMonth() || month.Year > 9997)
                throw new AutomationException(CampaignError.Validation, "El mes no corresponde al intervalo de generación.");
            var schedule = AutomationCalendarValidation.Schedule(
                config.Calendar ?? throw new AutomationException(CampaignError.Validation, "El calendario SQL está incompleto."),
                technical.TimeZone);
            if (await repository.GetByMonthAsync(month, token) is not null)
                return new AutomationGeneration(false, true, month);
            var candidate = CampaignService.BuildCampaign(month, clock.GetUtcNow(), schedule);
            var existing = await store.ReadCampaignsAsync(token);
            var conflicts = existing.Where(other => CampaignNominationPolicy.Conflicts(candidate, other, clock.GetUtcNow()))
                .Select(other => other.EvaluationMonth.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            if (conflicts.Length > 0)
                throw new AutomationException(CampaignError.Concurrency,
                    $"No se puede generar {month:yyyy-MM}: sus postulaciones entran en conflicto con {string.Join(", ", conflicts)}. Revisa esas convocatorias; no se cerrarán automáticamente.");
            var result = await campaigns.EnsureAutomaticMonthAsync(month, schedule, token);
            if (!result.IsSuccess)
                throw new AutomationException(result.Error!.Value, result.Message ?? "No fue posible generar la convocatoria.");
            // This transaction holds the shared lock since the existence check.
            return new AutomationGeneration(true, true, month);
        }, token);
    }

    private DateOnly LocalMonth()
    {
        var date = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), technical.TimeZone);
        return new(date.Year, date.Month, 1);
    }
    private DateOnly FirstMonth() => technical.FirstEvaluationMonth ?? LocalMonth().AddMonths(-1);
    private static AutomationConfigurationDto ToDto(CampaignAutomationConfiguration c) =>
        new(c.IsEnabled, AutomationCalendarValidation.ToInput(c.Calendar), Convert.ToBase64String(c.RowVersion), c.UpdatedAtUtc, c.UpdatedByUserId);
    private static string AuditValues(CampaignAutomationConfiguration c) => JsonSerializer.Serialize(new
    {
        c.IsEnabled, Calendar = AutomationCalendarValidation.ToInput(c.Calendar), c.UpdatedAtUtc, c.UpdatedByUserId
    });
    private static AutomationException InvalidVersion() => new(CampaignError.Validation, "Envía la versión obtenida al consultar la configuración.");
    private static AutomationException Conflict() => new(CampaignError.Concurrency, "La configuración cambió. Vuelve a consultarla antes de guardar.");
}
