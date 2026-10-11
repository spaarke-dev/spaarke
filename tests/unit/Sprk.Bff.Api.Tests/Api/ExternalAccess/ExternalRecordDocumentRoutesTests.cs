using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Azure.Core;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.ExternalAccess;

/// <summary>
/// spaarke-SPA-external-access-platform-r3 task 031 (FR-24): document list, download, versions and upload on every core
/// record an external caller can reach (project, matter, work assignment, service request), through the REAL
/// <c>/api/v1/external</c> routes and handlers.
/// </summary>
/// <remarks>
/// <para>Doubles sit at module boundaries only: the caller principal (<see cref="ICallerPrincipalResolver"/>), the
/// Dataverse data seams (<see cref="ExternalDataService"/>'s virtual members, kept in memory), the owner resolver, the
/// SPE facade (<see cref="ISpeFileOperations"/>) and the storage pointer resolver. The container resolver is the REAL one
/// over a mocked record read. The in-memory document store is keyed by the parent bind the REAL
/// <see cref="ExternalDataService.BuildDocumentCreatePayload"/> wrote, so a wrong navigation property or entity set
/// breaks the round trip instead of passing it.</para>
/// <para>Invoice is not covered: its routes are not mapped (task 031 escalated it; no Tier-2 answer for an invoice id).</para>
/// </remarks>
[Trait("category", "external-access")]
public sealed class ExternalRecordDocumentRoutesTests : IClassFixture<ExternalRecordDocumentRoutesTests.Fixture>
{
    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999031");
    private static readonly Guid OtherContact = Guid.Parse("88888888-8888-8888-8888-888888888031");

    private static readonly Guid RecordA = Guid.Parse("a0310000-0000-4000-8000-00000000000a");
    private static readonly Guid RecordB = Guid.Parse("a0310000-0000-4000-8000-00000000000b");
    private static readonly Guid OutsideRecord = Guid.Parse("a0310000-0000-4000-8000-0000000000ff");

    private readonly Fixture _fixture;

    public ExternalRecordDocumentRoutesTests(Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
    }

    public static TheoryData<string> AllParents => new() { "projects", "matters", "workassignments", "servicerequests" };

    /// <summary>The three accessible-root types, where an access LEVEL (ViewOnly / Collaborate) exists.</summary>
    public static TheoryData<string> RootParents => new() { "projects", "matters", "workassignments" };

