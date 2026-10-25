using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptInjectionLab.Core;
using PromptInjectionLab.Core.Config;
using PromptInjectionLab.Core.Domain;
using PromptInjectionLab.Core.Llm;
using PromptInjectionLab.Core.Results;

namespace PromptInjectionLab.Tests;

/// <summary>DI/provider selection, redaction, backoff math, run manifest and repository secret hygiene.</summary>
public static class ProviderInfrastructureTests
{
    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        return new ServiceCollection().AddLogging().AddPromptInjectionLab(config).BuildServiceProvider();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PromptInjectionLab.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new Exception("Repository root (PromptInjectionLab.sln) not found.");
    }

    private static IEnumerable<string> ApiKeyValues(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.Object)
            foreach (var p in e.EnumerateObject())
            {
                if (p.Name.Equals("ApiKey", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String)
                    yield return p.Value.GetString() ?? "";
                foreach (var v in ApiKeyValues(p.Value)) yield return v;
            }
    }

    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        // ---- provider selection ----
        yield return ("Factory: default provider is local Ollama (not paid)", () =>
        {
            using var sp = Services(new() { ["Ollama:Model"] = "qwen3.5:4b" });
            var llm = sp.GetRequiredService<ILlmClient>();
            Check.True(llm is OllamaLlmClient);
            Check.False(llm.Info.IsPaid);
            Check.Equal("http://localhost:11434/api/chat", llm.Info.Endpoint);
            return Task.CompletedTask;
        });

        yield return ("Factory: provider name is case-insensitive (openai -> OpenAiLlmClient)", () =>
        {
            using var sp = Services(new()
            {
                ["Llm:Provider"] = "openai",
                ["OpenAI:Model"] = "gpt-test",
                ["OpenAI:ApiKey"] = "sk-test-1234567890",
            });
            Check.True(sp.GetRequiredService<ILlmClient>() is OpenAiLlmClient);
            return Task.CompletedTask;
        });

        yield return ("Factory: Anthropic and DeepSeek resolve with model + key", () =>
        {
            using (var sp = Services(new() { ["Llm:Provider"] = "Anthropic", ["Anthropic:Model"] = "claude-x", ["Anthropic:ApiKey"] = "sk-ant-test-123456" }))
                Check.True(sp.GetRequiredService<ILlmClient>() is AnthropicLlmClient);
            using (var sp = Services(new() { ["Llm:Provider"] = "DeepSeek", ["DeepSeek:Model"] = "deepseek-chat", ["DeepSeek:ApiKey"] = "sk-test-123456789" }))
                Check.True(sp.GetRequiredService<ILlmClient>() is DeepSeekLlmClient);
            return Task.CompletedTask;
        });

        yield return ("Factory: unknown provider -> LlmConfigurationException listing options", () =>
        {
            using var sp = Services(new() { ["Llm:Provider"] = "Gemini" });
            try
            {
                sp.GetRequiredService<ILlmClient>();
                throw new Exception("expected failure");
            }
            catch (LlmConfigurationException ex)
            {
                Check.Contains("Ollama, OpenAI, Anthropic, DeepSeek", ex.Message);
            }
            return Task.CompletedTask;
        });

        yield return ("Factory: paid provider with no key fails before any request", () =>
        {
            using var sp = Services(new()
            {
                ["Llm:Provider"] = "OpenAI",
                ["OpenAI:Model"] = "gpt-test",
                ["OpenAI:ApiKeyEnvironmentVariable"] = $"PIL_UNSET_{Guid.NewGuid():N}",
            });
            try
            {
                sp.GetRequiredService<ILlmClient>();
                throw new Exception("expected failure");
            }
            catch (LlmConfigurationException ex)
            {
                Check.Contains("API key is not set", ex.Message);
            }
            return Task.CompletedTask;
        });

        yield return ("Factory: JSON null Temperature binds to 'not sent'", () =>
        {
            using var sp = Services(new()
            {
                ["Llm:Provider"] = "OpenAI", ["OpenAI:Model"] = "gpt-test", ["OpenAI:ApiKey"] = "sk-test-1234567890",
                ["OpenAI:Temperature"] = null,
            });
            Check.Null(sp.GetRequiredService<ILlmClient>().Info.Temperature);
            return Task.CompletedTask;
        });

        // ---- redaction / backoff ----
        yield return ("Redactor: removes exact key, sk-* tokens and Bearer tokens", () =>
        {
            var text = "key=sk-proj-AbCdEf123456 masked=sk-abc***wxyz exact=SECRETVALUE99 auth=Bearer abcdef123456789";
            var r = SecretRedactor.Redact(text, "SECRETVALUE99");
            Check.DoesNotContain("AbCdEf123456", r);
            Check.DoesNotContain("sk-abc", r);
            Check.DoesNotContain("SECRETVALUE99", r);
            Check.DoesNotContain("abcdef123456789", r);
            Check.Contains("key=", r);
            return Task.CompletedTask;
        });

        yield return ("Backoff: exponential from base, capped by MaxRetryDelaySeconds", () =>
        {
            var o = new OpenAiOptions { RetryBaseDelayMs = 1000, MaxRetryDelaySeconds = 3 };
            Check.Equal(TimeSpan.FromSeconds(1), HttpLlmTransport.NextDelay(o, 1, null));
            Check.Equal(TimeSpan.FromSeconds(2), HttpLlmTransport.NextDelay(o, 2, null));
            Check.Equal(TimeSpan.FromSeconds(3), HttpLlmTransport.NextDelay(o, 3, null));
            Check.Equal(TimeSpan.FromSeconds(3), HttpLlmTransport.NextDelay(o, 1, TimeSpan.FromMinutes(5)));
            return Task.CompletedTask;
        });

        yield return ("Classify: status codes map to error kinds; only transient ones retry", () =>
        {
            Check.Equal(LlmErrorKind.AuthenticationFailed, HttpLlmTransport.Classify(System.Net.HttpStatusCode.Forbidden));
            Check.Equal(LlmErrorKind.RateLimited, HttpLlmTransport.Classify((System.Net.HttpStatusCode)429));
            Check.Equal(LlmErrorKind.ServerError, HttpLlmTransport.Classify((System.Net.HttpStatusCode)529));
            Check.Equal(LlmErrorKind.BadRequest, HttpLlmTransport.Classify((System.Net.HttpStatusCode)422));
            Check.True(HttpLlmTransport.IsRetryable(LlmErrorKind.RateLimited));
            Check.False(HttpLlmTransport.IsRetryable(LlmErrorKind.Timeout));
            Check.False(HttpLlmTransport.IsRetryable(LlmErrorKind.AuthenticationFailed));
            return Task.CompletedTask;
        });

        // ---- run context / manifest ----
        yield return ("RunContext: built from provider info + lab settings", () =>
        {
            var info = new LlmClientInfo("Anthropic", "claude-x", "https://api.anthropic.com/v1/messages", 0.0, 16, null, "2023-06-01", true);
            var ctx = RunContext.From(info, new LabOptions { ExperimentId = "exp1", DatasetVersion = "ds-v1" }, manifestSha256: "abc");
            Check.Equal("Anthropic", ctx.Provider);
            Check.Equal("claude-x", ctx.ModelName);
            Check.Equal("2023-06-01", ctx.ApiVersion);
            Check.Equal("exp1", ctx.ExperimentId);
            Check.Equal("ds-v1", ctx.DatasetVersion);
            Check.Equal("abc", ctx.ManifestSha256);
            return Task.CompletedTask;
        });

        yield return ("RunManifest: JSONL line has settings, null attack/defense, no secrets", async () =>
        {
            var csv = Path.Combine(Path.GetTempPath(), $"pil_{Guid.NewGuid():N}.csv");
            try
            {
                var info = new LlmClientInfo("OpenAI", "gpt-test", "https://api.openai.com/v1/chat/completions", null, 16, 7, "v1", true);
                var ctx = RunContext.From(info, new LabOptions(), manifestSha256: "m1");
                var entry = RunManifestWriter.Build(ctx, new LabOptions(), new CategoryRegistry(), "SYS", "TEMPLATE",
                    "manifest.csv", 5, DateTime.UtcNow, DateTime.UtcNow, new Dictionary<string, int> { ["Valid"] = 5 });
                await RunManifestWriter.AppendAsync(csv, entry);
                await RunManifestWriter.AppendAsync(csv, entry);

                var lines = File.ReadAllLines(RunManifestWriter.PathFor(csv));
                Check.Equal(2, lines.Length);
                using var doc = JsonDocument.Parse(lines[0]);
                var root = doc.RootElement;
                Check.Equal("gpt-test", root.GetProperty("Llm").GetProperty("Model").GetString());
                Check.Equal(7, root.GetProperty("Llm").GetProperty("Seed").GetInt32());
                Check.Equal(JsonValueKind.Null, root.GetProperty("Llm").GetProperty("Temperature").ValueKind);
                Check.Equal(JsonValueKind.Null, root.GetProperty("AttackScenario").ValueKind);
                Check.Equal(JsonValueKind.Null, root.GetProperty("Defense").ValueKind);
                Check.Equal(5, root.GetProperty("Categories").GetArrayLength());
                Check.DoesNotContain("ApiKey", lines[0]);
            }
            finally
            {
                File.Delete(RunManifestWriter.PathFor(csv));
            }
        });

        // ---- repository secret hygiene ----
        yield return ("Hygiene: .gitignore excludes local config, env files and results", () =>
        {
            var lines = File.ReadAllLines(Path.Combine(RepoRoot(), ".gitignore")).Select(l => l.Trim()).ToHashSet();
            foreach (var required in new[] { "appsettings.Local.json", ".env", "results*.csv", "results*.jsonl" })
                Check.True(lines.Contains(required), $".gitignore is missing '{required}'");
            return Task.CompletedTask;
        });

        yield return ("Hygiene: committed appsettings files contain no API key values", () =>
        {
            var cli = Path.Combine(RepoRoot(), "src", "PromptInjectionLab.Cli");
            foreach (var name in new[] { "appsettings.json", "appsettings.Local.example.json" })
            {
                var text = File.ReadAllText(Path.Combine(cli, name));
                using var doc = JsonDocument.Parse(text);
                foreach (var v in ApiKeyValues(doc.RootElement))
                    Check.Equal("", v, $"{name} has a non-empty ApiKey");
                Check.Equal(text, SecretRedactor.Redact(text), $"{name} contains a key-shaped token");
            }
            return Task.CompletedTask;
        });

        yield return ("Hygiene: default appsettings selects Ollama and no paid provider", () =>
        {
            var text = File.ReadAllText(Path.Combine(RepoRoot(), "src", "PromptInjectionLab.Cli", "appsettings.json"));
            using var doc = JsonDocument.Parse(text);
            Check.Equal("Ollama", doc.RootElement.GetProperty("Llm").GetProperty("Provider").GetString());
            return Task.CompletedTask;
        });
    }
}
