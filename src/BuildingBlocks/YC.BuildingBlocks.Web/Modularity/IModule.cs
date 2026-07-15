using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace YC.BuildingBlocks.Web.Modularity;

/// <summary>
/// A self-contained feature module. Each module owns its service registration and endpoint mapping so the host does
/// not hand-wire them — <c>AddModules("Platform","Iam")</c> discovers implementations by assembly and drives this
/// contract. Adding a module then means adding its name to that list, nothing more.
/// </summary>
public interface IModule
{
    /// <summary>Short module name (maps to an assembly via <see cref="ModularityOptions.AssemblyNameFormat"/>, by default <c>{RootNamespace}.Modules.{Name}</c>).</summary>
    string Name { get; }

    /// <summary>The module assembly — scanned for MediatR handlers and FluentValidation validators.</summary>
    Assembly Assembly { get; }

    /// <summary>Register the module's services (DbContext, repositories, options, consumers, …).</summary>
    void Add(IServiceCollection services, IConfiguration configuration);

    /// <summary>Map the module's endpoints. The passed builder is the shared <c>/api</c> group.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
