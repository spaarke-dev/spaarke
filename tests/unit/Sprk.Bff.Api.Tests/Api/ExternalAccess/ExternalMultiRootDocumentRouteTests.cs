using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Tests.AccessControl;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r3 task 039, fix round 2: the single-document external routes (content download
/// and version history) apply the every-root rule (<see cref="ExternalMultiRootRule"/>, an INTERIM decision pending the
/// owner) to ONE document, as the list does to many. A caller holding Read on project P only, who knows the id of a
/// document filed to P AND to a matter M they cannot read, is refused with the route's one "not part of the requested
/// record" response, before any SPE pointer is resolved.
/// </summary>
/// <remarks>
/// Driven through the REAL routes and the REAL <see cref="ExternalDataService"/> over
/// <see cref="ExternalRootEventReadTests.FakeDataverse"/>, which answers the single-row read through the request's
/// <c>$select</c>: a read that drops a root link the rule needs sees it as "not filed" and a refusal test fails. The
/// doubles are the caller principal and the storage layer (pointer resolver, SPE facade), which counts what it was asked.
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalMultiRootDocumentRouteTests : IClassFixture<ExternalMultiRootDocumentRouteTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999039");
    private static readonly Guid RecordA = Guid.Parse("a0390000-0000-4000-8000-00000000000a");
    private static readonly Guid RecordB = Guid.Parse("a0390000-0000-4000-8000-00000000000b");

    /// <summary>A secure record the caller holds nothing on.</summary>
    private static readonly Guid Hidden = Guid.Parse("a0390000-0000-4000-8000-0000000000dd");

    private readonly Fixture _fixture;

    public ExternalMultiRootDocumentRouteTests(Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    /// <summary>The two single-document routes, as the path suffix after <c>/documents/{id}/</c>.</summary>
    public static TheoryData<string, string> SegmentsAndRoutes
    {
        get
        {
            var data = new TheoryData<string, string>();
            foreach (var segment in new[] { "projects", "matters", "workassignments" })
            {
                data.Add(segment, "content");
                data.Add(segment, "versions");
            }

            return data;
        }
    }

    // =====================================================================
    // Must-fire: the document is also filed to a root the caller cannot read -> the not-under-parent response
    // =====================================================================

    [Theory]
    [MemberData(nameof(SegmentsAndRoutes))]
    public async Task ADocumentAlsoFiledToARootTheCallerCannotRead_IsRefused_AsNotUnderTheParent_AndNoPointerIsResolved(
        string segment, string route)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var reference = await RefusalOf(segment, route); // a document that is not under this record: the route's own answer

        // Typed link to each of the OTHER two roots, and the related twin of each of the THREE (the six root links).
        var hiddenLinks = new List<(string Column, Guid Value)>
        {
            ("_sprk_relatedproject_value", Hidden),
            ("_sprk_relatedmatter_value", Hidden),
            ("_sprk_relatedworkassignment_value", Hidden),
        };
        if (segment != "projects") hiddenLinks.Add(("_sprk_project_value", Hidden));
        if (segment != "matters") hiddenLinks.Add(("_sprk_matter_value", Hidden));
        if (segment != "workassignments") hiddenLinks.Add(("_sprk_workassignment_value", Hidden));

        foreach (var link in hiddenLinks)
        {
            _fixture.ResetCalls();
            var id = _fixture.Data.AddDocument(segment, RecordA, "Also filed elsewhere", link);

            using var response = await Get(segment, id, route);

            (await Summarise(response)).Should().Be(reference, $"a document also filed through {link.Column} reads as not under this record");
            _fixture.PointerResolutions.Should().Be(0, "authorization precedes pointer resolution");
            _fixture.FileReads.Should().Be(0, $"nothing is read from SPE for a refused document ({link.Column})");
        }
    }

    [Theory]
    [MemberData(nameof(SegmentsAndRoutes))]
    public async Task TheRefusal_IsIdentical_ForAMissingDocument_AMisfiledOne_AndAMultiRootOne(string segment, string route)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var misfiled = _fixture.Data.AddDocument(segment, RecordB, "Another record's");
        var multiRoot = _fixture.Data.AddDocument(segment, RecordA, "Also in a hidden record", ("_sprk_relatedmatter_value", Hidden));

        using var missing = await Get(segment, Guid.NewGuid(), route);
        using var other = await Get(segment, misfiled, route);
        using var also = await Get(segment, multiRoot, route);

        var expected = await Summarise(missing);
        expected.Should().StartWith("403");
        (await Summarise(other)).Should().Be(expected);
        (await Summarise(also)).Should().Be(expected, "the deny cannot be used to tell whether a document exists");
    }

    [Theory]
    [MemberData(nameof(SegmentsAndRoutes))]
    public async Task ADocumentWithAMalformedRootLookup_IsRefused_FailClosed(string segment, string route)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var reference = await RefusalOf(segment, route);

        var garbled = _fixture.Data.AddDocumentRaw(segment, RecordA, "Garbled", ("_sprk_relatedmatter_value", "not-a-guid"));
        var numeric = _fixture.Data.AddDocumentRaw(segment, RecordA, "Numeric", ("_sprk_matter_value", 42));

        foreach (var id in new[] { garbled, numeric })
        {
            _fixture.ResetCalls();
            using var response = await Get(segment, id, route);

            (await Summarise(response)).Should().Be(reference);
            _fixture.PointerResolutions.Should().Be(0);
        }
    }

    // =====================================================================
    // Must-not-fire: nothing the caller may read is withheld
    // =====================================================================

    [Theory]
    [MemberData(nameof(SegmentsAndRoutes))]
    public async Task ADocumentFiledToOnlyTheRouteRoot_IsServed(string segment, string route)
    {
        _fixture.Principal = Principal(segment, RecordA);
        var id = _fixture.Data.AddDocument(segment, RecordA, "Just here");

        using var response = await Get(segment, id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.PointerResolutions.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SegmentsAndRoutes))]
    public async Task ADocumentAlsoFiledToARootTheCallerCanRead_IsServed(string segment, string route)
    {
        // Read on the route root AND on a second root of another type; the document carries a typed link to the second root and
        // the related twin of the route root (the same record twice is one root).
        var second = segment == "matters" ? "projects" : "matters";
        _fixture.Principal = PrincipalHolding((segment, RecordA), (second, RecordB));
        var id = _fixture.Data.AddDocument(
            segment, RecordA, "Dual",
            (second == "projects" ? "_sprk_project_value" : "_sprk_matter_value", RecordB),
            (RelatedTwinOf(segment), RecordA));

        using var response = await Get(segment, id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.PointerResolutions.Should().Be(1);
    }

    [Theory]
    [MemberData(nameof(SegmentsAndRoutes))]
    public async Task ADocumentFiledToAnotherRecordOfTheSameType_TheCallerCanReadToo_IsServed(string segment, string route)
    {
        _fixture.Principal = PrincipalHolding((segment, RecordA), (segment, RecordB));
        var id = _fixture.Data.AddDocument(segment, RecordA, "Two projects", (RelatedTwinOf(segment), RecordB));

        using var response = await Get(segment, id, route);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    // =====================================================================
    // The read itself: all six root links, through the route's lookup too
    // =====================================================================

    [Theory]
    [InlineData(ExternalDataService.DocumentParentKind.Project)]
    [InlineData(ExternalDataService.DocumentParentKind.Matter)]
    [InlineData(ExternalDataService.DocumentParentKind.WorkAssignment)]
    [InlineData(ExternalDataService.DocumentParentKind.ServiceRequest)]
    [InlineData(ExternalDataService.DocumentParentKind.Invoice)]
    public async Task TheScopingRead_SelectsEveryRootLink_OfTheCanonicalDocumentLinkVocabulary(ExternalDataService.DocumentParentKind kind)
    {
        await _fixture.NewService().GetDocumentParentAndNameAsync(kind, Guid.NewGuid());

        var url = _fixture.Data.Urls.Should().ContainSingle().Subject;
        foreach (var field in DocumentLinkFields.All.Where(f =>
                     f.TargetEntityLogicalName is "sprk_project" or "sprk_matter" or "sprk_workassignment"))
        {
            url.Should().Contain($"_{field.LogicalName}_value", $"{field.LogicalName} is a root link of a document");
        }

        url.Should().Contain("sprk_documentname").And.Contain(ExternalDataService.GetDocumentParentBinding(kind).LookupValueAttribute);
        url.Split("$select=")[1].Split(',').Should().OnlyHaveUniqueItems("a duplicate column is a malformed select");
    }

    [Fact]
    public async Task TheScopingRead_OfAMissingDocument_IsAbsent_AndOfAnExistingOne_CarriesTheRootLinks()
    {
        var id = Guid.Parse(_fixture.Data.AddDocument("projects", RecordA, "Doc", ("_sprk_relatedmatter_value", Hidden)));
        var service = _fixture.NewService();

        var absent = await service.GetDocumentParentAndNameAsync(ExternalDataService.DocumentParentKind.Project, Guid.NewGuid());
        var found = await service.GetDocumentParentAndNameAsync(ExternalDataService.DocumentParentKind.Project, id);

        absent.Should().Be(ExternalDataService.DocumentScopeRead.Absent);
        found.ParentId.Should().Be(RecordA);
        found.DocumentName.Should().Be("Doc");
        found.Roots.Project.Should().Be(RecordA.ToString());
        found.Roots.RelatedMatter.Should().Be(Hidden.ToString());
        found.Roots.Matter.Should().BeNull();
    }

    [Fact]
    public void DocumentScopeRead_IsReadableUnder_NeedsTheRouteParentAndEveryRoot()
    {
        var caller = Principal("projects", RecordA);
        var projectOnly = new ExternalDataService.DocumentRootLinks(RecordA.ToString(), null, null, null, null, null);
        var withHidden = projectOnly with { RelatedMatter = Hidden.ToString() };

        new ExternalDataService.DocumentScopeRead(RecordA, "x", projectOnly).IsReadableUnder(RecordA, caller).Should().BeTrue();
        new ExternalDataService.DocumentScopeRead(RecordA, "x", withHidden).IsReadableUnder(RecordA, caller).Should().BeFalse();
        new ExternalDataService.DocumentScopeRead(RecordB, "x", projectOnly).IsReadableUnder(RecordA, caller).Should().BeFalse();
        new ExternalDataService.DocumentScopeRead(null, "x", projectOnly).IsReadableUnder(RecordA, caller).Should().BeFalse();
        ExternalDataService.DocumentScopeRead.Absent.IsReadableUnder(RecordA, caller).Should().BeFalse();
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    /// <summary>The refusal the route gives a document that is not under the record (it exists, under another record).</summary>
    private async Task<string> RefusalOf(string segment, string route)
    {
        var elsewhere = _fixture.Data.AddDocument(segment, RecordB, "Another record's");
        using var response = await Get(segment, elsewhere, route);
        var summary = await Summarise(response);
        summary.Should().StartWith("403");
        return summary;
    }

    private async Task<HttpResponseMessage> Get(string segment, string documentId, string route)
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/documents/{documentId}/{route}");
    }

    private Task<HttpResponseMessage> Get(string segment, Guid documentId, string route) => Get(segment, documentId.ToString(), route);

    /// <summary>A refusal as a caller sees it: status, title and detail (no per-request fields).</summary>
    private static async Task<string> Summarise(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Forbidden)
            return $"{(int)response.StatusCode}";

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        return string.Join("|", new[] { "403" }.Concat(root.EnumerateObject()
            .Where(p => p.Name is not ("traceId" or "instance" or "correlationId" or "type"))
            .Select(p => $"{p.Name}={p.Value}")));
    }

    private static string RelatedTwinOf(string segment) => segment switch
    {
        "projects" => "_sprk_relatedproject_value",
        "matters" => "_sprk_relatedmatter_value",
        _ => "_sprk_relatedworkassignment_value",
    };

    private static CallerPrincipal Principal(string segment, Guid record) => PrincipalHolding((segment, record));

    private static CallerPrincipal PrincipalHolding(params (string Segment, Guid Record)[] held) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = held.Where(h => h.Segment == "projects")
            .Select(h => CallerProjectAccess.FromLevel(h.Record, ExternalAccessLevel.ViewOnly)).ToList(),
        MatterAccess = held.Where(h => h.Segment == "matters").ToDictionary(h => h.Record, _ => AccessRights.Read),
        WorkAssignmentAccess = held.Where(h => h.Segment == "workassignments").ToDictionary(h => h.Record, _ => AccessRights.Read),
    };

    // =====================================================================
    // Fixture
    // =====================================================================

    /// <summary>The external collaboration host with the principal stubbed, the REAL data service over <see cref="Data"/>, and a counting storage layer.</summary>
    public sealed class Fixture : ExternalCollaborationTestFixture
    {
        private readonly ExternalTodoScopeTestFixture.StubCallerPrincipalResolver _principals = new();
        private int _pointerResolutions;
        private int _fileReads;

        public ExternalRootEventReadTests.FakeDataverse Data { get; } = new();

        public CallerPrincipal? Principal
        {
            get => _principals.Principal;
            set => _principals.Principal = value;
        }

        /// <summary>How many times the storage pointer resolver was asked.</summary>
        public int PointerResolutions => Volatile.Read(ref _pointerResolutions);

        /// <summary>How many times SPE content or versions were read.</summary>
        public int FileReads => Volatile.Read(ref _fileReads);

        public void Reset()
        {
            Principal = null;
            Data.Reset();
            ResetCalls();
        }

        public void ResetCalls()
        {
            Interlocked.Exchange(ref _pointerResolutions, 0);
            Interlocked.Exchange(ref _fileReads, 0);
        }

        /// <summary>A real data service over <see cref="Data"/>, for the read-level tests.</summary>
        public ExternalDataService NewService() => new(
            new HttpClient(Data),
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
                .Build(),
            new ExternalRecordDocumentRoutesTests.InMemoryExternalDataService.TestCredential(),
            NullLogger<ExternalDataService>.Instance);

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICallerPrincipalResolver>(_ => _principals);
                services.AddScoped(_ => NewService());

                var pointers = new Mock<IDocumentStorageResolver>();
                pointers.Setup(p => p.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Interlocked.Increment(ref _pointerResolutions);
                        return Task.FromResult(("b!drive-0000000000000000", "item-0000000000000000"));
                    });
                services.RemoveAll<IDocumentStorageResolver>();
                services.AddSingleton(pointers.Object);

                var files = new Mock<ISpeFileOperations>();
                files.Setup(f => f.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Interlocked.Increment(ref _fileReads);
                        return Task.FromResult<Stream?>(new MemoryStream("bytes"u8.ToArray()));
                    });
                files.Setup(f => f.ListFileVersionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(() =>
                    {
                        Interlocked.Increment(ref _fileReads);
                        return Task.FromResult<IReadOnlyList<VersionInfoDto>?>(new List<VersionInfoDto>());
                    });
                services.RemoveAll<ISpeFileOperations>();
                services.AddSingleton(files.Object);
            });
        }
    }
}
