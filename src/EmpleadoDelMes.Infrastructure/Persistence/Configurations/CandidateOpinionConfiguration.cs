using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

public sealed class CandidateOpinionConfiguration : IEntityTypeConfiguration<CandidateOpinion>
{
    public void Configure(EntityTypeBuilder<CandidateOpinion> builder)
    {
        builder.ToTable("CandidateOpinions", table =>
            table.HasCheckConstraint("CK_CandidateOpinions_Source", "[OpinionSource] IN (1, 2, 3, 4)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CampaignId);
        builder.Property(x => x.CandidateEmployeeId);
        builder.Property(x => x.OpinionSource).HasConversion<int>();
        builder.Property(x => x.Content).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedByUserId);
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.LastModifiedAtUtc).HasColumnType("datetimeoffset(7)");
        builder.HasIndex(x => new { x.CampaignId, x.CandidateEmployeeId, x.OpinionSource }).IsUnique();
        builder.HasOne<Preselection>().WithMany()
            .HasForeignKey(x => new { x.CampaignId, x.CandidateEmployeeId })
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
    }
}
