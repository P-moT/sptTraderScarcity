# Trader Scarcity
## After the fall of Tushonka, things are much harder to obtain.
 - This mod will severely limit the items that the Traders have in stock.
 - Default values are how I prefer them, but they can be changed to your liking.
 - Modded traders can be excluded.
 - Due to Traders not having as many items, they may be more willing to sell exclusive stock.
 - New multi-option crafting helps make up the difference: one recipe, many ways in, many ways out.
---
Directly inspired by the trader behavior of the original [Realism Mod](https://sp-mod.com/mod/416/spt-realism-mod) for 3.11 by [Fontaine](https://sp-mod.com/user/6053/fontaine)

### Scarcity Notes
- This mod hamstrings the traders, but doesn't touch the flea.
- I would highly recommend a mod that restricts the flea market to round out the experience.
- [Hardcore Rules](https://sp-mod.com/mod/784/hardcore-rules) has a customisable config that pairs well (this is what I use)
- [ServerValueModifier](https://sp-mod.com/mod/236/server-value-modifier-svm) could be tweaked to a good result (most everyone has this already, so could be an alternative if you don't want some of the behavior of HardcoreRules)

## Config

Every setting can be changed in the SPT web interface (SIC) on the **Trader Scarcity** page. Saved changes are written to `config.json` and apply from each trader's next restock, without a restart - except `EnableRecipes`, which needs a server restart.

Settings live in `config.json` in the mod folder. If you edit the file by hand instead, restart the server afterwards.

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

Instead of dozens of near-identical recipes, each new recipe takes **any one of several inputs** and makes **any one of several outputs**. For example, the intermediate ammo press takes 1x Hawk *or* 1x Eagle *or* 3x Kite, and makes 150x 5.56 M855 *or* 70x 7.62x51 M80 *or* 60x 7.62x54R LPS, and so on.

| Recipe | Station | Time | Takes any one of | Makes any one of |
| --- | --- | --- | --- | --- |
| Gunpowder salvage | Workbench 1 | 45m | F-1 · RGD-5 + VOG-25 · M67 · 5.45 HP · 5.56 HP · 7.62x39 HP | Gunpowder "Kite" |
| Ammo press (basic) | Workbench 1 | 30m | Kite | 5.56 FMJ · 7.62x51 TCW SP · 9x19 Pst · 9x18 PSV · 5.45 PS · 7.62x39 FMJ · 12ga 8.5mm Magnum |
| Ammo press (intermediate) | Workbench 2 | 45m | Hawk · Eagle · 3x Kite | 5.56 M855 · 7.62x51 M80 · 7.62x54R LPS · .300 BLK M62 · 9x39 SPP · 4.6 FMJ SX |
| Ammo press (advanced) | Workbench 3 | 60m | 2x Hawk and Eagle, in any pairing | 5.56 M855A1 · 5.56 M995 · 7.62x39 BP · 7.62x51 M80A1 · 7.62x54R PS · 7.62x54R SNB · .300 BLK AP · 9x39 BP · 4.6 AP SX |
| Field dressing | Medstation 1 | 30m | Pile of meds · Analgin · AI-2 · Aseptic bandages | Army bandages · Esmarch tourniquets · Aluminum splint |
| Trauma kit | Medstation 2 | 90m | Medical tools + Pile of meds · Car first aid kits · Salewa + Army bandages | IFAK · CALOK-B · CMS (180m) · Grizzly (180m) |
| Stim lab | Medstation 3 | 180m | Propital · SJ1 + syringe · Morphine + saline + syringe | Meldonin · AHF1-M (120m) · Zagustin (300m) · Perfotoran (300m) |
| Ration kitchen | Nutrition Unit 1 | 60m | Green peas + rye croutons · pacific saury · sprats · humpback salmon · Emelya croutons | Water · small beef stew · Slickers · instant noodles · Army crackers |
| Field rations | Nutrition Unit 2 | 120m | Small beef stew · condensed milk + Army crackers · sugar + oat flakes | Iskra · MRE · Emergency Water Ration · Salty Dog sausage |
| Electronics salvage | Workbench 1 | 60m | Broken GPhone · Broken LCD · DVD drive + PSU · Gas analyzer · Damaged hard drives | Wires · capacitors · PCBs · magnets |
| Hardware salvage | Workbench 2 | 90m | Radiator helix + corrugated hose · spark plugs · military corrugated tube | Bolts · screw nuts · metal spare parts · insulating tape · packs of nails |
| Heavy salvage | Workbench 3 | 180m | Car battery · electric motor + spark plugs · electric drills · metal fuel tank + corrugated hose | Bolts · screw nuts · metal spare parts · wires |

Times in brackets are per-output overrides. Some recipes also need a tool, which is kept: Pliers (gunpowder and hardware salvage), Leatherman (intermediate press), Set of files "Master" (advanced press), Screwdriver (electronics salvage) and Toolset (heavy salvage).

The ammo presses return at most 1.0x their input at handbook prices, so you spend gunpowder to choose your ammo, not to make a profit. Meds, food and salvage are more generous at roughly 1.0–1.6x, with the most valuable outputs taking longer to craft.

### Two parts
- **Server** (`SPT_Runtime\user\mods\TraderScarcity`) - adds the recipes. Each option combination becomes a normal hideout recipe, so everything works without the client part, just as separate cards.
- **Client** (`BepInEx\plugins\TraderScarcity\TraderScarcity.Client.dll`) - folds each recipe into **one card** with `< Input 2/5 >` and `< Output 1/3 >` pickers. Hover a picker to list every option. Pickers lock while that recipe is crafting or has items waiting. The input picker sits at the left edge of the crafting window and the output picker at the right edge, level with their recipe (in Usable Crafting, at the edges of its details pane) - they stay put when switching options changes the recipe's length. Usable Crafting's grid tiles follow the picked option too (icon, count, and producing / ready highlights), and multi-option recipes stand out with their own background tint and an "inputs>outputs" badge (e.g. `3>5`) - both can be turned off or recoloured in the config. Layout (edges, or both in one corner), inset and size can be changed in the BepInEx config (F12).

### Writing your own
Recipes live in `recipes.jsonc`. Items are 24 character ids - the file explains every field. Mistakes are skipped with a warning in the server console rather than stopping the mod.
- The easiest way is the recipe editor on the **Trader Scarcity** page in SIC. Recipes are grouped by station as in the hideout, and items are found by their English name. Saving writes `recipes.jsonc`; restart the server and the game for the changes to take effect.
- Keep a recipe's `Id` and the order of its options stable - they decide the in-game recipe ids. Renaming an `Id` loses crafts in progress for that recipe; reordering or removing options changes which output an in-progress craft gives, or loses it if its slot no longer exists. Add new options at the end.
- `Extras` are used up whichever input is picked, `Tools` are needed but kept, and an output can set its own `Minutes`.
- `Color` (e.g. `"#2f6fd6"`) gives a recipe its own tile tint in Usable Crafting. The ammo presses and the salvage recipes use it for tiers: Workbench 1 blue, Workbench 2 green, Workbench 3 red.

### Crafting Notes
- This feature was developed with [UsableCrafting](https://sp-mod.com/mod/3149/usable-crafting) in mind.
- It functions identically without it, but looks great with it. 
- With enough patience in writing recipes, you could craft 100s of items using only a handful of categorical craft
cards. What's included is just the basic consumables. Ammo/Meds/Food.