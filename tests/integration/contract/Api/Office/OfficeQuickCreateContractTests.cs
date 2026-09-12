using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.ServiceModel;
using System.Text.Json;
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
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// HTTP contract for <c>POST /api/office/quickcreate/matter</c> after spaarkeai-word-add-in-r1 task 030 (FR-13):
/// the Matter path runs through <c>RecordCreationService</c> — a load-bearing owner, business-unit defaults, the
/// matter-type lookup and the Field Mapping Framework — behind <c>QuickCreateSourceAccessFilter</c>. It never writes
/// <c>sprk_matternumber</c> (numbering is left to a planned separate component — owner decision 2026-09-11) and never
/// rejects a missing, empty or unknown matter type.
/// </summary>
/// <remarks>
/// The REAL pipeline runs end to end (routing, OfficeAuthFilter, the source-access filter, the handler,
/// OfficeService, RecordCreationService, CreateTimeFieldMapping). Only the Dataverse boundary
/// (<see cref="IGenericEntityService"/>, <see cref="IFieldMappingDataverseService"/>), the caller → systemuser resolver,
/// and the OBO rights probe (its designated virtual seam) are substituted — configured LOCALLY in
/// <see cref="OfficeQuickCreateTestWebAppFactory"/>, so the shared <see cref="OfficeTestWebAppFactory"/> is untouched.
/// "No row created" is asserted as "<see cref="IGenericEntityService.CreateAsync"/> was never called".
/// </remarks>
public class OfficeQuickCreateContractTests
{
    private const string Route = "/api/office/quickcreate/matter";

    /// <summary>Dataverse <c>0x80040217 ObjectDoesNotExist</c> as a signed int — what a retrieve of a missing row faults with.</summary>
    private const int ObjectDoesNotExist = -2147220969;

    private static readonly Guid OwnerId = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1a01");
    private static readonly Guid MatterTypeId = Guid.Parse("b2c4e6a8-1d3f-4a5b-8c7d-9e0f1a2b3c01");
    private static readonly Guid BusinessUnitId = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a99d1d7");
    private static readonly Guid SearchIndexId = Guid.Parse("fdcc183b-8b71-f111-ab0d-7ced8ddc4cc6");
    private static readonly Guid SourceProjectId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c01");
    private static readonly Guid AccountId = Guid.Parse("9f8e7d6c-5b4a-4c3d-8e2f-1a0b9c8d7e01");
    private static readonly Guid CreatedMatterId = Guid.Parse("00000000-0000-0000-0000-00000000c0de");

    // ── Happy path ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Matter_WithMatterType_Returns201_SetsTypeOwnerAndBusinessUnitDefaults_AndNoNumber()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeMatterTypeExists(factory);
        ArrangeBusinessUnit(factory);
        var created = CaptureCreate(factory);

