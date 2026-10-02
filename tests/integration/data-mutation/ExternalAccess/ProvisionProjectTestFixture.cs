using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Tests.Integration.Workspace;

namespace Sprk.Bff.Api.Tests.DataMutation.ExternalAccess;

/// <summary>
/// Test host for <c>/provision-project</c> and <c>/unsecure-project</c>: in-memory <c>sprk_project</c>,
/// <c>sprk_matter</c> and <c>sprk_workassignment</c> rows, a controllable Secure Record business unit with its DEFAULT
/// team, its NAMED owner team and decoys, the named team's members, the business unit's users, a substituted SPE
/// container creator, a recording delegation probe, and a record of every write the endpoints issued.
/// </summary>
/// <remarks>
/// <para><b>Payloads are recorded, not just entity-set names.</b> "Provisioning never writes
/// <c>sprk_externalaccount</c>" can only be asserted on the payload, because that column lives INSIDE a payload aimed
/// at an entity set provisioning legitimately writes.</para>
///
/// <para><b>The rows are mutable and the ownership PATCH is applied to them.</b> The endpoints assign ownership and
/// then READ THE OWNER BACK. A fixture whose rows never changed would fail that read-back on every happy path, so the
/// double applies <c>ownerid@odata.bind</c> the way Dataverse would — including <c>owningbusinessunit</c>, which a team
/// owner derives. The switch that stops applying it is what the read-back perturbation flips.</para>
///
/// <para><b>Task 144 — the roster proves the default team is never chosen.</b> The Secure Record BU's DEFAULT team is
/// always present, next to decoys that are near-misses of the named team on every predicate the endpoint's filter
/// carries (name, teamtype, isdefault). Dropping any predicate selects a decoy or the default team and a test goes red;
/// see <see cref="RowsJsonFor"/>.</para>
/// </remarks>
public sealed class ProvisionProjectTestFixture : WorkspaceTestFixture
{
    private const string ProjectEntitySet = "sprk_projects";
    private const string MatterEntitySet = "sprk_matters";
    private const string WorkAssignmentEntitySet = "sprk_workassignments";

    /// <summary>The BU name the endpoint is configured to resolve, and which this double answers for.</summary>
    /// <remarks>
    /// Renamed <c>Secure Project</c> → <c>Secure Record</c> by task 121 (D-12 §2). Self-consistent with the config key
    /// set below; kept equal to production so the fixture does not lie about the real name.
    /// </remarks>
    public const string SecureBuName = "Secure Record";

    /// <summary>The named owner team the endpoint is configured to resolve (task 144; owner decision F9).</summary>
    public const string SecureOwnerTeamName = "Secure Record Owners";

    public static readonly Guid SecureBuId = Guid.Parse("d9ec0b6f-0000-0000-0000-0000000000b0");

    /// <summary>The NAMED, non-default owner team — the only team provisioning may assign to (task 144).</summary>
    public static readonly Guid SecureOwnerTeamId = Guid.Parse("daec0b6f-0000-0000-0000-0000000000f1");

    /// <summary>
    /// The Secure Record BU's DEFAULT team — the owner before task 144, and a team provisioning must NEVER choose
    /// again: its membership follows every user placed in the BU and cannot be curated.
    /// </summary>
    public static readonly Guid SecureDefaultTeamId = Guid.Parse("daec0b6f-0000-0000-0000-0000000000e0");

    /// <summary>
    /// Teams that exist on the same business unit but must NEVER be chosen as its owner. Each is a near-miss of the
    /// named team on exactly one predicate: the access team has the right NAME but the wrong TYPE; the extra owner team
    /// has the right type but the wrong NAME.
    /// </summary>
    public static readonly Guid DecoyAccessTeamId = Guid.Parse("daec0b6f-0000-0000-0000-0000000000a1");
    public static readonly Guid DecoyOwnerTeamId = Guid.Parse("daec0b6f-0000-0000-0000-0000000000a2");

    /// <summary>The container id the substituted SpeFileStore returns for a successful creation.</summary>
    public const string ProvisionedContainerId = "b!provisioned-secure-container";

    private readonly ConcurrentDictionary<Guid, SeededRecord> _records = new();

    /// <summary>Monotonic counter making UPDATE order observable — see <see cref="RecordedUpdate"/>.</summary>
    private int _updateSequence;

    // ── Recorded writes ──────────────────────────────────────────────────────

    /// <summary>Entity sets the endpoint issued a CREATE against, in order.</summary>
    public ConcurrentBag<string> CreatedEntitySets { get; } = new();

    /// <summary>Every UPDATE the endpoint issued: entity set, record id, and the payload's keys/values.</summary>
    public ConcurrentBag<RecordedUpdate> Updates { get; } = new();

