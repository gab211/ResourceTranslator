using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace ResourceTranslator;

internal sealed class AppSettings
{
    public string TranslationMode { get; set; } = "SingleFile";
    public ApiProvider Provider { get; set; } = ApiProvider.OpenAI;
    public string ApiBaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ProtectedApiKey { get; set; } = string.Empty;
    public string Model { get; set; } = "gpt-5-mini";
    public string TargetLanguage { get; set; } = "English";
    public int ChunkSize { get; set; } = 12000;
    public int ContextLines { get; set; } = 3;
    public string CustomInstruction { get; set; } = string.Empty;
    public string GlossaryText { get; set; } = string.Empty;

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

    public string GetApiKey()
    {
        if (string.IsNullOrEmpty(ProtectedApiKey))
            return string.Empty;

        try
        {
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(
                    Convert.FromBase64String(ProtectedApiKey),
                    optionalEntropy: null,
                    DataProtectionScope.CurrentUser));
        }
        catch (CryptographicException)
        {
            return string.Empty;
        }
        catch (FormatException)
        {
            return string.Empty;
        }
    }

    public void SetApiKey(string apiKey)
    {
        ProtectedApiKey = string.IsNullOrEmpty(apiKey)
            ? string.Empty
            : Convert.ToBase64String(
                ProtectedData.Protect(
                    Encoding.UTF8.GetBytes(apiKey),
                    optionalEntropy: null,
                    DataProtectionScope.CurrentUser));
    }
}