// TraderScarcity - Copyright (C) 2026 P_moT
// SPDX-License-Identifier: GPL-3.0-only

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TraderScarcity;

// One stack in the SIC recipe editor
public class StackDraft
{
    public string Item { get; set; } = "";
    public int Count { get; set; } = 1;

    public StackDraft Clone() => (StackDraft)MemberwiseClone();
}

public class OutputDraft : StackDraft
{
    // Overrides the recipe's Minutes for this output
    public double? Minutes { get; set; }

    public new OutputDraft Clone() => (OutputDraft)MemberwiseClone();
}

// A recipe as the SIC editor holds it. Inputs are AnyOf - each one is a list, because an input can be a bundle of items.
public class RecipeDraft
{
    private static int _nextKey;

    // Identifies the draft on the page, since Id itself can be edited on a new recipe
    public int Key { get; } = Interlocked.Increment(ref _nextKey);

    public string Id { get; set; } = "";
    // Whether this recipe was in recipes.jsonc when the editor opened. Its Id is then fixed, since renaming loses crafts in progress.
    public bool Existing { get; set; }
    // The "// ---- Name" heading it sits under in recipes.jsonc
    public string Section { get; set; } = "";
    public string Area { get; set; } = "Workbench";
    public int Level { get; set; } = 1;
    public double Minutes { get; set; } = 60;
    public string? Color { get; set; }
    // The comment after "Color" in recipes.jsonc, e.g. "T1 - blue". Kept as it is, and dropped if the colour is cleared.
    public string? ColorNote { get; set; }
    // Comment lines written inside the recipe in recipes.jsonc, one per line
    public string Note { get; set; } = "";
    public List<string> Tools { get; set; } = [];
    public List<StackDraft> Extras { get; set; } = [];
    public List<List<StackDraft>> Inputs { get; set; } = [];
    public List<OutputDraft> Outputs { get; set; } = [];

    public static RecipeDraft From(MultiRecipe recipe, string section, string note, string? colorNote)
    {
        return new RecipeDraft
        {
            Id = recipe.Id,
            Existing = true,
            Section = section,
            Note = note,
            ColorNote = colorNote,
            Area = recipe.Area,
            Level = recipe.Level,
            Minutes = recipe.Minutes,
            Color = string.IsNullOrWhiteSpace(recipe.Color) ? null : recipe.Color,
            Tools = recipe.Tools?.ToList() ?? [],
            Extras = recipe.Extras?.Select(stack => new StackDraft { Item = stack.Item, Count = stack.Count }).ToList() ?? [],
            Inputs = recipe.AnyOf?.Select(option => option.Stacks.Select(stack => new StackDraft { Item = stack.Item, Count = stack.Count }).ToList()).ToList() ?? [],
            Outputs = recipe.OneOf?.Select(option => new OutputDraft { Item = option.Item, Count = option.Count, Minutes = option.Minutes }).ToList() ?? [],
        };
    }
}

public sealed record RecipeFileContents(string Header, List<RecipeDraft> Recipes);

