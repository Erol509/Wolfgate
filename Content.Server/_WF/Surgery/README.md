# Surgery

State-driven surgery, reproducing /tg/station's surgical gameplay on Wolfgate's anatomy. A body part carries
an anatomical surgical state (skin cut or open, vessels clamped, bone sawed or drilled, organs incised, cavity
widened, and so on). Holding a tool and using it on a patient offers the operations the patient's actual state
permits, through a radial wheel. The old fixed step window is gone; operations are available because the body
is in the right condition, not because the surgeon reached a numbered step.

Entry points: `SurgicalOperationSystem` (availability, do-after, failure, effects), `SharedSurgicalOperationSystem`
(state gates and radial options), `SurgicalSiteSystem` (part state), `OperatingComputerSystem` and
`OperatingComputerWindow` (patient monitoring and operation catalog). Legacy surgery and step prototypes remain
the effect layer, so every existing Wolfgate wound, organ, limb and synthetic procedure keeps its behaviour.

## How it works

- `SurgicalSiteComponent` caches the part's state; the surgical marker components (`IncisionOpen`, `SkinRetracted`,
  `BleedersClamped`, `RibcageSawed`, `InternalBleedersClamped`, plus the new `BoneDrilled`, `CavityWidened`,
  `PlasticApplied` and `ProstheticUnsecured`) remain the authority. A part that lacks skin, bones or vessels
  reports the matching states as already present, so a skeleton skips the incision and a slime skips sawing.
- Each `surgicalOperation` prototype names the legacy surgery providing its conditions and the step providing its
  effects, plus its state gate, accepted tools (including ghetto substitutes and their speed penalties), time and
  flags. The generic operations cover incision, retraction, clamping, sawing, drilling, organ incision, organ
  manipulation, closing, bone repair and the mechanical equivalents.
- `python Tools/_WF/Surgery/generate_operations.py` derives one operation per procedure-specific legacy step from
  the legacy surgery and step prototypes. Steps already covered generically (organ insertion and removal, the
  per-surgery seal-wound steps) are skipped so the wheel does not repeat them. Run it after editing a legacy surgery.
- The operating computer tracks the patient buckled to an adjacent operating table and publishes vitals, the
  selected zone's surgical state and every operation the state permits. It opens on a hand click or with a
  surgical tool; the table itself has no UI. Operations become available when the station
  researches the technology named in a `surgicalResearch` prototype.

## Compatibility notes

- The legacy step-selection message still starts the equivalent state-driven operation, so the autodoc and older
  internal callers keep working without a second execution engine. The old step window itself (SurgeryWindow,
  SurgeryStepButton) was removed; SurgeryBui keeps its registered name but now opens the radial.
- Detached-limb surgery is limited to operations flagged `NoPatient`; the radial itself opens on a patient.
- The radial shows at most eight slots at once and puts the rest on further pages (the tg next-page sprite), so a
  deep anatomical state never produces an unreadably large wheel.
- A missing limb is replaced the tg way: open the torso (skin cut, retracted and bleeders clamped), then use the
  detached limb on the patient while targeting the torso. Reattaching clears the stump's amputation-consequence
  wound instead of requiring a separate repair procedure first.
- Organ insertion and removal go through the single generic organ-manipulation operation: the radial shows one
  slice, and choosing it opens a small organ list with OK and Cancel. Closing the site (`WFMendSkin`) affixes
  limbs and organs that the operation reattached, because the per-surgery seal-wound steps are not generated.
  The autodoc keeps calling the legacy surgery and step prototypes directly, so it needs no changes.
- Cavity widening and closing are not offered at present: the chest cavity state and its marker still exist for
  legacy compatibility, but nothing sets it.
- A procedure's last generated step ends its Wolfmed "in progress" listing instead of waiting for the skipped
  legacy closing step, so stopping internal bleeding or extracting embedded objects removes those slices as soon
  as the part no longer has the wound or object.
- The operating computer keeps its shell while state updates arrive: only the active tab is refreshed, the
  possible-operations and catalog scroll positions are preserved, and the mechanical filter toggles visibility
  in place instead of rebuilding the list. Its possible-operations list also checks each operation's body part
  and zone, so an open torso does not list head or limb procedures.
