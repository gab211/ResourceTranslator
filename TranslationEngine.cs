using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ResourceTranslator;

internal sealed record TranslationProgress(
    int Completed,
    int Total,
    string Message);

internal enum TranslationIssueSeverity
{
    Warning,
    Error,
    Critical
}

internal sealed record TranslationIssue(
    TranslationIssueSeverity Severity,
    int PartNumber,
    int? LineNumber,
    string? EntryName,
    string Message,
    string? OriginalText,
    bool OriginalRetained,
    int AffectedCount = 1);

internal sealed record TranslationRunReport(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string SourceFileName,
    string TargetLanguage,
    ApiProvider Provider,
    string Model,
    int TotalParts,
    int RetainedOriginalCount,
    IReadOnlyList<TranslationIssue> Issues,
    string? LogFilePath)
{
    public bool HasSeriousErrors =>
        Issues.Any(issue =>
            issue.Severity is
                TranslationIssueSeverity.Error or
                TranslationIssueSeverity.Critical);
}

internal sealed class TranslationEngine(OpenAiClient client)
{
    private const int MaximumBatchAttempts = 2;
    private const int MaximumSingleItemFallbacksPerChunk = 5;

    private sealed record TranslatableSegment(
        int LineIndex,
        string EntryName,
        string Prefix,
        string SourceText,
        string Suffix,
        char? QuoteCharacter);

    private sealed record ProtectedToken(
        string Marker,
        string Value);

    private sealed record PreparedText(
        string OriginalText,
        string SourceCore,
        string LeadingWhitespace,
        string TrailingWhitespace,
        string PromptText,
        IReadOnlyList<ProtectedToken> ProtectedTokens);

    private sealed record PreparedSegment(
        TranslatableSegment Segment,
        PreparedText Text);

    private sealed record ChunkTranslationResult(
        string Text,
        IReadOnlyList<TranslationIssue> Issues);

    private sealed record SingleTranslationResult(
        bool Success,
        string? Value,
        string? Problem);

