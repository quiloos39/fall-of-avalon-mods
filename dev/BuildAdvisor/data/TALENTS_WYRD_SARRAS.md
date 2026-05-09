# Wyrd Arthur + Sarras talent reference


## TalentTree_WyrdArthur (points spent: 3)

### (root)

- ⬜ **Wyrdskill_PrologueUpgrade_DeflectProjectiles** (0/1) — `Wyrdskill_PrologueUpgrade_DeflectProjectiles`
  - parent: `Wyrdskill_Prologue`
  - R1: When using King's Soul, you deflect all incoming projectiles.

- ✅ **Wyrdskill_PrologueUpgrade_PreventDeathActivateSkill** (1/1) — `Wyrdskill_PrologueUpgrade_PreventDeathActivateSkill`
  - parent: `Wyrdskill_Prologue`
  - R1: If King's Soul is fully charged, automatically use it when you would receive lethal damage.

- ✅ **Wyrdskill_PrologueUpgrade_SpawnWyrdsoulEssence** (1/1) — `Wyrdskill_PrologueUpgrade_SpawnWyrdsoulEssence`
  - parent: `Wyrdskill_Prologue`
  - R1: Dealing Damage has a small chance to create a Wyrd Essence, which can be collected to regenerate a portion of King's Soul.

- ⬜ **Wyrdskill_ExcaliburUpgrade_FinishersRestoreMorePower** (0/1) — `Wyrdskill_ExcaliburUpgrade_FinishersRestoreMorePower`
  - parent: `Wyrdskill_Excalibur`
  - R1: Finishers restore 400 % more King's Soul.

- ⬜ **Wyrdskill_ExcaliburUpgrade_KillsIncreaseDMG** (0/1) — `Wyrdskill_ExcaliburUpgrade_KillsIncreaseDMG`
  - parent: `Wyrdskill_Excalibur`
  - R1: When using King's Soul, your Damage increases by 50 % for each enemy killed.

- ⬜ **Wyrdskill_ExcaliburUpgrade_ReceiveLessDMG** (0/1) — `Wyrdskill_ExcaliburUpgrade_ReceiveLessDMG`
  - parent: `Wyrdskill_Excalibur`
  - R1: When using King's Soul, you receive 50 % less Damage.

- ⬜ **Wyrdskill_ShieldUpgrade_DoubleBlasta** (0/1) — `Wyrdskill_ShieldUpgrade_DoubleBlasta`
  - parent: `Wyrdskill_Shield`
  - R1: You create another blast of energy when the King's Soul ends.

- ✅ **Wyrdskill_ShieldUpgrade_EchoDamageDealt** (1/1) — `Wyrdskill_ShieldUpgrade_EchoDamageDealt`
  - parent: `Wyrdskill_Shield`
  - R1: Enemies take the amount of Damage you have dealt to them when using King's Soul once again after it ends.

- ⬜ **Wyrdskill_ShieldUpgrade_IncreaseStatByDamageTaken** (0/1) — `Wyrdskill_ShieldUpgrade_IncreaseStatByDamageTaken`
  - parent: `Wyrdskill_Shield`
  - R1: When using King's Soul, a Wyrd shield collects 10 % of the Damage dealt to you. When King's Soul ends you gain **Mana Shield** equal to accumulated Damage for 30s.

- ⬜ **Wyrdskill_HelmetUpgrade_IncreasedDMGandSpellPower** (0/1) — `Wyrdskill_HelmetUpgrade_IncreasedDMGandSpellPower`
  - parent: `Wyrdskill_Helmet`
  - R1: When using King's Soul, your Physical Damage and Spell Power increases by 50 %.

- ⬜ **Wyrdskill_HelmetUpgrade_KillingProlongsWyrdskill** (0/1) — `Wyrdskill_HelmetUpgrade_KillingProlongsWyrdskill`
  - parent: `Wyrdskill_Helmet`
  - R1: Killing enemies restores a portion of King's Soul.

- ⬜ **Wyrdskill_HelmetUpgrade_LowerCosts** (0/1) — `Wyrdskill_HelmetUpgrade_LowerCosts`
  - parent: `Wyrdskill_Helmet`
  - R1: When using King's Soul, your actions do not cost Mana or Stamina and do not use up normal quality ammunition.


## TalentTree_Sarras (points spent: 0)

### Mystic

- ⬜ **Mana Spring** (0/1) — `SOS_Talent_OnDamageDealt_RegenerateStatOverDuration`
  - R1: Regenerates 2 % of the last Damage Dealt as Mana over 6s.

- ⬜ **Melee Macabre** (0/1) — `SOS_Talent_ConsecutiveHitsIncreaseSummonDamage`
  - parent: `SOS_Talent_OnDamageDealt_RegenerateStatOverDuration`
  - R1: Each of your **Consecutive Hit** increases your Summons Physical Damage by 2 %.

