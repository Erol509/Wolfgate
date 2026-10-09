using Content.Shared._WF.Encounters;

namespace Content.Server._WF.Encounters;

/// <summary>Raised on the encounter entity, and broadcast, once its ships are crewed and have their orders.</summary>
[ByRefEvent]
public readonly record struct WFEncounterStartedEvent(EntityUid Encounter);

/// <summary>Raised on the encounter entity, and broadcast, when it resolves. Its ships may still exist.</summary>
[ByRefEvent]
public readonly record struct WFEncounterResolvedEvent(EntityUid Encounter, WFEncounterResolution Resolution);
