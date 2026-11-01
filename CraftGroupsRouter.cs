// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Utils;

namespace TraderScarcity;

// Tells the client plugin which generated recipes belong together: { recipeId: { Group, Input, Output } }.
// Empty when EnableRecipes is off, so the plugin has nothing to merge.
[Injectable]
public class CraftGroupsRouter(JsonUtil jsonUtil, HttpResponseUtil httpResponseUtil) : StaticRouter(jsonUtil,
[
    new RouteAction("/traderscarcity/craftgroups",
        (string url, IRequestData info, MongoId sessionId, string? output, CancellationToken cancellationToken) =>
            new ValueTask<object>(httpResponseUtil.NoBody(MultiRecipes.Groups))),
]);
