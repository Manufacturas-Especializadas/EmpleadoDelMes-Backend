using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Domain.ValueObjects;

// Inmutable: los cambios se realizan exclusivamente mediante Campaign.
public sealed class CampaignPeriod
{
    internal CampaignPeriod(
        DateTimeOffset? scheduledOpeningAtUtc = null,
        DateTimeOffset? scheduledClosingAtUtc = null,
        CampaignPeriodMode mode = CampaignPeriodMode.ManuallyClosed)
    {
        ScheduledOpeningAtUtc = scheduledOpeningAtUtc;
        ScheduledClosingAtUtc = scheduledClosingAtUtc;
        Mode = mode;
    }

    public DateTimeOffset? ScheduledOpeningAtUtc { get; }
    public DateTimeOffset? ScheduledClosingAtUtc { get; }
    public CampaignPeriodMode Mode { get; }

    internal CampaignPeriod WithSchedule(DateTimeOffset openingAt, DateTimeOffset closingAt)
    {
        if (openingAt >= closingAt)
        {
            throw new ArgumentException("La apertura debe ser anterior al cierre.", nameof(closingAt));
        }

        return new CampaignPeriod(openingAt.ToUniversalTime(), closingAt.ToUniversalTime(), Mode);
    }

    internal CampaignPeriod WithMode(CampaignPeriodMode mode)
    {
        if (mode == CampaignPeriodMode.Automatic && ScheduledOpeningAtUtc is null)
        {
            throw new InvalidOperationException("Configure el horario antes de habilitar el modo automático.");
        }

        return new CampaignPeriod(ScheduledOpeningAtUtc, ScheduledClosingAtUtc, mode);
    }

    internal bool IsOpen(DateTimeOffset now) => Mode switch
    {
        CampaignPeriodMode.ManuallyOpen => true,
        CampaignPeriodMode.Automatic => now >= ScheduledOpeningAtUtc && now < ScheduledClosingAtUtc,
        _ => false
    };
}
