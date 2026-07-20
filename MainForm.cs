namespace ResourceTranslator;

internal sealed partial class MainForm : Form
{
    private readonly AppSettings _settings;
    private readonly OpenAiClient _client = new();
    private CancellationTokenSource? _cts;

    public MainForm()
    {
        InitializeComponent();

        _settings = AppSettings.Load();

        WireEvents();
        LoadSettings();
        UpdateModeUi();
    }

    private bool IsFolderBatchMode =>
        _translationModeTabs.SelectedTab == _folderBatchTab;

    private void WireEvents()
    {
        _provider.SelectedIndexChanged += (_, _) =>
            ApplyProviderDefaults();

        _showApiKey.Click += (_, _) =>
        {
            _apiKey.UseSystemPasswordChar =
                !_apiKey.UseSystemPasswordChar;

            _showApiKey.Text =
                _apiKey.UseSystemPasswordChar
                    ? "Show"
                    : "Hide";
        };

        _loadModels.Click += async (_, _) =>
            await LoadModelsAsync();

        _browse.Click += (_, _) =>
            SelectInputFile();

        _browseSourceFolder.Click += (_, _) =>
            SelectSourceFolder();

        _browseTargetFolder.Click += (_, _) =>
            SelectTargetFolder();

        _translationModeTabs.SelectedIndexChanged += (_, _) =>
            UpdateModeUi();

        _translate.Click += async (_, _) =>
            await StartTranslationAsync();

        _cancel.Click += (_, _) =>
            _cts?.Cancel();

        FormClosing += (_, _) =>
            SaveSettings();
    }

    private void LoadSettings()
    {
        _provider.SelectedItem =
            _settings.Provider == ApiProvider.LMStudio
                ? "LM Studio"
                : "OpenAI";

        _baseUrl.Text = _settings.ApiBaseUrl;
        _model.Text = _settings.Model;
        _language.Text = _settings.TargetLanguage;

        _chunkSize.Value = Math.Clamp(
            _settings.ChunkSize,
            (int)_chunkSize.Minimum,
            (int)_chunkSize.Maximum);

        _contextLines.Value = Math.Clamp(
            _settings.ContextLines,
            (int)_contextLines.Minimum,
            (int)_contextLines.Maximum);

        _customInstruction.Text =
            _settings.CustomInstruction;

        _inputFile.Text =
            File.Exists(_settings.LastInputFile)
                ? _settings.LastInputFile
                : string.Empty;

        _sourceFolder.Text =
            Directory.Exists(_settings.LastSourceFolder)
                ? _settings.LastSourceFolder
                : string.Empty;

        _targetFolder.Text =
            _settings.LastTargetFolder;

        _batchExtensions.Text =
            string.IsNullOrWhiteSpace(_settings.BatchExtensions)
                ? ".md;.html"
                : _settings.BatchExtensions;

        _includeSubfolders.Checked =
            _settings.IncludeSubfolders;

        _copyOtherFiles.Checked =
            _settings.CopyOtherFiles;

        _overwriteExisting.Checked =
            _settings.OverwriteExistingFiles;

        _translationModeTabs.SelectedTab =
            _settings.UseFolderBatch
                ? _folderBatchTab
                : _singleFileTab;
    }

    private void SaveSettings()
    {
        _settings.Provider = SelectedProvider;
        _settings.ApiBaseUrl = _baseUrl.Text.Trim();
        _settings.Model = _model.Text.Trim();
        _settings.TargetLanguage = _language.Text.Trim();
        _settings.ChunkSize = (int)_chunkSize.Value;
        _settings.ContextLines = (int)_contextLines.Value;
        _settings.CustomInstruction = _customInstruction.Text;

        _settings.UseFolderBatch = IsFolderBatchMode;
        _settings.LastInputFile = _inputFile.Text;
        _settings.LastSourceFolder = _sourceFolder.Text;
        _settings.LastTargetFolder = _targetFolder.Text;
        _settings.BatchExtensions = _batchExtensions.Text.Trim();
        _settings.IncludeSubfolders = _includeSubfolders.Checked;
        _settings.CopyOtherFiles = _copyOtherFiles.Checked;
        _settings.OverwriteExistingFiles = _overwriteExisting.Checked;

        _settings.Save();
    }

