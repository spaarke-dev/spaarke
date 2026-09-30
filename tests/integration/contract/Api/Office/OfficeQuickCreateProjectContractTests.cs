using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
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
/// HTTP contract for <c>POST /api/office/quickcreate/project</c> after spaarkeai-word-add-in-r1 task 031 (FR-13):
/// the Project path runs through <c>RecordCreationService</c> — a load-bearing owner, business-unit defaults and the
/// Field Mapping Framework — behind <c>QuickCreateSourceAccessFilter</c>.
/// </summary>
/// <remarks>
/// <para>🔴 The load-bearing assertion in this file is that <b><c>sprk_projectnumber</c> is NEVER written</b> — not
/// directly and not through a field-mapping rule of any type or casing. It is the <c>sprk_project</c> PRIMARY NAME
/// attribute, and the owner decided on 2026-09-17 that numbering belongs to a separate on-create component, so a
/// pane-created Project deliberately has a blank display name until that ships. See
/// <c>projects/spaarkeai-word-add-in-r1/notes/031-project-semantics.md</c>. A future change that starts populating
/// the number here must delete these assertions consciously, not discover them.</para>
/// <para>Deliberately a SEPARATE file from <c>OfficeQuickCreateContractTests</c> so task 030's Matter contract stays
/// byte-for-byte untouched (task 031 AC7). It reuses that file's
/// <see cref="OfficeQuickCreateTestWebAppFactory"/> host: the real pipeline runs end to end (routing, OfficeAuthFilter,
/// the source-access filter, the handler, OfficeService, RecordCreationService, CreateTimeFieldMapping) and only the
/// Dataverse boundary, the caller → systemuser resolver and the OBO rights probe are substituted. "No row created" is
/// asserted as "<see cref="IGenericEntityService.CreateAsync"/> was never called".</para>
/// </remarks>
public class OfficeQuickCreateProjectContractTests
{
    private const string Route = "/api/office/quickcreate/project";

    private static readonly Guid OwnerId = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1a01");
    private static readonly Guid BusinessUnitId = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a99d1d7");
    private static readonly Guid SearchIndexId = Guid.Parse("fdcc183b-8b71-f111-ab0d-7ced8ddc4cc6");
    private static readonly Guid SourceProjectId = Guid.Parse("1a2b3c4d-5e6f-4a7b-8c9d-0e1f2a3b4c01");
    private static readonly Guid AccountId = Guid.Parse("9f8e7d6c-5b4a-4c3d-8e2f-1a0b9c8d7e01");
    private static readonly Guid MatterTypeId = Guid.Parse("b2c4e6a8-1d3f-4a5b-8c7d-9e0f1a2b3c01");
    private static readonly Guid CreatedProjectId = Guid.Parse("00000000-0000-0000-0000-0000000c0de1");

    // ── Happy path ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Project_Returns201_OwnedByCaller_WithBusinessUnitDefaults_AndNeverANumber()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        var created = CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new
        {
            name = "  Acme Migration  ",
            description = "From Word",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        json.GetProperty("id").GetString().Should().Be(CreatedProjectId.ToString("D"));
        json.GetProperty("entityType").GetString().Should().Be("Project");
        json.GetProperty("logicalName").GetString().Should().Be("sprk_project");
        json.GetProperty("name").GetString().Should().Be("Acme Migration");
        json.TryGetProperty("warnings", out _).Should()
            .BeFalse("a Project create has no optional type to warn about — unlike Matter, a clean create is silent");

        var project = created.Entity!;
        project.LogicalName.Should().Be("sprk_project");
        project.GetAttributeValue<string>("sprk_projectname").Should().Be("Acme Migration");
        project.GetAttributeValue<string>("sprk_projectdescription").Should().Be("From Word");
        project.GetAttributeValue<EntityReference>("ownerid").Should()
            .BeEquivalentTo(new EntityReference("systemuser", OwnerId));
        project.GetAttributeValue<string>("sprk_searchindexname").Should().Be("spaarke-files-index");
        project.GetAttributeValue<EntityReference>("sprk_ai_search_index").Id.Should().Be(SearchIndexId);
        AssertNoProjectNumberSent(project);
        project.Contains("sprk_containerid").Should()
            .BeFalse("the container is derived server-side, never stamped on create (task 076 W1)");
    }

    [Fact]
    public async Task Post_Project_IgnoresMatterTypeId_AndNeverReadsAMatterType()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        var created = CaptureCreate(factory);

