# Trader Scarcity
## After the fall of Tushonka, things are much harder to obtain.
### - This mod will severely limit the items that the Traders have in stock.
### - Default values are how I prefer them, but they can be changed to your liking.
### - Modded traders can be excluded.
### - Due to Traders not having as many items, they may be more willing to sell exclusive stock.

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