using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
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
        => new(strategies, _entity.Object, NullLogger<ThreadResolver>.Instance);

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
        communication["sprk_regardingrecordid"] = matterId.ToString();
        communication["sprk_regardingrecordtype"] = "sprk_matter";
        communication["sprk_regardingrecordname"] = "Acme v Widgets";
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
        thread.GetAttributeValue<string>("sprk_regardingrecordtype").Should().Be("sprk_matter");
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
        communication["sprk_regardingrecordid"] = matterId.ToString();
        communication["sprk_regardingrecordtype"] = "sprk_matter";
        communication["sprk_regardingrecordname"] = "Acme v Widgets";
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
        thread.GetAttributeValue<string>("sprk_regardingrecordtype").Should().Be("sprk_matter");
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
        communication["sprk_regardingrecordid"] = matterId.ToString();
        communication["sprk_regardingrecordtype"] = "sprk_matter";
        SetupRegardingRead(request.CommunicationId, communication);
        var existingDefault = Guid.NewGuid();
        SetupExistingDefaultThread(existingDefault);
        var sut = CreateSut(new StubThreadKeyStrategy(CommunicationType.Message, ThreadKeyResolution.Skip));

        var result = await sut.ResolveAndAssignThreadAsync(request, CancellationToken.None);

        result.Should().Be(existingDefault);
        _entity.Verify(e => e.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyAssigned(request.CommunicationId, existingDefault, Times.Once());
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
        thread.GetAttributeValue<string>("sprk_regardingrecordtype").Should().Be("systemuser");
        thread.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(ownerId.ToString());
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
        commA["sprk_regardingrecordid"] = Guid.NewGuid().ToString();
        commA["sprk_regardingrecordtype"] = "sprk_project";
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
        communication["sprk_regardingrecordid"] = Guid.NewGuid().ToString();
        communication["sprk_regardingrecordtype"] = "sprk_matter";
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

    /// <summary>Sets up the thread retrieve for <c>ReDeriveThreadNameAsync</c> (columns include the marker + denormalized regarding quartet).</summary>
    private void SetupThreadRead(Guid threadId, DataverseEntity thread) =>
        _entity
            .Setup(e => e.RetrieveAsync(
                "sprk_communicationthread",
                threadId,
                It.Is<string[]>(c => c.Contains(NameIsAutoDerivedField) && c.Contains("sprk_regardingrecordtype")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);

    private static DataverseEntity Thread(
        Guid id, string? name = null, bool? nameIsAutoDerived = null,
        string? regardingType = null, string? regardingId = null, string? regardingName = null)
    {
        var thread = new DataverseEntity("sprk_communicationthread") { Id = id };
        if (name is not null) thread["sprk_name"] = name;
        if (nameIsAutoDerived is { } marker) thread[NameIsAutoDerivedField] = marker;
        if (regardingType is not null) thread["sprk_regardingrecordtype"] = regardingType;
        if (regardingId is not null) thread["sprk_regardingrecordid"] = regardingId;
        if (regardingName is not null) thread["sprk_regardingrecordname"] = regardingName;
        return thread;
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
        // The Tier-3 per-user master ("Messages") is keyed (systemuser, ownerId) — "systemuser" is NOT a
        // resolvable regarding entity and MUST NEVER be treated as one, regardless of the marker.
        var threadId = Guid.NewGuid();
        var ownerId = Guid.NewGuid();
        SetupThreadRead(threadId, Thread(
            threadId, name: "Messages", nameIsAutoDerived: true,
            regardingType: "systemuser", regardingId: ownerId.ToString()));
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
}
