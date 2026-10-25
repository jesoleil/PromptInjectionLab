namespace PromptInjectionLab.Core.Llm;

/// <summary>Provider-independent failure classes recorded in the ErrorKind column.</summary>
public enum LlmErrorKind
{
    None,
    Timeout,
    Cancelled,
    RateLimited,
    ServerError,
    AuthenticationFailed,
    NotFound,
    BadRequest,
    InvalidResponse,
    NetworkError,
}

/// <summary>
/// Result of one model call (after retries). The positional members are the
/// original contract; the init-only members carry provider metadata when the
/// provider reports it (null otherwise).
/// </summary>
public sealed record LlmResponse(bool Ok, string RawText, string? Error, long LlmLatencyMs, bool TimedOut = false)
{
    public LlmErrorKind ErrorKind { get; init; } = Ok ? LlmErrorKind.None : (TimedOut ? LlmErrorKind.Timeout : LlmErrorKind.ServerError);
    public int? HttpStatus { get; init; }

    /// <summary>Model identifier echoed back by the provider (may include a dated version).</summary>
    public string? ResponseModel { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }

    /// <summary>Provider stop reason, e.g. "stop", "length", "end_turn", "max_tokens".</summary>
    public string? FinishReason { get; init; }

    /// <summary>HTTP attempts made (1 = no retry).</summary>
    public int Attempts { get; init; } = 1;
}
