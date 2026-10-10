using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Signals;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Signals;

/// <summary>
/// Real-Dataverse seam for <see cref="SignalWriter"/> (task 030; spec FR-03, FR-14, NFR-08 — rework
/// 2026-10-04 after the independent review's live-verified findings). Writes ONE <c>sprk_signal</c> row
/// against the task 004/005 seed in <c>spaarkedev1</c> using the production writer logic, and deletes it in
/// <see cref="DisposeAsync"/>.
/// </summary>
/// <remarks>
/// <para><b>Two connections, deliberately (F17/F18).</b> <see cref="LiveDataverse.OperatorClient"/> is
/// NEVER impersonated — it is used for the sysadmin business-unit read (<see cref="SysadminAdapter"/>, exactly
/// mirroring <c>SignalsModule</c>'s own split) and for cleanup, because the WRITER principal does not hold
/// Delete on <c>sprk_signal</c> (<c>security-roles.md</c> §3) and a cleanup delete issued as the writer would
/// itself fail. <see cref="LiveDataverse.WriterClient"/> IS impersonated via <c>MSCRMCallerID</c>
/// (<c>CallerId</c>) so Dataverse enforces the WRITER's own privileges for the actual Signal
/// create/retrieve-by-key/update — the same convention <c>SignalPredicateTests.LiveDataverse</c> uses for
/// reads.</para>
/// <para><b>What this seam proves, and what it does NOT.</b> It proves <see cref="SignalWriter"/>'s write-side
/// logic — create-first + reconcile-on-duplicate, the ADR-024 resolver-field write, the FR-14
/// <c>owningbusinessunit</c> derivation + read-back verification, the §0.3 sentence substitution — against
/// REAL <c>sprk_signal</c> schema, running AS the writer principal. It does <b>NOT</b> prove the writer's
/// dedicated managed identity (<c>mi-ontology-writer-dev</c>, task 006) can acquire a token in production,
/// because <see cref="Azure.Identity.ManagedIdentityCredential"/> only resolves inside an Azure-hosted process
/// with an <c>IDENTITY_ENDPOINT</c> — no workstation has one. <see cref="LiveDataverse"/> connects with
/// <see cref="AzureCliCredential"/> (the operator's own <c>az login</c>) — NOT
/// <see cref="DefaultAzureCredential"/>, which task 021's <c>SignalPredicateTests</c> found fails to obtain a
/// token on this workstation. <b>The managed-identity token path is first exercised only after
/// <c>spaarke-bff-dev</c> is next deployed</b> with the config key set.</para>
/// <para><b>Skip-via-return, opt-in.</b> Same convention as <c>SignalPredicateTests</c>: unless
/// <see cref="SignalPredicateTests.UrlEnvVar"/> is set, every test returns immediately — zero live operations in
/// a plain <c>dotnet test</c> run. Set <see cref="SignalPredicateTests.CallerIdEnvVar"/> to the writer principal
/// (<c>3121bf1b-9fbf-f111-aaaf-0022482913fc</c>) so <see cref="LiveDataverse.WriterClient"/> impersonates it.</para>
/// <para><b>Test rows.</b> Every row this test creates is named with the <c>ONTOLOGY DEV TEST 030</c> prefix and
/// deleted (via the NON-impersonated operator client) in <see cref="DisposeAsync"/> — best-effort, so a thrown
/// assertion still attempts cleanup. No row is left behind on a passing OR a failing run.</para>
/// </remarks>
[Trait("status", "repaired")]
[Trait("Category", "Live")]
public sealed class SignalWriterSeamTests : IClassFixture<SignalWriterSeamTests.LiveDataverse>, IAsyncLifetime
{
    // task 004/005 seed (spaarkedev1; notes/seed-data-state.md).
    private static readonly Guid PolicyId = Guid.Parse("4d204810-61bf-f111-aaaf-0022482913fc");
    private static readonly Guid PolicyVersionId = Guid.Parse("42b3e716-61bf-f111-aaaf-0022482913fc");
    private static readonly Guid PositiveMatter = Guid.Parse("2444af6d-e1f2-f011-8406-7ced8d1dc988"); // REAL-2026-123456.01
    private static readonly Guid PositiveCommunication = Guid.Parse("f00b6389-8fbf-f111-aaaf-0022482913fc"); // on PositiveMatter

