using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Api.Office;

/// <summary>
/// Field mapping task 018b-1c: the behaviour the Office quick-create adapter (<c>OfficeService.QuickCreateViaCreationServiceAsync</c>)
/// adds on the unified create, over HTTP — the Assigned To contact checked as the caller (UAC-r2 condition (b) item 1), the
/// maker's-contact default (018a K-7), the "No matter type was supplied" warning (G1), no bearer token (G2), and the request
/// contract that keeps G4 condition 1 unreachable.
/// </summary>
/// <remarks>
/// The real pipeline runs end to end on <see cref="OfficeQuickCreateTestWebAppFactory"/>: the caller's Dataverse is the
/// closed-world <see cref="OfficeCallerDataverseDouble"/>, the create is captured by <see cref="RecordCreationWriteCapture"/>.
/// The secure-source G4 pins (a secure matter, isolation) need the provisioning host and live in
/// <c>SecureRootCreateRouteTests</c>.
/// </remarks>
[Trait("status", "task-018b-field-mapping")]
public class OfficeQuickCreateAdapterContractTests
{
    private static readonly Guid CallerId = OfficeQuickCreateWorld.FieldsCaller;
    private static readonly Guid BusinessUnitId = OfficeQuickCreateWorld.FieldsBusinessUnit;
    private static readonly Guid MatterTypeId = OfficeQuickCreateWorld.FieldsMatterType;
    private static readonly Guid ChosenContact = OfficeQuickCreateWorld.ChosenContact;
    private static readonly Guid MakersContact = OfficeQuickCreateWorld.MakersContact;
    private static readonly Guid SourceProjectId = OfficeQuickCreateWorld.ContractSourceProject;

    private const string AssignedTo = "sprk_assignedtointernal";

    /// <summary>018a K-7: the service's words for a post-mapping default it did not write.</summary>
    private const string DefaultDroppedPrefix = "The default value for 'sprk_assignedtointernal' was not written";

    /// <summary>018a K-3: the service's words for an optional reference it did not write (not found, or not permitted).</summary>
    private const string UnusableMatterTypeWarning =
        "The value you chose for 'sprk_mattertype' was not written: it was not found, or you do not have permission to use it; "
        + "the matter was created without it.";

    public static TheoryData<string> MatterAndProject => new() { "matter", "project" };

    // ── Assigned To: the contact the request names is checked as the caller (UAC-r2 condition (b) item 1) ──────────

