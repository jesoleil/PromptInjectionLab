namespace PromptInjectionLab.Core.Extraction;

public interface ITextExtractor
{
    bool CanHandle(string path);
    Task<string> ExtractAsync(string path, CancellationToken ct = default);
}
