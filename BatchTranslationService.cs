using System.Text;

namespace ResourceTranslator;

internal sealed record BatchTranslationOptions(
    string SourceFolder,
    string TargetFolder,
    IReadOnlyCollection<string> Extensions,
    bool IncludeSubfolders,
    bool CopyOtherFiles,
    bool OverwriteExistingFiles,
    string TargetLanguage,
    string CustomInstruction,
    int ChunkSize,
    int ContextLines,
    ApiProvider Provider,
    string BaseUrl,
    string ApiKey,
    string Model);

internal sealed record BatchTranslationProgress(
    int CompletedFiles,
    int TotalFiles,
    string CurrentFile,
    string Message);

internal enum BatchFileStatus
{
    Translated,
    TranslatedWithWarnings,
    Copied,
    Skipped,
    OriginalRetained,
    Failed
}

internal sealed record BatchFileResult(
    string RelativePath,
    string SourcePath,
    string TargetPath,
    BatchFileStatus Status,
    string Message,
    int RetainedOriginalCount,
    string? DiagnosticLogPath);

internal sealed record BatchTranslationReport(
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    string SourceFolder,
    string TargetFolder,
    string TargetLanguage,
    int TotalFiles,
    int TranslatedFiles,
    int FilesWithWarnings,
    int CopiedFiles,
    int SkippedFiles,
    int OriginalRetainedFiles,
    int FailedFiles,
    IReadOnlyList<BatchFileResult> Files,
    string? LogFilePath)
{
    public bool HasProblems =>
        FilesWithWarnings > 0 ||
        OriginalRetainedFiles > 0 ||
        FailedFiles > 0;
}