    /// <summary>Every (entity set, record id) the delegation filter asked the caller's rights about (task 144).</summary>
    public ConcurrentBag<(string EntitySet, Guid RecordId)> DelegationProbes { get; } = new();

    // ── Task 061: the POA share plane ───────────────────────────────────────

    /// <summary>The systemuser the caller resolves to — i.e. the record's creator.</summary>
    public static readonly Guid CallerSystemUserId = Guid.Parse("c0000000-0000-0000-0000-00000000cafe");

    /// <summary>Every share the endpoint issued, in order.</summary>
    public ConcurrentBag<RecordedShare> Grants { get; } = new();

    /// <summary>Every share the endpoint revoked, in order.</summary>
    public ConcurrentBag<RecordedShare> Revokes { get; } = new();

    /// <summary>When false, the caller's Dataverse identity cannot be established.</summary>
    public bool CallerSystemUserIdResolves { get; set; } = true;

    /// <summary>When false, the delegation probe reports Read only — the caller lacks Write (task 144).</summary>
    public bool CallerHoldsWrite { get; set; } = true;

    /// <summary>Principal whose share throws, to model a partial-share failure.</summary>
    public Guid? FailShareForPrincipal { get; set; }

    /// <summary>Principal whose REVOKE throws, to model a partial-sweep failure.</summary>
    public Guid? FailRevokeForPrincipal { get; set; }

    /// <summary>When false, the STRICT share read throws — the "incomplete counts as failed" cases (ISS-018).</summary>
    public bool StrictShareReadSucceeds { get; set; } = true;

    /// <summary>When false, the SOFT share read throws too — a total read failure.</summary>
    public bool SoftShareReadSucceeds { get; set; } = true;

    /// <summary>Captured log entries, so a test can assert a warning was actually WRITTEN.</summary>
    public LogCapture Logs { get; } = new();

    /// <summary>One recorded POA operation. <c>AccessRightsCsv</c> is null for a revoke.</summary>
    public sealed record RecordedShare(
        string EntitySet, Guid RecordId, DataversePrincipalRef Principal, string? AccessRightsCsv);

    /// <summary>Container display names passed to SPE, so a test can prove a container was created.</summary>
    public ConcurrentBag<string> CreatedContainerDisplayNames { get; } = new();

    /// <summary>
    /// One recorded UPDATE. <c>Sequence</c> is a monotonic counter, because <see cref="Updates"/> is a
    /// <c>ConcurrentBag</c> and bags do NOT preserve insertion order.
    /// </summary>
    public sealed record RecordedUpdate(
        string EntitySet,
        Guid RecordId,
        IReadOnlyDictionary<string, string?> Payload,
        int Sequence = 0);

    // ── Controllable environment shape ───────────────────────────────────────

    /// <summary>How many business units answer the configured name. Default 1.</summary>
    public int SecureBuMatchCount { get; set; } = 1;

    /// <summary>How many NAMED owner teams answer for the resolved BU (task 144). Default 1.</summary>
    public int OwnerTeamMatchCount { get; set; } = 1;

    /// <summary>
    /// When true, the BU's DEFAULT team carries the configured owner-team NAME — the misconfiguration in which only
    /// <c>isdefault eq false</c> stands between provisioning and the default team (task 144).
    /// </summary>
    public bool DefaultTeamCarriesOwnerTeamName { get; set; }

    /// <summary>Members of the named owner team, of any kind (task 144). Default none.</summary>
    public List<Guid> OwnerTeamMembers { get; } = new();

    /// <summary>When false, the named team's membership read throws (task 144).</summary>
    public bool MembershipReadSucceeds { get; set; } = true;

    /// <summary>Systemusers whose business unit is the Secure Record BU (task 144). Default none.</summary>
    public List<SecureBuUser> SecureBuUsers { get; } = new();

    /// <summary>When false, the Secure Record BU's user read throws (task 144).</summary>
    public bool BusinessUnitUserReadSucceeds { get; set; } = true;

    /// <summary>A systemuser sitting in the Secure Record BU.</summary>
    public sealed record SecureBuUser(Guid Id, bool IsDisabled = false, bool IsApplicationUser = false);

    /// <summary>When false, SPE container creation returns null (Graph failure). Default true.</summary>
    public bool SpeContainerCreationSucceeds { get; set; } = true;

    /// <summary>When false, an UPDATE whose payload carries <c>sprk_containerid</c> throws. Default true.</summary>
    public bool ContainerStampSucceeds { get; set; } = true;

