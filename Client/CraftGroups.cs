// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.Linq;
using EFT.Hideout;
using Newtonsoft.Json;
using SPT.Common.Http;

namespace TraderScarcity.Client;

/// Position of a generated recipe in its multi-option recipe, as sent by the server.
public class GroupRef
{
    public string Group;
    public int Input;
    public int Output;
    // Optional "#RRGGBB" / "#RRGGBBAA" tile colour from recipes.json
    public string Color;
}

/// A sibling recipe and where it sits in its group.
public readonly struct Sibling
{
    public readonly ProductionScheme Scheme;
    public readonly GroupRef Ref;

    public Sibling(ProductionScheme scheme, GroupRef groupRef)
    {
        Scheme = scheme;
        Ref = groupRef;
    }
}

internal static class CraftGroups
{
    private const string Route = "/traderscarcity/craftgroups";

    // recipe id -> group position. Null until fetched; empty if the server has no multi-option recipes.
    private static Dictionary<string, GroupRef> _byRecipe;
    private static DateTime _nextAttempt = DateTime.MinValue;

    // group -> every recipe id in it, built with _byRecipe
    private static Dictionary<string, List<string>> _byGroup = new Dictionary<string, List<string>>();

    // Last picked (input, output) per group, so a card reopens on the player's choice
    private static readonly Dictionary<string, (int Input, int Output)> Selected = new Dictionary<string, (int, int)>();

    public static bool TryGet(string recipeId, out GroupRef groupRef)
    {
        groupRef = null;
        return recipeId != null && Load() && _byRecipe.TryGetValue(recipeId, out groupRef);
    }

    /// Every recipe of the group this producer knows about, ordered by input then output.
    public static List<Sibling> Siblings(string group, ItemsProducerBase producer)
    {
        List<Sibling> siblings = new List<Sibling>();
        if (producer?.Schemes == null || !Load())
        {
            return siblings;
        }
        foreach (BaseHideoutScheme scheme in producer.Schemes.Values)
        {
            if (scheme is ProductionScheme production && !production.locked && _byRecipe.TryGetValue(production._id, out GroupRef groupRef) && groupRef.Group == group)
            {
                siblings.Add(new Sibling(production, groupRef));
            }
        }
        return siblings.OrderBy(s => s.Ref.Input).ThenBy(s => s.Ref.Output).ToList();
    }

    /// The sibling a card or tile for this recipe should show: one that is producing or has items ready, else the player's
    /// last pick, else the recipe itself. Recipes outside a group come back unchanged.
    public static ProductionScheme Effective(ProductionScheme scheme, ItemsProducerBase producer)
    {
        if (scheme == null || producer == null || !TryGet(scheme._id, out GroupRef groupRef))
        {
            return scheme;
        }
        List<Sibling> siblings = Siblings(groupRef.Group, producer);
        Sibling chosen = siblings.FirstOrDefault(sibling => IsActive(producer, sibling.Scheme._id));
        if (chosen.Scheme == null && TryGetSelected(groupRef.Group, out var selected))
        {
            chosen = siblings.FirstOrDefault(sibling => sibling.Ref.Input == selected.Input && sibling.Ref.Output == selected.Output);
        }
        return chosen.Scheme ?? scheme;
    }

    /// Whether two recipes are the same recipe or siblings of one multi-option recipe.
    public static bool SameGroup(BaseHideoutScheme a, BaseHideoutScheme b)
    {
        return a == b || (a != null && b != null && TryGet(a._id, out GroupRef refA) && TryGet(b._id, out GroupRef refB) && refA.Group == refB.Group);
    }

    /// Every recipe id in the group of this recipe, or null if it isn't in one.
    public static List<string> GroupIds(string recipeId)
    {
        return TryGet(recipeId, out GroupRef groupRef) && _byGroup.TryGetValue(groupRef.Group, out List<string> ids) ? ids : null;
    }

    public static bool IsActive(ItemsProducerBase producer, string schemeId)
    {
        return producer.ProducingItems.ContainsKey(schemeId) || producer.CompleteItemsStorage.GetItemsCount(schemeId) > 0;
    }

    public static bool TryGetSelected(string group, out (int Input, int Output) selection)
    {
        return Selected.TryGetValue(group, out selection);
    }

    public static void Select(string group, int input, int output)
    {
        Selected[group] = (input, output);
    }

    private static bool Load()
    {
        if (_byRecipe != null)
        {
            return true;
        }
        if (DateTime.UtcNow < _nextAttempt)
        {
            return false;
        }
        try
        {
            string json = RequestHandler.GetJson(Route);
            _byRecipe = JsonConvert.DeserializeObject<Dictionary<string, GroupRef>>(json) ?? new Dictionary<string, GroupRef>();
            _byRecipe = _byRecipe.Where(entry => entry.Value?.Group != null).ToDictionary(entry => entry.Key, entry => entry.Value);
            _byGroup = _byRecipe.GroupBy(entry => entry.Value.Group).ToDictionary(group => group.Key, group => group.Select(entry => entry.Key).ToList());
            Plugin.Log.LogInfo($"Loaded {_byRecipe.Count} multi-option recipe(s) from the server");
            return true;
        }
        catch (Exception ex)
        {
            // Server mod missing or recipes disabled - siblings just show as separate cards. Retry later in case the server was busy.
            _nextAttempt = DateTime.UtcNow.AddMinutes(1);
            Plugin.Log.LogWarning($"Could not load multi-option recipes from {Route}, cards won't be merged: {ex.Message}");
            return false;
        }
    }
}
