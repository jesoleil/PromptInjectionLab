using PromptInjectionLab.Core.Domain;
using PromptInjectionLab.Core.Results;

namespace PromptInjectionLab.Core.Pipeline;

public interface IClassificationPipeline
{
    Task<RunRecord> RunAsync(DatasetItem item, ExperimentCondition condition, RunContext context, CancellationToken ct = default);
}
