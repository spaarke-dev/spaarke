using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.DI;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Dataverse.Models;
using Sprk.Bff.Api.Services.Identity;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// unified-access-control-r2 task 188 (D7 T7, #2126 / ISS-015): THE document narrowing on every external document surface,
/// through the REAL routes (list, download, versions), the REAL documents-module fetch pipeline and the REAL
/// <see cref="ExternalDataService"/> over <see cref="ExternalRootEventReadTests.FakeDataverse"/>. Each case passes the root
/// rule (the document and every ancestor are filed under project <see cref="P"/>, which the caller reads), so a hidden
/// document is hidden by the narrowing and nothing else. A hidden document gets the routes' one "not part of this record"
/// refusal, and nothing is read from SPE.
/// </summary>
[Trait("category", "external-access")]
[Trait("status", "repaired")]
public sealed class ExternalDocumentNarrowingRouteTests : IClassFixture<ExternalMultiRootDocumentRouteTests.Fixture>
{
    private const int Standard = 100000000;
    private const int Restricted = 100000002;

    private static readonly Guid P = Guid.Parse("a1880000-0000-4000-8000-0000000000a1");
    private static readonly Guid O = Guid.Parse("a1880000-0000-4000-8000-0000000000e1");
    private static readonly Guid X = Guid.Parse("a1880000-0000-4000-8000-0000000000d1");

    private readonly ExternalMultiRootDocumentRouteTests.Fixture _fixture;

    public ExternalDocumentNarrowingRouteTests(ExternalMultiRootDocumentRouteTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Principal = Reader(P);
        _fixture.Data.AddOpenRoot(ExternalGrantRootType.Matter, O);
    }

    /// <summary>Every case the predicate hides (the task's must-fire list, the scope addition included).</summary>
    public static TheoryData<string> HiddenCases => new()
    {
        "own-restricted",
        "internal-only-communication",
        "private-thread-communication",
        "unreadable-flag",
        "parent-eml-internal-only",
        "parent-eml-restricted",
        "grandparent-restricted",
        "four-hop-chain",
        "cycle",
        "own-visible-link-under-restricted-eml",
        "own-visible-link-four-hop-chain",
    };

    /// <summary>Every case the predicate leaves visible (the task's must-not-fire list).</summary>
    public static TheoryData<string> VisibleCases => new()
    {
        "visible-communication",
        "no-link-standard",
        "standard-parent-no-link",
        "parent-eml-visible-communication",
        "own-visible-link-standard-parent",
        "own-visible-link-parent-eml-internal-only",
    };

    public static TheoryData<string, string> HiddenCasesByRoute => Cross(HiddenCases);

    public static TheoryData<string, string> VisibleCasesByRoute => Cross(VisibleCases);

    private static TheoryData<string, string> Cross(TheoryData<string> cases)
    {
        var data = new TheoryData<string, string>();
        foreach (var @case in cases.Select(c => (string)c[0]))
        {
            data.Add(@case, "content");
            data.Add(@case, "versions");
        }

        return data;
    }

    // ── The list ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(HiddenCases))]
    public async Task List_HidesTheDocument(string @case)
    {
        var plain = Doc("plain");
        Seed(@case);

        var body = await List();

        body.Value.Select(d => d.SprkDocumentid).Should().Equal(new[] { plain }, @case);
        body.Truncated.Should().BeFalse("a document the rule hides is not a fault");
    }

    [Theory]
    [MemberData(nameof(VisibleCases))]
    public async Task List_KeepsTheDocument(string @case)
    {
        var visible = Seed(@case);

        (await List()).Value.Select(d => d.SprkDocumentid).Should().Equal(new[] { visible }, @case);
    }

