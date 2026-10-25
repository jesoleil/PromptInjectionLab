using PromptInjectionLab.Core.Domain;
using PromptInjectionLab.Core.Prompt;

namespace PromptInjectionLab.Tests;

public static class PromptBuilderTests
{
    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        yield return ("PromptBuilder: substitutes content and category names", () =>
        {
            var builder = new PromptBuilder(); // embedded template
            var registry = new CategoryRegistry();
            var (system, user) = builder.Build(SystemInstructionVariant.Standard, registry, "HELLO-DOC");

            Check.Contains("HELLO-DOC", user);
            foreach (var name in new[] { "HR", "Finance", "Technical", "Legal", "General" })
                Check.Contains($"- {name}", user);
            Check.DoesNotContain("1 =", user);
            Check.DoesNotContain("ID number", user);
            Check.DoesNotContain("{Content}", user);
            Check.DoesNotContain("{Categories}", user);
            Check.Contains("ONLY the category name", system);
            return Task.CompletedTask;
        });
    }
}