    private const string TestPolicyCode = "ONTOLOGY-DEV-TEST-030";

    /// <summary>The BU1 matter's business unit (R10, second independent review) — used ONLY as a sanity
    /// cross-check; the actual assertion reads the matter's owningbusinessunit INDEPENDENTLY at run time
    /// rather than trusting this literal alone.</summary>
    private static readonly Guid ExpectedPositiveMatterBusinessUnitId = Guid.Parse("cb15f587-baa0-f111-aaac-000d3a99d1d7");

    private readonly LiveDataverse _dv;

    public SignalWriterSeamTests(LiveDataverse dv)
    {
        _dv = dv;
    }

    [Fact]
    public async Task WriteAsync_MatterSubject_CreatesThenReconciles_AndSetsOwningBusinessUnit()
    {
        if (!_dv.IsLive) return;

        var writer = BuildWriter();
        var request = Request(PositiveMatter, "sprk_matter", "REAL-2026-123456.01");

        var first = await writer.WriteAsync(request);
        var second = await writer.WriteAsync(request);

        first.Created.Should().BeTrue();
        second.Created.Should().BeFalse("re-evaluation must reconcile on sprk_dedupekey, never duplicate");
        second.SignalId.Should().Be(first.SignalId);
        first.GroupingMatterId.Should().Be(PositiveMatter);
        first.OwningBusinessUnitId.Should().NotBe(Guid.Empty);

        // R10 (second independent review): assert against the matter's OWN business unit, read
        // INDEPENDENTLY here -- not against first.OwningBusinessUnitId, which is the writer's OWN reported
        // value and would make this a tautology (the writer agreeing with itself proves nothing about
        // whether it read the real matter correctly).
        var matterRow = await _dv.OperatorClient.RetrieveAsync("sprk_matter", PositiveMatter, new ColumnSet("owningbusinessunit"));
        var matterBusinessUnitId = matterRow.GetAttributeValue<EntityReference>("owningbusinessunit").Id;
        matterBusinessUnitId.Should().Be(ExpectedPositiveMatterBusinessUnitId,
            "sanity check -- if the seed data's BU assignment ever changes, this test's premise needs re-checking too");

        var row = await _dv.OperatorClient.RetrieveAsync("sprk_signal", first.SignalId,
            new ColumnSet("ownerid", "owningbusinessunit", "sprk_matter", "sprk_regardingmatter", "sprk_dedupekey", "sprk_firstdetected", "sprk_lastevaluated"));
        row.GetAttributeValue<EntityReference>("owningbusinessunit").Id.Should().Be(matterBusinessUnitId,
            "the Signal's BU must match the MATTER's independently-read BU, not merely echo what the writer itself computed");
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(PositiveMatter);
    }

    [Fact]
    public async Task WriteAsync_CommunicationSubject_DerivesTheRealGroupingMatter()
    {
        if (!_dv.IsLive) return;

        var writer = BuildWriter();
        var request = Request(PositiveCommunication, "sprk_communication", "2027 rate schedule update");

        var result = await writer.WriteAsync(request);

        result.GroupingMatterId.Should().Be(PositiveMatter,
            "sprk_communication.sprk_regardingmatter on the seeded row points at the positive matter");
    }

