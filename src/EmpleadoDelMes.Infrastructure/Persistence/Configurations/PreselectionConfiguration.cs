using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

public sealed class PreselectionConfiguration : IEntityTypeConfiguration<Preselection>
{
    public void Configure(EntityTypeBuilder<Preselection> builder)
    {
        builder.ToTable("Preselections");
        builder.HasKey(x => new { x.CampaignId, x.CandidateEmployeeId });
        builder.Property(x => x.CampaignId).ValueGeneratedNever();
        builder.Property(x => x.CandidateEmployeeId).ValueGeneratedNever();
        builder.Property(x => x.IsSelected);
        builder.Property(x => x.LastChangedByUserId);
        builder.Property(x => x.LastChangedAtUtc).HasColumnType("datetimeoffset(7)");
        builder.HasOne<Campaign>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Employee>().WithMany().HasForeignKey(x => x.CandidateEmployeeId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.LastChangedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
    }
}
