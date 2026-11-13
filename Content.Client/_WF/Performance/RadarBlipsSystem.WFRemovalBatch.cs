using Content.Shared._WF.Performance;

namespace Content.Client._Mono.Radar;

public sealed partial class RadarBlipsSystem
{
    private readonly HashSet<NetEntity> _wfRemoved = new();

    /// <summary>Drops every blip the server removed this tick.</summary>
    private void WFRemoveBlips(WFBlipRemovalBatchEvent args)
    {
        _wfRemoved.Clear();
        _wfRemoved.UnionWith(args.Blips);
        _blips.RemoveAll(blip => _wfRemoved.Contains(blip.Uid));
    }
}
