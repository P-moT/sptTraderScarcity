// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Utils;
using TraderID = SPTarkov.Server.Core.Models.Enums.Traders;

namespace TraderScarcity;

// Scarcity mode for trader stock.
// Every restock, each offer rolls whether it is in stock at all, and if so, how many.
// Out of stock = StackObjectsCount 0, so the offer stays visible but cannot be bought.
// Used both at server start (TraderScarcityLoader) and on every trader restock (ResetExpiredTraderPatch).
public static class AssortScarcity
{
    // Set from config.json by TraderScarcityLoader before the first roll.
    public static ScarcityConfig Config = new();

    // Original loyalty levels per trader. ResetExpiredTrader only restores Items, so bumps would stack without this.
    private static readonly Dictionary<MongoId, Dictionary<MongoId, int>> OriginalLoyaltyLevels = new();

    // Base class nodes from templates/items.json
    private static readonly MongoId AmmoClass = "5485a8684bdc2da71d8b4567";
    private static readonly MongoId AmmoBoxClass = "543be5cb4bdc2deb348b4568";
    private static readonly MongoId WeaponClass = "5422acb9af1c889c16000029";
    private static readonly MongoId EquipmentClass = "543be5f84bdc2dd4348b456a"; // Covers ArmoredEquipment > Armor/Headwear/ArmorPlate, backpacks, rigs
    private static readonly MongoId ModClass = "5448fe124bdc2da5018b4567";
    private static readonly MongoId MoneyClass = "543be5dd4bdc2deb348b4569";

    // Base game traders the Traders setting covers, by name. Any trader not here (apart from Lightkeeper and Ref) was added by a mod.
    private static readonly Dictionary<MongoId, string> TraderNames = new()
    {
        [TraderID.PRAPOR] = "Prapor", [TraderID.THERAPIST] = "Therapist", [TraderID.SKIER] = "Skier", [TraderID.PEACEKEEPER] = "Peacekeeper",
        [TraderID.MECHANIC] = "Mechanic", [TraderID.RAGMAN] = "Ragman", [TraderID.JAEGER] = "Jaeger", [TraderID.FENCE] = "Fence",
    };

    // ExcludedItems as a set, rebuilt whenever the settings are swapped
    private static ScarcityConfig? _excludedFor;
    private static HashSet<string> _excluded = [];

    private static bool IsExcluded(MongoId template)
    {
        ScarcityConfig config = Config;
        if (!ReferenceEquals(config, _excludedFor))
        {
            _excluded = new HashSet<string>(config.ExcludedItems, StringComparer.OrdinalIgnoreCase);
            _excludedFor = config;
        }
        return _excluded.Count > 0 && _excluded.Contains(template.ToString());
    }

    private static bool IsTraderEnabled(MongoId trader) =>
        TraderNames.TryGetValue(trader, out string? name) ? Config.IsTraderEnabled(name) : Config.IncludeModdedTraders;

