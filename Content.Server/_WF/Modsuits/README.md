# Modsuits

The MODsuit port from /tg/station: a back-worn control unit deploys a helmet, chestplate, gauntlets and boots,
seals them, and runs a catalogue of insertable modules. The control unit stores its core and modules behind a
wires panel; the module UI installs, removes, pins and toggles them. Modules spend the core's charge and add up
to the theme's complexity cap.

The core is bolted behind the panel, not an ejectable cell: unbolt it with a wrench, and swap the power cell
inside it with the panel's "Remove power cell" verb while the panel is open. Native suits ship with a core, a
charged cell and the compact storage, flashlight and welding modules. Part deployment is quick; sealing runs
at the theme's activation step. Suit activation and powered modules check the cell's charge, and the ion
jetpack module draws on it instead of a gas tank.

While a suit is active it projects a theme-coloured hardlight mesh over the wearer's body and marking layers, so
tails, ears, hair and wings that sit outside the suit sprite stay visible as hardlight instead of being hidden.
The mesh is a screen-space checker built in the shader; the per-theme Skyrat mask files are kept as source and
attribution but are not sampled at run time. Clothing layers are never tinted, so the effect cannot show through
the suit.

Entry points: `ModSuitComponent` (theme, parts, core slot, module container), `ModModuleComponent` (type,
complexity, power, cooldown, incompatibilities), `ModThemePrototype` (complexity cap, drain, inbuilt modules),
`ModSuitSystem` (deployment, sealing, power, module installation), the module systems under
`Content.Shared/_WF/Modsuits/Modules`, `ModSuitHardlightSystem` on the client for the sealed-suit mesh, and
`ModSuitWindow` on the client.

Original assets are the /tg/ MODsuit module icon set, copied to `Resources/Textures/_WF/Modsuits/mod_modules.rsi`
with its CC-BY-SA-3.0 notice. Suit part sprites remain in the existing `_Goobstation`, `_Mono` and `_WF`
modsuit RSIs. Additional module sprites and sounds are attributed in their RSI/audio `meta.json` and
`attributions.yml` files.

## Module checklist

Status is against the /tg/ catalogue at the revision the port was taken from. `Sprite` means the original module
icon is present in `mod_modules.rsi`. `Behaviour` is what runs in game, not just the prototype.

Only modules whose behaviour runs in game are listed here; every other /tg/ module is under
"Not implemented" below.

