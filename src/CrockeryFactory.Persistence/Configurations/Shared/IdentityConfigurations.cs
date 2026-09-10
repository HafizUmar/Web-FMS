using CrockeryFactory.Shared.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CrockeryFactory.Persistence.Configurations.Shared;

/// <summary>
/// Identity's own tables, moved out of dbo into their own schema and given names that
/// read like the rest of the database rather than like a framework's defaults.
/// </summary>
public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("Users", ModuleSchemas.Identity);

        b.Property(x => x.FullName).HasMaxLength(160).IsRequired();

        b.HasData(SeedData.SystemUser());
    }
}

public class AppRoleConfiguration : IEntityTypeConfiguration<AppRole>
{
    public void Configure(EntityTypeBuilder<AppRole> b)
    {
        b.ToTable("Roles", ModuleSchemas.Identity);

        b.Property(x => x.Description).HasMaxLength(300);

        b.HasData(SeedData.Roles());
    }
}

public class IdentityUserRoleConfiguration : IEntityTypeConfiguration<IdentityUserRole<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> b) =>
        b.ToTable("UserRoles", ModuleSchemas.Identity);
}

public class IdentityUserClaimConfiguration : IEntityTypeConfiguration<IdentityUserClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> b)
    {
        b.ToTable("UserClaims", ModuleSchemas.Identity);

        // The 256-char string convention is right for the domain but wrong here:
        // a truncated claim value is an authorisation bug that only appears in production.
        b.Property(x => x.ClaimValue).HasColumnType("nvarchar(max)");
        b.Property(x => x.ClaimType).HasColumnType("nvarchar(max)");
    }
}

public class IdentityUserLoginConfiguration : IEntityTypeConfiguration<IdentityUserLogin<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> b) =>
        b.ToTable("UserLogins", ModuleSchemas.Identity);
}

public class IdentityUserTokenConfiguration : IEntityTypeConfiguration<IdentityUserToken<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> b)
    {
        b.ToTable("UserTokens", ModuleSchemas.Identity);
        b.Property(x => x.Value).HasColumnType("nvarchar(max)");
    }
}

public class IdentityRoleClaimConfiguration : IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> b)
    {
        b.ToTable("RoleClaims", ModuleSchemas.Identity);
        b.Property(x => x.ClaimValue).HasColumnType("nvarchar(max)");
        b.Property(x => x.ClaimType).HasColumnType("nvarchar(max)");
    }
}
