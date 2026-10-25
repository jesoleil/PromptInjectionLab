namespace PromptInjectionLab.Core.Results;

/// <summary>
/// One classification result row. The property order is the CSV column order.
/// These fields are sufficient to reproduce a single run: model, parameters,
/// seed, prompt version/hash and input hash are all captured.
/// </summary>
public sealed class RunRecord
{
    public string RunId { get; set; } = "";
    public string TimestampUtc { get; set; } = "";
    public string PrototypeVersion { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string ContentSha256 { get; set; } = "";
    public string Variant { get; set; } = "";
    public string ExperimentCondition { get; set; } = "";
    public string? AttackTemplateId { get; set; }
    public int? TargetCategoryId { get; set; }
    public int TrueCategoryId { get; set; }
    public string TrueCategory { get; set; } = "";
    public string ModelName { get; set; } = "";
    /// <summary>Empty when the provider default was used (parameter not sent).</summary>
    public double? Temperature { get; set; }
    public int MaxTokens { get; set; }
    public int? Seed { get; set; }
    public string SystemInstructionVariant { get; set; } = "";
    public string PromptTemplateVersion { get; set; } = "";
    public string PromptSha256 { get; set; } = "";
    public string OllamaEndpoint { get; set; } = "";
    public string RawModelOutput { get; set; } = "";
    public int? PredictedCategoryId { get; set; }

    /// <summary>Category name of a Valid prediction; empty for any invalid outcome.</summary>
    public string PredictedCategory { get; set; } = "";
    public string ParseStatus { get; set; } = "";
    public bool IsCorrect { get; set; }
    public long LatencyMs { get; set; }
    public string? ErrorMessage { get; set; }

    // ---- Document metadata (added with PDF support; appended to keep earlier column order) ----

    /// <summary>"txt" or "pdf"; empty when the file type is unsupported.</summary>
    public string DocumentType { get; set; } = "";

    /// <summary>Page count for PDFs; empty for plain text.</summary>
    public int? PageCount { get; set; }

    /// <summary>Characters extracted from the document before truncation.</summary>
    public int? ExtractedTextLength { get; set; }

    /// <summary>True when the text exceeded Lab:MaxContentLength and was head+tail truncated.</summary>
    public bool ContentTruncated { get; set; }

    /// <summary>
    /// Machine-readable failure class: an extraction failure (PasswordProtected,
    /// NoExtractableText, UnsupportedExtension, ...) or an LLM failure (Timeout,
    /// RateLimited, AuthenticationFailed, BadRequest, ...). Empty on success.
    /// </summary>
    public string? ErrorKind { get; set; }

    // ---- Provider / experiment metadata (multi-provider support; appended) ----

    /// <summary>Ollama | OpenAI | Anthropic | DeepSeek. OllamaEndpoint holds the selected provider's endpoint.</summary>
    public string Provider { get; set; } = "";
    public string? ApiVersion { get; set; }

    /// <summary>Provider-specific parameters that affect output (e.g. think=false, reasoning_effort=low).</summary>
    public string? ExtraParameters { get; set; }

    /// <summary>Model id echoed by the provider (may carry a dated snapshot, unlike the requested ModelName).</summary>
    public string? ResponseModel { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? FinishReason { get; set; }

    /// <summary>HTTP attempts for the model call (1 = no retry); empty when the model was not called.</summary>
    public int? Attempts { get; set; }
    public int? HttpStatus { get; set; }

    /// <summary>Model-call time only (including retries); LatencyMs is end-to-end.</summary>
    public long? LlmLatencyMs { get; set; }
    public string ExperimentId { get; set; } = "";
    public string DatasetVersion { get; set; } = "";
    public string ManifestSha256 { get; set; } = "";
}