    /// <summary>
    /// Task 039 (D-33) live gate, writer half: a Signal whose subject or grouping matter is under a SECURE record is
    /// created owned by the Secure Record Owners team — set in the create, by the writer principal, through uac-r2's real
    /// resolver — with the business unit derived from that team. Opt-in on top of the seam's own gate: set
    /// <see cref="SecureSubjectEnvVar"/> to <c>logicalname:id</c> of a probe subject (prefix <c>zz-039-</c>) on a Secure
    /// matter, or filed under a Secure record. The row is swept by <see cref="DisposeAsync"/> like every other row here.
    /// </summary>
    [Fact]
    public async Task WriteAsync_SubjectUnderASecureRecord_IsOwnedByTheSecureRecordOwnersTeam_SetInTheCreate()
    {
        if (!_dv.IsLive) return;
        if (Environment.GetEnvironmentVariable(SecureSubjectEnvVar) is not { Length: > 0 } raw) return;
        var parts = raw.Split(':', 2);
        var (subjectEntity, subjectId) = (parts[0], Guid.Parse(parts[1]));

        var result = await BuildWriter().WriteAsync(Request(subjectId, subjectEntity, "zz-039 secure probe subject"));

        var team = (await _dv.OperatorClient.RetrieveMultipleAsync(new QueryExpression("team")
        {
            ColumnSet = new ColumnSet("teamid", "businessunitid"),
            Criteria = { Conditions = { new ConditionExpression("name", ConditionOperator.Equal, "Secure Record Owners") } },
        })).Entities.Should().ContainSingle().Subject;
        var row = await _dv.OperatorClient.RetrieveAsync("sprk_signal", result.SignalId,
            new ColumnSet("ownerid", "owningteam", "owninguser", "owningbusinessunit"));

        result.Created.Should().BeTrue();
        result.SecureOwnerTeamId.Should().Be(team.Id);
        row.GetAttributeValue<EntityReference>("owningteam")?.Id.Should().Be(team.Id, "the Secure Record Owners team owns it");
        row.GetAttributeValue<EntityReference>("owninguser").Should().BeNull("never the writer on the secure path");
        row.GetAttributeValue<EntityReference>("owningbusinessunit").Id
            .Should().Be(team.GetAttributeValue<EntityReference>("businessunitid").Id, "the business unit derives from the team");
    }

    // ── Task 037: Do-lane subjects (event, To Do, work assignment) against spaarkedev1 ───────────────────────────
    // Probe subjects are created by the OPERATOR client with the zz-037- prefix and deleted in DisposeAsync; the Signals
    // are written by the WRITER principal (impersonated), so Dataverse enforces the writer's own privileges.

    private const string DoTestPolicyCode = "ONTOLOGY-DEV-TEST-037";
    private readonly List<(string Entity, Guid Id)> _probeRows = new();

    private async Task<Guid> CreateProbeAsync(Entity probe)
    {
        var id = await _dv.OperatorClient.CreateAsync(probe);
        _probeRows.Add((probe.LogicalName, id));
        return id;
    }

    private static SignalWriteRequest DoRequest(Guid subjectId, string subjectEntity) => new(
        PolicyId: PolicyId,
        PolicyCode: DoTestPolicyCode,
        PolicyVersionId: PolicyVersionId,
        SubjectEntityLogicalName: subjectEntity,
        SubjectId: subjectId,
        SubjectDisplayName: "zz-037 probe",
        ShortHeadline: "ONTOLOGY DEV TEST 037 - seam sanity check",
        Lane: SignalWriter.LaneDo,
        Severity: SignalWriter.SeverityInfo,
        MessageTemplate: "ONTOLOGY DEV TEST 037: {{note}}",
        FactValues: new Dictionary<string, object?> { ["note"] = "seam test row, safe to delete" });

    private async Task<string> CatalogNameAsync(Guid recordTypeRefId) =>
        (await _dv.OperatorClient.RetrieveAsync("sprk_recordtype_ref", recordTypeRefId, new ColumnSet("sprk_recordlogicalname")))
            .GetAttributeValue<string>("sprk_recordlogicalname");

    private async Task<Guid> BusinessUnitOfAsync(string entity, Guid id) =>
        (await _dv.OperatorClient.RetrieveAsync(entity, id, new ColumnSet("owningbusinessunit")))
            .GetAttributeValue<EntityReference>("owningbusinessunit").Id;

