namespace PromptInjectionLab.Core.Results;

/// <summary>
/// Appends run records to a CSV file, flushing after each row so that an
/// interrupted run loses at most the record in flight. If the target file
/// already has content, the header is not rewritten (append mode).
/// Uses the dependency-free <see cref="Csv"/> helper.
/// </summary>
public sealed class CsvRunRecordSink : IRunRecordSink
{
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _headerWritten;

    public CsvRunRecordSink(string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _headerWritten = File.Exists(path) && new FileInfo(path).Length > 0;
        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read));
    }

    public async Task WriteAsync(RunRecord record, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (!_headerWritten)
            {
                await _writer.WriteLineAsync(Csv.HeaderLine<RunRecord>());
                _headerWritten = true;
            }
            await _writer.WriteLineAsync(Csv.FormatRecord(record));
            await _writer.FlushAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync();
        _lock.Dispose();
    }
}
