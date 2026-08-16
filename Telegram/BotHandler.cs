using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SplitMoneyTg.Application;
using SplitMoneyTg.Domain;
using SplitMoneyTg.Infrastructure;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace SplitMoneyTg.Telegram;

public sealed class BotHandler(
    ITelegramBotClient bot,
    AppDbContext db,
    BalanceService balanceService)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task Handle(Update update, CancellationToken ct)
    {
        if (await db.ProcessedUpdates.AnyAsync(x => x.UpdateId == update.Id, ct)) return;
        await HandleCore(update, ct);
        db.ProcessedUpdates.Add(new ProcessedUpdate { UpdateId = update.Id });
        await db.SaveChangesAsync(ct);
    }

    private async Task HandleCore(Update update, CancellationToken ct)
    {
        if (update.CallbackQuery is { } callback)
        {
            try
            {
                await bot.AnswerCallbackQuery(callback.Id, cancellationToken: ct);
            }
            catch (global::Telegram.Bot.Exceptions.ApiRequestException exception) when (exception.ErrorCode == 400)
            {
                // Telegram may redeliver a callback after its acknowledgement window expires.
            }
            try
            {
                await HandleCallback(callback, ct);
            }
            catch (Exception exception) when (exception is FormatException or OverflowException or ArgumentOutOfRangeException or IndexOutOfRangeException)
            {
                await ClearSession(callback.From.Id, ct);
                await ShowMain(callback.From.Id, "Кнопка устарела. Выберите действие в актуальном меню.", ct);
            }
            return;
        }

        if (update.Message is not { From: { } from, Text: { } text }) return;
        await UpsertUser(from, ct);
        if (text.StartsWith("/start", StringComparison.Ordinal))
        {
            await ClearSession(from.Id, ct);
            var argument = text.Split(' ', 2).ElementAtOrDefault(1);
            if (argument?.StartsWith("join_") == true)
                await Join(from.Id, argument[5..], ct);
            else
                await ShowMain(from.Id, "Бот поможет разделить покупки и свести долги.", ct);
            return;
        }
        if (text == "/cancel")
        {
            await ClearSession(from.Id, ct);
            await ShowMain(from.Id, "Действие отменено.", ct);
            return;
        }

        var session = await db.Sessions.FindAsync([from.Id], ct);
        if (session is null)
        {
            await ShowMain(from.Id, "Выберите действие.", ct);
            return;
        }
        await HandleText(from.Id, text.Trim(), session, ct);
    }

    private async Task HandleCallback(CallbackQuery callback, CancellationToken ct)
    {
        if (callback.From is not { } from || callback.Data is not { } data) return;
        await UpsertUser(from, ct);
        var chatId = from.Id;

        if (data == "main") { await ClearSession(chatId, ct); await ShowMain(chatId, "Главное меню", ct); return; }
        if (data == "groups") { await ShowGroups(chatId, ct); return; }
        if (data == "newgroup") { await ShowGroupTypePicker(chatId, ct); return; }
        if (data.StartsWith("group_type:")) { await ChooseGroupType(chatId, data, ct); return; }
        if (data == "details") { await SetSession(chatId, "payment_details", new(), ct); await Send(chatId, "Введите реквизиты свободным текстом (до 500 символов):", ct); return; }
        if (data.StartsWith("group:")) { await ShowGroup(chatId, ParseGuid(data[6..]), ct); return; }
        if (data.StartsWith("group_delete_confirm:")) { await DeleteGroup(chatId, ParseGuid(data["group_delete_confirm:".Length..]), ct); return; }
        if (data.StartsWith("group_delete:")) { await ConfirmDeleteGroup(chatId, ParseGuid(data["group_delete:".Length..]), ct); return; }
        if (data.StartsWith("invite:")) { await Invite(chatId, ParseGuid(data[7..]), ct); return; }
        if (data.StartsWith("balance:")) { await ShowBalance(chatId, ParseGuid(data[8..]), ct); return; }
        if (data.StartsWith("expenses:")) { await ShowExpenses(chatId, ParseGuid(data[9..]), ct); return; }
        if (data.StartsWith("expense_new:")) { await StartExpense(chatId, ParseGuid(data[12..]), null, ct); return; }
        if (data.StartsWith("expense_delete:")) { await DeleteExpense(chatId, ParseGuid(data[15..]), ct); return; }
        if (data.StartsWith("expense_edit:")) { await EditExpense(chatId, ParseGuid(data[13..]), ct); return; }
        if (data.StartsWith("managed_new:")) { await StartManagedParticipant(chatId, ParseGuid(data["managed_new:".Length..]), ct); return; }
        if (data.StartsWith("participants_list:")) { await ShowParticipants(chatId, ParseGuid(data["participants_list:".Length..]), ct); return; }
        if (data == "managed_details_skip") { await SaveManagedParticipant(chatId, null, ct); return; }
        if (data.StartsWith("me:")) { await StartManagedParticipantEdit(chatId, data, ct); return; }
        if (data == "managed_edit_menu") { await ShowManagedParticipantEditMenu(chatId, ct); return; }
        if (data == "managed_edit_name") { await StartManagedParticipantNameEdit(chatId, ct); return; }
        if (data == "managed_edit_details") { await StartManagedParticipantDetailsEdit(chatId, ct); return; }
        if (data == "managed_edit_details_clear") { await ClearManagedParticipantDetails(chatId, ct); return; }
        if (data == "managed_delete") { await ConfirmManagedParticipantDelete(chatId, ct); return; }
        if (data == "managed_delete_confirm") { await DeleteManagedParticipant(chatId, ct); return; }
        if (data.StartsWith("payer:")) { await ChoosePayer(chatId, long.Parse(data[6..], CultureInfo.InvariantCulture), ct); return; }
        if (data.StartsWith("participant:")) { await ToggleParticipant(chatId, long.Parse(data[12..], CultureInfo.InvariantCulture), ct); return; }
        if (data == "participants_all") { await SelectAllParticipants(chatId, ct); return; }
        if (data == "participants_done") { await ParticipantsDone(chatId, ct); return; }
        if (data == "split_equal") { await SaveEqualExpense(chatId, ct); return; }
        if (data == "split_manual") { await StartManualShares(chatId, ct); return; }
        if (data.StartsWith("pay:")) { await MarkPaid(chatId, data[4..], ct); return; }
        if (data.StartsWith("transfer_confirm:")) { await ResolveTransfer(chatId, ParseGuid(data[17..]), true, ct); return; }
        if (data.StartsWith("transfer_reject:")) { await ResolveTransfer(chatId, ParseGuid(data[16..]), false, ct); return; }
        if (data.StartsWith("leave:")) { await LeaveGroup(chatId, ParseGuid(data[6..]), ct); return; }
    }

    private async Task HandleText(long userId, string text, UserSession session, CancellationToken ct)
    {
        var data = Deserialize(session.DataJson);
        if (data.GroupId != Guid.Empty && await MemberGroup(userId, data.GroupId, ct) is null)
        {
            await ClearSession(userId, ct);
            await ShowGroups(userId, ct);
            return;
        }
        switch (session.State)
        {
            case "group_name":
                if (text.Length is < 1 or > 100) { await Send(userId, "Название должно содержать от 1 до 100 символов.", ct); return; }
                var group = new ExpenseGroup { Name = text, OwnerId = userId, Type = data.GroupType };
                group.Members.Add(new GroupMember { UserId = userId });
                group.Participants.Add(new GroupParticipant { ParticipantId = userId, TelegramUserId = userId });
                db.Groups.Add(group);
                await db.SaveChangesAsync(ct);
                await ClearSession(userId, ct);
                await ShowGroup(userId, group.Id, ct);
                break;
            case "payment_details":
                if (text.Length > 500) { await Send(userId, "Реквизиты не должны превышать 500 символов.", ct); return; }
                (await db.Users.FindAsync([userId], ct))!.PaymentDetails = text;
                await db.SaveChangesAsync(ct);
                await ClearSession(userId, ct);
                await ShowMain(userId, "Реквизиты сохранены.", ct);
                break;
            case "managed_name":
                if (text.Length is < 1 or > 100) { await Send(userId, "Имя должно содержать от 1 до 100 символов.", ct); return; }
                data.ManagedName = text;
                await SetSession(userId, "managed_details", data, ct);
                await Send(userId, "Введите реквизиты участника или нажмите «Пропустить»:", ct,
                    new InlineKeyboardMarkup([[InlineKeyboardButton.WithCallbackData("⏭️ Пропустить", "managed_details_skip")]]));
                break;
            case "managed_details":
                if (text.Length > 500) { await Send(userId, "Реквизиты не должны превышать 500 символов.", ct); return; }
                await SaveManagedParticipant(userId, text, ct);
                break;
            case "managed_edit_name":
                if (text.Length is < 1 or > 100) { await Send(userId, "Имя должно содержать от 1 до 100 символов.", ct); return; }
                await SaveManagedParticipantName(userId, text, ct);
                break;
            case "managed_edit_details":
                if (text.Length > 500) { await Send(userId, "Реквизиты не должны превышать 500 символов.", ct); return; }
                await SaveManagedParticipantDetails(userId, text, ct);
                break;
            case "expense_description":
                if (text.Length is < 1 or > 200) { await Send(userId, "Описание должно содержать от 1 до 200 символов.", ct); return; }
                data.Description = text;
                await SetSession(userId, "expense_amount", data, ct);
                await Send(userId, "Введите сумму в рублях, например 1250,50:", ct);
                break;
            case "expense_amount":
                if (!TryMoney(text, out var amount)) { await Send(userId, "Некорректная сумма. Пример: 1250,50", ct); return; }
                data.AmountKopecks = amount;
                await SetSession(userId, "expense_payer", data, ct);
                await ShowPayers(userId, data.GroupId, ct);
                break;
            case "manual_share":
                if (!TryMoney(text, out var share)) { await Send(userId, "Некорректная сумма. Пример: 500,00", ct); return; }
                BalanceService.SetShare(data.Shares, data.ParticipantIds, data.ManualIndex, share);
                data.ManualIndex++;
                if (data.ManualIndex < data.ParticipantIds.Count)
                {
                    await SetSession(userId, "manual_share", data, ct);
                    await AskManualShare(userId, data, ct);
                }
                else if (data.Shares.Values.Sum() != data.AmountKopecks)
                {
                    data.Shares.Clear(); data.ManualIndex = 0;
                    await SetSession(userId, "manual_share", data, ct);
                    await Send(userId, $"Сумма долей должна быть {Money(data.AmountKopecks)}. Введите доли заново.", ct);
                    await AskManualShare(userId, data, ct);
                }
                else await SaveExpense(userId, data, ct);
                break;
        }
    }

    private async Task ShowMain(long chatId, string text, CancellationToken ct) => await Send(chatId, text, ct,
        new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("👥 Мои группы", "groups"), InlineKeyboardButton.WithCallbackData("➕ Создать группу", "newgroup")],
            [InlineKeyboardButton.WithCallbackData("💳 Реквизиты", "details")]
        ]));

    private async Task ShowGroups(long userId, CancellationToken ct)
    {
        var groups = await db.GroupMembers.Where(x => x.UserId == userId && x.IsActive && !x.Group.IsArchived &&
                                                  (x.Group.Type == GroupType.Collective || x.Group.OwnerId == userId))
            .Select(x => x.Group).OrderBy(x => x.Name).ToListAsync(ct);
        var rows = groups.Select(x => new[] { InlineKeyboardButton.WithCallbackData(
            $"{(x.Type == GroupType.Collective ? "👥" : "👤")} [{(x.Type == GroupType.Collective ? "Коллективная" : "Самостоятельная")}] {x.Name}", $"group:{x.Id:N}") }).ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData("➕ Создать группу", "newgroup")]);
        rows.Add([InlineKeyboardButton.WithCallbackData("◀️ Назад", "main")]);
        await Send(userId, groups.Count == 0 ? "У вас пока нет групп." : "Ваши группы:", ct, new InlineKeyboardMarkup(rows));
    }

    private async Task ShowGroup(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await MemberGroup(userId, groupId, ct);
        if (group is null) { await ShowGroups(userId, ct); return; }
        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("🧾 Добавить покупку", $"expense_new:{group.Id:N}") },
            new[] { InlineKeyboardButton.WithCallbackData("🛒 Покупки", $"expenses:{group.Id:N}"), InlineKeyboardButton.WithCallbackData("⚖️ Баланс и долги", $"balance:{group.Id:N}") }
        };
        if (group.Type == GroupType.Collective)
        {
            rows.Add([InlineKeyboardButton.WithCallbackData("✉️ Пригласить", $"invite:{group.Id:N}"), InlineKeyboardButton.WithCallbackData("🚪 Выйти", $"leave:{group.Id:N}")]);
            rows.Add([InlineKeyboardButton.WithCallbackData("👥 Участники", $"participants_list:{group.Id:N}")]);
        }
        else
        {
            rows.Add([InlineKeyboardButton.WithCallbackData("➕ Добавить пользователя", $"managed_new:{group.Id:N}")]);
            rows.Add([InlineKeyboardButton.WithCallbackData("👥 Участники", $"participants_list:{group.Id:N}")]);
        }
        if (group.OwnerId == userId)
            rows.Add([InlineKeyboardButton.WithCallbackData("🗑️ Удалить группу", $"group_delete:{group.Id:N}")]);
        rows.Add([InlineKeyboardButton.WithCallbackData("◀️ К группам", "groups")]);
        await Send(userId, $"Группа: {group.Name}\nТип: {(group.Type == GroupType.Collective ? "коллективная" : "самостоятельная")}", ct, new InlineKeyboardMarkup(rows));
    }

    private async Task ConfirmDeleteGroup(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(x => x.Id == groupId && x.OwnerId == userId && !x.IsArchived, ct);
        if (group is null) { await ShowGroups(userId, ct); return; }
        await Send(userId, $"Удалить группу «{group.Name}»? Она исчезнет у всех участников. Отменить это действие через бота будет нельзя.", ct,
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("🗑️ Удалить", $"group_delete_confirm:{group.Id:N}")],
                [InlineKeyboardButton.WithCallbackData("❌ Отмена", $"group:{group.Id:N}")]
            ]));
    }

    private async Task DeleteGroup(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await db.Groups.FirstOrDefaultAsync(x => x.Id == groupId && x.OwnerId == userId && !x.IsArchived, ct);
        if (group is null) { await ShowGroups(userId, ct); return; }

        group.IsArchived = true;
        var invitations = await db.Invitations.Where(x => x.GroupId == groupId && x.IsActive).ToListAsync(ct);
        foreach (var invitation in invitations) invitation.IsActive = false;
        var transfers = await db.Transfers.Where(x => x.GroupId == groupId && x.Status == TransferStatus.Pending).ToListAsync(ct);
        foreach (var transfer in transfers) transfer.Status = TransferStatus.Cancelled;
        await db.SaveChangesAsync(ct);
        await ClearSession(userId, ct);
        await Send(userId, $"Группа «{group.Name}» удалена.", ct);
        await ShowGroups(userId, ct);
    }

    private async Task ShowGroupTypePicker(long userId, CancellationToken ct)
    {
        await ClearSession(userId, ct);
        await Send(userId, "Выберите тип группы:", ct, new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("👥 Коллективная", $"group_type:{(int)GroupType.Collective}")],
            [InlineKeyboardButton.WithCallbackData("👤 Самостоятельная", $"group_type:{(int)GroupType.Standalone}")],
            [InlineKeyboardButton.WithCallbackData("◀️ Назад", "main")]
        ]));
    }

    private async Task ChooseGroupType(long userId, string callbackData, CancellationToken ct)
    {
        if (!int.TryParse(callbackData["group_type:".Length..], out var value) || !Enum.IsDefined(typeof(GroupType), value))
        { await ShowMain(userId, "Неизвестный тип группы.", ct); return; }
        await SetSession(userId, "group_name", new FlowData { GroupType = (GroupType)value }, ct);
        await Send(userId, "Введите название группы:", ct);
    }

    private async Task Invite(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await MemberGroup(userId, groupId, ct);
        if (group is null) return;
        if (group.Type != GroupType.Collective) { await ShowGroup(userId, groupId, ct); return; }
        var invite = new Invitation { GroupId = groupId, CreatedById = userId, Token = Guid.NewGuid().ToString("N") };
        db.Invitations.Add(invite);
        await db.SaveChangesAsync(ct);
        var me = await bot.GetMe(ct);
        await Send(userId, $"Приглашение в «{group.Name}»:\nhttps://t.me/{me.Username}?start=join_{invite.Token}", ct);
    }

    private async Task Join(long userId, string token, CancellationToken ct)
    {
        var invite = await db.Invitations.FirstOrDefaultAsync(x => x.Token == token && x.IsActive, ct);
        if (invite is null) { await ShowMain(userId, "Приглашение недействительно.", ct); return; }
        var group = await db.Groups.FindAsync([invite.GroupId], ct);
        if (group is null || group.IsArchived || group.Type != GroupType.Collective) { await ShowMain(userId, "Эта группа не принимает приглашения.", ct); return; }
        var member = await db.GroupMembers.FindAsync([invite.GroupId, userId], ct);
        if (member is null) db.GroupMembers.Add(new GroupMember { GroupId = invite.GroupId, UserId = userId });
        else member.IsActive = true;
        var participant = await db.GroupParticipants.FindAsync([invite.GroupId, userId], ct);
        if (participant is null) db.GroupParticipants.Add(new GroupParticipant { GroupId = invite.GroupId, ParticipantId = userId, TelegramUserId = userId });
        else participant.IsActive = true;
        await db.SaveChangesAsync(ct);
        await ShowGroup(userId, invite.GroupId, ct);
    }

    private async Task StartManagedParticipant(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await MemberGroup(userId, groupId, ct);
        if (group is null || group.Type != GroupType.Standalone || group.OwnerId != userId)
        { await ShowGroups(userId, ct); return; }
        await SetSession(userId, "managed_name", new FlowData { GroupId = groupId }, ct);
        await Send(userId, "Введите имя нового участника:", ct);
    }

    private async Task SaveManagedParticipant(long userId, string? paymentDetails, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "managed_details", ct); if (session is null) return;
        var data = Deserialize(session.DataJson);
        var group = await MemberGroup(userId, data.GroupId, ct);
        if (group is null || group.Type != GroupType.Standalone || group.OwnerId != userId)
        { await ClearSession(userId, ct); await ShowGroups(userId, ct); return; }
        var duplicate = false;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await LockGroup(data.GroupId, ct);
            duplicate = (await Participants(data.GroupId, ct)).Any(x =>
                string.Equals(x.DisplayName, data.ManagedName, StringComparison.OrdinalIgnoreCase));
            if (!duplicate)
            {
                long participantId;
                do participantId = Random.Shared.NextInt64(long.MinValue, 0);
                while (await db.GroupParticipants.AnyAsync(x => x.GroupId == data.GroupId && x.ParticipantId == participantId, ct));
                db.GroupParticipants.Add(new GroupParticipant
                {
                    GroupId = data.GroupId,
                    ParticipantId = participantId,
                    DisplayName = data.ManagedName,
                    PaymentDetails = paymentDetails
                });
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }
        if (duplicate)
        { await Send(userId, "Активный участник с таким именем уже существует. Введите другое имя:", ct); await SetSession(userId, "managed_name", data, ct); return; }
        await ClearSession(userId, ct);
        await Send(userId, $"Участник «{data.ManagedName}» добавлен.", ct);
        await ShowGroup(userId, data.GroupId, ct);
    }

    private async Task StartManagedParticipantEdit(long userId, string callbackData, CancellationToken ct)
    {
        var payload = callbackData.Split(':');
        if (payload.Length != 3 || !ulong.TryParse(payload[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var encodedId))
            throw new FormatException("Invalid managed participant callback");
        var data = new FlowData
        {
            GroupId = ParseGuid(payload[1]),
            ManagedParticipantId = unchecked((long)encodedId)
        };
        var group = await MemberGroup(userId, data.GroupId, ct);
        var participant = await db.GroupParticipants.FindAsync([data.GroupId, data.ManagedParticipantId], ct);
        if (group is null || group.Type != GroupType.Standalone || group.OwnerId != userId ||
            participant is null || !participant.IsActive || participant.TelegramUserId is not null)
        { await ShowGroups(userId, ct); return; }
        await SetSession(userId, "managed_edit_menu", data, ct);
        await ShowManagedParticipantEditMenu(userId, ct);
    }

    private async Task ShowManagedParticipantEditMenu(long userId, CancellationToken ct)
    {
        var context = await RequiredManagedParticipant(userId, ct, "managed_edit_menu", "managed_edit_name", "managed_edit_details", "managed_delete_confirm");
        if (context is not { } edit) return;
        await SetSession(userId, "managed_edit_menu", edit.Data, ct);
        var details = string.IsNullOrWhiteSpace(edit.Participant.PaymentDetails) ? "не указаны" : edit.Participant.PaymentDetails;
        var usedInPurchases = await HasParticipantPurchases(edit.Data.GroupId, edit.Data.ManagedParticipantId, ct);
        var text = $"Участник: {edit.Participant.DisplayName}\nРеквизиты: {details}" +
                   (usedInPurchases ? "\n\nУдаление недоступно: участник используется в покупках." : "");
        var rows = new List<InlineKeyboardButton[]>
        {
            new[] { InlineKeyboardButton.WithCallbackData("✏️ Изменить имя", "managed_edit_name") },
            new[] { InlineKeyboardButton.WithCallbackData("💳 Изменить реквизиты", "managed_edit_details") }
        };
        if (!usedInPurchases)
            rows.Add([InlineKeyboardButton.WithCallbackData("🗑️ Удалить участника", "managed_delete")]);
        rows.Add([InlineKeyboardButton.WithCallbackData("◀️ К участникам", $"participants_list:{edit.Data.GroupId:N}")]);
        await Send(userId, text, ct, new InlineKeyboardMarkup(rows));
    }

    private async Task StartManagedParticipantNameEdit(long userId, CancellationToken ct)
    {
        var context = await RequiredManagedParticipant(userId, ct, "managed_edit_menu");
        if (context is not { } edit) return;
        await SetSession(userId, "managed_edit_name", edit.Data, ct);
        await Send(userId, $"Введите новое имя для «{edit.Participant.DisplayName}»:", ct,
            new InlineKeyboardMarkup([[InlineKeyboardButton.WithCallbackData("◀️ Назад", "managed_edit_menu")]]));
    }

    private async Task SaveManagedParticipantName(long userId, string name, CancellationToken ct)
    {
        var context = await RequiredManagedParticipant(userId, ct, "managed_edit_name");
        if (context is not { } edit) return;
        var duplicate = false;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await LockGroup(edit.Data.GroupId, ct);
            duplicate = (await Participants(edit.Data.GroupId, ct)).Any(x =>
                x.ParticipantId != edit.Data.ManagedParticipantId && string.Equals(x.DisplayName, name, StringComparison.OrdinalIgnoreCase));
            if (!duplicate)
            {
                edit.Participant.DisplayName = name;
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }
        if (duplicate) { await Send(userId, "Активный участник с таким именем уже существует. Введите другое имя:", ct); return; }
        await ClearSession(userId, ct);
        await Send(userId, $"Имя участника изменено на «{name}».", ct);
        await ShowParticipants(userId, edit.Data.GroupId, ct);
    }

    private async Task StartManagedParticipantDetailsEdit(long userId, CancellationToken ct)
    {
        var context = await RequiredManagedParticipant(userId, ct, "managed_edit_menu");
        if (context is not { } edit) return;
        await SetSession(userId, "managed_edit_details", edit.Data, ct);
        await Send(userId, $"Введите новые реквизиты для «{edit.Participant.DisplayName}»:", ct,
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("🗑️ Удалить реквизиты", "managed_edit_details_clear")],
                [InlineKeyboardButton.WithCallbackData("◀️ Назад", "managed_edit_menu")]
            ]));
    }

    private async Task SaveManagedParticipantDetails(long userId, string paymentDetails, CancellationToken ct)
    {
        var context = await RequiredManagedParticipant(userId, ct, "managed_edit_details");
        if (context is not { } edit) return;
        edit.Participant.PaymentDetails = paymentDetails;
        await db.SaveChangesAsync(ct);
        await ClearSession(userId, ct);
        await Send(userId, $"Реквизиты участника «{edit.Participant.DisplayName}» обновлены.", ct);
        await ShowParticipants(userId, edit.Data.GroupId, ct);
    }

    private async Task ClearManagedParticipantDetails(long userId, CancellationToken ct)
    {
        var context = await RequiredManagedParticipant(userId, ct, "managed_edit_details");
        if (context is not { } edit) return;
        edit.Participant.PaymentDetails = null;
        await db.SaveChangesAsync(ct);
        await ClearSession(userId, ct);
        await Send(userId, $"Реквизиты участника «{edit.Participant.DisplayName}» удалены.", ct);
        await ShowParticipants(userId, edit.Data.GroupId, ct);
    }

    private async Task ConfirmManagedParticipantDelete(long userId, CancellationToken ct)
    {
        var context = await RequiredManagedParticipant(userId, ct, "managed_edit_menu");
        if (context is not { } edit) return;
        if (await HasParticipantPurchases(edit.Data.GroupId, edit.Data.ManagedParticipantId, ct))
        { await ShowManagedParticipantEditMenu(userId, ct); return; }
        await SetSession(userId, "managed_delete_confirm", edit.Data, ct);
        await Send(userId, $"Удалить участника «{edit.Participant.DisplayName}»? Отменить это действие будет нельзя.", ct,
            new InlineKeyboardMarkup([
                [InlineKeyboardButton.WithCallbackData("🗑️ Удалить", "managed_delete_confirm")],
                [InlineKeyboardButton.WithCallbackData("❌ Отмена", "managed_edit_menu")]
            ]));
    }

    private async Task DeleteManagedParticipant(long userId, CancellationToken ct)
    {
        var context = await RequiredManagedParticipant(userId, ct, "managed_delete_confirm");
        if (context is not { } edit) return;
        var usedInPurchases = false;
        var deleted = false;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await LockGroup(edit.Data.GroupId, ct);
            usedInPurchases = await HasParticipantPurchases(edit.Data.GroupId, edit.Data.ManagedParticipantId, ct);
            var participant = await db.GroupParticipants.FirstOrDefaultAsync(x =>
                x.GroupId == edit.Data.GroupId && x.ParticipantId == edit.Data.ManagedParticipantId &&
                x.IsActive && x.TelegramUserId == null, ct);
            if (!usedInPurchases && participant is not null)
            {
                db.GroupParticipants.Remove(participant);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                deleted = true;
            }
        }
        if (!deleted)
        {
            if (usedInPurchases)
            {
                await SetSession(userId, "managed_edit_menu", edit.Data, ct);
                await Send(userId, "Удаление недоступно: участник используется в покупках.", ct);
                await ShowManagedParticipantEditMenu(userId, ct);
            }
            else
            {
                await ClearSession(userId, ct);
                await ShowParticipants(userId, edit.Data.GroupId, ct);
            }
            return;
        }
        await ClearSession(userId, ct);
        await Send(userId, $"Участник «{edit.Participant.DisplayName}» удален.", ct);
        await ShowParticipants(userId, edit.Data.GroupId, ct);
    }

    private async Task ShowParticipants(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await MemberGroup(userId, groupId, ct);
        if (group is null) { await ShowGroups(userId, ct); return; }
        var participants = await Participants(groupId, ct);
        var text = group.Type == GroupType.Collective
            ? "Участники:\n" + string.Join("\n", participants.Select(x => $"• {x.DisplayName}"))
            : "Участники:\n" + string.Join("\n", participants.Select(x =>
                $"• {x.DisplayName}" + (string.IsNullOrWhiteSpace(x.PaymentDetails) ? "" : $"\n  Реквизиты: {x.PaymentDetails}")));
        var rows = new List<InlineKeyboardButton[]>();
        if (group.Type == GroupType.Standalone)
        {
            rows.AddRange(participants.Where(x => x.TelegramUserId is null).Select(x => new[]
            {
                InlineKeyboardButton.WithCallbackData($"✏️ {Short(x.DisplayName)}", $"me:{groupId:N}:{EncodeParticipantId(x.ParticipantId)}")
            }));
            rows.Add([InlineKeyboardButton.WithCallbackData("➕ Добавить пользователя", $"managed_new:{groupId:N}")]);
        }
        rows.Add([InlineKeyboardButton.WithCallbackData("◀️ Назад", $"group:{groupId:N}")]);
        await SendLong(userId, text, ct, new InlineKeyboardMarkup(rows));
    }

    private async Task StartExpense(long userId, Guid groupId, Guid? expenseId, CancellationToken ct)
    {
        if (await MemberGroup(userId, groupId, ct) is null) return;
        await SetSession(userId, "expense_description", new FlowData { GroupId = groupId, ExpenseId = expenseId }, ct);
        await Send(userId, "Введите название покупки:", ct);
    }

    private async Task ShowPayers(long userId, Guid groupId, CancellationToken ct)
    {
        var members = await Participants(groupId, ct);
        var rows = members.Select(x => new[] { InlineKeyboardButton.WithCallbackData(x.DisplayName, $"payer:{x.ParticipantId}") });
        await Send(userId, "Кто оплатил покупку?", ct, new InlineKeyboardMarkup(rows));
    }

    private async Task ChoosePayer(long userId, long payerId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "expense_payer", ct); if (session is null) return;
        var data = Deserialize(session.DataJson);
        if (!await IsParticipant(payerId, data.GroupId, ct)) return;
        data.PayerId = payerId;
        await SetSession(userId, "expense_participants", data, ct);
        await ShowParticipantPicker(userId, data, ct);
    }

    private async Task ToggleParticipant(long userId, long participantId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "expense_participants", ct); if (session is null) return;
        var data = Deserialize(session.DataJson);
        if (!await IsParticipant(participantId, data.GroupId, ct)) return;
        if (!data.ParticipantIds.Remove(participantId)) data.ParticipantIds.Add(participantId);
        await SetSession(userId, "expense_participants", data, ct);
        await ShowParticipantPicker(userId, data, ct);
    }

    private async Task SelectAllParticipants(long userId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "expense_participants", ct); if (session is null) return;
        var data = Deserialize(session.DataJson);
        data.ParticipantIds = (await Participants(data.GroupId, ct)).Select(x => x.ParticipantId).ToList();
        await SetSession(userId, "expense_participants", data, ct);
        await ShowParticipantPicker(userId, data, ct);
    }

    private async Task ShowParticipantPicker(long userId, FlowData data, CancellationToken ct)
    {
        var members = await Participants(data.GroupId, ct);
        var rows = members.Select(x => new[] { InlineKeyboardButton.WithCallbackData(
            $"{(data.ParticipantIds.Contains(x.ParticipantId) ? "✓" : "○")} {x.DisplayName}", $"participant:{x.ParticipantId}") }).ToList();
        rows.Insert(0, [InlineKeyboardButton.WithCallbackData("☑️ Выбрать всех", "participants_all")]);
        rows.Add([InlineKeyboardButton.WithCallbackData("✅ Готово", "participants_done")]);
        await Send(userId, "Выберите участников покупки:", ct, new InlineKeyboardMarkup(rows));
    }

    private async Task ParticipantsDone(long userId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "expense_participants", ct); if (session is null) return;
        var data = Deserialize(session.DataJson);
        if (data.ParticipantIds.Count == 0) { await Send(userId, "Выберите хотя бы одного участника.", ct); return; }
        await Send(userId, "Как разделить покупку?", ct, new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("➗ Поровну", "split_equal"), InlineKeyboardButton.WithCallbackData("✍️ Вручную", "split_manual")]
        ]));
    }

    private async Task SaveEqualExpense(long userId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "expense_participants", ct); if (session is null) return;
        var data = Deserialize(session.DataJson);
        var amounts = BalanceService.SplitEqually(data.AmountKopecks, data.ParticipantIds.Count);
        data.Shares.Clear();
        for (var i = 0; i < data.ParticipantIds.Count; i++)
            data.Shares[data.ParticipantIds[i]] = amounts[i];
        await SaveExpense(userId, data, ct);
    }

    private async Task StartManualShares(long userId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "expense_participants", ct); if (session is null) return;
        var data = Deserialize(session.DataJson); data.ManualIndex = 0; data.Shares.Clear();
        await SetSession(userId, "manual_share", data, ct);
        await AskManualShare(userId, data, ct);
    }

    private async Task AskManualShare(long userId, FlowData data, CancellationToken ct)
    {
        var member = await Participant(data.GroupId, data.ParticipantIds[data.ManualIndex], ct);
        await Send(userId, $"Доля для {member!.DisplayName} в рублях:", ct);
    }

    private async Task SaveExpense(long userId, FlowData data, CancellationToken ct)
    {
        var group = await MemberGroup(userId, data.GroupId, ct);
        if (group is null || group.Type == GroupType.Standalone && group.OwnerId != userId)
        {
            await ClearSession(userId, ct);
            await ShowGroups(userId, ct);
            return;
        }
        if (data.AmountKopecks <= 0 || data.Shares.Count == 0 || data.Shares.Values.Any(x => x <= 0) || data.Shares.Values.Sum() != data.AmountKopecks)
        {
            await ClearSession(userId, ct);
            await Send(userId, "Доли покупки указаны некорректно. Создайте покупку заново.", ct);
            await ShowGroup(userId, data.GroupId, ct);
            return;
        }

        string? error = null;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await LockGroup(data.GroupId, ct);
            group = await MemberGroup(userId, data.GroupId, ct);
            if (group is null || group.Type == GroupType.Standalone && group.OwnerId != userId)
                error = "access";

            var activeMemberIds = error is null
                ? await db.GroupParticipants.Where(x => x.GroupId == data.GroupId && x.IsActive)
                    .Select(x => x.ParticipantId).ToListAsync(ct)
                : [];
            if (error is null && (!activeMemberIds.Contains(data.PayerId) || data.Shares.Keys.Any(x => !activeMemberIds.Contains(x))))
                error = "participants";

            Expense? expense = null;
            if (error is null && data.ExpenseId is { } id)
            {
                expense = await db.Expenses.Include(x => x.Shares).FirstOrDefaultAsync(x =>
                    x.Id == id && x.GroupId == data.GroupId && x.AuthorId == userId, ct);
                if (expense is null) error = "expense";
                else db.ExpenseShares.RemoveRange(expense.Shares);
            }
            else if (error is null)
            {
                expense = new Expense { GroupId = data.GroupId, AuthorId = userId };
                db.Expenses.Add(expense);
            }

            if (error is null)
            {
                expense!.Description = data.Description;
                expense.AmountKopecks = data.AmountKopecks;
                expense.PayerId = data.PayerId;
                expense.Shares = data.Shares.Select(x => new ExpenseShare { UserId = x.Key, AmountKopecks = x.Value }).ToList();
                await CancelPending(data.GroupId, ct);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
        }

        if (error == "access")
        {
            await ClearSession(userId, ct);
            await ShowGroups(userId, ct);
            return;
        }
        if (error is not null)
        {
            await ClearSession(userId, ct);
            await Send(userId, error == "participants"
                ? "Состав группы изменился или данные покупки устарели. Создайте покупку заново."
                : "Покупка больше не существует. Откройте актуальный список покупок.", ct);
            await ShowGroup(userId, data.GroupId, ct);
            return;
        }
        await ClearSession(userId, ct);
        await ShowGroup(userId, data.GroupId, ct);
    }

    private async Task ShowExpenses(long userId, Guid groupId, CancellationToken ct)
    {
        if (await MemberGroup(userId, groupId, ct) is null) return;
        var groupExpenses = db.Expenses.Where(x => x.GroupId == groupId);
        var total = await groupExpenses.SumAsync(x => (long?)x.AmountKopecks, ct) ?? 0;
        var expenses = await groupExpenses.OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct);
        var users = await Participants(groupId, ct); var names = users.ToDictionary(x => x.ParticipantId, x => x.DisplayName);
        var text = $"Общая сумма покупок: {Money(total)}\n\n" +
                   (expenses.Count == 0 ? "Покупок пока нет." : string.Join("\n", expenses.Select(x => $"• {x.Description}: {Money(x.AmountKopecks)}, оплатил {names.GetValueOrDefault(x.PayerId, x.PayerId.ToString())}")));
        var rows = expenses.Where(x => x.AuthorId == userId).Select(x => new[]
        {
            InlineKeyboardButton.WithCallbackData($"✏️ Изменить: {Short(x.Description)}", $"expense_edit:{x.Id:N}"),
            InlineKeyboardButton.WithCallbackData("🗑️ Удалить", $"expense_delete:{x.Id:N}")
        }).ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData("◀️ Назад", $"group:{groupId:N}")]);
        await Send(userId, text, ct, new InlineKeyboardMarkup(rows));
    }

    private async Task EditExpense(long userId, Guid expenseId, CancellationToken ct)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(x => x.Id == expenseId && x.AuthorId == userId, ct);
        if (expense is null) { await ShowMain(userId, "Покупка не найдена. Выберите действие.", ct); return; }
        await StartExpense(userId, expense.GroupId, expense.Id, ct);
    }

    private async Task DeleteExpense(long userId, Guid expenseId, CancellationToken ct)
    {
        var expense = await db.Expenses.FirstOrDefaultAsync(x => x.Id == expenseId && x.AuthorId == userId, ct);
        if (expense is null) { await Send(userId, "Удалять покупку может только ее автор.", ct); return; }
        if (await MemberGroup(userId, expense.GroupId, ct) is null) { await ShowGroups(userId, ct); return; }
        db.Expenses.Remove(expense); await CancelPending(expense.GroupId, ct); await db.SaveChangesAsync(ct);
        await ShowExpenses(userId, expense.GroupId, ct);
    }

    private async Task ShowBalance(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await MemberGroup(userId, groupId, ct);
        if (group is null) return;
        var balances = await balanceService.GetBalances(groupId, ct);
        var users = await Participants(groupId, ct); var names = users.ToDictionary(x => x.ParticipantId, x => x.DisplayName);
        var lines = balances.OrderByDescending(x => x.Value).Select(x => $"{names.GetValueOrDefault(x.Key, x.Key.ToString())}: {(x.Value >= 0 ? "+" : "")}{Money(x.Value)}");
        var suggestions = BalanceService.Minimize(balances);
        var text = "Баланс:\n" + string.Join("\n", lines);
        if (suggestions.Count == 0) text += "\n\nВсе расчеты закрыты.";
        else text += "\n\nРекомендуемые переводы:\n" + string.Join("\n", suggestions.Select(x =>
        {
            var details = users.FirstOrDefault(u => u.ParticipantId == x.ToUserId)?.PaymentDetails;
            return $"{names.GetValueOrDefault(x.FromUserId, x.FromUserId.ToString())} → {names.GetValueOrDefault(x.ToUserId, x.ToUserId.ToString())}: {Money(x.AmountKopecks)}" +
                   ((group.Type == GroupType.Standalone || x.FromUserId == userId) && !string.IsNullOrWhiteSpace(details) ? $"\nРеквизиты: {details}" : "");
        }));
        var rows = group.Type == GroupType.Collective
            ? suggestions.Where(x => x.FromUserId == userId && names.ContainsKey(x.ToUserId)).Select(x => new[]
            {
                InlineKeyboardButton.WithCallbackData($"💸 Я перевел {names[x.ToUserId]} {Money(x.AmountKopecks)}", $"pay:{groupId:N}:{x.ToUserId}")
            }).ToList()
            : [];
        rows.Add([InlineKeyboardButton.WithCallbackData("◀️ Назад", $"group:{groupId:N}")]);
        await SendLong(userId, text, ct, new InlineKeyboardMarkup(rows));
    }

    private async Task MarkPaid(long userId, string payload, CancellationToken ct)
    {
        var split = payload.Split(':'); var groupId = ParseGuid(split[0]); var toUserId = long.Parse(split[1], CultureInfo.InvariantCulture);
        var group = await MemberGroup(userId, groupId, ct);
        if (group is null || group.Type != GroupType.Collective) { await ShowGroup(userId, groupId, ct); return; }
        var suggestions = BalanceService.Minimize(await balanceService.GetBalances(groupId, ct));
        var suggestion = suggestions.FirstOrDefault(x => x.FromUserId == userId && x.ToUserId == toUserId);
        if (suggestion is null) { await Send(userId, "Баланс изменился. Откройте расчет заново.", ct); await ShowGroup(userId, groupId, ct); return; }
        if (await db.Transfers.AnyAsync(x => x.GroupId == groupId && x.FromUserId == userId && x.ToUserId == toUserId && x.Status == TransferStatus.Pending, ct))
        { await Send(userId, "Этот перевод уже ожидает подтверждения.", ct); return; }
        var transfer = new Transfer { GroupId = groupId, FromUserId = userId, ToUserId = toUserId, AmountKopecks = suggestion.AmountKopecks };
        db.Transfers.Add(transfer); await db.SaveChangesAsync(ct);
        var sender = await db.Users.FindAsync([userId], ct);
        await Send(toUserId, $"{sender!.DisplayName} отметил перевод {Money(transfer.AmountKopecks)}. Подтвердите получение:", ct,
            new InlineKeyboardMarkup([[InlineKeyboardButton.WithCallbackData("✅ Получено", $"transfer_confirm:{transfer.Id:N}"), InlineKeyboardButton.WithCallbackData("❌ Не получено", $"transfer_reject:{transfer.Id:N}")]]));
        await Send(userId, "Ожидаем подтверждения получателя.", ct);
    }

    private async Task ResolveTransfer(long userId, Guid transferId, bool confirmed, CancellationToken ct)
    {
        var transfer = await db.Transfers.FirstOrDefaultAsync(x => x.Id == transferId && x.ToUserId == userId &&
            x.Status == TransferStatus.Pending && db.Groups.Any(g => g.Id == x.GroupId && !g.IsArchived), ct);
        if (transfer is null) { await ShowMain(userId, "Перевод уже обработан или не найден. Выберите действие.", ct); return; }
        transfer.Status = confirmed ? TransferStatus.Confirmed : TransferStatus.Rejected; transfer.ResolvedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await Send(userId, confirmed ? "Получение подтверждено." : "Перевод отклонен.", ct);
        await Send(transfer.FromUserId, confirmed ? "Получатель подтвердил перевод." : "Получатель не подтвердил перевод.", ct);
    }

    private async Task LeaveGroup(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await MemberGroup(userId, groupId, ct); if (group is null) return;
        if (group.Type != GroupType.Collective) { await ShowGroup(userId, groupId, ct); return; }
        if (group.OwnerId == userId) { await Send(userId, "Владелец не может выйти из активной группы.", ct); return; }
        var balance = await balanceService.GetBalances(groupId, ct);
        if (balance.GetValueOrDefault(userId) != 0) { await Send(userId, "Сначала закройте свой баланс.", ct); return; }
        (await db.GroupMembers.FindAsync([groupId, userId], ct))!.IsActive = false;
        var participant = await db.GroupParticipants.FindAsync([groupId, userId], ct);
        if (participant is not null) participant.IsActive = false;
        await db.SaveChangesAsync(ct); await ShowGroups(userId, ct);
    }

    private async Task UpsertUser(global::Telegram.Bot.Types.User telegramUser, CancellationToken ct)
    {
        var user = await db.Users.FindAsync([telegramUser.Id], ct);
        var name = string.Join(' ', new[] { telegramUser.FirstName, telegramUser.LastName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        if (user is null) db.Users.Add(new AppUser { TelegramId = telegramUser.Id, DisplayName = name, Username = telegramUser.Username });
        else { user.DisplayName = name; user.Username = telegramUser.Username; }
        await db.SaveChangesAsync(ct);
    }

    private async Task<ExpenseGroup?> MemberGroup(long userId, Guid groupId, CancellationToken ct) =>
        await db.GroupMembers
            .Where(x => x.GroupId == groupId && x.UserId == userId && x.IsActive && !x.Group.IsArchived &&
                        (x.Group.Type == GroupType.Collective || x.Group.OwnerId == userId))
            .Select(x => x.Group).FirstOrDefaultAsync(ct);
    private async Task<bool> IsParticipant(long participantId, Guid groupId, CancellationToken ct) =>
        await db.GroupParticipants.AnyAsync(x => x.ParticipantId == participantId && x.GroupId == groupId && x.IsActive, ct);
    private async Task<List<ParticipantInfo>> Participants(Guid groupId, CancellationToken ct)
    {
        var participants = await db.GroupParticipants.Where(x => x.GroupId == groupId && x.IsActive)
            .Include(x => x.TelegramUser).ToListAsync(ct);
        return participants.Select(x => new ParticipantInfo(
                x.ParticipantId,
                x.TelegramUser?.DisplayName ?? x.DisplayName ?? "Без имени",
                x.TelegramUser?.PaymentDetails ?? x.PaymentDetails,
                x.TelegramUserId))
            .OrderBy(x => x.DisplayName).ToList();
    }
    private async Task<ParticipantInfo?> Participant(Guid groupId, long participantId, CancellationToken ct) =>
        (await Participants(groupId, ct)).FirstOrDefault(x => x.ParticipantId == participantId);
    private async Task<bool> HasParticipantPurchases(Guid groupId, long participantId, CancellationToken ct) =>
        await db.Expenses.AnyAsync(x => x.GroupId == groupId &&
            (x.PayerId == participantId || x.Shares.Any(share => share.UserId == participantId)), ct);
    private async Task CancelPending(Guid groupId, CancellationToken ct) => await db.Transfers.Where(x => x.GroupId == groupId && x.Status == TransferStatus.Pending).ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, TransferStatus.Cancelled), ct);
    private async Task LockGroup(Guid groupId, CancellationToken ct) => await db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT 1 FROM \"Groups\" WHERE \"Id\" = {groupId} FOR UPDATE", ct);

    private async Task<(FlowData Data, GroupParticipant Participant)?> RequiredManagedParticipant(
        long userId, CancellationToken ct, params string[] states)
    {
        var session = await db.Sessions.FindAsync([userId], ct);
        if (session is not null && states.Contains(session.State))
        {
            var data = Deserialize(session.DataJson);
            var group = await MemberGroup(userId, data.GroupId, ct);
            var participant = await db.GroupParticipants.FindAsync([data.GroupId, data.ManagedParticipantId], ct);
            if (group?.Type == GroupType.Standalone && group.OwnerId == userId &&
                participant is { IsActive: true, TelegramUserId: null })
                return (data, participant);
        }
        await ClearSession(userId, ct);
        await ShowGroups(userId, ct);
        return null;
    }

    private async Task<UserSession?> RequiredSession(long userId, string state, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([userId], ct);
        if (session?.State == state)
        {
            var data = Deserialize(session.DataJson);
            if (data.GroupId == Guid.Empty || await MemberGroup(userId, data.GroupId, ct) is not null) return session;
        }
        await ClearSession(userId, ct);
        await ShowMain(userId, "Действие устарело. Выберите действие в актуальном меню.", ct);
        return null;
    }
    private async Task SetSession(long userId, string state, FlowData data, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([userId], ct);
        if (session is null) db.Sessions.Add(new UserSession { UserId = userId, State = state, DataJson = JsonSerializer.Serialize(data, JsonOptions) });
        else { session.State = state; session.DataJson = JsonSerializer.Serialize(data, JsonOptions); session.UpdatedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(ct);
    }
    private async Task ClearSession(long userId, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([userId], ct); if (session is null) return;
        db.Sessions.Remove(session); await db.SaveChangesAsync(ct);
    }
    private async Task Send(long chatId, string text, CancellationToken ct, InlineKeyboardMarkup? markup = null) =>
        await bot.SendMessage(chatId, text, replyMarkup: markup, cancellationToken: ct);

    private async Task SendLong(long chatId, string text, CancellationToken ct, InlineKeyboardMarkup? markup = null)
    {
        const int maxLength = 3800;
        var chunks = new List<string>();
        var current = "";
        foreach (var line in text.Split('\n'))
        {
            if (current.Length > 0 && current.Length + line.Length + 1 > maxLength)
            {
                chunks.Add(current);
                current = "";
            }
            if (line.Length > maxLength)
            {
                if (current.Length > 0) { chunks.Add(current); current = ""; }
                for (var offset = 0; offset < line.Length; offset += maxLength)
                    chunks.Add(line.Substring(offset, Math.Min(maxLength, line.Length - offset)));
                continue;
            }
            current += (current.Length == 0 ? "" : "\n") + line;
        }
        if (current.Length > 0) chunks.Add(current);
        if (chunks.Count == 0) chunks.Add(" ");
        for (var i = 0; i < chunks.Count; i++)
            await Send(chatId, chunks[i], ct, i == chunks.Count - 1 ? markup : null);
    }

    private static FlowData Deserialize(string json) => JsonSerializer.Deserialize<FlowData>(json, JsonOptions) ?? new();
    private static Guid ParseGuid(string value) => Guid.ParseExact(value, "N");
    private static string EncodeParticipantId(long value) => unchecked((ulong)value).ToString("x16", CultureInfo.InvariantCulture);
    private static string Short(string value) => value.Length <= 20 ? value : value[..17] + "...";
    private static string Money(long kopecks) => (kopecks / 100m).ToString("N2", CultureInfo.GetCultureInfo("ru-RU")) + " ₽";
    private static bool TryMoney(string text, out long kopecks)
    {
        var normalized = text.Replace(" ", "").Replace(',', '.');
        if (decimal.TryParse(normalized, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && amount > 0 && decimal.Round(amount, 2) == amount && amount <= 10_000_000_000m)
        { kopecks = decimal.ToInt64(amount * 100); return true; }
        kopecks = 0; return false;
    }

    private sealed class FlowData
    {
        public Guid GroupId { get; set; }
        public GroupType GroupType { get; set; }
        public Guid? ExpenseId { get; set; }
        public string ManagedName { get; set; } = "";
        public long ManagedParticipantId { get; set; }
        public string Description { get; set; } = "";
        public long AmountKopecks { get; set; }
        public long PayerId { get; set; }
        public List<long> ParticipantIds { get; set; } = [];
        public Dictionary<long, long> Shares { get; set; } = [];
        public int ManualIndex { get; set; }
    }

    private sealed record ParticipantInfo(long ParticipantId, string DisplayName, string? PaymentDetails, long? TelegramUserId);
}