| /tg/ module | Prototype | Sprite | Behaviour | Reused system or new behaviour | Test |
|---|---|---|---|---|---|
| energy_shield | ModsuitModuleEnergyShield | yes | yes | new energy shield | manual |
| noslip | ModsuitModuleNoSlip | yes | yes | existing NoSlip | manual |
| power_kick | ModsuitModulePowerKick | yes | yes | existing throwing, stun and damage | manual |
| chameleon | ModsuitModuleChameleon | yes | yes | existing chameleon clothing | manual |
| plate_compression | ModsuitModulePlateCompression | yes | yes | new suit part scaling | manual |
| infiltrator | ModsuitModuleInfiltrator | yes | yes | new core program grants | manual |
| welding | ModsuitModuleWelding | yes | yes | existing EyeProtection | manual |
| welding/syndicate | ModsuitModuleSyndicateWelding | yes | yes | existing EyeProtection and FlashImmunity | manual |
| t_ray | ModsuitModuleTRay | yes | yes | existing TrayScanner | manual |
| magboot | ModsuitModuleMagboots | yes | yes | existing Magboots | manual |
| magboot/advanced | ModsuitModuleMagbootsAdvanced | yes | yes | existing Magboots | manual |
| tether | ModsuitModuleTether | yes | yes | new tether device | manual |
| rad_protection | ModsuitModuleRadProtection | yes | yes | existing radiation protection | manual |
| constructor | ModsuitModuleConstructor | yes | yes | existing RCD | manual |
| headprotector | ModsuitModuleHeadProtector | yes | yes | existing head protection | manual |
| mister | ModsuitModuleMister | yes | yes | existing spray | manual |
| mister/atmos | ModsuitModuleResinMister | yes | yes | existing spray | manual |
| storage | ModsuitModuleStorageCompact | yes | yes | existing Storage | `ModsuitModuleTest` |
| storage/large_capacity | ModsuitModuleStorageLarge | yes | yes | existing Storage | manual |
| storage/syndicate | ModsuitModuleStorageSyndicate | yes | yes | existing Storage | manual |
| storage/belt | ModsuitModuleStorageCase | yes | yes | existing Storage | manual |
| storage/bluespace | ModsuitModuleStorageBluespace | yes | yes | existing Storage | manual |
| jetpack | ModsuitModuleJetpack | yes | yes | existing Jetpack | manual |
| jetpack/advanced | ModsuitModuleJetpackAdvanced | yes | yes | existing Jetpack | manual |
| jump_jet | ModsuitModuleJumpJet | yes | yes | existing throwing | manual |
| mouthhole | ModsuitModuleMouthhole | yes | yes | existing IngestionBlocker | manual |
| emp_shield | ModsuitModuleEmpShield | yes | yes | existing EMP events | manual |
| emp_shield/advanced | ModsuitModuleEmpShieldAdvanced | yes | yes | existing EMP events | manual |
| flashlight | ModsuitModuleFlashlight | yes | yes | existing PointLight | manual |
| dispenser | ModsuitModuleBurgerDispenser | yes | yes | existing spawning | manual |
| longfall | ModsuitModuleLongfall | yes | yes | existing CE z-level fall damage | manual |
| thermal_regulator | ModsuitModuleThermalRegulator | yes | yes | existing server temperature | manual |
| dna_lock | ModsuitModuleDnaLock | yes | yes | new suit access check | manual |
| hat_stabilizer | ModsuitModuleHatStabilizer | yes | yes | existing item slots | manual |
| hat_stabilizer/syndicate | ModsuitModuleHatStabilizerElite | yes | yes | existing hat stabilizer | manual |
| signlang_radio | ModsuitModuleGloveTranslator | yes | yes | existing language translator | manual |
| joint_torsion | ModsuitModuleJointTorsion | yes | yes | existing movement and battery | manual |
| recycler | ModsuitModuleRecycler | yes | yes | existing storage and spawning | manual |
| recycler/donk | ModsuitModuleDonkRecycler | yes | yes | existing storage and spawning | manual |
| shock_absorber | ModsuitModuleShockAbsorber | yes | yes | existing stun events | manual |
| balloon | ModsuitModuleBalloonBlower | yes | yes | existing spawning | manual |
| paper_dispenser | ModsuitModulePaperDispenser | yes | yes | existing spawning | manual |
| stamp | ModsuitModuleStamper | yes | yes | existing stamp devices | manual |
| recycler/donk/safe | ModsuitModuleDonkSafeRecycler | yes | yes | existing storage and spawning | manual |
| health_analyzer | ModsuitModuleHealthAnalyzer | yes | yes | existing health analyzer | manual |
| injector | ModsuitModuleInjector | yes | yes | existing bloodstream | manual |
| criminalcapture/patienttransport | ModsuitModulePatientTransport | yes | yes | existing entity storage and pulling | manual |
| defibrillator | ModsuitModuleDefibrillator | yes | yes | existing defibrillator device | manual |
| defibrillator/combat | ModsuitModuleCombatDefibrillator | yes | yes | existing defibrillator device | manual |
| stealth | ModsuitModuleCloak | yes | yes | existing stealth | manual |
| emp_shield/pulse | ModsuitModuleEmpPulse | yes | yes | existing EMP pulse | manual |
| adrenaline_boost | ModsuitModuleAdrenalineBoost | yes | yes | existing reagents and status effects | manual |
| reagent_scanner | ModsuitModuleReagentScanner | yes | yes | existing reagent scanner | manual |
| reagent_scanner/advanced | ModsuitModuleReagentScanner | yes | yes | existing reagent scanner | manual |
| magnetic_harness | ModsuitModuleMagneticHarness | yes | yes | existing hands and prediction | manual |
| pepper_shoulders | ModsuitModulePepperShoulders | yes | yes | existing smoke | manual |
| holster | ModsuitModuleHolster | yes | yes | existing item slots | manual |
| megaphone | ModsuitModuleMegaphone | yes | yes | existing chat range expansion | manual |
| criminalcapture | ModsuitModuleCriminalCapture | yes | yes | existing entity storage | manual |
| projectile_dampener | ModsuitModuleProjectileDampener | yes | yes | existing projectile physics | manual |
| shooting_assistant | ModsuitModuleShootingAssistant | yes | yes | existing gun modifiers | manual |
| shove_blocker | ModsuitModuleBulwark | yes | yes | existing disarm events | manual |
| shove_blocker/locked | ModsuitModuleBulwarkLocked | yes | yes | existing bulwark | manual |
| bikehorn | ModsuitModuleBikeHorn | yes | yes | existing sound | manual |
| balloon/advanced | ModsuitModuleAdvancedBalloonBlower | yes | yes | existing spawning | manual |
| waddle | ModsuitModuleWaddle | yes | yes | existing WaddleWhenWorn | manual |
| mister/cleaner | ModsuitModuleJanitorMister | yes | yes | existing spray | manual |
| gps | ModsuitModuleGps | yes | yes | existing GPS | manual |
| clamp | ModsuitModuleClamp | yes | yes | existing pulling | manual |
| clamp/loader | ModsuitModuleBuiltinLoaderClamp | yes | yes | existing pulling | manual |
| drill | ModsuitModuleDrill | yes | yes | existing mining | manual |
| orebag | ModsuitModuleOreBag | yes | yes | existing storage | manual |
| hydraulic | ModsuitModuleBuiltinHydraulicArms | yes | yes | existing throwing | manual |
| magnet | ModsuitModuleBuiltinLoaderMagnet | yes | yes | existing pulling | manual |
| ash_accretion | ModsuitModuleBuiltinAshAccretion | yes | yes | existing weather and tile data | manual |
| sphere_transform | ModsuitModuleBuiltinSphereTransform | yes | yes | new sphere controller | manual |
| candela_spool | ModsuitModuleCandelaSpool | yes | yes | existing beacon spooling | manual |
| visor | ModsuitModuleVisorMedical and variants | yes | yes | existing HUDs | manual |
| visor/medhud | ModsuitModuleVisorMedical | yes | yes | existing HUD | manual |
| visor/diaghud | ModsuitModuleVisorDiagnostic | yes | yes | existing HUD | manual |
| visor/sechud | ModsuitModuleVisorSecurity | yes | yes | existing HUD | manual |
| visor/thermal | ModsuitModuleThermalVisor | yes | yes | existing thermal vision | manual |
| visor/night | ModsuitModuleNightVisor | yes | yes | existing night vision | manual |
| storage/holding | ModsuitModuleStorageHolding | yes | yes | existing bag of holding | manual |
| kinesis | ModsuitModuleKinesis | yes | yes | new kinesis controller | manual |

