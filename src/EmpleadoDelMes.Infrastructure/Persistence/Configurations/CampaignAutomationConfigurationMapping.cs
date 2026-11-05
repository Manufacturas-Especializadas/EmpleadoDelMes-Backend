using EmpleadoDelMes.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

public sealed class CampaignAutomationConfigurationMapping : IEntityTypeConfiguration<CampaignAutomationConfiguration>
{
    public void Configure(EntityTypeBuilder<CampaignAutomationConfiguration> b)
    {
        b.ToTable("CampaignAutomationConfiguration", t => t.HasCheckConstraint("CK_Automation_Singleton", "[Id] = 1"));
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.RowVersion).IsRowVersion();
        b.OwnsOne(c => c.Calendar, calendar =>
        {
            calendar.Property(c => c.NominationOpeningLocalTime).HasColumnType("time(0)");
            calendar.Property(c => c.NominationClosingLocalTime).HasColumnType("time(0)");
            calendar.Property(c => c.OpinionOpeningLocalTime).HasColumnType("time(0)");
            calendar.Property(c => c.OpinionClosingLocalTime).HasColumnType("time(0)");
        });
        b.HasData(new { Id = 1, IsEnabled = false });
    }
}

public sealed class CampaignAutomationAuditMapping : IEntityTypeConfiguration<CampaignAutomationAuditEntry>
{
    public void Configure(EntityTypeBuilder<CampaignAutomationAuditEntry> b)
    {
        b.ToTable("CampaignAutomationAudit");
        b.HasKey(c => c.Id);
        b.Property(c => c.Id).ValueGeneratedNever();
        b.Property(c => c.PreviousValues).HasMaxLength(4000).IsRequired();
        b.Property(c => c.NewValues).HasMaxLength(4000).IsRequired();
        b.HasIndex(c => c.ChangedAtUtc);
    }
}
