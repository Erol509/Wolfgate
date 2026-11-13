namespace Content.Shared._WF.ShipCommand;

/// <summary>Raised on a stack about to give <paramref name="Moved"/> of its <paramref name="Count"/> to <paramref name="Recipient"/>.</summary>
[ByRefEvent]
public readonly record struct WFStackMergeEvent(EntityUid Recipient, int Moved, int Count);
