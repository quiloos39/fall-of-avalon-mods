# Armor catalog — best per slot

From `CatalogProbe.cs` (n=1148 armor templates with armor>0).

### IMPORTANT: there are NO armor set bonuses in this game.

Investigated `ItemSet.cs` — it's a *character-creator preset* loader, not a worn-set bonus mechanic. Each armor piece's `OnEquip_*` effects fire individually; there's no "wear 4 pieces of X" reward. So mixing armor types is fine; the only synergy is **proficiency XP** (heavy/medium/light gain XP per piece worn).


## Helmet — top 15 by base armor

| Rank | Name | Tier | Armor | +/lvl | Wt | Reqs | Template |
|---:|---|---|---:|---:|---:|---|---|
| 1 | Plaguewraith Head | Medium T6 | 12 | +1 | 7.0 | str5 end15 pra5 | `ItemTemplate_Armor_Medium_T6_Head_PlaguewraithHead` |
| 2 | Champion of Tuathan Helm | Head ChampionOfTuathanHelm | 10 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Head_ChampionOfTuathanHelm` |
| 3 | Titansteel Helmet | Heavy T5 | 10 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Head_UlfrTitansteelHelmet` |
| 4 | Winged Cavalier Helmet | Medium T5 | 10 | +1 | 2.0 | str7 end10 per4 | `ItemTemplate_Armor_Medium_T5_Head_WingedCavalierHelmet` |
| 5 | Wayfarer's Circlet | Medium T5 | 10 | +1 | 1.0 | str5 dex5 end11 | `ItemTemplate_Armor_Medium_T5_Head_WayfarersCirclet_Hidden` |
| 6 | Blackwater Helm | Heavy T5 | 10 | +1 | 3.5 | str5 end10 | `ItemTemplate_Armor_Heavy_T5_Head_FemaleDrownedKnightHelmet` |
| 7 | Blackwater Helm | Heavy T5 | 10 | +1 | 3.5 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Head_NormalDrownedKnightHelmet` |
| 8 | Blackwater Helm | Heavy T5 | 10 | +1 | 3.5 | str10 | `ItemTemplate_Armor_Heavy_T5_Head_DrownedKnightHelmet` |
| 9 | Wayfarer's Circlet | Medium T5 | 10 | +1 | 1.0 | str5 dex5 end11 | `ItemTemplate_Armor_Medium_T5_Head_WayfarersCirclet` |
| 10 | Crystal Walker Helmet | Medium T5 | 10 | +1 | 5.0 | str5 end15 | `ItemTemplate_Armor_Medium_T5_Head_CrystalWalkerHelmet` |
| 11 | Úlfr Warsworn Helm | Heavy T5 | 10 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Head_UlfrWarswornHelm` |
| 12 | Captain Lir's Tricorn | Medium T5 | 10 | +1 | 3.0 | end10 pra5 | `ItemTemplate_Armor_Medium_T5_Head_CaptainLirsTricorn` |
| 13 | Head of Scion of the Depths | Medium T5 | 10 | +1 | 2.0 | end10 pra5 | `ItemTemplate_Armor_Medium_T5_Head_ScionsOfTheDeepHead` |
| 14 | Head of Archivist | Medium T5 | 10 | +1 | 2.0 | end10 pra5 | `ItemTemplate_Armor_Medium_T5_Head_ArchivistHead` |
| 15 | Volker's Bloodrage Helmet | Heavy T5 | 9 | +1 | 4.5 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Head_VolkerBloodrageHelmet` |

## Cuirass — top 15 by base armor

