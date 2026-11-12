// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text.Json;
using SPTarkov.Common.Models.Logging;

namespace TraderScarcity;

// Tuning knobs - InStockChance is 0..1, quantities are inclusive.
public class StockRule
{
    public double InStockChance { get; set; }
    public int Min { get; set; }
    public int Max { get; set; }

    public StockRule Clone()
    {
        return (StockRule)MemberwiseClone();
    }
}

// Mirrors config.json. Defaults here are used for anything missing from the file, or for everything if the file can't be read.
public class ScarcityConfig
{
    // Master switch for trader stock rolls. Off = traders go back to full stock at their next restock.
    public bool EnableScarcity { get; set; } = true;

    // Print a summary to the server console on every roll (startup + each restock).
    public bool LogRolls { get; set; } = false;

    // Also roll stock for traders added by other mods. Off = only base game traders are affected.
    public bool IncludeModdedTraders { get; set; } = true;

    // Which base game traders are rolled, by name. Fence is off by default - his rotating stock is already random, whatever he has he has.
    // Lightkeeper and Ref are never rolled. A trader missing from the file gets its default.
    public Dictionary<string, bool> Traders { get; set; } = DefaultTraders();

    // Item template ids that are never rolled, at any trader - they keep the trader's normal stock.
    public List<string> ExcludedItems { get; set; } = [];

    // Read from config files written before 0.3.3, when Fence had his own switch. Never written back.
    public bool? IncludeFence { get; set; }

    // The base game traders this mod can roll, in the order they're shown and written
    public static readonly string[] TraderNames = ["Prapor", "Therapist", "Skier", "Peacekeeper", "Mechanic", "Ragman", "Jaeger", "Fence"];

    public static bool DefaultFor(string trader) => trader != "Fence";

    private static Dictionary<string, bool> DefaultTraders() =>
        TraderNames.ToDictionary(name => name, DefaultFor, StringComparer.OrdinalIgnoreCase);

    public bool IsTraderEnabled(string trader) => Traders.TryGetValue(trader, out bool on) ? on : DefaultFor(trader);

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

    private static readonly object SaveLock = new();
    private static ScarcityConfig? _loaded;

    // The settings as they were when the server started - recipes are only added then, so the SIC page compares against this
    public static ScarcityConfig? AtStartup { get; private set; }

    // Set when config.json couldn't be read. The SIC page won't save then, so a typo in the file can't be overwritten with defaults.
    public static string? LoadError { get; private set; }

    // Read once and shared - the crafting loader needs it before the trader loader runs, and each warning should print once.
    public static ScarcityConfig Load<T>(string modFolder, ISptLogger<T> logger)
    {
        if (_loaded is null)
        {
            _loaded = Read(modFolder, logger);
            AtStartup = _loaded.Clone();
        }
        return _loaded;
    }

