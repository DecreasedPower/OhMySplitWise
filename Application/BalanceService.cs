using Microsoft.EntityFrameworkCore;
using SplitMoneyTg.Domain;
using SplitMoneyTg.Infrastructure;

namespace SplitMoneyTg.Application;

public sealed record SuggestedTransfer(long FromUserId, long ToUserId, long AmountKopecks);

public sealed class BalanceService(AppDbContext db)
{
    public async Task<Dictionary<long, long>> GetBalances(Guid groupId, CancellationToken ct = default)
    {
        var memberIds = await db.GroupParticipants.Where(x => x.GroupId == groupId && x.IsActive)
            .Select(x => x.ParticipantId).ToListAsync(ct);
        var balances = memberIds.ToDictionary(x => x, _ => 0L);

        var expenses = await db.Expenses.Where(x => x.GroupId == groupId).Include(x => x.Shares).ToListAsync(ct);
        foreach (var expense in expenses)
        {
            balances.TryAdd(expense.PayerId, 0);
            balances[expense.PayerId] += expense.AmountKopecks;
            foreach (var share in expense.Shares)
            {
                balances.TryAdd(share.UserId, 0);
                balances[share.UserId] -= share.AmountKopecks;
            }
        }

        var transfers = await db.Transfers
            .Where(x => x.GroupId == groupId && x.Status == TransferStatus.Confirmed).ToListAsync(ct);
        foreach (var transfer in transfers)
        {
            balances.TryAdd(transfer.FromUserId, 0);
            balances.TryAdd(transfer.ToUserId, 0);
            balances[transfer.FromUserId] += transfer.AmountKopecks;
            balances[transfer.ToUserId] -= transfer.AmountKopecks;
        }

        return balances;
    }

    public static IReadOnlyList<SuggestedTransfer> Minimize(IReadOnlyDictionary<long, long> balances)
    {
        var debtors = new PriorityQueue<(long UserId, long Amount), long>();
        var creditors = new PriorityQueue<(long UserId, long Amount), long>();
        foreach (var (userId, amount) in balances)
        {
            if (amount < 0) debtors.Enqueue((userId, -amount), amount);
            if (amount > 0) creditors.Enqueue((userId, amount), -amount);
        }

        var result = new List<SuggestedTransfer>();
        while (debtors.TryDequeue(out var debtor, out _) && creditors.TryDequeue(out var creditor, out _))
        {
            var amount = Math.Min(debtor.Amount, creditor.Amount);
            result.Add(new(debtor.UserId, creditor.UserId, amount));
            debtor.Amount -= amount;
            creditor.Amount -= amount;
            if (debtor.Amount > 0) debtors.Enqueue(debtor, -debtor.Amount);
            if (creditor.Amount > 0) creditors.Enqueue(creditor, -creditor.Amount);
        }
        return result;
    }

    public static IReadOnlyDictionary<long, long> SplitEqually(long amount, IReadOnlyCollection<long> participantIds, long payerId)
    {
        if (amount <= 0 || participantIds.Count == 0) throw new ArgumentOutOfRangeException();
        var orderedIds = participantIds.Order().ToArray();
        if (orderedIds.Distinct().Count() != orderedIds.Length)
            throw new ArgumentException("Participant IDs must be unique.", nameof(participantIds));

        var result = orderedIds.ToDictionary(x => x, _ => amount / orderedIds.Length);
        var startIndex = Array.IndexOf(orderedIds, payerId);
        if (startIndex < 0) startIndex = 0;
        for (var offset = 0; offset < amount % orderedIds.Length; offset++)
            result[orderedIds[(startIndex + offset) % orderedIds.Length]]++;
        return result;
    }

    public static void SetShare(IDictionary<long, long> shares, IReadOnlyList<long> participantIds, int index, long amount)
    {
        if (index < 0 || index >= participantIds.Count) throw new ArgumentOutOfRangeException(nameof(index));
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount));
        shares[participantIds[index]] = amount;
    }
}
