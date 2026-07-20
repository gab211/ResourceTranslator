using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ResourceTranslator;

internal sealed partial class MainForm
{
    private IContainer? components = null;

    private TableLayoutPanel _rootLayout = null!;
    private TableLayoutPanel _topLayout = null!;
    private TableLayoutPanel _formLayout = null!;
    private TableLayoutPanel _bottomLayout = null!;

    private Label _providerLabel = null!;
    private Label _apiKeyLabel = null!;
    private Label _baseUrlLabel = null!;
    private Label _modelLabel = null!;
    private Label _languageLabel = null!;
    private Label _chunkSizeLabel = null!;
    private Label _contextLinesLabel = null!;
    private Label _customInstructionLabel = null!;
    private Label _infoLabel = null!;

    private ComboBox _provider = null!;
    private TextBox _apiKey = null!;
    private Button _showApiKey = null!;
    private TextBox _baseUrl = null!;
    private ComboBox _model = null!;
    private Button _loadModels = null!;
    private ComboBox _language = null!;
    private NumericUpDown _chunkSize = null!;
    private NumericUpDown _contextLines = null!;
    private TextBox _customInstruction = null!;

    private TabControl _translationModeTabs = null!;
    private TabPage _singleFileTab = null!;
    private TableLayoutPanel _singleFileLayout = null!;
    private Label _inputFileLabel = null!;
    private TextBox _inputFile = null!;
    private Button _browse = null!;

    private TabPage _folderBatchTab = null!;
    private TableLayoutPanel _folderBatchLayout = null!;
    private Label _sourceFolderLabel = null!;
    private TextBox _sourceFolder = null!;
    private Button _browseSourceFolder = null!;
    private Label _targetFolderLabel = null!;
    private TextBox _targetFolder = null!;
    private Button _browseTargetFolder = null!;
    private Label _batchExtensionsLabel = null!;
    private TextBox _batchExtensions = null!;
    private Label _batchOptionsLabel = null!;
    private FlowLayoutPanel _batchOptionsPanel = null!;
    private CheckBox _includeSubfolders = null!;
    private CheckBox _copyOtherFiles = null!;
    private CheckBox _overwriteExisting = null!;

