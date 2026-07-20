namespace ResourceTranslator;

internal sealed record TranslationChunk(int Index, int StartLine, IReadOnlyList<string> Lines,
    IReadOnlyList<string> ContextBefore, IReadOnlyList<string> ContextAfter)
{
    public string Content => string.Join("\n", Lines);
}

internal static class Chunker
{
    public static List<TranslationChunk> Create(string text, int maxChars, int contextLines)
    {
        var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
        var result = new List<TranslationChunk>();
        var start = 0;

        while (start < lines.Length)
        {
            var end = start;
            var size = 0;
            while (end < lines.Length)
            {
                var next = lines[end].Length + (end > start ? 1 : 0);
                if (end > start && size + next > maxChars) break;
                size += next;
                end++;
                if (size >= maxChars) break;
            }

            // Prefer a structural boundary near the end without creating tiny chunks.
            if (end < lines.Length && end - start > 8)
            {
                var minimum = start + Math.Max(4, (end - start) * 2 / 3);
                for (var candidate = end - 1; candidate >= minimum; candidate--)
                {
                    if (IsBoundary(lines[candidate]))
                    {
                        end = candidate + 1;
                        break;
                    }
                }
            }

            var beforeStart = Math.Max(0, start - contextLines);
            var afterEnd = Math.Min(lines.Length, end + contextLines);
            result.Add(new TranslationChunk(
                result.Count,
                start,
                lines[start..end],
                lines[beforeStart..start],
                lines[end..afterEnd]));
            start = end;
        }

        return result;
    }

    private static bool IsBoundary(string line)
    {
        var value = line.Trim();
        return value.Length == 0 || value is "}" or "}," or "]" or "]," or ");" ||
               value.EndsWith(";", StringComparison.Ordinal) ||
               value.EndsWith("</data>", StringComparison.OrdinalIgnoreCase) ||
               value.EndsWith("</string>", StringComparison.OrdinalIgnoreCase);
    }
}