| Rank | Name | Tier | Armor | +/lvl | Wt | Reqs | Template |
|---:|---|---|---:|---:|---:|---|---|
| 1 | Shadowblade's Lamellar Armor | Light T0 | 80 | +1 | 1.0 | - | `ItemTemplate_Armor_Light_T0_Torso_ShadowbladesLamellarArmor` |
| 2 | Steel Chestplate | Heavy T4 | 15 | +1 | 10.0 | str9 end9 | `ItemTemplate_Armor_Heavy_T4_Body_SteelChestplate` |
| 3 | Ulfr's Titansteel Armor | Heavy T5 | 15 | +1 | 10.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Body_UlfrTitansteelArmor` |
| 4 | Abyssal Heir Cuirass | Heavy T4 | 15 | +1 | 10.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T4_Body_AbyssalHeirCuirass` |
| 5 | Kelpweave Garb | Heavy T4 | 15 | +1 | 10.0 | end5 pra5 | `ItemTemplate_Armor_Heavy_T4_Body_ReefboundVest3` |
| 6 | Kelpweave Garb | Heavy T4 | 15 | +1 | 10.0 | end5 pra5 | `ItemTemplate_Armor_Heavy_T4_Body_ReefboundVest2` |
| 7 | Kelpweave Garb | Heavy T4 | 15 | +1 | 10.0 | end5 pra5 | `ItemTemplate_Armor_Heavy_T4_Body_ReefboundVest1` |
| 8 | Úlfr Warsworn Chestplate | Heavy T5 | 15 | +1 | 10.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Body_UlfrWarswornChestplate` |
| 9 | Sir Lancelot's Chestplate | Heavy T6 | 15 | +1 | 3.0 | str15 dex8 end15 | `ItemTemplate_Armor_Heavy_T6_Body_SirLancelotsChestplate` |
| 10 | The Green Knight's Chestplate | Medium T5 | 15 | +1 | 5.0 | str10 dex9 end15 per5 | `ItemTemplate_Armor_Medium_T5_Body_TheGreenKnightsChestplate` |
| 11 | Duel Knight Armor | Heavy T6 | 14 | +1 | 13.0 | str8 dex8 end9 | `ItemTemplate_Armor_Heavy_T6_Body_DrastusArmor` |
| 12 | Volker's Bloodrage Armor | Heavy T5 | 14 | +1 | 8.5 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Body_VolkerBloodrageArmor` |
| 13 | Winged Cavalier Breastplate | Medium T5 | 14 | +1 | 10.0 | str7 end10 per4 | `ItemTemplate_Armor_Medium_T5_Body_WingedCavalierBreastplate` |
| 14 | Sir Gawain's Weathered Cuirass | Heavy T6 | 14 | +1 | 4.0 | str8 dex8 end15 per8 | `ItemTemplate_Armor_Heavy_T6_Body_SirGawainsCuirass_Dummy` |
| 15 | Crystal Walker Chestplate | Medium T5 | 14 | +1 | 9.0 | str5 end15 | `ItemTemplate_Armor_Medium_T5_Body_CrystalWalkerChestplate` |

## Gauntlets — top 15 by base armor

