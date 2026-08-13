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
            await HandleCallback(callback, ct);
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
        if (data == "newgroup") { await SetSession(chatId, "group_name", new(), ct); await Send(chatId, "Введите название группы:", ct); return; }
        if (data == "details") { await SetSession(chatId, "payment_details", new(), ct); await Send(chatId, "Введите реквизиты свободным текстом (до 500 символов):", ct); return; }
        if (data.StartsWith("group:")) { await ShowGroup(chatId, ParseGuid(data[6..]), ct); return; }
        if (data.StartsWith("invite:")) { await Invite(chatId, ParseGuid(data[7..]), ct); return; }
        if (data.StartsWith("balance:")) { await ShowBalance(chatId, ParseGuid(data[8..]), ct); return; }
        if (data.StartsWith("expenses:")) { await ShowExpenses(chatId, ParseGuid(data[9..]), ct); return; }
        if (data.StartsWith("expense_new:")) { await StartExpense(chatId, ParseGuid(data[12..]), null, ct); return; }
        if (data.StartsWith("expense_delete:")) { await DeleteExpense(chatId, ParseGuid(data[15..]), ct); return; }
        if (data.StartsWith("expense_edit:")) { await EditExpense(chatId, ParseGuid(data[13..]), ct); return; }
        if (data.StartsWith("payer:")) { await ChoosePayer(chatId, long.Parse(data[6..], CultureInfo.InvariantCulture), ct); return; }
        if (data.StartsWith("participant:")) { await ToggleParticipant(chatId, long.Parse(data[12..], CultureInfo.InvariantCulture), ct); return; }
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
        switch (session.State)
        {
            case "group_name":
                if (text.Length is < 1 or > 100) { await Send(userId, "Название должно содержать от 1 до 100 символов.", ct); return; }
                var group = new ExpenseGroup { Name = text, OwnerId = userId };
                group.Members.Add(new GroupMember { UserId = userId });
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
            [InlineKeyboardButton.WithCallbackData("Мои группы", "groups"), InlineKeyboardButton.WithCallbackData("Создать группу", "newgroup")],
            [InlineKeyboardButton.WithCallbackData("Реквизиты", "details")]
        ]));

    private async Task ShowGroups(long userId, CancellationToken ct)
    {
        var groups = await db.GroupMembers.Where(x => x.UserId == userId && x.IsActive && !x.Group.IsArchived)
            .Select(x => x.Group).OrderBy(x => x.Name).ToListAsync(ct);
        var rows = groups.Select(x => new[] { InlineKeyboardButton.WithCallbackData(x.Name, $"group:{x.Id:N}") }).ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData("Создать группу", "newgroup")]);
        rows.Add([InlineKeyboardButton.WithCallbackData("Назад", "main")]);
        await Send(userId, groups.Count == 0 ? "У вас пока нет групп." : "Ваши группы:", ct, new InlineKeyboardMarkup(rows));
    }

    private async Task ShowGroup(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await MemberGroup(userId, groupId, ct);
        if (group is null) { await ShowGroups(userId, ct); return; }
        await Send(userId, $"Группа: {group.Name}", ct, new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("Добавить покупку", $"expense_new:{group.Id:N}")],
            [InlineKeyboardButton.WithCallbackData("Покупки", $"expenses:{group.Id:N}"), InlineKeyboardButton.WithCallbackData("Баланс и долги", $"balance:{group.Id:N}")],
            [InlineKeyboardButton.WithCallbackData("Пригласить", $"invite:{group.Id:N}"), InlineKeyboardButton.WithCallbackData("Выйти", $"leave:{group.Id:N}")],
            [InlineKeyboardButton.WithCallbackData("К группам", "groups")]
        ]));
    }

    private async Task Invite(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await MemberGroup(userId, groupId, ct);
        if (group is null) return;
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
        var member = await db.GroupMembers.FindAsync([invite.GroupId, userId], ct);
        if (member is null) db.GroupMembers.Add(new GroupMember { GroupId = invite.GroupId, UserId = userId });
        else member.IsActive = true;
        await db.SaveChangesAsync(ct);
        await ShowGroup(userId, invite.GroupId, ct);
    }

    private async Task StartExpense(long userId, Guid groupId, Guid? expenseId, CancellationToken ct)
    {
        if (await MemberGroup(userId, groupId, ct) is null) return;
        await SetSession(userId, "expense_description", new FlowData { GroupId = groupId, ExpenseId = expenseId }, ct);
        await Send(userId, "Введите название покупки:", ct);
    }

    private async Task ShowPayers(long userId, Guid groupId, CancellationToken ct)
    {
        var members = await Members(groupId, ct);
        var rows = members.Select(x => new[] { InlineKeyboardButton.WithCallbackData(x.DisplayName, $"payer:{x.TelegramId}") });
        await Send(userId, "Кто оплатил покупку?", ct, new InlineKeyboardMarkup(rows));
    }

    private async Task ChoosePayer(long userId, long payerId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "expense_payer", ct); if (session is null) return;
        var data = Deserialize(session.DataJson);
        if (!await IsMember(payerId, data.GroupId, ct)) return;
        data.PayerId = payerId;
        await SetSession(userId, "expense_participants", data, ct);
        await ShowParticipantPicker(userId, data, ct);
    }

    private async Task ToggleParticipant(long userId, long participantId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "expense_participants", ct); if (session is null) return;
        var data = Deserialize(session.DataJson);
        if (!await IsMember(participantId, data.GroupId, ct)) return;
        if (!data.ParticipantIds.Remove(participantId)) data.ParticipantIds.Add(participantId);
        await SetSession(userId, "expense_participants", data, ct);
        await ShowParticipantPicker(userId, data, ct);
    }

    private async Task ShowParticipantPicker(long userId, FlowData data, CancellationToken ct)
    {
        var members = await Members(data.GroupId, ct);
        var rows = members.Select(x => new[] { InlineKeyboardButton.WithCallbackData(
            $"{(data.ParticipantIds.Contains(x.TelegramId) ? "✓" : "○")} {x.DisplayName}", $"participant:{x.TelegramId}") }).ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData("Готово", "participants_done")]);
        await Send(userId, "Выберите участников покупки:", ct, new InlineKeyboardMarkup(rows));
    }

    private async Task ParticipantsDone(long userId, CancellationToken ct)
    {
        var session = await RequiredSession(userId, "expense_participants", ct); if (session is null) return;
        var data = Deserialize(session.DataJson);
        if (data.ParticipantIds.Count == 0) { await Send(userId, "Выберите хотя бы одного участника.", ct); return; }
        await Send(userId, "Как разделить покупку?", ct, new InlineKeyboardMarkup([
            [InlineKeyboardButton.WithCallbackData("Поровну", "split_equal"), InlineKeyboardButton.WithCallbackData("Вручную", "split_manual")]
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
        var member = await db.Users.FindAsync([data.ParticipantIds[data.ManualIndex]], ct);
        await Send(userId, $"Доля для {member!.DisplayName} в рублях:", ct);
    }

    private async Task SaveExpense(long userId, FlowData data, CancellationToken ct)
    {
        var activeMemberIds = await db.GroupMembers
            .Where(x => x.GroupId == data.GroupId && x.IsActive)
            .Select(x => x.UserId)
            .ToListAsync(ct);
        if (!activeMemberIds.Contains(data.PayerId) || data.Shares.Keys.Any(x => !activeMemberIds.Contains(x)))
        {
            await ClearSession(userId, ct);
            await Send(userId, "Состав группы изменился или данные покупки устарели. Создайте покупку заново.", ct);
            await ShowGroup(userId, data.GroupId, ct);
            return;
        }
        if (data.AmountKopecks <= 0 || data.Shares.Count == 0 || data.Shares.Values.Any(x => x <= 0) || data.Shares.Values.Sum() != data.AmountKopecks)
        {
            await ClearSession(userId, ct);
            await Send(userId, "Доли покупки указаны некорректно. Создайте покупку заново.", ct);
            await ShowGroup(userId, data.GroupId, ct);
            return;
        }

        Expense expense;
        if (data.ExpenseId is { } id)
        {
            expense = await db.Expenses.Include(x => x.Shares).FirstAsync(x => x.Id == id && x.AuthorId == userId, ct);
            db.ExpenseShares.RemoveRange(expense.Shares);
        }
        else
        {
            expense = new Expense { GroupId = data.GroupId, AuthorId = userId };
            db.Expenses.Add(expense);
        }
        expense.Description = data.Description;
        expense.AmountKopecks = data.AmountKopecks;
        expense.PayerId = data.PayerId;
        expense.Shares = data.Shares.Select(x => new ExpenseShare { UserId = x.Key, AmountKopecks = x.Value }).ToList();
        await CancelPending(data.GroupId, ct);
        await db.SaveChangesAsync(ct);
        await ClearSession(userId, ct);
        await ShowGroup(userId, data.GroupId, ct);
    }

    private async Task ShowExpenses(long userId, Guid groupId, CancellationToken ct)
    {
        if (await MemberGroup(userId, groupId, ct) is null) return;
        var expenses = await db.Expenses.Where(x => x.GroupId == groupId).OrderByDescending(x => x.CreatedAt).Take(20).ToListAsync(ct);
        var users = await Members(groupId, ct); var names = users.ToDictionary(x => x.TelegramId, x => x.DisplayName);
        var text = expenses.Count == 0 ? "Покупок пока нет." : string.Join("\n", expenses.Select(x => $"• {x.Description}: {Money(x.AmountKopecks)}, оплатил {names.GetValueOrDefault(x.PayerId, x.PayerId.ToString())}"));
        var rows = expenses.Where(x => x.AuthorId == userId).Select(x => new[]
        {
            InlineKeyboardButton.WithCallbackData($"Изменить: {Short(x.Description)}", $"expense_edit:{x.Id:N}"),
            InlineKeyboardButton.WithCallbackData("Удалить", $"expense_delete:{x.Id:N}")
        }).ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData("Назад", $"group:{groupId:N}")]);
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
        db.Expenses.Remove(expense); await CancelPending(expense.GroupId, ct); await db.SaveChangesAsync(ct);
        await ShowExpenses(userId, expense.GroupId, ct);
    }

    private async Task ShowBalance(long userId, Guid groupId, CancellationToken ct)
    {
        if (await MemberGroup(userId, groupId, ct) is null) return;
        var balances = await balanceService.GetBalances(groupId, ct);
        var users = await Members(groupId, ct); var names = users.ToDictionary(x => x.TelegramId, x => x.DisplayName);
        var lines = balances.OrderByDescending(x => x.Value).Select(x => $"{names.GetValueOrDefault(x.Key, x.Key.ToString())}: {(x.Value >= 0 ? "+" : "")}{Money(x.Value)}");
        var suggestions = BalanceService.Minimize(balances);
        var text = "Баланс:\n" + string.Join("\n", lines);
        if (suggestions.Count == 0) text += "\n\nВсе расчеты закрыты.";
        else text += "\n\nРекомендуемые переводы:\n" + string.Join("\n", suggestions.Select(x =>
        {
            var details = users.FirstOrDefault(u => u.TelegramId == x.ToUserId)?.PaymentDetails;
            return $"{names.GetValueOrDefault(x.FromUserId, x.FromUserId.ToString())} → {names.GetValueOrDefault(x.ToUserId, x.ToUserId.ToString())}: {Money(x.AmountKopecks)}" +
                   (x.FromUserId == userId && !string.IsNullOrWhiteSpace(details) ? $"\nРеквизиты: {details}" : "");
        }));
        var rows = suggestions.Where(x => x.FromUserId == userId && names.ContainsKey(x.ToUserId)).Select(x => new[]
        {
            InlineKeyboardButton.WithCallbackData($"Я перевел {names[x.ToUserId]} {Money(x.AmountKopecks)}", $"pay:{groupId:N}:{x.ToUserId}")
        }).ToList();
        rows.Add([InlineKeyboardButton.WithCallbackData("Назад", $"group:{groupId:N}")]);
        await Send(userId, text, ct, new InlineKeyboardMarkup(rows));
    }

    private async Task MarkPaid(long userId, string payload, CancellationToken ct)
    {
        var split = payload.Split(':'); var groupId = ParseGuid(split[0]); var toUserId = long.Parse(split[1], CultureInfo.InvariantCulture);
        if (await MemberGroup(userId, groupId, ct) is null) return;
        var suggestions = BalanceService.Minimize(await balanceService.GetBalances(groupId, ct));
        var suggestion = suggestions.FirstOrDefault(x => x.FromUserId == userId && x.ToUserId == toUserId);
        if (suggestion is null) { await Send(userId, "Баланс изменился. Откройте расчет заново.", ct); await ShowGroup(userId, groupId, ct); return; }
        if (await db.Transfers.AnyAsync(x => x.GroupId == groupId && x.FromUserId == userId && x.ToUserId == toUserId && x.Status == TransferStatus.Pending, ct))
        { await Send(userId, "Этот перевод уже ожидает подтверждения.", ct); return; }
        var transfer = new Transfer { GroupId = groupId, FromUserId = userId, ToUserId = toUserId, AmountKopecks = suggestion.AmountKopecks };
        db.Transfers.Add(transfer); await db.SaveChangesAsync(ct);
        var sender = await db.Users.FindAsync([userId], ct);
        await Send(toUserId, $"{sender!.DisplayName} отметил перевод {Money(transfer.AmountKopecks)}. Подтвердите получение:", ct,
            new InlineKeyboardMarkup([[InlineKeyboardButton.WithCallbackData("Получено", $"transfer_confirm:{transfer.Id:N}"), InlineKeyboardButton.WithCallbackData("Не получено", $"transfer_reject:{transfer.Id:N}")]]));
        await Send(userId, "Ожидаем подтверждения получателя.", ct);
    }

    private async Task ResolveTransfer(long userId, Guid transferId, bool confirmed, CancellationToken ct)
    {
        var transfer = await db.Transfers.FirstOrDefaultAsync(x => x.Id == transferId && x.ToUserId == userId && x.Status == TransferStatus.Pending, ct);
        if (transfer is null) { await ShowMain(userId, "Перевод уже обработан или не найден. Выберите действие.", ct); return; }
        transfer.Status = confirmed ? TransferStatus.Confirmed : TransferStatus.Rejected; transfer.ResolvedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        await Send(userId, confirmed ? "Получение подтверждено." : "Перевод отклонен.", ct);
        await Send(transfer.FromUserId, confirmed ? "Получатель подтвердил перевод." : "Получатель не подтвердил перевод.", ct);
    }

    private async Task LeaveGroup(long userId, Guid groupId, CancellationToken ct)
    {
        var group = await MemberGroup(userId, groupId, ct); if (group is null) return;
        if (group.OwnerId == userId) { await Send(userId, "Владелец не может выйти из активной группы.", ct); return; }
        var balance = await balanceService.GetBalances(groupId, ct);
        if (balance.GetValueOrDefault(userId) != 0) { await Send(userId, "Сначала закройте свой баланс.", ct); return; }
        (await db.GroupMembers.FindAsync([groupId, userId], ct))!.IsActive = false;
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
        await db.GroupMembers.Where(x => x.GroupId == groupId && x.UserId == userId && x.IsActive).Select(x => x.Group).FirstOrDefaultAsync(ct);
    private async Task<bool> IsMember(long userId, Guid groupId, CancellationToken ct) => await db.GroupMembers.AnyAsync(x => x.UserId == userId && x.GroupId == groupId && x.IsActive, ct);
    private async Task<List<AppUser>> Members(Guid groupId, CancellationToken ct) => await db.GroupMembers.Where(x => x.GroupId == groupId && x.IsActive).Select(x => x.User).OrderBy(x => x.DisplayName).ToListAsync(ct);
    private async Task CancelPending(Guid groupId, CancellationToken ct) => await db.Transfers.Where(x => x.GroupId == groupId && x.Status == TransferStatus.Pending).ExecuteUpdateAsync(x => x.SetProperty(t => t.Status, TransferStatus.Cancelled), ct);

    private async Task<UserSession?> RequiredSession(long userId, string state, CancellationToken ct)
    {
        var session = await db.Sessions.FindAsync([userId], ct);
        if (session?.State == state) return session;
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

    private static FlowData Deserialize(string json) => JsonSerializer.Deserialize<FlowData>(json, JsonOptions) ?? new();
    private static Guid ParseGuid(string value) => Guid.ParseExact(value, "N");
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
        public Guid? ExpenseId { get; set; }
        public string Description { get; set; } = "";
        public long AmountKopecks { get; set; }
        public long PayerId { get; set; }
        public List<long> ParticipantIds { get; set; } = [];
        public Dictionary<long, long> Shares { get; set; } = [];
        public int ManualIndex { get; set; }
    }
}