    [Fact]
    public async Task List_AnOwnValueWiderThanTheRoot_HasNoEffect_TheRootStillWithholds()
    {
        var plain = Doc("plain");
        _fixture.Data.AddSecureRoot(ExternalGrantRootType.Matter, X);
        Doc("Standard, but also filed to secure X", permission: Standard, extra: ("_sprk_matter_value", X));

        (await List()).Value.Select(d => d.SprkDocumentid).Should().Equal(new[] { plain });
    }

    [Fact]
    public async Task List_WhenTheNarrowingReadFails_HidesThePage_AndSaysTruncated()
    {
        Doc("plain");
        Seed("parent-eml-visible-communication");
        _fixture.Data.KeyReadFaults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;

        var body = await List();

        body.Value.Should().BeEmpty("a failed narrowing read shows nothing");
        body.Truncated.Should().BeTrue();
    }

    [Fact]
    public async Task List_ReadsTheAccessPermission_AndTheParentLinks_InTheListQuery()
    {
        Doc("plain");

        await List();

        _fixture.Data.Urls.Should().Contain(u => u.Contains("/sprk_documents?", StringComparison.Ordinal)
            && u.Contains("sprk_accesspermission", StringComparison.Ordinal)
            && u.Contains("_sprk_relatedcommunication_value", StringComparison.Ordinal)
            && u.Contains("_sprk_parentdocument_value", StringComparison.Ordinal)
            && u.Contains("_sprk_project_value eq", StringComparison.Ordinal));
    }