| Rank | Name | Tier | Armor | +/lvl | Wt | Reqs | Template |
|---:|---|---|---:|---:|---:|---|---|
| 1 | Ulfr's Titansteel Gauntlets | Heavy T5 | 10 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Arms_UlfrTitansteelGauntlets` |
| 2 | Winged Cavalier Gauntlets | Medium T5 | 10 | +1 | 5.0 | str7 end10 per4 | `ItemTemplate_Armor_Medium_T5_Arms_WingedCavalierGauntlets` |
| 3 | Abyssal Heir Gauntlets | Heavy T4 | 10 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T4_Arms_AbyssalHeirGauntlets` |
| 4 | Leather Gloves | Heavy T4 | 10 | +1 | 5.0 | end5 pra5 | `ItemTemplate_Armor_Heavy_T4_Arms_ReefboundGauntlets` |
| 5 | Crystal Walker Bracers | Medium T5 | 10 | +1 | 3.0 | str5 end15 | `ItemTemplate_Armor_Medium_T5_Arms_CrystalWalkerBracers` |
| 6 | Úlfr Warsworn Gauntlets | Heavy T5 | 10 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Arms_UlfrWarswornGauntlets` |
| 7 | Champion of Tuathan Gauntlets | Arms ChampionOfTuathanGauntlets | 9 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Arms_ChampionOfTuathanGauntlets` |
| 8 | Volker's Bloodrage Gauntlets | Heavy T5 | 9 | +1 | 4.5 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Arms_VolkerBloodrageGauntlets` |
| 9 | Wayfarer's Cuffs | Medium T5 | 9 | +1 | 1.0 | str5 dex5 end11 | `ItemTemplate_Armor_Medium_T5_Arms_WayfarersCuffs_Hidden` |
| 10 | Ashen Veil Bracers | Medium T5 | 9 | +1 | 1.0 | str6 dex6 end7 per6 | `ItemTemplate_Armor_Medium_T5_Arms_AshenVeilBracers` |
| 11 | Wayfarer's Cuffs | Medium T5 | 9 | +1 | 1.0 | str5 dex5 end11 | `ItemTemplate_Armor_Medium_T5_Arms_WayfarersCuffs` |
| 12 | Volker Warborn Gloves | Heavy T5 | 9 | +1 | 4.5 | str12 end8 | `ItemTemplate_Armor_Heavy_T5_Arms_VolkerWarbornGloves` |
| 13 | Harbinger of Night Vambraces | Arms HarbingerOfNightVambraces | 8 | +1 | 4.0 | str10 end10 | `ItemTemplate_Armor_Arms_HarbingerOfNightVambraces` |
| 14 | Ulfr's Stormforged Gauntlets | Heavy T4 | 8 | +1 | 4.0 | str8 end8 | `ItemTemplate_Armor_Heavy_T4_Arms_UlfrStormforgedGauntlets` |
| 15 | Duel Knight Gloves | Light T5 | 8 | +1 | 4.5 | str7 dex7 end20 | `ItemTemplate_Armor_Light_T5_Arms_DuelKnightGloves` |

## Greaves — top 15 by base armor

| Rank | Name | Tier | Armor | +/lvl | Wt | Reqs | Template |
|---:|---|---|---:|---:|---:|---|---|
| 1 | Crystal Walker Greaves | Medium T5 | 12 | +1 | 5.0 | str5 end15 | `ItemTemplate_Armor_Medium_T5_Legs_CrystalWalkerGreaves` |
| 2 | Blackwater Greaves  | Heavy T5 | 11 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Legs_NormalDrownedKnightGreaves` |
| 3 | Blackwater Greaves  | Heavy T5 | 11 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Legs_DrownedKnightGreaves` |
| 4 | Sir Tristan's Greaves | Heavy T6 | 11 | +1 | 4.5 | str6 dex6 end5 spi3 per5 pra3 | `ItemTemplate_Armor_Heavy_T6_Legs_TristanGreaves` |
| 5 | Ashen Veil Greaves | Medium T5 | 11 | +1 | 1.5 | str6 dex6 end7 per6 | `ItemTemplate_Armor_Medium_T5_Legs_AshenVeilGreaves` |
| 6 | King Arthur's Greaves | Heavy T6 | 11 | +1 | 4.5 | str6 dex6 end6 spi6 per5 pra5 | `ItemTemplate_Armor_Heavy_T6_Legs_KingArthursGreaves` |
| 7 | Conqueror of Avalon Legguards | Heavy T6 | 11 | +1 | 4.5 | str6 dex6 end6 spi6 per5 pra5 | `ItemTemplate_Armor_Heavy_T6_Legs_ConquerorOfAvalonLegguards` |
| 8 | Harbinger of Night Legguards | Legs HarbingerOfNightLegguards | 10 | +1 | 6.0 | str10 end10 | `ItemTemplate_Armor_Legs_HarbingerOfNightLegguards` |
| 9 | Champion of Tuathan Legguards | Legs ChampionOfTuathanLegguards | 10 | +1 | 6.0 | str10 end10 | `ItemTemplate_Armor_Legs_ChampionOfTuathanLegguards` |
| 10 | Duel Knight Trousers | Light T5 | 10 | +1 | 4.5 | str7 dex7 end20 | `ItemTemplate_Armor_Light_T5_Legs_DuelKnightTrousers_Unbalanced` |
| 11 | Duel Knight Trousers | Light T5 | 10 | +1 | 4.5 | str7 dex7 end20 | `ItemTemplate_Armor_Light_T5_Legs_DuelKnightTrousers` |
| 12 | Abyssal Heir Greaves | Heavy T4 | 10 | +1 | 4.0 | str10 end10 | `ItemTemplate_Armor_Heavy_T4_Legs_AbyssalHeirGreaves` |
| 13 | Elite Stonewarden's Greaves | Heavy T5 | 10 | +1 | 4.0 | str8 end5 spi8 | `ItemTemplate_Armor_Heavy_T5_Legs_SpikyGreaves` |
| 14 | Perceval's Dress | Light T5 | 10 | +1 | 4.0 | str8 end5 spi8 | `ItemTemplate_Armor_Light_T5_Legs_PercevalsLegs` |
| 15 | Sir Galahad's Sabatons | Heavy T6 | 10 | +1 | 4.0 | str15 dex15 end8 | `ItemTemplate_Armor_Heavy_T6_Legs_SirGalahadsGreaves` |

