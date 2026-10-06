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
public class ProvisionProjectTestFixture : WorkspaceTestFixture
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

    /// <summary>Every ModifyAccess the endpoint issued (task 133: an existing share set to exact rights, or restored).</summary>
    public ConcurrentBag<RecordedShare> Modifies { get; } = new();

    /// <summary>
    /// The POA shares as they stand: (record, principal) → the rights mask Dataverse would store. Grants union into it,
    /// modifies replace, revokes remove — and BOTH share reads answer from it, so a read sees what the writes did
    /// (task 133; before it the reads were derived from <see cref="Grants"/>, which a revoke never shrank).
    /// </summary>
    private readonly ConcurrentDictionary<(Guid RecordId, DataversePrincipalRef Principal), int> _shares = new();

    /// <summary>Seeds a share that exists before the call (task 133: the pre-call share set compensation restores).</summary>
    public void SeedShare(Guid recordId, DataversePrincipalRef principal, string accessRightsCsv)
        => _shares[(recordId, principal)] = RecordShareLevels.MaskForRightsCsv(accessRightsCsv);

    /// <summary>Task 158 r1: removes a share outside the BFF (a model-driven-app Share dialog, an import).</summary>
    public void RemoveShare(Guid recordId, DataversePrincipalRef principal) => _shares.TryRemove((recordId, principal), out _);

    /// <summary>Task 158 r1: the mask any principal's share on the record carries now (0 = no share).</summary>
    public int ShareMaskOf(Guid recordId, DataversePrincipalRef principal)
        => _shares.TryGetValue((recordId, principal), out var mask) ? mask : 0;

    /// <summary>The mask <paramref name="systemUserId"/>'s share on the record carries now (0 = no share).</summary>
    public int ShareMaskOf(Guid recordId, Guid systemUserId)
        => _shares.TryGetValue((recordId, DataversePrincipalRef.User(systemUserId)), out var mask) ? mask : 0;

    /// <summary>
    /// The owner's S5 invariant (session 27 round 3; task 133 amendment R3): at least one person can open the record.
    /// True when it is NOT owned by the memberless secure owner team (its pre-call owner — the creator or their
    /// business-unit team — still reaches it), or when some systemuser holds a share carrying Read.
    /// </summary>
    public bool SomeoneCanOpen(Guid recordId)
        => OwningTeamOf(recordId) != SecureOwnerTeamId
           || _shares.Any(s => s.Key.RecordId == recordId
                               && s.Key.Principal.Kind == DataversePrincipalKind.SystemUser
                               && (s.Value & 1) == 1);

    /// <summary>Every principal holding a share on the record now, with its mask.</summary>
    public IReadOnlyDictionary<DataversePrincipalRef, int> SharesOn(Guid recordId)
        => _shares.Where(k => k.Key.RecordId == recordId).ToDictionary(k => k.Key.Principal, k => k.Value);

    // ── Task 133: the platform behaviours the live gate proves, and the faults compensation must survive ──

    /// <summary>Live gate (a) disproved: GrantAccess to the record's CURRENT owning user is refused.</summary>
    public bool GrantToCurrentOwnerRefused { get; set; }

    /// <summary>Live gate (b) disproved: an owner change drops this principal's share on the record.</summary>
    public Guid? AssignDropsShareOf { get; set; }

    /// <summary>GrantAccess / ModifyAccess for this principal throws while the secure owner team owns the record.</summary>
    public Guid? FailShareWhileSecureOwned { get; set; }

    /// <summary>The STRICT share read throws while the secure owner team owns the record.</summary>
    public bool FailStrictShareReadWhileSecureOwned { get; set; }

    /// <summary>The next N STRICT share reads throw (the pre-call read is the first one provisioning issues).</summary>
    public int FailNextStrictShareReads { get; set; }

    /// <summary>An <c>ownerid</c> PATCH binding to this principal throws (recorded first) — a failed compensation.</summary>
    public Guid? FailOwnerBindTo { get; set; }

    /// <summary>
    /// The owner read-back throws: any root read whose projection omits <c>sprk_issecure</c> (Step 1 reads it; the
    /// read-back after an assignment does not).
    /// </summary>
    public bool OwnerReadBackFails { get; set; }

    /// <summary>
    /// Systemusers a resume may look up by id, as (disabled, application user). The caller is seeded by Reset. A null
    /// <c>IsDisabled</c> is emitted as a JSON null — a row that does not prove the user enabled.
    /// </summary>
    public Dictionary<Guid, (bool? IsDisabled, bool IsApplicationUser)> SystemUsers { get; } = new();

    /// <summary>When false, a systemuser read by id throws (task 133: an unreadable createdby).</summary>
    public bool SystemUserByIdReadSucceeds { get; set; } = true;

    /// <summary>A read of THIS systemuser by id throws; others answer (task 133 b2: an unreadable createdby only).</summary>
    public Guid? SystemUserReadFailsFor { get; set; }

    /// <summary>
    /// The HTTP status a failing systemuser read (<see cref="SystemUserByIdReadSucceeds"/> false, or
    /// <see cref="SystemUserReadFailsFor"/>) carries, raised in the real client's shape — <see cref="HttpRequestException"/>
    /// with the status (owner round 14 item 3, task 133 c1-r4: a 401/403 is refused, a 503/429 transient). Unset, the
    /// failure is a non-HTTP fault, as before.
    /// </summary>
    public System.Net.HttpStatusCode? SystemUserReadFailsWith { get; set; }

    /// <summary>
    /// Whether the environment carries <c>sprk_createdbyperson</c> (task 133 b2, owner round 7 item 2). When false, a
    /// projection naming its read form 400s, as Dataverse answers before <c>Set-RecordCreatorPersonSchema.ps1</c> runs.
    /// </summary>
    public bool CreatorPersonColumnExists { get; set; } = true;

    /// <summary>
    /// Task 143 — the No Access list provisioning asks about the creator, a resume's person and each named colleague.
    /// The PRODUCTION <see cref="SecureShareNoAccessGuard"/> runs over this deny-list reader (wire seam only), a flag read
    /// that answers every record secure (provisioning only runs on secure records), and a link store that answers every
    /// user as "read, linked to nobody". <see cref="Reset"/> empties the list.
    /// </summary>
    internal AccessControl.GrantPolicyTestDoubles.SeamNoAccessListReader NoAccessList { get; private set; } = new();

    /// <summary>Task 143: the guard's flag/organization reads. Default: every record secure, no organizations.</summary>
    internal AccessControl.GrantPolicyTestDoubles.FlagStubParticipationService NoAccessReads { get; private set; } =
        new(defaultFlags: new RootRecordFlags(IsSecure: true, IsRestricted: false));

    /// <summary>
    /// Task 158 r1 (owner round 30): task 142's provenance ledger, in memory — where each share passed on to a filed secure
    /// root came from. The REAL secure-root inheritance writes and reads it; reset per test.
    /// </summary>
    internal AccessControl.AssignedAccessTestDoubles.FakeAssignedAccessStore InheritedLedger { get; private set; } = new();

    /// <summary>
    /// Task 158 r1c-v2 (round 47 item 1): task 142's materializer harness over the SAME ledger — its registry values
    /// (<c>Store.Assign</c>), contact links (<c>LinkedContact</c>) and record flags (<c>Participations</c>) — whose materializer
    /// the host's inheritance runs at once when a share it removes ends an Assigned-To row's coverage. Reset per test.
    /// </summary>
    internal AccessControl.AssignedAccessTestDoubles.Harness AssignedAccess { get; private set; } = null!;

    /// <summary>
    /// Task 158 r1c-v2: runs just before a revoke on a record lands (recorded first) — what the ledger says at the moment a
    /// share is removed (round 47 item 2: the operator's Declined marker is already there). Reset per test.
    /// </summary>
    internal Action<Guid, DataversePrincipalRef>? OnRevoke { get; set; }

    /// <summary>Task 158 r1c-v2: runs just before a ModifyAccess on a record lands (recorded first). Reset per test.</summary>
    internal Action<Guid, DataversePrincipalRef>? OnModify { get; set; }

    /// <summary>
    /// Task 158 r1c-v2: runs just AFTER a GrantAccess on a record landed — a concurrent change between a pass's share and its
    /// own checks of it (the parent unsecured or unshared, the provenance row ended). Reset per test.
    /// </summary>
    internal Action<Guid, DataversePrincipalRef>? OnGranted { get; set; }

    /// <summary>
    /// The materializer the host's inheritance holds: the harness's, over this host's share seam (so it reads the shares
    /// the inheritance writes), this host's real No Access guard, and this host's scopes (round 47 item 1 (3): its
    /// sharee-only pass reaches this host's inheritance).
    /// </summary>
    private Sprk.Bff.Api.Services.ExternalAccess.AssignedAccessMaterializer AssignedAccessMaterializerIn(
        IServiceProvider sp, IDataverseRecordShareService shares)
    {
        AssignedAccess ??= new AccessControl.AssignedAccessTestDoubles.Harness(InheritedLedger);
        AssignedAccess.SharesOverride = shares;
        AssignedAccess.GuardOverride = sp.GetRequiredService<SecureShareNoAccessGuard>();
        AssignedAccess.Scopes = sp.GetRequiredService<IServiceScopeFactory>();
        return AssignedAccess.Materializer;
    }

    /// <summary>Task 143 r1: systemusers whose task-141 link read FAILS (the guard then cannot verify them).</summary>
    internal HashSet<Guid> UnreadableLinkUsers { get; } = new();

    /// <summary>Business units' shared containers (<c>businessunit.sprk_containerid</c>), by business unit id (task 133 b2).</summary>
    public Dictionary<Guid, string> BusinessUnitContainers { get; } = new();

    /// <summary>When true, every query that looks a container up by <c>sprk_containerid</c> throws (task 133 b2).</summary>
    public bool ContainerOwnershipReadFails { get; set; }

    /// <summary>
    /// When set, every query that looks a container up by <c>sprk_containerid</c> fails with this HTTP status, in the real
    /// client's shape — <see cref="HttpRequestException"/> carrying it (owner round 14 item 3, task 133 c1-r4: a 401/403 is
    /// refused, a 503/429 transient).
    /// </summary>
    public System.Net.HttpStatusCode? ContainerOwnershipReadFailsWith { get; set; }

    /// <summary>
    /// A RevokeAccess for this principal is accepted but NOT applied — the share stays (task 133 b2: a restore that
    /// writes but reads back wrong). Not recorded in <see cref="Revokes"/>, which lists shares actually removed.
    /// </summary>
    public Guid? RevokeNotAppliedFor { get; set; }

    /// <summary>
    /// After an <c>ownerid</c> PATCH binding to this principal, the NEXT owner read-back throws (task 133 b2: a
    /// compensating move whose outcome cannot be verified). Unlike <see cref="OwnerReadBackFails"/>, the forward move's
    /// read-back still works.
    /// </summary>
    public Guid? FailOwnerReadBackAfterBindTo { get; set; }

    private bool _failNextOwnerReadBack;

    /// <summary>A container this host is configured to use for many records (<c>Communication:ArchiveContainerId</c>).</summary>
    public const string ConfiguredArchiveContainerId = "b!configured-communication-archive";

    /// <summary>The container type the host is configured with; <see cref="Reset"/> restores it.</summary>
    public const string ConfiguredContainerTypeId = "11111111-2222-3333-4444-555555555555";

    /// <summary>
    /// Changes <c>SharePointEmbedded:ContainerTypeId</c> on the running host — the endpoint reads it per request
    /// (task 133: an unconfigured container type is refused before any change).
    /// </summary>
    public void SetContainerTypeId(string? value)
    {
        Services.GetRequiredService<IConfiguration>()["SharePointEmbedded:ContainerTypeId"] = value;
        _containerTypeChanged = true;
    }

    private bool _containerTypeChanged;

    /// <summary>When false, the caller's Dataverse identity cannot be established.</summary>
    public bool CallerSystemUserIdResolves { get; set; } = true;

    /// <summary>When false, the delegation probe reports Read only — the caller lacks Write (task 144).</summary>
    public bool CallerHoldsWrite { get; set; } = true;

    /// <summary>
    /// When true, the caller's rights include Delete on every record, WHOEVER owns it — a Full Access holder, or an
    /// administrator whose security role grants Delete at business-unit or organization depth (task 150: owner round 3b F3,
    /// who may REMOVE the secure designation). Delete held by a ROLE.
    /// </summary>
    public bool CallerHoldsDelete { get; set; }

    /// <summary>
    /// Task 150 (round 53 item 3): Delete held by OWNERSHIP — a security role granting Delete at USER depth, so the probe
    /// answers Delete only for a record whose CURRENT owning user (the seeded row, read at the moment of the probe) is the
    /// caller. Unlike <see cref="CallerHoldsDelete"/> it is gone once the record is moved to the Secure Record owner team,
    /// so a fixture can tell a caller who held Full Access by owning the record from one who held it by a role — and a
    /// floor read after the owner move would miss the former.
    /// </summary>
    public bool CallerDeletesWhatTheyOwn { get; set; }

    /// <summary>
    /// Task 150 r2 (verifier F6): the SECOND rights probe of a record THROWS. The first is the route's Write gate; the
    /// second is the unsecure endpoint's own Full Access check (owner round 3b F3) — or, on Make Secure, the caller's
    /// effective-rights floor (round 46 item 1).
    /// </summary>
    public bool FullAccessProbeThrows { get; set; }

    /// <summary>
    /// Task 150 (round 46 item 1): the SECOND rights probe of a record answers <see cref="AccessRights.None"/> — what the
    /// real probe answers when it cannot answer (deliberately indistinguishable from "no rights") — while the first, the
    /// route's Write gate, answered Write.
    /// </summary>
    public bool FollowUpRightsProbeAnswersNone { get; set; }

    /// <summary>
    /// Task 150: every root read returns <c>sprk_issecure</c> EMPTY (JSON null) — what Dataverse answers for a
    /// field-secured column the reading identity has no Read on.
    /// </summary>
    public bool SecureFlagReadsEmpty { get; set; }

    /// <summary>Task 150: an UPDATE setting <c>sprk_issecure</c> to true throws (recorded first).</summary>
    public bool SecureFlagWriteFails { get; set; }

    /// <summary>Task 150: an UPDATE setting <c>sprk_issecure</c> to true is accepted but NOT applied.</summary>
    public bool SecureFlagWriteNotApplied { get; set; }
    /// Task 158 r1: the caller's AppendTo on any record the probe is asked about (G5 for a create INTO isolation — AppendTo
    /// on each secure parent). Default false, as before: every pre-r1 contract keeps the rights it was written against.
    /// </summary>
    public bool CallerHoldsAppendTo { get; set; }

    /// <summary>Task 158 r1: the caller's answer to a table-privilege check (G5's Create on the table).</summary>
    public bool CallerHoldsCreatePrivilege { get; set; } = true;

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

    /// <summary>
    /// Task 149: the Dataverse the secure-child share synchronizer reads (its POA writes go to the recording share
    /// service, so they land in <see cref="Grants"/>). Default: an environment with no Secure Record BU, where the
    /// fan-out after provisioning reads nothing and writes nothing.
    /// </summary>
    internal SecureChildShareWorld ChildWorld { get; set; } = SecureChildShareWorld.WithoutSecureBusinessUnit();

    /// <summary>
    /// One recorded POA operation. <c>AccessRightsCsv</c> is null for a revoke. <c>Sequence</c> shares
    /// <see cref="RecordedUpdate"/>'s counter, so a test can order a share against an owner PATCH (task 133).
    /// </summary>
    public sealed record RecordedShare(
        string EntitySet, Guid RecordId, DataversePrincipalRef Principal, string? AccessRightsCsv, int Sequence = 0);

    /// <summary>The next value of the counter <see cref="RecordedUpdate"/> and <see cref="RecordedShare"/> share.</summary>
    internal int NextSequence() => Interlocked.Increment(ref _updateSequence);

    /// <summary>Container display names passed to SPE, so a test can prove a container was created.</summary>
    public ConcurrentBag<string> CreatedContainerDisplayNames { get; } = new();

    /// <summary>
    /// The owning business unit passed with each container create — the unit stamped on the container
    /// (unified-access-control-r2 task 165, owner round 20 item 1).
    /// </summary>
    public ConcurrentBag<Guid> CreatedContainerBusinessUnits { get; } = new();

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

    /// <summary>When false, an UPDATE that RECORDS a container (a non-null <c>sprk_containerid</c>) throws. Default true.</summary>
    public bool ContainerStampSucceeds { get; set; } = true;

    /// <summary>
    /// When false, an UPDATE that CLEARS <c>sprk_containerid</c> (a null value) throws — the unlinking of a shared
    /// container before the owner move (task 133 r1). Default true.
    /// </summary>
    public bool ContainerClearSucceeds { get; set; } = true;

    /// <summary>
    /// When set, a read naming <c>sprk_createdbyperson</c> fails TRANSIENTLY with this HTTP status, in an environment that
    /// has the column (task 133 r1: told apart from the 400 an environment without it answers). Raised in the real
    /// client's shape — <c>EnsureSuccessStatusCode</c> → <see cref="HttpRequestException"/> carrying the status (task 133
    /// r2, verifier round 5 seed P14: an <see cref="InvalidOperationException"/> here let "every HTTP failure is
    /// column-missing" pass unseen).
    /// </summary>
    public System.Net.HttpStatusCode? CreatorPersonReadFailsWith { get; set; }

    /// <summary>
    /// When false, the ownership PATCH is accepted but NOT applied to the in-memory row — Dataverse's
    /// real behaviour for an unrecognised <c>@odata.bind</c> property.
    /// </summary>
    public bool OwnershipPatchIsApplied { get; set; } = true;

    // ── Task 133 c1 (owner round 10 item 4): the rows an owner move of a root cascades to ──

    /// <summary>
    /// The rows a root's Assign cascades to, by child id. Live metadata (2026-10-03): a project or matter cascades Assign
    /// to <c>sharepointdocumentlocation</c> and <c>sharepointdocument</c> on <c>regardingobjectid</c> (and to the
    /// business-owned <c>team</c>, which has no owner); a work assignment to nothing. <see cref="ApplyUpdate"/> applies the
    /// cascade the way Dataverse does — every child of the moved root takes the root's new owner.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, CascadeChildRow> _cascadeChildren = new();

    private sealed record CascadeChildRow(string EntitySet, string IdColumn, Guid Id, Guid RootId, DataversePrincipalRef Owner);

    /// <summary>
    /// Seeds a row a root's Assign cascades to: <c>sharepointdocumentlocation</c> or <c>sharepointdocument</c>, regarding
    /// <paramref name="rootId"/>, owned by <paramref name="owner"/>.
    /// </summary>
    public void SeedCascadeChild(Guid rootId, string table, Guid childId, DataversePrincipalRef owner)
        => _cascadeChildren[childId] = table switch
        {
            "sharepointdocumentlocation" => new("sharepointdocumentlocations", "sharepointdocumentlocationid", childId, rootId, owner),
            "sharepointdocument" => new("sharepointdocuments", "sharepointdocumentid", childId, rootId, owner),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "Not a table a root's Assign cascades to.")
        };

    /// <summary>The current owner of a seeded cascade child.</summary>
    public DataversePrincipalRef? OwnerOfCascadeChild(Guid childId)
        => _cascadeChildren.TryGetValue(childId, out var child) ? child.Owner : null;

    /// <summary>
    /// Dataverse's live answer in dev (2026-10-03): a read of <c>sharepointdocuments</c> is refused 400 — 0x80071017
    /// "SharePoint S2S and MSTeams integration is not enabled for this org". Default true, as in dev; false models an
    /// org with that integration on.
    /// </summary>
    public bool SharePointDocumentReadRefused { get; set; } = true;

    /// <summary>
    /// When set, the snapshot's read of a root's <c>sharepointdocumentlocations</c> (by <c>regardingobjectid</c>) fails
    /// with this status, in the real client's shape (<see cref="HttpRequestException"/> carrying it).
    /// </summary>
    public System.Net.HttpStatusCode? CascadeChildSnapshotReadFailsWith { get; set; }

    /// <summary>An <c>ownerid</c> PATCH on THIS cascade child throws (recorded first) — a child that cannot be put back.</summary>
    public Guid? FailChildOwnerBindFor { get; set; }

    /// <summary>
    /// An <c>ownerid</c> PATCH on THIS cascade child is ACCEPTED (recorded, no error) but NOT applied: the child keeps the
    /// owner it had (task 133 c1-r2). The child-level twin of <see cref="OwnershipPatchIsApplied"/> — Dataverse's silent
    /// <c>@odata.bind</c> failure — so the restore's read-back, not the PATCH's success, must decide.
    /// </summary>
    public Guid? IgnoreChildOwnerBindFor { get; set; }

    /// <summary>
    /// Once an <c>ownerid</c> PATCH on THIS cascade child has been sent (recorded first), the row no longer reads — deleted
    /// by someone else, or simply not returned (task 133 c1-r4): the restore's read-back after the PATCH finds NO row. The
    /// PATCH itself is accepted, or — with <see cref="FailChildOwnerBindFor"/> on the same child — refused. The read
    /// BEFORE the PATCH still finds the row, so it is never <c>Gone</c>: only that read decides <c>Gone</c>, and a row that
    /// disappears after its PATCH is a failure (<c>NotApplied</c> / <c>Refused</c>), never "restored".
    /// </summary>
    public Guid? RemoveChildOnBindFor { get; set; }

    /// <summary>
    /// A read of THIS cascade child returns its row WITHOUT its id column (task 133 c1-r2): a row the snapshot could not
    /// key a restore on, so the snapshot must refuse it — never record it under an empty id.
    /// </summary>
    public Guid? ChildRowReadWithoutIdFor { get; set; }

    /// <summary>
    /// A by-id read of THIS cascade child throws 503, in the real client's shape (task 133 c1-r1): the restore's read of
    /// its current owner, BEFORE any PATCH — a child whose owner cannot be read, so nothing is written to it. The
    /// snapshot's by-regarding read is unaffected.
    /// </summary>
    public Guid? FailChildOwnerReadFor { get; set; }

    /// <summary>
    /// Once an <c>ownerid</c> PATCH on THIS cascade child has been applied, its by-id read throws 503 (task 133 c1-r1):
    /// the restore's read-back AFTER the PATCH — a child put back whose owner cannot be confirmed. The read before the
    /// PATCH still answers.
    /// </summary>
    public Guid? FailChildOwnerReadBackAfterBindFor { get; set; }

    /// <summary>The cascade children an <c>ownerid</c> PATCH has been applied to (for <see cref="FailChildOwnerReadBackAfterBindFor"/>).</summary>
    private readonly ConcurrentDictionary<Guid, byte> _cascadeChildBindsApplied = new();

    /// <summary>Every query the endpoints issued (entity set, filter) — so a test can prove a table was never read.</summary>
    public ConcurrentBag<(string EntitySet, string? Filter)> Queries { get; } = new();

    /// <summary>
    /// When true, the ownership PATCH is APPLIED and then fails as an HttpClient timeout would — Dataverse committed
    /// it, the caller never heard back (unified-access-control-r2 task 132: an ambiguous re-own). Default false.
    /// Batch 4 integration: task 132's own <c>OwnerReadBackFails</c> is the same flag as task 133's above (the owner
    /// read-back throws), so the fixture keeps the one definition.
    /// </summary>
    public bool OwnershipPatchTimesOutAfterApplying { get; set; }

    private sealed record SeededRecord(
        string EntitySet, Guid Id, Guid? OwningTeamId, string? ContainerId, Guid? LegacySecurityBuId, bool IsSecure,
        Guid? OwningUserId = null, Guid? OwningBusinessUnitId = null, Guid? CreatedBy = null, Guid? CreatedByPerson = null);

    /// <summary>
    /// Seeds a project row. Every Dataverse row has an owner and a creator, so a row seeded without an owning team is
    /// owned by the caller (the wizard's own create), and every row is created by the caller unless
    /// <paramref name="createdBy"/> says otherwise (task 133: what compensation restores, and who a resume shares to).
    /// </summary>
    public void SeedProject(
        Guid projectId,
        Guid? owningTeamId = null,
        string? containerId = null,
        Guid? legacySecurityBuId = null,
        bool isSecure = true,
        Guid? owningBusinessUnitId = null,
        Guid? owningUserId = null,
        Guid? createdBy = null,
        Guid? createdByPerson = null)
        => Seed(ProjectEntitySet, projectId, owningTeamId, containerId, legacySecurityBuId, isSecure, owningBusinessUnitId,
            owningUserId, createdBy, createdByPerson);

    /// <summary>Seeds a matter row (task 144; creator columns task 133 b2).</summary>
    public void SeedMatter(
        Guid matterId, Guid? owningTeamId = null, string? containerId = null, bool isSecure = true,
        Guid? createdBy = null, Guid? createdByPerson = null)
        => Seed(MatterEntitySet, matterId, owningTeamId, containerId, null, isSecure, null, null, createdBy, createdByPerson);

    /// <summary>Seeds a work-assignment row (task 144; creator columns task 133 b2).</summary>
    public void SeedWorkAssignment(
        Guid workAssignmentId, Guid? owningTeamId = null, string? containerId = null, bool isSecure = true,
        Guid? createdBy = null, Guid? createdByPerson = null)
        => Seed(WorkAssignmentEntitySet, workAssignmentId, owningTeamId, containerId, null, isSecure, null, null,
            createdBy, createdByPerson);

    private void Seed(
        string entitySet, Guid id, Guid? owningTeamId, string? containerId, Guid? legacySecurityBuId, bool isSecure,
        Guid? owningBusinessUnitId, Guid? owningUserId, Guid? createdBy, Guid? createdByPerson)
    {
        _records[id] = new SeededRecord(
            entitySet, id, owningTeamId, containerId, legacySecurityBuId, isSecure,
            OwningUserId: owningTeamId is null ? owningUserId ?? CallerSystemUserId : null,
            OwningBusinessUnitId: owningBusinessUnitId ?? BusinessUnitOf(owningTeamId),
            CreatedBy: createdBy ?? CallerSystemUserId,
            CreatedByPerson: createdByPerson);
        MirrorIntoChildWorld(_records[id]);
    }

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
    /// <summary>
    /// Batch-4 integration (round 26 item 3): the Make Secure file relocation runs through the REAL
    /// <see cref="Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator"/>. A test that exercises it supplies one over
    /// its own document-pointer world (read at request time); otherwise the host's own registration is used (no document
    /// of these worlds carries a file, so it answers NoFile and moves nothing).
    /// </summary>
    internal Func<Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator>? RelocatorOverride { get; set; }

    public void Reset()
    {
        RelocatorOverride = null;
        _records.Clear();
        CreatedEntitySets.Clear();
        Updates.Clear();
        DelegationProbes.Clear();
        CreatedContainerDisplayNames.Clear();
        CreatedContainerBusinessUnits.Clear();
        SecureBuMatchCount = 1;
        OwnerTeamMatchCount = 1;
        DefaultTeamCarriesOwnerTeamName = false;
        OwnerTeamMembers.Clear();
        MembershipReadSucceeds = true;
        SecureBuUsers.Clear();
        BusinessUnitUserReadSucceeds = true;
        SpeContainerCreationSucceeds = true;
        ContainerStampSucceeds = true;
        ContainerClearSucceeds = true;
        CreatorPersonReadFailsWith = null;
        OwnershipPatchIsApplied = true;
        OwnershipPatchTimesOutAfterApplying = false;
        _updateSequence = 0;
        Grants.Clear();
        Revokes.Clear();
        Modifies.Clear();
        _shares.Clear();
        GrantToCurrentOwnerRefused = false;
        AssignDropsShareOf = null;
        FailShareWhileSecureOwned = null;
        FailStrictShareReadWhileSecureOwned = false;
        FailNextStrictShareReads = 0;
        FailOwnerBindTo = null;
        OwnerReadBackFails = false;
        SystemUsers.Clear();
        SystemUsers[CallerSystemUserId] = (false, false);
        SystemUserByIdReadSucceeds = true;
        SystemUserReadFailsFor = null;
        SystemUserReadFailsWith = null;
        CreatorPersonColumnExists = true;
        BusinessUnitContainers.Clear();
        ContainerOwnershipReadFails = false;
        ContainerOwnershipReadFailsWith = null;
        RevokeNotAppliedFor = null;
        FailOwnerReadBackAfterBindTo = null;
        _failNextOwnerReadBack = false;
        if (_containerTypeChanged)
            SetContainerTypeId(ConfiguredContainerTypeId);
        _containerTypeChanged = false;
        _cascadeChildren.Clear();
        SharePointDocumentReadRefused = true;
        CascadeChildSnapshotReadFailsWith = null;
        FailChildOwnerBindFor = null;
        IgnoreChildOwnerBindFor = null;
        RemoveChildOnBindFor = null;
        ChildRowReadWithoutIdFor = null;
        FailChildOwnerReadFor = null;
        FailChildOwnerReadBackAfterBindFor = null;
        _cascadeChildBindsApplied.Clear();
        Queries.Clear();
        CallerSystemUserIdResolves = true;
        CallerHoldsWrite = true;
        CallerHoldsDelete = false;
        CallerDeletesWhatTheyOwn = false;
        FullAccessProbeThrows = false;
        FollowUpRightsProbeAnswersNone = false;
        SecureFlagReadsEmpty = false;
        SecureFlagWriteFails = false;
        SecureFlagWriteNotApplied = false;
        CallerHoldsAppendTo = false;
        CallerHoldsCreatePrivilege = true;
        FailShareForPrincipal = null;
        FailRevokeForPrincipal = null;
        StrictShareReadSucceeds = true;
        SoftShareReadSucceeds = true;
        NoAccessList = new AccessControl.GrantPolicyTestDoubles.SeamNoAccessListReader();
        NoAccessReads = new AccessControl.GrantPolicyTestDoubles.FlagStubParticipationService(
            defaultFlags: new RootRecordFlags(IsSecure: true, IsRestricted: false));
        UnreadableLinkUsers.Clear();
        InheritedLedger = new AccessControl.AssignedAccessTestDoubles.FakeAssignedAccessStore();
        AssignedAccess = new AccessControl.AssignedAccessTestDoubles.Harness(InheritedLedger);
        OnRevoke = null;
        OnModify = null;
        OnGranted = null;
        Logs.Clear();
        ChildWorld = SecureChildShareWorld.WithoutSecureBusinessUnit();
        _childWorldMirrorsRoots = false;
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
                ["SharePointEmbedded:ContainerTypeId"] = ConfiguredContainerTypeId,
                // Task 133 b2: a container this host uses for many records — a record carrying it is not its own.
                ["Communication:ArchiveContainerId"] = ConfiguredArchiveContainerId
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
            var recordShare = new RecordingRecordShareService(this);
            services.AddSingleton<IDataverseRecordShareService>(recordShare);

            // Task 149: the REAL synchronizer, over the test's ChildWorld (read at call time) and the recording shares —
            // scoped with THIS host's real No Access guard, as production registers it (ExternalAccessModule). Task 158 r1c:
            // it was a singleton over a guard that walls nobody, so no mirror wall was ever exercised through this host.
            services.RemoveAll<SecureChildShareSynchronizer>();
            services.AddScoped(sp => SecureChildShareWorld.SynchronizerOver(
                () => ChildWorld, recordShare, sp.GetRequiredService<SecureShareNoAccessGuard>()));

            // Task 148: the REAL reconciler (real resolver, real synchronizer) over the same ChildWorld and recording shares;
            // the platform-cascade rows through this fixture's DataverseWebApiClient double, as in production.
            services.RemoveAll<SecureChildReconciler>();
            services.AddScoped(sp => SecureChildShareWorld.ReconcilerOver(
                () => ChildWorld, recordShare, sp.GetRequiredService<DataverseWebApiClient>(),
                // Batch-4 integration, 148 × 132: the child owner-change eviction, through the host's hook as in production.
                accessCacheInvalidator: sp.GetService<Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator>()));

            // Round 26 item 3 (batch-4 integration): the Make Secure file relocator — the test's, when it supplies one.
            services.RemoveAll<Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator>();
            services.AddScoped(sp => RelocatorOverride is { } make
                ? make()
                : ActivatorUtilities.CreateInstance<Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator>(
                    sp, sp.GetRequiredService<DataverseAccessDataSource>()));

            // Task 158: the REAL secure-root inheritance — it reads what work assignments and projects are filed under
            // through the same ChildWorld, and secures one through THIS host's provisioning (its DataverseWebApiClient
            // double, its container stub, its recording shares). With the default world (no rows) it finds nothing filed
            // under anything, so every existing provisioning / unsecure contract is unchanged.
            services.RemoveAll<SecureRootInheritance>();
            services.AddScoped(sp => new SecureRootInheritance(
                SecureChildShareWorld.EntitiesOver(() => ChildWorld).Object,
                sp.GetRequiredService<DataverseWebApiClient>(),
                sp.GetRequiredService<SpeFileStore>(),
                recordShare,
                sp.GetRequiredService<SecureChildReconciler>(),
                sp.GetRequiredService<SecureChildShareSynchronizer>(),
                sp.GetRequiredService<SecureShareNoAccessGuard>(),
                InheritedLedger,
                AssignedAccessMaterializerIn(sp, recordShare),
                sp.GetRequiredService<IConfiguration>(),
                sp.GetRequiredService<ILogger<SecureRootInheritance>>(),
                // Batch-4 integration: provisioning's owner-change eviction and Make Secure file relocation, as in the host.
                sp.GetService<Sprk.Bff.Api.Services.Ai.Membership.IMembershipCacheInvalidator>(),
                sp.GetRequiredService<Sprk.Bff.Api.Services.Documents.DocumentContainerRelocator>()));

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

                    Queries.Add((entitySet, filter));
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

            // Task 143 — the production guard over this fixture's deny list (read per request, so Reset takes effect).
            // Every user is "read, linked to no contact": the systemuser subject is what these tests exercise.
            var links = new Mock<IContactIdentityStore>();
            links.Setup(s => s.GetSystemUserAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => UnreadableLinkUsers.Contains(id)
                    ? SystemUserLookup.Failed
                    : new SystemUserLookup(LookupStatus.Read, new SystemUserIdentityRow(id, null, null, null, null, null)));
            services.RemoveAll<SecureShareNoAccessGuard>();
            services.AddScoped(_ => new SecureShareNoAccessGuard(
                NoAccessReads, NoAccessList, links.Object,
                // Task 158 r1c-v2 (round 39 item 2): its filing walk reads what work assignments and projects are filed under
                // through the same ChildWorld the inheritance reads.
                SecureChildShareWorld.EntitiesOver(() => ChildWorld).Object,
                NullLogger<SecureShareNoAccessGuard>.Instance));

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

        if (FailOwnerBindTo is { } refusedOwner
            && flat.TryGetValue("ownerid@odata.bind", out var refusedBind)
            && refusedBind is not null
            && ParseIdFromBind(refusedBind) == refusedOwner)
        {
            throw new InvalidOperationException("Dataverse 403: simulated refusal of the ownership assignment.");
        }

        if (FailOwnerReadBackAfterBindTo is { } unverifiableOwner
            && flat.TryGetValue("ownerid@odata.bind", out var unverifiableBind)
            && unverifiableBind is not null
            && ParseIdFromBind(unverifiableBind) == unverifiableOwner)
        {
            _failNextOwnerReadBack = true;
        }

        var setsSecureFlag = flat.TryGetValue("sprk_issecure", out var flagWrite)
                             && bool.TryParse(flagWrite, out var flagValue) && flagValue;

        if (setsSecureFlag && SecureFlagWriteFails)
            throw new InvalidOperationException("Dataverse 403: simulated refusal of the sprk_issecure write (field security).");

        if (flat.TryGetValue("sprk_containerid", out var containerWrite))
        {
            if (containerWrite is not null && !ContainerStampSucceeds)
            {
                throw new InvalidOperationException(
                    "Dataverse 400: simulated failure recording sprk_containerid.");
            }

            if (containerWrite is null && !ContainerClearSucceeds)
            {
                throw new InvalidOperationException(
                    "Dataverse 503: simulated failure clearing sprk_containerid.");
            }
        }

        // Task 133 c1: putting a cascaded child back on its own owner — its own owner bind, as the endpoint sends it.
        if (_cascadeChildren.TryGetValue(id, out var cascadeChild)
            && cascadeChild.EntitySet == entitySet
            && flat.TryGetValue("ownerid@odata.bind", out var childBind)
            && childBind is not null)
        {
            // Task 133 c1-r4: the row is gone once its bind has been sent (recorded above) — whatever the PATCH reports.
            if (RemoveChildOnBindFor == id)
                _cascadeChildren.TryRemove(id, out _);

            if (FailChildOwnerBindFor == id)
                throw new InvalidOperationException("Dataverse 403: simulated refusal of a child row's ownership assignment.");

            // Task 133 c1-r2: accepted, recorded above, and silently not applied. Task 133 c1-r4: or accepted, with the
            // row gone — nothing left to apply it to.
            if (IgnoreChildOwnerBindFor == id || RemoveChildOnBindFor == id)
                return Task.CompletedTask;

            if (ParseIdFromBind(childBind) is { } childOwnerId)
            {
                _cascadeChildren[id] = cascadeChild with
                {
                    Owner = childBind.Contains("/systemusers(", StringComparison.OrdinalIgnoreCase)
                        ? DataversePrincipalRef.User(childOwnerId)
                        : DataversePrincipalRef.Team(childOwnerId)
                };
                _cascadeChildBindsApplied[id] = 0;
            }

            return Task.CompletedTask;
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

                    // Live gate (b) disproved: the reassignment drops this principal's share.
                    if (AssignDropsShareOf is { } dropped)
                        _shares.TryRemove((id, DataversePrincipalRef.User(dropped)), out _);

                    // Task 133 c1: the Assign cascade (live metadata) — every child of a moved project or matter takes
                    // the root's new owner; a work assignment cascades nothing.
                    if (entitySet is ProjectEntitySet or MatterEntitySet)
                    {
                        var newOwner = ownerBind.Contains("/systemusers(", StringComparison.OrdinalIgnoreCase)
                            ? DataversePrincipalRef.User(parsed)
                            : DataversePrincipalRef.Team(parsed);
                        foreach (var child in _cascadeChildren.Values.Where(c => c.RootId == id).ToList())
                            _cascadeChildren[child.Id] = child with { Owner = newOwner };
                    }
                }
            }

            if (flat.TryGetValue("sprk_issecure", out var isSecureRaw) && !(setsSecureFlag && SecureFlagWriteNotApplied))
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

            MirrorIntoChildWorld(_records[id]);
        }

        if (OwnershipPatchTimesOutAfterApplying && flat.ContainsKey("ownerid@odata.bind"))
        {
            // Applied above; the response never arrives — what an HttpClient timeout looks like to the caller.
            return Task.FromException(new TaskCanceledException(
                "Simulated timeout: Dataverse committed the ownership PATCH but the response never arrived."));
        }

        return Task.CompletedTask;
    }

    // ── Task 148: the root as the reconciler, the resolver and the synchronizer see it ─────────────────────────────

    /// <summary>
    /// When true (<see cref="UseChildWorldForRoots"/>), every seeded root is ALSO a row of <see cref="ChildWorld"/>, and every
    /// owner / flag change the endpoints make to it is applied there too — so the secure-child reconciler (which reads the
    /// root, its children and the Secure team through <c>IGenericEntityService</c>) sees the same record the endpoint moved.
    /// </summary>
    private bool _childWorldMirrorsRoots;

    /// <summary>
    /// Task 148: a <see cref="ChildWorld"/> whose Secure Record BU and named owner team are THIS fixture's ids (so both
    /// planes agree on which team isolates a record), with the caller in the general business unit, sharing this fixture's
    /// write sequence, and mirroring every seeded root (those seeded so far and those seeded after).
    /// </summary>
    internal SecureChildShareWorld UseChildWorldForRoots()
    {
        ChildWorld = SecureChildShareWorld.Standard(SecureBuId, SecureOwnerTeamId)
            .User(CallerSystemUserId, SecureChildShareWorld.GeneralBu);
        ChildWorld.Sequence = NextSequence;
        // Task 158 r1: a row the isolated-create compensation deletes is gone from this fixture's Dataverse too.
        ChildWorld.OnDeleted = (_, id) => _records.TryRemove(id, out SeededRecord? _);
        _childWorldMirrorsRoots = true;
        foreach (var record in _records.Values)
            MirrorIntoChildWorld(record);
        return ChildWorld;
    }

    private void MirrorIntoChildWorld(SeededRecord record)
    {
        if (!_childWorldMirrorsRoots)
            return;

        var logical = record.EntitySet[..^1]; // sprk_projects → sprk_project (the three roots)
        if (!ChildWorld.Has(logical, record.Id))
            ChildWorld.Add(logical, record.Id);

        if (record.OwningTeamId is { } team)
            ChildWorld.MoveOwner(logical, record.Id, DataversePrincipalRef.Team(team));
        else if (record.OwningUserId is { } user)
            ChildWorld.MoveOwner(logical, record.Id, DataversePrincipalRef.User(user));
        ChildWorld.Set(logical, record.Id, "sprk_issecure", record.IsSecure);

        // Task 158: the record's own container — the secure-root inheritance reads it (with the owner) to tell an isolated
        // record from one whose provisioning has not completed.
        ChildWorld.Set(logical, record.Id, "sprk_containerid", record.ContainerId);
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
        "ownerid", "_owningteam_value", "_owninguser_value", "_owningbusinessunit_value", "_createdby_value"
    };

    /// <summary>The <c>sprk_matter</c> columns these endpoints may read (live metadata 2026-10-01, task 144).</summary>
    internal static readonly HashSet<string> LiveMatterColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_matterid", "sprk_mattername", "sprk_matternumber", "sprk_issecure", "sprk_accesspermission",
        "sprk_containerid", "_sprk_securitybu_value", "statecode", "statuscode", "createdon", "modifiedon",
        "ownerid", "_owningteam_value", "_owninguser_value", "_owningbusinessunit_value", "_createdby_value"
    };

    /// <summary>
    /// The <c>sprk_workassignment</c> columns these endpoints may read (live metadata 2026-10-01, task 144). Its name
    /// column is <c>sprk_name</c> — the one root whose name column is NOT <c>{entity}name</c>.
    /// </summary>
    internal static readonly HashSet<string> LiveWorkAssignmentColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "sprk_workassignmentid", "sprk_name", "sprk_issecure", "sprk_accesspermission", "sprk_containerid",
        "_sprk_securitybu_value", "statecode", "statuscode", "createdon", "modifiedon",
        "ownerid", "_owningteam_value", "_owninguser_value", "_owningbusinessunit_value", "_createdby_value"
    };

    /// <summary>
    /// The read form of <c>sprk_createdbyperson</c> — on all three roots once <c>scripts/Set-RecordCreatorPersonSchema.ps1</c>
    /// has run (task 133 b2, owner round 7 item 2; NOT yet live in dev — the schema is a pending manual gate). Kept out of
    /// the live sets above, which are verified metadata, and admitted only while <see cref="CreatorPersonColumnExists"/>.
    /// </summary>
    internal const string CreatorPersonReadColumn = "_sprk_createdbyperson_value";

    /// <summary>
    /// Dataverse's own behaviour: a projection naming a column the table lacks is a 400. THE GUARD that task 016 built
    /// for the closure cascade and this fixture once lacked — a fake that ignores the projection goes green on code
    /// that 400s in production.
    /// </summary>
    private void RejectUnknownColumns(string entitySet, string? select)
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
            if (CreatorPersonColumnExists && string.Equals(column, CreatorPersonReadColumn, StringComparison.OrdinalIgnoreCase))
            {
                if (CreatorPersonReadFailsWith is { } transientStatus)
                {
                    throw new HttpRequestException(
                        $"Dataverse {(int)transientStatus}: simulated transient failure reading the creator person.",
                        inner: null,
                        statusCode: transientStatus);
                }
                continue;
            }

            if (!live.Contains(column))
            {
                // The shape the real DataverseWebApiClient raises: EnsureSuccessStatusCode → HttpRequestException carrying
                // the status (task 133 r1 tells a 400 — a column this environment lacks — from a transient failure).
                throw new HttpRequestException(
                    $"Dataverse 400: Could not find a property named '{column}' on entity set '{entitySet}'.",
                    inner: null,
                    statusCode: System.Net.HttpStatusCode.BadRequest);
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
    /// <summary>
    /// <see cref="ContainerOwnershipReadFailsWith"/>: a container-holder read fails with that status, in the real client's
    /// shape (task 133 c1-r4).
    /// </summary>
    private void ThrowIfContainerOwnershipReadFailsWithStatus()
    {
        if (ContainerOwnershipReadFailsWith is { } status)
        {
            throw new HttpRequestException(
                $"Dataverse {(int)status}: simulated failure reading which records hold a container.",
                inner: null,
                statusCode: status);
        }
    }

    private string RowsJsonFor(string entitySet, string? filter, string? select = null, int? top = null)
    {
        RejectUnknownColumns(entitySet, select);

        var payload = new List<Dictionary<string, object?>>();

        switch (entitySet)
        {
            case ProjectEntitySet:
            case MatterEntitySet:
            case WorkAssignmentEntitySet:
                // Task 133 b2: "which roots record this container?" — honours the container literal and the record the
                // query excludes, so a classification that forgot either would go red.
                if (filter is not null && ExtractQuoted(filter, "sprk_containerid eq ") is { } holderContainer)
                {
                    if (ContainerOwnershipReadFails)
                        throw new InvalidOperationException("Dataverse 503: simulated failure reading container holders.");
                    ThrowIfContainerOwnershipReadFailsWithStatus();

                    var excluded = ExtractGuidAfter(filter, " ne ");
                    var holderIdColumn = entitySet switch
                    {
                        ProjectEntitySet => "sprk_projectid",
                        MatterEntitySet => "sprk_matterid",
                        _ => "sprk_workassignmentid"
                    };
                    payload.AddRange(_records.Values
                        .Where(r => r.EntitySet == entitySet
                                    && string.Equals(r.ContainerId, holderContainer, StringComparison.Ordinal)
                                    && r.Id != excluded)
                        .Select(r => new Dictionary<string, object?> { [holderIdColumn] = r.Id }));
                    break;
                }

                var seeded = _records.Values.FirstOrDefault(
                    r => r.EntitySet == entitySet
                         && filter is not null
                         && filter.Contains(r.Id.ToString(), StringComparison.OrdinalIgnoreCase));

                if (seeded is not null && OwnerReadBackFails
                    && !(select ?? string.Empty).Contains("sprk_issecure", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Dataverse 503: simulated failure reading the owner back.");
                }

                if (seeded is not null && _failNextOwnerReadBack
                    && (select ?? string.Empty).Contains("_owningteam_value", StringComparison.OrdinalIgnoreCase)
                    && !(select ?? string.Empty).Contains("sprk_issecure", StringComparison.OrdinalIgnoreCase))
                {
                    _failNextOwnerReadBack = false;
                    throw new InvalidOperationException("Dataverse 503: simulated failure reading the owner back after the undo.");
                }

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
                        ["sprk_issecure"] = SecureFlagReadsEmpty ? null : seeded.IsSecure
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

                    if (seeded.CreatedBy is { } createdBy && createdBy != Guid.Empty)
                        row["_createdby_value"] = createdBy;

                    if (seeded.CreatedByPerson is { } createdByPerson
                        && (select ?? string.Empty).Contains(CreatorPersonReadColumn, StringComparison.OrdinalIgnoreCase))
                    {
                        row[CreatorPersonReadColumn] = createdByPerson;
                    }

                    payload.Add(row);
                }
                break;

            case "sharepointdocumentlocations":
            case "sharepointdocuments":
                // Task 133 c1: the rows a root's Assign cascades to, by regardingobjectid (the snapshot) or by id (a
                // restore's read and read-back).
                if (entitySet == "sharepointdocuments" && SharePointDocumentReadRefused)
                {
                    throw new HttpRequestException(
                        "Dataverse 400: 0x80071017 SharePoint S2S and MSTeams integration is not enabled for this org.",
                        inner: null,
                        statusCode: System.Net.HttpStatusCode.BadRequest);
                }

                var regardingRoot = filter is null ? null : ExtractGuidAfter(filter, "_regardingobjectid_value eq ");
                if (regardingRoot is not null && entitySet == "sharepointdocumentlocations"
                    && CascadeChildSnapshotReadFailsWith is { } snapshotStatus)
                {
                    throw new HttpRequestException(
                        $"Dataverse {(int)snapshotStatus}: simulated failure reading the document locations.",
                        inner: null,
                        statusCode: snapshotStatus);
                }

                // Task 133 c1-r1: a restore's by-id read of one child — before its PATCH, or its read-back after it.
                if (regardingRoot is null && filter is not null
                    && ((FailChildOwnerReadFor is { } unreadableChild
                         && filter.Contains(unreadableChild.ToString(), StringComparison.OrdinalIgnoreCase))
                        || (FailChildOwnerReadBackAfterBindFor is { } unverifiableChild
                            && _cascadeChildBindsApplied.ContainsKey(unverifiableChild)
                            && filter.Contains(unverifiableChild.ToString(), StringComparison.OrdinalIgnoreCase))))
                {
                    throw new HttpRequestException(
                        "Dataverse 503: simulated failure reading a related row's owner by id.",
                        inner: null,
                        statusCode: System.Net.HttpStatusCode.ServiceUnavailable);
                }

                payload.AddRange(_cascadeChildren.Values
                    .Where(c => c.EntitySet == entitySet
                                && (regardingRoot is { } rootId
                                    ? c.RootId == rootId
                                    : filter is not null && filter.Contains(c.Id.ToString(), StringComparison.OrdinalIgnoreCase)))
                    .Select(c =>
                    {
                        var row = new Dictionary<string, object?>
                        {
                            [c.Owner.Kind == DataversePrincipalKind.SystemUser ? "_owninguser_value" : "_owningteam_value"] = c.Owner.Id
                        };
                        if (ChildRowReadWithoutIdFor != c.Id) // task 133 c1-r2: a row read without its id column
                            row[c.IdColumn] = c.Id;
                        return row;
                    }));
                break;

            case "businessunits" when filter is not null && ExtractQuoted(filter, "sprk_containerid eq ") is { } buContainer:
                // Task 133 b2: which business unit's shared container is this?
                if (ContainerOwnershipReadFails)
                    throw new InvalidOperationException("Dataverse 503: simulated failure reading business-unit containers.");
                ThrowIfContainerOwnershipReadFailsWithStatus();

                payload.AddRange(BusinessUnitContainers
                    .Where(b => string.Equals(b.Value, buContainer, StringComparison.Ordinal))
                    .Select(b => new Dictionary<string, object?> { ["businessunitid"] = b.Key }));
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

            case "systemusers" when filter is not null && filter.Contains("systemuserid eq ", StringComparison.OrdinalIgnoreCase):
                // Task 133: a resume reads the record's createdby by id.
                if (!SystemUserByIdReadSucceeds
                    || (SystemUserReadFailsFor is { } unreadableUser
                        && filter.Contains(unreadableUser.ToString(), StringComparison.OrdinalIgnoreCase)))
                {
                    // Task 133 c1-r4 (owner round 14 item 3): in the real client's shape when a status is set.
                    if (SystemUserReadFailsWith is { } systemUserStatus)
                    {
                        throw new HttpRequestException(
                            $"Dataverse {(int)systemUserStatus}: simulated systemuser read failure.",
                            inner: null,
                            statusCode: systemUserStatus);
                    }

                    throw new InvalidOperationException("Dataverse 503: simulated systemuser read failure.");
                }

                foreach (var (userId, (isDisabled, isApplicationUser)) in SystemUsers)
                {
                    if (!filter.Contains(userId.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
                    payload.Add(new Dictionary<string, object?>
                    {
                        ["systemuserid"] = userId,
                        ["isdisabled"] = isDisabled,
                        ["applicationid"] = isApplicationUser ? Guid.Parse("a0000000-0000-0000-0000-0000000000a9") : null
                    });
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

    /// <summary>The GUID following <paramref name="prefix"/> in an OData filter (e.g. <c>" ne "</c>), or null.</summary>
    private static Guid? ExtractGuidAfter(string filter, string prefix)
    {
        var at = filter.IndexOf(prefix, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return null;
        var start = at + prefix.Length;
        var length = Math.Min(36, filter.Length - start);
        return Guid.TryParse(filter.AsSpan(start, length), out var id) ? id : null;
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
            Guid containerTypeId, string displayName, Guid owningBusinessUnitId, string? description = null,
            CancellationToken ct = default)
        {
            _fixture.CreatedContainerDisplayNames.Add(displayName);
            _fixture.CreatedContainerBusinessUnits.Add(owningBusinessUnitId);

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
            var earlierProbes = _fixture.DelegationProbes.Count(p => p.EntitySet == entitySet && p.RecordId == recordId);
            _fixture.DelegationProbes.Add((entitySet, recordId));
            if (_fixture.FullAccessProbeThrows && earlierProbes >= 1)
                throw new HttpRequestException("Dataverse 503: simulated failure of RetrievePrincipalAccess.");
            if (_fixture.FollowUpRightsProbeAnswersNone && earlierProbes >= 1)
                return Task.FromResult(AccessRights.None);

            var rights = _fixture.CallerHoldsWrite
                ? AccessRights.Read | AccessRights.Write
                : AccessRights.Read;

            // Delete by a role (whoever owns the record), or by ownership (only while the caller owns it — read now).
            var deletes = _fixture.CallerHoldsDelete
                          || (_fixture.CallerDeletesWhatTheyOwn && _fixture.OwningUserOf(recordId) == CallerSystemUserId);
            if (deletes)
                rights |= AccessRights.Delete;
            // Task 158 r1: AppendTo on each secure parent (G5 for a create INTO isolation).
            return Task.FromResult(_fixture.CallerHoldsAppendTo ? rights | AccessRights.AppendTo : rights);
        }

        /// <summary>Task 158 r1: the caller's own table privilege (G5 Create), as the OBO probe would answer it.</summary>
        public override Task<bool> CallerHoldsPrivilegeAsync(
            string? callerBearerToken, string privilegeName, CancellationToken ct = default)
            => Task.FromResult(_fixture.CallerHoldsCreatePrivilege);

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
            ThrowIfWriteRefused(recordId, principal);

            if (_fixture.GrantToCurrentOwnerRefused
                && principal.Kind == DataversePrincipalKind.SystemUser
                && _fixture.OwningUserOf(recordId) == principal.Id)
            {
                throw new InvalidOperationException($"Seeded refusal: {principal.Id} owns the record.");
            }

            _fixture.Grants.Add(new RecordedShare(entitySetName, recordId, principal, accessRightsCsv, _fixture.NextSequence()));

            // GrantAccess on an existing share is not documented to replace its rights; the double unions them, the
            // conservative reading for a test that asserts what a share may carry.
            var mask = RecordShareLevels.MaskForRightsCsv(accessRightsCsv);
            _fixture._shares.AddOrUpdate((recordId, principal), mask, (_, existing) => existing | mask);
            _fixture.OnGranted?.Invoke(recordId, principal);
            return Task.CompletedTask;
        }

        public Task RevokeAccessAsync(
            string entitySetName, Guid recordId, DataversePrincipalRef principal,
            CancellationToken ct = default)
        {
            // Checked BEFORE recording: `Revokes` means "shares that were actually removed".
            if (_fixture.FailRevokeForPrincipal == principal.Id)
                throw new InvalidOperationException($"Seeded revoke failure for {principal.Id}.");

            // Task 133 b2: accepted, not applied — the share stays, and only a read-back can tell.
            if (_fixture.RevokeNotAppliedFor == principal.Id)
                return Task.CompletedTask;

            _fixture.Revokes.Add(new RecordedShare(entitySetName, recordId, principal, null, _fixture.NextSequence()));
            _fixture.OnRevoke?.Invoke(recordId, principal);
            _fixture._shares.TryRemove((recordId, principal), out _);
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
                _fixture._shares
                    .Where(s => s.Key.RecordId == recordId)
                    .Select(s => new DataversePrincipalAccess(s.Key.Principal, s.Value, DateTimeOffset.UtcNow))
                    .ToList());
        }

        // Task 133: provisioning sets an EXISTING creator share to exact rights, and restores one on compensation.
        public Task ModifyAccessAsync(
            string entitySetName, Guid recordId, DataversePrincipalRef principal,
            string accessRightsCsv, CancellationToken ct = default)
        {
            ThrowIfWriteRefused(recordId, principal);

            _fixture.Modifies.Add(new RecordedShare(entitySetName, recordId, principal, accessRightsCsv, _fixture.NextSequence()));
            _fixture.OnModify?.Invoke(recordId, principal);
            _fixture._shares[(recordId, principal)] = RecordShareLevels.MaskForRightsCsv(accessRightsCsv);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DataversePrincipalAccess>> GetPrincipalAccessOrThrowAsync(
            string entityLogicalName, Guid recordId, CancellationToken ct = default)
        {
            if (!_fixture.StrictShareReadSucceeds
                || (_fixture.FailStrictShareReadWhileSecureOwned
                    && _fixture.OwningTeamOf(recordId) == SecureOwnerTeamId))
            {
                throw new InvalidOperationException(
                    $"The shares on {entityLogicalName}({recordId}) could not be read completely: " +
                    "seeded refusal.");
            }

            if (_fixture.FailNextStrictShareReads > 0)
            {
                _fixture.FailNextStrictShareReads--;
                throw new InvalidOperationException(
                    $"The shares on {entityLogicalName}({recordId}) could not be read completely: " +
                    "seeded one-off refusal.");
            }

            return GetPrincipalAccessAsync(entityLogicalName, recordId, ct);
        }

        // Task 149: the batched strict read the secure-child synchronizer uses. Provisioning's own steps never call it.
        public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>> GetPrincipalAccessForRecordsOrThrowAsync(
            string entityLogicalName, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
        {
            var answer = new Dictionary<Guid, IReadOnlyList<DataversePrincipalAccess>>();
            foreach (var id in recordIds.Distinct())
                answer[id] = await GetPrincipalAccessOrThrowAsync(entityLogicalName, id, ct);
            return answer;
        }

        /// <summary>The write refusals Grant and Modify share (a creator share is written by either).</summary>
        private void ThrowIfWriteRefused(Guid recordId, DataversePrincipalRef principal)
        {
            if (_fixture.FailShareForPrincipal == principal.Id)
                throw new InvalidOperationException($"Seeded share failure for {principal.Id}.");

            if (_fixture.FailShareWhileSecureOwned == principal.Id && _fixture.OwningTeamOf(recordId) == SecureOwnerTeamId)
                throw new InvalidOperationException($"Seeded share failure for {principal.Id} on a team-owned record.");
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
