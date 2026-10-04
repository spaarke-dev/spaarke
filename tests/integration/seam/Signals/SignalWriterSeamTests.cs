using Azure.Core;
using Azure.Identity;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
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

    private SignalWriter BuildWriter()
    {
        var writerClient = new OntologyWriterDataverseClient(() => _dv.WriterClient, NullLogger<OntologyWriterDataverseClient>.Instance);
        var sysadmin = new SysadminAdapter(_dv.OperatorClient);
        return new SignalWriter(writerClient, sysadmin, TimeProvider.System, NullLogger<SignalWriter>.Instance);
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
        query.Criteria.AddCondition("sprk_policycode", ConditionOperator.Equal, TestPolicyCode);

        EntityCollection toDelete;
        try
        {
            toDelete = await _dv.OperatorClient.RetrieveMultipleAsync(query);
        }
        catch
        {
            return; // Best-effort: a cleanup failure must not mask the test's own assertion failure.
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
    }

    /// <summary>
    /// The ONE read <see cref="SignalWriter"/> performs through the shared sysadmin seam (the grouping
    /// matter's <c>owningbusinessunit</c>, F25) — backed here by the NON-impersonated operator connection,
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
        public Task<EntityCollection> RetrieveMultipleAsync(QueryExpression query, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EntityCollection> RetrieveMultipleAsync(FetchExpression fetch, CancellationToken ct = default) => throw new NotImplementedException();
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
