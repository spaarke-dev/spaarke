using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FieldMapping;
using Sprk.Bff.Api.Services.RecordCreation;
using Sprk.Bff.Api.Tests.AccessControl;
using Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;
using Sprk.Bff.Api.Tests.Integration.DataMutation.CoreAncestorStamping;
using Sprk.Bff.Api.Tests.TestInfrastructure;
using Xunit;
using Directory = Sprk.Bff.Api.Tests.TestInfrastructure.OwnershipDirectory;
using ScriptedUserClient =
    Sprk.Bff.Api.Tests.Integration.DataMutation.RecordOwnership.SecureChildOwnershipAiToolTests.ScriptedUserClient;

namespace Sprk.Bff.Api.Tests.Seam.RecordCreation;

/// <summary>
/// field-mapping-server-write-path-r1 task 014 — field mapping INSIDE the unified create (spec FR-06 step 3, FR-02, FR-03,
/// FR-04) and the caller checks on the MERGED payload by origin (FR-08 and its 2026-10-20 amendment; owner decision
/// 2026-10-19), closing #1617 and #1614. Driven end to end through the REAL <see cref="RecordCreationService"/>, the REAL
/// <see cref="MappingSourceResolver"/>, <see cref="MappingSourceReadProbe"/> and <see cref="CreateTimeFieldMapping"/>, the
/// REAL <see cref="CallerWriteAuthorization"/> checks and <see cref="DataverseWriteItemMapper"/> over the caller's scripted
/// Dataverse, and the REAL owner resolver, re-stamp and sharee mirror (the harness of
/// <c>RecordCreationServicePipelineSeamTests</c>).
/// </summary>
/// <remarks>
/// <para><b>Doubled</b> only at the module boundaries: the caller's Dataverse (scripted, plus the target table's column
/// metadata), the caller-rights probe's wire (<see cref="ReadProbe"/>), the app-only profile reader and create, and the
/// app-only source read.</para>
/// <para><b>What is observed.</b> The create is asserted on what the app-only seam was asked to write; warnings on the
/// outcome's <see cref="RecordCreationOutcome.MappingWarnings"/> (code + column); refusals through
/// <see cref="RecordCreationProblemMap"/>, the body a route returns.</para>
/// </remarks>
[Trait("status", "new")]
public sealed partial class RecordCreationServiceMappingSeamTests
{
    private static readonly Guid Caller = Guid.Parse("a0140000-0000-4000-8000-0000000000aa");
    private static readonly Guid CallerOid = Guid.Parse("a0140000-0000-4000-8000-0000000000ab");
    private static readonly Guid OrdinaryMatter = Guid.Parse("a0140000-0000-4000-8000-000000000001");
    private static readonly Guid MissingMatter = Guid.Parse("a0140000-0000-4000-8000-000000000002");
    private static readonly Guid SecureMatter = Guid.Parse("a0140000-0000-4000-8000-000000000003");
    private static readonly Guid DeniedMatter = Guid.Parse("a0140000-0000-4000-8000-000000000004");
    private static readonly Guid SourceProject = Guid.Parse("a0140000-0000-4000-8000-000000000005");
    private static readonly Guid OtherProject = Guid.Parse("a0140000-0000-4000-8000-000000000006");
    private static readonly Guid Assignee = Guid.Parse("a0140000-0000-4000-8000-0000000000c1");
    private static readonly Guid CallersContact = Guid.Parse("a0140000-0000-4000-8000-0000000000c2");
    private static readonly Guid MatterType = Guid.Parse("a0140000-0000-4000-8000-0000000000e1");
    private static readonly Guid Communication = Guid.Parse("a0140000-0000-4000-8000-000000000007");
    private const string CallerToken = "caller-bearer-token";
    private const string FixedCorrelation = "seam-014-correlation";

    private const int Copy = CreateTimeFieldMapping.MappingTypeCopy;
    private const int Default = CreateTimeFieldMapping.MappingTypeDefault;
    private const int Text = CreateTimeFieldMapping.FieldTypeText;
    private const int Lookup = CreateTimeFieldMapping.FieldTypeLookup;
    private const int OptionSet = CreateTimeFieldMapping.FieldTypeOptionSet;
    private const int Number = CreateTimeFieldMapping.FieldTypeNumber;
    private const int Memo = CreateTimeFieldMapping.FieldTypeMemo;

    private readonly Directory _world = Directory.Standard()
        .WithUser(Caller, CallerOid, Directory.ChildBu)
        .WithOrdinaryRoot("sprk_matter", OrdinaryMatter)
        .WithSecureRoot("sprk_matter", SecureMatter);

    private readonly List<string> _journal = [];
    private readonly ScriptedUserClient _scripted = new(Caller);
    private readonly MappingUserClient _user;

    /// <summary>
    /// #2188: the caller's read of the mapping source (<c>MappingSourceCallerMask</c>) — by default it sees exactly what the
    /// app-only read returned; a test marks a column field-secured or the read failed.
    /// </summary>
    private readonly MappingSourceCallerReadDouble _callerRead;
    private readonly List<(string Table, Guid Id, Dictionary<string, object?> Fields)> _creates = [];
    private readonly Mock<IFieldMappingDataverseService> _appOnly = new(MockBehavior.Strict);
    private readonly Mock<IGenericEntityService> _sourceReader = new(MockBehavior.Strict);
    private readonly Dictionary<(string Source, string Target), FieldMappingProfileEntity> _profiles = new();
    private readonly Dictionary<(string Table, Guid Id), Entity> _sources = new();
    private readonly List<(string Table, Guid Id, string[] Columns)> _sourceReads = [];
    private readonly HashSet<Guid> _readable = [];
    private readonly Dictionary<Guid, AccessRights> _rights = [];
    private readonly ReadProbe _probe;
    private readonly StampWorld _stamps = new StampWorld()
        .Row("sprk_matter", OrdinaryMatter).Row("sprk_matter", SecureMatter);
    private readonly SecureChildShareWorld _shareWorld = SecureChildShareWorld.Standard()
        .OrdinaryRoot("sprk_matter", OrdinaryMatter)
        .SecureRoot("sprk_matter", SecureMatter);
    private readonly RecordingPublisher _publisher = new();
    private Exception? _profileFault;
    private Func<string, RecordTableRule?> _tableRules = RecordTableRules.Find;
    private Func<SecureRootFilingGate> _rootFiling = SecureRootFilingGateFixtures.NothingSecure;

    /// <summary>Task 037: the service's scope factory (a host with the "Assigned *" materializer) and logger, when a test sets them.</summary>
    private IServiceScopeFactory? _scopes;
    private ILogger<RecordCreationService>? _logger;
    private Action<string, Guid, Dictionary<string, object?>>? _onCreate;

    public RecordCreationServiceMappingSeamTests()
    {
        _callerRead = MappingSourceCallerReadDouble.MirroringAppReads(_sourceReader);
        _user = new MappingUserClient(_callerRead.RoutedOver(_scripted), _journal);
        _probe = new ReadProbe(
            id => _rights.TryGetValue(id, out var rights) ? rights
                : _readable.Contains(id) ? AccessRights.Read | AccessRights.Write
                : AccessRights.None,
            _journal);

        _appOnly
            .Setup(a => a.UpdateRecordFieldsAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object?>>(), It.IsAny<CancellationToken>(), null))
            .Callback<string, Guid, Dictionary<string, object?>, CancellationToken, Guid?>((table, id, fields, _, _) =>
            {
                _journal.Add("create");

                // The app-only create is a Web API PATCH: a body naming a lookup by its logical name is refused, as live.
                if (ScriptedUserClient.UndeclaredPropertyIn(table, JsonSerializer.Serialize(fields), "ownerid", "sprk_CreatedByPerson")
                    is { } undeclared)
                {
                    throw new HttpRequestException($"0x80060888 Could not find a property named '{undeclared}'.", null,
                        System.Net.HttpStatusCode.BadRequest);
                }

                _creates.Add((table, id, fields));
                Materialize(table, id, fields);
                _onCreate?.Invoke(table, id, fields); // task 037: a test's own view of the created row
            })
            .Returns(Task.CompletedTask);

