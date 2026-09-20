namespace SplitMoneyTg.Domain;

public sealed class AppUser
{
    public long TelegramId { get; set; }
    public string DisplayName { get; set; } = "";
    public string? Username { get; set; }
    public string? PaymentDetails { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum GroupType { Collective, Standalone }

public sealed class ExpenseGroup
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public long OwnerId { get; set; }
    public GroupType Type { get; set; }
    public bool IsArchived { get; set; }
    public long Revision { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<GroupMember> Members { get; set; } = [];
    public List<GroupParticipant> Participants { get; set; } = [];
}

public sealed class GroupMember
{
    public Guid GroupId { get; set; }
    public long UserId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset JoinedAt { get; set; } = DateTimeOffset.UtcNow;
    public ExpenseGroup Group { get; set; } = null!;
    public AppUser User { get; set; } = null!;
}

public sealed class GroupParticipant
{
    public Guid GroupId { get; set; }
    public long ParticipantId { get; set; }
    public long? TelegramUserId { get; set; }
    public string? DisplayName { get; set; }
    public string? PaymentDetails { get; set; }
    public bool IsActive { get; set; } = true;
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ExpenseGroup Group { get; set; } = null!;
    public AppUser? TelegramUser { get; set; }
}

public sealed class Invitation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Token { get; set; } = "";
    public Guid GroupId { get; set; }
    public long CreatedById { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(7);
    public DateTimeOffset? RevokedAt { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public long AuthorId { get; set; }
    public long PayerId { get; set; }
    public string Description { get; set; } = "";
    public long AmountKopecks { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ExpenseShare> Shares { get; set; } = [];
}

public sealed class ExpenseShare
{
    public Guid? GroupId { get; set; }
    public Guid ExpenseId { get; set; }
    public long UserId { get; set; }
    public long AmountKopecks { get; set; }
    public Expense Expense { get; set; } = null!;
}

public enum TransferStatus { Pending, Confirmed, Rejected, Cancelled }

public sealed class Transfer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GroupId { get; set; }
    public long FromUserId { get; set; }
    public long ToUserId { get; set; }
    public long AmountKopecks { get; set; }
    public TransferStatus Status { get; set; } = TransferStatus.Pending;
    public long Version { get; set; } = 1;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAt { get; set; }
}

public sealed class UserSession
{
    public long UserId { get; set; }
    public string State { get; set; } = "";
    public string DataJson { get; set; } = "{}";
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProcessedUpdate
{
    public int UpdateId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ApiIdempotencyRecord
{
    public long UserId { get; set; }
    public string Key { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public int ResponseStatusCode { get; set; }
    public string? ResponseContentType { get; set; }
    public string ResponseBody { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddDays(30);
}