Variants that share one prototype are listed separately because the /tg/ catalogue does; the prototype column names
the shared item.

## Not implemented

These /tg/ modules have no working behaviour in Wolfgate, so they are not installable. Their icons stay in
`mod_modules.rsi` for a future port. The stub prototypes and inert components of `plasma_stabilizer`,
`quick_carry`, `quick_carry/advanced`, `quick_cuff`, `thread_ripper` and `anomaly_locked/antigrav` were removed
rather than left in the catalogue doing nothing.

| /tg/ module | Prototype | Sprite | Behaviour | Reused system or new behaviour | Test |
|---|---|---|---|---|---|
| energy_shield/wizard | - | yes | no | wizard-only variant not ported | - |
| anti_magic | — | yes | no | needs magic-nullification infrastructure | — |
| anti_magic/wizard | — | yes | no | wizard-only variant not ported | — |
| insignia | — | yes | no | cosmetic insignia system absent | — |
| flamethrower | — | yes | no | no handheld flamethrower system | — |
| demoralizer | — | yes | no | psi/voice system absent | — |
| medbeam | — | yes | no | no medical beam gun prototype or system | — |
| stealth/wraith | - | yes | no | wraith-only variant not ported | - |
| status_readout | — | yes | no | status readout HUD absent | — |
| flashlight/darkness | — | yes | no | flashdark light system absent | — |
| plasma_stabilizer | — | yes | no | no gas-ignition system for plasmamen; stub prototype removed | — |
| fishing_glove | — | yes | no | no fishing system | — |
| springlock | — | yes | no | no springlock suit mechanism | — |
| visor/rave | — | yes | no | rave overlay system absent | — |
| tanner | — | yes | no | tanning system absent | — |
| atrocinator | — | yes | no | no atrocinator system | — |
| quick_carry | — | yes | partial | no grab-stage or carry-speed hook; stub prototype removed | — |
| quick_carry/advanced | — | yes | partial | no grab-stage or carry-speed hook; stub prototype removed | — |
| organizer | — | yes | no | organ replacement surgery adapter absent | — |
| thread_ripper | — | yes | no | no clothing-ignoring surgery system; stub prototype removed | — |
| surgical_processor | — | yes | no | Shitmed surgery processor adapter absent | — |
| stealth/ninja | - | yes | no | ninja-only variant not ported | - |
| welding/camera_vision | — | yes | no | camera vision system absent | — |
| dispenser/ninja | — | yes | no | ninja-only dispenser variant not ported | — |
| hacker | - | yes | no | ninja-only variant not ported | - |
| weapon_recall | - | yes | no | ninja-only variant not ported | - |
| dna_lock/reinforced | - | yes | no | ninja-only variant not ported | - |
| status_readout/ninja | — | yes | no | ninja-only variant not ported | — |
| energy_net | — | yes | no | energy net structure absent | — |
| anomaly_locked/teleporter | — | yes | no | anomaly-locked teleporter absent | — |
| dispenser/mirage | — | yes | no | mirage grenade absent | — |
| active_sonar | — | yes | no | sonar overlay absent | — |
| anomaly_locked/antigrav | — | yes | no | no weightlessness hook on the wearer; stub prototype removed | — |
| quick_cuff | — | yes | partial | no cuff-duration hook; stub prototype removed | — |
| microwave_beam | — | yes | no | microwave cooking beam absent | — |
| selfcleaner | — | yes | no | perfumer/self-clean system absent | — |
| disposal_connector | — | yes | no | disposal connector absent | — |
| eradication_lock | — | yes | no | timeline infrastructure absent | — |
| rewinder | — | yes | no | timeline infrastructure absent | — |
| timestopper | — | yes | no | timeline infrastructure absent | — |
| timeline_jumper | — | yes | no | timeline infrastructure absent | — |
| tem | — | yes | no | timeline infrastructure absent | — |
| visor/meson | — | yes | no | meson vision absent | — |
| kinesis/prototype | - | yes | no | ninja variant not ported | - |
| kinesis/plus | - | yes | no | admin variant not ported | - |
| kinesis/admin | - | yes | no | admin variant not ported | - |
| pathfinder | — | yes | no | pathfinder pod system absent | — |

