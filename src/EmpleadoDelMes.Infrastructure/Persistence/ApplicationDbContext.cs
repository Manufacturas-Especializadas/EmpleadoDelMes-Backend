using EmpleadoDelMes.Domain.Entities;
using EmpleadoDelMes.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EmpleadoDelMes.Infrastructure.Persistence;

public sealed class ApplicationDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Campaign> Campaigns => Set<Campaign>();
    public DbSet<Nomination> Nominations => Set<Nomination>();
    public DbSet<Preselection> Preselections => Set<Preselection>();
    public DbSet<CandidateOpinion> CandidateOpinions => Set<CandidateOpinion>();
    public DbSet<NominationAuditEntry> NominationAuditEntries => Set<NominationAuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
