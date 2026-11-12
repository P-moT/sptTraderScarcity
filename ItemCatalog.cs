// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Text;
using System.Text.RegularExpressions;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Locales;

namespace TraderScarcity;

// One item as the recipe editor shows it. Id is the 24 character template id written to recipes.jsonc.
public sealed record CatalogItem(string Id, string Name, string ShortName, string Category)
{
    internal string SearchText { get; init; } = "";
}

// English names for every item in the database, so the SIC recipe editor can search by name and write template ids.
// Built on first use rather than at startup, so it only costs anything when the page is opened.
[Injectable(InjectionType.Singleton)]
public class ItemCatalog(TemplateTable templateTable, LocaleService localeService)
{
    private readonly object _lock = new();
    private Dictionary<string, CatalogItem>? _items;

    public CatalogItem? Get(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }
        return Items.TryGetValue(id.Trim(), out CatalogItem? item) ? item : null;
    }

    // Forget the index - the next lookup rebuilds it from the database as it is then
    public void Reset()
    {
        lock (_lock)
        {
            _items = null;
        }
    }

    // Name for recipes.jsonc comments and the page - the id itself when the item isn't in the database
    public string NameOf(string? id)
    {
        return Get(id)?.Name ?? id ?? "";
    }

    // Every word typed has to appear in the name or short name, in any order. A pasted template id finds that item.
    // Best matches first: exact short name, exact name, name starting with the search, words starting with each search word, then anything else.
    public List<CatalogItem> Search(string? query, int max = 50)
    {
        string normalized = Normalize(query ?? "");
        if (normalized.Length == 0)
        {
            return [];
        }
        if (Get(normalized) is { } byId)
        {
            return [byId];
        }

        string[] words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Items.Values
            .Where(item => words.All(item.SearchText.Contains))
            .Select(item => (Item: item, Rank: Rank(item, normalized, words)))
            .OrderBy(match => match.Rank)
            .ThenBy(match => match.Item.Name.Length)
            .ThenBy(match => match.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(max)
            .Select(match => match.Item)
            .ToList();
    }

    private static int Rank(CatalogItem item, string query, string[] words)
    {
        string name = Normalize(item.Name);
        string shortName = Normalize(item.ShortName);
        if (shortName == query) return 0;
        if (name == query) return 1;
        if (name.StartsWith(query, StringComparison.Ordinal)) return 2;
        if (shortName.StartsWith(query, StringComparison.Ordinal)) return 3;
        string[] itemWords = item.SearchText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.All(word => itemWords.Any(itemWord => itemWord.StartsWith(word, StringComparison.Ordinal)))) return 4;
        return 5;
    }

    private Dictionary<string, CatalogItem> Items
    {
        get
        {
            lock (_lock)
            {
                return _items ??= Build();
            }
        }
    }

    private Dictionary<string, CatalogItem> Build()
    {
        Dictionary<string, string> locale = localeService.GetLocaleDb("en");
        Dictionary<MongoId, TemplateItem> templates = templateTable.Items;
        var items = new Dictionary<string, CatalogItem>(StringComparer.OrdinalIgnoreCase);
        foreach (TemplateItem template in templates.Values)
        {
            // Nodes are categories (Ammo, Meds, ...), not items anyone can hold
            if (!string.Equals(template.Type, "Item", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            string id = template.Id.ToString();
            // Items added by mods sometimes have no English name - fall back to the template's own name so they can still be found
            if (!locale.TryGetValue($"{id} Name", out string? name) || string.IsNullOrWhiteSpace(name))
            {
                name = template.Name;
            }
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }
            locale.TryGetValue($"{id} ShortName", out string? shortName);
            string category = templates.TryGetValue(template.Parent, out TemplateItem? parent) ? Humanize(parent.Name) : "";
            items[id] = new CatalogItem(id, name.Trim(), shortName?.Trim() ?? "", category)
            {
                SearchText = Normalize($"{name} {shortName}"),
            };
        }
        return items;
    }

    // Lower case, quotes and brackets dropped, so `gunpowder kite` finds Gunpowder "Kite" and `7.62x39 bp` finds 7.62x39mm BP gzh
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(c) || c is '.' or '-' or '/' or '+' ? c : ' ');
        }
        return Regex.Replace(builder.ToString(), " {2,}", " ").Trim();
    }

    // "BuildingMaterial" -> "Building material"
    private static string Humanize(string? node)
    {
        if (string.IsNullOrEmpty(node))
        {
            return "";
        }
        string spaced = Regex.Replace(node, "(?<=[a-z])(?=[A-Z])", " ");
        return char.ToUpperInvariant(spaced[0]) + spaced[1..].ToLowerInvariant();
    }
}
