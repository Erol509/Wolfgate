using Robust.Shared.Serialization;

namespace Content.Shared._WF.Performance;

/// <summary>Every radar blip that went away during one server tick, in place of one event per blip.</summary>
[Serializable, NetSerializable]
public sealed class WFBlipRemovalBatchEvent(List<NetEntity> blips) : EntityEventArgs
{
    public readonly List<NetEntity> Blips = blips;
}
