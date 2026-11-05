namespace EmpleadoDelMes.Application.Campaigns;

public sealed record CampaignSchedulePoint(int DayOffset, TimeOnly LocalTime)
{
    public const int MinimumDayOffset = -31;
    public const int MaximumDayOffset = 365;

    public DateTimeOffset ToUtc(DateOnly evaluationMonth, TimeZoneInfo zone)
    {
        if (DayOffset is < MinimumDayOffset or > MaximumDayOffset)
            throw new ArgumentException("Los días relativos deben estar entre -31 y 365.");
        if (evaluationMonth.Day != 1 || evaluationMonth == new DateOnly(9999, 12, 1))
            throw new ArgumentException("El mes evaluado debe permitir calcular el inicio del mes siguiente.");
        // AddMonths/AddDays enforce DateOnly bounds; no wraparound or silent date clamping.
        var local = evaluationMonth.AddMonths(1).AddDays(DayOffset).ToDateTime(LocalTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local) || zone.IsAmbiguousTime(local))
            throw new ArgumentException("El calendario contiene una hora local inexistente o ambigua. Ajusta la programación.");
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone));
    }
}

public sealed record CampaignSchedule(TimeZoneInfo TimeZone, CampaignSchedulePoint NominationOpening,
    CampaignSchedulePoint NominationClosing, CampaignSchedulePoint OpinionOpening, CampaignSchedulePoint OpinionClosing)
{
    public CampaignScheduledDates Calculate(DateOnly month)
    {
        var dates = new CampaignScheduledDates(NominationOpening.ToUtc(month, TimeZone), NominationClosing.ToUtc(month, TimeZone),
            OpinionOpening.ToUtc(month, TimeZone), OpinionClosing.ToUtc(month, TimeZone));
        if (dates.NominationOpening >= dates.NominationClosing || dates.OpinionOpening >= dates.OpinionClosing)
            throw new ArgumentException("La apertura de cada periodo debe ser anterior a su cierre.");
        return dates;
    }
}

public sealed record CampaignScheduledDates(DateTimeOffset NominationOpening, DateTimeOffset NominationClosing,
    DateTimeOffset OpinionOpening, DateTimeOffset OpinionClosing);
