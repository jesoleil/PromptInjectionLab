namespace PromptInjectionLab.Core.Results;

/// <summary>
/// Appends run records to a CSV file, flushing after each row so that an
/// interrupted run loses at most the record in flight. If the target file
/// already has content, the header is not rewritten (append mode) — but only
/// when its header matches the current <see cref="RunRecord"/> columns; a file
/// written by an older prototype version is refused so that rows with
/// different column layouts are never mixed in one file.
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
        if (_headerWritten)
        {
            var existing = ReadFirstLine(path);
            var expected = Csv.HeaderLine<RunRecord>();
            if (!string.Equals(existing, expected, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"'{path}' already exists with a different column layout (written by another prototype version). " +
                    "Use a new --out file; existing results are left untouched.");
        }

        _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read));
    }

    private static string ReadFirstLine(string path)
    {
        using var reader = new StreamReader(path);
        return (reader.ReadLine() ?? "").TrimStart('﻿');
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
