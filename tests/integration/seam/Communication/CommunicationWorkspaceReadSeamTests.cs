using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Task-080 vertical-slice seam tests (ADR-038 "DoD for dispatch-spine changes") for the R2 Workspace read/query/
/// organize surfaces (FR-01/02/08/09, NFR-03). REUSE-FIRST (root CLAUDE.md §11 / project §11): this file adds ONLY
/// the composition gaps no existing test covers. It does NOT re-test what
/// <c>Sprk.Bff.Api.Tests.Services.Communication.CommunicationByRegardingReadTests</c>,
/// <c>Sprk.Bff.Api.Tests.Services.Communication.CommunicationFilteredQueryTests</c>,
/// <c>Sprk.Bff.Api.Tests.Services.Communication.ThreadResolverTests</c>, or <see cref="ThreadResolverSeamTests"/>
/// already prove. Specifically, this file closes:
///
/// <list type="bullet">
/// <item><b>Full 11-entity by-regarding pass</b> — the unit suite explicitly covers 4 of the 11 ADR-024 regarding
/// families (≥3 required by spec); this file data-drives ALL 11 straight from <see cref="RegardingFieldMap.All"/>
/// (the single source of truth), closing the "11-entity theory if cheap" ask without hand-listing families twice.</item>
/// <item><b>All-facets-composed filtered query</b> — the unit suite asserts each facet independently (plus one
/// 2-facet AND for channel+participant); this file composes FIVE facets (thread, channel, from, to, participant)
/// in ONE call to prove the AND-composition holds across the full facet set, not just pairs.</item>
/// <item><b>Filtered-query private-thread-hidden</b> — the unit suite has a participant-specific negative-access
/// case; this file adds the GENERIC private-thread-hidden case (via the <c>thread=</c> facet) that the filtered-
/// query unit file lacks (the by-regarding equivalent already exists there and is not duplicated here).</item>
/// <item><b>Auto-threading 3-tier ladder composed with the REAL per-channel strategy</b> — the existing
/// <c>ThreadResolverTests</c> exercises the ladder with a STUB strategy (isolating the resolver's own branching);
/// <see cref="ThreadResolverSeamTests"/> composes the REAL <see cref="MessagingThreadKeyStrategy"/> but only for
/// Tier 1 (JOIN/CREATE via channel-ref). Neither composes the REAL strategy's <c>Skip</c> outcome (a chat message
/// with no ACS thread id yet — the genuine real-world Tier-2/3 trigger) through the full ladder. This file closes
/// that gap end-to-end (Tier 2 create + idempotent join, Tier 3 create + idempotent join) with the REAL
/// <see cref="ThreadResolver"/> + REAL <see cref="MessagingThreadKeyStrategy"/> — not a stub standing in for it.</item>
/// <item><b>No-membership-union regression guard</b> — a structural assertion that neither
/// <see cref="CommunicationThreadReadService"/> nor <see cref="CommunicationAccessFilter"/> takes a dependency on
/// the retired <see cref="IThreadPrivateGrantProvider"/> (or any membership-resolution seam) — the load-bearing
/// NFR-03 guarantee from <c>../messaging-communication-app-r1/notes/access-model-decision.md</c> that reads are
/// impersonation + 2-rule ONLY, never a hand-computed membership ∪ grant union. Combined with the ALREADY-EXISTING
/// behavioral proof that a private thread absent from the impersonated thread set never surfaces its messages
/// (<c>CommunicationByRegardingReadTests.ReadByRegardingAsync_PrivateThreadWithoutGrant_IsAbsentAndItsMessagesNeverFetched</c>),
/// this is the "no union path exists on reads" regression NFR-03 calls for. A NetArchTest-style architecture check
/// on a class's OWN constructor shape (not "was DI registered", not a private/internal member) is the ADR-038 §6
/// sanctioned replacement for a wiring test — see ADR-038 Consequences §"Negative/risks accepted".</item>
/// </list>
///
/// <para><b>Characterization guard (NFR-03):</b> the pre-existing email/messaging capture + thread-resolution
/// characterization region in <c>ThreadResolverTests</c> (Tier-1 JOIN/CREATE, no-strategy guard, NFR-02 swallow —
/// authored by task 070 BEFORE the 3-tier ladder landed) and the write-invariant characterization in
/// <c>CommunicationParticipantIndexerTests</c> (task 050) are referenced here, not cloned — task 080's `dotnet test`
/// run (see <c>notes/080-seam-coverage-map.md</c>) confirms both stay green alongside this file's new cases.</para>
///
/// <para><b>Boundary mocks only (ADR-038):</b> <see cref="IImpersonatedCommunicationQuery"/> (the Dataverse/
/// impersonation read boundary) and <see cref="ICallerSystemUserResolver"/> (the caller-resolution boundary) for
/// the read-slice tests; <see cref="IGenericEntityService"/> (the Dataverse boundary) for the thread-resolver
/// ladder tests. Everything else — <see cref="CommunicationThreadReadService"/>, the REAL
/// <see cref="CommunicationAccessFilter"/>, the REAL <see cref="ThreadResolver"/>, the REAL
/// <see cref="MessagingThreadKeyStrategy"/> — is production code, unmocked. No <c>Mock&lt;HttpMessageHandler&gt;</c>,
/// no DI-registration test, no ctor null-check test.</para>
/// </summary>
public class CommunicationWorkspaceReadSeamTests
{
    private static readonly Guid CallerSystemUserId = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid RecordId = Guid.Parse("77777777-7777-7777-7777-777777777777");