    /// <summary>
    /// When false, the ownership PATCH is accepted but NOT applied to the in-memory row — Dataverse's
    /// real behaviour for an unrecognised <c>@odata.bind</c> property.
    /// </summary>
    public bool OwnershipPatchIsApplied { get; set; } = true;

    private sealed record SeededRecord(
        string EntitySet, Guid Id, Guid? OwningTeamId, string? ContainerId, Guid? LegacySecurityBuId, bool IsSecure,
        Guid? OwningUserId = null, Guid? OwningBusinessUnitId = null);

    /// <summary>Seeds a project row.</summary>
    public void SeedProject(
        Guid projectId,
        Guid? owningTeamId = null,
        string? containerId = null,
        Guid? legacySecurityBuId = null,
        bool isSecure = true,
        Guid? owningBusinessUnitId = null)
        => Seed(ProjectEntitySet, projectId, owningTeamId, containerId, legacySecurityBuId, isSecure, owningBusinessUnitId);

    /// <summary>Seeds a matter row (task 144).</summary>
    public void SeedMatter(Guid matterId, Guid? owningTeamId = null, string? containerId = null, bool isSecure = true)
        => Seed(MatterEntitySet, matterId, owningTeamId, containerId, null, isSecure, null);

    /// <summary>Seeds a work-assignment row (task 144).</summary>
    public void SeedWorkAssignment(Guid workAssignmentId, Guid? owningTeamId = null, string? containerId = null, bool isSecure = true)
        => Seed(WorkAssignmentEntitySet, workAssignmentId, owningTeamId, containerId, null, isSecure, null);

    private void Seed(
        string entitySet, Guid id, Guid? owningTeamId, string? containerId, Guid? legacySecurityBuId, bool isSecure,
        Guid? owningBusinessUnitId)
        => _records[id] = new SeededRecord(
            entitySet, id, owningTeamId, containerId, legacySecurityBuId, isSecure,
            OwningBusinessUnitId: owningBusinessUnitId ?? BusinessUnitOf(owningTeamId));

    /// <summary>The BU a team owner places a record in — what Dataverse derives <c>owningbusinessunit</c> from.</summary>
    private static Guid? BusinessUnitOf(Guid? owningTeamId) =>
        owningTeamId is { } team
        && (team == SecureOwnerTeamId || team == SecureDefaultTeamId || team == DecoyAccessTeamId || team == DecoyOwnerTeamId)
            ? SecureBuId
            : null;

    /// <summary>The current owner of a seeded record — what the endpoint's read-back would observe.</summary>
    public Guid? OwningTeamOf(Guid recordId)
        => _records.TryGetValue(recordId, out var r) ? r.OwningTeamId : null;

    /// <summary>The current owning user of a seeded record.</summary>
    public Guid? OwningUserOf(Guid recordId)
        => _records.TryGetValue(recordId, out var r) ? r.OwningUserId : null;

    /// <summary>The current container recorded on a seeded record.</summary>
    public string? ContainerIdOf(Guid recordId)
        => _records.TryGetValue(recordId, out var r) ? r.ContainerId : null;

    /// <summary>The current secure flag of a seeded record.</summary>
    public bool? IsSecureOf(Guid recordId)
        => _records.TryGetValue(recordId, out var r) ? r.IsSecure : null;

