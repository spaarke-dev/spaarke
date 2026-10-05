using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.ServiceModel;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// HTTP contract of the "+ New" form's fields (spaarkeai-word-add-in-r1 task 100, owner UAT round 5 item 3 and
/// decision B): <c>practiceAreaId</c> (Matter), <c>projectTypeId</c> (Project), <c>description</c> and
/// <c>assignedToContactId</c> (all three) on <c>POST /api/office/quickcreate/{type}</c>, the Read gate on the contact,
/// and <c>GET /api/office/quickcreate/defaults</c> — the prefill, which must be the same contact the server assigns.
/// </summary>
/// <remarks>
/// The REAL pipeline runs (routing, OfficeAuthFilter, QuickCreateSourceAccessFilter, the handler, OfficeService,
/// RecordCreationService); only the Dataverse boundary, the caller resolver, the OBO rights probe and — where a test
/// needs the maker's link — the identity service are substituted (<see cref="OfficeQuickCreateTestWebAppFactory"/>).
/// Columns are the live ones (spaarkedev1, 2026-10-05).
/// </remarks>
[Trait("status", "new")]
public class OfficeQuickCreateFieldsContractTests
{
    private static readonly Guid CallerId = Guid.Parse("7d0e8a41-3b5c-4f2e-9a10-5c2b7e4f1100");
    private static readonly Guid MatterTypeId = Guid.Parse("b2c4e6a8-1d3f-4a5b-8c7d-9e0f1a2b3100");
    private static readonly Guid PracticeAreaId = Guid.Parse("b41377db-690e-f111-8342-7c1e520aa4df");
    private static readonly Guid ProjectTypeId = Guid.Parse("0ed9d8ac-b018-f111-8343-7ced8d1dc988");
    private static readonly Guid ChosenContact = Guid.Parse("cccccccc-1000-4000-8000-000000000100");
    private static readonly Guid MakersContact = Guid.Parse("eeeeeeee-1000-4000-8000-000000000100");
    private static readonly Guid CreatedId = Guid.Parse("00000000-0000-0000-0000-000000000100");
    private static readonly Guid BusinessUnitId = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a990100");

    /// <summary>Dataverse <c>0x80040217 ObjectDoesNotExist</c> as a signed int.</summary>
    private const int ObjectDoesNotExist = -2147220969;