    private static readonly ColumnSet SignalColumns = new(
        "ownerid", "owningbusinessunit", "sprk_matter", "sprk_corerecordtype", "sprk_corerecordid", "sprk_duedate", "sprk_signalstatus",
        "sprk_regardingevent", "sprk_regardingtodo", "sprk_regardingworkassignment", "sprk_regardingrecordtype", "sprk_regardingrecordid");

    [Theory]
    [InlineData("sprk_event")]
    [InlineData("sprk_todo")]
    public async Task WriteAsync_EventAndTodoUnderAMatter_GroupUnderTheMatter_CreateAndReconcile_RefreshDueDate(string subjectEntity)
    {
        if (!_dv.IsLive) return;

        var probe = new Entity(subjectEntity)
        {
            [subjectEntity == "sprk_event" ? "sprk_eventname" : "sprk_name"] = "zz-037-" + subjectEntity,
            ["sprk_regardingmatter"] = new EntityReference("sprk_matter", PositiveMatter),
            ["sprk_duedate"] = new DateTime(2026, 11, 2),
        };
        var subjectId = await CreateProbeAsync(probe);
        var writer = BuildWriter();

        var first = await writer.WriteAsync(DoRequest(subjectId, subjectEntity));
        var row = await _dv.OperatorClient.RetrieveAsync("sprk_signal", first.SignalId, SignalColumns);

        first.Created.Should().BeTrue();
        (await CatalogNameAsync(row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id)).Should().Be("sprk_matter");
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(PositiveMatter.ToString("D"));
        row.GetAttributeValue<EntityReference>("sprk_matter").Id.Should().Be(PositiveMatter);
        row.GetAttributeValue<EntityReference>("owningbusinessunit").Id.Should().Be(await BusinessUnitOfAsync("sprk_matter", PositiveMatter));
        row.GetAttributeValue<DateTime?>("sprk_duedate")!.Value.Date.Should().Be(new DateTime(2026, 11, 2));

        // The subject's date changes while the Signal is open: reconcile refreshes it.
        await _dv.OperatorClient.UpdateAsync(new Entity(subjectEntity, subjectId) { ["sprk_duedate"] = new DateTime(2026, 11, 9) });
        var second = await writer.WriteAsync(DoRequest(subjectId, subjectEntity));
        second.Created.Should().BeFalse();
        second.SignalId.Should().Be(first.SignalId);
        (await _dv.OperatorClient.RetrieveAsync("sprk_signal", first.SignalId, SignalColumns))
            .GetAttributeValue<DateTime?>("sprk_duedate")!.Value.Date.Should().Be(new DateTime(2026, 11, 9));

        // Resolved: the date the Signal last saw is frozen.
        await _dv.OperatorClient.UpdateAsync(new Entity("sprk_signal", first.SignalId) { ["sprk_signalstatus"] = new OptionSetValue(100000002) });
        await _dv.OperatorClient.UpdateAsync(new Entity(subjectEntity, subjectId) { ["sprk_duedate"] = new DateTime(2026, 11, 20) });
        await writer.WriteAsync(DoRequest(subjectId, subjectEntity));
        (await _dv.OperatorClient.RetrieveAsync("sprk_signal", first.SignalId, SignalColumns))
            .GetAttributeValue<DateTime?>("sprk_duedate")!.Value.Date.Should().Be(new DateTime(2026, 11, 9));
    }

