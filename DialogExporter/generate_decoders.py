#!/usr/bin/env python3
"""
Code generator: read each decompiled S*.cs step class, extract its Read method
and field types, then emit a C# StepDispatcher_Generated.cs file with one
decoder method per step type. The decoder consumes exactly the bytes the
game's writer emits.

Approach: regex-parse decompiled source — fragile but the decompiler output is
extremely regular. Inheritance is tracked so subclasses chain through the
parent's field reads first.
"""
import os
import re
import sys
from pathlib import Path

DECOMPILE_ROOTS = [
    Path(r"C:/temp/decompile/full_tg/Awaken.TG.Main.Stories.Steps"),
    Path(r"C:/temp/decompile/full_tg/Awaken.TG.Main.Stories.Quests"),
    Path(r"C:/temp/decompile/full_tg/Awaken.TG.Main.Stories.Quests.Objectives"),
    Path(r"C:/temp/decompile/full_tg/Awaken.TG.Main.Tutorials"),
]

# Where to scan for enum/struct definitions referenced by step fields:
ENUM_SCAN_ROOT = Path(r"C:/temp/decompile/full_tg")

# Hand-rolled struct decoders (types where Read overloads exist in StoryReader
# or layouts complex enough we want explicit handling).
EXTRA_STRUCT_READS = {
    "Vector3":            ["r.ReadFloat();", "r.ReadFloat();", "r.ReadFloat();"],
    "Vector2":            ["r.ReadFloat();", "r.ReadFloat();"],
    "Quaternion":         ["r.ReadFloat();", "r.ReadFloat();", "r.ReadFloat();", "r.ReadFloat();"],
    "Color":              ["r.ReadFloat();", "r.ReadFloat();", "r.ReadFloat();", "r.ReadFloat();"],
    # ConditionalInt is { bool enable; int value }. There's no specific
    # Read(ref ConditionalInt) overload so it falls through to the generic
    # Read<T>() which copies sizeof(T) bytes. With C# default sequential
    # layout, the int field aligns on a 4-byte boundary → 8 bytes total
    # (bool + 3 bytes padding + int).
    "ConditionalInt":     ["r.ReadBool();", "r.ReadByte();", "r.ReadByte();", "r.ReadByte();", "r.ReadInt32();"],
    "ARTimeSpan":         ["r.ReadInt64();"],          # 8 bytes ticks
    "DuelistSettings":    ["r.ReadBool();", "r.ReadBool();", "r.ReadBool();", "r.ReadBool();",
                            "r.ReadBool();", "r.ReadARAssetRef();"],
    # DuelArenaReferenceData: { ArenaDataSource(byte) arenaDataSource;
    #                           TemplateReference arenaFaction; SceneReference sceneRef;
    #                           LocationReference locationRef }
    # Has explicit Read overload — field-by-field, no padding.
    # LocationReference inlined as 4 separate statements (byte tag + 3 string arrays).
    "DuelArenaReferenceData": ["r.ReadByte();", "r.ReadTemplateRefGuid();", "r.ReadSceneReference();",
                                "r.ReadByte();", "r.ReadStringArray();",
                                "r.ReadTemplateRefArray();", "r.ReadActorRefArray();"],
    # ActionData: { Action action; int index; bool toggle }. No specific Read
    # overload → unmanaged sizeof = 12 (4 + 4 + 1 + 3 padding to 4-byte alignment).
    "ActionData":         ["r.ReadInt32();", "r.ReadInt32();", "r.ReadBool();",
                            "r.ReadByte();", "r.ReadByte();", "r.ReadByte();"],
    "EmotionData":        ["r.ReadDouble();", "r.ReadString();", "r.ReadInt32();", "r.ReadInt32();", "r.ReadFloat();"],
    "StoryBookmark":      ["r.ReadTemplateRefGuid();", "r.ReadString();"],
    # StatValue: { float value; ValueType type } — both unmanaged, both 4-aligned, no padding.
    "StatValue":          ["r.ReadFloat();", "r.ReadInt32();"],
    # SavedAnimatorParameter: { AnimatorControllerParameterType type (int); bool boolValue;
    #                            float floatValue; int intValue }
    # No specific Read overload → unmanaged Read<T>() reads sizeof(T) = 16 bytes
    # (4 + 1 + 3 padding + 4 + 4 with default sequential layout).
    "SavedAnimatorParameter": ["r.ReadInt32();", "r.ReadBool();",
                                "r.ReadByte();", "r.ReadByte();", "r.ReadByte();",
                                "r.ReadFloat();", "r.ReadInt32();"],
    # CharacterPresetData: { bool random; int gender; int skinColor; ...15 ints total }
    # No Read overload → generic Read<T>() copies 64 bytes
    # (1 bool + 3 padding to 4-byte align + 15 × 4-byte ints).
    "CharacterPresetData": (
        ["r.ReadBool();", "r.ReadByte();", "r.ReadByte();", "r.ReadByte();"]
        + ["r.ReadInt32();"] * 15
    ),
    # AliveAudioContainer: class with audioType (CharacterType=int) + 11 EventReference
    # fields. Has explicit Read overload — field-by-field, no padding.
    "AliveAudioContainer": (
        ["r.ReadInt32();"]
        + ["r.ReadFmodGuid();"] * 11
    ),
}

