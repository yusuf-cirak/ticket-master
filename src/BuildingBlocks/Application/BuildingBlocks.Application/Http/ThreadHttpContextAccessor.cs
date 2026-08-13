#nullable enable
using Microsoft.AspNetCore.Http;

namespace BuildingBlocks.Application.Http;

/// <summary>
/// Static access to the current request's <see cref="HttpContext"/> — the same ambient value <c>IHttpContextAccessor</c>
/// exposes, without injecting it. Wraps the DI accessor (captured once at startup via <see cref="Configure"/>), so it
/// works anywhere without threading the context through. <see cref="Current"/> is null outside a request.
/// </summary>
public static class ThreadHttpContextAccessor
{
    private static IHttpContextAccessor? _accessor;

    public static void Configure(IHttpContextAccessor accessor) => _accessor = accessor;

    public static HttpContext? Current => _accessor?.HttpContext;
}
