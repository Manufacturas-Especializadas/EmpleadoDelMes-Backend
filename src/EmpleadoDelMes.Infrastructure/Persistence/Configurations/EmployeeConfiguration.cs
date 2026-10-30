using EmpleadoDelMes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

public sealed class EmployeeConfiguration : IEntityTypeConfiguration<Employee>
{
    public void Configure(EntityTypeBuilder<Employee> builder)
    {
        builder.ToTable("Employees");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PayrollNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.PayrollNumber).IsUnique();
        builder.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
    }
}
