using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Tests.Api.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using EntityReference = Microsoft.Xrm.Sdk.EntityReference;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.CreatorPerson;

/// <summary>
/// unified-access-control-r2 task 133 b2 — owner round 7 item 2, option (a): every BFF APP-ONLY create of a project,
/// matter or work assignment records the PERSON who asked for it in <c>sprk_createdbyperson</c>, because its
/// <c>createdby</c> is the BFF application user.
/// </summary>
/// <remarks>
/// <para><b>The failure mode.</b> Secure provisioning's resume shares a stranded secure record to the person who created
/// it. For an app-created row there was nobody to share to (createdonbehalfby is empty, live 2026-10-01), so the resume
/// could only refuse. A writer that forgot the stamp would pass every create test and silently bring that back.</para>
/// <para>Field mapping task 018c: the Office quick-create of a matter or project now creates through the unified
/// <c>RecordCreationService</c> (step 9 stamps the person WhoAmI names), so these tests drive the real route over the Office
/// contract host (<see cref="OfficeQuickCreateTestWebAppFactory"/>, the caller's Dataverse <see cref="OfficeCallerDataverseDouble"/>,
/// the create captured by <see cref="RecordCreationWriteCapture"/>) instead of the deleted Office creation core.</para>
/// <para>The chat create (<c>dataverse.create_record</c>, created by the app since task 146; its stamp rides that create
/// per owner round 10) is pinned in <c>SecureChildOwnershipAiToolTests</c>, and its refusal of an item naming the column
/// in <c>DataverseCreateRecordHandlerTests</c>;
/// the column's schema (name, target, tables, field security) against these constants in
/// <see cref="RecordCreatorPersonSchemaAgreementTests"/>.</para>
/// </remarks>
[Trait("status", "task-133-uac-r2")]
public class RecordCreatorPersonStampTests
{
    private static readonly Guid Caller = OfficeQuickCreateWorld.FieldsCaller;
    private static readonly Guid BusinessUnit = OfficeQuickCreateWorld.FieldsBusinessUnit;
    private static readonly Guid SourceProject = OfficeQuickCreateWorld.ContractSourceProject;
    private static readonly Guid SomeoneElse = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    public static TheoryData<string> MatterAndProject => new() { "matter", "project" };

    private static EntityReference PersonOf(Entity row) =>
        row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column);

    // =====================================================================================
    // Office quick-create — the unified create behind the Office adapter (matter, project)
    // =====================================================================================

    [Theory]
    [MemberData(nameof(MatterAndProject))]
    public async Task OfficeQuickCreate_RecordsTheCallerAsTheCreatorPerson(string type)
    {
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        var created = CaptureCreate(factory, type);

        var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/office/quickcreate/{type}", new QuickCreateRequest { Name = "New record" });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        PersonOf(created.Entity!).Should().BeEquivalentTo(new EntityReference("systemuser", Caller),
            "the create is app-only, so createdby is the BFF application user — this column is the person");
        created.Entity!.GetAttributeValue<EntityReference>("ownerid").Should().BeEquivalentTo(
            new EntityReference("team", RecordOwnershipResolverDouble.DefaultTeamId),
            "access still comes from the team; the column decides only who a resume shares to");
    }

    /// <summary>
    /// A field-mapping Copy rule targeting the column cannot name the SOURCE record's creator as this one's: the column is
    /// protected from mapping like the owner, and the caller is what lands.
    /// </summary>
    [Theory]
    [MemberData(nameof(MatterAndProject))]
    public async Task OfficeQuickCreate_AMappingRuleCannotChooseTheCreatorPerson(string type)
    {
        var targetTable = type == "matter" ? "sprk_matter" : "sprk_project";
        using var factory = new OfficeQuickCreateTestWebAppFactory();
        ArrangeResolvedCaller(factory);
        factory.AccessProbe
            .Setup(p => p.GetCallerRightsAsync(It.IsAny<string>(), "sprk_projects", SourceProject, It.IsAny<CancellationToken>()))
            .ReturnsAsync(AccessRights.Read | AccessRights.AppendTo);
        factory.Entities
            .Setup(e => e.RetrieveAsync("sprk_project", SourceProject, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_project", SourceProject)
            {
                [RecordCreatorPerson.Column] = new EntityReference("systemuser", SomeoneElse),
            });
        factory.FieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync("sprk_project", targetTable, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "copy creator",
                SourceEntity = "sprk_project",
                TargetEntity = targetTable,
                IsActive = true,
                Rules =
                [
                    new FieldMappingRuleEntity
                    {
                        Id = Guid.NewGuid(), Name = "copy-creator-person",
                        SourceField = RecordCreatorPerson.Column, SourceFieldType = 1,
                        TargetField = RecordCreatorPerson.Column, TargetFieldType = 1,
                        MappingType = 0, ExecutionOrder = 1, IsActive = true,
                    },
                ],
            });
        var created = CaptureCreate(factory, type);

        var response = await factory.CreateClient().PostAsJsonAsync($"/api/office/quickcreate/{type}", new QuickCreateRequest
        {
            Name = "New record",
            SourceEntityType = "sprk_project",
            SourceRecordId = SourceProject,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        PersonOf(created.Entity!).Id.Should().Be(Caller, "the source record's creator is not this record's creator");
    }

    // POST /api/v1/work-assignments (WorkAssignmentEndpoints) no longer exists: task 166 deleted the route as caller-less
    // (sweep finding S-76, owner round 10 item 1; absence pinned by DeadRouteRetirementTests), so the work-assignment
    // stamp tests that drove its handler went with it. Work assignments are created through the MDA and the Create Work
    // Assignment wizard, which run as the user (createdby is the person).

    // ── Helpers (the Office contract host's, as OfficeQuickCreateAdapterContractTests arranges them) ──────────────────

    private static void ArrangeResolvedCaller(OfficeQuickCreateTestWebAppFactory factory)
    {
        factory.CallerResolver
            .Setup(r => r.ResolveAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(Caller.ToString("D")));
        factory.Entities
            .Setup(e => e.RetrieveAsync("team", RecordOwnershipResolverDouble.DefaultTeamId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("team", RecordOwnershipResolverDouble.DefaultTeamId)
            {
                ["businessunitid"] = new EntityReference("businessunit", BusinessUnit),
            });
        factory.Entities
            .Setup(e => e.RetrieveAsync("businessunit", BusinessUnit, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("businessunit", BusinessUnit));
    }

    /// <summary>The service's step-11 write, and the created row's number read-back (any id: the service picks it).</summary>
    private static RecordCreationWriteCapture CaptureCreate(OfficeQuickCreateTestWebAppFactory factory, string type)
    {
        var table = type == "matter" ? "sprk_matter" : "sprk_project";
        var numberColumn = type == "matter" ? "sprk_matternumber" : "sprk_projectnumber";
        var capture = factory.CaptureRecordCreationWrite();
        factory.Entities
            .Setup(e => e.RetrieveAsync(
                table, It.Is<Guid>(id => id != SourceProject),
                It.Is<string[]>(columns => columns.Length == 1 && columns[0] == numberColumn),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, string[] _, CancellationToken _) => new Entity(table, id) { [numberColumn] = "NUM-133" });
        return capture;
    }
}
