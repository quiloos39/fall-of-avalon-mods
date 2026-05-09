# Build recommendations — actionable plan

For the **Lv 57 dual-Perceval poison/bleed tank** build. Last refresh: 2026-05-09.

> Numbers come from live in-game probes. Re-run via `dev/BuildAdvisor/` whenever gear/talents change. See [PROBE_SYSTEM.md](PROBE_SYSTEM.md).

---

## Snapshot at refresh time

| Stat | Value |
|---|---|
| HP | 199 |
| Stamina (regen/s) | 125 (72) |
| Mana (regen/s) | 36 (2) |
| Total armor | 65 → 65% DR |
| IncomingDmg | 0.85 (= 15% extra DR) |
| Effective HP (physical) | **~673** |
| MeleeDmgMul | 2.65 |
| TwoHandedDmgMul | 1.40 |
| AttackSpeed | 1.40 |
| Crit % / Crit dmg | 12% / ×1.98 |
| DebuffStrength | 1.75 (poison/bleed scaling) |
| **Unspent points** | **4 stat · 4 talent** |

---

## 1. Cuirass swap → Shadowblade's Lamellar Armor ⭐⭐⭐

**Single highest-impact change.** Recipe is already known (`found:True` in armor catalog). Light T0, **80 base armor, no requirements**.

