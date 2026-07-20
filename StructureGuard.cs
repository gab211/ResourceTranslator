using System.Text.RegularExpressions;

namespace ResourceTranslator;

internal static partial class StructureGuard
{
    [GeneratedRegex(@"\{\{[^{}]+\}\}|\{\d+(?::[^{}]+)?\}|%\d*\$?[sdif]|\$\{[^{}]+\}|#[A-Za-z_][A-Za-z0-9_]*#|:[A-Za-z_][A-Za-z0-9_]*")]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"</?[A-Za-z][^>]*>")]
    private static partial Regex TagRegex();

    [GeneratedRegex("^(\\s*(?:\"(?:\\\\.|[^\"])*\"|'(?:\\\\.|[^'])*'|[A-Za-z_$][A-Za-z0-9_$.-]*)\\s*:\\s*)")]
    private static partial Regex PropertyPrefixRegex();

    [GeneratedRegex(@"^(\s*[A-Za-z0-9_.-]+\s*[=:]\s*)")]
    private static partial Regex PropertiesPrefixRegex();

    public static bool Validate(string source, string translated, out string problem)
    {
        var sourceLines = source.Split('\n');
        var targetLines = translated.Split('\n');
        if (sourceLines.Length != targetLines.Length)
        {
            problem = $"Line count changed from {sourceLines.Length} to {targetLines.Length}.";
            return false;
        }

        for (var i = 0; i < sourceLines.Length; i++)
        {
            var a = sourceLines[i];
            var b = targetLines[i];
            if (LeadingWhitespace(a) != LeadingWhitespace(b))
            {
                problem = $"Indentation changed in line {i + 1}.";
                return false;
            }

            if (!SameMatches(PlaceholderRegex(), a, b))
            {
                problem = $"Placeholder changed in line {i + 1}.";
                return false;
            }

            if (!SameMatches(TagRegex(), a, b))
            {
                problem = $"XML/HTML tag changed in line {i + 1}.";
                return false;
            }

            var prefixA = GetPrefix(a);
            var prefixB = GetPrefix(b);
            if (prefixA is not null && !string.Equals(prefixA, prefixB, StringComparison.Ordinal))
            {
                problem = $"Resource key/property prefix changed in line {i + 1}.";
                return false;
            }
        }

        problem = string.Empty;
        return true;
    }

    private static string? GetPrefix(string line)
    {
        var match = PropertyPrefixRegex().Match(line);
        if (match.Success) return match.Groups[1].Value;
        match = PropertiesPrefixRegex().Match(line);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool SameMatches(Regex regex, string a, string b) =>
        regex.Matches(a).Select(m => m.Value).SequenceEqual(regex.Matches(b).Select(m => m.Value), StringComparer.Ordinal);

    private static string LeadingWhitespace(string value) => new(value.TakeWhile(char.IsWhiteSpace).ToArray());
}