- Advanced tg operations without a Wolfgate equivalent yet (lobotomy, pacify, brainwash, revival, autopsy,
  dentals, plastic surgery naming, bioware, xeno surgery) are not shipped. The framework supports them; they need
  Wolfgate-side effects before they can be added honestly.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Surgery/OperatingComputerSystem.cs`](OperatingComputerSystem.cs)
- [`Content.Server/_WF/Surgery/SurgicalOperationSystem.cs`](SurgicalOperationSystem.cs)

### Shared

- [`Content.Shared/_WF/Surgery/OperatingComputerComponent.cs`](../../../Content.Shared/_WF/Surgery/OperatingComputerComponent.cs)
- [`Content.Shared/_WF/Surgery/SharedSurgicalOperationSystem.cs`](../../../Content.Shared/_WF/Surgery/SharedSurgicalOperationSystem.cs)
- [`Content.Shared/_WF/Surgery/SurgeryMarkers.cs`](../../../Content.Shared/_WF/Surgery/SurgeryMarkers.cs)
- [`Content.Shared/_WF/Surgery/SurgeryState.cs`](../../../Content.Shared/_WF/Surgery/SurgeryState.cs)
- [`Content.Shared/_WF/Surgery/SurgicalAnatomyPrototype.cs`](../../../Content.Shared/_WF/Surgery/SurgicalAnatomyPrototype.cs)
- [`Content.Shared/_WF/Surgery/SurgicalOperationDoAfterEvent.cs`](../../../Content.Shared/_WF/Surgery/SurgicalOperationDoAfterEvent.cs)
- [`Content.Shared/_WF/Surgery/SurgicalOperationEvents.cs`](../../../Content.Shared/_WF/Surgery/SurgicalOperationEvents.cs)
- [`Content.Shared/_WF/Surgery/SurgicalOperationPrototype.cs`](../../../Content.Shared/_WF/Surgery/SurgicalOperationPrototype.cs)
- [`Content.Shared/_WF/Surgery/SurgicalResearchPrototype.cs`](../../../Content.Shared/_WF/Surgery/SurgicalResearchPrototype.cs)
- [`Content.Shared/_WF/Surgery/SurgicalSiteComponent.cs`](../../../Content.Shared/_WF/Surgery/SurgicalSiteComponent.cs)
- [`Content.Shared/_WF/Surgery/SurgicalSiteSystem.cs`](../../../Content.Shared/_WF/Surgery/SurgicalSiteSystem.cs)
- [`Content.Shared/_WF/Surgery/WFSurgeryAffixOnCloseComponent.cs`](../../../Content.Shared/_WF/Surgery/WFSurgeryAffixOnCloseComponent.cs)
- [`Content.Shared/_WF/Surgery/WFSurgeryAffixSystem.cs`](../../../Content.Shared/_WF/Surgery/WFSurgeryAffixSystem.cs)

### Client

- [`Content.Client/_WF/Surgery/BodyZoneSelectorControl.cs`](../../../Content.Client/_WF/Surgery/BodyZoneSelectorControl.cs)
- [`Content.Client/_WF/Surgery/OperatingComputerBui.cs`](../../../Content.Client/_WF/Surgery/OperatingComputerBui.cs)
- [`Content.Client/_WF/Surgery/OperatingComputerWindow.xaml`](../../../Content.Client/_WF/Surgery/OperatingComputerWindow.xaml)
- [`Content.Client/_WF/Surgery/OperatingComputerWindow.xaml.cs`](../../../Content.Client/_WF/Surgery/OperatingComputerWindow.xaml.cs)
- [`Content.Client/_WF/Surgery/OrganSelectionWindow.xaml`](../../../Content.Client/_WF/Surgery/OrganSelectionWindow.xaml)
- [`Content.Client/_WF/Surgery/OrganSelectionWindow.xaml.cs`](../../../Content.Client/_WF/Surgery/OrganSelectionWindow.xaml.cs)
- [`Content.Client/_WF/Surgery/SurgicalRadialBui.cs`](../../../Content.Client/_WF/Surgery/SurgicalRadialBui.cs)
- [`Content.Client/_WF/Surgery/SurgicalRadialButton.cs`](../../../Content.Client/_WF/Surgery/SurgicalRadialButton.cs)
- [`Content.Client/_WF/Surgery/SurgicalRadialMenu.xaml`](../../../Content.Client/_WF/Surgery/SurgicalRadialMenu.xaml)
- [`Content.Client/_WF/Surgery/SurgicalRadialMenu.xaml.cs`](../../../Content.Client/_WF/Surgery/SurgicalRadialMenu.xaml.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Surgery/OperatingComputerTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Surgery/OperatingComputerTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Surgery/SurgeryOperationTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Surgery/SurgeryOperationTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Surgery/SurgeryUiTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Surgery/SurgeryUiTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Surgery/SurgicalToolGateTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Surgery/SurgicalToolGateTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Surgery/anatomy.yml`](../../../Resources/Prototypes/_WF/Surgery/anatomy.yml)
- [`Resources/Prototypes/_WF/Surgery/legacy_operations.yml`](../../../Resources/Prototypes/_WF/Surgery/legacy_operations.yml)
- [`Resources/Prototypes/_WF/Surgery/operating_computer.yml`](../../../Resources/Prototypes/_WF/Surgery/operating_computer.yml)
- [`Resources/Prototypes/_WF/Surgery/operations.yml`](../../../Resources/Prototypes/_WF/Surgery/operations.yml)
- [`Resources/Prototypes/_WF/Surgery/research.yml`](../../../Resources/Prototypes/_WF/Surgery/research.yml)
- [`Resources/Prototypes/_WF/Surgery/surgeries.yml`](../../../Resources/Prototypes/_WF/Surgery/surgeries.yml)
- [`Resources/Prototypes/_WF/Surgery/surgery_steps.yml`](../../../Resources/Prototypes/_WF/Surgery/surgery_steps.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Surgery/legacy_operations.ftl`](../../../Resources/Locale/en-US/_WF/Surgery/legacy_operations.ftl)
- [`Resources/Locale/en-US/_WF/Surgery/surgery.ftl`](../../../Resources/Locale/en-US/_WF/Surgery/surgery.ftl)

### Textures

- [`Resources/Textures/_WF/Surgery/BodyZones/ATTRIBUTION.md`](../../../Resources/Textures/_WF/Surgery/BodyZones/ATTRIBUTION.md)
- [`Resources/Textures/_WF/Surgery/BodyZones/base.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/base.png)
- [`Resources/Textures/_WF/Surgery/BodyZones/chest.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/chest.png)
- [`Resources/Textures/_WF/Surgery/BodyZones/eyes.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/eyes.png)
- [`Resources/Textures/_WF/Surgery/BodyZones/groin.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/groin.png)
- [`Resources/Textures/_WF/Surgery/BodyZones/head.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/head.png)
- [`Resources/Textures/_WF/Surgery/BodyZones/l_arm.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/l_arm.png)
- [`Resources/Textures/_WF/Surgery/BodyZones/l_leg.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/l_leg.png)
- [`Resources/Textures/_WF/Surgery/BodyZones/mouth.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/mouth.png)
- [`Resources/Textures/_WF/Surgery/BodyZones/r_arm.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/r_arm.png)
- [`Resources/Textures/_WF/Surgery/BodyZones/r_leg.png`](../../../Resources/Textures/_WF/Surgery/BodyZones/r_leg.png)
- [`Resources/Textures/_WF/Surgery/RadialIcons.rsi/`](../../../Resources/Textures/_WF/Surgery/RadialIcons.rsi/)
- [`Resources/Textures/_WF/Surgery/RadialUI.rsi/`](../../../Resources/Textures/_WF/Surgery/RadialUI.rsi/)

