using System.ServiceModel;
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

    [Fact(DisplayName = "Task 075 reverse (C-2): a page full of claimants of THIS container refuses")]
    public async Task Reverse_ProbeTruncation_Refuses()
    {
        // C-2 regression. TopCount does not populate MoreRecords, so a full page is the only signal that a
        // claimant may lie beyond it. 25 secure records all claiming ONE container is pathological
        // co-mingling in its own right, so refusing is both honest and correct.
        //
        // NOTE this test was rewritten: its first version built 25 secure claimants whose stored container
        // did NOT match, which is the N-1 defect (a probe with no container filter) dressed up as intended
        // behaviour. The rows must actually claim the queried container for the bound to mean anything.
        var manyClaimants = Enumerable.Range(0, 25)
            .Select(_ => (SecureProjectEntity, Guid.NewGuid(), true))
            .ToArray();

        var resolver = Build(
            securable: [SecureProjectEntity],
            claimants: manyClaimants,
            storedContainerOverride: OwnContainer);

        var act = async () => await resolver.ResolveOwningRecordAsync(OwnContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("container_ownership_indeterminate");
    }

    [Fact(DisplayName = "Task 075 reverse (N-1): 25 secure records that DON'T claim this container must not break resolution")]
    public async Task Reverse_ManyUnrelatedSecureRecords_StillResolvesTheOwner()
    {
        // N-1 regression, and the reason it matters: 25 secure records EACH WITH THEIR OWN CONTAINER is the
        // intended steady state of this feature, not an edge case.
        //
        // The first version of the C-1/C-2 fix filtered the secure probe on `sprk_issecure == true AND
        // sprk_containerid NOT NULL` with no container-VALUE condition. It therefore returned "any 25 secure
        // records" rather than "claimants of THIS container", the page filled as soon as the org merely HELD
        // 25 secure records, and the truncation guard then threw for EVERY container — including the correct
        // owner's. Tasks 073 (write) and 078 (read) would have been permanently dead at a trivially
        // reachable number. Fail-closed, so not a disclosure, but a hard availability cliff.
        var unrelated = Enumerable.Range(0, 25)
            .Select(i => new Claimant(SecureProjectEntity, Guid.NewGuid(), true, $"b!unrelated-container-{i:D4}"))
            .Append(new Claimant(SecureProjectEntity, RecordId, true, OwnContainer))
            .ToArray();

        var resolver = Build(securable: [SecureProjectEntity], explicitClaimants: unrelated);

        var owner = await resolver.ResolveOwningRecordAsync(OwnContainer);

        owner.Should().NotBeNull(
            "the probe must be scoped to claimants of THIS container, so unrelated secure records — however "
            + "many — cannot fill the page and turn every lookup into a refusal");
        owner!.RecordId.Should().Be(RecordId);
    }

    [Fact(DisplayName = "Task 075 reverse (N-2): a PADDED non-secure claimant is still detected as co-mingling")]
    public async Task Reverse_PaddedNonSecureClaimant_IsDetected()
    {
        // N-2 regression. The co-mingling probe originally filtered `container Equal <trimmed>` with
        // ColumnSet(false) — verbatim the C-1 defect, mirrored onto the detector. A non-secure record stamped
        // "  b!x  " sharing a secure record's b!x was invisible, so nonSecureClaimantCount stayed 0, the
        // ambiguity refusal never fired, and the reverse lookup named the secure record as SOLE owner of a
        // co-mingled container. Fail-open on exactly the condition this wave exists to detect.
        var resolver = Build(
            securable: [SecureProjectEntity],
            explicitClaimants:
            [
                new Claimant(SecureProjectEntity, RecordId, true, OwnContainer),
                new Claimant(SecureProjectEntity, Guid.NewGuid(), false, $"  {OwnContainer}  ")
            ]);

        var act = async () => await resolver.ResolveOwningRecordAsync(OwnContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("container_ownership_ambiguous");
    }

    [Fact(DisplayName = "Task 075 reverse (N-3): a NULL-flagged non-secure claimant is still detected as co-mingling")]
    public async Task Reverse_NullFlaggedNonSecureClaimant_IsDetected()
    {
        // N-3 regression. `sprk_issecure NotEqual true` is SQL `<> 1`, and `NULL <> 1` is UNKNOWN, so rows
        // with a NULL flag were EXCLUDED from the co-mingling probe. Those rows are legitimate and expected —
        // Dataverse does not back-fill a Two Options column on existing rows, and field-level security
        // returns the row with the attribute masked rather than erroring. A second, independent blind spot in
        // the same detector.
        var resolver = Build(
            securable: [SecureProjectEntity],
            explicitClaimants:
            [
                new Claimant(SecureProjectEntity, RecordId, true, OwnContainer),
                new Claimant(SecureProjectEntity, Guid.NewGuid(), null, OwnContainer)
            ]);

        var act = async () => await resolver.ResolveOwningRecordAsync(OwnContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("container_ownership_ambiguous");
    }

    [Fact(DisplayName = "Task 075 (N-4): a typed ObjectDoesNotExist fault becomes the documented 404")]
    public async Task RecordNotFound_IsClassifiedFromTheTypedFault()
    {
        var entityService = Substitute.For<IGenericEntityService>();
        entityService.RetrieveAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147220969 },
                // Deliberately NOT an English "does not exist" message: the classification must come from the
                // error code, because Dataverse fault messages are localized and the old substring match
                // silently stopped working on a non-English org.
                new FaultReason("Die angeforderte Entität existiert nicht.")));

        var resolver = Build(securable: [SecureProjectEntity], entityService: entityService);

        var act = async () => await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, SharedBuContainer);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("container_record_not_found");
    }

    [Fact(DisplayName = "Task 075 (N-4): a schema/FLS 'attribute was not found' fault is NOT mis-reported as a missing record")]
    public async Task AttributeNotFoundFault_IsNotMisclassifiedAsRecordNotFound()
    {
        // The over-breadth half of N-4. "Attribute sprk_issecure was not found" is a schema or field-level
        // security error — precisely the masked-attribute case the absent-flag warning exists to surface —
        // and reporting it to an operator as "the record does not exist" misdiagnoses it. It must propagate.
        var entityService = Substitute.For<IGenericEntityService>();
        entityService.RetrieveAsync(
                Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = -2147217149 },
                new FaultReason("Attribute sprk_issecure was not found in the metadata cache.")));

        var resolver = Build(securable: [SecureProjectEntity], entityService: entityService);

        var act = async () => await resolver.ResolveForRecordAsync(
            SecureProjectEntity, RecordId, SharedBuContainer);

        await act.Should().ThrowAsync<FaultException<OrganizationServiceFault>>(
            "a schema or field-level-security fault must propagate, not be relabelled as a missing record");
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

    /// <summary>
    /// A claimant of a container, in the shape the reverse-lookup probes actually see.
    /// </summary>
    /// <param name="Entity">Entity logical name.</param>
    /// <param name="Id">Record id.</param>
    /// <param name="Flag">
    /// The <c>sprk_issecure</c> value. <c>null</c> means the attribute is ABSENT on the returned row — a NULL
    /// flag, which is what Dataverse returns for an unset Two Options column and for a field-level-security
    /// masked value. That is exactly the shape a <c>NotEqual true</c>-only filter silently excludes.
    /// </param>
    /// <param name="Stored">The stored <c>sprk_containerid</c> value, verbatim (may be padded).</param>
    private sealed record Claimant(string Entity, Guid Id, bool? Flag, string Stored);

    /// <summary>
    /// Evaluates the query's real <c>sprk_issecure</c> conditions against a row's flag value under SQL
    /// three-valued logic, so the double reflects what Dataverse would return rather than what the probe was
    /// intended to mean.
    ///
    /// <para>The load-bearing rule is <c>NotEqual</c>: it maps to SQL <c>&lt;&gt; 1</c>, and <c>NULL &lt;&gt;
    /// 1</c> is UNKNOWN, so a NULL-flagged row is EXCLUDED by it. Modelling that is what makes the N-3
    /// regression test bite instead of passing vacuously.</para>
    /// </summary>
    private static bool FlagConditionsMatch(QueryExpression query, bool isSecureProbe, bool? flag)
    {
        const string flagAttribute = "sprk_issecure";

        static bool Satisfies(ConditionExpression condition, bool? flag) => condition.Operator switch
        {
            // NULL = 1 is UNKNOWN → excluded.
            ConditionOperator.Equal => flag.HasValue && flag.Value == (bool)condition.Values[0],

            // NULL <> 1 is UNKNOWN → excluded. THE N-3 SEMANTIC.
            ConditionOperator.NotEqual => flag.HasValue && flag.Value != (bool)condition.Values[0],

            ConditionOperator.Null => !flag.HasValue,
            ConditionOperator.NotNull => flag.HasValue,

            _ => throw new InvalidOperationException(
                $"The test double does not model ConditionOperator.{condition.Operator} on {flagAttribute}. "
                + "Add it rather than defaulting, or a query change will silently stop being verified.")
        };

        var topLevel = query.Criteria.Conditions
            .Where(c => c.AttributeName == flagAttribute)
            .ToList();

        // Top-level flag conditions are ANDed (the secure probe's `flag Equal true`).
        if (topLevel.Any(c => !Satisfies(c, flag)))
        {
            return false;
        }

        // Nested filters are the co-mingle probe's Or group.
        foreach (var nested in query.Criteria.Filters)
        {
            var conditions = nested.Conditions.Where(c => c.AttributeName == flagAttribute).ToList();
            if (conditions.Count == 0)
            {
                continue;
            }

            var satisfied = nested.FilterOperator == LogicalOperator.Or
                ? conditions.Any(c => Satisfies(c, flag))
                : conditions.All(c => Satisfies(c, flag));

            if (!satisfied)
            {
                return false;
            }
        }

        // Guard against a probe that carries no flag condition at all — that would make both passes see every
        // row and the secure/non-secure split would stop being tested.
        if (topLevel.Count == 0 && query.Criteria.Filters.Count == 0)
        {
            throw new InvalidOperationException(
                $"A reverse-lookup probe carried NO {flagAttribute} condition (isSecureProbe={isSecureProbe}). "
                + "Both passes would then see every row and the secure/non-secure split would be untested.");
        }

        return true;
    }

    /// <summary>
    /// Evaluates the query's real <c>sprk_containerid</c> condition against a row's stored value, honouring
    /// the operator the resolver actually used.
    ///
    /// <para><b>Why this evaluates rather than assumes.</b> An earlier version looked only for a
    /// <c>Like</c> condition and fell back to "match everything" when it found none. That made the N-2 test
    /// pass VACUOUSLY: reverting the co-mingle probe to <c>Equal</c> removed the Like, the fallback matched
    /// every row, and the padded claimant was still "detected" — so the perturbation did not bite. A double
    /// that defaults to permissive cannot test a filter.</para>
    ///
    /// <para><c>Like</c> is mirrored as a substring match after unescaping the bracket forms the resolver
    /// applies (<c>[_]</c> → <c>_</c>), because that is what T-SQL <c>LIKE '%…%'</c> means — deliberately
    /// WIDER than the answer, with the resolver's code-side trim-compare as the authority. <c>Equal</c> is
    /// mirrored as an exact, UNTRIMMED comparison, which is precisely why it misses a padded stored value.
    /// </para>
    /// </summary>
    private static bool ContainerConditionMatches(QueryExpression query, string stored)
    {
        const string containerAttribute = "sprk_containerid";

        var conditions = query.Criteria.Conditions
            .Concat(query.Criteria.Filters.SelectMany(f => f.Conditions))
            .Where(c => c.AttributeName == containerAttribute)
            .ToList();

        if (conditions.Count == 0)
        {
            throw new InvalidOperationException(
                $"A reverse-lookup probe carried NO {containerAttribute} condition. The probe would then "
                + "return 'any N secure records' rather than claimants of the queried container — the N-1 "
                + "defect — so the double refuses to model it as a match-all.");
        }

        foreach (var condition in conditions)
        {
            var value = condition.Values.FirstOrDefault() as string ?? string.Empty;

            var matches = condition.Operator switch
            {
                ConditionOperator.Like => stored.Contains(
                    value.Trim('%')
                        .Replace("[_]", "_", StringComparison.Ordinal)
                        .Replace("[%]", "%", StringComparison.Ordinal)
                        .Replace("[[]", "[", StringComparison.Ordinal),
                    StringComparison.Ordinal),

                // Exact and UNTRIMMED — this is the operator whose use on a padded stored value is the bug.
                ConditionOperator.Equal => string.Equals(stored, value, StringComparison.Ordinal),

                ConditionOperator.NotNull => !string.IsNullOrEmpty(stored),
                ConditionOperator.Null => string.IsNullOrEmpty(stored),

                _ => throw new InvalidOperationException(
                    $"The test double does not model ConditionOperator.{condition.Operator} on "
                    + $"{containerAttribute}. Add it rather than defaulting, or a query change will silently "
                    + "stop being verified.")
            };

            if (!matches)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Normalizes the simple tuple form plus any explicit <see cref="Claimant"/> rows into one list, and
    /// computes which probe each row belongs to. Pass 1 sees <c>flag == true</c>; pass 2 sees
    /// <c>flag != true OR flag IS NULL</c>.
    /// </summary>
    private static List<(Claimant Row, bool MatchesSecureProbe)> ExpandClaimants(
        (string Entity, Guid Id, bool IsSecure)[]? simple,
        Claimant[]? explicitRows,
        string stored)
    {
        var result = new List<(Claimant, bool)>();

        foreach (var (entity, id, isSecure) in simple ?? [])
        {
            result.Add((new Claimant(entity, id, isSecure, stored), isSecure));
        }

        foreach (var row in explicitRows ?? [])
        {
            result.Add((row, row.Flag == true));
        }

        return result;
    }

    private static RecordContainerResolver Build(
        string[] securable,
        Entity? record = null,
        (string Entity, Guid Id, bool IsSecure)[]? claimants = null,
        IGenericEntityService? entityService = null,
        string? storedContainerOverride = null,
        Claimant[]? explicitClaimants = null)
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

            // The reverse direction issues TWO queries per securable entity (the C-2 fix), so this double
            // must answer them SEPARATELY — a blended collection would pass against the very truncation bug
            // the split exists to remove.
            //
            //   pass 1, secure probe    — filters sprk_issecure == true AND container LIKE …
            //   pass 2, co-mingle probe — filters container LIKE … AND a NESTED Or on the flag
            //
            // DISCRIMINATED ON THE NESTED FILTER, deliberately not on the ColumnSet. Both probes now select
            // sprk_containerid — they must, because the LIKE filter is wider than the answer and the exact
            // compare happens in code — so a ColumnSet-based discriminator would silently route both queries
            // to the same branch and the suite would mis-report. The nested Or is unique to pass 2 and is
            // load-bearing there (it is the NULL-flag fix), so it is a stable key.
            var stored = storedContainerOverride ?? OwnContainer;

            svc.RetrieveMultipleAsync(Arg.Any<QueryExpression>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var query = call.Arg<QueryExpression>();
                    var isSecureProbe = (query.Criteria?.Filters?.Count ?? 0) == 0;

                    var collection = new EntityCollection();

                    foreach (var (claimant, _) in
                             ExpandClaimants(claimants, explicitClaimants, stored))
                    {
                        // Evaluate the query's ACTUAL flag conditions against the row, under SQL three-valued
                        // logic — do NOT assume what the probe "means".
                        //
                        // An earlier version of this double classified rows by `Flag == true` and routed them
                        // to pass 1 / pass 2 accordingly. That made the N-3 test pass VACUOUSLY: reverting the
                        // nested Or to `NotEqual true` alone changed the query but not the double, so the
                        // NULL-flag row still reached the co-mingling probe and the perturbation did not bite.
                        // Modelling `NULL <> 1 → UNKNOWN → excluded` is the whole point of that test.
                        if (!FlagConditionsMatch(query, isSecureProbe, claimant.Flag))
                        {
                            continue;
                        }

                        // The LIKE filter is also mirrored, because it is wider than the answer: it matches a
                        // superstring, and the resolver's code-side compare is what narrows it. A double that
                        // pre-filtered exactly would hide whether that compare exists at all.
                        if (!ContainerConditionMatches(query, claimant.Stored))
                        {
                            continue;
                        }

                        var row = new Entity(claimant.Entity, claimant.Id);

                        // A NULL flag is represented by the attribute being ABSENT, which is how Dataverse
                        // returns an unset Two Options column — and is exactly the shape the NotEqual-only
                        // filter used to exclude.
                        if (claimant.Flag.HasValue)
                        {
                            row["sprk_issecure"] = claimant.Flag.Value;
                        }

                        row["sprk_containerid"] = claimant.Stored;

                        collection.Entities.Add(row);
                    }

                    return Task.FromResult(collection);
                });
        }

        return new RecordContainerResolver(registry, svc, NullLogger<RecordContainerResolver>.Instance);
    }
}
