using Content.Shared._WF.Traders;
using Content.Shared.Mobs.Systems;
using Content.Shared.StatusIcon;
using Content.Shared.StatusIcon.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WF.Traders;

/// <summary>
/// Puts a question mark over every living trader, where the SSD indicator would be, so players can tell them
/// from anyone else standing about.
/// </summary>
public sealed class TraderIconSystem : EntitySystem
{
    private static readonly ProtoId<SsdIconPrototype> Icon = "WFTraderIcon";

    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private MobStateSystem _mobState = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<TraderComponent, GetStatusIconsEvent>(OnGetStatusIcons);
    }

    private void OnGetStatusIcons(Entity<TraderComponent> ent, ref GetStatusIconsEvent args)
    {
        if (_mobState.IsDead(ent) || !_prototypes.TryIndex(Icon, out var icon))
            return;

        args.StatusIcons.Add(icon);
    }
}
