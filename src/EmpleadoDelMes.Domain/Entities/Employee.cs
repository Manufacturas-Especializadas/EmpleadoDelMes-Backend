namespace EmpleadoDelMes.Domain.Entities;

public sealed class Employee
{
    public Employee(Guid id, string payrollNumber, string fullName)
    {
        Id = DomainGuard.RequiredId(id, nameof(id));
        PayrollNumber = DomainGuard.RequiredText(payrollNumber, nameof(payrollNumber));
        FullName = DomainGuard.RequiredText(fullName, nameof(fullName));
    }

    public Guid Id { get; }
    public string PayrollNumber { get; }
    public string FullName { get; private set; }

    public void CorrectName(string fullName)
    {
        FullName = DomainGuard.RequiredText(fullName, nameof(fullName));
    }
}