    // ── Download and versions ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(HiddenCasesByRoute))]
    public async Task SingleDocumentRoute_RefusesTheDocument_AsNotUnderTheParent_AndReadsNothingFromSpe(string @case, string route)
    {
        var reference = await RefusalOf(route);
        var id = Seed(@case);
        _fixture.ResetCalls();

        using var response = await Get(id, route);

        (await Summarise(response)).Should().Be(reference, @case);
        _fixture.PointerResolutions.Should().Be(0, "nothing is read from SPE");
        _fixture.FileReads.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(VisibleCasesByRoute))]
    public async Task SingleDocumentRoute_ServesTheDocument(string @case, string route)
    {
        var id = Seed(@case);

        using var response = await Get(id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, @case);
    }

    [Theory]
    [InlineData("content")]
    [InlineData("versions")]
    public async Task SingleDocumentRoute_WhenTheNarrowingReadFails_Refuses_AndReadsNothingFromSpe(string route)
    {
        var reference = await RefusalOf(route);
        var id = Seed("parent-eml-visible-communication");
        _fixture.Data.KeyReadFaults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;
        _fixture.ResetCalls();

        using var response = await Get(id, route);

        (await Summarise(response)).Should().Be(reference);
        _fixture.PointerResolutions.Should().Be(0);
    }

    // ── The documents module (grid fetch) ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DocumentsModuleFetch_HidesOwnAndAncestorRestricted_AndInternalOnlyParents_AndStripsTheColumnsItProjected()
    {
        var emlRestricted = Ancestor(permission: Restricted);
        var emlInternal = Ancestor(communication: Communication(internalOnly: true, ThreadPrivacyState.Open));
        var emlStandard = Ancestor(permission: Standard);
        var parentOfRestricted = Ancestor(parent: emlRestricted);
        var rows = new IReadOnlyDictionary<string, object?>[]
        {
            ModuleRow("plain"),
            ModuleRow("own restricted", permission: Restricted),
            ModuleRow("under a Restricted .eml", parent: emlRestricted),
            ModuleRow("under an internal-only .eml", parent: emlInternal),
            ModuleRow("under a Standard .eml", parent: emlStandard),
            ModuleRow("under a Restricted grandparent", parent: parentOfRestricted),
            ModuleRow("linked to a visible communication", communication: Communication(internalOnly: false, ThreadPrivacyState.Open)),
        };
        string? executed = null;
        var data = _fixture.NewService();

        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto("sprk_document", GridFetch, PagingCookie: null),
            Reader(P), Registry(), new FetchXmlEntityExtractor(),
            (request, _) =>
            {
                executed = request.FetchXml;
                return Task.FromResult(new FetchResponseDto(ProjectThroughFetch(request.FetchXml, rows), MoreRecords: false, PagingCookie: null));
            },
            data.GetNonSecureRootsAsync, data.NarrowDocumentsAsync, NullLogger.Instance, CancellationToken.None,
            data.GetLiveParentRootsAsync);

        executed.Should().Contain("name=\"sprk_accesspermission\"").And.Contain("name=\"sprk_parentdocument\"")
            .And.Contain("name=\"sprk_relatedcommunication\"");
        var body = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!;
        body.Entities.Select(r => r["sprk_documentname"]).Should().Equal(
            "plain", "under a Standard .eml", "linked to a visible communication");
        body.Truncated.Should().BeFalse();
        body.Entities.Should().OnlyContain(r => !r.ContainsKey("sprk_accesspermission") && !r.ContainsKey("sprk_parentdocument"),
            "a column projected only for the narrowing is not returned");
    }

    // ── R3 ISS-011 (#1886): the documents module says whether a row has a file; the pointers never leave ───────────

    private const string UsableDriveId = "b!OWdlYTJhZGUtYWFkZC00NDkxLWE1NjctMTIzNDU2Nzg5MGFi";
    private const string UsableItemId = "01LBYCMX76QPLGITR47BB355T4G2CVDL2B";

    private async Task<(FetchResponseDto Body, string? Executed)> FetchDocuments(params IReadOnlyDictionary<string, object?>[] rows)
    {
        string? executed = null;
        var data = _fixture.NewService();

        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto("sprk_document", GridFetch, PagingCookie: null),
            Reader(P), Registry(), new FetchXmlEntityExtractor(),
            (request, _) =>
            {
                executed = request.FetchXml;
                return Task.FromResult(new FetchResponseDto(ProjectThroughFetch(request.FetchXml, rows), MoreRecords: false, PagingCookie: null));
            },
            data.GetNonSecureRootsAsync, data.NarrowDocumentsAsync, NullLogger.Instance, CancellationToken.None,
            data.GetLiveParentRootsAsync);

        return (result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!, executed);
    }

    [Fact]
    public async Task DocumentsModuleFetch_HasFile_IsTrueWithPointers_AndFalseWithoutThem_ForRowsTheGridDidNotAskPointersFor()
    {
        var (body, executed) = await FetchDocuments(
            ModuleRow("with file", driveId: UsableDriveId, itemId: UsableItemId),
            ModuleRow("no pointers"),
            ModuleRow("drive only", driveId: UsableDriveId),
            ModuleRow("blank item", driveId: UsableDriveId, itemId: " "));

        executed.Should().Contain("name=\"sprk_graphdriveid\"").And.Contain("name=\"sprk_graphitemid\"",
            "the server projects the pointers itself: the grid FetchXML never names them");
        body.Entities.ToDictionary(r => (string)r["sprk_documentname"]!, r => r["hasFile"])
            .Should().Equal(new Dictionary<string, object?>
            {
                ["with file"] = true, ["no pointers"] = false, ["drive only"] = false, ["blank item"] = false,
            });
        body.Entities.Should().HaveCount(4, "a file-less document is still listed");
    }

    [Fact]
    public async Task DocumentsModuleFetch_TheResponseCarriesOnlyTheBoolean_NoPointerAndNoGraph()
    {
        var (body, _) = await FetchDocuments(ModuleRow("with file", driveId: UsableDriveId, itemId: UsableItemId));

        body.Entities.Should().ContainSingle().Which.Keys
            .Should().NotContain(k => k.Contains("graph", StringComparison.OrdinalIgnoreCase))
            .And.Contain("hasFile");
        var json = System.Text.Json.JsonSerializer.Serialize(body);
        json.Should().Contain("\"hasFile\":true");
        json.Should().NotContainEquivalentOf("graph").And.NotContain(UsableDriveId).And.NotContain(UsableItemId);
    }

    [Fact]
    public void FileState_IsDerivedForTheDocumentsModuleOnly_AndNoPointerIsReadable()
    {
        var registry = Registry();
        var documents = registry.FindByEntity("sprk_document")!;

        documents.FileStateAttributes.Should().BeEquivalentTo("sprk_graphdriveid", "sprk_graphitemid");
        documents.ReadableColumns.Should().NotIntersectWith(ExternalModuleRegistry.PointerColumns);
        documents.ReadableColumns.Should().NotContain(c => c.Equals("hasFile", StringComparison.OrdinalIgnoreCase));
        registry.Modules.Where(m => !m.NarrowsDocuments)
            .Should().OnlyContain(m => !m.FileStateAttributes.Any(), "no other module reads a pointer");
    }

    [Fact]
    public async Task DocumentsModuleFetch_WhenTheNarrowingReadFails_HidesTheRows_AndSaysTruncated()
    {
        var eml = Ancestor(permission: Standard);
        var rows = new IReadOnlyDictionary<string, object?>[] { ModuleRow("plain"), ModuleRow("attachment", parent: eml) };
        _fixture.Data.KeyReadFaults["sprk_documents"] = HttpStatusCode.ServiceUnavailable;
        var data = _fixture.NewService();

        var result = await ExternalModuleDataEndpoints.ExecuteScopedFetchCoreAsync(
            new FetchRequestDto("sprk_document", GridFetch, PagingCookie: null),
            Reader(P), Registry(), new FetchXmlEntityExtractor(),
            (request, _) => Task.FromResult(new FetchResponseDto(ProjectThroughFetch(request.FetchXml, rows), MoreRecords: false, PagingCookie: null)),
            data.GetNonSecureRootsAsync, data.NarrowDocumentsAsync, NullLogger.Instance, CancellationToken.None,
            data.GetLiveParentRootsAsync);

        var body = result.Should().BeOfType<Ok<FetchResponseDto>>().Subject.Value!;
        body.Entities.Should().BeEmpty();
        body.Truncated.Should().BeTrue();
    }

    // ── Who is narrowed ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnInternalSystemUser_IsNotNarrowed_AndNothingIsRead()
    {
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity.Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var row = Row(permission: Restricted);
        var before = _fixture.Data.Urls.Count;

        var result = await _fixture.Data.NewService(identity: identity.Object)
            .NarrowDocumentsAsync(SystemUser(), new[] { row });

        result.IsVisible(row).Should().BeTrue("on the internal plane a child's value is display only");
        _fixture.Data.Urls.Count.Should().Be(before);
    }

    [Fact]
    public async Task ASystemUserFlaggedExternal_IsNarrowed()
    {
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity.Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var row = Row(permission: Restricted);

        var result = await _fixture.Data.NewService(identity: identity.Object).NarrowDocumentsAsync(SystemUser(), new[] { row });

        result.IsVisible(row).Should().BeFalse();
    }

    [Fact]
    public async Task ASystemUserWhoseLookupFails_IsNarrowed_FailClosed()
    {
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity.Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("lookup failed (test)"));
        var row = Row(permission: Restricted);

        var result = await _fixture.Data.NewService(identity: identity.Object).NarrowDocumentsAsync(SystemUser(), new[] { row });

        result.IsVisible(row).Should().BeFalse();
    }

    [Fact]
    public async Task AContact_IsNarrowed_EvenWithTheResolverPresent()
    {
        var identity = new Mock<ISystemUserIdentityResolver>();
        identity.Setup(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var row = Row(permission: Restricted);

        var result = await _fixture.Data.NewService(identity: identity.Object).NarrowDocumentsAsync(Reader(P), new[] { row });

        result.IsVisible(row).Should().BeFalse();
        identity.Verify(i => i.IsExternalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── The assistant's seam (main-session decision 2026-10-20) ─────────────────────────────────────────────

    /// <summary>
    /// <c>AddExternalAccess</c> registers the assistant tools' <see cref="AppOnlyDocumentNarrowing"/> over the external
    /// plane's app-only reader: the documents are re-read BY ID through <see cref="ExternalDataService"/> (whose client is the
    /// fake here) and decided there.
    /// </summary>
    [Fact]
    public async Task TheAssistantsNarrowing_IsRegistered_OverTheAppOnlyReader()
    {
        var appOnly = new ExternalRootEventReadTests.FakeDataverse();
        var restricted = Guid.NewGuid();
        var underRestricted = Guid.NewGuid();
        var plain = Guid.NewGuid();
        appOnly.AddAncestorDocument(restricted, Restricted, communication: null, parent: null);
        appOnly.AddAncestorDocument(underRestricted, accessPermission: null, communication: null, parent: restricted);
        appOnly.AddAncestorDocument(plain, accessPermission: null, communication: null, parent: null);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build());
        services.AddSingleton<Azure.Core.TokenCredential>(new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential());
        services.AddExternalAccess();
        services.AddHttpClient<ExternalDataService>().ConfigurePrimaryHttpMessageHandler(() => appOnly);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var narrowing = await scope.ServiceProvider.GetRequiredService<AppOnlyDocumentNarrowing>()(
            new[] { restricted, underRestricted, plain }, CancellationToken.None);

        narrowing.IsVisible(plain).Should().BeTrue();
        narrowing.ReasonFor(restricted).Should().Be(DocumentNarrowingReason.OwnRestricted);
        narrowing.ReasonFor(underRestricted).Should().Be(DocumentNarrowingReason.AncestorRestricted);
        appOnly.KeyReads("sprk_documents").Should().ContainSingle("the documents by id; the parent came back with them");
    }

    /// <summary>
    /// Task 188 round 3 slice B: <c>AddExternalAccess</c> registers the internal document routes'
    /// <see cref="InternalRouteDocumentNarrowing"/> over the SAME app-only reader and decision (not a test double), and its
    /// one cost line says <c>plane=internal-routes</c>. Test hosts that are not about the narrowing replace it with
    /// <c>AllVisibleDocumentNarrowing</c>; this pins that production does not.
    /// </summary>
    [Fact]
    public async Task TheInternalRoutesNarrowing_IsRegistered_OverTheAppOnlyReader()
    {
        var appOnly = new ExternalRootEventReadTests.FakeDataverse();
        var logger = new CapturingLogger();
        var restricted = Guid.NewGuid();
        var underRestricted = Guid.NewGuid();
        var plain = Guid.NewGuid();
        appOnly.AddAncestorDocument(restricted, Restricted, communication: null, parent: null);
        appOnly.AddAncestorDocument(underRestricted, accessPermission: null, communication: null, parent: restricted);
        appOnly.AddAncestorDocument(plain, accessPermission: null, communication: null, parent: null);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build());
        services.AddSingleton<Azure.Core.TokenCredential>(new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential());
        services.AddExternalAccess();
        services.AddHttpClient<ExternalDataService>().ConfigurePrimaryHttpMessageHandler(() => appOnly);
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ExternalDataService>>(logger);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();

        var narrowing = await scope.ServiceProvider.GetRequiredService<InternalRouteDocumentNarrowing>()(
            new[] { restricted, underRestricted, plain }, CancellationToken.None);

        narrowing.IsVisible(plain).Should().BeTrue();
        narrowing.ReasonFor(restricted).Should().Be(DocumentNarrowingReason.OwnRestricted);
        narrowing.ReasonFor(underRestricted).Should().Be(DocumentNarrowingReason.AncestorRestricted);
        appOnly.KeyReads("sprk_documents").Should().ContainSingle("the documents by id, app-only; the parent came back with them");
        logger.Lines.Should().ContainSingle(l => l.Contains("[EXT-NARROWING] cost")).Which.Should()
            .Contain("plane=internal-routes").And.Contain("rows=3").And.Contain("hidden=2");
    }

    [Fact]
    public async Task TheAssistantsNarrowing_LogsOneCostLine_IdsNeverLogged()
    {
        var appOnly = new ExternalRootEventReadTests.FakeDataverse();
        var logger = new CapturingLogger();
        var eml = Guid.NewGuid();
        var attachment = Guid.NewGuid();
        appOnly.AddAncestorDocument(eml, Restricted, communication: null, parent: null);
        appOnly.AddAncestorDocument(attachment, accessPermission: null, communication: null, parent: eml);
        var service = new ExternalDataService(
            new HttpClient(appOnly),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                .Build(),
            new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
            logger);

        await service.ReadAndNarrowDocumentsAsync(new[] { attachment }, CancellationToken.None);

        logger.Lines.Should().ContainSingle(l => l.Contains("[EXT-NARROWING] cost")).Which.Should()
            .Contain("plane=assistant").And.Contain("rows=1").And.Contain("hidden=1").And.Contain("hopLevels=1")
            .And.Contain("documentReads=2").And.Contain("communicationReads=0").And.Contain("threadReads=0")
            .And.NotContain(attachment.ToString()).And.NotContain(eml.ToString());
    }

    [Fact]
    public async Task TheExternalPlane_LogsOneCostLinePerRequest()
    {
        var logger = new CapturingLogger();
        var eml = Ancestor(permission: Restricted);
        var service = new ExternalDataService(
            new HttpClient(_fixture.Data),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                .Build(),
            new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
            logger);
        var rows = new[]
        {
            new DocumentNarrowingRow(Guid.NewGuid(), DocumentPermission.NotNarrowed, Guid.Empty, eml),
            new DocumentNarrowingRow(Guid.NewGuid(), DocumentPermission.NotNarrowed, Guid.Empty, Guid.Empty),
        };

        await service.NarrowDocumentsAsync(Reader(P), rows);

        logger.Lines.Should().ContainSingle(l => l.Contains("[EXT-NARROWING] cost")).Which.Should()
            .Contain("plane=external").And.Contain("rows=2").And.Contain("hidden=1").And.Contain("documentReads=1")
            .And.NotContain(eml.ToString());
    }

    /// <summary>Records every formatted line at every level (the cost line is Debug below the threshold).</summary>
    private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<ExternalDataService>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => _lines.ToList();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;

        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId,
            TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _lines.Enqueue(formatter(state, exception));
    }

    // ── Seeding ──────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>Seeds one case's document (under <see cref="P"/>) and whatever its chain needs; returns its id.</summary>
    private string Seed(string @case)
    {
        switch (@case)
        {
            case "own-restricted":
                return Doc(@case, permission: Restricted);
            case "internal-only-communication":
                return Doc(@case, communication: Communication(internalOnly: true, ThreadPrivacyState.Open));
            case "private-thread-communication":
                return Doc(@case, communication: Communication(internalOnly: false, ThreadPrivacyState.Private));
            case "unreadable-flag":
                return Doc(@case, communication: Communication(internalOnly: null, ThreadPrivacyState.Open));
            case "parent-eml-internal-only":
                return Doc(@case, parent: Ancestor(communication: Communication(internalOnly: true, ThreadPrivacyState.Open)));
            case "parent-eml-restricted":
                return Doc(@case, parent: Ancestor(permission: Restricted));
            case "grandparent-restricted":
                return Doc(@case, parent: Ancestor(permission: Standard, parent: Ancestor(permission: Restricted)));
            case "four-hop-chain":
            {
                var p4 = Ancestor(communication: Communication(internalOnly: false, ThreadPrivacyState.Open));
                return Doc(@case, parent: Ancestor(parent: Ancestor(parent: Ancestor(parent: p4))));
            }
            case "cycle":
            {
                var a = Guid.NewGuid();
                var b = Ancestor(parent: a);
                Ancestor(id: a, parent: b);
                return Doc(@case, parent: a);
            }
            case "visible-communication":
                return Doc(@case, communication: Communication(internalOnly: false, ThreadPrivacyState.Open));
            case "no-link-standard":
                return Doc(@case, permission: Standard);
            case "standard-parent-no-link":
                return Doc(@case, parent: Ancestor(permission: Standard));
            case "parent-eml-visible-communication":
                return Doc(@case, parent: Ancestor(communication: Communication(internalOnly: false, ThreadPrivacyState.Open)));
            // Main-session decision 2026-10-20: the ancestor leg checks EVERY ancestor (past the document's own link); the
            // communication leg takes the FIRST link; a chain past the bound fails closed even under an own visible link.
            case "own-visible-link-under-restricted-eml":
                return Doc(@case, communication: Communication(internalOnly: false, ThreadPrivacyState.Open),
                    parent: Ancestor(permission: Restricted));
            case "own-visible-link-four-hop-chain":
                return Doc(@case, communication: Communication(internalOnly: false, ThreadPrivacyState.Open),
                    parent: Ancestor(parent: Ancestor(parent: Ancestor(parent: Ancestor()))));
            case "own-visible-link-standard-parent":
                return Doc(@case, communication: Communication(internalOnly: false, ThreadPrivacyState.Open),
                    parent: Ancestor(permission: Standard));
            case "own-visible-link-parent-eml-internal-only":
                return Doc(@case, communication: Communication(internalOnly: false, ThreadPrivacyState.Open),
                    parent: Ancestor(communication: Communication(internalOnly: true, ThreadPrivacyState.Open)));
            default:
                throw new ArgumentOutOfRangeException(nameof(@case), @case, null);
        }
    }

    /// <summary>A document filed to <see cref="P"/>, with its own value and links.</summary>
    private string Doc(string name, int? permission = null, Guid? communication = null, Guid? parent = null,
        (string Column, Guid Value)? extra = null)
    {
        var links = new List<(string Column, Guid Value)>();
        if (communication is { } c) links.Add(("_sprk_relatedcommunication_value", c));
        if (parent is { } p) links.Add(("_sprk_parentdocument_value", p));
        if (extra is { } e) links.Add(e);
        var id = _fixture.Data.AddDocument("projects", P, name, links.ToArray());
        if (permission is { } value)
            _fixture.Data.SetColumn("sprk_documents", "sprk_documentid", id, "sprk_accesspermission", value);
        return id;
    }

    /// <summary>
    /// An ancestor document (not on any route's list), filed under <see cref="P"/> in the filing world so the root rule's
    /// live-parent walk passes it: what hides a descendant is then the narrowing alone.
    /// </summary>
    private Guid Ancestor(Guid? id = null, int? permission = null, Guid? communication = null, Guid? parent = null)
    {
        var documentId = id ?? Guid.NewGuid();
        _fixture.Data.AddAncestorDocument(documentId, permission, communication, parent);
        _fixture.Data.AddFilingRow("sprk_document", documentId, ("sprk_project", "sprk_project", P));
        return documentId;
    }

    /// <summary>A communication (filed under ordinary matter <see cref="O"/>) on a new thread of <paramref name="privacy"/>.</summary>
    private Guid Communication(bool? internalOnly, ThreadPrivacyState privacy)
    {
        var id = Guid.NewGuid();
        var thread = Guid.NewGuid();
        _fixture.Data.AddThread(thread, (int)privacy);
        _fixture.Data.AddCommunication(id, internalOnly, thread);
        _fixture.Data.AddFilingRow("sprk_communication", id, ("sprk_regardingmatter", "sprk_matter", O));
        return id;
    }

    private const string GridFetch =
        "<fetch><entity name='sprk_document'><attribute name='sprk_documentid'/><attribute name='sprk_documentname'/>"
        + "<attribute name='sprk_project'/></entity></fetch>";

    /// <summary>A documents-module row filed to <see cref="P"/>, as the SDK returns it (lookups as references, the choice as an option).</summary>
    private static IReadOnlyDictionary<string, object?> ModuleRow(
        string name, int? permission = null, Guid? parent = null, Guid? communication = null,
        string? driveId = null, string? itemId = null) => new Dictionary<string, object?>
    {
        ["sprk_graphdriveid"] = driveId,
        ["sprk_graphitemid"] = itemId,
        ["@logicalName"] = "sprk_document",
        ["sprk_documentid"] = Guid.NewGuid(),
        ["sprk_documentname"] = name,
        ["sprk_project"] = new EntityReference("sprk_project", P),
        ["sprk_accesspermission"] = permission is { } p ? new OptionSetValue(p) : null,
        ["sprk_parentdocument"] = parent is { } d ? new EntityReference("sprk_document", d) : null,
        ["sprk_relatedcommunication"] = communication is { } c ? new EntityReference("sprk_communication", c) : null,
    };

    /// <summary>Dataverse omits a null attribute and every attribute the executed FetchXML did not name.</summary>
    private static IReadOnlyList<IReadOnlyDictionary<string, object?>> ProjectThroughFetch(
        string fetchXml, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var names = System.Xml.Linq.XDocument.Parse(fetchXml).Descendants("attribute")
            .Select(a => (string?)a.Attribute("name")).OfType<string>().ToHashSet(StringComparer.OrdinalIgnoreCase);
        return rows.Select(row => (IReadOnlyDictionary<string, object?>)row
                .Where(kv => kv.Key == "@logicalName" || (kv.Value is not null && names.Contains(kv.Key)))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static ExternalModuleRegistry Registry()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddExternalAccess();
        return services.BuildServiceProvider().GetRequiredService<ExternalModuleRegistry>();
    }

    private static DocumentNarrowingRow Row(int? permission) => new(
        Guid.NewGuid(),
        permission == Restricted ? DocumentPermission.Restricted : DocumentPermission.NotNarrowed,
        Guid.Empty,
        Guid.Empty);

    // ── Routes ───────────────────────────────────────────────────────────────────────────────────────────────

    private async Task<ExternalDocumentListResponse> List()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return (await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/projects/{P}/documents"))!;
    }

    private async Task<HttpResponseMessage> Get(string documentId, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.GetAsync($"/api/v1/external/projects/{P}/documents/{documentId}/{route}");
    }

    /// <summary>The route's response for a document that is not under the parent at all (the reference refusal).</summary>
    private async Task<string> RefusalOf(string route)
    {
        var elsewhere = _fixture.Data.AddDocument("projects", Guid.NewGuid(), "Another record's");
        using var response = await Get(elsewhere, route);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        return await Summarise(response);
    }

    /// <summary>A refusal as a caller sees it: status, title and detail (no per-request fields).</summary>
    private static async Task<string> Summarise(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return $"{(int)response.StatusCode}";

        using var doc = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return string.Join("|", new[] { "403" }.Concat(doc.RootElement.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance" or "correlationId" or "type"))
            .Select(p => $"{p.Name}={p.Value}")));
    }

    private static CallerPrincipal Reader(Guid project) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = Guid.NewGuid(),
        Email = "external.user@example.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(project, ExternalAccessLevel.ViewOnly) },
        MatterAccess = new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };

    private static CallerPrincipal SystemUser() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = Guid.NewGuid(),
        SystemUserId = Guid.NewGuid(),
        Email = "internal.user@example.test",
        ProjectAccess = new[] { CallerProjectAccess.FromLevel(P, ExternalAccessLevel.ViewOnly) },
        MatterAccess = new Dictionary<Guid, AccessRights>(),
        WorkAssignmentAccess = new Dictionary<Guid, AccessRights>(),
    };
}
