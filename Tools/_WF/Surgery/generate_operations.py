#!/usr/bin/env python3
"""Generate state-driven surgical operations from Wolfgate's legacy Shitmed surgery prototypes.

Each legacy surgery is a numbered chain of steps. The state-driven engine reproduces the same
effects through those step prototypes, but gates every step on the anatomical state it needs.
This script derives those states by walking each surgery's prerequisite chain and simulating the
marker components its steps add and remove.

Run from the repository root:

    python Tools/_WF/Surgery/generate_operations.py
"""

from __future__ import annotations

import re
from pathlib import Path

import yaml

ROOT = Path(__file__).resolve().parents[3]
PROTOTYPES = ROOT / "Resources" / "Prototypes"
OUT_OPS = PROTOTYPES / "_WF" / "Surgery" / "legacy_operations.yml"
OUT_LOC = ROOT / "Resources" / "Locale" / "en-US" / "_WF" / "Surgery" / "legacy_operations.ftl"

# Steps that the generic operations already cover. Generating them per surgery would clutter the radial
# with duplicates that do exactly the same thing.
GENERIC_STEPS = {
    "SurgeryStepOpenIncisionScalpel",
    "SurgeryStepCarefulIncisionScalpel",
    "SurgeryStepRetractSkin",
    "SurgeryStepClampBleeders",
    "SurgeryStepSawBones",
    "SurgeryStepClampInternalBleeders",
    "SurgeryStepCloseBones",
    "SurgeryStepMendRibcage",
    "SurgeryStepCloseIncision",
    "SurgeryStepSealTendWound",
}

# Surgeries owned by this module. They are hand-written operations already; generating a second operation from
# their generic effect step would bypass the state gate (and produced duplicate, mechanical-looking radial options).
GENERIC_SURGERY_IDS = {
    "WFSurgeryInciseSkin",
    "WFSurgeryRetractSkin",
    "WFSurgeryClampBleeders",
    "WFSurgeryUnclampBleeders",
    "WFSurgerySawBone",
    "WFSurgeryDrillBone",
    "WFSurgeryInciseOrgans",
    "WFSurgeryMendSkin",
    "WFSurgeryFixBones",
    "WFSurgeryWidenCavity",
    "WFSurgeryCloseCavity",
    "WFSurgeryUnscrewShell",
    "WFSurgeryOpenHatch",
    "WFSurgeryPrepareElectronics",
    "WFSurgeryUnwrenchEndoskeleton",
    "WFSurgeryWrenchEndoskeleton",
    "WFSurgeryScrewShell",
    "WFSurgeryOrganManipulation",
    "WFSurgeryImplantRemoval",
}

# The step's own tool components mapped to ghetto alternatives from /tg/station.
TOOL_QUALITY = {
    "Scalpel": ("Slicing", 2.25),
    "Retractor": ("Prying", 2.25),
    "Hemostat": ("Cutting", 1.67),
    "Tweezers": ("Prying", 2.85),
    "BoneSaw": ("Sawing", 1.0),
    "Drill": ("Screwing", 4.0),
    "Cautery": ("Welding", 1.5),
    "BoneSetter": ("Anchoring", 2.0),
}

MARKER_TO_STATE = {
    "IncisionOpen": "SkinCut",
    "SkinRetracted": "SkinOpen",
    "BleedersClamped": "VesselsClamped",
    "InternalBleedersClamped": "OrgansCut",
    "RibcageSawed": "BoneSawed",
    "RibcageOpen": "BoneSawed",
    "BoneDrilled": "BoneDrilled",
    "PlasticApplied": "PlasticApplied",
    "ProstheticUnsecured": "ProstheticUnsecured",
    "CavityWidened": "CavityWidened",
}

CLOSING = ("seal", "close", "mend", "cauter", "weld", "affix", "screw")
ORGAN_WORDS = ("organ", "brain", "heart", "liver", "lung", "stomach", "kidney", "eye", "ear")


class Loader(yaml.SafeLoader):
    pass


def _unknown(loader, tag_suffix, node):
    if isinstance(node, yaml.MappingNode):
        return {f"__tag__": tag_suffix, **loader.construct_mapping(node)}
    if isinstance(node, yaml.SequenceNode):
        return loader.construct_sequence(node)
    return loader.construct_scalar(node)


Loader.add_multi_constructor("!", _unknown)


def load_prototypes():
    entities = {}
    for path in PROTOTYPES.rglob("*.yml"):
        try:
            text = path.read_text(encoding="utf-8")
        except UnicodeDecodeError:
            continue
        if "SurgeryStep" not in text and "type: Surgery" not in text:
            continue
        try:
            data = yaml.load(text, Loader=Loader)
        except yaml.YAMLError:
            continue
        if not isinstance(data, list):
            continue
        for entry in data:
            if not isinstance(entry, dict) or entry.get("type") != "entity":
                continue
            ident = entry.get("id")
            if not ident:
                continue
            entities[ident] = entry
    return entities