    /// <summary>
    /// Clears seeded rows, the write log and every environment switch. Called from the test class
    /// constructor, which xUnit runs before EVERY test.
    /// </summary>
    public void Reset()
    {
        _records.Clear();
        CreatedEntitySets.Clear();
        Updates.Clear();
        DelegationProbes.Clear();
        CreatedContainerDisplayNames.Clear();
        SecureBuMatchCount = 1;
        OwnerTeamMatchCount = 1;
        DefaultTeamCarriesOwnerTeamName = false;
        OwnerTeamMembers.Clear();
        MembershipReadSucceeds = true;
        SecureBuUsers.Clear();
        BusinessUnitUserReadSucceeds = true;
        SpeContainerCreationSucceeds = true;
        ContainerStampSucceeds = true;
        OwnershipPatchIsApplied = true;
        _updateSequence = 0;
        Grants.Clear();
        Revokes.Clear();
        CallerSystemUserIdResolves = true;
        CallerHoldsWrite = true;
        FailShareForPrincipal = null;
        FailRevokeForPrincipal = null;
        StrictShareReadSucceeds = true;
        SoftShareReadSucceeds = true;
        Logs.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Set explicitly rather than relying on the production defaults, so a test proves the CONFIGURED
                // names are honoured.
                ["SecureRecord:BusinessUnitName"] = SecureBuName,
                ["SecureRecord:OwnerTeamName"] = SecureOwnerTeamName,
                ["SharePointEmbedded:ContainerTypeId"] = "11111111-2222-3333-4444-555555555555"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // The delegation gate (task 008) answers Write unless a test turns it off, and RECORDS what it was asked
            // about — task 144's "the record authorized is the record re-owned" is asserted on that record.
            services.RemoveAll<CallerRecordAccessProbe>();
            services.AddSingleton<CallerRecordAccessProbe>(new RecordingCallerRecordAccessProbe(this));

            // Task 061 — the POA share plane. Recorded rather than mocked.
            services.RemoveAll<IDataverseRecordShareService>();
            services.AddSingleton<IDataverseRecordShareService>(new RecordingRecordShareService(this));

            var client = new Mock<DataverseWebApiClient>(
                ClientConfig(), NullLogger<DataverseWebApiClient>.Instance,
                // Moq matches a class-proxy constructor EXACTLY; the two optional credential slots are passed
                // positionally as null because this double never authenticates.
                null!, null!);

            // The handlers read every row through their OWN private DTOs, which this assembly cannot name. So the
            // double answers in Dataverse's WIRE shape (JSON) and lets the handler's own deserialization run.
            client
                .Setup(c => c.QueryAsync<It.IsAnyType>(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .Returns(new InvocationFunc(invocation =>
                {
                    var rowType = invocation.Method.GetGenericArguments()[0];
                    var entitySet = (string)invocation.Arguments[0];
                    var filter = invocation.Arguments[1] as string;
                    var select = invocation.Arguments[2] as string;

                    var top = invocation.Arguments[3] as int?;

                    var json = RowsJsonFor(entitySet, filter, select, top);
                    var listType = typeof(List<>).MakeGenericType(rowType);
                    var rows = JsonSerializer.Deserialize(json, listType)
                               ?? Activator.CreateInstance(listType)!;

                    return typeof(Task)
                        .GetMethod(nameof(Task.FromResult))!
                        .MakeGenericMethod(listType)
                        .Invoke(null, new[] { rows })!;
                }));

            client
                .Setup(c => c.CreateAsync(It.IsAny<string>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string entitySet, object _, CancellationToken _) =>
                {
                    CreatedEntitySets.Add(entitySet);
                    return Guid.NewGuid();
                });

            client
                .Setup(c => c.UpdateAsync(
                    It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<object>(), It.IsAny<CancellationToken>()))
                .Returns((string entitySet, Guid id, object payload, CancellationToken _) =>
                    ApplyUpdate(entitySet, id, payload));

            services.RemoveAll<DataverseWebApiClient>();
            services.AddSingleton(client.Object);

            // Container creation, substituted at the ADR-007 facade.
            services.RemoveAll<SpeFileStore>();
            services.AddSingleton<SpeFileStore>(sp => new StubSpeFileStore(sp, this));

            // Capture logs in-memory so a test can assert the endpoint actually WROTE its warning.
            services.AddSingleton<ILoggerProvider>(Logs);
        });
    }

