using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using BuildingBlocks.Application;

namespace BuildingBlocks.Modularity;

/// <summary>Holds the registered modules so endpoint mapping can iterate them after the app is built.</summary>
public sealed class ModuleRegistry
{
    private readonly List<IModule> _modules = [];
    public IReadOnlyList<IModule> Modules => _modules;
    internal void Add(IModule module) => _modules.Add(module);
}

/// <summary>Type-safe collection of modules to register; see <see cref="ModuleDiscoveryExtensions.AddModules"/>.</summary>
public interface IModuleCollection
{
    /// <summary>Registers <typeparamref name="TModule"/>. Compile-time checked — the host references the module project.</summary>
    IModuleCollection Add<TModule>() where TModule : IModule, new();
}

public static class ModuleDiscoveryExtensions
{
    /// <summary>
    /// Registers the given modules by type: instantiates each <see cref="IModule"/>, runs its <c>Add</c>, and registers
    /// FluentValidation validators across every module assembly in one pass. Type-safe — no assembly-name magic strings
    /// or reflection; the host references each module project directly. Mediator itself is registered separately in
    /// the host's <c>Program.cs</c> (see <c>AddMediator</c>) — its source generator can only run in the outermost project.
    /// </summary>
    /// <example><code>
    /// builder.AddModules(m => m
    ///     .Add&lt;PlatformModule&gt;()
    ///     .Add&lt;IamModule&gt;());
    /// </code></example>
    public static WebApplicationBuilder AddModules(this WebApplicationBuilder builder, Action<IModuleCollection> configure)
    {
        var collection = new ModuleCollection();
        configure(collection);

        var registry = new ModuleRegistry();
        var assemblies = new List<Assembly>(collection.Instances.Count);

        foreach (var module in collection.Instances)
        {
            module.Add(builder.Services, builder.Configuration);
            registry.Add(module);
            assemblies.Add(module.Assembly);
        }

        // One FluentValidation registration spanning every module assembly.
        builder.Services.AddApplicationValidation([.. assemblies]);
        builder.Services.AddSingleton(registry);
        return builder;
    }

    /// <summary>
    /// The registered <see cref="ModuleRegistry"/>, retrievable at build time (before the container is built) so host
    /// wiring can enumerate module instances — e.g. to filter those that contribute Kafka consumers. Requires
    /// <see cref="AddModules"/> to have run first.
    /// </summary>
    public static ModuleRegistry GetModuleRegistry(this IServiceCollection services) =>
        services.LastOrDefault(d => d.ServiceType == typeof(ModuleRegistry))?.ImplementationInstance as ModuleRegistry
            ?? throw new InvalidOperationException("AddModules must be called before GetModuleRegistry.");

    /// <summary>Maps every registered module's endpoints into <paramref name="endpoints"/> (the shared <c>/api</c> group).</summary>
    public static IEndpointRouteBuilder MapModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var registry = endpoints.ServiceProvider.GetRequiredService<ModuleRegistry>();
        foreach (var module in registry.Modules)
            module.MapEndpoints(endpoints);
        return endpoints;
    }

    private sealed class ModuleCollection : IModuleCollection
    {
        public List<IModule> Instances { get; } = [];

        public IModuleCollection Add<TModule>() where TModule : IModule, new()
        {
            Instances.Add(new TModule());
            return this;
        }
    }
}
