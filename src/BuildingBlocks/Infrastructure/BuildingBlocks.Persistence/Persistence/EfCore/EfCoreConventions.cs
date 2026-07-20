using System.Reflection;
using Microsoft.EntityFrameworkCore;

namespace BuildingBlocks.Persistence.EfCore;

/// <summary>
/// The persistence conventions every module DbContext opts into, so each one is one call instead of repeated
/// boilerplate: snake_case names + <b>no-tracking by default</b> at the options level, and Vogen converter +
/// null→empty string registration at the model level.
/// </summary>
public static class EfCoreConventions
{
    /// <summary>
    /// snake_case naming + <see cref="QueryTrackingBehavior.NoTracking"/> as the default. Reads are the common case in a
    /// web app and shouldn't pay for change tracking; a handler that loads an entity to mutate it opts back in with
    /// <c>.AsTracking()</c>. (Inserts via <c>Add</c> and EF Identity's explicit <c>Update</c>/<c>Attach</c> are
    /// unaffected — tracking behavior only governs entities returned by queries.)
    /// </summary>
    public static DbContextOptionsBuilder UseModuleConventions(this DbContextOptionsBuilder options) =>
        options
            .UseSnakeCaseNamingConvention()
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

    /// <summary>
    /// Model-level conventions: registers the reflection-free Vogen EF converters for the value objects in
    /// <paramref name="assembly"/> and maps <c>string</c> through <see cref="NullToEmptyStringConverter"/>.
    /// Call from a context's <c>ConfigureConventions</c> with <c>GetType().Assembly</c>.
    /// </summary>
    public static void ApplyAppConventions(this ModelConfigurationBuilder configurationBuilder, Assembly assembly)
    {
        configurationBuilder.RegisterVogenEfCoreConverters(assembly);
        configurationBuilder.Properties<string>().HaveConversion<NullToEmptyStringConverter>();
    }
}
