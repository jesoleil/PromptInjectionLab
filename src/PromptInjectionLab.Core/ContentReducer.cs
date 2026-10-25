namespace PromptInjectionLab.Core;

/// <summary>
/// Safe truncation preserving document head and tail (~2/3 head + ~1/3 tail).
/// Mirrors the portal's content-reduction idea; standalone re-implementation.
/// </summary>
public static class ContentReducer
{
    public static string TruncateSafely(string content, int maxLength)
    {
        if (string.IsNullOrEmpty(content) || maxLength <= 0 || content.Length <= maxLength)
            return content ?? "";

        var headLen = (int)(maxLength * 2.0 / 3.0);
        var tailLen = maxLength - headLen;
        if (headLen <= 0) return content[..maxLength];
        var head = content[..headLen];
        var tail = content[^tailLen..];
        return head + "\n...\n" + tail;
    }
}
