using System.Text;
using System.Text.RegularExpressions;

namespace ResourceTranslator;

public enum GlossaryEntryKind { Keep, Translate }

public sealed record GlossaryEntry(GlossaryEntryKind Kind, string Source, string? Target);
public sealed record GlossaryParseResult(IReadOnlyList<GlossaryEntry> Entries, IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

public static class Glossary
{
    public static GlossaryParseResult Parse(string? text)
    {
        var entries = new List<GlossaryEntry>();
        var errors = new List<string>();
        var sources = new Dictionary<string, GlossaryEntry>(StringComparer.OrdinalIgnoreCase);
        var lines = (text ?? string.Empty).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var fields = line.Split('\t');
            GlossaryEntry? entry = null;
            if (fields.Length == 2 && fields[0] == "KEEP" && !string.IsNullOrWhiteSpace(fields[1]))
                entry = new(GlossaryEntryKind.Keep, fields[1], null);
            else if (fields.Length == 3 && fields[0] == "TRANSLATE" &&
                     !string.IsNullOrWhiteSpace(fields[1]) && !string.IsNullOrWhiteSpace(fields[2]))
                entry = new(GlossaryEntryKind.Translate, fields[1], fields[2]);
            else
                errors.Add($"Line {i + 1}: expected KEEP<TAB>source or TRANSLATE<TAB>source<TAB>target.");

            if (entry is null) continue;
            if (sources.TryGetValue(entry.Source, out var previous))
            {
                var same = previous.Kind == entry.Kind && string.Equals(previous.Target, entry.Target, StringComparison.Ordinal);
                errors.Add($"Line {i + 1}: {(same ? "duplicate" : "conflicting")} entry for '{entry.Source}' (first declared on line {entries.IndexOf(previous) + 1}).");
                continue;
            }
            sources.Add(entry.Source, entry);
            entries.Add(entry);
        }
        return new(entries, errors);
    }

    public static string Export(IEnumerable<GlossaryEntry> entries) => string.Join(Environment.NewLine,
        entries.Select(e => e.Kind == GlossaryEntryKind.Keep
            ? $"KEEP\t{e.Source}" : $"TRANSLATE\t{e.Source}\t{e.Target}"));

    public static string BuildInstruction(IReadOnlyList<GlossaryEntry> entries)
    {
        var terms = entries.Where(e => e.Kind == GlossaryEntryKind.Translate).ToArray();
        if (terms.Length == 0) return string.Empty;
        return "MANDATORY GLOSSARY (case-insensitive source matching; respect whole-word boundaries; preserve the source occurrence's capitalization where applicable). Use every target term exactly as specified and do not translate these source terms differently:\n" +
               string.Join("\n", terms.Select(e => $"{e.Source} → {e.Target}"));
    }

    public static string MaskKeepTerms(string input, IReadOnlyList<GlossaryEntry> entries, List<(string Marker, string Value)> tokens)
    {
        var terms = entries.Where(e => e.Kind == GlossaryEntryKind.Keep).OrderByDescending(e => e.Source.Length).ToArray();
        if (terms.Length == 0 || input.Length == 0) return input;
        var pattern = string.Join("|", terms.Select(e => Regex.Escape(e.Source)));
        return Regex.Replace(input, pattern, m =>
        {
            if (m.Index > 0 && IsWord(input[m.Index - 1]) && IsWord(m.Value[0])) return m.Value;
            var end = m.Index + m.Length;
            if (end < input.Length && IsWord(input[end - 1]) && IsWord(input[end])) return m.Value;
            var marker = $"\uE000RTKEEP{tokens.Count + 1:D5}\uE001";
            tokens.Add((marker, m.Value));
            return marker;
        }, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
}