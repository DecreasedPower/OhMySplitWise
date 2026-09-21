namespace SplitMoneyTg.Api;

public sealed record MutationRequirements(bool RequiresEntityVersion = false, bool RequiresGroupRevision = false);
