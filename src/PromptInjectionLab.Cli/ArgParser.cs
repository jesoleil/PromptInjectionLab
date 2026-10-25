namespace PromptInjectionLab.Cli;

/// <summary>Minimal "--key value" argument parser. A flag with no value becomes "true".</summary>
public static class ArgParser
{
    public static Dictionary<string, string> Parse(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) continue;
            var key = args[i][2..];
            var value = (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                ? args[++i]
                : "true";
            result[key] = value;
        }
        return result;
    }
}