    private TextBox _log = null!;
    private Label _status = null!;
    private ProgressBar _progress = null!;
    private Button _cancel = null!;
    private Button _translate = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing && components != null)
        {
            components.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _rootLayout = new TableLayoutPanel();
        _topLayout = new TableLayoutPanel();
        _formLayout = new TableLayoutPanel();
        _providerLabel = new Label();
        _provider = new ComboBox();
        _apiKeyLabel = new Label();
        _apiKey = new TextBox();
        _showApiKey = new Button();
        _baseUrlLabel = new Label();
        _baseUrl = new TextBox();
        _modelLabel = new Label();
        _model = new ComboBox();
        _loadModels = new Button();
        _languageLabel = new Label();
        _language = new ComboBox();
        _chunkSizeLabel = new Label();
        _chunkSize = new NumericUpDown();
        _contextLinesLabel = new Label();
        _contextLines = new NumericUpDown();
        _customInstructionLabel = new Label();
        _customInstruction = new TextBox();
        _translationModeTabs = new TabControl();
        _singleFileTab = new TabPage();
        _singleFileLayout = new TableLayoutPanel();
        _inputFileLabel = new Label();
        _inputFile = new TextBox();
        _browse = new Button();
        _folderBatchTab = new TabPage();
        _folderBatchLayout = new TableLayoutPanel();
        _sourceFolderLabel = new Label();
        _sourceFolder = new TextBox();
        _browseSourceFolder = new Button();
        _targetFolderLabel = new Label();
        _targetFolder = new TextBox();
        _browseTargetFolder = new Button();
        _batchExtensionsLabel = new Label();
        _batchExtensions = new TextBox();
        _batchOptionsLabel = new Label();
        _batchOptionsPanel = new FlowLayoutPanel();
        _includeSubfolders = new CheckBox();
        _copyOtherFiles = new CheckBox();
        _overwriteExisting = new CheckBox();
        _infoLabel = new Label();
        _log = new TextBox();
        _bottomLayout = new TableLayoutPanel();
        _status = new Label();
        _progress = new ProgressBar();
        _cancel = new Button();
        _translate = new Button();
        _rootLayout.SuspendLayout();
        _topLayout.SuspendLayout();
        _formLayout.SuspendLayout();
        ((ISupportInitialize)_chunkSize).BeginInit();
        ((ISupportInitialize)_contextLines).BeginInit();
        _translationModeTabs.SuspendLayout();
        _singleFileTab.SuspendLayout();
        _singleFileLayout.SuspendLayout();
        _folderBatchTab.SuspendLayout();
        _folderBatchLayout.SuspendLayout();
        _batchOptionsPanel.SuspendLayout();
        _bottomLayout.SuspendLayout();
        SuspendLayout();
        // 
        // _rootLayout
        // 
        _rootLayout.ColumnCount = 1;
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _rootLayout.Controls.Add(_topLayout, 0, 0);
        _rootLayout.Controls.Add(_log, 0, 1);
        _rootLayout.Controls.Add(_bottomLayout, 0, 2);
        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.Location = new Point(0, 0);
        _rootLayout.Name = "_rootLayout";
        _rootLayout.Padding = new Padding(12);
        _rootLayout.RowCount = 3;
        _rootLayout.RowStyles.Add(new RowStyle());
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle());
        _rootLayout.Size = new Size(984, 861);
        _rootLayout.TabIndex = 0;
        // 
        // _topLayout
        // 
        _topLayout.AutoSize = true;
        _topLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _topLayout.ColumnCount = 1;
        _topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _topLayout.Controls.Add(_formLayout, 0, 0);
        _topLayout.Controls.Add(_translationModeTabs, 0, 1);
        _topLayout.Controls.Add(_infoLabel, 0, 2);
        _topLayout.Dock = DockStyle.Top;
        _topLayout.Location = new Point(12, 12);
        _topLayout.Margin = new Padding(0);
        _topLayout.Name = "_topLayout";
        _topLayout.RowCount = 3;
        _topLayout.RowStyles.Add(new RowStyle());
        _topLayout.RowStyles.Add(new RowStyle());
        _topLayout.RowStyles.Add(new RowStyle());
        _topLayout.Size = new Size(960, 548);
        _topLayout.TabIndex = 0;
        // 
        // _formLayout
        // 
        _formLayout.AutoSize = true;
        _formLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _formLayout.ColumnCount = 3;
        _formLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150F));
        _formLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _formLayout.ColumnStyles.Add(new ColumnStyle());
        _formLayout.Controls.Add(_providerLabel, 0, 0);
        _formLayout.Controls.Add(_provider, 1, 0);
        _formLayout.Controls.Add(_apiKeyLabel, 0, 1);
        _formLayout.Controls.Add(_apiKey, 1, 1);
        _formLayout.Controls.Add(_showApiKey, 2, 1);
        _formLayout.Controls.Add(_baseUrlLabel, 0, 2);
        _formLayout.Controls.Add(_baseUrl, 1, 2);
        _formLayout.Controls.Add(_modelLabel, 0, 3);
        _formLayout.Controls.Add(_model, 1, 3);
        _formLayout.Controls.Add(_loadModels, 2, 3);
        _formLayout.Controls.Add(_languageLabel, 0, 4);
        _formLayout.Controls.Add(_language, 1, 4);
        _formLayout.Controls.Add(_chunkSizeLabel, 0, 5);
        _formLayout.Controls.Add(_chunkSize, 1, 5);
        _formLayout.Controls.Add(_contextLinesLabel, 0, 6);
        _formLayout.Controls.Add(_contextLines, 1, 6);
        _formLayout.Controls.Add(_customInstructionLabel, 0, 7);
        _formLayout.Controls.Add(_customInstruction, 1, 7);
        _formLayout.Dock = DockStyle.Top;
        _formLayout.Location = new Point(0, 0);
        _formLayout.Margin = new Padding(0);
        _formLayout.Name = "_formLayout";
        _formLayout.RowCount = 8;
        _formLayout.RowStyles.Add(new RowStyle());
        _formLayout.RowStyles.Add(new RowStyle());
        _formLayout.RowStyles.Add(new RowStyle());
        _formLayout.RowStyles.Add(new RowStyle());
        _formLayout.RowStyles.Add(new RowStyle());
        _formLayout.RowStyles.Add(new RowStyle());
        _formLayout.RowStyles.Add(new RowStyle());
        _formLayout.RowStyles.Add(new RowStyle());
        _formLayout.Size = new Size(960, 311);
        _formLayout.TabIndex = 0;
        // 
        // _providerLabel
        // 
        _providerLabel.Anchor = AnchorStyles.Left;
        _providerLabel.AutoSize = true;
        _providerLabel.Location = new Point(3, 10);
        _providerLabel.Margin = new Padding(3, 8, 3, 3);
        _providerLabel.Name = "_providerLabel";
        _providerLabel.Size = new Size(51, 15);
        _providerLabel.TabIndex = 0;
        _providerLabel.Text = "Provider";
        // 
        // _provider
        // 
        _formLayout.SetColumnSpan(_provider, 2);
        _provider.Dock = DockStyle.Fill;
        _provider.DropDownStyle = ComboBoxStyle.DropDownList;
        _provider.FormattingEnabled = true;
        _provider.Items.AddRange(new object[] { "OpenAI", "LM Studio" });
        _provider.Location = new Point(153, 4);
        _provider.Margin = new Padding(3, 4, 3, 4);
        _provider.Name = "_provider";
        _provider.Size = new Size(804, 23);
        _provider.TabIndex = 0;
        // 
        // _apiKeyLabel
        // 
        _apiKeyLabel.Anchor = AnchorStyles.Left;
        _apiKeyLabel.AutoSize = true;
        _apiKeyLabel.Location = new Point(3, 41);
        _apiKeyLabel.Margin = new Padding(3, 8, 3, 3);
        _apiKeyLabel.Name = "_apiKeyLabel";
        _apiKeyLabel.Size = new Size(87, 15);
        _apiKeyLabel.TabIndex = 1;
        _apiKeyLabel.Text = "API key / token";
        // 
        // _apiKey
        // 
        _apiKey.Dock = DockStyle.Fill;
        _apiKey.Location = new Point(153, 35);
        _apiKey.Margin = new Padding(3, 4, 3, 4);
        _apiKey.Name = "_apiKey";
        _apiKey.Size = new Size(713, 23);
        _apiKey.TabIndex = 1;
        _apiKey.UseSystemPasswordChar = true;
        // 
        // _showApiKey
        // 
        _showApiKey.AutoSize = true;
        _showApiKey.Location = new Point(872, 34);
        _showApiKey.Name = "_showApiKey";
        _showApiKey.Size = new Size(75, 25);
        _showApiKey.TabIndex = 2;
        _showApiKey.Text = "Show";
        _showApiKey.UseVisualStyleBackColor = true;
        // 
        // _baseUrlLabel
        // 
        _baseUrlLabel.Anchor = AnchorStyles.Left;
        _baseUrlLabel.AutoSize = true;
        _baseUrlLabel.Location = new Point(3, 72);
        _baseUrlLabel.Margin = new Padding(3, 8, 3, 3);
        _baseUrlLabel.Name = "_baseUrlLabel";
        _baseUrlLabel.Size = new Size(76, 15);
        _baseUrlLabel.TabIndex = 2;
        _baseUrlLabel.Text = "API base URL";
        // 
        // _baseUrl
        // 
        _formLayout.SetColumnSpan(_baseUrl, 2);
        _baseUrl.Dock = DockStyle.Fill;
        _baseUrl.Location = new Point(153, 66);
        _baseUrl.Margin = new Padding(3, 4, 3, 4);
        _baseUrl.Name = "_baseUrl";
        _baseUrl.Size = new Size(804, 23);
        _baseUrl.TabIndex = 3;
        // 
        // _modelLabel
        // 
        _modelLabel.Anchor = AnchorStyles.Left;
        _modelLabel.AutoSize = true;
        _modelLabel.Location = new Point(3, 103);
        _modelLabel.Margin = new Padding(3, 8, 3, 3);
        _modelLabel.Name = "_modelLabel";
        _modelLabel.Size = new Size(41, 15);
        _modelLabel.TabIndex = 3;
        _modelLabel.Text = "Model";
        // 
        // _model
        // 
        _model.Dock = DockStyle.Fill;
        _model.FormattingEnabled = true;
        _model.Location = new Point(153, 97);
        _model.Margin = new Padding(3, 4, 3, 4);
        _model.Name = "_model";
        _model.Size = new Size(713, 23);
        _model.TabIndex = 4;
        // 
        // _loadModels
        // 
        _loadModels.AutoSize = true;
        _loadModels.Location = new Point(872, 96);
        _loadModels.Name = "_loadModels";
        _loadModels.Size = new Size(85, 25);
        _loadModels.TabIndex = 5;
        _loadModels.Text = "Load models";
        _loadModels.UseVisualStyleBackColor = true;
        // 
        // _languageLabel
        // 
        _languageLabel.Anchor = AnchorStyles.Left;
        _languageLabel.AutoSize = true;
        _languageLabel.Location = new Point(3, 134);
        _languageLabel.Margin = new Padding(3, 8, 3, 3);
        _languageLabel.Name = "_languageLabel";
        _languageLabel.Size = new Size(91, 15);
        _languageLabel.TabIndex = 4;
        _languageLabel.Text = "Target language";
        // 
        // _language
        // 
        _formLayout.SetColumnSpan(_language, 2);
        _language.Dock = DockStyle.Fill;
        _language.FormattingEnabled = true;
        _language.Items.AddRange(new object[] { "English", "French", "Spanish", "Italian", "Dutch", "Polish", "Portuguese", "Danish", "Swedish", "Norwegian", "Finnish", "Czech", "Ukrainian", "Turkish", "Japanese", "Korean", "Simplified Chinese" });
        _language.Location = new Point(153, 128);
        _language.Margin = new Padding(3, 4, 3, 4);
        _language.Name = "_language";
        _language.Size = new Size(804, 23);
        _language.TabIndex = 6;
        // 
        // _chunkSizeLabel
        // 
        _chunkSizeLabel.Anchor = AnchorStyles.Left;
        _chunkSizeLabel.AutoSize = true;
        _chunkSizeLabel.Location = new Point(3, 165);
        _chunkSizeLabel.Margin = new Padding(3, 8, 3, 3);
        _chunkSizeLabel.Name = "_chunkSizeLabel";
        _chunkSizeLabel.Size = new Size(91, 15);
        _chunkSizeLabel.TabIndex = 5;
        _chunkSizeLabel.Text = "Max. chunk size";
        // 
        // _chunkSize
        // 
        _formLayout.SetColumnSpan(_chunkSize, 2);
        _chunkSize.Dock = DockStyle.Left;
        _chunkSize.Increment = new decimal(new int[] { 1000, 0, 0, 0 });
        _chunkSize.Location = new Point(153, 159);
        _chunkSize.Margin = new Padding(3, 4, 3, 4);
        _chunkSize.Maximum = new decimal(new int[] { 100000, 0, 0, 0 });
        _chunkSize.Minimum = new decimal(new int[] { 2000, 0, 0, 0 });
        _chunkSize.Name = "_chunkSize";
        _chunkSize.Size = new Size(130, 23);
        _chunkSize.TabIndex = 7;
        _chunkSize.Value = new decimal(new int[] { 12000, 0, 0, 0 });
        // 
        // _contextLinesLabel
        // 
        _contextLinesLabel.Anchor = AnchorStyles.Left;
        _contextLinesLabel.AutoSize = true;
        _contextLinesLabel.Location = new Point(3, 196);
        _contextLinesLabel.Margin = new Padding(3, 8, 3, 3);
        _contextLinesLabel.Name = "_contextLinesLabel";
        _contextLinesLabel.Size = new Size(76, 15);
        _contextLinesLabel.TabIndex = 6;
        _contextLinesLabel.Text = "Context lines";
        // 
        // _contextLines
        // 
        _formLayout.SetColumnSpan(_contextLines, 2);
        _contextLines.Dock = DockStyle.Left;
        _contextLines.Location = new Point(153, 190);
        _contextLines.Margin = new Padding(3, 4, 3, 4);
        _contextLines.Maximum = new decimal(new int[] { 20, 0, 0, 0 });
        _contextLines.Name = "_contextLines";
        _contextLines.Size = new Size(130, 23);
        _contextLines.TabIndex = 8;
        _contextLines.Value = new decimal(new int[] { 3, 0, 0, 0 });
        // 
        // _customInstructionLabel
        // 
        _customInstructionLabel.Anchor = AnchorStyles.Left;
        _customInstructionLabel.AutoSize = true;
        _customInstructionLabel.Location = new Point(3, 259);
        _customInstructionLabel.Margin = new Padding(3, 8, 3, 3);
        _customInstructionLabel.Name = "_customInstructionLabel";
        _customInstructionLabel.Size = new Size(93, 15);
        _customInstructionLabel.TabIndex = 7;
        _customInstructionLabel.Text = "Extra instruction";
        // 
        // _customInstruction
        // 
        _formLayout.SetColumnSpan(_customInstruction, 2);
        _customInstruction.Dock = DockStyle.Fill;
        _customInstruction.Location = new Point(153, 221);
        _customInstruction.Margin = new Padding(3, 4, 3, 4);
        _customInstruction.Multiline = true;
        _customInstruction.Name = "_customInstruction";
        _customInstruction.ScrollBars = ScrollBars.Vertical;
        _customInstruction.Size = new Size(804, 86);
        _customInstruction.TabIndex = 9;
        // 
        // _translationModeTabs
        // 
        _translationModeTabs.Controls.Add(_singleFileTab);
        _translationModeTabs.Controls.Add(_folderBatchTab);
        _translationModeTabs.Dock = DockStyle.Top;
        _translationModeTabs.Location = new Point(0, 319);
        _translationModeTabs.Margin = new Padding(0, 8, 0, 0);
        _translationModeTabs.Name = "_translationModeTabs";
        _translationModeTabs.SelectedIndex = 0;
        _translationModeTabs.Size = new Size(960, 190);
        _translationModeTabs.TabIndex = 10;
        // 
        // _singleFileTab
        // 
        _singleFileTab.Controls.Add(_singleFileLayout);
        _singleFileTab.Location = new Point(4, 24);
        _singleFileTab.Name = "_singleFileTab";
        _singleFileTab.Padding = new Padding(8);
        _singleFileTab.Size = new Size(952, 162);
        _singleFileTab.TabIndex = 0;
        _singleFileTab.Text = "Single file";
        _singleFileTab.UseVisualStyleBackColor = true;
        // 
        // _singleFileLayout
        // 
        _singleFileLayout.ColumnCount = 3;
        _singleFileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        _singleFileLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _singleFileLayout.ColumnStyles.Add(new ColumnStyle());
        _singleFileLayout.Controls.Add(_inputFileLabel, 0, 0);
        _singleFileLayout.Controls.Add(_inputFile, 1, 0);
        _singleFileLayout.Controls.Add(_browse, 2, 0);
        _singleFileLayout.Dock = DockStyle.Top;
        _singleFileLayout.Location = new Point(8, 8);
        _singleFileLayout.Name = "_singleFileLayout";
        _singleFileLayout.RowCount = 1;
        _singleFileLayout.RowStyles.Add(new RowStyle());
        _singleFileLayout.Size = new Size(936, 34);
        _singleFileLayout.TabIndex = 0;
        // 
        // _inputFileLabel
        // 
        _inputFileLabel.Anchor = AnchorStyles.Left;
        _inputFileLabel.AutoSize = true;
        _inputFileLabel.Location = new Point(3, 9);
        _inputFileLabel.Name = "_inputFileLabel";
        _inputFileLabel.Size = new Size(54, 15);
        _inputFileLabel.TabIndex = 0;
        _inputFileLabel.Text = "Input file";
        // 
        // _inputFile
        // 
        _inputFile.Dock = DockStyle.Fill;
        _inputFile.Location = new Point(133, 4);
        _inputFile.Margin = new Padding(3, 4, 3, 4);
        _inputFile.Name = "_inputFile";
        _inputFile.ReadOnly = true;
        _inputFile.Size = new Size(719, 23);
        _inputFile.TabIndex = 0;
        // 
        // _browse
        // 
        _browse.AutoSize = true;
        _browse.Location = new Point(858, 3);
        _browse.Name = "_browse";
        _browse.Size = new Size(75, 25);
        _browse.TabIndex = 1;
        _browse.Text = "Browse...";
        _browse.UseVisualStyleBackColor = true;
        // 
        // _folderBatchTab
        // 
        _folderBatchTab.Controls.Add(_folderBatchLayout);
        _folderBatchTab.Location = new Point(4, 24);
        _folderBatchTab.Name = "_folderBatchTab";
        _folderBatchTab.Padding = new Padding(8);
        _folderBatchTab.Size = new Size(952, 162);
        _folderBatchTab.TabIndex = 1;
        _folderBatchTab.Text = "Folder batch";
        _folderBatchTab.UseVisualStyleBackColor = true;
        // 
        // _folderBatchLayout
        // 
        _folderBatchLayout.AutoSize = true;
        _folderBatchLayout.ColumnCount = 3;
        _folderBatchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130F));
        _folderBatchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _folderBatchLayout.ColumnStyles.Add(new ColumnStyle());
        _folderBatchLayout.Controls.Add(_sourceFolderLabel, 0, 0);
        _folderBatchLayout.Controls.Add(_sourceFolder, 1, 0);
        _folderBatchLayout.Controls.Add(_browseSourceFolder, 2, 0);
        _folderBatchLayout.Controls.Add(_targetFolderLabel, 0, 1);
        _folderBatchLayout.Controls.Add(_targetFolder, 1, 1);
        _folderBatchLayout.Controls.Add(_browseTargetFolder, 2, 1);
        _folderBatchLayout.Controls.Add(_batchExtensionsLabel, 0, 2);
        _folderBatchLayout.Controls.Add(_batchExtensions, 1, 2);
        _folderBatchLayout.Controls.Add(_batchOptionsLabel, 0, 3);
        _folderBatchLayout.Controls.Add(_batchOptionsPanel, 1, 3);
        _folderBatchLayout.Dock = DockStyle.Fill;
        _folderBatchLayout.Location = new Point(8, 8);
        _folderBatchLayout.Name = "_folderBatchLayout";
        _folderBatchLayout.RowCount = 4;
        _folderBatchLayout.RowStyles.Add(new RowStyle());
        _folderBatchLayout.RowStyles.Add(new RowStyle());
        _folderBatchLayout.RowStyles.Add(new RowStyle());
        _folderBatchLayout.RowStyles.Add(new RowStyle());
        _folderBatchLayout.Size = new Size(936, 146);
        _folderBatchLayout.TabIndex = 0;
        // 
        // _sourceFolderLabel
        // 
        _sourceFolderLabel.Anchor = AnchorStyles.Left;
        _sourceFolderLabel.AutoSize = true;
        _sourceFolderLabel.Location = new Point(3, 8);
        _sourceFolderLabel.Name = "_sourceFolderLabel";
        _sourceFolderLabel.Size = new Size(77, 15);
        _sourceFolderLabel.TabIndex = 0;
        _sourceFolderLabel.Text = "Source folder";
        // 
        // _sourceFolder
        // 
        _sourceFolder.Dock = DockStyle.Fill;
        _sourceFolder.Location = new Point(133, 4);
        _sourceFolder.Margin = new Padding(3, 4, 3, 4);
        _sourceFolder.Name = "_sourceFolder";
        _sourceFolder.Size = new Size(719, 23);
        _sourceFolder.TabIndex = 0;
        // 
        // _browseSourceFolder
        // 
        _browseSourceFolder.AutoSize = true;
        _browseSourceFolder.Location = new Point(858, 3);
        _browseSourceFolder.Name = "_browseSourceFolder";
        _browseSourceFolder.Size = new Size(75, 25);
        _browseSourceFolder.TabIndex = 1;
        _browseSourceFolder.Text = "Browse...";
        _browseSourceFolder.UseVisualStyleBackColor = true;
        // 
        // _targetFolderLabel
        // 
        _targetFolderLabel.Anchor = AnchorStyles.Left;
        _targetFolderLabel.AutoSize = true;
        _targetFolderLabel.Location = new Point(3, 39);
        _targetFolderLabel.Name = "_targetFolderLabel";
        _targetFolderLabel.Size = new Size(73, 15);
        _targetFolderLabel.TabIndex = 1;
        _targetFolderLabel.Text = "Target folder";
        // 
        // _targetFolder
        // 
        _targetFolder.Dock = DockStyle.Fill;
        _targetFolder.Location = new Point(133, 35);
        _targetFolder.Margin = new Padding(3, 4, 3, 4);
        _targetFolder.Name = "_targetFolder";
        _targetFolder.Size = new Size(719, 23);
        _targetFolder.TabIndex = 2;
        // 
        // _browseTargetFolder
        // 
        _browseTargetFolder.AutoSize = true;
        _browseTargetFolder.Location = new Point(858, 34);
        _browseTargetFolder.Name = "_browseTargetFolder";
        _browseTargetFolder.Size = new Size(75, 25);
        _browseTargetFolder.TabIndex = 3;
        _browseTargetFolder.Text = "Browse...";
        _browseTargetFolder.UseVisualStyleBackColor = true;
        // 
        // _batchExtensionsLabel
        // 
        _batchExtensionsLabel.Anchor = AnchorStyles.Left;
        _batchExtensionsLabel.AutoSize = true;
        _batchExtensionsLabel.Location = new Point(3, 70);
        _batchExtensionsLabel.Name = "_batchExtensionsLabel";
        _batchExtensionsLabel.Size = new Size(84, 15);
        _batchExtensionsLabel.TabIndex = 2;
        _batchExtensionsLabel.Text = "Translate types";
        // 
        // _batchExtensions
        // 
        _folderBatchLayout.SetColumnSpan(_batchExtensions, 2);
        _batchExtensions.Dock = DockStyle.Fill;
        _batchExtensions.Location = new Point(133, 66);
        _batchExtensions.Margin = new Padding(3, 4, 3, 4);
        _batchExtensions.Name = "_batchExtensions";
        _batchExtensions.PlaceholderText = ".md;.html";
        _batchExtensions.Size = new Size(800, 23);
        _batchExtensions.TabIndex = 4;
        _batchExtensions.Text = ".md;.html";
        // 
        // _batchOptionsLabel
        // 
        _batchOptionsLabel.Anchor = AnchorStyles.Left;
        _batchOptionsLabel.AutoSize = true;
        _batchOptionsLabel.Location = new Point(3, 112);
        _batchOptionsLabel.Name = "_batchOptionsLabel";
        _batchOptionsLabel.Size = new Size(49, 15);
        _batchOptionsLabel.TabIndex = 3;
        _batchOptionsLabel.Text = "Options";
        // 
        // _batchOptionsPanel
        // 
        _batchOptionsPanel.AutoSize = true;
        _folderBatchLayout.SetColumnSpan(_batchOptionsPanel, 2);
        _batchOptionsPanel.Controls.Add(_includeSubfolders);
        _batchOptionsPanel.Controls.Add(_copyOtherFiles);
        _batchOptionsPanel.Controls.Add(_overwriteExisting);
        _batchOptionsPanel.Dock = DockStyle.Fill;
        _batchOptionsPanel.Location = new Point(130, 93);
        _batchOptionsPanel.Margin = new Padding(0);
        _batchOptionsPanel.Name = "_batchOptionsPanel";
        _batchOptionsPanel.Size = new Size(806, 53);
        _batchOptionsPanel.TabIndex = 5;
        // 
        // _includeSubfolders
        // 
        _includeSubfolders.AutoSize = true;
        _includeSubfolders.Checked = true;
        _includeSubfolders.CheckState = CheckState.Checked;
        _includeSubfolders.Location = new Point(3, 6);
        _includeSubfolders.Margin = new Padding(3, 6, 16, 3);
        _includeSubfolders.Name = "_includeSubfolders";
        _includeSubfolders.Size = new Size(123, 19);
        _includeSubfolders.TabIndex = 0;
        _includeSubfolders.Text = "Include subfolders";
        _includeSubfolders.UseVisualStyleBackColor = true;
        // 
        // _copyOtherFiles
        // 
        _copyOtherFiles.AutoSize = true;
        _copyOtherFiles.Checked = true;
        _copyOtherFiles.CheckState = CheckState.Checked;
        _copyOtherFiles.Location = new Point(145, 6);
        _copyOtherFiles.Margin = new Padding(3, 6, 16, 3);
        _copyOtherFiles.Name = "_copyOtherFiles";
        _copyOtherFiles.Size = new Size(172, 19);
        _copyOtherFiles.TabIndex = 1;
        _copyOtherFiles.Text = "Copy other files unchanged";
        _copyOtherFiles.UseVisualStyleBackColor = true;
        // 
        // _overwriteExisting
        // 
        _overwriteExisting.AutoSize = true;
        _overwriteExisting.Location = new Point(336, 6);
        _overwriteExisting.Margin = new Padding(3, 6, 3, 3);
        _overwriteExisting.Name = "_overwriteExisting";
        _overwriteExisting.Size = new Size(179, 19);
        _overwriteExisting.TabIndex = 2;
        _overwriteExisting.Text = "Overwrite existing target files";
        _overwriteExisting.UseVisualStyleBackColor = true;
        // 
        // _infoLabel
        // 
        _infoLabel.AutoSize = true;
        _infoLabel.Dock = DockStyle.Top;
        _infoLabel.Location = new Point(0, 517);
        _infoLabel.Margin = new Padding(0, 8, 0, 8);
        _infoLabel.MaximumSize = new Size(930, 0);
        _infoLabel.Name = "_infoLabel";
        _infoLabel.Padding = new Padding(0, 4, 0, 4);
        _infoLabel.Size = new Size(930, 23);
        _infoLabel.TabIndex = 11;
        _infoLabel.Text = "Single-file translation supports OpenAI and a local LM Studio server.";
        // 
        // _log
        // 
        _log.Dock = DockStyle.Fill;
        _log.Location = new Point(15, 566);
        _log.Margin = new Padding(3, 6, 3, 6);
        _log.Multiline = true;
        _log.Name = "_log";
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Size = new Size(954, 240);
        _log.TabIndex = 1;
        // 
        // _bottomLayout
        // 
        _bottomLayout.AutoSize = true;
        _bottomLayout.ColumnCount = 4;
        _bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
        _bottomLayout.ColumnStyles.Add(new ColumnStyle());
        _bottomLayout.ColumnStyles.Add(new ColumnStyle());
        _bottomLayout.Controls.Add(_status, 0, 0);
        _bottomLayout.Controls.Add(_progress, 1, 0);
        _bottomLayout.Controls.Add(_cancel, 2, 0);
        _bottomLayout.Controls.Add(_translate, 3, 0);
        _bottomLayout.Dock = DockStyle.Fill;
        _bottomLayout.Location = new Point(12, 812);
        _bottomLayout.Margin = new Padding(0);
        _bottomLayout.Name = "_bottomLayout";
        _bottomLayout.RowCount = 1;
        _bottomLayout.RowStyles.Add(new RowStyle());
        _bottomLayout.Size = new Size(960, 37);
        _bottomLayout.TabIndex = 2;
        // 
        // _status
        // 
        _status.AutoEllipsis = true;
        _status.Dock = DockStyle.Fill;
        _status.Location = new Point(3, 0);
        _status.Name = "_status";
        _status.Size = new Size(496, 37);
        _status.TabIndex = 0;
        _status.Text = "Ready.";
        _status.TextAlign = ContentAlignment.MiddleLeft;
        // 
        // _progress
        // 
        _progress.Dock = DockStyle.Fill;
        _progress.Location = new Point(505, 5);
        _progress.Margin = new Padding(3, 5, 3, 5);
        _progress.Name = "_progress";
        _progress.Size = new Size(214, 27);
        _progress.TabIndex = 1;
        // 
        // _cancel
        // 
        _cancel.AutoSize = true;
        _cancel.Enabled = false;
        _cancel.Location = new Point(725, 4);
        _cancel.Margin = new Padding(3, 4, 3, 3);
        _cancel.Name = "_cancel";
        _cancel.Size = new Size(63, 25);
        _cancel.TabIndex = 11;
        _cancel.Text = "Cancel";
        _cancel.UseVisualStyleBackColor = true;
        // 
        // _translate
        // 
        _translate.AutoSize = true;
        _translate.Location = new Point(794, 4);
        _translate.Margin = new Padding(3, 4, 3, 3);
        _translate.Name = "_translate";
        _translate.Size = new Size(163, 25);
        _translate.TabIndex = 12;
        _translate.Text = "Translate and save";
        _translate.UseVisualStyleBackColor = true;
        // 
        // MainForm
        // 
        AcceptButton = _translate;
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        CancelButton = _cancel;
        ClientSize = new Size(984, 861);
        Controls.Add(_rootLayout);
        Font = new Font("Segoe UI", 9F);
        MinimumSize = new Size(900, 720);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "Resource Translator - OpenAI / LM Studio";
        _rootLayout.ResumeLayout(false);
        _rootLayout.PerformLayout();
        _topLayout.ResumeLayout(false);
        _topLayout.PerformLayout();
        _formLayout.ResumeLayout(false);
        _formLayout.PerformLayout();
        ((ISupportInitialize)_chunkSize).EndInit();
        ((ISupportInitialize)_contextLines).EndInit();
        _translationModeTabs.ResumeLayout(false);
        _singleFileTab.ResumeLayout(false);
        _singleFileLayout.ResumeLayout(false);
        _singleFileLayout.PerformLayout();
        _folderBatchTab.ResumeLayout(false);
        _folderBatchTab.PerformLayout();
        _folderBatchLayout.ResumeLayout(false);
        _folderBatchLayout.PerformLayout();
        _batchOptionsPanel.ResumeLayout(false);
        _batchOptionsPanel.PerformLayout();
        _bottomLayout.ResumeLayout(false);
        _bottomLayout.PerformLayout();
        ResumeLayout(false);
    }
}
