using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Identity;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// field-mapping-server-write-path-r1 task 056b (design note <c>notes/task-056-private-message-design.md</c> §3.3, Q1, Q6, C3):
/// the per-viewer read of private messages. A private message is a Direct message that also names one core record it is about.
/// <list type="bullet">
///   <item><c>GET /threads?kind=direct&amp;about={table}:{id}</c> lists the caller's strict pair threads holding one of the caller's
///   private messages about the record (<see cref="CommunicationThreadReadService.ListThreadsAsync"/>).</item>
///   <item><c>GET /threads/{id}/messages?about=…</c> narrows a thread's messages to those (<see cref="CommunicationThreadReadService.ReadThreadAsync"/>).</item>
///   <item>Every thread-read message carries <c>privateContext</c>, the record resolved AS THE VIEWER, or null.</item>
/// </list>
/// The impersonated query is answered by <see cref="RowSecurityEmulator"/>: each row lists the users row security lets read it,
/// and the emulator evaluates the <c>$filter</c>, <c>$orderby</c>, <c>$top</c> and <c>$select</c> the service sends (an unknown
/// column throws, as Dataverse 400s). So the filters below are tested by their effect, and row security stays the visibility gate.
/// Boundary doubles only: the impersonated query, the caller resolver, the app-only thread and share reads of the pair check. The
/// read service, the access filter and the pair-shape rule are production code.
/// </summary>
public class CommunicationPrivateContextReadSeamTests
{
    private const string ThreadTable = "sprk_communicationthread";
    private const int ThreadTypeDirect = 100000001;
    private const int ThreadTypeRecordAnchored = 100000000;
    private const int ReadMask = 1;

    private static readonly Guid Alice = Guid.Parse("56560000-0000-0000-0000-00000000000a");
    private static readonly Guid Bob = Guid.Parse("56560000-0000-0000-0000-00000000000b");
    private static readonly Guid Carol = Guid.Parse("56560000-0000-0000-0000-00000000000c"); // the third user
    private static readonly Guid Dave = Guid.Parse("56560000-0000-0000-0000-00000000000d");

    private static readonly Guid Matter = Guid.Parse("56560000-0000-0000-0000-0000000000a1");
    private static readonly Guid OtherMatter = Guid.Parse("56560000-0000-0000-0000-0000000000a2");
    private static readonly Guid Project = Guid.Parse("56560000-0000-0000-0000-0000000000b1");

    /// <summary>The 055 <c>kind=direct</c> thread query for <c>top=20</c>, captured from the code before task 056 (byte-identical pin).</summary>
    private const string DirectListQuery055Top20 =
        "$select=sprk_communicationthreadid,sprk_name,sprk_threadtype,createdon,sprk_ispinned,_sprk_regardingmatter_value," +
        "_sprk_regardingproject_value,_sprk_regardinginvoice_value,_sprk_regardingservicerequest_value," +
        "_sprk_regardingworkassignment_value,_sprk_regardingevent_value,_sprk_regardingbudget_value,_sprk_regardinganalysis_value," +
        "_sprk_regardingorganization_value,_sprk_regardingaccount_value,_sprk_regardingperson_value" +
        "&$filter=statecode eq 0 and sprk_threadtype eq 100000001 and _sprk_regardingmatter_value eq null and " +
        "_sprk_regardingproject_value eq null and _sprk_regardinginvoice_value eq null and _sprk_regardingservicerequest_value eq null and " +
        "_sprk_regardingworkassignment_value eq null and _sprk_regardingevent_value eq null and _sprk_regardingbudget_value eq null and " +
        "_sprk_regardinganalysis_value eq null and _sprk_regardingorganization_value eq null and _sprk_regardingaccount_value eq null and " +
        "_sprk_regardingperson_value eq null&$orderby=createdon desc,sprk_communicationthreadid desc&$top=51";

