// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using EFT.Hideout;
using EFT.UI;
using EFT.UI.DragAndDrop;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace TraderScarcity.Client;

// Usable Crafting replaces the production list with a grid of item tiles plus a details pane.
// Its tiles are built from the merged list's anchor recipe, so without this they'd always show the first output and
// miss producing / ready highlights for the other options. It's a soft dependency, so everything here goes through
// reflection and is only switched on when it's installed.
internal static class UsableCraftingCompat
{
    private static Type _gridType;
    private static Type _tileFactoryType;
    private static MethodInfo _tileFactoryShow;
    private static MethodInfo _tileSelect;
    private static MethodInfo _tileUpdateInfo;
    private static MethodInfo _tileFactoryClose;

    private const string BadgeName = "TraderScarcityBadge";
    private static readonly AccessTools.FieldRef<ItemView, Color> BackgroundColor = AccessTools.FieldRefAccess<ItemView, Color>("BackgroundColor");
    private static readonly AccessTools.FieldRef<ItemView, Color> OriginalBackgroundColor = AccessTools.FieldRefAccess<ItemView, Color>("OriginalBackgroundColor");

    public static bool Available => _gridType != null;

    public static void Enable()
    {
        _gridType = AccessTools.TypeByName("UsableCrafting.Views.GridProduceView");
        _tileFactoryType = AccessTools.TypeByName("UsableCrafting.Views.SimpleHideoutItemViewFactory");
        Type tileType = AccessTools.TypeByName("UsableCrafting.Views.SimpleHideoutItemView");
        _tileFactoryShow = _tileFactoryType == null ? null : AccessTools.Method(_tileFactoryType, "Show");
        _tileSelect = tileType == null ? null : AccessTools.Method(tileType, "Select");
        if (_gridType == null || _tileFactoryShow == null || _tileSelect == null
            || AccessTools.Method(_gridType, "Show") == null || AccessTools.Method(_gridType, "OnFavoriteUpdateHandler") == null)
        {
            _gridType = null;
            return;
        }
        _tileUpdateInfo = AccessTools.Method(tileType, "UpdateInfo");
        _tileFactoryClose = AccessTools.Method(_tileFactoryType, "Close");
        new TileShowsSelectedSiblingPatch().Enable();
        new RestoreSelectionPatch().Enable();
        new FavoriteLookupPatch().Enable();
        if (_tileUpdateInfo != null && _tileFactoryClose != null)
        {
            new TileTintPatch().Enable();
            new TileMarkerCleanupPatch().Enable();
        }
        Plugin.Log.LogInfo("Usable Crafting found - its tiles will follow the picked recipe option");
    }

    // After the details pane switches option, rebuild that recipe's tile so it shows the new output.
    public static void RefreshTile(ProduceView detailsView, ShowArgs args)
    {
        if (!Available || detailsView == null)
        {
            return;
        }
        try
        {
            Component grid = detailsView.GetComponentsInParent<Component>(true).FirstOrDefault(component => component != null && component.GetType() == _gridType);
            if (grid == null || detailsView.Scheme == null)
            {
                return;
            }
            Traverse gridFields = Traverse.Create(grid);
            if (!(gridFields.Field("Views").GetValue() is IDictionary views))
            {
                return;
            }
            // Tiles are keyed by the anchor recipe - find the one in the same group as the recipe now in the pane
            object key = views.Keys.Cast<BaseHideoutScheme>().FirstOrDefault(scheme => CraftGroups.SameGroup(scheme, detailsView.Scheme));
            if (!(key is ProductionScheme anchor) || !(views[key] is Component factory))
            {
                return;
            }
            Traverse oldTile = Traverse.Create(Traverse.Create(factory).Field("view").GetValue());
            if (oldTile.GetValue() == null)
            {
                return;
            }
            // Show rebuilds the tile (our prefix swaps in the picked sibling); the old one is released, so the grid mustn't
            // try to deselect it. Selecting the new tile makes it the grid's selection again and re-shows the pane.
            gridFields.Field("SelectedView").SetValue(null);
            _tileFactoryShow.Invoke(factory,
            [
                args.ItemUiContext, args.InventoryController, anchor,
                oldTile.Field("Producer").GetValue(), oldTile.Field("OnStartProducing").GetValue(), oldTile.Field("GetProducedItems").GetValue(),
                oldTile.Field("AvailableFavorites").GetValue(), oldTile.Field("OnClickAction").GetValue(),
            ]);
            object newTile = Traverse.Create(factory).Field("view").GetValue();
            if (newTile != null)
            {
                _tileSelect.Invoke(newTile, [false]);
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.LogError($"Failed to refresh Usable Crafting tile: {ex}");
        }
    }

    // Build each tile from the sibling that matters - so its icon, count and producing / ready highlights are for the
    // option actually being crafted or picked, not the first one.
    public class TileShowsSelectedSiblingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => _tileFactoryShow;

        [PatchPrefix]
        public static void Prefix(ref ProductionScheme scheme, ItemsProducerBase producer)
        {
            scheme = CraftGroups.Effective(scheme, producer);
        }

        [PatchPostfix]
        public static void Postfix(object __instance, ItemsProducerBase producer)
        {
            try
            {
                if (Traverse.Create(__instance).Field("view").GetValue() is ItemView tile)
                {
                    ApplyBadge(tile, producer);
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"Failed to mark Usable Crafting tile: {ex}");
            }
        }
    }

    // The tile's recipe, if it belongs to a multi-option recipe.
    private static bool TryGetGroup(ItemView tile, out GroupRef groupRef)
    {
        groupRef = null;
        return Traverse.Create(tile).Property("Scheme").GetValue() is ProductionScheme scheme && CraftGroups.TryGet(scheme._id, out groupRef);
    }

