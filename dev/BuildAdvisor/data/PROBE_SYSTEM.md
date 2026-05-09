# BuildAdvisor probe system — how to refresh

All data files in `dev/BuildAdvisor/data/` are generated from live in-game probes via `HttpProbe`. Re-run them whenever:

- You level up or change attributes
- You acquire/lose new gear
- The game is patched (templates may change)

## Prerequisites

1. The game is running with `HttpProbe.dll` in `BepInEx/plugins/` (already installed; check `BepInEx/LogOutput.log`).
2. Default port: `127.0.0.1:8989`. Endpoints listed at `GET /` (base64-encoded).

## One-step refresh (PowerShell or bash)

```bash
# Build
dotnet build dev/BuildAdvisor/BuildAdvisor.csproj -c Release

# Run a probe (e.g., Probe / Run for full state dump)
DLL_B64=$(base64 -w 0 dev/BuildAdvisor/bin/Release/BuildAdvisor.dll)
printf '%s' "{\"dllBase64\":\"$DLL_B64\",\"type\":\"BuildAdvisor.Probe\",\"method\":\"Run\"}"   | base64 -w 0 > /tmp/payload.b64
curl -s -X POST http://127.0.0.1:8989/eval   -H "Content-Type: application/json"   --data-binary @/tmp/payload.b64   -o dev/BuildAdvisor/probe_outer.json
```

## Available probes

| Probe class | Purpose | Output JSON |
|---|---|---|
| `Probe` | Hero state: stats, attributes, talents (taken/untaken), equipped, craftable weapons | `probe_now_pretty.json` |
| `GearProbe` | Equipped + loose inventory with gem slots & passive effects | `gear_pretty.json` |
| `CatalogProbe` | All weapon / armor / jewelry / loaded-gem templates with stats & effects | `catalog.json` |
| `GemDetailProbe` | Decoded skill graphs for every gem in inventory or attached | `gem_details.json` |
| `TalentDescProbe` | Descriptions for selected talents (filtered) | `desc_pretty.json` |
| `FullTalentProbe` | Descriptions for **every** talent in **every** tree (base + Wyrd + Sarras) | `FullTalentProbe.json` |
| `SkillsProbe` | Learned & equipped skills (mostly item-bound effects) | `SkillsProbe.json` |
| `SimProbe` | Apply temporary stat changes, read new modifiers, restore | `sim_*.json` |
| `DeepProbe` | Damage formula breakdown via `Damage.GetStatModifiers` | `deep_out.json` |
| `ArmorProbe` | Armor recipe values from `TemplatesProvider` | `armor_*.json` |

## Decoding the response

The HTTP response is `base64(JSON)`. The inner JSON has `result` which is itself a JSON-encoded string (sometimes double-encoded). Pipeline:

```python
import json, base64
with open('out_outer.json', 'rb') as f: raw = f.read()
d = json.loads(base64.b64decode(raw).decode())
inner = d['result']
while isinstance(inner, str): inner = json.loads(inner)  # may be doubly-encoded
```

## Markdown generation

The data .md files (`BUILD_SNAPSHOT.md`, `WEAPONS.md`, etc.) are rendered from the JSON probe outputs by inline Python snippets — see `dev/BuildAdvisor/RECOMMENDATIONS.md` for the pipeline. Edit the snippets to change layout.

## When the game has been patched

After any Steam update, refresh in this order:

1. Re-decompile DLLs into `refs/game/` (see `docs/POST_GAME_PATCH.md`).
2. `dotnet build dev/BuildAdvisor/BuildAdvisor.csproj` — type / member references may have moved.
3. Re-run all probes.
4. Re-render markdown.
