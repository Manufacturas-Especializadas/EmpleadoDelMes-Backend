using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Application.Campaigns;

public static class CampaignNominationPolicy
{
    public static bool Conflicts(Campaign candidate, Campaign other, DateTimeOffset now)
    {
        if (candidate.Id == other.Id || candidate.IsArchived || other.IsArchived) return false;
        var left = candidate.NominationPeriod;
        var right = other.NominationPeriod;
        if (left.Mode == CampaignPeriodMode.ManuallyClosed || right.Mode == CampaignPeriodMode.ManuallyClosed) return false;
        if (left.Mode == CampaignPeriodMode.ManuallyOpen && right.Mode == CampaignPeriodMode.ManuallyOpen) return true;
        // Manual opening has no end: reject every automatic window that has not finished yet.
        if (left.Mode == CampaignPeriodMode.ManuallyOpen) return right.ScheduledClosingAtUtc > now;
        if (right.Mode == CampaignPeriodMode.ManuallyOpen) return left.ScheduledClosingAtUtc > now;
        // Half-open intervals [opening, closing); adjacent windows are valid.
        return left.ScheduledOpeningAtUtc < right.ScheduledClosingAtUtc && right.ScheduledOpeningAtUtc < left.ScheduledClosingAtUtc;
    }
}