    [Theory]
    [MemberData(nameof(MatterAndProject))]
    public async Task Post_WithANamedContactTheCallerCannotAppendTo_Returns403AssigneeInaccessible_InTheFiltersShape_WritesNothing_NeverNotFound(string type)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeContactRead(factory, ChosenContact); // the route's Read gate passes: the refusal is the service's AppendTo
        factory.CallerDataverse.NoAppendTo.Add(("contact", ChosenContact));
        var created = CaptureCreate(factory, type);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "Not Mine To Assign", AssignedToContactId = ChosenContact });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        var problem = await ReadProblemAsync(response);
        problem.Should().ContainKey("errorCode").WhoseValue.Should().Be("OFFICE_009", "the filter's code, not a second shape");
        problem.Should().ContainKey("reasonCode").WhoseValue.Should().Be(QuickCreateSourceAccessFilter.AssigneeDeniedReasonCode,
            "the refusal names the Assigned To field (G5: the service names the refused column)");
        problem.Should().ContainKey("title").WhoseValue.Should().Be("Forbidden");
        problem.Should().ContainKey("detail").WhoseValue.Should().Be(QuickCreateSourceAccessFilter.AssigneeDeniedDetail)
            .And.NotContainEquivalentOf("not found", "it never reads as 'matter not found'");
        created.Attempts.Should().BeEmpty("a refused assignee writes nothing");
        factory.CallerDataverse.Requests.Should().Contain(r =>
                r.Contains("RetrievePrincipalAccess", StringComparison.Ordinal) && r.Contains($"contacts({ChosenContact:D})", StringComparison.Ordinal),
            "AppendTo on the contact is asked AS THE CALLER");

        // The same refusal, the other way in: a contact the caller cannot READ is the route filter's. The two bodies are identical
        // but for the per-request ids — one shape for the pane to key on.
        using var gated = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(gated);
        gated.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "contacts", ChosenContact, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.AppendTo); // rights, but not Read
        CaptureCreate(gated, type);
        var filtered = await gated.CreateClient().PostAsJsonAsync(
            $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "Not Mine To Read", AssignedToContactId = ChosenContact });

        filtered.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var filterProblem = await ReadProblemAsync(filtered);
        foreach (var perRequest in new[] { "correlationId", "traceId" })
        {
            problem.Remove(perRequest);
            filterProblem.Remove(perRequest);
        }

        problem.Should().BeEquivalentTo(filterProblem, "the service's Assigned To refusal and the filter's are one shape");
    }

    [Theory]
    [MemberData(nameof(MatterAndProject))]
    public async Task Post_WithAReadableAppendableNamedContact_Returns201_AndWritesTheAssignedToBind(string type)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeContactRead(factory, ChosenContact);
        var created = CaptureCreate(factory, type);

        var response = await factory.CreateClient().PostAsJsonAsync($"/api/office/quickcreate/{type}", new QuickCreateRequest
        {
            Name = "Assigned By Name",
            MatterTypeId = MatterTypeId,
            AssignedToContactId = ChosenContact,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Writes.Should().ContainSingle();
        created.Writes[0].Fields.Should().ContainKey("sprk_AssignedToInternal@odata.bind");
        Text(created.Writes[0].Fields["sprk_AssignedToInternal@odata.bind"]).Should().Be($"/contacts({ChosenContact:D})",
            "the named contact is bound in the caller's payload");
        created.Entity!.GetAttributeValue<EntityReference>(AssignedTo).Should().BeEquivalentTo(new EntityReference("contact", ChosenContact));
    }

    // ── The maker's-contact default (018a K-7; UAC-r2 018a review) ─────────────────────────────────────────────────

    [Theory]
    [MemberData(nameof(MatterAndProject))]
    public async Task Post_WhenTheCallerCannotAppendToTheMakersContact_DropsTheDefault_Returns201_WarnsDefaultDropped_AndWritesOnce(string type)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        factory.CallerDataverse.NoAppendTo.Add(("contact", MakersContact));
        using var host = WithMakersContact(factory, MakersContact);
        var created = CaptureCreate(factory, type);

        var response = await host.CreateClient().PostAsJsonAsync(
            $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "Default Not Usable", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Created, "a default the caller cannot use is dropped, never refused");
        var body = await response.Content.ReadFromJsonAsync<QuickCreateResponse>();
        body!.Warnings.Should().ContainSingle(w => w.StartsWith(DefaultDroppedPrefix, StringComparison.Ordinal),
            "record_creation.default_value_dropped reaches the user");
        created.Entity!.Contains(AssignedTo).Should().BeFalse("the dropped default is not written");
        created.Attempts.Should().ContainSingle("exactly one write: nothing writes Assigned To after the drop");
        created.Writes.Single().Fields.Keys.Should().NotContain(k => k.StartsWith("sprk_AssignedToInternal", StringComparison.OrdinalIgnoreCase));
        factory.Entities.Invocations.Should().NotContain(i => i.Method.Name.StartsWith("Update", StringComparison.Ordinal)
                                                              || i.Method.Name.StartsWith("Create", StringComparison.Ordinal),
            "nothing writes Assigned To app-only after a drop");
    }

    [Theory]
    [MemberData(nameof(MatterAndProject))]
    public async Task Post_WhenTheCallerCanUseTheMakersContact_WritesItAsTheDefault_WithoutAWarning(string type)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        using var host = WithMakersContact(factory, MakersContact);
        var created = CaptureCreate(factory, type);

        var response = await host.CreateClient().PostAsJsonAsync(
            $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "Default Usable", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<QuickCreateResponse>())!.Warnings.Should().BeNull();
        created.Entity!.GetAttributeValue<EntityReference>(AssignedTo).Should().BeEquivalentTo(new EntityReference("contact", MakersContact));
        created.Attempts.Should().ContainSingle();
    }

    [Theory]
    [MemberData(nameof(MatterAndProject))]
    public async Task Post_WhenTheMakerHasNoLinkedContact_LeavesAssignedToBlank_WithoutAWarning(string type)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        using var host = WithMakersContact(factory, contactId: null);
        var created = CaptureCreate(factory, type);

        var response = await host.CreateClient().PostAsJsonAsync(
            $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "No Maker Contact", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<QuickCreateResponse>())!.Warnings.Should().BeNull("a missing link is logged, not warned");
        created.Entity!.Contains(AssignedTo).Should().BeFalse("never an email match — no link, no default");
    }

    // ── G1: "No matter type was supplied" ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Post_Matter_NameOnly_CarriesTheNoMatterTypeWarning()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        CaptureCreate(factory, "matter");

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest { Name = "Name Only" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<QuickCreateResponse>())!.Warnings
            .Should().Equal(new[] { OfficeService.NoMatterTypeWarning });
    }

    [Fact]
    public async Task Post_Matter_WithTheTypeFilledByFieldMapping_DoesNotCarryTheNoMatterTypeWarning()
    {
        // The service names the type by its bind key in navigation casing (sprk_MatterType@odata.bind): the adapter's
        // "was it written" test must be case-insensitive and see through the bind suffix.
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeSourceRead(factory, new Entity("sprk_project", SourceProjectId)
        {
            ["sprk_mattertype"] = new EntityReference("sprk_mattertype_ref", MatterTypeId),
        });
        factory.FieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync("sprk_project", "sprk_matter", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Profile("sprk_project", "sprk_matter",
                Rule("copy-type", "sprk_mattertype", 1, "sprk_mattertype", 1, mappingType: 0)));
        var created = CaptureCreate(factory, "matter");

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/quickcreate/matter", new QuickCreateRequest
        {
            Name = "Mapped Type",
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Writes.Single().Fields.Should().ContainKey("sprk_MatterType@odata.bind", "the precondition: the mapped type is written");
        ((await response.Content.ReadFromJsonAsync<QuickCreateResponse>())!.Warnings ?? Array.Empty<string>())
            .Should().NotContain(OfficeService.NoMatterTypeWarning);
    }

    [Fact]
    public async Task Post_Matter_WithAnUnknownRequestedType_GetsOnlyTheReferenceDropWarning_TheNoTypeWarningIsSuppressed()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        factory.CallerDataverse.Missing.Add(("sprk_mattertype_ref", MatterTypeId));
        var created = CaptureCreate(factory, "matter");

        var response = await factory.CreateClient().PostAsJsonAsync(
            "/api/office/quickcreate/matter", new QuickCreateRequest { Name = "Ghost Type", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await response.Content.ReadFromJsonAsync<QuickCreateResponse>())!.Warnings
            .Should().Equal(new[] { UnusableMatterTypeWarning }, "the type the user chose already has its own warning");
        created.Entity!.Contains("sprk_mattertype").Should().BeFalse();
    }

    // ── G2: no bearer token (UAC-r2 condition (b) item 2) ────────────────────────────────────────────────────────

    /// <summary>
    /// The caller's Dataverse answers WhoAmI as the real <see cref="IDataverseUserClient"/> does with no bearer token
    /// (<c>UserContextRequired</c>; pinned at the client by <c>ConfidentialClientMigrationSeamTests.DataverseUserClient_WithNoBearerToken_ReturnsUserContextRequired_BeforeAnyExchange</c>)
    /// or with no usable OBO: 500 <c>caller_rights_unverifiable</c>, fail closed. Must not fire: a caller Dataverse does not
    /// know (<c>AccessDenied</c>) stays 403 <c>owner_unresolved</c>.
    /// </summary>
    [Theory]
    [InlineData("matter", DataverseUserClientErrorCodes.UserContextRequired, 0, HttpStatusCode.InternalServerError, "caller_rights_unverifiable")]
    [InlineData("matter", DataverseUserClientErrorCodes.OboNotConfigured, 0, HttpStatusCode.InternalServerError, "caller_rights_unverifiable")]
    [InlineData("matter", DataverseUserClientErrorCodes.OboExchangeFailed, 0, HttpStatusCode.InternalServerError, "caller_rights_unverifiable")]
    [InlineData("matter", DataverseUserClientErrorCodes.AccessDenied, 403, HttpStatusCode.Forbidden, "owner_unresolved")]
    [InlineData("project", DataverseUserClientErrorCodes.AccessDenied, 403, HttpStatusCode.Forbidden, "owner_unresolved")]
    public async Task Post_WhenWhoAmIFails_MapsNoTokenTo500CallerRightsUnverifiable_AndNoUserTo403_WritingNothing(
        string type, string whoAmIFailure, int whoAmIStatus, HttpStatusCode expected, string errorCode)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        factory.CallerDataverse.WhoAmIFailure = DataverseUserResponse.Fail(whoAmIStatus, whoAmIFailure, "WhoAmI failed.");
        var created = CaptureCreate(factory, type);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "Unchecked Caller", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(expected);
        (await ReadProblemAsync(response)).Should().ContainKey("errorCode").WhoseValue.Should().Be(errorCode);
        created.Attempts.Should().BeEmpty("never allowed because it could not be checked");
    }

    // ── G4 condition 1: the request contract carries no regarding member ─────────────────────────────────────────

    /// <summary>
    /// G4 condition 1 is satisfied by the contract's shape (decision 2026-10-22): <see cref="QuickCreateRequest"/> has no
    /// regarding member, so a client cannot send a pair and the adapter writes only the derived one. A member that adds one
    /// — by its name or its effective JSON name — reopens the condition.
    /// </summary>
    [Fact]
    public void QuickCreateRequest_HasNoRegardingMember_ByNameOrJsonName()
    {
        const string Message =
            "a regarding member reintroduces G4 condition 1: refuse a differing pair under a secure source with 403 secure_source_filing_mismatch";

        var offending = typeof(QuickCreateRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .SelectMany(p => new[]
            {
                p.Name,
                p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name),
            })
            .Where(name => name.Contains("regarding", StringComparison.OrdinalIgnoreCase))
            .ToList();

        typeof(QuickCreateRequest).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Should().Contain(p => p.Name == nameof(QuickCreateRequest.SourceRecordId), "the scan reads the request's real members");
        if (offending.Count > 0)
            Assert.Fail(Message);
    }

    // ── #2191 K2: a project created from a record whose pair cannot be derived is refused (re-pointed by 018c) ──────

    /// <summary>
    /// #2191 K2 and its review K1, re-pointed from the deleted Office core's tests (018c): a project created FROM a record is
    /// filed under it, so when the pair's record type cannot be derived the create is refused — 403
    /// <c>record_owner_parent_undetermined</c>, nothing written — never created unfiled (and so possibly not isolated). No
    /// catalog row is configuration ("Ask an administrator", never "Try again"); a catalog read that faults is "Try again.".
    /// </summary>
    [Theory]
    [InlineData(false, "Ask an administrator to check the record types configured in this environment.")]
    [InlineData(true, "Try again.")]
    public async Task Post_Project_FromARecordWhosePairCannotBeDerived_Returns403Undetermined_AndWritesNothing(
        bool catalogFaults, string ending)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        ArrangeSourceRead(factory, new Entity("sprk_project", SourceProjectId));
        var catalog = factory.Entities.Setup(e => e.RetrieveMultipleAsync(
            It.Is<Microsoft.Xrm.Sdk.Query.QueryExpression>(q => q.EntityName == "sprk_recordtype_ref"), It.IsAny<CancellationToken>()));
        if (catalogFaults)
            catalog.ThrowsAsync(new TimeoutException("catalog read timed out"));
        else
            catalog.ReturnsAsync(new EntityCollection());
        var created = CaptureCreate(factory, "project");

        var response = await factory.CreateClient().PostAsJsonAsync("/api/office/quickcreate/project", new QuickCreateRequest
        {
            Name = "Lease review",
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProjectId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        var problem = await ReadProblemAsync(response);
        problem["errorCode"].Should().Be(Sprk.Bff.Api.Services.Dataverse.RecordOwnerRefusal.ParentUndetermined);
        problem["detail"].Should().EndWith(ending);
        if (!catalogFaults)
            problem["detail"].Should().NotContain("Try again", "no catalog row is configuration: a retry will not help");
        created.Attempts.Should().BeEmpty("refused before the create: never filed under nothing");
    }

    // ── The quick-create defaults prefill is the server's own default (task 100; moved intact by 018c) ──────────────

    /// <summary>
    /// Task 100: the pane's prefill and the server's default are ONE answer — <c>GET /quickcreate/defaults</c>
    /// (<c>OfficeService.ResolveDefaultAssigneeAsync</c>) names exactly the contact a create that names no assignee is given, and
    /// offers none, with none written, when the maker has no link. Re-pointed from the deleted core's unit test (018c).
    /// </summary>
    [Theory]
    [MemberData(nameof(MatterAndProject))]
    public async Task GetDefaults_ThePrefill_IsTheContactTheCreateAssigns_AndNoneWithoutALink(string type)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        using var host = WithMakersContact(factory, MakersContact);
        factory.Entities
            .Setup(e => e.RetrieveAsync("contact", MakersContact, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("contact", MakersContact) { ["fullname"] = "Maker Person" });
        var created = CaptureCreate(factory, type);
        var client = host.CreateClient();

        var prefill = await DefaultAssigneeAsync(client);
        var response = await client.PostAsJsonAsync(
            $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "Uses The Default", MatterTypeId = MatterTypeId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        prefill.Should().NotBeNull();
        prefill!.Id.Should().Be(created.Entity!.GetAttributeValue<EntityReference>(AssignedTo).Id,
            "the prefill names exactly the contact the create assigns when the request names none");
        prefill.Name.Should().Be("Maker Person");
        prefill.Email.Should().BeNull();

        using var unlinkedFactory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(unlinkedFactory);
        using var unlinked = WithMakersContact(unlinkedFactory, contactId: null);
        var unlinkedCreate = CaptureCreate(unlinkedFactory, type);
        var unlinkedClient = unlinked.CreateClient();

        (await DefaultAssigneeAsync(unlinkedClient)).Should().BeNull("no link, no prefill");
        (await unlinkedClient.PostAsJsonAsync(
                $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "No Default", MatterTypeId = MatterTypeId }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        unlinkedCreate.Entity!.Contains(AssignedTo).Should().BeFalse("and no default is written either");
    }

    /// <summary>The contact row cannot be read: no prefill (200, <c>assignedTo: null</c>), never a fault.</summary>
    [Fact]
    public async Task GetDefaults_WhenTheContactCannotBeRead_GivesNoPrefill_NeverThrows()
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        using var host = WithMakersContact(factory, MakersContact);
        factory.Entities
            .Setup(e => e.RetrieveAsync("contact", MakersContact, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("dataverse slow"));

        var response = await host.CreateClient().GetAsync("/api/office/quickcreate/defaults");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<QuickCreateDefaultsResponse>())!.AssignedTo.Should().BeNull();
    }

    private static async Task<QuickCreateContactOption?> DefaultAssigneeAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/office/quickcreate/defaults");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<QuickCreateDefaultsResponse>())!.AssignedTo;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>A resolved caller whose owning team's business unit answers (no "business-unit defaults" warning).</summary>
    private static void ArrangeResolvedCaller(OfficeQuickCreateTestWebAppFactory factory)
    {
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(CallerId.ToString("D")));
        factory.Entities
            .Setup(e => e.RetrieveAsync("team", RecordOwnershipResolverDouble.DefaultTeamId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("team", RecordOwnershipResolverDouble.DefaultTeamId)
            {
                ["businessunitid"] = new EntityReference("businessunit", BusinessUnitId),
            });
        factory.Entities
            .Setup(e => e.RetrieveAsync("businessunit", BusinessUnitId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("businessunit", BusinessUnitId) { ["sprk_searchindexname"] = "spaarke-files-index" });
    }

    /// <summary>The route's Read gate on the named contact (<c>QuickCreateSourceAccessFilter</c>) passes.</summary>
    private static void ArrangeContactRead(OfficeQuickCreateTestWebAppFactory factory, Guid contactId)
        => factory.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "contacts", contactId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);

    /// <summary>The source the caller may read, read app-only (and masked through the caller's mirror read).</summary>
    private static void ArrangeSourceRead(OfficeQuickCreateTestWebAppFactory factory, Entity source)
    {
        factory.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "sprk_projects", SourceProjectId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read);
        factory.Entities
            .Setup(e => e.RetrieveAsync("sprk_project", SourceProjectId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
    }

    /// <summary>A host whose identity service gives every user <paramref name="contactId"/> as their linked contact (null = none).</summary>
    private static WebApplicationFactory<Program> WithMakersContact(OfficeQuickCreateTestWebAppFactory factory, Guid? contactId)
    {
        var identity = IdentityNormalizationFixtures.WithContact(contactId);
        return factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IIdentityNormalizationService>();
            services.AddSingleton(identity.Object);
        }));
    }

    /// <summary>The service's step-11 write, and the created row's number read-back (any id: the service picks it).</summary>
    private static RecordCreationWriteCapture CaptureCreate(OfficeQuickCreateTestWebAppFactory factory, string type)
    {
        var table = type == "matter" ? "sprk_matter" : "sprk_project";
        var numberColumn = type == "matter" ? "sprk_matternumber" : "sprk_projectnumber";
        var capture = factory.CaptureRecordCreationWrite();
        factory.Entities
            .Setup(e => e.RetrieveAsync(
                table, It.Is<Guid>(id => id != SourceProjectId),
                It.Is<string[]>(columns => columns.Length == 1 && columns[0] == numberColumn),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => new Entity(table, id) { [numberColumn] = "NUM-018" });
        return capture;
    }

    private static FieldMappingProfileEntity Profile(string source, string target, params FieldMappingRuleEntity[] rules) => new()
    {
        Id = Guid.NewGuid(),
        Name = $"{source} to {target}",
        SourceEntity = source,
        TargetEntity = target,
        IsActive = true,
        Rules = rules.ToList(),
    };

    private static FieldMappingRuleEntity Rule(
        string name, string sourceField, int sourceType, string targetField, int targetType, int mappingType) => new()
        {
            Id = Guid.NewGuid(),
            Name = name,
            SourceField = sourceField,
            SourceFieldType = sourceType,
            TargetField = targetField,
            TargetFieldType = targetType,
            MappingType = mappingType,
            ExecutionOrder = 1,
            IsActive = true,
        };

    /// <summary>A Web API body value as text (the capture holds the service's <see cref="JsonElement"/>s).</summary>
    private static string? Text(object? value) =>
        value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value as string;

    private static async Task<Dictionary<string, string?>> ReadProblemAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString());
    }
}