| | Current (Revenant's Ribcage +7) | Shadowblade's Lamellar (no reinforcement) |
|---|---|---|
| Slot armor | 13 | 80 |
| Total armor | 65 | ~132 → **clamped 95** (cap) |
| Damage reduction | 65% | **95%** (cap) |
| Effective HP (from 199) | 673 | **~4,685** (~**7× tankier**) |

**Tradeoffs**: lose Revenant's two `OnEquip_IncreaseStat` passive bonuses. Per [JEWELRY.md](JEWELRY.md)-style decoded values, those Revenant tweaks are minor; the +67 raw armor is worth it.

There are **no armor set bonuses** in this game — confirmed by reading `ItemSet.cs` (it's a character-creator preset, not a worn-set mechanic). Mixing armor pieces is fine.

---

## 2. Talent points: Exploitation 1→3 (+2) + First Blood 0→2 (+2) ⭐⭐⭐

Sources: [TALENTS_BASE_TREES.md](TALENTS_BASE_TREES.md) for descriptions, [DPS_COMPARISON.md](DPS_COMPARISON.md) for impact.

| Pts | Talent | Path | Effect | Why |
|---|---|---|---|---|
| 2 | **Exploitation of Afflictions** 1→3 | PRA · Statuses | +10/20/30% damage vs status'd enemy | Perceval's `Weapon_OnHitApplyStatusFlipFlop` guarantees poison/bleed every hit. **Always-on +20%.** |
| 2 | **First Blood** 0→2 | END · Stamina (under Determination/Poised for Impact) | +10/20% physical damage at full stamina | You regen 72 stamina/s → near-100% uptime. |

**Sim says modeled +14% damage** (DPS_COMPARISON.md), but sim is **conservative** because:
- Exploitation is actually a *target-side IncomingDamage multiplier* — the real gain is closer to **+20% true damage**, not the +7% modeled.
- Combined real expected gain: **~30–35% damage** in normal fights.

### Alternative if you want pure defense

Swap First Blood for **Toughened by Pain 0→2** (PRA, behind Hedgehog Knight you have):
- "Gain up to 15% Damage Reduction depending on missing Health"
- Stacks on top of armor cap (multiplicative chain), so even at 95% DR this further multiplies effective HP.

### Don't take

| Talent | Why not |
|---|---|
| **Endurance Training** | Mis-named — it's "armor *weight* thresholds", not armor multiplier (verified in description text). |
| **Weapons of Great Heft** | +2% per weight. Perceval's weighs 2.0 → only +4% damage. |
| Any Shield branch (Counterweight, Shieldbearer, etc.) | You dual-wield, no shield equipped. |
| **Furious Assault** | +1-2% AS per 10 missing HP. With 199 HP, marginal. |

---

## 3. Stat points: 2 PER + 2 END ⭐⭐

| Per point | Effect |
|---|---|
| PER | +0.05 CritDmg, +0.01 CritChance, +0.08 SneakDmg |
| END | +8 HP, +1.5 stamina regen, +10 carry, +0.01 melee dmg via Inner Strength |
| STR (you have 20) | Past softcap, ~+0.012 effective per pt — skip |
| DEX (you have 6) | Already past softcap on AttackSpeed — skip |

**Recommendation**: 2 PER + 2 END
- +10% CritDmg, +2% CritChance (~+3% effective DPS — see DPS sim)
- +16 HP, +1 stamina regen, +2% melee via Inner Strength

If you want pure DPS instead → **4 PER** (~+6.4% DPS in sim).
If you want pure tank → **4 END** (+32 HP, 192 → 224 with multiplier; +4% melee via Inner Strength).

---

## 4. Slot Gambit Scale gem in Helmet's free slot ⭐

Revenant's Cowl has `gemSlots: {available: 2, max: 2}` but only **1 gem attached**. You're sitting on:
- **Gambit Scale** — Armor gem (T4/T5), 2 effects:
  - +2% DashIFramesBonus (longer dodge i-frames)
  - 25% chance OnDamageTaken to apply a status (3s) — defensive proc

It's a free slot, fill it.

### Other gem moves

| Gem | Where | Considering |
|---|---|---|
| **Rough-Hewn Relic** (Weapon, +10% MeleeDmgMul) | Loose | Slightly better than your weapon-slotted **Feral Charm** (+7% Strength) — but only 1 copy exists, you'd unbalance the dual-Perceval symmetry. Pass unless you commit to one weapon. |
| **Bloodthorn** (Weapon, 5% on-hit +5 Health) | Loose | Weak proc, skip. |
| **Vengeful Spike** (Greaves, +5% MeleeRetaliation) | Slotted | Niche stat. Could swap if you find better armor gem. |
| **Blood-tainted Shell +1** (Greaves, OnKill +10 HP) | Slotted | Solid in-fight sustain. Keep. |

See [GEMS.md](GEMS.md) for full breakdowns.

---

## 5. Long-term: Wyrd Excalibur upgrades

Catalog of available King's Soul (Wyrd Skill) upgrades — see [TALENTS_WYRD_SARRAS.md](TALENTS_WYRD_SARRAS.md) for full text:

| Wyrd upgrade | Effect | Priority |
|---|---|---|
| **Excalibur · KillsIncreaseDMG** | +50% damage per enemy killed during King's Soul | ⭐⭐⭐ |
| **Excalibur · ReceiveLessDMG** | -50% damage taken during King's Soul | ⭐⭐⭐ |
| **Helmet · IncreasedDMGandSpellPower** | +50% Physical Damage during King's Soul | ⭐⭐ |
| **Helmet · LowerCosts** | No mana/stamina costs during King's Soul | ⭐⭐ |
| **Shield · IncreaseStatByDamageTaken** | 10% damage taken → Mana Shield 30s | ⭐⭐ |
| **Shield · DoubleBlasta** | +1 blast at end | ⭐ |
| **Helmet · KillingProlongsWyrdskill** | +King's Soul on kills | ⭐ |
| **Excalibur · FinishersRestoreMorePower** | +400% King's Soul on finishers | ⭐ |
| **Prologue · DeflectProjectiles** | Deflect projectiles during King's Soul | ⭐ |

**Cost**: WyrdMemoryShards. You currently have **0**. Earned by cleansing Wyrdness areas (the corrupted purple zones). The "12 Wyrd Whispers" is a separate flag-counter, not currency.

---

## 6. Long-term: Sarras tree (46 nodes, all unspent)

Requires **CatalystTalentPoints** (you have 0). Earned via main-quest Sarras Power Catalyst items.

Best Warrior-subtree picks for your build (full text in [TALENTS_WYRD_SARRAS.md](TALENTS_WYRD_SARRAS.md)):

- **Strength In Numbers** (root): +damage per nearby enemy
- **The Heaviest of Attacks**: +damage on long heavy attack charge
- **Juggernaut**: -damage taken during attacks
- **Vicious Gamble**: trade min for max damage (great with Crushing Blow AOE)

---

## 7. Better gear to hunt for

### Weapons

You're already optimal — see [WEAPONS.md](WEAPONS.md). **Dual Perceval's Left Blade** beats every other 2H setup once you account for double-hit and FlipFlop status. Excalibur (Tier 7) requires STR 30; Galahad's Mace (Tier 6) requires PER 8. None are clear upgrades over dual-wield.

### Jewelry

Per [JEWELRY.md](JEWELRY.md) — these would beat current pieces if you find them:

| Slot | Current | Upgrade target | Why |
|---|---|---|---|
| Amulet | Stonewarden Talisman (+5% CC, +5% CD) | **No Quarter** (`SOS_Jewelry_..._Sarras_Melee`) | Flat **+20% MeleeDamageMultiplier** — beats CC/CD combo by ~7%. |
| Amulet | Stonewarden Talisman | **Wyrdstone Trinket** (DarvsTrinket) | ×1.25 MaxHealth multiplier (199 → 249) — pure tank. |
| Ring | Perfect Ring for a Warrior +6 | **Swordsman Amulet** (it's a Ring template) | **+25% Strength when dual-wielding** — better than +11% Strength. |
| Ring | (Spare Ring1 if you swap Poison) | **Sigil of the Round Table** | +5% MeleeDmgMul (tierable, +1%/lvl). |
| Ring | (defensive option) | **Ring of Despair** | -20% IncomingDamage flat. Strong defense. |

**Keep Poison Ring** — its +100% damage vs status'd enemies is unbeaten in the build.

### Armor

Beyond the Cuirass swap, your other slots are at the cap regardless. Don't bother chasing armor on:
- Helmet (you're at +9, can reinforce to +13)
- Gauntlets/Greaves/Boots (10–11 armor each is fine; armor cap dominates)

---

## TL;DR action list

1. ⭐ **Craft Shadowblade's Lamellar Armor**, equip in Cuirass slot → 95% DR
2. ⭐ **Talent points (4)**: Exploitation of Afflictions 1→3 + First Blood 0→2
3. **Stat points (4)**: 2 PER + 2 END (or 4 PER if you want pure offense, 4 END if pure tank)
4. **Slot Gambit Scale** in Revenant's Cowl's free gem slot
5. *(when found)* Swap **Warrior Ring** for **Swordsman Amulet** (it's a ring template)
6. *(long-term)* Cleanse Wyrdness for shards → take Excalibur's KillsIncreaseDMG + ReceiveLessDMG
7. *(long-term)* Sarras Catalyst Points → Warrior subtree (Heaviest of Attacks, Juggernaut, Strength In Numbers)

**Conservative simulated impact** of doing 1+2+3:
- Damage: +14–18%
- Effective HP: ~7× from Cuirass swap alone

**Realistic impact** (talent multipliers stack better than the conservative sim):
- Damage: +25–35%
- Effective HP: ~7× from Cuirass + ~+8% from END

---

## Provenance

| Source | What it answers |
|---|---|
| [BUILD_SNAPSHOT.md](BUILD_SNAPSHOT.md) | What is my current build? |
| [WEAPONS.md](WEAPONS.md) | What weapons exist, ranked by damage? |
| [ARMOR.md](ARMOR.md) | What armor exists, per slot? |
| [JEWELRY.md](JEWELRY.md) | What rings/amulets exist with full stat tweaks? |
| [GEMS.md](GEMS.md) | What gems are slotted/loose, with effects? |
| [TALENTS_BASE_TREES.md](TALENTS_BASE_TREES.md) | Every base talent's per-rank description |
| [TALENTS_WYRD_SARRAS.md](TALENTS_WYRD_SARRAS.md) | Wyrd / Sarras descriptions |
| [DPS_COMPARISON.md](DPS_COMPARISON.md) | Quantified DPS deltas for each scenario |
| [PROBE_SYSTEM.md](PROBE_SYSTEM.md) | How to refresh the probes |
