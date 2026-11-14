using Content.Shared._Onyx.Wounds;
using Content.Shared._Shitmed.Body.Events;
using Content.Shared._Shitmed.Cybernetics;
using Content.Shared._Shitmed.Medical.Surgery.Steps.Parts;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Silicons.Borgs.Components;
using Robust.Shared.Prototypes;

namespace Content.Shared._WF.Surgery;

/// <summary>
/// Maintains <see cref="SurgicalSiteComponent"/> from the surgical marker components on a body part.
/// </summary>
/// <remarks>
/// Marker components are the authority: operations mutate them through their legacy step effects, and this
/// system mirrors the result into one bitfield for availability checks and the operating computer. Parts that
/// lack skin, bones or vessels report the corresponding states as already present, so a skeleton skips the
/// incision and a slime skips sawing.
/// </remarks>
public sealed partial class SurgicalSiteSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly HashSet<EntityUid> _dirty = new();

    /// <summary>Body part species treated as mechanical even without a wound profile.</summary>
    private static readonly HashSet<string> MechanicalSpecies = new(StringComparer.OrdinalIgnoreCase)
    {
        "Cyborg",
        "IPC",
        "Synth",
        "Android",
    };

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyPartComponent, ComponentStartup>(OnPartStartup);
        SubscribeLocalEvent<BodyPartComponent, BodyPartAddedEvent>(OnPartAdded);
        SubscribeLocalEvent<BodyPartComponent, BodyPartAttachedEvent>(OnPartAttached);

        SubscribeMarker<IncisionOpenComponent>();
        SubscribeMarker<SkinRetractedComponent>();
        SubscribeMarker<BleedersClampedComponent>();
        SubscribeMarker<InternalBleedersClampedComponent>();
        SubscribeMarker<RibcageSawedComponent>();
        SubscribeMarker<RibcageOpenComponent>();
        SubscribeMarker<BoneDrilledComponent>();
        SubscribeMarker<PlasticAppliedComponent>();
        SubscribeMarker<ProstheticUnsecuredComponent>();
        SubscribeMarker<CavityWidenedComponent>();
    }

    private void SubscribeMarker<T>() where T : IComponent
    {
        SubscribeLocalEvent<T, ComponentStartup>(OnMarkerChanged);
        SubscribeLocalEvent<T, ComponentShutdown>(OnMarkerShutdown);
    }

    private void OnMarkerChanged<T>(Entity<T> ent, ref ComponentStartup args) where T : IComponent
    {
        Recalculate(ent.Owner);
    }

    private void OnMarkerShutdown<T>(Entity<T> ent, ref ComponentShutdown args) where T : IComponent
    {
        // Recalculate after the component is actually gone. Another system owns ComponentRemove for some
        // markers, and a part may lose several markers at once, so defer to the next update.
        if (!TerminatingOrDeleted(ent))
            _dirty.Add(ent.Owner);
    }

    /// <inheritdoc/>
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_dirty.Count == 0)
            return;

        foreach (var part in _dirty)
        {
            if (!TerminatingOrDeleted(part))
                Recalculate(part);
        }

        _dirty.Clear();
    }

    private void OnPartStartup(Entity<BodyPartComponent> ent, ref ComponentStartup args)
    {
        EnsureComp<SurgicalSiteComponent>(ent);
        Recalculate(ent);
    }

    private void OnPartAdded(Entity<BodyPartComponent> ent, ref BodyPartAddedEvent args)
    {
        Recalculate(ent);
    }

    private void OnPartAttached(Entity<BodyPartComponent> ent, ref BodyPartAttachedEvent args)
    {
        Recalculate(ent);
    }

    /// <summary>Recomputes the cached state of a part from its marker components.</summary>
    public void Recalculate(EntityUid part)
    {
        if (!TryComp<SurgicalSiteComponent>(part, out var site))
            return;

        var state = SurgeryState.None;

        if (HasComp<IncisionOpenComponent>(part))
            state |= SurgeryState.SkinCut;
        if (HasComp<SkinRetractedComponent>(part))
            state |= SurgeryState.SkinOpen;
        if (HasComp<BleedersClampedComponent>(part))
            state |= SurgeryState.VesselsClamped;
        else if (state.HasAny(SurgeryStateExtensions.AllSkin))
            state |= SurgeryState.VesselsUnclamped;
        if (HasComp<RibcageSawedComponent>(part) || HasComp<RibcageOpenComponent>(part))
            state |= SurgeryState.BoneSawed;
        if (HasComp<BoneDrilledComponent>(part))
            state |= SurgeryState.BoneDrilled;
        if (HasComp<InternalBleedersClampedComponent>(part))
            state |= SurgeryState.OrgansCut;
        if (HasComp<PlasticAppliedComponent>(part))
            state |= SurgeryState.PlasticApplied;
        if (HasComp<ProstheticUnsecuredComponent>(part))
            state |= SurgeryState.ProstheticUnsecured;
        if (HasComp<CavityWidenedComponent>(part))
            state |= SurgeryState.CavityWidened;

        // Pulled-back skin supersedes a mere cut.
        if (state.HasAny(SurgeryState.SkinOpen))
            state &= ~SurgeryState.SkinCut;

        if (site.State == state)
            return;

        site.State = state;
        Dirty(part, site);
    }

    /// <summary>The state stored on the part, recalculating it if the part has no cached site yet.</summary>
    public SurgeryState GetState(EntityUid part)
    {
        if (!TryComp<SurgicalSiteComponent>(part, out var site))
            return SurgeryState.None;

        return site.State;
    }

    /// <summary>The part's state with the states implied by its anatomy added.</summary>
    public SurgeryState GetEffectiveState(EntityUid part)
    {
        var state = GetState(part);

        if (!HasSkin(part))
            state = (state | SurgeryStateExtensions.SkinlessStates) & ~SurgeryState.SkinCut;
        if (!HasBones(part))
            state |= SurgeryStateExtensions.BonelessStates;
        if (!HasVessels(part))
            state |= SurgeryStateExtensions.VessellessStates;

        return state;
    }

    /// <summary>Whether the part satisfies all the passed states, accounting for anatomy.</summary>
    public bool HasAllStates(EntityUid part, SurgeryState states) =>
        states == SurgeryState.None || GetEffectiveState(part).HasAll(states);

    /// <summary>Whether the part satisfies any of the passed states, accounting for anatomy.</summary>
    public bool HasAnyStates(EntityUid part, SurgeryState states) =>
        states == SurgeryState.None || GetEffectiveState(part).HasAny(states);

    /// <summary>Whether the part can have an incision made in it.</summary>
    public bool HasSkin(EntityUid part)
    {
        if (TryComp<SurgicalSiteComponent>(part, out var site) && site.HasSkin is { } skin)
            return skin;
        // A mechanical part still has a shell that must be unscrewed; only species without skin skip it.
        return !IsSkinlessSpecies(part);
    }

    /// <summary>Whether the part has a bone that can be sawed.</summary>
    public bool HasBones(EntityUid part)
    {
        if (TryComp<SurgicalSiteComponent>(part, out var site) && site.HasBones is { } bones)
            return bones;

        // A mechanical part has an endoskeleton, not an organic bone, but it is opened the same way.
        return !IsBonelessSpecies(part);
    }

    /// <summary>Whether the part has blood vessels that can be clamped.</summary>
    public bool HasVessels(EntityUid part)
    {
        if (TryComp<SurgicalSiteComponent>(part, out var site) && site.HasVessels is { } vessels)
            return vessels;

        if (TryComp<BodyPartComponent>(part, out var bodyPart) && bodyPart.Species.Length > 0 &&
            _prototypes.TryIndex<SurgicalAnatomyPrototype>(bodyPart.Species, out var anatomy))
        {
            return anatomy.HasVessels;
        }

        return !IsMechanical(part);
    }

    /// <summary>Whether the part is a robotic chassis or a cybernetic limb.</summary>
    public bool IsMechanical(EntityUid part)
    {
        if (HasComp<CyberneticsComponent>(part))
            return true;

        if (TryComp<BodyPartComponent>(part, out var bodyPart))
        {
            if (bodyPart.Species.Length > 0 && MechanicalSpecies.Contains(bodyPart.Species))
                return true;
            if (bodyPart.Body is { } body && HasComp<BorgChassisComponent>(body))
                return true;
        }

        if (!TryComp<WoundableComponent>(part, out var woundable) ||
            !_prototypes.TryIndex(woundable.Profile, out var profile) ||
            profile.TreatmentCapabilities.Count == 0)
        {
            return false;
        }

        return !profile.TreatmentCapabilities.Contains(TreatmentCapability.Biological) &&
               (profile.TreatmentCapabilities.Contains(TreatmentCapability.Mechanical) ||
                profile.TreatmentCapabilities.Contains(TreatmentCapability.Electrical));
    }

    /// <summary>Adds or removes one state on a part by toggling its marker component.</summary>
    public void SetState(EntityUid part, SurgeryState state, bool value)
    {
        switch (state)
        {
            case SurgeryState.SkinCut:
                SetMarker<IncisionOpenComponent>(part, value);
                break;
            case SurgeryState.SkinOpen:
                SetMarker<SkinRetractedComponent>(part, value);
                break;
            case SurgeryState.VesselsClamped:
                SetMarker<BleedersClampedComponent>(part, value);
                break;
            case SurgeryState.VesselsUnclamped:
                // Unclamping is the absence of the clamp marker; cutting the skin re-adds it in Recalculate.
                if (!value)
                    SetMarker<BleedersClampedComponent>(part, true);
                else if (HasComp<BleedersClampedComponent>(part))
                    RemCompDeferred<BleedersClampedComponent>(part);
                break;
            case SurgeryState.OrgansCut:
                SetMarker<InternalBleedersClampedComponent>(part, value);
                break;
            case SurgeryState.BoneSawed:
                SetMarker<RibcageSawedComponent>(part, value);
                SetMarker<RibcageOpenComponent>(part, false);
                break;
            case SurgeryState.BoneDrilled:
                SetMarker<BoneDrilledComponent>(part, value);
                break;
            case SurgeryState.PlasticApplied:
                SetMarker<PlasticAppliedComponent>(part, value);
                break;
            case SurgeryState.ProstheticUnsecured:
                SetMarker<ProstheticUnsecuredComponent>(part, value);
                break;
            case SurgeryState.CavityWidened:
                SetMarker<CavityWidenedComponent>(part, value);
                break;
        }

        Recalculate(part);
    }

    /// <summary>Clears every state a closed incision should clear.</summary>
    public void ClearOnClose(EntityUid part)
    {
        foreach (var (state, _) in SurgeryStateExtensions.Readable)
        {
            if (!SurgeryStateExtensions.ClearedOnClose.HasAny(state))
                continue;

            switch (state)
            {
                case SurgeryState.VesselsUnclamped:
                    continue;
                case SurgeryState.SkinOpen:
                    SetMarker<SkinRetractedComponent>(part, false);
                    continue;
                case SurgeryState.SkinCut:
                    SetMarker<IncisionOpenComponent>(part, false);
                    continue;
                case SurgeryState.VesselsClamped:
                    SetMarker<BleedersClampedComponent>(part, false);
                    continue;
            }

            SetState(part, state, false);
        }

        Recalculate(part);
    }

    private void SetMarker<T>(EntityUid part, bool value) where T : IComponent, new()
    {
        if (value)
            EnsureComp<T>(part);
        else if (HasComp<T>(part))
            RemComp<T>(part);
    }

    /// <summary>Human readable state lines for the operating computer.</summary>
    public List<string> GetReadableState(EntityUid part)
    {
        var lines = new List<string>();
        var state = GetState(part);

        if (!HasBones(part))
            lines.Add(Loc.GetString("surgery-state-lacks-bones"));
        if (!HasVessels(part))
            lines.Add(Loc.GetString("surgery-state-lacks-vessels"));
        if (!HasSkin(part))
            lines.Add(Loc.GetString("surgery-state-lacks-skin"));

        foreach (var (flag, locId) in SurgeryStateExtensions.Readable)
        {
            if (state.HasAny(flag))
                lines.Add(Loc.GetString(locId));
        }

        if (lines.Count == 0)
            lines.Add(Loc.GetString("surgery-state-intact"));

        return lines;
    }

    private bool IsBonelessSpecies(EntityUid part)
    {
        if (!TryComp<BodyPartComponent>(part, out var bodyPart) || bodyPart.Species.Length == 0)
            return false;

        return _prototypes.TryIndex<SurgicalAnatomyPrototype>(bodyPart.Species, out var anatomy) && !anatomy.HasBones;
    }

    private bool IsSkinlessSpecies(EntityUid part)
    {
        if (!TryComp<BodyPartComponent>(part, out var bodyPart) || bodyPart.Species.Length == 0)
            return false;

        return _prototypes.TryIndex<SurgicalAnatomyPrototype>(bodyPart.Species, out var anatomy) && !anatomy.HasSkin;
    }
}
