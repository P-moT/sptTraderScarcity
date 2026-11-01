# Trader Scarcity
## After the fall of Tushonka, things are much harder to obtain.
### - This mod will severely limit the items that the Traders have in stock.
### - Default values are how I prefer them, but they can be changed to your liking.
### - Modded traders can be excluded.
### - Due to Traders not having as many items, they may be more willing to sell exclusive stock.
### - New multi-option crafting helps make up the difference: one recipe, many ways in, many ways out.
---
Directly inspired by the trader behavior of the original [Realism Mod](https://sp-mod.com/mod/416/spt-realism-mod) for 3.11 by [Fontaine](https://sp-mod.com/user/6053/fontaine)

### Scarcity Notes
- This mod hamstrings the traders, but doesn't touch the flea.
- I would highly recommend a mod that restricts the flea market to round out the experience.
- [Hardcore Rules](https://sp-mod.com/mod/784/hardcore-rules) has a customisable config that pairs well (this is what I use)
- [ServerValueModifier](https://sp-mod.com/mod/236/server-value-modifier-svm) could be tweaked to a good result (most everyone has this already, so could be an alternative if you don't want some of the behavior of HardcoreRules)

## Config

Settings live in `config.json` in the mod folder. Restart the server after changing them.

| Setting | Default | Description |
| --- | --- | --- |
| `LogRolls` | `false` | Log every stock roll to the server console. |
| `IncludeModdedTraders` | `true` | Whether traders added by other mods are affected. |
| `IncludeFence` | `false` | Whether Fence is affected. |
| `EnableRecipes` | `true` | Add the multi-option crafting recipes from `recipes.jsonc`. |
| `LoyaltyBumpChance` | `0.1` | Chance (0 to 1) for an in-stock LL2+ item to be sold one loyalty level lower until the next restock. e.g. if 20 LL2 items roll in stock, about 2 become available at LL1. |
| `Ammo` | `0.30`, `10`–`90` | Loose rounds. |
| `AmmoBox` | `0.30`, `1`–`1` | Ammo boxes. |
| `Gear` | `0.25`, `1`–`1` | Weapons, armor, plates, helmets, rigs, backpacks, weapon mods. |
| `Other` | `0.35`, `1`–`1` | Everything else: meds, provisions, barter items, keys, containers, etc. |

Each item category has three values: `InStockChance` is the chance (0 to 1) that an offer is in stock after a restock, and `Min` / `Max` set how many the trader has when it is.

## Multi-option crafting

Instead of dozens of near-identical recipes, each new recipe takes **any one of several inputs** and makes **any one of several outputs**. For example, the rifle ammo press takes 1x Hawk *or* 1x Eagle *or* 2x Kite + matches, and makes 240x 5.56 M855 *or* 100x 7.62x51 M80 *or* 90x 7.62x54R LPS, and so on.

| Recipe | Station | Turns | Into |
| --- | --- | --- | --- |
| Gunpowder salvage | Workbench 1 | grenades or piles of junk rounds | Gunpowder "Kite" |
| Ammo press (basic) | Workbench 1 | Kite | 9x19, 9x18, 5.45, 7.62x39, 12ga, 4.6 |
| Ammo press (rifle) | Workbench 2 | Hawk, Eagle, or 2x Kite | 5.56, 7.62x51, 7.62x54R, .300 BLK, 9x39 |
| Ammo press (AP) | Workbench 3 | two gunpowders | M855A1, M80A1, PS, .300 AP, BP, PBP, 4.6 AP, AP-20 |
| Field dressing | Medstation 1 | meds, painkillers, bandages | bandages, tourniquets, splints |
| Trauma kit | Medstation 2 | med tools, first aid kits | IFAK, CALOK-B, CMS, Grizzly |
| Stim lab | Medstation 3 | Propital, SJ1, Morphine | Meldonin, AHF1-M, Zagustin, Perfotoran |
| Ration kitchen | Nutrition Unit 1 | cheap canned food | water, stew, snacks |
| Field rations | Nutrition Unit 2 | stew, condensed milk, sugar | Iskra, MRE, water rations, sausage |
| Electronics salvage | Workbench 1 | broken phones, LCDs, drives | wires, capacitors, PCBs, cords, magnets |
| Hardware salvage | Workbench 2 | nails, screws, hand tools, hoses | bolts, metal parts, KEKTAPE, tape, Poxeram |

Outputs are worth roughly 1.2–1.6x their inputs at handbook prices.

### Two parts
- **Server** (`SPT_Runtime\user\mods\TraderScarcity`) - adds the recipes. Each option combination becomes a normal hideout recipe, so everything works without the client part, just as separate cards.
- **Client** (`BepInEx\plugins\TraderScarcity\TraderScarcity.Client.dll`) - folds each recipe into **one card** with `< Input 2/5 >` and `< Output 1/3 >` pickers. Hover a picker to list every option. Pickers lock while that recipe is crafting or has items waiting. The input picker sits at the left edge of the crafting window and the output picker at the right edge, level with their recipe (in Usable Crafting, at the edges of its details pane) - they stay put when switching options changes the recipe's length. Usable Crafting's grid tiles follow the picked option too (icon, count, and producing / ready highlights), and multi-option recipes stand out with their own background tint and an "inputs>outputs" badge (e.g. `3>5`) - both can be turned off or recoloured in the config. Layout (edges, or both in one corner), inset and size can be changed in the BepInEx config (F12).

### Writing your own
Recipes live in `recipes.jsonc`. Items are 24 character ids - the file explains every field. Mistakes are skipped with a warning in the server console rather than stopping the mod.
- Keep a recipe's `Id` and the order of its options stable - they decide the in-game recipe ids, and crafts in progress for a changed recipe are lost. Add new options at the end.
- `Extras` are used up whichever input is picked, `Tools` are needed but kept, and an output can set its own `Minutes`.
- `Color` (e.g. `"#2f6fd6"`) gives a recipe its own tile tint in Usable Crafting. The ammo presses use it for tiers: basic blue, rifle green, AP red.

### Crafting Notes
- This feature was developed with [UsableCrafting](https://sp-mod.com/mod/3149/usable-crafting) in mind.
- It functions identically without it, but looks great with it. 
- With enough patience in writing recipes, you could craft 100s of items using only a handful of categorical craft
cards. What's included is just the basic consumables. Ammo/Meds/Food.