_RESOLVED = {}


def resolve(ident, entities, seen=None):
    """Flatten prototype inheritance so steps defined on an abstract parent are visible."""
    if ident in _RESOLVED:
        return _RESOLVED[ident]
    seen = seen or set()
    if ident in seen:
        return {"id": ident, "components": []}
    seen.add(ident)

    entity = entities.get(ident)
    if entity is None:
        return {"id": ident, "components": []}

    merged = {"id": ident, "name": entity.get("name", ident), "description": entity.get("description"),
              "components": []}

    parents = entity.get("parent") or []
    if isinstance(parents, str):
        parents = [parents]

    by_type = {}
    order = []
    for parent_id in parents:
        parent = resolve(parent_id, entities, seen)
        for comp in parent["components"]:
            ctype = comp.get("type")
            if ctype is None:
                continue
            if ctype not in by_type:
                order.append(ctype)
            by_type[ctype] = comp
        if merged.get("name") in (None, ident) and parent.get("name") not in (None, parent_id):
            merged["name"] = parent["name"]
        if not merged.get("description") and parent.get("description"):
            merged["description"] = parent["description"]

    for comp in entity.get("components") or []:
        if not isinstance(comp, dict):
            continue
        ctype = comp.get("type")
        if ctype is None:
            continue
        if ctype not in by_type:
            order.append(ctype)
        by_type[ctype] = comp

    merged["components"] = [by_type[t] for t in order]
    _RESOLVED[ident] = merged
    return merged


def components_of(entity, component):
    for comp in entity.get("components") or []:
        if isinstance(comp, dict) and comp.get("type") == component:
            return comp
    return None


def surgical_steps(entities):
    steps = {}
    for ident in entities:
        entity = resolve(ident, entities)
        comp = components_of(entity, "SurgeryStep")
        if comp is None:
            continue
        steps[ident] = {
            "id": ident,
            "name": entity.get("name", ident),
            "add": [c.get("type") for c in comp.get("add") or [] if isinstance(c, dict)],
            "remove": [c.get("type") for c in comp.get("remove") or [] if isinstance(c, dict)],
            "tool": [c.get("type") for c in comp.get("tool") or [] if isinstance(c, dict)],
            "duration": float(comp.get("duration", 2)),
            "repeatable": components_of(entity, "SurgeryRepeatableStep") is not None,
            "entity": entity,
        }
    return steps


def surgeries(entities):
    result = {}
    for ident in entities:
        entity = resolve(ident, entities)
        comp = components_of(entity, "Surgery")
        if comp is None:
            continue
        result[ident] = {
            "id": ident,
            "name": entity.get("name", ident),
            "description": entity.get("description"),
            "steps": list(comp.get("steps") or []),
            "requirement": comp.get("requirement"),
            "entity": entity,
        }
    return result


def chain_for(surgery_id, all_surgeries, seen=None):
    seen = seen or []
    if surgery_id in seen:
        return []
    seen.append(surgery_id)
    surgery = all_surgeries.get(surgery_id)
    if surgery is None:
        return []
    chain = []
    if surgery["requirement"]:
        chain.extend(chain_for(surgery["requirement"], all_surgeries, seen))
    chain.extend(surgery["steps"])
    return chain


def simulate(step_ids, all_steps):
    """Marker components present after running a list of steps from an intact part."""
    markers = set()
    for step_id in step_ids:
        step = all_steps.get(step_id)
        if step is None:
            continue
        for removed in step["remove"]:
            markers.discard(removed)
        for added in step["add"]:
            markers.add(added)
    return markers


