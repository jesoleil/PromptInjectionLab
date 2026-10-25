namespace PromptInjectionLab.Core.Metrics;

public sealed record ClassMetric(int CategoryId, int Support, double Precision, double Recall, double F1);

public sealed class MetricsReport
{
    public int Total { get; set; }
    public int Correct { get; set; }

    /// <summary>Records whose outcome was not a valid in-range prediction (counted against accuracy).</summary>
    public int Invalid { get; set; }

    public double Accuracy { get; set; }
    public double MacroF1 { get; set; }
    public double InvalidRate { get; set; }
    public double LatencyP50Ms { get; set; }
    public double LatencyP95Ms { get; set; }
    public List<ClassMetric> PerClass { get; set; } = new();
}
