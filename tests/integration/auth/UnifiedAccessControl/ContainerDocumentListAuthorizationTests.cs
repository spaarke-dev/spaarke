using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 078 — <see cref="ContainerDocumentAuthorizationFilter"/>, the gate on
/// <c>GET /api/v1/containers/{containerId}/documents</c>.
/// </summary>
/// <remarks>
/// <para><b>What is real and what is substituted.</b> The <see cref="RecordContainerResolver"/> is the REAL
/// one, over substituted <see cref="ISecurableEntityRegistry"/> / <see cref="IGenericEntityService"/> — which
/// is what <see cref="OwningSecureRecord"/>'s doc comment prescribes for consumers ("the tests substitute the
/// resolver's DEPENDENCIES … and exercise the real decision logic, which is higher-fidelity than mocking the
/// decision itself"). <see cref="AuthorizationService"/> is likewise real, over a hand-written
/// <see cref="IAccessDataSource"/> double. So the container→record decision and the rights comparison are
/// both production code here; only Dataverse itself is stood in for. No transport mock (ADR-038 ban B1).</para>
///
/// <para><b>SCOPE — this suite tests the FILTER, not the route end to end.</b> Every case drives
/// <see cref="ContainerDocumentAuthorizationFilter.InvokeAsync"/> directly with hand-set route values, so
/// "reaches the handler" below means the filter passed through, nothing more. End-to-end the route is
/// separately unreachable: the handler validates <c>Guid.TryParse(containerId)</c> while
/// <c>sprk_containerid</c> stores an SPE <c>b!…</c> string (see the endpoint's own comment, and task 078
/// notes §4). That is a pre-existing type bug, deliberately not fixed here — but it means these tests cannot
/// and do not claim the route works.</para>
///
/// <para><b>PERTURBATION-CHECKED 2026-08-28</b>, because this project has been burned by tests that stayed
/// green against a broken read (45 of them). Deleting the <c>owner is null</c> refusal from the filter turns
/// <see cref="ContainerWithNoEstablishableOwner_IsRefused"/> red; deleting the
/// <c>!AccessRights.HasFlag(Read)</c> refusal turns <see cref="CallerWithoutReadOnOwningRecord_IsDenied"/>
/// red naming a pass-through. Neither deny test can pass against a gate that does not decide.</para>
/// </remarks>
public class ContainerDocumentListAuthorizationTests
{
    /// <summary>
    /// The container a SECURE project claims — the one with an establishable owner.
    /// </summary>
    /// <remarks>
    /// Contains an underscore ON PURPOSE. <c>_</c> is LIKE-significant, the resolver bracket-escapes it
    /// (<c>EscapeForLike</c>), and its own doc says SPE drive ids "routinely contain" one. With an
    /// underscore-free id the fixture's escape round-trip would be inert and would pin nothing.
    /// </remarks>
    private const string OwnedContainer = "b!owned_container_078";

    /// <summary>A shared business-unit container: real, in use, and claimed by no SECURE record.</summary>
    private const string SharedContainer = "b!shared_bu_container_078";

    private const string SecureEntity = "sprk_project";
    private static readonly Guid SecureRecordId = Guid.Parse("07807807-0000-0000-0000-000000000078");

    private const string CallerOid = "3f2504e0-4f89-11d3-9a0c-0305e82c3301";

    /// <summary>The single code every resource-side refusal must carry. See the uniformity test.</summary>
    private const string UniformDenialCode = "container_documents_access_denied";

    // =============================================================================================
    // THE LOAD-BEARING TEST
    // =============================================================================================