    private static ScarcityConfig Read<T>(string modFolder, ISptLogger<T> logger)
    {
        string path = Path.Combine(modFolder, "config.json");
        ScarcityConfig config;
        try
        {
            config = JsonSerializer.Deserialize<ScarcityConfig>(File.ReadAllText(path), JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            LoadError = $"Could not read {path}: {ex.Message}";
            logger.Warning($"[TraderScarcity] Could not read {path}, using defaults: {ex.Message}");
            return new();
        }
        config.Validate(logger);
        return config;
    }

    public ScarcityConfig Clone()
    {
        var copy = (ScarcityConfig)MemberwiseClone();
        copy.Traders = new Dictionary<string, bool>(Traders, StringComparer.OrdinalIgnoreCase);
        copy.ExcludedItems = [.. ExcludedItems];
        copy.Ammo = Ammo.Clone();
        copy.AmmoBox = AmmoBox.Clone();
        copy.Gear = Gear.Clone();
        copy.Other = Other.Clone();
        return copy;
    }

    // Written by the SIC page. Writes config.json and swaps the live settings, so trader stock follows from each trader's next restock.
    // nameOf labels each excluded item with a comment, so the file can still be read by hand
    public static void Save(string modFolder, ScarcityConfig config, Func<string, string>? nameOf = null)
    {
        lock (SaveLock)
        {
            if (LoadError != null)
            {
                throw new InvalidOperationException(LoadError);
            }

            // Written to one side first, so a failure part way through can't leave a damaged config.json
            string path = Path.Combine(modFolder, "config.json");
            string temp = path + ".tmp";
            File.WriteAllText(temp, config.ToJson(nameOf));
            File.Move(temp, path, overwrite: true);

            _loaded = config.Clone();
            AssortScarcity.Config = _loaded;
        }
    }

    // Same layout as the shipped file, one line per category, so it stays easy to edit by hand
    private string ToJson(Func<string, string>? nameOf)
    {
        static string B(bool value) => value ? "true" : "false";
        static string D(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
        static string R(string name, StockRule rule) =>
            $"\"{name}\":{new string(' ', 8 - name.Length)}{{ \"InStockChance\": {D(rule.InStockChance)}, \"Min\": {rule.Min}, \"Max\": {rule.Max} }}";

        return string.Join("\n",
            "{",
            $"    \"EnableScarcity\": {B(EnableScarcity)},",
            $"    \"LogRolls\": {B(LogRolls)},",
            $"    \"IncludeModdedTraders\": {B(IncludeModdedTraders)},",
            $"    \"Traders\": {{ {string.Join(", ", TraderNames.Select(name => $"\"{name}\": {B(IsTraderEnabled(name))}"))} }},",
            ExcludedJson(nameOf),
            $"    \"EnableRecipes\": {B(EnableRecipes)},",
            $"    \"LoyaltyBumpChance\": {D(LoyaltyBumpChance)},",
            $"    {R(nameof(Ammo), Ammo)},",
            $"    {R(nameof(AmmoBox), AmmoBox)},",
            $"    {R(nameof(Gear), Gear)},",
            $"    {R(nameof(Other), Other)}",
            "}") + "\n";
    }

    private string ExcludedJson(Func<string, string>? nameOf)
    {
        if (ExcludedItems.Count == 0)
        {
            return "    \"ExcludedItems\": [],";
        }
        var lines = new List<string> { "    \"ExcludedItems\": [" };
        for (int i = 0; i < ExcludedItems.Count; i++)
        {
            string entry = $"        \"{ExcludedItems[i]}\"{(i < ExcludedItems.Count - 1 ? "," : "")}";
            string? name = nameOf?.Invoke(ExcludedItems[i]);
            lines.Add(string.IsNullOrEmpty(name) ? entry : $"{entry.PadRight(36)} // {name}");
        }
        lines.Add("    ],");
        return string.Join("\n", lines);
    }

    // Out of range values are clamped rather than rejected, so one typo doesn't turn the whole mod off.
    private void Validate<T>(ISptLogger<T> logger)
    {
        var traders = DefaultTraders();
        foreach (var (name, on) in Traders ?? [])
        {
            if (traders.ContainsKey(name))
            {
                traders[name] = on;
            }
            else
            {
                logger.Warning($"[TraderScarcity] Traders: unknown trader \"{name}\", ignored. Known: {string.Join(", ", TraderNames)}");
            }
        }
        // Older config files had a separate switch for Fence - it still counts when Traders doesn't mention him
        if (IncludeFence is bool includeFence && Traders?.Keys.Any(name => name.Equals("Fence", StringComparison.OrdinalIgnoreCase)) != true)
        {
            traders["Fence"] = includeFence;
        }
        Traders = traders;
        IncludeFence = null;
        ExcludedItems = (ExcludedItems ?? []).Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct().ToList();

        LoyaltyBumpChance = ClampChance(LoyaltyBumpChance, nameof(LoyaltyBumpChance), logger);
        Ammo = ValidateRule(Ammo, nameof(Ammo), new ScarcityConfig().Ammo, logger);
        AmmoBox = ValidateRule(AmmoBox, nameof(AmmoBox), new ScarcityConfig().AmmoBox, logger);
        Gear = ValidateRule(Gear, nameof(Gear), new ScarcityConfig().Gear, logger);
        Other = ValidateRule(Other, nameof(Other), new ScarcityConfig().Other, logger);
    }

    private static StockRule ValidateRule<T>(StockRule? rule, string name, StockRule fallback, ISptLogger<T> logger)
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

    private static double ClampChance<T>(double value, string name, ISptLogger<T> logger)
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
