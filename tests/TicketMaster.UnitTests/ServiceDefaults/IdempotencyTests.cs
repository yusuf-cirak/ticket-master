using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketMaster.ServiceDefaults;
using Xunit;

namespace TicketMaster.UnitTests.ServiceDefaults;

public class IdempotencyTests
{
    private static DefaultHttpContext BuildContext(string method, bool requireIdempotency, IDistributedCache cache, string? key = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(cache);
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Request.Method = method;
        context.Request.Path = "/api/pay";
        if (key is not null)
            context.Request.Headers["Idempotency-Key"] = key;
        context.Response.Body = new MemoryStream();
        if (requireIdempotency)
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask,
                new EndpointMetadataCollection(RequireIdempotencyMetadata.Instance), "pay"));
        return context;
    }

    private static IdempotencyMiddleware Middleware(RequestDelegate next) =>
        new(next, NullLogger<IdempotencyMiddleware>.Instance);

    [Fact]
    public async Task Missing_key_on_required_endpoint_returns_400()
    {
        var context = BuildContext("POST", requireIdempotency: true, new FakeCache());
        var called = false;

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        context.Response.StatusCode.Should().Be(400);
        called.Should().BeFalse();
    }

    [Fact]
    public async Task Non_required_endpoint_passes_through()
    {
        var context = BuildContext("POST", requireIdempotency: false, new FakeCache());
        var called = false;

        await Middleware(_ => { called = true; return Task.CompletedTask; }).InvokeAsync(context);

        called.Should().BeTrue();
    }

    [Fact]
    public async Task Replays_stored_response_on_repeated_key()
    {
        var cache = new FakeCache();
        RequestDelegate handler = async c =>
        {
            c.Response.StatusCode = 201;
            c.Response.ContentType = "application/json";
            await c.Response.Body.WriteAsync(Encoding.UTF8.GetBytes("{\"id\":1}"));
        };

        var first = BuildContext("POST", requireIdempotency: true, cache, key: "k-1");
        await Middleware(handler).InvokeAsync(first);

        var calls = 0;
        var second = BuildContext("POST", requireIdempotency: true, cache, key: "k-1");
        await Middleware(c => { calls++; return handler(c); }).InvokeAsync(second);

        calls.Should().Be(0); // handler not re-run — response replayed from cache
        second.Response.StatusCode.Should().Be(201);
        second.Response.Headers["Idempotency-Replayed"].ToString().Should().Be("true");
    }

    private sealed class FakeCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _store = new();
        public byte[]? Get(string key) => _store.TryGetValue(key, out var v) ? v : null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
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
