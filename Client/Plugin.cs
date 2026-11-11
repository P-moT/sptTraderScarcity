// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace TraderScarcity.Client;

public enum PickerLayout
{
    Edges,
    Corner,
}

// Client half of multi-option crafting.
// The server expands each multi-option recipe into sibling recipes (one per input x output). This plugin folds the
// siblings back into one card and adds input/output pickers to it. Pure UI - every craft is a real server recipe.
[BepInPlugin("com.pmot.traderscarcity.client", "TraderScarcity Crafting", "0.3.2")]
[BepInDependency("com.SPT.core", "4.1.0")]
[BepInDependency("com.deadlauncher.usablecrafting", BepInDependency.DependencyFlags.SoftDependency)]
public class Plugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    internal static ConfigEntry<PickerLayout> PickerLayout;
    internal static ConfigEntry<Vector2> PickerEdgeInset;
    internal static ConfigEntry<TextAnchor> PickerCorner;
    internal static ConfigEntry<Vector2> PickerOffset;
    internal static ConfigEntry<int> PickerFontSize;
    internal static ConfigEntry<bool> TileBadge;
    internal static ConfigEntry<bool> TileTint;
    internal static ConfigEntry<Color> TileTintColor;

    private void Awake()
    {
        Log = Logger;
        PickerLayout = Config.Bind("Picker", "Layout", Client.PickerLayout.Edges,
            "Edges: input picker at the left edge of the crafting window, output picker at the right edge, level with their recipe. Corner: both together in one corner of the card");
        PickerEdgeInset = Config.Bind("Picker", "EdgeInset", new Vector2(8f, 0f), "Edges layout - x: distance in from the window's left/right edge, y: up (+) or down (-) from the recipe's middle, in pixels");
        PickerCorner = Config.Bind("Picker", "Corner", TextAnchor.UpperRight, "Corner layout - which corner of the recipe card the pickers sit in");
        PickerOffset = Config.Bind("Picker", "Offset", new Vector2(-6f, -4f), "Corner layout - offset from that corner, in pixels");
        PickerFontSize = Config.Bind("Picker", "FontSize", 13, new ConfigDescription("Picker text size", new AcceptableValueRange<int>(8, 30)));
        TileBadge = Config.Bind("Usable Crafting", "Badge", true, "Mark multi-option recipe tiles with an \"inputs>outputs\" badge, e.g. 3>5. Applies when the station is next opened");
        TileTint = Config.Bind("Usable Crafting", "Tint", true, "Give multi-option recipe tiles their own background colour. Applies when the station is next opened");
        TileTintColor = Config.Bind("Usable Crafting", "TintColor", new Color(0.2f, 0.62f, 0.72f, 0.35f), "Background colour for multi-option recipe tiles");
        PickerLayout.SettingChanged += (_, _) => GroupPicker.RefreshAllLayouts();
        PickerEdgeInset.SettingChanged += (_, _) => GroupPicker.RefreshAllLayouts();
        PickerCorner.SettingChanged += (_, _) => GroupPicker.RefreshAllLayouts();
        PickerOffset.SettingChanged += (_, _) => GroupPicker.RefreshAllLayouts();
        PickerFontSize.SettingChanged += (_, _) => GroupPicker.RefreshAllLayouts();

        new MergeSiblingsPatch().Enable();
        new ShowSelectedSiblingPatch().Enable();
        new RefreshPickerPatch().Enable();
        new ItemChangedPatch().Enable();
        new GroupFavoritePatch().Enable();
        new GroupFavoriteIndexPatch().Enable();
        new GroupFavoriteSetPatch().Enable();
        UsableCraftingCompat.Enable(); // Loaded before us (soft dependency), so its types are already there
    }
}
