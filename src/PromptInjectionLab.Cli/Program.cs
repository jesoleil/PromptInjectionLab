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

// Local overrides / secrets (git-ignored). For local Ollama no secret is needed;
// any API key for a remote OpenAI-compatible endpoint goes here or in env vars,
// never in appsettings.json.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false);

builder.Services.AddPromptInjectionLab(builder.Configuration);
using var host = builder.Build();

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
var options = ArgParser.Parse(args.Skip(1).ToArray());

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
