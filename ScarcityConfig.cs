// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Text.Json;
using SPTarkov.Common.Models.Logging;

namespace TraderScarcity;

// Tuning knobs - InStockChance is 0..1, quantities are inclusive.
public class StockRule
{
    public double InStockChance { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }
}

// Mirrors config.json. Defaults here are used for anything missing from the file, or for everything if the file can't be read.
public class ScarcityConfig
{
    // Print a summary to the server console on every roll (startup + each restock).
    public bool LogRolls { get; set; } = false;

    // Also roll stock for traders added by other mods. Off = only base game traders are affected.
    public bool IncludeModdedTraders { get; set; } = true;

    // Also roll stock for Fence. Off by default - his rotating stock is already random, whatever he has he has.
    public bool IncludeFence { get; set; } = false;

    // Add the multi-option crafting recipes from recipes.jsonc.
    public bool EnableRecipes { get; set; } = true;

    // Boon - chance an in-stock LL2+ offer is sold one loyalty level lower for this restock.
    public double LoyaltyBumpChance { get; set; } = 0.1;

    public StockRule Ammo { get; set; } = new() { InStockChance = 0.30, Min = 10, Max = 90 }; // Loose rounds
    public StockRule AmmoBox { get; set; } = new() { InStockChance = 0.30, Min = 1, Max = 1 };
    public StockRule Gear { get; set; } = new() { InStockChance = 0.25, Min = 1, Max = 1 };   // Weapons, armor, plates, helmets, rigs, backpacks, weapon mods
    public StockRule Other { get; set; } = new() { InStockChance = 0.35, Min = 1, Max = 1 };  // Meds, provisions, barter items, keys, containers, etc.

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static ScarcityConfig Load(string modFolder, ISptLogger<TraderScarcityLoader> logger)
    {
        string path = Path.Combine(modFolder, "config.json");
        ScarcityConfig config;
        try
        {
            config = JsonSerializer.Deserialize<ScarcityConfig>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            logger.Warning($"[TraderScarcity] Could not read {path}, using defaults: {ex.Message}");
            return new();
        }
        config.Validate(logger);
        return config;
    }

    // Out of range values are clamped rather than rejected, so one typo doesn't turn the whole mod off.
    private void Validate(ISptLogger<TraderScarcityLoader> logger)
    {
        LoyaltyBumpChance = ClampChance(LoyaltyBumpChance, nameof(LoyaltyBumpChance), logger);
        Ammo = ValidateRule(Ammo, nameof(Ammo), new ScarcityConfig().Ammo, logger);
        AmmoBox = ValidateRule(AmmoBox, nameof(AmmoBox), new ScarcityConfig().AmmoBox, logger);
        Gear = ValidateRule(Gear, nameof(Gear), new ScarcityConfig().Gear, logger);
        Other = ValidateRule(Other, nameof(Other), new ScarcityConfig().Other, logger);
    }

    private static StockRule ValidateRule(StockRule? rule, string name, StockRule fallback, ISptLogger<TraderScarcityLoader> logger)
    {
        if (rule is null)//"Gear": null in the file
        {
            logger.Warning($"[TraderScarcity] {name} is missing, using defaults");
            return fallback;
        }
        rule.InStockChance = ClampChance(rule.InStockChance, $"{name}.InStockChance", logger);
        if (rule.Min < 1)
        {
            logger.Warning($"[TraderScarcity] {name}.Min must be at least 1, using 1");
            rule.Min = 1;
        }
        if (rule.Max < rule.Min)
        {
            logger.Warning($"[TraderScarcity] {name}.Max is below Min, using {rule.Min}");
            rule.Max = rule.Min;
        }
        return rule;
    }

    private static double ClampChance(double value, string name, ISptLogger<TraderScarcityLoader> logger)
    {
        if (value is >= 0 and <= 1)
        {
            return value;
        }
        double clamped = Math.Clamp(value, 0, 1);
        logger.Warning($"[TraderScarcity] {name} must be between 0 and 1, using {clamped}");
        return clamped;
    }
}
