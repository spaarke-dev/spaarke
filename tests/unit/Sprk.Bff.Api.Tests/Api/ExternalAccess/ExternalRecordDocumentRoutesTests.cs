using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.ExternalAccess.Dtos;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Jobs;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
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
/// <para>Invoice (owner decision 2026-10-11, option A): its rights are INHERITED from its parent records — the union
/// over its matter and project. In this fixture each invoice's parent is a matter (<see cref="ParentMatterOf"/>) on
/// which the caller holds the stated level, unless a test sets the parents itself.</para>
/// <para>Secure records (owner decision 2026-10-11): the container resolver's record reads are configurable per record
/// (<see cref="Fixture.Rows"/>), so a service request regarding a secure matter, an invoice under one, and non-secure
/// parents of every type are resolved by the REAL resolver.</para>
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

    public static TheoryData<string> AllParents => new() { "projects", "matters", "workassignments", "servicerequests", "invoices" };

    /// <summary>The types whose rights carry an access LEVEL (ViewOnly / Collaborate): the three accessible roots, and an
    /// invoice, which inherits its parent's level (owner option A).</summary>
    public static TheoryData<string> RootParents => new() { "projects", "matters", "workassignments", "invoices" };

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

        // The REAL container resolver: a secure root's bytes go to its own container, and so do an invoice's under its
        // (secure) parent matter; a service request that regards nothing goes to the owning business unit's container.
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
    // Task 036 (FR-20, owner round 59 item 2): a caller whose access Dataverse decided (Read only on its own term) uploads
    // only where Dataverse, asked as the caller, lets it attach — AppendTo on the record; for an invoice, on the invoice
    // itself, before the secure-roots gate.
    // =====================================================================

    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Fr20_Upload_ReadAndWriteButNoAppendToInDataverse_Is403_AndWritesNothing(string segment)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA, AccessRights.Read | AccessRights.Write);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Uploads.Should().BeEmpty("nothing reaches SPE unless Dataverse confirms the write");
        _fixture.Data.Documents.Should().BeEmpty();
        _fixture.Probe.AppOnlyCalls.Should().ContainSingle().Which.Should().Match<(Guid SystemUserId, string EntitySet, Guid RecordId)>(
            c => c.SystemUserId == CallerUser && c.EntitySet == EntitySetOf(segment) && c.RecordId == RecordA, "Dataverse is asked about the target record");
    }

    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Fr20_Upload_AppendToInDataverse_Is201(string segment)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(CallerUser, DocumentCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle();
        _fixture.Data.Documents.Should().ContainSingle();
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().Equal(new[] { (CallerUser, DocumentCreatePrivilege) });
    }

    // Task 036 fix round 1 (F1): the document is written app-only, so AppendTo on the parent is not enough — the caller
    // must also hold Create on sprk_document, as MDA would require of them.
    private const string DocumentCreatePrivilege = "prvCreatesprk_Document";

    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Fr20_Upload_AppendToButNoCreatePrivilegeOnSprkDocument_Is403_AndWritesNothing(string segment)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA,
            AccessRights.Read | AccessRights.Write | AccessRights.Append | AccessRights.AppendTo | AccessRights.Delete);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden, "MDA would refuse a user without prvCreatesprk_Document");
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.Documents.Should().BeEmpty();
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().Equal(new[] { (CallerUser, DocumentCreatePrivilege) });
    }

    // Task 036 fix round 3 (F3-2): Dataverse's CreateAccess on the parent is the table's create privilege, not leave to
    // attach to this record. With the document Create privilege held and no AppendTo, the upload is refused.
    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Fr20_Upload_DataverseCreateAccessWithoutAppendTo_Is403_AndWritesNothing(string segment)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA, AccessRights.Read | AccessRights.Create)
            .HoldsAppOnly(CallerUser, DocumentCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden, "CreateAccess is not AppendTo");
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.Documents.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Fr20_Upload_APrivilegeReadThatFaults_Is503_AndWritesNothing(string segment)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(CallerUser, DocumentCreatePrivilege);
        _fixture.Probe.AppOnlyPrivilegeFault = new HttpRequestException("RetrieveUserSetOfPrivilegesByNames failed");

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        // Fix round 4 (F1): a failed privilege read adds no Create (NFR-01), and is a retry-able 503, not a denial.
        await ShouldBeDataverseUnavailable(upload);
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.Documents.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Fr20_Upload_AProbeThatFaults_Is503_AndWritesNothing(string segment)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA, AccessRights.Read | AccessRights.AppendTo);
        _fixture.Probe.AppOnlyFault = new HttpRequestException("Dataverse unreachable");

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        // Fix round 4 (F1): a failed confirmation adds no rights (NFR-01), and is a retry-able 503, not a denial.
        await ShouldBeDataverseUnavailable(upload);
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.Documents.Should().BeEmpty();
    }

    /// <summary>Task 036 fix round 4 (F1): every confirmed parent, against each way Dataverse can fail to answer — the rights
    /// question, and the sprk_document Create privilege after Dataverse answered AppendTo.</summary>
    public static TheoryData<string, string, string> ParentsByUnansweredShape()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var segment in new[] { "projects", "matters", "workassignments", "invoices" })
        {
            foreach (var shape in new[]
                     {
                         CallerRecordAccessProbeDouble.WireShape.Timeout, CallerRecordAccessProbeDouble.WireShape.Unknown,
                         CallerRecordAccessProbeDouble.WireShape.ServerFault,
                     })
            {
                data.Add(segment, shape, CallerRecordAccessProbeDouble.WireShape.Held);
                data.Add(segment, CallerRecordAccessProbeDouble.WireShape.AppendTo, shape);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ParentsByUnansweredShape))]
    public async Task Fr20_Upload_DataverseGivesNoAnswer_OverTheWire_Is503_AndWritesNothing(
        string segment, string rights, string privilege)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.UseAppOnlyWire(rights, privilege);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        await ShouldBeDataverseUnavailable(upload);
        _fixture.Uploads.Should().BeEmpty("nothing reaches SPE on a question Dataverse did not answer");
        _fixture.Data.Documents.Should().BeEmpty();
    }

    [Theory]
    [InlineData("projects", CallerRecordAccessProbeDouble.WireShape.Denial, CallerRecordAccessProbeDouble.WireShape.Held)]
    [InlineData("invoices", CallerRecordAccessProbeDouble.WireShape.Denial, CallerRecordAccessProbeDouble.WireShape.Held)]
    [InlineData("matters", CallerRecordAccessProbeDouble.WireShape.AppendTo, CallerRecordAccessProbeDouble.WireShape.NotHeld)]
    public async Task Fr20_Upload_ARealDenial_OverTheWire_StaysA403_AndWritesNothing(string segment, string rights, string privilege)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.UseAppOnlyWire(rights, privilege);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden, await upload.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.Documents.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CallerRecordAccessProbeDouble.WireShape.Timeout)]
    [InlineData(CallerRecordAccessProbeDouble.WireShape.Unknown)]
    [InlineData(CallerRecordAccessProbeDouble.WireShape.ServerFault)]
    public async Task Fr20_List_DataverseGivesNoAnswer_OverTheWire_StillLists_WithCanCreateFalse(string shape)
    {
        // The list's canCreate is a UI affordance: an unanswered question offers no upload, and the list is still served.
        _fixture.Principal = SystemUserReading("projects", RecordA, dataverseDecides: true);
        _fixture.Probe.UseAppOnlyWire(shape);

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/projects/{RecordA}/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ExternalDocumentListResponse>())!.CanCreate.Should().BeFalse();
    }

    private static async Task ShouldBeDataverseUnavailable(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, body);
        System.Text.Json.JsonDocument.Parse(body).RootElement.GetProperty("reasonCode").GetString()
            .Should().Be("sdap.access.dataverse_unavailable");
        body.Should().Contain("nothing was written").And.Contain("Try again");
    }

    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Fr20_Upload_FlagOff_TheComposedRightsDecide_AndDataverseIsNeverAsked(string segment)
    {
        // The same Read-only caller with the flag off: Dataverse would say AppendTo, but nobody asks — still 403.
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: false);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA, AccessRights.Read | AccessRights.AppendTo);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("flag off is byte-equivalent: no Dataverse confirmation");
    }

    // Task 036 fix round 1 (F4, open question g settled): a Dataverse-decided caller's list reports the UPLOAD ROUTE's own
    // answer as canCreate — one probe for the record in hand (+ the sprk_document privilege when Dataverse says AppendTo).

    public static TheoryData<string, AccessRights, bool, bool> Fr20ListCanCreateCases()
    {
        var data = new TheoryData<string, AccessRights, bool, bool>();
        foreach (var segment in new[] { "projects", "matters", "workassignments", "invoices" })
        {
            data.Add(segment, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo, true, true);
            data.Add(segment, AccessRights.Read | AccessRights.Write | AccessRights.AppendTo, false, false);
            data.Add(segment, AccessRights.Read, true, false);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Fr20ListCanCreateCases))]
    public async Task Fr20_List_CanCreate_IsTheUploadRoutesDataverseAnswer(
        string segment, AccessRights dataverse, bool holdsCreate, bool expected)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA, dataverse);
        if (holdsCreate)
        {
            _fixture.Probe.HoldsAppOnly(CallerUser, DocumentCreatePrivilege);
        }

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK, "Read is unchanged: Dataverse only widens a parent already held");
        var list = await response.Content.ReadFromJsonAsync<ExternalDocumentListResponse>();
        list!.CanCreate.Should().Be(expected);
        _fixture.Probe.AppOnlyCalls.Should().ContainSingle().Which.Should().Match<(Guid SystemUserId, string EntitySet, Guid RecordId)>(
            c => c.SystemUserId == CallerUser && c.EntitySet == EntitySetOf(segment) && c.RecordId == RecordA, "one probe, for the record in hand");
    }

    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Fr20_List_AProbeThatFaults_ReportsCanCreateFalse_AndStillLists(string segment)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: true);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(CallerUser, DocumentCreatePrivilege);
        _fixture.Probe.AppOnlyFault = new HttpRequestException("Dataverse unreachable");

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/documents");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<ExternalDocumentListResponse>())!.CanCreate.Should().BeFalse(
            "a failed confirmation adds no rights (NFR-01)");
    }

    [Theory]
    [MemberData(nameof(RootParents))]
    public async Task Fr20_List_FlagOff_ReportsCanCreateFromTheComposedRights_AndAsksDataverseNothing(string segment)
    {
        _fixture.Principal = SystemUserReading(segment, RecordA, dataverseDecides: false);
        _fixture.Probe.AnswerAppOnly(CallerUser, EntitySetOf(segment), RecordA, AccessRights.Read | AccessRights.AppendTo)
            .HoldsAppOnly(CallerUser, DocumentCreatePrivilege);

        using var client = _fixture.CreateAuthenticatedClient();
        var list = await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/{segment}/{RecordA}/documents");

        list!.CanCreate.Should().BeFalse("flag off, the composed Read decides, exactly as before");
        _fixture.Probe.AppOnlyCalls.Should().BeEmpty("flag off is byte-equivalent: no Dataverse confirmation");
        _fixture.Probe.AppOnlyPrivilegeCalls.Should().BeEmpty();
    }

    // =====================================================================
    // Task 054 review: the list says whether the caller can upload (so the SPA offers Upload only to one who can)
    // =====================================================================

    [Theory]
    [InlineData("projects", ExternalAccessLevel.ViewOnly, false)]
    [InlineData("projects", ExternalAccessLevel.Collaborate, true)]
    [InlineData("projects", ExternalAccessLevel.FullAccess, true)]
    [InlineData("matters", ExternalAccessLevel.ViewOnly, false)]
    [InlineData("matters", ExternalAccessLevel.Collaborate, true)]
    [InlineData("matters", ExternalAccessLevel.FullAccess, true)]
    [InlineData("workassignments", ExternalAccessLevel.ViewOnly, false)]
    [InlineData("workassignments", ExternalAccessLevel.Collaborate, true)]
    [InlineData("workassignments", ExternalAccessLevel.FullAccess, true)]
    [InlineData("invoices", ExternalAccessLevel.ViewOnly, false)]
    [InlineData("invoices", ExternalAccessLevel.Collaborate, true)]
    [InlineData("invoices", ExternalAccessLevel.FullAccess, true)]
    public async Task List_ReportsCanCreate_FromTheSameRightsTheUploadRouteChecks(
        string segment, ExternalAccessLevel level, bool expectedCanCreate)
    {
        _fixture.Principal = Principal(segment, RecordA, level);

        using var client = _fixture.CreateAuthenticatedClient();
        using var list = await client.GetAsync($"/api/v1/external/{segment}/{RecordA}/documents");

        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await list.Content.ReadFromJsonAsync<ExternalDocumentListResponse>();
        body!.CanCreate.Should().Be(expectedCanCreate);

        // The wire member is always written, camelCase, after the existing ones (a missing member would read as "cannot").
        var raw = await client.GetStringAsync($"/api/v1/external/{segment}/{RecordA}/documents");
        raw.Should().Contain($"\"canCreate\":{(expectedCanCreate ? "true" : "false")}");
        raw.IndexOf("\"value\"", StringComparison.Ordinal).Should().BeLessThan(raw.IndexOf("\"canCreate\"", StringComparison.Ordinal));

        // And the flag agrees with the upload route's own answer for the same caller.
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());
        upload.StatusCode.Should().Be(expectedCanCreate ? HttpStatusCode.Created : HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task List_OnAServiceRequest_ForItsRequester_ReportsCanCreate()
    {
        _fixture.Principal = CollaborateOn("servicerequests", RecordA);

        using var client = _fixture.CreateAuthenticatedClient();
        var body = await client.GetFromJsonAsync<ExternalDocumentListResponse>(
            $"/api/v1/external/servicerequests/{RecordA}/documents");

        body!.CanCreate.Should().BeTrue("the requester always holds Read and Create on their own request");
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
    // Owner decision 2026-10-11 (1): invoice documents INHERIT the access held on the invoice's parents (option A)
    // =====================================================================

    private static readonly Guid MatterM = Guid.Parse("c0310000-0000-4000-8000-00000000000d");
    private static readonly Guid ProjectP = Guid.Parse("c0310000-0000-4000-8000-00000000000e");

    [Fact]
    public async Task Invoice_UnderTwoParents_TakesTheUnionOfTheCallersRightsOnThem()
    {
        // ViewOnly on the matter, Collaborate on the project: Create on EITHER parent admits an upload, as Read on either
        // admits the list (the invoices module's and the message routes' "any accessible parent" rule). Both parents are
        // non-secure here: an invoice under TWO secure roots is refused by the container resolver (ambiguous, 409) —
        // that is the storage rule, not the rights rule this test is about.
        _fixture.Data.InvoiceParents[RecordA] = (MatterM, ProjectP);
        NonSecure("sprk_matter", MatterM);
        NonSecure("sprk_project", ProjectP);
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(ProjectP, ExternalAccessLevel.Collaborate) },
            MatterAccess = new Dictionary<Guid, AccessRights> { [MatterM] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.ViewOnly) },
        };

        using var client = _fixture.CreateAuthenticatedClient();
        var list = await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/invoices/{RecordA}/documents");
        using var upload = await client.PostAsync($"/api/v1/external/invoices/{RecordA}/documents", FileForm());

        list!.CanCreate.Should().BeTrue();
        upload.StatusCode.Should().Be(HttpStatusCode.Created);

        // Control: ViewOnly on both parents reads but cannot upload.
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(ProjectP, ExternalAccessLevel.ViewOnly) },
            MatterAccess = new Dictionary<Guid, AccessRights> { [MatterM] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.ViewOnly) },
        };
        (await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/invoices/{RecordA}/documents"))!
            .CanCreate.Should().BeFalse();
        (await client.PostAsync($"/api/v1/external/invoices/{RecordA}/documents", FileForm()))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.Uploads.Should().ContainSingle("only the Collaborate caller's upload reached SPE");
    }

    [Fact]
    public async Task Invoice_UnderRecordsTheCallerCannotRead_AndAMissingInvoice_DenyIdentically_AndTouchNothing()
    {
        // The caller reads matter M; invoice A is filed under ANOTHER matter, and OutsideRecord is no invoice at all.
        _fixture.Data.InvoiceParents[RecordA] = (Guid.NewGuid(), null);
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights> { [MatterM] = Collaborate },
        };
        var documentOnA = _fixture.Data.Seed(ExternalDataService.DocumentParentKind.Invoice, RecordA, "a.pdf", "b!c", "item-a");
        _fixture.Data.DocumentReads.Clear();

        using var client = _fixture.CreateAuthenticatedClient();
        var basePath = $"/api/v1/external/invoices/{RecordA}/documents";
        var foreign = new[]
        {
            await client.GetAsync(basePath),
            await client.GetAsync($"{basePath}/{documentOnA}/content"),
            await client.GetAsync($"{basePath}/{documentOnA}/versions"),
            await client.PostAsync(basePath, FileForm()),
        };
        var missing = await client.GetAsync($"/api/v1/external/invoices/{OutsideRecord}/documents");

        foreign.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Forbidden);
        missing.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await missing.Content.ReadAsStringAsync()).Should().Be(await foreign[0].Content.ReadAsStringAsync(),
            "a missing invoice and one under records the caller cannot read are indistinguishable");
        _fixture.Data.DocumentReads.Should().BeEmpty("the parent gate runs before any document read");
        _fixture.Downloads.Should().BeEmpty();
        _fixture.Uploads.Should().BeEmpty();
    }

    [Fact]
    public async Task Invoice_ForACallerWhoReadsNoMatterOrProject_IsDeniedBeforeAnyInvoiceRead()
    {
        _fixture.Data.InvoiceParents[RecordA] = (MatterM, ProjectP);
        _fixture.Principal = WorkforceContact();

        using var client = _fixture.CreateAuthenticatedClient();
        (await client.GetAsync($"/api/v1/external/invoices/{RecordA}/documents")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PostAsync($"/api/v1/external/invoices/{RecordA}/documents", FileForm())).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        _fixture.Data.InvoiceReads.Should().Be(0, "the read could not change the answer");
    }

    [Fact]
    public async Task Invoice_AParentReadThatFails_Is503_NotA403_AndTouchesNothing()
    {
        _fixture.Principal = Principal("invoices", RecordA, ExternalAccessLevel.Collaborate);
        _fixture.Data.InvoiceFault = new ExternalDataService.DataverseUnavailableException("Dataverse answered 429.");
        var document = _fixture.Data.Seed(ExternalDataService.DocumentParentKind.Invoice, RecordA, "i.pdf", "b!c", "item-i");
        _fixture.Data.DocumentReads.Clear();

        using var client = _fixture.CreateAuthenticatedClient();
        var basePath = $"/api/v1/external/invoices/{RecordA}/documents";
        var responses = new[]
        {
            await client.GetAsync(basePath),
            await client.GetAsync($"{basePath}/{document}/content"),
            await client.GetAsync($"{basePath}/{document}/versions"),
            await client.PostAsync(basePath, FileForm()),
        };

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.ServiceUnavailable);
        (await responses[3].Content.ReadAsStringAsync()).Should().Contain("sdap.access.invoice_unreadable");
        _fixture.Data.DocumentReads.Should().BeEmpty();
        _fixture.Downloads.Should().BeEmpty();
        _fixture.Uploads.Should().BeEmpty();

        _fixture.Data.InvoiceFault = null;
        (await client.GetAsync(basePath)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // =====================================================================
    // unified-access-control-r2 task 187, fix round 2 (F1): an upload to an invoice passes the invoice gate (D4)
    // =====================================================================
    //
    // The caller holds Collaborate on ordinary matter M. Invoice A is filed to M and to project X. Option A still lets the
    // caller reach A through M, but when X is SECURE and the caller cannot read it, A is behind X's wall (D4): its thread is
    // refused, its documents are withheld, and an upload is refused too, before SPE or Dataverse is touched. (Without the
    // gate the upload answered 201 and wrote the file into X's container.)

    private static readonly Guid ProjectX = Guid.Parse("c0310000-0000-4000-8000-0000000000a7");
    private static readonly Guid AgreementG = Guid.Parse("c0310000-0000-4000-8000-0000000000a8");

    /// <summary>Invoice <see cref="RecordA"/> under ordinary matter M and project X; a caller with Collaborate on M only.</summary>
    private void SeedInvoiceUnderMAndX(bool secureX)
    {
        _fixture.Data.InvoiceParents[RecordA] = (MatterM, ProjectX);
        NonSecure("sprk_matter", MatterM);
        if (secureX) SecureProject(ProjectX);
        else NonSecure("sprk_project", ProjectX);
        _fixture.Data.RootIsSecure[(ExternalGrantRootType.Project, ProjectX)] = secureX;
        _fixture.Principal = CollaboratorOnMatterM();
    }

    private static CallerPrincipal CollaboratorOnMatterM(params Guid[] alsoMatters) => new()
    {
        Plane = CallerPrincipalPlane.CiamContact,
        ContactId = CallerContact,
        Email = "external.user@example.test",
        ProjectAccess = Array.Empty<CallerProjectAccess>(),
        MatterAccess = new[] { MatterM }.Concat(alsoMatters).ToDictionary(m => m, _ => Collaborate),
    };

    private void SecureProject(Guid project) =>
        _fixture.Rows[("sprk_project", project)] = new Entity("sprk_project", project)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = SecureMatterContainer,
            ["owningbusinessunit"] = new EntityReference("businessunit", Fixture.OwningBusinessUnitId),
        };

    private async Task<HttpResponseMessage> UploadToInvoiceA()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        return await client.PostAsync($"/api/v1/external/invoices/{RecordA}/documents", FileForm());
    }

    private void NothingWasWritten()
    {
        _fixture.Uploads.Should().BeEmpty("nothing reaches SPE");
        _fixture.Data.Documents.Should().BeEmpty("no sprk_document row is created");
        _fixture.Ownership.Requests.Should().BeEmpty("the upload stops at the gate, before the owner is resolved");
    }

    [Fact]
    public async Task InvoiceUpload_AlsoUnderASecureProjectTheCallerCannotRead_Is403_AndWritesNothing_F1()
    {
        SeedInvoiceUnderMAndX(secureX: true);

        using var upload = await UploadToInvoiceA();

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden, await upload.Content.ReadAsStringAsync());
        NothingWasWritten();

        // The read routes keep D4's own shapes: the list answers (the document walk withholds its documents) and offers no
        // upload.
        using var client = _fixture.CreateAuthenticatedClient();
        var list = await client.GetFromJsonAsync<ExternalDocumentListResponse>($"/api/v1/external/invoices/{RecordA}/documents");
        list!.CanCreate.Should().BeFalse("an upload that can only fail is not offered");
    }

    [Fact]
    public async Task InvoiceUpload_AlsoUnderAnOrdinaryProject_Is201_OptionA_F1()
    {
        SeedInvoiceUnderMAndX(secureX: false);

        using var upload = await UploadToInvoiceA();

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle().Which.Container.Should().Be(Fixture.BusinessUnitContainer);
        _fixture.Data.Documents.Should().ContainSingle();
    }

    [Fact]
    public async Task InvoiceUpload_AlsoUnderASecureProjectTheCallerReads_Is201_F1()
    {
        SeedInvoiceUnderMAndX(secureX: true);
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(ProjectX, ExternalAccessLevel.ViewOnly) },
            MatterAccess = new Dictionary<Guid, AccessRights> { [MatterM] = Collaborate },
        };

        using var upload = await UploadToInvoiceA();

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle().Which.Container.Should().Be(SecureMatterContainer, "X's own container");
    }

    [Fact]
    public async Task InvoiceUpload_WhoseAgreementIsUnderASecureMatterTheCallerCannotRead_Is403_AndWritesNothing_F1()
    {
        var secureMatter = Guid.Parse("c0310000-0000-4000-8000-0000000000a9");
        _fixture.Data.InvoiceParents[RecordA] = (MatterM, null);
        _fixture.Data.InvoiceAgreements[RecordA] = AgreementG;
        _fixture.Data.LiveRoots[("sprk_agreement", AgreementG)] = new[] { (ExternalGrantRootType.Matter, secureMatter) };
        _fixture.Data.RootIsSecure[(ExternalGrantRootType.Matter, secureMatter)] = true;
        NonSecure("sprk_matter", MatterM);
        _fixture.Principal = CollaboratorOnMatterM();

        using var upload = await UploadToInvoiceA();

        upload.StatusCode.Should().Be(HttpStatusCode.Forbidden, await upload.Content.ReadAsStringAsync());
        NothingWasWritten();

        // Control: the same invoice for a caller who also reads the secure matter.
        _fixture.Principal = CollaboratorOnMatterM(secureMatter);
        using var admitted = await UploadToInvoiceA();
        admitted.StatusCode.Should().Be(HttpStatusCode.Created, await admitted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task InvoiceUpload_WhenTheSecureFlagReadFails_Is503_AndWritesNothing_F1()
    {
        SeedInvoiceUnderMAndX(secureX: false);
        _fixture.Data.SecureFlagFault = new HttpRequestException("connection reset");

        using var upload = await UploadToInvoiceA();

        upload.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await upload.Content.ReadAsStringAsync()).Should().Contain("sdap.access.invoice_unreadable");
        NothingWasWritten();
    }

    [Fact]
    public async Task InvoiceUpload_WhenTheAgreementWalkFaults_Is503_AndWritesNothing_F1()
    {
        _fixture.Data.InvoiceParents[RecordA] = (MatterM, null);
        _fixture.Data.InvoiceAgreements[RecordA] = AgreementG;
        _fixture.Data.LiveParentFault = true;
        NonSecure("sprk_matter", MatterM);
        _fixture.Principal = CollaboratorOnMatterM();

        using var upload = await UploadToInvoiceA();

        upload.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await upload.Content.ReadAsStringAsync()).Should().Contain("sdap.access.invoice_unreadable");
        NothingWasWritten();
    }

    [Fact]
    public async Task InvoiceRights_WithoutCreate_CauseNoGateRead_F1()
    {
        SeedInvoiceUnderMAndX(secureX: true);
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights> { [MatterM] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.ViewOnly) },
        };

        using var client = _fixture.CreateAuthenticatedClient();
        (await client.GetAsync($"/api/v1/external/invoices/{RecordA}/documents")).StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.Data.InvoiceGateReads.Should().Be(0, "a ViewOnly reader has no Create for the gate to decide");
    }

    // =====================================================================
    // Owner decision 2026-10-11 (2): a document of a secure record — or of a record whose chain reaches one — goes into
    // that secure record's OWN container; every other document into the owning business unit's one container
    // =====================================================================

    private const string SecureMatterContainer = "b!secure-matter-own-container";

    private void SecureMatter(Guid matter) =>
        _fixture.Rows[("sprk_matter", matter)] = new Entity("sprk_matter", matter)
        {
            ["sprk_issecure"] = true,
            ["sprk_containerid"] = SecureMatterContainer,
            ["owningbusinessunit"] = new EntityReference("businessunit", Fixture.OwningBusinessUnitId),
        };

    private void NonSecure(string entity, Guid id) =>
        _fixture.Rows[(entity, id)] = new Entity(entity, id)
        {
            ["sprk_issecure"] = false,
            ["owningbusinessunit"] = new EntityReference("businessunit", Fixture.OwningBusinessUnitId),
        };

    private void ServiceRequestRegarding(Guid request, string column, string target, Guid targetId) =>
        _fixture.Rows[("sprk_servicerequest", request)] = new Entity("sprk_servicerequest", request)
        {
            ["owningbusinessunit"] = new EntityReference("businessunit", Fixture.OwningBusinessUnitId),
            [column] = new EntityReference(target, targetId),
        };

    [Fact]
    public async Task ServiceRequest_RegardingASecureMatter_StoresInTheMattersContainer_AndDownloadAndDeleteResolveTheSameContainer()
    {
        SecureMatter(MatterM);
        ServiceRequestRegarding(RecordA, "sprk_regardingmatter", "sprk_matter", MatterM);
        _fixture.Principal = CollaborateOn("servicerequests", RecordA);
        var bytes = "secure intake"u8.ToArray();

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/servicerequests/{RecordA}/documents", FileForm(bytes));

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle().Which.Container.Should().Be(SecureMatterContainer,
            "a service request regarding a secure matter stores its documents in the matter's own container, never the "
            + "business unit's shared one");
        var documentId = Guid.Parse((await upload.Content.ReadFromJsonAsync<ExternalDocumentDto>())!.SprkDocumentid!);

        // Download, through the route, with the REAL pointer check (what DocumentStorageResolver runs) — the container
        // derived for the document is the one the upload chose, so its own file is served.
        _fixture.VerifyPointers = true;
        using var download = await client.GetAsync($"/api/v1/external/servicerequests/{RecordA}/documents/{documentId}/content");
        download.StatusCode.Should().Be(HttpStatusCode.OK, await download.Content.ReadAsStringAsync());
        (await download.Content.ReadAsByteArrayAsync()).Should().Equal(bytes);

        // Delete: DocumentCheckoutService's verified-file-first delete runs the SAME check before touching the file. It
        // accepts the secure container and refuses the business unit's.
        var resolver = _fixture.Services.GetRequiredService<RecordContainerResolver>();
        var (_, itemId) = _fixture.Data.PointersOf(documentId);
        var derived = await resolver.DeriveDocumentContainersAsync(documentId);
        derived.Decided.Should().BeTrue(derived.Reason);
        derived.IsSecure.Should().BeTrue();
        derived.AllowedContainers.Should().Equal(SecureMatterContainer);

        await resolver.Invoking(r => r.EnsureDocumentPointerContainerAsync(documentId, SecureMatterContainer, itemId))
            .Should().NotThrowAsync();
        (await resolver.Invoking(r => r.EnsureDocumentPointerContainerAsync(documentId, Fixture.BusinessUnitContainer, itemId))
                .Should().ThrowAsync<Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException>())
            .Which.Code.Should().Be(RecordContainerResolver.DocumentStorageUnverifiedCode);
    }

    [Fact]
    public async Task ServiceRequest_WhoseRowCannotBeReadByTheContainerResolver_Is503_AndStoresAndWritesNothing()
    {
        // Review K4: the service request is a CORE record, so a throttled read of its own row (its regarding chain) used to
        // reach the route as a raw fault; UploadDocumentForParent catches only SdapProblemException. Same typed 503 as
        // every other route, with no exception text.
        _fixture.Principal = CollaborateOn("servicerequests", RecordA);
        _fixture.RowFaults[("sprk_servicerequest", RecordA)] = new TimeoutException("Dataverse timed out (secret-detail)");

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/servicerequests/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var body = await upload.Content.ReadAsStringAsync();
        body.Should().Contain(RecordContainerResolver.AncestorUnresolvedCode);
        body.Should().NotContain("secret-detail").And.NotContain("timed out");
        _fixture.Uploads.Should().BeEmpty();
        _fixture.Data.Documents.Should().BeEmpty("nothing was written");
    }

    [Fact]
    public async Task ServiceRequest_RegardingANonSecureMatter_StoresInTheBusinessUnitsContainer()
    {
        NonSecure("sprk_matter", MatterM);
        ServiceRequestRegarding(RecordA, "sprk_regardingmatter", "sprk_matter", MatterM);
        _fixture.Principal = CollaborateOn("servicerequests", RecordA);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/servicerequests/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        _fixture.Uploads.Should().ContainSingle().Which.Container.Should().Be(Fixture.BusinessUnitContainer);

        var documentId = Guid.Parse((await upload.Content.ReadFromJsonAsync<ExternalDocumentDto>())!.SprkDocumentid!);
        _fixture.VerifyPointers = true;
        (await client.GetAsync($"/api/v1/external/servicerequests/{RecordA}/documents/{documentId}/content"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ServiceRequest_RegardingAnInvoiceUnderASecureMatter_StoresInTheMattersContainer()
    {
        // The chain: request -> invoice -> secure matter. The invoice is read live and walked with its own links.
        var invoice = Guid.Parse("c0310000-0000-4000-8000-0000000000aa");
        SecureMatter(MatterM);
        _fixture.Data.InvoiceParents[invoice] = (MatterM, null);
        ServiceRequestRegarding(RecordA, "sprk_regardinginvoice", "sprk_invoice", invoice);
        _fixture.Principal = CollaborateOn("servicerequests", RecordA);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/servicerequests/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle().Which.Container.Should().Be(SecureMatterContainer);
    }

    [Fact]
    public async Task InvoiceDocument_UnderASecureMatter_IsStoredInTheMattersContainer_AndServedFromIt()
    {
        // Regression: a document on a CHILD of a secure root (an invoice under a secure matter) takes the root's container.
        SecureMatter(MatterM);
        _fixture.Data.InvoiceParents[RecordA] = (MatterM, null);
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "external.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights> { [MatterM] = Collaborate },
        };

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/invoices/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        _fixture.Uploads.Should().ContainSingle().Which.Container.Should().Be(SecureMatterContainer);

        var documentId = Guid.Parse((await upload.Content.ReadFromJsonAsync<ExternalDocumentDto>())!.SprkDocumentid!);
        _fixture.VerifyPointers = true;
        (await client.GetAsync($"/api/v1/external/invoices/{RecordA}/documents/{documentId}/content"))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task NonSecureParents_OfEveryType_ShareTheBusinessUnitsOneContainer_ThereIsNoContainerPerType()
    {
        // One container per customer business unit; none per record type. A non-secure project, matter, work assignment,
        // service request and invoice all store into the business unit's container.
        NonSecure("sprk_project", RecordA);
        NonSecure("sprk_matter", RecordA);
        NonSecure("sprk_workassignment", RecordA);
        NonSecure("sprk_matter", ParentMatterOf(RecordA));
        _fixture.Data.InvoiceParents[RecordA] = (ParentMatterOf(RecordA), null);
        _fixture.Data.Requesters[RecordA] = CallerContact;
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            ContactId = CallerContact,
            Email = "staff@customer.test",
            ProjectAccess = new[] { CallerProjectAccess.FromLevel(RecordA, ExternalAccessLevel.Collaborate) },
            MatterAccess = new Dictionary<Guid, AccessRights> { [RecordA] = Collaborate, [ParentMatterOf(RecordA)] = Collaborate },
            WorkAssignmentAccess = new Dictionary<Guid, AccessRights> { [RecordA] = Collaborate },
        };

        using var client = _fixture.CreateAuthenticatedClient();
        foreach (var segment in new[] { "projects", "matters", "workassignments", "servicerequests", "invoices" })
        {
            using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());
            upload.StatusCode.Should().Be(HttpStatusCode.Created, $"{segment}: {await upload.Content.ReadAsStringAsync()}");
        }

        _fixture.Uploads.Select(u => u.Container).Should().HaveCount(5).And.OnlyContain(c => c == Fixture.BusinessUnitContainer);
    }

    // =====================================================================
    // Task 063 (owner decision 2026-10-13): an external upload is queued for app-only indexing AND the document profile
    // =====================================================================

    [Theory]
    [MemberData(nameof(AllParents))]
    public async Task Upload_QueuesAppOnlyIndexing_AndTheDocumentProfile_OnceEach_ForTheNewDocument(string segment)
    {
        _fixture.Principal = CollaborateOn(segment, RecordA);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/{segment}/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var created = await upload.Content.ReadFromJsonAsync<ExternalDocumentDto>();
        var row = _fixture.Data.Documents.Should().ContainSingle().Subject;

        // Indexing: the app-only seam, with the pointers the row was written with (not a second derivation of them).
        var index = _fixture.IndexRequests.Should().ContainSingle().Subject;
        index.DocumentId.Should().Be(created!.SprkDocumentid);
        index.DriveId.Should().Be(row.DriveId);
        index.ItemId.Should().Be(row.ItemId);
        index.FileName.Should().Be("brief.pdf");
        index.FileSizeBytes.Should().Be(3);
        index.TenantId.Should().Be("test-tenant-id", "the Spaarke tenant from configuration, never the caller token's tid");
        index.Source.Should().Be("ExternalUpload");

        // Profile: one AppOnlyDocumentAnalysis job for the new document, under its document key.
        var documentId = Guid.Parse(created.SprkDocumentid!);
        var job = _fixture.SubmittedJobs.Should().ContainSingle().Subject;
        job.JobType.Should().Be(AppOnlyDocumentAnalysisJobHandler.JobTypeName);
        job.SubjectId.Should().Be(documentId.ToString());
        job.IdempotencyKey.Should().Be(AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(documentId));
        job.Payload!.RootElement.GetProperty("DocumentId").GetGuid().Should().Be(documentId);
        job.Payload.RootElement.GetProperty("Source").GetString().Should().Be("ExternalUpload");

        // Both queues run under a bounded token of their own: a hung Service Bus must not hold the 201 (was None).
        // And that token is NOT the request's own: HttpContext.RequestAborted is also cancellable and not yet cancelled, so
        // the check above alone would pass a route that still queued under the caller's token (cancelled on disconnect).
        var calls = _fixture.QueueTokens.ToList();
        calls.Should().HaveCount(2);
        calls.Should().OnlyContain(c => c.Queue.CanBeCanceled && !c.Queue.IsCancellationRequested);
        calls.Should().OnlyContain(c => c.Request.HasValue && c.Request.Value.CanBeCanceled,
            "the double must have seen the request's RequestAborted token for the comparison to mean anything");
        calls.Should().OnlyContain(c => c.Queue != c.Request!.Value, "the queues must not run under HttpContext.RequestAborted");
    }

    // The production enqueuer returns Failed (it catches its own failures); the route logs that and still answers 201.
    [Fact]
    public async Task Upload_WhenTheIndexEnqueueReturnsFailed_IsLoggedWithTheDocumentAndParent_AndStill201()
    {
        _fixture.Principal = CollaborateOn("matters", RecordA);
        _fixture.IndexReturnsFailed = true;

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/matters/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var created = await upload.Content.ReadFromJsonAsync<ExternalDocumentDto>();
        _fixture.SubmittedJobs.Should().ContainSingle("a failed index enqueue does not stop the profile");
        _fixture.Logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("[EXT-UPLOAD]") && e.Message.Contains("indexing")
            && e.Message.Contains(created!.SprkDocumentid!) && e.Message.Contains(RecordA.ToString()));
    }

    // The production enqueuer catches and logs its own failures; this pins the route's defensive catch, so that no
    // enqueuer (or a future one) that throws can turn a stored upload into a 500.
    [Fact]
    public async Task Upload_WhenTheIndexEnqueueThrows_IsLogged_AndStill201_AndTheProfileIsStillQueued()
    {
        _fixture.Principal = CollaborateOn("matters", RecordA);
        _fixture.IndexFault = new InvalidOperationException("service bus down");

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/matters/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var created = await upload.Content.ReadFromJsonAsync<ExternalDocumentDto>();
        created!.SprkDocumentid.Should().Be(_fixture.Data.Documents.Should().ContainSingle().Subject.Id.ToString());
        _fixture.IndexRequests.Should().ContainSingle();
        _fixture.SubmittedJobs.Should().ContainSingle("an indexing failure does not stop the profile");
        _fixture.Logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("[EXT-UPLOAD]") && e.Message.Contains("indexing")
            && e.Message.Contains(created.SprkDocumentid!));
    }

    [Fact]
    public async Task Upload_WhenTheProfileSubmitThrows_IsLogged_AndStill201_AndIndexingIsStillQueued()
    {
        _fixture.Principal = CollaborateOn("projects", RecordA);
        _fixture.JobFault = new InvalidOperationException("service bus down");

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/projects/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var created = await upload.Content.ReadFromJsonAsync<ExternalDocumentDto>();
        created!.SprkDocumentid.Should().Be(_fixture.Data.Documents.Should().ContainSingle().Subject.Id.ToString());
        _fixture.IndexRequests.Should().ContainSingle();
        _fixture.Logs.Entries.Should().Contain(e =>
            e.Level == LogLevel.Warning && e.Message.Contains("[EXT-UPLOAD]") && e.Message.Contains("profile")
            && e.Message.Contains(created.SprkDocumentid!));
    }

    [Fact]
    public async Task Upload_ANameCollision_EnqueuesNothing()
    {
        _fixture.Principal = CollaborateOn("projects", RecordA);
        _fixture.UploadFault = new SpaarkeStorageException("exists", statusCode: 409);

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/projects/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.Conflict);
        _fixture.IndexRequests.Should().BeEmpty();
        _fixture.SubmittedJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_WithNoUploadHandle_EnqueuesNothing()
    {
        _fixture.Principal = CollaborateOn("projects", RecordA);
        _fixture.UploadReturnsNoHandle = true;

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/projects/{RecordA}/documents", FileForm());

        upload.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        _fixture.IndexRequests.Should().BeEmpty();
        _fixture.SubmittedJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_WhenTheDocumentCreateFails_EnqueuesNothing()
    {
        _fixture.Principal = CollaborateOn("projects", RecordA);
        _fixture.Data.CreateFault = new HttpRequestException("dataverse down");

        using var client = _fixture.CreateAuthenticatedClient();
        using var upload = await client.PostAsync($"/api/v1/external/projects/{RecordA}/documents", FileForm());

        ((int)upload.StatusCode).Should().BeGreaterThanOrEqualTo(500);
        _fixture.Uploads.Should().ContainSingle("the bytes landed before the row create failed");
        _fixture.IndexRequests.Should().BeEmpty();
        _fixture.SubmittedJobs.Should().BeEmpty();
    }

    // =====================================================================
    // The list query and the create bind per parent (pure)
    // =====================================================================

    [Theory]
    [InlineData(ExternalDataService.DocumentParentKind.Project, "_sprk_project_value", "sprk_Project@odata.bind", "sprk_projects")]
    [InlineData(ExternalDataService.DocumentParentKind.Matter, "_sprk_matter_value", "sprk_Matter@odata.bind", "sprk_matters")]
    [InlineData(ExternalDataService.DocumentParentKind.WorkAssignment, "_sprk_workassignment_value", "sprk_WorkAssignment@odata.bind", "sprk_workassignments")]
    [InlineData(ExternalDataService.DocumentParentKind.ServiceRequest, "_sprk_relatedservicerequest_value", "sprk_RelatedServiceRequest@odata.bind", "sprk_servicerequests")]
    [InlineData(ExternalDataService.DocumentParentKind.Invoice, "_sprk_invoice_value", "sprk_Invoice@odata.bind", "sprk_invoices")]
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
    private static readonly Guid DocInvoice = Guid.Parse("d0310000-0000-4000-8000-000000000005");

    [Theory]
    [InlineData(ExternalDataService.DocumentParentKind.Project, "d0310000-0000-4000-8000-000000000001")]
    [InlineData(ExternalDataService.DocumentParentKind.Matter, "d0310000-0000-4000-8000-000000000002")]
    [InlineData(ExternalDataService.DocumentParentKind.WorkAssignment, "d0310000-0000-4000-8000-000000000003")]
    [InlineData(ExternalDataService.DocumentParentKind.ServiceRequest, "d0310000-0000-4000-8000-000000000004")]
    [InlineData(ExternalDataService.DocumentParentKind.Invoice, "d0310000-0000-4000-8000-000000000005")]
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
            ["_sprk_invoice_value"] = DocInvoice.ToString(),
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
    public void ProjectListUrl_IsTheShippedQuery_PlusTheOtherRootLinks()
    {
        // Task 032 (FR-26, review 031 K2) appended the two creator stamps; the task 039 fix round added the other root links
        // (typed + related twin) so a document also filed to a root the caller cannot read can be withheld. Filter and order unchanged.
        ExternalDataService.BuildDocumentListUrl("https://org.test/api/data/v9.2", ExternalDataService.DocumentParentKind.Project, RecordA)
            .Should().Be("https://org.test/api/data/v9.2/sprk_documents?$filter="
                         + Uri.EscapeDataString($"_sprk_project_value eq {RecordA}")
                         + "&$select=sprk_documentid,sprk_documentname,sprk_documenttype,sprk_filesummary,_sprk_project_value"
                         + ",_sprk_relatedproject_value,_sprk_matter_value,_sprk_relatedmatter_value"
                         + ",_sprk_workassignment_value,_sprk_relatedworkassignment_value,createdon"
                         // R3 ISS-011 (#1886): the SPE pointers, read only to derive hasFile (never put on the wire).
                         + ",sprk_graphdriveid,sprk_graphitemid"
                         + ",_sprk_createdbyperson_value,_sprk_createdbycontact_value"
                         // Task 187: the document's other parents, whose LIVE roots the rule takes in.
                         + ",_sprk_canonicaldocument_value,_sprk_invoice_value,_sprk_parentdocument_value,_sprk_relatedagreement_value"
                         + ",_sprk_relatedcommunication_value,_sprk_relatedevent_value,_sprk_relatedinvoice_value,_sprk_relatedtodo_value"
                         // Task 188: the document's own Access Permission, which the document narrowing reads (its two
                         // lookups, the related communication and the parent document, are already selected above).
                         + ",sprk_accesspermission"
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
        "invoices" => ExternalDataService.DocumentParentKind.Invoice,
        _ => ExternalDataService.DocumentParentKind.ServiceRequest,
    };

    /// <summary>The matter an invoice of this fixture is filed under, unless a test sets the invoice's parents itself.</summary>
    private static Guid ParentMatterOf(Guid invoice)
    {
        var bytes = invoice.ToByteArray();
        bytes[0] ^= 0xFF;
        return new Guid(bytes);
    }

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

    private CallerPrincipal Principal(string segment, Guid record, ExternalAccessLevel level) =>
        Principal(segment, new[] { record }, level);

    /// <summary>A caller holding <paramref name="level"/> on <paramref name="records"/> of the route's type. For an invoice
    /// (owner option A) the level is held on the invoice's parent matter (<see cref="ParentMatterOf"/>), which is where an
    /// invoice's rights come from.</summary>
    private CallerPrincipal Principal(string segment, Guid[] records, ExternalAccessLevel level)
    {
        if (segment == "invoices")
        {
            foreach (var record in records)
                _fixture.Data.InvoiceParents[record] = (ParentMatterOf(record), null);

            return new CallerPrincipal
            {
                Plane = CallerPrincipalPlane.CiamContact,
                ContactId = CallerContact,
                Email = "external.user@example.test",
                ProjectAccess = Array.Empty<CallerProjectAccess>(),
                MatterAccess = records.ToDictionary(ParentMatterOf, _ => ExternalAccessLevels.ToAccessRights(level)),
            };
        }

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

    private static readonly Guid CallerUser = Guid.Parse("77777777-7777-7777-7777-777777777031");

    /// <summary>Task 036: a licensed systemuser holding Read only on <paramref name="record"/> (for an invoice, on its
    /// parent matter) — the own term a flag-on composition gives. <paramref name="dataverseDecides"/> is the flag.</summary>
    private CallerPrincipal SystemUserReading(string segment, Guid record, bool dataverseDecides)
    {
        var reads = Principal(segment, record, ExternalAccessLevel.ViewOnly);
        return new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.Workforce,
            SystemUserId = CallerUser,
            ContactId = CallerContact,
            Email = "licensed@customer.test",
            ProjectAccess = reads.ProjectAccess,
            MatterAccess = reads.MatterAccess,
            WorkAssignmentAccess = reads.WorkAssignmentAccess,
            DataverseConfirmsWrites = dataverseDecides,
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

        public static readonly Guid OwningBusinessUnitId = Guid.Parse("b0310000-0000-4000-8000-0000000000b1");

        /// <summary>The application id the BFF uploads as (what an app-only upload's <c>createdBy.application.id</c> says).</summary>
        private static readonly Guid BffApplicationId = Guid.Parse("b0310000-0000-4000-8000-0000000000ff");

        private readonly ExternalTodoScopeTestFixture.StubCallerPrincipalResolver _principals = new();
        private readonly ConcurrentDictionary<string, byte[]> _spe = new();

        /// <summary>The container each stored item was uploaded into (what Graph answers for an item in a drive).</summary>
        private readonly ConcurrentDictionary<string, string> _itemDrives = new();

        /// <summary>
        /// Rows the container resolver reads, by (entity, id), overriding the defaults: a project, matter or work assignment
        /// is secure with <see cref="RecordContainer"/>; a service request regards nothing; an invoice's parents are
        /// <see cref="InMemoryExternalDataService.InvoiceParents"/>; a document is the stored row. Every row names
        /// <see cref="OwningBusinessUnitId"/>, whose container is <see cref="BusinessUnitContainer"/>.
        /// </summary>
        public ConcurrentDictionary<(string Entity, Guid Id), Entity> Rows { get; } = new();

        /// <summary>Records whose container-resolver read FAILS (a throttle or outage), by (entity, id).</summary>
        public ConcurrentDictionary<(string Entity, Guid Id), Exception> RowFaults { get; } = new();

        /// <summary>
        /// When set, the download and versions routes' pointer resolution runs the REAL document-pointer check
        /// (<see cref="RecordContainerResolver.EnsureDocumentPointerContainerAsync"/>) with the STRICT derived-container
        /// rule — so the route tests prove the STRICT pointer rule, as production's <c>DocumentStorageResolver</c> and the
        /// delete-own route's <c>DocumentCheckoutService</c> will apply it once the strict rule is switched on. The
        /// INTERIM rule (production's setting today) is not exercised here: it serves the legacy business-unit-container
        /// service-request documents through the business-unit-subtree arm.
        /// </summary>
        public bool VerifyPointers { get; set; }

        public InMemoryExternalDataService Data { get; } = new();

        public RecordOwnershipResolverDouble Ownership { get; } = new();

        /// <summary>Every SPE upload, as (container, path).</summary>
        public ConcurrentQueue<(string Container, string Path)> Uploads { get; } = new();

        /// <summary>Every SPE download, as (drive, item).</summary>
        public ConcurrentQueue<(string Drive, string Item)> Downloads { get; } = new();

        /// <summary>Task 063: every app-only index request the upload route made.</summary>
        public ConcurrentQueue<PostUploadIndexingRequest> IndexRequests { get; } = new();

        /// <summary>Task 063: every job submitted to the job queue (the document profile).</summary>
        public ConcurrentQueue<JobContract> SubmittedJobs { get; } = new();

        /// <summary>
        /// Task 063: for each post-upload queue call, the token it received and the token of the request being served
        /// (<c>HttpContext.RequestAborted</c>, read through <see cref="IHttpContextAccessor"/> inside the double). A queue
        /// token that equals the request's would be cancelled with the caller's disconnect.
        /// </summary>
        public ConcurrentQueue<(CancellationToken Queue, CancellationToken? Request)> QueueTokens { get; } = new();

        /// <summary>Set to make the index enqueue throw (after recording the request).</summary>
        public Exception? IndexFault { get; set; }

        /// <summary>Set to make the index enqueue return <see cref="PostUploadIndexingResult.Failed"/> (the production enqueuer's failure shape).</summary>
        public bool IndexReturnsFailed { get; set; }

        /// <summary>Set to make the job submit throw (nothing is recorded).</summary>
        public Exception? JobFault { get; set; }

        /// <summary>Set to make the SPE upload throw.</summary>
        public Exception? UploadFault { get; set; }

        /// <summary>Set to make the SPE upload return no handle.</summary>
        public bool UploadReturnsNoHandle { get; set; }

        /// <summary>Every log entry the host wrote.</summary>
        public CapturingLoggerProvider Logs { get; } = new();

        public CallerPrincipal? Principal
        {
            get => _principals.Principal;
            set => _principals.Principal = value;
        }

        /// <summary>Task 036: the caller's own Dataverse rights, as the upload route asks a Dataverse-decided caller for them.</summary>
        public CallerRecordAccessProbeDouble Probe { get; } = new();

        public void Reset()
        {
            Data.Reset();
            Probe.Reset();
            Principal = null;
            Ownership.TeamId = RecordOwnershipResolverDouble.DefaultTeamId;
            Ownership.Requests.Clear();
            Uploads.Clear();
            Downloads.Clear();
            IndexRequests.Clear();
            SubmittedJobs.Clear();
            QueueTokens.Clear();
            IndexFault = null;
            IndexReturnsFailed = false;
            JobFault = null;
            UploadFault = null;
            UploadReturnsNoHandle = false;
            Logs.Clear();
            _spe.Clear();
            _itemDrives.Clear();
            Rows.Clear();
            RowFaults.Clear();
            VerifyPointers = false;
        }

        /// <summary>
        /// Places <paramref name="bytes"/> in SPE as item <paramref name="itemId"/> of <paramref name="drive"/>, as if the BFF had
        /// uploaded it there — so a pointer to it passes the item-exists half of the pointer check and only the container rule
        /// can refuse it.
        /// </summary>
        public void StoreItem(string drive, string itemId, byte[] bytes)
        {
            _spe[itemId] = bytes;
            _itemDrives[itemId] = drive;
        }

        /// <summary>The served request's <c>RequestAborted</c> token, or null when no request is in flight on this flow.</summary>
        private CancellationToken? RequestAbortedToken() =>
            Services.GetRequiredService<IHttpContextAccessor>().HttpContext?.RequestAborted;

        /// <summary>The row the container resolver reads for (<paramref name="entity"/>, <paramref name="id"/>).</summary>
        private Entity RecordRow(string entity, Guid id)
        {
            if (RowFaults.TryGetValue((entity, id), out var fault))
                throw fault;

            if (Rows.TryGetValue((entity, id), out var row))
                return row;

            var businessUnit = new EntityReference("businessunit", OwningBusinessUnitId);
            switch (entity)
            {
                case "businessunit":
                    return new Entity(entity, id) { ["sprk_containerid"] = BusinessUnitContainer };
                case "sprk_servicerequest":
                    return new Entity(entity, id) { ["owningbusinessunit"] = businessUnit };
                case "sprk_invoice":
                    var (matter, project) = Data.InvoiceParents.TryGetValue(id, out var parents)
                        ? parents
                        : throw new InvalidOperationException($"Unmodelled invoice {id}");
                    var invoice = new Entity(entity, id) { ["owningbusinessunit"] = businessUnit };
                    if (matter is { } m) invoice["sprk_matter"] = new EntityReference("sprk_matter", m);
                    if (project is { } p) invoice["sprk_project"] = new EntityReference("sprk_project", p);
                    return invoice;
                case "sprk_document":
                    return Data.DocumentEntity(id, OwningBusinessUnitId);
                default:
                    return new Entity(entity, id)
                    {
                        ["sprk_issecure"] = true,
                        ["sprk_containerid"] = RecordContainer,
                        ["owningbusinessunit"] = businessUnit,
                    };
            }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);

            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<ICallerPrincipalResolver>(_ => _principals);
                services.AddScoped(_ => (ExternalDataService)Data);
                services.AddScoped<CallerRecordAccessProbe>(_ => Probe); // task 036 (overrides the typed client)
                services.RemoveAll<IRecordOwnershipResolver>();
                services.AddSingleton<IRecordOwnershipResolver>(Ownership);

                // The REAL container resolver, answering as production does: a project, matter and work assignment are
                // securable; a service request is NOT securable, and neither is an invoice (its flag is not a security
                // input, SecurableEntityRegistry.FlagIsNotASecurityInput) — both are decided by the records they name.
                var registry = new Mock<ISecurableEntityRegistry>();
                registry.Setup(r => r.ClassifyEntityAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string entity, CancellationToken _) =>
                        entity is "sprk_servicerequest" or "sprk_invoice" or "sprk_document"
                            ? EntitySecurability.NotSecurable
                            : EntitySecurability.Securable);
                var records = new Mock<IGenericEntityService>();
                records.Setup(r => r.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string entity, Guid id, string[] _, CancellationToken _) => RecordRow(entity, id));

                // SPE facade, in memory: an upload stores the bytes under a new item id; a download returns them.
                var files = new Mock<ISpeFileOperations>();
                files.Setup(f => f.GetItemCreatorAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((string drive, string item, CancellationToken _) =>
                        _itemDrives.TryGetValue(item, out var stored) && stored == drive
                            ? new SpeItemCreator(item, null, BffApplicationId.ToString())
                            : null);

                var resolver = new RecordContainerResolver(
                    registry.Object, records.Object, NullLogger<RecordContainerResolver>.Instance,
                    speFiles: files.Object,
                    configuration: new ConfigurationBuilder()
                        .AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            [RecordContainerResolver.StrictDerivedContainerKey] = "true",
                            ["AzureAd:ClientId"] = BffApplicationId.ToString(),
                        })
                        .Build());
                services.RemoveAll<RecordContainerResolver>();
                services.AddSingleton(resolver);
                files.Setup(f => f.UploadSmallAsync(
                        It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<ConflictBehavior>(), It.IsAny<CancellationToken>()))
                    .Returns((string container, string path, Stream content, ConflictBehavior _, CancellationToken _) =>
                    {
                        if (UploadFault is { } uploadFault)
                            throw uploadFault;
                        if (UploadReturnsNoHandle)
                            return Task.FromResult<FileHandleDto?>(null);

                        Uploads.Enqueue((container, path));
                        using var buffer = new MemoryStream();
                        content.CopyTo(buffer);
                        var itemId = $"item-{Guid.NewGuid():N}";
                        _spe[itemId] = buffer.ToArray();
                        _itemDrives[itemId] = container;
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

                // Pointer resolver: the document row's own pointers, as the real resolver reads them — and, when
                // VerifyPointers is set, the real resolver's pointer check on them first, as DocumentStorageResolver does.
                var pointers = new Mock<IDocumentStorageResolver>();
                pointers.Setup(p => p.GetSpePointersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                    .Returns(async (Guid documentId, CancellationToken ct) =>
                    {
                        var (driveId, itemId) = Data.PointersOf(documentId);
                        if (VerifyPointers)
                            await resolver.EnsureDocumentPointerContainerAsync(documentId, driveId, itemId, ct);
                        return (driveId, itemId);
                    });
                services.RemoveAll<IDocumentStorageResolver>();
                services.AddSingleton(pointers.Object);

                // Task 063: the two post-upload queues, recorded. The index enqueue records the request, then throws when
                // IndexFault is set; the job submit throws when JobFault is set.
                services.AddHttpContextAccessor();
                var indexing = new Mock<IPostUploadIndexingEnqueuer>();
                indexing.Setup(i => i.EnqueueAppOnlyIfApplicableAsync(It.IsAny<PostUploadIndexingRequest>(), It.IsAny<CancellationToken>()))
                    .Returns((PostUploadIndexingRequest request, CancellationToken token) =>
                    {
                        IndexRequests.Enqueue(request);
                        QueueTokens.Enqueue((token, RequestAbortedToken()));
                        if (IndexFault is { } fault)
                            return Task.FromException<PostUploadIndexingResult>(fault);
                        return Task.FromResult(IndexReturnsFailed
                            ? PostUploadIndexingResult.Failed("ServiceBusException")
                            : PostUploadIndexingResult.Submitted(Guid.NewGuid()));
                    });
                services.RemoveAll<IPostUploadIndexingEnqueuer>();
                services.AddSingleton(indexing.Object);

                var serviceBusOptions = new Mock<IOptions<ServiceBusOptions>>();
                serviceBusOptions.Setup(o => o.Value).Returns(new ServiceBusOptions { QueueName = "test-jobs" });
                var jobs = new Mock<JobSubmissionService>(
                    MockBehavior.Loose,
                    serviceBusOptions.Object,
                    NullLogger<JobSubmissionService>.Instance,
                    new Mock<ServiceBusClient>().Object);
                jobs.Setup(j => j.SubmitJobAsync(It.IsAny<JobContract>(), It.IsAny<CancellationToken>()))
                    .Returns((JobContract job, CancellationToken token) =>
                    {
                        if (JobFault is { } fault)
                            return Task.FromException(fault);
                        SubmittedJobs.Enqueue(job);
                        QueueTokens.Enqueue((token, RequestAbortedToken()));
                        return Task.CompletedTask;
                    });
                services.RemoveAll<JobSubmissionService>();
                services.AddSingleton(jobs.Object);

                services.AddSingleton<ILoggerProvider>(Logs);
            });
        }
    }

    /// <summary>Captures every log entry (task 063: the post-upload queue failures are logged).</summary>
    public sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly List<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> Entries
        {
            get { lock (_entries) return _entries.ToList(); }
        }

        public void Clear()
        {
            lock (_entries) _entries.Clear();
        }

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose() { }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._entries) owner._entries.Add(new LogEntry(logLevel, formatter(state, exception)));
            }
        }
    }

    /// <summary>One captured log entry.</summary>
    public sealed record LogEntry(LogLevel Level, string Message);

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

        /// <summary>Invoice id → its parent matter and project. Absent = the invoice does not exist.</summary>
        public ConcurrentDictionary<Guid, (Guid? MatterId, Guid? ProjectId)> InvoiceParents { get; } = new();

        /// <summary>Set to make the invoice parent read fail the way a throttled or unreachable Dataverse does.</summary>
        public Exception? InvoiceFault { get; set; }

        /// <summary>How many times an invoice's parents were read.</summary>
        public int InvoiceReads;

        /// <summary>Task 063: set to make the document create fail the way an unreachable Dataverse does.</summary>
        public Exception? CreateFault { get; set; }

        // ---- Task 187 fix round 2 (F1): the invoice gate's reads (GetInvoiceLookupsAsync, the flags, the live parents) ----

        /// <summary>Invoice id → the agreement it regards (<c>sprk_regardingagreement</c>). Absent = none.</summary>
        public ConcurrentDictionary<Guid, Guid> InvoiceAgreements { get; } = new();

        /// <summary>A root's effective secure state as the secure-flag read answers it. Absent = unreadable (counts as secure).</summary>
        public ConcurrentDictionary<(ExternalGrantRootType Root, Guid Id), bool> RootIsSecure { get; } = new();

        /// <summary>Set to make the secure-flag read fail.</summary>
        public Exception? SecureFlagFault { get; set; }

        /// <summary>A parent record's live roots, as the live-parent walk answers them. Absent = not resolved (fail closed).</summary>
        public ConcurrentDictionary<(string Table, Guid Id), IReadOnlyList<(ExternalGrantRootType Root, Guid Id)>> LiveRoots { get; } = new();

        /// <summary>Set to make the live-parent walk report a fault.</summary>
        public bool LiveParentFault { get; set; }

        /// <summary>How many times the invoice gate read the invoice.</summary>
        public int InvoiceGateReads;

        public void Reset()
        {
            CreateFault = null;
            lock (_documents) _documents.Clear();
            Requesters.Clear();
            DocumentReads.Clear();
            RequesterFault = null;
            Interlocked.Exchange(ref RequesterReads, 0);
            InvoiceParents.Clear();
            InvoiceFault = null;
            Interlocked.Exchange(ref InvoiceReads, 0);
            InvoiceAgreements.Clear();
            RootIsSecure.Clear();
            SecureFlagFault = null;
            LiveRoots.Clear();
            LiveParentFault = false;
            Interlocked.Exchange(ref InvoiceGateReads, 0);
        }

        // The invoice row as the gate reads it: the same parents as GetInvoiceParentsAsync, plus its agreement.
        public override Task<IReadOnlyDictionary<string, object?>> GetInvoiceLookupsAsync(
            Guid invoiceId, IReadOnlyCollection<string> lookups, CancellationToken ct = default)
        {
            Interlocked.Increment(ref InvoiceGateReads);
            if (InvoiceFault is { } fault)
                throw fault;
            if (!InvoiceParents.TryGetValue(invoiceId, out var parents))
                throw new RecordNotFoundException("sprk_invoice", invoiceId);

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            if (parents.MatterId is { } matter && lookups.Contains("sprk_matter")) row["sprk_matter"] = matter;
            if (parents.ProjectId is { } project && lookups.Contains("sprk_project")) row["sprk_project"] = project;
            if (InvoiceAgreements.TryGetValue(invoiceId, out var agreement) && lookups.Contains("sprk_regardingagreement"))
                row["sprk_regardingagreement"] = agreement;
            return Task.FromResult<IReadOnlyDictionary<string, object?>>(row);
        }

        public override Task<NonSecureRootsRead> GetNonSecureRootsAsync(
            IReadOnlyCollection<(ExternalGrantRootType Root, Guid Id)> roots, CancellationToken ct = default)
        {
            if (SecureFlagFault is { } fault)
                throw fault;
            return Task.FromResult(new NonSecureRootsRead(
                roots.Where(r => RootIsSecure.TryGetValue(r, out var secure) && !secure).ToHashSet()));
        }

        public override Task<LiveParentRootsRead> GetLiveParentRootsAsync(
            IReadOnlyCollection<(string Table, Guid Id)> parents, CancellationToken ct = default)
        {
            var resolved = new Dictionary<(string Table, Guid Id), IReadOnlyList<(ExternalGrantRootType Root, Guid Id)>>();
            if (!LiveParentFault)
            {
                foreach (var parent in parents)
                {
                    if (LiveRoots.TryGetValue(parent, out var roots))
                        resolved[parent] = roots;
                }
            }

            return Task.FromResult(new LiveParentRootsRead(resolved, LiveParentFault));
        }

        public override Task<(Guid? MatterId, Guid? ProjectId)?> GetInvoiceParentsAsync(Guid invoiceId, CancellationToken ct = default)
        {
            Interlocked.Increment(ref InvoiceReads);
            if (InvoiceFault is { } fault)
                throw fault;
            return Task.FromResult(InvoiceParents.TryGetValue(invoiceId, out var parents)
                ? parents
                : ((Guid? MatterId, Guid? ProjectId)?)null);
        }

        /// <summary>
        /// The stored document as the container resolver reads a <c>sprk_document</c> row: each parent lookup the REAL
        /// builder bound, its owning business unit, and the field-secured copy of its item id (written by the same create).
        /// </summary>
        public Entity DocumentEntity(Guid documentId, Guid owningBusinessUnit)
        {
            var stored = Documents.Single(d => d.Id == documentId);
            var row = new Entity("sprk_document", documentId)
            {
                ["owningbusinessunit"] = new EntityReference("businessunit", owningBusinessUnit),
                [DocumentPointerBinding.BoundItemIdColumn] = stored.ItemId,
            };

            foreach (var kind in Enum.GetValues<DocumentParentKind>())
            {
                if (ParentOf(stored, kind) is { } parentId)
                {
                    var binding = GetDocumentParentBinding(kind);
                    row[binding.LookupAttribute] = new EntityReference(binding.EntityLogicalName, parentId);
                }
            }

            return row;
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
            DocumentParentKind parentKind, Guid parentId, CallerPrincipal caller, CancellationToken ct = default)
        {
            DocumentReads.Enqueue($"list:{parentKind}:{parentId}");
            return Task.FromResult(new ExternalCollectionResponse<ExternalDocumentDto>
            {
                Value = Documents.Where(d => ParentOf(d, parentKind) == parentId)
                    .Select(d => new ExternalDocumentDto { SprkDocumentid = d.Id.ToString(), SprkName = d.Name })
                    .ToList(),
            });
        }

        // Every document here is filed to ONE parent (the builder binds one), so it has no other root link: the every-root
        // rule (task 039 fix round) has nothing to refuse. Documents with several roots are
        // ExternalMultiRootDocumentRouteTests, over the real service.
        public override Task<DocumentScopeRead> GetDocumentParentAndNameAsync(
            DocumentParentKind parentKind, Guid documentId, CancellationToken ct = default)
        {
            DocumentReads.Enqueue($"scope:{parentKind}:{documentId}");
            var row = Documents.SingleOrDefault(d => d.Id == documentId);
            return Task.FromResult(row is null
                ? DocumentScopeRead.Absent
                : new DocumentScopeRead(ParentOf(row, parentKind), row.Name, default));
        }

        // The real seam's body without the transport: build the payload, stamp it (task 030), store it.
        public override Task<ExternalDocumentDto> CreateDocumentAsync(
            DocumentParentKind parentKind, Guid parentId, ExternalUploadedFilePointers pointers, Guid owningTeamId,
            CallerPrincipal creator, CancellationToken ct = default)
        {
            if (CreateFault is { } fault)
                throw fault;

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
