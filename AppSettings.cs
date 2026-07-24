using System.Text.Json;

namespace ResourceTranslator;

internal sealed class AppSettings
{
    public string TranslationMode { get; set; } = "SingleFile";
    public ApiProvider Provider { get; set; } = ApiProvider.OpenAI;
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string Model { get; set; } = "gpt-5-mini";
    public string TargetLanguage { get; set; } = "English";
    public int ChunkSize { get; set; } = 12000;
    public int ContextLines { get; set; } = 3;
    public string CustomInstruction { get; set; } = string.Empty;

    public bool UseFolderBatch { get; set; }
    public string LastInputFile { get; set; } = string.Empty;
    public string LastSourceFolder { get; set; } = string.Empty;
    public string LastTargetFolder { get; set; } = string.Empty;
    public string BatchExtensions { get; set; } = ".md;.html";
    public bool IncludeSubfolders { get; set; } = true;
    public bool CopyOtherFiles { get; set; } = true;
    public bool OverwriteExistingFiles { get; set; }

    private static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "ResourceTranslator",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsPath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(
                       File.ReadAllText(SettingsPath)) ??
                   new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(SettingsPath)!);

        File.WriteAllText(
            SettingsPath,
            JsonSerializer.Serialize(
                this,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                }));
    }
}