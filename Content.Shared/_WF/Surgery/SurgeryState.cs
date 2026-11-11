using System;

namespace Content.Shared._WF.Surgery;

/// <summary>
/// Anatomical surgical state of a body part, mirroring /tg/station's bodypart surgery bitfield.
/// </summary>
[Flags]
public enum SurgeryState : ushort
{
    None = 0,

    /// <summary>An incision has been made into the skin.</summary>
    SkinCut = 1 << 0,

    /// <summary>The skin has been pulled back.</summary>
    SkinOpen = 1 << 1,

    /// <summary>Blood vessels are exposed and bleeding.</summary>
    VesselsUnclamped = 1 << 2,

    /// <summary>Blood vessels are exposed but clamped.</summary>
    VesselsClamped = 1 << 3,

    /// <summary>The organ tissue has been incised.</summary>
    OrgansCut = 1 << 4,

    /// <summary>Holes have been drilled in the bone, exclusive with <see cref="BoneSawed"/>.</summary>
    BoneDrilled = 1 << 5,

    /// <summary>The bone has been sawed apart.</summary>
    BoneSawed = 1 << 6,

    /// <summary>Plastic has been applied during plastic surgery.</summary>
    PlasticApplied = 1 << 7,

    /// <summary>An arbitrary prosthetic is attached but not yet secured.</summary>
    ProstheticUnsecured = 1 << 8,

    /// <summary>The chest cavity has been widened for implants.</summary>
    CavityWidened = 1 << 9,
}

/// <summary>
/// Groups and helpers for <see cref="SurgeryState"/>.
/// </summary>
public static class SurgeryStateExtensions
{
    /// <summary>Both skin states.</summary>
    public const SurgeryState AllSkin = SurgeryState.SkinCut | SurgeryState.SkinOpen;

    /// <summary>Both vessel states.</summary>
    public const SurgeryState AllVessels = SurgeryState.VesselsUnclamped | SurgeryState.VesselsClamped;

    /// <summary>Both bone states.</summary>
    public const SurgeryState AllBones = SurgeryState.BoneDrilled | SurgeryState.BoneSawed;

    /// <summary>Every organ state.</summary>
    public const SurgeryState AllOrgans = SurgeryState.OrgansCut;

    /// <summary>States cleared when an incision is closed.</summary>
    public const SurgeryState ClearedOnClose = AllSkin | AllVessels | AllBones | AllOrgans | SurgeryState.CavityWidened;

    /// <summary>States a skinless part always counts as having.</summary>
    public const SurgeryState SkinlessStates = SurgeryState.SkinOpen;

    /// <summary>States a boneless part always counts as having.</summary>
    public const SurgeryState BonelessStates = SurgeryState.BoneDrilled | SurgeryState.BoneSawed;

    /// <summary>States a part without blood vessels always counts as having.</summary>
    public const SurgeryState VessellessStates = SurgeryState.VesselsClamped | SurgeryState.OrgansCut;

    /// <summary>Whether every bit in <paramref name="check"/> is present.</summary>
    public static bool HasAll(this SurgeryState state, SurgeryState check) => (state & check) == check;

    /// <summary>Whether any bit in <paramref name="check"/> is present.</summary>
    public static bool HasAny(this SurgeryState state, SurgeryState check) => (state & check) != 0;

    /// <summary>Localisation ids for the readable states, in ascending bit order.</summary>
    public static readonly (SurgeryState State, string LocId)[] Readable =
    {
        (SurgeryState.SkinCut, "surgery-state-skin-cut"),
        (SurgeryState.SkinOpen, "surgery-state-skin-open"),
        (SurgeryState.VesselsUnclamped, "surgery-state-vessels-unclamped"),
        (SurgeryState.VesselsClamped, "surgery-state-vessels-clamped"),
        (SurgeryState.OrgansCut, "surgery-state-organs-cut"),
        (SurgeryState.BoneDrilled, "surgery-state-bone-drilled"),
        (SurgeryState.BoneSawed, "surgery-state-bone-sawed"),
        (SurgeryState.PlasticApplied, "surgery-state-plastic-applied"),
        (SurgeryState.ProstheticUnsecured, "surgery-state-prosthetic-unsecured"),
        (SurgeryState.CavityWidened, "surgery-state-cavity-widened"),
    };
}
