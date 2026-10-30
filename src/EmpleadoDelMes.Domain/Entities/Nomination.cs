using EmpleadoDelMes.Domain.Enums;

namespace EmpleadoDelMes.Domain.Entities;

public sealed class Nomination
{
    // Rehidratación de persistencia; la creación de dominio usa el constructor público.
    private Nomination()
    {
        SponsorPayrollNumber = null!;
        SponsorFullName = null!;
        CandidatePayrollNumber = null!;
        CandidateFullName = null!;
        Justification = null!;
    }

    public Nomination(Guid id, Guid campaignId, Employee sponsor, Employee candidate,
        Guid createdByUserId, NominationQualities qualities, string justification, DateTimeOffset registeredAt)
    {
        ArgumentNullException.ThrowIfNull(sponsor);
        ArgumentNullException.ThrowIfNull(candidate);
        Id = DomainGuard.RequiredId(id, nameof(id));
        CampaignId = DomainGuard.RequiredId(campaignId, nameof(campaignId));
        CreatedByUserId = DomainGuard.RequiredId(createdByUserId, nameof(createdByUserId));
        if (sponsor.Id == candidate.Id)
        {
            throw new ArgumentException("El patrocinador no puede postularse a sí mismo.", nameof(candidate));
        }

        ValidateQualities(qualities);
        Justification = DomainGuard.RequiredText(justification, nameof(justification), 500);
        Qualities = qualities;
        SponsorEmployeeId = sponsor.Id;
        CandidateEmployeeId = candidate.Id;
        SponsorPayrollNumber = sponsor.PayrollNumber;
        SponsorFullName = sponsor.FullName;
        CandidatePayrollNumber = candidate.PayrollNumber;
        CandidateFullName = candidate.FullName;
        RegisteredAtUtc = registeredAt.ToUniversalTime();
    }

    public Guid Id { get; }
    public Guid CampaignId { get; private set; }
    public Guid SponsorEmployeeId { get; private set; }
    public Guid CandidateEmployeeId { get; private set; }
    public Guid CreatedByUserId { get; }
    public string SponsorPayrollNumber { get; private set; }
    public string SponsorFullName { get; private set; }
    public string CandidatePayrollNumber { get; private set; }
    public string CandidateFullName { get; private set; }
    public NominationQualities Qualities { get; private set; }
    public string Justification { get; private set; }
    public DateTimeOffset RegisteredAtUtc { get; }
    public bool IsCancelled { get; private set; }

    // Application deberá autorizar y registrar la auditoría en la misma operación.
    public void Correct(NominationQualities qualities, string justification)
    {
        ValidateQualities(qualities);
        var validatedJustification = DomainGuard.RequiredText(justification, nameof(justification), 500);
        Qualities = qualities;
        Justification = validatedJustification;
    }

    public void Cancel() => IsCancelled = true;

    // Las instantáneas se toman de los mismos empleados que aportan las identidades.
    // Application comprobará existencia, permisos, duplicados y efectos sobre preselecciones,
    // y guardará la auditoría en la misma transacción. Esta operación no modifica opiniones.
    public void Correct(Guid campaignId, Employee sponsor, Employee candidate,
        NominationQualities qualities, string justification)
    {
        ArgumentNullException.ThrowIfNull(sponsor);
        ArgumentNullException.ThrowIfNull(candidate);
        var validatedCampaignId = DomainGuard.RequiredId(campaignId, nameof(campaignId));
        if (sponsor.Id == candidate.Id)
        {
            throw new ArgumentException("El patrocinador no puede postularse a sí mismo.", nameof(candidate));
        }

        ValidateQualities(qualities);
        var validatedJustification = DomainGuard.RequiredText(justification, nameof(justification), 500);

        // Toda la validación precede a la modificación para evitar cambios parciales.
        CampaignId = validatedCampaignId;
        SponsorEmployeeId = sponsor.Id;
        SponsorPayrollNumber = sponsor.PayrollNumber;
        SponsorFullName = sponsor.FullName;
        CandidateEmployeeId = candidate.Id;
        CandidatePayrollNumber = candidate.PayrollNumber;
        CandidateFullName = candidate.FullName;
        Qualities = qualities;
        Justification = validatedJustification;
    }

    private static void ValidateQualities(NominationQualities qualities)
    {
        const NominationQualities valid = NominationQualities.Teamwork | NominationQualities.ProblemSolving
            | NominationQualities.Commitment | NominationQualities.Initiative;
        if (qualities == NominationQualities.None || (qualities & ~valid) != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(qualities), "Seleccione al menos una cualidad válida.");
        }
    }
}
