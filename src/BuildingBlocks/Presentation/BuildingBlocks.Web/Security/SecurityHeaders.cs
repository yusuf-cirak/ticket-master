using Microsoft.AspNetCore.Builder;

namespace BuildingBlocks.Web;

/// <summary>
/// Security response headers applied to every response: deny framing, block MIME sniffing, a strict referrer
/// policy, a locked-down permissions policy (no camera/microphone/geolocation), the server header removed, and
/// HSTS. Applied by the ServiceDefaults pipeline via <see cref="Default"/>.
/// </summary>
public static class SecurityHeaders
{
    internal static HeaderPolicyCollection Default { get; } = new HeaderPolicyCollection()
        .AddFrameOptionsDeny()
        .AddContentTypeOptionsNoSniff()
        .AddReferrerPolicyStrictOriginWhenCrossOrigin()
        .RemoveServerHeader()
        .AddStrictTransportSecurityMaxAgeIncludeSubDomains()
        .AddXssProtectionEnabled()
        .AddContentSecurityPolicy(csp =>
        {
            csp.AddDefaultSrc().Self();
            csp.AddChildSrc().Self();
            csp.AddImgSrc().Self();
            csp.AddStyleSrc().Self();
            csp.AddFontSrc().Self();
            csp.AddScriptSrc().Self();
            csp.AddFormAction().Self();
            csp.AddBaseUri().Self();
            csp.AddManifestSrc().Self();
            csp.AddMediaSrc().Self();
            csp.AddWorkerSrc().Self();
            csp.AddTrustedTypes().Default();

            csp.AddObjectSrc().None();
            csp.AddFrameAncestors().None();

            csp.AddUpgradeInsecureRequests();
        });
}