OUTPUT_FILE = Path(r"C:/Users/quilo/source/DialogExporter/StepDispatcher_Generated.cs")


# Map field type → reader method (returning a value we discard, just to advance the cursor)
# Specific overloads in StoryStreamReader.cs.
TYPE_READS = {
    "bool": "r.ReadBool();",
    "byte": "r.ReadByte();",
    "sbyte": "r.ReadByte();",
    "short": "r.ReadInt16();",
    "ushort": "r.ReadUInt16();",
    "int": "r.ReadInt32();",
    "uint": "r.ReadUInt32();",
    "long": "r.ReadInt64();",
    "ulong": "r.ReadUInt64();",
    "float": "r.ReadFloat();",
    "double": "r.ReadDouble();",
    "string": "r.ReadString();",
    "string[]": "r.ReadStringArray();",
    # game-specific
    "LightLocString": "r.ReadLightLocString();",
    "LocString": "r.ReadLocString();",
    "ActorRef": "r.ReadActorRefGuid();",
    "ActorRef[]": "r.ReadActorRefArray();",
    "ActorStateRef": "r.ReadActorStateName();",
    "TemplateReference": "r.ReadTemplateRefGuid();",
    "TemplateReference[]": "r.ReadTemplateRefArray();",
    "RichEnumReference": "r.ReadRichEnumRefStr();",
    "RichEnumReference[]": "r.ReadRichEnumRefArray();",
    "LocationReference": "r.ReadLocationReference();",
    "SceneReference": "r.ReadSceneReference();",
    "SpriteReference": "r.ReadSpriteReference();",
    "ShareableSpriteReference": "r.ReadShareableSpriteReference();",
    "ARAssetReference": "r.ReadARAssetRef();",
    "ShareableARAssetReference": "r.ReadShareableARAssetReference();",
    "EventReference": "r.ReadFmodGuid();",
    "IntRange": "r.ReadIntRange();",
    "BaseAudioSource": "r.ReadBaseAudioSource();",
    "Variable": "r.ReadVariable();",
    "VariableDefine": "r.ReadVariableDefine();",
    "VariableReferenceDefine": "r.ReadVariableReferenceDefine();",
    "Context": "r.ReadContext();",
    "Context[]": "ReadCtxArray(r);",  # we'll define a helper
    "ItemSpawningData": "r.ReadItemSpawningData();",
    "ItemSpawningData[]": "r.ReadItemSpawningDataArray();",
    "EmotionData": "r.ReadEmotionData();",
    "EmotionData[]": "r.ReadEmotionDataArray();",
    "StoryBookmark": "r.ReadStoryBookmark();",
    "StoryChapter": "r.ReadChapterIndex();",
    "RuntimeChoice": "r.ReadRuntimeChoice();",
    "ChoiceIcon": "r.ReadByte();",          # enum : byte
    "TimeSpans": "r.ReadInt32();",          # default-int enum
    "ContextType": "r.ReadByte();",         # explicit byte enum
    "GUID": "r.ReadFmodGuid();",            # FMOD.GUID = 16 bytes
    "JournalGuid": "r.ReadJournalGuid();",
    "LoadingHandle": "r.ReadLoadingHandle();",
    "RichLabelUsage": "r.ReadRichLabelUsage();",
    "RichLabelUsageEntry": "r.ReadRichLabelUsageEntry();",
}

