// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using SPTarkov.Common.Models.Logging;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Enums.Hideout;

namespace TraderScarcity;

// Where a generated recipe sits in its multi-option recipe. Sent to the client plugin, which folds siblings into one card.
public record CraftGroupRef(string Group, int Input, int Output, string? Color);

// Multi-option crafting.
// The client only understands fixed recipes, so each MultiRecipe is expanded into one vanilla recipe per (input option x output option).
// Without the client plugin those show as separate cards; with it they are merged into one card with pickers.
public static class MultiRecipes
{
    // recipe id -> group position, served by CraftGroupsRouter
    public static readonly Dictionary<string, CraftGroupRef> Groups = new();

    // Above this many combinations a recipe is still added, but the list gets crowded without the client plugin
    private const int CombinationWarning = 64;

    public static void Apply(CraftingConfig config, List<HideoutProduction> recipes, Dictionary<MongoId, TemplateItem> templates, ISptLogger<ScarcityCraftingLoader> logger, ItemHelper? itemHelper, bool log)
    {
        HashSet<MongoId> existing = recipes.Select(recipe => recipe.Id).ToHashSet();
        HashSet<string> seenGroups = new(StringComparer.OrdinalIgnoreCase);
        int groups = 0, added = 0;
        foreach (MultiRecipe group in config.Recipes)
        {
            string name = string.IsNullOrWhiteSpace(group.Id) ? "(no Id)" : group.Id;
            if (string.IsNullOrWhiteSpace(group.Id) || !seenGroups.Add(group.Id))
            {
                logger.Warning($"[TraderScarcity] Recipe {name}: missing or duplicate Id, skipped");
                continue;
            }
            if (!TryParseArea(group.Area, out HideoutAreas area))
            {
                logger.Warning($"[TraderScarcity] Recipe {name}: unknown Area \"{group.Area}\", skipped");
                continue;
            }

            //Tools and extras are shared by every combination, so one bad entry only drops that entry
            List<MongoId> tools = (group.Tools ?? []).Where(tool => IsKnown(tool, templates, name, "Tools", logger)).Select(tool => (MongoId)tool).ToList();
            List<ItemStack> extras = (group.Extras ?? []).Where(stack => IsValidStack(stack, templates, name, "Extras", logger)).ToList();

            //Keep the original option index, so a dropped option doesn't shift the ids of the ones after it
            var inputs = ValidOptions(group.AnyOf, templates, name, "AnyOf", logger);
            var outputs = ValidOptions(group.OneOf, templates, name, "OneOf", logger)
                .Where(output => output.Option.Items is not { Count: > 0 } || Warn(logger, name, "OneOf", "bundles are input only"))
                .ToList();
            if (inputs.Count == 0 || outputs.Count == 0)
            {
                logger.Warning($"[TraderScarcity] Recipe {name}: needs at least one valid AnyOf and one valid OneOf option, skipped");
                continue;
            }
            if (inputs.Count * outputs.Count > CombinationWarning)
            {
                logger.Warning($"[TraderScarcity] Recipe {name}: {inputs.Count * outputs.Count} combinations - consider splitting it");
            }

            string? color = group.Color;
            if (color is not null && !Regex.IsMatch(color, "^#?([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$"))
            {
                logger.Warning($"[TraderScarcity] Recipe {name}: Color \"{color}\" should look like #3a7bd5, using the default");
                color = null;
            }
            else if (color is not null && !color.StartsWith('#'))
            {
                color = "#" + color;
            }

            int level = Math.Max(group.Level, 1);
            foreach (var (input, inputIndex) in inputs)
            {
                foreach (var (output, outputIndex) in outputs)
                {
                    MongoId id = RecipeId(group.Id, inputIndex, outputIndex);
                    if (!existing.Add(id))
                    {
                        continue;
                    }
                    double minutes = Math.Max(output.Minutes ?? group.Minutes, 0.1);
                    List<Requirement> requirements = [new() { AreaType = (int)area, RequiredLevel = level, Type = "Area" }];
                    //Merge repeats, so "Kite + 2x Kite" asks for 3x Kite rather than two requirements the client can't satisfy separately
                    foreach (var stack in input.Stacks.Concat(extras).GroupBy(stack => stack.Item.ToLowerInvariant()))
                    {
                        requirements.Add(new()
                        {
                            TemplateId = stack.Key,
                            Count = stack.Sum(part => Math.Max(part.Count, 1)),
                            IsEncoded = false,
                            IsFunctional = false,
                            IsSpawnedInSession = false,
                            Type = "Item",
                        });
                    }
                    requirements.AddRange(tools.Select(tool => new Requirement { TemplateId = tool, Type = "Tool" }));
                    recipes.Add(new HideoutProduction
                    {
                        Id = id,
                        AreaType = area,
                        Requirements = requirements,
                        ProductionTime = Math.Round(minutes * 60),
                        EndProduct = output.Item,
                        Count = Math.Max(output.Count, 1),
                        IsEncoded = false,
                        Locked = false,
                        NeedFuelForAllProductionTime = false,
                        Continuous = false,
                        ProductionLimitCount = 0,
                        IsCodeProduction = false,
                    });
                    Groups[id.ToString()] = new CraftGroupRef(group.Id, inputIndex, outputIndex, color);
                    added++;
                }
            }
            groups++;
            if (log)
            {
                string ins = string.Join(" | ", inputs.Select(input => string.Join(" + ", input.Option.Stacks.Select(stack => $"{stack.Count}x {GetName(stack.Item, itemHelper)}"))));
                string outs = string.Join(" | ", outputs.Select(output => $"{output.Option.Count}x {GetName(output.Option.Item, itemHelper)}"));
                logger.LogWithColor($"[TraderScarcity]     {group.Id} ({area} L{level}): {ins}  ->  {outs}", Spectre.Console.Color.Blue);
            }
        }
        logger.LogWithColor($"[TraderScarcity] Crafting: {groups} multi-option recipe(s) -> {added} hideout recipes", Spectre.Console.Color.Blue);
    }

