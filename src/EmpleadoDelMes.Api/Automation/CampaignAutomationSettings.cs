using System.Globalization;
using EmpleadoDelMes.Application.Campaigns;

namespace EmpleadoDelMes.Api.Automation;

public sealed record CampaignAutomationSettings(bool Enabled, TimeZoneInfo? TimeZone = null,
    int CheckIntervalMinutes = 60, DateOnly FirstEvaluationMonth = default, CampaignSchedule? Schedule = null)
{
    public static CampaignAutomationSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("CampaignAutomation");
        var enabledText = section["Enabled"];
        if (enabledText is null) return new(false);
        if (!bool.TryParse(enabledText, out var enabled))
            throw new InvalidOperationException("CampaignAutomation:Enabled debe ser true o false.");
        if (!enabled) return new(false);

        var intervalText = section["CheckIntervalMinutes"];
        var interval = 60;
        if (intervalText is not null && (!int.TryParse(intervalText, NumberStyles.None, CultureInfo.InvariantCulture, out interval) || interval is < 1 or > 1440))
            throw new InvalidOperationException("CampaignAutomation:CheckIntervalMinutes debe ser un entero entre 1 y 1440.");
        if (!DateOnly.TryParseExact(section["FirstEvaluationMonth"], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var first) || first.Day != 1)
            throw new InvalidOperationException("Configura CampaignAutomation:FirstEvaluationMonth con el primer día del mes, en formato yyyy-MM-dd.");

        var zoneId = section["TimeZoneId"];
        if (string.IsNullOrWhiteSpace(zoneId) || !TimeZoneInfo.TryConvertIanaIdToWindowsId(zoneId, out _))
            throw new InvalidOperationException("Configura CampaignAutomation:TimeZoneId con un identificador IANA reconocido.");
        try
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            var schedule = ReadSchedule(section.GetSection("Schedule"), zone, first);
            return new(true, zone, interval, first, schedule);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException("La zona horaria de la planta no está disponible. Revisa la configuración y los datos horarios del entorno.");
        }
    }

    private static CampaignSchedule? ReadSchedule(IConfigurationSection section, TimeZoneInfo zone, DateOnly first)
    {
        var text = section["Enabled"];
        if (text is null) return null;
        if (!bool.TryParse(text, out var enabled))
            throw new InvalidOperationException("CampaignAutomation:Schedule:Enabled debe ser true o false.");
        if (!enabled) return null;
        if (zone.Id != "America/Monterrey")
            throw new InvalidOperationException("La programación automática requiere CampaignAutomation:TimeZoneId=America/Monterrey.");

        CampaignSchedulePoint Point(string name)
        {
            if (!int.TryParse(section[name + "DayOffset"], NumberStyles.None, CultureInfo.InvariantCulture, out var day) || day < 0 || day > 365)
                throw new InvalidOperationException($"Configura CampaignAutomation:Schedule:{name}DayOffset como entero entre 0 y 365.");
            if (!TimeOnly.TryParseExact(section[name + "LocalTime"], "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                throw new InvalidOperationException($"Configura CampaignAutomation:Schedule:{name}LocalTime en formato HH:mm.");
            return new(day, time);
        }
        var schedule = new CampaignSchedule(zone, Point("NominationOpening"), Point("NominationClosing"),
            Point("OpinionOpening"), Point("OpinionClosing"));
        try
        {
            // Validate every supported evaluation month, including recovery and future calendar transitions.
            // 9997 is the last complete year allowing offsets up to 365 without DateOnly overflow.
            if (first.Year > 9997) throw new ArgumentException();
            CampaignScheduledDates? previous = null;
            for (var month = first; month.Year <= 9997; month = month.AddMonths(1))
            {
                var dates = schedule.Calculate(month);
                if (previous is not null && previous.NominationClosing > dates.NominationOpening)
                    throw new ArgumentException();
                previous = dates;
            }
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("La programación automática contiene intervalos inválidos, horas inexistentes o ambiguas, o postulaciones solapadas entre meses. Revisa CampaignAutomation:Schedule.");
        }
        return schedule;
    }
}
