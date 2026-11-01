// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using SPTarkov.Common.Models.Logging;

namespace TraderScarcity;

// One stack of an item - "Count" of "Item" (a 24 character template id).
public class ItemStack
{
    public string Item { get; set; } = "";
    public int Count { get; set; } = 1;
}

// One choice on either side of a recipe. Either a single stack (Item + Count) or a bundle (Items).
public class RecipeOption : ItemStack
{
    public List<ItemStack>? Items { get; set; }

    // Output options only - overrides the recipe's Minutes for this output.
    public double? Minutes { get; set; }

    public List<ItemStack> Stacks => Items is { Count: > 0 } ? Items : [new ItemStack { Item = Item, Count = Count }];
}

// A multi-option recipe. The player picks one AnyOf input and one OneOf output -
// the server expands it into one vanilla recipe per combination (see MultiRecipes).
public class MultiRecipe
{
    // Stable key - generated recipe ids are derived from it, so renaming it orphans crafts in progress.
    public string Id { get; set; } = "";
    public string Area { get; set; } = "Workbench";
    public int Level { get; set; } = 1;
    public double Minutes { get; set; } = 60;

    // Optional "#RRGGBB" (or "#RRGGBBAA") tile colour for the client plugin's Usable Crafting tint
    public string? Color { get; set; }
    public List<string>? Tools { get; set; }
    public List<ItemStack>? Extras { get; set; }
    public List<RecipeOption>? AnyOf { get; set; }
    public List<RecipeOption>? OneOf { get; set; }
}

// Mirrors recipes.jsonc.
public class CraftingConfig
{
    public List<MultiRecipe> Recipes { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static CraftingConfig Load(string modFolder, ISptLogger<ScarcityCraftingLoader> logger)
    {
        string path = Path.Combine(modFolder, "recipes.jsonc");
        try
        {
            return JsonSerializer.Deserialize<CraftingConfig>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            logger.Warning($"[TraderScarcity] Could not read {path}, no recipes added: {ex.Message}");
            return new();
        }
    }
}