- ⬜ **Allies Together Strong** (0/1) — `SOS_Talent_IncreasedAllyDamageByAllyCount`
  - parent: `SOS_Talent_ConsecutiveHitsIncreaseSummonDamage`
  - R1: You and all friendly units deal increased Damage by 5 % for each friendly unit in combat.

- ⬜ **Parting Gift** (0/1) — `SOS_Talent_OnSummonDeathDropManaEssence`
  - parent: `SOS_Talent_ConsecutiveHitsIncreaseSummonDamage`
  - R1: Your Summons leave Mana Essence on death, which can be collected to restore 30 Mana.

- ⬜ **Summoner's Curse** (0/1) — `SOS_Talent_SummonsCurseTheirKiller`
  - parent: `SOS_Talent_OnSummonDeathDropManaEssence`
  - R1: Your Summons apply a curse to the enemy that killed them, dealing 400 % of their Attack Damage over 8s.

- ⬜ **Ebb and Flow** (0/1) — `SOS_Talent_IncreaseLightCastAndHeavyCastSpells`
  - parent: `SOS_Talent_OnDamageDealt_RegenerateStatOverDuration`
  - R1: Spells that deal Damage in an area have 20% increased Damage./Spells that do not deal Damage in an area apply a **Stack of Frailty** on enemies hit.

- ⬜ **Overcasting** (0/1) — `SOS_Talent_IncreaseDamageOnHeavyCastLonger`
  - parent: `SOS_Talent_IncreaseLightCastAndHeavyCastSpells`
  - R1: Holding fully Charged Spells up to 4s increases their Base Maximum Damage by up to 40 %.

- ⬜ **Elemental Exposure** (0/1) — `SOS_Talent_FrailtyIncreasesBuildup`
  - parent: `SOS_Talent_IncreaseLightCastAndHeavyCastSpells`
  - R1: Each **Stack of Frailty** on an enemy increases the speed of applying an elemental Status to them by up to 50 %.

- ⬜ **Elemental Crescendo** (0/1) — `SOS_Talent_OnKill_SpreadStatuses`
  - parent: `SOS_Talent_FrailtyIncreasesBuildup`
  - R1: When an enemy dies when **Burning**, **Poisoned**, ** Chilled** or **Frozen**, they explode spreading their elemental statuses to nearby enemies.

- ⬜ **Viral Elements** (0/1) — `SOS_Talent_SpreadActiveStatuses`
  - parent: `SOS_Talent_FrailtyIncreasesBuildup`
  - R1: Enemies with an active elemental Status spread it to nearby enemies within 4m.

- ⬜ **Recovery Focus** (0/1) — `SOS_Talent_IncreaseStatOnAbstractEquipped`
  - parent: `SOS_Talent_OnDamageDealt_RegenerateStatOverDuration`
  - R1: Having a Wand, Soul Cube, Shield or a Melee Weapon equipped increases Mana Regeneration by 20 %

- ⬜ **Arcane Smite** (0/1) — `SOS_Talent_IncreaseDamageByStatAndPayManaCost`
  - parent: `SOS_Talent_IncreaseStatOnAbstractEquipped`
  - R1: Every Melee Attack is magically empowered, dealing increased Damage by 50 % of your Spell Power, but also costing you Mana equal to 200 % of its Stamina Cost.

- ⬜ **Relic Hunter** (0/1) — `SOS_Talent_IncreaseStatForEachRelic`
  - parent: `SOS_Talent_IncreaseStatOnAbstractEquipped`
  - R1: Each socketed Relic in your equipped Items increases your Spell Power by 7 % and Max Mana by 2 %.

- ⬜ **Infinity Cubes** (0/1) — `SOS_Talent_OnSoulCubeCastDecreaseStat`
  - parent: `SOS_Talent_IncreaseStatForEachRelic`
  - R1: Using a Soul Cube decreases Mana Cost by 50 % for 6s.

- ⬜ **Battlemage** (0/1) — `SOS_Talent_MeleeWeaponsAndShieldsAreNowWands`
  - parent: `SOS_Talent_IncreaseStatForEachRelic`
  - R1: Melee Weapons and Shields are considered as Wands.

### Rogue

- ⬜ **Frailty Broker** (0/1) — `SOS_Talent_ApplyFrailty`
  - R1: Dealing Damage to an enemy applies a **Stack of Frailty**./Heavy Attacks and **Weak Spot Hits** apply an additional **Stack of Frailty**.

- ⬜ **Frailty Expert** (0/1) — `SOS_Talent_ApplyFrailtyCritical`
  - parent: `SOS_Talent_ApplyFrailty`
  - R1: **Critical Hits** apply an additional **Stack of Frailty**.