    private static readonly Regex ProtectedTokenRegex = new(
        @"\{\{[^{}\r\n]+\}\}" +
        @"|\$\{[^{}\r\n]+\}" +
        @"|\{(?:\d+|[A-Za-z_][A-Za-z0-9_.:-]*)(?::[^{}\r\n]+)?\}" +
        @"|%\d*\$?[-+0-9.#]*[sdif]" +
        @"|&(?:[A-Za-z][A-Za-z0-9]+|#\d+|#x[0-9A-Fa-f]+);" +
        @"|\\(?:[nrt0\\'""bfa]|u[0-9A-Fa-f]{4}|x[0-9A-Fa-f]{2,4})" +
        @"|</?[A-Za-z][^>\r\n]*>" +
        @"|https?://[^\s""'<>]+",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex InternalMarkerRegex = new(
        @"@@RT_PH_\d{4}_\d{3}@@",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex DottedIdentifierRegex = new(
        @"^[A-Za-z_$][A-Za-z0-9_$-]*(?:\.[A-Za-z_$][A-Za-z0-9_$-]*)+$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    private static readonly Regex EmailRegex = new(
        @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant);

    public TranslationRunReport? LastRunReport { get; private set; }

    public async Task<string> TranslateAsync(
        string source,
        string fileName,
        string targetLanguage,
        string customInstruction,
        int chunkSize,
        int contextLines,
        ApiProvider provider,
        string baseUrl,
        string apiKey,
        string model,
        IProgress<TranslationProgress> progress,
        CancellationToken ct,
        string? outputFilePath = null)
    {
        var startedAt = DateTimeOffset.Now;

        var chunks = Chunker.Create(
            source,
            chunkSize,
            contextLines);

        var output = new StringBuilder(
            source.Length + source.Length / 4);

        var issues = new List<TranslationIssue>();

        var currentStartLine = 1;

        for (var index = 0; index < chunks.Count; index++)
        {
            ct.ThrowIfCancellationRequested();

            var chunk = chunks[index];

            progress.Report(
                new TranslationProgress(
                    index,
                    chunks.Count,
                    $"Translating part {index + 1} of {chunks.Count}..."));

            ChunkTranslationResult result;

            try
            {
                result = await TranslateValidatedChunk(
                    chunk,
                    currentStartLine,
                    fileName,
                    targetLanguage,
                    customInstruction,
                    provider,
                    baseUrl,
                    apiKey,
                    model,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                /*
                 * Ultimative Absicherung:
                 *
                 * Auch ein unerwarteter Fehler darf niemals einen bereits
                 * lange laufenden Batch vollständig abbrechen.
                 */
                var issue = new TranslationIssue(
                    TranslationIssueSeverity.Critical,
                    index + 1,
                    currentStartLine,
                    null,
                    $"The complete part was retained because of an " +
                    $"unexpected error: {ex.Message}",
                    ShortenForLog(chunk.Content, 500),
                    true,
                    Math.Max(1, chunk.Lines.Count));

                result = new ChunkTranslationResult(
                    chunk.Content,
                    new[] { issue });
            }

            if (index > 0)
                output.Append('\n');

            output.Append(result.Text);

            foreach (var issue in result.Issues)
            {
                issues.Add(issue);

                progress.Report(
                    new TranslationProgress(
                        index + 1,
                        chunks.Count,
                        FormatIssueForProgress(issue)));
            }

            currentStartLine += chunk.Lines.Count;
        }

        var retainedOriginalCount = issues
            .Where(issue => issue.OriginalRetained)
            .Sum(issue => Math.Max(1, issue.AffectedCount));

        var finishedAt = DateTimeOffset.Now;

        var provisionalReport = new TranslationRunReport(
            startedAt,
            finishedAt,
            fileName,
            targetLanguage,
            provider,
            model,
            chunks.Count,
            retainedOriginalCount,
            issues.ToArray(),
            null);

        var logFilePath = await UpdateSidecarLogAsync(
            provisionalReport,
            outputFilePath,
            progress);

        var finalReport = provisionalReport with
        {
            LogFilePath = logFilePath
        };

        LastRunReport = finalReport;

        if (retainedOriginalCount > 0)
        {
            var logInformation = string.IsNullOrWhiteSpace(logFilePath)
                ? "No log file could be written because no output path was supplied."
                : $"Details: {logFilePath}";

            progress.Report(
                new TranslationProgress(
                    chunks.Count,
                    chunks.Count,
                    $"Translation complete with {retainedOriginalCount} " +
                    $"untranslated or retained item(s). {logInformation}"));
        }
        else
        {
            progress.Report(
                new TranslationProgress(
                    chunks.Count,
                    chunks.Count,
                    "Translation complete without retained original values."));
        }

        return output.ToString();
    }

    private async Task<ChunkTranslationResult> TranslateValidatedChunk(
        TranslationChunk chunk,
        int chunkStartLine,
        string fileName,
        string targetLanguage,
        string customInstruction,
        ApiProvider provider,
        string baseUrl,
        string apiKey,
        string model,
        CancellationToken ct)
    {
        if (IsStructuredResourceFile(fileName))
        {
            var segments = ExtractTranslatableSegments(
                chunk.Lines,
                fileName);

            if (segments.Count == 0)
            {
                return new ChunkTranslationResult(
                    chunk.Content,
                    Array.Empty<TranslationIssue>());
            }

            return await TranslateStructuredChunk(
                chunk,
                chunkStartLine,
                segments,
                fileName,
                targetLanguage,
                customInstruction,
                provider,
                baseUrl,
                apiKey,
                model,
                ct);
        }

        return await TranslateWholeLineChunk(
            chunk,
            chunkStartLine,
            fileName,
            targetLanguage,
            customInstruction,
            provider,
            baseUrl,
            apiKey,
            model,
            ct);
    }

    private async Task<ChunkTranslationResult> TranslateStructuredChunk(
        TranslationChunk chunk,
        int chunkStartLine,
        IReadOnlyList<TranslatableSegment> segments,
        string fileName,
        string targetLanguage,
        string customInstruction,
        ApiProvider provider,
        string baseUrl,
        string apiKey,
        string model,
        CancellationToken ct)
    {
        var preparedSegments = segments
            .Select(
                (segment, index) =>
                    new PreparedSegment(
                        segment,
                        PrepareText(
                            segment.SourceText,
                            index)))
            .ToArray();

        /*
         * Jeder Wert beginnt mit dem sicheren Original.
         *
         * Nur erfolgreich geprüfte Übersetzungen ersetzen diesen Wert.
         */
        var translatedValues = preparedSegments
            .Select(prepared => prepared.Segment.SourceText)
            .ToArray();

        var resolved = new bool[preparedSegments.Length];
        var failureReasons = new string?[preparedSegments.Length];

        string? lastBatchProblem = null;

        for (var attempt = 1;
             attempt <= MaximumBatchAttempts;
             attempt++)
        {
            ct.ThrowIfCancellationRequested();

            string raw;

            try
            {
                var prompt = BuildStructuredPrompt(
                    chunk,
                    preparedSegments,
                    fileName,
                    targetLanguage,
                    customInstruction,
                    lastBatchProblem);

                raw = await client.TranslateAsync(
                    provider,
                    baseUrl,
                    apiKey,
                    model,
                    prompt,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastBatchProblem =
                    $"Batch attempt {attempt} failed: {ex.Message}";

                continue;
            }

            var candidates = ExtractTaggedValuesPartial(
                raw,
                preparedSegments.Length,
                CreateItemTag);

            var newlyResolved = 0;

            for (var index = 0;
                 index < preparedSegments.Length;
                 index++)
            {
                if (resolved[index])
                    continue;

                var candidate = candidates[index];

                if (candidate is null)
                {
                    failureReasons[index] =
                        $"The model omitted marker {CreateItemTag(index)}.";

                    continue;
                }

                var prepared = preparedSegments[index];

                if (!TryRestoreProtectedCore(
                        prepared.Text,
                        candidate,
                        out var translatedCore,
                        out var restoreProblem))
                {
                    failureReasons[index] = restoreProblem;
                    continue;
                }

                translatedCore = NormalizeForQuoteContainer(
                    translatedCore,
                    prepared.Segment.QuoteCharacter);

                if (string.IsNullOrWhiteSpace(translatedCore) &&
                    !string.IsNullOrWhiteSpace(
                        prepared.Text.SourceCore))
                {
                    failureReasons[index] =
                        "The translated value was empty.";

                    continue;
                }

                if (string.Equals(
                        translatedCore,
                        prepared.Text.SourceCore,
                        StringComparison.Ordinal) &&
                    !MayRemainUnchanged(
                        prepared.Text.SourceCore))
                {
                    failureReasons[index] =
                        "The value was returned unchanged.";

                    continue;
                }

                translatedValues[index] =
                    prepared.Text.LeadingWhitespace +
                    translatedCore +
                    prepared.Text.TrailingWhitespace;

                resolved[index] = true;
                failureReasons[index] = null;
                newlyResolved++;
            }

            if (resolved.All(value => value))
                break;

            if (newlyResolved == 0)
            {
                lastBatchProblem =
                    $"Batch attempt {attempt} did not resolve any " +
                    $"additional values.";
            }
        }

        var unresolvedIndexes = Enumerable
            .Range(0, preparedSegments.Length)
            .Where(index => !resolved[index])
            .ToArray();

        /*
         * Nur eine begrenzte Anzahl einzelner Fallback-Requests.
         *
         * So kann ein schlechtes lokales Modell den Batch nicht durch
         * hunderte Einzelanfragen stundenlang blockieren.
         */
        foreach (var index in unresolvedIndexes
                     .Take(MaximumSingleItemFallbacksPerChunk))
        {
            ct.ThrowIfCancellationRequested();

            var singleResult =
                await TryTranslateSingleStructuredItem(
                    chunk,
                    preparedSegments[index],
                    fileName,
                    targetLanguage,
                    customInstruction,
                    provider,
                    baseUrl,
                    apiKey,
                    model,
                    ct);

            if (!singleResult.Success ||
                singleResult.Value is null)
            {
                failureReasons[index] =
                    singleResult.Problem ??
                    failureReasons[index] ??
                    "The single-value fallback failed.";

                continue;
            }

            translatedValues[index] =
                singleResult.Value;

            resolved[index] = true;
            failureReasons[index] = null;
        }

        var issues = new List<TranslationIssue>();

        for (var index = 0;
             index < preparedSegments.Length;
             index++)
        {
            if (resolved[index])
                continue;

            var prepared = preparedSegments[index];

            var absoluteLineNumber =
                chunkStartLine +
                prepared.Segment.LineIndex;

            var fallbackWasSkipped =
                unresolvedIndexes
                    .Take(MaximumSingleItemFallbacksPerChunk)
                    .All(itemIndex => itemIndex != index);

            var reason = failureReasons[index];

            if (fallbackWasSkipped)
            {
                reason =
                    $"The individual fallback was skipped because the " +
                    $"limit of {MaximumSingleItemFallbacksPerChunk} " +
                    $"fallback requests per part was reached. " +
                    $"{reason}";
            }

            if (string.IsNullOrWhiteSpace(reason))
            {
                reason =
                    lastBatchProblem ??
                    "The value could not be translated safely.";
            }

            issues.Add(
                new TranslationIssue(
                    TranslationIssueSeverity.Error,
                    chunk.Index + 1,
                    absoluteLineNumber,
                    prepared.Segment.EntryName,
                    reason,
                    prepared.Segment.SourceText,
                    true));
        }

        var reconstructed = ReconstructStructuredChunk(
            chunk.Lines,
            preparedSegments,
            translatedValues);

        return new ChunkTranslationResult(
            reconstructed,
            issues);
    }

    private async Task<SingleTranslationResult>
        TryTranslateSingleStructuredItem(
            TranslationChunk chunk,
            PreparedSegment preparedSegment,
            string fileName,
            string targetLanguage,
            string customInstruction,
            ApiProvider provider,
            string baseUrl,
            string apiKey,
            string model,
            CancellationToken ct)
    {
        try
        {
            var prompt = BuildStructuredPrompt(
                chunk,
                new[] { preparedSegment },
                fileName,
                targetLanguage,
                customInstruction,
                "Translate this single value. Preserve every protected " +
                "marker exactly.");

            var raw = await client.TranslateAsync(
                provider,
                baseUrl,
                apiKey,
                model,
                prompt,
                ct);

            var candidates = ExtractTaggedValuesPartial(
                raw,
                1,
                CreateItemTag);

            var candidate = candidates[0];

            if (candidate is null)
            {
                return new SingleTranslationResult(
                    false,
                    null,
                    $"The model omitted marker {CreateItemTag(0)} " +
                    $"during the individual fallback.");
            }

            if (!TryRestoreProtectedCore(
                    preparedSegment.Text,
                    candidate,
                    out var translatedCore,
                    out var restoreProblem))
            {
                return new SingleTranslationResult(
                    false,
                    null,
                    restoreProblem);
            }

            translatedCore = NormalizeForQuoteContainer(
                translatedCore,
                preparedSegment.Segment.QuoteCharacter);

            if (string.IsNullOrWhiteSpace(translatedCore) &&
                !string.IsNullOrWhiteSpace(
                    preparedSegment.Text.SourceCore))
            {
                return new SingleTranslationResult(
                    false,
                    null,
                    "The individual fallback returned an empty value.");
            }

            if (string.Equals(
                    translatedCore,
                    preparedSegment.Text.SourceCore,
                    StringComparison.Ordinal) &&
                !MayRemainUnchanged(
                    preparedSegment.Text.SourceCore))
            {
                return new SingleTranslationResult(
                    false,
                    null,
                    "The individual fallback returned the original " +
                    "value unchanged.");
            }

            var value =
                preparedSegment.Text.LeadingWhitespace +
                translatedCore +
                preparedSegment.Text.TrailingWhitespace;

            return new SingleTranslationResult(
                true,
                value,
                null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SingleTranslationResult(
                false,
                null,
                $"The individual fallback failed: {ex.Message}");
        }
    }

    private async Task<ChunkTranslationResult> TranslateWholeLineChunk(
        TranslationChunk chunk,
        int chunkStartLine,
        string fileName,
        string targetLanguage,
        string customInstruction,
        ApiProvider provider,
        string baseUrl,
        string apiKey,
        string model,
        CancellationToken ct)
    {
        var preparedLines = chunk.Lines
            .Select(
                (line, index) =>
                    PrepareText(line, index))
            .ToArray();

        var translatedLines = chunk.Lines.ToArray();
        var resolved = new bool[preparedLines.Length];
        var failureReasons = new string?[preparedLines.Length];

        for (var index = 0;
             index < preparedLines.Length;
             index++)
        {
            if (preparedLines[index].SourceCore.Length == 0)
                resolved[index] = true;
        }

        string? lastBatchProblem = null;

        for (var attempt = 1;
             attempt <= MaximumBatchAttempts;
             attempt++)
        {
            ct.ThrowIfCancellationRequested();

            string raw;

            try
            {
                var prompt = BuildWholeLinePrompt(
                    chunk,
                    preparedLines,
                    fileName,
                    targetLanguage,
                    customInstruction,
                    lastBatchProblem);

                raw = await client.TranslateAsync(
                    provider,
                    baseUrl,
                    apiKey,
                    model,
                    prompt,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastBatchProblem =
                    $"Batch attempt {attempt} failed: {ex.Message}";

                continue;
            }

            var candidates = ExtractTaggedValuesPartial(
                raw,
                preparedLines.Length,
                CreateLineTag);

            var newlyResolved = 0;

            for (var index = 0;
                 index < preparedLines.Length;
                 index++)
            {
                if (resolved[index])
                    continue;

                var candidate = candidates[index];

                if (candidate is null)
                {
                    failureReasons[index] =
                        $"The model omitted marker {CreateLineTag(index)}.";

                    continue;
                }

                var prepared = preparedLines[index];

                if (!TryRestoreProtectedCore(
                        prepared,
                        candidate,
                        out var translatedCore,
                        out var restoreProblem))
                {
                    failureReasons[index] = restoreProblem;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(translatedCore) &&
                    !string.IsNullOrWhiteSpace(
                        prepared.SourceCore))
                {
                    failureReasons[index] =
                        "The translated line was empty.";

                    continue;
                }

                if (LooksLikeNaturalLanguage(
                        prepared.SourceCore) &&
                    !MayRemainUnchanged(
                        prepared.SourceCore) &&
                    string.Equals(
                        translatedCore,
                        prepared.SourceCore,
                        StringComparison.Ordinal))
                {
                    failureReasons[index] =
                        "The line was returned unchanged.";

                    continue;
                }

                translatedLines[index] =
                    prepared.LeadingWhitespace +
                    translatedCore +
                    prepared.TrailingWhitespace;

                resolved[index] = true;
                failureReasons[index] = null;
                newlyResolved++;
            }

            if (resolved.All(value => value))
                break;

            if (newlyResolved == 0)
            {
                lastBatchProblem =
                    $"Batch attempt {attempt} did not resolve any " +
                    $"additional lines.";
            }
        }

        var issues = new List<TranslationIssue>();

        for (var index = 0;
             index < preparedLines.Length;
             index++)
        {
            if (resolved[index])
                continue;

            issues.Add(
                new TranslationIssue(
                    TranslationIssueSeverity.Error,
                    chunk.Index + 1,
                    chunkStartLine + index,
                    null,
                    failureReasons[index] ??
                    lastBatchProblem ??
                    "The line could not be translated safely.",
                    chunk.Lines[index],
                    true));
        }

        return new ChunkTranslationResult(
            string.Join("\n", translatedLines),
            issues);
    }

    private static bool IsStructuredResourceFile(
        string fileName)
    {
        var extension = Path
            .GetExtension(fileName)
            .ToLowerInvariant();

        return extension is
            ".js" or
            ".json" or
            ".ts" or
            ".resx" or
            ".xml" or
            ".properties" or
            ".ini" or
            ".lang" or
            ".cfg";
    }

    private static List<TranslatableSegment>
        ExtractTranslatableSegments(
            IReadOnlyList<string> lines,
            string fileName)
    {
        var result =
            new List<TranslatableSegment>();

        var extension = Path
            .GetExtension(fileName)
            .ToLowerInvariant();

        var isXml =
            extension is ".xml" or ".resx";

        var allowsUnquotedValues =
            extension is
                ".properties" or
                ".ini" or
                ".lang" or
                ".cfg";

        for (var lineIndex = 0;
             lineIndex < lines.Count;
             lineIndex++)
        {
            var line = lines[lineIndex];

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (IsCommentLine(line))
                continue;

            TranslatableSegment? segment;

            if (isXml)
            {
                segment = TryParseXmlValue(
                    line,
                    lineIndex);

                if (segment is not null)
                    result.Add(segment);

                continue;
            }

            segment = TryParseQuotedAssignment(
                line,
                lineIndex);

            if (segment is not null)
            {
                result.Add(segment);
                continue;
            }

            if (!allowsUnquotedValues)
                continue;

            segment = TryParseUnquotedKeyValue(
                line,
                lineIndex);

            if (segment is not null)
                result.Add(segment);
        }

        return result;
    }

    private static bool IsCommentLine(
        string line)
    {
        var trimmed = line.TrimStart();

        return trimmed.StartsWith(
                   "//",
                   StringComparison.Ordinal) ||
               trimmed.StartsWith(
                   "/*",
                   StringComparison.Ordinal) ||
               trimmed.StartsWith(
                   "*",
                   StringComparison.Ordinal) ||
               trimmed.StartsWith(
                   "#",
                   StringComparison.Ordinal) ||
               trimmed.StartsWith(
                   ";",
                   StringComparison.Ordinal);
    }

    private static TranslatableSegment?
        TryParseQuotedAssignment(
            string line,
            int lineIndex)
    {
        var separatorIndex =
            FindAssignmentSeparatorOutsideQuotes(line);

        if (separatorIndex < 0)
            return null;

        var valueStart = separatorIndex + 1;

        while (valueStart < line.Length &&
               char.IsWhiteSpace(line[valueStart]))
        {
            valueStart++;
        }

        if (valueStart >= line.Length)
            return null;

        var quoteCharacter = line[valueStart];

        if (quoteCharacter != '"' &&
            quoteCharacter != '\'' &&
            quoteCharacter != '`')
        {
            return null;
        }

        var valueEnd = FindClosingQuote(
            line,
            valueStart,
            quoteCharacter);

        if (valueEnd < 0)
            return null;

        var sourceText = line[
            (valueStart + 1)..valueEnd];

        if (!ShouldTranslateValue(sourceText))
            return null;

        var entryName = ExtractEntryName(
            line[..separatorIndex]);

        return new TranslatableSegment(
            lineIndex,
            entryName,
            line[..(valueStart + 1)],
            sourceText,
            line[valueEnd..],
            quoteCharacter);
    }

    private static string ExtractEntryName(
        string propertyPart)
    {
        var value = propertyPart.Trim();

        if (value.Length >= 2)
        {
            var first = value[0];
            var last = value[^1];

            if ((first == '"' && last == '"') ||
                (first == '\'' && last == '\'') ||
                (first == '`' && last == '`'))
            {
                value = value[1..^1];
            }
        }

        return string.IsNullOrWhiteSpace(value)
            ? "(unknown entry)"
            : value;
    }

    private static int
        FindAssignmentSeparatorOutsideQuotes(
            string line)
    {
        char? activeQuote = null;
        var escaped = false;

        for (var index = 0;
             index < line.Length;
             index++)
        {
            var character = line[index];

            if (activeQuote is not null)
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

                if (character == activeQuote)
                    activeQuote = null;

                continue;
            }

            if (character == '"' ||
                character == '\'' ||
                character == '`')
            {
                activeQuote = character;
                continue;
            }

            if (character == ':' ||
                character == '=')
            {
                return index;
            }
        }

        return -1;
    }

    private static int FindClosingQuote(
        string line,
        int openingQuoteIndex,
        char quoteCharacter)
    {
        var escaped = false;

        for (var index = openingQuoteIndex + 1;
             index < line.Length;
             index++)
        {
            var character = line[index];

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

            if (character == quoteCharacter)
                return index;
        }

        return -1;
    }

    private static TranslatableSegment?
        TryParseXmlValue(
            string line,
            int lineIndex)
    {
        var valueTagStart = line.IndexOf(
            "<value",
            StringComparison.OrdinalIgnoreCase);

        if (valueTagStart < 0)
            return null;

        var contentStart = line.IndexOf(
            '>',
            valueTagStart);

        if (contentStart < 0)
            return null;

        contentStart++;

        var contentEnd = line.IndexOf(
            "</value>",
            contentStart,
            StringComparison.OrdinalIgnoreCase);

        if (contentEnd < 0)
            return null;

        var sourceText = line[
            contentStart..contentEnd];

        if (!ShouldTranslateValue(sourceText))
            return null;

        return new TranslatableSegment(
            lineIndex,
            $"XML value at line {lineIndex + 1}",
            line[..contentStart],
            sourceText,
            line[contentEnd..],
            null);
    }

    private static TranslatableSegment?
        TryParseUnquotedKeyValue(
            string line,
            int lineIndex)
    {
        var equalsIndex = line.IndexOf('=');
        var colonIndex = line.IndexOf(':');

        var separatorIndex = -1;

        if (equalsIndex >= 0 &&
            colonIndex >= 0)
        {
            separatorIndex = Math.Min(
                equalsIndex,
                colonIndex);
        }
        else if (equalsIndex >= 0)
        {
            separatorIndex = equalsIndex;
        }
        else if (colonIndex >= 0)
        {
            separatorIndex = colonIndex;
        }

        if (separatorIndex <= 0)
            return null;

        var valueStart = separatorIndex + 1;

        while (valueStart < line.Length &&
               char.IsWhiteSpace(line[valueStart]))
        {
            valueStart++;
        }

        if (valueStart >= line.Length)
            return null;

        var valueEnd = line.Length;

        while (valueEnd > valueStart &&
               char.IsWhiteSpace(line[valueEnd - 1]))
        {
            valueEnd--;
        }

        var sourceText = line[
            valueStart..valueEnd];

        if (!ShouldTranslateValue(sourceText))
            return null;

        return new TranslatableSegment(
            lineIndex,
            ExtractEntryName(line[..separatorIndex]),
            line[..valueStart],
            sourceText,
            line[valueEnd..],
            null);
    }

    private static bool ShouldTranslateValue(
        string value)
    {
        if (!LooksLikeNaturalLanguage(value))
            return false;

        if (MayRemainUnchanged(value))
            return false;

        if (IsClearlyTechnicalValue(value))
            return false;

        return true;
    }

    private static bool LooksLikeNaturalLanguage(
        string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();

        if (trimmed.StartsWith(
                "http://",
                StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith(
                "https://",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return trimmed.Count(char.IsLetter) >= 2;
    }

    private static bool IsClearlyTechnicalValue(
        string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0)
            return true;

        if (trimmed.Equals(
                "true",
                StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(
                "false",
                StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(
                "null",
                StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals(
                "undefined",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (decimal.TryParse(
                trimmed,
                out _))
        {
            return true;
        }

        if (trimmed.StartsWith(
                "./",
                StringComparison.Ordinal) ||
            trimmed.StartsWith(
                "../",
                StringComparison.Ordinal) ||
            trimmed.StartsWith(
                "/",
                StringComparison.Ordinal))
        {
            return true;
        }

        if (EmailRegex.IsMatch(trimmed))
            return true;

        if (DottedIdentifierRegex.IsMatch(trimmed))
            return true;

        if (!trimmed.Any(char.IsWhiteSpace) &&
            (trimmed.Contains('/') ||
             trimmed.Contains('\\')))
        {
            return true;
        }

        return false;
    }

    private static bool MayRemainUnchanged(
        string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length == 0)
            return true;

        if (trimmed.Length <= 12 &&
            trimmed.Any(char.IsLetter) &&
            trimmed
                .Where(char.IsLetter)
                .All(char.IsUpper))
        {
            return true;
        }

        return trimmed.Equals(
                   "CodeRoom",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "FAQ",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Demo",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Test",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Code",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Index",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Menu",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "System",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Admin",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "User",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Scratch",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "HTML",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "CSS",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "JavaScript",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "TypeScript",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Python",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Java",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "Ruby",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "JSON",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "XML",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "API",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "URL",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "ID",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "OpenAI",
                   StringComparison.OrdinalIgnoreCase) ||
               trimmed.Equals(
                   "LM Studio",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static PreparedText PrepareText(
        string text,
        int ownerIndex)
    {
        var leadingWhitespace =
            GetLeadingWhitespace(text);

        var trailingWhitespace =
            GetTrailingWhitespace(text);

        var coreLength =
            text.Length -
            leadingWhitespace.Length -
            trailingWhitespace.Length;

        if (coreLength < 0)
            coreLength = 0;

        var sourceCore =
            coreLength == 0
                ? string.Empty
                : text.Substring(
                    leadingWhitespace.Length,
                    coreLength);

        var protectedTokens =
            new List<ProtectedToken>();

        var promptText = ProtectedTokenRegex.Replace(
            sourceCore,
            match =>
            {
                var marker =
                    $"@@RT_PH_{ownerIndex + 1:D4}_" +
                    $"{protectedTokens.Count + 1:D3}@@";

                protectedTokens.Add(
                    new ProtectedToken(
                        marker,
                        match.Value));

                return marker;
            });

        return new PreparedText(
            text,
            sourceCore,
            leadingWhitespace,
            trailingWhitespace,
            promptText,
            protectedTokens);
    }

    private static bool TryRestoreProtectedCore(
        PreparedText prepared,
        string translatedMaskedText,
        out string restoredCore,
        out string problem)
    {
        var candidate =
            translatedMaskedText.Trim();

        foreach (var token in prepared.ProtectedTokens)
        {
            var occurrenceCount =
                CountOccurrences(
                    candidate,
                    token.Marker);

            if (occurrenceCount == 0)
            {
                restoredCore = string.Empty;
                problem =
                    $"Protected marker {token.Marker} was omitted.";

                return false;
            }

            if (occurrenceCount > 1)
            {
                restoredCore = string.Empty;
                problem =
                    $"Protected marker {token.Marker} was duplicated.";

                return false;
            }
        }

        foreach (var token in prepared.ProtectedTokens)
        {
            candidate = candidate.Replace(
                token.Marker,
                token.Value,
                StringComparison.Ordinal);
        }

        if (InternalMarkerRegex.IsMatch(candidate) ||
            candidate.Contains(
                "@@RT_PH_",
                StringComparison.Ordinal))
        {
            restoredCore = string.Empty;
            problem =
                "The output contains an unknown or malformed " +
                "protected marker.";

            return false;
        }

        restoredCore = candidate;
        problem = string.Empty;

        return true;
    }

    private static string NormalizeForQuoteContainer(
        string value,
        char? quoteCharacter)
    {
        if (quoteCharacter is null)
            return value;

        var result = value;

        if (quoteCharacter == '"')
        {
            result = RemoveRedundantQuoteEscape(
                result,
                '\'');
        }
        else if (quoteCharacter == '\'')
        {
            result = RemoveRedundantQuoteEscape(
                result,
                '"');
        }
        else if (quoteCharacter == '`')
        {
            result = RemoveRedundantQuoteEscape(
                result,
                '\'');

            result = RemoveRedundantQuoteEscape(
                result,
                '"');
        }

        return EscapeUnescapedCharacter(
            result,
            quoteCharacter.Value);
    }

    private static string RemoveRedundantQuoteEscape(
        string value,
        char quoteCharacter)
    {
        var result =
            new StringBuilder(value.Length);

        var index = 0;

        while (index < value.Length)
        {
            if (value[index] != '\\')
            {
                result.Append(value[index]);
                index++;
                continue;
            }

            var slashStart = index;

            while (index < value.Length &&
                   value[index] == '\\')
            {
                index++;
            }

            var slashCount =
                index - slashStart;

            if (index < value.Length &&
                value[index] == quoteCharacter)
            {
                if (slashCount % 2 == 1)
                    slashCount--;

                result.Append(
                    '\\',
                    slashCount);

                result.Append(quoteCharacter);
                index++;

                continue;
            }

            result.Append(
                '\\',
                slashCount);
        }

        return result.ToString();
    }

    private static string EscapeUnescapedCharacter(
        string value,
        char character)
    {
        var result =
            new StringBuilder(value.Length + 8);

        for (var index = 0;
             index < value.Length;
             index++)
        {
            var current = value[index];

            if (current != character)
            {
                result.Append(current);
                continue;
            }

            var precedingBackslashes = 0;
            var previousIndex = index - 1;

            while (previousIndex >= 0 &&
                   value[previousIndex] == '\\')
            {
                precedingBackslashes++;
                previousIndex--;
            }

            if (precedingBackslashes % 2 == 0)
                result.Append('\\');

            result.Append(current);
        }

        return result.ToString();
    }

    private static string BuildStructuredPrompt(
        TranslationChunk chunk,
        IReadOnlyList<PreparedSegment> segments,
        string fileName,
        string targetLanguage,
        string customInstruction,
        string? retryProblem)
    {
        var contextBefore = string.Join(
            "\n",
            chunk.ContextBefore);

        var contextAfter = string.Join(
            "\n",
            chunk.ContextAfter);

        var taggedValues = string.Join(
            "\n",
            segments.Select(
                (segment, index) =>
                    $"{CreateItemTag(index)}" +
                    segment.Text.PromptText));

        return $$"""
Translate every resource value below into {{targetLanguage}}.

The source file is {{fileName}}.

You receive only human-readable inner values.
Keys, comments, indentation, quotes, commas, semicolons, and file syntax are managed by the application.

STRICT OUTPUT FORMAT:
- Return exactly one line for every input item.
- Every output line must begin with its original @@ITEM_XXXX@@ marker.
- Preserve every item marker exactly.
- Return only the item marker followed by the translated value.
- Do not return keys, quotes, commas, semicolons, explanations, headings, notes, or markdown fences.
- Never split one item into multiple lines.
- Never combine multiple items.
- Never omit an item.

PROTECTED MARKERS:
- Values may contain markers such as @@RT_PH_0001_001@@.
- Preserve each protected marker exactly once.
- Do not translate, rename, split, remove, duplicate, or reformat protected markers.
- Protected markers may move within the translated sentence when required by the target language.

TRANSLATION RULES:
- Translate every natural-language value completely.
- Use natural {{targetLanguage}} wording.
- Do not manually add JavaScript escaping.
- Do not add backslashes before apostrophes or quotation marks.
- Preserve product names and technical terms where appropriate.
- Do not summarize, improve, shorten, expand, or explain the text.
{{(string.IsNullOrWhiteSpace(customInstruction)
        ? ""
        : "Additional instruction: " + customInstruction)}}
{{(retryProblem is null
        ? ""
        : "The previous output was rejected because: " + retryProblem)}}

READ-ONLY CONTEXT BEFORE:
<<<CONTEXT_BEFORE
{{contextBefore}}
CONTEXT_BEFORE

VALUES TO TRANSLATE:
<<<VALUES
{{taggedValues}}
VALUES

READ-ONLY CONTEXT AFTER:
<<<CONTEXT_AFTER
{{contextAfter}}
CONTEXT_AFTER
""";
    }

    private static string BuildWholeLinePrompt(
        TranslationChunk chunk,
        IReadOnlyList<PreparedText> lines,
        string fileName,
        string targetLanguage,
        string customInstruction,
        string? retryProblem)
    {
        var contextBefore = string.Join(
            "\n",
            chunk.ContextBefore);

        var contextAfter = string.Join(
            "\n",
            chunk.ContextAfter);

        var taggedContent = string.Join(
            "\n",
            lines.Select(
                (line, index) =>
                    $"{CreateLineTag(index)}" +
                    line.PromptText));

        return $$"""
Translate the human-readable text below into {{targetLanguage}}.

The source file is {{fileName}}.

STRICT OUTPUT FORMAT:
- Return exactly one output line for every input line.
- Every output line must begin with its original @@LINE_XXXX@@ marker.
- Preserve all line markers exactly.
- Do not add explanations, headings, notes, or markdown fences.
- Never split one marked line into multiple lines.
- Never combine multiple marked lines.
- Empty lines must still be returned with their marker.

PROTECTED MARKERS:
- Text may contain markers such as @@RT_PH_0001_001@@.
- Preserve every protected marker exactly once.
- Do not translate, rename, remove, duplicate, or reformat protected markers.

TRANSLATION RULES:
- Translate all human-readable text.
- Use natural {{targetLanguage}} wording.
- Do not manually add escaping.
- Do not summarize, improve, shorten, expand, reorder, or explain.
{{(string.IsNullOrWhiteSpace(customInstruction)
        ? ""
        : "Additional instruction: " + customInstruction)}}
{{(retryProblem is null
        ? ""
        : "The previous output was rejected because: " + retryProblem)}}

READ-ONLY CONTEXT BEFORE:
<<<CONTEXT_BEFORE
{{contextBefore}}
CONTEXT_BEFORE

CONTENT:
<<<CONTENT
{{taggedContent}}
CONTENT

READ-ONLY CONTEXT AFTER:
<<<CONTEXT_AFTER
{{contextAfter}}
CONTEXT_AFTER
""";
    }

    private static string ReconstructStructuredChunk(
        IReadOnlyList<string> sourceLines,
        IReadOnlyList<PreparedSegment> segments,
        IReadOnlyList<string> translatedValues)
    {
        var resultLines =
            sourceLines.ToArray();

        for (var index = 0;
             index < segments.Count;
             index++)
        {
            var segment =
                segments[index].Segment;

            resultLines[segment.LineIndex] =
                segment.Prefix +
                translatedValues[index] +
                segment.Suffix;
        }

        return string.Join(
            "\n",
            resultLines);
    }

    private static string?[] ExtractTaggedValuesPartial(
        string response,
        int expectedCount,
        Func<int, string> tagFactory)
    {
        var normalized = response
            .Replace("\r\n", "\n")
            .Replace("\r", "\n");

        var extractedValues =
            new string?[expectedCount];

        foreach (var responseLine in normalized.Split('\n'))
        {
            var candidate =
                responseLine.TrimStart();

            for (var index = 0;
                 index < expectedCount;
                 index++)
            {
                if (extractedValues[index] is not null)
                    continue;

                var tag = tagFactory(index);

                if (!candidate.StartsWith(
                        tag,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                extractedValues[index] =
                    candidate[tag.Length..];

                break;
            }
        }

        return extractedValues;
    }

    private static async Task<string?> UpdateSidecarLogAsync(
        TranslationRunReport report,
        string? outputFilePath,
        IProgress<TranslationProgress> progress)
    {
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            if (report.HasSeriousErrors)
            {
                progress.Report(
                    new TranslationProgress(
                        report.TotalParts,
                        report.TotalParts,
                        "ERROR: A diagnostic log is required, but no " +
                        "output file path was supplied to TranslationEngine."));
            }

            return null;
        }

        var logFilePath =
            outputFilePath + ".log.txt";

        try
        {
            if (!report.HasSeriousErrors)
            {
                /*
                 * Ein Fehlerlog eines älteren Laufs darf nach einem
                 * erfolgreichen Lauf nicht fälschlich liegen bleiben.
                 */
                if (File.Exists(logFilePath))
                    File.Delete(logFilePath);

                return null;
            }

            var directory =
                Path.GetDirectoryName(logFilePath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var logText = BuildLogText(
                report,
                outputFilePath);

            await File.WriteAllTextAsync(
                logFilePath,
                logText,
                new UTF8Encoding(false),
                CancellationToken.None);

            progress.Report(
                new TranslationProgress(
                    report.TotalParts,
                    report.TotalParts,
                    $"ERROR LOG WRITTEN: {logFilePath}"));

            return logFilePath;
        }
        catch (Exception ex)
        {
            progress.Report(
                new TranslationProgress(
                    report.TotalParts,
                    report.TotalParts,
                    $"ERROR: The diagnostic log could not be written: " +
                    ex.Message));

            return null;
        }
    }

    private static string BuildLogText(
        TranslationRunReport report,
        string outputFilePath)
    {
        var builder =
            new StringBuilder();

        builder.AppendLine(
            "Resource Translator – Translation Diagnostic Log");

        builder.AppendLine(
            "================================================");

        builder.AppendLine(
            $"Started:          {report.StartedAt:yyyy-MM-dd HH:mm:ss zzz}");

        builder.AppendLine(
            $"Finished:         {report.FinishedAt:yyyy-MM-dd HH:mm:ss zzz}");

        builder.AppendLine(
            $"Source file:      {report.SourceFileName}");

        builder.AppendLine(
            $"Output file:      {outputFilePath}");

        builder.AppendLine(
            $"Target language:  {report.TargetLanguage}");

        builder.AppendLine(
            $"Provider:         {report.Provider}");

        builder.AppendLine(
            $"Model:            {report.Model}");

        builder.AppendLine(
            $"Parts:            {report.TotalParts}");

        builder.AppendLine(
            $"Retained values:  {report.RetainedOriginalCount}");

        builder.AppendLine();

        builder.AppendLine(
            "IMPORTANT:");

        builder.AppendLine(
            "The entries listed below could not be translated safely.");

        builder.AppendLine(
            "Their original source text was retained so that the output");

        builder.AppendLine(
            "file remains structurally valid and the batch can continue.");

        builder.AppendLine();

        for (var index = 0;
             index < report.Issues.Count;
             index++)
        {
            var issue =
                report.Issues[index];

            builder.AppendLine(
                $"[{index + 1}] {issue.Severity.ToString().ToUpperInvariant()}");

            builder.AppendLine(
                $"Part:          {issue.PartNumber}");

            if (issue.LineNumber.HasValue)
            {
                builder.AppendLine(
                    $"Line:          {issue.LineNumber.Value}");
            }

            if (!string.IsNullOrWhiteSpace(
                    issue.EntryName))
            {
                builder.AppendLine(
                    $"Entry:         {issue.EntryName}");
            }

            builder.AppendLine(
                $"Reason:        {issue.Message}");

            builder.AppendLine(
                $"Original kept: {(issue.OriginalRetained ? "yes" : "no")}");

            builder.AppendLine(
                $"Affected:      {issue.AffectedCount}");

            if (!string.IsNullOrWhiteSpace(
                    issue.OriginalText))
            {
                builder.AppendLine(
                    "Original text:");

                builder.AppendLine(
                    issue.OriginalText);
            }

            builder.AppendLine(
                new string('-', 72));
        }

        return builder.ToString();
    }

    private static string FormatIssueForProgress(
        TranslationIssue issue)
    {
        var prefix =
            issue.Severity.ToString().ToUpperInvariant();

        var location =
            $"Part {issue.PartNumber}";

        if (issue.LineNumber.HasValue)
            location += $", line {issue.LineNumber.Value}";

        if (!string.IsNullOrWhiteSpace(
                issue.EntryName))
        {
            location += $", entry \"{issue.EntryName}\"";
        }

        return $"{prefix}: {location}: {issue.Message} " +
               "The original text was retained.";
    }

    private static int CountOccurrences(
        string value,
        string searchValue)
    {
        if (searchValue.Length == 0)
            return 0;

        var count = 0;
        var startIndex = 0;

        while (true)
        {
            var foundIndex = value.IndexOf(
                searchValue,
                startIndex,
                StringComparison.Ordinal);

            if (foundIndex < 0)
                return count;

            count++;

            startIndex =
                foundIndex + searchValue.Length;
        }
    }

    private static string GetLeadingWhitespace(
        string value)
    {
        var index = 0;

        while (index < value.Length &&
               char.IsWhiteSpace(value[index]))
        {
            index++;
        }

        return value[..index];
    }

    private static string GetTrailingWhitespace(
        string value)
    {
        var index = value.Length;

        while (index > 0 &&
               char.IsWhiteSpace(value[index - 1]))
        {
            index--;
        }

        return value[index..];
    }

    private static string ShortenForLog(
        string value,
        int maximumLength)
    {
        if (value.Length <= maximumLength)
            return value;

        return value[..maximumLength] +
               Environment.NewLine +
               "... [shortened]";
    }

    private static string CreateLineTag(
        int index)
    {
        return $"@@LINE_{index + 1:D4}@@";
    }

    private static string CreateItemTag(
        int index)
    {
        return $"@@ITEM_{index + 1:D4}@@";
    }
}