        _appOnly
            .Setup(a => a.GetFieldMappingProfileWithRulesAsync(It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns((string source, string target, bool _, CancellationToken _) =>
            {
                _journal.Add("profile");
                if (_profileFault is { } fault)
                    throw fault;
                return Task.FromResult(_profiles.GetValueOrDefault((source, target)));
            });

        _sourceReader
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string table, Guid id, string[] columns, CancellationToken _) =>
            {
                _journal.Add("source-read");
                _sourceReads.Add((table, id, columns));
                if (!_sources.TryGetValue((table, id), out var row))
                    throw new InvalidOperationException($"unscripted source {table}({id})");
                var read = new Entity(table, id);
                foreach (var column in columns.Where(row.Attributes.ContainsKey))
                    read[column] = row[column];
                return Task.FromResult(read);
            });

        // The target tables' columns as their metadata declares them (read as the caller).
        _user.Columns["sprk_event"] = new()
        {
            ["sprk_eventname"] = ("String", true),
            ["sprk_description"] = ("Memo", true),
            ["sprk_location"] = ("String", true),
            ["sprk_assignedto"] = ("Lookup", true),
            ["sprk_regardingmatter"] = ("Lookup", true),
            ["sprk_regardingproject"] = ("Lookup", true),
            ["sprk_budgetamount"] = ("Money", true),
            ["sprk_duedate"] = ("DateTime", true),
            ["sprk_eventcode"] = ("String", false),
        };
        _user.Columns["sprk_document"] = new()
        {
            ["sprk_documentdescription"] = ("Memo", true),
            [AccessInheritance.Column] = ("Picklist", true),
            ["sprk_accesspermission"] = ("Picklist", true),
        };
        _user.Columns["sprk_invoice"] = new()
        {
            ["sprk_matter"] = ("Lookup", true),
            ["sprk_project"] = ("Lookup", true),
            ["sprk_description"] = ("Memo", true),
        };
        _user.Columns["sprk_project"] = new()
        {
            ["sprk_description"] = ("Memo", true),
        };

