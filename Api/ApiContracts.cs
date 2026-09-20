using SplitMoneyTg.Domain;

namespace SplitMoneyTg.Api;

public sealed record UserProfileDto(long Id, string DisplayName, string? Username, string? PaymentDetails, long Version);
public sealed record UpdateProfileRequest(string? PaymentDetails);
public sealed record GroupSummaryDto(Guid Id, string Name, GroupType Type, long OwnerId, int ParticipantCount, long MyBalanceKopecks, long TotalExpensesKopecks, long Revision);
public sealed record GroupDto(Guid Id, string Name, GroupType Type, long OwnerId, int ParticipantCount, long MyBalanceKopecks, long TotalExpensesKopecks, DateTimeOffset CreatedAt, bool IsOwner, long Revision);
public sealed record CreateGroupRequest(string Name, GroupType Type);
public sealed record ParticipantDto(long Id, string DisplayName, long? TelegramUserId, string? PaymentDetails, bool IsCurrentUser, bool CanEdit, long Version);
public sealed record ParticipantRequest(string DisplayName, string? PaymentDetails);
public sealed record ExpenseShareDto(long ParticipantId, string ParticipantName, long AmountKopecks);
public sealed record ExpenseDto(Guid Id, Guid GroupId, string Description, long AmountKopecks, long PayerId, string PayerName, long AuthorId, bool CanEdit, DateTimeOffset CreatedAt, long Version, IReadOnlyList<ExpenseShareDto>? Shares = null);
public sealed record ExpenseShareRequest(long ParticipantId, long AmountKopecks);
public sealed record ExpenseRequest(string Description, long AmountKopecks, long PayerId, IReadOnlyList<ExpenseShareRequest> Shares);
public sealed record BalanceDto(long ParticipantId, string ParticipantName, long AmountKopecks);
public sealed record SuggestedTransferDto(long FromParticipantId, string FromName, long ToParticipantId, string ToName, long AmountKopecks, string? PaymentDetails, bool CanMarkPaid, Guid? PendingTransferId);
public sealed record PendingTransferDto(Guid Id, string FromName, string ToName, long AmountKopecks, bool CanResolve, long Version);
public sealed record BalanceOverviewDto(IReadOnlyList<BalanceDto> Balances, IReadOnlyList<SuggestedTransferDto> Suggestions, IReadOnlyList<PendingTransferDto> PendingTransfers, long GroupRevision);
public sealed record MarkPaidRequest(long ToParticipantId);
public sealed record ResolveTransferRequest(string Status);
public sealed record InvitationDto(Guid Id, string Token, string ShareUrl, string TelegramShareUrl, DateTimeOffset ExpiresAt, long Version, bool IsActive);
