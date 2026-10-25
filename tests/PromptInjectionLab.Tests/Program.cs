using PromptInjectionLab.Tests;

var tests = new List<(string Name, Func<Task> Run)>();
tests.AddRange(CategoryRegistryTests.Tests());
tests.AddRange(CategoryOutputParserTests.Tests());
tests.AddRange(MetricsCalculatorTests.Tests());
tests.AddRange(PromptBuilderTests.Tests());
tests.AddRange(DocumentExtractionTests.Tests());
tests.AddRange(CsvRunRecordSinkTests.Tests());
tests.AddRange(OllamaLlmClientTests.Tests());
tests.AddRange(ProviderAdapterTests.Tests());
tests.AddRange(ProviderInfrastructureTests.Tests());
tests.AddRange(ClassificationPipelineTests.Tests());

int passed = 0, failed = 0;
foreach (var (name, run) in tests)
{
    try
    {
        await run();
        passed++;
        Console.WriteLine($"PASS  {name}");
    }
    catch (Exception ex)
    {
        failed++;
        Console.WriteLine($"FAIL  {name}: {ex.Message}");
    }
}

Console.WriteLine();
Console.WriteLine($"{passed} passed, {failed} failed, {tests.Count} total");
return failed == 0 ? 0 : 1;
