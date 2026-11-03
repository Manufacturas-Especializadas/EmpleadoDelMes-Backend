using System.Globalization;

namespace EmpleadoDelMes.Api.Automation;

public sealed record CampaignAutomationSettings(bool Enabled, TimeZoneInfo? TimeZone = null,
    int CheckIntervalMinutes = 60, DateOnly FirstEvaluationMonth = default)
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
            return new(true, TimeZoneInfo.FindSystemTimeZoneById(zoneId), interval, first);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException("La zona horaria de la planta no está disponible. Revisa la configuración y los datos horarios del entorno.");
        }
    }
}
