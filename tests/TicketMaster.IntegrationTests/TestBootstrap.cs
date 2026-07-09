using System.Runtime.CompilerServices;

namespace TicketMaster.IntegrationTests;

internal static class TestBootstrap
{
    /// <summary>
    /// Runs before any test in this assembly. Provides the key material so the host can decrypt the
    /// <c>secret:</c>-prefixed demo value in appsettings.Development.json at startup (matches the documented dev key).
    /// </summary>
    [ModuleInitializer]
    internal static void Init() =>
        Environment.SetEnvironmentVariable("CONFIG_SECRET_KEY", "development-secret-material-change-me");
}
