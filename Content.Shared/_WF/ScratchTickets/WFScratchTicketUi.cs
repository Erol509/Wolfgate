using Robust.Shared.Serialization;

namespace Content.Shared._WF.ScratchTickets;

[Serializable, NetSerializable]
public enum WFScratchTicketUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum WFScratchTicketVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum WFScratchTicketLayers : byte
{
    Base,
}

/// <summary>How the ticket looks in the world.</summary>
[Serializable, NetSerializable]
public enum WFScratchTicketLook : byte
{
    Fresh,
    Lost,
    Won,

    // Banded piles of unscratched tickets: 2 or more, 10 or more, and a full 20.
    Stack2,
    Stack10,
    Stack20,
}

/// <summary>The ticket's hidden cells, sent to whoever holds it open.</summary>
[Serializable, NetSerializable]
public sealed class WFScratchTicketState : BoundUserInterfaceState
{
    public readonly int[] Cells;
    public readonly bool Scratched;
    public readonly int Prize;

    public WFScratchTicketState(int[] cells, bool scratched, int prize)
    {
        Cells = cells;
        Scratched = scratched;
        Prize = prize;
    }
}

/// <summary>The holder has scratched the foil off.</summary>
[Serializable, NetSerializable]
public sealed class WFScratchTicketRevealedMessage : BoundUserInterfaceMessage;
