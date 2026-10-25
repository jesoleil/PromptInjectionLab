namespace PromptInjectionLab.Core.Domain;

/// <summary>
/// Experiment conditions. Phase 1 implements <see cref="Baseline"/> only.
/// Attacked inputs and defense layers are added in later phases and are
/// deliberately NOT present yet so nothing untested is implied.
/// </summary>
public enum ExperimentCondition
{
    Baseline = 0,
}
