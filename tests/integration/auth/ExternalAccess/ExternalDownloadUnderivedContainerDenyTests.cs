// ebilling-r1 task 028 (FR-16, D-55, D-63), kept and renamed by task 124 review fix round 1: a firm (CIAM) user can never download
// the bytes of a document whose file pointer is in a container no record derives for it (a container that exists only as an app
// setting, or as nobody's container at all).
//
// HISTORY. Task 028 wrote these two classes for one such container, the eBilling Procedure Library. Task 124 retired that container
// and its setting (a Procedure file is now a Stored File in the Finance file store), but the rule these tests pin never depended on
// that container: they do not set the retired key, and they use a made-up container id that no derivation can produce. They prove the
// GENERAL rule, so they stay under a name that says so. (The Stored File deny proof is ExternalInvoiceFileRoutesTests
// .ProcedureSourceFile_IsNeverListedOrServedToAnExternalCaller and the file-store route census guard.)
//
// SCOPE — BYTES, NOT METADATA. These tests prove the file content is refused. They do NOT prove the row's metadata is hidden:
// a document row filed (by mistake) under a record the firm can read still appears in that record's external document LIST
// (name, summary). Closing that needs a change to the shared external-access list routes — DEF-015.
//
// KEEP-path classification (ADR-038 section 2 path #1, security-auth): every test asserts an AUTHORIZATION decision for an
// external caller. Compiled into Sprk.Bff.Api.Tests by the existing tests/integration/auth glob.
//
// WHY THE DENY HOLDS (the trace, task 028 notes):
//   OPEN. A CIAM token authenticates only on the /api/v1/external group. Its file-bytes routes are:
//   - /{projects|matters|workassignments|servicerequests}/{id}/documents/{documentId}/content, which (1) need Read on the
//     route record, (2) need the document filed under that record, and only then (3) resolve the row's SPE pointer through
//     DocumentStorageResolver, which runs RecordContainerResolver's document-pointer check before the app-only read. That check
//     honours only a container DERIVED for the document's own records (a business unit's container, a secure record's own
//     container, the communication archive, the unfiled default) — never a container that is only an app setting or that no
//     record derives. So even such a document filed (by mistake) under a record the firm CAN read is refused.
//     (The /invoices/{id}/documents routes retired with ebilling-r1 task 093.)
//   - /invoices/{id}/files/{fileId}/content (ebilling-r1 task 093), which serves a Stored File, not an SPE document, and only
//     one the invoice files gate admitted: a row whose parent is the route's invoice, whose purpose is Invoice PDF, marked
//     firm-visible, Servable and with no source. A Procedure Version's file is parented on its Procedure Version and has
//     another purpose, so it is never in that set; the route answers it with the uniform 404 (the parent and purpose checks are
//     pinned by ExternalInvoiceFileRoutesTests: another invoice's file, a supporting file, a Procedure source file).
//
// Every must-fire has its must-not-fire control (the same document through its derived container IS served; a document the firm
// uploaded IS served), so a check that only ever refuses would fail here.

using System.Net;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>The document-pointer check never serves a pointer into a container no record derives, under either rule.</summary>
[Trait("category", "external-access")]
public sealed class ExternalDownloadUnderivedContainerPointerTests
{
    private const string Item = "01UNDERIVEDCONTAINERITEM";
    private const string UnderivedContainer = "b!underived-container";
    private const string FirmMatterUnitContainer = "b!firm-matter-business-unit-container";

    private static readonly Guid Root = TestRecordContainerResolver.PointerWorldRootBusinessUnit;
    private static readonly Guid CustomerUnit = Guid.Parse("a0000000-0000-4000-8000-000000000028");
    private static readonly Guid FirmMatter = Guid.Parse("1b000000-0000-4000-8000-000000000028");
    private static readonly Guid DocumentId = Guid.Parse("1d000000-0000-4000-8000-000000000028");

    /// <summary>A plain matter a firm user can read, in a customer business unit that stamps its own container.</summary>
    private static World Environment(bool strict)
    {
        var world = new World { Strict = strict };
        world.BusinessUnits[CustomerUnit] = (Root, FirmMatterUnitContainer);
        world.Rows[("sprk_matter", FirmMatter)] = new Entity("sprk_matter", FirmMatter)
        {
            ["sprk_issecure"] = false,
            ["owningbusinessunit"] = new EntityReference("businessunit", CustomerUnit),
        };

        // The worst case: a document filed (by mistake) to the firm's matter, its file in a container no record derives,
        // uploaded by the row's own creator — so only the CONTAINER half of the check can refuse it.
        var row = World.Document(DocumentId, owningBusinessUnit: CustomerUnit);
        row["sprk_matter"] = new EntityReference("sprk_matter", FirmMatter);
        world.Rows[("sprk_document", DocumentId)] = row;
        return world;
    }

