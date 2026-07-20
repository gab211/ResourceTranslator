using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace ResourceTranslator;

internal sealed class OpenAiClient : IDisposable
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };

    public async Task<IReadOnlyList<string>> GetModelsAsync(ApiProvider provider, string baseUrl, string apiKey, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, Combine(baseUrl, "models"));
        AddAuthorization(request, apiKey);
        using var response = await _http.SendAsync(request, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, json, provider);

        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").EnumerateArray()
            .Select(x => x.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .Where(x => provider != ApiProvider.OpenAI || x.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<string> TranslateAsync(ApiProvider provider, string baseUrl, string apiKey, string model, string prompt, CancellationToken ct)
    {
        return provider == ApiProvider.LMStudio
            ? await TranslateWithChatCompletionsAsync(provider, baseUrl, apiKey, model, prompt, ct)
            : await TranslateWithResponsesAsync(provider, baseUrl, apiKey, model, prompt, ct);
    }

    private async Task<string> TranslateWithResponsesAsync(ApiProvider provider, string baseUrl, string apiKey, string model, string prompt, CancellationToken ct)
    {
        var payload = new
        {
            model,
            input = new object[]
            {
                new { role = "system", content = "You are a deterministic localization engine. Follow the user's format-preservation rules exactly." },
                new { role = "user", content = prompt }
            }
        };

        using var request = CreateJsonRequest(HttpMethod.Post, Combine(baseUrl, "responses"), payload, apiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, json, provider);
        return ExtractResponsesOutputText(json);
    }

    private async Task<string> TranslateWithChatCompletionsAsync(ApiProvider provider, string baseUrl, string apiKey, string model, string prompt, CancellationToken ct)
    {
        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = "You are a deterministic localization engine. Follow the user's format-preservation rules exactly." },
                new { role = "user", content = prompt }
            },
            temperature = 0,
            stream = false
        };

        using var request = CreateJsonRequest(HttpMethod.Post, Combine(baseUrl, "chat/completions"), payload, apiKey);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        var json = await response.Content.ReadAsStringAsync(ct);
        EnsureSuccess(response, json, provider);
        return ExtractChatCompletionText(json);
    }

    private static HttpRequestMessage CreateJsonRequest(HttpMethod method, string url, object payload, string apiKey)
    {
        var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        AddAuthorization(request, apiKey);
        return request;
    }

    private static void AddAuthorization(HttpRequestMessage request, string apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    private static string ExtractResponsesOutputText(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("output_text", out var direct) && direct.ValueKind == JsonValueKind.String)
            return direct.GetString() ?? string.Empty;

        if (document.RootElement.TryGetProperty("output", out var output))
        {
            foreach (var item in output.EnumerateArray())
            {
                if (!item.TryGetProperty("content", out var content)) continue;
                foreach (var part in content.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
                        return text.GetString() ?? string.Empty;
                }
            }
        }
        throw new InvalidOperationException("The API response did not contain text output.");
    }

    private static string ExtractChatCompletionText(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var first = choices[0];
            if (first.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
                return content.GetString() ?? string.Empty;
        }
        throw new InvalidOperationException("The LM Studio response did not contain message content.");
    }

    private static void EnsureSuccess(HttpResponseMessage response, string body, ApiProvider provider)
    {
        if (response.IsSuccessStatusCode) return;
        string message;
        try
        {
            using var doc = JsonDocument.Parse(body);
            message = doc.RootElement.GetProperty("error").GetProperty("message").GetString() ?? body;
        }
        catch { message = body; }
        throw new HttpRequestException($"{ProviderName(provider)} API error {(int)response.StatusCode}: {message}");
    }

    private static string ProviderName(ApiProvider provider) => provider == ApiProvider.LMStudio ? "LM Studio" : "OpenAI";
    private static string Combine(string baseUrl, string relative) => $"{baseUrl.TrimEnd('/')}/{relative}";
    public void Dispose() => _http.Dispose();
}
