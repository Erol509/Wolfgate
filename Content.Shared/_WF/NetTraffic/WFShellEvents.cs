using System.Numerics;
using Robust.Shared.Serialization;

namespace Content.Shared._WF.NetTraffic;

/// <summary>A visibility layer no eye has: an entity on it is never sent to clients.</summary>
public static class WFNetTraffic
{
    public const ushort NeverSentLayer = 1 << 15;
}

/// <summary>
/// One tick's ship shells for one player. A shell flies straight, so the client draws its own copy from where and how
/// fast it was fired instead of being sent the projectile entity and its position every tick.
/// </summary>
[Serializable, NetSerializable]
public sealed class WFShellBatchEvent : EntityEventArgs
{
    public List<WFShellFired> Fired = new();
    public List<WFShellMoved> Moved = new();
    public List<WFShellEnded> Ended = new();
}

/// <summary>A shell fired at a server tick: its sprite's prototype, map, position, velocity, rotation and life left.</summary>
[Serializable, NetSerializable]
public readonly record struct WFShellFired(int Id, string Prototype, NetEntity Map, Vector2 Position, Vector2 Velocity,
    float Rotation, uint Tick, float Lifetime);

/// <summary>A shell whose course changed, as of a server tick.</summary>
[Serializable, NetSerializable]
public readonly record struct WFShellMoved(int Id, Vector2 Position, Vector2 Velocity, float Rotation, uint Tick);

/// <summary>A shell gone, last seen here.</summary>
[Serializable, NetSerializable]
public readonly record struct WFShellEnded(int Id, Vector2 Position);
