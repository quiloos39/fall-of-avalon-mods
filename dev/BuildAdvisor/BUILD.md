# Tainted Grail: Fall of Avalon — Build Math Investigation

How the damage and defense numbers were derived. No recommendations, just the work.

---

## Methodology

1. Used the existing `HttpProbe` plugin (BepInEx HTTP server inside the game on `127.0.0.1:8989`) to inject DLLs at runtime via `/eval`.
2. Wrote `BuildAdvisor` probes that read live `Hero` state via reflection on the actual game classes.
3. Cross-referenced live state against decompiled source in `refs/game/TG.Main/`.
4. For "what if" questions, simulated stat changes by temporarily applying tweaks via `Stat.SetTo()` and reverted after reading.

Probe sources:

- `dev/BuildAdvisor/Probe.cs` — full state dump (stats, talents, equipped, recipes)
- `dev/BuildAdvisor/SimProbe.cs` — apply temporary stat changes, read new MultMod, restore
- `dev/BuildAdvisor/DeepProbe.cs` — damage formula via `Damage.GetStatModifiers`, armor breakdown
- `dev/BuildAdvisor/ArmorProbe.cs` — query `TemplatesProvider` for armor recipe values

---

## Damage formula (verified)

From `Awaken.TG.Main.Fights.DamageInfo/RawDamageData.cs:148`:

```
FinalDamage = (WeaponBase + LinearModifier) × MultModifier × AddedMultModifier
```

Three modifier slots:

| Slot | Source | Stacking |
|---|---|---|
| `LinearModifier` | `StrengthLinear` flat add | flat sum |
| `MultModifier` | base mul + chain of `MultiplyMultModifier` calls | **multiplicative chain** |
| `AddedMultModifier` | starts at 1.0; chain of `AddMultModifier` calls | **additive** (+0.X each) |

### MultModifier construction

From `Damage.cs:325` (`Damage.GetStatModifiers`):

```
CompoundStat = 1 + (charStrength - 1) + (profDmgMul - 1)
multStatModifier = CompoundStat + (MeleeDmgMul - 1)
multStatModifier *= ItemRequirementsUtils.GetDamageMultiplier(...)
```

For 2H melee:
- `profDmgMul` = `TwoHandedMeleeDamageMultiplier`
- `charStrength` = `CharacterStats.Strength` (NOT the RPG attribute)
- `MeleeDmgMul` = `MeleeDamageMultiplier`

### Multiplicative chain (`MultiplyMultModifier` callers)