- ⬜ **Skirmisher Charge** (0/1) — `SOS_Talent_OnRangedDamageBuffMelee`
  - parent: `SOS_Talent_ApplyFrailtyCritical`
  - R1: Gain 35 % increased Attack Speed for 4s when Dealing Damage with a Ranged Weapon.

- ⬜ **Skirmisher Retreat** (0/1) — `SOS_Talent_OnMeleeDamageBuffRanged`
  - parent: `SOS_Talent_ApplyFrailtyCritical`
  - R1: Gain 7 % increased Ranged Critical Chance for 4s when Dealing Damage with a Melee Weapon.

- ⬜ **Chink in the Armor** (0/1) — `SOS_Talent_OnDamage_ConsumeFrailtyAndApplyAnotherStatusBasedOnStacks`
  - parent: `SOS_Talent_ApplyFrailtyCritical`
  - R1: Pushing an enemy consumes all **Stacks of Frailty**, **Breaking enemy Armor** for up to 15s based on consumed stacks.

- ⬜ **Riposte** (0/1) — `SOS_Talent_DamageDodgedIncreasesCritChance`
  - parent: `SOS_Talent_OnDamage_ConsumeFrailtyAndApplyAnotherStatusBasedOnStacks`
  - R1: Successfully Dodging an attack with a Dash increases Critical Chance by 10 % for 8s.

- ⬜ **Dependable Luck** (0/1) — `SOS_Talent_NonCriticalHitsIncreaseStatResetOnCriticalHit`
  - parent: `SOS_Talent_ApplyFrailty`
  - R1: Each Non-**Critical Hit** increases Critical Chance by 5 %. / Resets after scoring a **Critical Hit**.

- ⬜ **Death From Nowhere** (0/1) — `SOS_Talent_KillingWithSneakIncreasesSneakDamage`
  - parent: `SOS_Talent_NonCriticalHitsIncreaseStatResetOnCriticalHit`
  - R1: Increases Sneak Damage by 50 %./Kills with a Sneak Attack increase Sneak Damage by another 50 % for 10s.

- ⬜ **Perfect Draw** (0/1) — `SOS_Talent_PerfectDraw`
  - parent: `SOS_Talent_NonCriticalHitsIncreaseStatResetOnCriticalHit`
  - R1: Releasing an Arrow within 0.4s of fully drawing your Bow counts as a Perfect Draw, which increases the Arrow's Base Damage by 65 %.

- ⬜ **Speed Demon** (0/1) — `SOS_Talent_IncreaseStatByNearbyStatus`
  - parent: `SOS_Talent_PerfectDraw`
  - R1: Each **Frailty Stack** on nearby enemies increases your Attack Speed by 1 % and Bow Draw Speed by 1 %.

- ⬜ **Perfect Pierce** (0/1) — `SOS_Talent_PerfectDrawPierce`
  - parent: `SOS_Talent_PerfectDraw`
  - R1: Perfectly Drawn Arrows pierce through 1 additional enemy./Arrows deal 100 % increased Damage for every enemy Pierced.

- ⬜ **Rising Exhaustion** (0/1) — `SOS_Talent_OnDamage_LowerTargetStatBasedOnStatusStacks`
  - parent: `SOS_Talent_ApplyFrailty`
  - R1: Dealing Damage to an enemy lowers their Stamina by up to 10 % of the Damage dealt based on the current **Stacks of Frailty**.

- ⬜ **Coup de Grâce** (0/1) — `SOS_Talent_OnDamage_KillTargetUnderStatThresholdAndStaggered`
  - parent: `SOS_Talent_OnDamage_LowerTargetStatBasedOnStatusStacks`
  - R1: Dealing Damage to a non-boss enemy who is **Staggered** and has less than 33 % of their Max Health instantly kills them.

- ⬜ **Wrecking Ball** (0/1) — `SOS_Talent_StoreOverkillDamage`
  - parent: `SOS_Talent_OnDamage_LowerTargetStatBasedOnStatusStacks`
  - R1: Excess Damage from a killing blow is stored to be dealt on your next Damage done within 10s.

- ⬜ **Less Waste** (0/1) — `SOS_Talent_FrailtyStacksCarryOverToNearbyEnemies`
  - parent: `SOS_Talent_StoreOverkillDamage`
  - R1: Killing an enemy with **Frailty** on them, puts 50 % of their stacks to a nearby enemy.

- ⬜ **Instinct of a Killer** (0/1) — `SOS_Talent_OnDamage_KillTargetUnderStatComparedToDamage`
  - parent: `SOS_Talent_StoreOverkillDamage`
  - R1: Dealing Damage to a non-boss enemy higher than 80 % of their current Health instantly kills them.