    /// <summary>
    /// Applies one UPDATE to the in-memory row, recording it first. Recording happens BEFORE the failure switches are
    /// consulted, so "this column was never written" forbids the attempt, not merely the effect.
    /// </summary>
    private Task ApplyUpdate(string entitySet, Guid id, object payload)
    {
        var flat = Flatten(payload);
        Updates.Add(new RecordedUpdate(entitySet, id, flat, Interlocked.Increment(ref _updateSequence)));

        if (flat.ContainsKey("sprk_containerid") && !ContainerStampSucceeds)
        {
            throw new InvalidOperationException(
                "Dataverse 400: simulated failure recording sprk_containerid.");
        }

        if (_records.TryGetValue(id, out var record) && record.EntitySet == entitySet)
        {
            if (flat.TryGetValue("ownerid@odata.bind", out var ownerBind)
                && ownerBind is not null
                && OwnershipPatchIsApplied)
            {
                // `ownerid` is polymorphic: provisioning binds a TEAM, the reverse path a SYSTEMUSER. The double
                // derives owningbusinessunit the way Dataverse does, so a re-read sees where the record now sits.
                var principalId = ParseIdFromBind(ownerBind);
                if (principalId is { } parsed)
                {
                    _records[id] = ownerBind.Contains("/systemusers(", StringComparison.OrdinalIgnoreCase)
                        ? record with { OwningUserId = parsed, OwningTeamId = null, OwningBusinessUnitId = null }
                        : record with { OwningTeamId = parsed, OwningUserId = null, OwningBusinessUnitId = BusinessUnitOf(parsed) };
                }
            }

            if (flat.TryGetValue("sprk_issecure", out var isSecureRaw))
            {
                record = _records[id];
                _records[id] = record with
                {
                    IsSecure = bool.TryParse(isSecureRaw, out var isSecure) && isSecure
                };
            }

            if (flat.TryGetValue("sprk_containerid", out var container))
            {
                record = _records[id];
                _records[id] = record with { ContainerId = container };
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>Extracts the GUID from an <c>/teams(guid)</c> OData bind value.</summary>
    private static Guid? ParseIdFromBind(string bind)
    {
        var open = bind.IndexOf('(');
        var close = bind.IndexOf(')');
        if (open < 0 || close <= open) return null;
        return Guid.TryParse(bind[(open + 1)..close], out var id) ? id : null;
    }

    /// <summary>Flattens a write payload to string values so tests can assert on keys and contents.</summary>
    private static IReadOnlyDictionary<string, string?> Flatten(object payload)
    {
        if (payload is IDictionary<string, object?> dict)
        {
            return dict.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString(),
                StringComparer.OrdinalIgnoreCase);
        }

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return doc.RootElement.EnumerateObject()
            .ToDictionary(
                p => p.Name,
                p => p.Value.ValueKind == JsonValueKind.Null ? null : p.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Every column live <c>sprk_project</c> actually exposes that these endpoints may read. Verified against live
    /// metadata 2026-08-25 (re-checked 2026-10-01). <c>sprk_securitybuid</c>, <c>sprk_specontainerid</c>,
    /// <c>sprk_externalaccountid</c> and <c>sprk_name</c> do not exist on this table.
    /// </summary>
    internal static readonly HashSet<string> LiveProjectColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "sprk_projectdescription",
        "sprk_issecure", "sprk_accesspermission", "sprk_containerid", "sprk_searchindexname",
        "_sprk_securitybu_value", "_sprk_externalaccount_value", "_sprk_mattertype_value",
        "_sprk_practicearea_value", "statecode", "statuscode", "createdon", "modifiedon",
        "ownerid", "_owningteam_value", "_owninguser_value", "_owningbusinessunit_value"
    };

    /// <summary>The <c>sprk_matter</c> columns these endpoints may read (live metadata 2026-10-01, task 144).</summary>
    internal static readonly HashSet<string> LiveMatterColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_matterid", "sprk_mattername", "sprk_matternumber", "sprk_issecure", "sprk_accesspermission",
        "sprk_containerid", "_sprk_securitybu_value", "statecode", "statuscode", "createdon", "modifiedon",
        "ownerid", "_owningteam_value", "_owninguser_value", "_owningbusinessunit_value"
    };

    /// <summary>
    /// The <c>sprk_workassignment</c> columns these endpoints may read (live metadata 2026-10-01, task 144). Its name
    /// column is <c>sprk_name</c> — the one root whose name column is NOT <c>{entity}name</c>.
    /// </summary>
    internal static readonly HashSet<string> LiveWorkAssignmentColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_workassignmentid", "sprk_name", "sprk_issecure", "sprk_accesspermission", "sprk_containerid",
        "_sprk_securitybu_value", "statecode", "statuscode", "createdon", "modifiedon",
        "ownerid", "_owningteam_value", "_owninguser_value", "_owningbusinessunit_value"
    };

    /// <summary>
    /// Dataverse's own behaviour: a projection naming a column the table lacks is a 400. THE GUARD that task 016 built
    /// for the closure cascade and this fixture once lacked — a fake that ignores the projection goes green on code
    /// that 400s in production.
    /// </summary>
    private static void RejectUnknownColumns(string entitySet, string? select)
    {
        var live = entitySet switch
        {
            ProjectEntitySet => LiveProjectColumns,
            MatterEntitySet => LiveMatterColumns,
            WorkAssignmentEntitySet => LiveWorkAssignmentColumns,
            _ => null
        };

        if (live is null || string.IsNullOrWhiteSpace(select)) return;

        foreach (var column in select.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!live.Contains(column))
            {
                throw new InvalidOperationException(
                    $"Dataverse 400: Could not find a property named '{column}' on entity set '{entitySet}'.");
            }
        }
    }

