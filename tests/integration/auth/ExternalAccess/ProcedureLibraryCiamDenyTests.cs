// ebilling-r1 task 028 (FR-16, D-55, D-63): a firm (CIAM) user can never download the bytes of a document in the Procedure
// Library container, and has no content or RAG search route through which to find one.
//
// SCOPE — BYTES, NOT METADATA. These tests prove the file content is refused. They do NOT prove the row's metadata is hidden:
// a Procedure Library sprk_document row filed (by mistake) under a record the firm can read still appears in that record's
// external document LIST (name, summary). Closing that needs a change to the shared external-access list routes — DEF-015.
//
// KEEP-path classification (ADR-038 section 2 path #1, security-auth): every test asserts an AUTHORIZATION decision for an
// external caller. Compiled into Sprk.Bff.Api.Tests by the existing tests/integration/auth glob.
//
// WHY THE DENY HOLDS (the trace, task 028 notes):
//   OPEN. A CIAM token authenticates only on the /api/v1/external group. Its only file-bytes routes are
//   /{projects|matters|workassignments|servicerequests|invoices}/{id}/documents/{documentId}/content, which (1) need Read on the
//   route record, (2) need the document filed under that record, and only then (3) resolve the row's SPE pointer through
//   DocumentStorageResolver, which runs RecordContainerResolver's document-pointer check before the app-only read. That check
//   honours only a container DERIVED for the document's own records (a business unit's container, a secure record's own
//   container, the communication archive, the unfiled default) — never a container that is only an app setting, such as the
//   Procedure Library. So even a Procedure Library document filed (by mistake) under a record the firm CAN read is refused.
//   SEARCH. No search route admits the Ciam scheme, and the default (workforce) scheme rejects every CIAM token
//   (AuthorizationModuleCiamTests). The /api/v1/external group has no content or RAG search route. (Its module-data route,
//   /api/v1/external/api/dataverse/*, does return sprk_documentname for a document filed under a firm-readable record: DEF-015.)
//
// Every must-fire has its must-not-fire control (the same document through its derived container IS served; the external
// group DOES admit the Ciam scheme), so a check that only ever refuses would fail here.

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Tests.Api.ExternalAccess;
using Xunit;
using World = TestRecordContainerResolver.DocumentPointerWorld;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>The document-pointer check never serves a Procedure Library pointer, under either rule.</summary>
[Trait("category", "external-access")]
public sealed class ProcedureLibraryPointerTests
{
    private const string Item = "01PROCEDURELIBRARYITEM";
    private const string ProcedureLibrary = "b!procedure-library";
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