    private readonly RowSecurityEmulator _dataverse = new();
    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<IDataverseRecordShareService> _shares = new();
    private readonly Dictionary<Guid, DataverseEntity> _threadEntities = new();
    private readonly Dictionary<Guid, List<DataversePrincipalAccess>> _threadShares = new();
    private DateTimeOffset _clock = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    public CommunicationPrivateContextReadSeamTests()
    {
        _entities
            .Setup(e => e.RetrieveAsync(ThreadTable, It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) =>
                _threadEntities.TryGetValue(id, out var t) ? t : new DataverseEntity(ThreadTable, Guid.Empty));
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync(ThreadTable, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, CancellationToken _) =>
                (IReadOnlyList<DataversePrincipalAccess>)(_threadShares.TryGetValue(id, out var s) ? s : new List<DataversePrincipalAccess>()));
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());

        _dataverse.AddRecord("sprk_matters", "sprk_matterid", Matter, "sprk_mattername", "Acme v. Widget", Alice, Bob, Carol, Dave);
        _dataverse.AddRecord("sprk_matters", "sprk_matterid", OtherMatter, "sprk_mattername", "Other matter", Alice, Bob, Carol, Dave);
        _dataverse.AddRecord("sprk_projects", "sprk_projectid", Project, "sprk_projectname", "Widget rollout", Alice, Bob, Carol, Dave);
    }

    private CommunicationThreadReadService Sut(Guid caller)
    {
        var resolver = new Mock<ICallerSystemUserResolver>();
        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(caller.ToString("D")));

        return new CommunicationThreadReadService(
            _dataverse,
            new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>()),
            resolver.Object,
            Mock.Of<ISystemUserIdentityResolver>(),
            Mock.Of<ILogger<CommunicationThreadReadService>>(),
            appOnlyQuery: null,
            entities: _entities.Object,
            recordShares: _shares.Object);
    }

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity(new[] { new Claim("oid", Guid.NewGuid().ToString()) }, "test"));

    private Task<ThreadListResult> ListAbout(Guid caller, string about, int? top = null, string? pageToken = null) =>
        Sut(caller).ListThreadsAsync(Principal(), null, top, pageToken, CancellationToken.None, kind: "direct", about: about);

    private Task<ThreadReadResult> ReadThread(Guid caller, Guid thread, string? about = null) =>
        Sut(caller).ReadThreadAsync(thread, Principal(), null, null, CancellationToken.None, about);

    // ── the about list (§3.3) ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListThreads_About_ReturnsOnlyThePairThreadsHoldingAPrivateMessageAboutTheRecord()
    {
        var aboutMatter = Pair(owner: Alice, other: Bob);
        PrivateMessage(aboutMatter, from: Alice, to: Bob, matter: Matter);
        var plain = Pair(owner: Alice, other: Dave);
        DirectMessage(plain, from: Alice, to: Dave);
        var aboutOther = Pair(owner: Carol, other: Alice);
        PrivateMessage(aboutOther, from: Carol, to: Alice, matter: OtherMatter);

        var sender = await ListAbout(Alice, $"sprk_matter:{Matter}");
        var recipient = await ListAbout(Bob, $"sprk_matter:{Matter}");

        sender.Threads.Select(t => t.ThreadId).Should().Equal(aboutMatter);
        recipient.Threads.Select(t => t.ThreadId).Should().Equal(aboutMatter);
        sender.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task ListThreads_About_ARecordWithNoPrivateMessages_IsAnEmptyList_WithNoThreadQuery()
    {
        var plain = Pair(owner: Alice, other: Bob);
        DirectMessage(plain, from: Alice, to: Bob);

        var result = await ListAbout(Alice, $"sprk_matter:{Matter}");

        result.Threads.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
        _dataverse.Calls.Should().NotContain(c => c.Set == "sprk_communicationthreads");
    }

    [Fact]
    public async Task ListThreads_About_KeepsThe055OneToOneNarrowing()
    {
        // A Direct thread that is not a strict pair (a third user share) holds a private message about the record: not listed.
        var notStrict = Pair(owner: Alice, other: Bob);
        _threadShares[notStrict].Add(new(DataversePrincipalRef.User(Dave), ReadMask, DateTimeOffset.UtcNow));
        PrivateMessage(notStrict, from: Alice, to: Bob, matter: Matter);
        var strict = Pair(owner: Dave, other: Alice);
        PrivateMessage(strict, from: Dave, to: Alice, matter: Matter);

        var result = await ListAbout(Alice, $"sprk_matter:{Matter}");

        result.Threads.Select(t => t.ThreadId).Should().Equal(strict);
    }

    [Fact]
    public async Task ListThreads_About_PagesWithThe055Cursor_WithoutSkippingOrRepeating()
    {
        var threads = new[] { Pair(Alice, Bob), Pair(Alice, Dave), Pair(Carol, Alice) };
        foreach (var t in threads)
            PrivateMessage(t, from: Alice, to: OtherOf(t, Alice), matter: Matter);

        var first = await ListAbout(Alice, $"sprk_matter:{Matter}", top: 2);
        var second = await ListAbout(Alice, $"sprk_matter:{Matter}", top: 2, pageToken: first.NextPageToken);

        // Newest thread first (055 order).
        first.Threads.Select(t => t.ThreadId).Should().Equal(threads[2], threads[1]);
        first.HasMore.Should().BeTrue();
        second.Threads.Select(t => t.ThreadId).Should().Equal(threads[0]);
        second.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task ListThreads_About_TheMessageQueryCarriesTheAboutFilter_AndTheThreadQueryIs055PlusOneClause()
    {
        var thread = Pair(owner: Alice, other: Bob);
        PrivateMessage(thread, from: Alice, to: Bob, matter: Matter);

        await ListAbout(Alice, $"sprk_matter:{Matter}", top: 20);

        var messageQuery = _dataverse.Calls.Single(c => c.Set == "sprk_communications").OData;
        var filter = Param(messageQuery, "$filter");
        filter.Should().Contain($"_sprk_privatecontextmatter_value eq {Matter}");
        filter.Should().Contain($"(_sprk_sentby_value eq {Alice} or _sprk_directrecipient_value eq {Alice})");
        foreach (var field in RegardingFieldMap.AllRegardingFields)
            filter.Should().Contain($"_{field}_value eq null", "a filed message is an ordinary record message");
        filter.Should().EndWith("statecode eq 0");
        _dataverse.Calls.Single(c => c.Set == "sprk_communications").Caller.Should().Be(Alice, "the message read is impersonated");

        var threadQuery = _dataverse.Calls.Single(c => c.Set == "sprk_communicationthreads").OData;
        threadQuery.Replace($" and (sprk_communicationthreadid eq {thread})", string.Empty, StringComparison.Ordinal)
            .Should().Be(DirectListQuery055Top20);
    }

    [Fact]
    public async Task ListThreads_KindDirectWithoutAbout_SendsThe055QueryByteForByte_AndNoMessageRead()
    {
        Pair(owner: Alice, other: Bob);

        await Sut(Alice).ListThreadsAsync(Principal(), null, 20, null, CancellationToken.None, kind: "direct");

        _dataverse.Calls.Select(c => c.Set).Should().Equal("sprk_communicationthreads");
        _dataverse.Calls[0].OData.Should().Be(DirectListQuery055Top20);
    }

    // ── AC2: a third user who reads the record and its threads sees nothing of the private message ────────────────────────────

    [Fact]
    public async Task AThirdUserWhoReadsTheRecordAndItsThreads_GetsNothing_FromTheByRegardingTheThreadOrTheAboutRead()
    {
        var (recordThread, pair, privateMessage) = RecordWithAPrivateMessage(thirdUserReadsEverything: false);

        var byRegarding = await Sut(Carol).ReadByRegardingAsync("sprk_matter", Matter, Principal(), CancellationToken.None);
        var pairRead = await ReadThread(Carol, pair);
        var pairAbout = await ReadThread(Carol, pair, $"sprk_matter:{Matter}");
        var recordAbout = await ReadThread(Carol, recordThread, $"sprk_matter:{Matter}");
        var list = await ListAbout(Carol, $"sprk_matter:{Matter}");

        byRegarding.Threads.Select(t => t.ThreadId).Should().Equal(recordThread);
        byRegarding.Threads.SelectMany(t => t.Messages).Should().NotContain(m => m.MessageId == privateMessage)
            .And.NotBeEmpty("the record's own message is read: the third user does read the record's threads");
        pairRead.Messages.Should().BeEmpty();
        pairAbout.Messages.Should().BeEmpty();
        recordAbout.Messages.Should().BeEmpty();
        list.Threads.Should().BeEmpty();

        // Control: the pair reads it.
        (await ReadThread(Bob, pair, $"sprk_matter:{Matter}")).Messages.Select(m => m.MessageId).Should().Equal(privateMessage);
    }

    [Fact]
    public async Task AWideReaderWhomRowSecurityShowsEveryRow_GetsNothing_FromTheByRegardingOrTheAboutReads()
    {
        // C3: row security decides who reads; the about filters narrow what is shown to the pair.
        var (recordThread, pair, privateMessage) = RecordWithAPrivateMessage(thirdUserReadsEverything: true);

        var byRegarding = await Sut(Carol).ReadByRegardingAsync("sprk_matter", Matter, Principal(), CancellationToken.None);
        var pairAbout = await ReadThread(Carol, pair, $"sprk_matter:{Matter}");
        var recordAbout = await ReadThread(Carol, recordThread, $"sprk_matter:{Matter}");
        var list = await ListAbout(Carol, $"sprk_matter:{Matter}");

        byRegarding.Threads.SelectMany(t => t.Messages).Should().NotContain(m => m.MessageId == privateMessage);
        pairAbout.Messages.Should().BeEmpty();
        recordAbout.Messages.Should().BeEmpty();
        list.Threads.Should().BeEmpty();
    }

    // ── a filed message is an ordinary record message ───────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFiledPrivateMessage_IsLeftOutOfTheAboutReads_AndShowsNoPrivateContext()
    {
        var pair = Pair(owner: Alice, other: Bob);
        var filed = PrivateMessage(pair, from: Alice, to: Bob, matter: Matter, filedUnderMatter: Matter);

        var list = await ListAbout(Alice, $"sprk_matter:{Matter}");
        var about = await ReadThread(Alice, pair, $"sprk_matter:{Matter}");
        var whole = await ReadThread(Alice, pair);

        list.Threads.Should().BeEmpty();
        about.Messages.Should().BeEmpty();
        whole.Messages.Should().ContainSingle(m => m.MessageId == filed).Which.PrivateContext.Should().BeNull();
    }

    // ── the thread read and privateContext ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadThread_About_NarrowsToThePrivateMessagesAboutThatRecord()
    {
        var pair = Pair(owner: Alice, other: Bob);
        var aboutMatter = PrivateMessage(pair, from: Alice, to: Bob, matter: Matter);
        PrivateMessage(pair, from: Bob, to: Alice, matter: OtherMatter);
        DirectMessage(pair, from: Bob, to: Alice);
        var reply = PrivateMessage(pair, from: Bob, to: Alice, matter: Matter);

        var result = await ReadThread(Alice, pair, $"sprk_matter:{Matter}");

        result.Messages.Select(m => m.MessageId).Should().Equal(aboutMatter, reply);
        result.Count.Should().Be(2);
    }

    [Fact]
    public async Task ReadThread_PrivateContext_IsTheRecordReadAsTheViewer_AndNullForAPlainMessage()
    {
        var pair = Pair(owner: Alice, other: Bob);
        var aboutMatter = PrivateMessage(pair, from: Alice, to: Bob, matter: Matter);
        var aboutProject = PrivateMessage(pair, from: Bob, to: Alice, project: Project);
        var plain = DirectMessage(pair, from: Bob, to: Alice);

        var result = await ReadThread(Alice, pair);

        var byId = result.Messages.ToDictionary(m => m.MessageId);
        byId[aboutMatter].PrivateContext.Should().Be(new MessagePrivateContext("sprk_matter", Matter, "Acme v. Widget"));
        byId[aboutProject].PrivateContext.Should().Be(new MessagePrivateContext("sprk_project", Project, "Widget rollout"));
        byId[plain].PrivateContext.Should().BeNull();
        _dataverse.Calls.Where(c => c.Set is "sprk_matters" or "sprk_projects").Should().OnlyContain(c => c.Caller == Alice,
            "the record is read as the viewer, never app-only");
    }

    [Fact]
    public async Task ReadThread_PrivateContext_IsNull_WhenTheViewerCannotReadTheRecord()
    {
        var hidden = Guid.Parse("56560000-0000-0000-0000-0000000000a9");
        _dataverse.AddRecord("sprk_matters", "sprk_matterid", hidden, "sprk_mattername", "Hidden matter", Bob);
        var pair = Pair(owner: Alice, other: Bob);
        var message = PrivateMessage(pair, from: Bob, to: Alice, matter: hidden);

        var viewerDenied = await ReadThread(Alice, pair);
        var viewerAllowed = await ReadThread(Bob, pair);

        viewerDenied.Messages.Single(m => m.MessageId == message).PrivateContext.Should().BeNull();
        viewerAllowed.Messages.Single(m => m.MessageId == message).PrivateContext!.Name.Should().Be("Hidden matter");
    }

    [Fact]
    public async Task ReadThread_PrivateContext_IsNull_WhenTheRecordReadFaults_AndTheMessagesAreStillRead()
    {
        var pair = Pair(owner: Alice, other: Bob);
        var message = PrivateMessage(pair, from: Alice, to: Bob, matter: Matter);
        _dataverse.FaultingSets.Add("sprk_matters");

        var result = await ReadThread(Alice, pair);

        result.Messages.Single(m => m.MessageId == message).PrivateContext.Should().BeNull();
    }

    [Fact]
    public async Task ReadThread_TheRecordNames_AreReadInOneBatchPerTable()
    {
        var pair = Pair(owner: Alice, other: Bob);
        PrivateMessage(pair, from: Alice, to: Bob, matter: Matter);
        PrivateMessage(pair, from: Bob, to: Alice, matter: Matter);
        PrivateMessage(pair, from: Bob, to: Alice, matter: OtherMatter);
        PrivateMessage(pair, from: Bob, to: Alice, project: Project);

        var result = await ReadThread(Alice, pair);

        result.Messages.Should().OnlyContain(m => m.PrivateContext != null && m.PrivateContext.Name != null);
        var matterReads = _dataverse.Calls.Where(c => c.Set == "sprk_matters").ToList();
        matterReads.Should().ContainSingle();
        Param(matterReads[0].OData, "$filter").Should().Contain(Matter.ToString()).And.Contain(OtherMatter.ToString());
        _dataverse.Calls.Count(c => c.Set == "sprk_projects").Should().Be(1);
    }

    [Fact]
    public async Task ReadThread_WithNoPrivateMessages_ReadsNoRecord()
    {
        var pair = Pair(owner: Alice, other: Bob);
        DirectMessage(pair, from: Alice, to: Bob);

        await ReadThread(Alice, pair);

        _dataverse.Calls.Should().NotContain(c => c.Set == "sprk_matters" || c.Set == "sprk_projects" || c.Set == "sprk_workassignments");
    }

    // ── 400s, before any read ───────────────────────────────────────────────────────────────────────────────────────────────

    public static TheoryData<string> MalformedAbout() => new()
    {
        "", "sprk_matter", "sprk_matter:", ":" + Matter, "sprk_matter:not-a-guid", $"sprk_matter:{Guid.Empty}",
        $"sprk_invoice:{Matter}", $"sprk_communication:{Matter}",
    };

    [Theory]
    [MemberData(nameof(MalformedAbout))]
    public async Task ListThreads_AMalformedAbout_Is400_BeforeAnyRead(string about)
    {
        var act = () => ListAbout(Alice, about);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
        _dataverse.Calls.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(MalformedAbout))]
    public async Task ReadThread_AMalformedAbout_Is400_BeforeAnyRead(string about)
    {
        var act = () => ReadThread(Alice, Guid.NewGuid(), about);

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
        _dataverse.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("all")]
    public async Task ListThreads_AboutWithAKindOtherThanDirect_Is400_BeforeAnyRead(string? kind)
    {
        var act = () => Sut(Alice).ListThreadsAsync(
            Principal(), null, null, null, CancellationToken.None, kind: kind, about: $"sprk_matter:{Matter}");

        (await act.Should().ThrowAsync<SdapProblemException>()).Which.StatusCode.Should().Be(400);
        _dataverse.Calls.Should().BeEmpty();
    }

    // ── arrange ─────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A matter with its record thread (one message every reader of the record reads) and an Alice–Bob pair holding
    /// a private message about the matter. Carol reads the matter and its thread; with <paramref name="thirdUserReadsEverything"/>
    /// row security also shows her the pair's rows (a reader wider than Basic).</summary>
    private (Guid RecordThread, Guid Pair, Guid PrivateMessage) RecordWithAPrivateMessage(bool thirdUserReadsEverything)
    {
        var recordThread = Guid.NewGuid();
        _dataverse.AddThread(ThreadRow(recordThread, ThreadTypeRecordAnchored, regardingMatter: Matter), Alice, Bob, Carol);
        _dataverse.AddMessage(MessageRow(Guid.NewGuid(), recordThread, sentBy: Alice, recipient: null, regardingMatter: Matter), Alice, Bob, Carol);

        var pair = Pair(owner: Alice, other: Bob, extraReaders: thirdUserReadsEverything ? new[] { Carol } : Array.Empty<Guid>());
        var message = PrivateMessage(pair, from: Alice, to: Bob, matter: Matter,
            extraReaders: thirdUserReadsEverything ? new[] { Carol } : Array.Empty<Guid>());
        return (recordThread, pair, message);
    }

    /// <summary>A Direct pair thread as <c>POST /threads/direct</c> creates it: owner + one Read share; row security shows it to both.</summary>
    private Guid Pair(Guid owner, Guid other, IReadOnlyList<Guid>? extraReaders = null)
    {
        var id = Guid.NewGuid();
        _threadEntities[id] = new DataverseEntity(ThreadTable, id)
        {
            ["sprk_threadtype"] = new OptionSetValue(ThreadTypeDirect),
            ["ownerid"] = new EntityReference("systemuser", owner),
            ["sprk_privacystate"] = new OptionSetValue((int)ThreadPrivacyState.Open),
        };
        _threadShares[id] = new List<DataversePrincipalAccess> { new(DataversePrincipalRef.User(other), ReadMask, DateTimeOffset.UtcNow) };
        _dataverse.AddThread(ThreadRow(id, ThreadTypeDirect), new[] { owner, other }.Concat(extraReaders ?? Array.Empty<Guid>()).ToArray());
        return id;
    }

    private Guid OtherOf(Guid thread, Guid participant)
    {
        var owner = ((EntityReference)_threadEntities[thread]["ownerid"]).Id;
        return owner == participant ? _threadShares[thread][0].Principal.Id : owner;
    }

    private Guid PrivateMessage(
        Guid thread, Guid from, Guid to, Guid? matter = null, Guid? project = null, Guid? filedUnderMatter = null,
        IReadOnlyList<Guid>? extraReaders = null)
    {
        var id = Guid.NewGuid();
        _dataverse.AddMessage(
            MessageRow(id, thread, from, to, contextMatter: matter, contextProject: project, regardingMatter: filedUnderMatter),
            new[] { from, to }.Concat(extraReaders ?? Array.Empty<Guid>()).ToArray());
        return id;
    }

    private Guid DirectMessage(Guid thread, Guid from, Guid to)
    {
        var id = Guid.NewGuid();
        _dataverse.AddMessage(MessageRow(id, thread, from, to), from, to);
        return id;
    }

    private Dictionary<string, JsonElement> ThreadRow(Guid id, int threadType, Guid? regardingMatter = null)
    {
        _clock = _clock.AddMinutes(1);
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationthreadid"] = Json(id.ToString()),
            ["sprk_name"] = Json("Conversation"),
            ["sprk_threadtype"] = JsonSerializer.SerializeToElement(threadType),
            ["sprk_ispinned"] = JsonSerializer.SerializeToElement(false),
            ["createdon"] = Json(_clock.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)),
            ["statecode"] = JsonSerializer.SerializeToElement(0),
        };
        foreach (var column in ThreadResolver.ThreadRegardingColumns)
            row[$"_{column}_value"] = Json(null);
        if (regardingMatter is { } m)
            row["_sprk_regardingmatter_value"] = Json(m.ToString());
        return row;
    }

    private Dictionary<string, JsonElement> MessageRow(
        Guid id, Guid thread, Guid sentBy, Guid? recipient,
        Guid? contextMatter = null, Guid? contextProject = null, Guid? regardingMatter = null)
    {
        _clock = _clock.AddMinutes(1);
        var row = new Dictionary<string, JsonElement>
        {
            ["sprk_communicationid"] = Json(id.ToString()),
            ["sprk_body"] = Json("body"),
            ["sprk_bodyformat"] = Json(null),
            ["sprk_communicationtype"] = JsonSerializer.SerializeToElement(100000004),
            ["sprk_from"] = Json(null),
            ["sprk_subject"] = Json(null),
            ["sprk_to"] = Json(null),
            ["sprk_direction"] = Json(null),
            ["_sprk_sentby_value"] = Json(sentBy.ToString()),
            ["_createdby_value"] = Json(null),
            ["sprk_sentat"] = Json(null),
            ["createdon"] = Json(_clock.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)),
            ["sprk_inreplyto"] = Json(null),
            ["sprk_isinternalonly"] = JsonSerializer.SerializeToElement(true),
            ["sprk_privilegeclassification"] = Json(null),
            ["sprk_isprivate"] = JsonSerializer.SerializeToElement(false),
            ["_sprk_communicationthread_value"] = Json(thread.ToString()),
            ["_sprk_directrecipient_value"] = Json(recipient?.ToString()),
            [$"_{PrivateContextColumns.Matter}_value"] = Json(contextMatter?.ToString()),
            [$"_{PrivateContextColumns.Project}_value"] = Json(contextProject?.ToString()),
            [$"_{PrivateContextColumns.WorkAssignment}_value"] = Json(null),
            ["statecode"] = JsonSerializer.SerializeToElement(0),
        };
        foreach (var field in RegardingFieldMap.AllRegardingFields)
            row[$"_{field}_value"] = Json(null);
        if (regardingMatter is { } m)
            row["_sprk_regardingmatter_value"] = Json(m.ToString());
        return row;
    }

    private static JsonElement Json(string? value) => JsonSerializer.SerializeToElement(value);

    private static string Param(string odata, string name) =>
        odata.Split('&').Select(p => p.Split('=', 2)).Single(p => p[0] == name)[1];
}