    // ── Matter ──────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Matter_WithEveryField_WritesEachValueToItsLiveColumn()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeExists(factory, "sprk_mattertype_ref", MatterTypeId);
        ArrangeExists(factory, "sprk_practicearea_ref", PracticeAreaId);
        ArrangeContactRights(factory, ChosenContact, AccessRights.Read);
        var created = CaptureCreate(factory, "sprk_matter", "sprk_matternumber", "MAT-000100");

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new
        {
            name = "Acme v. Globex",
            description = "Filed from Word",
            matterTypeId = MatterTypeId,
            practiceAreaId = PracticeAreaId,
            assignedToContactId = ChosenContact,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        json.TryGetProperty("warnings", out _).Should().BeFalse("every value resolved");

        var matter = created.Single();
        matter.GetAttributeValue<string>("sprk_mattername").Should().Be("Acme v. Globex");
        matter.GetAttributeValue<string>("sprk_matterdescription").Should().Be("Filed from Word");
        matter.GetAttributeValue<EntityReference>("sprk_mattertype").Should().BeEquivalentTo(new EntityReference("sprk_mattertype_ref", MatterTypeId));
        matter.GetAttributeValue<EntityReference>("sprk_practicearea").Should().BeEquivalentTo(new EntityReference("sprk_practicearea_ref", PracticeAreaId));
        matter.GetAttributeValue<EntityReference>("sprk_assignedtointernal").Should().BeEquivalentTo(new EntityReference("contact", ChosenContact));
        matter.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(
            new EntityReference("team", RecordOwnershipResolverDouble.DefaultTeamId), "ownership (I-6) is unchanged");
        factory.AccessProbe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string>(), "contacts", ChosenContact, It.IsAny<CancellationToken>()),
            Times.Once, "the posted contact is authorized as the CALLER before the create");
    }

    [Fact]
    public async Task Post_Matter_WithUnknownPracticeArea_CreatesWithoutIt_AndWarns_NeverA400()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeExists(factory, "sprk_mattertype_ref", MatterTypeId);
        factory.Entities
            .Setup(e => e.RetrieveAsync("sprk_practicearea_ref", PracticeAreaId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(NotFound("sprk_practicearea_ref"));
        var created = CaptureCreate(factory, "sprk_matter", "sprk_matternumber", "MAT-000101");

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest
        {
            Name = "Ghost Area",
            MatterTypeId = MatterTypeId,
            PracticeAreaId = PracticeAreaId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().ContainSingle(w => w.Contains("practice area was not found"));
        created.Single().Contains("sprk_practicearea").Should().BeFalse("a dangling lookup would fault the whole create");
    }

    [Fact]
    public async Task Post_Matter_WithoutAnAssignee_AssignsTheMakersLinkedContact_TheServersStatedDefault()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeExists(factory, "sprk_mattertype_ref", MatterTypeId);
        using var host = WithMakersContact(factory, MakersContact);
        var created = CaptureCreate(factory, "sprk_matter", "sprk_matternumber", "MAT-000102");

        var response = await host.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest
        {
            Name = "Cleared Assignee",
            MatterTypeId = MatterTypeId,
            AssignedToContactId = Guid.Empty, // a cleared field — "the server's default"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Single().GetAttributeValue<EntityReference>("sprk_assignedtointernal")
            .Should().BeEquivalentTo(new EntityReference("contact", MakersContact), "the default is the maker's linked contact (task 152)");
        factory.AccessProbe.Verify(
            p => p.GetCallerRightsAsync(It.IsAny<string>(), "contacts", It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never, "no contact was posted, so there is none to authorize");
    }

    // ── Project ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Project_WithEveryField_WritesEachValueToItsLiveColumn()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeExists(factory, "sprk_projecttype_ref", ProjectTypeId);
        ArrangeContactRights(factory, ChosenContact, AccessRights.Read);
        var created = CaptureCreate(factory, "sprk_project", "sprk_projectnumber", "PRJ-000100");

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/quickcreate/project", new
        {
            name = "Due Diligence",
            description = "Workstream for the closing",
            projectTypeId = ProjectTypeId,
            assignedToContactId = ChosenContact,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await ReadJsonAsync(response);
        json.TryGetProperty("warnings", out _).Should().BeFalse();

        var project = created.Single();
        project.GetAttributeValue<string>("sprk_projectname").Should().Be("Due Diligence");
        project.GetAttributeValue<string>("sprk_projectdescription").Should().Be("Workstream for the closing");
        project.GetAttributeValue<EntityReference>("sprk_projecttype_ref").Should().BeEquivalentTo(new EntityReference("sprk_projecttype_ref", ProjectTypeId));
        project.GetAttributeValue<EntityReference>("sprk_assignedtointernal").Should().BeEquivalentTo(new EntityReference("contact", ChosenContact));
        project.Contains("sprk_practicearea").Should().BeFalse("a project's form has no practice area");
    }

    [Fact]
    public async Task Post_Project_WithAMatterOnlyField_IgnoresIt()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        var created = CaptureCreate(factory, "sprk_project", "sprk_projectnumber", "PRJ-000101");

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/quickcreate/project", new QuickCreateRequest
        {
            Name = "No Type Project",
            PracticeAreaId = PracticeAreaId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().BeNull("Project Type is optional and a matter-only field is ignored, so nothing is missing");
        created.Single().Contains("sprk_practicearea").Should().BeFalse();
        created.Single().Contains("sprk_projecttype_ref").Should().BeFalse();
    }

    // ── Invoice ─────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Invoice_WithEveryField_WritesNameDescriptionAndAssignedTo1()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeContactRights(factory, ChosenContact, AccessRights.Read);
        var created = CaptureCreate(factory, numberEntity: null, numberAttribute: null, number: null);

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/quickcreate/invoice", new
        {
            name = "INV-2026-0100",
            description = "Outside counsel, September",
            assignedToContactId = ChosenContact,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var invoice = created.Single();
        invoice.LogicalName.Should().Be("sprk_invoice");
        invoice.GetAttributeValue<string>("sprk_name").Should().Be("INV-2026-0100");
        invoice.GetAttributeValue<string>("sprk_description").Should().Be("Outside counsel, September");
        invoice.GetAttributeValue<EntityReference>("sprk_assignedto1").Should().BeEquivalentTo(new EntityReference("contact", ChosenContact));
        invoice.Contains("sprk_assignedtointernal").Should().BeFalse("sprk_invoice has no such column");
    }

    [Fact]
    public async Task Post_Invoice_WithoutAnAssignee_IsLeftUnassigned_TheInvoiceHasNoServerDefault()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        using var host = WithMakersContact(factory, MakersContact);
        var created = CaptureCreate(factory, numberEntity: null, numberAttribute: null, number: null);

        var response = await host.CreateClient().PostAsJsonAsync(
            "/api/office/quickcreate/invoice", new QuickCreateRequest { Name = "INV-2026-0101" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Single().Contains("sprk_assignedto1").Should().BeFalse("an invoice's stated default is no assignee");
        created.Single().Contains("sprk_description").Should().BeFalse("a blank description is not written");
    }

    // ── The assignee gate: Read on the contact, one body for every refusal ──────────────────────────────

    public static TheoryData<string> AssigneeTypes => new() { "matter", "project", "invoice" };

    [Theory]
    [MemberData(nameof(AssigneeTypes))]
    public async Task Post_WithAContactTheCallerCannotRead_Returns403_AndCreatesNothing(string type)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeContactRights(factory, ChosenContact, AccessRights.AppendTo); // rights, but not Read
        CaptureCreate(factory, numberEntity: null, numberAttribute: null, number: null);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "Not Mine", AssignedToContactId = ChosenContact });

        await AssertAssigneeDeniedAsync(factory, response);
    }

    [Fact]
    public async Task Post_DeniedAndNonexistentAndUncheckableContacts_AreIndistinguishable()
    {
        // The probe answers None for a contact that does not exist AND for one the caller may not read (Dataverse
        // reports both as 404 under OBO), and this gate turns a thrown check into the same answer. Every caller-visible
        // field but the correlation id must match, or the route is a contact-existence oracle.
        var unreadable = await DeniedBodyAsync(f => ArrangeContactRights(f, ChosenContact, AccessRights.AppendTo));
        var nonexistent = await DeniedBodyAsync(f => ArrangeContactRights(f, ChosenContact, AccessRights.None));
        var unchecked_ = await DeniedBodyAsync(f => f.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "contacts", ChosenContact, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("dataverse unreachable")));

        nonexistent.Should().BeEquivalentTo(unreadable);
        unchecked_.Should().BeEquivalentTo(unreadable);
        unreadable["reasonCode"].Should().Be(QuickCreateSourceAccessFilter.AssigneeDeniedReasonCode);
        unreadable["detail"].Should().Be(QuickCreateSourceAccessFilter.AssigneeDeniedDetail);
        unreadable["detail"].Should().NotContain(ChosenContact.ToString("D"), "the id is logged, never echoed");
    }

    // ── GET /quickcreate/defaults: the prefill is the server's own default ──────────────────────────────

    [Fact]
    public async Task GetDefaults_ReturnsTheCallersLinkedContact_TheSameOneTheCreateAssigns()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        using var host = WithMakersContact(factory, MakersContact);
        factory.Entities
            .Setup(e => e.RetrieveAsync("contact", MakersContact, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("contact", MakersContact) { ["fullname"] = "Ralph Schroeder", ["emailaddress1"] = "ralph@spaarke.test" });

        var response = await host.CreateClient().GetAsync("/api/office/quickcreate/defaults");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<QuickCreateDefaultsResponse>();
        body!.AssignedTo.Should().BeEquivalentTo(new QuickCreateContactOption
        {
            Id = MakersContact,
            Name = "Ralph Schroeder",
            Email = "ralph@spaarke.test",
        });
    }

    [Fact]
    public async Task GetDefaults_WhenTheCallerHasNoLinkedContact_ReturnsNoPrefill()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        using var host = WithMakersContact(factory, contactId: null);

        var response = await host.CreateClient().GetAsync("/api/office/quickcreate/defaults");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("assignedTo").ValueKind.Should().Be(JsonValueKind.Null);
        factory.Entities.Verify(
            e => e.RetrieveAsync("contact", It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never, "never an email match — no link, no prefill");
    }

    [Fact]
    public async Task GetDefaults_WhenTheCallerIsUnresolved_ReturnsNoPrefill_NotARefusal()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-matching-systemuser"));

        var response = await factory.CreateClient().GetAsync("/api/office/quickcreate/defaults");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadJsonAsync(response)).GetProperty("assignedTo").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetDefaults_WhenUnauthenticated_Returns401()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Unauthenticated", "true");

        var response = await client.GetAsync("/api/office/quickcreate/defaults");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<Dictionary<string, string?>> DeniedBodyAsync(Action<OfficeQuickCreateTestWebAppFactory> arrangeProbe)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        arrangeProbe(factory);
        CaptureCreate(factory, numberEntity: null, numberAttribute: null, number: null);

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/quickcreate/matter", new QuickCreateRequest { Name = "Probe", AssignedToContactId = ChosenContact });

        await AssertAssigneeDeniedAsync(factory, response);
        var problem = await ReadProblemAsync(response);
        problem.Remove("correlationId");
        problem.Remove("traceId");
        return problem;
    }

    private static async Task AssertAssigneeDeniedAsync(OfficeQuickCreateTestWebAppFactory factory, HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_009");
        problem.Should().ContainKey("reasonCode").WhoseValue.Should().Be(QuickCreateSourceAccessFilter.AssigneeDeniedReasonCode);
        factory.Entities.Verify(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Never,
            "a refused assignee writes nothing");
    }

    /// <summary>
    /// A resolved caller whose business unit answers (so no "business-unit defaults" warning muddies a test that
    /// asserts the create had nothing to warn about).
    /// </summary>
    private static void ArrangeResolvedCaller(OfficeQuickCreateTestWebAppFactory factory)
    {
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerId.ToString("D")));
        factory.Entities
            .Setup(e => e.RetrieveAsync("systemuser", CallerId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("systemuser", CallerId) { ["businessunitid"] = new EntityReference("businessunit", BusinessUnitId) });
        factory.Entities
            .Setup(e => e.RetrieveAsync("businessunit", BusinessUnitId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("businessunit", BusinessUnitId) { ["sprk_searchindexname"] = "spaarke-files-index" });
    }

    private static void ArrangeExists(OfficeQuickCreateTestWebAppFactory factory, string entity, Guid id)
        => factory.Entities
            .Setup(e => e.RetrieveAsync(entity, id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(entity, id));

    private static void ArrangeContactRights(OfficeQuickCreateTestWebAppFactory factory, Guid contactId, AccessRights rights)
        => factory.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "contacts", contactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rights);

    /// <summary>
    /// A host over <paramref name="factory"/> whose identity service gives every user <paramref name="contactId"/> as
    /// their linked contact (null = no link). The factory's other doubles stay registered.
    /// </summary>
    private static WebApplicationFactory<Program> WithMakersContact(OfficeQuickCreateTestWebAppFactory factory, Guid? contactId)
    {
        var identity = IdentityNormalizationFixtures.WithContact(contactId);
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIdentityNormalizationService>();
            services.AddSingleton(identity.Object);
        }));
    }

    /// <summary>Captures every create payload; arranges the number read-back for numbered entities (task 076).</summary>
    private static List<Entity> CaptureCreate(
        OfficeQuickCreateTestWebAppFactory factory, string? numberEntity, string? numberAttribute, string? number)
    {
        var created = new List<Entity>();
        factory.Entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => created.Add(entity))
            .ReturnsAsync(CreatedId);

        if (numberEntity is not null && numberAttribute is not null)
        {
            factory.Entities
                .Setup(e => e.RetrieveAsync(
                    numberEntity, CreatedId,
                    It.Is<string[]>(columns => columns.Length == 1 && columns[0] == numberAttribute),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Entity(numberEntity, CreatedId) { [numberAttribute] = number });
        }

        return created;
    }

    private static FaultException<OrganizationServiceFault> NotFound(string entity) => new(
        new OrganizationServiceFault { ErrorCode = ObjectDoesNotExist, Message = $"{entity} Does Not Exist" },
        new FaultReason("Does Not Exist"));

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task<Dictionary<string, string?>> ReadProblemAsync(HttpResponseMessage response)
    {
        var root = await ReadJsonAsync(response);
        return root.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString());
    }
}
