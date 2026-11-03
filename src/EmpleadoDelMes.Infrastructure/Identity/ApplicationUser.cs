using Microsoft.AspNetCore.Identity;

namespace EmpleadoDelMes.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser<Guid>
{
    public bool MustChangePassword { get; set; }

    public Guid? EmployeeId { get; set; }
    public bool IsActive { get; set; } = true;
}
