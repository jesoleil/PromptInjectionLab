using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
        services.Configure<OllamaOptions>(config.GetSection(OllamaOptions.SectionName));
        services.Configure<LabOptions>(config.GetSection(LabOptions.SectionName));

        services.AddSingleton(new CategoryRegistry());
        services.AddSingleton<IPromptBuilder>(_ => new PromptBuilder());
        services.AddSingleton<ICategoryOutputParser, CategoryOutputParser>();
        services.AddSingleton<ITextExtractor, TxtTextExtractor>();

        services.AddHttpClient<ILlmClient, OllamaLlmClient>((sp, c) =>
        {
            var o = sp.GetRequiredService<IOptions<OllamaOptions>>().Value;
            c.Timeout = TimeSpan.FromSeconds(o.TimeoutSeconds > 0 ? o.TimeoutSeconds : 120);
        });

        services.AddTransient<IClassificationPipeline, ClassificationPipeline>();
        return services;
    }
}
