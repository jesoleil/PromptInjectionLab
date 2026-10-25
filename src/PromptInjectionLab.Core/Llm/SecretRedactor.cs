using System.Text.RegularExpressions;

namespace PromptInjectionLab.Core.Llm;

/// <summary>
/// Removes credentials from any text that may reach logs, the console or CSV
/// (error messages, response bodies). Providers sometimes echo a partially
/// masked key in 401 responses, so both the exact key and key-shaped tokens
/// are replaced.
/// </summary>
public static partial class SecretRedactor
{
    public const string Mask = "[REDACTED]";

    public static string Redact(string? text, params string?[] secrets)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var result = text;
        foreach (var s in secrets)
            if (!string.IsNullOrEmpty(s) && s.Length >= 8)
                result = result.Replace(s, Mask, StringComparison.Ordinal);
        return KeyShaped().Replace(result, Mask);
    }

    // sk-..., sk-proj-..., sk-ant-... style keys (also partially masked "sk-abc***xyz") and Bearer tokens.
    [GeneratedRegex(@"(sk-[A-Za-z0-9_\-\*\.]{6,})|((?<=Bearer\s)[A-Za-z0-9_\-\.]{8,})", RegexOptions.IgnoreCase)]
    private static partial Regex KeyShaped();
}