// Reads and writes recipes.jsonc for the SIC page. Writing keeps the file's own header and "// ----" section headings,
// and puts each item's English name next to its id, so the file stays readable by hand.
public static class RecipeFile
{
    private const int CommentColumn = 72;
    private static readonly object SaveLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly Regex SectionLine = new(@"^\s*//\s*-{3,}\s*(.*?)\s*$");
    private static readonly Regex IdLine = new("^\\s*\"Id\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"");

    private static readonly Regex ColorNoteLine = new(@"^\s*""Color""\s*:.*?//\s*(.*?)\s*$");
    private static readonly Regex CommentLine = new(@"^\s*//\s?(.*?)\s*$");

    public static string PathIn(string modFolder) => Path.Combine(modFolder, "recipes.jsonc");

    // Throws when the file can't be read or parsed, so the page can refuse to save over it
    public static RecipeFileContents Load(string modFolder)
    {
        string path = PathIn(modFolder);
        if (!File.Exists(path))
        {
            return new RecipeFileContents(DefaultHeader, []);
        }
        string text = File.ReadAllText(path);
        CraftingConfig config = JsonSerializer.Deserialize<CraftingConfig>(text, JsonOptions) ?? new();

        // Comments are invisible to the parser, so read them off the text: the section heading each recipe sits under,
        // the note after its Color, and any comment lines of its own
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var colorNotes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var notes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string section = "";
        string? current = null;
        foreach (string line in text.Split('\n'))
        {
            Match heading = SectionLine.Match(line);
            if (heading.Success)
            {
                section = heading.Groups[1].Value;
                current = null;
                continue;
            }
            Match id = IdLine.Match(line);
            if (id.Success)
            {
                current = JsonSerializer.Deserialize<string>($"\"{id.Groups[1].Value}\"") ?? "";
                sections.TryAdd(current, section);
                continue;
            }
            if (current == null)
            {
                continue;
            }
            Match colorNote = ColorNoteLine.Match(line);
            if (colorNote.Success && colorNote.Groups[1].Value.Length > 0)
            {
                colorNotes.TryAdd(current, colorNote.Groups[1].Value);
            }
            Match comment = CommentLine.Match(line);
            if (comment.Success)
            {
                if (!notes.TryGetValue(current, out List<string>? lines))
                {
                    notes[current] = lines = [];
                }
                lines.Add(comment.Groups[1].Value);
            }
        }

        List<RecipeDraft> recipes = config.Recipes
            .Where(recipe => recipe is not null)
            .Select(recipe => RecipeDraft.From(recipe, sections.GetValueOrDefault(recipe.Id ?? "", ""),
                string.Join("\n", notes.GetValueOrDefault(recipe.Id ?? "", [])), colorNotes.GetValueOrDefault(recipe.Id ?? "")))
            .ToList();
        return new RecipeFileContents(Header(text), recipes);
    }

    public static void Save(string modFolder, string header, IReadOnlyList<RecipeDraft> recipes, Func<string, string> nameOf)
    {
        lock (SaveLock)
        {
            // Written to one side first, so a failure part way through can't leave a damaged recipes.jsonc
            string path = PathIn(modFolder);
            string temp = path + ".tmp";
            File.WriteAllText(temp, Write(header, recipes, nameOf));
            File.Move(temp, path, overwrite: true);
        }
    }

    public static string Write(string header, IReadOnlyList<RecipeDraft> recipes, Func<string, string> nameOf)
    {
        var lines = new List<string>();
        lines.AddRange(header.TrimEnd('\r', '\n').Split('\n').Select(line => line.TrimEnd('\r')));
        lines.Add("{");
        lines.Add("    \"Recipes\": [");

        // Grouped by section, in the order each section first appears, so a new recipe lands under its heading
        List<IGrouping<string, RecipeDraft>> sections = recipes.GroupBy(recipe => recipe.Section.Trim()).ToList();
        for (int s = 0; s < sections.Count; s++)
        {
            if (s > 0)
            {
                lines.Add("");
            }
            if (sections[s].Key.Length > 0)
            {
                lines.Add($"        // {new string('-', 64)} {OneLine(sections[s].Key)}");
            }
            List<RecipeDraft> inSection = sections[s].ToList();
            for (int r = 0; r < inSection.Count; r++)
            {
                bool last = s == sections.Count - 1 && r == inSection.Count - 1;
                lines.AddRange(Render(RecipeLines(inSection[r], nameOf, last), "        "));
            }
        }

        lines.Add("    ]");
        lines.Add("}");
        return string.Join("\n", lines) + "\n";
    }

    private static List<(string Code, string? Comment)> RecipeLines(RecipeDraft recipe, Func<string, string> nameOf, bool last)
    {
        var properties = new List<List<(string Code, string? Comment)>>
        {
            new() { ($"\"Id\": {Str(recipe.Id.Trim())}", null) },
        };
        if (!string.IsNullOrWhiteSpace(recipe.Color))
        {
            properties.Add([($"\"Color\": {Str(recipe.Color.Trim())}", recipe.ColorNote)]);
        }
        properties.Add([($"\"Area\": {Str(recipe.Area)}", null)]);
        properties.Add([($"\"Level\": {recipe.Level}", null)]);
        properties.Add([($"\"Minutes\": {Num(recipe.Minutes)}", null)]);
        if (recipe.Tools.Count > 0)
        {
            properties.Add(ArrayProperty("Tools", recipe.Tools.Select(tool => Lines((Str(tool), nameOf(tool)))).ToList()));
        }
        if (recipe.Extras.Count > 0)
        {
            properties.Add(ArrayProperty("Extras", recipe.Extras.Select(stack => Lines((StackCode(stack), nameOf(stack.Item)))).ToList()));
        }
        // The recipe's own comment lines go just above its inputs
        List<(string Code, string? Comment)> anyOf = recipe.Note.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .Select(line => ("", (string?)line))
            .ToList();
        anyOf.AddRange(ArrayProperty("AnyOf", recipe.Inputs.Select(input => InputLines(input, nameOf)).ToList()));
        properties.Add(anyOf);
        properties.Add(ArrayProperty("OneOf", recipe.Outputs.Select(output => Lines((StackCode(output), nameOf(output.Item)))).ToList()));

        var lines = new List<(string, string?)> { ("{", null) };
        lines.AddRange(Indent(Join(properties), "    "));
        lines.Add((last ? "}" : "},", null));
        return lines;
    }

    // A single item is { "Item": id, "Count": n }; two or more are a bundle that are all used up together
    private static List<(string Code, string? Comment)> InputLines(List<StackDraft> input, Func<string, string> nameOf)
    {
        if (input.Count == 1)
        {
            return Lines((StackCode(input[0]), nameOf(input[0].Item)));
        }
        var lines = new List<(string, string?)> { ("{ \"Items\": [", null) };
        lines.AddRange(Indent(Join(input.Select(stack => Lines((StackCode(stack), nameOf(stack.Item)))).ToList()), "    "));
        lines.Add(("] }", null));
        return lines;
    }

    private static string StackCode(StackDraft stack)
    {
        string code = $"{{ \"Item\": {Str(stack.Item.Trim())}, \"Count\": {stack.Count}";
        if (stack is OutputDraft { Minutes: { } minutes })
        {
            code += $", \"Minutes\": {Num(minutes)}";
        }
        return code + " }";
    }

    private static List<(string Code, string? Comment)> ArrayProperty(string name, List<List<(string Code, string? Comment)>> elements)
    {
        var lines = new List<(string, string?)> { ($"\"{name}\": [", null) };
        lines.AddRange(Indent(Join(elements), "    "));
        lines.Add(("]", null));
        return lines;
    }

    // Comma after every element but the last, put before the element's comment
    private static List<(string Code, string? Comment)> Join(List<List<(string Code, string? Comment)>> elements)
    {
        var lines = new List<(string, string?)>();
        for (int i = 0; i < elements.Count; i++)
        {
            List<(string Code, string? Comment)> element = elements[i].ToList();
            if (i < elements.Count - 1 && element.Count > 0)
            {
                element[^1] = (element[^1].Code + ",", element[^1].Comment);
            }
            lines.AddRange(element);
        }
        return lines;
    }

    private static List<(string Code, string? Comment)> Lines((string Code, string? Comment) line) => [line];

    private static IEnumerable<(string Code, string? Comment)> Indent(IEnumerable<(string Code, string? Comment)> lines, string indent)
    {
        return lines.Select(line => (indent + line.Code, line.Comment));
    }

    private static IEnumerable<string> Render(IEnumerable<(string Code, string? Comment)> lines, string indent)
    {
        foreach ((string code, string? comment) in lines)
        {
            string text = indent + code;
            if (code.Trim().Length == 0)
            {
                yield return text + "// " + OneLine(comment ?? "");
                continue;
            }
            yield return string.IsNullOrWhiteSpace(comment)
                ? text
                : text.PadRight(Math.Max(CommentColumn, text.Length + 1)) + "// " + OneLine(comment);
        }
    }

    private static string Header(string text)
    {
        var header = new StringBuilder();
        foreach (string line in text.Split('\n'))
        {
            if (line.TrimStart().StartsWith('{'))
            {
                break;
            }
            header.Append(line.TrimEnd('\r')).Append('\n');
        }
        return header.Length > 0 ? header.ToString() : DefaultHeader;
    }

    private static string Str(string value) => JsonSerializer.Serialize(value);

    private static string Num(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string OneLine(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private const string DefaultHeader = "// TraderScarcity - multi-option crafting recipes. Edit them on the Trader Scarcity page in SIC, or by hand.\n";
}
