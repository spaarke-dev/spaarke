using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Identity;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Context;

/// <summary>
/// Issue #1576 (a) — <see cref="CallerSystemUserResolver"/> delegates the oid → systemuserid read to the shared
/// <see cref="ISystemUserIdentityResolver"/> (which excludes disabled systemusers) and keeps its own honest-outcome
/// contract: resolved, <c>no-matching-systemuser</c>, <c>lookup-failed</c>, and caller cancellation propagating.
/// </summary>
/// <remarks>The identity resolver is substituted at its interface with a hand-written fake (ADR-038): this class's
/// behaviour is the mapping of that resolver's three outcomes, which a fake drives exactly.</remarks>
public sealed class CallerSystemUserResolverTests
{
    private static readonly Guid Oid = Guid.Parse("15760000-0000-0000-0000-00000000aa01");
    private static readonly Guid SystemUserId = Guid.Parse("15760000-0000-0000-0000-00000000bb01");

    [Fact]
    public async Task Resolves_the_systemuserid_the_identity_resolver_returns_for_the_callers_oid()
    {
        var identity = new FakeIdentityResolver { Answer = _ => Task.FromResult<Guid?>(SystemUserId) };

        var result = await Resolver(identity).ResolveAsync(Caller(Oid), CancellationToken.None);

        result.IsResolved.Should().BeTrue();
        result.SystemUserId.Should().Be(SystemUserId.ToString("D"));
        identity.Asked.Should().Equal(Oid.ToString("D"));
    }

    [Fact]
    public async Task No_active_systemuser_is_an_honest_no_matching_systemuser()
    {
        // A disabled systemuser is the case this covers: ResolveSystemUserIdAsync filters isdisabled = false and
        // answers null for it.
        var identity = new FakeIdentityResolver { Answer = _ => Task.FromResult<Guid?>(null) };

        var result = await Resolver(identity).ResolveAsync(Caller(Oid), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.SystemUserId.Should().BeNull();
        result.UnresolvedReason.Should().Be("no-matching-systemuser");
    }

    [Fact]
    public async Task A_failed_lookup_is_an_honest_lookup_failed_never_a_guess()
    {
        var identity = new FakeIdentityResolver
        {
            Answer = _ => Task.FromException<Guid?>(new InvalidOperationException("simulated Dataverse fault")),
        };

        var result = await Resolver(identity).ResolveAsync(Caller(Oid), CancellationToken.None);

        result.IsResolved.Should().BeFalse();
        result.UnresolvedReason.Should().Be("lookup-failed");
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var identity = new FakeIdentityResolver
        {
            Answer = ct => Task.FromException<Guid?>(new OperationCanceledException(ct)),
        };

        var act = () => Resolver(identity).ResolveAsync(Caller(Oid), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_caller_with_no_oid_claim_never_reaches_the_identity_resolver()
    {
        var identity = new FakeIdentityResolver { Answer = _ => Task.FromResult<Guid?>(SystemUserId) };

        var result = await Resolver(identity).ResolveAsync(new ClaimsPrincipal(new ClaimsIdentity()), CancellationToken.None);

        result.UnresolvedReason.Should().Be("no-oid-claim");
        identity.Asked.Should().BeEmpty();
    }

    private static CallerSystemUserResolver Resolver(ISystemUserIdentityResolver identity)
        => new(identity, NullLogger<CallerSystemUserResolver>.Instance);

    private static ClaimsPrincipal Caller(Guid oid)
        => new(new ClaimsIdentity(new[] { new Claim("oid", oid.ToString("D")) }, "test"));

    private sealed class FakeIdentityResolver : ISystemUserIdentityResolver
    {
        public required Func<CancellationToken, Task<Guid?>> Answer { get; init; }

        public List<string> Asked { get; } = new();

        public Task<Guid?> ResolveSystemUserIdAsync(string oid, CancellationToken ct = default)
        {
            Asked.Add(oid);
            return Answer(ct);
        }

        public Task<string?> ResolveOidAsync(Guid systemUserId, CancellationToken ct = default)
            => throw new NotSupportedException("CallerSystemUserResolver only maps oid → systemuserid.");

        public Task<bool> IsExternalAsync(Guid systemUserId, CancellationToken ct = default)
            => throw new NotSupportedException("CallerSystemUserResolver only maps oid → systemuserid.");
    }
}
