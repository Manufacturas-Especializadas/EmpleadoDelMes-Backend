namespace EmpleadoDelMes.Domain.Entities;

public sealed class SponsorEmployeeAssignment
{
    private SponsorEmployeeAssignment() { }
    public SponsorEmployeeAssignment(Guid id, Guid sponsorEmployeeId, Guid employeeId, DateTimeOffset createdAt)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        SponsorEmployeeId = DomainGuard.RequiredId(sponsorEmployeeId, nameof(sponsorEmployeeId));
        EmployeeId = DomainGuard.RequiredId(employeeId, nameof(employeeId));
        if (SponsorEmployeeId == EmployeeId) throw new ArgumentException("No se permite la autoasignación.");
        IsActive = true;
        CreatedAtUtc = UpdatedAtUtc = createdAt.ToUniversalTime();
    }
    public Guid Id { get; }
    public Guid SponsorEmployeeId { get; }
    public Guid EmployeeId { get; }
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public void Withdraw(DateTimeOffset now)
    {
        if (!IsActive) return;
        if (now < CreatedAtUtc) throw new ArgumentException("La fecha de retiro no puede ser anterior a la creación.");
        IsActive = false;
        UpdatedAtUtc = now.ToUniversalTime();
    }
}
