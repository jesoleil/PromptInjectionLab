using PromptInjectionLab.Core;
using PromptInjectionLab.Core.Results;

namespace PromptInjectionLab.Tests;

public static class CsvRunRecordSinkTests
{
    private static string TempCsv() => Path.Combine(Path.GetTempPath(), $"pil_{Guid.NewGuid():N}.csv");

    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        yield return ("CsvSink: writes header once and appends on reopen", async () =>
        {
            var path = TempCsv();
            try
            {
                await using (var s = new CsvRunRecordSink(path))
                    await s.WriteAsync(new RunRecord { ItemId = "a", DocumentType = "pdf", PageCount = 2 });
                await using (var s = new CsvRunRecordSink(path))
                    await s.WriteAsync(new RunRecord { ItemId = "b" });

                var lines = File.ReadAllLines(path);
                Check.Equal(3, lines.Length);
                Check.Equal(Csv.HeaderLine<RunRecord>(), lines[0]);

                var back = Csv.ReadRecords<RunRecord>(File.ReadAllText(path));
                Check.Equal("a", back[0].ItemId);
                Check.Equal("pdf", back[0].DocumentType);
                Check.Equal(2, back[0].PageCount);
                Check.Null(back[1].PageCount);
            }
            finally { File.Delete(path); }
        });

        yield return ("CsvSink: refuses to append to a file with an older column layout", async () =>
        {
            var path = TempCsv();
            const string oldContent = "RunId,ItemId,LatencyMs\nr1,doc1,10\n";
            File.WriteAllText(path, oldContent);
            try
            {
                try
                {
                    await using var s = new CsvRunRecordSink(path);
                    throw new Exception("expected InvalidOperationException");
                }
                catch (InvalidOperationException ex)
                {
                    Check.Contains("different column layout", ex.Message);
                }
                Check.Equal(oldContent, File.ReadAllText(path)); // untouched
            }
            finally { File.Delete(path); }
        });

        yield return ("CsvSink: old-format results still load for metrics (missing columns default)", () =>
        {
            const string old = "RunId,ItemId,TrueCategoryId,PredictedCategoryId,ParseStatus,IsCorrect,LatencyMs\n" +
                               "r1,doc1,3,3,Valid,True,12\n";
            var recs = Csv.ReadRecords<RunRecord>(old);
            Check.Equal(1, recs.Count);
            Check.Equal(3, recs[0].PredictedCategoryId);
            Check.Equal("", recs[0].DocumentType);
            Check.Null(recs[0].PageCount);
            return Task.CompletedTask;
        });
    }
}
