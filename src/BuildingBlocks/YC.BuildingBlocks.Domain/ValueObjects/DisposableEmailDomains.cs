using System.Collections.Frozen;
using System.IO;
using System.Reflection;

namespace YC.BuildingBlocks.Domain.ValueObjects;

/// <summary>
/// The disposable/throwaway email-domain blocklist, loaded once from the embedded
/// <c>DisposableEmailDomains.txt</c> resource into a <see cref="FrozenSet{T}"/> for allocation-free O(1) lookups.
/// The list is data, not code, so it can grow (or be regenerated from the community list via CI) without touching
/// the <see cref="Email"/> validation logic.
/// </summary>
internal static class DisposableEmailDomains
{
    private static readonly FrozenSet<string> Domains = Load();

    public static bool Contains(string domain) => Domains.Contains(domain);

    /// <summary>Number of blocked domains loaded — exposed for a sanity/regression test.</summary>
    public static int Count => Domains.Count;

    private static FrozenSet<string> Load()
    {
        var assembly = typeof(DisposableEmailDomains).Assembly;
        var resourceName = Array.Find(assembly.GetManifestResourceNames(),
            n => n.EndsWith("DisposableEmailDomains.txt", StringComparison.Ordinal))
            ?? throw new InvalidOperationException("Embedded resource 'DisposableEmailDomains.txt' was not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);

        var set = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed[0] == '#')
                continue;
            set.Add(trimmed.ToLowerInvariant());
        }

        return set.ToFrozenSet(StringComparer.Ordinal);
    }
}