### Warrior

- ⬜ **Strength In Numbers** (0/1) — `SOS_Talent_IncreasedDamageForEveryNearbyEnemy`
  - R1: Each enemy within a radius of 6m increases your Physical Damage by 10 %.

- ⬜ **Menace of the Weary** (0/1) — `SOS_Talent_IncreaseDamageBasedOnTargetStamina`
  - parent: `SOS_Talent_IncreasedDamageForEveryNearbyEnemy`
  - R1: Enemies take up to 33 % increased Damage based on missing Stamina. / **Staggered** enemies count as missing 100 % Stamina.

- ⬜ **Deadly Rejuvenation** (0/1) — `SOS_Talent_OnKill_LowerStaminaCost`
  - parent: `SOS_Talent_IncreaseDamageBasedOnTargetStamina`
  - R1: Killing an enemy lowers Stamina Cost of your next Attack done within 4s by 100 %.

- ⬜ **The Heaviest of Attacks** (0/1) — `SOS_Talent_IncreaseDamageOnHeavyAttackLonger`
  - parent: `SOS_Talent_IncreaseDamageBasedOnTargetStamina`
  - R1: Holding Heavy Attacks up to 3s increases their Damage Dealt by up to 100 %.

- ⬜ **Juggernaut** (0/1) — `SOS_Talent_DecreaseDamageTakenDuringAttacks`
  - parent: `SOS_Talent_IncreaseDamageOnHeavyAttackLonger`
  - R1: Reduces Melee Damage Taken by 33 % during your own Melee Attacks.

- ⬜ **Flooding Force** (0/1) — `SOS_Talent_CritsAreDealtToNearbyEnemies`
  - parent: `SOS_Talent_IncreaseDamageOnHeavyAttackLonger`
  - R1: 50 % of Damage from a **Critical Hit** is dealt to nearby enemies.

- ⬜ **Desire For Speed** (0/1) — `SOS_Talent_IncreasedAttackSpeedForEveryNearbyEnemy`
  - parent: `SOS_Talent_IncreasedDamageForEveryNearbyEnemy`
  - R1: Each enemy within a radius of 6m increases your Attack Speed by 8 %.

- ⬜ **Useful Overload** (0/1) — `SOS_Talent_IncreasesWeaponMaxDMGByArmorWeight`
  - parent: `SOS_Talent_IncreasedAttackSpeedForEveryNearbyEnemy`
  - R1: Increases weapon's maximum damage output by 1 for every kilogram of your armor weight.

- ⬜ **Kickstart** (0/1) — `SOS_Talent_HeavyAttackIncreaseAttackSpeed`
  - parent: `SOS_Talent_IncreasedAttackSpeedForEveryNearbyEnemy`
  - R1: Heavy Attacks increase Attack Speed by 25 % for 4s.

- ⬜ **Death From Above** (0/1) — `SOS_Talent_JumpAttacksAreStronger`
  - parent: `SOS_Talent_HeavyAttackIncreaseAttackSpeed`
  - R1: Melee Attacks done when jumping or falling are always **Critical**, but also decrease your Attack Speed and Movement Speed by 50% for 3s afterwards.

- ⬜ **Vicious Gamble** (0/1) — `SOS_Talent_DecreaseMinDamageIncreaseMaxDamage`
  - parent: `SOS_Talent_HeavyAttackIncreaseAttackSpeed`
  - R1: All Melee and Ranged weapons have 25 % increased base maximum Damage, but 25 % decreased base minimum Damage.

- ⬜ **Endurance Fighter** (0/1) — `SOS_Talent_LoweredStaminaCostForEveryNearbyEnemy`
  - parent: `SOS_Talent_IncreasedDamageForEveryNearbyEnemy`
  - R1: Each enemy within a radius of 6m decreases your Stamina Cost by 5 %.

- ⬜ **Moment to Breathe** (0/1) — `SOS_Talent_DecreaseDamageTakenDuringKnockdownAndOverexert`
  - parent: `SOS_Talent_LoweredStaminaCostForEveryNearbyEnemy`
  - R1: Reduce the first received Damage by 100 % when you are **Knocked Down** or **Overexerted**.

- ⬜ **Strong Resolve** (0/1) — `SOS_Talent_DebuffDuration`
  - parent: `SOS_Talent_LoweredStaminaCostForEveryNearbyEnemy`
  - R1: Lowers duration of all Debuffs on you by 33 %.

- ⬜ **Crushing Impact** (0/1) — `SOS_Talent_PushingDealsDamageAndStunsNearbyEnemies`
  - parent: `SOS_Talent_DebuffDuration`
  - R1: Push deals 50 % of Weapon Damage and **Stuns** all enemies in 6m range./Can only occur once every 15s.

