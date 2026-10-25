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
    private readonly DocumentTextReader _reader;
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
        _prompt = prompt;
        _llm = llm;
        _parser = parser;
        _categories = categories;
        _lab = lab.Value;
        _reader = new DocumentTextReader(extractors, _lab.MaxFileSizeBytes);
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
            Provider = ctx.Provider,
            ApiVersion = ctx.ApiVersion,
            ExtraParameters = ctx.ExtraParameters,
            ExperimentId = ctx.ExperimentId,
            DatasetVersion = ctx.DatasetVersion,
            ManifestSha256 = ctx.ManifestSha256,
            IsCorrect = false,
        };

        try
        {
            ExtractedDocument doc;
            try
            {
                doc = await _reader.ReadAsync(item.Path, ct);
            }
            catch (DocumentExtractionException ex)
            {
                // Nothing is sent to the model when the document cannot be read.
                record.ParseStatus = ParseStatus.ExtractionError.ToString();
                record.ErrorKind = ex.Failure.ToString();
                record.ErrorMessage = ex.Message;
                record.DocumentType = DocumentTypeOf(item.Path);
                return record;
            }

            var raw = doc.Text;
            record.DocumentType = doc.DocumentType;
            record.PageCount = doc.PageCount;
            record.ExtractedTextLength = raw.Length;
            record.ContentSha256 = Sha256(raw);
            var content = ContentReducer.TruncateSafely(raw, _lab.MaxContentLength);
            record.ContentTruncated = _lab.MaxContentLength > 0 && raw.Length > _lab.MaxContentLength;

            var (system, user) = _prompt.Build(SystemInstructionVariant.Standard, _categories, content);
            record.PromptSha256 = Sha256(system + "\n" + user);

            var resp = await _llm.CompleteAsync(system, user, ct);
            record.RawModelOutput = resp.RawText;
            record.LlmLatencyMs = resp.LlmLatencyMs;
            record.Attempts = resp.Attempts;
            record.HttpStatus = resp.HttpStatus;
            record.ResponseModel = resp.ResponseModel;
            record.InputTokens = resp.InputTokens;
            record.OutputTokens = resp.OutputTokens;
            record.FinishReason = resp.FinishReason;

            if (!resp.Ok)
            {
                var timedOut = resp.TimedOut || resp.ErrorKind == LlmErrorKind.Timeout;
                record.ParseStatus = (timedOut ? ParseStatus.Timeout : ParseStatus.ApiError).ToString();
                record.ErrorKind = resp.ErrorKind.ToString();
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
            record.ErrorKind = "UnexpectedError";
            record.ErrorMessage = SecretRedactor.Redact(ex.Message);
        }
        finally
        {
            sw.Stop();
            record.LatencyMs = sw.ElapsedMilliseconds;
        }

        return record;
    }

    private static string DocumentTypeOf(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        return ext is "txt" or "pdf" ? ext : "";
    }

    private static string Sha256(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