        // matterTypeId is a Matter-only field on the shared request. The Project path has no type lookup in r1, so a
        // stray value must be inert — not copied onto the project, and not the cause of a Dataverse read.
        var response = await factory.CreateClient().PostAsJsonAsync(
            Route, new QuickCreateRequest { Name = "Typed By Mistake", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Entity!.Contains("sprk_mattertype").Should().BeFalse();
        factory.Entities.Verify(
            e => e.RetrieveAsync("sprk_mattertype_ref", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "the Project path has no matter-type concept");
    }

    [Fact]
    public async Task Post_Project_WhenBusinessUnitDefaultsCannotBeRead_StillCreates_AndWarns()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        factory.Entities
            .Setup(e => e.RetrieveAsync("systemuser", OwnerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse slow"));
        var created = CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            Route, new QuickCreateRequest { Name = "No BU Defaults" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, "index-routing hints are never worth failing a create");
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().ContainSingle(w => w.Contains("Business-unit search defaults could not be read"));
        // Pins the SECOND sentence too: ApplyBusinessUnitDefaultsAsync is shared with the Matter path, and its noun
        // was hardcoded to "matter" until task 031's Step 9.5 review caught it leaking onto Project creates.
        body.Warnings!.Single().Should().Contain("this project falls back")
            .And.NotContain("matter", "the shared BU helper must not leak the Matter noun onto the Project path");
        created.Entity!.Contains("sprk_searchindexname").Should().BeFalse();
        created.Entity.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OwnerId);
    }

    // ── Field mapping ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Project_WithSourceContextAndProfile_AppliesEveryRule_ButNeverTheProtectedFields()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        ArrangeSourceRights(factory, AccessRights.Read);
        var created = CaptureCreate(factory);

        // Source and target are both sprk_project: same-entity mapping is supported by the framework (no
        // source == target guard), and it keeps the access-filter's entity-set mapping on a path task 030 proved.
        ArrangeProfile(factory,
            Rule("copy-text", "sprk_clientreference", 0, "sprk_clientreference", 0, mappingType: 0, order: 1),
            Rule("copy-lookup", "sprk_client", 1, "sprk_client", 1, mappingType: 0, order: 2),
            Rule("default-option", "", 0, "sprk_billingstatus", 2, mappingType: 1, order: 3, defaultValue: "3"),
            Rule("template", "", 0, "sprk_projectnotes", 6, mappingType: 3, order: 4,
                expression: "Branched from {sprk_projectname} ({sprk_notonsource})"),
            // The four shapes a rule could use to reach the protected number, including a padded, mis-cased target.
            Rule("number-default", "", 0, "sprk_projectnumber", 0, mappingType: 1, order: 5, defaultValue: "HACK-000001"),
            Rule("number-copy", "sprk_projectnumber", 0, "sprk_projectnumber", 0, mappingType: 0, order: 6),
            Rule("number-template-mis-cased", "", 0, "  SPRK_ProjectNumber ", 0, mappingType: 3, order: 7,
                expression: "X-{sprk_projectnumber}"),
            Rule("number-concat", "", 0, "sprk_projectnumber", 0, mappingType: 2, order: 8, expression: "{sprk_projectnumber}"),
            Rule("owner-copy", "sprk_client", 1, "ownerid", 1, mappingType: 0, order: 9),
            Rule("container-default", "", 0, "sprk_containerid", 0, mappingType: 1, order: 10, defaultValue: "b!shared"));

        var requestedColumns = ArrangeSource(factory, new Entity("sprk_project", SourceProjectId)
        {
            ["sprk_clientreference"] = "ACME-REF",
            ["sprk_client"] = new EntityReference("account", AccountId),
            ["sprk_projectname"] = "Acme Parent Project",
            ["sprk_projectnumber"] = "PRJ-000123",
        });

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "Acme Phase 2",
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();

        // One source read spanning every Copy field and every placeholder — never one read per rule.
        requestedColumns.Single().Should().BeEquivalentTo(
            "sprk_clientreference", "sprk_client", "sprk_projectname", "sprk_notonsource", "sprk_projectnumber");

        var project = created.Entity!;
        project.GetAttributeValue<string>("sprk_clientreference").Should().Be("ACME-REF");
        project.GetAttributeValue<EntityReference>("sprk_client").Should()
            .BeEquivalentTo(new EntityReference("account", AccountId));
        project.GetAttributeValue<OptionSetValue>("sprk_billingstatus").Value.Should().Be(3);
        project.GetAttributeValue<string>("sprk_projectnotes").Should().Be("Branched from Acme Parent Project ()");
        project.GetAttributeValue<string>("sprk_projectname").Should().Be("Acme Phase 2");

        // The protected attributes: never sent on create, whatever the profile says — Default, Copy, Template,
        // Concat, and a mis-cased, padded target alike.
        AssertNoProjectNumberSent(project);
        project.GetAttributeValue<EntityReference>("ownerid").Should()
            .BeEquivalentTo(new EntityReference("systemuser", OwnerId));
        project.Contains("sprk_containerid").Should().BeFalse();

        body!.Warnings.Should().NotBeNull();
        body.Warnings!.Count(w => w.Contains("sprk_projectnumber", StringComparison.OrdinalIgnoreCase)).Should().Be(4);
        body.Warnings.Should().Contain(w => w.Contains("ownerid"))
            .And.Contain(w => w.Contains("sprk_containerid"))
            .And.Contain(w => w.Contains("sprk_notonsource"));
    }

    [Fact]
    public async Task Post_Project_WithSourceContextButNoProfile_IsAGracefulNoOp_AndStillCreatesWithOwner()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        ArrangeSourceRights(factory, AccessRights.Read);
        var created = CaptureCreate(factory);
        factory.FieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FieldMappingProfileEntity?)null);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "No Profile Project",
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().BeNull("a missing profile is a silent no-op, not a failure");
        created.Entity!.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(OwnerId);
        created.Entity.GetAttributeValue<string>("sprk_projectname").Should().Be("No Profile Project");
        factory.Entities.Verify(
            e => e.RetrieveAsync("sprk_project", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "with no profile there is nothing to read from the source record");
    }

    [Fact]
    public async Task Post_Project_WhenMappingBlanksTheName_KeepsTheRequestedName()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeBusinessUnit(factory);
        ArrangeSourceRights(factory, AccessRights.Read);
        var created = CaptureCreate(factory);
        ArrangeProfile(factory,
            Rule("blank-name", "", 0, "sprk_projectname", 0, mappingType: 3, order: 1, expression: "{sprk_notonsource}"));
        ArrangeSource(factory, new Entity("sprk_project", SourceProjectId));

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "Requested Name",
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Name.Should().Be("Requested Name");
        created.Entity!.GetAttributeValue<string>("sprk_projectname").Should().Be("Requested Name");
        body.Warnings.Should().Contain(w => w.Contains("name you entered was kept"));
    }

    // ── Negative: authentication / authorization ────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Project_WhenUnauthenticated_Returns401_AndCreatesNothing()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        CaptureCreate(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        var response = await client.PostAsJsonAsync(Route, new QuickCreateRequest { Name = "Anon Project" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertNothingCreated(factory);
    }

    [Fact]
    public async Task Post_Project_WhenCallerHasNoDataverseUser_Returns403_AndCreatesNothing()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));
        CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(
            Route, new QuickCreateRequest { Name = "Orphan Project" });

        // Task 031 decision: the owner is LOAD-BEARING for Project too (task 030 left this to 031). An unresolved
        // caller is refused rather than silently producing an app-owned record — the defect FR-13 exists to fix.
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ReadProblemAsync(response)).Should().ContainKey("errorCode").WhoseValue.Should().Be("owner_unresolved");
        AssertNothingCreated(factory);
    }

    [Fact]
    public async Task Post_Project_WhenCallerCannotReadTheSourceRecord_Returns403_ReadsNothing_AndCreatesNothing()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeSourceRights(factory, AccessRights.AppendTo); // holds rights, but not Read
        CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest
        {
            Name = "Someone Else's Source",
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_009");
        problem.Should().ContainKey("reasonCode").WhoseValue.Should().Be("insufficient_rights");
        problem.Should().ContainKey("correlationId").WhoseValue.Should().NotBeNullOrEmpty();

        AssertNothingCreated(factory);
        factory.FieldMappings.Verify(
            m => m.GetFieldMappingProfileWithRulesAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never, "the filter runs before the creation service, so no mapping config is read");
    }

    // ── Negative: validation ────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_Project_WithBlankName_Returns400ProblemDetails_AndCreatesNothing(string name)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        CaptureCreate(factory);

        var response = await factory.CreateClient().PostAsJsonAsync(Route, new QuickCreateRequest { Name = name });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a blank name is a 400, never an unhandled exception");
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        (await ReadProblemAsync(response)).Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_007");
        AssertNothingCreated(factory);
    }

    [Fact]
    public async Task Post_Project_WithHalfASourceContext_Returns400_AndCreatesNothing()
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
    //
    // Deliberately local rather than shared with OfficeQuickCreateContractTests: extracting them would edit task
    // 030's file, which AC7 requires to pass unmodified. The duplication is a few lines of arrangement.

    /// <summary>No key on the create payload names the project number, in any casing or padding.</summary>
    private static void AssertNoProjectNumberSent(Entity project)
        => project.Attributes.Keys
            .Should().NotContain(key => string.Equals(key.Trim(), "sprk_projectnumber", StringComparison.OrdinalIgnoreCase),
                "numbering is left to a planned separate on-create component; this path must never send sprk_projectnumber");

    private static void AssertNothingCreated(OfficeQuickCreateTestWebAppFactory factory)
        => factory.Entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never);

    private static void ArrangeResolvedCaller(OfficeQuickCreateTestWebAppFactory factory)
        => factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(OwnerId.ToString("D")));

    private static void ArrangeBusinessUnit(OfficeQuickCreateTestWebAppFactory factory)
    {
        factory.Entities
            .Setup(e => e.RetrieveAsync("systemuser", OwnerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", OwnerId)
            {
                ["businessunitid"] = new EntityReference("businessunit", BusinessUnitId),
            });

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
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync("sprk_project", "sprk_project", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "Project to Project",
                SourceEntity = "sprk_project",
                TargetEntity = "sprk_project",
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
            .ReturnsAsync(CreatedProjectId);
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