internal sealed class BatchTranslationService(OpenAiClient client)
{
    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value)
        {
            callback(value);
        }
    }

    public static IReadOnlyCollection<string> ParseExtensions(string value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPart in value.Split(
                     new[] { ';', ',', '|', ' ', '\t', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries |
                     StringSplitOptions.TrimEntries))
        {
            var extension = rawPart.Trim();

            if (extension.StartsWith("*.", StringComparison.Ordinal))
                extension = extension[1..];
            else if (extension.StartsWith('*'))
                extension = extension[1..];

            if (!extension.StartsWith('.'))
                extension = "." + extension;

            if (extension.Length <= 1)
                continue;

            if (extension.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                continue;

            result.Add(extension.ToLowerInvariant());
        }

        return result
            .OrderBy(extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<BatchTranslationReport> TranslateAsync(
        BatchTranslationOptions options,
        IProgress<BatchTranslationProgress> progress,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.Now;

        var sourceRoot = NormalizeDirectory(options.SourceFolder);
        var targetRoot = NormalizeDirectory(options.TargetFolder);
        var extensions = new HashSet<string>(
            options.Extensions.Select(NormalizeExtension),
            StringComparer.OrdinalIgnoreCase);

        ValidateOptions(sourceRoot, targetRoot, extensions);

        Directory.CreateDirectory(targetRoot);

        var allSourceFiles = EnumerateSourceFiles(
                sourceRoot,
                options.IncludeSubfolders,
                ct)
            .OrderBy(
                path => Path.GetRelativePath(sourceRoot, path),
                StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var filesToProcess = options.CopyOtherFiles
            ? allSourceFiles
            : allSourceFiles
                .Where(path => extensions.Contains(
                    Path.GetExtension(path)))
                .ToArray();

        var results = new List<BatchFileResult>(filesToProcess.Length);

        ReportProgressSafely(
            progress,
            new BatchTranslationProgress(
                0,
                filesToProcess.Length,
                string.Empty,
                $"Found {filesToProcess.Length} file(s) to process."));

        for (var fileIndex = 0;
             fileIndex < filesToProcess.Length;
             fileIndex++)
        {
            ct.ThrowIfCancellationRequested();

            var sourcePath = filesToProcess[fileIndex];
            var relativePath = Path.GetRelativePath(sourceRoot, sourcePath);
            var targetPath = Path.Combine(targetRoot, relativePath);
            var shouldTranslate = extensions.Contains(
                Path.GetExtension(sourcePath));

            progress.Report(
                new BatchTranslationProgress(
                    fileIndex,
                    filesToProcess.Length,
                    relativePath,
                    $"Processing file {fileIndex + 1} of " +
                    $"{filesToProcess.Length}: {relativePath}"));

            BatchFileResult result;

            try
            {
                result = await ProcessFileAsync(
                    options,
                    sourcePath,
                    targetPath,
                    relativePath,
                    fileIndex,
                    filesToProcess.Length,
                    shouldTranslate,
                    progress,
                    ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                result = new BatchFileResult(
                    relativePath,
                    sourcePath,
                    targetPath,
                    BatchFileStatus.Failed,
                    $"Unexpected batch error while processing the file: {ex.Message}",
                    1,
                    null);
            }

            results.Add(result);

            ReportProgressSafely(
                progress,
                new BatchTranslationProgress(
                    fileIndex + 1,
                    filesToProcess.Length,
                    relativePath,
                    $"{result.Status}: {relativePath}. {result.Message}"));
        }

        var provisionalReport = BuildReport(
            startedAt,
            DateTimeOffset.Now,
            sourceRoot,
            targetRoot,
            options.TargetLanguage,
            results,
            null);

        var logFilePath = await UpdateBatchLogAsync(
            provisionalReport,
            options,
            CancellationToken.None);

        var finalReport = provisionalReport with
        {
            LogFilePath = logFilePath
        };

        ReportProgressSafely(
            progress,
            new BatchTranslationProgress(
                filesToProcess.Length,
                filesToProcess.Length,
                string.Empty,
                BuildCompletionMessage(finalReport)));

        return finalReport;
    }

    private async Task<BatchFileResult> TranslateFileAsync(
        BatchTranslationOptions options,
        string sourcePath,
        string targetPath,
        string relativePath,
        int fileIndex,
        int totalFiles,
        IProgress<BatchTranslationProgress> progress,
        CancellationToken ct)
    {
        try
        {
            EnsureTargetDirectory(targetPath);

            var sourceFile = TextFileData.Read(sourcePath);

            var fileProgress = new CallbackProgress<TranslationProgress>(
                item =>
                {
                    ReportProgressSafely(
                        progress,
                        new BatchTranslationProgress(
                            fileIndex,
                            totalFiles,
                            relativePath,
                            $"File {fileIndex + 1}/{totalFiles}, " +
                            $"{relativePath}: {item.Message}"));
                });

            string translated;
            TranslationRunReport? translationReport;

            if (DocumentTranslationService.Supports(relativePath))
            {
                var documentService =
                    new DocumentTranslationService(client);

                var outcome = await documentService.TranslateAsync(
                    sourceFile.Text,
                    relativePath,
                    targetPath,
                    options.TargetLanguage,
                    options.CustomInstruction,
                    options.ChunkSize,
                    options.ContextLines,
                    options.Provider,
                    options.BaseUrl,
                    options.ApiKey,
                    options.Model,
                    fileProgress,
                    ct);

                translated = outcome.Text;
                translationReport = outcome.Report;
            }
            else
            {
                var engine = new TranslationEngine(client);

                translated = await engine.TranslateAsync(
                    sourceFile.Text,
                    relativePath,
                    options.TargetLanguage,
                    options.CustomInstruction,
                    options.ChunkSize,
                    options.ContextLines,
                    options.Provider,
                    options.BaseUrl,
                    options.ApiKey,
                    options.Model,
                    fileProgress,
                    ct,
                    targetPath);

                translationReport = engine.LastRunReport;
            }

            sourceFile.Write(targetPath, translated);

            var retainedCount =
                translationReport?.RetainedOriginalCount ?? 0;

            if (retainedCount > 0)
            {
                return new BatchFileResult(
                    relativePath,
                    sourcePath,
                    targetPath,
                    BatchFileStatus.TranslatedWithWarnings,
                    $"Translation completed, but {retainedCount} " +
                    "value(s) or line(s) remained in the original language.",
                    retainedCount,
                    translationReport?.LogFilePath);
            }

            return new BatchFileResult(
                relativePath,
                sourcePath,
                targetPath,
                BatchFileStatus.Translated,
                "Translation completed successfully.",
                0,
                null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            try
            {
                EnsureTargetDirectory(targetPath);
                File.Copy(sourcePath, targetPath, true);
                CopyFileMetadata(sourcePath, targetPath);

                return new BatchFileResult(
                    relativePath,
                    sourcePath,
                    targetPath,
                    BatchFileStatus.OriginalRetained,
                    "The file could not be translated and was copied " +
                    $"unchanged. Reason: {ex.Message}",
                    1,
                    null);
            }
            catch (Exception copyException)
            {
                return new BatchFileResult(
                    relativePath,
                    sourcePath,
                    targetPath,
                    BatchFileStatus.Failed,
                    "Translation failed and the original file could not " +
                    $"be copied. Translation error: {ex.Message} " +
                    $"Copy error: {copyException.Message}",
                    1,
                    null);
            }
        }
    }

    private async Task<BatchFileResult> ProcessFileAsync(
        BatchTranslationOptions options,
        string sourcePath,
        string targetPath,
        string relativePath,
        int fileIndex,
        int totalFiles,
        bool shouldTranslate,
        IProgress<BatchTranslationProgress> progress,
        CancellationToken ct)
    {
        if (File.Exists(targetPath) &&
            !options.OverwriteExistingFiles)
        {
            return new BatchFileResult(
                relativePath,
                sourcePath,
                targetPath,
                BatchFileStatus.Skipped,
                "Target file already exists and overwrite is disabled.",
                0,
                null);
        }

        if (shouldTranslate)
        {
            return await TranslateFileAsync(
                options,
                sourcePath,
                targetPath,
                relativePath,
                fileIndex,
                totalFiles,
                progress,
                ct);
        }

        return CopyFile(
            sourcePath,
            targetPath,
            relativePath,
            options.OverwriteExistingFiles);
    }

    private static BatchFileResult CopyFile(
        string sourcePath,
        string targetPath,
        string relativePath,
        bool overwrite)
    {
        try
        {
            EnsureTargetDirectory(targetPath);
            File.Copy(sourcePath, targetPath, overwrite);
            CopyFileMetadata(sourcePath, targetPath);

            return new BatchFileResult(
                relativePath,
                sourcePath,
                targetPath,
                BatchFileStatus.Copied,
                "File was copied without translation.",
                0,
                null);
        }
        catch (Exception ex)
        {
            return new BatchFileResult(
                relativePath,
                sourcePath,
                targetPath,
                BatchFileStatus.Failed,
                $"The file could not be copied: {ex.Message}",
                1,
                null);
        }
    }

    private static IEnumerable<string> EnumerateSourceFiles(
        string sourceRoot,
        bool includeSubfolders,
        CancellationToken ct)
    {
        if (!includeSubfolders)
        {
            foreach (var file in Directory.EnumerateFiles(sourceRoot))
            {
                ct.ThrowIfCancellationRequested();
                yield return file;
            }

            yield break;
        }

        var pendingDirectories = new Stack<string>();
        pendingDirectories.Push(sourceRoot);

        while (pendingDirectories.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var currentDirectory = pendingDirectories.Pop();

            foreach (var file in Directory.EnumerateFiles(currentDirectory))
            {
                ct.ThrowIfCancellationRequested();
                yield return file;
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         currentDirectory))
            {
                ct.ThrowIfCancellationRequested();

                var attributes = File.GetAttributes(directory);

                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                pendingDirectories.Push(directory);
            }
        }
    }

    private static void ValidateOptions(
        string sourceRoot,
        string targetRoot,
        IReadOnlyCollection<string> extensions)
    {
        if (!Directory.Exists(sourceRoot))
        {
            throw new DirectoryNotFoundException(
                $"The source folder does not exist: {sourceRoot}");
        }

        if (extensions.Count == 0)
        {
            throw new InvalidOperationException(
                "Enter at least one file extension, for example .md;.html.");
        }

        if (PathsEqual(sourceRoot, targetRoot))
        {
            throw new InvalidOperationException(
                "Source and target folder must be different.");
        }

        if (IsPathInside(targetRoot, sourceRoot) ||
            IsPathInside(sourceRoot, targetRoot))
        {
            throw new InvalidOperationException(
                "Source and target folder must not be nested inside " +
                "each other. Choose separate folder trees.");
        }
    }

    private static BatchTranslationReport BuildReport(
        DateTimeOffset startedAt,
        DateTimeOffset finishedAt,
        string sourceRoot,
        string targetRoot,
        string targetLanguage,
        IReadOnlyList<BatchFileResult> results,
        string? logFilePath)
    {
        return new BatchTranslationReport(
            startedAt,
            finishedAt,
            sourceRoot,
            targetRoot,
            targetLanguage,
            results.Count,
            results.Count(item =>
                item.Status == BatchFileStatus.Translated),
            results.Count(item =>
                item.Status == BatchFileStatus.TranslatedWithWarnings),
            results.Count(item =>
                item.Status == BatchFileStatus.Copied),
            results.Count(item =>
                item.Status == BatchFileStatus.Skipped),
            results.Count(item =>
                item.Status == BatchFileStatus.OriginalRetained),
            results.Count(item =>
                item.Status == BatchFileStatus.Failed),
            results,
            logFilePath);
    }

    private static async Task<string?> UpdateBatchLogAsync(
        BatchTranslationReport report,
        BatchTranslationOptions options,
        CancellationToken ct)
    {
        var safeLanguage = CreateSafeName(options.TargetLanguage);
        var logFilePath = Path.Combine(
            report.TargetFolder,
            $"translation-batch.{safeLanguage}.log.txt");

        if (!report.HasProblems)
        {
            try
            {
                if (File.Exists(logFilePath))
                    File.Delete(logFilePath);
            }
            catch
            {
                // A stale log is not allowed to fail an otherwise
                // successful batch.
            }

            return null;
        }

        var builder = new StringBuilder();

        builder.AppendLine("Resource Translator - Folder Batch Log");
        builder.AppendLine("======================================");
        builder.AppendLine($"Started:          {report.StartedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Finished:         {report.FinishedAt:yyyy-MM-dd HH:mm:ss zzz}");
        builder.AppendLine($"Source folder:    {report.SourceFolder}");
        builder.AppendLine($"Target folder:    {report.TargetFolder}");
        builder.AppendLine($"Target language:  {report.TargetLanguage}");
        builder.AppendLine($"Provider:         {options.Provider}");
        builder.AppendLine($"Model:            {options.Model}");
        builder.AppendLine($"Extensions:       {string.Join(";", options.Extensions)}");
        builder.AppendLine($"Total files:      {report.TotalFiles}");
        builder.AppendLine($"Translated:       {report.TranslatedFiles}");
        builder.AppendLine($"With warnings:    {report.FilesWithWarnings}");
        builder.AppendLine($"Copied:           {report.CopiedFiles}");
        builder.AppendLine($"Skipped:          {report.SkippedFiles}");
        builder.AppendLine($"Original retained:{report.OriginalRetainedFiles}");
        builder.AppendLine($"Failed:           {report.FailedFiles}");
        builder.AppendLine();
        builder.AppendLine(
            "Only problematic files are listed below. Successfully " +
            "translated or copied files are omitted.");
        builder.AppendLine();

        foreach (var result in report.Files.Where(item =>
                     item.Status is
                         BatchFileStatus.TranslatedWithWarnings or
                         BatchFileStatus.OriginalRetained or
                         BatchFileStatus.Failed))
        {
            builder.AppendLine($"Status:          {result.Status}");
            builder.AppendLine($"Relative path:   {result.RelativePath}");
            builder.AppendLine($"Source:          {result.SourcePath}");
            builder.AppendLine($"Target:          {result.TargetPath}");
            builder.AppendLine($"Message:         {result.Message}");
            builder.AppendLine($"Original items:  {result.RetainedOriginalCount}");

            if (!string.IsNullOrWhiteSpace(result.DiagnosticLogPath))
            {
                builder.AppendLine(
                    $"File detail log: {result.DiagnosticLogPath}");
            }

            builder.AppendLine(new string('-', 72));
        }

        Directory.CreateDirectory(report.TargetFolder);

        await File.WriteAllTextAsync(
            logFilePath,
            builder.ToString(),
            new UTF8Encoding(false),
            ct);

        return logFilePath;
    }

    private static string BuildCompletionMessage(
        BatchTranslationReport report)
    {
        var message =
            $"Batch complete. Translated: {report.TranslatedFiles}, " +
            $"with warnings: {report.FilesWithWarnings}, " +
            $"copied: {report.CopiedFiles}, " +
            $"skipped: {report.SkippedFiles}, " +
            $"original retained: {report.OriginalRetainedFiles}, " +
            $"failed: {report.FailedFiles}.";

        if (!string.IsNullOrWhiteSpace(report.LogFilePath))
            message += $" Log: {report.LogFilePath}";

        return message;
    }

    private static string NormalizeDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("A folder path is missing.");

        return Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(path.Trim()));
    }

    private static string NormalizeExtension(string extension)
    {
        var value = extension.Trim();

        if (value.StartsWith("*.", StringComparison.Ordinal))
            value = value[1..];
        else if (value.StartsWith('*'))
            value = value[1..];

        if (!value.StartsWith('.'))
            value = "." + value;

        return value.ToLowerInvariant();
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            left,
            right,
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal);
    }

    private static bool IsPathInside(string candidate, string parent)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        var parentWithSeparator =
            Path.TrimEndingDirectorySeparator(parent) +
            Path.DirectorySeparatorChar;

        return candidate.StartsWith(
            parentWithSeparator,
            comparison);
    }

    private static void EnsureTargetDirectory(string targetPath)
    {
        var directory = Path.GetDirectoryName(targetPath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
    }

    private static void CopyFileMetadata(
        string sourcePath,
        string targetPath)
    {
        try
        {
            File.SetCreationTimeUtc(
                targetPath,
                File.GetCreationTimeUtc(sourcePath));

            File.SetLastWriteTimeUtc(
                targetPath,
                File.GetLastWriteTimeUtc(sourcePath));
        }
        catch
        {
            // Metadata preservation must never fail the batch.
        }
    }

    private static void ReportProgressSafely<T>(
        IProgress<T> progress,
        T value)
    {
        try
        {
            progress.Report(value);
        }
        catch
        {
            // Progress callbacks must never fail the batch.
        }
    }

    private static string CreateSafeName(string value)
    {
        var result = string.Concat(
            value
                .Trim()
                .ToLowerInvariant()
                .Select(character =>
                    char.IsLetterOrDigit(character) ||
                    character is '-' or '_'
                        ? character
                        : '-'));

        while (result.Contains("--", StringComparison.Ordinal))
            result = result.Replace("--", "-", StringComparison.Ordinal);

        return string.IsNullOrWhiteSpace(result)
            ? "target"
            : result.Trim('-');
    }
}
