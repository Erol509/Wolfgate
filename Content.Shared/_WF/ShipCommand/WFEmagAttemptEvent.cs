namespace Content.Shared._WF.ShipCommand;

/// <summary>Raised broadcast before an emag takes effect on <paramref name="Target"/>; cancelling it refuses the emag.</summary>
[ByRefEvent]
public record struct WFEmagAttemptEvent(EntityUid User, EntityUid Target, bool Cancelled = false);
