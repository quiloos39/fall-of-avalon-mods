# Jewelry — rings & amulets, all stats decoded

From `CatalogProbe.cs` (n=158 jewelry templates).

Each effect is shown as `EffectGraph (StatEnum=Stat) ModifyValue=X GainValue=Y` where `ModifyValue` is base bonus and `GainValue` is bonus per reinforcement level (for tierable items).


## MELEE damage

### Amulets

- **Lucky Charm** (`ItemTemplate_Jewelry_Static_Amulet_LuckyCharm`)
  - OnKill_GiveMoney (StatEnumGain=Strength) Amount=5.0, AmountGain=1.0
- **No Quarter** (`ItemTemplate_SOS_Jewelry_Static_Amulet_SarrasAmulet_Melee`)
  - OnEquip_IncreaseStat (StatEnum=MeleeDamageMultiplier) ModifyValue=0.2, GainValue=0.01
- **Berserker's Call** (`ItemTemplate_Jewelry_Static_Amulet_BerserkersCall`)
  - OnEquip_IncreaseStatWhenUnarmored (StatEnum=Strength) ModifyValue=0.3, ValueGain=0.03
- **Swordsman Amulet** (`ItemTemplate_Jewelry_Static_Ring_SwordsmanAmulet`)
  - OnEquip_DualWieldingIncreaseStat (StatEnum=Strength) ModifyValue=0.25, ValueGain=0.01

### Rings

- **Volker Clan Ring** (`ItemTemplate_Jewelry_Static_Ring_VolkerRing`)
  - OnDamageTaken_IncreaseStatFlat (StatEnum=Strength, StatEnum1=None, StatEnum_2=None, StatEnum_3=None) AddValue=0.15, Gain=0.01, Duration=3.0
- **Swordsman Amulet** (`ItemTemplate_Jewelry_Static_Ring_SwordsmanAmulet`)
  - OnEquip_DualWieldingIncreaseStat (StatEnum=Strength) ModifyValue=0.25, ValueGain=0.01
- **Sigil of the Round Table** (`ItemTemplate_Jewelry_Static_Ring_SigilOfTheRoundTable`)
  - OnEquip_IncreaseStat (StatEnum=MeleeDamageMultiplier) ModifyValue=0.05, GainValue=0.01
- **The Perfect Ring for a Warrior** (`ItemTemplate_Jewelry_Static_Ring_OrdinaryPhysicalDamageRing`)
  - OnEquip_IncreaseStat (StatEnum=Strength) ModifyValue=0.05, GainValue=0.01

## CRIT chance / damage

### Amulets

- **Amulet of the Waning Moon** (`ItemTemplate_Jewelry_Static_Amulet_AmuletOfTheWaningMoon_Unbalanced`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.1, GainValue=0.01
  - OnEquip_IncreaseStat (StatEnum=CriticalChance) ModifyValue=0.1
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxMana) ModifyValue=0.1, GainValue=0.01
- **Stonewarden Talisman** (`ItemTemplate_Jewelry_Tierable_Amulet_StonewardensTalisman_Unbalanced`)
  - OnEquip_IncreaseStat (StatEnum=CriticalChance) ModifyValue=0.1
- **Theud Protection Charm** (`ItemTemplate_Jewelry_Static_Amulet_TheudProtectionCharm`)
  - OnEquip_SleepGivesBlessings (StatEnum=CriticalChance) Duration=300.0, DurationGain=10.0
- **Lucky Amulet** (`ItemTemplate_Jewelry_Static_Amulet_LuckyAmulet`)
  - OnEquip_IncreaseLuck (StatEnum=CriticalChance) ModifyValue=0.15
- **Colm's Finger Necklace** (`ItemTemplate_Jewelry_Static_Amulet_ColmsFingerNecklace`)
  - OnEquip_IncreaseStat (StatEnum=CriticalChance) ModifyValue=0.05

### Rings

- **Perilous Orb** (`ItemTemplate_Jewelry_Tierable_Ring_PerilousOrb_Unbalanced`)
  - OnEquip_IncreaseStat (StatEnum=CriticalChance) ModifyValue=0.03, GainValue=0.003
