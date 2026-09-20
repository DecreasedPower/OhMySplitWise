using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SplitMoneyTg.Api;
using SplitMoneyTg.Domain;
using SplitMoneyTg.Infrastructure;
using Telegram.Bot;

namespace SplitMoneyTg.Application;

public sealed class MiniAppService(
    AppDbContext db,
    BalanceService balanceService,
    ITelegramBotClient bot,
    ILogger<MiniAppService> logger)
{
    private const long MaximumExpenseKopecks = 1_000_000_000_000;

    public async Task UpsertUser(TelegramMiniAppUser telegramUser, CancellationToken ct)
    {
        if (db.Database.IsRelational())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Users" ("TelegramId", "DisplayName", "Username", "PaymentDetails", "CreatedAt")
                VALUES ({telegramUser.Id}, {telegramUser.DisplayName}, {telegramUser.Username}, NULL, {DateTimeOffset.UtcNow})
                ON CONFLICT ("TelegramId") DO UPDATE
                SET "DisplayName" = EXCLUDED."DisplayName", "Username" = EXCLUDED."Username"
                """, ct);
            return;
        }

        var user = await db.Users.FindAsync([telegramUser.Id], ct);
        if (user is null)
            db.Users.Add(new AppUser { TelegramId = telegramUser.Id, DisplayName = telegramUser.DisplayName, Username = telegramUser.Username });
        else
        {
            user.DisplayName = telegramUser.DisplayName;
            user.Username = telegramUser.Username;
        }
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserProfileDto> GetProfile(long userId, CancellationToken ct) =>
        ToProfile(await db.Users.FindAsync([userId], ct) ?? throw NotFound("User not found."));

    public async Task<UserProfileDto> UpdateProfile(long userId, UpdateProfileRequest request, CancellationToken ct)
    {
        ValidateOptional(request.PaymentDetails, 500, "Payment details");
        var user = await db.Users.FindAsync([userId], ct) ?? throw NotFound("User not found.");
        user.PaymentDetails = NormalizeOptional(request.PaymentDetails);
        await db.SaveChangesAsync(ct);
        return ToProfile(user);
    }

    public async Task<IReadOnlyList<GroupSummaryDto>> GetGroups(long userId, CancellationToken ct)
    {
        var groups = await db.GroupMembers.Where(x => x.UserId == userId && x.IsActive && !x.Group.IsArchived &&
                (x.Group.Type == GroupType.Collective || x.Group.OwnerId == userId))
            .Select(x => x.Group).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var result = new List<GroupSummaryDto>(groups.Count);
        foreach (var group in groups) result.Add(await ToGroupSummary(group, userId, ct));
        return result;
    }

    public async Task<GroupDto> GetGroup(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await RequireGroup(userId, groupId, ct);
        var summary = await ToGroupSummary(group, userId, ct);
        return new(summary.Id, summary.Name, summary.Type, summary.OwnerId, summary.ParticipantCount,
            summary.MyBalanceKopecks, summary.TotalExpensesKopecks, group.CreatedAt, group.OwnerId == userId);
    }

    public async Task<GroupDto> CreateGroup(long userId, CreateGroupRequest request, CancellationToken ct)
    {
        var name = ValidateRequired(request.Name, 100, "Group name");
        if (!Enum.IsDefined(request.Type)) throw Validation("Unknown group type.", "invalid_group_type");
        var group = new ExpenseGroup { Name = name, OwnerId = userId, Type = request.Type };
        group.Members.Add(new GroupMember { UserId = userId });
        group.Participants.Add(new GroupParticipant { ParticipantId = userId, TelegramUserId = userId });
        db.Groups.Add(group);
        await db.SaveChangesAsync(ct);
        return new(group.Id, group.Name, group.Type, group.OwnerId, 1, 0, 0, group.CreatedAt, true);
    }

    public Task DeleteGroup(long userId, Guid groupId, CancellationToken ct) => WithGroupLock(groupId, async () =>
    {
        var group = await RequireGroup(userId, groupId, ct);
        if (group.OwnerId != userId) throw Forbidden("Only the group owner can delete it.");
        group.IsArchived = true;
        foreach (var invitation in await db.Invitations.Where(x => x.GroupId == groupId && x.IsActive).ToListAsync(ct)) invitation.IsActive = false;
        await CancelPending(groupId, ct);
    }, ct);

    public Task LeaveGroup(long userId, Guid groupId, CancellationToken ct) => WithGroupLock(groupId, async () =>
    {
        var group = await RequireGroup(userId, groupId, ct);
        if (group.Type != GroupType.Collective) throw Validation("Standalone groups cannot be left.", "invalid_group_type");
        if (group.OwnerId == userId) throw Conflict("The owner cannot leave an active group.", "owner_cannot_leave");
        var balances = await balanceService.GetBalances(groupId, ct);
        if (balances.GetValueOrDefault(userId) != 0) throw Conflict("Settle your balance before leaving.", "nonzero_balance");
        var member = await db.GroupMembers.FindAsync([groupId, userId], ct) ?? throw NotFound("Membership not found.");
        member.IsActive = false;
        var participant = await db.GroupParticipants.FindAsync([groupId, userId], ct);
        if (participant is not null) participant.IsActive = false;
        await CancelPending(groupId, ct);
    }, ct);

    public async Task<IReadOnlyList<ParticipantDto>> GetParticipants(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await RequireGroup(userId, groupId, ct);
        var participants = await ParticipantInfos(groupId, true, ct);
        return participants.Select(x => new ParticipantDto(x.Id, x.Name, x.TelegramUserId,
            group.Type == GroupType.Standalone || x.TelegramUserId == userId ? x.PaymentDetails : null,
            x.TelegramUserId == userId, group.Type == GroupType.Standalone && group.OwnerId == userId && x.TelegramUserId is null)).ToList();
    }

    public Task<ParticipantDto> AddParticipant(long userId, Guid groupId, ParticipantRequest request, CancellationToken ct) =>
        WithGroupLock(groupId, async () =>
        {
            var group = await RequireStandaloneOwner(userId, groupId, ct);
            var name = ValidateRequired(request.DisplayName, 100, "Participant name");
            ValidateOptional(request.PaymentDetails, 500, "Payment details");
            if ((await ParticipantInfos(group.Id, true, ct)).Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw Conflict("An active participant with this name already exists.", "duplicate_participant_name");
            long id;
            do id = Random.Shared.NextInt64(long.MinValue, 0);
            while (await db.GroupParticipants.AnyAsync(x => x.GroupId == groupId && x.ParticipantId == id, ct));
            var participant = new GroupParticipant { GroupId = groupId, ParticipantId = id, DisplayName = name, PaymentDetails = NormalizeOptional(request.PaymentDetails) };
            db.GroupParticipants.Add(participant);
            return ToParticipant(participant, userId, true);
        }, ct);

    public Task<ParticipantDto> UpdateParticipant(long userId, Guid groupId, long participantId, ParticipantRequest request, CancellationToken ct) =>
        WithGroupLock(groupId, async () =>
        {
            await RequireStandaloneOwner(userId, groupId, ct);
            var participant = await RequireManagedParticipant(groupId, participantId, ct);
            var name = ValidateRequired(request.DisplayName, 100, "Participant name");
            ValidateOptional(request.PaymentDetails, 500, "Payment details");
            if ((await ParticipantInfos(groupId, true, ct)).Any(x => x.Id != participantId && string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw Conflict("An active participant with this name already exists.", "duplicate_participant_name");
            participant.DisplayName = name;
            participant.PaymentDetails = NormalizeOptional(request.PaymentDetails);
            return ToParticipant(participant, userId, true);
        }, ct);

    public Task DeleteParticipant(long userId, Guid groupId, long participantId, CancellationToken ct) => WithGroupLock(groupId, async () =>
    {
        await RequireStandaloneOwner(userId, groupId, ct);
        var participant = await RequireManagedParticipant(groupId, participantId, ct);
        if (await db.Expenses.AnyAsync(x => x.GroupId == groupId && (x.PayerId == participantId || x.Shares.Any(s => s.UserId == participantId)), ct))
            throw Conflict("A participant used in expenses cannot be deleted.", "participant_in_use");
        db.GroupParticipants.Remove(participant);
        await CancelPending(groupId, ct);
    }, ct);

    public async Task<IReadOnlyList<ExpenseDto>> GetExpenses(long userId, Guid groupId, CancellationToken ct)
    {
        await RequireGroup(userId, groupId, ct);
        var expenses = await db.Expenses.Where(x => x.GroupId == groupId).OrderByDescending(x => x.CreatedAt).ToListAsync(ct);
        var names = (await ParticipantInfos(groupId, false, ct)).ToDictionary(x => x.Id, x => x.Name);
        return expenses.Select(x => ToExpense(x, userId, names)).ToList();
    }

    public async Task<ExpenseDto> GetExpense(long userId, Guid groupId, Guid expenseId, CancellationToken ct)
    {
        await RequireGroup(userId, groupId, ct);
        var expense = await db.Expenses.Include(x => x.Shares).FirstOrDefaultAsync(x => x.GroupId == groupId && x.Id == expenseId, ct)
            ?? throw NotFound("Expense not found.");
        var names = (await ParticipantInfos(groupId, false, ct)).ToDictionary(x => x.Id, x => x.Name);
        return ToExpense(expense, userId, names, true);
    }

    public Task<ExpenseDto> CreateExpense(long userId, Guid groupId, ExpenseRequest request, CancellationToken ct) =>
        SaveExpense(userId, groupId, null, request, ct);

    public Task<ExpenseDto> UpdateExpense(long userId, Guid groupId, Guid expenseId, ExpenseRequest request, CancellationToken ct) =>
        SaveExpense(userId, groupId, expenseId, request, ct);

    public Task DeleteExpense(long userId, Guid groupId, Guid expenseId, CancellationToken ct) => WithGroupLock(groupId, async () =>
    {
        await RequireWriteAccess(userId, groupId, ct);
        var expense = await db.Expenses.FirstOrDefaultAsync(x => x.GroupId == groupId && x.Id == expenseId, ct) ?? throw NotFound("Expense not found.");
        if (expense.AuthorId != userId) throw Forbidden("Only the expense author can delete it.");
        db.Expenses.Remove(expense);
        await CancelPending(groupId, ct);
    }, ct);

    public async Task<BalanceOverviewDto> GetBalances(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await RequireGroup(userId, groupId, ct);
        var balances = await balanceService.GetBalances(groupId, ct);
        var participants = await ParticipantInfos(groupId, false, ct);
        var byId = participants.ToDictionary(x => x.Id);
        var pending = await db.Transfers.Where(x => x.GroupId == groupId && x.Status == TransferStatus.Pending).ToListAsync(ct);
        var suggestions = BalanceService.Minimize(balances).Select(x =>
        {
            var from = byId.GetValueOrDefault(x.FromUserId, new(x.FromUserId, x.FromUserId.ToString(), null, null));
            var to = byId.GetValueOrDefault(x.ToUserId, new(x.ToUserId, x.ToUserId.ToString(), null, null));
            var transfer = pending.FirstOrDefault(t => t.FromUserId == x.FromUserId && t.ToUserId == x.ToUserId);
            return new SuggestedTransferDto(x.FromUserId, from.Name, x.ToUserId, to.Name, x.AmountKopecks,
                group.Type == GroupType.Standalone || x.FromUserId == userId ? to.PaymentDetails : null,
                group.Type == GroupType.Collective && x.FromUserId == userId, transfer?.Id);
        }).ToList();
        var incoming = pending.Where(x => x.ToUserId == userId).Select(x => new PendingTransferDto(x.Id,
            byId.GetValueOrDefault(x.FromUserId)?.Name ?? x.FromUserId.ToString(),
            byId.GetValueOrDefault(x.ToUserId)?.Name ?? x.ToUserId.ToString(), x.AmountKopecks, true)).ToList();
        return new(balances.Select(x => new BalanceDto(x.Key, byId.GetValueOrDefault(x.Key)?.Name ?? x.Key.ToString(), x.Value))
            .OrderByDescending(x => x.AmountKopecks).ToList(), suggestions, incoming);
    }

    public async Task MarkPaid(long userId, Guid groupId, MarkPaidRequest request, CancellationToken ct)
    {
        Transfer? created = null;
        await WithGroupLock(groupId, async () =>
        {
            var group = await RequireGroup(userId, groupId, ct);
            if (group.Type != GroupType.Collective) throw Validation("Transfers are only available in collective groups.", "invalid_group_type");
            var suggestion = BalanceService.Minimize(await balanceService.GetBalances(groupId, ct))
                .FirstOrDefault(x => x.FromUserId == userId && x.ToUserId == request.ToParticipantId)
                ?? throw Conflict("The balance changed. Refresh and try again.", "stale_balance");
            if (await db.Transfers.AnyAsync(x => x.GroupId == groupId && x.FromUserId == userId &&
                    x.ToUserId == request.ToParticipantId && x.Status == TransferStatus.Pending, ct))
                throw Conflict("This transfer is already awaiting confirmation.", "transfer_pending");
            created = new Transfer { GroupId = groupId, FromUserId = userId, ToUserId = request.ToParticipantId, AmountKopecks = suggestion.AmountKopecks };
            db.Transfers.Add(created);
        }, ct);
        var sender = await db.Users.FindAsync([userId], ct);
        await Notify(created!.ToUserId, $"{sender?.DisplayName ?? "Участник"} отметил перевод {Money(created.AmountKopecks)}. Подтвердите получение в приложении.", ct);
    }

    public async Task ResolveTransfer(long userId, Guid groupId, Guid transferId, ResolveTransferRequest request, CancellationToken ct)
    {
        var confirmed = request.Status switch
        {
            "confirmed" => true,
            "rejected" => false,
            _ => throw Validation("Status must be confirmed or rejected.", "invalid_transfer_status")
        };
        Transfer? resolved = null;
        await WithGroupLock(groupId, async () =>
        {
            if (!await db.Groups.AnyAsync(x => x.Id == groupId && !x.IsArchived, ct))
                throw NotFound("Group not found.");
            resolved = await db.Transfers.FirstOrDefaultAsync(x => x.Id == transferId && x.GroupId == groupId && x.ToUserId == userId, ct)
                ?? throw NotFound("Transfer not found.");
            if (resolved.Status != TransferStatus.Pending) throw Conflict("Transfer has already been resolved.", "transfer_resolved");
            resolved.Status = confirmed ? TransferStatus.Confirmed : TransferStatus.Rejected;
            resolved.ResolvedAt = DateTimeOffset.UtcNow;
        }, ct);
        await Notify(resolved!.FromUserId, confirmed ? "Получатель подтвердил перевод." : "Получатель не подтвердил перевод.", ct);
    }

    public async Task<InvitationDto> CreateInvitation(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await RequireGroup(userId, groupId, ct);
        if (group.Type != GroupType.Collective) throw Validation("Invitations are only available in collective groups.", "invalid_group_type");
        var invitation = new Invitation { GroupId = groupId, CreatedById = userId, Token = Guid.NewGuid().ToString("N") };
        db.Invitations.Add(invitation);
        await db.SaveChangesAsync(ct);
        var botUser = await bot.GetMe(ct);
        var shareUrl = $"https://t.me/{botUser.Username}?start=join_{invitation.Token}";
        var telegramShareUrl = $"https://t.me/share/url?url={Uri.EscapeDataString(shareUrl)}&text={Uri.EscapeDataString($"Присоединяйтесь к группе «{group.Name}»")}";
        return new(invitation.Token, shareUrl, telegramShareUrl);
    }

    public static void ValidateExpense(ExpenseRequest request)
    {
        ValidateRequired(request.Description, 200, "Description");
        if (request.AmountKopecks is <= 0 or > MaximumExpenseKopecks)
            throw Validation("Amount must be positive and not exceed 10,000,000,000 rubles.", "invalid_amount");
        if (request.Shares is null || request.Shares.Count == 0 || request.Shares.Any(x => x.AmountKopecks <= 0))
            throw Validation("At least one positive share is required.", "invalid_shares");
        if (request.Shares.Select(x => x.ParticipantId).Distinct().Count() != request.Shares.Count)
            throw Validation("Each participant can have only one share.", "duplicate_share");
        long total;
        try { total = request.Shares.Aggregate(0L, (sum, x) => checked(sum + x.AmountKopecks)); }
        catch (OverflowException) { throw Validation("Share total is too large.", "invalid_shares"); }
        if (total != request.AmountKopecks) throw Validation("Share total must equal the expense amount.", "share_total_mismatch");
    }

    private Task<ExpenseDto> SaveExpense(long userId, Guid groupId, Guid? expenseId, ExpenseRequest request, CancellationToken ct)
    {
        ValidateExpense(request);
        return WithGroupLock(groupId, async () =>
        {
            await RequireWriteAccess(userId, groupId, ct);
            var activeIds = await db.GroupParticipants.Where(x => x.GroupId == groupId && x.IsActive).Select(x => x.ParticipantId).ToListAsync(ct);
            if (!activeIds.Contains(request.PayerId) || request.Shares.Any(x => !activeIds.Contains(x.ParticipantId)))
                throw Conflict("The participant list changed. Refresh and try again.", "stale_participants");
            Expense expense;
            if (expenseId is { } id)
            {
                expense = await db.Expenses.Include(x => x.Shares).FirstOrDefaultAsync(x => x.GroupId == groupId && x.Id == id, ct)
                    ?? throw NotFound("Expense not found.");
                if (expense.AuthorId != userId) throw Forbidden("Only the expense author can edit it.");
                db.ExpenseShares.RemoveRange(expense.Shares);
            }
            else
            {
                expense = new Expense { GroupId = groupId, AuthorId = userId };
                db.Expenses.Add(expense);
            }
            expense.Description = request.Description.Trim();
            expense.AmountKopecks = request.AmountKopecks;
            expense.PayerId = request.PayerId;
            expense.Shares = request.Shares.Select(x => new ExpenseShare { UserId = x.ParticipantId, AmountKopecks = x.AmountKopecks }).ToList();
            await CancelPending(groupId, ct);
            var names = (await ParticipantInfos(groupId, false, ct)).ToDictionary(x => x.Id, x => x.Name);
            return ToExpense(expense, userId, names, true);
        }, ct);
    }

    private async Task<ExpenseGroup> RequireGroup(long userId, Guid groupId, CancellationToken ct) =>
        await db.GroupMembers.Where(x => x.GroupId == groupId && x.UserId == userId && x.IsActive && !x.Group.IsArchived &&
                (x.Group.Type == GroupType.Collective || x.Group.OwnerId == userId))
            .Select(x => x.Group).FirstOrDefaultAsync(ct) ?? throw NotFound("Group not found.");

    private async Task<ExpenseGroup> RequireStandaloneOwner(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await RequireGroup(userId, groupId, ct);
        if (group.Type != GroupType.Standalone || group.OwnerId != userId)
            throw Forbidden("Only a standalone group owner can manage participants.");
        return group;
    }

    private async Task RequireWriteAccess(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await RequireGroup(userId, groupId, ct);
        if (group.Type == GroupType.Standalone && group.OwnerId != userId) throw Forbidden("Only the group owner can modify expenses.");
    }

    private async Task<GroupParticipant> RequireManagedParticipant(Guid groupId, long participantId, CancellationToken ct) =>
        await db.GroupParticipants.FirstOrDefaultAsync(x => x.GroupId == groupId && x.ParticipantId == participantId && x.IsActive && x.TelegramUserId == null, ct)
        ?? throw NotFound("Managed participant not found.");

    private async Task<GroupSummaryDto> ToGroupSummary(ExpenseGroup group, long userId, CancellationToken ct)
    {
        var balances = await balanceService.GetBalances(group.Id, ct);
        return new(group.Id, group.Name, group.Type, group.OwnerId,
            await db.GroupParticipants.CountAsync(x => x.GroupId == group.Id && x.IsActive, ct),
            balances.GetValueOrDefault(userId),
            await db.Expenses.Where(x => x.GroupId == group.Id).SumAsync(x => (long?)x.AmountKopecks, ct) ?? 0);
    }

    private async Task<List<ParticipantInfo>> ParticipantInfos(Guid groupId, bool activeOnly, CancellationToken ct)
    {
        var query = db.GroupParticipants.Where(x => x.GroupId == groupId);
        if (activeOnly) query = query.Where(x => x.IsActive);
        return (await query.Include(x => x.TelegramUser).ToListAsync(ct)).Select(x => new ParticipantInfo(x.ParticipantId,
            x.TelegramUser?.DisplayName ?? x.DisplayName ?? "Без имени", x.TelegramUser?.PaymentDetails ?? x.PaymentDetails, x.TelegramUserId))
            .OrderBy(x => x.Name).ToList();
    }

    private async Task CancelPending(Guid groupId, CancellationToken ct)
    {
        foreach (var transfer in await db.Transfers.Where(x => x.GroupId == groupId && x.Status == TransferStatus.Pending).ToListAsync(ct))
            transfer.Status = TransferStatus.Cancelled;
    }

    private async Task WithGroupLock(Guid groupId, Func<Task> action, CancellationToken ct) =>
        await WithGroupLock<object?>(groupId, async () => { await action(); return null; }, ct);

    private async Task<T> WithGroupLock<T>(Guid groupId, Func<Task<T>> action, CancellationToken ct)
    {
        IDbContextTransaction? transaction = null;
        if (db.Database.IsRelational())
        {
            transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Groups\" WHERE \"Id\" = {groupId} FOR UPDATE", ct);
        }
        try
        {
            var result = await action();
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        }
        finally
        {
            if (transaction is not null) await transaction.DisposeAsync();
        }
    }

    private async Task Notify(long chatId, string text, CancellationToken ct)
    {
        try { await bot.SendMessage(chatId, text, cancellationToken: ct); }
        catch (Exception exception) { logger.LogWarning(exception, "Could not send transfer notification to {ChatId}", chatId); }
    }

    private static UserProfileDto ToProfile(AppUser x) => new(x.TelegramId, x.DisplayName, x.Username, x.PaymentDetails);
    private static ParticipantDto ToParticipant(GroupParticipant x, long userId, bool canEdit) =>
        new(x.ParticipantId, x.DisplayName ?? x.TelegramUser?.DisplayName ?? "Без имени", x.TelegramUserId,
            x.PaymentDetails ?? x.TelegramUser?.PaymentDetails, x.TelegramUserId == userId, canEdit);
    private static ExpenseDto ToExpense(Expense x, long userId, IReadOnlyDictionary<long, string> names, bool shares = false) =>
        new(x.Id, x.GroupId, x.Description, x.AmountKopecks, x.PayerId, names.GetValueOrDefault(x.PayerId, x.PayerId.ToString()),
            x.AuthorId, x.AuthorId == userId, x.CreatedAt, shares ? x.Shares.Select(s => new ExpenseShareDto(s.UserId,
                names.GetValueOrDefault(s.UserId, s.UserId.ToString()), s.AmountKopecks)).ToList() : null);
    private static string Money(long kopecks) => $"{kopecks / 100}.{Math.Abs(kopecks % 100):00} ₽";
    private static string ValidateRequired(string? value, int maxLength, string field)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maxLength)
            throw Validation($"{field} must contain between 1 and {maxLength} characters.", "validation_error");
        return normalized;
    }
    private static void ValidateOptional(string? value, int maxLength, string field)
    {
        if (value?.Length > maxLength) throw Validation($"{field} must not exceed {maxLength} characters.", "validation_error");
    }
    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ApiException Validation(string detail, string code) => new(400, "Validation failed", detail, code);
    private static ApiException Forbidden(string detail) => new(403, "Forbidden", detail, "forbidden");
    private static ApiException NotFound(string detail) => new(404, "Not found", detail, "not_found");
    private static ApiException Conflict(string detail, string code) => new(409, "Conflict", detail, code);

    private sealed record ParticipantInfo(long Id, string Name, string? PaymentDetails, long? TelegramUserId);
}