### Tools

- [`Tools/_WF/Surgery/extract_radial_icons.py`](../../../Tools/_WF/Surgery/extract_radial_icons.py)
- [`Tools/_WF/Surgery/extract_radial_ui.py`](../../../Tools/_WF/Surgery/extract_radial_ui.py)
- [`Tools/_WF/Surgery/generate_operations.py`](../../../Tools/_WF/Surgery/generate_operations.py)

## Non-modular edits

- [`Content.Client/_Shitmed/Medical/Surgery/SurgeryBui.cs`](../../../Content.Client/_Shitmed/Medical/Surgery/SurgeryBui.cs): the step window is now the radial
- [`Content.Server/_Shitmed/Medical/Surgery/SurgerySystem.cs`](../../_Shitmed/Medical/Surgery/SurgerySystem.cs): replaced by the state-driven radial
- [`Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.Steps.cs`](../../../Content.Shared/_Shitmed/Surgery/SharedSurgerySystem.Steps.cs)
  - replaces the step-selection UI
  - caller-validated tools skip the step's list
- [`Content.Shared/_Shitmed/Surgery/Steps/SurgeryCanPerformStepEvent.cs`](../../../Content.Shared/_Shitmed/Surgery/Steps/SurgeryCanPerformStepEvent.cs): caller-validated tools
- [`Content.Shared/_Shitmed/Surgery/SurgeryStepEvent.cs`](../../../Content.Shared/_Shitmed/Surgery/SurgeryStepEvent.cs): caller-validated tools

<!-- WOLFGATE-GENERATED END -->
