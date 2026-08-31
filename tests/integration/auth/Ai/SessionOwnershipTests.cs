using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Tests.Api.Ai;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Auth.Ai;

/// <summary>
/// <c>tests/integration/auth/**</c> — authorization KEEP category (ADR-038 §2 path #1).
/// Pins issue #863: a chat session belongs to ONE user, and nobody else can reach it.
/// </summary>
/// <remarks>
/// <para>
/// <b>What was actually wrong.</b> A session carried a tenant and no owner. Task 059 closed the
/// cross-TENANT half. Within a tenant, every session-scoped route was open to every authenticated
/// user — and <c>GET /api/ai/chat/sessions</c> published the ids, titles and content previews needed
/// to use them. The mitigation recorded in <c>notes/059-tenant-header-decisions.md</c> §6a ("ids are
/// 128-bit random, so exploitation requires a leaked id") never held: the ids were listed.
/// </para>
/// <para>
/// <b>Why these tests assert on the DENIAL and not on a resolver.</b> Asserting that some helper
/// returns the right owner string stays green through the exact refactor that stops calling it.
/// Every test below states its assertion about the OTHER user's data — a session that must still
/// exist, a list that must not contain it — for the same reason the sibling tenant suite does
/// (see that directory's README).
/// </para>
/// <para>
/// <b>The positive control is not decoration.</b> A guard that denies everyone passes every negative
/// test in this file and is still broken; <see cref="Evaluate_ForTheOwner_Allows"/> is what stops
/// that shipping. Two things during #863 would have made these pass for the wrong reason: the
/// filter's first draft echoed the session id in its detail string (caught by an existing ADR-019
/// assertion on the dispatch route), and six fixtures minted a fresh oid per request.
/// </para>
/// <para>
/// <b>What is NOT covered here, and where it is.</b> The History-list owner predicate
/// (<c>c.ownerOid = @ownerOid</c>) is a Cosmos query, so behaviour-testing it needs an emulator this
/// suite does not have; it is covered structurally by
/// <c>SessionOwnershipGuardTests.Rule2</c>, which also asserts the <c>NOT IS_DEFINED</c> escape
/// hatch has not been added back. Only the service's own fail-closed guard is exercised below.
/// </para>
/// <para>
/// <b>Pre-fix behaviour.</b> There is nothing to run these against pre-fix: before #863
/// <c>ChatSession</c> had no owner field, so the denial tests do not compile against that tree
/// rather than failing against it. Stated plainly instead of implying a red-then-green run that did
/// not happen. What WAS observed red-then-green is
/// <c>SessionOwnershipGuardTests.Rule1</c>: removing one <c>.AddSessionOwnershipFilter()</c> line
/// turns it red, restoring it turns it green.
/// </para>
/// </remarks>
[Trait("category", "security-auth")]
public sealed class SessionOwnershipTests
{
    private const string Tenant = "aaaaaaaa-1111-2222-3333-444444444444";
    private const string OtherTenant = "bbbbbbbb-5555-6666-7777-888888888888";

    private static ChatSessionManager BuildManager()
        => new(
            cache: new InMemoryTenantCache(),
            dataverseRepository: new CapturingChatDataverseRepository(),
            logger: NullLogger<ChatSessionManager>.Instance,
            persistence: null,
            cleanupSignal: null);