- `HeavyAttackDamageMultiplier` (weapon-specific, ~2.0 for Perceval's)
- Armor mitigation (target-side)
- `IncomingDamage` multiplier (target-side)
- Block reductions

### Additive chain (`AddMultModifier` callers)

- `AliveIncreasedDamage` zones (environmental)
- Possibly crit/weakspot (not fully verified)

---

## Stat tweak composition

From `Awaken.TG.Main.Heroes.Stats.Tweaks/TweakSystem.cs:111`:

Priority buckets applied in order:

1. `PreSet`
2. `AddPreMultiply` — **most damage stats land here**
3. `Multiply`
4. `Add`
5. `Override`

**Key finding**: within a single bucket, all tweaks are applied to the *same* starting value, and their deltas are *summed* into the running total.

```
For two Multi tweaks of ×1.20 and ×1.30:
  delta_1 = num × 1.20 - num = num × 0.20
  delta_2 = num × 1.30 - num = num × 0.30
  num2 = num + delta_1 + delta_2 = num × 1.50

NOT num × 1.20 × 1.30 = num × 1.56
```

So multipliers in the same bucket stack additively, not multiplicatively.

### Helper functions

`Awaken.Utility/M.cs:291`:
```
MergeMultipliers(a, b) = a + b - 1
```

`Stat.cs:293`:
```
Stat.ToMultiplier(x) = (100 + x) / 100
```

`ToMultiplier` is only invoked for `OperationType.Multi` effects (converts a percentage like 25 into a 1.25 ratio before storing as a tweak).

---

## RPG attribute → stat tweak mapping (probed live)

Per HeroRPGStats attribute, applied as `AddPreMultiply` tweaks:

| Attribute | Per-point effect |
|---|---|
| Strength | +0.02 MeleeDamageMultiplier, +3 MaxStamina, −0.01 ArmorWeightMultiplier |
| Dexterity | +0.04 AttackSpeed, +0.02 RangedDamageMultiplier, −0.03 Visibility, −0.03 Noise |
| Endurance | +8 MaxHealth, +1.5 StaminaRegen, +10 EncumbranceLimit |
| Spirituality | +0.02 MagicStrength, +6 MaxMana, +0.3 ManaRegen |
| Perception | +0.05 CriticalDamageMultiplier, +0.01 CriticalChance, +0.08 SneakDamageMultiplier |
| Practicality | +0.05 WeakSpotDamageMultiplier, +0.10 EquipmentLevelBonus, +0.10 AlchemyLevelBonus, +0.10 CookingLevelBonus, −0.01 StaminaUsageMultiplier, −0.01 ManaUsageMultiplier |

Per proficiency level (also `AddPreMultiply`):

| Proficiency | Per-level effect |
|---|---|
| OneHanded | +0.002 OneHandedMeleeDamageMultiplier, ×0.999 LightAttackCost, ×0.999 HeavyAttackCost |
| TwoHanded | +0.002 TwoHandedMeleeDamageMultiplier, ×0.999 LightAttackCost, ×0.999 HeavyAttackCost |
| Archery | +0.002 RangedDamageMultiplier |
| Magic | +0.002 MagicStrength |
| Athletics | +0.005 SprintSpeed, +0.005 SwimSpeed |
| Evasion | +0.003 DashSpeed, ×0.998 DashStamina |
| Acrobatics | ×0.995 FallDamageMultiplier |
| Unarmed | +0.03 UnarmedMeleeDamageMultiplier |

---

## Armor mitigation formula

From `Damage.cs:594`:

```
ArmorDamageReduction = clamp(armorValue / 100, 0, 0.95)
ArmorMitigatedMultiplier = 1 - ArmorDamageReduction
```

Soft cap at 95% damage reduction (armor value 95+).

### Total armor calculation

From `Hero.cs:1462`:

```
ArmorValue = (AliveStats.Armor + sum(equipped armor pieces × reduction multiplier)) × ArmorMultiplier
```

Each piece's effective armor depends on whether the wearer meets its requirements (`ItemRequirementsUtils.GetArmorReductionMultiplier`).

### Item requirement penalty

From `ItemRequirementsUtils.cs:12`:

```
b = 1 if requirements met
  = 1 - 0.1 - missingPoints × DamageDecreasePerMissingPoint if not met
return clamp(b, 0.05, 1)
```

Same formula applies to weapon damage and armor effectiveness when reqs not met.

---

## Attack speed composition

From `CharacterWeapon.cs:525`:

```
animationSpeed = MergeMultipliers(weaponSpecificSpeed × itemMul, hero.AttackSpeed)
              = (weaponSpecificSpeed × itemMul) + hero.AttackSpeed - 1
```

For a 2H heavy attack:
- `weaponSpecificSpeed` = `TwoHandedHeavyAttackSpeed.ModifiedValue` (base 0.9, range [0.5, 3.0])
- `hero.AttackSpeed` = `HeroStats.AttackSpeed` (base 1.0, range [0.5, 3.0])

### AttackSpeed cap

`HeroStats.AttackSpeed` is a `LimitedStat` capped at 3.0. Each DEX point contributes +0.04 via `AddPreMultiply`.

### Empirical observation: soft cap

Predicted: DEX 6 → 22 should give +0.04 × 16 = +0.64 to AttackSpeed.
Observed: +0.39 only.

Conclusion: `HeroAttributeSoftCapSetting` is enabled, reducing per-point gain past a threshold. Effective DEX gain past ~6 is roughly 60% of linear value.

---

## Verified numerical snapshots (live probe)

### Snapshot A — original allocation (STR 25, DEX 6, END 15, PRA 5, PER 5, SPI 1)

```
charStrength.ModifiedValue:    1.15
twoHandedDmgMul:               1.40
meleeDmgMul:                   2.64
attackSpeed:                   1.37
multStatModifier:              3.19
  = (1 + 0.15 + 0.40) + (2.64 - 1)
  = 1.55 + 1.64
weaponBase:                    87 - 94
heavyAttackMul:                2.0
linearStatMod:                 0.0
itemReqPenalty:                1.0 (Perceval's reqs met)

Per-swing light:               87 × 3.19 = 277.5  ...  94 × 3.19 = 299.9
Per-swing heavy:               277.5 × 2.0 = 555.1  ...  299.9 × 2.0 = 599.7

effLightSpeed: 1.0 + 1.37 - 1 = 1.37
effHeavySpeed: 0.9 + 1.37 - 1 = 1.27

Light DPS:                     ~395
Heavy DPS:                     ~705
MaxHealth:                     135
```

### Snapshot B — after stat respec (STR 18, DEX 22, END 7) — verified live

```
charStrength:                  1.41 (jumped +0.26 from talent recompute)
meleeDmgMul:                   2.47 (-0.17 from STR/END loss)
attackSpeed:                   1.76 (+0.39 from DEX, soft-capped)
multStatModifier:              3.28
Per-swing light:               285 - 308
Per-swing heavy:               571 - 617
Light DPS:                     521
Heavy DPS:                     984
MaxHealth:                     74.5
```

### Snapshot C — simulated tank (STR 20, DEX 6, END 21) — verified via SimProbe

```
charStrength:                  1.18
meleeDmgMul:                   2.65 (+0.18 from STR/END regain)
attackSpeed:                   1.37 (DEX back to floor)
multStatModifier:              3.23
Per-swing light:               281 - 304
Per-swing heavy:               562 - 607
Light DPS:                     400
Heavy DPS:                     742
MaxHealth:                     190 (END 7 → 21 = +115.5 HP, ~8.25 HP per END point)
```

---

## Armor verification

### Live armor state (probed via `Hero.TotalArmor`)

```
hero.AliveStats.Armor:         2.14   (innate base)
hero.AliveStats.ArmorMultiplier: 1.0
sum of equipped armor pieces:  57.0
total armor:                   59.14
damage reduction:              clamp(59.14/100, 0, 0.95) = 59.14%
incomingDamage multiplier:     1.0
```

### Equipped armor pieces (live)

| Slot | Item | Armor |
|---|---|---:|
| Helmet | Revenant's Cowl +9 | 13 |
| Cuirass | Revenant's Ribcage +7 | 13 |
| Gauntlets | Duel Knight Gloves +5 | 10 |
| Greaves | Duel Knight Trousers +7 | 11 |
| Boots | Champion's Boots +5 | 10 |
| Back | Broc Meala Basket +8 | 0 |

### Recipe armor values (probed via `TemplatesProvider.GetAllOfType<ItemTemplate>()`)

Reading the `ItemStatsAttachment` on each template:

| Template | Class | Armor base | ArmorGain | Weight | Reqs |
|---|---|---:|---:|---:|---|
| ShadowbladesLamellarArmor (Cuirass) | Light T0 | **80** | +1 | 1.0 | none |
| KnightsHelmet | Heavy T3 | 5 | +1 | 4.0 | STR 6 / END 5 |
| KnightsBreastplate | Heavy T3 | 10 | +1 | 7.9 | STR 6 / END 5 |
| KnightsGauntlets | Heavy T3 | 4 | +1 | 4.7 | STR 6 / END 5 |
| KnightsGreaves | Heavy T3 | 5 | +1 | 3.2 | STR 6 / END 5 |
| KnightsBoots | Heavy T3 | 4 | +1 | 5.0 | STR 6 / END 5 |
| BloodsoakedHood | Medium T4 | 7 | +1 | 1.5 | END 4 |
| BloodsoakedVest | Medium T4 | 9 | +1 | 3.5 | END 4 |
| FlamegorgedMaw | Medium T4 | 8 | +1 | 1.6 | END 8 / PRA 8 |

(Reinforcement: each `+N` adds `N × armorGain` to the base.)

---

## Talent rank caps (probed)

Read via `Talent.MaxLevel` for each entry in the `BaseTalentTables`:

| Talent | Max ranks |
|---|---:|
| Brutish Fighter, Mighty Blows, Perpetual Swings, Fury Without Fatigue, Enduring Strikes, Giant's Grace, Unstoppable Force, Crushing Blow, Unstoppable Impact, Culling the Herd | 3 |
| Godly Physique | 5 |
| Dominating Presence, Weapons of Great Heft | 1 |
| Executioner | 2 |
| **Carnage** | **1** (despite "up to 50%" wording in tooltip) |
| **Critical Cascade** | **1** (despite "up to 20%" wording) |
| Increased Vitality | 3 |
| Born in Armor | 4 |
| Armored Skin | 3 |
| Inner Strength | 1 |
| Hedgehog Knight | 1 |
| Exploitation of Afflictions, Potent Poisons | 3 |
| Stacking Peril | 2 |
| First Blood | 3 |
| Determination | 2 |
| Frequent Exercise, Poised for Impact | 1 |
| Extra Lucky, Respite in Cruelty | 3 |
| Go for the Eyes | 4 |
| Critical Aegis, Bloodthirst | 2 |
| Silent Hunter | 3 |
| Cheat Death | 1 |

---

## Talent parent-child requirements

Each `Talent` has a `Parent` (`TalentTemplate`) field. Acquiring a child talent requires the parent to be at level ≥ 1.

Sample chains observed in the dump:

```
Brutish Fighter (root)
└── Unstoppable Force
    ├── Crushing Blow
    └── Unstoppable Impact
        ├── Culling the Herd
        └── Dominating Presence

Born in Armor (root)
├── Endurance Training
├── Armored Skin
│   ├── The Best Offense
│   └── Uncanny Agility
│       └── Acrobatic Dodge
└── Inner Strength
    ├── Hedgehog Knight
    └── Toughened by Pain

Frequent Exercise (root)
├── Determination
│   ├── Poised for Impact
│   └── First Blood
└── Gaining Momentum
    ├── Atone in Suffering
    ├── Back into the Fray
    └── Exploit Weakness

Increased Vitality (root)
├── Carnage
│   └── Cruelty
└── Unstoppable Rage
    └── Furious Assault

Extra Lucky (root)
├── Critical Cascade
│   └── Go for the Eyes
└── Respite in Cruelty
    ├── Deep Wounds
    └── Critical Aegis
        ├── Bloodthirst
        └── Swift and Fierce

Silent Hunter (root)
├── Cheater's Gambit
├── Assassin's Doctrine
└── Cheat Death
```

Subtree groupings (from `TalentSubtreeType.cs` localized display names): Two Handed, One Handed, General, Unarmed, Armor, Statuses, Healing, Crafting & Trading, Stamina, Health, Shields, Critical Hits, Daggers, Stealth, Wands, Summoning, Combat, General and Buffs, Parry, Attack Speed, Movement, Bows, Sarras Warrior/Mage/Rogue.

---

## Mistakes corrected during the investigation

| Issue | Initial belief | Corrected understanding |
|---|---|---|
| Talent stacking | "+50% Carnage stacks multiplicatively with +30% talent" | Both feed `MeleeDmgMul` via `AddPreMultiply` → stack additively in same bucket |
| DEX scaling | linear (+0.04 AttackSpeed × 16 = +0.64) | soft cap reduces gain past threshold (~+0.39 observed for the same 16 DEX) |
| Carnage rank | assumed 3/3 with linear scaling | actually max rank 1 |
| Critical Cascade rank | assumed 3/3 | actually max rank 1 |
| Talent prerequisites | assumed flat tree | each talent has a `Parent` template that must be ≥ 1 |
| Subtree names | used internal enum names (`StrengthTwoHanded`) | actual UI displays localized names (`Two Handed`) |
| Knight's heavy set | assumed heavy = high armor | Knight's pieces base 4-10 armor, lower than several T4 medium options |
| Shadowblade's Lamellar Armor | initially overlooked | 80 base armor, Cuirass slot, no requirements — single-craft armor cap |
| Per-swing damage after respec | predicted −6.9% | actually +6% (CharacterStats.Strength jumped from talent recompute) |

---

## Source code map

Game-side code referenced in `refs/game/TG.Main/`:

| Subsystem | File |
|---|---|
| Damage formula | `Awaken.TG.Main.Fights.DamageInfo/Damage.cs` |
| Final calculation | `Awaken.TG.Main.Fights.DamageInfo/RawDamageData.cs` |
| Compound stats | `Awaken.TG.Main.Heroes.Stats/CompoundStat.cs` |
| Tweak priority/stacking | `Awaken.TG.Main.Heroes.Stats.Tweaks/TweakSystem.cs` |
| Stat conversions | `Awaken.TG.Main.Heroes.Stats/Stat.cs` |
| Operation types | `Awaken.TG.Main.Heroes.Stats/OperationType.cs` |
| Item requirement penalty | `Awaken.TG.Main.Heroes.Items/ItemRequirementsUtils.cs` |
| Attack speed | `Awaken.TG.Main.Heroes.Combat/CharacterWeapon.cs` |
| Helper math | `Awaken.Utility.M.cs` |
| Subtree display names | `Awaken.TG.Main.Heroes.CharacterSheet.TalentTrees.Subtree/TalentSubtreeType.cs` |
| Equipment slots | `Awaken.TG.Main.Heroes.Items/EquipmentSlotType.cs` |
| Armor totals | `Awaken.TG.Main.Heroes/Hero.cs` (`ArmorValue`, `TotalArmor`) |
| Hero stats | `Awaken.TG.Main.Heroes/HeroStats.cs`, `Awaken.TG.Main.Character/CharacterStats.cs`, `Awaken.TG.Main.Character/HeroRPGStats.cs` |
| Talents | `Awaken.TG.Main.Heroes.Development.Talents/Talent.cs`, `TalentTemplate.cs`, `TalentTable.cs` |
| RPG stat config | `Awaken.TG.Main.Heroes.Stats.Observers/RPGStatParams.cs`, `StatEffect.cs` |
| Item stats | `Awaken.TG.Main.Heroes.Items.Weapons/ItemStats.cs` |
| Item attachments | `Awaken.TG.Main.Heroes.Items.Attachments/ItemStatsAttachment.cs`, `ItemEquipSpec.cs`, `ItemStatsRequirementsAttachment.cs` |
