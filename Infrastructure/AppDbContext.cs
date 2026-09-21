using Microsoft.EntityFrameworkCore;
using SplitMoneyTg.Domain;

namespace SplitMoneyTg.Infrastructure;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<ExpenseGroup> Groups => Set<ExpenseGroup>();
    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();
    public DbSet<GroupParticipant> GroupParticipants => Set<GroupParticipant>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<Expense> Expenses => Set<Expense>();
    public DbSet<ExpenseShare> ExpenseShares => Set<ExpenseShare>();
    public DbSet<Transfer> Transfers => Set<Transfer>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<ProcessedUpdate> ProcessedUpdates => Set<ProcessedUpdate>();
    public DbSet<ApiIdempotencyRecord> ApiIdempotencyRecords => Set<ApiIdempotencyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().HasKey(x => x.TelegramId);
        modelBuilder.Entity<AppUser>().Property(x => x.TelegramId).ValueGeneratedNever();
        modelBuilder.Entity<AppUser>().Property(x => x.Version).HasDefaultValue(1L).IsConcurrencyToken();
        modelBuilder.Entity<ExpenseGroup>().HasKey(x => x.Id);
        modelBuilder.Entity<ExpenseGroup>().Property(x => x.Revision).HasDefaultValue(1L).IsConcurrencyToken();
        modelBuilder.Entity<ExpenseGroup>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GroupMember>().HasKey(x => new { x.GroupId, x.UserId });
        modelBuilder.Entity<GroupMember>().HasOne(x => x.Group).WithMany(x => x.Members).HasForeignKey(x => x.GroupId);
        modelBuilder.Entity<GroupMember>().HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GroupParticipant>().HasKey(x => new { x.GroupId, x.ParticipantId });
        modelBuilder.Entity<GroupParticipant>().HasOne(x => x.Group).WithMany(x => x.Participants).HasForeignKey(x => x.GroupId);
        modelBuilder.Entity<GroupParticipant>().HasOne(x => x.TelegramUser).WithMany().HasForeignKey(x => x.TelegramUserId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<GroupParticipant>().HasIndex(x => new { x.GroupId, x.TelegramUserId }).IsUnique();
        modelBuilder.Entity<GroupParticipant>().Property(x => x.Version).HasDefaultValue(1L).IsConcurrencyToken();
        modelBuilder.Entity<Invitation>().HasKey(x => x.Id);
        modelBuilder.Entity<Invitation>().HasIndex(x => x.Token).IsUnique();
        modelBuilder.Entity<Invitation>().HasOne<ExpenseGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Invitation>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedById).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Invitation>().HasIndex(x => new { x.GroupId, x.CreatedById })
            .IsUnique().HasFilter("\"IsActive\"").HasDatabaseName("IX_Invitations_OneActivePerCreatorGroup");
        modelBuilder.Entity<Invitation>().Property(x => x.ExpiresAt).HasDefaultValueSql("CURRENT_TIMESTAMP + INTERVAL '7 days'");
        modelBuilder.Entity<Invitation>().Property(x => x.Version).HasDefaultValue(1L).IsConcurrencyToken();
        modelBuilder.Entity<Expense>().HasKey(x => x.Id);
        modelBuilder.Entity<Expense>().HasAlternateKey(x => new { x.GroupId, x.Id });
        modelBuilder.Entity<Expense>().HasOne<ExpenseGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Expense>().HasOne<GroupMember>().WithMany().HasForeignKey(x => new { x.GroupId, x.AuthorId }).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Expense>().HasOne<GroupParticipant>().WithMany().HasForeignKey(x => new { x.GroupId, x.PayerId }).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Expense>().HasIndex(x => new { x.GroupId, x.CreatedAt });
        modelBuilder.Entity<Expense>().Property(x => x.Version).HasDefaultValue(1L).IsConcurrencyToken();
        modelBuilder.Entity<Expense>().HasMany(x => x.Shares).WithOne(x => x.Expense)
            .HasForeignKey(nameof(ExpenseShare.GroupId), nameof(ExpenseShare.ExpenseId))
            .HasPrincipalKey(nameof(Expense.GroupId), nameof(Expense.Id)).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ExpenseShare>().HasKey(x => new { x.ExpenseId, x.UserId });
        modelBuilder.Entity<ExpenseShare>().HasOne<GroupParticipant>().WithMany()
            .HasForeignKey(nameof(ExpenseShare.GroupId), nameof(ExpenseShare.UserId))
            .HasPrincipalKey(nameof(GroupParticipant.GroupId), nameof(GroupParticipant.ParticipantId)).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Transfer>().HasKey(x => x.Id);
        modelBuilder.Entity<Transfer>().HasOne<ExpenseGroup>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Transfer>().HasOne<GroupParticipant>().WithMany().HasForeignKey(x => new { x.GroupId, x.FromUserId }).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Transfer>().HasOne<GroupParticipant>().WithMany().HasForeignKey(x => new { x.GroupId, x.ToUserId }).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<Transfer>().HasIndex(x => new { x.GroupId, x.Status });
        modelBuilder.Entity<Transfer>().Property(x => x.Version).HasDefaultValue(1L).IsConcurrencyToken();
        modelBuilder.Entity<UserSession>().HasKey(x => x.UserId);
        modelBuilder.Entity<UserSession>().Property(x => x.UserId).ValueGeneratedNever();
        modelBuilder.Entity<UserSession>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ProcessedUpdate>().HasKey(x => x.UpdateId);
        modelBuilder.Entity<ProcessedUpdate>().Property(x => x.UpdateId).ValueGeneratedNever();
        modelBuilder.Entity<ApiIdempotencyRecord>().HasKey(x => new { x.UserId, x.Key });
        modelBuilder.Entity<ApiIdempotencyRecord>().HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ApiIdempotencyRecord>().Property(x => x.Key).HasMaxLength(100);
        modelBuilder.Entity<ApiIdempotencyRecord>().Property(x => x.RequestHash).HasMaxLength(64);
        modelBuilder.Entity<ApiIdempotencyRecord>().Property(x => x.ResponseContentType).HasMaxLength(200);
        modelBuilder.Entity<ApiIdempotencyRecord>().HasIndex(x => x.ExpiresAt);
        modelBuilder.Entity<ExpenseGroup>().Property(x => x.Name).HasMaxLength(100);
        modelBuilder.Entity<Expense>().Property(x => x.Description).HasMaxLength(200);
        modelBuilder.Entity<AppUser>().Property(x => x.PaymentDetails).HasMaxLength(500);
        modelBuilder.Entity<GroupParticipant>().Property(x => x.DisplayName).HasMaxLength(100);
        modelBuilder.Entity<GroupParticipant>().Property(x => x.PaymentDetails).HasMaxLength(500);
        modelBuilder.Entity<ExpenseGroup>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Groups_Type", "\"Type\" IN (0, 1)");
            t.HasCheckConstraint("CK_Groups_Name", "length(btrim(\"Name\")) BETWEEN 1 AND 100");
        });
        modelBuilder.Entity<GroupParticipant>().ToTable(t => t.HasCheckConstraint("CK_GroupParticipants_Identity",
            "(\"TelegramUserId\" IS NOT NULL AND \"ParticipantId\" = \"TelegramUserId\" AND \"ParticipantId\" > 0) OR " +
            "(\"TelegramUserId\" IS NULL AND \"ParticipantId\" < 0 AND length(btrim(\"DisplayName\")) BETWEEN 1 AND 100)"));
        modelBuilder.Entity<Expense>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Expenses_Amount", "\"AmountKopecks\" BETWEEN 1 AND 1000000000000");
            t.HasCheckConstraint("CK_Expenses_Description", "length(btrim(\"Description\")) BETWEEN 1 AND 200");
        });
        modelBuilder.Entity<ExpenseShare>().ToTable(t => t.HasCheckConstraint("CK_ExpenseShares_Amount", "\"AmountKopecks\" > 0"));
        modelBuilder.Entity<Transfer>().ToTable(t =>
        {
            t.HasCheckConstraint("CK_Transfers_Amount", "\"AmountKopecks\" > 0");
            t.HasCheckConstraint("CK_Transfers_Participants", "\"FromUserId\" <> \"ToUserId\"");
            t.HasCheckConstraint("CK_Transfers_Status", "\"Status\" IN (0, 1, 2, 3)");
            t.HasCheckConstraint("CK_Transfers_Resolution", "(\"Status\" = 0 AND \"ResolvedAt\" IS NULL) OR (\"Status\" IN (1, 2) AND \"ResolvedAt\" IS NOT NULL) OR \"Status\" = 3");
        });
        modelBuilder.Entity<Invitation>().ToTable(t => t.HasCheckConstraint("CK_Invitations_Token", "\"Token\" ~ '^[0-9a-f]{32}$'"));
        modelBuilder.Entity<UserSession>().ToTable(t => t.HasCheckConstraint("CK_Sessions_DataJson", "\"DataJson\"::jsonb IS NOT NULL"));
    }
}
