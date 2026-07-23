using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ResourceTranslator;

internal sealed record DocumentTranslationOutcome(
    string Text,
    TranslationRunReport? Report,
    int SegmentCount);

internal sealed class DocumentTranslationService(OpenAiClient client)
{
    private sealed record DocumentSegment(
        int Index,
        int Start,
        int Length,
        int LineNumber,
        string OriginalText,
        string PromptText,
        IReadOnlyList<DocumentToken> Tokens);

    private sealed record DocumentToken(
        string Marker,
        string Value);

    private sealed record SourceLine(
        int Number,
        int Start,
        string Text);

    private static readonly HashSet<string> TranslatableFrontMatterKeys =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "title",
            "linktitle",
            "description",
            "summary",
            "subtitle",
            "heading",
            "seotitle",
            "seodescription",
            "pagetitle",
            "menutitle"
        };

    private static readonly Regex FrontMatterPropertyRegex = new(
        @"^(?<indent>\s*)(?<key>[A-Za-z0-9_.-]+)\s*(?<separator>:|=)\s*(?<value>.*)$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownFenceRegex = new(
        @"^\s*(?<fence>`{3,}|~{3,})",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownHorizontalRuleRegex = new(
        @"^\s{0,3}((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownTableSeparatorRegex = new(
        @"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownReferenceRegex = new(
        @"^\s*\[[^\]]+\]:\s*\S+",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownHtmlBlockStartRegex = new(
        @"^\s*<(?<name>div|section|article|header|footer|main|aside|nav|form|figure|figcaption|table|thead|tbody|tfoot|tr|ul|ol|blockquote|details|summary)\b",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownHtmlTagRegex = new(
        @"<\s*(?<closing>/)?\s*(?<name>[A-Za-z0-9:-]+)\b[^>]*?(?<selfclosing>/)?\s*>",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownProtectedInlineRegex = new(
        @"\{\{[^{}\r\n]*\}\}" +
        @"|`+[^`\r\n]*`+" +
        @"|<!--[\s\S]*?-->" +
        @"|</?[^>\r\n]+>" +
        @"|https?://[^\s\)\]<>""']+" +
        @"|\]\([^\)\r\n]+\)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex HtmlAttributeRegex = new(
        @"(?<name>title|alt|placeholder|aria-label)\s*=\s*(?<quote>[""'])(?<value>.*?)(\k<quote>)",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    private static readonly Regex HtmlTagNameRegex = new(
        @"^<\s*(?<closing>/)?\s*(?<name>[A-Za-z0-9:-]+)",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex HtmlMetaDescriptionRegex = new(
        @"<\s*meta\b(?=[^>]*\bname\s*=\s*([""'])description\1)[^>]*\bcontent\s*=\s*(?<quote>[""'])(?<value>.*?)(\k<quote>)[^>]*>",
        RegexOptions.Compiled |
        RegexOptions.IgnoreCase |
        RegexOptions.CultureInvariant);

    public static bool Supports(string fileName)
    {
        var extension = Path
            .GetExtension(fileName)
            .ToLowerInvariant();

        return extension is
            ".md" or
            ".markdown" or
            ".html" or
            ".htm";
    }

    public async Task<DocumentTranslationOutcome> TranslateAsync(
        string source,
        string fileName,
        string outputFilePath,
        string targetLanguage,
        string customInstruction,
        int chunkSize,
        int contextLines,
        ApiProvider provider,
        string baseUrl,
        string apiKey,
        string model,
        IProgress<TranslationProgress> progress,
        CancellationToken ct)
    {
        var extension = Path
            .GetExtension(fileName)
            .ToLowerInvariant();

        var rawSegments = extension is ".md" or ".markdown"
            ? ExtractMarkdownSegments(source)
            : ExtractHtmlSegments(source);

        var segments = rawSegments
            .OrderBy(segment => segment.Start)
            .Select(
                (segment, index) => PrepareSegment(segment, index))
            .ToArray();

        if (segments.Length == 0)
        {
            progress.Report(
                new TranslationProgress(
                    1,
                    1,
                    "No translatable document segments were found."));

            return new DocumentTranslationOutcome(
                source,
                null,
                0);
        }

        var pseudoResource = BuildPseudoResource(segments);
        var engine = new TranslationEngine(client);

        var documentInstruction =
            "The values are visible text fragments extracted from a Hugo " +
            "Markdown or HTML document. Preserve Markdown inline syntax, " +
            "Hugo template markers, HTML fragments and protected placeholders " +
            "exactly. Do not add headings, list markers or punctuation that " +
            "is not present in the value. Do not translate CSS stylesheets, " +
            "JavaScript code, or inline code blocks.";

        if (segments.Any(segment => segment.OriginalText.Length < 80))
        {
            documentInstruction +=
                Environment.NewLine +
                "For very short UI strings such as headlines, buttons, " +
                "labels, and calls to action, translate naturally in the " +
                "context of the surrounding HTML section instead of using " +
                "overly literal wording.";
        }

        if (!string.IsNullOrWhiteSpace(customInstruction))
        {
            documentInstruction +=
                Environment.NewLine +
                customInstruction;
        }

        var translatedResource = await engine.TranslateAsync(
            pseudoResource,
            Path.GetFileName(fileName) + ".segments.json",
            targetLanguage,
            documentInstruction,
            chunkSize,
            contextLines,
            provider,
            baseUrl,
            apiKey,
            model,
            progress,
            ct,
            outputFilePath);

        var translatedValues = ParseTranslatedResource(
            translatedResource,
            segments);

        var translatedDocument = ReconstructDocument(
            source,
            segments,
            translatedValues);

        return new DocumentTranslationOutcome(
            translatedDocument,
            engine.LastRunReport,
            segments.Length);
    }

    private static IReadOnlyList<DocumentSegment> ExtractMarkdownSegments(
        string source)
    {
        var result = new List<DocumentSegment>();
        var lines = EnumerateLines(source).ToArray();

        var frontMatterStart = -1;
        var frontMatterEnd = -1;
        string? frontMatterFence = null;

        if (lines.Length > 0)
        {
            var first = lines[0].Text.Trim();

            if (first is "---" or "+++")
            {
                frontMatterStart = 0;
                frontMatterFence = first;

                for (var index = 1; index < lines.Length; index++)
                {
                    if (lines[index].Text.Trim() == frontMatterFence)
                    {
                        frontMatterEnd = index;
                        break;
                    }
                }
            }
        }

        if (frontMatterStart == 0 && frontMatterEnd > 0)
        {
            for (var index = 1; index < frontMatterEnd; index++)
            {
                TryAddFrontMatterSegment(
                    result,
                    lines[index]);
            }
        }

        var bodyStart = frontMatterEnd >= 0
            ? frontMatterEnd + 1
            : 0;

        var inFence = false;
        char fenceCharacter = '\0';
        var minimumFenceLength = 0;
        var inHtmlComment = false;
        var inStyleBlock = false;
        var inScriptBlock = false;
        var inHtmlBlock = false;
        var htmlBlockStartIndex = -1;
        var htmlBlockTagStack = new Stack<string>();

        for (var index = bodyStart; index < lines.Length; index++)
        {
            var line = lines[index];
            var trimmed = line.Text.Trim();

            if (inHtmlComment)
            {
                if (line.Text.Contains("-->", StringComparison.Ordinal))
                    inHtmlComment = false;

                continue;
            }

            if (line.Text.Contains("<!--", StringComparison.Ordinal))
            {
                if (!line.Text.Contains("-->", StringComparison.Ordinal))
                    inHtmlComment = true;

                continue;
            }

            if (inStyleBlock)
            {
                if (line.Text.Contains("</style>", StringComparison.OrdinalIgnoreCase))
                    inStyleBlock = false;

                continue;
            }

            if (line.Text.Contains("<style", StringComparison.OrdinalIgnoreCase))
            {
                if (!line.Text.Contains("</style>", StringComparison.OrdinalIgnoreCase))
                    inStyleBlock = true;

                continue;
            }

            if (inScriptBlock)
            {
                if (line.Text.Contains("</script>", StringComparison.OrdinalIgnoreCase))
                    inScriptBlock = false;

                continue;
            }

            if (line.Text.Contains("<script", StringComparison.OrdinalIgnoreCase))
            {
                if (!line.Text.Contains("</script>", StringComparison.OrdinalIgnoreCase))
                    inScriptBlock = true;

                continue;
            }

            if (inHtmlBlock)
            {
                UpdateMarkdownHtmlBlockState(
                    line.Text,
                    htmlBlockTagStack);

                if (htmlBlockTagStack.Count > 0)
                    continue;

                AddMarkdownHtmlBlockSegment(
                    result,
                    lines,
                    htmlBlockStartIndex,
                    index,
                    source);

                inHtmlBlock = false;
                htmlBlockStartIndex = -1;
                continue;
            }

            if (TryGetMarkdownHtmlBlockStartTag(
                    line.Text,
                    out _))
            {
                inHtmlBlock = true;
                htmlBlockStartIndex = index;
                htmlBlockTagStack.Clear();

                UpdateMarkdownHtmlBlockState(
                    line.Text,
                    htmlBlockTagStack);

                if (htmlBlockTagStack.Count == 0)
                {
                    AddMarkdownHtmlBlockSegment(
                        result,
                        lines,
                        htmlBlockStartIndex,
                        index,
                        source);

                    inHtmlBlock = false;
                    htmlBlockStartIndex = -1;
                }

                continue;
            }

            var fenceMatch = MarkdownFenceRegex.Match(line.Text);

            if (fenceMatch.Success)
            {
                var fence = fenceMatch.Groups["fence"].Value;

                if (!inFence)
                {
                    inFence = true;
                    fenceCharacter = fence[0];
                    minimumFenceLength = fence.Length;
                }
                else if (fence[0] == fenceCharacter &&
                         fence.Length >= minimumFenceLength)
                {
                    inFence = false;
                }

                continue;
            }

            if (inFence ||
                trimmed.Length == 0 ||
                MarkdownHorizontalRuleRegex.IsMatch(line.Text) ||
                MarkdownReferenceRegex.IsMatch(line.Text))
            {
                continue;
            }

            if (MarkdownTableSeparatorRegex.IsMatch(line.Text))
                continue;

            if (LooksLikeMarkdownTable(line.Text))
            {
                AddMarkdownTableSegments(result, line);
                continue;
            }

            AddMarkdownBodySegment(result, line);
        }

        if (inHtmlBlock && htmlBlockStartIndex >= 0)
        {
            AddMarkdownHtmlBlockSegment(
                result,
                lines,
                htmlBlockStartIndex,
                lines.Length - 1,
                source);
        }

        return RemoveOverlappingSegments(result);
    }

    private static IReadOnlyList<DocumentSegment> ExtractHtmlSegments(
        string source)
    {
        var result = new List<DocumentSegment>();
        AddHtmlSegments(
            result,
            source,
            0,
            source.Length,
            1);

        return RemoveOverlappingSegments(result);
    }

    private static void TryAddFrontMatterSegment(
        ICollection<DocumentSegment> result,
        SourceLine line)
    {
        var match = FrontMatterPropertyRegex.Match(line.Text);

        if (!match.Success)
            return;

        var key = match.Groups["key"].Value;

        if (!TranslatableFrontMatterKeys.Contains(key))
            return;

        var valueGroup = match.Groups["value"];
        var rawValue = valueGroup.Value;

        if (string.IsNullOrWhiteSpace(rawValue))
            return;

        var localStart = valueGroup.Index;
        var localLength = valueGroup.Length;

        TrimRange(line.Text, ref localStart, ref localLength);

        if (localLength <= 0)
            return;

        var first = line.Text[localStart];
        var last = line.Text[localStart + localLength - 1];

        if ((first == '"' && last == '"') ||
            (first == '\'' && last == '\''))
        {
            localStart++;
            localLength -= 2;
        }

        if (localLength <= 0)
            return;

        var value = line.Text.Substring(localStart, localLength);

        if (!LooksTranslatable(value))
            return;

        result.Add(
            new DocumentSegment(
                result.Count,
                line.Start + localStart,
                localLength,
                line.Number,
                value,
                value,
                Array.Empty<DocumentToken>()));
    }

    private static void AddMarkdownBodySegment(
        ICollection<DocumentSegment> result,
        SourceLine line)
    {
        var localStart = 0;
        var localLength = line.Text.Length;

        TrimRange(line.Text, ref localStart, ref localLength);

        if (localLength <= 0)
            return;

        ConsumeMarkdownPrefix(
            line.Text,
            ref localStart,
            ref localLength);

        TrimRange(line.Text, ref localStart, ref localLength);

        if (localLength <= 0)
            return;

        var candidate = line.Text.Substring(localStart, localLength);

        if (!LooksTranslatable(candidate))
            return;

        result.Add(
            new DocumentSegment(
                result.Count,
                line.Start + localStart,
                localLength,
                line.Number,
                candidate,
                candidate,
                Array.Empty<DocumentToken>()));
    }

    private static void AddMarkdownTableSegments(
        ICollection<DocumentSegment> result,
        SourceLine line)
    {
        var cellStart = 0;
        var inCode = false;
        var escaped = false;

        for (var index = 0; index <= line.Text.Length; index++)
        {
            var atEnd = index == line.Text.Length;
            var character = atEnd ? '\0' : line.Text[index];

            if (!atEnd)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (character == '`')
                {
                    inCode = !inCode;
                    continue;
                }
            }

            if (!atEnd && (character != '|' || inCode))
                continue;

            var localStart = cellStart;
            var localLength = index - cellStart;

            TrimRange(
                line.Text,
                ref localStart,
                ref localLength);

            if (localLength > 0)
            {
                var candidate = line.Text.Substring(
                    localStart,
                    localLength);

                if (LooksTranslatable(candidate))
                {
                    result.Add(
                        new DocumentSegment(
                            result.Count,
                            line.Start + localStart,
                            localLength,
                            line.Number,
                            candidate,
                            candidate,
                            Array.Empty<DocumentToken>()));
                }
            }

            cellStart = index + 1;
        }
    }

    private static void AddHtmlAttributeSegments(
        ICollection<DocumentSegment> result,
        string source,
        int tagStart,
        string tagText,
        int lineNumber)
    {
        foreach (Match match in HtmlAttributeRegex.Matches(tagText))
        {
            var valueGroup = match.Groups["value"];
            var value = valueGroup.Value;

            if (!LooksTranslatable(value))
                continue;

            result.Add(
                new DocumentSegment(
                    result.Count,
                    tagStart + valueGroup.Index,
                    valueGroup.Length,
                    lineNumber + CountNewLines(
                        tagText,
                        0,
                        valueGroup.Index),
                    value,
                    value,
                    Array.Empty<DocumentToken>()));
        }

        var metaMatch = HtmlMetaDescriptionRegex.Match(tagText);

        if (!metaMatch.Success)
            return;

        var metaValueGroup = metaMatch.Groups["value"];
        var metaValue = metaValueGroup.Value;

        if (!LooksTranslatable(metaValue))
            return;

        result.Add(
            new DocumentSegment(
                result.Count,
                tagStart + metaValueGroup.Index,
                metaValueGroup.Length,
                lineNumber + CountNewLines(
                    tagText,
                    0,
                    metaValueGroup.Index),
                metaValue,
                metaValue,
                Array.Empty<DocumentToken>()));
    }

    private static void AddTrimmedSegment(
        ICollection<DocumentSegment> result,
        string source,
        int start,
        int length,
        int lineNumber)
    {
        var localStart = start;
        var localLength = length;

        while (localLength > 0 &&
               char.IsWhiteSpace(source[localStart]))
        {
            if (source[localStart] == '\n')
                lineNumber++;

            localStart++;
            localLength--;
        }

        while (localLength > 0 &&
               char.IsWhiteSpace(
                   source[localStart + localLength - 1]))
        {
            localLength--;
        }

        if (localLength <= 0)
            return;

        var value = source.Substring(localStart, localLength);

        if (!LooksTranslatable(value))
            return;

        result.Add(
            new DocumentSegment(
                result.Count,
                localStart,
                localLength,
                lineNumber,
                value,
                value,
                Array.Empty<DocumentToken>()));
    }

    private static DocumentSegment PrepareSegment(
        DocumentSegment sourceSegment,
        int index)
    {
        var tokens = new List<DocumentToken>();

        var promptText = MarkdownProtectedInlineRegex.Replace(
            sourceSegment.OriginalText,
            match =>
            {
                var marker =
                    "{{RTDOC_" +
                    (index + 1).ToString("D5") +
                    "_" +
                    (tokens.Count + 1).ToString("D3") +
                    "}}";

                tokens.Add(
                    new DocumentToken(
                        marker,
                        match.Value));

                return marker;
            });

        return sourceSegment with
        {
            Index = index,
            PromptText = promptText,
            Tokens = tokens
        };
    }

    private static string BuildPseudoResource(
        IReadOnlyList<DocumentSegment> segments)
    {
        var values = new Dictionary<string, string>(
            segments.Count,
            StringComparer.Ordinal);

        foreach (var segment in segments)
        {
            values[CreateSegmentKey(segment.Index)] =
                segment.PromptText;
        }

        return JsonSerializer.Serialize(
            values,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
    }

    private static IReadOnlyDictionary<int, string> ParseTranslatedResource(
        string translatedResource,
        IReadOnlyList<DocumentSegment> segments)
    {
        using var document = JsonDocument.Parse(translatedResource);

        var result = new Dictionary<int, string>();

        foreach (var segment in segments)
        {
            var key = CreateSegmentKey(segment.Index);

            if (!document.RootElement.TryGetProperty(
                    key,
                    out var valueElement) ||
                valueElement.ValueKind != JsonValueKind.String)
            {
                result[segment.Index] = segment.OriginalText;
                continue;
            }

            var translatedValue =
                valueElement.GetString() ?? segment.OriginalText;

            if (!TryRestoreDocumentTokens(
                    segment,
                    translatedValue,
                    out var restored))
            {
                result[segment.Index] = segment.OriginalText;
                continue;
            }

            result[segment.Index] = restored;
        }

        return result;
    }

    private static bool TryRestoreDocumentTokens(
        DocumentSegment segment,
        string translatedValue,
        out string restored)
    {
        var candidate = translatedValue;

        foreach (var token in segment.Tokens)
        {
            if (CountOccurrences(candidate, token.Marker) != 1)
            {
                restored = segment.OriginalText;
                return false;
            }
        }

        foreach (var token in segment.Tokens)
        {
            candidate = candidate.Replace(
                token.Marker,
                token.Value,
                StringComparison.Ordinal);
        }

        restored = candidate;
        return true;
    }

    private static string ReconstructDocument(
        string source,
        IReadOnlyList<DocumentSegment> segments,
        IReadOnlyDictionary<int, string> translatedValues)
    {
        var result = new StringBuilder(source);

        foreach (var segment in segments
                     .OrderByDescending(item => item.Start))
        {
            result.Remove(segment.Start, segment.Length);
            result.Insert(
                segment.Start,
                translatedValues.TryGetValue(
                    segment.Index,
                    out var translated)
                    ? translated
                    : segment.OriginalText);
        }

        return result.ToString();
    }

    private static IReadOnlyList<DocumentSegment> RemoveOverlappingSegments(
        IEnumerable<DocumentSegment> segments)
    {
        var ordered = segments
            .OrderBy(segment => segment.Start)
            .ThenByDescending(segment => segment.Length)
            .ToArray();

        var result = new List<DocumentSegment>();
        var lastEnd = -1;

        foreach (var segment in ordered)
        {
            if (segment.Start < lastEnd)
                continue;

            result.Add(segment);
            lastEnd = segment.Start + segment.Length;
        }

        return result;
    }

    private static IEnumerable<SourceLine> EnumerateLines(string source)
    {
        var start = 0;
        var lineNumber = 1;

        while (start < source.Length)
        {
            var end = start;

            while (end < source.Length &&
                   source[end] != '\r' &&
                   source[end] != '\n')
            {
                end++;
            }

            yield return new SourceLine(
                lineNumber,
                start,
                source.Substring(start, end - start));

            if (end >= source.Length)
                yield break;

            if (source[end] == '\r' &&
                end + 1 < source.Length &&
                source[end + 1] == '\n')
            {
                start = end + 2;
            }
            else
            {
                start = end + 1;
            }

            lineNumber++;
        }
    }

    private static void ConsumeMarkdownPrefix(
        string line,
        ref int start,
        ref int length)
    {
        var end = start + length;
        var index = start;

        while (index < end && char.IsWhiteSpace(line[index]))
            index++;

        while (index < end && line[index] == '>')
        {
            index++;

            while (index < end && line[index] == ' ')
                index++;
        }

        if (index < end && line[index] == '#')
        {
            while (index < end && line[index] == '#')
                index++;

            while (index < end && line[index] == ' ')
                index++;
        }
        else if (index + 1 < end &&
                 (line[index] == '-' ||
                  line[index] == '*' ||
                  line[index] == '+') &&
                 char.IsWhiteSpace(line[index + 1]))
        {
            index += 2;

            if (index + 2 < end &&
                line[index] == '[' &&
                (line[index + 1] == ' ' ||
                 line[index + 1] == 'x' ||
                 line[index + 1] == 'X') &&
                line[index + 2] == ']')
            {
                index += 3;

                while (index < end && line[index] == ' ')
                    index++;
            }
        }
        else
        {
            var numberStart = index;

            while (index < end && char.IsDigit(line[index]))
                index++;

            if (index > numberStart &&
                index + 1 < end &&
                (line[index] == '.' || line[index] == ')') &&
                char.IsWhiteSpace(line[index + 1]))
            {
                index += 2;
            }
            else
            {
                index = numberStart;
            }
        }

        var consumed = index - start;
        start = index;
        length -= consumed;
    }

    private static void TrimRange(
        string value,
        ref int start,
        ref int length)
    {
        while (length > 0 &&
               char.IsWhiteSpace(value[start]))
        {
            start++;
            length--;
        }

        while (length > 0 &&
               char.IsWhiteSpace(value[start + length - 1]))
        {
            length--;
        }
    }

    private static bool LooksLikeMarkdownTable(string line)
    {
        var pipeCount = 0;
        var escaped = false;

        foreach (var character in line)
        {
            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (character == '\\')
            {
                escaped = true;
                continue;
            }

            if (character == '|')
                pipeCount++;
        }

        return pipeCount >= 2;
    }

    private static void AddMarkdownHtmlBlockSegment(
        ICollection<DocumentSegment> result,
        IReadOnlyList<SourceLine> lines,
        int startLineIndex,
        int endLineIndex,
        string source)
    {
        if (startLineIndex < 0 ||
            endLineIndex < startLineIndex ||
            startLineIndex >= lines.Count)
        {
            return;
        }

        var start = lines[startLineIndex].Start;
        var end = endLineIndex + 1 < lines.Count
            ? lines[endLineIndex + 1].Start
            : source.Length;

        if (end <= start)
            return;

        AddHtmlSegments(
            result,
            source,
            start,
            end,
            lines[startLineIndex].Number);
    }

    private static void AddHtmlSegments(
        ICollection<DocumentSegment> result,
        string source,
        int start,
        int end,
        int lineNumber)
    {
        var skipTagStack = new Stack<string>();
        var index = start;

        while (index < end)
        {
            if (source[index] != '<')
            {
                var textStart = index;
                var textLine = lineNumber;

                while (index < end && source[index] != '<')
                {
                    if (source[index] == '\n')
                        lineNumber++;

                    index++;
                }

                if (skipTagStack.Count == 0)
                {
                    AddTrimmedSegment(
                        result,
                        source,
                        textStart,
                        index - textStart,
                        textLine);
                }

                continue;
            }

            if (source.IndexOf("<!--", index, StringComparison.Ordinal) == index)
            {
                var commentEnd = source.IndexOf(
                    "-->",
                    index + 4,
                    StringComparison.Ordinal);

                if (commentEnd < 0 || commentEnd >= end)
                {
                    lineNumber += CountNewLines(
                        source,
                        index,
                        end - index);

                    break;
                }

                lineNumber += CountNewLines(
                    source,
                    index,
                    commentEnd + 3 - index);

                index = commentEnd + 3;
                continue;
            }

            var tagEnd = FindHtmlTagEnd(source, index);

            if (tagEnd < 0 || tagEnd >= end)
                break;

            var tagLength = tagEnd - index + 1;
            var tagText = source.Substring(index, tagLength);
            var tagMatch = HtmlTagNameRegex.Match(tagText);

            if (tagMatch.Success)
            {
                var tagName = tagMatch.Groups["name"].Value;
                var isClosing = tagMatch.Groups["closing"].Success;
                var isSelfClosing = tagText.TrimEnd().EndsWith(
                    "/>",
                    StringComparison.Ordinal);

                if (IsSkippedHtmlElement(tagName))
                {
                    if (isClosing)
                    {
                        if (skipTagStack.Count > 0 &&
                            string.Equals(
                                skipTagStack.Peek(),
                                tagName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            skipTagStack.Pop();
                        }
                    }
                    else if (!isSelfClosing)
                    {
                        skipTagStack.Push(tagName);
                    }
                }

                if (skipTagStack.Count == 0 && !isClosing)
                {
                    AddHtmlAttributeSegments(
                        result,
                        source,
                        index,
                        tagText,
                        lineNumber);
                }
            }

            lineNumber += CountNewLines(
                source,
                index,
                tagLength);

            index = tagEnd + 1;
        }
    }

    private static bool TryGetMarkdownHtmlBlockStartTag(
        string line,
        out string tagName)
    {
        var match = MarkdownHtmlBlockStartRegex.Match(line);

        if (!match.Success)
        {
            tagName = string.Empty;
            return false;
        }

        tagName = match.Groups["name"].Value;
        return true;
    }

    private static void UpdateMarkdownHtmlBlockState(
        string line,
        Stack<string> tagStack)
    {
        foreach (Match match in MarkdownHtmlTagRegex.Matches(line))
        {
            var name = match.Groups["name"].Value;

            if (!IsMarkdownHtmlBlockTag(name))
                continue;

            var isClosing = match.Groups["closing"].Success;
            var isSelfClosing = match.Groups["selfclosing"].Success ||
                                match.Value.TrimEnd().EndsWith(
                                    "/>",
                                    StringComparison.Ordinal);

            if (isClosing)
            {
                if (tagStack.Count > 0 &&
                    string.Equals(
                        tagStack.Peek(),
                        name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    tagStack.Pop();
                }

                continue;
            }

            if (!isSelfClosing)
                tagStack.Push(name);
        }
    }

    private static bool IsMarkdownHtmlBlockTag(string tagName)
    {
        return tagName.Equals("div", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("section", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("article", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("header", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("footer", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("main", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("aside", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("nav", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("form", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("figure", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("figcaption", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("table", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("thead", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("tbody", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("tfoot", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("tr", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("ul", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("ol", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("blockquote", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("details", StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals("summary", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksTranslatable(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var visible = MarkdownProtectedInlineRegex.Replace(
            value,
            string.Empty);

        return visible.Count(char.IsLetter) >= 2;
    }

    private static int FindHtmlTagEnd(string source, int tagStart)
    {
        char? quote = null;

        for (var index = tagStart + 1;
             index < source.Length;
             index++)
        {
            var character = source[index];

            if (quote is not null)
            {
                if (character == quote)
                    quote = null;

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character == '>')
                return index;
        }

        return -1;
    }

    private static bool IsSkippedHtmlElement(string tagName)
    {
        return tagName.Equals(
                   "script",
                   StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals(
                   "style",
                   StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals(
                   "code",
                   StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals(
                   "pre",
                   StringComparison.OrdinalIgnoreCase) ||
               tagName.Equals(
                   "svg",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static int CountNewLines(
        string value,
        int start,
        int length)
    {
        var count = 0;
        var end = Math.Min(value.Length, start + length);

        for (var index = start; index < end; index++)
        {
            if (value[index] == '\n')
                count++;
        }

        return count;
    }

    private static int CountOccurrences(
        string value,
        string searchValue)
    {
        var count = 0;
        var start = 0;

        while (true)
        {
            var index = value.IndexOf(
                searchValue,
                start,
                StringComparison.Ordinal);

            if (index < 0)
                return count;

            count++;
            start = index + searchValue.Length;
        }
    }

    private static string CreateSegmentKey(int index)
    {
        return $"__rt_segment_{index + 1:D6}";
    }
}