/// <summary>
/// An <see cref="IImpersonatedCommunicationQuery"/> that answers like Dataverse for a small in-memory set: a row is returned only
/// to a caller listed as one of its readers (row security), and the request's <c>$filter</c> (<c>eq</c>/<c>ne</c>/<c>lt</c>/<c>gt</c>,
/// <c>and</c>, <c>or</c>, parentheses), <c>$orderby</c>, <c>$top</c> and <c>$select</c> are applied. A filter or select naming a
/// column the table's rows do not have throws, as Dataverse answers 400.
/// </summary>
internal sealed class RowSecurityEmulator : IImpersonatedCommunicationQuery
{
    private readonly Dictionary<string, List<(Dictionary<string, JsonElement> Row, HashSet<Guid> Readers)>> _sets = new(StringComparer.Ordinal);

    public List<(string Set, string OData, Guid Caller)> Calls { get; } = new();

    public HashSet<string> FaultingSets { get; } = new(StringComparer.Ordinal);

    public void AddThread(Dictionary<string, JsonElement> row, params Guid[] readers) => Add("sprk_communicationthreads", row, readers);

    public void AddMessage(Dictionary<string, JsonElement> row, params Guid[] readers) => Add("sprk_communications", row, readers);

    public void AddRecord(string set, string idColumn, Guid id, string nameColumn, string name, params Guid[] readers) =>
        Add(set, new Dictionary<string, JsonElement>
        {
            [idColumn] = JsonSerializer.SerializeToElement(id.ToString()),
            [nameColumn] = JsonSerializer.SerializeToElement(name),
        }, readers);