Two inbuilt prototypes were removed with them: `ModsuitModuleBuiltinAutoInjector` (no clothing auto-injector
system) and `ModsuitModuleBuiltinGrip` (no grab-strength hook).

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Modsuits/ModChameleonModuleSystem.cs`](ModChameleonModuleSystem.cs)
- [`Content.Server/_WF/Modsuits/ModInjectModuleSystem.cs`](ModInjectModuleSystem.cs)
- [`Content.Server/_WF/Modsuits/ModLoudspeakerChatSystem.cs`](ModLoudspeakerChatSystem.cs)
- [`Content.Server/_WF/Modsuits/ModMouthholeModuleSystem.cs`](ModMouthholeModuleSystem.cs)
- [`Content.Server/_WF/Modsuits/ModPatientTransportSystem.cs`](ModPatientTransportSystem.cs)
- [`Content.Server/_WF/Modsuits/ModRecyclerSystem.cs`](ModRecyclerSystem.cs)
- [`Content.Server/_WF/Modsuits/ModThermalRegulatorModuleSystem.cs`](ModThermalRegulatorModuleSystem.cs)

### Shared

- [`Content.Shared/_WF/Modsuits/Components/ModCoreComponent.cs`](../../../Content.Shared/_WF/Modsuits/Components/ModCoreComponent.cs)
- [`Content.Shared/_WF/Modsuits/Components/ModModuleComponent.cs`](../../../Content.Shared/_WF/Modsuits/Components/ModModuleComponent.cs)
- [`Content.Shared/_WF/Modsuits/Components/ModPartComponent.cs`](../../../Content.Shared/_WF/Modsuits/Components/ModPartComponent.cs)
- [`Content.Shared/_WF/Modsuits/Components/ModSealedComponentsComponent.cs`](../../../Content.Shared/_WF/Modsuits/Components/ModSealedComponentsComponent.cs)
- [`Content.Shared/_WF/Modsuits/Components/ModSuitComponent.cs`](../../../Content.Shared/_WF/Modsuits/Components/ModSuitComponent.cs)
- [`Content.Shared/_WF/Modsuits/ModSuitActionExtensions.cs`](../../../Content.Shared/_WF/Modsuits/ModSuitActionExtensions.cs)
- [`Content.Shared/_WF/Modsuits/ModSuitEvents.cs`](../../../Content.Shared/_WF/Modsuits/ModSuitEvents.cs)
- [`Content.Shared/_WF/Modsuits/ModSuitUi.cs`](../../../Content.Shared/_WF/Modsuits/ModSuitUi.cs)
- [`Content.Shared/_WF/Modsuits/ModThemePrototype.cs`](../../../Content.Shared/_WF/Modsuits/ModThemePrototype.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModArmorModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModArmorModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModClampSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModClampSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModCloakModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModCloakModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModDeviceModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModDeviceModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModDeviceSelectorSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModDeviceSelectorSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModDnaLockModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModDnaLockModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModEmpPulseModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModEmpPulseModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModEmpShieldModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModEmpShieldModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModEnergyShieldModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModEnergyShieldModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModGrantComponentsModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModGrantComponentsModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModGunModulesSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModGunModulesSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModHolsterModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModHolsterModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModJetpackModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModJetpackModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModKinesisComponents.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModKinesisComponents.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModKinesisSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModKinesisSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModLightModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModLightModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModLoaderMiningComponents.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModLoaderMiningComponents.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModLoaderMiningModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModLoaderMiningModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModLongfallModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModLongfallModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModLoudspeakerModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModLoudspeakerModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModMagneticHarnessSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModMagneticHarnessSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModModuleComponents.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModModuleComponents.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModOpenUiModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModOpenUiModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModPlateCompressionModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModPlateCompressionModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModPowerKickModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModPowerKickModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModSoundModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModSoundModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModSpeedModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModSpeedModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModSphereComponents.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModSphereComponents.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModSphereRollController.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModSphereRollController.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModSphereTransformModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModSphereTransformModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModStorageModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModStorageModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModStripComponentsModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModStripComponentsModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModTrayScannerModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModTrayScannerModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModUtilityModuleComponents.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModUtilityModuleComponents.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModUtilityModuleSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModUtilityModuleSystem.cs)
- [`Content.Shared/_WF/Modsuits/Modules/ModWearerEffectsSystem.cs`](../../../Content.Shared/_WF/Modsuits/Modules/ModWearerEffectsSystem.cs)
- [`Content.Shared/_WF/Modsuits/Systems/ModCoreSystem.cs`](../../../Content.Shared/_WF/Modsuits/Systems/ModCoreSystem.cs)
- [`Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Activation.cs`](../../../Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Activation.cs)
- [`Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.cs`](../../../Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.cs)
- [`Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Modules.cs`](../../../Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Modules.cs)
- [`Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Parts.cs`](../../../Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Parts.cs)
- [`Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Power.cs`](../../../Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Power.cs)
- [`Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Ui.cs`](../../../Content.Shared/_WF/Modsuits/Systems/ModSuitSystem.Ui.cs)

### Client

- [`Content.Client/_WF/Modsuits/ModAshCoatVisualsSystem.cs`](../../../Content.Client/_WF/Modsuits/ModAshCoatVisualsSystem.cs)
- [`Content.Client/_WF/Modsuits/ModDampenedProjectileVisualsSystem.cs`](../../../Content.Client/_WF/Modsuits/ModDampenedProjectileVisualsSystem.cs)
- [`Content.Client/_WF/Modsuits/ModDampenerFieldOverlay.cs`](../../../Content.Client/_WF/Modsuits/ModDampenerFieldOverlay.cs)
- [`Content.Client/_WF/Modsuits/ModDampenerFieldSystem.cs`](../../../Content.Client/_WF/Modsuits/ModDampenerFieldSystem.cs)
- [`Content.Client/_WF/Modsuits/ModHatVisualsSystem.cs`](../../../Content.Client/_WF/Modsuits/ModHatVisualsSystem.cs)
- [`Content.Client/_WF/Modsuits/ModKinesisInputSystem.cs`](../../../Content.Client/_WF/Modsuits/ModKinesisInputSystem.cs)
- [`Content.Client/_WF/Modsuits/ModMagneticHarnessPredictionSystem.cs`](../../../Content.Client/_WF/Modsuits/ModMagneticHarnessPredictionSystem.cs)
- [`Content.Client/_WF/Modsuits/ModSphereInputSystem.cs`](../../../Content.Client/_WF/Modsuits/ModSphereInputSystem.cs)
- [`Content.Client/_WF/Modsuits/ModSphereVisualsComponent.cs`](../../../Content.Client/_WF/Modsuits/ModSphereVisualsComponent.cs)
- [`Content.Client/_WF/Modsuits/ModSphereVisualsSystem.cs`](../../../Content.Client/_WF/Modsuits/ModSphereVisualsSystem.cs)
- [`Content.Client/_WF/Modsuits/ModSuitBoundUserInterface.cs`](../../../Content.Client/_WF/Modsuits/ModSuitBoundUserInterface.cs)
- [`Content.Client/_WF/Modsuits/ModSuitHardlightSystem.cs`](../../../Content.Client/_WF/Modsuits/ModSuitHardlightSystem.cs)
- [`Content.Client/_WF/Modsuits/ModSuitSealedVisualsComponent.cs`](../../../Content.Client/_WF/Modsuits/ModSuitSealedVisualsComponent.cs)
- [`Content.Client/_WF/Modsuits/ModSuitUiSystem.cs`](../../../Content.Client/_WF/Modsuits/ModSuitUiSystem.cs)
- [`Content.Client/_WF/Modsuits/ModSuitVisualizerSystem.cs`](../../../Content.Client/_WF/Modsuits/ModSuitVisualizerSystem.cs)
- [`Content.Client/_WF/Modsuits/ModSuitWindow.xaml`](../../../Content.Client/_WF/Modsuits/ModSuitWindow.xaml)
- [`Content.Client/_WF/Modsuits/ModSuitWindow.xaml.cs`](../../../Content.Client/_WF/Modsuits/ModSuitWindow.xaml.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Modsuits/ModSuitHardlightTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Modsuits/ModSuitHardlightTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Modsuits/ModsuitModulesContentTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Modsuits/ModsuitModulesContentTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Modsuits/ModsuitModuleTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Modsuits/ModsuitModuleTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Modsuits/ModSuitWindowTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Modsuits/ModSuitWindowTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Modsuits/actions.yml`](../../../Resources/Prototypes/_WF/Modsuits/actions.yml)
- [`Resources/Prototypes/_WF/Modsuits/balloons.yml`](../../../Resources/Prototypes/_WF/Modsuits/balloons.yml)
- [`Resources/Prototypes/_WF/Modsuits/contraband.yml`](../../../Resources/Prototypes/_WF/Modsuits/contraband.yml)
- [`Resources/Prototypes/_WF/Modsuits/guidebook.yml`](../../../Resources/Prototypes/_WF/Modsuits/guidebook.yml)
- [`Resources/Prototypes/_WF/Modsuits/lathe.yml`](../../../Resources/Prototypes/_WF/Modsuits/lathe.yml)
- [`Resources/Prototypes/_WF/Modsuits/mod_modules.yml`](../../../Resources/Prototypes/_WF/Modsuits/mod_modules.yml)
- [`Resources/Prototypes/_WF/Modsuits/modsuit_parts.yml`](../../../Resources/Prototypes/_WF/Modsuits/modsuit_parts.yml)
- [`Resources/Prototypes/_WF/Modsuits/modules_extra.yml`](../../../Resources/Prototypes/_WF/Modsuits/modules_extra.yml)
- [`Resources/Prototypes/_WF/Modsuits/research.yml`](../../../Resources/Prototypes/_WF/Modsuits/research.yml)
- [`Resources/Prototypes/_WF/Modsuits/shaders.yml`](../../../Resources/Prototypes/_WF/Modsuits/shaders.yml)
- [`Resources/Prototypes/_WF/Modsuits/tags.yml`](../../../Resources/Prototypes/_WF/Modsuits/tags.yml)
- [`Resources/Prototypes/_WF/Modsuits/themes.yml`](../../../Resources/Prototypes/_WF/Modsuits/themes.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Modsuits/modsuit.ftl`](../../../Resources/Locale/en-US/_WF/Modsuits/modsuit.ftl)

### Textures

- [`Resources/Textures/_WF/Modsuits/balloons.rsi/`](../../../Resources/Textures/_WF/Modsuits/balloons.rsi/)
- [`Resources/Textures/_WF/Modsuits/Hardlight/alert_amber.png`](../../../Resources/Textures/_WF/Modsuits/Hardlight/alert_amber.png)
- [`Resources/Textures/_WF/Modsuits/Hardlight/ATTRIBUTION.txt`](../../../Resources/Textures/_WF/Modsuits/Hardlight/ATTRIBUTION.txt)
- [`Resources/Textures/_WF/Modsuits/Hardlight/contractor_red.png`](../../../Resources/Textures/_WF/Modsuits/Hardlight/contractor_red.png)
- [`Resources/Textures/_WF/Modsuits/Hardlight/cosmic_blue.png`](../../../Resources/Textures/_WF/Modsuits/Hardlight/cosmic_blue.png)
- [`Resources/Textures/_WF/Modsuits/Hardlight/evil_green.png`](../../../Resources/Textures/_WF/Modsuits/Hardlight/evil_green.png)
- [`Resources/Textures/_WF/Modsuits/Hardlight/extrashield_green.png`](../../../Resources/Textures/_WF/Modsuits/Hardlight/extrashield_green.png)
- [`Resources/Textures/_WF/Modsuits/Hardlight/hazard_orange.png`](../../../Resources/Textures/_WF/Modsuits/Hardlight/hazard_orange.png)
- [`Resources/Textures/_WF/Modsuits/Hardlight/royal_purple.png`](../../../Resources/Textures/_WF/Modsuits/Hardlight/royal_purple.png)
- [`Resources/Textures/_WF/Modsuits/Hardlight/standard_blue.png`](../../../Resources/Textures/_WF/Modsuits/Hardlight/standard_blue.png)
- [`Resources/Textures/_WF/Modsuits/mod_modules.rsi/`](../../../Resources/Textures/_WF/Modsuits/mod_modules.rsi/)
- [`Resources/Textures/_WF/Modsuits/Shaders/mod_dampener_field.swsl`](../../../Resources/Textures/_WF/Modsuits/Shaders/mod_dampener_field.swsl)
- [`Resources/Textures/_WF/Modsuits/Shaders/mod_hardlight.swsl`](../../../Resources/Textures/_WF/Modsuits/Shaders/mod_hardlight.swsl)
- [`Resources/Textures/_WF/Modsuits/Shaders/mod_sphere.swsl`](../../../Resources/Textures/_WF/Modsuits/Shaders/mod_sphere.swsl)

### Audio

- [`Resources/Audio/_WF/Modsuits/attributions.yml`](../../../Resources/Audio/_WF/Modsuits/attributions.yml)
- [`Resources/Audio/_WF/Modsuits/ballin.ogg`](../../../Resources/Audio/_WF/Modsuits/ballin.ogg)
- [`Resources/Audio/_WF/Modsuits/balloon_pop.ogg`](../../../Resources/Audio/_WF/Modsuits/balloon_pop.ogg)
- [`Resources/Audio/_WF/Modsuits/ballout.ogg`](../../../Resources/Audio/_WF/Modsuits/ballout.ogg)
- [`Resources/Audio/_WF/Modsuits/ErrorBeep2.wav`](../../../Resources/Audio/_WF/Modsuits/ErrorBeep2.wav)
- [`Resources/Audio/_WF/Modsuits/gravhit.ogg`](../../../Resources/Audio/_WF/Modsuits/gravhit.ogg)
- [`Resources/Audio/_WF/Modsuits/loader_charge.ogg`](../../../Resources/Audio/_WF/Modsuits/loader_charge.ogg)
- [`Resources/Audio/_WF/Modsuits/loader_launch.ogg`](../../../Resources/Audio/_WF/Modsuits/loader_launch.ogg)
- [`Resources/Audio/_WF/Modsuits/MaxTempAlertCut.wav`](../../../Resources/Audio/_WF/Modsuits/MaxTempAlertCut.wav)
- [`Resources/Audio/_WF/Modsuits/rolling_ball_loop.ogg`](../../../Resources/Audio/_WF/Modsuits/rolling_ball_loop.ogg)
- [`Resources/Audio/_WF/Modsuits/sonic_jackhammer.ogg`](../../../Resources/Audio/_WF/Modsuits/sonic_jackhammer.ogg)

### Guidebook

- [`Resources/ServerInfo/_WF/Modsuits/Guidebook/Modsuits.xml`](../../../Resources/ServerInfo/_WF/Modsuits/Guidebook/Modsuits.xml)

## Non-modular edits

- [`Content.Client/Movement/Systems/JetpackSystem.cs`](../../../Content.Client/Movement/Systems/JetpackSystem.cs): MOD jetpack modules keep their own module sprite, which has no jetpack icon states
- [`Content.Server/Movement/Systems/JetpackSystem.cs`](../../Movement/Systems/JetpackSystem.cs): the ion jetpack module burns the suit core, not a gas tank
- [`Resources/Prototypes/_Goobstation/Entities/Clothing/Back/modsuit.yml`](../../../Resources/Prototypes/_Goobstation/Entities/Clothing/Back/modsuit.yml)
  - module and part containers
  - the module UI replaces the toggleable clothing UI
  - the native module system spawns and seals the suit parts
  - the core slot replaces the power cell slot
  - the module system owns the suit parts
  - a MOD core replaces the power cell
- [`Resources/Prototypes/_Goobstation/Entities/Clothing/Hands/modsuit.yml`](../../../Resources/Prototypes/_Goobstation/Entities/Clothing/Hands/modsuit.yml)
- [`Resources/Prototypes/_Goobstation/Entities/Clothing/Head/modsuit.yml`](../../../Resources/Prototypes/_Goobstation/Entities/Clothing/Head/modsuit.yml)
  - the hardlight shader replaces hiding the head layers
  - components granted while the native module system has the helmet sealed
  - head layers stay visible; the hardlight shader projects them over the sealed suit
- [`Resources/Prototypes/_Goobstation/Entities/Clothing/OuterClothing/modsuit.yml`](../../../Resources/Prototypes/_Goobstation/Entities/Clothing/OuterClothing/modsuit.yml): the native module system grants sealed components
- [`Resources/Prototypes/_Goobstation/Entities/Clothing/Shoes/modsuit.yml`](../../../Resources/Prototypes/_Goobstation/Entities/Clothing/Shoes/modsuit.yml)
- [`Resources/Prototypes/_Goobstation/Entities/Objects/Specific/Robotics/modsuit_parts.yml`](../../../Resources/Prototypes/_Goobstation/Entities/Objects/Specific/Robotics/modsuit_parts.yml): the standard core holds a swappable power cell
- [`Resources/Prototypes/_Mono/Entities/Clothing/Back/modsuit.yml`](../../../Resources/Prototypes/_Mono/Entities/Clothing/Back/modsuit.yml)
  - the native module system owns the suit parts
  - a MOD core replaces the power cell
  - the native module system sets its own sealed appearance
- [`Resources/Prototypes/_Mono/Entities/Clothing/Hands/modsuit.yml`](../../../Resources/Prototypes/_Mono/Entities/Clothing/Hands/modsuit.yml): the native module system sets its own sealed appearance
- [`Resources/Prototypes/_Mono/Entities/Clothing/OuterClothing/modsuit.yml`](../../../Resources/Prototypes/_Mono/Entities/Clothing/OuterClothing/modsuit.yml): the native module system sets its own sealed appearance
- [`Resources/Prototypes/_Mono/Entities/Clothing/Shoes/modsuit.yml`](../../../Resources/Prototypes/_Mono/Entities/Clothing/Shoes/modsuit.yml): the native module system sets its own sealed appearance
- [`Resources/Prototypes/Entities/Structures/Machines/lathe.yml`](../../../Resources/Prototypes/Entities/Structures/Machines/lathe.yml): module recipes

<!-- WOLFGATE-GENERATED END -->