- **Perilous Orb +2** (`ItemTemplate_Jewelry_Tierable_Ring_PerilousOrb_Tier2`)
  - OnEquip_IncreaseStat (StatEnum=CriticalChance) ModifyValue=0.07
- **Perilous Orb +1** (`ItemTemplate_Jewelry_Tierable_Ring_PerilousOrb_Tier1`)
  - OnEquip_IncreaseStat (StatEnum=CriticalChance) ModifyValue=0.05

## WEAK SPOT damage

### Amulets

- **Hunter's Amulet** (`ItemTemplate_Jewelry_Static_Amulet_HuntersAmulet`)
  - OnEquip_IncreaseStat (StatEnum=WeakSpotDamageMultiplier) ModifyValue=0.2, GainValue=0.1

## BONUS damage vs STATUS'D


### Rings

- **Ring of Ruin** (`ItemTemplate_SOS_Jewelry_Static_Ring_RingOfRuin`)
  - OnEquip_BonusDamageForTargetWithStatus (StatEnum=StatusResistance) BonusDamageMultiplier=0.1, BonusDamageGain=0.01
- **Drowner's Debt** (`ItemTemplate_SOS_Jewelry_Static_Ring_DrownersDebt`)
  - OnEquip_BonusDamageForTargetWithStatus (StatEnum=StatusResistance) BonusDamageMultiplier=0.15, BonusDamageGain=0.01
- **Poison Ring** (`ItemTemplate_Jewelry_Static_Ring_PoisonRing`)
  - OnEquip_BonusDamageForTargetWithStatus (StatEnum=StatusResistance) BonusDamageMultiplier=1.0, BonusDamageGain=0.01

## MAX HEALTH

### Amulets