        // The worst case: a guideline document filed (by mistake) to the firm's matter, its file in the Procedure Library,
        // uploaded by the row's own creator — so only the CONTAINER half of the check can refuse it.
        var row = World.Document(DocumentId, owningBusinessUnit: CustomerUnit);
        row["sprk_matter"] = new EntityReference("sprk_matter", FirmMatter);
        world.Rows[("sprk_document", DocumentId)] = row;
        return world;
    }

    [Theory]
    [InlineData(false)] // the interim rule (production default)
    [InlineData(true)]  // the strict derived-container rule
    public async Task APointerIntoTheProcedureLibrary_OnADocumentFiledToAFirmReadableMatter_IsRefused(bool strict)
    {
        var resolver = Environment(strict).Build();

        (await resolver.IsDocumentPointerContainerAllowedAsync(DocumentId, ProcedureLibrary, Item)).Should().BeFalse(
            "the Procedure Library is no business unit's, secure record's or archive container, so no app-only document read "
            + "(the external download route's included) may follow a pointer into it (D-63)");
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
/// matter gets no bytes of a Procedure Library document filed to that matter.
/// </summary>
[Trait("category", "external-access")]
public sealed class ProcedureLibraryExternalDownloadTests : IClassFixture<ExternalRecordDocumentRoutesTests.Fixture>
{
    private const string ProcedureLibrary = "b!procedure-library";

    private static readonly Guid CallerContact = Guid.Parse("99999999-9999-9999-9999-999999999028");
    private static readonly Guid FirmMatter = Guid.Parse("a0280000-0000-4000-8000-00000000000a");

    private readonly ExternalRecordDocumentRoutesTests.Fixture _fixture;

    public ProcedureLibraryExternalDownloadTests(ExternalRecordDocumentRoutesTests.Fixture fixture)
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
    public async Task ProcedureLibraryDocument_FiledToAMatterTheFirmCanRead_StreamsNoBytes()
    {
        var document = _fixture.Data.Seed(
            ExternalDataService.DocumentParentKind.Matter, FirmMatter, "billing-guidelines.pdf", ProcedureLibrary, "item-guidelines");
        // The item really is in the Procedure Library (uploaded by the BFF), so the item-exists half of the pointer check
        // passes and only the container rule can refuse it.
        _fixture.StoreItem(ProcedureLibrary, "item-guidelines", new byte[] { 7, 7, 7 });

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

/// <summary>
/// No search route is reachable with a CIAM token: the only routes admitting the Ciam scheme are the <c>/api/v1/external</c>
/// group's, and none of them searches. Read from the REAL app's endpoint table and policies.
/// </summary>
[Trait("category", "external-access")]
public sealed class CiamSearchRouteTests : IClassFixture<CustomWebAppFactory>
{
    private readonly CustomWebAppFactory _factory;

    public CiamSearchRouteTests(CustomWebAppFactory factory) => _factory = factory;

    [Fact]
    public async Task OnlyExternalGroupRoutesAdmitTheCiamScheme_AndNoneOfThemSearches_AndNoSearchRouteIsAnonymous()
    {
        var policies = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();
        var routes = EndpointTable.Endpoints(_factory);

        var ciamRoutes = new List<string>();
        var searchRoutes = new List<(string Route, bool Anonymous, bool AdmitsCiam)>();
        foreach (var endpoint in routes)
        {
            var route = "/" + (endpoint.RoutePattern.RawText ?? string.Empty).TrimStart('/');
            var anonymous = endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
            var policy = await AuthorizationPolicy.CombineAsync(
                policies,
                endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>(),
                endpoint.Metadata.GetOrderedMetadata<AuthorizationPolicy>())
                ?? await policies.GetFallbackPolicyAsync();
            var admitsCiam = policy?.AuthenticationSchemes.Contains(AuthSchemes.Ciam) == true;

            if (admitsCiam)
                ciamRoutes.Add(route);
            if (route.Contains("search", StringComparison.OrdinalIgnoreCase))
                searchRoutes.Add((route, anonymous, admitsCiam));
        }

        // Must-fire control: the detection sees the external group's Ciam scheme (otherwise every assertion below is vacuous).
        ciamRoutes.Should().Contain(r => r.StartsWith("/api/v1/external/", StringComparison.OrdinalIgnoreCase));
        searchRoutes.Should().NotBeEmpty("the BFF has search routes; finding none means the scan is broken");

        ciamRoutes.Should().OnlyContain(
            r => r.Equals("/api/v1/external", StringComparison.OrdinalIgnoreCase)
                 || r.StartsWith("/api/v1/external/", StringComparison.OrdinalIgnoreCase),
            "a CIAM token authenticates only on the external group (not on the workforce /api/v1/external-access group)");
        ciamRoutes.Should().NotContain(r => r.Contains("search", StringComparison.OrdinalIgnoreCase),
            "the external group has no content or RAG search route (D-63); the module-data route can still return sprk_documentname for a document filed under a firm-readable record, which is DEF-015 and not asserted here");
        searchRoutes.Should().OnlyContain(s => !s.Anonymous && !s.AdmitsCiam,
            "every search route is authenticated on the workforce scheme, which rejects CIAM tokens");
    }
}
