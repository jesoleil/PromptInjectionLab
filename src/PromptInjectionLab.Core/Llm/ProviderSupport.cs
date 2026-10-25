using System.Text.Json;
using PromptInjectionLab.Core.Config;

namespace PromptInjectionLab.Core.Llm;

/// <summary>Small helpers shared by the adapters: key resolution, validation and JSON reading.</summary>
public static class ProviderSupport
{
    /// <summary>
    /// ApiKey from configuration (appsettings.Local.json / "&lt;Section&gt;__ApiKey"),
    /// otherwise the environment variable named in ApiKeyEnvironmentVariable.
    /// </summary>
    public static string? ResolveApiKey(ProviderOptions o, Func<string, string?>? env = null)
    {
        if (!string.IsNullOrWhiteSpace(o.ApiKey)) return o.ApiKey.Trim();
        if (string.IsNullOrWhiteSpace(o.ApiKeyEnvironmentVariable)) return null;
        var v = (env ?? Environment.GetEnvironmentVariable)(o.ApiKeyEnvironmentVariable);
        return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
    }

    public static string RequireApiKey(ProviderOptions o, string provider, string section, Func<string, string?>? env = null) =>
        ResolveApiKey(o, env) ?? throw new LlmConfigurationException(
            $"{provider} API key is not set. Set the {o.ApiKeyEnvironmentVariable} environment variable " +
            $"or {section}:ApiKey in appsettings.Local.json (git-ignored). Never put keys in appsettings.json.");

    public static void RequireModel(ProviderOptions o, string section)
    {
        if (string.IsNullOrWhiteSpace(o.Model))
            throw new LlmConfigurationException(
                $"{section}:Model is empty. Set the exact model id (e.g. via --model or appsettings).");
    }

    public static void RequireCommon(ProviderOptions o, string section, double maxTemperature)
    {
        RequireModel(o, section);
        if (o.MaxTokens < 1)
            throw new LlmConfigurationException($"{section}:MaxTokens must be >= 1.");
        if (o.Temperature is { } t && (t < 0 || t > maxTemperature))
            throw new LlmConfigurationException($"{section}:Temperature must be between 0 and {maxTemperature} (got {t}).");
        if (o.MaxRetries < 0)
            throw new LlmConfigurationException($"{section}:MaxRetries must be >= 0.");
        if (!Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _))
            throw new LlmConfigurationException($"{section}:BaseUrl is not an absolute URL.");
    }

    public static void RejectSeed(ProviderOptions o, string provider, string section)
    {
        if (o.Seed is not null)
            throw new LlmConfigurationException(
                $"{provider} does not support a seed parameter. Set {section}:Seed to null " +
                "(it is not silently ignored so results are not mislabelled as seeded).");
    }

    public static LlmResponse Failure(HttpLlmTransport.Result r) =>
        new(false, "", r.Error, r.ElapsedMs, TimedOut: r.Kind == LlmErrorKind.Timeout)
        {
            ErrorKind = r.Kind,
            HttpStatus = r.Status,
            Attempts = r.Attempts,
        };

    public static LlmResponse InvalidResponse(HttpLlmTransport.Result r, string why) =>
        new(false, "", $"Invalid response: {why}", r.ElapsedMs)
        {
            ErrorKind = LlmErrorKind.InvalidResponse,
            HttpStatus = r.Status,
            Attempts = r.Attempts,
        };

    public static string? Str(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    public static int? Int(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number &&
        v.TryGetInt32(out var i) ? i : null;

    public static JsonElement Obj(this JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) ? v : default;

    /// <summary>Parses an OpenAI-style chat.completion body (also used by DeepSeek).</summary>
    public static LlmResponse ParseChatCompletion(HttpLlmTransport.Result r)
    {
        try
        {
            using var doc = JsonDocument.Parse(r.Body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return InvalidResponse(r, "body is not a JSON object");

            string text = "";
            string? finish = null;
            if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                var msg = first.Obj("message");
                // A refusal is still model output: keep it so the parser marks it invalid, never correct.
                text = msg.Str("content") ?? msg.Str("refusal") ?? "";
                finish = first.Str("finish_reason");
            }

            var usage = root.Obj("usage");
            return new LlmResponse(true, text, null, r.ElapsedMs)
            {
                HttpStatus = r.Status,
                Attempts = r.Attempts,
                ResponseModel = root.Str("model"),
                InputTokens = usage.Int("prompt_tokens"),
                OutputTokens = usage.Int("completion_tokens"),
                FinishReason = finish,
            };
        }
        catch (JsonException ex)
        {
            return InvalidResponse(r, ex.Message);
        }
    }
}
