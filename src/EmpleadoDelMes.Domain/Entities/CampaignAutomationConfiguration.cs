namespace EmpleadoDelMes.Domain.Entities;

public sealed record CampaignAutomationCalendar(
    int NominationOpeningDayOffset, TimeOnly NominationOpeningLocalTime,
    int NominationClosingDayOffset, TimeOnly NominationClosingLocalTime,
    int OpinionOpeningDayOffset, TimeOnly OpinionOpeningLocalTime,
    int OpinionClosingDayOffset, TimeOnly OpinionClosingLocalTime);

public sealed class CampaignAutomationConfiguration
{
    public int Id { get; private set; } = 1;
    public bool IsEnabled { get; private set; }
    public CampaignAutomationCalendar? Calendar { get; private set; }
    public DateTimeOffset? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Configure(bool enabled, CampaignAutomationCalendar calendar, Guid administrator, DateTimeOffset now)
    {
        if (administrator == Guid.Empty) throw new ArgumentException("Se requiere un administrador.");
        IsEnabled = enabled;
        Calendar = calendar;
        UpdatedAtUtc = now;
        UpdatedByUserId = administrator;
    }
}

public sealed class CampaignAutomationAuditEntry
{
    public Guid Id { get; set; }
    public Guid AdministratorId { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; }
    public string PreviousValues { get; set; } = "";
    public string NewValues { get; set; } = "";
}
