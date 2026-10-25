using PromptInjectionLab.Core.Domain;

namespace PromptInjectionLab.Tests;

public static class CategoryRegistryTests
{
    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        yield return ("Registry: default has the five fixed categories in order", () =>
        {
            var names = new CategoryRegistry().All.Select(c => c.Name).ToArray();
            Check.Equal("HR,Finance,Technical,Legal,General", string.Join(",", names));
            return Task.CompletedTask;
        });

        yield return ("Registry: FindByName is case-insensitive", () =>
        {
            var r = new CategoryRegistry();
            Check.Equal(3, r.FindByName("technical")?.Id);
            Check.Equal(1, r.FindByName("hr")?.Id);
            Check.Equal("General", r.FindByName("GENERAL")?.Name);
            return Task.CompletedTask;
        });

        yield return ("Registry: FindByName rejects unknown / partial / null", () =>
        {
            var r = new CategoryRegistry();
            Check.Null(r.FindByName("Marketing"));
            Check.Null(r.FindByName("Tech"));
            Check.Null(r.FindByName(" Legal "));
            Check.Null(r.FindByName(null));
            return Task.CompletedTask;
        });

        yield return ("Registry: prompt list contains names only", () =>
        {
            var list = new CategoryRegistry().AsPromptList();
            Check.Equal("- HR\n- Finance\n- Technical\n- Legal\n- General", list);
            return Task.CompletedTask;
        });
    }
}
