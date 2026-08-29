using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using BuildingBlocks.Web;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class IdempotencyTests
{
    private static readonly IdempotencyEndpointFilter Filter =
        new(NullLogger<IdempotencyEndpointFilter>.Instance);

    private static (DefaultHttpContext http, EndpointFilterInvocationContext filterContext) BuildContext(
        string method, IDistributedCache cache, string key = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cache);
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Request.Method = method;
        http.Request.Path = "/api/pay";
        if (key is not null)
            http.Request.Headers["Idempotency-Key"] = key;
        http.Response.Body = new MemoryStream();
        return (http, EndpointFilterInvocationContext.Create(http));
    }

    private static async Task ExecuteIfResult(object result, HttpContext http)
    {
        if (result is IResult r)
            await r.ExecuteAsync(http);
    }

    [Fact]
    public async Task Missing_key_returns_400()
    {
        var (http, filterContext) = BuildContext("POST", new FakeCache());
        var called = false;
        EndpointFilterDelegate next = _ => { called = true; return ValueTask.FromResult<object>(Results.Ok()); };

        var result = await Filter.InvokeAsync(filterContext, next);

        result.Should().BeOfType<ProblemHttpResult>()
            .Which.StatusCode.Should().Be(400);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task Safe_method_passes_through()
    {
        var (_, filterContext) = BuildContext("GET", new FakeCache());
        var called = false;
        EndpointFilterDelegate next = _ => { called = true; return ValueTask.FromResult<object>(Results.Ok()); };

        await Filter.InvokeAsync(filterContext, next);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task Replays_stored_response_on_repeated_key()
    {
        var cache = new FakeCache();
        EndpointFilterDelegate handler = _ => ValueTask.FromResult<object>(new RawResult(201, "application/json", "{\"id\":1}"u8.ToArray()));

        var (_, first) = BuildContext("POST", cache, key: "k-1");
        await Filter.InvokeAsync(first, handler);

        var calls = 0;
        EndpointFilterDelegate countingHandler = _ =>
        {
            calls++;
            return ValueTask.FromResult<object>(new RawResult(201, "application/json", "{\"id\":1}"u8.ToArray()));
        };
        var (secondHttp, second) = BuildContext("POST", cache, key: "k-1");
        var result = await Filter.InvokeAsync(second, countingHandler);
        await ExecuteIfResult(result, secondHttp);

        calls.Should().Be(0); // handler not re-run — response replayed from cache
        secondHttp.Response.StatusCode.Should().Be(201);
        secondHttp.Response.Headers["Idempotency-Replayed"].ToString().Should().Be("true");
    }

    // A DI-free IResult so result execution in unit tests doesn't require JsonOptions/logging services.
    private sealed class RawResult(int status, string contentType, byte[] body) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.StatusCode = status;
            httpContext.Response.ContentType = contentType;
            await httpContext.Response.Body.WriteAsync(body, httpContext.RequestAborted);
        }
    }

    private sealed class FakeCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new(StringComparer.Ordinal);
        public byte[] Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public Task<byte[]> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _store[key] = value;
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            _store[key] = value;
            return Task.CompletedTask;
        }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            _store.Remove(key);
            return Task.CompletedTask;
        }
    }
}
