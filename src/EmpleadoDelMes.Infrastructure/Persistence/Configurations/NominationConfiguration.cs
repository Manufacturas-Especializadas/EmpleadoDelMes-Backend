using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

public sealed class NominationConfiguration : IEntityTypeConfiguration<Nomination>
{
    public void Configure(EntityTypeBuilder<Nomination> builder)
    {
        builder.ToTable("Nominations", table =>
        {
            table.HasCheckConstraint("CK_Nominations_DifferentEmployees", "[SponsorEmployeeId] <> [CandidateEmployeeId]");
            table.HasCheckConstraint("CK_Nominations_Qualities", "[Qualities] > 0 AND ([Qualities] & ~15) = 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CreatedByUserId);
        builder.Property(x => x.RegisteredAtUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.SponsorPayrollNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.CandidatePayrollNumber).HasMaxLength(32).IsRequired();
        builder.Property(x => x.SponsorFullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CandidateFullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Justification).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Qualities).HasConversion<int>();
        builder.Property(x => x.IsCancelled);
        builder.HasIndex(x => new { x.CampaignId, x.SponsorEmployeeId, x.CandidateEmployeeId }).IsUnique();
        builder.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.SponsorEmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.CandidateEmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
    }
}
