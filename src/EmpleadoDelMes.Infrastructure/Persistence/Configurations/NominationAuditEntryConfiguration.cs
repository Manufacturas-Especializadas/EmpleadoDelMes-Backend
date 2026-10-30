using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

public sealed class NominationAuditEntryConfiguration : IEntityTypeConfiguration<NominationAuditEntry>
{
    public void Configure(EntityTypeBuilder<NominationAuditEntry> builder)
    {
        builder.ToTable("NominationAuditEntries", table =>
            table.HasCheckConstraint("CK_NominationAuditEntries_Action", "[Action] IN (1, 2)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.NominationId);
        builder.Property(x => x.ActorUserId);
        builder.Property(x => x.Action).HasConversion<int>();
        builder.Property(x => x.OccurredAtUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.Reason).IsRequired();
        builder.Property(x => x.Details);
        builder.HasOne<Nomination>().WithMany().HasForeignKey(x => x.NominationId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
