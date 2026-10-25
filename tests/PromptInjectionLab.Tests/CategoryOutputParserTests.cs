using PromptInjectionLab.Core.Domain;
using PromptInjectionLab.Core.Parsing;

namespace PromptInjectionLab.Tests;

public static class CategoryOutputParserTests
{
    private static readonly CategoryOutputParser Parser = new();
    private static readonly CategoryRegistry Registry = new(); // HR=1, Finance=2, Technical=3, Legal=4, General=5

    private static void ExpectValid(string raw, string expectedName)
    {
        var (id, status) = Parser.Parse(raw, Registry);
        Check.Equal(ParseStatus.Valid, status, $"raw='{raw}'");
        Check.Equal(Registry.FindByName(expectedName)!.Id, id, $"raw='{raw}'");
    }

    private static void ExpectInvalid(string raw, ParseStatus expected)
    {
        var (id, status) = Parser.Parse(raw, Registry);
        Check.Equal(expected, status, $"raw='{raw}'");
        Check.Null(id, $"raw='{raw}'");
    }

    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        yield return ("Parser: 'Technical' -> Valid Technical", () => { ExpectValid("Technical", "Technical"); return Task.CompletedTask; });
        yield return ("Parser: 'technical' (case) -> Valid Technical", () => { ExpectValid("technical", "Technical"); return Task.CompletedTask; });
        yield return ("Parser: 'FINANCE' (case) -> Valid Finance", () => { ExpectValid("FINANCE", "Finance"); return Task.CompletedTask; });
        yield return ("Parser: '  HR \\n' (whitespace) -> Valid HR", () => { ExpectValid("  HR \n", "HR"); return Task.CompletedTask; });
        yield return ("Parser: 'Legal.' (trailing punct) -> Valid Legal", () => { ExpectValid("Legal.", "Legal"); return Task.CompletedTask; });
        yield return ("Parser: 'General!?' (trailing punct) -> Valid General", () => { ExpectValid("General!?", "General"); return Task.CompletedTask; });

        yield return ("Parser: '' -> Empty", () => { ExpectInvalid("", ParseStatus.Empty); return Task.CompletedTask; });
        yield return ("Parser: whitespace -> Empty", () => { ExpectInvalid("   ", ParseStatus.Empty); return Task.CompletedTask; });
        yield return ("Parser: '...' -> Unparseable", () => { ExpectInvalid("...", ParseStatus.Unparseable); return Task.CompletedTask; });

        yield return ("Parser: 'Marketing' (not allowed) -> OutOfRange", () => { ExpectInvalid("Marketing", ParseStatus.OutOfRange); return Task.CompletedTask; });
        yield return ("Parser: 'Tech' (partial) -> OutOfRange", () => { ExpectInvalid("Tech", ParseStatus.OutOfRange); return Task.CompletedTask; });
        yield return ("Parser: '3' (numeric id) -> OutOfRange", () => { ExpectInvalid("3", ParseStatus.OutOfRange); return Task.CompletedTask; });
        yield return ("Parser: '.Technical' (leading punct) -> OutOfRange", () => { ExpectInvalid(".Technical", ParseStatus.OutOfRange); return Task.CompletedTask; });

        yield return ("Parser: sentence containing a name -> Unparseable", () =>
        {
            ExpectInvalid("The category is Technical", ParseStatus.Unparseable);
            return Task.CompletedTask;
        });
        yield return ("Parser: two names -> Unparseable", () =>
        {
            ExpectInvalid("Finance Legal", ParseStatus.Unparseable);
            return Task.CompletedTask;
        });
    }
}
