using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// <para>
/// Tests for <see cref="ThreadResolver"/> — the shared, direction-symmetric, channel-agnostic thread
/// find-or-create seam invoked by BOTH inbound capture (email = <c>IncomingCommunicationProcessor</c>,
/// chat = <c>MessagingIngestor</c>) AND outbound send (<c>CommunicationService</c>).
/// </para>
/// <para>
/// <b>CHARACTERIZATION region</b> pins the R1 behavior that task 070 (FR-09) MUST preserve while editing
/// this shared code: Tier-1 JOIN by channel key, record-anchored CREATE, Direct CREATE, the no-strategy
/// guard, and the NFR-02 non-fatal swallow. These were authored + confirmed GREEN against the UNCHANGED
/// resolver before the 3-tier ladder was added, and stay green after (only the former SKIP→null case now
/// falls through to Tier 2/3).
/// </para>
/// <para>
/// <b>EXTENSION region</b> exercises the FR-09 auto-threading ladder: Tier 2 (per-record default, one per
/// record, lazy), Tier 3 (per-user master catch-all, one per owning user), the never-null guarantee, and
/// the ladder's own NFR-02 non-fatal behavior.
/// </para>
/// <para>
/// The class-under-test is a domain service; its Dataverse dependency (<see cref="IGenericEntityService"/>)
/// is a module boundary mocked via Moq (allowed), and the per-channel key strategy is a controllable
/// in-memory stub so the resolver's OWN branching is exercised in isolation from strategy internals.
/// </para>
/// </summary>
public class ThreadResolverTests
{
    private const int ThreadTypeRecordAnchored = 100000000;
    private const int ThreadTypeDirect = 100000001;
    private const int PrivacyStateOpen = 100000000;

    private readonly Mock<IGenericEntityService> _entity = new();