        // The source matters the caller files under (each a record the probe answers Read on unless a test says not).
        _readable.UnionWith([OrdinaryMatter, SecureMatter, SourceProject]);
        _sources[("sprk_matter", OrdinaryMatter)] = new Entity("sprk_matter", OrdinaryMatter)
        {
            ["sprk_matterdescription"] = "From the matter",
            ["sprk_location"] = "Courtroom 4",
            ["sprk_assignedtointernal"] = new EntityReference("contact", Assignee),
            ["sprk_primaryproject"] = new EntityReference("sprk_project", OtherProject),
            ["sprk_duedate"] = new DateTime(2027, 3, 4, 0, 0, 0, DateTimeKind.Utc),
        };
        _sources[("sprk_matter", SecureMatter)] = _sources[("sprk_matter", OrdinaryMatter)];
        _sources[("sprk_project", SourceProject)] = new Entity("sprk_project", SourceProject)
        {
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", SecureMatter),
            ["sprk_projectdescription"] = "From the project",
        };
    }

    // =====================================================================================
    // #1617 — a mapped value the caller may not set is dropped, named; the create proceeds
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AMappedLookupTheCallerCannotAppendTo_IsDroppedWithAWarningNamingTheColumn_AndTheRowIsCreated()
    {
        MatterToEventProfile();
        _scripted.NoAppendToIn.Add(("contacts", Assignee));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter", "a mapped value the caller may set is written");
        fields.Should().NotContainKey("sprk_AssignedTo@odata.bind", "#1617: a mapped value is never written past a check it failed");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Code.Should().Be(RecordCreationWarning.MappedValueDroppedCode);
        warning.Column.Should().Be("sprk_assignedto");
        warning.Detail.Should().NotContain(Assignee.ToString("D")).And.NotContain("contact");
        outcome.Warnings.Should().Equal(warning.Detail);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.FieldMapping).State.Should().Be(RecordCreationStepState.Ran);
    }

    [Fact]
    public async Task CreateAsync_AMappedLookupTheCallerMayAppendTo_IsWritten_ByTheTablesNavigationProperty()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({Assignee:D})");
        StringOf(fields, "sprk_description").Should().Be("From the matter");
        StringOf(fields, "sprk_location").Should().Be("Courtroom 4");
        outcome.MappingWarnings.Should().BeEmpty();
        _scripted.Posts.Should().BeEmpty("the row is never created as the caller");
    }

    [Fact]
    public async Task CreateAsync_ACallersOwnLookupTheyCannotAppendTo_StillRefusesWithTheUniformNotFound_MappingOrNot()
    {
        MatterToEventProfile();
        _scripted.NoAppendToIn.Add(("contacts", CallersContact));

        var mapped = await Service().CreateAsync(EventUnder(OrdinaryMatter, assignedTo: CallersContact), AsCaller(), CancellationToken.None);
        var unmapped = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, assignedTo: CallersContact) with { Mapping = null }, AsCaller(), CancellationToken.None);

        mapped.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        Problem(mapped, "sprk_event").Status.Should().Be(StatusCodes.Status404NotFound);
        JsonSerializer.Serialize(Problem(mapped, "sprk_event"))
            .Should().Be(JsonSerializer.Serialize(Problem(unmapped, "sprk_event")), "the caller's own refusal is unchanged by mapping");
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AMappedFieldSecuredValue_IsDropped_ButACallersFieldSecuredValue_Refuses()
    {
        MatterToEventProfile();
        _scripted.SecuredColumns.Add("sprk_location");

        var mapped = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        mapped.Succeeded.Should().BeTrue(mapped.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_location");
        mapped.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w => w.Code == RecordCreationWarning.MappedValueDroppedCode && w.Column == "sprk_location");

        _creates.Clear();
        var supplied = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, extra: ("sprk_location", "Room 9")), AsCaller(), CancellationToken.None);

        supplied.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.Forbidden);
        supplied.Refusal.Code.Should().Be(RecordCreationRefusal.DeniedCode);
        supplied.Refusal.Detail.Should().Contain("sprk_location");
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // FR-08 amendment / UAC-r2 K-2 — a mapped FILING lookup is the caller's own: AppendTo it fails refuses
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AMappedFilingLookupTheCallerCannotAppendTo_IsRefused_AndNothingIsWritten()
    {
        ProjectToEventProfile();
        _scripted.NoAppendTo.Add(SecureMatter);

        var outcome = await Service().CreateAsync(
            Event(new { sprk_eventname = "Hearing" }, mappingSource: ("sprk_project", SourceProject)), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied,
            "a mapped lineage lookup files the row as the caller's would: refused, never dropped");
        var problem = Problem(outcome, "sprk_event");
        problem.Status.Should().Be(StatusCodes.Status404NotFound);
        JsonSerializer.Serialize(problem).Should().NotContain(SecureMatter.ToString("D"));
        _creates.Should().BeEmpty();
        _journal.Should().NotContain(["owner", "create"]);
    }

    [Fact]
    public async Task CreateAsync_AMappedFilingLookupTheCallerMayAppendTo_FilesTheRowThere_UnderItsSecureOwner()
    {
        ProjectToEventProfile();

        var outcome = await Service().CreateAsync(
            Event(new { sprk_eventname = "Hearing" }, mappingSource: ("sprk_project", SourceProject)), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_RegardingMatter@odata.bind").Should().Be($"/sprk_matters({SecureMatter:D})");
        Owner(fields).Should().Be(Directory.SecureNamedTeam, "the owner is decided on the merged row (secure-if-any)");
        StringOf(fields, "sprk_description").Should().Be("From the project");
    }

    [Fact]
    public async Task CreateAsync_AFilingLookupMarkedServerSet_IsRefusedBeforeAnyCheck_AndNothingIsWritten()
    {
        var request = EventUnder(OrdinaryMatter) with
        {
            Mapping = null,
            ServerSetLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { " SPRK_RegardingMatter " },
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.ServerSetFilingLookupCode);
        outcome.Refusal.Kind.Should().Be(RecordCreationRefusalKind.Undecided);
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status500InternalServerError);
        _journal.Should().NotContain(["user:check", "owner", "create"]);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ARuleTargetingAServerSetLookup_IsSkipped_SoNoMappedValueRidesPastItsAppendTo()
    {
        Profile("sprk_matter", "sprk_event", Rule("sprk_assignedtointernal", "sprk_assignedto", Lookup));
        _scripted.NoAppendToIn.Add(("contacts", Assignee));
        var request = EventUnder(OrdinaryMatter) with
        {
            ServerSetLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_assignedto" },
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_AssignedTo@odata.bind");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Detail.Contains("sprk_assignedto"));
    }

    [Fact]
    public async Task CreateAsync_AMappedWorkAssignmentFilingLookupTheCallerCannotAppendTo_IsRefused_AndNothingIsWritten()
    {
        // Fix round 1 (review K-1): a work assignment's own filing column (SecureRootInheritance.FilingColumnsOf), mapped
        // from the project the caller names, is the caller's to file under: AppendTo it fails refuses, never drops.
        _user.Columns["sprk_workassignment"] = new() { ["sprk_regardingmatter"] = ("Lookup", true) };
        Profile("sprk_project", "sprk_workassignment", Rule("sprk_regardingmatter", "sprk_regardingmatter", Lookup));
        _scripted.NoAppendTo.Add(SecureMatter);

        var outcome = await Service().CreateAsync(
            Request("sprk_workassignment", new Dictionary<string, object?> { ["sprk_name"] = "Review the lease" },
                mappingSource: ("sprk_project", SourceProject)),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied,
            "a mapped work assignment filing lookup files the row as the caller's would: refused, never dropped");
        Problem(outcome, "sprk_workassignment").Status.Should().Be(StatusCodes.Status404NotFound);
        _creates.Should().BeEmpty();
        _journal.Should().NotContain(["owner", "create"]);
    }

    [Fact]
    public async Task CreateAsync_AWorkAssignmentFilingLookupMarkedServerSet_IsStillAskedAppendTo_UnderAnOrdinaryRecord()
    {
        // Fix round 1 (review F-1): the plan asks AppendTo on a SECURE record only, so a filing lookup the server set
        // under an ORDINARY one is asked at step 6 — the same uniform answer as the unmarked request's.
        _scripted.NoAppendTo.Add(OrdinaryMatter);

        var marked = await Service().CreateAsync(
            WorkAssignmentUnder(OrdinaryMatter, serverSet: "sprk_regardingmatter"), AsCaller(), CancellationToken.None);
        var unmarked = await Service().CreateAsync(WorkAssignmentUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        marked.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        Problem(marked, "sprk_workassignment").Status.Should().Be(StatusCodes.Status404NotFound);
        JsonSerializer.Serialize(Problem(marked, "sprk_workassignment"))
            .Should().Be(JsonSerializer.Serialize(Problem(unmarked, "sprk_workassignment")),
                "marked server-set or not, a record the caller may not file under answers alike");
        _creates.Should().BeEmpty();
        _journal.Should().NotContain("create");
    }

    [Fact]
    public async Task CreateAsync_AWorkAssignmentFilingLookupMarkedServerSet_IsAskedBeforeThePlanReadsTheRecord()
    {
        // Review pass 2 (finding A): asked after the plan, a plan that refused first — live, a matter whose secure flag
        // cannot be read (409); here, a host whose inheritance cannot read the record at all — answered differently from
        // step 5's uniform not-found, telling a record that exists from a missing one. Asked before it, the answers match.
        _rootFiling = SecureRootFilingGateFixtures.Unregistered;
        _scripted.NoAppendTo.Add(OrdinaryMatter);

        var marked = await Service().CreateAsync(
            WorkAssignmentUnder(OrdinaryMatter, serverSet: "sprk_regardingmatter"), AsCaller(), CancellationToken.None);
        var unmarked = await Service().CreateAsync(WorkAssignmentUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        marked.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        marked.Refusal.OwnerDecision!.RefusalCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "refused in the shape of the plan's own AppendTo callback");
        JsonSerializer.Serialize(Problem(marked, "sprk_workassignment"))
            .Should().Be(JsonSerializer.Serialize(Problem(unmarked, "sprk_workassignment")));
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AWorkAssignmentFilingLookupMarkedServerSet_TheCallerMayAppendTo_IsCreatedThere()
    {
        var outcome = await Service().CreateAsync(
            WorkAssignmentUnder(OrdinaryMatter, serverSet: "sprk_regardingmatter"), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_RegardingMatter@odata.bind")
            .Should().Be($"/sprk_matters({OrdinaryMatter:D})");
    }

    // =====================================================================================
    // Fix round 3 (UAC-r2 review, F-B) — "filing" is also the owner decision's own test (OwnedChildWrite.ParentsOf)
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AWorkAssignmentsRegardingCommunicationMarkedServerSet_IsAskedAppendToBeforeThePlan_AndRefused()
    {
        // No filing list names sprk_workassignment.sprk_regardingcommunication (#2190), yet the owner decision files the row
        // under the communication. Server-set, it skipped step 5's AppendTo, so it is asked before the plan (must fire).
        _scripted.NoAppendTo.Add(Communication);

        var marked = await Service().CreateAsync(WorkAssignmentUnderCommunication(serverSet: true), AsCaller(), CancellationToken.None);

        marked.Refusal.Should().NotBeNull("a row is never filed under a record the caller was not asked about");
        marked.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        marked.Refusal.OwnerDecision!.RefusalCode.Should().Be(DataverseUserClientErrorCodes.AccessDenied,
            "refused by step 6's pre-plan AppendTo, in the plan's own shape");
        Problem(marked, "sprk_workassignment").Status.Should().Be(StatusCodes.Status404NotFound);
        _creates.Should().BeEmpty();
        _journal.Should().NotContain(["owner", "create"]);
    }

    [Fact]
    public async Task CreateAsync_AMappedWorkAssignmentRegardingCommunicationTheCallerCannotAppendTo_IsRefused_NotDropped()
    {
        // Closes review K-5: a mapped lookup the owner decision files the row under is the caller's own (must fire).
        _user.Columns["sprk_workassignment"] = new() { ["sprk_regardingcommunication"] = ("Lookup", true) };
        _sources[("sprk_project", SourceProject)]["sprk_communication"] = new EntityReference("sprk_communication", Communication);
        Profile("sprk_project", "sprk_workassignment", Rule("sprk_communication", "sprk_regardingcommunication", Lookup));
        _scripted.NoAppendTo.Add(Communication);

        var outcome = await Service().CreateAsync(
            Request("sprk_workassignment", new Dictionary<string, object?> { ["sprk_name"] = "Review the lease" },
                mappingSource: ("sprk_project", SourceProject)),
            AsCaller(), CancellationToken.None);

        outcome.Refusal.Should().NotBeNull("a mapped filing lookup is refused, never dropped");
        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied);
        Problem(outcome, "sprk_workassignment").Status.Should().Be(StatusCodes.Status404NotFound);
        _creates.Should().BeEmpty();
        _journal.Should().NotContain(["owner", "create"]);
    }

    [Fact]
    public async Task CreateAsync_AChildsOwnershipParentLookupNoListNames_MarkedServerSet_IsRefusedBeforeMappingReadsAnything()
    {
        // An invoice's sprk_regardingworkassignment is in no lineage list, yet the owner decision files the invoice under the
        // work assignment: server-set, it is the K-2 refusal (must fire). Review K-1: refused before step 3 reads anything.
        Profile("sprk_matter", "sprk_invoice", Rule("sprk_matterdescription", "sprk_description", Memo));
        var workAssignment = Guid.NewGuid();
        var item = new DataverseWriteItemMapper.MappedItem(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["sprk_invoicenumber"] = "INV-1",
                ["sprk_RegardingWorkAssignment@odata.bind"] = $"/sprk_workassignments({workAssignment:D})",
            }),
            ["sprk_invoicenumber", "sprk_regardingworkassignment"])
        {
            Lookups = [new DataverseWriteItemMapper.MappedLookup(
                "sprk_regardingworkassignment", "sprk_workassignment", "sprk_workassignments", workAssignment)],
        };
        var request = new RecordCreationRequest("sprk_invoice", new RecordCreationPayload.Mapped(item))
        {
            CorrelationId = FixedCorrelation,
            ServerSetLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_regardingworkassignment" },
            Mapping = RecordCreationMapping.ForCaller(_probe, CallerToken, "sprk_matter", OrdinaryMatter),
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.ServerSetFilingLookupCode);
        Problem(outcome, "sprk_invoice").Status.Should().Be(StatusCodes.Status500InternalServerError);
        _journal.Should().NotContain(["profile", "probe", "source-read", "user:check", "owner", "create"]);
        outcome.Steps.Should().NotContain(s => s.Step == RecordCreationStep.FieldMapping, "refused before step 3");
        outcome.Steps[^1].Should().Be(new RecordCreationStepRecord(
            RecordCreationStep.CallerChecks, RecordCreationStepState.Refused, RecordCreationRefusal.ServerSetFilingLookupCode));
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ACitationLookupMarkedServerSet_IsNotAFilingLookup_AndPassesTheGuard()
    {
        // A Signal CITES its rule (RecordOwnershipResolver.IsCitation): the owner decision files nothing under it, so it is
        // no filing lookup, server-set or not (must not fire).
        var policy = Guid.NewGuid();
        var item = new DataverseWriteItemMapper.MappedItem(
            JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["sprk_name"] = "Spend over budget",
                ["sprk_Policy@odata.bind"] = $"/sprk_policies({policy:D})",
            }),
            ["sprk_name", "sprk_policy"])
        {
            Lookups = [new DataverseWriteItemMapper.MappedLookup("sprk_policy", "sprk_policy", "sprk_policies", policy)],
        };
        var request = new RecordCreationRequest("sprk_signal", new RecordCreationPayload.Mapped(item))
        {
            CorrelationId = FixedCorrelation,
            ServerSetLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_policy" },
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Refusal?.Code.Should().NotBe(RecordCreationRefusal.ServerSetFilingLookupCode);
        _journal.Should().Contain("user:check", "the create went past the guard to step 5's checks");
    }

    [Fact]
    public async Task CreateAsync_AWorkAssignmentsAssignedToMarkedServerSet_IsNotAFilingLookup_AndCostsNoAppendTo()
    {
        // A person is no ownership parent: a server-set sprk_assignedto stays exempt from AppendTo (must not fire).
        _scripted.NoAppendToIn.Add(("contacts", Assignee));
        var request = Request("sprk_workassignment", new Dictionary<string, object?>
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            ["sprk_AssignedTo@odata.bind"] = $"/contacts({Assignee:D})",
        }, mapping: false) with
        {
            ServerSetLookupColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_assignedto" },
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({Assignee:D})");
    }

    // =====================================================================================
    // #1614 — a Project's pair is the caller's, AppendTo-checked; a rule never writes it
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AProjectFiledByItsPairUnderAMatterTheCallerCannotAppendTo_IsRefused_EvenWithAMappingProfile()
    {
        _scripted.RecordTypes[MatterType] = "sprk_matter";
        _scripted.NoAppendTo.Add(OrdinaryMatter);
        Profile("sprk_matter", "sprk_project",
            Rule(null, SecureRootInheritance.PairIdColumn, Text, Default, OrdinaryMatter.ToString("D")),
            Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_project", new Dictionary<string, object?>
            {
                ["sprk_projectname"] = "Phase 2",
                [SecureRootInheritance.PairIdColumn] = OrdinaryMatter.ToString("D"),
                ["sprk_RegardingRecordType@odata.bind"] = $"/sprk_recordtype_refs({MatterType:D})",
            }),
            AsCaller(), CancellationToken.None);

        outcome.Refusal!.Kind.Should().Be(RecordCreationRefusalKind.NotFoundOrDenied, "#1614: the pair's record costs AppendTo");
        Problem(outcome, "sprk_project").Status.Should().Be(StatusCodes.Status404NotFound);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ACallersUntypedPair_IsRefusedAsWithoutMapping_EvenWhenAMappedLookupWouldComplete()
    {
        // Fix round 1 (review K-3): the caller's own values are asked alone first, so a mapped sprk_regardingmatter naming
        // the same record never makes a pair the caller sent without its type pass.
        ProjectToEventProfile(); // maps the project's sprk_regardingmatter (SecureMatter), which the caller may AppendTo
        Dictionary<string, object?> Body() => new()
        {
            ["sprk_eventname"] = "Hearing",
            [SecureRootInheritance.PairIdColumn] = SecureMatter.ToString("D"),
        };

        var mapped = await Service().CreateAsync(
            Request("sprk_event", Body(), mappingSource: ("sprk_project", SourceProject)), AsCaller(), CancellationToken.None);
        var unmapped = await Service().CreateAsync(Request("sprk_event", Body(), mapping: false), AsCaller(), CancellationToken.None);

        unmapped.Refusal!.Code.Should().Be(RecordCreationRefusal.InvalidPayloadCode, "task 013's answer to an untyped pair");
        mapped.Refusal.Should().NotBeNull("mapping never makes the caller's own payload pass");
        JsonSerializer.Serialize(Problem(mapped, "sprk_event")).Should().Be(JsonSerializer.Serialize(Problem(unmapped, "sprk_event")));
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ARuleTargetingTheRegardingPairOrItsResolverColumns_IsSkipped_AndThePairIsNeverMapped()
    {
        Profile("sprk_matter", "sprk_event",
            Rule(null, SecureRootInheritance.PairIdColumn, Text, Default, OrdinaryMatter.ToString("D")),
            Rule(null, "  SPRK_RegardingRecordName ", Text, Default, "Acme"),
            Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Keys.Should().NotContain(k => k.StartsWith("sprk_regardingrecord", StringComparison.OrdinalIgnoreCase));
        StringOf(fields, "sprk_description").Should().Be("From the matter");
        outcome.MappingWarnings.Should().HaveCount(2)
            .And.OnlyContain(w => w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Column == null);
    }

    // =====================================================================================
    // FR-02 fill-only, FR-03 source, FR-04 Read-as-caller first, never an existence oracle
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_TheCallersValueWins_AndAColumnTheyLeftEmptyIsFilled_InTheSameCreate()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, extra: ("sprk_description", "Mine")), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("Mine");
        StringOf(fields, "sprk_location").Should().Be("Courtroom 4");
        StringOf(fields, "sprk_eventname").Should().Be("Hearing");
        _journal.Should().ContainInOrder("user:whoami", "profile", "probe", "source-read", "user:columns", "owner", "create");
        _journal.IndexOf("probe").Should().BeLessThan(_journal.IndexOf("source-read"), "FR-04: Read is asked BEFORE the source is read");
        _probe.Asked.Should().ContainSingle().Which.Should().Be((CallerToken, "sprk_matters", OrdinaryMatter));
    }

    [Fact]
    public async Task CreateAsync_ARuleWhoseTargetIsSpelledDifferently_StillSeesTheCallersValue_AndNeverOverwritesIt()
    {
        // The caller's columns are logical names; a profile authored as "  SPRK_Description " targets the same column.
        Profile("sprk_matter", "sprk_event",
            Rule("sprk_matterdescription", "  SPRK_Description ", Memo),
            Rule("sprk_location", " Sprk_Location", Text));

        var outcome = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, extra: ("sprk_description", "Mine")), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("Mine", "FR-02: fill-only, whatever the rule's spelling");
        StringOf(fields, "sprk_location").Should().Be("Courtroom 4");
        fields.Keys.Should().OnlyContain(k => k == k.Trim() && !k.StartsWith("SPRK_", StringComparison.Ordinal) && !k.StartsWith("Sprk_", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CreateAsync_AMissingSourceAndOneTheCallerCannotRead_GiveTheSameOutcome_AndReadNothing()
    {
        MatterToEventProfile();
        // Fix round 1 (review F-2): the denied source EXISTS, with every value the profile copies, and the caller holds
        // rights on it — only not Read. The missing one is not there at all.
        _sources[("sprk_matter", DeniedMatter)] = _sources[("sprk_matter", OrdinaryMatter)];
        _rights[DeniedMatter] = AccessRights.Write | AccessRights.AppendTo | AccessRights.Append;

        var missing = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", MissingMatter)), AsCaller(), CancellationToken.None);
        var denied = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", DeniedMatter)), AsCaller(), CancellationToken.None);

        missing.Succeeded.Should().BeTrue(missing.Refusal?.Detail);
        denied.Succeeded.Should().BeTrue(denied.Refusal?.Detail);
        denied.MappingWarnings.Should().Equal(missing.MappingWarnings);
        denied.Warnings.Should().Equal(missing.Warnings);
        var warning = missing.MappingWarnings.Should().ContainSingle().Subject;
        warning.Should().Be(new RecordCreationWarning(
            RecordCreationWarning.MappingSourceUnavailableCode, null, MappingSourceReadProbe.SourceUnavailableWarning));
        _creates.Should().HaveCount(2);
        _creates[0].Fields.Keys.Order().Should().Equal(_creates[1].Fields.Keys.Order());
        _creates[0].Fields.Should().NotContainKey("sprk_description");
        _sourceReads.Should().BeEmpty("neither source is read: the probe answered first");
        _probe.Asked.Select(a => a.Id).Should().Equal(MissingMatter, DeniedMatter);
    }

    // =====================================================================================
    // #2188 / DI-004 — the app-only source read is masked to what the CALLER can read before any rule sees it
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_ASourceColumnTheCallerCannotRead_IsNotCopied_ButTheReadableOnesAre()
    {
        // Must fire: the app read returns sprk_location ("Courtroom 4"); field security hides it from the caller.
        MatterToEventProfile();
        _callerRead.Secured.Add("sprk_location");

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_location", "#2188: a column the caller cannot read is never copied");
        fields.Values.Select(v => v?.ToString()).Should().NotContain(v => v != null && v.Contains("Courtroom 4"));
        StringOf(fields, "sprk_description").Should().Be("From the matter", "a column the caller can read is copied");
        StringOf(fields, "sprk_AssignedTo@odata.bind").Should().Be($"/contacts({Assignee:D})", "a readable lookup is copied");
        outcome.MappingWarnings.Should().BeEmpty("a hidden column looks exactly like an empty one: a Copy skips it silently");
        _callerRead.Paths.Should().Equal($"sprk_matters({OrdinaryMatter:D})");
        _journal.Should().ContainInOrder("probe", "source-read", "user:source-read", "user:columns", "owner", "create");
    }

    [Theory]
    [InlineData("fault")]
    [InlineData("timeout")]
    [InlineData("403")]
    [InlineData("500")]
    [InlineData("no-user-context")]
    public async Task CreateAsync_WhenTheCallerReadOfTheSourceFails_NothingIsMapped_WithTheUniformWarning_AndTheRowIsCreated(
        string failure)
    {
        MatterToEventProfile();
        _sources[("sprk_matter", DeniedMatter)] = _sources[("sprk_matter", OrdinaryMatter)];
        _rights[DeniedMatter] = AccessRights.Write | AccessRights.AppendTo | AccessRights.Append;
        switch (failure)
        {
            case "fault": _callerRead.Throw = new HttpRequestException("caller read failed at 10.0.0.9"); break;
            case "timeout": _callerRead.Throw = new TaskCanceledException("caller read timed out"); break; // not the caller's token
            case "403": _callerRead.FailWithStatus = 403; break;
            case "500": _callerRead.FailWithStatus = 500; break;
            case "no-user-context": _callerRead.FailWithStatus = 0; break; // no caller context (OBO unavailable)
        }

        var failed = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);
        var denied = await Service().CreateAsync(
            EventUnder(OrdinaryMatter, mappingSource: ("sprk_matter", DeniedMatter)), AsCaller(), CancellationToken.None);

        failed.Succeeded.Should().BeTrue("a mapping failure never refuses the create: {0}", failed.Refusal?.Detail);
        _sourceReads.Should().ContainSingle("the app read happened (the probe answered Read) — and was then discarded");
        _callerRead.Paths.Should().ContainSingle();
        failed.MappingWarnings.Should().ContainSingle().Which.Should().Be(new RecordCreationWarning(
            RecordCreationWarning.MappingSourceUnavailableCode, null, MappingSourceReadProbe.SourceUnavailableWarning));
        failed.MappingWarnings.Should().Equal(denied.MappingWarnings, "FR-04: indistinguishable from a source the caller cannot read");
        failed.Warnings.Should().Equal(denied.Warnings);
        _creates.Should().HaveCount(2);
        _creates[0].Fields.Keys.Order().Should().Equal(_creates[1].Fields.Keys.Order(), "nothing mapped, either way");
        _creates[0].Fields.Should().NotContainKey("sprk_description").And.NotContainKey("sprk_location")
            .And.NotContainKey("sprk_AssignedTo@odata.bind");
        StringOf(_creates[0].Fields, "sprk_eventname").Should().Be("Hearing", "the caller's own values are created");
    }

    [Fact]
    public async Task CreateAsync_ADefaultOnlyProfile_ReadsNoSource_SoMakesNoCallerRead_AndStillAppliesItsDefaults()
    {
        Profile("sprk_matter", "sprk_event", Rule(null, "sprk_location", Text, Default, "Room 1"));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_location").Should().Be("Room 1");
        _sourceReads.Should().BeEmpty();
        _callerRead.Paths.Should().BeEmpty("nothing was read app-only, so there is nothing to mask");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_AHalfNamedMappingSource_IsUndetermined_NotInferred()
    {
        MatterToEventProfile();
        var request = EventUnder(OrdinaryMatter) with
        {
            Mapping = RecordCreationMapping.ForCaller(_probe, CallerToken, sourceTable: "sprk_matter"),
        };

        var outcome = await Service().CreateAsync(request, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Code.Should().Be(RecordCreationWarning.MappingSourceUndeterminedCode);
        _journal.Should().NotContain(["profile", "probe", "source-read"]);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
    }

    [Fact]
    public async Task CreateAsync_WithNoMappingAsked_ReadsAndWritesNothingExtra()
    {
        MatterToEventProfile();

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter) with { Mapping = null }, AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.Steps.Single(s => s.Step == RecordCreationStep.FieldMapping).State.Should().Be(RecordCreationStepState.NoOp);
        _journal.Should().NotContain(["profile", "probe", "source-read", "user:columns"]);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
    }

    [Fact]
    public async Task CreateAsync_AsTheSystem_WithAMapping_IsAnArgumentError_AndNothingIsAskedOrWritten()
    {
        // A server writer's item (a system principal's Web API payload is its own argument error).
        var mapped = await DataverseWriteItemMapper.MapAsync(_scripted, "sprk_event", JsonSerializer.SerializeToElement(new
        {
            sprk_eventname = "Hearing",
            sprk_regardingmatter = new { relatedTable = "sprk_matter", recordId = OrdinaryMatter },
        }), CancellationToken.None);
        var request = new RecordCreationRequest("sprk_event", new RecordCreationPayload.Mapped(mapped.Item!))
        {
            Mapping = RecordCreationMapping.ForCaller(_probe, CallerToken),
        };

        var create = () => Service().CreateAsync(
            request, RecordCreationPrincipal.ForSystem(RecordRequester.Of(Caller, CallerOid)), CancellationToken.None);

        (await create.Should().ThrowAsync<ArgumentException>()).Which.Message.Should().Contain("Field mapping");
        _journal.Should().BeEmpty();
        _callerRead.Paths.Should().BeEmpty("#2188: only a caller reaches step 3, so no system create reaches the mask");
        _creates.Should().BeEmpty();
    }

    // =====================================================================================
    // NFR-01 by origin — a fault checking a mapped value drops it; a fault checking the caller's refuses
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_WhenTheCheckOfAMappedValueThrows_ThatValueIsDropped_AndTheRowIsCreated()
    {
        MatterToEventProfile();
        _user.ThrowOn = path => path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                                && path.Contains(Assignee.ToString("D"), StringComparison.OrdinalIgnoreCase)
            ? new TimeoutException("access read timed out at 10.0.0.7")
            : null;

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_AssignedTo@odata.bind");
        var warning = outcome.MappingWarnings.Should().ContainSingle().Subject;
        warning.Column.Should().Be("sprk_assignedto");
        warning.Detail.Should().NotContain("10.0.0.7").And.NotContain("timed out");
    }

    [Fact]
    public async Task CreateAsync_WhenTheCheckOfACallersValueThrows_Is503_AndNothingIsWritten()
    {
        MatterToEventProfile();
        _user.ThrowOn = path => path.Contains("RetrievePrincipalAccess", StringComparison.Ordinal)
                                && path.Contains(OrdinaryMatter.ToString("D"), StringComparison.OrdinalIgnoreCase)
            ? new TimeoutException("access read timed out at 10.0.0.7")
            : null;

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Refusal!.Code.Should().Be(RecordCreationRefusal.CheckFailedCode);
        Problem(outcome, "sprk_event").Status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _creates.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_WhenTheMappingConfigurationCannotBeRead_NothingIsMapped_AndTheRowIsCreated()
    {
        MatterToEventProfile();
        _profileFault = new TimeoutException("profile read timed out at 10.0.0.8");

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.MappingWarnings.Should().ContainSingle().Which.Should().Be(new RecordCreationWarning(
            RecordCreationWarning.MappingUnavailableCode, null, RecordCreationService.MappingConfigurationUnreadable));
        _journal.Should().NotContain(["probe", "source-read"]);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
    }

    [Fact]
    public async Task CreateAsync_WhenMappingFaultsAfterTheSourceRead_NothingIsMapped_AndTheRowIsCreatedWithTheCallersValues()
    {
        MatterToEventProfile();
        _user.ThrowOn = path => path.Contains("AttributeType", StringComparison.Ordinal)
            ? new TimeoutException("metadata read timed out at 10.0.0.9")
            : null;

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.MappingWarnings.Should().ContainSingle().Which.Should().Be(new RecordCreationWarning(
            RecordCreationWarning.MappingUnavailableCode, null, RecordCreationService.MappingFaulted));
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_description");
        StringOf(fields, "sprk_eventname").Should().Be("Hearing");
    }

    [Fact]
    public async Task CreateAsync_WhenMappingFaultsAfterARuleWasSkippedAndAValueDropped_OnlyTheFaultIsReported()
    {
        // Fix round 1 (review K-8): nothing was mapped, so what the faulted step had said about single rules and values
        // is not reported beside "none were copied".
        Profile("sprk_matter", "sprk_event",
            Rule(null, RecordCreatorPerson.Column, Text, Default, "someone"), // a protected target: skipped
            Rule("sprk_matterdescription", "sprk_eventcode", Text), // not valid for create: dropped
            Rule("sprk_matterdescription", "sprk_description", Memo));
        _user.ThrowOn = path => _sourceReads.Count > 0
                                && (path.Contains("ManyToOneRelationships", StringComparison.Ordinal)
                                    || path.Contains("EntitySetName", StringComparison.Ordinal))
            ? new TimeoutException("metadata read timed out at 10.0.0.9")
            : null;

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        outcome.MappingWarnings.Should().ContainSingle().Which.Should().Be(new RecordCreationWarning(
            RecordCreationWarning.MappingUnavailableCode, null, RecordCreationService.MappingFaulted));
        outcome.Warnings.Should().Equal(RecordCreationService.MappingFaulted);
        _creates.Should().ContainSingle().Which.Fields.Should().NotContainKey("sprk_description");
    }

    // =====================================================================================
    // A mapped value the column cannot take is dropped, named — never a failed create
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_AMappedValueTheTargetColumnCannotTake_IsDroppedNamingTheColumn()
    {
        Profile("sprk_matter", "sprk_event",
            Rule("sprk_assignedtointernal", "sprk_regardingproject", Lookup), // a contact into a project lookup
            Rule("sprk_matterdescription", "sprk_eventcode", Text), // not valid for create
            Rule("sprk_matterdescription", "sprk_nosuchcolumn", Text),
            Rule("sprk_location", "sprk_budgetamount", Text), // text into money
            Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        StringOf(fields, "sprk_description").Should().Be("From the matter");
        fields.Keys.Should().NotContain(["sprk_RegardingProject@odata.bind", "sprk_eventcode", "sprk_nosuchcolumn", "sprk_budgetamount"]);
        outcome.MappingWarnings.Select(w => (w.Code, w.Column)).Should().BeEquivalentTo(new[]
        {
            (RecordCreationWarning.MappedValueDroppedCode, (string?)"sprk_regardingproject"),
            (RecordCreationWarning.MappedValueDroppedCode, "sprk_eventcode"),
            (RecordCreationWarning.MappedValueDroppedCode, "sprk_nosuchcolumn"),
            (RecordCreationWarning.MappedValueDroppedCode, "sprk_budgetamount"),
        });
    }

    [Fact]
    public async Task CreateAsync_AMoneyDefaultAndADateCopy_ReachTheBodyAsTheColumnsTakeThem()
    {
        // DI-001 / #2124: the Money column comes from the table's metadata; the Web API takes its amount as a number. The
        // date column's behaviour (DateOnly) decides its wire form.
        _user.DateBehaviours[("sprk_event", "sprk_duedate")] = "DateOnly";
        Profile("sprk_matter", "sprk_event",
            Rule(null, "sprk_budgetamount", Number, Default, "1250.50"),
            Rule("sprk_duedate", "sprk_duedate", CreateTimeFieldMapping.FieldTypeDateTime));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        ((JsonElement)fields["sprk_budgetamount"]!).GetDecimal().Should().Be(1250.50m);
        StringOf(fields, "sprk_duedate").Should().Be("2027-03-04");
        outcome.MappingWarnings.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateAsync_ADateRuleWhoseTargetIsNoLogicalName_NeverReachesTheMetadataFilter_AndIsDroppedNamed()
    {
        // Fix round 1 (review K-9): a rule's target is admin-written text; only a logical name is put in a $filter.
        _user.DateBehaviours[("sprk_event", "sprk_duedate")] = "DateOnly";
        Profile("sprk_matter", "sprk_event",
            Rule("sprk_duedate", "sprk_due'date", CreateTimeFieldMapping.FieldTypeDateTime),
            Rule("sprk_duedate", "sprk_duedate", CreateTimeFieldMapping.FieldTypeDateTime));

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        StringOf(_creates.Should().ContainSingle().Subject.Fields, "sprk_duedate").Should().Be("2027-03-04");
        _user.Paths.Where(p => p.Contains("EntityDefinitions", StringComparison.Ordinal))
            .Should().NotContain(p => p.Contains("due'date", StringComparison.Ordinal));
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappedValueDroppedCode && w.Column == "sprk_due'date");
    }

    // =====================================================================================
    // UAC-r2 D7 — a document's access record is never mapped; its access permission stays fill-only
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_ADocument_NeverMapsItsAccessInheritance_ButFillsItsAccessPermission()
    {
        Profile("sprk_matter", "sprk_document",
            Rule(null, "  SPRK_AccessInheritance ", OptionSet, Default, "100000001"),
            Rule(null, "sprk_accesspermission", OptionSet, Default, "100000002"),
            Rule("sprk_matterdescription", "sprk_documentdescription", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_document", new Dictionary<string, object?>
            {
                ["sprk_documentname"] = "Brief.docx",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            }, mapping: true),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey(AccessInheritance.Column, "D7: the BFF writes a document's access record (must fire)");
        ((JsonElement)fields["sprk_accesspermission"]!).GetInt32().Should().Be(100000002, "fill-only (must not fire)");
        StringOf(fields, "sprk_documentdescription").Should().Be("From the matter");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Detail.Contains(AccessInheritance.Column));
    }

    // =====================================================================================
    // ebilling-r1's review of 013 (items 1 to 3)
    // =====================================================================================

    [Fact]
    public async Task CreateAsync_ThePreWriteRuleSeesExactlyTheMergedItemTheCreateWrites_WithEveryMappedLookupsEntitySet()
    {
        MatterToEventProfile();
        var asked = WithTestRule();

        var outcome = await Service().CreateAsync(EventUnder(OrdinaryMatter), AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var row = asked.Should().ContainSingle().Subject;
        var (_, _, fields) = _creates.Should().ContainSingle().Subject;
        var written = fields.Where(f => f.Key is not "ownerid@odata.bind" and not "sprk_CreatedByPerson@odata.bind")
            .ToDictionary(f => f.Key, f => JsonSerializer.Serialize(f.Value));
        OwnedChildWrite.BodyFields(row.Item.JsonBody).ToDictionary(f => f.Key, f => JsonSerializer.Serialize(f.Value))
            .Should().BeEquivalentTo(written, "item 1: the pre-write rule and the create read the SAME merged item");
        row.Item.Columns.Should().Contain(["sprk_description", "sprk_location", "sprk_assignedto"]);
        row.Item.Lookups.Should().ContainEquivalentOf(
            new DataverseWriteItemMapper.MappedLookup("sprk_assignedto", "contact", "contacts", Assignee),
            "item 2: a mapped lookup carries its entity set");
        row.Item.Lookups.Should().OnlyContain(l => !string.IsNullOrEmpty(l.RelatedEntitySet));
    }

    [Fact]
    public async Task CreateAsync_AnInvoice_NeverMapsASecondCoreLookup_ButStillFillsANonCoreColumn()
    {
        Profile("sprk_matter", "sprk_invoice",
            Rule("sprk_primaryproject", "sprk_project", Lookup),
            Rule("sprk_matterdescription", "sprk_description", Memo));

        var outcome = await Service().CreateAsync(
            Request("sprk_invoice", new Dictionary<string, object?>
            {
                ["sprk_invoicenumber"] = "INV-1",
                ["sprk_Matter@odata.bind"] = $"/sprk_matters({OrdinaryMatter:D})",
            }, mapping: true),
            AsCaller(), CancellationToken.None);

        outcome.Succeeded.Should().BeTrue(outcome.Refusal?.Detail);
        var fields = _creates.Should().ContainSingle().Subject.Fields;
        fields.Should().NotContainKey("sprk_Project@odata.bind", "item 3: the core record is the caller's to name (must fire)");
        StringOf(fields, "sprk_Matter@odata.bind").Should().Be($"/sprk_matters({OrdinaryMatter:D})");
        StringOf(fields, "sprk_description").Should().Be("From the matter", "a non-core column is still filled (must not fire)");
        outcome.MappingWarnings.Should().ContainSingle()
            .Which.Should().Match<RecordCreationWarning>(w =>
                w.Code == RecordCreationWarning.MappingRuleSkippedCode && w.Detail.Contains("sprk_project"));
    }

    // =====================================================================================
    // Harness
    // =====================================================================================

    private void MatterToEventProfile() => Profile("sprk_matter", "sprk_event",
        Rule("sprk_matterdescription", "sprk_description", Memo),
        Rule("sprk_location", "sprk_location", Text),
        Rule("sprk_assignedtointernal", "sprk_assignedto", Lookup));

    private void ProjectToEventProfile() => Profile("sprk_project", "sprk_event",
        Rule("sprk_regardingmatter", "sprk_regardingmatter", Lookup),
        Rule("sprk_projectdescription", "sprk_description", Memo));

    private void Profile(string source, string target, params FieldMappingRuleEntity[] rules) =>
        _profiles[(source, target)] = new FieldMappingProfileEntity
        {
            Name = $"{source} to {target}",
            SourceEntity = source,
            TargetEntity = target,
            IsActive = true,
            Rules = [.. rules.Select((r, i) => { r.ExecutionOrder = i; return r; })],
        };

    private static FieldMappingRuleEntity Rule(
        string? sourceField, string targetField, int targetFieldType, int mappingType = Copy, string? defaultValue = null) => new()
    {
        Name = $"{sourceField ?? "default"} to {targetField.Trim()}",
        SourceField = sourceField ?? string.Empty,
        TargetField = targetField,
        TargetFieldType = targetFieldType,
        MappingType = mappingType,
        DefaultValue = defaultValue,
        IsActive = true,
    };

    private RecordCreationService Service()
    {
        var restampEntities = new Mock<IGenericEntityService>(MockBehavior.Strict);
        restampEntities
            .Setup(e => e.RetrieveAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .Returns((string t, Guid id, string[] c, CancellationToken ct) => _stamps.Service.RetrieveAsync(t, id, c, ct));
        restampEntities
            .Setup(e => e.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns((string t, Guid id, Dictionary<string, object> f, CancellationToken ct) => _stamps.Service.UpdateAsync(t, id, f, ct));
        restampEntities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<QueryExpression>(), It.IsAny<CancellationToken>()))
            .Returns((QueryExpression q, CancellationToken ct) => _stamps.Service.RetrieveMultipleAsync(q, ct));
        restampEntities
            .Setup(e => e.RetrieveMultipleAsync(It.IsAny<FetchExpression>(), It.IsAny<CancellationToken>()))
            .Returns((FetchExpression q, CancellationToken ct) => _stamps.Service.RetrieveMultipleAsync(q, ct));
        var restamper = new CoreAncestorRestamper(restampEntities.Object, _stamps.Resolver, NullLogger<CoreAncestorRestamper>.Instance);

        var shares = SecureChildShareWorld.SynchronizerOver(() => _shareWorld, new FakeRecordShareTable());

        return new RecordCreationService(
            new JournalingResolver(_world.Resolver(null), _journal), _appOnly.Object, _sourceReader.Object,
            _rootFiling(), restamper, shares,
            _scopes ?? new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), _publisher,
            _logger ?? NullLogger<RecordCreationService>.Instance)
        {
            TableRules = t => _tableRules(t),
        };
    }

    /// <summary>Registers a fixture pre-write rule on the event table (the invoice-family rule is not on this branch).</summary>
    private List<PreWriteRow> WithTestRule()
    {
        var asked = new List<PreWriteRow>();
        var rule = RecordTableRules.Get("sprk_event") with
        {
            PreWriteRules =
            [
                new PreWriteRule(new NamedRule("SeamTestRule", "A fixture rule standing in for the invoice-family guard."),
                    (row, _) =>
                    {
                        asked.Add(row);
                        return Task.FromResult<PreWriteRefusal?>(null);
                    }),
            ],
        };
        _tableRules = t => t == "sprk_event" ? rule : RecordTableRules.Find(t);
        return asked;
    }

    private RecordCreationPrincipal AsCaller() => RecordCreationPrincipal.ForCaller(_user, CallerOid);

    /// <summary>An event filed under <paramref name="matter"/> by the caller's own bind, asking for field mapping.</summary>
    private RecordCreationRequest EventUnder(
        Guid matter, Guid? assignedTo = null, (string Column, object Value)? extra = null, (string Table, Guid Id)? mappingSource = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["sprk_eventname"] = "Hearing",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})",
        };
        if (assignedTo is { } contact)
            body["sprk_AssignedTo@odata.bind"] = $"/contacts({contact:D})";
        if (extra is { } more)
            body[more.Column] = more.Value;
        return Request("sprk_event", body, mapping: true, mappingSource);
    }

    /// <summary>A work assignment filed under <paramref name="matter"/> by the caller's bind, asking for no mapping; the
    /// matter marked server-set when <paramref name="serverSet"/> names it.</summary>
    private RecordCreationRequest WorkAssignmentUnder(Guid matter, string? serverSet = null) =>
        Request("sprk_workassignment", new Dictionary<string, object?>
        {
            ["sprk_name"] = "Review the lease",
            ["sprk_RegardingMatter@odata.bind"] = $"/sprk_matters({matter:D})",
        }, mapping: false) with
        {
            ServerSetLookupColumns = serverSet is null ? null : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { serverSet },
        };

    /// <summary>A work assignment filed under <see cref="Communication"/> by the caller's bind, asking for no mapping; the
    /// lookup marked server-set when <paramref name="serverSet"/>.</summary>
    private RecordCreationRequest WorkAssignmentUnderCommunication(bool serverSet) =>
        Request("sprk_workassignment", new Dictionary<string, object?>
        {
            ["sprk_name"] = "Reply to the client",
            ["sprk_RegardingCommunication@odata.bind"] = $"/sprk_communications({Communication:D})",
        }, mapping: false) with
        {
            ServerSetLookupColumns = serverSet
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "sprk_regardingcommunication" }
                : null,
        };

    private RecordCreationRequest Event(object body, (string Table, Guid Id)? mappingSource) =>
        Request("sprk_event", JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(body))!, mapping: true,
            mappingSource);

    /// <summary>A browser writer's Web API body, asking for field mapping read as the caller.</summary>
    private RecordCreationRequest Request(
        string table, Dictionary<string, object?> body, bool mapping = true, (string Table, Guid Id)? mappingSource = null) =>
        new(table, new RecordCreationPayload.WebApi(JsonSerializer.SerializeToElement(body)))
        {
            CorrelationId = FixedCorrelation,
            Mapping = mapping
                ? RecordCreationMapping.ForCaller(_probe, CallerToken, mappingSource?.Table, mappingSource?.Id)
                : null,
        };

    private static Microsoft.AspNetCore.Mvc.ProblemDetails Problem(RecordCreationOutcome outcome, string table) =>
        RecordCreationProblemMap.ToProblemDetails(outcome.Refusal!, table, outcome.CorrelationId);

    private static string? StringOf(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) && value is JsonElement { ValueKind: JsonValueKind.String } text ? text.GetString() : null;

    private static Guid Owner(IReadOnlyDictionary<string, object?> fields) =>
        fields.TryGetValue("ownerid@odata.bind", out var bind) && bind is string path
        && Guid.TryParse(path["/teams(".Length..^1], out var team)
            ? team
            : Guid.Empty;

    /// <summary>The app-only create as Dataverse would store it: every lookup bind as a column, in both worlds.</summary>
    private void Materialize(string table, Guid id, Dictionary<string, object?> fields)
    {
        var lookups = new List<(string Column, string Target, Guid Id)>();
        var shareColumns = new List<(string Column, object Value)>();
        foreach (var (key, value) in fields)
        {
            if (!key.EndsWith("@odata.bind", StringComparison.Ordinal))
                continue;
            var path = value is JsonElement { ValueKind: JsonValueKind.String } element ? element.GetString() : value as string;
            if (path is null)
                continue;
            var trimmed = path.TrimStart('/');
            var set = trimmed[..trimmed.IndexOf('(')];
            var target = Guid.Parse(trimmed[(trimmed.IndexOf('(') + 1)..trimmed.IndexOf(')')]);
            var navigation = key[..^"@odata.bind".Length];
            if (navigation == "ownerid")
            {
                shareColumns.Add(("owningteam", new EntityReference("team", target)));
            }
            else if (set != "systemusers")
            {
                lookups.Add((navigation.ToLowerInvariant(), set.TrimEnd('s'), target));
                shareColumns.Add((navigation.ToLowerInvariant(), new EntityReference(set.TrimEnd('s'), target)));
            }
        }

        _stamps.Row(table, id, [.. lookups]);
        if (SecureChildLineage.IsChild(table))
            _shareWorld.Add(table, id, [.. shareColumns]);
    }

    /// <summary>
    /// The caller's Dataverse: the scripted client, plus the target table's column metadata (<c>AttributeType</c>,
    /// <c>IsValidForCreate</c>, a date column's <c>DateTimeBehavior</c>), journaled by kind, with a thrown-fault switch.
    /// </summary>
    private sealed partial class MappingUserClient(IDataverseUserClient inner, List<string> journal) : IDataverseUserClient
    {
        public Dictionary<string, Dictionary<string, (string Type, bool ValidForCreate)>> Columns { get; } = new();

        public Dictionary<(string Table, string Column), string> DateBehaviours { get; } = new();

        public Func<string, Exception?>? ThrowOn { get; set; }

        /// <summary>Task 018a (F3): a read answered with this NON-throwing failure (a 429/503) instead of its data.</summary>
        public Func<string, DataverseUserResponse?>? FailOn { get; set; }

        /// <summary>Every path read, unescaped, in order.</summary>
        public List<string> Paths { get; } = [];

        [GeneratedRegex(@"^EntityDefinitions\(LogicalName='(?<t>[a-z_]+)'\)")]
        private static partial Regex TableOf();

        [GeneratedRegex(@"LogicalName eq '(?<c>[a-z0-9_]+)'")]
        private static partial Regex AttributeNamed();

        public Task<DataverseUserResponse> GetAsync(string relativePath, CancellationToken cancellationToken)
        {
            var path = Uri.UnescapeDataString(relativePath);
            journal.Add(KindOf(path));
            Paths.Add(path);
            if (ThrowOn?.Invoke(path) is { } fault)
                throw fault;
            if (FailOn?.Invoke(path) is { } failure)
                return Task.FromResult(failure);

            if (path.Contains("AttributeType", StringComparison.Ordinal) || path.Contains("DateTimeAttributeMetadata", StringComparison.Ordinal))
            {
                var table = TableOf().Match(path).Groups["t"].Value;
                var asked = AttributeNamed().Matches(path).Select(m => m.Groups["c"].Value).ToHashSet(StringComparer.Ordinal);
                var columns = Columns.GetValueOrDefault(table) ?? new();
                object[] value = path.Contains("DateTimeAttributeMetadata", StringComparison.Ordinal)
                    ? DateBehaviours.Where(d => d.Key.Table == table && asked.Contains(d.Key.Column))
                        .Select(d => (object)new { LogicalName = d.Key.Column, DateTimeBehavior = new { Value = d.Value } }).ToArray()
                    : columns.Where(c => asked.Contains(c.Key))
                        .Select(c => (object)new { LogicalName = c.Key, AttributeType = c.Value.Type, IsValidForCreate = c.Value.ValidForCreate })
                        .ToArray();
                return Task.FromResult(DataverseUserResponse.Ok(200, JsonSerializer.SerializeToElement(new { value })));
            }

            return inner.GetAsync(relativePath, cancellationToken);
        }

        public Task<DataverseUserResponse> PostAsync(string absoluteApiPath, string jsonBody, CancellationToken cancellationToken) =>
            inner.PostAsync(absoluteApiPath, jsonBody, cancellationToken);

        public Task<DataverseUserResponse> PostAsync(
            string absoluteApiPath, string jsonBody, bool preferRepresentation, CancellationToken cancellationToken) =>
            inner.PostAsync(absoluteApiPath, jsonBody, preferRepresentation, cancellationToken);

        public Task<DataverseUserResponse> PatchAsync(string relativePath, string jsonBody, CancellationToken cancellationToken) =>
            inner.PatchAsync(relativePath, jsonBody, cancellationToken);

        public Task<DataverseUserResponse> DeleteAsync(string relativePath, CancellationToken cancellationToken) =>
            inner.DeleteAsync(relativePath, cancellationToken);

        [GeneratedRegex(@"^[a-z_]+\([0-9a-fA-F-]{36}\)$")]
        private static partial Regex SingleRowRead();

        private static string KindOf(string path) =>
            path == "WhoAmI()" ? "user:whoami"
            : SingleRowRead().IsMatch(path) ? "user:source-read"
            : path.Contains("AttributeType", StringComparison.Ordinal) || path.Contains("DateTimeAttributeMetadata", StringComparison.Ordinal)
                ? "user:columns"
            : path.Contains("ManyToOneRelationships", StringComparison.Ordinal) || path.Contains("EntitySetName", StringComparison.Ordinal)
                ? "user:map"
                : "user:check";
    }

    /// <summary>The production caller-rights probe with only its answer substituted (the seam the probe documents).</summary>
    private sealed class ReadProbe(Func<Guid, AccessRights> rightsOn, List<string> journal)
        : CallerRecordAccessProbe(new HttpClient(), new ConfigurationBuilder().Build(), NullLogger<CallerRecordAccessProbe>.Instance)
    {
        public List<(string? Token, string EntitySet, Guid Id)> Asked { get; } = [];

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            journal.Add("probe");
            Asked.Add((callerBearerToken, entitySet, recordId));
            return Task.FromResult(rightsOn(recordId));
        }
    }

    /// <summary>The REAL resolver, journaled.</summary>
    private sealed class JournalingResolver(IRecordOwnershipResolver inner, List<string> journal) : IRecordOwnershipResolver
    {
        public Task<Guid?> ResolveOwningTeamAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            journal.Add("owner");
            return inner.ResolveOwningTeamAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ResolveOwnerAsync(RecordOwnershipContext context, CancellationToken ct)
        {
            journal.Add("owner");
            return inner.ResolveOwnerAsync(context, ct);
        }

        public Task<RecordOwnerResolution> ReparentAsync(
            RecordReparent request, Func<CancellationToken, Task> applyChange, CancellationToken ct) =>
            inner.ReparentAsync(request, applyChange, ct);
    }

    private sealed class RecordingPublisher : IMembershipEventPublisher
    {
        public Task PublishAsync(MembershipChangedEvent evt, CancellationToken ct) => Task.CompletedTask;
    }
}
