using System.Linq;
using Content.Server._WF.CargoSales;
using Content.Server._WF.ReaverCampaign.Components;
using Content.Server._WF.TradeGoods;
using Content.Server._WF.TradeGoods.Systems;
using Content.Server.Cargo.Systems;
using Content.Shared._WF.ReaverCampaign;
using Content.Shared._WF.TradeGoods;
using Content.Shared.Tag;
using Robust.Shared.Containers;

namespace Content.Server._WF.ReaverCampaign.Systems;

public sealed partial class WFReaverCampaignSystem
{
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private TagSystem _tag = default!;
    [Dependency] private WFTradeMarketSystem _market = default!;

    /// <summary>Banks a funding sale toward the next payout, and pays it out if one is due.</summary>
    private void OnTradeSale(WFTradeSaleEvent args)
    {
        if (!Running
            || Campaign is not { FundingCrates: > 0 } campaign
            || !FundsReavers(args)
            || State(true) is not { Finished: false } state)
            return;

        state.Funding = Math.Min(state.Funding + 1, FundingChest(campaign));
        TickFunding(campaign, state);
    }

    /// <summary>The most crates the war chest banks: <see cref="WFReaverCampaignPrototype.FundingBank"/> payouts' worth.</summary>
    public static int FundingChest(WFReaverCampaignPrototype campaign)
    {
        return campaign.FundingCrates * Math.Max(1, campaign.FundingBank);
    }

    /// <summary>Banks the tagged goods of a cargo pallet sale, narcotics by default, by their appraised value.</summary>
    private void OnPalletSale(ref WFPalletSaleEvent args)
    {
        if (!Running
            || Campaign is not { FundingCrates: > 0, FundingGoodsValue: > 0 } campaign
            || State(true) is not { Finished: false } state)
            return;

        var value = 0.0;
        foreach (var good in args.Goods)
        {
            if (!_tag.HasTag(good, campaign.FundingGoodsTag))
                continue;

            // A tagged thing inside another is already in that one's price.
            if (_container.TryGetContainingContainer((good, null, null), out var holder)
                && args.Goods.Contains(holder.Owner)
                && _tag.HasTag(holder.Owner, campaign.FundingGoodsTag))
                continue;

            value += _pricing.GetPrice(good);
        }

        BankGoods(campaign, state, value);
    }

    /// <summary>Adds goods value to the war chest, a crate for every <see cref="WFReaverCampaignPrototype.FundingGoodsValue"/>.</summary>
    public void BankGoods(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state, double value)
    {
        if (value <= 0 || campaign.FundingGoodsValue <= 0)
            return;

        state.FundingGoods += value;
        var crates = (int) (state.FundingGoods / campaign.FundingGoodsValue);
        state.FundingGoods -= crates * campaign.FundingGoodsValue;
        var chest = FundingChest(campaign);
        // A full chest takes no more; what is over is lost.
        if (state.Funding + crates >= chest)
            state.FundingGoods = 0;

        state.Funding = Math.Min(state.Funding + crates, chest);
        TickFunding(campaign, state);
    }

    /// <summary>Whether a sale funds the Reavers: smuggling crates at open chutes, and freight taken off NPC haulers fenced at the Underground.</summary>
    private bool FundsReavers(WFTradeSaleEvent args)
    {
        if (_market.Profile(args.Grid).ContrabandGoods.Contains(args.Good))
            return !_market.IsHiddenMarket(args.Grid);

        return _market.IsSealedOnly(args.Chute)
               && TryComp<WFTradeOriginComponent>(args.Crate, out var origin)
               && origin.Run != null;
    }

    /// <summary>Spends one payout from the war chest once the campaign isn't held, a stronghold stands to spend it and the last payout has cooled down.</summary>
    private void TickFunding(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        if (campaign.FundingCrates <= 0 || state.Funding < campaign.FundingCrates || Held || state.Finished ||
            _timing.CurTime < state.NextFunding)
            return;

        if (Strongholds().Count == 0)
            return;

        state.Funding -= campaign.FundingCrates;
        state.NextFunding = _timing.CurTime + campaign.FundingCooldown;
        Fund(campaign, state);
    }

    /// <summary>
    /// Spends one payout: every stronghold's next reinforcement and the next spread come sooner by
    /// <see cref="WFReaverCampaignPrototype.FundingShare"/> of their intervals, an extra patrol goes out if one may, and
    /// the sector hears of it on the radio.
    /// </summary>
    public void Fund(WFReaverCampaignPrototype campaign, WFReaverCampaignComponent state)
    {
        var now = _timing.CurTime;
        var reinforcementCut = ReinforcementInterval(campaign) * campaign.FundingShare;
        foreach (var stronghold in Strongholds())
        {
            stronghold.Comp1.NextReinforcement = MaxTime(now, stronghold.Comp1.NextReinforcement - reinforcementCut);
        }

        if (state.NextSpread is { } next)
            state.NextSpread = MaxTime(now, next - campaign.SpreadInterval * campaign.FundingShare);

        FundPatrol(campaign);
        state.Funded++;
        AnnounceRadio(Loc.GetString("wf-reaver-funded"));
    }

    /// <summary>
    /// Sends a patrol from the stronghold with the fewest out, while the patrols out stay within the usual cap plus
    /// <see cref="WFReaverCampaignPrototype.FundingPatrols"/>. It leaves the stronghold's own patrol schedule as it was.
    /// </summary>
    private void FundPatrol(WFReaverCampaignPrototype campaign)
    {
        if (campaign.FundingPatrols <= 0)
            return;

        var patrols = Patrols();
        if (patrols.Count >= PatrolCap() + campaign.FundingPatrols)
            return;

        foreach (var stronghold in Strongholds().OrderBy(entry => patrols.Count(patrol => patrol.Comp1.Stronghold == entry.Owner)))
        {
            if (TryLaunchPatrol(stronghold.Owner, out _, reschedule: false))
                return;
        }
    }
}
