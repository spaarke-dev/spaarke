using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 075 — behavioural tests for the record-aware container resolver.
///
/// <para><b>Why these are behavioural and not wiring tests.</b> Provisioning has stamped
/// <c>sprk_project.sprk_containerid</c> since task 021 and nothing read it, so every secure document landed
/// in a shared container. A test asserting "the resolver is registered in DI" would have passed throughout
/// that entire period (ADR-038 bans exactly that shape). These assert the DECISION on a record, and each one
/// fails if the secure branch is removed.</para>
///
/// <para><b>The thing that makes the failure irreversible</b>, and therefore the reason a returned-and-ignored
/// error was not good enough: SharePoint Embedded permissions are additive-only — inheritance cannot be
/// broken on an individual file — so no later per-item permission can retract a document from a shared
/// container. Fail-closed here is the only mechanism available.</para>
/// </summary>
public class RecordContainerResolverTests
{
    private const string SecureProjectEntity = "sprk_project";
    private const string NonSecurableEntity = "sprk_invoice";
    private const string OwnContainer = "b!secure-own-container-0000000000";
    private const string SharedBuContainer = "b!shared-bu-container-000000000000";

    private static readonly Guid RecordId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ============================================================================================
    // FORWARD — the storage decision
    // ============================================================================================

    [Fact(DisplayName = "Task 075: a secure record resolves to its OWN container, not the shared fallback")]
    public async Task SecureRecord_ResolvesToItsOwnContainer()
    {
        var resolver = Build(
            securable: [SecureProjectEntity],
            record: Row(isSecure: true, containerId: OwnContainer));

        var decision = await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, nonSecureFallbackContainerId: SharedBuContainer);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedSecure);

        decision.ContainerId.Should().Be(
            OwnContainer,
            "this is the whole point of the seam — provisioning stamped a container and it must now be read. "
            + "Resolving to the shared BU container here is the isolation failure task 075 exists to remove.");

        decision.ContainerId.Should().NotBe(SharedBuContainer);
    }

    [Fact(DisplayName = "Task 075: a secure record with NO container FAILS CLOSED even though a fallback is available")]
    public async Task SecureRecord_WithoutContainer_FailsClosed_AndDoesNotFallBack()
    {
        // THE most important assertion in the task. A usable fallback is deliberately supplied: the failure
        // mode being prevented is not "no container available" but "a shared container was available and got
        // used silently, and the upload succeeded".
        var resolver = Build(
            securable: [SecureProjectEntity],
            record: Row(isSecure: true, containerId: null));

        var act = async () => await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, nonSecureFallbackContainerId: SharedBuContainer);

        var ex = await act.Should().ThrowAsync<SdapProblemException>();

        ex.Which.Code.Should().Be("secure_record_container_missing");
        ex.Which.StatusCode.Should().Be(409);
    }

    [Theory(DisplayName = "Task 075: every blank form of a secure record's container fails closed")]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public async Task SecureRecord_WithBlankContainer_FailsClosed(string? containerId)
    {
        // Dataverse returns an unset NVARCHAR as an empty string as readily as null. A null-only check would
        // resolve to a blank container id and surface as a confusing Graph error rather than a refusal.
        var resolver = Build(
            securable: [SecureProjectEntity],
            record: Row(isSecure: true, containerId: containerId));

        var act = async () => await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, SharedBuContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("secure_record_container_missing");
    }

    [Fact(DisplayName = "Task 075: a NON-secure record still resolves through the BU cascade, unchanged")]
    public async Task NonSecureRecord_ResolvesThroughTheBusinessUnitCascade()
    {
        var resolver = Build(
            securable: [SecureProjectEntity],
            record: Row(isSecure: false, containerId: null));

        var decision = await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, nonSecureFallbackContainerId: SharedBuContainer);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback);
        decision.ContainerId.Should().Be(SharedBuContainer, "non-secure behaviour must not change");
    }

    [Fact(DisplayName = "Task 075: a non-secure record's own stamped container is IGNORED in favour of the cascade")]
    public async Task NonSecureRecord_IgnoresItsOwnStampedContainer()
    {
        // Deliberate. Three live projects currently carry the ROOT business unit's container id because the
        // creation wizard's BU cascade writes this column (which task 076 removes). Reading a non-secure
        // record's own stamp would silently redirect content for any record carrying a stale one.
        var resolver = Build(
            securable: [SecureProjectEntity],
            record: Row(isSecure: false, containerId: "b!some-stale-stamp-00000000000000"));

        var decision = await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, nonSecureFallbackContainerId: SharedBuContainer);

        decision.ContainerId.Should().Be(SharedBuContainer);
    }

    [Fact(DisplayName = "Task 075: a non-securable entity never reads the record at all")]
    public async Task NonSecurableEntity_ShortCircuits_WithoutReadingTheRecord()
    {
        // Both a correctness and a cost assertion: an entity that cannot carry sprk_issecure cannot be
        // secure, so the fallback is right AND no Dataverse round trip should be spent proving it. That is
        // what keeps this seam cheap enough to sit on every upload path.
        var entityService = Substitute.For<IGenericEntityService>();
        var resolver = Build(securable: [SecureProjectEntity], entityService: entityService);

        var decision = await resolver.ResolveForRecordAsync(
            NonSecurableEntity, RecordId, nonSecureFallbackContainerId: SharedBuContainer);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.ResolvedFallback);
        decision.ContainerId.Should().Be(SharedBuContainer);

        await entityService.DidNotReceiveWithAnyArgs()
            .RetrieveAsync(default!, default, default!, default);
    }

    [Fact(DisplayName = "Task 075: 'unresolved' is reachable ONLY for a non-secure record")]
    public async Task Unresolved_IsReachable_OnlyForANonSecureRecord()
    {
        // Preserves the existing skip on an unconfigured Communication:ArchiveContainerId — a config absence
        // on a non-secure path is not a security event and must not start throwing.
        var nonSecure = Build(
            securable: [SecureProjectEntity],
            record: Row(isSecure: false, containerId: null));

        var decision = await nonSecure.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, nonSecureFallbackContainerId: null);

        decision.Outcome.Should().Be(ContainerDecisionOutcome.Unresolved);
        decision.ContainerId.Should().BeNull();

        // The same inputs on a SECURE record must throw rather than reach the quiet-skip path, because a
        // caller treating Unresolved as "skip" cannot tell it apart from success.
        var secure = Build(
            securable: [SecureProjectEntity],
            record: Row(isSecure: true, containerId: null));

        var act = async () => await secure.ResolveForRecordAsync(SecureProjectEntity, RecordId, null);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("secure_record_container_missing");
    }

    // ============================================================================================
    // FORWARD — "I could not find out" must not become "not secure"
    // ============================================================================================

    [Fact(DisplayName = "Task 075: a metadata failure PROPAGATES rather than defaulting to not-secure")]
    public async Task MetadataFailure_Propagates_AndDoesNotDefaultToNonSecure()
    {
        // The subtle version of the same bug. If an unavailable metadata service were read as "this entity
        // is not securable", every record would silently resolve to the shared fallback — the identical
        // isolation failure, with an extra step and no log line saying so.
        var registry = Substitute.For<ISecurableEntityRegistry>();
        registry.IsSecurableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("Dataverse metadata unavailable"));

        var resolver = new RecordContainerResolver(
            registry, Substitute.For<IGenericEntityService>(),
            NullLogger<RecordContainerResolver>.Instance);

        var act = async () => await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, SharedBuContainer);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "an undetermined securability answer must never resolve to a shared container");
    }

    [Fact(DisplayName = "Task 075: a record-read failure PROPAGATES rather than defaulting to not-secure")]
    public async Task RecordReadFailure_Propagates()
    {
        var entityService = Substitute.For<IGenericEntityService>();
        entityService.RetrieveAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TimeoutException("Dataverse timed out"));

        var resolver = Build(securable: [SecureProjectEntity], entityService: entityService);

        var act = async () => await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, SharedBuContainer);

        await act.Should().ThrowAsync<TimeoutException>();
    }

    [Fact(DisplayName = "Task 075: a securable entity with an empty record id refuses rather than falling back")]
    public async Task SecurableEntity_WithEmptyRecordId_Refuses()
    {
        var resolver = Build(securable: [SecureProjectEntity]);

        var act = async () => await resolver.ResolveForRecordAsync(
            SecureProjectEntity, Guid.Empty, SharedBuContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("container_record_not_found");
    }

    [Fact(DisplayName = "Task 075: a missing securable record refuses rather than falling back")]
    public async Task MissingSecurableRecord_Refuses()
    {
        var resolver = Build(securable: [SecureProjectEntity], record: null);

        var act = async () => await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, SharedBuContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("container_record_not_found");
    }

    // ============================================================================================
    // REVERSE — container -> owning record (consumed by tasks 073 and 078)
    // ============================================================================================

    [Fact(DisplayName = "Task 075 reverse: a secure record's container resolves back to that record")]
    public async Task Reverse_ResolvesTheOwningSecureRecord()
    {
        var resolver = Build(
            securable: [SecureProjectEntity],
            claimants: [(SecureProjectEntity, RecordId, true)]);

        var owner = await resolver.ResolveOwningRecordAsync(OwnContainer);

        owner.Should().NotBeNull();
        owner!.EntityLogicalName.Should().Be(SecureProjectEntity);
        owner.RecordId.Should().Be(RecordId, "this is the authorization subject for tasks 073 and 078");
    }

    [Fact(DisplayName = "Task 075 reverse: a shared container with no secure claimant resolves to null, not an error")]
    public async Task Reverse_SharedContainer_ResolvesToNull()
    {
        // The live state: three projects share the ROOT BU container id, all non-secure. "No record owns
        // this container" is an ANSWER — 073/078 decide what it means for them — not a failure.
        var resolver = Build(
            securable: [SecureProjectEntity],
            claimants:
            [
                (SecureProjectEntity, Guid.NewGuid(), false),
                (SecureProjectEntity, Guid.NewGuid(), false),
                (SecureProjectEntity, Guid.NewGuid(), false)
            ]);

        var owner = await resolver.ResolveOwningRecordAsync(SharedBuContainer);

        owner.Should().BeNull();
    }

    [Fact(DisplayName = "Task 075 reverse: a container claimed by no record at all resolves to null")]
    public async Task Reverse_UnclaimedContainer_ResolvesToNull()
    {
        var resolver = Build(securable: [SecureProjectEntity], claimants: []);

        (await resolver.ResolveOwningRecordAsync(SharedBuContainer)).Should().BeNull();
    }

    [Fact(DisplayName = "Task 075 reverse: a secure record SHARING its container with a non-secure record refuses")]
    public async Task Reverse_SecureSharingWithNonSecure_Refuses()
    {
        // This is the condition the whole wave exists to prevent, observed from the other direction: a
        // secure record's container is also some non-secure record's container, so content is co-mingled.
        // Naming one owner would authorize against the wrong record; SPE cannot retract the co-mingling.
        var resolver = Build(
            securable: [SecureProjectEntity],
            claimants:
            [
                (SecureProjectEntity, RecordId, true),
                (SecureProjectEntity, Guid.NewGuid(), false)
            ],
            storedContainerOverride: SharedBuContainer);

        var act = async () => await resolver.ResolveOwningRecordAsync(SharedBuContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("container_ownership_ambiguous");
    }

    [Fact(DisplayName = "Task 075 reverse: two secure records claiming one container refuses")]
    public async Task Reverse_TwoSecureClaimants_Refuses()
    {
        var resolver = Build(
            securable: [SecureProjectEntity],
            claimants:
            [
                (SecureProjectEntity, Guid.NewGuid(), true),
                (SecureProjectEntity, Guid.NewGuid(), true)
            ]);

        var act = async () => await resolver.ResolveOwningRecordAsync(OwnContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("container_ownership_ambiguous");
    }

    [Fact(DisplayName = "Task 075 reverse (C-1): a PADDED stored container still resolves to its owner")]
    public async Task Reverse_PaddedStoredContainer_StillResolves()
    {
        // C-1 regression. The forward direction trims, so a record stamped "  b!x  " stores content in b!x.
        // If the reverse direction filtered Dataverse on the trimmed value it would MISS that row — Dataverse
        // does not trim stored values — yielding zero secure claimants and the fail-OPEN answer "this is an
        // ordinary shared container". Tasks 073/078 would then authorize a secure container as unowned.
        var resolver = Build(
            securable: [SecureProjectEntity],
            claimants: [(SecureProjectEntity, RecordId, true)],
            storedContainerOverride: $"  {OwnContainer}  ");

        var owner = await resolver.ResolveOwningRecordAsync(OwnContainer);

        owner.Should().NotBeNull("a padded stored container id must still resolve to its owning record");
        owner!.RecordId.Should().Be(RecordId);
    }

    [Fact(DisplayName = "Task 075 reverse (C-1): a container that merely CONTAINS the query as a substring is not a match")]
    public async Task Reverse_SubstringContainer_IsNotAMatch()
    {
        // The other half of C-1: the fix must not over-match. SPE drive ids routinely contain '_', which is a
        // LIKE single-character wildcard, so a `Like '%…%'` filter would match unrelated containers. Matching
        // is exact-after-trim, so a superstring is not an owner.
        var resolver = Build(
            securable: [SecureProjectEntity],
            claimants: [(SecureProjectEntity, RecordId, true)],
            storedContainerOverride: OwnContainer + "-suffix");

        (await resolver.ResolveOwningRecordAsync(OwnContainer)).Should().BeNull();
    }

    [Fact(DisplayName = "Task 075 reverse (C-2): hitting the probe bound REFUSES instead of reporting 'unowned'")]
    public async Task Reverse_ProbeTruncation_Refuses()
    {
        // C-2 regression. TopCount does not populate MoreRecords, so truncation is invisible. If the probe
        // silently truncated, a secure claimant outside the page would read as absent → null → "shared
        // container". Reaching the bound means ownership is genuinely unknown, so it refuses.
        var manyClaimants = Enumerable.Range(0, 25)
            .Select(_ => (SecureProjectEntity, Guid.NewGuid(), true))
            .ToArray();

        var resolver = Build(
            securable: [SecureProjectEntity],
            claimants: manyClaimants,
            storedContainerOverride: "b!some-other-container-0000000");

        var act = async () => await resolver.ResolveOwningRecordAsync(OwnContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("container_ownership_indeterminate");
    }

    [Fact(DisplayName = "Task 075 reverse: a blank container id resolves to null without querying")]
    public async Task Reverse_BlankContainerId_ResolvesToNull()
    {
        var entityService = Substitute.For<IGenericEntityService>();
        var resolver = Build(securable: [SecureProjectEntity], entityService: entityService);

        (await resolver.ResolveOwningRecordAsync("   ")).Should().BeNull();

        await entityService.DidNotReceiveWithAnyArgs()
            .RetrieveMultipleAsync(default(QueryExpression)!, default);
    }

    // ============================================================================================
    // Builders
    // ============================================================================================

    private static Entity Row(bool isSecure, string? containerId)
    {
        var row = new Entity(SecureProjectEntity, RecordId)
        {
            ["sprk_issecure"] = isSecure
        };

        if (containerId is not null)
        {
            row["sprk_containerid"] = containerId;
        }

        return row;
    }

    private static RecordContainerResolver Build(
        string[] securable,
        Entity? record = null,
        (string Entity, Guid Id, bool IsSecure)[]? claimants = null,
        IGenericEntityService? entityService = null,
        string? storedContainerOverride = null)
    {
        var registry = Substitute.For<ISecurableEntityRegistry>();
        var set = new HashSet<string>(securable.Select(s => s.ToLowerInvariant()), StringComparer.Ordinal);

        registry.GetSecurableEntitiesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlySet<string>>(set));
        registry.IsSecurableAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(set.Contains(call.Arg<string>().ToLowerInvariant())));

        var svc = entityService ?? Substitute.For<IGenericEntityService>();

        if (entityService is null)
        {
            svc.RetrieveAsync(
                    Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(record!));

            // The resolver's reverse direction issues TWO queries per securable entity, and the split is the
            // C-2 fix, so this double must answer them SEPARATELY rather than returning one blended
            // collection — otherwise the test would pass against the truncation bug it exists to catch.
            //
            //   secure probe   — selects sprk_containerid, filters sprk_issecure == true
            //   co-mingle probe — selects nothing, TopCount 1, filters sprk_issecure != true
            //
            // Discriminated on whether the ColumnSet asks for the container column.
            var stored = storedContainerOverride ?? OwnContainer;

            svc.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var query = call.Arg<QueryExpression>();
                    var isSecureProbe = query.ColumnSet?.Columns?.Contains("sprk_containerid") == true;

                    var collection = new EntityCollection();

                    foreach (var (entity, id, isSecure) in claimants ?? [])
                    {
                        if (isSecure != isSecureProbe)
                        {
                            continue;
                        }

                        var row = new Entity(entity, id) { ["sprk_issecure"] = isSecure };

                        if (isSecureProbe)
                        {
                            // Only the secure probe selects the container column, and the resolver matches on
                            // it in code (trim-tolerant, exact) rather than in the filter — that is the C-1
                            // fix, so the stored value is what these tests vary.
                            row["sprk_containerid"] = stored;
                        }

                        collection.Entities.Add(row);
                    }

                    return Task.FromResult(collection);
                });
        }

        return new RecordContainerResolver(registry, svc, NullLogger<RecordContainerResolver>.Instance);
    }
}
