using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PromptInjectionLab.Cli;
using PromptInjectionLab.Core;

// Anchor the content root to the binary's folder so appsettings.json (copied
// next to the executable) is found regardless of the current working directory.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

// Keep console output clean: only warnings/errors from the logging pipeline.
// (Command output itself is written directly, not through ILogger.)
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// Local overrides / secrets (git-ignored). Local Ollama needs no secret; API keys
// for OpenAI / Anthropic / DeepSeek go here or (preferred) in environment
// variables, never in appsettings.json. Keys are never printed or logged.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var options = ArgParser.Parse(args.Skip(1).ToArray());

// Convenience CLI overrides (highest precedence): --provider, --model, --experiment, --dataset-version.
var overrides = new Dictionary<string, string?>();
var provider = options.GetValueOrDefault("provider") ?? builder.Configuration["Llm:Provider"] ?? "Ollama";
var section = provider.ToLowerInvariant() switch
{
    "openai" => "OpenAI",
    "anthropic" => "Anthropic",
    "deepseek" => "DeepSeek",
    _ => "Ollama",
};
if (options.TryGetValue("provider", out var p)) overrides["Llm:Provider"] = p;
if (options.TryGetValue("model", out var m)) overrides[$"{section}:Model"] = m;
if (options.TryGetValue("experiment", out var e)) overrides["Lab:ExperimentId"] = e;
if (options.TryGetValue("dataset-version", out var dv)) overrides["Lab:DatasetVersion"] = dv;
builder.Configuration.AddInMemoryCollection(overrides);

builder.Services.AddPromptInjectionLab(builder.Configuration);
using var host = builder.Build();

try
{
    return command switch
    {
        "check" => await Commands.CheckAsync(host.Services),
        "run" => await Commands.RunAsync(host.Services, options),
        "metrics" => await Commands.MetricsAsync(host.Services, options),
        _ => Commands.Help(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}
