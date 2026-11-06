using EmpleadoDelMes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;
public sealed class CampaignEvaluatorConfiguration : IEntityTypeConfiguration<CampaignEvaluator>
{
    public void Configure(EntityTypeBuilder<CampaignEvaluator> b)
    {
        b.ToTable("CampaignEvaluators");
        b.HasKey(x => new { x.CampaignId, x.CandidateEmployeeId });
        b.Property(x => x.CampaignId).ValueGeneratedNever();
        b.Property(x => x.CandidateEmployeeId).ValueGeneratedNever();
        b.Property(x => x.AssignmentId);
        b.Property(x => x.LinkedAtUtc).HasColumnType("datetimeoffset(7)");
        b.HasOne<Preselection>().WithMany().HasForeignKey(x => new { x.CampaignId, x.CandidateEmployeeId }).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<SponsorEmployeeAssignment>().WithMany().HasForeignKey(x => x.AssignmentId).OnDelete(DeleteBehavior.NoAction);
    }
}
