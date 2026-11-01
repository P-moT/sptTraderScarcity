// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT;
using EFT.Hideout;
using EFT.InventoryLogic;
using EFT.UI;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace TraderScarcity.Client;

/// Every production list (vanilla list and UsableCrafting's grid) is built from GetSortedSchemes.
/// Keep one scheme per group - its "anchor" - at the position of its best ranked sibling (ready / producing first).
/// The anchor is the lowest (input, output) sibling, so it stays the same between calls and views keyed by it stay valid.
public class MergeSiblingsPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ProductionPanel), nameof(ProductionPanel.GetSortedSchemes));
    }

    [PatchPostfix]
    public static void Postfix(ref IEnumerable<ProductionScheme> __result)
    {
        if (__result == null)
        {
            return;
        }
        List<ProductionScheme> sorted = __result.ToList();
        Dictionary<string, (ProductionScheme Scheme, GroupRef Ref)> anchors = new Dictionary<string, (ProductionScheme, GroupRef)>();
        foreach (ProductionScheme scheme in sorted)
        {
            if (CraftGroups.TryGet(scheme._id, out GroupRef groupRef)
                && (!anchors.TryGetValue(groupRef.Group, out var anchor) || (groupRef.Input, groupRef.Output).CompareTo((anchor.Ref.Input, anchor.Ref.Output)) < 0))
            {
                anchors[groupRef.Group] = (scheme, groupRef);
            }
        }
        if (anchors.Count == 0)
        {
            __result = sorted;
            return;
        }
        List<ProductionScheme> merged = new List<ProductionScheme>(sorted.Count);
        HashSet<string> emitted = new HashSet<string>();
        foreach (ProductionScheme scheme in sorted)
        {
            if (!CraftGroups.TryGet(scheme._id, out GroupRef groupRef))
            {
                merged.Add(scheme);
            }
            else if (emitted.Add(groupRef.Group))
            {
                merged.Add(anchors[groupRef.Group].Scheme);
            }
        }
        __result = merged;
    }
}

/// When a card is shown for a group, show the sibling that matters instead: one that is producing or has items ready,
/// else the player's last pick. Covers vanilla cards and UsableCrafting's details view - both go through this Show.
public class ShowSelectedSiblingPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ProduceView), nameof(ProduceView.Show),
        [
            typeof(ItemUiContext), typeof(InventoryController), typeof(ProductionScheme), typeof(ItemsProducerBase),
            typeof(Action<string>), typeof(Action<string>), typeof(bool),
        ]);
    }

    [PatchPrefix]
    public static void Prefix(ProduceView __instance, ItemUiContext itemUiContext, InventoryController inventoryController, ref ProductionScheme scheme,
        ItemsProducerBase producer, Action<string> onStartProducing, Action<string> getProducedItems, bool availableFavorites)
    {
        try
        {
            if (scheme == null || !CraftGroups.TryGet(scheme._id, out GroupRef groupRef))
            {
                GroupPicker.Detach(__instance);
                return;
            }
            List<Sibling> siblings = CraftGroups.Siblings(groupRef.Group, producer);
            if (siblings.Count <= 1)
            {
                GroupPicker.Detach(__instance);
                return;
            }
            scheme = CraftGroups.Effective(scheme, producer);
            GroupPicker.Attach(__instance, new ShowArgs(itemUiContext, inventoryController, producer, onStartProducing, getProducedItems, availableFavorites), groupRef.Group);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to pick recipe option: {ex}");
        }
    }

    [PatchPostfix]
    public static void Postfix(ProduceView __instance)
    {
        GroupPicker.Refresh(__instance);
    }
}

/// Lock / unlock the pickers as production starts, finishes and is collected.
public class RefreshPickerPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ProduceView), nameof(ProduceView.UpdateView));
    }

    [PatchPostfix]
    public static void Postfix(ProduceView __instance)
    {
        GroupPicker.Refresh(__instance);
    }
}

/// Favourites are stored per recipe, and each option of a multi-option recipe is its own recipe - so switching options
/// dropped the star. Treat the whole group as one: favourite if any option is, sorted by its best placed option.
public class GroupFavoritePatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(BaseHideoutScheme), nameof(BaseHideoutScheme.FavoriteScheme));
    }

    [PatchPostfix]
    public static void Postfix(BaseHideoutScheme __instance, ref bool __result)
    {
        if (!__result && CraftGroups.GroupIds(__instance._id) is List<string> ids)
        {
            __result = ids.Any(id => PrefsUtils.TryGetFavoriteIndex(id, out _));
        }
    }
}

public class GroupFavoriteIndexPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertyGetter(typeof(BaseHideoutScheme), nameof(BaseHideoutScheme.FavoriteIndex));
    }

    [PatchPostfix]
    public static void Postfix(BaseHideoutScheme __instance, ref int __result)
    {
        if (CraftGroups.GroupIds(__instance._id) is List<string> ids)
        {
            foreach (string id in ids)
            {
                if (PrefsUtils.TryGetFavoriteIndex(id, out int index) && index < __result)
                {
                    __result = index;
                }
            }
        }
    }
}

/// Starring or un-starring one option applies to every option of the recipe, so the stored state stays consistent too.
public class GroupFavoriteSetPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.PropertySetter(typeof(BaseHideoutScheme), nameof(BaseHideoutScheme.FavoriteScheme));
    }

    [PatchPostfix]
    public static void Postfix(BaseHideoutScheme __instance, bool value)
    {
        if (!(CraftGroups.GroupIds(__instance._id) is List<string> ids))
        {
            return;
        }
        foreach (string id in ids)
        {
            bool stored = PrefsUtils.TryGetFavoriteIndex(id, out _);
            if (value && !stored)
            {
                PrefsUtils.AddFavoriteScheme(id);
            }
            else if (!value && stored)
            {
                PrefsUtils.DeleteFavoriteScheme(id);
            }
        }
    }
}

/// The panel only refreshes cards whose recipe uses the changed item - checked against the anchor, which may not be the
/// sibling on screen. Refresh every card instead so have/need counts stay right.
public class ItemChangedPatch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(ProductionPanel), nameof(ProductionPanel.ItemChangedHandler));
    }

    [PatchPostfix]
    public static void Postfix(ProductionPanel __instance)
    {
        __instance.UpdateViews();
    }
}
