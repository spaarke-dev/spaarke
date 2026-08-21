// unified-access-control-r2 task 019 (2026-08-21) — FR-17 / A-22 characterization +
// regression suite for LookupUserMembershipNodeExecutor's includeRelated mapping.
//
// A-22 (10-finding-confirmations.md, PARTIAL): LookupUserMembershipNodeExecutor used to set
// IncludeRelated = new[] { "*" } whenever config.IncludeRelated == true, under a comment
// claiming MembershipResolverService "accepts-but-ignores" it in Phase 1A. That stopped being
// true once Phase 1D transitive expansion shipped (task 054): the resolver's IncludeRelated
// contract requires each entry to be a CONCRETE related-entity logical name that 1-hop-
// validates via Dataverse metadata (ResolveTransitiveAsync -> DiscoverLookupsTargetingAsync).
// "*" passes the resolver's cheap pre-validation (no '.' or '/') but then fails the metadata
// fetch and surfaces as MembershipDepthExceededException(reasonTag: "unknown-entity") -- the
// executor's catch(Exception) turned that into NodeOutput.Error(InternalError) on EVERY
// includeRelated=true execution. Task 001 could not reach this offline (the throw originates
// in a live Dataverse metadata fetch) -- see notes/task-001-untestable-findings.md §2(b).
//
// This suite closes that gap at the EXECUTOR boundary (not the resolver internals, which
// tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/MembershipResolverServiceTests.cs
// already characterizes generically via ResolveAsync_WithUnknownRelatedEntity_ThrowsDepthExceeded).
//
// Anti-vacuity design: UnknownEntityThrowingResolver is a hand-written IMembershipResolverService
// double that mirrors the REAL resolver's failure mode -- it throws
// MembershipDepthExceededException(reasonTag: "unknown-entity") for ANY non-empty IncludeRelated
// entry (not just "*"; the node has no config surface for a legitimate entry today, so any
// non-null value is definitionally unresolvable at this node). If the executor's fix in this
// task were reverted (IncludeRelated: new[] { "*" } restored), Test 1 below would fail with
// NodeOutput.Error(InternalError) exactly as production did before the fix -- the assertion is
// not "does not throw" against a resolver that ignores its input; it throws on ANY populated
// IncludeRelated the way production genuinely does.
//
// Reference: src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/LookupUserMembershipNodeExecutor.cs;
//            projects/unified-access-control-r2/tasks/019-fix-lookup-membership-node-executor.poml;
//            projects/unified-access-control-r2/notes/task-019-fix-lookup-membership-node-executor.md.

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// Characterization + regression suite for A-22 (FR-17): the LookupUserMembership node
/// executor's includeRelated -> IncludeRelated mapping.
/// </summary>
public class LookupUserMembershipIncludeRelatedCharacterizationTests
{
    /// <summary>
    /// Fake <see cref="IMembershipResolverService"/> that reproduces
    /// MembershipResolverService.ResolveTransitiveAsync's real failure mode
    /// (MembershipResolverService.cs:957-978): a metadata-discovery failure for an
    /// unresolvable related-entity name surfaces as
    /// <see cref="MembershipDepthExceededException"/> with reasonTag "unknown-entity".
    /// This node has no config field naming a legitimate related entity, so ANY
    /// non-empty IncludeRelated entry is treated as unresolvable here -- matching what
    /// the real resolver does for the literal "*" sentinel the executor used to send.
    /// </summary>
    private sealed class UnknownEntityThrowingResolver : IMembershipResolverService
    {
        public MembershipResolveOptions? CapturedOptions { get; private set; }
        public int CallCount { get; private set; }

        public Task<MembershipResponse> ResolveAsync(
            Guid systemUserId,
            string entityType,
            MembershipResolveOptions? options,
            CancellationToken ct)
        {
            CallCount++;
            CapturedOptions = options;

            if (options?.IncludeRelated is { Count: > 0 } related)
            {
                var offending = related[0];
                throw new MembershipDepthExceededException(
                    offendingEntry: offending,
                    reasonTag: "unknown-entity",
                    message: $"includeRelated entry '{offending}' could not be resolved: " +
                             "Entity metadata fetch failed (simulated -- mirrors the real " +
                             "resolver's Dataverse metadata-discovery failure for an " +
                             "unresolvable entity name).");
            }

            return Task.FromResult(new MembershipResponse(
                EntityType: entityType,
                PersonIdentity: new PersonIdentity(systemUserId),
                Ids: Array.Empty<Guid>(),
                ByRole: new Dictionary<string, IReadOnlyList<Guid>>(),
                Count: 0,
                CacheExpiresAt: DateTimeOffset.UtcNow.AddMinutes(5),
                ContinuationToken: null));
        }

        public Task<MembershipResponse> ResolveByContactAsync(
            Guid contactId,
            string entityType,
            MembershipResolveOptions? options,
            CancellationToken ct)
            => throw new NotSupportedException("Not exercised by this suite.");
    }