    // Re-roll stock for every root offer of a trader. Lightkeeper and Ref are left alone, and so are modded traders unless IncludeModdedTraders is on.
    // Fence is skipped here because his real stock is generated separately - see ApplyFence.
    public static void Apply(Trader trader, Dictionary<MongoId, TemplateItem> templates, ISptLogger<TraderScarcityLoader>? logger = null, ItemHelper? itemHelper = null)
    {
        if (trader?.Assort?.Items is null || trader.Base.Id == TraderID.LIGHTHOUSEKEEPER || trader.Base.Id == TraderID.FENCE || trader.Base.Id == TraderID.REF)
        {
            return;
        }
        if (!Config.EnableScarcity || !IsTraderEnabled(trader.Base.Id))
        {
            // Turned off from the SIC page - ResetExpiredTrader already gave back full stock, but not the loyalty levels
            RestoreLoyaltyLevels(trader);
            return;
        }
        var loyaltyLevels = trader.Assort.LoyalLevelItems;
        if (!OriginalLoyaltyLevels.TryGetValue(trader.Base.Id, out var originalLevels))
        {
            originalLevels = new();
            if (loyaltyLevels is not null)
            {
                foreach (var level in loyaltyLevels)
                {
                    originalLevels[level.Key] = level.Value;
                }
            }
            OriginalLoyaltyLevels[trader.Base.Id] = originalLevels;
        }
        //Quest-locked offers stay hidden regardless of loyalty level, so they can't receive the boon.
        //Read live, so mods that unlock quest assort (by emptying these) are respected.
        HashSet<MongoId> questLocked = [];
        if (trader.QuestAssort is not null)
        {
            foreach (var status in trader.QuestAssort.Values)
            {
                if (status is null) continue;
                foreach (var questOffer in status.Keys)
                {
                    questLocked.Add(questOffer);
                }
            }
        }
        int offers = 0, inStock = 0;
        List<Item> boons = [];
        foreach (Item offer in trader.Assort.Items)
        {
            if (offer.ParentId != "hideout") //Only root items are offers, the rest are attachments of presets
            {
                continue;
            }
            if (!RollStock(offer, templates))
            {
                continue;
            }
            offers++;
            if (offer.Upd!.StackObjectsCount > 0) inStock++;

            //Loyalty boon - always start from the original level, so a bump lasts one restock only
            if (loyaltyLevels is not null && originalLevels.TryGetValue(offer.Id, out int originalLevel))
            {
                bool bumped = offer.Upd.StackObjectsCount > 0 && originalLevel > 1 && !questLocked.Contains(offer.Id) && Random.Shared.NextDouble() < Config.LoyaltyBumpChance;
                loyaltyLevels[offer.Id] = bumped ? originalLevel - 1 : originalLevel;
                if (bumped)
                {
                    boons.Add(offer);
                }
            }
        }
        if (Config.LogRolls && logger is not null)
        {
            logger.LogWithColor($"[TraderScarcity] {trader.Base.Nickname}: {inStock}/{offers} offers in stock, {boons.Count} loyalty boon(s)", Spectre.Console.Color.Blue);
            foreach (Item boon in boons)
            {
                int level = originalLevels[boon.Id];
                //Same item can be sold in several offers (different price, barter, preset) - list the others so the right one can be found in game
                var otherLevels = trader.Assort.Items
                    .Where(other => other.ParentId == "hideout" && other.Template == boon.Template && other.Id != boon.Id)
                    .Select(other => originalLevels.TryGetValue(other.Id, out int otherLevel) ? $"LL{otherLevel}{(questLocked.Contains(other.Id) ? " quest" : "")}" : "LL?")
                    .ToList();
                string others = otherLevels.Count > 0 ? $" | other offers of this item: {string.Join(", ", otherLevels)}" : "";
                logger.LogWithColor($"[TraderScarcity]     Boon: {GetName(boon.Template, itemHelper)} x{boon.Upd!.StackObjectsCount} for {GetPrice(trader, boon.Id, itemHelper)} (LL{level} -> LL{level - 1}){others}", Spectre.Console.Color.Green);
            }
        }
    }

    // Undo any loyalty boon still in place from the last roll
    private static void RestoreLoyaltyLevels(Trader trader)
    {
        var loyaltyLevels = trader.Assort.LoyalLevelItems;
        if (loyaltyLevels is null || !OriginalLoyaltyLevels.TryGetValue(trader.Base.Id, out var originalLevels))
        {
            return;
        }
        foreach (var level in originalLevels)
        {
            loyaltyLevels[level.Key] = level.Value;
        }
    }

