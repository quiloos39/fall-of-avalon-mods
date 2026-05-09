# BuildAdvisor data

This folder is the **persistent reference** for the current TG:FoA save's build state and the data we use to advise on it. It's what you read instead of re-running probes.

| File | Purpose |
|---|---|
| [RECOMMENDATIONS.md](RECOMMENDATIONS.md) | **Start here** — actionable plan with sourced reasoning |
| [BUILD_SNAPSHOT.md](BUILD_SNAPSHOT.md) | Current hero state (stats, attributes, talents, gear) |
| [WEAPONS.md](WEAPONS.md) | All weapon templates ranked by damage |
| [ARMOR.md](ARMOR.md) | All armor templates per slot, sorted by base armor |
| [JEWELRY.md](JEWELRY.md) | All rings/amulets with decoded `OnEquip_*` stat tweaks |
| [GEMS.md](GEMS.md) | Gems currently in inventory or attached, with effect graphs |
| [TALENTS_BASE_TREES.md](TALENTS_BASE_TREES.md) | Every talent in STR/DEX/SPI/PER/PRA/END/RedDeath, per-rank descriptions |
| [TALENTS_WYRD_SARRAS.md](TALENTS_WYRD_SARRAS.md) | Wyrd Arthur and Sarras tree descriptions |
| [DPS_COMPARISON.md](DPS_COMPARISON.md) | Sim'd DPS / effective HP for current vs scenarios |
| [PROBE_SYSTEM.md](PROBE_SYSTEM.md) | How to re-run the probes that built this folder |

## When to refresh

- Picked up new gear → `GearProbe` + `GemDetailProbe`
- Spent points → `Probe` + `FullTalentProbe`
- Game patched → all of the above + re-decompile DLLs
- Curiosity about a stat → `DpsProbe` (edit scenarios in `dev/BuildAdvisor/DpsProbe.cs`)

## Probe-to-file mapping

```
Probe.cs              → probe_now_pretty.json   → BUILD_SNAPSHOT.md
GearProbe.cs          → gear_pretty.json        → (raw data only)
CatalogProbe.cs       → catalog.json            → WEAPONS.md, ARMOR.md, JEWELRY.md
GemDetailProbe.cs     → gem_details.json        → GEMS.md
FullTalentProbe.cs    → FullTalentProbe.json    → TALENTS_BASE_TREES.md, TALENTS_WYRD_SARRAS.md
SkillsProbe.cs        → SkillsProbe.json        → (mostly redundant with gear)
DpsProbe.cs           → dps_compare.json        → DPS_COMPARISON.md
```
