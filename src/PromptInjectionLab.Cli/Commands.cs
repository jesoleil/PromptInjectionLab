using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core;
using PromptInjectionLab.Core.Config;
using PromptInjectionLab.Core.Domain;
using PromptInjectionLab.Core.Llm;
using PromptInjectionLab.Core.Metrics;
using PromptInjectionLab.Core.Pipeline;
using PromptInjectionLab.Core.Prompt;
using PromptInjectionLab.Core.Results;

namespace PromptInjectionLab.Cli;

public static class Commands
{
    public static int Help()
    {
        Console.WriteLine(
@"PromptInjectionLab — clean-document classification baseline (TXT + PDF)

Usage:
  check                                            Verify the selected provider is reachable and the model exists
                                                   (no text is generated; paid providers use a free metadata endpoint)
  run --data <manifest.csv> --out <results.csv>    Classify documents (Baseline) and log results
      [--provider Ollama|OpenAI|Anthropic|DeepSeek] [--model <id>]
      [--experiment <label>] [--dataset-version <label>]
      [--allow-paid]                               Required for any non-local (billed) provider
  metrics --in <results.csv>                       Compute accuracy / macro-F1 / invalid rate / latency

Supported document types: .txt, .pdf (text-based PDFs only; scanned PDFs need OCR, which is not supported).

Configuration (appsettings.json, appsettings.Local.json or env vars):
  Llm:Provider (default Ollama); <Provider>:Model, :Temperature, :MaxTokens, :Seed, :TimeoutSeconds, :MaxRetries
  API keys: OPENAI_API_KEY / ANTHROPIC_API_KEY / DEEPSEEK_API_KEY environment variables (never appsettings.json)
  Lab:MaxContentLength, Lab:MaxFileSizeBytes, Lab:ExperimentId, Lab:DatasetVersion");
        return 0;
    }

    public static async Task<int> CheckAsync(IServiceProvider sp)
    {
        var llm = sp.GetRequiredService<ILlmClient>();
        var info = llm.Info;

        Console.WriteLine($"Provider        : {info.Provider}{(info.IsPaid ? " (paid API)" : " (local)")}");
        Console.WriteLine($"Endpoint        : {info.Endpoint}");
        Console.WriteLine($"Configured model: {(string.IsNullOrWhiteSpace(info.Model) ? "(not set)" : info.Model)}");

        var h = await llm.CheckAsync();
        if (!h.Reachable)
        {
            Console.WriteLine($"[FAIL] Not reachable: {h.Error}");
            if (!info.IsPaid)
                Console.WriteLine("Hint: start the server with `ollama serve` and confirm the base URL.");
            return 1;
        }

        Console.WriteLine(info.IsPaid
            ? "[OK]   Reachable and authenticated."
            : "[OK]   Reachable. Installed models: " +
              (h.AvailableModels.Count == 0 ? "(none)" : string.Join(", ", h.AvailableModels)));

        if (string.IsNullOrWhiteSpace(info.Model))
        {
            Console.WriteLine($"[WARN] No model configured. Set {info.Provider}:Model (e.g. qwen3.5:4b for Ollama).");
            return 1;
        }
        if (!h.ModelAvailable)
        {
            Console.WriteLine(info.IsPaid
                ? $"[FAIL] Model '{info.Model}' is not available to this API key. {h.Error}"
                : $"[FAIL] Model '{info.Model}' is not installed. Run: ollama pull {info.Model}");
            return 1;
        }

        Console.WriteLine($"[OK]   Model '{info.Model}' is available.");
        return 0;
    }

    public static async Task<int> RunAsync(IServiceProvider sp, Dictionary<string, string> opts)
    {
        if (!opts.TryGetValue("data", out var manifestPath))
        {
            Console.Error.WriteLine("Missing --data <manifest.csv>");
            return 1;
        }
        var outPath = opts.TryGetValue("out", out var o2) ? o2 : "results.csv";

        if (!File.Exists(manifestPath))
        {
            Console.Error.WriteLine($"Manifest not found: {manifestPath}");
            return 1;
        }

        var categories = sp.GetRequiredService<CategoryRegistry>();
        var items = LoadManifest(manifestPath, categories);
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(manifestPath))!;

        var llm = sp.GetRequiredService<ILlmClient>(); // validates provider configuration
        if (llm.Info.IsPaid && !opts.ContainsKey("allow-paid"))
        {
            Console.Error.WriteLine(
                $"Provider '{llm.Info.Provider}' (model '{llm.Info.Model}') is a paid API: this run would make " +
                $"{items.Count} billed request(s). Re-run with --allow-paid to confirm. Nothing was sent.");
            return 2;
        }

        var lab = sp.GetRequiredService<IOptions<LabOptions>>().Value;
        var pipeline = sp.GetRequiredService<IClassificationPipeline>();
        var manifestSha = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(manifestPath))).ToLowerInvariant();
        var ctx = RunContext.From(llm.Info, lab, manifestSha256: manifestSha);

        Console.WriteLine($"Run {ctx.RunId} | provider={ctx.Provider} | model={ctx.ModelName} | items={items.Count} | out={outPath}");

        var started = DateTime.UtcNow;
        var statusCounts = new Dictionary<string, int>();
        await using (var sink = new CsvRunRecordSink(outPath))
        {
            int done = 0;
            foreach (var item in items)
            {
                if (!Path.IsPathRooted(item.Path))
                    item.Path = Path.Combine(baseDir, item.Path);

                var rec = await pipeline.RunAsync(item, ExperimentCondition.Baseline, ctx);
                await sink.WriteAsync(rec);
                done++;
                statusCounts[rec.ParseStatus] = statusCounts.GetValueOrDefault(rec.ParseStatus) + 1;

                var mark = rec.IsCorrect ? "ok" : rec.ParseStatus + (rec.ErrorKind is null ? "" : $"/{rec.ErrorKind}");
                var pred = rec.PredictedCategory.Length > 0 ? rec.PredictedCategory : "-";
                Console.WriteLine($"  [{done}/{items.Count}] {item.ItemId} true={rec.TrueCategory} pred={pred} ({mark}) {rec.LatencyMs}ms");
                if (rec.ErrorMessage is { Length: > 0 } && !rec.IsCorrect && rec.ParseStatus != "Valid")
                    Console.WriteLine($"         {rec.ErrorMessage}");
            }
            Console.WriteLine($"Done. Wrote {done} record(s) to {outPath}");
        }

        var (system, userTemplate) = sp.GetRequiredService<IPromptBuilder>()
            .Build(SystemInstructionVariant.Standard, categories, "{Content}");
        await RunManifestWriter.AppendAsync(outPath, RunManifestWriter.Build(ctx, lab, categories, system, userTemplate,
            Path.GetFullPath(manifestPath), items.Count, started, DateTime.UtcNow, statusCounts));
        Console.WriteLine($"Run settings appended to {RunManifestWriter.PathFor(outPath)}");
        return 0;
    }

    public static Task<int> MetricsAsync(IServiceProvider sp, Dictionary<string, string> opts)
    {
        if (!opts.TryGetValue("in", out var inPath))
        {
            Console.Error.WriteLine("Missing --in <results.csv>");
            return Task.FromResult(1);
        }
        if (!File.Exists(inPath))
        {
            Console.Error.WriteLine($"Results file not found: {inPath}");
            return Task.FromResult(1);
        }

        var records = Csv.ReadRecords<RunRecord>(File.ReadAllText(inPath));
        var r = MetricsCalculator.Compute(records);

        Console.WriteLine($"Records     : {r.Total}");
        Console.WriteLine($"Correct     : {r.Correct}");
        Console.WriteLine($"Invalid     : {r.Invalid}  ({r.InvalidRate:P1})");
        Console.WriteLine($"Accuracy    : {r.Accuracy:P2}");
        Console.WriteLine($"Macro-F1    : {r.MacroF1:F4}");
        Console.WriteLine($"Latency p50 : {r.LatencyP50Ms:F0} ms");
        Console.WriteLine($"Latency p95 : {r.LatencyP95Ms:F0} ms");
        var categories = sp.GetRequiredService<CategoryRegistry>();
        Console.WriteLine("Per class (category: support  P / R / F1):");
        foreach (var c in r.PerClass)
        {
            var name = categories.Find(c.CategoryId)?.Name ?? c.CategoryId.ToString();
            Console.WriteLine($"  {name}: n={c.Support}  P={c.Precision:F3} R={c.Recall:F3} F1={c.F1:F3}");
        }

        return Task.FromResult(0);
    }

    private static List<DatasetItem> LoadManifest(string path, CategoryRegistry categories)
    {
        var rows = Csv.ReadRecords<ManifestRow>(File.ReadAllText(path));
        var items = new List<DatasetItem>();
        for (int i = 0; i < rows.Count; i++)
        {
            var m = rows[i];
            var line = i + 2; // 1-based, after the header row
            var trueCategory = categories.FindByName(m.true_category?.Trim())
                ?? throw new InvalidOperationException(
                    $"Manifest line {line}: unknown true_category '{m.true_category}'. " +
                    $"Allowed: {string.Join(", ", categories.All.Select(c => c.Name))}");

            int? targetId = null;
            if (!string.IsNullOrWhiteSpace(m.target_category))
            {
                targetId = categories.FindByName(m.target_category.Trim())?.Id
                    ?? throw new InvalidOperationException(
                        $"Manifest line {line}: unknown target_category '{m.target_category}'.");
            }

            items.Add(new DatasetItem
            {
                ItemId = m.item_id,
                Path = m.path,
                TrueCategoryId = trueCategory.Id,
                Variant = string.IsNullOrWhiteSpace(m.variant) ? "clean" : m.variant,
                AttackTemplateId = string.IsNullOrWhiteSpace(m.attack_template_id) ? null : m.attack_template_id,
                TargetCategoryId = targetId,
            });
        }
        return items;
    }

    private sealed class ManifestRow
    {
        public string item_id { get; set; } = "";
        public string path { get; set; } = "";
        public string? true_category { get; set; }
        public string? variant { get; set; }
        public string? attack_template_id { get; set; }
        public string? target_category { get; set; }
    }
}
