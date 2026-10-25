using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PromptInjectionLab.Core.Config;
using PromptInjectionLab.Core.Domain;

namespace PromptInjectionLab.Core.Results;

/// <summary>
/// Appends one JSON line per run to "&lt;results&gt;.runs.jsonl" with every setting
/// needed to repeat the run: provider, exact model id, parameters, prompt
/// template version + hash, category set, content limits, dataset/manifest
/// identity and timing. Secrets (API keys) are never part of it.
/// </summary>
public static class RunManifestWriter
{
    public static string PathFor(string resultsCsvPath) => resultsCsvPath + ".runs.jsonl";

    public static async Task AppendAsync(string resultsCsvPath, object entry, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = false });
        await File.AppendAllTextAsync(PathFor(resultsCsvPath), json + Environment.NewLine, ct);
    }

    public static object Build(RunContext ctx, LabOptions lab, CategoryRegistry categories, string systemPrompt,
        string userTemplate, string manifestPath, int items, DateTime startedUtc, DateTime finishedUtc,
        IReadOnlyDictionary<string, int> statusCounts) => new
    {
        ctx.RunId,
        StartedUtc = startedUtc.ToString("o"),
        FinishedUtc = finishedUtc.ToString("o"),
        ctx.PrototypeVersion,
        ctx.ExperimentId,
        ctx.DatasetVersion,
        Manifest = manifestPath,
        ctx.ManifestSha256,
        Items = items,
        Llm = new
        {
            ctx.Provider,
            Model = ctx.ModelName,
            Endpoint = ctx.OllamaEndpoint,
            ctx.ApiVersion,
            ctx.Temperature,
            ctx.MaxTokens,
            ctx.Seed,
            ctx.ExtraParameters,
        },
        Prompt = new
        {
            ctx.PromptTemplateVersion,
            ctx.SystemInstructionVariant,
            SystemPromptSha256 = Sha256(systemPrompt),
            UserTemplateSha256 = Sha256(userTemplate),
        },
        Categories = categories.All.Select(c => new { c.Id, c.Name }),
        Content = new { lab.MaxContentLength, lab.MaxFileSizeBytes },
        AttackScenario = (string?)null,  // not implemented in this prototype version
        Defense = (string?)null,         // not implemented in this prototype version
        StatusCounts = statusCounts,
    };

    private static string Sha256(string s) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s))).ToLowerInvariant();
}