## Boots — top 15 by base armor

| Rank | Name | Tier | Armor | +/lvl | Wt | Reqs | Template |
|---:|---|---|---:|---:|---:|---|---|
| 1 | Champion of Tuathan Sabatons | Feet ChampionOfTuathanSabatons | 9 | +1 | 5.0 | str10 end10 | `ItemTemplate_Armor_Feet_ChampionOfTuathanSabatons` |
| 2 | Blackwater Sabatons | Heavy T5 | 9 | +1 | 2.5 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Feet_DrownedKnightBoots` |
| 3 | Blackwater Sabatons | Heavy T5 | 9 | +1 | 2.5 | str10 end10 | `ItemTemplate_Armor_Heavy_T5_Feet_NormalDrownedKnightBoots` |
| 4 | Harbinger of Night Sabatons | Feet HarbingerOfNightSabatons | 8 | +1 | 4.0 | str10 end10 | `ItemTemplate_Armor_Feet_HarbingerOfNightSabatons` |
| 5 | Volker Berserker's Boots | Heavy T4 | 8 | +1 | 4.5 | str8 end8 | `ItemTemplate_Armor_Heavy_T4_Feet_VolkerBerserkerBoots` |
| 6 | Winged Cavalier Boots | Medium T5 | 8 | +1 | 5.0 | str7 end10 per4 | `ItemTemplate_Armor_Medium_T5_Feet_WingedCavalierBoots` |
| 7 | Sir Gawain's Weathered Sabatons | Heavy T6 | 8 | +1 | 1.5 | str8 dex8 end15 per8 | `ItemTemplate_Armor_Heavy_T6_Feet_SirGawainsSabatons_Dummy` |
| 8 | Lionheart Boots | Medium T4 | 8 | +1 | 3.4 | str5 dex9 end5 | `ItemTemplate_Armor_Medium_T4_Feet_LionheartBoots` |
| 9 | Lead Sabatons | Heavy T3 | 8 | +1 | 6.0 | dex4 end4 pra5 | `ItemTemplate_Armor_Heavy_T3_Feet_LeadSabatons` |
| 10 | Wayfarer's Boots | Medium T5 | 8 | +1 | 1.0 | str5 dex5 end11 | `ItemTemplate_Armor_Medium_T5_Feet_WayfarersBoots` |
| 11 | Crystal Walker Boots | Medium T5 | 8 | +1 | 3.0 | str5 end15 | `ItemTemplate_Armor_Medium_T5_Feet_CrystalWalkerBoots` |
| 12 | Sir Gawain's Weathered Sabatons | Heavy T6 | 8 | +1 | 1.5 | str8 dex8 end15 per8 | `ItemTemplate_Armor_Heavy_T6_Feet_SirGawainsSabatons` |
| 13 | Titan's Boots | Heavy T4 | 7 | +1 | 5.0 | end10 | `ItemTemplate_Armor_Heavy_T4_Feet_TitansBoots` |
| 14 | Duel Knight Boots | Light T6 | 7 | +1 | 6.0 | str8 dex8 end9 | `ItemTemplate_Armor_Light_T6_Feet_DrastusBoots` |
| 15 | Ghoststride Boots | Light T5 | 7 | +1 | 0.6 | spi10 | `ItemTemplate_Armor_Light_T5_Feet_GhoststrideBoots` |

## Back — top 15 by base armor

| Rank | Name | Tier | Armor | +/lvl | Wt | Reqs | Template |
|---:|---|---|---:|---:|---:|---|---|
| 1 | ItemTemplate_Armor_Light_T0_Back_ | Light T0 | 0 | +1 | 1.0 | - | `ItemTemplate_Armor_Light_T0_Back_` |
| 2 | ItemTemplate_Armor_Medium_T2_Back_ | Medium T2 | 0 | +1 | 1.0 | - | `ItemTemplate_Armor_Medium_T2_Back_` |
| 3 | ItemTemplate_Armor_Medium_T1_Back_ | Medium T1 | 0 | +1 | 1.0 | - | `ItemTemplate_Armor_Medium_T1_Back_` |
| 4 | ItemTemplate_Armor_Medium_T0_Back_ | Medium T0 | 0 | +1 | 1.0 | - | `ItemTemplate_Armor_Medium_T0_Back_` |
| 5 | ItemTemplate_Armor_Light_T2_Back_ | Light T2 | 0 | +1 | 1.0 | - | `ItemTemplate_Armor_Light_T2_Back_` |
| 6 | ItemTemplate_Armor_Light_T1_Back_ | Light T1 | 0 | +1 | 1.0 | - | `ItemTemplate_Armor_Light_T1_Back_` |
| 7 | ItemTemplate_Armor_Heavy_T2_Back_ | Heavy T2 | 0 | +1 | 1.0 | - | `ItemTemplate_Armor_Heavy_T2_Back_` |
| 8 | ItemTemplate_Armor_Heavy_T1_Back_ | Heavy T1 | 0 | +1 | 1.0 | - | `ItemTemplate_Armor_Heavy_T1_Back_` |
| 9 | ItemTemplate_Armor_Heavy_T0_Back_ | Heavy T0 | 0 | +1 | 1.0 | - | `ItemTemplate_Armor_Heavy_T0_Back_` |
| 10 | Dál Riata Totem | Medium T5 | 0 | +0 | 1.5 | - | `ItemTemplate_Armor_Medium_T5_Back_DalRiataTotem_Volker` |
| 11 | Dál Riata Totem | Medium T5 | 0 | +0 | 1.5 | - | `ItemTemplate_Armor_Medium_T5_Back_DalRiataTotem_Ulfr` |
| 12 | Dál Riata Totem | Medium T5 | 0 | +0 | 1.5 | - | `ItemTemplate_Armor_Medium_T5_Back_DalRiataTotem_Theud` |
| 13 | Dál Riata Totem | Medium T5 | 0 | +0 | 1.5 | - | `ItemTemplate_Armor_Medium_T5_Back_DalRiataTotem_Sveinn` |
| 14 | Sveinn Pathfinder's Totem | Heavy T4 | 0 | +0 | 1.0 | - | `ItemTemplate_Armor_Heavy_T4_Back_SveinnPathfinderTotem_v1` |
| 15 | Sveinn Pathfinder's Totem | Heavy T4 | 0 | +0 | 1.0 | - | `ItemTemplate_Armor_Heavy_T4_Back_SveinnPathfinderTotem` |

## Armor cap reminder

From `Damage.cs:594`:

```
ArmorDamageReduction = clamp(armorValue / 100, 0, 0.95)
```

Soft cap is 95% damage reduction at armor value 95. Past that, additional armor is wasted (move budget into HP / IncomingDamage / talents).
