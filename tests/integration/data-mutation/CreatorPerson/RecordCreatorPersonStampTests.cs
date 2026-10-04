using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;

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
/// <para>The chat create (<c>dataverse.create_record</c>, created by the app since task 146; its stamp rides that create
/// per owner round 10) is pinned in <c>SecureChildOwnershipAiToolTests</c>, and its refusal of an item naming the column
/// in <c>DataverseCreateRecordHandlerTests</c>;
/// the column's schema (name, target, tables, field security) against these constants in
/// <see cref="RecordCreatorPersonSchemaAgreementTests"/>.</para>
/// </remarks>
[Trait("status", "task-133-uac-r2")]
public class RecordCreatorPersonStampTests
{
    private static readonly Guid Caller = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000c1");
    private static readonly Guid Assignee = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a5");
    private static readonly Guid Team = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid SourceRecord = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid SomeoneElse = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");

    private readonly Mock<IGenericEntityService> _entities = new(MockBehavior.Loose);
    private readonly Mock<IFieldMappingDataverseService> _fieldMappings = new();
    private readonly List<Entity> _created = new();

    public RecordCreatorPersonStampTests()
    {
        _entities.Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => _created.Add(e))
            .ReturnsAsync(Guid.NewGuid());
    }

    private static EntityReference PersonOf(Entity row) =>
        row.GetAttributeValue<EntityReference>(RecordCreatorPerson.Column);

    // =====================================================================================
    // Office quick-create — RecordCreationService (matter, project)
    // =====================================================================================

    private RecordCreationService OfficeCreator()
    {
        var ownership = new Mock<IRecordOwnershipResolver>();
        ownership.Setup(o => o.ResolveOwningTeamAsync(It.IsAny<RecordOwnershipContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Team);
        return new RecordCreationService(
            _entities.Object, _fieldMappings.Object, ownership.Object,
            IdentityNormalizationFixtures.NoLinkedContact(),
            Sprk.Bff.Api.Tests.TestInfrastructure.SecureRootFilingGateFixtures.NothingSecure(),
            Sprk.Bff.Api.Tests.AccessControl.AssignedAccessTestDoubles.InertMaterializer(),
            NullLogger<RecordCreationService>.Instance);
    }

    [Theory]
    [InlineData(QuickCreateEntityType.Matter)]
    [InlineData(QuickCreateEntityType.Project)]
    public async Task OfficeQuickCreate_RecordsTheCallerAsTheCreatorPerson(QuickCreateEntityType type)
    {
        var result = await OfficeCreator().CreateAsync(new RecordCreationRequest
        {
            EntityType = type,
            Name = "New record",
            CallerUserId = "oid",
            OwnerSystemUserId = Caller.ToString("D"),
        });

        result.Succeeded.Should().BeTrue();
        PersonOf(_created.Single()).Should().BeEquivalentTo(new EntityReference("systemuser", Caller),
            "the create is app-only, so createdby is the BFF application user — this column is the person");
        _created.Single()["ownerid"].Should().BeEquivalentTo(new EntityReference("team", Team),
            "access still comes from the team; the column decides only who a resume shares to");
    }

    /// <summary>
    /// A field-mapping Copy rule targeting the column cannot name the SOURCE record's creator as this one's: the column is
    /// protected from mapping like the owner, and the caller is what lands.
    /// </summary>
    [Theory]
    [InlineData(QuickCreateEntityType.Matter, "sprk_project", "sprk_matter")]
    [InlineData(QuickCreateEntityType.Project, "sprk_matter", "sprk_project")]
    public async Task OfficeQuickCreate_AMappingRuleCannotChooseTheCreatorPerson(
        QuickCreateEntityType type, string sourceEntity, string targetEntity)
    {
        _fieldMappings
            .Setup(m => m.GetFieldMappingProfileWithRulesAsync(sourceEntity, targetEntity, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FieldMappingProfileEntity
            {
                Id = Guid.NewGuid(),
                Name = "copy creator",
                SourceEntity = sourceEntity,
                TargetEntity = targetEntity,
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
        _entities
            .Setup(e => e.RetrieveAsync(sourceEntity, SourceRecord, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity(sourceEntity, SourceRecord)
            {
                [RecordCreatorPerson.Column] = new EntityReference("systemuser", SomeoneElse),
            });

        var result = await OfficeCreator().CreateAsync(new RecordCreationRequest
        {
            EntityType = type,
            Name = "New record",
            CallerUserId = "oid",
            OwnerSystemUserId = Caller.ToString("D"),
            SourceEntityLogicalName = sourceEntity,
            SourceRecordId = SourceRecord,
        });

        result.Succeeded.Should().BeTrue();
        PersonOf(_created.Single()).Id.Should().Be(Caller, "the source record's creator is not this record's creator");
    }

    // =====================================================================================
    // POST /api/v1/work-assignments — WorkAssignmentEndpoints
    // =====================================================================================

    /// <summary>The WhoAmI seam, substituted: the caller resolves to <see cref="Caller"/>, nobody, or a throw.</summary>
    private sealed class WhoAmI : CallerRecordAccessProbe
    {
        private readonly Func<Guid?> _answer;

        public WhoAmI(Func<Guid?> answer)
            : base(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
            => _answer = answer;

        public override Task<Guid?> GetCallerSystemUserIdAsync(string? callerBearerToken, CancellationToken ct = default)
            => Task.FromResult(_answer());
    }

    private Task<IResult> CreateWorkAssignmentAsync(Func<Guid?> whoAmI)
    {
        var http = new DefaultHttpContext();
        http.Request.Headers.Authorization = "Bearer user-token";
        return WorkAssignmentEndpoints.CreateWorkAssignmentAsync(
            new CreateWorkAssignmentRequest("Review the draft", Assignee),
            _entities.Object,
            new NotificationService(_entities.Object, NullLogger<NotificationService>.Instance),
            new WhoAmI(whoAmI),
            http,
            NullLogger<Program>.Instance,
            CancellationToken.None);
    }

    [Fact]
    public async Task WorkAssignmentCreate_RecordsTheCallerAsTheCreatorPerson()
    {
        var result = await CreateWorkAssignmentAsync(() => Caller);

        result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.Created<CreateWorkAssignmentResponse>>();
        var row = _created.Single(e => e.LogicalName == "sprk_workassignment");
        PersonOf(row).Should().BeEquivalentTo(new EntityReference("systemuser", Caller),
            "the caller is who asked for it — never the request body, never the assignee");
        row["ownerid"].Should().BeEquivalentTo(new EntityReference("systemuser", Assignee));
    }

    /// <summary>
    /// The caller's identity cannot be established (WhoAmI answers nobody, or throws): refused 403 BEFORE the create.
    /// An app-created row with nobody recorded is exactly what a resume could only refuse.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkAssignmentCreate_WhenTheCallerCannotBeIdentified_CreatesNothing(bool throws)
    {
        var result = await CreateWorkAssignmentAsync(() => throws ? throw new InvalidOperationException("OBO failed") : (Guid?)null);

        var problem = result.Should().BeOfType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        problem.ProblemDetails.Extensions["reasonCode"].Should().Be(WorkAssignmentEndpoints.CreatorUnresolvedReasonCode);
        _created.Should().BeEmpty("nothing is created without the person who asked for it");
    }
}
