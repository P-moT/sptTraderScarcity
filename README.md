# Trader Scarcity
## After the fall of Tushonka, things are much harder to obtain.
### - This mod will severely limit the items that the Traders have in stock.
### - Default values are how I prefer them, but they can be changed to your liking.
### - Modded traders can be excluded.
### - Due to Traders not having as many items, they may be more willing to sell exclusive stock.
---
Directly inspired by the trader behavior of the original [Realism Mod](https://sp-mod.com/mod/416/spt-realism-mod) for 3.11 by [Fontaine](https://sp-mod.com/user/6053/fontaine)
---
## Recommendations
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
| `LoyaltyBumpChance` | `0.1` | Chance (0 to 1) for an in-stock LL2+ item to be sold one loyalty level lower until the next restock. e.g. if 20 LL2 items roll in stock, about 2 become available at LL1. |
| `Ammo` | `0.30`, `10`–`90` | Loose rounds. |
| `AmmoBox` | `0.30`, `1`–`1` | Ammo boxes. |
| `Gear` | `0.25`, `1`–`1` | Weapons, armor, plates, helmets, rigs, backpacks, weapon mods. |
| `Other` | `0.35`, `1`–`1` | Everything else: meds, provisions, barter items, keys, containers, etc. |

Each item category has three values: `InStockChance` is the chance (0 to 1) that an offer is in stock after a restock, and `Min` / `Max` set how many the trader has when it is.