# Some types we treat as scalar primitives even though they're enums / structs.
# Anything else that looks like an enum or a small struct we read as int (4 bytes).
# This is best-effort — failures will surface at runtime as misalignment.
GAME_ENUMS_DEFAULT_INT = {
    "ChoiceIcon", "TimeSpans", "VariableType", "EmotionState",
    "ExpressionHandler", "ExpressionComponent.ExpressionHandler",
    "VCutsceneDialogueControlled.Action", "ARMath.RoundingType",
    "Activity", "Activity.End", "QuestState", "QuestObjectiveState",
    "MapChange", "MapChange.Direction",
    "Stat.Type", "TutorialKeys",
    "DialogueType",
    # Third-party SDK enums (default-int):
    "GAProgressionStatus",       # GameAnalyticsSDK.Net.EGAProgressionStatus
    "EGAProgressionStatus",
}

# Skip the subclass extras for these — their Read calls base.Read (handled separately).


def find_cs_files():
    out = []
    for root in DECOMPILE_ROOTS:
        if not root.exists():
            continue
        for p in root.rglob("S*.cs"):
            if p.name.startswith("SEditor"):
                continue
            out.append(p)
    return out


CLASS_RE = re.compile(r'public\s+(?:abstract\s+|sealed\s+)?class\s+(\w+)\s*(?::\s*([^{,]+(?:,\s*[^{,]+)*))?\s*[\n{]')
TYPE_RE = re.compile(r'public\s+override\s+byte\s+Type\s*=>\s*(\d+)\s*;')
# Allow verbatim identifiers (`@default`, `@event`, etc.) — strip the @ when
# normalising the captured field name.
FIELD_RE = re.compile(r'^\s*public\s+([A-Za-z_][\w<>?\[\]\.]*)\s+@?(\w+)\s*(?:=\s*[^;]+)?;', re.MULTILINE)
READ_RE = re.compile(
    r'public\s+override\s+void\s+Read\s*\(\s*StoryReader\s+reader\s*\)\s*\{([\s\S]*?)\n\s*\}',
    re.MULTILINE,
)
READ_CALL_RE = re.compile(r'reader\.Read\s*\(\s*ref\s+@?(\w+)\s*\)\s*;')


def parse_step_file(path):
    text = path.read_text(encoding='utf-8', errors='replace')

    # Extract class header
    m = CLASS_RE.search(text)
    if not m:
        return None
    class_name = m.group(1)
    bases = (m.group(2) or "").split(',')
    parent = None
    for b in bases:
        b = b.strip()
        # skip interfaces (start with I and uppercase next letter)
        if b and b[0] == 'I' and len(b) > 1 and b[1].isupper():
            continue
        parent = b
        break

    # Extract Type byte if present
    type_match = TYPE_RE.search(text)
    type_byte = int(type_match.group(1)) if type_match else None

    # Extract fields
    fields = {}
    for f in FIELD_RE.finditer(text):
        ftype, fname = f.group(1).strip(), f.group(2).strip()
        # Skip property setters etc — FIELD_RE only matches non-property fields
        fields[fname] = ftype

    # Extract Read body
    rm = READ_RE.search(text)
    reads = []
    has_base_call = False
    if rm:
        body = rm.group(1)
        if 'base.Read' in body:
            has_base_call = True
        for c in READ_CALL_RE.finditer(body):
            reads.append(c.group(1))

    return {
        'name': class_name,
        'parent': parent,
        'type_byte': type_byte,
        'fields': fields,
        'reads': reads,
        'has_base_call': has_base_call,
        'has_read_method': rm is not None,
        'path': path,
    }


# Cache for enum/struct lookups
_type_cache = {}

ENUM_DECL_RE = re.compile(r'public\s+enum\s+(\w+)\s*(?::\s*(\w+))?\s*[\n{]')
STRUCT_DECL_RE = re.compile(r'public\s+(?:readonly\s+)?struct\s+(\w+)')


def find_nested_enum_in_file(path, name):
    """Look for `enum NAME` declared inside the same .cs file as the
    referencing class. Returns underlying type or None.
    Nested enums shadow same-named top-level enums in C#."""
    try:
        text = path.read_text(encoding='utf-8', errors='replace')
    except Exception:
        return None
    pattern = re.compile(rf'\benum\s+{re.escape(name)}\b\s*(?::\s*(\w+))?')
    m = pattern.search(text)
    if m:
        return m.group(1) or "int"
    return None


