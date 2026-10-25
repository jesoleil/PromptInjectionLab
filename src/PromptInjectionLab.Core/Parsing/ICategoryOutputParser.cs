using PromptInjectionLab.Core.Domain;

namespace PromptInjectionLab.Core.Parsing;

public interface ICategoryOutputParser
{
    (int? CategoryId, ParseStatus Status) Parse(string rawText, CategoryRegistry categories);
}