    // Stable across restarts and installs, so crafts in progress survive a server restart.
    public static MongoId RecipeId(string group, int input, int output)
    {
        byte[] hash = MD5.HashData(Encoding.UTF8.GetBytes($"traderscarcity|{group}|{input}|{output}"));
        return Convert.ToHexString(hash)[..24].ToLowerInvariant();
    }

    private static List<(RecipeOption Option, int Index)> ValidOptions(List<RecipeOption>? options, Dictionary<MongoId, TemplateItem> templates, string recipe, string field, ISptLogger<ScarcityCraftingLoader> logger)
    {
        List<(RecipeOption, int)> valid = [];
        for (int i = 0; i < (options?.Count ?? 0); i++)
        {
            RecipeOption option = options![i];
            if (option is not null && option.Stacks.All(stack => IsValidStack(stack, templates, recipe, field, logger)))
            {
                valid.Add((option, i));
            }
        }
        return valid;
    }

    private static bool IsValidStack(ItemStack? stack, Dictionary<MongoId, TemplateItem> templates, string recipe, string field, ISptLogger<ScarcityCraftingLoader> logger)
    {
        if (stack is null || !IsKnown(stack.Item, templates, recipe, field, logger))
        {
            return false;
        }
        if (stack.Count < 1)
        {
            logger.Warning($"[TraderScarcity] Recipe {recipe}: {field} count for {stack.Item} must be at least 1, using 1");
            stack.Count = 1;
        }
        return true;
    }

    private static bool IsKnown(string? tpl, Dictionary<MongoId, TemplateItem> templates, string recipe, string field, ISptLogger<ScarcityCraftingLoader> logger)
    {
        if (tpl is null || !MongoId.IsValidMongoId(tpl) || !templates.ContainsKey(tpl))
        {
            logger.Warning($"[TraderScarcity] Recipe {recipe}: {field} item \"{tpl}\" doesn't exist, that entry is skipped");
            return false;
        }
        return true;
    }

    private static bool Warn(ISptLogger<ScarcityCraftingLoader> logger, string recipe, string field, string message)
    {
        logger.Warning($"[TraderScarcity] Recipe {recipe}: {field} {message}, that entry is skipped");
        return false;
    }

    // Area names as the game has them, plus the names players know two stations by
    private static bool TryParseArea(string? value, out HideoutAreas area)
    {
        string? key = value?.Replace(" ", "");
        if (string.Equals(key, "NutritionUnit", StringComparison.OrdinalIgnoreCase))
        {
            area = HideoutAreas.Kitchen;
            return true;
        }
        if (string.Equals(key, "Lavatory", StringComparison.OrdinalIgnoreCase))
        {
            area = HideoutAreas.WaterCloset;
            return true;
        }
        return Enum.TryParse(value?.Replace(" ", ""), true, out area) && area != HideoutAreas.NotSet;
    }

    private static string GetName(string tpl, ItemHelper? itemHelper)
    {
        try//Logging must never break loading, e.g. if locales aren't loaded yet
        {
            return itemHelper?.GetItemName(tpl) ?? tpl;
        }
        catch
        {
            return tpl;
        }
    }
}
