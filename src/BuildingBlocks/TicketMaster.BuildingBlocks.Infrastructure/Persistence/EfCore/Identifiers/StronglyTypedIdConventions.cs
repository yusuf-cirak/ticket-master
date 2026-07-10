using System.Reflection;
using Microsoft.EntityFrameworkCore;
using TicketMaster.BuildingBlocks.Domain.Identifiers;

namespace TicketMaster.BuildingBlocks.Infrastructure.Persistence.EfCore.Identifiers;

public static class StronglyTypedIdConventions
{
    /// <summary>
    /// Registers the value converter for every concrete strongly-typed id (<see cref="GuidId"/>/<see cref="StringId"/>/
    /// <see cref="IntId"/>) found in the given assemblies, so a DbContext maps them to their underlying column type
    /// without per-property configuration. Call from <c>ConfigureConventions</c>.
    /// </summary>
    public static void ApplyStronglyTypedIds(this ModelConfigurationBuilder builder, params Assembly[] assemblies)
    {
        foreach (var idType in assemblies.SelectMany(GetLoadableTypes).Where(t => t is { IsAbstract: false, IsClass: true }))
        {
            var converter = ConverterFor(idType);
            if (converter is not null)
                builder.Properties(idType).HaveConversion(converter);
        }
    }

    private static Type ConverterFor(Type idType)
    {
        if (typeof(GuidId).IsAssignableFrom(idType))
            return typeof(GuidIdConverter<>).MakeGenericType(idType);
        if (typeof(StringId).IsAssignableFrom(idType))
            return typeof(StringIdConverter<>).MakeGenericType(idType);
        if (typeof(IntId).IsAssignableFrom(idType))
            return typeof(IntIdConverter<>).MakeGenericType(idType);
        return null;
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
