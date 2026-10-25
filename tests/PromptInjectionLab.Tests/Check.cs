namespace PromptInjectionLab.Tests;

/// <summary>Minimal assertion helpers used by the built-in test runner.</summary>
public static class Check
{
    public static void Equal<T>(T expected, T actual, string? msg = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Equal failed: expected [{expected}], got [{actual}]. {msg}");
    }

    public static void AlmostEqual(double expected, double actual, double tol, string? msg = null)
    {
        if (Math.Abs(expected - actual) > tol)
            throw new Exception($"AlmostEqual failed: expected {expected}±{tol}, got {actual}. {msg}");
    }

    public static void True(bool condition, string? msg = null)
    {
        if (!condition) throw new Exception($"True failed. {msg}");
    }

    public static void False(bool condition, string? msg = null)
    {
        if (condition) throw new Exception($"False failed. {msg}");
    }

    public static void Null(object? o, string? msg = null)
    {
        if (o is not null) throw new Exception($"Null failed: got [{o}]. {msg}");
    }

    public static void NotNullOrEmpty(string? s, string? msg = null)
    {
        if (string.IsNullOrEmpty(s)) throw new Exception($"NotNullOrEmpty failed. {msg}");
    }

    public static void Contains(string sub, string s)
    {
        if (s is null || !s.Contains(sub)) throw new Exception($"Contains failed: '{sub}' not found.");
    }

    public static void DoesNotContain(string sub, string s)
    {
        if (s is not null && s.Contains(sub)) throw new Exception($"DoesNotContain failed: '{sub}' present.");
    }
}