    [Fact(DisplayName = "Task 078: a caller with no Read on the owning record cannot list the container's documents")]
    public async Task CallerWithoutReadOnOwningRecord_IsDenied()
    {
        var accessDataSource = new StubAccessDataSource(AccessRights.None);

        var (result, reachedHandler) = await InvokeAsync(OwnedContainer, accessDataSource);

        reachedHandler.Should().BeFalse(
            "the whole point of the gate is that the handler never runs for an unauthorized caller — this "
            + "route reads document metadata through the BFF's own app-only identity, so the filter is the "
            + "ENTIRE security boundary (project CLAUDE.md fact 1)");

        StatusOf(result).Should().Be(403);

        // The owning record WAS asked about, and it was asked about as the caller. A gate that denied
        // without consulting anything would satisfy the status assertion while deciding nothing —
        // that is ArchTest Rule B's finding #1 in miniature.
        accessDataSource.ObservedEntitySet.Should().Be("sprk_projects");
        accessDataSource.ObservedRecordId.Should().Be(SecureRecordId);
        accessDataSource.ObservedUserId.Should().Be(CallerOid);
        accessDataSource.ObservedCallerToken.Should().NotBeNullOrWhiteSpace(
            "an app-only evaluation answers 'can the APPLICATION see this', which on a BFF-served read is "
            + "always yes — finding A-2");
    }

    // =============================================================================================
    // FAIL CLOSED — ADR-003
    // =============================================================================================

    [Fact(DisplayName = "Task 078: a container that resolves to no owning record is REFUSED, not listed")]
    public async Task ContainerWithNoEstablishableOwner_IsRefused()
    {
        // A shared business-unit container. RecordContainerResolver.ResolveOwningRecordAsync returns null
        // here by design — that is an ANSWER ("no secure record claims this"), not a failure. This test
        // pins what task 078 does with that answer: REFUSE. Unknown must never become permitted.
        //
        // Rights are deliberately set to FULL: if the filter ever allowed on a null owner, a
        // rights-limited double could mask it. The caller is maximally privileged and still denied,
        // because there is no record to be privileged ON.
        var accessDataSource = new StubAccessDataSource(AccessRights.Read | AccessRights.Write);

        var (result, reachedHandler) = await InvokeAsync(SharedContainer, accessDataSource);

        reachedHandler.Should().BeFalse();
        StatusOf(result).Should().Be(403);

        accessDataSource.WasConsulted.Should().BeFalse(
            "there is no record to authorize against, so the refusal must come before any rights question "
            + "— consulting Dataverse with a fabricated subject is how 'do not invent an owner' gets "
            + "violated (task 078 escalation trigger)");
    }

    [Fact(DisplayName = "Task 078: ambiguous container ownership refuses rather than naming an owner")]
    public async Task AmbiguousContainerOwnership_IsRefused()
    {
        // Two secure records claiming one container. RecordContainerResolver throws
        // container_ownership_ambiguous rather than picking one; the filter folds that into the uniform
        // denial. Asserting the OUTCOME (refused, indistinguishably) rather than the MECHANISM (a thrown
        // SdapProblemException) is deliberate — an earlier version of this test pinned the exception, which
        // would have failed the very fix that removed the 409's disclosure while keeping it fail-closed.
        var accessDataSource = new StubAccessDataSource(AccessRights.Read | AccessRights.Write);

        var (result, reachedHandler) = await InvokeAsync(
            OwnedContainer,
            accessDataSource,
            extraSecureClaimant: Guid.Parse("07807807-0000-0000-0000-0000000000ff"));

        reachedHandler.Should().BeFalse();
        StatusOf(result).Should().Be(403);
        accessDataSource.WasConsulted.Should().BeFalse();
    }

    [Fact(DisplayName = "Task 078: an absent caller identity is denied 401 before any container work")]
    public async Task MissingCallerIdentity_IsDenied()
    {
        var accessDataSource = new StubAccessDataSource(AccessRights.Read);

        var (result, reachedHandler) = await InvokeAsync(
            OwnedContainer, accessDataSource, user: new ClaimsPrincipal(new ClaimsIdentity()));

        reachedHandler.Should().BeFalse();
        StatusOf(result).Should().Be(401);
        accessDataSource.WasConsulted.Should().BeFalse();
    }