def find_type_def(name):
    """Locate the enum/struct decl for `name` anywhere under ENUM_SCAN_ROOT.
    Returns ("enum", underlying_type) or ("struct", path) or None."""
    if name in _type_cache:
        return _type_cache[name]
    # Quick filename-based scan first
    cand_paths = list(ENUM_SCAN_ROOT.rglob(f"{name}.cs"))
    for p in cand_paths:
        try:
            text = p.read_text(encoding='utf-8', errors='replace')
        except Exception:
            continue
        for m in ENUM_DECL_RE.finditer(text):
            if m.group(1) == name:
                underlying = m.group(2) or "int"
                _type_cache[name] = ("enum", underlying)
                return _type_cache[name]
        m2 = STRUCT_DECL_RE.search(text)
        if m2 and m2.group(1) == name:
            _type_cache[name] = ("struct", p)
            return _type_cache[name]
    # Slow path: nested enums live inside other class files. Grep the whole
    # decompile dir for "enum NAME" declarations.
    pattern = re.compile(rf'\benum\s+{re.escape(name)}\b\s*(?::\s*(\w+))?')
    for p in ENUM_SCAN_ROOT.rglob("*.cs"):
        try:
            text = p.read_text(encoding='utf-8', errors='replace')
        except Exception:
            continue
        m = pattern.search(text)
        if m:
            underlying = m.group(1) or "int"
            _type_cache[name] = ("enum", underlying)
            return _type_cache[name]
    _type_cache[name] = None
    return None


def field_type_to_reader(ftype, declaring_file=None):
    """Resolve a field type to a reader call. Returns the reader expression
    (as a string) or None if unsupported. If declaring_file is provided,
    nested enums declared in that file shadow same-named top-level types."""
    ftype = ftype.strip()
    # Strip nullable
    if ftype.endswith("?"):
        ftype = ftype[:-1]
    if ftype in TYPE_READS:
        return TYPE_READS[ftype]
    # Hand-rolled struct expansions
    if ftype in EXTRA_STRUCT_READS:
        return "{ " + " ".join(f"_ = {c}" for c in EXTRA_STRUCT_READS[ftype]) + " }"
    # Enums we know about
    if ftype in GAME_ENUMS_DEFAULT_INT:
        return "r.ReadInt32();"
    # Ends with [] — array of game type
    if ftype.endswith('[]'):
        elem = ftype[:-2]
        if ftype in TYPE_READS:
            return TYPE_READS[ftype]
        elem_reader = field_type_to_reader(elem)
        if elem_reader:
            # Strip the leading `_ = ` if any so we can build a clean inner body
            inner = elem_reader
            if inner.startswith("{"):
                # Multi-statement block already wrapped in braces
                inner_body = inner.strip()[1:-1].strip()  # remove outer {}
            else:
                inner_body = "_ = " + inner
            return f"{{ int n = r.ReadInt32(); for (int i = 0; i < n; i++) {{ {inner_body} }} }}"
        return None
    # FMOD GUID is referenced as plain GUID inside Stories.Steps
    if ftype == "FMOD.GUID":
        return "r.ReadFmodGuid();"

    # Last resort: look up the type in the decompile tree.
    bare = ftype.split('.')[-1]   # strip qualifying namespace/class prefix
    # First, prefer a nested enum in the same .cs file (C# scoping rules).
    if declaring_file is not None:
        nested = find_nested_enum_in_file(declaring_file, bare)
        if nested is not None:
            sizes = {
                "byte": "r.ReadByte();", "sbyte": "r.ReadByte();",
                "short": "r.ReadInt16();", "ushort": "r.ReadUInt16();",
                "int": "r.ReadInt32();", "uint": "r.ReadUInt32();",
                "long": "r.ReadInt64();", "ulong": "r.ReadUInt64();",
            }
            return sizes.get(nested, "r.ReadInt32();")
    info = find_type_def(bare)
    if info is not None:
        kind, payload = info
        if kind == "enum":
            underlying = payload
            sizes = {
                "byte": "r.ReadByte();", "sbyte": "r.ReadByte();",
                "short": "r.ReadInt16();", "ushort": "r.ReadUInt16();",
                "int": "r.ReadInt32();", "uint": "r.ReadUInt32();",
                "long": "r.ReadInt64();", "ulong": "r.ReadUInt64();",
            }
            return sizes.get(underlying, "r.ReadInt32();")
    # Unity enums we know are int-sized
    if ftype in ("AnimatorControllerParameterType",):
        return "r.ReadInt32();"
    return None


