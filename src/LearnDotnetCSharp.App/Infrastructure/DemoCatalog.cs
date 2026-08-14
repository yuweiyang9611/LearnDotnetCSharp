using System.Reflection;

namespace LearnDotnetCSharp.Infrastructure;

public sealed class DemoCatalog
{
    private readonly IReadOnlyDictionary<string, IDemo> demosById;

    private DemoCatalog(IReadOnlyDictionary<string, IDemo> demosById)
    {
        this.demosById = demosById;
    }

    public IReadOnlyList<IDemo> All => demosById.Values
        .OrderBy(demo => demo.Metadata.Category, StringComparer.Ordinal)
        .ThenBy(demo => demo.Metadata.Id, StringComparer.Ordinal)
        .ToArray();

    public static DemoCatalog Discover(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var demos = assembly.DefinedTypes
            .Where(type => !type.IsAbstract && !type.IsInterface && typeof(IDemo).IsAssignableFrom(type))
            .Select(type => (IDemo?)Activator.CreateInstance(type.AsType())
                ?? throw new InvalidOperationException($"Could not create demo '{type.FullName}'."))
            .ToArray();

        var duplicate = demos
            .GroupBy(demo => demo.Metadata.Id, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate demo id '{duplicate.Key}'.");
        }

        return new DemoCatalog(demos.ToDictionary(
            demo => demo.Metadata.Id,
            StringComparer.OrdinalIgnoreCase));
    }

    public bool TryGet(string id, out IDemo? demo) => demosById.TryGetValue(id, out demo);
}