    /// <summary>
    /// The Dataverse wire payload this double returns for one query.
    /// </summary>
    /// <remarks>
    /// <para><b>This double honours <c>$top</c> and the discriminating <c>$filter</c> predicates, and that is
    /// load-bearing.</b> A double that discards part of the query goes green on code that builds that part wrongly:
    /// the perturbation sweep once found that changing the BU lookup from <c>$top=2</c> to <c>$top=1</c>, and dropping
    /// the team predicates, broke nothing.</para>
    /// <para><b>Task 144 team roster.</b> For the Secure Record BU it holds: the DEFAULT team (named after the BU, or —
    /// when <see cref="DefaultTeamCarriesOwnerTeamName"/> — after the owner team); an ACCESS team carrying the owner
    /// team's name; an extra OWNER team with a near-miss name; and <see cref="OwnerTeamMatchCount"/> named owner teams.
    /// Drop <c>name eq</c> → the extra owner team matches (ambiguous). Drop <c>teamtype eq 0</c> → the access team
    /// matches (ambiguous). Drop <c>isdefault eq false</c> → the default team can be chosen. Decoys come first so a
    /// regressed <c>$top=1</c> picks a decoy rather than the answer.</para>
    /// <para><b>Users.</b> The business-unit user read applies the predicates it is given: a query that added
    /// <c>isdisabled eq false</c> or <c>applicationid eq null</c> would hide a disabled or application user, and the
    /// tests that seed one would go red.</para>
    /// </remarks>
    private string RowsJsonFor(string entitySet, string? filter, string? select = null, int? top = null)
    {
        RejectUnknownColumns(entitySet, select);

        var payload = new List<Dictionary<string, object?>>();

        switch (entitySet)
        {
            case ProjectEntitySet:
            case MatterEntitySet:
            case WorkAssignmentEntitySet:
                var seeded = _records.Values.FirstOrDefault(
                    r => r.EntitySet == entitySet
                         && filter is not null
                         && filter.Contains(r.Id.ToString(), StringComparison.OrdinalIgnoreCase));

                if (seeded is not null)
                {
                    var (idColumn, nameColumn, displayName) = entitySet switch
                    {
                        ProjectEntitySet => ("sprk_projectid", "sprk_projectname", "Seeded Secure Project"),
                        MatterEntitySet => ("sprk_matterid", "sprk_mattername", "Seeded Secure Matter"),
                        _ => ("sprk_workassignmentid", "sprk_name", "Seeded Secure Work Assignment")
                    };

                    var row = new Dictionary<string, object?>
                    {
                        [idColumn] = seeded.Id,
                        [nameColumn] = displayName,
                        ["sprk_issecure"] = seeded.IsSecure
                    };

                    if (seeded.LegacySecurityBuId is { } legacy)
                        row["_sprk_securitybu_value"] = legacy;

                    if (seeded.ContainerId is { } container)
                        row["sprk_containerid"] = container;

                    if (seeded.OwningTeamId is { } team)
                        row["_owningteam_value"] = team;

                    if (seeded.OwningUserId is { } owningUser)
                        row["_owninguser_value"] = owningUser;

                    if (seeded.OwningBusinessUnitId is { } owningBu)
                        row["_owningbusinessunit_value"] = owningBu;

                    payload.Add(row);
                }
                break;

            case "businessunits":
                // Answer only the CONFIGURED name.
                if (filter is not null && filter.Contains($"'{SecureBuName}'", StringComparison.Ordinal))
                {
                    for (var i = 0; i < SecureBuMatchCount; i++)
                    {
                        payload.Add(new Dictionary<string, object?>
                        {
                            ["businessunitid"] = i == 0 ? SecureBuId : Guid.NewGuid(),
                            ["name"] = SecureBuName
                        });
                    }
                }
                break;

            case "teams":
                if (filter is not null && filter.Contains(SecureBuId.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    var roster = new List<(Guid Id, string Name, bool IsDefault, int TeamType)>
                    {
                        (DecoyAccessTeamId, SecureOwnerTeamName, false, 1),
                        (DecoyOwnerTeamId, $"{SecureOwnerTeamName} Extra", false, 0),
                        (SecureDefaultTeamId, DefaultTeamCarriesOwnerTeamName ? SecureOwnerTeamName : SecureBuName, true, 0)
                    };

                    for (var i = 0; i < OwnerTeamMatchCount; i++)
                    {
                        roster.Add((i == 0 ? SecureOwnerTeamId : Guid.NewGuid(), SecureOwnerTeamName, false, 0));
                    }

                    // Apply only the predicates the caller actually asked for.
                    var requiresDefault = filter.Contains("isdefault eq true", StringComparison.OrdinalIgnoreCase);
                    var requiresNonDefault = filter.Contains("isdefault eq false", StringComparison.OrdinalIgnoreCase);
                    var requiresOwnerType = filter.Contains("teamtype eq 0", StringComparison.OrdinalIgnoreCase);
                    var requiredName = ExtractQuoted(filter, "name eq ");

                    foreach (var team in roster)
                    {
                        if (requiresDefault && !team.IsDefault) continue;
                        if (requiresNonDefault && team.IsDefault) continue;
                        if (requiresOwnerType && team.TeamType != 0) continue;
                        if (requiredName is not null && !string.Equals(team.Name, requiredName, StringComparison.OrdinalIgnoreCase)) continue;

                        payload.Add(new Dictionary<string, object?>
                        {
                            ["teamid"] = team.Id,
                            ["name"] = team.Name
                        });
                    }
                }
                break;

            case "teammemberships":
                // Members of exactly the team named in the filter. Only the NAMED team has members to report; any other
                // team id (the default team, a decoy) answers empty, so a resolver that checked the wrong team's
                // membership would wave a populated named team through.
                if (filter is not null && filter.Contains(SecureOwnerTeamId.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    if (!MembershipReadSucceeds)
                        throw new InvalidOperationException("Dataverse 503: simulated teammembership read failure.");

                    payload.AddRange(OwnerTeamMembers.Select(m => new Dictionary<string, object?>
                    {
                        ["systemuserid"] = m
                    }));
                }
                break;

            case "systemusers":
                if (filter is not null && filter.Contains(SecureBuId.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    if (!BusinessUnitUserReadSucceeds)
                        throw new InvalidOperationException("Dataverse 503: simulated systemuser read failure.");

                    var excludesDisabled = filter.Contains("isdisabled eq false", StringComparison.OrdinalIgnoreCase);
                    var excludesApplicationUsers = filter.Contains("applicationid eq null", StringComparison.OrdinalIgnoreCase);

                    payload.AddRange(SecureBuUsers
                        .Where(u => !(excludesDisabled && u.IsDisabled))
                        .Where(u => !(excludesApplicationUsers && u.IsApplicationUser))
                        .Select(u => new Dictionary<string, object?> { ["systemuserid"] = u.Id }));
                }
                break;
        }

        // Honour $top. A double that ignores it lets a wrong $top pass unnoticed.
        if (top is { } limit && payload.Count > limit)
            payload = payload.Take(limit).ToList();

        return JsonSerializer.Serialize(payload);
    }

    /// <summary>The single-quoted literal following <paramref name="prefix"/> in an OData filter, or null.</summary>
    private static string? ExtractQuoted(string filter, string prefix)
    {
        var at = filter.IndexOf(prefix + "'", StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var start = at + prefix.Length + 1;
        var end = filter.IndexOf('\'', start);
        return end < 0 ? null : filter[start..end].Replace("''", "'");
    }

    /// <summary>Config sufficient for the real <see cref="DataverseWebApiClient"/> constructor (Moq invokes it).</summary>
    private static IConfiguration ClientConfig() =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Dataverse:ServiceUrl"] = "https://test.crm.dynamics.com",
            // Enabling managed identity takes the MI branch, whose DefaultAzureCredential is constructed lazily and
            // never authenticates — this client is fully stubbed.
            ["Graph:ManagedIdentity:Enabled"] = "true",
            ["API_APP_ID"] = "00000000-0000-0000-0000-0000000000aa",
            ["API_CLIENT_SECRET"] = "test-secret",
            ["TENANT_ID"] = "00000000-0000-0000-0000-0000000000bb"
        }).Build();

    public HttpClient CreateEntitledClient()
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "provision-test-token");
        return client;
    }

