using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

public sealed class CampaignConfiguration : IEntityTypeConfiguration<Campaign>
{
    public void Configure(EntityTypeBuilder<Campaign> builder)
    {
        builder.ToTable("Campaigns", table =>
        {
            table.HasCheckConstraint("CK_Campaigns_EvaluationMonth", "DAY([EvaluationMonth]) = 1");
            ConfigurePeriodConstraints(table, "Nomination");
            ConfigurePeriodConstraints(table, "Opinion");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.EvaluationMonth).HasColumnType("date");
        builder.HasIndex(x => x.EvaluationMonth).IsUnique();
        builder.Property(x => x.CreatedAtUtc).HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.IsArchived);
        ConfigurePeriod(builder.ComplexProperty(x => x.NominationPeriod), "Nomination");
        ConfigurePeriod(builder.ComplexProperty(x => x.OpinionPeriod), "Opinion");
        builder.Property<byte[]>("RowVersion").IsRowVersion().IsRequired();
    }

    private static void ConfigurePeriod(ComplexPropertyBuilder<CampaignPeriod> builder, string prefix)
    {
        builder.IsRequired();
        builder.Property(x => x.ScheduledOpeningAtUtc)
            .HasColumnName(prefix + "OpeningAtUtc").HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.ScheduledClosingAtUtc)
            .HasColumnName(prefix + "ClosingAtUtc").HasColumnType("datetimeoffset(7)");
        builder.Property(x => x.Mode).HasColumnName(prefix + "Mode").HasConversion<int>();
    }

    private static void ConfigurePeriodConstraints(TableBuilder<Campaign> table, string prefix)
    {
        table.HasCheckConstraint($"CK_Campaigns_{prefix}Interval",
            $"([{prefix}OpeningAtUtc] IS NULL AND [{prefix}ClosingAtUtc] IS NULL) OR " +
            $"([{prefix}OpeningAtUtc] IS NOT NULL AND [{prefix}ClosingAtUtc] IS NOT NULL " +
            $"AND [{prefix}OpeningAtUtc] < [{prefix}ClosingAtUtc])");
        table.HasCheckConstraint($"CK_Campaigns_{prefix}Mode",
            $"[{prefix}Mode] IN (0, 1, 2) AND ([{prefix}Mode] <> 0 OR " +
            $"([{prefix}OpeningAtUtc] IS NOT NULL AND [{prefix}ClosingAtUtc] IS NOT NULL))");
    }
}