    [Fact(DisplayName = "Task 078: an absent bearer token is denied 401 — never evaluated app-only")]
    public async Task MissingCallerToken_IsDeniedAsUnauthorized()
    {
        // AuthorizationService already fails closed on a blank token, so this was never a hole. What this
        // pins is that a missing CREDENTIAL answers 401 rather than being mislabelled as a 403 access
        // decision — and that it is answered BEFORE the resolver's Dataverse round trips.
        var accessDataSource = new StubAccessDataSource(AccessRights.Read | AccessRights.Write);

        var (result, reachedHandler) = await InvokeAsync(
            OwnedContainer, accessDataSource, omitBearerToken: true);

        reachedHandler.Should().BeFalse();
        StatusOf(result).Should().Be(401);
        accessDataSource.WasConsulted.Should().BeFalse();
    }

    // =============================================================================================
    // THE DENIAL MUST NOT BE AN ORACLE
    // =============================================================================================

    [Fact(DisplayName = "Task 078: every resource-side refusal is indistinguishable — one status, one code")]
    public async Task AllResourceSideRefusals_AreIndistinguishableToTheCaller()
    {
        // The first version of this filter shipped a DISTINCT errorCode per branch (no-owner /
        // owner-not-authorizable / no-Read) and let the resolver's 409 propagate carrying "More than one
        // record claims this container". Uniform prose with a discriminating code is not uniform: together
        // those let an unauthorized caller partition container ids by whether a secure record claims one,
        // before any rights check ran. Caught at Step 9.5 review; this is the regression guard.
        //
        // Sibling policy, verbatim (SemanticSearchAuthorizationFilter): "the two cases must stay
        // indistinguishable to the caller in EVERY channel, not just the prose."

        // (a) no owning record — a shared container
        var noOwner = await InvokeAsync(SharedContainer, new StubAccessDataSource(AccessRights.Read));

        // (b) ambiguous ownership — two secure claimants
        var ambiguous = await InvokeAsync(
            OwnedContainer,
            new StubAccessDataSource(AccessRights.Read),
            extraSecureClaimant: Guid.Parse("07807807-0000-0000-0000-0000000000ff"));

        // (c) owned, authorizable, but the caller holds no Read
        var noRead = await InvokeAsync(OwnedContainer, new StubAccessDataSource(AccessRights.None));

        var codes = new[]
        {
            ErrorCodeOf(noOwner.Result),
            ErrorCodeOf(ambiguous.Result),
            ErrorCodeOf(noRead.Result),
        };

        codes.Should().AllBe(UniformDenialCode,
            "a per-branch error code is an oracle: it tells a caller who holds nothing WHY they were "
            + "refused, which partitions container ids into classes by their ownership state");

        new[] { StatusOf(noOwner.Result), StatusOf(ambiguous.Result), StatusOf(noRead.Result) }
            .Should().AllBeEquivalentTo(403);

        new[] { DetailOf(noOwner.Result), DetailOf(ambiguous.Result), DetailOf(noRead.Result) }
            .Should().AllBe("You do not have access to this container.");
    }

    // =============================================================================================
    // THE GATE MUST NOT SIMPLY REFUSE EVERYTHING
    // =============================================================================================

    [Fact(DisplayName = "Task 078: the filter passes an authorized caller through to the handler")]
    public async Task CallerWithReadOnOwningRecord_IsPassedThroughByTheFilter()
    {
        // Without this, "deny everything" would pass every test above. The project's own constraint is
        // explicit that a refusal breaking a legitimate list view gets reverted, which reopens the hole.
        //
        // NOTE the deliberately narrow claim: the FILTER passes through. It does not assert the route
        // returns documents — end to end it cannot, because of the pre-existing containerId type bug
        // documented in the class remarks. Overstating this as "the listing works" is exactly the kind of
        // claim this project has been burned by.
        var accessDataSource = new StubAccessDataSource(AccessRights.Read);
        var sentinel = new object();

        var (result, reachedHandler) = await InvokeAsync(
            OwnedContainer, accessDataSource, handlerResult: sentinel);

        reachedHandler.Should().BeTrue();

        // BeSameAs, not BeNull: the filter must return the handler's own result untouched. A filter that
        // swallowed or rewrote it would still satisfy a null check, because the default double returns null.
        result.Should().BeSameAs(sentinel);
    }

