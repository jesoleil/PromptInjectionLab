using PromptInjectionLab.Core.Domain;

namespace PromptInjectionLab.Core.Prompt;

public interface IPromptBuilder
{
    (string System, string User) Build(SystemInstructionVariant variant, CategoryRegistry categories, string content);
}
