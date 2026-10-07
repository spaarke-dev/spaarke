using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters;

/// <summary>
/// <c>IdempotencyFilter</c>'s replay gate (spaarkeai-word-add-in-r1 task 047). A route can mark a request as never
/// replayable, because whether it repeats an earlier request depends on state its key cannot see. The Office save route
/// does so for a Word VERSION save: the key names content, and B, then A, then B again used to be answered with the
/// first B save's cached 202. Such a request keeps the in-flight lock, but is neither answered from nor written to the
/// response cache.
/// </summary>
[Trait("status", "repaired")]
public class IdempotencyFilterReplayGateTests
{
    private const string ClientKey = "client-key";

    private static readonly ClaimsPrincipal User = new(new ClaimsIdentity(
        new[] { new Claim("oid", "user-123"), new Claim("tid", "test-tenant-1") }, "TestAuth"));

    private static ITenantCache CreateCache() => new TenantCache(
        new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
        Mock.Of<ILogger<TenantCache>>());

    private static IdempotencyFilter Filter(ITenantCache cache, Func<EndpointFilterInvocationContext, bool>? mayReplay) =>
        new(cache, Mock.Of<ILogger<IdempotencyFilter>>(), bindClientKeyTo: _ => null, mayReplayResponse: mayReplay);

    private static (DefaultHttpContext Http, EndpointFilterInvocationContext Context) Request()
    {
        var http = new DefaultHttpContext { User = User, TraceIdentifier = "trace" };
        http.Request.Method = "POST";
        http.Request.Path = "/api/office/save";
        http.Request.Headers["X-Idempotency-Key"] = ClientKey;
        var context = new Mock<EndpointFilterInvocationContext>();
        context.Setup(c => c.HttpContext).Returns(http);
        return (http, context.Object);
    }

    private static int StatusOf(object? result) => ((IStatusCodeHttpResult)result!).StatusCode ?? 200;

    [Fact]
    public async Task NonReplayableRequest_UnderAKeyWithACachedResponse_ReachesTheHandler()
    {
        var cache = CreateCache();
        var calls = 0;
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Accepted(null, new { call = ++calls }));

        await Filter(cache, mayReplay: null).InvokeAsync(Request().Context, next);
        var (http, context) = Request();
        await Filter(cache, mayReplay: _ => false).InvokeAsync(context, next);

        calls.Should().Be(2, "a non-replayable request is never answered from the response cache");
        http.Response.Headers["X-Idempotency-Status"].ToString().Should().Be("new");
    }

    [Fact]
    public async Task NonReplayableRequest_IsNotWrittenToTheResponseCache()
    {
        var cache = CreateCache();
        var calls = 0;
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Accepted(null, new { call = ++calls }));

        await Filter(cache, mayReplay: _ => false).InvokeAsync(Request().Context, next);
        var (http, context) = Request();
        await Filter(cache, mayReplay: null).InvokeAsync(context, next);

        calls.Should().Be(2, "nothing was cached for a later request under the same key to replay");
        http.Response.Headers["X-Idempotency-Status"].ToString().Should().Be("new");
    }

    [Fact]
    public async Task NonReplayableRequest_StillTakesTheInFlightLock()
    {
        // A second request under the same key arrives while the first is still executing (a concurrent double submit).
        var cache = CreateCache();
        var filter = Filter(cache, mayReplay: _ => false);
        object? concurrent = null;
        var innerCalls = 0;

        await filter.InvokeAsync(Request().Context, async _ =>
        {
            concurrent = await filter.InvokeAsync(Request().Context, _ =>
            {
                innerCalls++;
                return ValueTask.FromResult<object?>(Results.Accepted());
            });
            return Results.Accepted();
        });

        innerCalls.Should().Be(0, "the concurrent duplicate never reaches the handler");
        StatusOf(concurrent).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task WhenTheReplayDecisionThrows_TheResponseIsNotReplayed()
    {
        var cache = CreateCache();
        var calls = 0;
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Accepted(null, new { call = ++calls }));

        await Filter(cache, mayReplay: null).InvokeAsync(Request().Context, next);
        await Filter(cache, mayReplay: _ => throw new InvalidOperationException("unclassifiable"))
            .InvokeAsync(Request().Context, next);

        calls.Should().Be(2, "a request that could not be classified is never answered with an unverified replay");
    }

    [Fact]
    public async Task ReplayableRequest_UnderTheSameKey_IsStillReplayed()
    {
        var cache = CreateCache();
        var calls = 0;
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Accepted(null, new { call = ++calls }));

        await Filter(cache, mayReplay: _ => true).InvokeAsync(Request().Context, next);
        var (http, context) = Request();
        var replay = await Filter(cache, mayReplay: _ => true).InvokeAsync(context, next);

        calls.Should().Be(1);
        StatusOf(replay).Should().Be(StatusCodes.Status202Accepted);
        http.Response.Headers["X-Idempotency-Status"].ToString().Should().Be("cached");
    }
}