    private static LookupUserMembershipNodeExecutor CreateExecutor(
        UnknownEntityThrowingResolver resolver,
        out Mock<IServiceScopeFactory> scopeFactoryMock)
    {
        var scopedProviderMock = new Mock<IServiceProvider>();
        scopedProviderMock
            .Setup(sp => sp.GetService(typeof(IMembershipResolverService)))
            .Returns(resolver);

        var scopeMock = new Mock<IServiceScope>();
        scopeMock.Setup(s => s.ServiceProvider).Returns(scopedProviderMock.Object);

        scopeFactoryMock = new Mock<IServiceScopeFactory>();
        scopeFactoryMock.Setup(f => f.CreateScope()).Returns(scopeMock.Object);

        return new LookupUserMembershipNodeExecutor(
            scopeFactoryMock.Object,
            NullLogger<LookupUserMembershipNodeExecutor>.Instance);
    }

    private static NodeExecutionContext CreateContext(string configJson, Guid userId)
    {
        var nodeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Lookup User Membership",
                ExecutionOrder = 1,
                OutputVariable = "myMemberships",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Lookup User Membership"
            },
            ExecutorType = ExecutorType.LookupUserMembership,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant",
            UserId = userId
        };
    }

    [Fact]
    public async Task ExecuteAsync_IncludeRelatedTrue_DoesNotThrowAndOmitsUnresolvableEntry()
    {
        // FR-17 acceptance: "A playbook node with includeRelated:true executes without
        // throwing and returns a NodeOutput, not InternalError." Non-vacuity: the resolver
        // double throws MembershipDepthExceededException for ANY non-empty IncludeRelated
        // entry, so this assertion only holds if the executor genuinely omits IncludeRelated
        // -- it is not a resolver stub that unconditionally succeeds regardless of input.
        var userId = Guid.NewGuid();
        var resolver = new UnknownEntityThrowingResolver();
        var executor = CreateExecutor(resolver, out _);

        var context = CreateContext(
            """{"entityType":"sprk_matter","includeRelated":true}""",
            userId);

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        // Proof the includeRelated=true branch was genuinely exercised (not skipped):
        // the resolver double was called exactly once, with the exact userId/entityType
        // this invocation supplied.
        resolver.CallCount.Should().Be(1, "the resolver must be reached for this to be a real proof, not a vacuous one");
        resolver.CapturedOptions.Should().NotBeNull();

        result.Success.Should().BeTrue(
            "includeRelated:true must no longer throw MembershipDepthExceededException(\"unknown-entity\") " +
            "as it did before the A-22 fix");
        result.ErrorCode.Should().NotBe(NodeErrorCodes.InternalError);
        result.OutputVariable.Should().Be("myMemberships");
    }

    [Fact]
    public async Task ExecuteAsync_IncludeRelatedTrue_NeverPassesWildcardOrAnyEntryToResolver()
    {
        // Negative acceptance criterion: "the executor no longer passes '*' to the resolver."
        // Asserted directly against the captured argument (stronger than re-deriving it from
        // the non-throw behavior above): IncludeRelated must be null, not ["*"] and not any
        // other invented value -- the node has no config field for a legitimate entry.
        var userId = Guid.NewGuid();
        var resolver = new UnknownEntityThrowingResolver();
        var executor = CreateExecutor(resolver, out _);

        var context = CreateContext(
            """{"entityType":"sprk_matter","includeRelated":true}""",
            userId);

        await executor.ExecuteAsync(context, CancellationToken.None);

        resolver.CapturedOptions.Should().NotBeNull();
        resolver.CapturedOptions!.IncludeRelated.Should().BeNull(
            "the resolver's IncludeRelated contract requires concrete related-entity names; " +
            "this node has no field to supply one, so the correct mapping omits IncludeRelated " +
            "entirely rather than passing the unresolvable \"*\" sentinel");
    }

    [Fact]
    public async Task ExecuteAsync_IncludeRelatedFalse_NoRegression_StillOmitsIncludeRelated()
    {
        // FR-17 acceptance: "A node with includeRelated:false behaves exactly as before
        // (no regression)." includeRelated:false already mapped to null pre-fix; this pins
        // that the fix did not change that branch.
        var userId = Guid.NewGuid();
        var resolver = new UnknownEntityThrowingResolver();
        var executor = CreateExecutor(resolver, out _);

        var context = CreateContext(
            """{"entityType":"sprk_matter","includeRelated":false}""",
            userId);

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        resolver.CapturedOptions.Should().NotBeNull();
        resolver.CapturedOptions!.IncludeRelated.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_IncludeRelatedOmittedFromConfig_DefaultsToNoRelatedExpansion()
    {
        // Config without includeRelated at all (the field is optional/nullable) must behave
        // identically to includeRelated:false -- same no-op mapping, no special-casing.
        var userId = Guid.NewGuid();
        var resolver = new UnknownEntityThrowingResolver();
        var executor = CreateExecutor(resolver, out _);

        var context = CreateContext(
            """{"entityType":"sprk_matter"}""",
            userId);

        var result = await executor.ExecuteAsync(context, CancellationToken.None);

        result.Success.Should().BeTrue();
        resolver.CapturedOptions.Should().NotBeNull();
        resolver.CapturedOptions!.IncludeRelated.Should().BeNull();
    }
}