    /// <summary>A caller authenticated in <paramref name="tenantId"/> as <paramref name="oid"/>.</summary>
    /// <remarks>
    /// <c>RequestServices</c> is populated deliberately. A bare <c>new DefaultHttpContext()</c>
    /// leaves it null even though the property is typed non-nullable, and no real request ever has
    /// it null — so the alternative "fix" (a <c>?.</c> in the filter) would be a defensive null
    /// check on a shape production cannot produce, added to satisfy a fixture. The fixture is the
    /// thing that was wrong (<c>bff-extensions.md</c> §F.2).
    /// </remarks>
    private static DefaultHttpContext ContextFor(string oid, string sessionId, string tenantId = Tenant)
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("oid", oid), new Claim("tid", tenantId)],
                authenticationType: "Test")),
            RequestServices = new ServiceCollection()
                .AddLogging()
                .BuildServiceProvider(),
        };
        ctx.Request.RouteValues["sessionId"] = sessionId;
        return ctx;
    }

    private static async Task<int?> StatusOf(IResult? result)
    {
        await Task.CompletedTask;
        return result is IStatusCodeHttpResult s ? s.StatusCode : null;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The denial — the half #863 was missing
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_ForADifferentUserInTheSameTenant_Denies404AndLeavesTheSessionIntact()
    {
        var manager = BuildManager();
        var victim = await manager.CreateSessionAsync(Tenant, TestSessionOwner.Oid, documentId: null);

        var result = await SessionOwnershipFilterExtensions.EvaluateAsync(
            ContextFor(TestSessionOwner.OtherOid, victim.SessionId), manager);

        // Reachability: stated about the VICTIM's session, so it cannot be satisfied by a check that
        // merely reports the right owner while still letting the request through.
        (await manager.GetSessionAsync(Tenant, victim.SessionId)).Should().NotBeNull(
            "a colleague in the same tenant must not be able to reach — and therefore delete, rename, " +
            "read or post into — another user's session");

        (await StatusOf(result)).Should().Be(404,
            "404, not 403: a 403 confirms the session id is real, which turns any id the caller can " +
            "guess or overhear into an existence oracle");
    }

    [Fact]
    public async Task Evaluate_ForADifferentUser_UsesTheSameAnswerAsAMissingSession()
    {
        // The two must be INDISTINGUISHABLE on the wire. If they ever diverge — different status,
        // different errorCode, different detail — the route becomes an oracle for which session ids
        // exist, and that is exactly what the 404-not-403 choice was made to prevent.
        var manager = BuildManager();
        var victim = await manager.CreateSessionAsync(Tenant, TestSessionOwner.Oid, documentId: null);

        var notYours = await SessionOwnershipFilterExtensions.EvaluateAsync(
            ContextFor(TestSessionOwner.OtherOid, victim.SessionId), manager);

        var doesNotExist = await SessionOwnershipFilterExtensions.EvaluateAsync(
            ContextFor(TestSessionOwner.OtherOid, "ffffffffffffffffffffffffffffffff"), manager);

        (await StatusOf(notYours)).Should().Be(await StatusOf(doesNotExist),
            "'not yours' and 'does not exist' must be the same answer");
    }

    [Fact]
    public async Task Evaluate_ForAPreIssue863SessionWithNoOwner_DeniesEveryone()
    {
        // The migration decision, made executable. A session written before OwnerOid existed matches
        // NOBODY. The tempting alternative — treat unowned as world-readable-within-the-tenant so
        // nobody loses their history — would preserve the disclosure on the oldest and most numerous
        // documents, which is the population most likely to still be live.
        var manager = BuildManager();
        var legacy = await manager.CreateSessionAsync(Tenant, TestSessionOwner.Oid, documentId: null);

        // Overwrite the cached copy with the pre-#863 shape: identical in every respect except that
        // it has no owner, which is exactly what a session persisted before the field looks like.
        await manager.UpdateSessionCacheAsync(legacy with { OwnerOid = null });

        foreach (var caller in new[] { TestSessionOwner.Oid, TestSessionOwner.OtherOid })
        {
            var result = await SessionOwnershipFilterExtensions.EvaluateAsync(
                ContextFor(caller, legacy.SessionId), manager);

            (await StatusOf(result)).Should().Be(404,
                $"an unowned (pre-#863) session must fail closed for {caller} as for everyone else");
        }
    }

    [Fact]
    public async Task Evaluate_ForACallerWithNoObjectIdClaim_Answers401NotAnUnfilteredPass()
    {
        // CallerResolution's contract: an unidentifiable caller is 401, never 403 — and, critically,
        // never a pass-through. An identity check that falls open when identity is absent is not a
        // check.
        var manager = BuildManager();
        var victim = await manager.CreateSessionAsync(Tenant, TestSessionOwner.Oid, documentId: null);

        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim("tid", Tenant)], authenticationType: "Test")),
        };
        ctx.Request.RouteValues["sessionId"] = victim.SessionId;

        var result = await SessionOwnershipFilterExtensions.EvaluateAsync(ctx, manager);

        result.Should().NotBeNull("a caller with no oid must be refused, not allowed through");
        (await StatusOf(result)).Should().Be(401);
    }

    [Fact]
    public async Task Evaluate_ForTheRightOidInTheWrongTenant_Denies()
    {
        // Ownership does not override tenant isolation; it is layered on top of it. A shared oid
        // across tenants (a guest, or a B2B identity) must not reach into the other tenant.
        var manager = BuildManager();
        var victim = await manager.CreateSessionAsync(Tenant, TestSessionOwner.Oid, documentId: null);

        var result = await SessionOwnershipFilterExtensions.EvaluateAsync(
            ContextFor(TestSessionOwner.Oid, victim.SessionId, tenantId: OtherTenant), manager);

        (await manager.GetSessionAsync(Tenant, victim.SessionId)).Should().NotBeNull();
        (await StatusOf(result)).Should().Be(404);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The positive control — a guard that denies everyone is also broken
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_ForTheOwner_Allows()
    {
        var manager = BuildManager();
        var mine = await manager.CreateSessionAsync(Tenant, TestSessionOwner.Oid, documentId: null);

        var result = await SessionOwnershipFilterExtensions.EvaluateAsync(
            ContextFor(TestSessionOwner.Oid, mine.SessionId), manager);

        result.Should().BeNull(
            "null means 'allow'. Without this, a filter that denied every request would satisfy every "
            + "other test in this file and lock every user out of their own conversations");
    }

    [Fact]
    public async Task ListRecentSessions_ForAnUnidentifiableCaller_ReturnsNothingRatherThanTheTenantList()
    {
        // The service's own fail-closed guard, behind the endpoint's 401. It matters because the
        // pre-#863 method had NO owner parameter at all: any call reaching it returned the whole
        // tenant. If a future caller forgets to resolve the oid and passes empty, the answer must be
        // "nothing", never "everything" — the failure mode that made this a disclosure rather than
        // just a missing check.
        // Cosmos deliberately null: the guard must return before anything touches the container.
        // If a future edit moves the check below GetContainer(), this NREs — which is the right
        // failure, because a guard that runs after the query is not a guard.
        var service = new Sprk.Bff.Api.Services.Ai.Sessions.SessionPersistenceService(
            new InMemoryTenantCache(),
            cosmosClient: null!,
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CosmosPersistence:Endpoint"] = "https://unused.documents.azure.com:443/",
                    ["CosmosPersistence:DatabaseName"] = "spaarke-ai",
                })
                .Build(),
            NullLogger<Sprk.Bff.Api.Services.Ai.Sessions.SessionPersistenceService>.Instance,
            contextEventEmitter: new Moq.Mock<Sprk.Bff.Api.Services.Ai.Telemetry.IContextEventEmitter>().Object);

        var result = await service.ListRecentSessionsAsync(Tenant, ownerOid: "", limit: 10);

        result.Should().BeEmpty(
            "an unidentifiable caller gets NOTHING — the pre-#863 shape returned the tenant's entire "
            + "session list, ids and content previews included");
    }

    [Fact]
    public async Task CreateSession_WithoutAnOwner_IsRefusedRatherThanMintedUnowned()
    {
        // An unowned session fails closed for everyone — including the person who just created it.
        // So minting one is not a lax default; it is a session that is broken on arrival. The
        // required-parameter + ThrowIfNullOrWhiteSpace pairing is what makes that unrepresentable.
        var manager = BuildManager();

        var act = async () => await manager.CreateSessionAsync(Tenant, "  ", documentId: null);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ownership must survive the warm tier
    //
    // Deliberately NOT tested here. The obvious test — reflect over the private mapper pair and
    // round-trip a session — is banned by ADR-038 B8 (internal-method access via reflection), and
    // the ban is right: it would couple this suite to two private method names. The invariant is
    // covered structurally instead by SessionOwnershipGuardTests.Rule3, which asserts BOTH
    // directions of the mapping are present and names the consequence of losing either (an evicted
    // session becomes inaccessible to its own owner). Recorded here rather than left as a silent
    // gap, because "ownership survives eviction" is a real risk and a reader deserves to know where
    // it is checked.
    // ─────────────────────────────────────────────────────────────────────────
}
