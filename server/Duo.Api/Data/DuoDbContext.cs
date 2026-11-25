using Microsoft.EntityFrameworkCore;

namespace Duo.Api.Data;

public sealed class DuoDbContext(DbContextOptions<DuoDbContext> options) : DbContext(options)
{
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();

    public DbSet<SessionEntity> Sessions => Set<SessionEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<AccountEntity>();
        account.ToTable("Accounts");
        account.HasKey(a => a.Id);
        account.Property(a => a.Id).HasMaxLength(32);
        account.Property(a => a.Username).HasMaxLength(20).IsRequired();
        account.Property(a => a.NormalizedUsername).HasMaxLength(20).IsRequired();
        account.Property(a => a.PasswordHash).HasMaxLength(256).IsRequired();
        account.HasIndex(a => a.NormalizedUsername).IsUnique();

        var session = modelBuilder.Entity<SessionEntity>();
        session.ToTable("Sessions");
        session.HasKey(s => s.TokenHash);
        session.Property(s => s.TokenHash).HasMaxLength(64);
        session.Property(s => s.AccountId).HasMaxLength(32).IsRequired();
        session.HasIndex(s => s.AccountId);
        session.HasIndex(s => s.ExpiresAt);
        session.HasOne(s => s.Account)
            .WithMany()
            .HasForeignKey(s => s.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