def main():
    files = find_cs_files()
    parsed = {}
    for p in files:
        info = parse_step_file(p)
        if info:
            parsed[info['name']] = info

    # Walk inheritance to flatten reads.
    def flat_reads(name):
        info = parsed.get(name)
        if info is None: return [], "missing"
        parent = info['parent']
        out = []
        # If THIS class has no Read override at all, it inherits the parent's
        # Read entirely (and base.Read implicitly walks up). If it DOES have a
        # Read override AND that override calls base.Read, we also chain.
        # Either way, we must walk up the chain when there's no override OR
        # when the override defers to the parent.
        chain_parent = (
            (not info['has_read_method'])
            or (info['has_base_call'] and parent and parent != "StoryStep")
        )
        if chain_parent and parent and parent != "StoryStep":
            parent_reads, status = flat_reads(parent)
            if status != "ok": return [], status
            out.extend(parent_reads)
        # 'StoryStep.Read' just reads conditions[]; we handle that at the
        # dispatcher level (one call before Decode), so don't repeat here.
        for fname in info['reads']:
            ftype = info['fields'].get(fname)
            if ftype is None:
                # Could be inherited. Walk up the parent chain.
                p = parent
                while p:
                    pinfo = parsed.get(p)
                    if pinfo is None: break
                    if fname in pinfo['fields']:
                        ftype = pinfo['fields'][fname]
                        break
                    p = pinfo['parent']
            if ftype is None:
                return [], f"unknown_field:{name}.{fname}"
            # The declaring file is the file where this field is defined; if
            # the field is inherited, walk up to find the file that declared
            # it. We approximate by using the immediate class file (info['path'])
            # when the field belongs to this class, and the parent's file
            # otherwise.
            declaring_file = info['path']
            if fname not in info['fields']:
                p = parent
                while p:
                    pinfo = parsed.get(p)
                    if pinfo is None: break
                    if fname in pinfo['fields']:
                        declaring_file = pinfo['path']
                        break
                    p = pinfo['parent']
            reader = field_type_to_reader(ftype, declaring_file)
            if reader is None:
                return [], f"unknown_type:{ftype} (field {name}.{fname})"
            out.append((fname, ftype, reader))
        return out, "ok"

    typed_steps = {}
    skipped = []
    for name, info in parsed.items():
        if info['type_byte'] is None:
            continue
        reads, status = flat_reads(name)
        if status != "ok":
            skipped.append((info['type_byte'], name, status))
            continue
        typed_steps[info['type_byte']] = (name, reads)

    # Emit C#
    out = []
    out.append("// AUTO-GENERATED by generate_decoders.py — do not edit by hand.")
    out.append("// Each method skips through one step's body, advancing the StoryStreamReader")
    out.append("// cursor by exactly the bytes the game's matching Write produced.")
    out.append("// The base StoryStep.conditions[] field is consumed by the dispatcher BEFORE")
    out.append("// calling these methods, so they only read the subclass-specific fields.")
    out.append("namespace DialogExporter;")
    out.append("")
    out.append("public static class StepDispatcher_Generated")
    out.append("{")
    out.append("    public static bool TrySkip(byte type, StoryStreamReader r)")
    out.append("    {")
    out.append("        switch (type)")
    out.append("        {")

    for tb in sorted(typed_steps.keys()):
        name, reads = typed_steps[tb]
        out.append(f"            case {tb}: // {name}")
        for fname, ftype, reader in reads:
            # Multi-statement decoders (struct expansions) start with `{` —
            # can't be the RHS of `_ = ...`, so emit them as plain statements.
            if reader.startswith("{") or reader.startswith("ReadCtxArray"):
                out.append(f"                {reader} // {ftype} {fname}")
            else:
                out.append(f"                _ = {reader} // {ftype} {fname}")
        out.append("                return true;")

    out.append("            default:")
    out.append("                return false;")
    out.append("        }")
    out.append("    }")

    # ReadCtxArray helper for Context[]
    out.append("")
    out.append("    private static void ReadCtxArray(StoryStreamReader r)")
    out.append("    {")
    out.append("        int n = r.ReadInt32();")
    out.append("        for (int i = 0; i < n; i++) _ = r.ReadContext();")
    out.append("    }")
    out.append("}")
    out.append("")

    OUTPUT_FILE.write_text("\n".join(out), encoding='utf-8')

    print(f"Generated {OUTPUT_FILE}: {len(typed_steps)} decoders, skipped {len(skipped)}")
    print()
    print("Skipped:")
    for tb, name, reason in sorted(skipped):
        print(f"  {tb:>3}  {name}  ({reason})")


if __name__ == '__main__':
    main()
