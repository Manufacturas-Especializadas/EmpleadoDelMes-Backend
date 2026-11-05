using System.Globalization;
using EmpleadoDelMes.Application.CampaignAutomation;

namespace EmpleadoDelMes.Api.Automation;

public sealed record CampaignAutomationSettings(bool Enabled, TimeZoneInfo? TimeZone = null,
    int CheckIntervalMinutes = 60, DateOnly FirstEvaluationMonth = default, bool LegacyScheduleDetected = false)
{
    public AutomationTechnicalSettings ToTechnical() => new(Enabled,
        TimeZone ?? TimeZoneInfo.FindSystemTimeZoneById("America/Monterrey"),
        FirstEvaluationMonth == default ? null : FirstEvaluationMonth, LegacyScheduleDetected);

    public static CampaignAutomationSettings Read(IConfiguration configuration)
    {
        var section = configuration.GetSection("CampaignAutomation");
        var enabledText = section["Enabled"];
        var enabled = false;
        if (enabledText is not null && !bool.TryParse(enabledText, out enabled))
            throw new InvalidOperationException("CampaignAutomation:Enabled debe ser true o false.");
        // Any remaining legacy key blocks generation, even Schedule:Enabled=false.
        var legacy = section.GetSection("Schedule").Exists();
        var zoneId = section["TimeZoneId"];
        if (enabled && zoneId != "America/Monterrey")
            throw new InvalidOperationException("CampaignAutomation:TimeZoneId debe ser America/Monterrey.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Monterrey");
        var interval = 60;
        var intervalText = section["CheckIntervalMinutes"];
        if (enabled && intervalText is not null && (!int.TryParse(intervalText, NumberStyles.None,
            CultureInfo.InvariantCulture, out interval) || interval is < 1 or > 1440))
            throw new InvalidOperationException("CampaignAutomation:CheckIntervalMinutes debe estar entre 1 y 1440.");
        var validFirst = DateOnly.TryParseExact(section["FirstEvaluationMonth"], "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var first) && first.Day == 1 && first.Year <= 9997;
        if (enabled && !validFirst)
            throw new InvalidOperationException("Configura FirstEvaluationMonth con el primer día del mes yyyy-MM-dd, hasta 9997.");
        return new(enabled, zone, interval, validFirst ? first : default, legacy);
    }
}
