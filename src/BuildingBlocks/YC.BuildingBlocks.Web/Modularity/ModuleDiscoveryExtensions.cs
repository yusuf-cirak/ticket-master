using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using YC.BuildingBlocks.Application;

namespace YC.BuildingBlocks.Web.Modularity;

/// <summary>Holds the discovered modules so endpoint mapping can iterate them after the app is built.</summary>
public sealed class ModuleRegistry
{
    private readonly List<IModule> _modules = [];
    public IReadOnlyList<IModule> Modules => _modules;
    internal void Add(IModule module) => _modules.Add(module);
}

public static class ModuleDiscoveryExtensions
{
    /// <summary>
    /// Loads each named module assembly, instantiates its <see cref="IModule"/>, runs its <c>Add</c>, and registers
    /// MediatR/validators across all module assemblies at once. The short-name → assembly-name mapping is product-neutral
    /// (see <see cref="ModularityOptions"/>): by default <c>{RootNamespace}.Modules.{name}</c> inferred from the entry
    /// assembly. Names are the only per-module wiring the host owns — everything else is discovered.
    /// </summary>
    public static WebApplicationBuilder AddModules(this WebApplicationBuilder builder, params string[] moduleNames)
        => builder.AddModules(configure: null, moduleNames);

    /// <summary>
    /// <inheritdoc cref="AddModules(WebApplicationBuilder, string[])"/> Overload accepting an
    /// <see cref="ModularityOptions"/> configurator to override the assembly-name convention.
    /// </summary>
    public static WebApplicationBuilder AddModules(this WebApplicationBuilder builder, Action<ModularityOptions> configure, params string[] moduleNames)
    {
        var options = new ModularityOptions
        {
            AssemblyNameFormat = ModularityOptions.DefaultFormatFor(builder.Environment.ApplicationName),
        };
        configure?.Invoke(options);

        var registry = new ModuleRegistry();
        var assemblies = new List<Assembly>();

        foreach (var name in moduleNames)
        {
            var assemblyName = options.ResolveAssemblyName(name);
            var assembly = Assembly.Load(assemblyName);
            var moduleType = Array.Find(assembly.GetTypes(),
                t => typeof(IModule).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
                ?? throw new InvalidOperationException(
                    $"Module assembly '{assemblyName}' contains no non-abstract IModule implementation.");

            var module = (IModule)Activator.CreateInstance(moduleType)!;
            module.Add(builder.Services, builder.Configuration);
            registry.Add(module);
            assemblies.Add(module.Assembly);
        }

        // One MediatR/FluentValidation registration spanning every module assembly.
        builder.Services.AddApplicationMessaging([.. assemblies]);
        builder.Services.AddSingleton(registry);
        return builder;
    }

    /// <summary>Maps every discovered module's endpoints into <paramref name="endpoints"/> (the shared <c>/api</c> group).</summary>
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider.GetRequiredService<ModuleRegistry>();
        foreach (var module in registry.Modules)
            module.MapEndpoints(endpoints);
        return endpoints;
    }
}