    // "3>5" in the tile's bottom-left corner - 3 input options, 5 outputs. The rest of the tile is Usable Crafting's.
    private static void ApplyBadge(ItemView tile, ItemsProducerBase producer)
    {
        RemoveBadge(tile);
        if (!Plugin.TileBadge.Value || !TryGetGroup(tile, out GroupRef groupRef))
        {
            return;
        }
        List<Sibling> siblings = CraftGroups.Siblings(groupRef.Group, producer);
        int inputs = siblings.Select(s => s.Ref.Input).Distinct().Count();
        int outputs = siblings.Select(s => s.Ref.Output).Distinct().Count();
        if (inputs * outputs <= 1)
        {
            return;
        }

        GameObject badge = new GameObject(BadgeName, typeof(RectTransform));
        badge.transform.SetParent(tile.transform, false);
        badge.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        UnityEngine.UI.Image background = badge.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(0f, 0f, 0f, 0.75f);
        background.raycastTarget = false; // Clicks still select the tile
        UnityEngine.UI.HorizontalLayoutGroup row = badge.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
        row.padding = new RectOffset(3, 3, 0, 0);
        row.childControlWidth = true;
        row.childControlHeight = true;
        UnityEngine.UI.ContentSizeFitter fitter = badge.AddComponent<UnityEngine.UI.ContentSizeFitter>();
        fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        RectTransform rect = (RectTransform)badge.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(2f, 2f);

        GameObject textObject = new GameObject("Text", typeof(RectTransform));
        textObject.transform.SetParent(badge.transform, false);
        TMPro.TextMeshProUGUI text = textObject.AddComponent<TMPro.TextMeshProUGUI>();
        TMPro.TextMeshProUGUI existing = tile.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true).FirstOrDefault(t => t != text);
        if (existing != null)
        {
            text.font = existing.font;
        }
        text.text = $"{inputs}>{outputs}";
        text.fontSize = 11;
        Color accent = TintFor(groupRef);
        text.color = new Color(Mathf.Lerp(accent.r, 1f, 0.5f), Mathf.Lerp(accent.g, 1f, 0.5f), Mathf.Lerp(accent.b, 1f, 0.5f), 1f);
        text.enableWordWrapping = false;
        text.raycastTarget = false;
    }

    // The recipe's own colour from recipes.jsonc, else the configured tint. A colour without opacity digits borrows the
    // configured tint's opacity, so it stays a background wash rather than covering the item icon.
    private static Color TintFor(GroupRef groupRef)
    {
        Color fallback = Plugin.TileTintColor.Value;
        if (string.IsNullOrEmpty(groupRef?.Color) || !ColorUtility.TryParseHtmlString(groupRef.Color, out Color color))
        {
            return fallback;
        }
        if (groupRef.Color.TrimStart('#').Length == 6)
        {
            color.a = fallback.a;
        }
        return color;
    }

    // Tiles come from a shared pool - take the badge off before one is handed to another recipe
    private static void RemoveBadge(ItemView tile)
    {
        Transform badge = tile != null ? tile.transform.Find(BadgeName) : null;
        if (badge != null)
        {
            UnityEngine.Object.Destroy(badge.gameObject);
        }
    }

    // Usable Crafting resets the tile background on every update (ready recipes turn its "finished" colour).
    // Tint multi-option recipes whenever it left the normal colour in place.
    public class TileTintPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => _tileUpdateInfo;

        [PatchPostfix]
        public static void Postfix(ItemView __instance)
        {
            if (__instance == null || !Plugin.TileTint.Value || !TryGetGroup(__instance, out GroupRef groupRef))
            {
                return;
            }
            if (BackgroundColor(__instance) == OriginalBackgroundColor(__instance))
            {
                BackgroundColor(__instance) = TintFor(groupRef);
                __instance.UpdateColor();
            }
        }
    }

    public class TileMarkerCleanupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => _tileFactoryClose;

        [PatchPrefix]
        public static void Prefix(object __instance)
        {
            if (Traverse.Create(__instance).Field("view").GetValue() is ItemView tile)
            {
                RemoveBadge(tile);
            }
        }
    }

    // The grid remembers its selected recipe between visits and reselects it by comparing against the list - which
    // only holds anchors. If a sibling was selected, point it at its group's anchor so the selection comes back.
    public class RestoreSelectionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(_gridType, "Show");

        [PatchPrefix]
        public static void Prefix(IEnumerable<ProductionScheme> items, object state)
        {
            if (items == null || state == null)
            {
                return;
            }
            Traverse selectedField = Traverse.Create(state).Field("SelectedScheme");
            if (selectedField.GetValue() is ProductionScheme selected && !items.Contains(selected))
            {
                ProductionScheme anchor = items.FirstOrDefault(item => CraftGroups.SameGroup(item, selected));
                if (anchor != null)
                {
                    selectedField.SetValue(anchor);
                }
            }
        }
    }

    // Starring a recipe in the details pane looks its tile up by recipe - a sibling isn't a key, the anchor is.
    public class FavoriteLookupPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method(_gridType, "OnFavoriteUpdateHandler");

        [PatchPrefix]
        public static void Prefix(object __instance, ref ProductionScheme scheme)
        {
            if (!(Traverse.Create(__instance).Field("Views").GetValue() is Dictionary<BaseHideoutScheme, IProduceView> views) || scheme == null || views.ContainsKey(scheme))
            {
                return;
            }
            ProductionScheme current = scheme;
            if (views.Keys.FirstOrDefault(key => CraftGroups.SameGroup(key, current)) is ProductionScheme anchor)
            {
                scheme = anchor;
            }
        }
    }
}
