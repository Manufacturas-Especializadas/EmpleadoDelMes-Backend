using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.HasOne<Employee>().WithOne().HasForeignKey<ApplicationUser>(x => x.EmployeeId)
            .IsRequired(false).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("[EmployeeId] IS NOT NULL");
    }
}
