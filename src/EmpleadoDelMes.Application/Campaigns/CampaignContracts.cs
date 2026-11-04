using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.Enums;
using EmpleadoDelMes.Domain.ValueObjects;

namespace EmpleadoDelMes.Application.Campaigns;

public sealed record CampaignPeriodDto(CampaignPeriodMode Mode, DateTimeOffset? ScheduledOpeningAtUtc,
    DateTimeOffset? ScheduledClosingAtUtc)
{
    public static CampaignPeriodDto FromDomain(CampaignPeriod period) =>
        new(period.Mode, period.ScheduledOpeningAtUtc, period.ScheduledClosingAtUtc);
}

public sealed record CampaignDto(Guid Id, DateOnly EvaluationMonth, DateTimeOffset CreatedAtUtc,
    bool IsArchived, CampaignPeriodDto NominationPeriod, CampaignPeriodDto OpinionPeriod,
    string Version = "", bool CanAcceptNominations = false, bool CanAcceptOpinions = false)
{
    public static CampaignDto FromDomain(Campaign campaign, string version = "", DateTimeOffset now = default) => new(campaign.Id, campaign.EvaluationMonth,
        campaign.CreatedAtUtc, campaign.IsArchived, CampaignPeriodDto.FromDomain(campaign.NominationPeriod),
        CampaignPeriodDto.FromDomain(campaign.OpinionPeriod), version, campaign.CanAcceptNominations(now), campaign.CanAcceptOpinions(now));
}

public enum CampaignChange
{
    ScheduleNominations, ScheduleOpinions, OpenNominations, CloseNominations, AutomaticNominations,
    OpenOpinions, CloseOpinions, AutomaticOpinions, Archive, Reopen
}

public sealed record CampaignPage(IReadOnlyList<CampaignDto> Items, int Total, int PageNumber, int PageSize);
public sealed record OpinionCampaignDto(Guid Id, DateOnly EvaluationMonth, bool CanAcceptOpinions, bool IsArchived);
public sealed record OpinionCampaignPage(IReadOnlyList<OpinionCampaignDto> Items, int Total, int PageNumber, int PageSize);
public enum CampaignError { Validation, NotFound, Duplicate, Concurrency, Persistence }
public sealed record CampaignResult<T>(T? Value, CampaignError? Error = null, string? Message = null)
{
    public bool IsSuccess => Error is null;
}

public sealed class CampaignPersistenceException(CampaignError error, Exception? inner = null)
    : Exception("Campaign persistence operation failed.", inner)
{
    public CampaignError Error { get; } = error;
}

public interface ICampaignRepository
{
    Task<CampaignDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<CampaignDto?> GetByMonthAsync(DateOnly evaluationMonth, CancellationToken cancellationToken);
    Task<CampaignDto> CreateAsync(Campaign campaign, CancellationToken cancellationToken);
    Task<CampaignPage> ListAsync(int pageNumber, int pageSize, CancellationToken cancellationToken);
    // The mutation and cross-campaign validation must execute under the same exclusive transaction.
    Task<CampaignResult<CampaignDto>> UpdateAsync(Guid id, byte[] expectedVersion,
        Func<Campaign, IReadOnlyList<Campaign>, CampaignResult<bool>> change, CancellationToken cancellationToken);
}