    [Fact]
    public async Task WriteAsync_WorkAssignmentSubject_IsItsOwnCoreRecord_AndCarriesResponseDueDate()
    {
        if (!_dv.IsLive) return;

        var subjectId = await CreateProbeAsync(new Entity("sprk_workassignment")
        {
            ["sprk_name"] = "zz-037-workassignment",
            ["sprk_responseduedate"] = new DateTime(2026, 11, 3),
        });

        var result = await BuildWriter().WriteAsync(DoRequest(subjectId, "sprk_workassignment"));
        var row = await _dv.OperatorClient.RetrieveAsync("sprk_signal", result.SignalId, SignalColumns);

        (await CatalogNameAsync(row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Id)).Should().Be("sprk_workassignment");
        row.GetAttributeValue<string>("sprk_corerecordid").Should().Be(subjectId.ToString("D"));
        row.GetAttributeValue<EntityReference>("sprk_matter").Should().BeNull("a work assignment is its own core record, not its matter (D-36)");
        row.GetAttributeValue<EntityReference>("sprk_regardingworkassignment").Id.Should().Be(subjectId);
        row.GetAttributeValue<EntityReference>("owningbusinessunit").Id.Should().Be(await BusinessUnitOfAsync("sprk_workassignment", subjectId));
        row.GetAttributeValue<DateTime?>("sprk_duedate")!.Value.Date.Should().Be(new DateTime(2026, 11, 3));
    }

    [Fact]
    public async Task WriteAsync_TodoWithNoMatterAndNoProject_HasNoCoreRecord_IsOwnedByTheTodosOwner()
    {
        if (!_dv.IsLive) return;

        var subjectId = await CreateProbeAsync(new Entity("sprk_todo") { ["sprk_name"] = "zz-037-todo-nocore" });
        var todoOwner = (await _dv.OperatorClient.RetrieveAsync("sprk_todo", subjectId, new ColumnSet("ownerid")))
            .GetAttributeValue<EntityReference>("ownerid");

        var first = await BuildWriter().WriteAsync(DoRequest(subjectId, "sprk_todo"));
        var second = await BuildWriter().WriteAsync(DoRequest(subjectId, "sprk_todo"));
        var row = await _dv.OperatorClient.RetrieveAsync("sprk_signal", first.SignalId, SignalColumns);

        first.Created.Should().BeTrue();
        second.Created.Should().BeFalse();
        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Should().BeNull();
        row.GetAttributeValue<string>("sprk_corerecordid").Should().BeNull();
        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(todoOwner.Id);
    }

    [Fact]
    public async Task WriteAsync_TeamOwnedTodoWithNoCoreRecord_IsOwnedByThatTeam()
    {
        if (!_dv.IsLive) return;

        // 12 of the 25 no-core To Dos in dev are owned by a BU default team (BFF-created rows, task 080).
        var teamId = Guid.Parse("cf15f587-baa0-f111-aaac-000d3a99d1d7"); // Spaarke Business Unit 1 (Owner team)
        var subjectId = await CreateProbeAsync(new Entity("sprk_todo")
        {
            ["sprk_name"] = "zz-037-todo-nocore-team",
            ["ownerid"] = new EntityReference("team", teamId),
        });

        var result = await BuildWriter().WriteAsync(DoRequest(subjectId, "sprk_todo"));
        var row = await _dv.OperatorClient.RetrieveAsync("sprk_signal", result.SignalId, SignalColumns);

        row.GetAttributeValue<EntityReference>("ownerid").Id.Should().Be(teamId);
        row.GetAttributeValue<EntityReference>("sprk_corerecordtype").Should().BeNull();
    }

    /// <summary>Opt-in probe subject for the task 039 live gate, as <c>logicalname:id</c>.</summary>
    public const string SecureSubjectEnvVar = "ONTOLOGY_039_SECURE_SUBJECT";

    private SignalWriter BuildWriter()
    {
        var writerClient = new OntologyWriterDataverseClient(() => _dv.WriterClient, NullLogger<OntologyWriterDataverseClient>.Instance);
        var sysadmin = new SysadminAdapter(_dv.OperatorClient);
        // Task 039: uac-r2's REAL resolver over the same sysadmin seam the BFF gives it (empty config = its defaults).
        var ownership = new RecordOwnershipResolver(
            sysadmin, new ConfigurationBuilder().Build(), NullLogger<RecordOwnershipResolver>.Instance);
        // Task 037: uac-r2's REAL core-ancestor resolver. The probe says every stamp column exists (they do, live).
        var coreAncestors = new CoreAncestorResolver(
            sysadmin,
            (_, _) => Task.FromResult<IReadOnlySet<string>>(new HashSet<string>(
                CoreAncestorResolver.CoreAncestorLookups.Select(l => l.LookupAttribute), StringComparer.OrdinalIgnoreCase)),
            NullLogger<CoreAncestorResolver>.Instance);
        return new SignalWriter(writerClient, sysadmin, coreAncestors, ownership, TimeProvider.System, NullLogger<SignalWriter>.Instance);
    }