    // Roll stock for Fence's generated assort (FenceService's cache - his entry in the traders table is only the pool it draws from).
    // Fence refreshes part of his stock every few minutes, so each offer is rolled once when it first appears rather than on every refresh.
    // No loyalty boon - Fence's levels come from scav karma, not normal trader loyalty.
    public static void ApplyFence(TraderAssort? mainAssort, TraderAssort? discountAssort, Dictionary<MongoId, TemplateItem> templates, ISptLogger<TraderScarcityLoader>? logger = null)
    {
        if (!Config.EnableScarcity || !Config.IsTraderEnabled("Fence"))
        {
            return;
        }
        //Fence has two assorts - the main one, and a discounted one for high scav karma
        List<Item> roots = new[] { mainAssort, discountAssort }
            .SelectMany(assort => assort?.Items ?? [])
            .Where(item => item.ParentId == "hideout")
            .ToList();
        //Forget offers that have rotated out, so the set doesn't grow forever
        HashSet<MongoId> current = roots.Select(root => root.Id).ToHashSet();
        RolledFenceOffers.RemoveWhere(id => !current.Contains(id));
        int offers = 0, inStock = 0;
        foreach (Item offer in roots)
        {
            if (!RolledFenceOffers.Add(offer.Id) || !RollStock(offer, templates))
            {
                continue;
            }
            offers++;
            if (offer.Upd!.StackObjectsCount > 0) inStock++;
        }
        if (Config.LogRolls && logger is not null && offers > 0)
        {
            logger.LogWithColor($"[TraderScarcity] Fence: {inStock}/{offers} new offers in stock", Spectre.Console.Color.Blue);
        }
    }

    // Offer ids already rolled, across Fence's main and discount assorts
    private static readonly HashSet<MongoId> RolledFenceOffers = new();

    // Roll whether an offer is in stock and how many. Returns false for offers that are left alone (currency exchange, ExcludedItems).
    private static bool RollStock(Item offer, Dictionary<MongoId, TemplateItem> templates)
    {
        StockRule? rule = IsExcluded(offer.Template) ? null : GetRule(offer.Template, templates);
        if (rule is null)
        {
            return false;
        }
        offer.Upd ??= new();
        offer.Upd.UnlimitedCount = false;
        offer.Upd.StackObjectsCount = Random.Shared.NextDouble() < rule.InStockChance
            ? Random.Shared.Next(rule.Min, rule.Max + 1)
            : 0;
        return true;
    }

    private static string GetPrice(Trader trader, MongoId offerId, ItemHelper? itemHelper)
    {
        if (trader.Assort.BarterScheme is null || !trader.Assort.BarterScheme.TryGetValue(offerId, out var schemes) || schemes.Count == 0)
        {
            return "?";
        }
        var cost = schemes[0];
        if (cost.Count == 1 && cost[0].Template == "5449016a4bdc2d6f028b456f") return $"{cost[0].Count:N0} RUB";
        if (cost.Count == 1 && cost[0].Template == "5696686a4bdc2da3298b456a") return $"{cost[0].Count:N0} USD";
        if (cost.Count == 1 && cost[0].Template == "569668774bdc2da2298b4568") return $"{cost[0].Count:N0} EUR";
        return "barter: " + string.Join(" + ", cost.Select(part => $"{part.Count}x {GetName(part.Template, itemHelper)}"));
    }

    private static string GetName(MongoId tpl, ItemHelper? itemHelper)
    {
        try//Logging must never break the roll, e.g. if locales aren't loaded yet at startup
        {
            return itemHelper?.GetItemName(tpl) ?? tpl.ToString();
        }
        catch
        {
            return tpl.ToString();
        }
    }

    private static StockRule? GetRule(MongoId tpl, Dictionary<MongoId, TemplateItem> templates)
    {
        // Walk up the parent chain, nearest class wins. Depth cap guards against broken modded templates.
        MongoId? current = tpl;
        for (int depth = 0; depth < 15 && current is not null && templates.TryGetValue(current.Value, out TemplateItem? template); depth++)
        {
            MongoId id = template.Id;
            if (id == MoneyClass) return null;
            if (id == AmmoClass) return Config.Ammo;
            if (id == AmmoBoxClass) return Config.AmmoBox;
            if (id == WeaponClass || id == EquipmentClass || id == ModClass) return Config.Gear;
            current = template.Parent;
        }
        return Config.Other;
    }
}
