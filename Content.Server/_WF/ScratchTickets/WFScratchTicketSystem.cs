using Content.Server._NF.Bank;
using Content.Server.Administration.Logs;
using Content.Server.Popups;
using Content.Server.Power.EntitySystems;
using Content.Server.Stack;
using Content.Shared._Mono.Traits.Physical;
using Content.Shared._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.ScratchTickets;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._WF.ScratchTickets;

/// <summary>Rolls scratch tickets, deals them off a pile, records the scratch and pays a winner out at an ATM.</summary>
public sealed partial class WFScratchTicketSystem : EntitySystem
{
    private static readonly ProtoId<StackPrototype> CashStack = "Credit";

    [Dependency] private IAdminLogManager _adminLog = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private BankSystem _bank = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private MetaDataSystem _meta = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StackSystem _stack = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WFScratchTicketComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<WFScratchTicketComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<WFScratchTicketComponent, StackCountChangedEvent>(OnCountChanged);
        SubscribeLocalEvent<WFScratchTicketComponent, UseInHandEvent>(OnUseInHand, before: new[] { typeof(ActivatableUISystem) });
        SubscribeLocalEvent<BankATMComponent, InteractUsingEvent>(OnAtmInteractUsing);

        Subs.BuiEvents<WFScratchTicketComponent>(WFScratchTicketUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnUiOpened);
            subs.Event<WFScratchTicketRevealedMessage>(OnRevealed);
        });
    }

    private void OnMapInit(Entity<WFScratchTicketComponent> ent, ref MapInitEvent args)
    {
        // A ticket saved on a map keeps what it rolled.
        if (ent.Comp.Cells.Count != WFScratchTicketRules.CellCount(ent.Comp.Format))
        {
            var winner = WFScratchTicketRules.Roll(ent.Comp.Format, ent.Comp.Symbols, ent.Comp.LoseWeight, new System.Random(_random.Next()), ent.Comp.Cells);
            ent.Comp.Prize = winner < 0 ? 0 : ent.Comp.Symbols[winner].Prize;
        }

        UpdateLook(ent);
    }

    private void OnUiOpened(Entity<WFScratchTicketComponent> ent, ref BoundUIOpenedEvent args)
    {
        Unstack(ent);
        UpdateUi(ent);
    }

    private void OnCountChanged(Entity<WFScratchTicketComponent> ent, ref StackCountChangedEvent args)
    {
        // A pack that has lost tickets is no longer a pack of ten.
        if (TryComp<StackComponent>(ent, out var stack)
            && _proto.TryIndex<StackPrototype>(stack.StackTypeId, out var type)
            && _proto.TryIndex<EntityPrototype>(type.Spawn, out var single))
        {
            _meta.SetEntityName(ent, single.Name);
        }

        UpdateLook(ent);
    }

    /// <summary>Using a pile pulls one ticket into a free hand and opens that.</summary>
    private void OnUseInHand(Entity<WFScratchTicketComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled || !TryComp<StackComponent>(ent, out var stack) || stack.Count <= 1)
            return;

        args.Handled = true;

        if (!_hands.TryGetEmptyHand(args.User, out _))
        {
            _popup.PopupEntity(Loc.GetString("wf-scratch-ticket-pile-no-hand"), ent, args.User);
            return;
        }

        if (_stack.Split(ent, 1, Transform(args.User).Coordinates, stack) is not { } pulled)
            return;

        if (_hands.TryPickupAnyHand(args.User, pulled, checkActionBlocker: false))
            _interaction.UseInHandInteraction(args.User, pulled, checkUseDelay: false);
    }

    /// <summary>
    /// A ticket that has been looked at no longer stacks: putting it back in a pile would swap a known ticket for a
    /// fresh one.
    /// </summary>
    private void Unstack(EntityUid uid)
    {
        RemComp<StackComponent>(uid);
    }

    private void OnRevealed(Entity<WFScratchTicketComponent> ent, ref WFScratchTicketRevealedMessage args)
    {
        Reveal(ent, args.Actor);
    }

    /// <summary>Marks the ticket scratched and shows what it won.</summary>
    public void Reveal(Entity<WFScratchTicketComponent> ent, EntityUid? user)
    {
        if (ent.Comp.Scratched)
            return;

        ent.Comp.Scratched = true;
        Unstack(ent);
        UpdateLook(ent);
        UpdateUi(ent);

        if (ent.Comp.Prize <= 0 || user == null)
            return;

        _audio.PlayEntity(ent.Comp.WinSound, user.Value, ent);
        _adminLog.Add(LogType.Action, LogImpact.Low,
            $"{ToPrettyString(user.Value):actor} scratched {ToPrettyString(ent)} and won {ent.Comp.Prize}");
    }

    private void OnExamined(Entity<WFScratchTicketComponent> ent, ref ExaminedEvent args)
    {
        if (!ent.Comp.Scratched)
            args.PushMarkup(Loc.GetString("wf-scratch-ticket-examine-fresh"));
        else if (ent.Comp.Prize > 0)
            args.PushMarkup(Loc.GetString("wf-scratch-ticket-examine-won", ("amount", BankSystemExtensions.ToSpesoString(ent.Comp.Prize))));
        else
            args.PushMarkup(Loc.GetString("wf-scratch-ticket-examine-lost"));
    }

    private void OnAtmInteractUsing(Entity<BankATMComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<WFScratchTicketComponent>(args.Used, out var ticket) || !this.IsPowered(ent, EntityManager))
            return;

        args.Handled = true;

        // A redeemed ticket lingers until the end of the tick; a second use in that tick must not pay again.
        if (EntityManager.IsQueuedForDeletion(args.Used) || TerminatingOrDeleted(args.Used))
            return;

        if (!ticket.Scratched || ticket.Prize <= 0)
        {
            var refusal = ticket.Scratched ? "wf-scratch-ticket-atm-lost" : "wf-scratch-ticket-atm-fresh";
            _popup.PopupEntity(Loc.GetString(refusal), ent, args.User);
            _audio.PlayPvs(ent.Comp.ErrorSound, ent);
            return;
        }

        // An Ironman's account is frozen: the money comes out as cash.
        var banked = !HasComp<IronmanComponent>(args.User) && _bank.TryBankDeposit(args.User, ticket.Prize, false);
        if (!banked)
            _stack.Spawn(ticket.Prize, CashStack, Transform(ent).Coordinates);

        _popup.PopupEntity(Loc.GetString(banked ? "wf-scratch-ticket-atm-paid" : "wf-scratch-ticket-atm-paid-cash",
            ("amount", BankSystemExtensions.ToSpesoString(ticket.Prize))), ent, args.User);
        _audio.PlayPvs(ent.Comp.ConfirmSound, ent);
        _adminLog.Add(LogType.ATMUsage, LogImpact.Low,
            $"{ToPrettyString(args.User):actor} redeemed {ToPrettyString(args.Used)} for {ticket.Prize} at {ToPrettyString(ent)}");
        QueueDel(args.Used);
    }

    private void UpdateUi(Entity<WFScratchTicketComponent> ent)
    {
        _ui.SetUiState(ent.Owner, WFScratchTicketUiKey.Key,
            new WFScratchTicketState(ent.Comp.Cells.ToArray(), ent.Comp.Scratched, ent.Comp.Prize));
    }

    private void UpdateLook(Entity<WFScratchTicketComponent> ent)
    {
        var count = _stack.GetCount(ent);
        var look = ent.Comp.Scratched ? ent.Comp.Prize > 0 ? WFScratchTicketLook.Won : WFScratchTicketLook.Lost
            : count >= 20 ? WFScratchTicketLook.Stack20
            : count >= 10 ? WFScratchTicketLook.Stack10
            : count >= 2 ? WFScratchTicketLook.Stack2
            : WFScratchTicketLook.Fresh;
        _appearance.SetData(ent, WFScratchTicketVisuals.State, look);
    }
}