    [Theory]
    [InlineData(false)] // the interim rule (production default)
    [InlineData(true)]  // the strict derived-container rule
    public async Task APointerIntoAnUnderivedContainer_OnADocumentFiledToAFirmReadableMatter_IsRefused(bool strict)
    {
        var resolver = Environment(strict).Build();

        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, UnderivedContainer, Item)).Should().BeFalse(
            "a container that is no business unit's, secure record's or archive container is derived for no record, so no app-only "
            + "document read (the external download route's included) may follow a pointer into it (D-63)");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Control_TheSameDocumentAndItem_InItsDerivedContainer_IsServed(bool strict)
    {
        var resolver = Environment(strict).Build();

        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, FirmMatterUnitContainer, Item)).Should().BeTrue();
    }
}

/// <summary>
/// Through the REAL <c>/api/v1/external</c> download route, with the REAL pointer check: a firm user holding Collaborate on a
/// matter gets no bytes of a document filed to that matter whose file pointer is in a container no record derives.
/// </summary>
[Trait("category", "external-access")]
public sealed class ExternalDownloadUnderivedContainerDenyTests : IClassFixture<ExternalRecordDocumentRoutesTests.Fixture>
{
    private const string UnderivedContainer = "b!underived-container";

    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999028");
    private static readonly Guid FirmMatter = Guid.Parse("a0280000-0000-4000-8000-00000000000a");

    private readonly ExternalRecordDocumentRoutesTests.Fixture _fixture;

    public ExternalDownloadUnderivedContainerDenyTests(ExternalRecordDocumentRoutesTests.Fixture fixture)
    {
        _fixture = fixture;
        _fixture.Reset();
        _fixture.Principal = new CallerPrincipal
        {
            Plane = CallerPrincipalPlane.CiamContact,
            ContactId = CallerContact,
            Email = "firm.user@example.test",
            ProjectAccess = Array.Empty<CallerProjectAccess>(),
            MatterAccess = new Dictionary<Guid, AccessRights>
            {
                [FirmMatter] = ExternalAccessLevels.ToAccessRights(ExternalAccessLevel.Collaborate),
            },
        };
        _fixture.VerifyPointers = true;
    }

    [Fact]
    public async Task ADocument_FiledToAMatterTheFirmCanRead_WhoseFileIsInAnUnderivedContainer_StreamsNoBytes()
    {
        var document = _fixture.Data.Seed(
            ExternalDataService.DocumentParentKind.Matter, FirmMatter, "billing-guidelines.pdf", UnderivedContainer, "item-guidelines");
        // The item really is in that container (uploaded by the BFF), so the item-exists half of the pointer check
        // passes and only the container rule can refuse it.
        _fixture.StoreItem(UnderivedContainer, "item-guidelines", new byte[] { 7, 7, 7 });

        using var client = _fixture.CreateAuthenticatedClient();
        using var response = await client.GetAsync($"/api/v1/external/matters/{FirmMatter}/documents/{document}/content");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadAsStringAsync()).Should().Contain(RecordContainerResolver.DocumentStorageUnverifiedCode);
        _fixture.Downloads.Should().BeEmpty("the pointer check refuses before any SPE read");
    }

    [Fact]
    public async Task Control_ADocumentTheFirmUploadedToTheSameMatter_IsServed()
    {
        using var client = _fixture.CreateAuthenticatedClient();
        using var form = new MultipartFormDataContent { { new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "brief.pdf" } };
        using var upload = await client.PostAsync($"/api/v1/external/matters/{FirmMatter}/documents", form);
        upload.StatusCode.Should().Be(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var documentId = upload.Headers.Location!.ToString().Split('/').Last();

        using var download = await client.GetAsync($"/api/v1/external/matters/{FirmMatter}/documents/{documentId}/content");

        download.StatusCode.Should().Be(HttpStatusCode.OK, await download.Content.ReadAsStringAsync());
    }
}