    // =============================================================================================
    // HARNESS
    // =============================================================================================

    /// <summary>
    /// Runs the real filter over the real resolver and the real <see cref="AuthorizationService"/>,
    /// reporting whether the downstream handler was reached.
    /// </summary>
    private static async Task<(object? Result, bool ReachedHandler)> InvokeAsync(
        string containerId,
        StubAccessDataSource accessDataSource,
        ClaimsPrincipal? user = null,
        Guid? extraSecureClaimant = null,
        bool omitBearerToken = false,
        object? handlerResult = null)
    {
        var filter = new ContainerDocumentAuthorizationFilter(
            BuildResolver(extraSecureClaimant),
            new AuthorizationService(
                accessDataSource,
                Array.Empty<IAuthorizationRule>(),
                NullLogger<AuthorizationService>.Instance),
            NullLogger<ContainerDocumentAuthorizationFilter>.Instance);

        var http = new DefaultHttpContext { User = user ?? CallerPrincipal() };
        http.Request.RouteValues[ContainerDocumentAuthorizationFilter.ContainerRouteParameter] = containerId;

        // REQUIRED, not incidental: AuthorizationService fails closed with no caller token (finding A-2),
        // so without this the filter would deny before IAccessDataSource is consulted and the deny tests
        // would be measuring the wrong guard.
        if (!omitBearerToken)
        {
            http.Request.Headers.Authorization = "Bearer test-caller-token";
        }

        var reachedHandler = false;

        var result = await filter.InvokeAsync(
            EndpointFilterInvocationContext.Create(http),
            _ =>
            {
                reachedHandler = true;
                return ValueTask.FromResult(handlerResult);
            });

        return (result, reachedHandler);
    }

    private static ClaimsPrincipal CallerPrincipal() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", CallerOid) }, "TestAuth"));

    /// <summary>
    /// The REAL <see cref="RecordContainerResolver"/>, backed by a Dataverse double that models one secure
    /// project claiming <see cref="OwnedContainer"/> and nothing at all claiming
    /// <see cref="SharedContainer"/>.
    /// </summary>
    /// <remarks>
    /// The double honours the query's own container condition rather than assuming what the probe means,
    /// so the resolver's code-side exact-after-trim compare is genuinely exercised. It deliberately does
    /// NOT model the co-mingling probe's page-fill path — the ambiguity test reaches its branch through two
    /// SECURE claimants, which needs no such modelling.
    /// <see cref="RecordContainerResolverTests"/> owns the resolver's own edge cases; duplicating them here
    /// would be a second suite for one component.
    /// </remarks>
    private static RecordContainerResolver BuildResolver(Guid? extraSecureClaimant)
    {
        var securable = new HashSet<string>(StringComparer.Ordinal) { SecureEntity };

        var registry = Substitute.For<ISecurableEntityRegistry>();

        // Only GetSecurableEntitiesAsync is stubbed: the REVERSE direction is the only one this filter
        // uses, and it never calls IsSecurableAsync. Stubbing that too would imply a dependency the code
        // under test does not have.
        registry.GetSecurableEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(securable));

        var entityService = Substitute.For<IGenericEntityService>();

        entityService.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var query = call.Arg<QueryExpression>();
                var collection = new EntityCollection();

                // Only the SECURE probe (sprk_issecure == true) has claimants in this fixture. The
                // co-mingling probe therefore returns empty, which is the correct modelling of "one
                // secure project owns its own container and no non-secure row shares it".
                var isSecureProbe = query.Criteria.Conditions.Any(c =>
                    c.AttributeName == SecurableEntityRegistry.SecureFlagAttribute
                    && c.Operator == ConditionOperator.Equal
                    && c.Values.Count == 1
                    && c.Values[0] is true);

                if (!isSecureProbe || !ContainerConditionMatches(query, OwnedContainer))
                {
                    return Task.FromResult(collection);
                }

