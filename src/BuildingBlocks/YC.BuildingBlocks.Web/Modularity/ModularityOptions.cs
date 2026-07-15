namespace YC.BuildingBlocks.Web.Modularity;

/// <summary>
/// Configures how <c>AddModules</c> resolves a module short-name (e.g. <c>"Iam"</c>) to its assembly name.
/// Product-neutral: by default the host's own root namespace is inferred from the application name, so an app named
/// <c>Acme.*</c> discovers <c>Acme.Modules.{name}</c> with no configuration. Override <see cref="AssemblyNameFormat"/>
/// when the convention differs.
/// </summary>
public sealed class ModularityOptions
{
    /// <summary>
    /// Composite format string mapping a module short-name to its assembly name; <c>{0}</c> is the name passed to
    /// <c>AddModules</c>. Left <c>null</c> it is inferred as <c>"{RootNamespace}.Modules.{0}"</c> where
    /// <c>RootNamespace</c> is the first segment of the host application name (e.g. <c>TicketMaster.AppHost</c> ⇒
    /// <c>"TicketMaster.Modules.{0}"</c>).
    /// </summary>
    public string AssemblyNameFormat { get; set; }

    internal string ResolveAssemblyName(string moduleName) => string.Format(AssemblyNameFormat, moduleName);

    /// <summary>Default format from the host application name's first '.'-segment (robust under WebApplicationFactory).</summary>
    internal static string DefaultFormatFor(string applicationName)
    {
        if (string.IsNullOrEmpty(applicationName))
            return "{0}";

        var dot = applicationName.IndexOf('.');
        var root = dot < 0 ? applicationName : applicationName[..dot];
        return $"{root}.Modules.{{0}}";
    }
}
