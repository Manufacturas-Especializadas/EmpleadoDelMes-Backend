using EmpleadoDelMes.Domain.Enums;
using EmpleadoDelMes.Domain.ValueObjects;

namespace EmpleadoDelMes.Domain.Entities;

public sealed class Campaign
{
    // Rehidratación de persistencia; la creación de dominio usa el constructor público.
    private Campaign()
    {
    }

    public Campaign(Guid id, DateOnly evaluationMonth, DateTimeOffset createdAt)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        EvaluationMonth = new DateOnly(evaluationMonth.Year, evaluationMonth.Month, 1);
        CreatedAtUtc = createdAt.ToUniversalTime();
    }

    public Guid Id { get; }
    public DateOnly EvaluationMonth { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public bool IsArchived { get; private set; }
    public CampaignPeriod NominationPeriod { get; private set; } = new();
    public CampaignPeriod OpinionPeriod { get; private set; } = new();

    public bool CanAcceptNominations(DateTimeOffset now) => !IsArchived && NominationPeriod.IsOpen(now);
    public bool CanAcceptOpinions(DateTimeOffset now) => !IsArchived && OpinionPeriod.IsOpen(now);

    public void ScheduleNominations(DateTimeOffset openingAt, DateTimeOffset closingAt)
    {
        NominationPeriod = NominationPeriod.WithSchedule(openingAt, closingAt);
    }

    public void ScheduleOpinions(DateTimeOffset openingAt, DateTimeOffset closingAt)
    {
        OpinionPeriod = OpinionPeriod.WithSchedule(openingAt, closingAt);
    }

    public void EnableAutomaticNominations()
    {
        EnsureNotArchived();
        NominationPeriod = NominationPeriod.WithMode(CampaignPeriodMode.Automatic);
    }

    public void EnableAutomaticOpinions()
    {
        EnsureNotArchived();
        OpinionPeriod = OpinionPeriod.WithMode(CampaignPeriodMode.Automatic);
    }

    public void OpenNominations()
    {
        EnsureNotArchived();
        NominationPeriod = NominationPeriod.WithMode(CampaignPeriodMode.ManuallyOpen);
    }

    public void OpenOpinions()
    {
        EnsureNotArchived();
        OpinionPeriod = OpinionPeriod.WithMode(CampaignPeriodMode.ManuallyOpen);
    }

    public void CloseNominations() => NominationPeriod = NominationPeriod.WithMode(CampaignPeriodMode.ManuallyClosed);
    public void CloseOpinions() => OpinionPeriod = OpinionPeriod.WithMode(CampaignPeriodMode.ManuallyClosed);

    public void Archive()
    {
        CloseNominations();
        CloseOpinions();
        IsArchived = true;
    }

    public void Reopen()
    {
        CloseNominations();
        CloseOpinions();
        IsArchived = false;
    }

    private void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new InvalidOperationException("Una convocatoria archivada no permite habilitar capturas.");
        }
    }
}
