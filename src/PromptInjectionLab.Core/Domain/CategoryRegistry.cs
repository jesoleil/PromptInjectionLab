namespace PromptInjectionLab.Core.Domain;

/// <summary>
/// Fixed, in-memory category list. Intentionally decoupled from any database.
/// Numeric IDs are internal (metrics); the model, manifest and results use names.
/// </summary>
public sealed class CategoryRegistry
{
    private readonly Dictionary<int, Category> _byId;
    private readonly Dictionary<string, Category> _byName;

    public CategoryRegistry(IEnumerable<Category>? categories = null)
    {
        var list = (categories ?? Default).ToList();
        if (list.Count == 0)
            throw new ArgumentException("Category list cannot be empty.", nameof(categories));
        _byId = list.ToDictionary(c => c.Id);
        _byName = list.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);
        All = list;
    }

    public IReadOnlyList<Category> All { get; }

    public static IReadOnlyList<Category> Default { get; } = new List<Category>
    {
        new(1, "HR"),
        new(2, "Finance"),
        new(3, "Technical"),
        new(4, "Legal"),
        new(5, "General"),
    };

    public bool IsValidId(int id) => _byId.ContainsKey(id);

    public Category? Find(int id) => _byId.TryGetValue(id, out var c) ? c : null;

    /// <summary>Case-insensitive exact name lookup (no trimming or fuzzy matching).</summary>
    public Category? FindByName(string? name) =>
        name is not null && _byName.TryGetValue(name, out var c) ? c : null;

    /// <summary>Renders the category names (no IDs) for injection into the prompt template.</summary>
    public string AsPromptList() => string.Join("\n", All.Select(c => $"- {c.Name}"));
}
