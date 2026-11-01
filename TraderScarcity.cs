// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Reflection;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Reflection.Patching;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Helpers.Traders;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Commerce;

namespace TraderScarcity;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.pmot.traderscarcity";
    public string Name { get; init; } = "TraderScarcity";
    public string Author { get; init; } = "P_moT";
    public List<string>? Contributors { get; init; }
    public SemanticVersioning.Version Version { get; init; } = new("0.2.0");
    public SemanticVersioning.Range SptVersion { get; init; } = new("4.1.6");


    public List<string>? Incompatibilities { get; init; }
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public string? Url { get; init; }
    public string License { get; init; } = "GPL-3.0-only";
    public bool HasPrepatcher { get; init; } = false;
}

// Runs after trader registration so traders added by other mods are rolled too
[Injectable(TypePriority = OnLoadOrder.TraderRegistration + 1)]
public class TraderScarcityLoader(
    ISptLogger<TraderScarcityLoader> logger,
    TradersTable tradersTable,
    TemplateTable templateTable,
    ItemHelper itemHelper,
    ModHelper modHelper,
    FenceService fenceService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        AssortScarcity.Config = ScarcityConfig.Load(modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly()), logger);

        ResetExpiredTraderPatch.Logger = logger;
        ResetExpiredTraderPatch.TemplateTable = templateTable;
        ResetExpiredTraderPatch.ItemHelper = itemHelper;

        new ResetExpiredTraderPatch().Enable();

        // Initial roll - restocks are re-rolled by the patch
        Dictionary<MongoId, Trader> traders = tradersTable;
        foreach (var trader in traders.Values)
        {
            AssortScarcity.Apply(trader, templateTable.Items, logger, itemHelper);
        }

        if (AssortScarcity.Config.IncludeFence)
        {
            FencePatches.Logger = logger;
            FencePatches.TemplateTable = templateTable;

            new GenerateFenceAssortsPatch().Enable();
            new PerformPartialRefreshPatch().Enable();

            // In case Fence's stock was already generated before this ran - offers are only ever rolled once, so this can't double up
            FencePatches.Roll(fenceService);
        }

        logger.Success("[TraderScarcity] Loaded");

        return Task.CompletedTask;
    }
}

// Runs right after the database is loaded and before mods that tune every recipe (SVM's craft time multiplier is
// Preload + 5), so multi-recipes get the same treatment as vanilla ones
[Injectable(TypePriority = OnLoadOrder.Preload + 4)]
public class ScarcityCraftingLoader(
    ISptLogger<ScarcityCraftingLoader> logger,
    HideoutTable hideoutTable,
    TemplateTable templateTable,
    ItemHelper itemHelper,
    ModHelper modHelper) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        // Runs before TraderScarcityLoader, so load config.json here - Load is shared, it's only read once
        string modFolder = modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());
        ScarcityConfig config = ScarcityConfig.Load(modFolder, logger);
        if (!config.EnableRecipes)
        {
            return Task.CompletedTask;
        }
        try
        {
            var crafting = CraftingConfig.Load(modFolder, logger);
            hideoutTable.Production.Recipes ??= [];
            MultiRecipes.Apply(crafting, hideoutTable.Production.Recipes, templateTable.Items, logger, itemHelper, config.LogRolls);
        }
        catch (Exception ex)
        {
            logger.Error($"[TraderScarcity] Failed to add recipes: {ex.Message}");
        }
        return Task.CompletedTask;
    }
}

public class ResetExpiredTraderPatch : AbstractPatch
{
    internal static ISptLogger<TraderScarcityLoader> Logger = null!;
    internal static TemplateTable TemplateTable = null!;
    internal static ItemHelper ItemHelper = null!;

    protected override MethodBase GetTargetMethod()
    {
        return typeof(TraderAssortHelper).GetMethod(nameof(TraderAssortHelper.ResetExpiredTrader))!;
    }

    // Postfix, because ResetExpiredTrader replaces trader.Assort.Items with a pristine copy - anything done before that is lost
    [PatchPostfix]
    public static void Postfix(Trader trader)
    {
        try
        {
            AssortScarcity.Apply(trader, TemplateTable.Items, Logger, ItemHelper);
        }
        catch (Exception ex)
        {
            Logger.Error($"[TraderScarcity] Failed to re-roll assort for trader {trader?.Base?.Nickname}: {ex.Message}");
        }
    }
}

// Fence's stock lives in FenceService's cache, built by GenerateFenceAssorts and topped up by PerformPartialRefresh.
// Both patches only run when IncludeFence is on.
internal static class FencePatches
{
    internal static ISptLogger<TraderScarcityLoader> Logger = null!;
    internal static TemplateTable TemplateTable = null!;

    internal static void Roll(FenceService fenceService)
    {
        try
        {
            AssortScarcity.ApplyFence(fenceService.GetMainFenceAssort(), fenceService.GetDiscountFenceAssort(), TemplateTable.Items, Logger);
        }
        catch (Exception ex)
        {
            Logger.Error($"[TraderScarcity] Failed to roll Fence's stock: {ex.Message}");
        }
    }
}

public class GenerateFenceAssortsPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(FenceService).GetMethod(nameof(FenceService.GenerateFenceAssorts))!;
    }

    [PatchPostfix]
    public static void Postfix(FenceService __instance)
    {
        FencePatches.Roll(__instance);
    }
}

public class PerformPartialRefreshPatch : AbstractPatch
{
    protected override MethodBase GetTargetMethod()
    {
        return typeof(FenceService).GetMethod(nameof(FenceService.PerformPartialRefresh))!;
    }

    [PatchPostfix]
    public static void Postfix(FenceService __instance)
    {
        FencePatches.Roll(__instance);
    }
}