    private static SignalWriteRequest Request(Guid subjectId, string subjectEntity, string displayName) => new(
        PolicyId: PolicyId,
        PolicyCode: TestPolicyCode,
        PolicyVersionId: PolicyVersionId,
        SubjectEntityLogicalName: subjectEntity,
        SubjectId: subjectId,
        SubjectDisplayName: displayName,
        ShortHeadline: "ONTOLOGY DEV TEST 030 - seam sanity check",
        Lane: SignalWriter.LaneDecide,
        Severity: SignalWriter.SeverityInfo,
        MessageTemplate: "ONTOLOGY DEV TEST 030: {{note}}",
        FactValues: new Dictionary<string, object?> { ["note"] = "seam test row, safe to delete" });

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// Best-effort cleanup, pass or fail. Deletes via the NON-impersonated OPERATOR client (F17): the writer
    /// holds no Delete privilege on sprk_signal.
    /// </summary>
    /// <remarks>
    /// R10 (second independent review): sweeps by <c>sprk_policycode == TestPolicyCode</c> rather than only
    /// the ids this run happened to create. A tracked-ids-only cleanup misses rows from a PRIOR run that
    /// crashed before reaching its own cleanup, or from a concurrent run sharing this test policy code — both
    /// would otherwise leave a dangling "ONTOLOGY DEV TEST 030" row for a human to find later.
    /// </remarks>
    public async Task DisposeAsync()
    {
        if (!_dv.IsLive) return;

        var query = new QueryExpression("sprk_signal") { ColumnSet = new ColumnSet("sprk_signalid") };
        query.Criteria.FilterOperator = LogicalOperator.Or;
        query.Criteria.AddCondition("sprk_policycode", ConditionOperator.Equal, TestPolicyCode);
        query.Criteria.AddCondition("sprk_policycode", ConditionOperator.Equal, DoTestPolicyCode);

        EntityCollection toDelete;
        try
        {
            toDelete = await _dv.OperatorClient.RetrieveMultipleAsync(query);
        }
        catch
        {
            toDelete = new EntityCollection();
        }

        foreach (var row in toDelete.Entities)
        {
            try
            {
                await _dv.OperatorClient.DeleteAsync("sprk_signal", row.Id);
            }
            catch
            {
                // Best-effort: a cleanup failure must not mask the test's own assertion failure.
            }
        }

        // Task 037: the probe subjects (zz-037- prefix) go after the Signals that point at them.
        foreach (var (entity, id) in _probeRows)
        {
            try
            {
                await _dv.OperatorClient.DeleteAsync(entity, id);
            }
            catch
            {
                // Best-effort; the run's final query by name prefix confirms nothing is left behind.
            }
        }
    }

    /// <summary>
    /// The reads <see cref="SignalWriter"/> performs through the shared sysadmin seam (the grouping matter's
    /// <c>owningbusinessunit</c>, F25, and — task 039 — the ownership resolver's) — backed here by the NON-impersonated operator connection,
    /// mirroring <c>SignalsModule</c>'s production split between the writer client and the shared
    /// <c>IGenericEntityService</c>.
    /// </summary>
    private sealed class SysadminAdapter : IGenericEntityService
    {
        private readonly ServiceClient _operatorClient;

        public SysadminAdapter(ServiceClient operatorClient) => _operatorClient = operatorClient;

        public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default) =>
            _operatorClient.RetrieveAsync(entityLogicalName, id, new ColumnSet(columns), ct);

