using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PromptInjectionLab.Core.Config;
using PromptInjectionLab.Core.Domain;
using PromptInjectionLab.Core.Extraction;
using PromptInjectionLab.Core.Llm;
using PromptInjectionLab.Core.Parsing;
using PromptInjectionLab.Core.Pipeline;
using PromptInjectionLab.Core.Prompt;

namespace PromptInjectionLab.Core;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPromptInjectionLab(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<LlmOptions>(config.GetSection(LlmOptions.SectionName));
        services.Configure<OllamaOptions>(config.GetSection(OllamaOptions.SectionName));
        services.Configure<OpenAiOptions>(config.GetSection(OpenAiOptions.SectionName));
        services.Configure<AnthropicOptions>(config.GetSection(AnthropicOptions.SectionName));
        services.Configure<DeepSeekOptions>(config.GetSection(DeepSeekOptions.SectionName));
        services.Configure<LabOptions>(config.GetSection(LabOptions.SectionName));

        services.AddSingleton(new CategoryRegistry());
        services.AddSingleton<IPromptBuilder>(_ => new PromptBuilder());
        services.AddSingleton<ICategoryOutputParser, CategoryOutputParser>();
        services.AddSingleton<ITextExtractor, TxtTextExtractor>();
        services.AddSingleton<ITextExtractor, PdfTextExtractor>();

        // Timeouts are enforced per attempt by HttpLlmTransport (so retries each get
        // the full budget); the HttpClient itself must not cut requests short.
        services.AddHttpClient(LlmClientFactory.HttpClientName, c => c.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<ILlmClient>(LlmClientFactory.Create);

        services.AddTransient<IClassificationPipeline, ClassificationPipeline>();
        return services;
    }
}