    /// <summary>Container creation without Graph, recording what was asked for.</summary>
    private sealed class StubSpeFileStore : SpeFileStore
    {
        private readonly ProvisionProjectTestFixture _fixture;

        public StubSpeFileStore(IServiceProvider sp, ProvisionProjectTestFixture fixture)
            : base(sp.GetRequiredService<ContainerOperations>(),
                   sp.GetRequiredService<DriveItemOperations>(),
                   sp.GetRequiredService<UploadSessionManager>(),
                   sp.GetRequiredService<UserOperations>())
        {
            _fixture = fixture;
        }

        public override Task<ContainerDto?> CreateContainerAsync(
            Guid containerTypeId, string displayName, string? description = null, CancellationToken ct = default)
        {
            _fixture.CreatedContainerDisplayNames.Add(displayName);

            return Task.FromResult<ContainerDto?>(
                _fixture.SpeContainerCreationSucceeds
                    ? new ContainerDto(
                        Id: ProvisionedContainerId,
                        DisplayName: displayName,
                        Description: description,
                        CreatedDateTime: DateTimeOffset.UnixEpoch)
                    : null);
        }
    }

    /// <summary>
    /// The delegation probe: Write unless <see cref="CallerHoldsWrite"/> is off, and a record of every record it was
    /// asked about — so a test can prove the filter authorized the SAME record the handler re-owned (task 144).
    /// </summary>
    private sealed class RecordingCallerRecordAccessProbe : CallerRecordAccessProbe
    {
        private readonly ProvisionProjectTestFixture _fixture;

