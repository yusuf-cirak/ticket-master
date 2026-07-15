using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace YC.BuildingBlocks.Persistence.EfCore;

public static class VogenEfCoreExtensions
{
    /// <summary>
    /// Registers the EF value converter that Vogen source-generates for every value object in the given assemblies —
    /// so ids map to their underlying column type with no per-id configuration. The <em>conversion</em> is the
    /// generated (reflection-free) converter; only this one-time startup <em>registration</em> scans the assembly.
    /// Call from <c>ConfigureConventions</c>.
    /// </summary>
    public static void RegisterVogenEfCoreConverters(this ModelConfigurationBuilder builder, params Assembly[] assemblies)
    {
        foreach (var valueObject in assemblies.SelectMany(GetLoadableTypes))
        {
            var converter = valueObject.GetNestedType("EfCoreValueConverter");
            if (converter is null)
                continue; // not a Vogen value object with an EF converter

            var comparer = valueObject.GetNestedType("EfCoreValueComparer");
            var properties = builder.Properties(valueObject);
            if (comparer is null)
                properties.HaveConversion(converter);
            else
                properties.HaveConversion(converter, comparer);
        }
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t is not null);
        }
    }
}
