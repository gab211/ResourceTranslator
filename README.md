# Resource Translator

A lightweight .NET 8 WinForms program for translating large resource, JSON, JavaScript, XML and other text files part by part through either OpenAI or a local LM Studio server.

## Requirements

- Windows 10/11
- Visual Studio 2022 with the ".NET desktop development" workload, or .NET 8 SDK
- For OpenAI: an OpenAI API key
- For LM Studio: LM Studio with the local API server started and a chat/instruction model available

## Start

1. Open `ResourceTranslator.sln`.
2. Build and run the project.
3. Select `OpenAI` or `LM Studio` as provider.
4. Select or type a model. `Load models` reads models from the selected provider.
5. Select the file and target language.
6. Click `Translate and save`.

CLI build:

```powershell
dotnet build ResourceTranslator.sln
dotnet run --project ResourceTranslator\ResourceTranslator.csproj
```

## LM Studio

1. Open LM Studio and load a suitable instruction/chat model.
2. Start the local server in the Developer area.
3. Select `LM Studio` in Resource Translator.
4. Keep the default base URL `http://localhost:1234/v1`, unless you changed the port.
5. Click `Load models` and select the local model.
6. An API token is optional unless authentication is enabled in LM Studio.

LM Studio is accessed through its OpenAI-compatible `/v1/models` and `/v1/chat/completions` endpoints. No LM Studio SDK or additional NuGet package is required.

## Translation safety

- Chunks are created along line and structural boundaries.
- Neighboring lines are sent as read-only context.
- The model must return only the translated chunk with the same line count.
- Resource keys, property prefixes, placeholders, tags and indentation are validated.
- A failed part is automatically retried up to three times.
- Original encoding, BOM and line endings are preserved.
- The source file is never overwritten unless you explicitly choose the same output name.

API keys and tokens are deliberately not saved to disk. Other settings are stored under `%LOCALAPPDATA%\ResourceTranslator\settings.json`.
