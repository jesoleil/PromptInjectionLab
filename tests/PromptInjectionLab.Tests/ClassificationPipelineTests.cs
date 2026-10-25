using Microsoft.Extensions.Options;
using PromptInjectionLab.Core.Config;
using PromptInjectionLab.Core.Domain;
using PromptInjectionLab.Core.Extraction;
using PromptInjectionLab.Core.Llm;
using PromptInjectionLab.Core.Parsing;
using PromptInjectionLab.Core.Pipeline;
using PromptInjectionLab.Core.Prompt;
using PromptInjectionLab.Tests.Fakes;

namespace PromptInjectionLab.Tests;

public static class ClassificationPipelineTests
{
    private static ClassificationPipeline BuildPipeline(ILlmClient llm) => new(
        new ITextExtractor[] { new TxtTextExtractor() },
        new PromptBuilder(),
        llm,
        new CategoryOutputParser(),
        new CategoryRegistry(),
        Options.Create(new LabOptions()));

    private static DatasetItem Item(string path, int trueId) =>
        new() { ItemId = "t1", Path = path, TrueCategoryId = trueId, Variant = "clean" };

    private static RunContext Ctx() =>
        RunContext.From(new OllamaOptions { Model = "fake" }, new LabOptions());

    private static string WriteTemp(string content)
    {
        var p = Path.Combine(Path.GetTempPath(), $"pil_{Guid.NewGuid():N}.txt");
        File.WriteAllText(p, content);
        return p;
    }

    public static IEnumerable<(string, Func<Task>)> Tests()
    {
        yield return ("Pipeline: correct prediction is marked correct", async () =>
        {
            var file = WriteTemp("This is an invoice. Total due 1980.00. Net 30.");
            try
            {
                var pipeline = BuildPipeline(FakeLlmClient.Returning("Finance"));
                var rec = await pipeline.RunAsync(Item(file, 2), ExperimentCondition.Baseline, Ctx());
                Check.Equal(nameof(ParseStatus.Valid), rec.ParseStatus);
                Check.Equal(2, rec.PredictedCategoryId);
                Check.Equal("Finance", rec.TrueCategory);
                Check.Equal("Finance", rec.PredictedCategory);
                Check.Equal("Finance", rec.RawModelOutput);
                Check.True(rec.IsCorrect);
                Check.NotNullOrEmpty(rec.ContentSha256);
                Check.NotNullOrEmpty(rec.PromptSha256);
            }
            finally { File.Delete(file); }
        });

        yield return ("Pipeline: wrong prediction is not correct", async () =>
        {
            var file = WriteTemp("Invoice body text.");
            try
            {
                var pipeline = BuildPipeline(FakeLlmClient.Returning("Legal"));
                var rec = await pipeline.RunAsync(Item(file, 2), ExperimentCondition.Baseline, Ctx());
                Check.Equal(nameof(ParseStatus.Valid), rec.ParseStatus);
                Check.Equal(4, rec.PredictedCategoryId);
                Check.Equal("Finance", rec.TrueCategory);
                Check.Equal("Legal", rec.PredictedCategory);
                Check.False(rec.IsCorrect);
            }
            finally { File.Delete(file); }
        });

        yield return ("Pipeline: tolerated variant 'finance.' is correct", async () =>
        {
            var file = WriteTemp("Invoice body text.");
            try
            {
                var rec = await BuildPipeline(FakeLlmClient.Returning(" finance.\n"))
                    .RunAsync(Item(file, 2), ExperimentCondition.Baseline, Ctx());
                Check.Equal(nameof(ParseStatus.Valid), rec.ParseStatus);
                Check.Equal("Finance", rec.PredictedCategory);
                Check.Equal(" finance.\n", rec.RawModelOutput);
                Check.True(rec.IsCorrect);
            }
            finally { File.Delete(file); }
        });

        yield return ("Pipeline: out-of-list answer is NOT correct", async () =>
        {
            var file = WriteTemp("Summer campaign, shop now.");
            try
            {
                var rec = await BuildPipeline(FakeLlmClient.Returning("Marketing"))
                    .RunAsync(Item(file, 5), ExperimentCondition.Baseline, Ctx());
                Check.Equal(nameof(ParseStatus.OutOfRange), rec.ParseStatus);
                Check.Null(rec.PredictedCategoryId);
                Check.Equal("", rec.PredictedCategory);
                Check.Equal("General", rec.TrueCategory);
                Check.Equal("Marketing", rec.RawModelOutput);
                Check.False(rec.IsCorrect);
            }
            finally { File.Delete(file); }
        });

        yield return ("Pipeline: sentence answer is Unparseable and NOT correct", async () =>
        {
            var file = WriteTemp("Router manual.");
            try
            {
                var rec = await BuildPipeline(FakeLlmClient.Returning("The category is Technical"))
                    .RunAsync(Item(file, 3), ExperimentCondition.Baseline, Ctx());
                Check.Equal(nameof(ParseStatus.Unparseable), rec.ParseStatus);
                Check.Equal("", rec.PredictedCategory);
                Check.False(rec.IsCorrect);
            }
            finally { File.Delete(file); }
        });

        yield return ("Pipeline: API failure -> ApiError, not correct", async () =>
        {
            var file = WriteTemp("Body.");
            try
            {
                var llm = new FakeLlmClient((_, _) => new LlmResponse(false, "", "boom", 5));
                var rec = await BuildPipeline(llm).RunAsync(Item(file, 2), ExperimentCondition.Baseline, Ctx());
                Check.Equal(nameof(ParseStatus.ApiError), rec.ParseStatus);
                Check.Null(rec.PredictedCategoryId);
                Check.False(rec.IsCorrect);
                Check.Equal("boom", rec.ErrorMessage);
            }
            finally { File.Delete(file); }
        });

        yield return ("Pipeline: timeout -> Timeout status", async () =>
        {
            var file = WriteTemp("Body.");
            try
            {
                var llm = new FakeLlmClient((_, _) => new LlmResponse(false, "", "timeout", 5, TimedOut: true));
                var rec = await BuildPipeline(llm).RunAsync(Item(file, 2), ExperimentCondition.Baseline, Ctx());
                Check.Equal(nameof(ParseStatus.Timeout), rec.ParseStatus);
                Check.False(rec.IsCorrect);
            }
            finally { File.Delete(file); }
        });

        yield return ("Pipeline: unsupported extension -> ExtractionError", async () =>
        {
            var rec = await BuildPipeline(FakeLlmClient.Returning("Finance"))
                .RunAsync(Item("nonexistent.pdf", 2), ExperimentCondition.Baseline, Ctx());
            Check.Equal(nameof(ParseStatus.ExtractionError), rec.ParseStatus);
            Check.False(rec.IsCorrect);
        });
    }
}