    private void Add(string set, Dictionary<string, JsonElement> row, Guid[] readers)
    {
        if (!_sets.TryGetValue(set, out var rows))
            _sets[set] = rows = new();
        rows.Add((row, readers.ToHashSet()));
    }

    public Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(
        string entitySetName, string? odataQuery, Guid callerSystemUserId, CancellationToken ct)
    {
        Calls.Add((entitySetName, odataQuery ?? string.Empty, callerSystemUserId));
        if (FaultingSets.Contains(entitySetName))
            throw new HttpRequestException("read faulted");

        var parameters = (odataQuery ?? string.Empty).Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : string.Empty, StringComparer.Ordinal);

        IEnumerable<Dictionary<string, JsonElement>> rows = (_sets.TryGetValue(entitySetName, out var all) ? all : new())
            .Where(r => r.Readers.Contains(callerSystemUserId))
            .Select(r => r.Row);

        if (parameters.TryGetValue("$filter", out var filter))
        {
            var expression = new FilterParser(filter).Parse();
            rows = rows.Where(expression).ToList();
        }

        if (parameters.TryGetValue("$orderby", out var orderBy))
        {
            var keys = orderBy.Split(',').Select(k => k.Trim().Split(' ')).Select(k => (Column: k[0], Desc: k.Length > 1 && k[1] == "desc")).ToList();
            rows = rows.OrderBy(r => r, Comparer<Dictionary<string, JsonElement>>.Create((a, b) =>
            {
                foreach (var (column, desc) in keys)
                {
                    var c = string.CompareOrdinal(Text(a, column), Text(b, column));
                    if (c != 0)
                        return desc ? -c : c;
                }

                return 0;
            })).ToList();
        }

