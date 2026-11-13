using Content.Server._WF.ShipCommand;

namespace Content.Server.Cargo.Systems;

/// <summary>Something a commanded ship's service sold below its list price resells for no more than was paid.</summary>
public sealed partial class PricingSystem
{
    /// <summary>Set while pricing as if nothing had been bought at a bargain.</summary>
    private bool _wfIgnoreBargains;

    /// <summary>An entity's own price less the bargain it was bought at, never below zero.</summary>
    private double WfBargainPrice(EntityUid uid, double price)
    {
        if (_wfIgnoreBargains || !TryComp<WFBargainComponent>(uid, out var bargain))
            return price;

        return Math.Max(0, price - bargain.Discount);
    }

    /// <summary>
    /// An entity's price as if nothing in it had been bought at a bargain: what a repair or a value cap goes by, where
    /// <see cref="GetPrice"/> is what it resells for.
    /// </summary>
    public double GetUndiscountedPrice(EntityUid uid, bool includeContents = true)
    {
        var outer = _wfIgnoreBargains;
        _wfIgnoreBargains = true;
        try
        {
            return GetPrice(uid, includeContents);
        }
        finally
        {
            _wfIgnoreBargains = outer;
        }
    }
}
