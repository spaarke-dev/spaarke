using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.WorkAssignments;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Context;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.WorkAssignments;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;

namespace Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership;

/// <summary>
/// spaarke-ontology-platform-r1 task 046 (D-21, D-59): the server-side work-assignment create — the REAL
/// <see cref="WorkAssignmentEndpoints"/> handler and <see cref="WorkAssignmentCreateService"/> over the REAL
/// <c>OwnedChildWrite</c> core, the REAL resolver over <see cref="Directory"/> and the caller's scripted Dataverse (the
/// harness the chat create tool and the child-record routes are tested on). The payload is the one the Create Work
/// Assignment wizard builds (<c>workAssignmentService.ts</c>).
/// </summary>
public sealed partial class SecureChildOwnershipAiToolTests
{
    private static readonly Guid WaRecordTypeMatter = Guid.Parse("a0460000-0000-4000-8000-000000000001");
    private static readonly Guid LawFirm = Guid.Parse("a0460000-0000-4000-8000-000000000002");

    /// <summary>The wizard's create payload for a work assignment regarding <paramref name="matter"/>.</summary>
    private static Dictionary<string, object?> WizardPayload(Guid matter, string name = "Review the lease") => new()
    {
        ["sprk_name"] = name,
        ["sprk_priority"] = 100000001,
        ["sprk_description"] = "Second draft",
        ["sprk_responseduedate"] = "2026-10-20",
        ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})",
        ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({WaRecordTypeMatter:D})",
        ["sprk_regardingrecordid"] = matter.ToString("D"),
        ["sprk_regardingrecordname"] = "Lease dispute",
        ["sprk_regardingrecordurl"] = $"https://org/main.aspx?etn=sprk_matter&id={matter:D}",
        ["sprk_AssignedLawFirm1@odata.bind"] = $"/sprk_organizations({LawFirm:D})",
    };

    // ── Create (D-59: team-owned, creator stamped, AppendTo on the parent every time) ────────────────────────────

    [Fact]
    public async Task WorkAssignmentCreate_TheWizardsPayloadUnderAnOrdinaryMatter_IsCreatedByTheApp_OwnedByTheMattersTeam_ForTheCaller()
    {
        var result = await CreateWorkAssignment(WizardPayload(OrdinaryMatter));

        Status(result).Should().Be(StatusCodes.Status201Created, Detail(result));
        _user.Posts.Should().BeEmpty("a work assignment is never created as, or owned by, the user (WP-3)");
        var (table, id, fields) = _appCreates.Should().ContainSingle().Subject;
        table.Should().Be("sprk_workassignment");
        Owner(fields).Should().Be(Directory.ChildTeam, "record-first: the regarding matter's business-unit team");
        fields.Should().ContainKey("sprk_CreatedByPerson@odata.bind")
            .WhoseValue.Should().Be($"/systemusers({Caller:D})", "createdby is the application; the person is recorded");
        Bind(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        Bind(fields, "sprk_AssignedLawFirm1@odata.bind").Should().Be($"/sprk_organizations({LawFirm:D})");
        foreach (var column in new[] { "sprk_name", "sprk_priority", "sprk_responseduedate", "sprk_regardingrecordid", "sprk_regardingrecordname" })
            fields.Should().ContainKey(column, "every field the wizard sets is carried (parity table rows 1-12)");
        fields.Keys.Should().NotContain("sprk_AssignedToInternal@odata.bind",
            "wizard parity: the chat tool's 'for person' default is not added on this path");
        Created(result).Should().Be(id);
    }

    [Fact]
    public async Task WorkAssignmentCreate_WithoutAppendToOnTheRegardingMatter_IsTheSame404AsAMissingMatter_AndNothingIsCreated()
    {
        var missingMatter = Guid.Parse("a0460000-0000-4000-8000-0000000000f2");
        _user.NoAppendTo.Add(OrdinaryMatter);
        _user.Missing.Add(missingMatter);

        var denied = await CreateWorkAssignment(WizardPayload(OrdinaryMatter));
        var missing = await CreateWorkAssignment(WizardPayload(missingMatter));

        Status(denied).Should().Be(StatusCodes.Status404NotFound, "D-59: AppendTo on the parent is checked every time");
        ReasonCode(denied).Should().Be(WorkAssignmentCreateService.ParentUnavailableCode);
        (Status(denied), ReasonCode(denied), Detail(denied)).Should().Be((Status(missing), ReasonCode(missing), Detail(missing)),
            "a record the caller cannot append to is not disclosed");
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkAssignmentCreate_WithoutTheCreatePrivilege_Is403_AndNothingIsCreated()
    {
        _user.Held.Remove("prvCreatesprk_workassignment");

        var result = await CreateWorkAssignment(WizardPayload(OrdinaryMatter));

        Status(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(result).Should().Be(WorkAssignmentCreateService.DeniedCode);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkAssignmentCreate_WithoutAName_Is400_AndNothingIsCreated()
    {
        var payload = WizardPayload(OrdinaryMatter, name: "   ");

        var result = await CreateWorkAssignment(payload);

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(WorkAssignmentCreateService.NameRequiredCode);
        _appCreates.Should().BeEmpty();
    }

    [Theory]
    [InlineData("sprk_containerid", "b!container-of-someone-else")]
    [InlineData("sprk_issecure", true)]
    [InlineData("sprk_SecurityBU@odata.bind", "/businessunits(a0460000-0000-4000-8000-0000000000b9)")]
    public async Task WorkAssignmentCreate_NamingAServerOwnedRootColumn_Is400_AndNothingIsCreated(string column, object value)
    {
        var payload = WizardPayload(OrdinaryMatter);
        payload[column] = value;

        var result = await CreateWorkAssignment(payload);

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
        ReasonCode(result).Should().Be(WorkAssignmentCreateService.ServerOwnedColumnCode);
        _appCreates.Should().BeEmpty("a root's container and secure state are the server's (task 076 W1, task 150)");
    }

    [Fact]
    public async Task WorkAssignmentCreate_UnderAMatterWhoseSecureFlagCannotBeRead_IsRefused_AndNothingIsCreated()
    {
        // Task 158 (owner round 17 item 3): an EMPTY parent flag is never "not secure" — the plan refuses before any write.
        var entities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        entities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<Microsoft.Xrm.Sdk.Query.QueryExpression>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Microsoft.Xrm.Sdk.Query.QueryExpression q, CancellationToken _) =>
                q.EntityName == "sprk_matter"
                    ? new EntityCollection(new List<Entity> { new("sprk_matter", OrdinaryMatter) })
                    : new EntityCollection());

        var result = await CreateWorkAssignment(WizardPayload(OrdinaryMatter), SecureRootFilingGateFixtures.Over(entities.Object));

        Status(result).Should().Be(StatusCodes.Status409Conflict);
        ReasonCode(result).Should().Be(RecordOwnerRefusal.ParentUndetermined);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
    }

    [Fact]
    public async Task WorkAssignmentCreate_ForACallerWithNoDataverseUser_IsTheSingle403_BeforeAnythingIsAsked()
    {
        var unresolved = new Mock<ICallerSystemUserResolver>(MockBehavior.Strict);
        unresolved.Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Unresolved("no-systemuser"));

        var result = await WorkAssignmentEndpoints.CreateAsync(
            Payload(WizardPayload(OrdinaryMatter)), unresolved.Object, Creator(), HttpContextOfCaller(),
            NullLogger<Program>.Instance, CancellationToken.None);

        Status(result).Should().Be(StatusCodes.Status403Forbidden);
        ReasonCode(result).Should().Be(WorkAssignmentEndpoints.CallerUnresolvedReasonCode);
        _appCreates.Should().BeEmpty();
        _user.Posts.Should().BeEmpty();
        _user.Patches.Should().BeEmpty();
    }

    // ── Harness ───────────────────────────────────────────────────────────────────────────────────────────────

    private WorkAssignmentCreateService Creator(SecureRootFilingGate? gate = null) =>
        new(_user, _world.Resolver(), _appOnly.Object, gate ?? SecureRootFilingGateFixtures.NothingSecure(),
            // No AssignedAccessMaterializer in this host: the L1 trigger logs and leaves the record to the job (never fails).
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<WorkAssignmentCreateService>.Instance);

    private Task<IResult> CreateWorkAssignment(Dictionary<string, object?> payload, SecureRootFilingGate? gate = null)
    {
        var resolved = new Mock<ICallerSystemUserResolver>(MockBehavior.Strict);
        resolved.Setup(r => r.ResolveAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CallerSystemUserResolution.Resolved(Caller.ToString("D")));

        return WorkAssignmentEndpoints.CreateAsync(
            Payload(payload), resolved.Object, Creator(gate), HttpContextOfCaller(), NullLogger<Program>.Instance,
            CancellationToken.None);
    }

    private static Guid Created(IResult result) =>
        JsonSerializer.SerializeToElement(((IValueHttpResult)result).Value)
            .GetProperty("id").GetGuid();
}