def state_requirements(surgery, step, markers):
    """Translate the simulated marker set into the operation's state gate."""
    step_id = step["id"]
    lower = step_id.lower()
    closing = any(word in lower for word in CLOSING)
    organ = any(word in lower for word in ORGAN_WORDS)

    has_skin_cut = "IncisionOpen" in markers
    has_skin_open = "SkinRetracted" in markers
    has_sawed = "RibcageSawed" in markers or "RibcageOpen" in markers
    has_drilled = "BoneDrilled" in markers
    has_organs = "InternalBleedersClamped" in markers
    has_cavity = "CavityWidened" in markers

    required = []
    any_required = []
    blocked = []

    surface = any(word in lower for word in ("tend", "repairbrute", "repairburn", "careful"))

    if closing:
        any_required.append("AllSkin")
    else:
        if has_skin_open:
            required.append("SkinOpen")
        elif has_skin_cut:
            required.append("SkinCut")

        # Surface work only needs the skin to be cut or open, like /tg/station's tend wounds.
        if surface and (has_skin_cut or has_skin_open):
            any_required.append("AllSkin")
            if "SkinCut" in required:
                required.remove("SkinCut")
            if "SkinOpen" in required:
                required.remove("SkinOpen")

        if has_sawed:
            required.append("BoneSawed")
        if has_drilled:
            required.append("BoneDrilled")
        if has_organs and organ:
            required.append("OrgansCut")
        if has_cavity:
            required.append("CavityWidened")

    # Internal bleeding is inside the cavity; do not offer it beside the surface clamp-bleeders operation.
    if step_id == "WFSurgeryStepStopInternalBleeding" and "OrgansCut" not in required:
        required.append("OrgansCut")

    # Repeat prevention for state-changing procedure-specific steps.
    if not step["repeatable"]:
        for added in step["add"]:
            translated = MARKER_TO_STATE.get(added)
            if translated and translated in (
                "OrgansCut",
                "BoneDrilled",
                "PlasticApplied",
                "CavityWidened",
            ):
                blocked.append(translated)

    def mask(names):
        mapping = {
            "SkinCut": ["SkinCut"],
            "SkinOpen": ["SkinOpen"],
            "AllSkin": ["SkinCut", "SkinOpen"],
            "BoneSawed": ["BoneSawed"],
            "BoneDrilled": ["BoneDrilled"],
            "OrgansCut": ["OrgansCut"],
            "CavityWidened": ["CavityWidened"],
        }
        result = []
        for name in names:
            for enum_name in mapping[name]:
                if enum_name not in result:
                    result.append(enum_name)
        return result

    return (
        mask(required) if required else None,
        mask(any_required) if any_required else None,
        mask(blocked) if blocked else None,
    )


def tools_for(step):
    """YAML tool entries: the step's own components first, then quality substitutes."""
    entries = []
    for tool in step["tool"]:
        if tool in ("BodyPart", "Organ"):
            continue
        entries.append({"component": tool, "speed": 1.0})
        if tool in TOOL_QUALITY:
            quality, speed = TOOL_QUALITY[tool]
            entries.append({"quality": quality, "speed": speed})

    if step["id"] in ("SurgeryStepInsertFeature", "SurgeryStepInsertOrgan"):
        entries.insert(0, {"component": "BodyPart" if "Feature" in step["id"] else "Organ", "speed": 1.0})

    if not entries:
        entries.append({"anyItem": True, "speed": 1.0})

    return entries


def organ_components(surgery):
    comp = components_of(surgery["entity"], "SurgeryOrganCondition")
    if comp is None:
        return []
    names = []
    for entry in comp.get("organ") or []:
        if isinstance(entry, dict) and entry.get("type"):
            names.append(entry["type"])
    return names


ICON_BY_STEP = {
    "SurgeryStepOpenIncisionScalpel": "make_incision",
    "SurgeryStepCarefulIncisionScalpel": "make_incision",
    "SurgeryStepRetractSkin": "retract_skin",
    "SurgeryStepClampBleeders": "clamp_bleeders",
    "SurgeryStepSawBones": "saw_bones",
    "SurgeryStepPriseOpenBones": "saw_bones",
    "SurgeryStepClampInternalBleeders": "incise_organs",
    "SurgeryStepCloseIncision": "mend_incision",
    "SurgeryStepSealTendWound": "mend_incision",
    "SurgeryStepSealWounds": "mend_incision",
    "SurgeryStepSealOrganWound": "mend_incision",
    "SurgeryStepRemoveOrgan": "base",
    "SurgeryStepInsertOrgan": "base",
    "SurgeryStepInsertFeature": "base",
    "SurgeryStepRemoveFeature": "amputate",
    "SurgeryStepSawFeature": "saw_bones",
    "SurgeryStepRepairBruteTissue": "base",
    "SurgeryStepRepairBurnTissue": "base",
    "SurgeryStepOpenOrganSlot": "base",
}


def icon_for_step(step_id):
    if step_id in ICON_BY_STEP:
        return ICON_BY_STEP[step_id]
    lowered = step_id.lower()
    if "incision" in lowered or "incise_skin" in lowered:
        return "make_incision"
    if "retract" in lowered:
        return "retract_skin"
    if "clamp" in lowered and "bleed" in lowered:
        return "clamp_bleeders"
    if "saw" in lowered or "bone" in lowered and "fix" not in lowered:
        return "saw_bones"
    if "drill" in lowered:
        return "drill_bones"
    if "organ" in lowered:
        return "base"
    if "seal" in lowered or "close" in lowered or "mend" in lowered:
        return "mend_incision"
    if "unscrew" in lowered:
        return "unscrew_shell"
    if "open_hatch" in lowered:
        return "open_hatch"
    if "electronics" in lowered:
        return "prepare_electronics"
    if "unwrench" in lowered:
        return "unwrench_endoskeleton"
    if "wrench" in lowered:
        return "wrench_endoskeleton"
    if "screw" in lowered:
        return "screw_shell"
    if "amput" in lowered or "remove_part" in lowered:
        return "amputate"
    return "base"


