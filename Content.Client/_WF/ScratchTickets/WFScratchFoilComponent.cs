namespace Content.Client._WF.ScratchTickets;

/// <summary>Keeps a ticket's half-scratched foil between openings of its window.</summary>
[RegisterComponent]
public sealed partial class WFScratchFoilComponent : Component
{
    /// <summary>The foil's opacity per pixel, row by row.</summary>
    public byte[] Alpha = Array.Empty<byte>();
}