    private void SelectInputFile()
    {
        using var dialog = new OpenFileDialog
        {
            Filter =
                "Resource and text files|" +
                "*.resx;*.json;*.js;*.ts;*.properties;*.xml;*.html;*.htm;" +
                "*.cs;*.txt;*.md;*.yaml;*.yml|" +
                "All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _inputFile.Text = dialog.FileName;
    }

    private void SelectSourceFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the source folder to translate.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(_sourceFolder.Text)
                ? _sourceFolder.Text
                : string.Empty
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _sourceFolder.Text = dialog.SelectedPath;

        if (string.IsNullOrWhiteSpace(_targetFolder.Text))
        {
            var sourceName = new DirectoryInfo(
                dialog.SelectedPath).Name;

            var parent = Directory.GetParent(
                dialog.SelectedPath)?.FullName;

            if (!string.IsNullOrWhiteSpace(parent))
            {
                _targetFolder.Text = Path.Combine(
                    parent,
                    sourceName + "-" +
                    CreateSafeLanguageName(_language.Text));
            }
        }
    }

    private void SelectTargetFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description =
                "Select or create the target folder for the translated tree.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_targetFolder.Text)
                ? _targetFolder.Text
                : string.Empty
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _targetFolder.Text = dialog.SelectedPath;
    }

    private async Task LoadModelsAsync()
    {
        try
        {
            ValidateCommonApiFields();

            SetBusy(
                true,
                "Loading models...");

            var models = await _client.GetModelsAsync(
                SelectedProvider,
                _baseUrl.Text.Trim(),
                _apiKey.Text.Trim(),
                CancellationToken.None);

            var selected = _model.Text;

            _model.Items.Clear();
            _model.Items.AddRange(
                models.Cast<object>().ToArray());

            _model.Text = selected;

            Log(
                $"Loaded {models.Count} models from " +
                $"{ProviderDisplayName}.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            SetBusy(
                false,
                "Ready.");
        }
    }

    private async Task StartTranslationAsync()
    {
        if (IsFolderBatchMode)
            await StartFolderBatchAsync();
        else
            await StartSingleFileTranslationAsync();
    }

    private async Task StartSingleFileTranslationAsync()
    {
        try
        {
            ValidateCommonApiFields();
            ValidateSingleFileFields();

            using var saveDialog = new SaveFileDialog
            {
                FileName = CreateOutputName(
                    _inputFile.Text,
                    _language.Text),
                InitialDirectory =
                    Path.GetDirectoryName(_inputFile.Text),
                Filter =
                    "Same file type|*" +
                    Path.GetExtension(_inputFile.Text) +
                    "|All files|*.*",
                OverwritePrompt = true
            };

            if (saveDialog.ShowDialog(this) != DialogResult.OK)
                return;

            SaveSettings();

            _cts = new CancellationTokenSource();

            SetBusy(
                true,
                "Reading source file...");

            _log.Clear();

            var file = TextFileData.Read(
                _inputFile.Text);

            var chunks = Chunker.Create(
                file.Text,
                (int)_chunkSize.Value,
                (int)_contextLines.Value);

            _progress.Maximum = Math.Max(1, chunks.Count);
            _progress.Value = 0;

            Log($"Source: {_inputFile.Text}");

            Log(
                $"Encoding: {file.Encoding.WebName}, " +
                $"BOM: {file.HasBom}, parts: {chunks.Count}");

            var progress =
                new Progress<TranslationProgress>(item =>
                {
                    _progress.Maximum =
                        Math.Max(1, item.Total);

                    _progress.Value =
                        Math.Min(
                            item.Completed,
                            _progress.Maximum);

                    _status.Text = item.Message;
                    Log(item.Message);
                });

            var engine =
                new TranslationEngine(_client);

            var translated =
                await engine.TranslateAsync(
                    file.Text,
                    Path.GetFileName(_inputFile.Text),
                    _language.Text.Trim(),
                    _customInstruction.Text.Trim(),
                    (int)_chunkSize.Value,
                    (int)_contextLines.Value,
                    SelectedProvider,
                    _baseUrl.Text.Trim(),
                    _apiKey.Text.Trim(),
                    _model.Text.Trim(),
                    progress,
                    _cts.Token,
                    saveDialog.FileName);

            file.Write(
                saveDialog.FileName,
                translated);

            _progress.Value = _progress.Maximum;

            Log($"Saved: {saveDialog.FileName}");

            ShowSingleFileCompletion(
                engine.LastRunReport,
                saveDialog.FileName);
        }
        catch (OperationCanceledException)
        {
            Log("Translation cancelled.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            FinishOperation();
        }
    }

    private async Task StartFolderBatchAsync()
    {
        try
        {
            ValidateCommonApiFields();
            ValidateFolderBatchFields();

            SaveSettings();

            _cts = new CancellationTokenSource();

            SetBusy(
                true,
                "Preparing folder batch...");

            _log.Clear();
            _progress.Value = 0;
            _progress.Maximum = 1;

            var extensions =
                BatchTranslationService.ParseExtensions(
                    _batchExtensions.Text);

            var options = new BatchTranslationOptions(
                _sourceFolder.Text.Trim(),
                _targetFolder.Text.Trim(),
                extensions,
                _includeSubfolders.Checked,
                _copyOtherFiles.Checked,
                _overwriteExisting.Checked,
                _language.Text.Trim(),
                _customInstruction.Text.Trim(),
                (int)_chunkSize.Value,
                (int)_contextLines.Value,
                SelectedProvider,
                _baseUrl.Text.Trim(),
                _apiKey.Text.Trim(),
                _model.Text.Trim());

            Log($"Source folder: {options.SourceFolder}");
            Log($"Target folder: {options.TargetFolder}");
            Log($"Extensions: {string.Join(";", extensions)}");
            Log($"Include subfolders: {options.IncludeSubfolders}");
            Log($"Copy other files: {options.CopyOtherFiles}");
            Log($"Overwrite existing: {options.OverwriteExistingFiles}");

            var progress =
                new Progress<BatchTranslationProgress>(item =>
                {
                    _progress.Maximum =
                        Math.Max(1, item.TotalFiles);

                    _progress.Value =
                        Math.Min(
                            item.CompletedFiles,
                            _progress.Maximum);

                    _status.Text = item.Message;
                    Log(item.Message);
                });

            var service =
                new BatchTranslationService(_client);

            var report = await service.TranslateAsync(
                options,
                progress,
                _cts.Token);

            _progress.Maximum = Math.Max(1, report.TotalFiles);
            _progress.Value = _progress.Maximum;

            Log(
                $"Batch saved to: {report.TargetFolder}");

            ShowBatchCompletion(report);
        }
        catch (OperationCanceledException)
        {
            Log(
                "Folder batch cancelled. Files already written remain " +
                "in the target folder.");
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            FinishOperation();
        }
    }

    private void ShowSingleFileCompletion(
        TranslationRunReport? report,
        string outputPath)
    {
        if (report is not null &&
            report.RetainedOriginalCount > 0)
        {
            var message =
                "Translation completed, but " +
                $"{report.RetainedOriginalCount} value(s) or line(s) " +
                "were retained in the original language.";

            if (!string.IsNullOrWhiteSpace(report.LogFilePath))
            {
                message +=
                    Environment.NewLine +
                    Environment.NewLine +
                    "Diagnostic log:" +
                    Environment.NewLine +
                    report.LogFilePath;

                Log(
                    $"Diagnostic log: {report.LogFilePath}");
            }

            MessageBox.Show(
                this,
                message,
                "Completed with warnings",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        MessageBox.Show(
            this,
            "Translation completed and saved successfully." +
            Environment.NewLine +
            Environment.NewLine +
            outputPath,
            "Completed",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowBatchCompletion(
        BatchTranslationReport report)
    {
        var message =
            $"Folder batch completed.{Environment.NewLine}" +
            $"Translated: {report.TranslatedFiles}{Environment.NewLine}" +
            $"With warnings: {report.FilesWithWarnings}{Environment.NewLine}" +
            $"Copied unchanged: {report.CopiedFiles}{Environment.NewLine}" +
            $"Skipped: {report.SkippedFiles}{Environment.NewLine}" +
            $"Original retained: {report.OriginalRetainedFiles}{Environment.NewLine}" +
            $"Failed: {report.FailedFiles}";

        if (!string.IsNullOrWhiteSpace(report.LogFilePath))
        {
            message +=
                Environment.NewLine +
                Environment.NewLine +
                "Batch log:" +
                Environment.NewLine +
                report.LogFilePath;

            Log($"Batch log: {report.LogFilePath}");
        }

        MessageBox.Show(
            this,
            message,
            report.HasProblems
                ? "Batch completed with warnings"
                : "Batch completed",
            MessageBoxButtons.OK,
            report.HasProblems
                ? MessageBoxIcon.Warning
                : MessageBoxIcon.Information);
    }

    private void ValidateCommonApiFields()
    {
        if (SelectedProvider == ApiProvider.OpenAI &&
            string.IsNullOrWhiteSpace(_apiKey.Text))
        {
            throw new InvalidOperationException(
                "Enter an OpenAI API key.");
        }

        if (string.IsNullOrWhiteSpace(_baseUrl.Text))
        {
            throw new InvalidOperationException(
                "Enter the API base URL.");
        }

        if (string.IsNullOrWhiteSpace(_model.Text))
        {
            throw new InvalidOperationException(
                "Select or enter a model.");
        }

        if (string.IsNullOrWhiteSpace(_language.Text))
        {
            throw new InvalidOperationException(
                "Select or enter a target language.");
        }
    }

    private void ValidateSingleFileFields()
    {
        if (!File.Exists(_inputFile.Text))
        {
            throw new FileNotFoundException(
                "Select an existing input file.");
        }
    }

    private void ValidateFolderBatchFields()
    {
        if (!Directory.Exists(_sourceFolder.Text))
        {
            throw new DirectoryNotFoundException(
                "Select an existing source folder.");
        }

        if (string.IsNullOrWhiteSpace(_targetFolder.Text))
        {
            throw new InvalidOperationException(
                "Select a target folder.");
        }

        var extensions =
            BatchTranslationService.ParseExtensions(
                _batchExtensions.Text);

        if (extensions.Count == 0)
        {
            throw new InvalidOperationException(
                "Enter at least one file extension, for example .md;.html.");
        }
    }

    private ApiProvider SelectedProvider =>
        _provider.SelectedItem?.ToString() == "LM Studio"
            ? ApiProvider.LMStudio
            : ApiProvider.OpenAI;

    private string ProviderDisplayName =>
        SelectedProvider == ApiProvider.LMStudio
            ? "LM Studio"
            : "OpenAI";

    private void ApplyProviderDefaults()
    {
        if (SelectedProvider == ApiProvider.LMStudio)
        {
            if (string.IsNullOrWhiteSpace(_baseUrl.Text) ||
                _baseUrl.Text.Contains(
                    "api.openai.com",
                    StringComparison.OrdinalIgnoreCase))
            {
                _baseUrl.Text =
                    "http://localhost:1234/v1";
            }

            _apiKey.PlaceholderText =
                "Optional unless LM Studio authentication is enabled";

            if (_model.Text.StartsWith(
                    "gpt-",
                    StringComparison.OrdinalIgnoreCase))
            {
                _model.Text = string.Empty;
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_baseUrl.Text) ||
                _baseUrl.Text.Contains(
                    "localhost:1234",
                    StringComparison.OrdinalIgnoreCase))
            {
                _baseUrl.Text =
                    "https://api.openai.com/v1";
            }

            _apiKey.PlaceholderText =
                "Required for OpenAI";

            if (string.IsNullOrWhiteSpace(_model.Text))
                _model.Text = "gpt-5-mini";
        }
    }

    private void UpdateModeUi()
    {
        if (IsFolderBatchMode)
        {
            _translate.Text = "Start folder batch";
            _infoLabel.Text =
                "Folder batch preserves the relative folder structure. " +
                "Only the selected extensions are translated. Other files " +
                "can optionally be copied unchanged. Existing target files " +
                "can be skipped so interrupted runs can be continued.";
        }
        else
        {
            _translate.Text = "Translate and save";
            _infoLabel.Text =
                "Single-file translation supports OpenAI and a local " +
                "LM Studio server. Keys, syntax, placeholders, indentation, " +
                "encoding and line endings are preserved and validated.";
        }
    }

    private void SetBusy(
        bool busy,
        string status)
    {
        _topLayout.Enabled = !busy;
        _translate.Enabled = !busy;
        _cancel.Enabled = busy && _cts is not null;
        _status.Text = status;
        UseWaitCursor = busy;
    }

    private void FinishOperation()
    {
        _cts?.Dispose();
        _cts = null;

        SetBusy(
            false,
            "Ready.");
    }

    private void ShowError(Exception ex)
    {
        Log("ERROR: " + ex.Message);

        MessageBox.Show(
            this,
            ex.Message,
            "Error",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }

    private void Log(string text)
    {
        _log.AppendText(
            $"[{DateTime.Now:HH:mm:ss}] " +
            text +
            Environment.NewLine);
    }

    private static string CreateOutputName(
        string input,
        string language)
    {
        return
            Path.GetFileNameWithoutExtension(input) +
            "." +
            CreateSafeLanguageName(language) +
            Path.GetExtension(input);
    }

    private static string CreateSafeLanguageName(string language)
    {
        var safeLanguage = string.Concat(
            language
                .ToLowerInvariant()
                .Where(character =>
                    char.IsLetterOrDigit(character) ||
                    character is '-' or '_'));

        return string.IsNullOrWhiteSpace(safeLanguage)
            ? "translated"
            : safeLanguage;
    }

    protected override void OnFormClosed(
        FormClosedEventArgs e)
    {
        _cts?.Dispose();
        _client.Dispose();

        base.OnFormClosed(e);
    }
}