def location_id(value):
    return re.sub(r"[^a-z0-9-]+", "-", value.lower()).strip("-")


def main():
    entities = load_prototypes()
    all_steps = surgical_steps(entities)
    all_surgeries = surgeries(entities)

    operations = []
    locale = []

    for surgery_id in sorted(all_surgeries):
        if surgery_id in GENERIC_SURGERY_IDS:
            continue

        surgery = all_surgeries[surgery_id]
        chain = chain_for(surgery_id, all_surgeries)
        if not chain:
            continue

        part_condition = components_of(surgery["entity"], "SurgeryPartCondition")
        part_type = None
        if part_condition and not part_condition.get("inverse"):
            part_type = part_condition.get("part")

        organ_types = organ_components(surgery)
        seen_steps = set()

        for index, step_id in enumerate(surgery["steps"]):
            if step_id in GENERIC_STEPS or step_id in seen_steps:
                continue
            seen_steps.add(step_id)

            step = all_steps.get(step_id)
            if step is None:
                continue

            markers = simulate(chain[: chain.index(step_id)], all_steps)
            required, any_required, blocked = state_requirements(surgery, step, markers)

            op_id = f"WFOp{surgery_id}_{step_id}".replace("__", "_")
            name = step["name"] or surgery["name"]
            lower = step_id.lower()
            flags = []
            if "Synth" in surgery_id or "Chassis" in surgery_id:
                flags.append("Mechanical")
            if any(word in lower for word in CLOSING):
                flags.append("PriorityNextStep")

            kind = "Default"
            if step_id == "SurgeryStepRemoveOrgan":
                kind = "OrganManipulation"
            elif step_id == "SurgeryStepInsertOrgan":
                kind = "OrganManipulation"

            operation = {
                "type": "surgicalOperation",
                "id": op_id,
                "icon": {
                    "sprite": "_WF/Surgery/RadialIcons.rsi",
                    "state": icon_for_step(step_id),
                },
                "name": f"wf-surgery-op-{location_id(op_id)}",
                "description": f"wf-surgery-op-{location_id(op_id)}-desc",
                "surgery": surgery_id,
                "step": step_id,
                "kind": kind,
                "priority": 10 + index,
                "tools": tools_for(step),
            }
            if required:
                operation["requiredStates"] = required
            if any_required:
                operation["anyRequiredStates"] = any_required
            if blocked:
                operation["blockedStates"] = blocked
            if part_type:
                operation["partType"] = part_type
                zones = {
                    "Torso": ["Torso", "Groin"],
                    "Head": ["Head"],
                    "Arm": ["LeftArm", "RightArm"],
                    "Hand": ["LeftHand", "RightHand"],
                    "Leg": ["LeftLeg", "RightLeg"],
                    "Foot": ["LeftFoot", "RightFoot"],
                }.get(part_type)
                if zones:
                    operation["zones"] = zones
            if flags:
                operation["flags"] = flags
            if organ_types:
                operation["organComponents"] = organ_types
            if step["repeatable"]:
                operation["flags"] = operation.get("flags", []) + ["Looping"]

            operations.append(operation)
            locale.append(f"wf-surgery-op-{location_id(op_id)} = {name}")
            desc = f"Perform {name.lower()} as part of {surgery['name'].lower()}."
            locale.append(f"wf-surgery-op-{location_id(op_id)}-desc = {desc}")

    OUT_OPS.parent.mkdir(parents=True, exist_ok=True)
    OUT_LOC.parent.mkdir(parents=True, exist_ok=True)

    header = (
        "# Generated by Tools/_WF/Surgery/generate_operations.py from the legacy surgery and step\n"
        "# prototypes. Do not edit by hand; rerun the script after changing a legacy surgery.\n\n"
    )
    OUT_OPS.write_text(header + yaml.safe_dump(operations, sort_keys=False, allow_unicode=True), encoding="utf-8")
    OUT_LOC.write_text("\n".join(locale) + "\n", encoding="utf-8")

    print(f"{len(operations)} operations written to {OUT_OPS}")
    print(f"{len(locale)} locale lines written to {OUT_LOC}")


if __name__ == "__main__":
    main()
