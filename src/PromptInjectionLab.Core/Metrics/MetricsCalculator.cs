using PromptInjectionLab.Core.Parsing;
using PromptInjectionLab.Core.Results;

namespace PromptInjectionLab.Core.Metrics;

/// <summary>
/// Computes clean-performance metrics from run records (Phase 1):
/// accuracy, macro-averaged F1, invalid rate and latency percentiles.
/// An invalid output (parse status != Valid) counts against accuracy and is
/// reported separately; it is never silently treated as correct.
/// </summary>
public static class MetricsCalculator
{
    public static MetricsReport Compute(IReadOnlyList<RunRecord> records)
    {
        var report = new MetricsReport { Total = records.Count };
        if (records.Count == 0) return report;

        var classIds = records.Select(r => r.TrueCategoryId).Distinct().OrderBy(x => x).ToList();
        var tp = classIds.ToDictionary(id => id, _ => 0);
        var fp = classIds.ToDictionary(id => id, _ => 0);
        var fn = classIds.ToDictionary(id => id, _ => 0);
        var support = classIds.ToDictionary(id => id, _ => 0);

        int correct = 0, invalid = 0;

        foreach (var r in records)
        {
            if (support.ContainsKey(r.TrueCategoryId)) support[r.TrueCategoryId]++;

            var validPrediction =
                string.Equals(r.ParseStatus, nameof(ParseStatus.Valid), StringComparison.Ordinal)
                && r.PredictedCategoryId.HasValue;

            if (!validPrediction)
            {
                invalid++;
                if (fn.ContainsKey(r.TrueCategoryId)) fn[r.TrueCategoryId]++;
                continue;
            }

            var pred = r.PredictedCategoryId!.Value;
            if (pred == r.TrueCategoryId)
            {
                correct++;
                if (tp.ContainsKey(pred)) tp[pred]++;
            }
            else
            {
                if (fp.ContainsKey(pred)) fp[pred]++;
                if (fn.ContainsKey(r.TrueCategoryId)) fn[r.TrueCategoryId]++;
            }
        }

        report.Correct = correct;
        report.Invalid = invalid;
        report.Accuracy = (double)correct / records.Count;
        report.InvalidRate = (double)invalid / records.Count;

        double f1Sum = 0;
        foreach (var id in classIds)
        {
            double p = (tp[id] + fp[id]) == 0 ? 0 : (double)tp[id] / (tp[id] + fp[id]);
            double rec = (tp[id] + fn[id]) == 0 ? 0 : (double)tp[id] / (tp[id] + fn[id]);
            double f1 = (p + rec) == 0 ? 0 : 2 * p * rec / (p + rec);
            f1Sum += f1;
            report.PerClass.Add(new ClassMetric(id, support[id], p, rec, f1));
        }
        report.MacroF1 = classIds.Count == 0 ? 0 : f1Sum / classIds.Count;

        var latencies = records.Select(r => (double)r.LatencyMs).OrderBy(x => x).ToList();
        report.LatencyP50Ms = Percentile(latencies, 50);
        report.LatencyP95Ms = Percentile(latencies, 95);
        return report;
    }

    private static double Percentile(List<double> sorted, double p)
    {
        if (sorted.Count == 0) return 0;
        if (sorted.Count == 1) return sorted[0];
        var rank = (p / 100.0) * (sorted.Count - 1);
        var lo = (int)Math.Floor(rank);
        var hi = (int)Math.Ceiling(rank);
        if (lo == hi) return sorted[lo];
        return sorted[lo] + (sorted[hi] - sorted[lo]) * (rank - lo);
    }
}
