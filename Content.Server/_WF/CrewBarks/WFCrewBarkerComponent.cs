namespace Content.Server._WF.CrewBarks;

/// <summary>A roster crewman or trader aboard a ship with <see cref="WFCrewBarksComponent"/>: his voice and his limits.</summary>
[RegisterComponent, Access(typeof(WFCrewBarkSystem))]
public sealed partial class WFCrewBarkerComponent : Component
{
    /// <summary>The ship he serves.</summary>
    [ViewVariables]
    public EntityUid Grid;

    /// <summary>His own lines are <c>wf-crew-bark-voice-kind-n</c>, said along with the common ones of each kind.</summary>
    [ViewVariables]
    public string Voice = WFCrewBarkSystem.Common;

    /// <summary>When he may next say any line.</summary>
    [ViewVariables]
    public TimeSpan NextLine;

    /// <summary>When he may next thank the Boss for a blow.</summary>
    [ViewVariables]
    public TimeSpan NextStruck;

    /// <summary>When he next has an idle thought.</summary>
    [ViewVariables]
    public TimeSpan NextIdle;

    /// <summary>When he last greeted, or passed over, each person.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> Greeted = new();

    /// <summary>Came aboard at the crew bay and is walking to his post, where he reports.</summary>
    [ViewVariables]
    public bool Arriving;

    /// <summary>The Fluent id of his last line.</summary>
    [ViewVariables]
    public string? LastLine;

    /// <summary>How many lines of each kind he has said.</summary>
    [ViewVariables]
    public Dictionary<WFCrewBark, int> Said = new();
}