    // =====================================================================
    // AC 1: a Collaborate caller uploads and downloads the same bytes, on every type
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllParents))]
    public async Task Upload_ThenDownload_ReturnsTheSameBytes_AndListsTheDocument(string segment)
    {
        _fixture.Principal = CollaborateOn(segment, RecordA);
        var bytes = "privileged contents"u8.ToArray();

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm(bytes));

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var created = await upload.Content.ReadFromJsonAsync<ExternalDocumentDto>();
        upload.Headers.Location!.ToString().Should().Be($"/api/v1/external/{segment}/{RecordA}/documents/{created!.SprkDocumentid}");

        // The row is bound to the ROUTE record through that type's own lookup, and owned/stored from that record.
        var row = _fixture.Data.Documents.Should().ContainSingle().Subject;
        row.Payload.Should().ContainKey(ExternalDataService.GetDocumentParentBinding(KindOf(segment)).BindKey)
            .WhoseValue.Should().Be($"/{EntitySetOf(segment)}({RecordA})");
        _fixture.Ownership.Requests.Should().ContainSingle()
            .Which.TargetEntityLogicalName.Should().Be(EntityOf(segment), "the owner is resolved from the route record");

        // The REAL container resolver: a secure root's bytes go to its own container; a service request is not
        // securable and has no ancestor-link row, so its bytes go to the owning business unit's shared container.
        _fixture.Uploads.Should().ContainSingle().Which.Container.Should().Be(
            segment == "servicerequests" ? Fixture.BusinessUnitContainer : Fixture.RecordContainer);

        using var download = await client.GetAsync(
            $"/api/v1/external/{segment}/{RecordA}/documents/{created.SprkDocumentid}/content");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        var listed = await client.GetFromJsonAsync<ExternalCollectionResponse<ExternalDocumentDto>>(
            $"/api/v1/external/{segment}/{RecordA}/documents");
        listed!.Value.Select(d => d.SprkDocumentid).Should().Equal(created.SprkDocumentid);
    }

    // =====================================================================
    // AC 7: uploaded rows carry the task-030 creator stamp
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllParents))]
    public async Task Upload_StampsTheResolvedCallerAsCreator(string segment)
    {
        _fixture.Principal = CollaborateOn(segment, RecordA);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        var row = _fixture.Data.Documents.Should().ContainSingle().Subject;
        row.Creator.Should().BeSameAs(_fixture.Principal, "the stamp comes from the principal the route resolved");
        row.Payload.Should().ContainKey(ExternalDataService.CreatedByContactBindKey)
            .WhoseValue.Should().Be($"/contacts({CallerContact:D})");
    }

    // =====================================================================
    // AC 2: a ViewOnly caller cannot upload
    // =====================================================================

    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Upload_ByAViewOnlyCaller_Is403_AndWritesNothing(string segment)
    {
        _fixture.Principal = Principal(segment, RecordA, ExternalAccessLevel.ViewOnly);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Uploads.Should().BeEmpty("a ViewOnly caller never reaches SPE");
        _fixture.Data.Documents.Should().BeEmpty();

        // ViewOnly still reads: the denial is the Create gate, not a broken route.
        using var list = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/documents");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // =====================================================================
    // AC 3: a record outside the caller's set is 403 on list, download, versions and upload
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllParents))]
    public async Task RecordOutsideTheCallersSet_Is403_OnEveryDocumentRoute_AndTouchesNoDocumentOrSpe(string segment)
    {
        _fixture.Principal = CollaborateOn(segment, RecordA);
        var documentOnOutside = _fixture.Data.Seed(KindOf(segment), OutsideRecord, "outside.pdf", "b!c", "item-outside");
        _fixture.Data.DocumentReads.Clear();

        using var client = _fixture.CreateAuthenticatedClient();
        var basePath = $"/api/v1/external/{segment}/{OutsideRecord}/documents";

        (await client.GetAsync(basePath)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{basePath}/{documentOnOutside}/content")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"{basePath}/{documentOnOutside}/versions")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync(basePath, FileForm())).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _fixture.Data.DocumentReads.Should().BeEmpty("the parent gate runs before any document read");
        _fixture.Downloads.Should().BeEmpty();
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.Documents.Should().ContainSingle("nothing was created");
    }

    // =====================================================================
    // AC 4: a document of another record is not downloadable through this route
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllParents))]
    public async Task DocumentOfAnotherRecord_IsNotDownloadable_ThroughThisRecordsRoute(string segment)
    {
        // The caller can read BOTH records; the document belongs to B and is asked for through A.
        _fixture.Principal = CollaborateOn(segment, RecordA, RecordB);
        var documentOnB = _fixture.Data.Seed(KindOf(segment), RecordB, "b.pdf", "b!c", "item-b");

        using var client = _fixture.CreateAuthenticatedClient();
        var content = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/documents/{documentOnB}/content");
        var versions = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/documents/{documentOnB}/versions");

        content.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        versions.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Downloads.Should().BeEmpty("scoping is decided before any SPE read");

        // Positive control: through its own record it streams.
        (await client.GetAsync($"/api/v1/external/{segment}/{RecordB}/documents/{documentOnB}/content"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DocumentLinkedToAMatter_IsNotDownloadable_ThroughAProjectRouteWithAnotherLookup()
    {
        // The caller holds Read on project A; the document is linked to MATTER A (same id), not to project A. The
        // scoping read asks the PROJECT lookup, which is empty, so the project route refuses it.
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.Collaborate) },
            MatterAccess = new Dictionary<Guid, AccessRights> { [RecordA] = Collaborate },
        };
        var matterDocument = _fixture.Data.Seed(ExternalDataService.DocumentParentKind.Matter, RecordA, "m.pdf", "b!c", "item-m");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/v1/external/projects/{RecordA}/documents/{matterDocument}/content");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Downloads.Should().BeEmpty();
    }

    // =====================================================================
    // AC 5: a service request — the requester only
    // =====================================================================

    [Fact]
    public async Task ServiceRequest_OfAnotherRequester_Is403_OnListAndUpload()
    {
        _fixture.Principal = WorkforceContact();
        _fixture.Data.Requesters[RecordA] = OtherContact;

        using var client = _fixture.CreateAuthenticatedClient();
        var list = await client.GetAsync($"/api/v1/external/servicerequests/{RecordA}/documents");
        var upload = await client.PostAsync($"/api/v1/external/servicerequests/{RecordA}/documents", FileForm());

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Data.DocumentReads.Should().BeEmpty();
        _fixture.Uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task ServiceRequest_ACiamPartnerRequester_WithThePartnerSwitchOff_Is403()
    {
        // FR-06: partners use service requests only when ExternalAccess:PartnerServiceRequestsEnabled is on (default
        // off). The rights come from the Service Requests module's own accessible set, so its switch applies here too.
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "partner@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
        };
        _fixture.Data.Requesters[RecordA] = CallerContact;

        using var client = _fixture.CreateAuthenticatedClient();
        var list = await client.GetAsync($"/api/v1/external/servicerequests/{RecordA}/documents");
        var upload = await client.PostAsync($"/api/v1/external/servicerequests/{RecordA}/documents", FileForm());

        list.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.RequesterReads.Should().Be(0,
            "the module grants this caller nothing, so the denial is decided before any Dataverse read");
    }

    [Fact]
    public async Task ServiceRequest_ARequesterRead_ThatFails_Is503_NotA403_AndTouchesNothing()
    {
        // A throttle or outage on the requester read is "could not tell", not "no access": 503 on every route, with
        // no SPE call, no document read and no write.
        _fixture.Principal = WorkforceContact();
        _fixture.Data.Requesters[RecordA] = CallerContact;
        _fixture.Data.RequesterFault = new ExternalDataService.DataverseUnavailableException("Dataverse answered 429.");
        var document = _fixture.Data.Seed(ExternalDataService.DocumentParentKind.ServiceRequest, RecordA, "sr.pdf", "b!c", "item-sr");
        _fixture.Data.DocumentReads.Clear();

        using var client = _fixture.CreateAuthenticatedClient();
        var basePath = $"/api/v1/external/servicerequests/{RecordA}/documents";
        var responses = new[]
        {
            await client.GetAsync(basePath),
            await client.GetAsync($"{basePath}/{document}/content"),
            await client.GetAsync($"{basePath}/{document}/versions"),
            await client.PostAsync(basePath, FileForm()),
        };

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.ServiceUnavailable);
        (await responses[0].Content.ReadAsStringAsync()).Should().Contain("sdap.access.service_request_unreadable");
        _fixture.Data.DocumentReads.Should().BeEmpty();
        _fixture.Downloads.Should().BeEmpty();
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.Documents.Should().ContainSingle("nothing was written");

        // The same request reads normally once Dataverse answers again.
        _fixture.Data.RequesterFault = null;
        (await client.GetAsync(basePath)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ServiceRequest_ThatDoesNotExist_DeniesExactlyLikeAnotherRequestersOne()
    {
        // No existence oracle: a missing request and someone else's request give the same status and body.
        _fixture.Principal = WorkforceContact();
        _fixture.Data.Requesters[RecordA] = OtherContact;

        using var client = _fixture.CreateAuthenticatedClient();
        var missing = await client.GetAsync($"/api/v1/external/servicerequests/{OutsideRecord}/documents");
        var others = await client.GetAsync($"/api/v1/external/servicerequests/{RecordA}/documents");

        missing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        others.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await missing.Content.ReadAsStringAsync()).Should().Be(await others.Content.ReadAsStringAsync());
    }

    // =====================================================================
    // The list query and the create bind per parent (pure)
    // =====================================================================

    [Theory]
    [InlineData(ExternalDataService.DocumentParentKind.Project, "_sprk_project_value", "sprk_Project@odata.bind", "sprk_projects")]
    [InlineData(ExternalDataService.DocumentParentKind.Matter, "_sprk_matter_value", "sprk_Matter@odata.bind", "sprk_matters")]
    [InlineData(ExternalDataService.DocumentParentKind.WorkAssignment, "_sprk_workassignment_value", "sprk_WorkAssignment@odata.bind", "sprk_workassignments")]
    [InlineData(ExternalDataService.DocumentParentKind.ServiceRequest, "_sprk_relatedservicerequest_value", "sprk_RelatedServiceRequest@odata.bind", "sprk_servicerequests")]
    public void ParentBinding_FiltersAndBindsThroughThatTypesOwnLookup(
        ExternalDataService.DocumentParentKind kind, string valueAttribute, string bindKey, string entitySet)
    {
        var url = ExternalDataService.BuildDocumentListUrl("https://org.test/api/data/v9.2", kind, RecordA);
        Uri.UnescapeDataString(url).Should().Contain($"$filter={valueAttribute} eq {RecordA}");

        var payload = ExternalDataService.BuildDocumentCreatePayload(
            kind, RecordA, new ExternalUploadedFilePointers("b!drive", "item-1", "a.pdf", 3, null), RecordOwnershipResolverDouble.DefaultTeamId);
        payload.Should().ContainKey(bindKey).WhoseValue.Should().Be($"/{entitySet}({RecordA})");
        payload.Keys.Count(k => k.EndsWith("@odata.bind", StringComparison.Ordinal) && k != ExternalDataService.OwnerBindKey)
            .Should().Be(1, "a document is filed to exactly one parent");
    }

    // The REAL scoping and requester reads, over a transport that answers like Dataverse: only the $select columns of
    // one row. A document linked to all four parents at once tells which lookup the read asked for.

    private static readonly Guid DocProject = Guid.Parse("d0310000-0000-4000-8000-000000000001");
    private static readonly Guid DocMatter = Guid.Parse("d0310000-0000-4000-8000-000000000002");
    private static readonly Guid DocWorkAssignment = Guid.Parse("d0310000-0000-4000-8000-000000000003");
    private static readonly Guid DocServiceRequest = Guid.Parse("d0310000-0000-4000-8000-000000000004");

    [Theory]
    [InlineData(ExternalDataService.DocumentParentKind.Project, "d0310000-0000-4000-8000-000000000001")]
    [InlineData(ExternalDataService.DocumentParentKind.Matter, "d0310000-0000-4000-8000-000000000002")]
    [InlineData(ExternalDataService.DocumentParentKind.WorkAssignment, "d0310000-0000-4000-8000-000000000003")]
    [InlineData(ExternalDataService.DocumentParentKind.ServiceRequest, "d0310000-0000-4000-8000-000000000004")]
    public async Task DocumentParentRead_ReturnsTheParentThroughThatTypesOwnLookup(
        ExternalDataService.DocumentParentKind kind, string expectedParent)
    {
        var transport = new DataverseRowHandler(new Dictionary<string, object?>
        {
            ["sprk_documentname"] = "brief.pdf",
            ["_sprk_project_value"] = DocProject.ToString(),
            ["_sprk_matter_value"] = DocMatter.ToString(),
            ["_sprk_workassignment_value"] = DocWorkAssignment.ToString(),
            ["_sprk_relatedservicerequest_value"] = DocServiceRequest.ToString(),
        });

        var (parent, name) = await NewService(transport).GetDocumentParentAndNameAsync(kind, Guid.NewGuid());

        parent.Should().Be(Guid.Parse(expectedParent));
        name.Should().Be("brief.pdf");
    }

    [Fact]
    public async Task DocumentParentRead_OfADocumentNotLinkedThroughThatLookup_HasNoParent()
    {
        var transport = new DataverseRowHandler(new Dictionary<string, object?>
        {
            ["sprk_documentname"] = "brief.pdf",
            ["_sprk_project_value"] = DocProject.ToString(),
            ["_sprk_matter_value"] = null,
        });

        var (parent, _) = await NewService(transport)
            .GetDocumentParentAndNameAsync(ExternalDataService.DocumentParentKind.Matter, Guid.NewGuid());

        parent.Should().BeNull("a project document is not a matter document");
    }

    [Fact]
    public async Task ServiceRequestRequesterRead_ReturnsTheRequester_AndNullForAMissingRequest()
    {
        var found = new DataverseRowHandler(new Dictionary<string, object?> { ["_sprk_requestedby_value"] = CallerContact.ToString() });
        var missing = new DataverseRowHandler(row: null);

        (await NewService(found).GetServiceRequestRequesterAsync(RecordA)).Should().Be(CallerContact);
        found.Urls.Should().ContainSingle().Which.Should().Contain($"/sprk_servicerequests({RecordA})?$select=_sprk_requestedby_value");
        (await NewService(missing).GetServiceRequestRequesterAsync(RecordA)).Should().BeNull();
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task ServiceRequestRequesterRead_OnANon404Failure_ThrowsRatherThanReportingNoRequest(HttpStatusCode status)
    {
        var service = NewService(new StatusHandler(status));

        var act = () => service.GetServiceRequestRequesterAsync(RecordA);

        await act.Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();
    }

    [Fact]
    public async Task ServiceRequestRequesterRead_OnATransportFault_Throws_AndOnA404ReturnsNull()
    {
        var timeout = NewService(new ThrowingHandler(new TaskCanceledException("timed out")));
        var unreachable = NewService(new ThrowingHandler(new HttpRequestException("connection reset")));

        await ((Func<Task>)(() => timeout.GetServiceRequestRequesterAsync(RecordA)))
            .Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();
        await ((Func<Task>)(() => unreachable.GetServiceRequestRequesterAsync(RecordA)))
            .Should().ThrowAsync<ExternalDataService.DataverseUnavailableException>();

        // A genuine 404 is still "no such request".
        (await NewService(new StatusHandler(HttpStatusCode.NotFound)).GetServiceRequestRequesterAsync(RecordA)).Should().BeNull();
    }

    private sealed class StatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class ThrowingHandler(Exception fault) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromException<HttpResponseMessage>(fault);
    }

    private static ExternalDataService NewService(HttpMessageHandler transport) => new(
        new HttpClient(transport),
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataverse:ServiceUrl"] = "https://org.test" })
            .Build(),
        new InMemoryExternalDataService.TestCredential(),
        NullLogger<ExternalDataService>.Instance);

    /// <summary>Answers a single-row GET with only the <c>$select</c> columns of <paramref name="row"/>, or 404 when null.</summary>
    private sealed class DataverseRowHandler(IReadOnlyDictionary<string, object?>? row) : HttpMessageHandler
    {
        public List<string> Urls { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = Uri.UnescapeDataString(request.RequestUri!.ToString());
            Urls.Add(url);
            if (row is null)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var selected = url[(url.IndexOf("$select=", StringComparison.Ordinal) + "$select=".Length)..].Split(',');
            var body = selected.Where(row.ContainsKey).ToDictionary(c => c, c => row[c]);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    [Fact]
    public void ProjectListUrl_IsUnchanged()
    {
        ExternalDataService.BuildDocumentListUrl("https://org.test/api/data/v9.2", ExternalDataService.DocumentParentKind.Project, RecordA)
            .Should().Be("https://org.test/api/data/v9.2/sprk_documents?$filter="
                         + Uri.EscapeDataString($"_sprk_project_value eq {RecordA}")
                         + "&$select=sprk_documentid,sprk_documentname,sprk_documenttype,sprk_filesummary,_sprk_project_value,createdon"
                         + "&$orderby=createdon desc,sprk_documentid asc");
    }

    // =====================================================================
    // Helpers
    // =====================================================================

    private static readonly AccessRights Collaborate = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate);

    private static ExternalDataService.DocumentParentKind KindOf(string segment) => segment switch
    {
        "projects" => ExternalDataService.DocumentParentKind.Project,
        "matters" => ExternalDataService.DocumentParentKind.Matter,
        "workassignments" => ExternalDataService.DocumentParentKind.WorkAssignment,
        _ => ExternalDataService.DocumentParentKind.ServiceRequest,
    };

    private static string EntityOf(string segment) => ExternalDataService.GetDocumentParentBinding(KindOf(segment)).EntityLogicalName;

    private static string EntitySetOf(string segment) => ExternalDataService.GetDocumentParentBinding(KindOf(segment)).EntitySet;

    /// <summary>A caller holding Collaborate on <paramref name="records"/> of the route's type (for a service request: the
    /// workforce contact who requested them).</summary>
    private CallerPrincipal CollaborateOn(string segment, params Guid[] records)
    {
        if (segment == "servicerequests")
        {
            foreach (var record in records)
                _fixture.Data.Requesters[record] = CallerContact;
            return WorkforceContact();
        }

        return Principal(segment, records, ExternalAccessLevel.Collaborate);
    }

    private static CallerPrincipal Principal(string segment, Guid record, ExternalAccessLevel level) =>
        Principal(segment, new[] { record }, level);

    private static CallerPrincipal Principal(string segment, Guid[] records, ExternalAccessLevel level)
    {
        var rights = records.ToDictionary(r => r, _ => ExternalAccessLevels.ToAccessRights(level));
        return new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = segment == "projects"
                ? records.Select(r => CallerProjectAccess.FromLevel(r, level)).ToList()
                : Array.Empty<CallerProjectAccess>(),
            MatterAccess = segment == "matters" ? rights : new Dictionary<Guid, AccessRights>(),
            WorkAssignmentAccess = segment == "workassignments" ? rights : new Dictionary<Guid, AccessRights>(),
        };
    }

    /// <summary>A workforce contact principal (a customer staff member with no Dataverse licence): a requester.</summary>
    private static CallerPrincipal WorkforceContact() => new()
    {
        Plane = CallerPrincipalPlane.Workforce,
        ContactId = CallerContact,
        Email = "staff@customer.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
    };

    private static MultipartFormDataContent FileForm(byte[]? bytes = null) => new()
    {
        { new ByteArrayContent(bytes ?? new byte[] { 1, 2, 3 }), "file", "brief.pdf" },
    };

    // =====================================================================
    // Fixture
    // =====================================================================

    /// <summary>
    /// The external collaboration host with the routes' module-boundary doubles: the stub principal, an in-memory
    /// <see cref="ExternalDataService"/>, the owner-resolver double, an in-memory SPE facade and pointer resolver, and the
    /// REAL <see cref="RecordContainerResolver"/> over a record read that names one container for every record.
    /// </summary>
    public sealed class Fixture : ExternalCollaborationTestFixture
    {
        /// <summary>The container a project, matter or work assignment (secure) names on its own row.</summary>
        public const string RecordContainer = "b!record-container";

        /// <summary>The shared container of the business unit that owns every service request.</summary>
        public const string BusinessUnitContainer = "b!business-unit-shared-container";

        private static readonly Guid OwningBusinessUnitId = Guid.Parse("b0310000-0000-4000-8000-0000000000b1");

        private readonly ExternalTodoScopeTestFixture.StubCallerPrincipalResolver _principals = new();
        private readonly ConcurrentDictionary<string, byte[]> _spe = new();

        public InMemoryExternalDataService Data { get; } = new();

        public RecordOwnershipResolverDouble Ownership { get; } = new();

        /// <summary>Every SPE upload, as (container, path).</summary>
        public ConcurrentQueue<(string Container, string Path)> Uploads { get; } = new();

        /// <summary>Every SPE download, as (drive, item).</summary>
        public ConcurrentQueue<(string Drive, string Item)> Downloads { get; } = new();

        public CallerPrincipal? Principal
        {
            get => _principals.Principal;
            set => _principals.Principal = value;
        }

        public void Reset()
        {
            Data.Reset();
            Principal = null;
            Ownership.TeamId = RecordOwnershipResolverDouble.DefaultTeamId;
            Ownership.Requests.Clear();
            Uploads.Clear();
            Downloads.Clear();
            _spe.Clear();
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICallerPrincipalResolver>(_ => _principals);
                services.AddScoped(_ => (ExternalDataService)Data);
                services.RemoveAll<IRecordOwnershipResolver>();
                services.AddSingleton<IRecordOwnershipResolver>(Ownership);

                // The REAL container resolver, answering as production does: a project, matter and work assignment are
                // securable, secure, and carry their own container; a service request is NOT securable and has no
                // ChildAncestorLinks row, so its container is its owning business unit's (the fallback branch).
                var registry = new Mock<ISecurableEntityRegistry>();
                registry.Setup(r => r.ClassifyEntityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string entity, CancellationToken _) =>
                        entity == "sprk_servicerequest" ? EntitySecurability.NotSecurable : EntitySecurability.Securable);
                var records = new Mock<IGenericEntityService>();
                records.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => entity switch
                    {
                        "businessunit" => new Entity(entity, id) { ["sprk_containerid"] = BusinessUnitContainer },
                        "sprk_servicerequest" => new Entity(entity, id)
                        {
                            ["owningbusinessunit"] = new EntityReference("businessunit", OwningBusinessUnitId),
                        },
                        _ => new Entity(entity, id)
                        {
                            ["sprk_issecure"] = true,
                            ["sprk_containerid"] = RecordContainer,
                        },
                    });
                services.RemoveAll<RecordContainerResolver>();
                services.AddSingleton(new RecordContainerResolver(
                    registry.Object, records.Object, NullLogger<RecordContainerResolver>.Instance));

                // SPE facade, in memory: an upload stores the bytes under a new item id; a download returns them.
                var files = new Mock<ISpeFileOperations>();
                files.Setup(f => f.UploadSmallAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                    .Returns((string container, string path, Stream content, ConflictBehavior _, CancellationToken _) =>
                    {
                        Uploads.Enqueue((container, path));
                        using var buffer = new MemoryStream();
                        content.CopyTo(buffer);
                        var itemId = $"item-{Guid.NewGuid():N}";
                        _spe[itemId] = buffer.ToArray();
                        return Task.FromResult<FileHandleDto?>(new FileHandleDto(itemId, path, null, buffer.Length,
                            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, false, "https://contoso.example/" + path, container));
                    });
                files.Setup(f => f.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns((string drive, string item, CancellationToken _) =>
                    {
                        Downloads.Enqueue((drive, item));
                        return Task.FromResult<Stream?>(_spe.TryGetValue(item, out var stored) ? new MemoryStream(stored) : new MemoryStream());
                    });
                services.RemoveAll<ISpeFileOperations>();
                services.AddSingleton(files.Object);

                // Pointer resolver: the document row's own pointers, as the real resolver reads them.
                var pointers = new Mock<IDocumentStorageResolver>();
                pointers.Setup(p => p.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((Guid documentId, CancellationToken _) => Data.PointersOf(documentId));
                services.RemoveAll<IDocumentStorageResolver>();
                services.AddSingleton(pointers.Object);
            });
        }
    }

    /// <summary>One stored document row: the payload the real builder produced and the creator it was stamped from.</summary>
    public sealed record StoredDocument(Guid Id, string Name, string DriveId, string ItemId,
        IReadOnlyDictionary<string, object?> Payload, CallerPrincipal? Creator);

    /// <summary>
    /// The document seams in memory. A row's parent is whatever <c>{nav}@odata.bind</c> the REAL
    /// <see cref="ExternalDataService.BuildDocumentCreatePayload"/> wrote, read back through the REAL
    /// <see cref="ExternalDataService.GetDocumentParentBinding"/> — the same lookup the list filter and the scoping read use.
    /// </summary>
    public sealed class InMemoryExternalDataService : ExternalDataService
    {
        public InMemoryExternalDataService()
            : base(new HttpClient(), new ConfigurationBuilder().Build(), new TestCredential(),
                   NullLogger<ExternalDataService>.Instance)
        {
        }

        private readonly List<StoredDocument> _documents = new();

        public IReadOnlyList<StoredDocument> Documents
        {
            get { lock (_documents) return _documents.ToList(); }
        }

        /// <summary>Service request id → requester contact. Absent = the request does not exist.</summary>
        public ConcurrentDictionary<Guid, Guid> Requesters { get; } = new();

        /// <summary>Set to make the requester read fail the way a throttled or unreachable Dataverse does.</summary>
        public Exception? RequesterFault { get; set; }

        /// <summary>How many times a service request's requester was read.</summary>
        public int RequesterReads;

        /// <summary>Every document read (list or scoping), so deny tests can assert none happened.</summary>
        public ConcurrentQueue<string> DocumentReads { get; } = new();

        public void Reset()
        {
            lock (_documents) _documents.Clear();
            Requesters.Clear();
            DocumentReads.Clear();
            RequesterFault = null;
            Interlocked.Exchange(ref RequesterReads, 0);
        }

        /// <summary>Seeds a document filed to <paramref name="parentId"/> through <paramref name="kind"/>'s lookup.</summary>
        public Guid Seed(DocumentParentKind kind, Guid parentId, string name, string driveId, string itemId)
        {
            var payload = BuildDocumentCreatePayload(kind, parentId,
                new ExternalUploadedFilePointers(driveId, itemId, name, 3, null), RecordOwnershipResolverDouble.DefaultTeamId);
            var id = Guid.NewGuid();
            lock (_documents) _documents.Add(new StoredDocument(id, name, driveId, itemId, payload, null));
            return id;
        }

        public (string DriveId, string ItemId) PointersOf(Guid documentId)
        {
            var row = Documents.Single(d => d.Id == documentId);
            return (row.DriveId, row.ItemId);
        }

        private static Guid? ParentOf(StoredDocument row, DocumentParentKind kind)
        {
            var binding = GetDocumentParentBinding(kind);
            return row.Payload.TryGetValue(binding.BindKey, out var bind)
                   && bind is string path
                   && path.StartsWith($"/{binding.EntitySet}(", StringComparison.Ordinal)
                   && Guid.TryParse(path[(binding.EntitySet.Length + 2)..^1], out var id)
                ? id
                : null;
        }

        public override Task<ExternalCollectionResponse<ExternalDocumentDto>> GetDocumentsAsync(
            DocumentParentKind parentKind, Guid parentId, CancellationToken ct = default)
        {
            DocumentReads.Enqueue($"list:{parentKind}:{parentId}");
            return Task.FromResult(new ExternalCollectionResponse<ExternalDocumentDto>
            {
                Value = Documents.Where(d => ParentOf(d, parentKind) == parentId)
                    .Select(d => new ExternalDocumentDto { SprkDocumentid = d.Id.ToString(), SprkName = d.Name })
                    .ToList(),
            });
        }

        public override Task<(Guid? ParentId, string? DocumentName)> GetDocumentParentAndNameAsync(
            DocumentParentKind parentKind, Guid documentId, CancellationToken ct = default)
        {
            DocumentReads.Enqueue($"scope:{parentKind}:{documentId}");
            var row = Documents.SingleOrDefault(d => d.Id == documentId);
            return Task.FromResult<(Guid?, string?)>(row is null ? (null, null) : (ParentOf(row, parentKind), row.Name));
        }

        // The real seam's body without the transport: build the payload, stamp it (task 030), store it.
        public override Task<ExternalDocumentDto> CreateDocumentAsync(
            DocumentParentKind parentKind, Guid parentId, ExternalUploadedFilePointers pointers, Guid owningTeamId,
            CallerPrincipal creator, CancellationToken ct = default)
        {
            var payload = BuildDocumentCreatePayload(parentKind, parentId, pointers, owningTeamId);
            StampCreator(payload, creator);
            var id = Guid.NewGuid();
            lock (_documents) _documents.Add(new StoredDocument(id, pointers.FileName, pointers.DriveId, pointers.ItemId, payload, creator));
            return Task.FromResult(new ExternalDocumentDto { SprkDocumentid = id.ToString(), SprkName = pointers.FileName });
        }

        public override Task<Guid?> GetServiceRequestRequesterAsync(Guid serviceRequestId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref RequesterReads);
            if (RequesterFault is { } fault)
                throw fault;
            return Task.FromResult(Requesters.TryGetValue(serviceRequestId, out var requester) ? requester : (Guid?)null);
        }

        internal sealed class TestCredential : TokenCredential
        {
            public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken ct) =>
                new("stub-token", DateTimeOffset.UtcNow.AddHours(1));

            public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken ct) =>
                new(new AccessToken("stub-token", DateTimeOffset.UtcNow.AddHours(1)));
        }
    }
}