        if (parameters.TryGetValue("$top", out var top))
            rows = rows.Take(int.Parse(top, CultureInfo.InvariantCulture));

        var result = rows.ToList();
        if (parameters.TryGetValue("$select", out var select))
        {
            var columns = select.Split(',');
            result = result.Select(r => columns.ToDictionary(c => c, c => Column(r, c))).ToList();
        }

        return Task.FromResult<IReadOnlyList<Dictionary<string, JsonElement>>>(result);
    }

    private static JsonElement Column(Dictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) ? value : throw new InvalidOperationException($"Unknown column '{column}' (Dataverse answers 400).");

    private static string Text(Dictionary<string, JsonElement> row, string column)
    {
        var value = Column(row, column);
        return value.ValueKind == JsonValueKind.String ? value.GetString()! : value.ValueKind == JsonValueKind.Null ? string.Empty : value.GetRawText();
    }

    /// <summary>A recursive-descent parser for the filter subset the read service sends.</summary>
    private sealed class FilterParser
    {
        private readonly List<string> _tokens;
        private int _at;

        public FilterParser(string filter) =>
            _tokens = Regex.Matches(filter, @"\(|\)|[^\s()]+").Select(m => m.Value).ToList();

        public Func<Dictionary<string, JsonElement>, bool> Parse()
        {
            var expression = Or();
            if (_at != _tokens.Count)
                throw new InvalidOperationException($"Unparsed filter text at token {_at}: {string.Join(' ', _tokens.Skip(_at))}");
            return expression;
        }

        private Func<Dictionary<string, JsonElement>, bool> Or()
        {
            var left = And();
            while (_at < _tokens.Count && _tokens[_at] == "or")
            {
                _at++;
                var l = left;
                var r = And();
                left = row => l(row) || r(row);
            }

            return left;
        }

        private Func<Dictionary<string, JsonElement>, bool> And()
        {
            var left = Primary();
            while (_at < _tokens.Count && _tokens[_at] == "and")
            {
                _at++;
                var l = left;
                var r = Primary();
                left = row => l(row) && r(row);
            }

            return left;
        }

        private Func<Dictionary<string, JsonElement>, bool> Primary()
        {
            if (_tokens[_at] == "(")
            {
                _at++;
                var inner = Or();
                if (_tokens[_at++] != ")")
                    throw new InvalidOperationException("Unbalanced parenthesis in filter.");
                return inner;
            }

            var column = _tokens[_at++];
            var op = _tokens[_at++];
            var literal = _tokens[_at++];
            return row => Compare(Column(row, column), op, literal);
        }

        private static bool Compare(JsonElement value, string op, string literal)
        {
            if (literal == "null")
            {
                var isNull = value.ValueKind == JsonValueKind.Null;
                return op switch { "eq" => isNull, "ne" => !isNull, _ => throw new InvalidOperationException($"{op} null") };
            }

            var text = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()!,
                JsonValueKind.Null => null,
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => value.GetRawText(),
            };
            if (text is null)
                return op == "ne";

            int Order() =>
                DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var a)
                && DateTimeOffset.TryParse(literal, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var b)
                    ? a.CompareTo(b)
                    : string.Compare(text, literal, StringComparison.OrdinalIgnoreCase);

            return op switch
            {
                "eq" => string.Equals(text, literal, StringComparison.OrdinalIgnoreCase),
                "ne" => !string.Equals(text, literal, StringComparison.OrdinalIgnoreCase),
                "lt" => Order() < 0,
                "gt" => Order() > 0,
                _ => throw new InvalidOperationException($"Unsupported operator '{op}'."),
            };
        }
    }
}
