# Resource Translator

Resource Translator is a lightweight .NET 8 WinForms application for translating large resource, JSON, JavaScript, XML, and other text files in manageable chunks using either OpenAI or a local LM Studio server.

## Features

- OpenAI and LM Studio support
- Translation of large files in chunks
- Context from neighboring lines
- Validation of line count, placeholders, tags, indentation, and resource keys
- Automatic retries for failed chunks
- Preservation of encoding, BOM, and line endings
- API keys and tokens are not stored on disk

## Requirements

### To run a precompiled release

- Windows 10 or Windows 11
- No compiler is required
- Depending on the release package, the .NET 8 Desktop Runtime may be required

Download the latest compiled version from the repository's **Releases** section, extract the ZIP archive, and start `ResourceTranslator.exe`.

### To build from source

- Windows 10 or Windows 11
- Visual Studio 2022 with the **.NET desktop development** workload, or
- .NET 8 SDK

For OpenAI, an OpenAI API key is required.

For LM Studio, start the local API server and load a suitable chat or instruction model.

## Build and run from source

The project does not use a Visual Studio solution file. Build the `.csproj` file directly from the repository root:

```powershell
dotnet restore ResourceTranslator.csproj
dotnet build ResourceTranslator.csproj
dotnet run --project ResourceTranslator.csproj
```

To create a release build:

```powershell
dotnet publish ResourceTranslator.csproj -c Release
```

To create a self-contained Windows x64 build for users without an installed .NET runtime:

```powershell
dotnet publish ResourceTranslator.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true
```

The published files are written to a directory below:

```text
bin/Release/net8.0-windows/win-x64/publish/
```

The exact path can vary if the target framework or runtime settings in the project file are changed.

## Usage

1. Start Resource Translator.
2. Select `OpenAI` or `LM Studio` as the provider.
3. Enter an API key or token when required.
4. Select or enter a model.
5. Use `Load models` to retrieve the available models from the selected provider.
6. Select the source file.
7. Select the target language.
8. Click `Translate and save`.
9. Choose the output file.

## LM Studio

1. Open LM Studio and load a suitable instruction or chat model.
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
- Resource keys, property prefixes, placeholders, tags, and indentation are validated.
- A failed chunk is automatically retried up to three times.
- Original encoding, BOM, and line endings are preserved.
- The source file is not overwritten unless the same output path is explicitly selected.

## Settings and privacy

API keys and tokens are deliberately not saved to disk.

Other settings are stored in:

```text
%LOCALAPPDATA%\ResourceTranslator\settings.json
```

Before publishing the source code, make sure that no API keys, tokens, passwords, or private local paths are hard-coded in the project.
