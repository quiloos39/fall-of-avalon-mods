# Gems — what they do, decoded

From `dev/BuildAdvisor/GemDetailProbe.cs`. Only gems currently in inventory or attached to equipped gear can be enumerated — other gem templates load on-demand via Addressables, so this list is your live picture.

## Currently slotted on equipped gear

### [MainHand] Perceval's Left Blade +11 → **Fore-Dweller Bauble** (`ItemTemplate_Gem_Tierable_Weapon_ForeDwellerBauble`, +0)

- OnKill_ApplyStatus (StatEnum0=Strength) Duration=Infinity, ChanceToApply=1.0, StackCount=1.0, StatValue0=0.1, StatGain0=0.005

### [MainHand] Perceval's Left Blade +11 → **Feral Charm** (`ItemTemplate_Gem_Tierable_Weapon_FeralCharm`, +0)

- OnEquip_IncreaseStat (StatEnum=Strength) ModifyValue=0.07, GainValue=0.007

### [OffHand] Perceval's Left Blade +11 → **Fore-Dweller Bauble** (`ItemTemplate_Gem_Tierable_Weapon_ForeDwellerBauble`, +0)

- OnKill_ApplyStatus (StatEnum0=Strength) Duration=Infinity, ChanceToApply=1.0, StackCount=1.0, StatValue0=0.1, StatGain0=0.005

### [OffHand] Perceval's Left Blade +11 → **Feral Charm** (`ItemTemplate_Gem_Tierable_Weapon_FeralCharm`, +0)

- OnEquip_IncreaseStat (StatEnum=Strength) ModifyValue=0.07, GainValue=0.007

### [Greaves] Duel Knight Trousers +7 → **Vengeful Spike** (`ItemTemplate_Gem_Tierable_Armor_VengefulSpike`, +0)

- OnEquip_IncreaseStat (StatEnum=MeleeRetaliation) ModifyValue=0.05, GainValue=0.005

### [Greaves] Duel Knight Trousers +7 → **Blood-tainted Shell +1** (`ItemTemplate_Gem_Tierable_Armor_BloodtaintedShell`, +1)

- Armor_OnKill_RestoreStat (StatEnum=Health) ModifyValue=10.0


## Loose (in inventory, can be slotted)

### **Rough-Hewn Relic** (`ItemTemplate_Gem_Static_Weapon_RoughHewnRelic`, type=Weapon, qty=1)

- OnEquip_IncreaseStat (StatEnum=MeleeDamageMultiplier) ModifyValue=0.1, GainValue=0.01

### **Gambit Scale** (`ItemTemplate_Gem_Static_Armor_GambitScale`, type=Armor, qty=1)

- OnEquip_IncreaseStat (StatEnum=DashIFramesBonus) ModifyValue=0.02
- OnDamageTaken_ApplyStatus Duration=3.0, ChanceToApply=0.25

### **Bloodthorn** (`ItemTemplate_Gem_Tierable_Weapon_Bloodthorn`, type=Weapon, qty=1)

- OnEquip_OnHit_IncreaseStat (Stat=Health) StatValue=5.0


## Quest-collectible relics (8 fragments, FAKE_FAKE templates)

These are visual lore items, not equippable gems:

- Toe of a Giant (`ItemTemplate_Gem_FAKE_FAKE_ToeGiant`)
- Splint from Merlin's Staff (`ItemTemplate_Gem_FAKE_FAKE_SplintFromMerlinsStaff`)
- Scale from the Green Knight's Armor (`ItemTemplate_Gem_FAKE_FAKE_ScaleFromTheGreenKnightsArmor`)
- King Arthur's Footwrap (`ItemTemplate_Gem_FAKE_FAKE_KingArthursFootwrap`)
- Hair of Lancelot's Horse's Mane (`ItemTemplate_Gem_FAKE_FAKE_HairLancelotsHorsesMane`)
- Gwenhwyfar's Left Glove (`ItemTemplate_Gem_FAKE_FAKE_GwenhwyfarsLeftGlove`)
- Galahad's Bone (`ItemTemplate_Gem_FAKE_FAKE_GalahadsBone`)
- Fore-Dweller's Tooth (`ItemTemplate_Gem_FAKE_FAKE_Fore-DwellersTooth`)

## Probe limitation

`TemplatesProvider.GetAllOfType<ItemTemplate>()` only returns gem templates that are currently loaded into the type-map. The "Static" (e.g., Rough-Hewn Relic) and "Tierable" (e.g., Bloodthorn) gem ItemTemplates load on-demand via Addressables when an item references them, so they don't appear in a static enumeration.

To discover *new* gems mid-game, pick them up — they'll appear in `GemDetailProbe`.
