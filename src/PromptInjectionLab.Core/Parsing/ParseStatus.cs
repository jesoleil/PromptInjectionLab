namespace PromptInjectionLab.Core.Parsing;

/// <summary>
/// Outcome of interpreting a model's raw output. Anything other than
/// <see cref="Valid"/> is an invalid result and is NEVER counted as correct.
/// </summary>
public enum ParseStatus
{
    Valid,
    Empty,
    Unparseable,
    OutOfRange,
    ApiError,
    Timeout,
    ExtractionError,
}