- **Ambergris Fragment +2** (`ItemTemplate_Jewelry_Tierable_Amulet_AmbergrisFragment_Tier2`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=30.0
- **Ambergris Fragment +1** (`ItemTemplate_Jewelry_Tierable_Amulet_AmbergrisFragment_Tier1`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=20.0
- **Ambergris Fragment** (`ItemTemplate_Jewelry_Tierable_Amulet_AmbergrisFragment`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=10.0, GainValue=1.0
- **Fairy Protection** (`ItemTemplate_Jewelry_Static_Amulet_FairyProtection`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=25.0, GainValue=2.0
- **Wyrdstone Trinket** (`ItemTemplate_Jewelry_Static_Amulet_DarvsTrinket`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxHealth) ModifyValue=0.25, GainValue=0.01
- **True All-Mother's Mercy Amulet** (`ItemTemplate_Jewelry_Static_Amulet_AllmothersMercyAmulet_Complete`)
  - OnEquip_AllmotherMercy_Complete (StatEnum=Health, StatEnumMultiply=MaxHealth) ModifyValue=1.0, PermanentFlatValue=35.0
- **The All-Mother's Mercy Amulet** (`ItemTemplate_Jewelry_Static_Amulet_AllmothersMercyAmulet`)
  - OnEquip_AllmotherMercy (StatEnum=Health, StatToCheck=MaxHealth) ModifyValue=1.0
- **Blood'n'Rum** (`ItemTemplate_SOS_Jewelry_Static_Amulet_SarrasAmulet_MaxHealth`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxHealth) ModifyValue=0.2, GainValue=0.01
- **Great Beast's Bone** (`ItemTemplate_Jewelry_Static_Amulet_GreatBeastBone`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxHealth) ModifyValue=0.05, GainValue=0.01
- **Amulet of a Novice Ogre Hunter** (`ItemTemplate_Jewelry_Static_Amulet_AmuletOfAOgreHunter`)
  - OnEquip_DecreaseStatAtStatThreshold (ModifiedStat=IncomingDamage, StatToCheck=Health, ReferencedStat=MaxHealth) Threshold=0.1, StatIncrease=0.2
  - OnEquip_BonusDamageOnYourFullStat (Stat=Health, StatToCheck=MaxHealth) BonusDamageMultiplier=0.33, BonusDamageMultiplierGain=0.03
- **Always Accessorize, Darling** (`ItemTemplate_Jewelry_Static_Amulet_AlwaysAccessorize`)
  - OnEquip_IncreaseStatAtStatThreshold (ModifiedStat=HealthRegen, StatToCheck=Health, ReferencedStat=MaxHealth) Threshold=0.1, StatIncrease=1.0, ValueGain=0.1
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxHealth) ModifyValue=0.05, GainValue=0.01
- **All-Mother's Protection Amulet** (`ItemTemplate_Jewelry_Static_Amulet_AllMotherProtectionAmulet`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=10.0, GainValue=1.0
  - OnEquip_DecreaseStatAtStatThreshold (ModifiedStat=IncomingDamage, StatToCheck=Health, ReferencedStat=MaxHealth) Threshold=0.1, StatIncrease=0.25
- **The Adventurer's Essential** (`ItemTemplate_Jewelry_Static_Amulet_AdventurersEssential`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=5.0, GainValue=1.0
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=10.0, GainValue=1.0
  - OnEquip_IncreaseStat (StatEnum=EncumbranceLimit) ModifyValue=15.0, GainValue=1.0

### Rings

- **Warded Guardian Seal** (`ItemTemplate_Jewelry_Static_Ring_WardedGuardianSeal`)
  - OnEquip_IncreaseStatForMissingStat (ModifyStat=Armor, StatToCheck=Health, ReferencedStat=MaxHealth) ModifyValueMax=5.0
- **Ring of Blood Transmutation** (`ItemTemplate_Jewelry_Static_Ring_RingOfBloodTransmutation`)
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=30.0, GainValue=3.0
  - OnEquip_DecreaseStat (StatEnum=MaxHealth) ModifyValue=30.0, ValueGain=3.0
- **Ring of Arcane Transmuation** (`ItemTemplate_Jewelry_Static_Ring_RingOfArcaneTransmuation`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=30.0, GainValue=3.0
  - OnEquip_DecreaseStat (StatEnum=MaxMana) ModifyValue=30.0, ValueGain=3.0
- **Bloodthorn Ring** (`ItemTemplate_Jewelry_Static_Ring_BloodthornRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=25.0, GainValue=2.0
  - OnUnequip_DestroyItem
- **Berg's Ring** (`ItemTemplate_Jewelry_Static_Ring_BergRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=10.0, GainValue=1.0
- **Lesser Health Ring** (`ItemTemplate_Jewelry_Static_Ring_LesserHealthRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=5.0, GainValue=1.0
- **Sage Ring** (`ItemTemplate_Jewelry_Static_Ring_OrdinaryHealthRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=10.0, GainValue=1.0
- **Oldsteel Ring** (`ItemTemplate_Jewelry_Static_Ring_OldsteelRing`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxHealth) ModifyValue=0.03, GainValue=0.01
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxStamina) ModifyValue=0.03, GainValue=0.01
- **Kamelot's Ring** (`ItemTemplate_Jewelry_Static_Ring_KamelotRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=10.0, GainValue=1.0
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=5.0, GainValue=1.0
- **Health Ring** (`ItemTemplate_Jewelry_Static_Ring_HealthRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=20.0, GainValue=2.0
- **Arthur's Seal** (`ItemTemplate_Jewelry_Static_Ring_ArthurSeal`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxHealth) ModifyValue=0.05, GainValue=0.01
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxStamina) ModifyValue=0.05, GainValue=0.01

## DAMAGE REDUCTION (IncomingDamage)

### Amulets

- **Surgeheart Pendant** (`ItemTemplate_SOS_Jewelry_Static_Amulet_SurgeheartPendant`)
  - OnEquip_OverrideStat (StatEnum=DashRegenDurationMultiplier)
  - OnEquip_IncreaseStat (StatEnum=IncomingDamage) ModifyValue=0.5
- **Lifeline Loop** (`ItemTemplate_SOS_Jewelry_Static_Amulet_LifelineLoop`)
  - OnEquip_DecreaseStatAtStatThreshold (ModifiedStat=IncomingDamage, StatToCheck=Stamina, ReferencedStat=MaxStamina) Threshold=0.1, StatIncrease=0.5
- **Amulet of a Novice Ogre Hunter** (`ItemTemplate_Jewelry_Static_Amulet_AmuletOfAOgreHunter`)
  - OnEquip_DecreaseStatAtStatThreshold (ModifiedStat=IncomingDamage, StatToCheck=Health, ReferencedStat=MaxHealth) Threshold=0.1, StatIncrease=0.2
  - OnEquip_BonusDamageOnYourFullStat (Stat=Health, StatToCheck=MaxHealth) BonusDamageMultiplier=0.33, BonusDamageMultiplierGain=0.03
- **All-Mother's Protection Amulet** (`ItemTemplate_Jewelry_Static_Amulet_AllMotherProtectionAmulet`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=10.0, GainValue=1.0
  - OnEquip_DecreaseStatAtStatThreshold (ModifiedStat=IncomingDamage, StatToCheck=Health, ReferencedStat=MaxHealth) Threshold=0.1, StatIncrease=0.25

### Rings

- **Ring of Despair** (`ItemTemplate_Jewelry_Static_Ring_RingOfDespair`)
  - OnEquip_DecreaseStat (StatEnum=IncomingDamage) ModifyValue=0.2
  - OnEquip_DecreaseStat (StatEnum=IncomingHealing) ModifyValue=0.75, ValueGain=-0.025

## FLAT ARMOR

### Amulets

- **Sanguine Heart** (`ItemTemplate_Jewelry_Static_Amulet_SanguineHeart`)
  - OnEquip_SetGameplayVariable (StatEnum=Armor) VariableValue=1.0
  - OnEquip_SetGameplayVariable VariableValue=0.5
- **Glittering Stone** (`ItemTemplate_Jewelry_Static_Amulet_GlitteringStone`)
  - OnEquip_IncreaseStat (StatEnum=Armor) ModifyValue=2.0
- **Amulet Of Defense** (`ItemTemplate_Jewelry_Static_Amulet_AmuletOfDefence`)
  - OnEquip_IncreaseStat (StatEnum=Armor) ModifyValue=1.0, GainValue=0.2

### Rings

- **Warded Guardian Seal** (`ItemTemplate_Jewelry_Static_Ring_WardedGuardianSeal`)
  - OnEquip_IncreaseStatForMissingStat (ModifyStat=Armor, StatToCheck=Health, ReferencedStat=MaxHealth) ModifyValueMax=5.0
- **Ring of Protection** (`ItemTemplate_Jewelry_Static_Ring_RingOfProtection`)
  - OnEquip_IncreaseStat (StatEnum=Armor) ModifyValue=1.0, GainValue=0.2

## HEALTH REGEN

### Amulets

- **Gift for the Beloved** (`ItemTemplate_SOS_Jewelry_Static_Amulet_GiftForBeloved`)
  - OnEquip_IncreaseStatDuringCombat (StatEnum=HealthRegen) ModifyValue=1.0, GainValue=0.1
  - GiftForBeloved_StartStoryOnPickup
- **Repentance** (`ItemTemplate_Jewelry_Static_Amulet_Repentance`)
  - Weapon_OnDamageTakenApplyStatusHealOverTime (StatEnumGain=HealthRegen) HealPercent=0.5, HealPercentGain=0.015, Duration=8.0
- **Always Accessorize, Darling** (`ItemTemplate_Jewelry_Static_Amulet_AlwaysAccessorize`)
  - OnEquip_IncreaseStatAtStatThreshold (ModifiedStat=HealthRegen, StatToCheck=Health, ReferencedStat=MaxHealth) Threshold=0.1, StatIncrease=1.0, ValueGain=0.1
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxHealth) ModifyValue=0.05, GainValue=0.01

### Rings

- **Ancient Oak's Root +2** (`ItemTemplate_Jewelry_Tierable_Ring_AncientOaksRoot_Tier2`)
  - OnEquip_IncreaseStat (StatEnum=HealthRegen) ModifyValue=0.6
- **Ancient Oak's Root +1** (`ItemTemplate_Jewelry_Tierable_Ring_AncientOaksRoot_Tier1`)
  - OnEquip_IncreaseStat (StatEnum=HealthRegen) ModifyValue=0.4
- **Ancient Oak's Root** (`ItemTemplate_Jewelry_Tierable_Ring_AncientOaksRoot`)
  - OnEquip_IncreaseStat (StatEnum=HealthRegen) ModifyValue=0.2, GainValue=0.2

## STAMINA

### Amulets

- **Ironlung Stone +2** (`ItemTemplate_Jewelry_Tierable_Amulet_IronlungStone_Tier2`)
  - OnEquip_IncreaseStat (StatEnum=StaminaRegen) ModifyValue=50.0
- **Ironlung Stone +1** (`ItemTemplate_Jewelry_Tierable_Amulet_IronlungStone_Tier1`)
  - OnEquip_IncreaseStat (StatEnum=StaminaRegen) ModifyValue=30.0
- **Ironlung Stone** (`ItemTemplate_Jewelry_Tierable_Amulet_IronlungStone`)
  - OnEquip_IncreaseStat (StatEnum=StaminaRegen) ModifyValue=10.0, GainValue=1.0
- **Ironclad Pebble +2** (`ItemTemplate_Jewelry_Tierable_Amulet_IroncladPebble_Tier2`)
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=9.0
- **Ironclad Pebble +1** (`ItemTemplate_Jewelry_Tierable_Amulet_IroncladPebble_Tier1`)
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=6.0
- **Ironclad Pebble** (`ItemTemplate_Jewelry_Tierable_Amulet_IroncladPebble`)
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=3.0, GainValue=1.0
- **Glass Acorn** (`ItemTemplate_Jewelry_Static_Amulet_OcuinnLeave_GlassAcorn`)
  - OnEquip_IncreaseStatWhenUnarmored (StatEnum=MaxStamina) ModifyValue=100.0, ValueGain=10.0
- **Lifeline Loop** (`ItemTemplate_SOS_Jewelry_Static_Amulet_LifelineLoop`)
  - OnEquip_DecreaseStatAtStatThreshold (ModifiedStat=IncomingDamage, StatToCheck=Stamina, ReferencedStat=MaxStamina) Threshold=0.1, StatIncrease=0.5
- **The Traveler's Best Friend** (`ItemTemplate_Jewelry_Static_Amulet_TravelersBestFriend`)
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=5.0, GainValue=1.0
  - OnEquip_IncreaseStat (StatEnum=StaminaRegen) ModifyValue=3.0, GainValue=1.0
- **Remedy** (`ItemTemplate_Jewelry_Static_Amulet_Remedy`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxStamina) ModifyValue=0.15, GainValue=0.01
  - OnEquip_IncreaseStat (StatEnum=IncomingHealing) ModifyValue=0.25

### Rings

- **Ring of Energy Transmutation** (`ItemTemplate_Jewelry_Static_Ring_RingOfEnergyTransmutation`)
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=30.0, GainValue=3.0
  - OnEquip_DecreaseStat (StatEnum=MaxStamina) ModifyValue=30.0, ValueGain=3.0
- **Ring of Blood Transmutation** (`ItemTemplate_Jewelry_Static_Ring_RingOfBloodTransmutation`)
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=30.0, GainValue=3.0
  - OnEquip_DecreaseStat (StatEnum=MaxHealth) ModifyValue=30.0, ValueGain=3.0
- **Acrobat's Ring** (`ItemTemplate_Jewelry_Static_Ring_AcrobatRing`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxStamina) ModifyValue=0.15, GainValue=0.01
- **Athlete's Pride** (`ItemTemplate_Jewelry_Static_Ring_AthletesPride`)
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=5.0, GainValue=1.0
- **Spellrush Ring** (`ItemTemplate_Jewelry_Static_Ring_SpellrushRing`)
  - OnEquip_OnConsecutiveHit_IncreaseStatMultiply (StatEnum=ManaRegen) AddValue=0.04, ValueGain=0.0015, MaxHits=20.0
  - OnEquip_OnConsecutiveHit_IncreaseStatMultiply (StatEnum=StaminaRegen) AddValue=0.04, ValueGain=0.0015, MaxHits=20.0
- **Deer Ring** (`ItemTemplate_Jewelry_Static_Ring_OrdinaryStaminaRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=5.0, GainValue=1.0
- **Oldsteel Ring** (`ItemTemplate_Jewelry_Static_Ring_OldsteelRing`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxHealth) ModifyValue=0.03, GainValue=0.01
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxStamina) ModifyValue=0.03, GainValue=0.01
- **Kamelot's Ring** (`ItemTemplate_Jewelry_Static_Ring_KamelotRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=10.0, GainValue=1.0
  - OnEquip_IncreaseStat (StatEnum=MaxStamina) ModifyValue=5.0, GainValue=1.0
- **Fisherman's Seal** (`ItemTemplate_Jewelry_Static_Ring_FishermanSeal`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxStamina) ModifyValue=0.03, GainValue=0.01
- **Arthur's Seal** (`ItemTemplate_Jewelry_Static_Ring_ArthurSeal`)
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxHealth) ModifyValue=0.05, GainValue=0.01
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxStamina) ModifyValue=0.05, GainValue=0.01
- **Flame's Embrace** (`ItemTemplate_Jewelry_Static_Ring_FirestarterRing1`)
  - OnEquip_IncreaseStatMultiplyDuringStatus (StatEnum=ManaRegen) MultiplyValue=0.33, GainValue=0.011
  - OnEquip_IncreaseStatMultiplyDuringStatus (StatEnum=StaminaRegen) MultiplyValue=0.33, GainValue=0.011
  - OnEquip_FirestarterBurnDamageReduction DamageReduction=0.5, DamageReductionGain=0.015

## RANGED

### Amulets

- **Crow's Nest** (`ItemTemplate_SOS_Jewelry_Static_Amulet_SarrasAmulet_Ranged`)
  - OnEquip_IncreaseStat (StatEnum=RangedDamageMultiplier) ModifyValue=0.2, GainValue=0.01

### Rings

- **Sveinn Clan Ring** (`ItemTemplate_Jewelry_Static_Ring_SveinnRing`)
  - OnEquip_IncreaseStat (StatEnum=RangedDamageMultiplier) ModifyValue=0.1, GainValue=0.01
- **Strong Arrow** (`ItemTemplate_Jewelry_Static_Ring_StrongArrow`)
  - OnEquip_IncreaseStat (StatEnum=RangedDamageMultiplier) ModifyValue=0.25, GainValue=0.01
- **Ring of Reduced Conjuration** (`ItemTemplate_Jewelry_Static_Ring_RingOfReducedConjuration`)
  - OnEquip_SetGameplayVariable (StatEnum=RangedDamageMultiplier) VariableValue=0.2, VariableGain=0.01
- **Archer's Ring** (`ItemTemplate_Jewelry_Static_Ring_ArchersRing`)
  - OnEquip_IncreaseStat (StatEnum=RangedDamageMultiplier) ModifyValue=0.15, GainValue=0.01

## MAGIC

### Amulets

- **Amulet of the Waning Moon** (`ItemTemplate_Jewelry_Static_Amulet_AmuletOfTheWaningMoon_Unbalanced`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.1, GainValue=0.01
  - OnEquip_IncreaseStat (StatEnum=CriticalChance) ModifyValue=0.1
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxMana) ModifyValue=0.1, GainValue=0.01
- **Archdruid's Amulet** (`ItemTemplate_Jewelry_Static_Amulet_ArchdruidsAmulet_Unbalanced`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.2, GainValue=0.02
  - OnEquip_IncreaseStat (StatEnum=MagicCriticalChance) ModifyValue=0.2
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxMana) ModifyValue=0.2, GainValue=0.02
- **The Horned Warden's Amulet** (`ItemTemplate_Jewelry_Static_Amulet_TheHornedWardensAmulet_Unbalanced`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.15, GainValue=0.01
  - OnEquip_IncreaseStat (StatEnum=MagicCriticalChance) ModifyValue=0.15
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxMana) ModifyValue=0.15, GainValue=0.01
- **Pendant of Endless Study** (`ItemTemplate_Jewelry_Static_Amulet_PendantOfEndlessStudy`)
  - OnEquip_IncreaseStatByMultiplyingStat (ModifiedStat=MagicStrength, ReferencedStat=Level) StatMultiplier=0.01, StatMultiplierGain=0.001
- **Drowned Lantern** (`ItemTemplate_SOS_Jewelry_Static_Amulet_SarrasAmulet_Magic`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.25, GainValue=0.02
- **Novice Amulet** (`ItemTemplate_Jewelry_Static_Amulet_NoviceAmulet`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.05, GainValue=0.01

### Rings

- **Theud Clan Ring** (`ItemTemplate_Jewelry_Static_Ring_TheudRing`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.07, GainValue=0.01
- **The Perfect Ring for a Mage** (`ItemTemplate_Jewelry_Static_Ring_OrdinaryMagicDamageRing`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.05, GainValue=0.01

## MANA

### Amulets

- **Amulet of the Waning Moon** (`ItemTemplate_Jewelry_Static_Amulet_AmuletOfTheWaningMoon_Unbalanced`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.1, GainValue=0.01
  - OnEquip_IncreaseStat (StatEnum=CriticalChance) ModifyValue=0.1
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxMana) ModifyValue=0.1, GainValue=0.01
- **Archdruid's Amulet** (`ItemTemplate_Jewelry_Static_Amulet_ArchdruidsAmulet_Unbalanced`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.2, GainValue=0.02
  - OnEquip_IncreaseStat (StatEnum=MagicCriticalChance) ModifyValue=0.2
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxMana) ModifyValue=0.2, GainValue=0.02
- **The Horned Warden's Amulet** (`ItemTemplate_Jewelry_Static_Amulet_TheHornedWardensAmulet_Unbalanced`)
  - OnEquip_IncreaseStat (StatEnum=MagicStrength) ModifyValue=0.15, GainValue=0.01
  - OnEquip_IncreaseStat (StatEnum=MagicCriticalChance) ModifyValue=0.15
  - OnEquip_IncreaseStatMultiply (StatEnum=MaxMana) ModifyValue=0.15, GainValue=0.01
- **Druid's Wishgem +2** (`ItemTemplate_Jewelry_Tierable_Amulet_DruidsWishgem_Tier2`)
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=50.0
- **Druid's Wishgem +1** (`ItemTemplate_Jewelry_Tierable_Amulet_DruidsWishgem_Tier1`)
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=30.0
- **Druid's Wishgem** (`ItemTemplate_Jewelry_Tierable_Amulet_DruidsWishgem`)
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=15.0, GainValue=1.0
- **Arcane Weaver's Prism +2** (`ItemTemplate_Jewelry_Tierable_Amulet_ArcaneWeaversPrism_Tier2`)
  - OnEquip_IncreaseStat (StatEnum=ManaRegen) ModifyValue=3.0
- **Arcane Weaver's Prism +1** (`ItemTemplate_Jewelry_Tierable_Amulet_ArcaneWeaversPrism_Tier1`)
  - OnEquip_IncreaseStat (StatEnum=ManaRegen) ModifyValue=2.0
- **Arcane Weaver's Prism** (`ItemTemplate_Jewelry_Tierable_Amulet_ArcaneWeaversPrism`)
  - OnEquip_IncreaseStat (StatEnum=ManaRegen) ModifyValue=1.0, GainValue=1.0
- **Triskelion Core** (`ItemTemplate_Jewelry_Static_Amulet_TriskelionCore`)
  - OnEquip_ExchangeHpManaStamina (Stat=Mana, StatToCheck=MaxMana) BonusDamageMultiplier=0.5
- **The Adventurer's Essential** (`ItemTemplate_Jewelry_Static_Amulet_AdventurersEssential`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=5.0, GainValue=1.0
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=10.0, GainValue=1.0
  - OnEquip_IncreaseStat (StatEnum=EncumbranceLimit) ModifyValue=15.0, GainValue=1.0

### Rings

- **Ring of Energy Transmutation** (`ItemTemplate_Jewelry_Static_Ring_RingOfEnergyTransmutation`)
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=30.0, GainValue=3.0
  - OnEquip_DecreaseStat (StatEnum=MaxStamina) ModifyValue=30.0, ValueGain=3.0
- **Ring of Arcane Transmuation** (`ItemTemplate_Jewelry_Static_Ring_RingOfArcaneTransmuation`)
  - OnEquip_IncreaseStat (StatEnum=MaxHealth) ModifyValue=30.0, GainValue=3.0
  - OnEquip_DecreaseStat (StatEnum=MaxMana) ModifyValue=30.0, ValueGain=3.0
- **Tide's Rise** (`ItemTemplate_SOS_Jewelry_Static_Ring_TidesRise`)
  - OnEquip_IncreaseStatAboveOrEqualStatThreshold_ImprovedWhenWearingRIng (StatEnum=ManaRegen, ReferencedStat=Mana) Threshold=0.5, ModifyValue=5.0, ItemMultiplier=1.5
- **Tide's Rest** (`ItemTemplate_SOS_Jewelry_Static_Ring_TidesRest`)
  - OnEquip_IncreaseStatBelowStatThreshold_ImprovedWhenWearingRIng (StatEnum=ManaRegen, ReferencedStat=Mana) Threshold=0.5, ModifyValue=5.0, ItemMultiplier=1.5
- **Driftglass** (`ItemTemplate_SOS_Jewelry_Static_Ring_Driftglass`)
  - OnEquip_IncreaseStatEveryFewSecondsDuringCombat (Stat=MaxMana) StatValue=0.05, Interval=5.0, MaxSteps=10.0
- **Bug-Shaped Ring** (`ItemTemplate_SOS_Jewelry_Static_Ring_BugShapedRing`)
  - OnEquip_IncreaseStatMultiply (StatEnum=ManaRegen) ModifyValue=0.03, GainValue=0.002
- **Novice Ring** (`ItemTemplate_Jewelry_Static_Ring_NoviceRing`)
  - OnEquip_IncreaseStat (StatEnum=ManaRegen) ModifyValue=1.0, GainValue=0.1
- **Lesser Mana Ring** (`ItemTemplate_Jewelry_Static_Ring_LesserManaRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=10.0, GainValue=1.0
- **Will-forged Band** (`ItemTemplate_Jewelry_Static_Ring_WillforgedBand`)
  - Weapon_WillforgedBand (StatEnum=MaxMana) ModifyValue=25.0
- **Spellrush Ring** (`ItemTemplate_Jewelry_Static_Ring_SpellrushRing`)
  - OnEquip_OnConsecutiveHit_IncreaseStatMultiply (StatEnum=ManaRegen) AddValue=0.04, ValueGain=0.0015, MaxHits=20.0
  - OnEquip_OnConsecutiveHit_IncreaseStatMultiply (StatEnum=StaminaRegen) AddValue=0.04, ValueGain=0.0015, MaxHits=20.0
- **Burdock Ring** (`ItemTemplate_Jewelry_Static_Ring_OrdinaryManaRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=15.0, GainValue=1.0
- **Mana Ring** (`ItemTemplate_Jewelry_Static_Ring_ManaRing`)
  - OnEquip_IncreaseStat (StatEnum=MaxMana) ModifyValue=25.0, GainValue=2.0
- **Flame's Embrace** (`ItemTemplate_Jewelry_Static_Ring_FirestarterRing1`)
  - OnEquip_IncreaseStatMultiplyDuringStatus (StatEnum=ManaRegen) MultiplyValue=0.33, GainValue=0.011
  - OnEquip_IncreaseStatMultiplyDuringStatus (StatEnum=StaminaRegen) MultiplyValue=0.33, GainValue=0.011
  - OnEquip_FirestarterBurnDamageReduction DamageReduction=0.5, DamageReductionGain=0.015
