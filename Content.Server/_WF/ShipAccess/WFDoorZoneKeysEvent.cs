using Content.Shared.StationRecords;

namespace Content.Server._WF.ShipAccess;

/// <summary>
/// Raised on a ship's grid while the reader of a door with the Zone rule is written: add the record keys of whoever the
/// door's zone admits now. The owner keys are in already; with nobody answering, the door admits only the owner.
/// </summary>
[ByRefEvent]
public readonly record struct WFDoorZoneKeysEvent(EntityUid Door, string? Zone, HashSet<StationRecordKey> Keys);
