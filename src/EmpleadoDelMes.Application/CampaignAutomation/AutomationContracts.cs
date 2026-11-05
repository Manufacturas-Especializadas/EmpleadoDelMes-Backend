using System.Text.Json.Serialization;
using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;

namespace EmpleadoDelMes.Application.CampaignAutomation;

public sealed record AutomationTechnicalSettings(bool Enabled, TimeZoneInfo TimeZone,
    DateOnly? FirstEvaluationMonth, bool LegacyScheduleDetected = false)
{
    public bool AllowsGeneration => Enabled && !LegacyScheduleDetected && FirstEvaluationMonth.HasValue;
}

public sealed record AutomationCalendarInput(
    int? NominationOpeningDayOffset, string? NominationOpeningLocalTime,
    int? NominationClosingDayOffset, string? NominationClosingLocalTime,
    int? OpinionOpeningDayOffset, string? OpinionOpeningLocalTime,
    int? OpinionClosingDayOffset, string? OpinionClosingLocalTime);

public sealed record AutomationUpdateRequest([property: JsonRequired] bool IsEnabled, AutomationCalendarInput? Calendar, string? Version);
public sealed record AutomationPreviewRequest(AutomationCalendarInput? Calendar, int UpcomingMonths = 3);
public sealed record AutomationConfigurationDto(bool IsEnabled, AutomationCalendarInput? Calendar,
    string Version, DateTimeOffset? UpdatedAtUtc, Guid? UpdatedByUserId);
public sealed record AutomationStatus(DateTimeOffset? LastCheckAtUtc = null,
    DateTimeOffset? LastGenerationAtUtc = null, DateOnly? LastGeneratedMonth = null, string? Error = null);
public sealed record AutomationState(AutomationConfigurationDto Configuration, bool TechnicalEnabled,
    bool EffectiveEnabled, bool LegacyScheduleDetected, string TimeZone, DateOnly? FirstEvaluationMonth,
    AutomationStatus OperationalStatus, string StatusScope = "CurrentInstance");
public sealed record AutomationPreviewMonth(DateOnly EvaluationMonth, CampaignScheduledDates Dates,
    bool AlreadyExists, Guid? ExistingCampaignId, bool IsArchived, bool IsPending,
    IReadOnlyList<string> Conflicts, CampaignDto? ExistingCampaign);
public sealed record AutomationPreview(string TimeZone, IReadOnlyList<AutomationPreviewMonth> Months,
    IReadOnlyList<DateOnly> PendingMonths);
public sealed record AutomationGeneration(bool Created, bool AdministrativelyEnabled, DateOnly Month);

// All writes and the re-read of configuration share the campaign administration lock.
// A failed callback must throw so the store rolls back the entire transaction.
public interface ICampaignAutomationStore
{
    Task<T> WithLockAsync<T>(Func<Task<T>> action, CancellationToken token);
    Task<CampaignAutomationConfiguration> ReadAsync(CancellationToken token);
    Task<IReadOnlyList<Campaign>> ReadCampaignsAsync(CancellationToken token);
    Task SaveAsync(CampaignAutomationConfiguration configuration, byte[] expectedVersion,
        CampaignAutomationAuditEntry audit, CancellationToken token);
}

public sealed class AutomationException(CampaignError error, string message) : Exception(message)
{
    public CampaignError Error { get; } = error;
}

// Per-process telemetry: never presented as durable or shared across App Service instances.
public sealed class CampaignAutomationStatus
{
    private readonly object gate = new();
    private AutomationStatus value = new();
    public AutomationStatus Read() { lock (gate) return value; }
    public void Checked(DateTimeOffset now) { lock (gate) value = value with { LastCheckAtUtc = now, Error = null }; }
    public void Generated(DateOnly month, DateTimeOffset now)
    { lock (gate) value = value with { LastGenerationAtUtc = now, LastGeneratedMonth = month }; }
    public void Failed(string message) { lock (gate) value = value with { Error = message }; }
}
