using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;
using PromptInjectionLab.Core.Domain;
using PromptInjectionLab.Core.Extraction;
using PromptInjectionLab.Core.Llm;
using PromptInjectionLab.Core.Parsing;
using PromptInjectionLab.Core.Prompt;
using PromptInjectionLab.Core.Results;

namespace PromptInjectionLab.Core.Pipeline;

/// <summary>
/// Baseline classification flow for a single document:
/// extract text -> safe truncate -> build prompt -> call LLM -> parse -> record.
/// End-to-end latency covers all of these steps.
/// </summary>
public sealed class ClassificationPipeline : IClassificationPipeline
{
    private readonly IEnumerable<ITextExtractor> _extractors;
    private readonly IPromptBuilder _prompt;
    private readonly ILlmClient _llm;
    private readonly ICategoryOutputParser _parser;
    private readonly CategoryRegistry _categories;
    private readonly LabOptions _lab;

    public ClassificationPipeline(
        IEnumerable<ITextExtractor> extractors,
        IPromptBuilder prompt,
        ILlmClient llm,
        ICategoryOutputParser parser,
        CategoryRegistry categories,
        IOptions<LabOptions> lab)
    {
        _extractors = extractors;
        _prompt = prompt;
        _llm = llm;
        _parser = parser;
        _categories = categories;
        _lab = lab.Value;
    }

    public async Task<RunRecord> RunAsync(DatasetItem item, ExperimentCondition condition, RunContext ctx, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var record = new RunRecord
        {
            RunId = ctx.RunId,
            TimestampUtc = DateTime.UtcNow.ToString("o"),
            PrototypeVersion = ctx.PrototypeVersion,
            ItemId = item.ItemId,
            FilePath = item.Path,
            Variant = item.Variant,
            ExperimentCondition = condition.ToString(),
            AttackTemplateId = item.AttackTemplateId,
            TargetCategoryId = item.TargetCategoryId,
            TrueCategoryId = item.TrueCategoryId,
            TrueCategory = _categories.Find(item.TrueCategoryId)?.Name ?? "",
            ModelName = ctx.ModelName,
            Temperature = ctx.Temperature,
            MaxTokens = ctx.MaxTokens,
            Seed = ctx.Seed,
            SystemInstructionVariant = ctx.SystemInstructionVariant,
            PromptTemplateVersion = ctx.PromptTemplateVersion,
            OllamaEndpoint = ctx.OllamaEndpoint,
            IsCorrect = false,
        };

        try
        {
            var extractor = _extractors.FirstOrDefault(e => e.CanHandle(item.Path));
            if (extractor is null)
            {
                record.ParseStatus = ParseStatus.ExtractionError.ToString();
                record.ErrorMessage = $"No text extractor for extension '{Path.GetExtension(item.Path)}'";
                return record;
            }

            var raw = await extractor.ExtractAsync(item.Path, ct);
            record.ContentSha256 = Sha256(raw);
            var content = ContentReducer.TruncateSafely(raw, _lab.MaxContentLength);

            var (system, user) = _prompt.Build(SystemInstructionVariant.Standard, _categories, content);
            record.PromptSha256 = Sha256(system + "\n" + user);

            var resp = await _llm.CompleteAsync(system, user, ct);
            record.RawModelOutput = resp.RawText;

            if (!resp.Ok)
            {
                record.ParseStatus = (resp.TimedOut ? ParseStatus.Timeout : ParseStatus.ApiError).ToString();
                record.ErrorMessage = resp.Error;
                record.PredictedCategoryId = null;
            }
            else
            {
                var (pred, status) = _parser.Parse(resp.RawText, _categories);
                record.PredictedCategoryId = pred;
                record.PredictedCategory = status == ParseStatus.Valid && pred.HasValue
                    ? _categories.Find(pred.Value)?.Name ?? ""
                    : "";
                record.ParseStatus = status.ToString();
                record.IsCorrect = status == ParseStatus.Valid && pred == item.TrueCategoryId;
            }
        }
        catch (Exception ex)
        {
            record.ParseStatus = ParseStatus.ApiError.ToString();
            record.ErrorMessage = ex.Message;
        }
        finally
        {
            sw.Stop();
            record.LatencyMs = sw.ElapsedMilliseconds;
        }

        return record;
    }

    private static string Sha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