    // ─────────────────────────────────────────────────────────────────────────────
    // Test doubles + builders
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Controllable per-channel key strategy: returns a fixed <see cref="ThreadKeyResolution"/> (JOIN /
    /// CREATE / SKIP), optionally throws on resolve (to exercise the resolver's NFR-02 swallow), and records
    /// whether the resolver invoked <see cref="OnThreadCreatedAsync"/> on the CREATE path.
    /// </summary>
    private sealed class StubThreadKeyStrategy : IThreadKeyStrategy
    {
        private readonly ThreadKeyResolution _resolution;
        private readonly bool _throwOnResolve;

        public StubThreadKeyStrategy(
            CommunicationType type, ThreadKeyResolution resolution, bool throwOnResolve = false)
        {
            SupportedType = type;
            _resolution = resolution;
            _throwOnResolve = throwOnResolve;
        }

        public CommunicationType SupportedType { get; }
        public int OnThreadCreatedCalls { get; private set; }

        public Task<ThreadKeyResolution> ResolveAsync(ThreadResolutionRequest request, CancellationToken ct)
            => _throwOnResolve
                ? throw new InvalidOperationException("simulated strategy failure")
                : Task.FromResult(_resolution);

        public Task OnThreadCreatedAsync(Guid threadId, ThreadResolutionRequest request, CancellationToken ct)
        {
            OnThreadCreatedCalls++;
            return Task.CompletedTask;
        }
    }

    private ThreadResolver CreateSut(params IThreadKeyStrategy[] strategies)
        => new(strategies, _entity.Object, new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(), NullLogger<ThreadResolver>.Instance);

    private static ThreadResolutionRequest Request(
        CommunicationType channel = CommunicationType.Email,
        Guid? communicationId = null,
        string? subject = null,
        string? acsThreadId = null) =>
        new()
        {
            CommunicationId = communicationId ?? Guid.NewGuid(),
            ChannelType = channel,
            Direction = CommunicationDirection.Outgoing,
            Message = new NormalizedMessage { Direction = CommunicationDirection.Outgoing, Subject = subject },
            AcsThreadId = acsThreadId,
        };

    /// <summary>Sets up the regarding read (columns include the denormalized regarding pointer).</summary>
    private void SetupRegardingRead(Guid communicationId, DataverseEntity communication) =>
        _entity
            .Setup(e => e.RetrieveAsync(
                "sprk_communication",
                communicationId,
                It.Is<string[]>(c => c.Contains("sprk_regardingrecordid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(communication);

    /// <summary>Sets up the owner read (columns == ["ownerid"]).</summary>
    private void SetupOwnerRead(Guid communicationId, Guid? ownerId) =>
        _entity
            .Setup(e => e.RetrieveAsync(
                "sprk_communication",
                communicationId,
                It.Is<string[]>(c => c.Contains("ownerid")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var comm = new DataverseEntity("sprk_communication") { Id = communicationId };
                if (ownerId is { } id)
                    comm["ownerid"] = new EntityReference("systemuser", id);
                return comm;
            });

    /// <summary>Captures the created thread entity and returns a fixed new thread id.</summary>
    private Guid SetupThreadCreateCapture(out List<DataverseEntity> created)
    {
        var captured = new List<DataverseEntity>();
        created = captured;
        var newId = Guid.NewGuid();
        _entity
            .Setup(e => e.CreateAsync(
                It.Is<DataverseEntity>(x => x.LogicalName == "sprk_communicationthread"),
                It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((x, _) => captured.Add(x))
            .ReturnsAsync(newId);
        return newId;
    }

    /// <summary>Sets up the default-thread find query to return no existing default (→ create path).</summary>
    private void SetupNoExistingDefaultThread() =>
        _entity
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

    /// <summary>Sets up the default-thread find query to return an existing default (→ join path).</summary>
    private void SetupExistingDefaultThread(Guid existingThreadId) =>
        _entity
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection
            {
                Entities = { new DataverseEntity("sprk_communicationthread") { Id = existingThreadId } },
            });

    private void VerifyAssigned(Guid communicationId, Guid threadId, Times times) =>
        _entity.Verify(e => e.UpdateAsync(
            "sprk_communication",
            communicationId,
            It.Is<Dictionary<string, object>>(d =>
                d.ContainsKey("sprk_communicationthread")
                && ((EntityReference)d["sprk_communicationthread"]).Id == threadId),
            It.IsAny<CancellationToken>()), times);

    // ═════════════════════════════════════════════════════════════════════════════
    // CHARACTERIZATION — R1 behavior that MUST be preserved (Tier 1 + NFR-02)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenStrategyJoinsExistingThread_AssignsAndReturnsIt()
    {
        var request = Request(CommunicationType.Email);
        var existingThread = Guid.NewGuid();
        var sut = CreateSut(new StubThreadKeyStrategy(
            CommunicationType.Email, ThreadKeyResolution.Join(existingThread)));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(existingThread);
        VerifyAssigned(request.CommunicationId, existingThread, Times.Once());
        // JOIN must NOT create a thread.
        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenStrategyCreatesNewWithRegarding_CreatesRecordAnchoredThread()
    {
        var request = Request(CommunicationType.Email, subject: "Contract review");
        var matterId = Guid.NewGuid();
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        SetMessageRegarding(communication, "sprk_matter", matterId, "Acme v Widgets");
        SetupRegardingRead(request.CommunicationId, communication);
        var newThreadId = SetupThreadCreateCapture(out var created);
        var strategy = new StubThreadKeyStrategy(CommunicationType.Email, ThreadKeyResolution.CreateNew);
        var sut = CreateSut(strategy);

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(newThreadId);
        created.Should().ContainSingle();
        var thread = created[0];
        thread.GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeRecordAnchored);
        thread.GetAttributeValue<OptionSetValue>("sprk_privacystate").Value.Should().Be(PrivacyStateOpen);
        thread.GetAttributeValue<string>("sprk_name").Should().Be("Contract review");
        thread.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(matterId.ToString());
        // Typed ADR-024 lookup (what the by-regarding read filters on) — NOT a 'sprk_regardingrecordtype' text attr.
        thread.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(matterId);
        thread.Contains("sprk_regardingrecordtype").Should().BeFalse();
        // A Tier-1 CREATE is NOT a default/master thread.
        thread.Contains("sprk_isdefaultthread").Should().BeFalse();
        strategy.OnThreadCreatedCalls.Should().Be(1);
        VerifyAssigned(request.CommunicationId, newThreadId, Times.Once());
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenStrategyCreatesNewWithNoRegarding_CreatesDirectThread()
    {
        var request = Request(CommunicationType.Message, subject: null);
        SetupRegardingRead(
            request.CommunicationId,
            new DataverseEntity("sprk_communication") { Id = request.CommunicationId }); // no regarding
        var newThreadId = SetupThreadCreateCapture(out var created);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.CreateNew));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(newThreadId);
        created.Should().ContainSingle();
        created[0].GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeDirect);
        created[0].Contains("sprk_isdefaultthread").Should().BeFalse();
    }

    [Theory]
    [InlineData("sprk_matter")]
    [InlineData("account")]
    [InlineData(null)]
    public async Task ResolveAndAssignAsync_Tier1Create_ReturnsTheAudienceTheCreateWrote(string? regardingType)
    {
        // #1629 F3: a Tier-1 CREATE grants what it wrote — the message's own typed regarding (record or party), or the
        // Direct participants when the message has none. A CREATE that always answered Direct would grant a record's
        // message to the creator's sharees instead of the record's members.
        var request = Request(CommunicationType.Email, subject: "Contract review");
        var regardingId = Guid.NewGuid();
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        if (regardingType is not null)
            SetMessageRegarding(communication, regardingType, regardingId, "Acme");
        SetupRegardingRead(request.CommunicationId, communication);
        var newThreadId = SetupThreadCreateCapture(out _);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Email, ThreadKeyResolution.CreateNew));

        var result = await sut.ResolveAndAssignAsync(request, CancellationToken.None);

        result.Should().Be(regardingType is null
            ? ThreadAssignment.Direct(newThreadId)
            : ThreadAssignment.Anchored(newThreadId, regardingType, regardingId));
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenNoStrategyForChannel_ReturnsNullWithoutTouchingDataverse()
    {
        // Misconfiguration guard (not a normal message path): a channel with no registered key strategy.
        var request = Request(CommunicationType.Message);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Email, ThreadKeyResolution.CreateNew));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().BeNull();
        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _entity.Verify(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(),
            It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenStrategyThrows_ReturnsNullAndDoesNotThrow()
    {
        var request = Request(CommunicationType.Email);
        var sut = CreateSut(new StubThreadKeyStrategy(
            CommunicationType.Email, ThreadKeyResolution.CreateNew, throwOnResolve: true));

        var act = async () => await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.Should().BeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // EXTENSION — FR-09 3-tier auto-threading ladder (former SKIP→null now falls through)
    // ═════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenTier1MissesWithRegarding_CreatesPerRecordDefaultLazily()
    {
        // Tier 2: message with a resolved regarding but no groupable Tier-1 key → lazily create the ONE
        // per-record default thread, marked sprk_isdefaultthread, and join it.
        var request = Request(CommunicationType.Message, acsThreadId: null); // Message + no ACS id → SKIP
        var matterId = Guid.NewGuid();
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        SetMessageRegarding(communication, "sprk_matter", matterId, "Acme v Widgets");
        SetupRegardingRead(request.CommunicationId, communication);
        SetupNoExistingDefaultThread();
        var newThreadId = SetupThreadCreateCapture(out var created);
        var strategy = new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip);
        var sut = CreateSut(strategy);

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(newThreadId);
        created.Should().ContainSingle();
        var thread = created[0];
        thread.GetAttributeValue<bool>("sprk_isdefaultthread").Should().BeTrue();
        thread.GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeRecordAnchored);
        // Tier-2 default now carries the typed lookup so it appears in the record's by-regarding list.
        thread.GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(matterId);
        thread.Contains("sprk_regardingrecordtype").Should().BeFalse();
        thread.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(matterId.ToString());
        thread.GetAttributeValue<string>("sprk_name").Should().Be("Acme v Widgets");
        VerifyAssigned(request.CommunicationId, newThreadId, Times.Once());
        // The ladder must NOT write a channel-ref row (no groupable channel key exists).
        strategy.OnThreadCreatedCalls.Should().Be(0);
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenSecondOrphanForSameRecord_JoinsSamePerRecordDefault()
    {
        // Tier 2 idempotency: a SECOND orphan for the same record finds the existing default and JOINs it —
        // no duplicate default thread is created (ONE per record).
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var matterId = Guid.NewGuid();
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        SetMessageRegarding(communication, "sprk_matter", matterId);
        SetupRegardingRead(request.CommunicationId, communication);
        var existingDefault = Guid.NewGuid();
        SetupExistingDefaultThread(existingDefault);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(existingDefault);
        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyAssigned(request.CommunicationId, existingDefault, Times.Once());
    }

    /// <summary>
    /// The Tier-2 find query evaluated against <paramref name="rows"/> the way Dataverse would: a row is returned only when
    /// every condition of the query holds on it (equality and null conditions; the only kinds the find uses). A row that
    /// sets no <c>statecode</c> is Active (0), as every Dataverse row is until it is deactivated.
    /// </summary>
    private void SetupDefaultThreadRows(params DataverseEntity[] rows) =>
        _entity
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var matches = rows.Where(row => q.Criteria.Conditions.All(c =>
                {
                    var value = row.Contains(c.AttributeName) ? row[c.AttributeName]
                        : c.AttributeName == "statecode" ? new OptionSetValue(0) : null;
                    var actual = value switch
                    {
                        EntityReference er => (object)er.Id,
                        OptionSetValue os => os.Value,
                        _ => value,
                    };
                    return c.Operator switch
                    {
                        ConditionOperator.Equal => Equals(actual, c.Values[0]),
                        ConditionOperator.Null => actual is null,
                        _ => false,
                    };
                }));
                return new EntityCollection(matches.Take(q.TopCount ?? int.MaxValue).ToList());
            });

    [Fact]
    public async Task ResolveAndAssignThreadAsync_Tier2_NeverJoinsAThreadWhosePairAloneNamesTheRecord()
    {
        // #1629: a user marked their own thread as a default and set its pair (plain text) to matter X while its typed
        // lookup names their own matter A. A message filed under X must not be drawn into that thread.
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var matterX = Guid.NewGuid();
        var matterA = Guid.NewGuid();
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        SetMessageRegarding(communication, "sprk_matter", matterX, "Secure matter");
        SetupRegardingRead(request.CommunicationId, communication);
        var forged = Thread(Guid.NewGuid(), regardingType: "sprk_matter", regardingId: matterA.ToString(),
            isDefault: true, threadType: ThreadTypeRecordAnchored);
        forged["sprk_regardingrecordid"] = matterX.ToString();
        var pairOnly = Thread(Guid.NewGuid(), isDefault: true, threadType: ThreadTypeRecordAnchored);
        pairOnly["sprk_regardingrecordid"] = matterX.ToString();
        SetupDefaultThreadRows(forged, pairOnly);
        var newThreadId = SetupThreadCreateCapture(out var created);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(newThreadId);
        created.Should().ContainSingle();
        created[0].GetAttributeValue<EntityReference>("sprk_regardingmatter").Id.Should().Be(matterX);
        VerifyAssigned(request.CommunicationId, forged.Id, Times.Never());
        VerifyAssigned(request.CommunicationId, pairOnly.Id, Times.Never());
        VerifyAssigned(request.CommunicationId, newThreadId, Times.Once());
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_Tier2_JoinsTheDefaultWhoseTypedLookupRepeatsThePair()
    {
        // The legitimate per-record default (typed lookup + the same pair) is still found and joined.
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var matterX = Guid.NewGuid();
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        SetMessageRegarding(communication, "sprk_matter", matterX);
        SetupRegardingRead(request.CommunicationId, communication);
        var legit = Thread(Guid.NewGuid(), regardingType: "sprk_matter", regardingId: matterX.ToString(),
            isDefault: true, threadType: ThreadTypeRecordAnchored);
        legit["sprk_regardingrecordid"] = matterX.ToString();
        SetupDefaultThreadRows(legit);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(legit.Id);
        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyAssigned(request.CommunicationId, legit.Id, Times.Once());
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_Tier2_NeverJoinsAnInactiveDefault_ItCreatesAnActiveOne()
    {
        // Task 129 behaviour review F3: a deactivated default is on no panel — a message joined to it would be hidden.
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var matterX = Guid.NewGuid();
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        SetMessageRegarding(communication, "sprk_matter", matterX);
        SetupRegardingRead(request.CommunicationId, communication);
        var inactive = Thread(Guid.NewGuid(), regardingType: "sprk_matter", regardingId: matterX.ToString(),
            isDefault: true, threadType: ThreadTypeRecordAnchored);
        inactive["sprk_regardingrecordid"] = matterX.ToString();
        inactive["statecode"] = new OptionSetValue(1);
        SetupDefaultThreadRows(inactive);
        var newThreadId = SetupThreadCreateCapture(out var created);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(newThreadId);
        created.Should().ContainSingle();
        VerifyAssigned(request.CommunicationId, inactive.Id, Times.Never());
    }

    [Fact]
    public async Task ResolveAndAssignAsync_Tier2_NeverJoinsADefaultThatAlsoCarriesAHigherPriorityLookup()
    {
        // #1629 F-B: a default thread with project P (and pair P) PLUS matter M, a matter the owner belongs to. ThreadAnchorOf
        // anchors it on M (matter outranks project), so P's messages must not join it; a default with only P is joined.
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var projectP = Guid.NewGuid();
        var matterM = Guid.NewGuid();
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        SetMessageRegarding(communication, "sprk_project", projectP);
        SetupRegardingRead(request.CommunicationId, communication);
        var mixed = Thread(Guid.NewGuid(), regardingType: "sprk_project", regardingId: projectP.ToString(),
            isDefault: true, threadType: ThreadTypeRecordAnchored);
        mixed["sprk_regardingmatter"] = new EntityReference("sprk_matter", matterM);
        mixed["sprk_regardingrecordid"] = projectP.ToString();
        ThreadResolver.ThreadAnchorOf(mixed).RecordType.Should().Be("sprk_matter", "the attack depends on the matter outranking the project");
        SetupDefaultThreadRows(mixed);
        var newThreadId = SetupThreadCreateCapture(out _);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignAsync(request, CancellationToken.None);

        result!.ThreadId.Should().Be(newThreadId);
        VerifyAssigned(request.CommunicationId, mixed.Id, Times.Never());
    }

    [Fact]
    public async Task ResolveAndAssignAsync_Tier2_JoinsADefaultCarryingOnlyTheKeysLookup_AndGrantsTheMessagesOwnRegarding()
    {
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var projectP = Guid.NewGuid();
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        SetMessageRegarding(communication, "sprk_project", projectP);
        SetupRegardingRead(request.CommunicationId, communication);
        var only = Thread(Guid.NewGuid(), regardingType: "sprk_project", regardingId: projectP.ToString(),
            isDefault: true, threadType: ThreadTypeRecordAnchored);
        only["sprk_regardingrecordid"] = projectP.ToString();
        SetupDefaultThreadRows(only);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignAsync(request, CancellationToken.None);

        // F-C: the audience is the message's own regarding — the thread is never re-read for it.
        result.Should().Be(ThreadAssignment.Anchored(only.Id, "sprk_project", projectP));
        VerifyAssigned(request.CommunicationId, only.Id, Times.Once());
        _entity.Verify(
            e => e.RetrieveAsync("sprk_communicationthread", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResolveAndAssignAsync_Tier3Master_GrantsNobody()
    {
        var request = Request(CommunicationType.Message, acsThreadId: null);
        SetupRegardingRead(request.CommunicationId, new DataverseEntity("sprk_communication") { Id = request.CommunicationId });
        SetupOwnerRead(request.CommunicationId, Guid.NewGuid());
        var existingMaster = Guid.NewGuid();
        SetupExistingDefaultThread(existingMaster);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignAsync(request, CancellationToken.None);

        result.Should().Be(ThreadAssignment.NoGrant(existingMaster));
    }

    [Theory]
    [InlineData(ThreadTypeDirect)]
    [InlineData(ThreadTypeRecordAnchored)]
    public async Task ResolveAndAssignAsync_Tier1Join_TakesTheAudienceFromTheReadThatFiledTheMessage(int threadType)
    {
        // #1629 F-C: the Tier-1 JOIN reads the thread ONCE (type + typed lookups) for both the owner re-file and the grant.
        // #1629 F1: a typed lookup wins over sprk_threadtype — a "Direct" thread whose owner set sprk_regardingmatter = M
        // files the message under M, so the audience is M's members, never the thread owner and their sharees.
        var request = Request(CommunicationType.Email);
        var existing = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var row = Thread(existing, regardingType: "sprk_matter", regardingId: matterId.ToString(), threadType: threadType);
        _entity
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", existing, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Email, ThreadKeyResolution.Join(existing)));

        var result = await sut.ResolveAndAssignAsync(request, CancellationToken.None);

        result.Should().Be(ThreadAssignment.Anchored(existing, "sprk_matter", matterId));
        _entity.Verify(
            e => e.RetrieveAsync("sprk_communicationthread", existing, It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(ThreadTypeDirect, true)]
    [InlineData(ThreadTypeRecordAnchored, false)]
    public async Task ResolveAndAssignAsync_Tier1Join_ThreadWithNoTypedLookup_IsDirectOnlyWhenTypedDirect(int threadType, bool direct)
    {
        // #1629 F1: Direct applies ONLY to a thread with no typed regarding lookup; a record-typed thread that lost its
        // lookup grants nobody (fail closed).
        var request = Request(CommunicationType.Email);
        var existing = Guid.NewGuid();
        var row = Thread(existing, threadType: threadType);
        _entity
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", existing, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Email, ThreadKeyResolution.Join(existing)));

        var result = await sut.ResolveAndAssignAsync(request, CancellationToken.None);

        result.Should().Be(direct ? ThreadAssignment.Direct(existing) : ThreadAssignment.NoGrant(existing));
    }

    [Fact]
    public async Task ResolveAndAssignAsync_Tier1Join_DirectThreadWithAPartyLookup_GrantsTheParty_NotTheThreadOwner()
    {
        // #1629 F1: a party anchor is not an ownership parent (no re-file), but it is still the thread's typed anchor.
        var request = Request(CommunicationType.Email);
        var existing = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var row = Thread(existing, regardingType: "account", regardingId: accountId.ToString(), threadType: ThreadTypeDirect);
        _entity
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", existing, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Email, ThreadKeyResolution.Join(existing)));

        var result = await sut.ResolveAndAssignAsync(request, CancellationToken.None);

        result.Should().Be(ThreadAssignment.Anchored(existing, "account", accountId));
    }

    [Fact]
    public void ThreadAnchorOf_AnchorsOnTheTypedLookupAndReportsThePairsStanding()
    {
        var matterA = Guid.NewGuid();
        var matterX = Guid.NewGuid();

        var disagreeing = Thread(Guid.NewGuid(), regardingType: "sprk_matter", regardingId: matterA.ToString());
        disagreeing["sprk_regardingrecordid"] = matterX.ToString();
        var anchor = ThreadResolver.ThreadAnchorOf(disagreeing);
        (anchor.RecordType, anchor.RecordId, anchor.Pair).Should().Be(("sprk_matter", (Guid?)matterA, ThreadResolver.ThreadPairAgreement.NotRepeated));

        var pairOnly = Thread(Guid.NewGuid());
        pairOnly["sprk_regardingrecordid"] = matterX.ToString();
        anchor = ThreadResolver.ThreadAnchorOf(pairOnly);
        anchor.HasAnchor.Should().BeFalse();
        anchor.Pair.Should().Be(ThreadResolver.ThreadPairAgreement.NotRepeated);

        var repeated = Thread(Guid.NewGuid(), regardingType: "sprk_matter", regardingId: matterA.ToString());
        repeated["sprk_regardingrecordid"] = matterA.ToString().ToUpperInvariant();
        anchor = ThreadResolver.ThreadAnchorOf(repeated);
        (anchor.RecordType, anchor.RecordId, anchor.Pair).Should().Be(("sprk_matter", (Guid?)matterA, ThreadResolver.ThreadPairAgreement.RepeatedByTypedLookup));

        var none = Thread(Guid.NewGuid());
        ThreadResolver.ThreadAnchorOf(none).Should().Be(new ThreadResolver.ThreadAnchor(null, null, ThreadResolver.ThreadPairAgreement.NoPair, null));
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenTier1MissesWithNoRegarding_CreatesPerUserMaster()
    {
        // Tier 3: no groupable key AND no regarding → lazily create the ONE per-user master catch-all,
        // keyed on the owning user, so the message still lands somewhere non-null.
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var ownerId = Guid.NewGuid();
        SetupRegardingRead(
            request.CommunicationId,
            new DataverseEntity("sprk_communication") { Id = request.CommunicationId }); // no regarding
        SetupOwnerRead(request.CommunicationId, ownerId);
        SetupNoExistingDefaultThread();
        var newThreadId = SetupThreadCreateCapture(out var created);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(newThreadId);
        created.Should().ContainSingle();
        var thread = created[0];
        thread.GetAttributeValue<bool>("sprk_isdefaultthread").Should().BeTrue();
        thread.GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeDirect);
        // Master is keyed on the owning-user GUID via sprk_regardingrecordid (globally unique). "systemuser" is
        // NOT an ADR-024 family, so NO typed regarding lookup is set, and there is no text type attr.
        thread.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(ownerId.ToString());
        thread.Contains("sprk_regardingrecordtype").Should().BeFalse();
        VerifyAssigned(request.CommunicationId, newThreadId, Times.Once());
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenSecondOrphanForSameUser_JoinsSamePerUserMaster()
    {
        // Tier 3 idempotency: a SECOND no-regarding orphan for the same owner finds the existing master and
        // JOINs it — ONE master per owning user.
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var ownerId = Guid.NewGuid();
        SetupRegardingRead(
            request.CommunicationId,
            new DataverseEntity("sprk_communication") { Id = request.CommunicationId });
        SetupOwnerRead(request.CommunicationId, ownerId);
        var existingMaster = Guid.NewGuid();
        SetupExistingDefaultThread(existingMaster);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(existingMaster);
        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyAssigned(request.CommunicationId, existingMaster, Times.Once());
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_OnEveryTier1MissPath_ReturnsNonNullThread()
    {
        // Never-null guarantee: both fall-through shapes (with-regarding → Tier 2, no-regarding → Tier 3)
        // resolve to a non-null thread.
        // With regarding → Tier 2.
        var reqA = Request(CommunicationType.Message, acsThreadId: null);
        var commA = new DataverseEntity("sprk_communication") { Id = reqA.CommunicationId };
        SetMessageRegarding(commA, "sprk_project", Guid.NewGuid());
        SetupRegardingRead(reqA.CommunicationId, commA);
        // No regarding → Tier 3.
        var reqB = Request(CommunicationType.Message, acsThreadId: null);
        SetupRegardingRead(reqB.CommunicationId, new DataverseEntity("sprk_communication") { Id = reqB.CommunicationId });
        SetupOwnerRead(reqB.CommunicationId, Guid.NewGuid());
        SetupNoExistingDefaultThread();
        SetupThreadCreateCapture(out _);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        (await sut.ResolveAndAssignThreadAsync(reqA, CancellationToken.None)).Should().NotBeNull();
        (await sut.ResolveAndAssignThreadAsync(reqB, CancellationToken.None)).Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenLadderCreateThrows_ReturnsNullAndDoesNotThrow()
    {
        // NFR-02: a Tier-2/Tier-3 Dataverse failure is caught by the resolver's non-fatal swallow — the
        // send/capture path never sees the exception; the message simply persists without a thread.
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var communication = new DataverseEntity("sprk_communication") { Id = request.CommunicationId };
        SetMessageRegarding(communication, "sprk_matter", Guid.NewGuid());
        SetupRegardingRead(request.CommunicationId, communication);
        SetupNoExistingDefaultThread();
        _entity
            .Setup(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated create failure"));
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var act = async () => await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        (await act.Should().NotThrowAsync()).Which.Should().BeNull();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // NEW — FR-07 naming re-derive (task 071): marker-gated ReDeriveThreadNameAsync +
    // Auto-stamp on CREATE. The Tier-1/2/3 CREATE characterization/extension tests above are untouched.
    // ═════════════════════════════════════════════════════════════════════════════

    private const string NameIsAutoDerivedField = "sprk_nameisautoderived";

    /// <summary>Sets up the thread retrieve for <c>ReDeriveThreadNameAsync</c> (columns include the marker + threadtype + typed regarding lookups).</summary>
    private void SetupThreadRead(Guid threadId, DataverseEntity thread) =>
        _entity
            .Setup(e => e.RetrieveAsync(
                "sprk_communicationthread",
                threadId,
                It.Is<string[]>(c => c.Contains(NameIsAutoDerivedField) && c.Contains("sprk_threadtype")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

    /// <summary>
    /// Builds a thread row. <paramref name="regardingType"/> is an ADR-024 entity logical name — the TYPED
    /// regarding lookup (via <see cref="RegardingFieldMap"/>) is set, mirroring production (there is no
    /// 'sprk_regardingrecordtype' text attribute on the thread). A master thread is <paramref name="isDefault"/>
    /// + threadtype Direct.
    /// </summary>
    private static DataverseEntity Thread(
        Guid id, string? name = null, bool? nameIsAutoDerived = null,
        string? regardingType = null, string? regardingId = null, string? regardingName = null,
        bool isDefault = false, int? threadType = null)
    {
        var thread = new DataverseEntity("sprk_communicationthread") { Id = id };
        if (name is not null) thread["sprk_name"] = name;
        if (nameIsAutoDerived is { } marker) thread[NameIsAutoDerivedField] = marker;
        if (regardingType is not null && regardingId is not null && RegardingFieldMap.FieldFor(regardingType) is { } field)
            thread[field] = new EntityReference(regardingType, Guid.Parse(regardingId));
        if (regardingName is not null) thread["sprk_regardingrecordname"] = regardingName;
        if (isDefault) thread["sprk_isdefaultthread"] = true;
        if (threadType is { } tt) thread["sprk_threadtype"] = new OptionSetValue(tt);
        return thread;
    }

    /// <summary>Stamps a message's TYPED ADR-024 regarding lookup (the field the resolver reads for the anchor).</summary>
    private static void SetMessageRegarding(DataverseEntity message, string entityType, Guid recordId, string? recordName = null)
    {
        if (RegardingFieldMap.FieldFor(entityType) is { } field)
            message[field] = new EntityReference(entityType, recordId);
        message["sprk_regardingrecordid"] = recordId.ToString();
        if (recordName is not null) message["sprk_regardingrecordname"] = recordName;
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_WhenMarkerAutoAndRegardingChanged_UpdatesName()
    {
        var threadId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        SetupThreadRead(threadId, Thread(
            threadId, name: "Old Name", nameIsAutoDerived: true,
            regardingType: "sprk_matter", regardingId: matterId.ToString(), regardingName: "Acme v Widgets"));
        var sut = CreateSut();

        await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread",
            threadId,
            It.Is<Dictionary<string, object>>(d => (string)d["sprk_name"] == "Acme v Widgets"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_WhenMarkerNullLegacyThread_TreatsAsAutoAndReDerives()
    {
        // A thread created before the task-002 schema landed has no sprk_nameisautoderived value yet.
        var threadId = Guid.NewGuid();
        SetupThreadRead(threadId, Thread(
            threadId, name: "Old Name", nameIsAutoDerived: null,
            regardingType: "sprk_project", regardingId: Guid.NewGuid().ToString(), regardingName: "Project Phoenix"));
        var sut = CreateSut();

        await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread",
            threadId,
            It.Is<Dictionary<string, object>>(d => (string)d["sprk_name"] == "Project Phoenix"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_WhenMarkerEdited_PreservesName()
    {
        // A user previously renamed the thread (marker flipped to Edited elsewhere) — a subsequent
        // regarding change MUST NOT clobber the user's name.
        var threadId = Guid.NewGuid();
        SetupThreadRead(threadId, Thread(
            threadId, name: "User's Custom Name", nameIsAutoDerived: false,
            regardingType: "sprk_matter", regardingId: Guid.NewGuid().ToString(), regardingName: "New Regarding Record"));
        var sut = CreateSut();

        await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        _entity.Verify(e => e.UpdateAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_WhenMasterThread_DoesNotAttemptRegardingResolution()
    {
        // The Tier-3 per-user master ("Messages") is a DEFAULT thread of type Direct — detected via the marker +
        // threadtype (no 'sprk_regardingrecordtype' text attr). Its name MUST NEVER re-derive, regardless of marker.
        var threadId = Guid.NewGuid();
        SetupThreadRead(threadId, Thread(
            threadId, name: "Messages", nameIsAutoDerived: true,
            isDefault: true, threadType: ThreadTypeDirect));
        var sut = CreateSut();

        await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        _entity.Verify(e => e.UpdateAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_WhenDerivedNameUnchanged_DoesNotWrite()
    {
        // Avoid a needless write when the re-derived name equals the current name.
        var threadId = Guid.NewGuid();
        SetupThreadRead(threadId, Thread(
            threadId, name: "Acme v Widgets", nameIsAutoDerived: true,
            regardingType: "sprk_matter", regardingId: Guid.NewGuid().ToString(), regardingName: "Acme v Widgets"));
        var sut = CreateSut();

        await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        _entity.Verify(e => e.UpdateAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_WhenRetrieveThrows_SwallowsAndDoesNotThrow()
    {
        // NFR-02: best-effort / non-fatal.
        var threadId = Guid.NewGuid();
        _entity
            .Setup(e => e.RetrieveAsync(
                "sprk_communicationthread", threadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated retrieve failure"));
        var sut = CreateSut();

        var act = async () => await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenCreatingThread_StampsNameIsAutoDerivedTrue()
    {
        // FR-07 step 3: the CREATE path stamps the marker Auto explicitly (not relying solely on the
        // Dataverse column default), so ReDeriveThreadNameAsync re-derives until the user renames it.
        var request = Request(CommunicationType.Email, subject: "Contract review");
        SetupRegardingRead(request.CommunicationId, new DataverseEntity("sprk_communication") { Id = request.CommunicationId });
        SetupThreadCreateCapture(out var created);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Email, ThreadKeyResolution.CreateNew));

        await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        created.Should().ContainSingle();
        created[0].GetAttributeValue<bool>(NameIsAutoDerivedField).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAndAssignThreadAsync_WhenCreatingDefaultThread_StampsNameIsAutoDerivedTrue()
    {
        // Same Auto stamp on the Tier-2/3 ladder CREATE path (FindOrCreateDefaultThreadAsync).
        var request = Request(CommunicationType.Message, acsThreadId: null);
        var ownerId = Guid.NewGuid();
        SetupRegardingRead(
            request.CommunicationId, new DataverseEntity("sprk_communication") { Id = request.CommunicationId });
        SetupOwnerRead(request.CommunicationId, ownerId);
        SetupNoExistingDefaultThread();
        SetupThreadCreateCapture(out var created);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        created.Should().ContainSingle();
        created[0].GetAttributeValue<bool>(NameIsAutoDerivedField).Should().BeTrue();
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // NEW — FR-17 (task 004): participant roll-up for a RECORD-LESS thread + BFF rename
    // (sprk_name + Edited marker in ONE update) + edit-preserve on a later re-derive.
    // ═════════════════════════════════════════════════════════════════════════════

    private const string ParticipantEntity = "sprk_communicationparticipant";

    /// <summary>Sets up the thread's message scan (RetrieveMultiple on sprk_communication by thread lookup).</summary>
    private void SetupThreadMessages(params Guid[] messageIds) =>
        _entity
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var c = new EntityCollection();
                foreach (var id in messageIds)
                    c.Entities.Add(new DataverseEntity("sprk_communication") { Id = id });
                return c;
            });

    /// <summary>Sets up the participant scan (RetrieveMultiple on sprk_communicationparticipant across the messages).</summary>
    private void SetupParticipants(params DataverseEntity[] participants) =>
        _entity
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == ParticipantEntity),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(participants.ToList<DataverseEntity>()));

    /// <summary>One junction row: a resolved systemuser/contact (display name via EntityReference.Name) OR a raw address.</summary>
    private static DataverseEntity Participant(string? systemUserName = null, string? contactName = null, string? address = null)
    {
        var p = new DataverseEntity(ParticipantEntity) { Id = Guid.NewGuid() };
        if (systemUserName is not null)
            p["sprk_systemuser"] = new EntityReference("systemuser", Guid.NewGuid()) { Name = systemUserName };
        if (contactName is not null)
            p["sprk_contact"] = new EntityReference("contact", Guid.NewGuid()) { Name = contactName };
        if (address is not null)
            p["sprk_addresstext"] = address;
        return p;
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_WhenRecordLessThreadWithParticipants_DerivesRollupName()
    {
        // FR-17: a record-less thread (marker Auto, NO regarding anchor) is named from its participants
        // ("Alice, Bob") rather than the generic "Conversation".
        var threadId = Guid.NewGuid();
        SetupThreadRead(threadId, Thread(threadId, name: "Conversation", nameIsAutoDerived: true)); // no regarding
        SetupThreadMessages(Guid.NewGuid(), Guid.NewGuid());
        SetupParticipants(
            Participant(systemUserName: "Bob"),
            Participant(contactName: "Alice"),
            Participant(systemUserName: "Bob")); // duplicate → deduped
        var sut = CreateSut();

        await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        // Deterministic ordinal order → "Alice, Bob" regardless of the query order above.
        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread",
            threadId,
            It.Is<Dictionary<string, object>>(d => (string)d["sprk_name"] == "Alice, Bob"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_WhenRecordLessThreadWithManyParticipants_TruncatesWithPlusCount()
    {
        // More than the shown cap (3) → "Alice, Bob, Carol, +N".
        var threadId = Guid.NewGuid();
        SetupThreadRead(threadId, Thread(threadId, name: "Conversation", nameIsAutoDerived: true));
        SetupThreadMessages(Guid.NewGuid());
        SetupParticipants(
            Participant(systemUserName: "Alice"),
            Participant(systemUserName: "Bob"),
            Participant(systemUserName: "Carol"),
            Participant(systemUserName: "Dave"),
            Participant(address: "erin@example.com"));
        var sut = CreateSut();

        await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread",
            threadId,
            It.Is<Dictionary<string, object>>(d => (string)d["sprk_name"] == "Alice, Bob, Carol, +2"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_WhenRecordLessThreadHasNoMessages_FallsBackToConversation()
    {
        // No messages → no participants to roll up → keep the generic "Conversation" fallback.
        var threadId = Guid.NewGuid();
        SetupThreadRead(threadId, Thread(threadId, name: "Old", nameIsAutoDerived: true));
        SetupThreadMessages(/* none */);
        var sut = CreateSut();

        await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread",
            threadId,
            It.Is<Dictionary<string, object>>(d => (string)d["sprk_name"] == "Conversation"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RenameThreadAsync_SetsNameAndFlipsMarkerToEditedInOneUpdate()
    {
        // FR-17 rename: sprk_name + sprk_nameisautoderived=false (Edited) written ATOMICALLY in ONE UpdateAsync.
        var threadId = Guid.NewGuid();
        Dictionary<string, object>? written = null;
        _entity
            .Setup(e => e.UpdateAsync(
                "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => written = f)
            .Returns(Task.CompletedTask);
        var sut = CreateSut();

        var persisted = await sut.RenameThreadAsync(threadId, "  Case strategy  ", CancellationToken.None);

        persisted.Should().Be("Case strategy"); // trimmed
        written.Should().NotBeNull();
        ((string)written!["sprk_name"]).Should().Be("Case strategy");
        ((bool)written[NameIsAutoDerivedField]).Should().BeFalse("rename flips the marker to Edited");
        // ONE update only (name + marker together) — not two writes.
        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RenameThreadAsync_WhenNameBlank_ThrowsAndDoesNotWrite()
    {
        var sut = CreateSut();

        var act = async () => await sut.RenameThreadAsync(Guid.NewGuid(), "   ", CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        _entity.Verify(e => e.UpdateAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // FR-24 pin field name (task 040 schema) — a local literal (not a production-internal reference), mirroring
    // the NameIsAutoDerivedField convention above.
    private const string PinnedField = "sprk_ispinned";

    [Fact]
    public async Task SetPinnedAsync_WithTrue_SetsIsPinnedTrueInOneUpdate()
    {
        // FR-24 pin: sprk_ispinned=true written in ONE UpdateAsync (mirrors RenameThreadAsync's atomic-write shape).
        var threadId = Guid.NewGuid();
        Dictionary<string, object>? written = null;
        _entity
            .Setup(e => e.UpdateAsync(
                "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => written = f)
            .Returns(Task.CompletedTask);
        var sut = CreateSut();

        var persisted = await sut.SetPinnedAsync(threadId, true, CancellationToken.None);

        persisted.Should().BeTrue();
        written.Should().NotBeNull();
        ((bool)written![PinnedField]).Should().BeTrue();
        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SetPinnedAsync_WithFalse_SetsIsPinnedFalse()
    {
        // Unpin: sprk_ispinned=false written explicitly (never relies on the Dataverse column default).
        var threadId = Guid.NewGuid();
        Dictionary<string, object>? written = null;
        _entity
            .Setup(e => e.UpdateAsync(
                "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => written = f)
            .Returns(Task.CompletedTask);
        var sut = CreateSut();

        var persisted = await sut.SetPinnedAsync(threadId, false, CancellationToken.None);

        persisted.Should().BeFalse();
        written.Should().NotBeNull();
        ((bool)written![PinnedField]).Should().BeFalse();
    }

    [Fact]
    public async Task RenameThenReDerive_OnEditedThread_IsNoOp_NamePreserved()
    {
        // End-to-end edit-preserve: rename flips the marker to Edited; a SUBSEQUENT re-derive reads that Edited
        // marker and MUST NOT overwrite the user's name (the marker gate short-circuits — no participant/anchor
        // roll-up is even attempted).
        var threadId = Guid.NewGuid();
        _entity
            .Setup(e => e.UpdateAsync(
                "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = CreateSut();

        await sut.RenameThreadAsync(threadId, "User's Custom Name", CancellationToken.None);

        // Now the thread reads back as Edited (marker=false) — mirror what the rename persisted.
        SetupThreadRead(threadId, Thread(
            threadId, name: "User's Custom Name", nameIsAutoDerived: false,
            regardingType: "sprk_matter", regardingId: Guid.NewGuid().ToString(), regardingName: "Some New Regarding"));

        await sut.ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        // The rename wrote once; the re-derive wrote NOTHING (edit-preserve).
        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReDeriveThreadNameAsync_ReadsOnlyTheTypedLookupsTheThreadHas()
    {
        // Task 129: sprk_regardingreportcard does not exist on sprk_communicationthread; selecting it faults the read in
        // Dataverse, so the re-derive (which the filing reconcile now calls) silently never renamed a thread.
        var threadId = Guid.NewGuid();
        string[]? selected = null;
        _entity
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", threadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string[], CancellationToken>((_, _, c, _) => selected = c)
            .ReturnsAsync(Thread(threadId, name: "x", nameIsAutoDerived: false));

        await CreateSut().ReDeriveThreadNameAsync(threadId, CancellationToken.None);

        selected.Should().NotBeNull();
        selected.Should().NotContain("sprk_regardingreportcard");
        selected.Should().Contain("sprk_regardingmatter");
    }

    // ═════════════════════════════════════════════════════════════════════════════
    // Task 129 — the thread follows the filing (ReconcileThreadForFilingAsync)
    // ═════════════════════════════════════════════════════════════════════════════

    private const int CommunicationTypeEmail = 100000000;
    private const int CommunicationTypeMessage = 100000004;
    private const int PrivacyStatePrivate = 100000001;

    private readonly Mock<Sprk.Bff.Api.Services.Communication.Access.IDirectThreadAccessService> _grants = new();
    private readonly Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble _owners = new();

    /// <summary>The Secure Record names, at their defaults (task 129 F1-b's secure-team detection).</summary>
    private static readonly Microsoft.Extensions.Configuration.IConfiguration SecureNames =
        new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

    private ThreadResolver CreateFilingSut(bool withConfiguration = true, params IThreadKeyStrategy[] strategies) =>
        new(strategies, _entity.Object, _owners, NullLogger<ThreadResolver>.Instance, _grants.Object,
            withConfiguration ? SecureNames : null);

    /// <summary>R's primary name as Dataverse holds it (K2: the anchor's display name is read server-side).</summary>
    private void SetupRecordName(string entityType, Guid recordId, string name)
    {
        var field = RegardingNameFields.PrimaryNameField(entityType)!;
        var row = new DataverseEntity(entityType) { Id = recordId };
        row[field] = name;
        _entity
            .Setup(e => e.RetrieveAsync(entityType, recordId, It.Is<string[]>(c => c.Contains(field)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
    }

    /// <summary>
    /// The Secure Record business unit and its named owner team <paramref name="secureTeam"/>, plus ordinary teams — what
    /// SecureChildShareSynchronizer's detection reads (a team by id; the named team in the business unit).
    /// </summary>
    private void SetupTeams(Guid secureTeam, params Guid[] ordinaryTeams)
    {
        DataverseEntity Team(Guid id, string name)
        {
            var team = new DataverseEntity("team") { Id = id };
            team["name"] = name;
            team["isdefault"] = false;
            team["teamtype"] = new OptionSetValue(Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.OwnerTeamType);
            return team;
        }

        var teams = ordinaryTeams.Select(t => Team(t, "Matter team"))
            .Append(Team(secureTeam, Sprk.Bff.Api.Infrastructure.Dataverse.SecureRecordOwnerTeam.OwnerTeamName(SecureNames)))
            .ToList();
        _entity
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "businessunit"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity> { new("businessunit") { Id = Guid.NewGuid() } }));
        _entity
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "team"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) =>
            {
                var byId = q.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "teamid")?.Values[0] as Guid?;
                return new EntityCollection(byId is { } id
                    ? teams.Where(t => t.Id == id).ToList()
                    : teams.Where(t => t.Id == secureTeam).ToList());
            });
    }

    /// <summary>R's per-record default thread (Tier 2), already there — where an email-only move lands.</summary>
    private Guid SetupRecordsDefaultThread(string entityType, Guid recordId)
    {
        var recordThread = Guid.NewGuid();
        SetupExistingDefaultThread(recordThread);
        _entity
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", recordThread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenThread(recordThread, ThreadTypeRecordAnchored, isDefault: true, regardingType: entityType, regardingId: recordId));
        return recordThread;
    }

    /// <summary>Only the email moved — to R's default thread, granted to R's members — and T was never written.</summary>
    private void VerifyOnlyTheEmailMoved(Guid communicationId, Guid threadId, Guid recordThread, string entityType, Guid recordId)
    {
        VerifyAssigned(communicationId, recordThread, Times.Once());
        VerifyThreadNeverWritten(threadId);
        _grants.Verify(g => g.GrantMessageAccessAsync(
            communicationId, ThreadAssignment.Anchored(recordThread, entityType, recordId), It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    /// <summary>The filed email as the reconcile reads it: its type, its thread, and its typed regarding (none when null).</summary>
    private DataverseEntity SetupFiledEmail(
        Guid communicationId, Guid? threadId, string? entityType, Guid recordId, string? recordName = "Acme v Widgets",
        int type = CommunicationTypeEmail)
    {
        var email = Message(communicationId, entityType, recordId, recordName);
        email["sprk_communicationtype"] = new OptionSetValue(type);
        if (threadId is { } t)
            email["sprk_communicationthread"] = new EntityReference("sprk_communicationthread", t);
        SetupRegardingRead(communicationId, email);
        return email;
    }

    private static DataverseEntity Message(
        Guid id, string? entityType, Guid recordId, string? recordName = null, int type = CommunicationTypeEmail)
    {
        var message = new DataverseEntity("sprk_communication") { Id = id };
        message["sprk_communicationtype"] = new OptionSetValue(type);
        if (entityType is not null)
            SetMessageRegarding(message, entityType, recordId, recordName);
        return message;
    }

    /// <summary>The reconcile's own thread read (it selects the privacy state; the re-derive's read selects the name marker).</summary>
    private void SetupReconcileThreadRead(Guid threadId, DataverseEntity thread) =>
        _entity
            .Setup(e => e.RetrieveAsync(
                "sprk_communicationthread",
                threadId,
                It.Is<string[]>(c => c.Contains("sprk_privacystate") && !c.Contains(NameIsAutoDerivedField)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

    private static DataverseEntity OpenThread(
        Guid id, int threadType = ThreadTypeDirect, bool isDefault = false, string? regardingType = null, Guid? regardingId = null)
    {
        var thread = Thread(id, regardingType: regardingType, regardingId: regardingId?.ToString(), isDefault: isDefault,
            threadType: threadType);
        thread["sprk_privacystate"] = new OptionSetValue(PrivacyStateOpen);
        return thread;
    }

    /// <summary>The thread's active messages (the reconcile's mixed-thread scan).</summary>
    private void SetupThreadMessagesForFiling(Guid threadId, params DataverseEntity[] messages) =>
        _entity
            .Setup(e => e.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => IsThreadMessageScan(q, threadId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(messages.ToList()));

    /// <summary>
    /// Behaviour review F4: the reconcile's scan is exactly T's ACTIVE messages, bounded at the limit + 1 — a query on any
    /// other thread, or one that drops the state filter or the bound, matches nothing (and so reads as an empty thread).
    /// </summary>
    private static bool IsThreadMessageScan(QueryExpression q, Guid threadId) =>
        q.EntityName == "sprk_communication"
        && q.TopCount == ThreadResolver.MaxFilingReconcileMessageScan + 1
        && q.Criteria.Conditions.Any(c => c.AttributeName == "sprk_communicationthread"
            && c.Operator == ConditionOperator.Equal && c.Values.Count == 1 && Equals(c.Values[0], threadId))
        && q.Criteria.Conditions.Any(c => c.AttributeName == "statecode"
            && c.Operator == ConditionOperator.Equal && c.Values.Count == 1 && Equals(c.Values[0], 0));

    private void VerifyThreadNeverWritten(Guid threadId) =>
        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);

    private void VerifyNoGrant() =>
        _grants.Verify(g => g.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()),
            Times.Never);

    [Fact]
    public async Task ReconcileThreadForFiling_UnfiledEmail_InAThreadAnchoredToNoRecord_DoesNothing()
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, entityType: null, Guid.Empty);
        SetupReconcileThreadRead(threadId, OpenThread(threadId)); // Direct, unanchored: on no record's panel

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyThreadNeverWritten(threadId);
        _entity.Verify(e => e.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entity.Verify(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoGrant();
    }

    [Fact]
    public async Task ReconcileThreadForFiling_ThreadAlreadyAnchoredToTheRecord_DoesNothing()
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        SetupReconcileThreadRead(threadId, OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_matter", regardingId: matter));

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyThreadNeverWritten(threadId);
        _entity.Verify(e => e.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoGrant();
    }

    [Theory]
    [InlineData(CommunicationTypeMessage, PrivacyStateOpen)] // a chat thread is a conversation between people, not a filing
    [InlineData(CommunicationTypeEmail, PrivacyStatePrivate)] // a private thread keeps its isolation (note §2)
    public async Task ReconcileThreadForFiling_AChatMessageOrAPrivateThread_IsLeftAsItIs(int type, int privacy)
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", Guid.NewGuid(), type: type);
        var thread = OpenThread(threadId);
        thread["sprk_privacystate"] = new OptionSetValue(privacy);
        SetupReconcileThreadRead(threadId, thread);
        SetupThreadMessagesForFiling(threadId, Message(communicationId, "sprk_matter", Guid.NewGuid()));
        SetupExistingDefaultThread(Guid.NewGuid());

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyThreadNeverWritten(threadId);
        _entity.Verify(e => e.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoGrant();
    }

    [Fact]
    public async Task ReconcileThreadForFiling_DirectThread_IsAnchoredToTheRecord_OwnedByItsTeam_Renamed_AndGrantsFiledMessagesOnly()
    {
        // The dev repro: an inbound email ingested unfiled (Direct thread, app-owned), then filed to a matter.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        // K2: the email's denormalized name is whatever the caller filed with; the thread takes the matter's own name.
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter, "a name the caller typed");
        SetupRecordName("sprk_matter", matter, "PAT-444023");
        SetupReconcileThreadRead(threadId, OpenThread(threadId));
        var filedSibling = Message(Guid.NewGuid(), "sprk_matter", matter);
        var unfiledSibling = Message(Guid.NewGuid(), entityType: null, Guid.Empty);
        SetupThreadMessagesForFiling(threadId, Message(communicationId, "sprk_matter", matter), filedSibling, unfiledSibling);
        // The re-derive reads the thread as the anchor left it (Auto marker).
        SetupThreadRead(threadId, Thread(threadId, name: "RE: invoice", nameIsAutoDerived: true,
            regardingType: "sprk_matter", regardingId: matter.ToString(), regardingName: "PAT-444023", threadType: ThreadTypeRecordAnchored));
        var writes = new List<Dictionary<string, object>>();
        _entity
            .Setup(e => e.UpdateAsync("sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, d, _) => writes.Add(d))
            .Returns(Task.CompletedTask);

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        writes.Should().HaveCount(2, "the anchor, then the marker-gated name re-derive");
        var anchor = writes[0];
        ((OptionSetValue)anchor["sprk_threadtype"]).Value.Should().Be(ThreadTypeRecordAnchored);
        ((EntityReference)anchor["sprk_regardingmatter"]).Id.Should().Be(matter);
        anchor["sprk_regardingrecordid"].Should().Be(matter.ToString());
        anchor["sprk_regardingrecordname"].Should().Be("PAT-444023");
        var owner = (EntityReference)anchor["ownerid"];
        (owner.LogicalName, owner.Id).Should().Be(("team", Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble.DefaultTeamId));
        _owners.Requests.Should().ContainSingle(r => r.TargetEntityLogicalName == "sprk_matter" && r.TargetRecordId == matter,
            "S6: the record thread's owner is the record's team, asked of the one resolver");
        writes[1].Should().ContainKey("sprk_name").WhoseValue.Should().Be("PAT-444023");

        var expected = ThreadAssignment.Anchored(threadId, "sprk_matter", matter);
        _grants.Verify(g => g.GrantMessageAccessAsync(communicationId, expected, It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.GrantMessageAccessAsync(filedSibling.Id, expected, It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.GrantMessageAccessAsync(unfiledSibling.Id, It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()),
            Times.Never, "an unfiled message keeps its visibility (design K-b)");
        // The email stays in its thread: nothing re-assigns it.
        _entity.Verify(e => e.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReconcileThreadForFiling_DirectThread_WhenTheRecordsOwnerIsRefused_LeavesTheThreadUntouched()
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        SetupReconcileThreadRead(threadId, OpenThread(threadId));
        SetupThreadMessagesForFiling(threadId, Message(communicationId, "sprk_matter", matter));
        _owners.TeamId = null; // S6 refusal (e.g. a secure parent that is not isolated)

        var act = () => CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        await act.Should().NotThrowAsync();
        VerifyThreadNeverWritten(threadId);
        _entity.Verify(e => e.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoGrant();
    }

    [Fact]
    public async Task ReconcileThreadForFiling_ThreadOfAPreviousRecord_WhoseOnlyFiledMessageIsReFiled_FollowsTheEmailWhole()
    {
        // Owner clarification 2026-10-11, case (a): T is anchored to R_old (a project) and this email is its only filed
        // message; the email is re-filed to matter R → the whole thread moves to R, and nothing on it still names R_old.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        var oldProject = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter, "a name the caller typed");
        SetupRecordName("sprk_matter", matter, "PAT-444023");
        var previous = OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_project", regardingId: oldProject);
        previous["sprk_regardingrecordid"] = oldProject.ToString();
        previous["sprk_regardingrecordname"] = "Old project";
        previous["sprk_regardingrecordurl"] = "https://old/project";
        SetupReconcileThreadRead(threadId, previous);
        var unfiledSibling = Message(Guid.NewGuid(), entityType: null, Guid.Empty);
        SetupThreadMessagesForFiling(threadId, Message(communicationId, "sprk_matter", matter), unfiledSibling);
        SetupThreadRead(threadId, Thread(threadId, name: "Old project", nameIsAutoDerived: true,
            regardingType: "sprk_matter", regardingId: matter.ToString(), regardingName: "PAT-444023", threadType: ThreadTypeRecordAnchored));
        var writes = new List<Dictionary<string, object>>();
        _entity
            .Setup(e => e.UpdateAsync("sprk_communicationthread", threadId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, d, _) => writes.Add(d))
            .Returns(Task.CompletedTask);

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        writes.Should().HaveCount(2, "the re-anchor, then the name re-derive");
        var anchor = writes[0];
        ((EntityReference)anchor["sprk_regardingmatter"]).Id.Should().Be(matter);
        anchor["sprk_regardingproject"].Should().Be(DBNull.Value, "the previous record's lookup is cleared");
        anchor["sprk_regardingrecordid"].Should().Be(matter.ToString());
        anchor["sprk_regardingrecordname"].Should().Be("PAT-444023");
        anchor["sprk_regardingrecordurl"].Should().Be(DBNull.Value, "R has no url, so the previous record's is cleared");
        ((EntityReference)anchor["ownerid"]).Id.Should().Be(Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble.DefaultTeamId);
        writes[1]["sprk_name"].Should().Be("PAT-444023");
        _grants.Verify(g => g.GrantMessageAccessAsync(
            communicationId, ThreadAssignment.Anchored(threadId, "sprk_matter", matter), It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.GrantMessageAccessAsync(unfiledSibling.Id, It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
        _entity.Verify(e => e.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never, "the email stays in its thread");
    }

    public static TheoryData<string> NotAnchorableThreads => new()
    {
        "master", "previous-records-default", "anchored-to-previous-record-and-mixed", "unanchored-and-mixed",
    };

    [Theory]
    [MemberData(nameof(NotAnchorableThreads))]
    public async Task ReconcileThreadForFiling_ThreadThatCannotFollow_MovesOnlyTheEmailToTheRecordsDefaultThread_AndGrantsIt(string shape)
    {
        // Owner clarification 2026-10-11, case (b) and the master: moving T would pull another message off its record (or
        // move a user's catch-all), so only the email moves.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        var oldProject = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        var thread = shape switch
        {
            "master" => OpenThread(threadId, isDefault: true),
            // Owner decision 2026-10-11: R_old's per-record default is its catch-all — never moved whole, even when this
            // email is its only filed message.
            "previous-records-default" => OpenThread(threadId, ThreadTypeRecordAnchored, isDefault: true,
                regardingType: "sprk_project", regardingId: oldProject),
            "anchored-to-previous-record-and-mixed" => OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_project", regardingId: oldProject),
            _ => OpenThread(threadId),
        };
        SetupReconcileThreadRead(threadId, thread);
        // A default thread is excluded on its own (its only filed message is this email); the others hold a message still
        // filed to R_old.
        SetupThreadMessagesForFiling(threadId, shape is "master" or "previous-records-default"
            ? new[] { Message(communicationId, "sprk_matter", matter) }
            : new[] { Message(communicationId, "sprk_matter", matter), Message(Guid.NewGuid(), "sprk_project", oldProject) });
        var recordThread = Guid.NewGuid();
        SetupExistingDefaultThread(recordThread);
        // The S6 join's read of R's default thread.
        _entity
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", recordThread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OpenThread(recordThread, ThreadTypeRecordAnchored, isDefault: true, regardingType: "sprk_matter", regardingId: matter));

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyAssigned(communicationId, recordThread, Times.Once());
        VerifyThreadNeverWritten(threadId);
        _owners.Reparents.Should().ContainSingle("the S6 join files the email under the record thread's record");
        _grants.Verify(g => g.GrantMessageAccessAsync(
            communicationId, ThreadAssignment.Anchored(recordThread, "sprk_matter", matter), It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ReconcileThreadForFiling_WhenADependencyThrows_NothingEscapes()
    {
        var communicationId = Guid.NewGuid();
        _entity
            .Setup(e => e.RetrieveAsync("sprk_communication", communicationId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse fault"));

        var act = () => CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        await act.Should().NotThrowAsync();
        VerifyNoGrant();
    }

    [Fact]
    public async Task ReconcileThreadForFiling_WhenTheGrantThrows_NothingEscapes()
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        SetupReconcileThreadRead(threadId, OpenThread(threadId));
        SetupThreadMessagesForFiling(threadId, Message(communicationId, "sprk_matter", matter));
        _grants
            .Setup(g => g.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated grant fault"));

        var act = () => CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // ── Security review of 48a2f8687: F1-a, F1-b, F1-c ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReconcileThreadForFiling_ThreadAnchoredToAnotherRecordTheEmailIsStillFiledTo_IsLeftWhereItIs()
    {
        // F1-a: E is filed to secure project P and T is anchored to P; a sharee adds matter M, which outranks P (ADR-024
        // priority), so M becomes R. T must stay on P — re-anchoring it would take P's thread out of P's Secure team.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        var project = Guid.NewGuid();
        var email = SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        email["sprk_regardingproject"] = new EntityReference("sprk_project", project);
        SetupReconcileThreadRead(threadId, OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_project", regardingId: project));
        var both = Message(communicationId, "sprk_matter", matter);
        both["sprk_regardingproject"] = new EntityReference("sprk_project", project);
        SetupThreadMessagesForFiling(threadId, both);
        SetupRecordsDefaultThread("sprk_matter", matter);

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyThreadNeverWritten(threadId);
        _entity.Verify(e => e.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never, "the email stays in P's thread");
        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyNoGrant();
    }

    [Theory]
    [InlineData(true)]  // the thread's owner IS the named Secure Record team
    [InlineData(false)] // no Secure Record names configured: a team-owned thread may be secure (fail closed)
    public async Task ReconcileThreadForFiling_ThreadOwnedByTheSecureTeam_ReFiledToAnOrdinaryRecord_MovesOnlyTheEmail(bool configured)
    {
        // F1-b: T is P's (secure) thread and this email is its only filed message; re-filed to ordinary matter M, T must
        // not follow — that would take P's thread (and every unfiled message in it) out of the Secure team.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        var oldProject = Guid.NewGuid();
        var secureTeam = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        var thread = OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_project", regardingId: oldProject);
        thread["owningteam"] = new EntityReference("team", secureTeam);
        thread["ownerid"] = new EntityReference("team", secureTeam);
        SetupReconcileThreadRead(threadId, thread);
        SetupThreadMessagesForFiling(threadId, Message(communicationId, "sprk_matter", matter), Message(Guid.NewGuid(), entityType: null, Guid.Empty));
        SetupTeams(secureTeam);
        var recordThread = SetupRecordsDefaultThread("sprk_matter", matter);

        await CreateFilingSut(withConfiguration: configured).ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyOnlyTheEmailMoved(communicationId, threadId, recordThread, "sprk_matter", matter);
    }

    [Fact]
    public async Task ReconcileThreadForFiling_ThreadOwnedByAnOrdinaryTeam_FollowsTheEmailWhole()
    {
        // F1-b control (must not fire): a team owner that is not the Secure team does not stop the whole move.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        var oldProject = Guid.NewGuid();
        var projectTeam = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        var thread = OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_project", regardingId: oldProject);
        thread["owningteam"] = new EntityReference("team", projectTeam);
        SetupReconcileThreadRead(threadId, thread);
        SetupThreadMessagesForFiling(threadId, Message(communicationId, "sprk_matter", matter));
        SetupTeams(secureTeam: Guid.NewGuid(), projectTeam);

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        _entity.Verify(e => e.UpdateAsync(
            "sprk_communicationthread", threadId,
            It.Is<Dictionary<string, object>>(d => d.ContainsKey("ownerid")), It.IsAny<CancellationToken>()), Times.Once);
        _grants.Verify(g => g.GrantMessageAccessAsync(
            communicationId, ThreadAssignment.Anchored(threadId, "sprk_matter", matter), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileThreadForFiling_OrdinaryThread_FiledToASecureRecord_MovesOnlyTheEmail()
    {
        // F1-b: T is an ordinary (here user-owned, unanchored) thread; R is secure. T must not move into the Secure team —
        // its unfiled messages would become a secure record's, and the sharee mirror is not written here (K1).
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var project = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_project", project);
        SetupReconcileThreadRead(threadId, OpenThread(threadId));
        SetupThreadMessagesForFiling(threadId, Message(communicationId, "sprk_project", project), Message(Guid.NewGuid(), entityType: null, Guid.Empty));
        _owners.IsSecureOwner = true;
        var recordThread = SetupRecordsDefaultThread("sprk_project", project);

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyOnlyTheEmailMoved(communicationId, threadId, recordThread, "sprk_project", project);
    }

    [Fact]
    public async Task ReconcileThreadForFiling_ThreadThatHoldsAChatMessage_MovesOnlyTheEmail()
    {
        // F1-c: a chat message in T is a conversation between people — anchoring T would re-own it and file every later
        // message that joins it under R. Only the email moves.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        SetupReconcileThreadRead(threadId, OpenThread(threadId));
        SetupThreadMessagesForFiling(
            threadId,
            Message(communicationId, "sprk_matter", matter),
            Message(Guid.NewGuid(), entityType: null, Guid.Empty, type: CommunicationTypeMessage));
        var recordThread = SetupRecordsDefaultThread("sprk_matter", matter);

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyOnlyTheEmailMoved(communicationId, threadId, recordThread, "sprk_matter", matter);
    }

    // ── Behaviour review of 48a2f8687: F2, F3 ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReconcileThreadForFiling_FiledEmailWithNoThread_GetsOneAnchoredToTheRecord_AndIsGrantedIt()
    {
        // F2(b): an Office add-in save captured without a thread (or any filed email whose resolution failed) never showed.
        // The reconcile resolves its thread through the capture paths' own join → grant.
        var communicationId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId: null, "sprk_matter", matter);
        var created = SetupThreadCreateCapture(out var threads);

        await CreateFilingSut(true, new StubThreadKeyStrategy(CommunicationType.Email, ThreadKeyResolution.CreateNew))
            .ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        threads.Should().ContainSingle();
        ((EntityReference)threads[0]["sprk_regardingmatter"]).Id.Should().Be(matter, "the new thread is anchored to the record");
        VerifyAssigned(communicationId, created, Times.Once());
        _grants.Verify(g => g.GrantMessageAccessAsync(
            communicationId, ThreadAssignment.Anchored(created, "sprk_matter", matter), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ReconcileThreadForFiling_UnfiledEmailWithNoThread_DoesNothing()
    {
        var communicationId = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId: null, entityType: null, Guid.Empty);

        await CreateFilingSut(true, new StubThreadKeyStrategy(CommunicationType.Email, ThreadKeyResolution.CreateNew))
            .ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _entity.Verify(e => e.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoGrant();
    }

    [Theory]
    [InlineData(true)]  // T is anchored to R itself — rule 1 would leave the email in it
    [InlineData(false)] // T is unanchored and this is its only filed email — rule 2 would move it whole
    public async Task ReconcileThreadForFiling_InactiveThread_MovesOnlyTheEmail_ToTheRecordsActiveDefaultThread(bool anchoredToTheRecord)
    {
        // F3: a deactivated thread is on no panel. The email leaves it for R's ACTIVE default thread; T is never moved whole.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        var thread = anchoredToTheRecord
            ? OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_matter", regardingId: matter)
            : OpenThread(threadId);
        thread["statecode"] = new OptionSetValue(1); // Inactive
        SetupReconcileThreadRead(threadId, thread);
        SetupThreadMessagesForFiling(threadId, Message(communicationId, "sprk_matter", matter));
        var defaultLookups = new List<QueryExpression>();
        var recordThread = SetupRecordsDefaultThread("sprk_matter", matter);
        _entity
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => defaultLookups.Add(q))
            .ReturnsAsync(new EntityCollection { Entities = { new DataverseEntity("sprk_communicationthread") { Id = recordThread } } });

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyOnlyTheEmailMoved(communicationId, threadId, recordThread, "sprk_matter", matter);
        defaultLookups.Should().NotBeEmpty().And.OnlyContain(q => q.Criteria.Conditions.Any(c =>
                c.AttributeName == "statecode" && c.Operator == ConditionOperator.Equal && Equals(c.Values[0], 0)),
            "an inactive default thread is never joined");
    }

    [Fact]
    public async Task ReconcileThreadForFiling_UnfiledEmail_InAThreadAnchoredToARecord_MovesToItsOwnersMasterThread_WithNoGrant()
    {
        // F3 (unlink): clearing the regarding left the email on the old record's panel. It moves to the thread the unfiled
        // ingest gives it — the per-user master keyed on the email's owner — and nobody is granted it.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var oldProject = Guid.NewGuid();
        var owner = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, entityType: null, Guid.Empty);
        SetupReconcileThreadRead(threadId, OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_project", regardingId: oldProject));
        SetupOwnerRead(communicationId, owner);
        var master = Guid.NewGuid();
        var masterKeys = new List<object>();
        _entity
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) =>
                masterKeys.AddRange(q.Criteria.Conditions.Where(c => c.AttributeName == "sprk_regardingrecordid").Select(c => c.Values[0])))
            .ReturnsAsync(new EntityCollection { Entities = { new DataverseEntity("sprk_communicationthread") { Id = master } } });

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyAssigned(communicationId, master, Times.Once());
        masterKeys.Should().Equal(owner.ToString());
        VerifyThreadNeverWritten(threadId);
        VerifyNoGrant();
    }

    [Fact]
    public async Task ReconcileThreadForFiling_UnfiledEmail_OwnedByTheRecordsTeam_MovesToItsCreatorsMasterThread()
    {
        // An un-filed email keeps its record's TEAM as owner. The per-user master is keyed on a systemuser, so it is keyed on
        // the user who created the email — never on the team's id.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var team = Guid.NewGuid();
        var creator = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, entityType: null, Guid.Empty);
        SetupReconcileThreadRead(threadId, OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_project", regardingId: Guid.NewGuid()));
        _entity
            .Setup(e => e.RetrieveAsync("sprk_communication", communicationId, It.Is<string[]>(c => c.Contains("ownerid")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DataverseEntity("sprk_communication")
            {
                Id = communicationId,
                ["ownerid"] = new EntityReference("team", team),
                ["createdby"] = new EntityReference("systemuser", creator),
            });
        var master = Guid.NewGuid();
        var masterKeys = new List<object>();
        _entity
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) =>
                masterKeys.AddRange(q.Criteria.Conditions.Where(c => c.AttributeName == "sprk_regardingrecordid").Select(c => c.Values[0])))
            .ReturnsAsync(new EntityCollection { Entities = { new DataverseEntity("sprk_communicationthread") { Id = master } } });

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        VerifyAssigned(communicationId, master, Times.Once());
        masterKeys.Should().Equal(creator.ToString());
        VerifyNoGrant();
    }

    [Fact]
    public async Task ReconcileThreadForFiling_UnfiledEmail_InAPrivateRecordThread_KeepsItsThread()
    {
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, entityType: null, Guid.Empty);
        var thread = OpenThread(threadId, ThreadTypeRecordAnchored, regardingType: "sprk_project", regardingId: Guid.NewGuid());
        thread["sprk_privacystate"] = new OptionSetValue(PrivacyStatePrivate);
        SetupReconcileThreadRead(threadId, thread);
        SetupOwnerRead(communicationId, Guid.NewGuid());
        SetupExistingDefaultThread(Guid.NewGuid());

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        _entity.Verify(e => e.UpdateAsync("sprk_communication", communicationId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        VerifyNoGrant();
    }

    [Fact]
    public async Task ReconcileThreadForFiling_TheMessageScan_IsTheThreadsActiveMessages_Bounded()
    {
        // F4: the scan the whole-thread move rests on. A scan of another thread, or of inactive messages too, or unbounded,
        // would decide on the wrong set — the matcher below accepts only the exact query.
        var communicationId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matter = Guid.NewGuid();
        SetupFiledEmail(communicationId, threadId, "sprk_matter", matter);
        SetupReconcileThreadRead(threadId, OpenThread(threadId));
        var scans = new List<QueryExpression>();
        _entity
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .Callback<QueryExpression, CancellationToken>((q, _) => scans.Add(q))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity> { Message(communicationId, "sprk_matter", matter) }));

        await CreateFilingSut().ReconcileThreadForFilingAsync(communicationId, CancellationToken.None);

        scans.Should().ContainSingle().Which.Should().Match<QueryExpression>(q => IsThreadMessageScan(q, threadId));
    }
}
