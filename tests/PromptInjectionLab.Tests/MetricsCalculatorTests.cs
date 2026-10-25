using PromptInjectionLab.Core.Metrics;
using PromptInjectionLab.Core.Parsing;
using PromptInjectionLab.Core.Results;

namespace PromptInjectionLab.Tests;

public static class MetricsCalculatorTests
{
    private static RunRecord Rec(int trueId, int? pred, ParseStatus status, long latency = 10) => new()
    {
        TrueCategoryId = trueId,
        PredictedCategoryId = pred,
        ParseStatus = status.ToString(),
        IsCorrect = status == ParseStatus.Valid && pred == trueId,
        LatencyMs = latency,
    };

    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        yield return ("Metrics: accuracy/invalid/macro-F1 on known set", () =>
        {
            var records = new List<RunRecord>
            {
                Rec(1, 1, ParseStatus.Valid),    // correct
                Rec(1, 2, ParseStatus.Valid),    // wrong
                Rec(2, 2, ParseStatus.Valid),    // correct
                Rec(2, null, ParseStatus.Empty), // invalid
            };
            var r = MetricsCalculator.Compute(records);
            Check.Equal(4, r.Total);
            Check.Equal(2, r.Correct);
            Check.Equal(1, r.Invalid);
            Check.AlmostEqual(0.50, r.Accuracy, 1e-6);
            Check.AlmostEqual(0.25, r.InvalidRate, 1e-6);
            // class1 F1=0.6667, class2 F1=0.5 -> macro 0.5833
            Check.AlmostEqual(0.58333, r.MacroF1, 1e-4);
            return Task.CompletedTask;
        });

        yield return ("Metrics: OutOfRange equal to true id is NOT correct", () =>
        {
            var r = MetricsCalculator.Compute(new List<RunRecord> { Rec(3, 3, ParseStatus.OutOfRange) });
            Check.Equal(0, r.Correct);
            Check.Equal(1, r.Invalid);
            Check.AlmostEqual(0.0, r.Accuracy, 1e-6);
            return Task.CompletedTask;
        });

        yield return ("Metrics: empty set -> zeroes", () =>
        {
            var r = MetricsCalculator.Compute(new List<RunRecord>());
            Check.Equal(0, r.Total);
            Check.AlmostEqual(0.0, r.Accuracy, 1e-6);
            Check.AlmostEqual(0.0, r.MacroF1, 1e-6);
            return Task.CompletedTask;
        });

        yield return ("Metrics: latency percentiles", () =>
        {
            var records = new List<RunRecord>
            {
                Rec(1, 1, ParseStatus.Valid, 10),
                Rec(1, 1, ParseStatus.Valid, 20),
                Rec(1, 1, ParseStatus.Valid, 30),
            };
            var r = MetricsCalculator.Compute(records);
            Check.AlmostEqual(20, r.LatencyP50Ms, 1e-6);
            Check.True(r.LatencyP95Ms >= 20 && r.LatencyP95Ms <= 30);
            return Task.CompletedTask;
        });
    }
}