        // Upper-case input — binding to Guid canonicalizes it, so the lookup carries the bare lower-case id (ADR-044).
        // (Brace-wrapped registry format is not accepted by System.Text.Json's Guid binding at all.)
        var response = await factory.CreateClient().PostAsJsonAsync(Route, new
        {
            name = "  Acme v. Globex  ",
            description = "From Word",
            matterTypeId = MatterTypeId.ToString("D").ToUpperInvariant(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        json.GetProperty("id").GetString().Should().Be(CreatedMatterId.ToString("D"));
        json.GetProperty("entityType").GetString().Should().Be("Matter");
        json.GetProperty("logicalName").GetString().Should().Be("sprk_matter");
        json.GetProperty("name").GetString().Should().Be("Acme v. Globex");
        json.TryGetProperty("number", out _).Should().BeFalse("nothing on this path assigns a number");
        json.TryGetProperty("warnings", out _).Should().BeFalse("a fully specified create has nothing to warn about");

        var matter = created.Entity!;
        matter.GetAttributeValue<EntityReference>("sprk_mattertype").Should().BeEquivalentTo(new EntityReference("sprk_mattertype_ref", MatterTypeId));
        matter.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("systemuser", OwnerId));
        matter.GetAttributeValue<string>("sprk_mattername").Should().Be("Acme v. Globex");
        matter.GetAttributeValue<string>("sprk_matterdescription").Should().Be("From Word");
        matter.GetAttributeValue<string>("sprk_searchindexname").Should().Be("spaarke-files-index");
        matter.GetAttributeValue<EntityReference>("sprk_ai_search_index").Id.Should().Be(SearchIndexId);
        AssertNoMatterNumberSent(matter);
        matter.Contains("sprk_containerid").Should().BeFalse("the container is derived server-side, never stamped on create (task 076 W1)");
    }

    // ── Matter type: never a rejection (owner decision 2026-09-11) ──────────────────────────────────────

    [Fact]
    public async Task Post_Matter_NameOnly_Returns201_NotA400_OwnedByCaller_AndWarnsAboutTheType()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        var created = CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new { name = "Name Only Matter" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, "a missing matter type is never a rejection");
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().ContainSingle(w => w.Contains("No matter type was supplied"));
        created.Entity!.Contains("sprk_mattertype").Should().BeFalse();
        AssertNoMatterNumberSent(created.Entity);
        created.Entity.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OwnerId);
    }

    [Fact]
    public async Task Post_Matter_WithEmptyGuidMatterType_IsTreatedAsNoType_Returns201()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        var created = CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            Route, new QuickCreateRequest { Name = "Empty Type", MatterTypeId = Guid.Empty });

        response.StatusCode.Should().Be(HttpStatusCode.Created, "Guid.Empty is a client's 'unset', not a malformed request");
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().ContainSingle(w => w.Contains("No matter type was supplied"));
        created.Entity!.Contains("sprk_mattertype").Should().BeFalse();
        factory.Entities.Verify(
            e => e.RetrieveAsync("sprk_mattertype_ref", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "there is no type to look up");
    }

    [Fact]
    public async Task Post_Matter_WithUnknownMatterType_Returns201_WithoutTheType_AndWarns()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        factory.Entities
            .Setup(e => e.RetrieveAsync("sprk_mattertype_ref", MatterTypeId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FaultException<OrganizationServiceFault>(
                new OrganizationServiceFault { ErrorCode = ObjectDoesNotExist, Message = "sprk_mattertype_ref Does Not Exist" },
                new FaultReason("Does Not Exist")));
        var created = CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            Route, new QuickCreateRequest { Name = "Ghost Type", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Created, "an unknown type is treated like a missing one — never a 400 or 500");
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().ContainSingle(w => w.Contains("matter type was not found"));
        created.Entity!.Contains("sprk_mattertype").Should().BeFalse("a dangling lookup would fail the whole create");
        created.Entity.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OwnerId);
        factory.Entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Post_Matter_WhenTheTypeCheckFails_CreatesWithoutTheType_AndWarnsDistinctly()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        factory.Entities
            .Setup(e => e.RetrieveAsync("sprk_mattertype_ref", MatterTypeId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse slow"));
        var created = CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            Route, new QuickCreateRequest { Name = "Transient Check", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Created, "the optional type never fails the create");
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().ContainSingle(w => w.Contains("could not be checked"))
            .And.NotContain(w => w.Contains("was not found"), "an unanswered check is reported distinctly from a missing type");
        created.Entity!.Contains("sprk_mattertype").Should().BeFalse("an unverified lookup that dangled would fault the whole create");
        created.Entity.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OwnerId);
    }

    // ── Field mapping ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Matter_WithSourceContextAndProfile_AppliesEveryRule_ButNeverTheProtectedFields()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeMatterTypeExists(factory);
        ArrangeBusinessUnit(factory);
        ArrangeSourceRights(factory, AccessRights.Read);
        var created = CaptureCreate(factory);
        ArrangeProfile(factory,
            Rule("copy-text", "sprk_clientreference", 0, "sprk_clientreference", 0, mappingType: 0, order: 1),
            Rule("copy-lookup", "sprk_client", 1, "sprk_client", 1, mappingType: 0, order: 2),
            Rule("default-option", "", 0, "sprk_billingstatus", 2, mappingType: 1, order: 3, defaultValue: "3"),
            Rule("template", "", 0, "sprk_matternotes", 6, mappingType: 3, order: 4,
                expression: "Opened from {sprk_projectname} ({sprk_notonsource})"),
            Rule("number-default", "", 0, "sprk_matternumber", 0, mappingType: 1, order: 5, defaultValue: "HACK-000001"),
            Rule("number-copy", "sprk_projectnumber", 0, "sprk_matternumber", 0, mappingType: 0, order: 6),
            Rule("number-template-mis-cased", "", 0, "  SPRK_MatterNumber ", 0, mappingType: 3, order: 7,
                expression: "X-{sprk_projectnumber}"),
            Rule("number-concat", "", 0, "sprk_matternumber", 0, mappingType: 2, order: 8, expression: "{sprk_projectnumber}"),
            Rule("owner-copy", "sprk_client", 1, "ownerid", 1, mappingType: 0, order: 9),
            Rule("container-default", "", 0, "sprk_containerid", 0, mappingType: 1, order: 10, defaultValue: "b!shared"));
        var requestedColumns = ArrangeSource(factory, new Entity("sprk_project", SourceProjectId)
        {
            ["sprk_clientreference"] = "ACME-REF",
            ["sprk_client"] = new EntityReference("account", AccountId),
            ["sprk_projectname"] = "Acme Project",
            ["sprk_projectnumber"] = "PRJ-000123",
        });

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "Acme Supply Agreement",
            MatterTypeId = MatterTypeId,
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();

        // One source read spanning every Copy field and every placeholder.
        requestedColumns.Single().Should().BeEquivalentTo(
            "sprk_clientreference", "sprk_client", "sprk_projectname", "sprk_notonsource", "sprk_projectnumber");

        var matter = created.Entity!;
        matter.GetAttributeValue<string>("sprk_clientreference").Should().Be("ACME-REF");
        matter.GetAttributeValue<EntityReference>("sprk_client").Should().BeEquivalentTo(new EntityReference("account", AccountId));
        matter.GetAttributeValue<OptionSetValue>("sprk_billingstatus").Value.Should().Be(3);
        matter.GetAttributeValue<string>("sprk_matternotes").Should().Be("Opened from Acme Project ()");

        // The protected attributes: never sent on create, whatever the profile says — Default, Copy, Template,
        // Concat, and a mis-cased, padded target alike.
        AssertNoMatterNumberSent(matter);
        matter.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(new EntityReference("systemuser", OwnerId));
        matter.Contains("sprk_containerid").Should().BeFalse();

        body!.Warnings.Should().NotBeNull();
        body.Warnings!.Count(w => w.Contains("sprk_matternumber", StringComparison.OrdinalIgnoreCase)).Should().Be(4);
        body.Warnings.Should().Contain(w => w.Contains("ownerid"))
            .And.Contain(w => w.Contains("sprk_containerid"))
            .And.Contain(w => w.Contains("sprk_notonsource"));
    }

    [Fact]
    public async Task Post_Matter_WithTypeSetOnlyByFieldMapping_CarriesTheMappedType()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        ArrangeSourceRights(factory, AccessRights.Read);
        var created = CaptureCreate(factory);
        ArrangeProfile(factory, Rule("copy-type", "sprk_mattertype", 1, "sprk_mattertype", 1, mappingType: 0, order: 1));
        ArrangeSource(factory, new Entity("sprk_project", SourceProjectId)
        {
            ["sprk_mattertype"] = new EntityReference("sprk_mattertype_ref", MatterTypeId),
        });

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "Mapped Type Matter",
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().BeNull("the type arrived through mapping, so nothing is missing");
        created.Entity!.GetAttributeValue<EntityReference>("sprk_mattertype").Id.Should().Be(MatterTypeId);
        AssertNoMatterNumberSent(created.Entity);
    }

    [Fact]
    public async Task Post_Matter_WhenMappingWritesANonMatterTypeValue_KeepsTheRequestedType()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeMatterTypeExists(factory);
        ArrangeBusinessUnit(factory);
        ArrangeSourceRights(factory, AccessRights.Read);
        var created = CaptureCreate(factory);
        ArrangeProfile(factory, Rule("bad-type", "sprk_client", 1, "sprk_mattertype", 1, mappingType: 0, order: 1));
        ArrangeSource(factory, new Entity("sprk_project", SourceProjectId)
        {
            ["sprk_client"] = new EntityReference("account", AccountId),
        });

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "Bad Mapped Type",
            MatterTypeId = MatterTypeId,
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().Contain(w => w.Contains("not a matter type"));
        created.Entity!.GetAttributeValue<EntityReference>("sprk_mattertype")
            .Should().BeEquivalentTo(new EntityReference("sprk_mattertype_ref", MatterTypeId),
                "a non-matter-type value would fail the whole create, so the requested type is restored");
    }

    [Fact]
    public async Task Post_Matter_WhenMappingWritesANonMatterTypeValue_AndNoTypeWasRequested_RemovesIt()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        ArrangeSourceRights(factory, AccessRights.Read);
        var created = CaptureCreate(factory);
        ArrangeProfile(factory, Rule("bad-type", "sprk_client", 1, "sprk_mattertype", 1, mappingType: 0, order: 1));
        ArrangeSource(factory, new Entity("sprk_project", SourceProjectId)
        {
            ["sprk_client"] = new EntityReference("account", AccountId),
        });

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "Bad Mapped Type, No Request",
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().Contain(w => w.Contains("not a matter type"))
            .And.Contain(w => w.Contains("No matter type was supplied"));
        created.Entity!.Contains("sprk_mattertype").Should().BeFalse("a non-matter-type value would fail the whole create");
    }

    [Fact]
    public async Task Post_Matter_WhenMappingBlanksTheName_KeepsTheRequestedName()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeMatterTypeExists(factory);
        ArrangeBusinessUnit(factory);
        ArrangeSourceRights(factory, AccessRights.Read);
        var created = CaptureCreate(factory);
        ArrangeProfile(factory, Rule("blank-name", "", 0, "sprk_mattername", 0, mappingType: 3, order: 1, expression: "{sprk_notonsource}"));
        ArrangeSource(factory, new Entity("sprk_project", SourceProjectId));

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "Requested Name",
            MatterTypeId = MatterTypeId,
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Name.Should().Be("Requested Name");
        created.Entity!.GetAttributeValue<string>("sprk_mattername").Should().Be("Requested Name");
        body.Warnings.Should().Contain(w => w.Contains("name you entered was kept"));
    }

    [Fact]
    public async Task Post_Matter_WithSourceContextButNoProfile_IsAGracefulNoOp_AndStillCreatesWithOwner()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeMatterTypeExists(factory);
        ArrangeBusinessUnit(factory);
        ArrangeSourceRights(factory, AccessRights.Read);
        var created = CaptureCreate(factory);
        factory.FieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldMappingProfileEntity?)null);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "No Profile Matter",
            MatterTypeId = MatterTypeId,
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().BeNull("a missing profile is a silent no-op");
        created.Entity!.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OwnerId);
        created.Entity.GetAttributeValue<EntityReference>("sprk_mattertype").Id.Should().Be(MatterTypeId);
        factory.Entities.Verify(
            e => e.RetrieveAsync("sprk_project", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "with no profile there is nothing to read from the source record");
    }

    // ── Negative: authentication / authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Matter_WhenUnauthenticated_Returns401_AndCreatesNothing()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        CaptureCreate(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        var response = await client.PostAsJsonAsync(Route, new QuickCreateRequest { Name = "Anon", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertNothingCreated(factory);
    }

    [Fact]
    public async Task Post_Matter_WhenCallerCannotReadTheSourceRecord_Returns403_ReadsNothing_AndCreatesNothing()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeSourceRights(factory, AccessRights.AppendTo); // holds rights, but not Read
        CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, SourceContextRequest("Someone Else's Project", "sprk_project"));

        await AssertSourceDeniedAsync(factory, response, "insufficient_rights");
    }

    [Fact]
    public async Task Post_Matter_WhenSourceRecordTypeCannotBeAuthorized_Returns403_WithoutAskingForRights()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, SourceContextRequest("Unmapped Type", "sprk_secretthing"));

        await AssertSourceDeniedAsync(factory, response, "entity_type_not_authorizable");
        factory.AccessProbe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("sprk_secretthing", "the caller's value is logged, not echoed");
    }

    [Fact]
    public async Task Post_Matter_WhenTheRightsProbeThrows_Returns403_FailClosed()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        factory.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "sprk_projects", SourceProjectId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse unreachable"));
        CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, SourceContextRequest("Probe Throws", "sprk_project"));

        await AssertSourceDeniedAsync(factory, response, "access_check_failed");
    }

    [Fact]
    public async Task Post_Matter_WhenCallerHasNoDataverseUser_Returns403_AndCreatesNothing()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));
        CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            Route, new QuickCreateRequest { Name = "Orphan", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemAsync(response)).Should().ContainKey("errorCode").WhoseValue.Should().Be("owner_unresolved");
        AssertNothingCreated(factory);
    }

    // ── Negative: validation ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_Matter_WithBlankName_Returns400ProblemDetails_AndCreatesNothing(string name)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest { Name = name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await ReadProblemAsync(response)).Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_007");
        AssertNothingCreated(factory);
    }

    [Fact]
    public async Task Post_Matter_WithHalfASourceContext_Returns400_AndCreatesNothing()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            Route, new QuickCreateRequest { Name = "Half Context", SourceRecordId = SourceProjectId });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        AssertNothingCreated(factory);
    }

    // ── Arrangement + assertion helpers ─────────────────────────────────────────────────────────────────

    private static QuickCreateRequest SourceContextRequest(string name, string sourceEntityType) => new()
    {
        Name = name,
        MatterTypeId = MatterTypeId,
        SourceEntityType = sourceEntityType,
        SourceRecordId = SourceProjectId,
    };

    /// <summary>No key on the create payload names the matter number, in any casing or padding.</summary>
    private static void AssertNoMatterNumberSent(Entity matter)
        => matter.Attributes.Keys
            .Should().NotContain(key => string.Equals(key.Trim(), "sprk_matternumber", StringComparison.OrdinalIgnoreCase),
                "numbering is left to a planned separate component; this path must never send sprk_matternumber");

    private static async Task AssertSourceDeniedAsync(
        OfficeQuickCreateTestWebAppFactory factory, HttpResponseMessage response, string reasonCode)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await ReadProblemAsync(response);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_009");
        problem.Should().ContainKey("reasonCode").WhoseValue.Should().Be(reasonCode);
        problem.Should().ContainKey("correlationId").WhoseValue.Should().NotBeNullOrEmpty();

        AssertNothingCreated(factory);
        factory.Entities.Verify(
            e => e.RetrieveAsync("sprk_project", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()), Times.Never);
        factory.FieldMappings.Verify(
            m => m.GetFieldMappingProfileWithRulesAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static void AssertNothingCreated(OfficeQuickCreateTestWebAppFactory factory)
        => factory.Entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);

    private static void ArrangeResolvedCaller(OfficeQuickCreateTestWebAppFactory factory)
        => factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(OwnerId.ToString("D")));

    private static void ArrangeMatterTypeExists(OfficeQuickCreateTestWebAppFactory factory)
        => factory.Entities
            .Setup(e => e.RetrieveAsync("sprk_mattertype_ref", MatterTypeId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_mattertype_ref", MatterTypeId));

    private static void ArrangeBusinessUnit(OfficeQuickCreateTestWebAppFactory factory)
    {
        factory.Entities
            .Setup(e => e.RetrieveAsync("systemuser", OwnerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", OwnerId) { ["businessunitid"] = new EntityReference("businessunit", BusinessUnitId) });

        factory.Entities
            .Setup(e => e.RetrieveAsync("businessunit", BusinessUnitId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("businessunit", BusinessUnitId)
            {
                ["sprk_searchindexname"] = "spaarke-files-index",
                ["sprk_ai_search_index"] = new EntityReference("sprk_aisearchindex", SearchIndexId),
            });
    }

    private static void ArrangeSourceRights(OfficeQuickCreateTestWebAppFactory factory, AccessRights rights)
        => factory.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "sprk_projects", SourceProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rights);

    private static void ArrangeProfile(OfficeQuickCreateTestWebAppFactory factory, params FieldMappingRuleEntity[] rules)
        => factory.FieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync("sprk_project", "sprk_matter", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "Project to Matter",
                SourceEntity = "sprk_project",
                TargetEntity = "sprk_matter",
                IsActive = true,
                Rules = rules.ToList(),
            });

    /// <summary>Arranges the source-record read and returns the column sets it was asked for (one entry per read).</summary>
    private static List<string[]> ArrangeSource(OfficeQuickCreateTestWebAppFactory factory, Entity source)
    {
        var requests = new List<string[]>();
        factory.Entities
            .Setup(e => e.RetrieveAsync("sprk_project", SourceProjectId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, string[], CancellationToken>((_, _, columns, _) => requests.Add(columns))
            .ReturnsAsync(source);
        return requests;
    }

    private static CreatedHolder CaptureCreate(OfficeQuickCreateTestWebAppFactory factory)
    {
        var holder = new CreatedHolder();
        factory.Entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => holder.Entity = entity)
            .ReturnsAsync(CreatedMatterId);
        return holder;
    }

    private static FieldMappingRuleEntity Rule(
        string name, string sourceField, int sourceType, string targetField, int targetType,
        int mappingType, int order, string? defaultValue = null, string? expression = null) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            SourceField = sourceField,
            SourceFieldType = sourceType,
            TargetField = targetField,
            TargetFieldType = targetType,
            MappingType = mappingType,
            ExecutionOrder = order,
            DefaultValue = defaultValue,
            Expression = expression,
            IsActive = true,
        };

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>The top-level string members of a ProblemDetails body (extensions are flattened to the root).</summary>
    private static async Task<Dictionary<string, string?>> ReadProblemAsync(HttpResponseMessage response)
    {
        var root = await ReadJsonAsync(response);
        return root.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString());
    }

    private sealed class CreatedHolder
    {
        public Entity? Entity { get; set; }
    }
}

/// <summary>
/// Office test host with the Dataverse boundary, the caller resolver and the OBO rights probe substituted, for the
/// quick-create contract. Inherits configuration, test auth and disabled rate limiting from
/// <see cref="OfficeTestWebAppFactory"/> without modifying it.
/// </summary>
public sealed class OfficeQuickCreateTestWebAppFactory : OfficeTestWebAppFactory
{
    public Mock<IGenericEntityService> Entities { get; } = new(MockBehavior.Loose);

    public Mock<IFieldMappingDataverseService> FieldMappings { get; } = new(MockBehavior.Loose);

    public Mock<ICallerSystemUserResolver> CallerResolver { get; } = new(MockBehavior.Loose);

    /// <summary>
    /// <see cref="CallerRecordAccessProbe.GetCallerRightsAsync"/> is the probe's designated virtual seam. Moq needs
    /// every constructor argument positionally; the last is the optional credential provider (absent in tests).
    /// </summary>
    public Mock<CallerRecordAccessProbe> AccessProbe { get; } = new(
        MockBehavior.Loose,
        new HttpClient(),
        new ConfigurationBuilder().Build(),
        NullLogger<CallerRecordAccessProbe>.Instance,
        null!);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IGenericEntityService>();
            services.AddSingleton(Entities.Object);

            services.RemoveAll<IFieldMappingDataverseService>();
            services.AddSingleton(FieldMappings.Object);

            services.RemoveAll<ICallerSystemUserResolver>();
            services.AddScoped(_ => CallerResolver.Object);

            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton(AccessProbe.Object);
        });
    }
}
