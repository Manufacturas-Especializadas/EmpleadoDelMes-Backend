using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EmpleadoDelMes.Infrastructure.Persistence.Configurations;

// Mantiene el mismo esquema cuando el contexto se construye sin el contenedor de Identity.
public sealed class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder)
    {
        builder.Property(x => x.LoginProvider).HasMaxLength(128);
        builder.Property(x => x.Name).HasMaxLength(128);
    }
}