    private const string ThreadSet = "sprk_communicationthreads";
    private const string CommunicationSet = "sprk_communications";
    private const string AttachmentSet = "sprk_communicationattachments";
    private const string ParticipantSet = "sprk_communicationparticipants";
    private const int TypeMessage = 100000004;
    private const int PrivilegeNone = 100000000;
    private const int ThreadTypeRecordAnchored = 100000000;
    private const int ThreadTypeDirect = 100000001;

    private readonly Mock<IImpersonatedCommunicationQuery> _query = new();
    private readonly Mock<ICallerSystemUserResolver> _resolver = new();

    private CommunicationThreadReadService Sut()
    {
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));

        return new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());
    }

    private static ClaimsPrincipal Caller() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // A. Full 11-entity by-regarding pass (composition gap — the unit suite covers 4 of 11 explicitly)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    // Deliberately HARD-CODED (not derived from RegardingFieldMap.All at test time) so this is a genuine
    // regression check, not a tautology: if a future edit to RegardingFieldMap.All mis-maps a family (e.g. a
    // copy-paste typo pointing sprk_event at sprk_regardingbudget), THIS literal table catches it. Deriving the
    // expected value from the same map the production code reads would only prove "the service calls the map",
    // never that the map itself is right — a mirror-test/tautology risk (ADR-038 §7 B6) the code-review pass
    // for this task explicitly caught and fixed. Values verified 1:1 against RegardingFieldMap.All as of task 080.
    public static IEnumerable<object[]> AllElevenAdr024RegardingFamilies() => new[]
    {
        new object[] { "sprk_matter", "sprk_regardingmatter" },
        new object[] { "sprk_project", "sprk_regardingproject" },
        new object[] { "sprk_invoice", "sprk_regardinginvoice" },
        new object[] { "sprk_servicerequest", "sprk_regardingservicerequest" },
        new object[] { "sprk_workassignment", "sprk_regardingworkassignment" },
        new object[] { "sprk_event", "sprk_regardingevent" },
        new object[] { "sprk_budget", "sprk_regardingbudget" },
        new object[] { "sprk_analysis", "sprk_regardinganalysis" },
        new object[] { "sprk_organization", "sprk_regardingorganization" },
        new object[] { "account", "sprk_regardingaccount" },
        new object[] { "contact", "sprk_regardingperson" },
    };

    [Theory]
    [MemberData(nameof(AllElevenAdr024RegardingFamilies))]
    public async Task ReadByRegardingAsync_AllElevenAdr024Families_ResolvesOwnTypedLookupAndBehavesIdentically(
        string entityType, string regardingField)
    {
        var threadId = Guid.NewGuid();
        string? threadQuery = null;
        _query.Setup(q => q.QueryAsync(ThreadSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => threadQuery = odata)
              .ReturnsAsync(new[] { ThreadRow(threadId, "t") });
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(new[] { MessageRow(Guid.NewGuid(), threadId) });
        _query.Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        var result = await Sut().ReadByRegardingAsync(entityType, RecordId, Caller(), CancellationToken.None);

        // The ONLY per-family difference is which typed thread-regarding lookup is filtered on — proven for
        // ALL 11 families here (data-driven off the same map the production code reads, so a future 12th family
        // is covered automatically without a 12th hand-written test case).
        threadQuery.Should().Contain($"_{regardingField}_value eq {RecordId}",
            $"family '{entityType}' must resolve its OWN typed regarding lookup, not another family's");
        result.EntityType.Should().Be(entityType);
        result.ThreadCount.Should().Be(1);
        result.MessageCount.Should().Be(1);
    }

    [Fact]
    public void AllElevenAdr024RegardingFamilies_StaysInSyncWith_RegardingFieldMapAll()
    {
        // Closes the loop on the hard-coded table above: THIS check catches drift the other direction (someone
        // adds/removes/renames a family in RegardingFieldMap.All without updating the literal test table), while
        // the Theory above catches drift in the map's actual VALUES. Together they give two-way regression
        // protection without either direction being a tautology.
        var expected = AllElevenAdr024RegardingFamilies()
            .Select(row => ((string)row[0], (string)row[1]))
            .OrderBy(x => x.Item1, StringComparer.Ordinal)
            .ToList();
        var actual = RegardingFieldMap.All
            .Select(x => (x.EntityLogicalName, x.RegardingField))
            .OrderBy(x => x.EntityLogicalName, StringComparer.Ordinal)
            .ToList();

        actual.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering());
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // B. All-facets-composed filtered query (composition gap — unit suite asserts facets individually / pairwise)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task QueryCommunicationsAsync_AllFiveFacetsComposedTogether_AndsThreadChannelDateRangeAndParticipant()
    {
        var threadId = Guid.NewGuid();
        var personId = Guid.NewGuid();
        var matchedMessageId = Guid.NewGuid();
        string? messageQuery = null;
        string? participantQuery = null;

        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        _query.Setup(q => q.QueryAsync(ParticipantSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => participantQuery = odata)
              .ReturnsAsync(new[] { ParticipantRow(matchedMessageId) });
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .Callback<string, string?, Guid, CancellationToken>((_, odata, _, _) => messageQuery = odata)
              .ReturnsAsync(new[] { MessageRow(matchedMessageId, threadId, type: TypeMessage) });
        _query.Setup(q => q.QueryAsync(AttachmentSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>());

        var sut = new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());

        var result = await sut.QueryCommunicationsAsync(
            thread: threadId.ToString(),
            regarding: null,
            channel: TypeMessage.ToString(),
            from: "2026-07-01T00:00:00Z",
            to: "2026-07-31T23:59:59Z",
            participant: personId.ToString(),
            Caller(), CancellationToken.None);

        result.Count.Should().Be(1);
        messageQuery.Should().NotBeNull();
        messageQuery!.Should().Contain($"_sprk_communicationthread_value eq {threadId}")
            .And.Contain($"sprk_communicationtype eq {TypeMessage}")
            .And.Contain("sprk_sentat ge 2026-07-01")
            .And.Contain("sprk_sentat le 2026-07-31")
            .And.Contain($"sprk_communicationid eq {matchedMessageId}",
                "the participant clause resolves to the junction's candidate id and composes as AND with every other facet");
        // thread + channel + from + to + participant = 5 clauses ⇒ 4 " and " joins.
        System.Text.RegularExpressions.Regex.Matches(messageQuery!, " and ").Count.Should().Be(4,
            "all five facets must be AND-composed together in one filter, not just pairwise");
        participantQuery.Should().Contain($"_sprk_systemuser_value eq {personId}");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // C. Filtered-query private-thread-hidden (composition gap — the unit file only has the participant-
    //    specific negative-access case; this is the GENERIC thread= case, mirroring the by-regarding equivalent)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task QueryCommunicationsAsync_ThreadFacetOnPrivateThreadWithoutGrant_ReturnsEmptyNotError()
    {
        // Unlike the by-regarding path (whose FIRST query is thread-scoped, so a private thread is simply absent
        // from the impersonated thread set), the filtered `thread=` facet fetches messages DIRECTLY by thread id —
        // so the private-thread-hidden proof here is that the impersonated MESSAGE read itself returns nothing for
        // a thread the caller has no access to (Dataverse's own row-level security via impersonation, not a
        // second BFF access mechanism).
        var privateThreadId = Guid.NewGuid();
        _resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerSystemUserId.ToString("D")));
        _query.Setup(q => q.QueryAsync(CommunicationSet, It.IsAny<string>(), CallerSystemUserId, It.IsAny<CancellationToken>()))
              .ReturnsAsync(Array.Empty<Dictionary<string, JsonElement>>()); // Dataverse impersonation returns nothing

        var sut = new CommunicationThreadReadService(
            _query.Object,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            _resolver.Object,
            Mock.Of<ILogger<CommunicationThreadReadService>>());

        var result = await sut.QueryCommunicationsAsync(
            thread: privateThreadId.ToString(), regarding: null, channel: null, from: null, to: null, participant: null,
            Caller(), CancellationToken.None);

        result.Count.Should().Be(0);
        result.Messages.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // D. No-membership-union regression guard (NFR-03) — structural + points at the existing behavioral proof
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void NoMembershipUnionRegression_ReadServiceAndAccessFilter_NeverDependOnRetiredGrantOrMembershipSeams()
    {
        // The membership ∪ overlay-grant union was RETIRED for reads on 2026-07-16
        // (../messaging-communication-app-r1/notes/access-model-decision.md): record-level read access is
        // Dataverse IMPERSONATION only; CommunicationAccessFilter applies just the 2 Spaarke business rules
        // (internal-only + privilege) ON TOP of the already-impersonated rows. IThreadPrivateGrantProvider is
        // RETAINED (its own doc comment: "NOT part of the R1 read path") but must never become a dependency of
        // either read surface again. This is a structural, NetArchTest-style architecture guard on the classes'
        // OWN constructor shape — not "was DI registered", not a private/internal member reflection test — the
        // ADR-038 §6 sanctioned replacement for the banned wiring-test patterns.
        var bannedSeamTypeNames = new[]
        {
            nameof(IThreadPrivateGrantProvider),
            "IMembershipResolverService",
            "IThreadMembershipDerivationService",
        };

        var readServiceParamTypeNames = typeof(CommunicationThreadReadService)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToList();
        var filterParamTypeNames = typeof(CommunicationAccessFilter)
            .GetConstructors().Single().GetParameters().Select(p => p.ParameterType.Name).ToList();

        foreach (var banned in bannedSeamTypeNames)
        {
            readServiceParamTypeNames.Should().NotContain(banned,
                $"{nameof(CommunicationThreadReadService)} must depend ONLY on the impersonation query + the 2-rule filter + the caller resolver — never a membership/grant union seam ({banned})");
            filterParamTypeNames.Should().NotContain(banned,
                $"{nameof(CommunicationAccessFilter)} must depend on nothing but its logger — internal-only + privilege are read directly off the message entity, never resolved via a membership/grant lookup ({banned})");
        }

        // The read service's dependency shape is exactly the 4 collaborators the access-model-decision note
        // prescribes (impersonated query, the shared 2-rule filter, the caller resolver, a logger) — no more,
        // no fewer. A 5th dependency creeping in is exactly the shape a reintroduced union path would take.
        readServiceParamTypeNames.Should().HaveCount(4);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // E. Auto-threading 3-tier ladder composed with the REAL MessagingThreadKeyStrategy (composition gap —
    //    ThreadResolverTests uses a STUB strategy for the ladder; ThreadResolverSeamTests uses the REAL strategy
    //    but only for Tier 1 JOIN/CREATE). Neither exercises the REAL strategy's Skip outcome through Tier 2/3.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    private static ThreadResolutionRequest MessageRequest(Guid communicationId) => new()
    {
        CommunicationId = communicationId,
        ChannelType = CommunicationType.Message,
        Direction = CommunicationDirection.Incoming,
        Message = new NormalizedMessage { Direction = CommunicationDirection.Incoming },
        AcsThreadId = null, // ⇒ the REAL MessagingThreadKeyStrategy returns Skip (no groupable channel key) —
                            // the genuine real-world Tier-2/3 trigger (a chat message before the ACS echo assigns
                            // its thread id), not a synthetic stub outcome.
    };

    private static void SetupRegardingRead(Mock<IGenericEntityService> entity, Guid communicationId, DataverseEntity communication) =>
        entity.Setup(e => e.RetrieveAsync(
                "sprk_communication", communicationId,
                It.Is<string[]>(c => c.Contains("sprk_regardingrecordid")), It.IsAny<CancellationToken>()))
              .ReturnsAsync(communication);

    private static void SetupOwnerRead(Mock<IGenericEntityService> entity, Guid communicationId, Guid ownerId) =>
        entity.Setup(e => e.RetrieveAsync(
                "sprk_communication", communicationId,
                It.Is<string[]>(c => c.Contains("ownerid")), It.IsAny<CancellationToken>()))
              .ReturnsAsync(() =>
              {
                  var comm = new DataverseEntity("sprk_communication") { Id = communicationId };
                  comm["ownerid"] = new EntityReference("systemuser", ownerId);
                  return comm;
              });

    private static void SetupNoExistingDefaultThread(Mock<IGenericEntityService> entity) =>
        entity.Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new EntityCollection());

    private static void SetupExistingDefaultThread(Mock<IGenericEntityService> entity, Guid existingThreadId) =>
        entity.Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
              .ReturnsAsync(new EntityCollection
              {
                  Entities = { new DataverseEntity("sprk_communicationthread") { Id = existingThreadId } },
              });

    private static Guid SetupThreadCreateCapture(Mock<IGenericEntityService> entity, out List<DataverseEntity> created)
    {
        var captured = new List<DataverseEntity>();
        created = captured;
        var newId = Guid.NewGuid();
        entity.Setup(e => e.CreateAsync(
                It.Is<DataverseEntity>(x => x.LogicalName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
              .Callback<DataverseEntity, CancellationToken>((x, _) => captured.Add(x))
              .ReturnsAsync(newId);
        return newId;
    }

    private static ThreadResolver CreateRealResolverWithMessagingStrategy(Mock<IGenericEntityService> entity) =>
        new(new IThreadKeyStrategy[] { new MessagingThreadKeyStrategy(entity.Object) }, entity.Object, NullLogger<ThreadResolver>.Instance);

    [Fact]
    public async Task ResolveAndAssignThreadAsync_RealStrategySkipsWithRegarding_LadderCreatesRecordDefaultThenIdempotentlyJoins()
    {
        var entity = new Mock<IGenericEntityService>();
        var matterId = Guid.NewGuid();
        var resolver = CreateRealResolverWithMessagingStrategy(entity);

        // First orphan for the record — Tier 1 (real strategy) Skips ⇒ Tier 2 lazily creates the per-record default.
        var firstRequest = MessageRequest(Guid.NewGuid());
        var firstComm = new DataverseEntity("sprk_communication") { Id = firstRequest.CommunicationId };
        firstComm["sprk_regardingrecordid"] = matterId.ToString();
        firstComm["sprk_regardingrecordtype"] = "sprk_matter";
        firstComm["sprk_regardingrecordname"] = "Acme v Widgets";
        SetupRegardingRead(entity, firstRequest.CommunicationId, firstComm);
        SetupNoExistingDefaultThread(entity);
        var defaultThreadId = SetupThreadCreateCapture(entity, out var created);

        var firstResult = await resolver.ResolveAndAssignThreadAsync(firstRequest, CancellationToken.None);

        firstResult.Should().Be(defaultThreadId);
        created.Should().ContainSingle();
        created[0].GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeRecordAnchored);
        created[0].GetAttributeValue<bool>("sprk_isdefaultthread").Should().BeTrue();

        // Second orphan for the SAME record — the real strategy Skips again, but the ladder now finds the
        // existing default and JOINS it — idempotent (ONE default thread per record), proven end-to-end with
        // REAL production types (not a stub standing in for Tier 1).
        var secondRequest = MessageRequest(Guid.NewGuid());
        var secondComm = new DataverseEntity("sprk_communication") { Id = secondRequest.CommunicationId };
        secondComm["sprk_regardingrecordid"] = matterId.ToString();
        secondComm["sprk_regardingrecordtype"] = "sprk_matter";
        SetupRegardingRead(entity, secondRequest.CommunicationId, secondComm);
        SetupExistingDefaultThread(entity, defaultThreadId);

        var secondResult = await resolver.ResolveAndAssignThreadAsync(secondRequest, CancellationToken.None);

        secondResult.Should().Be(defaultThreadId);
        entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Once,
            "the second orphan for the same record must JOIN the existing default, not create a duplicate");
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_RealStrategySkipsWithNoRegarding_LadderCreatesPerUserMasterThenIdempotentlyJoins()
    {
        var entity = new Mock<IGenericEntityService>();
        var ownerId = Guid.NewGuid();
        var resolver = CreateRealResolverWithMessagingStrategy(entity);

        // First no-regarding orphan for the owner — Tier 1 (real strategy) Skips, no regarding ⇒ Tier 3 lazily
        // creates the per-user master catch-all.
        var firstRequest = MessageRequest(Guid.NewGuid());
        SetupRegardingRead(entity, firstRequest.CommunicationId, new DataverseEntity("sprk_communication") { Id = firstRequest.CommunicationId });
        SetupOwnerRead(entity, firstRequest.CommunicationId, ownerId);
        SetupNoExistingDefaultThread(entity);
        var masterThreadId = SetupThreadCreateCapture(entity, out var created);

        var firstResult = await resolver.ResolveAndAssignThreadAsync(firstRequest, CancellationToken.None);

        firstResult.Should().NotBeNull();
        firstResult.Should().Be(masterThreadId);
        created.Should().ContainSingle();
        created[0].GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeDirect);
        created[0].GetAttributeValue<string>("sprk_regardingrecordtype").Should().Be("systemuser");

        // Second no-regarding orphan for the SAME owner — real strategy Skips again; the ladder JOINS the
        // existing master (ONE master per owning user) — idempotent, end-to-end, real production types.
        var secondRequest = MessageRequest(Guid.NewGuid());
        SetupRegardingRead(entity, secondRequest.CommunicationId, new DataverseEntity("sprk_communication") { Id = secondRequest.CommunicationId });
        SetupOwnerRead(entity, secondRequest.CommunicationId, ownerId);
        SetupExistingDefaultThread(entity, masterThreadId);

        var secondResult = await resolver.ResolveAndAssignThreadAsync(secondRequest, CancellationToken.None);

        secondResult.Should().Be(masterThreadId);
        entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Once,
            "the second no-regarding orphan for the SAME owner must JOIN the existing per-user master, not create a duplicate");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════════
    // Row builders (OData JSON shape) — mirror the existing unit-test builders (no new shape invented)
    // ═══════════════════════════════════════════════════════════════════════════════════════════════════

    private static Dictionary<string, JsonElement> ThreadRow(Guid id, string name) => new()
    {
        ["sprk_communicationthreadid"] = El(id.ToString()),
        ["sprk_name"] = El(name),
        ["createdon"] = El("2026-07-19T12:00:00Z"),
    };

    private static Dictionary<string, JsonElement> MessageRow(
        Guid id, Guid threadId, int? type = null, bool internalOnly = false, int privilege = PrivilegeNone) => new()
        {
            ["sprk_communicationid"] = El(id.ToString()),
            ["_sprk_communicationthread_value"] = El(threadId.ToString()),
            ["createdon"] = El("2026-07-19T12:00:00Z"),
            ["sprk_isinternalonly"] = El(internalOnly),
            ["sprk_privilegeclassification"] = El(privilege),
            ["sprk_body"] = El("m"),
            ["sprk_communicationtype"] = El(type ?? TypeMessage),
        };

    private static Dictionary<string, JsonElement> ParticipantRow(Guid communicationId) => new()
    {
        ["_sprk_communication_value"] = El(communicationId.ToString()),
    };

    private static JsonElement El<T>(T value) => JsonSerializer.SerializeToElement(value);
}
