using DepartmentManagement.Domain.Auditing;
using DepartmentManagement.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DepartmentManagement.Infrastructure.Persistence;

/// <summary>Central SQL Server database for Identity and application data.</summary>
public sealed class ApplicationDbContext(
    DbContextOptions<ApplicationDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(user => user.DisplayName)
                .HasMaxLength(120)
                .IsRequired();
        });

        builder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(log => log.Id);

            entity.Property(log => log.Username).HasMaxLength(256).IsRequired();
            entity.Property(log => log.Action).HasMaxLength(100).IsRequired();
            entity.Property(log => log.Module).HasMaxLength(100).IsRequired();
            entity.Property(log => log.EntityType).HasMaxLength(150);
            entity.Property(log => log.EntityId).HasMaxLength(200);
            entity.Property(log => log.DetailsJson).HasColumnType("nvarchar(max)");
            entity.Property(log => log.ClientMachine).HasMaxLength(256);
            entity.Property(log => log.ClientIp).HasMaxLength(64);
            entity.Property(log => log.CorrelationId).HasMaxLength(100);

            // No FK to AspNetUsers: audit history must remain readable if a user is disabled or deleted.
            entity.HasIndex(log => log.OccurredAtUtc);
            entity.HasIndex(log => new { log.UserId, log.OccurredAtUtc });
            entity.HasIndex(log => new { log.Module, log.Action, log.OccurredAtUtc });
        });

        builder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(token => token.Id);

            entity.Property(token => token.TokenHash)
                .HasMaxLength(64)
                .IsRequired();

            entity.Property(token => token.ReplacedByTokenHash)
                .HasMaxLength(64);

            entity.Property(token => token.CreatedByIp)
                .HasMaxLength(64);

            entity.Property(token => token.RevokedByIp)
                .HasMaxLength(64);

            entity.Property(token => token.RowVersion)
                .IsRowVersion();

            entity.HasIndex(token => token.TokenHash)
                .IsUnique();

            entity.HasIndex(token => new
            {
                token.UserId,
                token.ExpiresAtUtc
            });

            entity.HasIndex(token => new
            {
                token.UserId,
                token.FamilyId
            });

            entity.HasIndex(token => token.FamilyId);

            entity.HasOne<ApplicationUser>()
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
