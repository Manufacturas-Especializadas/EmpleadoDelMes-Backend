using System.Globalization;
using EmpleadoDelMes.Application.Campaigns;
using EmpleadoDelMes.Domain.Entities;

namespace EmpleadoDelMes.Application.CampaignAutomation;

public static class AutomationCalendarValidation
{
    public static CampaignAutomationCalendar Parse(AutomationCalendarInput? input)
    {
        if (input is null) throw Invalid();
        int Day(int? value) => value is >= 0 and <= 365 ? value.Value : throw Invalid();
        TimeOnly Time(string? value) => TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var time) ? time : throw Invalid();
        return new(Day(input.NominationOpeningDayOffset), Time(input.NominationOpeningLocalTime),
            Day(input.NominationClosingDayOffset), Time(input.NominationClosingLocalTime),
            Day(input.OpinionOpeningDayOffset), Time(input.OpinionOpeningLocalTime),
            Day(input.OpinionClosingDayOffset), Time(input.OpinionClosingLocalTime));
    }

    public static CampaignSchedule Schedule(CampaignAutomationCalendar calendar, TimeZoneInfo zone)
    {
        if (zone.Id != "America/Monterrey") throw Invalid();
        return new(zone, new(calendar.NominationOpeningDayOffset, calendar.NominationOpeningLocalTime),
            new(calendar.NominationClosingDayOffset, calendar.NominationClosingLocalTime),
            new(calendar.OpinionOpeningDayOffset, calendar.OpinionOpeningLocalTime),
            new(calendar.OpinionClosingDayOffset, calendar.OpinionClosingLocalTime));
    }

    public static void Validate(CampaignSchedule schedule, DateOnly first)
    {
        try
        {
            if (first.Year > 9997) throw Invalid();
            CampaignScheduledDates? previous = null;
            // Validate all supported months, including historical DST and future Gregorian transitions.
            for (var month = first; month.Year <= 9997; month = month.AddMonths(1))
            {
                var dates = schedule.Calculate(month);
                if (previous is not null && previous.NominationClosing > dates.NominationOpening) throw Invalid();
                previous = dates;
            }
        }
        catch (ArgumentException) { throw Invalid(); }
    }

    public static AutomationCalendarInput? ToInput(CampaignAutomationCalendar? c) => c is null ? null :
        new(c.NominationOpeningDayOffset, c.NominationOpeningLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            c.NominationClosingDayOffset, c.NominationClosingLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            c.OpinionOpeningDayOffset, c.OpinionOpeningLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture),
            c.OpinionClosingDayOffset, c.OpinionClosingLocalTime.ToString("HH:mm", CultureInfo.InvariantCulture));

    private static AutomationException Invalid() => new(CampaignError.Validation,
        "Completa los cuatro horarios HH:mm y desplazamientos de 0 a 365 días. Cada apertura debe preceder al cierre; las postulaciones mensuales no pueden solaparse ni usar horas ambiguas o inexistentes.");
}
