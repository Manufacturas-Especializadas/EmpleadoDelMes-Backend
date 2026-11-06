using EmpleadoDelMes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

public sealed class SponsorEmployeeAssignmentConfiguration : IEntityTypeConfiguration<SponsorEmployeeAssignment>
{
    public void Configure(EntityTypeBuilder<SponsorEmployeeAssignment> b)
    {
        b.ToTable("SponsorEmployeeAssignments", t =>
        {
            t.HasCheckConstraint("CK_SponsorEmployeeAssignments_DifferentEmployees", "[SponsorEmployeeId] <> [EmployeeId]");
            t.HasCheckConstraint("CK_SponsorEmployeeAssignments_Dates", "[UpdatedAtUtc] >= [CreatedAtUtc]");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.SponsorEmployeeId);
        b.Property(x => x.EmployeeId);
        b.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(7)");
        b.Property(x => x.UpdatedAtUtc).HasColumnType("datetimeoffset(7)");
        b.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
        b.HasIndex(x => x.EmployeeId).IsUnique().HasFilter("[IsActive] = 1");
        b.HasIndex(x => new { x.SponsorEmployeeId, x.IsActive, x.EmployeeId });
        b.HasIndex(x => new { x.EmployeeId, x.CreatedAtUtc });
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<Employee>().WithMany().HasForeignKey(x => x.SponsorEmployeeId).OnDelete(DeleteBehavior.NoAction);
    }
}
