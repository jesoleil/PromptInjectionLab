namespace PromptInjectionLab.Core.Extraction;

/// <summary>Why a document could not be turned into text. Recorded in the ErrorKind column.</summary>
public enum ExtractionFailure
{
    FileNotFound,
    UnsupportedExtension,
    FileTooLarge,
    EmptyDocument,
    CorruptDocument,
    PasswordProtected,

    /// <summary>The PDF parsed but contains no text layer (e.g. scanned images; OCR is not supported).</summary>
    NoExtractableText,
    ReadError,
}

/// <summary>
/// Expected, classified extraction failure. The pipeline records it as
/// ParseStatus.ExtractionError and never sends anything to the model.
/// </summary>
public sealed class DocumentExtractionException : Exception
{
    public DocumentExtractionException(ExtractionFailure failure, string message, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
    }

    public ExtractionFailure Failure { get; }
}