        public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<(Guid Id, bool Created)> UpsertAsync(Entity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default) => throw new NotImplementedException();
        public Task BulkUpdateAsync(string entityLogicalName, List<(Guid id, Dictionary<string, object> fields)> updates, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<Entity> RetrieveByAlternateKeyAsync(string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetEntitySetNameAsync(string entityLogicalName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<LookupNavigationMetadata> GetLookupNavigationAsync(string childEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<string> GetCollectionNavigationAsync(string parentEntityLogicalName, string relationshipSchemaName, CancellationToken ct = default) => throw new NotImplementedException();
        // Task 039: the ownership resolver's reads (business units, teams, root flags).
        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default) =>
            _operatorClient.RetrieveMultipleAsync(query, ct);
        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default) =>
            _operatorClient.RetrieveMultipleAsync(fetch, ct);
        public Task DeleteAsync(string entityLogicalName, Guid id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AssociateAsync(string entityLogicalName, Guid entityId, string relationshipName, IEnumerable<EntityReference> relatedEntities, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>Two live <see cref="ServiceClient"/>s per test class (operator + impersonated-writer), or
    /// neither when not opted in. Shares <see cref="SignalPredicateTests"/>'s env-var gate so operators set one
    /// pair of variables for both seams.</summary>
    public sealed class LiveDataverse : IAsyncLifetime
    {
        public ServiceClient OperatorClient { get; private set; } = null!;
        public ServiceClient WriterClient { get; private set; } = null!;

        public bool IsLive { get; } = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SignalPredicateTests.UrlEnvVar));

        public Task InitializeAsync()
        {
            if (!IsLive) return Task.CompletedTask;

            var url = new Uri(Environment.GetEnvironmentVariable(SignalPredicateTests.UrlEnvVar)!.TrimEnd('/'));
            var scope = $"{url.GetLeftPart(UriPartial.Authority)}/.default";
            // AzureCliCredential, NOT DefaultAzureCredential (task 030 rework F19): DefaultAzureCredential
            // failed to obtain a token on this workstation for task 021's SignalPredicateTests; `az` is
            // logged in as the operator here.
            var credential = new AzureCliCredential();

            ServiceClient BuildClient() => new(
                instanceUrl: url,
                tokenProviderFunction: _ => Task.FromResult(
                    credential.GetToken(new TokenRequestContext(new[] { scope }), CancellationToken.None).Token),
                useUniqueInstance: true);

            OperatorClient = BuildClient();
            if (!OperatorClient.IsReady)
            {
                throw new InvalidOperationException($"Operator ServiceClient not ready: {OperatorClient.LastError}");
            }

            WriterClient = BuildClient();
            if (!WriterClient.IsReady)
            {
                throw new InvalidOperationException($"Writer ServiceClient not ready: {WriterClient.LastError}");
            }

            // R10 (second independent review): FAIL FAST when unset, rather than silently running "as the
            // writer" while actually impersonating nobody (i.e. running as the OPERATOR). A seam whose whole
            // point is proving the WRITER's own privileges must never pass by accident while testing a
            // different, more-privileged principal.
            var callerId = Environment.GetEnvironmentVariable(SignalPredicateTests.CallerIdEnvVar);
            if (string.IsNullOrWhiteSpace(callerId))
            {
                throw new InvalidOperationException(
                    $"{SignalPredicateTests.CallerIdEnvVar} is unset. This seam must run AS THE WRITER via " +
                    "MSCRMCallerID impersonation -- without it, every assertion below would silently run as " +
                    $"the OPERATOR instead. Set it to the writer's systemuserid (3121bf1b-9fbf-f111-aaaf-0022482913fc) " +
                    $"whenever {SignalPredicateTests.UrlEnvVar} is set.");
            }

            WriterClient.CallerId = Guid.Parse(callerId);

            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            OperatorClient?.Dispose();
            WriterClient?.Dispose();
            return Task.CompletedTask;
        }
    }
}
