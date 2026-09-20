namespace SplitMoneyTg.Application;

public sealed class PostCommitActions
{
    private readonly List<Func<CancellationToken, Task>> actions = [];

    public void Add(Func<CancellationToken, Task> action) => actions.Add(action);

    public async Task Run(CancellationToken ct)
    {
        foreach (var action in actions) await action(ct);
        actions.Clear();
    }
}