        public RecordingCallerRecordAccessProbe(ProvisionProjectTestFixture fixture)
            : base(new HttpClient(), new ConfigurationBuilder().Build(),
                   NullLogger<CallerRecordAccessProbe>.Instance)
        {
            _fixture = fixture;
        }

        public override Task<AccessRights> GetCallerRightsAsync(
            string? callerBearerToken, string entitySet, Guid recordId, CancellationToken ct = default)
        {
            _fixture.DelegationProbes.Add((entitySet, recordId));
            return Task.FromResult(_fixture.CallerHoldsWrite
                ? AccessRights.Read | AccessRights.Write
                : AccessRights.Read);
        }

        /// <summary>
        /// Task 061: who provisioning shares the record back to. <c>null</c> models "the caller's Dataverse identity
        /// could not be established", which must FAIL the provision rather than complete it.
        /// </summary>
        public override Task<Guid?> GetCallerSystemUserIdAsync(
            string? callerBearerToken, CancellationToken ct = default)
            => Task.FromResult(_fixture.CallerSystemUserIdResolves ? CallerSystemUserId : (Guid?)null);
    }

    /// <summary>Records every POA share the endpoints issue, and fails them on demand.</summary>
    internal sealed class RecordingRecordShareService : IDataverseRecordShareService
    {
        private readonly ProvisionProjectTestFixture _fixture;

        public RecordingRecordShareService(ProvisionProjectTestFixture fixture) => _fixture = fixture;

        public Task GrantAccessAsync(
            string entitySetName, Guid recordId, DataversePrincipalRef principal,
            string accessRightsCsv, CancellationToken ct = default)
        {
            if (_fixture.FailShareForPrincipal == principal.Id)
                throw new InvalidOperationException($"Seeded share failure for {principal.Id}.");

            _fixture.Grants.Add(new RecordedShare(entitySetName, recordId, principal, accessRightsCsv));
            return Task.CompletedTask;
        }

        public Task RevokeAccessAsync(
            string entitySetName, Guid recordId, DataversePrincipalRef principal,
            CancellationToken ct = default)
        {
            // Checked BEFORE recording: `Revokes` means "shares that were actually removed".
            if (_fixture.FailRevokeForPrincipal == principal.Id)
                throw new InvalidOperationException($"Seeded revoke failure for {principal.Id}.");

            _fixture.Revokes.Add(new RecordedShare(entitySetName, recordId, principal, null));
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessAsync(
            string entityLogicalName, Guid recordId, CancellationToken ct = default)
        {
            if (!_fixture.SoftShareReadSucceeds)
            {
                throw new InvalidOperationException(
                    $"Seeded transport failure reading the shares on {entityLogicalName}({recordId}).");
            }

            return Task.FromResult<IReadOnlyList<DataversePrincipalAccess>>(
                _fixture.Grants
                    .Where(g => g.RecordId == recordId)
                    .Select(g => new DataversePrincipalAccess(g.Principal, 1, DateTimeOffset.UtcNow))
                    .ToList());
        }

        // Provisioning creates shares and never changes one, so a call here means the endpoint's behaviour changed.
        public Task ModifyAccessAsync(
            string entitySetName, Guid recordId, DataversePrincipalRef principal,
            string accessRightsCsv, CancellationToken ct = default)
            => throw new NotSupportedException("Provisioning creates shares; it never modifies one.");

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(
            string entityLogicalName, Guid recordId, CancellationToken ct = default)
        {
            if (!_fixture.StrictShareReadSucceeds)
            {
                throw new InvalidOperationException(
                    $"The shares on {entityLogicalName}({recordId}) could not be read completely: " +
                    "seeded refusal.");
            }

            return GetPrincipalAccessAsync(entityLogicalName, recordId, ct);
        }
    }

    /// <summary>
    /// In-memory <see cref="ILoggerProvider"/> capturing what the host logged, for assertion. Cleared by
    /// <see cref="Reset"/>, because the fixture is shared across the class.
    /// </summary>
    public sealed class LogCapture : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public void Clear()
        {
            while (_entries.TryDequeue(out _)) { }
        }

        public ILogger CreateLogger(string categoryName) => new QueueLogger(_entries);

        public void Dispose() { }

        private sealed class QueueLogger : ILogger
        {
            private readonly ConcurrentQueue<LogEntry> _entries;

            public QueueLogger(ConcurrentQueue<LogEntry> entries) => _entries = entries;

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
                => _entries.Enqueue(new LogEntry(logLevel, formatter(state, exception)));
        }
    }

    /// <summary>One captured log entry.</summary>
    public sealed record LogEntry(LogLevel Level, string Message);
}
