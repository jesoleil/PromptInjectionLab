namespace PromptInjectionLab.Core.Extraction;

/// <summary>
/// Text extracted from a document plus metadata worth recording with each run.
/// The text is untrusted data: it is only ever placed inside the prompt's
/// document block and is never interpreted or executed by the prototype.
/// </summary>
/// <param name="DocumentType">Lower-case type label, e.g. "txt" or "pdf".</param>
/// <param name="PageCount">Number of pages for paged formats (PDF); null for plain text.</param>
public sealed record ExtractedDocument(string Text, string DocumentType, int? PageCount = null);
