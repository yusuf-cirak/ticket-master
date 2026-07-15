using System.IO.Compression;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.DependencyInjection;

namespace YC.BuildingBlocks.Web;

/// <summary>
/// Compression defaults. Outbound: Brotli + gzip response compression (disabled over HTTPS to avoid the BREACH
/// attack; opt in per response). Inbound: request decompression so clients may send a compressed body
/// (Content-Encoding: br/gzip/deflate). The decompressed stream is still bounded by Kestrel's
/// <c>MaxRequestBodySize</c>, which limits the buffered output and caps decompression-bomb exposure.
/// </summary>
public static class CompressionExtensions
{
    public static WebApplicationBuilder AddCompression(this WebApplicationBuilder builder)
    {
        // Accept br/gzip/deflate request bodies; unsupported encodings fall through untouched (415 if the
        // endpoint can't read them). Default providers only — no custom codecs that could widen the attack surface.
        builder.Services.AddRequestDecompression();
        
        
        builder.Services.AddResponseCompression(o =>
        {
            o.EnableForHttps = false;
            o.Providers.Add<BrotliCompressionProvider>();
            o.Providers.Add<GzipCompressionProvider>();
        });

        return builder;
    }
}
