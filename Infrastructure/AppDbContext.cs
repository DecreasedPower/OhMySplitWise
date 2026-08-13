using Microsoft.EntityFrameworkCore;
using SplitMoneyTg.Domain;

namespace SplitMoneyTg.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ExpenseGroup> Groups => Set<ExpenseGroup>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseShare> ExpenseShares => Set<ExpenseShare>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<ProcessedUpdate> ProcessedUpdates => Set<ProcessedUpdate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasKey(x => x.TelegramId);
        modelBuilder.Entity<ExpenseGroup>().HasKey(x => x.Id);
        modelBuilder.Entity<GroupMember>().HasKey(x => new { x.GroupId, x.UserId });
        modelBuilder.Entity<GroupMember>().HasOne(x => x.Group).WithMany(x => x.Members).HasForeignKey(x => x.GroupId);
        modelBuilder.Entity<GroupMember>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invitation>().HasKey(x => x.Id);
        modelBuilder.Entity<Invitation>().HasIndex(x => x.Token).IsUnique();
        modelBuilder.Entity<Expense>().HasKey(x => x.Id);
        modelBuilder.Entity<Expense>().HasMany(x => x.Shares).WithOne(x => x.Expense).HasForeignKey(x => x.ExpenseId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ExpenseShare>().HasKey(x => new { x.ExpenseId, x.UserId });
        modelBuilder.Entity<Transfer>().HasKey(x => x.Id);
        modelBuilder.Entity<UserSession>().HasKey(x => x.UserId);
        modelBuilder.Entity<ProcessedUpdate>().HasKey(x => x.UpdateId);
        modelBuilder.Entity<ExpenseGroup>().Property(x => x.Name).HasMaxLength(100);
        modelBuilder.Entity<Expense>().Property(x => x.Description).HasMaxLength(200);
        modelBuilder.Entity<AppUser>().Property(x => x.PaymentDetails).HasMaxLength(500);
    }
}
