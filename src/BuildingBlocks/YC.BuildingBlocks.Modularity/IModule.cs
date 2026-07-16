using System.Reflection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace YC.BuildingBlocks.Modularity;

/// <summary>
/// A self-contained feature module. Each module owns its service registration and endpoint mapping so the host does
/// not hand-wire them — the host registers modules by type (<c>AddModules(m =&gt; m.Add&lt;PlatformModule&gt;())</c>) and
/// this contract drives the rest. Adding a module means adding one <c>.Add&lt;T&gt;()</c> line, nothing more.
/// </summary>
public interface IModule
{
    /// <summary>Short module name, for diagnostics/logging (discovery is by type, not by this name).</summary>
    string Name { get; }

    /// <summary>The module assembly — scanned for MediatR handlers and FluentValidation validators.</summary>
    Assembly Assembly { get; }

    /// <summary>Register the module's services (DbContext, repositories, options, consumers, …).</summary>
    void Add(IServiceCollection services, IConfiguration configuration);

    /// <summary>Map the module's endpoints. The passed builder is the shared <c>/api</c> group.</summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
