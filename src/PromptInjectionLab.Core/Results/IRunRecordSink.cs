namespace PromptInjectionLab.Core.Results;

public interface IRunRecordSink : IAsyncDisposable
{
    Task WriteAsync(RunRecord record, CancellationToken ct = default);
}