                collection.Entities.Add(SecureRow(SecureRecordId));

                if (extraSecureClaimant is { } second)
                {
                    collection.Entities.Add(SecureRow(second));
                }

                return Task.FromResult(collection);
            });

        return new RecordContainerResolver(
            registry, entityService, NullLogger<RecordContainerResolver>.Instance);
    }

    private static Entity SecureRow(Guid id)
    {
        var row = new Entity(SecureEntity, id);
        row[SecurableEntityRegistry.SecureFlagAttribute] = true;
        row["sprk_containerid"] = OwnedContainer;
        return row;
    }

    /// <summary>
    /// Whether the query's LIKE condition on the container column would match the stored value. Mirrors
    /// the production filter's shape (bracket-escaped, wrapped in <c>%</c>) so a fixture that pre-filtered
    /// exactly could not hide whether the resolver's own compare exists.
    /// </summary>
    /// <remarks>
    /// The unescape is live rather than decorative: both container constants contain <c>_</c>, which
    /// <c>EscapeForLike</c> rewrites to <c>[_]</c>. Skip the unescape and the needle stops matching, so this
    /// helper genuinely pins that the production query escapes LIKE metacharacters.
    /// </remarks>
    private static bool ContainerConditionMatches(QueryExpression query, string storedValue)
    {
        var condition = query.Criteria.Conditions
            .FirstOrDefault(c => c.AttributeName == "sprk_containerid"
                                 && c.Operator == ConditionOperator.Like);

        if (condition?.Values.FirstOrDefault() is not string pattern)
        {
            return false;
        }

        var needle = pattern
            .Trim('%')
            .Replace("[[]", "[", StringComparison.Ordinal)
            .Replace("[%]", "%", StringComparison.Ordinal)
            .Replace("[_]", "_", StringComparison.Ordinal);

        return storedValue.Contains(needle, StringComparison.Ordinal);
    }

    private static int? StatusOf(object? result) => result switch
    {
        IStatusCodeHttpResult s => s.StatusCode,
        _ => null,
    };

    private static string? ErrorCodeOf(object? result) =>
        result is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult p
        && p.ProblemDetails.Extensions.TryGetValue("errorCode", out var code)
            ? code as string
            : null;

    private static string? DetailOf(object? result) =>
        result is Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult p
            ? p.ProblemDetails.Detail
            : null;

    /// <summary>
    /// Records what the filter asked <see cref="IAccessDataSource"/>, and answers with fixed rights. The
    /// observed arguments are the assertion surface: the defect this task closes was the ABSENCE of the
    /// question, so the test observes that the question is asked, about the right record, as the caller.
    /// </summary>
    private sealed class StubAccessDataSource(AccessRights rights) : IAccessDataSource
    {
        public bool WasConsulted { get; private set; }
        public string? ObservedUserId { get; private set; }
        public string? ObservedEntitySet { get; private set; }
        public Guid? ObservedRecordId { get; private set; }
        public string? ObservedCallerToken { get; private set; }

        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId,
            string entitySetName,
            Guid recordId,
            string? userAccessToken,
            CancellationToken ct = default)
        {
            WasConsulted = true;
            ObservedUserId = userId;
            ObservedEntitySet = entitySetName;
            ObservedRecordId = recordId;
            ObservedCallerToken = userAccessToken;

            return Task.FromResult(new AccessSnapshot
            {
                UserId = userId,
                ResourceId = recordId.ToString(),
                AccessRights = rights,
            });
        }

        /// <summary>
        /// The document-only sibling. This filter must never route through it — its target is hard-coded to
        /// <c>sprk_documents</c>, so asking it about a project answers None for every caller however
        /// privileged. Throwing makes a regression that reaches it fail loudly rather than deny silently.
        /// </summary>
        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default) =>
            throw new NotSupportedException(
                "ContainerDocumentAuthorizationFilter must authorize the OWNING RECORD via "
                + "GetRecordAccessAsync, not a document via GetUserAccessAsync.");
    }
}
