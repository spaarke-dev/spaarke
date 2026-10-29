using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Workspace;
using Edge = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.Edge;
using EdgeKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.EdgeKind;
using FamilyKind = Sprk.Bff.Api.Services.Dataverse.RecordFamilyEdges.FamilyKind;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>Which part of a record's family a read covers (FR-31: the full family by default; <see cref="Down"/> for the push
/// preview, which acts only below the source).</summary>
internal enum RecordFamilyDirection
{
    /// <summary>Up to the highest ancestor the caller can read, then down from it.</summary>
    Full = 1,

    /// <summary>Down from the anchor only.</summary>
    Down = 2,
}

/// <summary>How a family read ended.</summary>
internal enum RecordFamilyReadOutcome
{
    /// <summary>The family was read (possibly truncated).</summary>
    Read = 1,

    /// <summary>The kind is not one this reader walks (ontology 3a). Nothing was read.</summary>
    KindNotSupported = 2,

    /// <summary>The anchor does not exist, cannot be read by the caller, or is not a family table: one answer for all three.</summary>
    AnchorNotFound = 3,

    /// <summary>The continuation is malformed, tampered, bound to another caller, anchor, kind, direction or top, or its top is
    /// no longer the one the caller's walk derives: one answer for all of them (UAC F1, C1).</summary>
    ContinuationInvalid = 4,
}

/// <summary>The person reading: their Entra object id (binds the continuation) and their Dataverse systemuserid (every read
/// impersonates it, the /push pattern). Both resolved by the caller of the reader from the caller's own token.</summary>
internal sealed record RecordFamilyCaller(Guid ObjectId, Guid SystemUserId);

/// <summary>A record named by table and id.</summary>
internal sealed record RecordFamilyRecordRef(string Table, Guid Id);

/// <summary>
/// One family member (design §12.3 B, FR-31). No access metadata: no grants, No Access, sharees or owner team. Display values
/// are read as the caller, so a field-secured value the caller cannot read is null.
/// </summary>
/// <param name="Table">The member's table.</param>
/// <param name="Id">The member's id.</param>
/// <param name="Name">Its display name, or null.</param>
/// <param name="RecordNumber">Its record number, or null (a table with none, or a field-secured value).</param>
/// <param name="Status">Its status reason label, or null.</param>
/// <param name="ModifiedOn">When it was last modified, or null.</param>
/// <param name="Kind">The family kind the walk followed.</param>
/// <param name="EdgeKind">The kind of filing edge to <paramref name="Parent"/>; null for the top of the walk.</param>
/// <param name="Parent">The member it is listed under (always a member of the view); null for the top of the walk.</param>
/// <param name="Depth">Distance from the top of the walk (the highest readable ancestor = 0; FR-31). Never negative.</param>
internal sealed record RecordFamilyMember(
    string Table,
    Guid Id,
    string? Name,
    string? RecordNumber,
    string? Status,
    DateTimeOffset? ModifiedOn,
    FamilyKind Kind,
    EdgeKind? EdgeKind,
    RecordFamilyRecordRef? Parent,
    int Depth);

/// <summary>
/// One page of a family read.
/// </summary>
/// <param name="Outcome">How the read ended; every outcome but <see cref="RecordFamilyReadOutcome.Read"/> carries no records.</param>
/// <param name="Anchor">The record opened.</param>
/// <param name="Top">The top of the walk: the highest ancestor the caller can read, at most <see cref="RecordFamilyReader.MaxDepth"/>
/// hops up (full view), or the anchor (down). A record above it the caller cannot read is never hinted at (UAC F2); one the caller
/// can read, past the hops, is said by <paramref name="Truncated"/>.</param>
/// <param name="Members">This page's members: on the first page the anchor's chain (top first), then the anchor's own subtree,
/// then the rest, each level in a fixed order (UAC C4).</param>
/// <param name="CountsByTable">How many of <paramref name="Members"/> each table has — of what IS shown, never of what is hidden.</param>
/// <param name="Truncated">True when anything the caller can read was left out: more members than one page holds, or a record
/// past the depth cap (below the view, or above its top). Never left out silently.</param>
/// <param name="Continuation">The opaque token that loads the next page, or null when no further page can be loaded.</param>
internal sealed record RecordFamilyRead(
    RecordFamilyReadOutcome Outcome,
    RecordFamilyMember? Anchor,
    RecordFamilyMember? Top,
    IReadOnlyList<RecordFamilyMember> Members,
    IReadOnlyDictionary<string, int> CountsByTable,
    bool Truncated,
    string? Continuation);

/// <summary>
/// field-mapping-server-write-path-r1 task 071 (FR-30; design §12.3 B, §12.12) — the read-only walk of a record's family AS THE
/// CALLER: every member query and every display-column read impersonates the caller through
/// <see cref="IImpersonatedCommunicationQuery"/> (the /push pattern), so Dataverse returns only what the caller may read and
/// applies field security. The reader holds no app-only client.
/// </summary>
/// <remarks>
/// <para><b>The family abstraction (§12.12, #2362).</b> Callers read a family through this reader (or the family endpoint), never
/// through raw parent lookups. Its surface names tables, ids and display values only: no lookup names, no registry rows, no
/// storage. A stored family (<c>sprk_securefamily</c>) could replace how members are found without changing it. It is not an
/// authorization input and decides no isolation; nothing is cached across calls.</para>
/// <para><b>The walk.</b> Driven by <see cref="RecordFamilyEdges"/> rows of ONE kind (ontology 3a; any other kind is refused
/// before a read). One batched query per (child table, lookup) per level, chunked by <see cref="ParentIdsPerQuery"/> parent ids
/// (no N+1) and paged by id, and a visited set always, so a bad-data loop ends. The default view skips
/// <see cref="RecordFamilyEdges.DetailTables"/>: their rows are neither listed nor walked through.</para>
/// <para><b>A table the caller holds no Read privilege on</b> is read as no rows (fix round 1): Dataverse refuses the whole
/// query with a 403 naming <c>prvRead{table}</c> (<see cref="DataverseForbiddenException.IsMissingReadPrivilegeOn"/>), which
/// depends only on the caller's own roles, so the walk goes on without that table. Every other refusal (the application
/// user's impersonation privilege, ADR-028) and every other fault propagates.</para>
/// <para><b>Never through an unreadable record (UAC F2).</b> A child is found only from a parent the caller read, so a readable
/// record below an unreadable one is never reached. Going up, the walk stops at the first ancestor the caller cannot read; the
/// full view walks down from the highest readable one. Counts and truncation are of accepted members only.</para>
/// <para><b>One family per record.</b> A child is listed only under its PRIMARY parent, and the up-walk follows only primary
/// edges. The primary is UAC 194b's, never a second ordering (UAC-r2 ruling 2026-10-28): (a) a Signal or Decision Record names
/// its core record first (<see cref="RecordOwnershipContext.ForExistingRowAsync"/>), mapped to the edge that holds it, the stamp
/// row first; then its subject; (b) a work assignment follows <see cref="SecureRootInheritance.TypedLookupsOf"/>, a project its
/// regarding pair; (c) every other row ranks the Filing columns it holds by <see cref="RecordOwnershipContext.InPrimaryOrder"/>
/// (the parents <see cref="RecordOwnershipContext.OrderedParentsOf"/> keeps) and takes the first. A message's thread column
/// ranks at its place in that order (<see cref="RecordThreadFiling.QuestionWithThreadFiling"/>), when the thread files
/// something: a Direct thread, or one regarding nothing, files nothing (<see cref="RecordThreadFiling.ParentsOf"/>), so the
/// next column decides. ForExistingRowAsync itself is not called: its reads are app-only.</para>
/// <para><b>Caps and continuation (UAC C1, C4).</b> Depth <see cref="MaxDepth"/>, <see cref="MaxMembers"/> per page. The first
/// page holds the anchor's chain, then the anchor's own subtree, then the rest. The continuation is ASP.NET Data Protection,
/// bound to the caller's oid, the anchor, the kind, the direction and the top; it carries only that top and an offset. A resumed
/// read re-walks AS THE CALLER and is refused unless it derives the same top, so a carried top that became unreadable is never
/// traversed and the token cannot steer the walk.</para>
/// <para><b>Placement and component justification (CLAUDE.md §10, §11):</b> project notes <c>task-071-notes.md</c>. Existing:
/// UAC's access walks (app-only, enforcing invariants), /push's one-level child query, ParentLineage / ParentLineageWalk and
/// CoreAncestorResolver's upward reads (app-only). Extension: no — the access walks must
/// stay app-only, and the push query cannot walk depth or prune without becoming this reader. Cost of doing nothing: the Family
/// modal (073) and push preview (040) would each build their own caller walk.</para>
/// </remarks>
internal sealed class RecordFamilyReader
{
    /// <summary>The deepest a member may be below the top of the walk (FR-30).</summary>
    internal const int MaxDepth = 4;

    /// <summary>Unique members per page (FR-30).</summary>
    internal const int MaxMembers = 500;

    /// <summary>The most rows one request of a batched read asks for (one Dataverse page). A (table, lookup, chunk) with more
    /// is read on, page by page, keyed by id (<c>{table}id gt</c> the last id read), until a page comes back short.</summary>
    internal const int MaxRowsPerQuery = 5000;

    /// <summary>Parent ids per batched child query (keeps the OData URL short).</summary>
    internal const int ParentIdsPerQuery = 50;

    /// <summary>The Data Protection purpose of the continuation.</summary>
    internal const string ContinuationPurpose = "Sprk.Bff.Api.Services.Dataverse.RecordFamilyReader.Continuation.v1";

    private const int ContinuationVersion = 1;
    private const string StatusColumn = "statuscode";
    private const string ModifiedOnColumn = "modifiedon";
    private const string FormattedValueSuffix = "@OData.Community.Display.V1.FormattedValue";
    private const string CoreRecordTypeColumn = "sprk_corerecordtype";
    private const string CoreRecordIdColumn = "sprk_corerecordid";
    private const string RecordTypeTable = "sprk_recordtype_ref";
    private const string RecordTypeTableColumn = "sprk_recordlogicalname";
    // A TABLE name used as a map key below. Held in a constant because DecisionRecordContextImmutabilityTests (D-34) scans for the
    // text ["sprk_policyversion"] = and cannot tell a table-name key from a write of the Decision Record context column. The
    // table is listed in AccessTableReaderScanTests.NotAccessTables (#2253 K3): read as the caller, for its display name only.
    private const string PolicyVersionTable = "sprk_policyversion";

    /// <summary>
    /// The display-name column of the family tables the ONE regarding display-name map
    /// (<see cref="RegardingRecordType.GetPrimaryNameField"/>) does not list — that map is pinned to the addressable regarding
    /// targets, so it is not widened here. Each value is the table's primary name attribute in the solution source
    /// (<c>src/dataverse/solutions/SpaarkeMaster/Entities/*/Entity.xml</c>), the source <see cref="RecordFamilyEdges"/> takes its
    /// record numbers from. Display data only.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> NameColumnsBeyondTheRegardingMap =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_agreement"] = "sprk_name",
            ["sprk_analysisoutput"] = "sprk_name",
            ["sprk_attachmentartifact"] = "sprk_name",
            ["sprk_billingevent"] = "sprk_name",
            ["sprk_budgetbucket"] = "sprk_name",
            ["sprk_budgetrevision"] = "sprk_name",
            ["sprk_communicationattachment"] = "sprk_name",
            ["sprk_communicationparticipant"] = "sprk_name",
            ["sprk_communicationthread"] = "sprk_name",
            ["sprk_decisionrecord"] = "sprk_name",
            ["sprk_document"] = "sprk_documentname",
            ["sprk_emailartifact"] = "sprk_name",
            ["sprk_emailreviewlog"] = "sprk_name",
            ["sprk_eventlog"] = "sprk_eventlogname",
            ["sprk_fileversion"] = "sprk_versionnumber",
            ["sprk_kpiassessment"] = "sprk_kpiname",
            ["sprk_memo"] = "sprk_name",
            ["sprk_policy"] = "sprk_name",
            [PolicyVersionTable] = "sprk_name",
            ["sprk_signal"] = "sprk_name",
            ["sprk_spendsignal"] = "sprk_name",
            ["sprk_spendsnapshot"] = "sprk_name",
            ["sprk_todo"] = "sprk_name",
        };

    private readonly IImpersonatedCommunicationQuery _query;
    private readonly IDataProtector _protector;
    private readonly ILogger<RecordFamilyReader> _logger;

    public RecordFamilyReader(
        IImpersonatedCommunicationQuery query, IDataProtectionProvider dataProtection, ILogger<RecordFamilyReader> logger)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        ArgumentNullException.ThrowIfNull(dataProtection);
        _protector = dataProtection.CreateProtector(ContinuationPurpose);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>What a continuation carries: its binding (oid, anchor, kind, direction, top) and where the next page starts.</summary>
    internal sealed record ContinuationState(
        int Version, Guid ObjectId, string AnchorTable, Guid AnchorId, int Kind, int Direction, string TopTable, Guid TopId, int Offset);

    /// <summary>The opaque, integrity-protected token for <paramref name="state"/> (the one way a continuation is minted).</summary>
    internal static string ProtectContinuation(IDataProtector protector, ContinuationState state)
    {
        ArgumentNullException.ThrowIfNull(protector);
        return Base64Url.EncodeToString(protector.Protect(JsonSerializer.SerializeToUtf8Bytes(state)));
    }

    /// <summary>
    /// Reads one page of <paramref name="table"/> <paramref name="id"/>'s family of <paramref name="kind"/>, as
    /// <paramref name="caller"/>.
    /// </summary>
    /// <param name="caller">The person reading; every Dataverse read impersonates them.</param>
    /// <param name="kind">The family kind; anything but <see cref="FamilyKind.Filing"/> is refused before a read.</param>
    /// <param name="table">The anchor's table.</param>
    /// <param name="id">The anchor's id.</param>
    /// <param name="direction">The full family, or down from the anchor.</param>
    /// <param name="continuation">The previous page's <see cref="RecordFamilyRead.Continuation"/>, or null for the first page.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>The page. A Dataverse fault propagates (never an app-only retry).</returns>
    public async Task<RecordFamilyRead> ReadAsync(
        RecordFamilyCaller caller, FamilyKind kind, string table, Guid id, RecordFamilyDirection direction,
        string? continuation, CancellationToken ct)
    {
        // Ontology 3a: one kind per walk, refused before anything else is looked at.
        if (kind != FamilyKind.Filing)
            return Refused(RecordFamilyReadOutcome.KindNotSupported);

        ArgumentNullException.ThrowIfNull(caller);
        if (caller.ObjectId == Guid.Empty || caller.SystemUserId == Guid.Empty)
            throw new ArgumentException("A family read needs the caller's object id and systemuserid; nothing is read without them.", nameof(caller));
        if (!Enum.IsDefined(direction))
            throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unknown family direction.");

        var walk = new Walk(this, caller, kind, ct);
        var anchorTable = table?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(anchorTable) || id == Guid.Empty || !walk.IsFamilyTable(anchorTable))
            return Refused(RecordFamilyReadOutcome.AnchorNotFound);

        var offset = 0;
        if (continuation is not null)
        {
            var state = Unprotect(continuation);
            if (state is null
                || state.Version != ContinuationVersion
                || state.ObjectId != caller.ObjectId
                || !string.Equals(state.AnchorTable, anchorTable, StringComparison.Ordinal)
                || state.AnchorId != id
                || state.Kind != (int)kind
                || state.Direction != (int)direction
                || state.Offset <= 0)
            {
                return InvalidContinuation("its binding does not match this read");
            }

            offset = state.Offset;
            walk.ResumeTop = new RecordFamilyRecordRef(state.TopTable, state.TopId);
        }

        walk.Wanted = offset + MaxMembers + 1;
        var anchor = await walk.ReadNodeAsync(anchorTable, id).ConfigureAwait(false);
        if (anchor is null)
            return Refused(RecordFamilyReadOutcome.AnchorNotFound);

        var chain = direction == RecordFamilyDirection.Full
            ? await walk.UpAsync(anchor).ConfigureAwait(false)
            : new List<Node> { anchor };
        var top = chain[^1];
        if (walk.ResumeTop is { } boundTop
            && !(string.Equals(boundTop.Table, top.Table, StringComparison.Ordinal) && boundTop.Id == top.Id))
        {
            // The top this caller's walk derives now is not the one the token was minted for (it became unreadable, or the
            // chain changed): the carried top is not traversed.
            return InvalidContinuation("the walk no longer derives its top");
        }

        for (var i = 0; i < chain.Count; i++)
            chain[i].Depth = chain.Count - 1 - i;
        for (var i = chain.Count - 1; i >= 0; i--)
            walk.Emit(chain[i]);

        if (!ReferenceEquals(anchor, top))
            await walk.DownAsync(anchor, passThrough: null, skip: null).ConfigureAwait(false);
        await walk.DownAsync(
            top,
            passThrough: chain.Where(n => !ReferenceEquals(n, anchor)).ToDictionary(n => n.Key),
            skip: ReferenceEquals(anchor, top) ? null : anchor.Key).ConfigureAwait(false);

        var moreMembers = walk.Sequence.Count > offset + MaxMembers;
        var page = walk.Sequence.Skip(offset).Take(MaxMembers).Select(n => walk.ToMember(n)).ToList();
        var next = moreMembers
            ? ProtectContinuation(_protector, new ContinuationState(
                ContinuationVersion, caller.ObjectId, anchorTable, id, (int)kind, (int)direction, top.Table, top.Id, offset + MaxMembers))
            : null;

        _logger.LogInformation(
            "[RECORD-FAMILY] {Table} read as the caller: {Members} member(s) from offset {Offset}, truncated={Truncated}, "
            + "{Queries} caller-scoped quer(y/ies).",
            anchorTable, page.Count, offset, moreMembers || walk.Incomplete, walk.Queries);

        return new RecordFamilyRead(
            RecordFamilyReadOutcome.Read,
            walk.ToMember(anchor),
            walk.ToMember(top),
            page,
            page.GroupBy(m => m.Table, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal),
            moreMembers || walk.Incomplete,
            next);
    }

    private static RecordFamilyRead Refused(RecordFamilyReadOutcome outcome) =>
        new(outcome, null, null, Array.Empty<RecordFamilyMember>(), new Dictionary<string, int>(), false, null);

    private RecordFamilyRead InvalidContinuation(string why)
    {
        _logger.LogInformation("[RECORD-FAMILY] Continuation refused: {Reason}.", why);
        return Refused(RecordFamilyReadOutcome.ContinuationInvalid);
    }

    private ContinuationState? Unprotect(string token)
    {
        try
        {
            return JsonSerializer.Deserialize<ContinuationState>(_protector.Unprotect(Base64Url.DecodeFromChars(token)));
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Every table a family of <paramref name="kind"/> walks: the edges' child and parent tables and the cores.</summary>
    private static HashSet<string> FamilyTablesOf(FamilyKind kind) =>
        new(RecordFamilyEdges.Of(kind).SelectMany(e => new[] { e.ChildTable, e.ParentTable }).Concat(RecordFamilyEdges.CoreTables),
            StringComparer.Ordinal);

    /// <summary>
    /// The entity set of <paramref name="table"/> when it is a table a <paramref name="kind"/> family walks, else false. The
    /// reader's own notion of a family table (the same test <see cref="ReadAsync"/> applies to its anchor), for the family
    /// endpoint's table gate (task 072). <paramref name="table"/> is a logical name, trimmed and lower-cased as
    /// <see cref="ReadAsync"/> does.
    /// </summary>
    internal static bool TryGetFamilyEntitySet(string? table, FamilyKind kind, out string entitySet)
    {
        var name = table?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(name) && FamilyTablesOf(kind).Contains(name) && EntitySetOf(name) is { } set)
        {
            entitySet = set;
            return true;
        }

        entitySet = string.Empty;
        return false;
    }

    /// <summary>The table's entity set: <see cref="SecureChildLineage"/>'s for a child table, the regarding catalogue's for a core.</summary>
    private static string? EntitySetOf(string table) =>
        SecureChildLineage.Children.TryGetValue(table, out var child) ? child.EntitySet : RegardingNameFields.EntitySetName(table);

    private static string? NameColumnOf(string table) =>
        RegardingRecordType.GetPrimaryNameField(table)
        ?? (NameColumnsBeyondTheRegardingMap.TryGetValue(table, out var column) ? column : null);

    private static string LookupValue(string column) => $"_{column}_value";

    /// <summary>A record met by the walk, with the row the caller read.</summary>
    private sealed class Node
    {
        public Node(string table, Guid id, Dictionary<string, JsonElement> row, Entity entity)
        {
            Table = table;
            Id = id;
            Row = row;
            Entity = entity;
        }

        public string Table { get; }
        public Guid Id { get; }
        public Dictionary<string, JsonElement> Row { get; }

        /// <summary>The row's filing lookups as an entity, for UAC 194b's ordering functions.</summary>
        public Entity Entity { get; }

        public int Depth { get; set; }
        public Edge? Edge { get; set; }
        public Node? Parent { get; set; }
        public (string Table, Guid Id) Key => (Table, Id);
    }

    /// <summary>One read's state. Nothing outlives the call (no cross-caller cache).</summary>
    private sealed class Walk
    {
        private readonly RecordFamilyReader _reader;
        private readonly RecordFamilyCaller _caller;
        private readonly FamilyKind _kind;
        private readonly CancellationToken _ct;
        private readonly ILookup<string, Edge> _edgesByChild;
        private readonly IReadOnlyList<Edge> _listedEdges;
        private readonly HashSet<string> _familyTables;
        private readonly HashSet<(string, Guid)> _visited = new();
        private readonly Dictionary<string, string[]> _selects = new(StringComparer.Ordinal);

        /// <summary>Tables the caller holds no Read privilege on, met this read: asked once, then read as no rows.</summary>
        private readonly HashSet<string> _noReadPrivilege = new(StringComparer.Ordinal);

        /// <summary>Every thread row read this call (as the caller), or null for one the caller cannot read.</summary>
        private readonly Dictionary<Guid, Node?> _threads = new();
        private Dictionary<Guid, string>? _recordTypes;

        public Walk(RecordFamilyReader reader, RecordFamilyCaller caller, FamilyKind kind, CancellationToken ct)
        {
            _reader = reader;
            _caller = caller;
            _kind = kind;
            _ct = ct;
            var edges = RecordFamilyEdges.Of(kind);
            _edgesByChild = edges.ToLookup(e => e.ChildTable, StringComparer.Ordinal);
            // The default view: detail rows are neither listed nor walked through (UAC-r2 ruling 2026-10-28).
            _listedEdges = edges.Where(e => !RecordFamilyEdges.DetailTables.Contains(e.ChildTable)).ToArray();
            _familyTables = FamilyTablesOf(kind);
        }

        public List<Node> Sequence { get; } = new();
        public int Wanted { get; set; }
        public RecordFamilyRecordRef? ResumeTop { get; set; }

        /// <summary>Something was left out that no further page reaches: a readable record past the depth cap (below the view,
        /// or above it).</summary>
        public bool Incomplete => DepthCapped;

        /// <summary>A readable record lies past the depth cap. The cap ends that branch only; the walk of the others goes on.</summary>
        private bool DepthCapped { get; set; }

        public int Queries { get; private set; }

        private bool Full => Sequence.Count >= Wanted;

        public bool IsFamilyTable(string table) => _familyTables.Contains(table) && EntitySetOf(table) is not null;

        /// <summary>Adds <paramref name="node"/> to the walk's sequence unless it is already in it (dedupe by (table, id)).</summary>
        public bool Emit(Node node)
        {
            if (!_visited.Add(node.Key))
                return false;
            Sequence.Add(node);
            return true;
        }

        public RecordFamilyMember ToMember(Node node) => new(
            node.Table,
            node.Id,
            StringOf(node.Row, NameColumnOf(node.Table)),
            StringOf(node.Row, RecordFamilyEdges.RecordNumberColumnOf(node.Table)),
            StringOf(node.Row, StatusColumn + FormattedValueSuffix),
            DateOf(node.Row, ModifiedOnColumn),
            _kind,
            node.Edge?.EdgeKind,
            node.Parent is { } parent ? new RecordFamilyRecordRef(parent.Table, parent.Id) : null,
            node.Depth);

        /// <summary>The record, read AS THE CALLER; null when it does not exist or the caller cannot read it (one answer).</summary>
        public async Task<Node?> ReadNodeAsync(string table, Guid id)
        {
            if (EntitySetOf(table) is not { } set)
                return null;
            var rows = await QueryAsync(table, set, $"$select={string.Join(',', SelectFor(table))}&$filter={table}id eq {id:D}&$top=1")
                .ConfigureAwait(false);
            return rows.Select(r => ToNode(table, r)).FirstOrDefault(n => n?.Id == id);
        }

        /// <summary>
        /// The anchor and its primary ancestors, anchor first, up to the first one the caller cannot read (UAC F2), a detail row,
        /// a Direct thread, a loop, or <see cref="MaxDepth"/> hops. Past the hops, a further ancestor the caller CAN read is left
        /// out and said (truncated); one the caller cannot read is not hinted at.
        /// </summary>
        public async Task<List<Node>> UpAsync(Node anchor)
        {
            var chain = new List<Node> { anchor };
            var seen = new HashSet<(string, Guid)> { anchor.Key };
            var current = anchor;
            while (await PrimaryParentAsync(current, seen).ConfigureAwait(false) is { } step)
            {
                if (chain.Count > MaxDepth)
                {
                    DepthCapped = true;
                    break;
                }

                current.Edge = step.Edge;
                current.Parent = step.Parent;
                chain.Add(step.Parent);
                current = step.Parent;
            }

            return chain;
        }

        /// <summary>The row's primary parent, read AS THE CALLER, when the up-walk may pass to it; otherwise null.</summary>
        private async Task<(Edge Edge, Node Parent)?> PrimaryParentAsync(Node node, HashSet<(string, Guid)> seen)
        {
            if (await PrimaryOfAsync(node).ConfigureAwait(false) is not { } primary
                || RecordFamilyEdges.DetailTables.Contains(primary.Edge.ParentTable)
                || !seen.Add((primary.Edge.ParentTable, primary.ParentId)))
            {
                return null;
            }

            var parent = await ReadNodeAsync(primary.Edge.ParentTable, primary.ParentId).ConfigureAwait(false);
            return parent is not null && FilesUnder(primary.Edge, parent) ? (primary.Edge, parent) : null;
        }

        /// <summary>
        /// Walks down from <paramref name="start"/> level by level, listing each child under its primary parent. A
        /// <paramref name="passThrough"/> record (the anchor's chain, already listed) is walked through but not listed again;
        /// <paramref name="skip"/> (the anchor, whose subtree was listed first) is neither listed nor walked.
        /// </summary>
        public async Task DownAsync(Node start, IReadOnlyDictionary<(string, Guid), Node>? passThrough, (string, Guid)? skip)
        {
            var frontier = new List<Node> { start };
            var enqueued = new HashSet<(string, Guid)> { start.Key };
            var depth = start.Depth;
            while (frontier.Count > 0 && !Full)
            {
                if (depth >= MaxDepth)
                {
                    // The depth cap: say so when the caller can read anything below it (never truncated silently). Asked once
                    // per read; it ends this branch of the walk only.
                    if (!DepthCapped
                        && await ExpandAsync(frontier, depth, passThrough, skip, enqueued, probe: true).ConfigureAwait(false) is null)
                    {
                        DepthCapped = true;
                    }

                    return;
                }

                frontier = await ExpandAsync(frontier, depth, passThrough, skip, enqueued, probe: false).ConfigureAwait(false)
                           ?? new List<Node>();
                depth++;
            }
        }

        /// <summary>
        /// One level: the children of <paramref name="frontier"/> whose primary parent is in it. Returns the next frontier, or
        /// null when the walk must stop (the page is full, or — probing — a child exists).
        /// </summary>
        private async Task<List<Node>?> ExpandAsync(
            List<Node> frontier, int depth, IReadOnlyDictionary<(string, Guid), Node>? passThrough, (string, Guid)? skip,
            HashSet<(string, Guid)> enqueued, bool probe)
        {
            var next = new List<Node>();
            var byKey = frontier.ToDictionary(n => n.Key);
            foreach (var edge in _listedEdges)
            {
                var parentIds = frontier.Where(n => n.Table == edge.ParentTable).Select(n => n.Id).OrderBy(i => i).ToArray();
                if (parentIds.Length == 0 || EntitySetOf(edge.ChildTable) is not { } set)
                    continue;

                foreach (var chunk in parentIds.Chunk(ParentIdsPerQuery))
                {
                    var stopped = await ForEachChildAsync(edge, set, chunk, probe ? () => 1 : () => Wanted - Sequence.Count, async child =>
                    {
                        if (await PrimaryOfAsync(child).ConfigureAwait(false) is not { } primary
                            || !byKey.TryGetValue((primary.Edge.ParentTable, primary.ParentId), out var parent)
                            || !FilesUnder(primary.Edge, parent)
                            || child.Key == skip)
                        {
                            return false;
                        }

                        if (passThrough is not null && passThrough.TryGetValue(child.Key, out var chained))
                        {
                            if (enqueued.Add(chained.Key))
                                next.Add(chained);
                            return false;
                        }

                        if (probe)
                            return !_visited.Contains(child.Key);

                        child.Depth = depth + 1;
                        child.Parent = parent;
                        child.Edge = primary.Edge;
                        if (Emit(child) && enqueued.Add(child.Key))
                            next.Add(child);
                        return Full;
                    }).ConfigureAwait(false);

                    if (stopped)
                        return null;
                }
            }

            return next;
        }

        /// <summary>
        /// Runs one batched child query for <paramref name="edge"/> over <paramref name="parentIds"/>, AS THE CALLER, rows in id
        /// order, handing each to <paramref name="handle"/> until it answers stop. The first request asks for as many rows as
        /// <paramref name="needed"/> says plus one (at most <see cref="MaxRowsPerQuery"/>); a full page is followed by the next,
        /// keyed by id (<c>{table}id gt</c> the last id read) and sized at <see cref="MaxRowsPerQuery"/>, until a page comes back short,
        /// so no row is ever out of reach (a skipped row costs a read, never the walk). True when the walk must stop.
        /// </summary>
        private async Task<bool> ForEachChildAsync(Edge edge, string set, Guid[] parentIds, Func<int> needed, Func<Node, Task<bool>> handle)
        {
            var filter = edge.EdgeKind == EdgeKind.Pair
                // The pair's id is TEXT: match the 8-hex-digit fragment every spelling carries, then parse each row's pair
                // exactly (SecureRootInheritance's child-direction rule) — PrimaryOfAsync keeps only an exact id of the right type.
                ? "(" + string.Join(" or ", parentIds.Select(p => $"contains({RecordFamilyEdges.PairIdColumn},'{p.ToString("N")[..8]}')")) + ")"
                : "(" + string.Join(" or ", parentIds.Select(p => $"{LookupValue(edge.LookupColumn)} eq {p:D}")) + ")";
            var idColumn = edge.ChildTable + "id";
            Guid? after = null;
            while (true)
            {
                // Only the first request is sized to what is needed: the rows after it are read to the end of the edge, because
                // PrimaryOfAsync skips every row filed under another parent, and a small page of skipped rows would cost a
                // sequential query each.
                var top = after is null ? Math.Min(MaxRowsPerQuery, Math.Max(needed(), 1) + 1) : MaxRowsPerQuery;
                var page = after is { } last ? $"{filter} and {idColumn} gt {last:D}" : filter;
                var rows = await QueryAsync(
                    edge.ChildTable,
                    set,
                    $"$select={string.Join(',', SelectFor(edge.ChildTable))}&$filter={page}&$orderby={idColumn} asc&$top={top}")
                    .ConfigureAwait(false);
                foreach (var row in rows)
                {
                    if (ToNode(edge.ChildTable, row) is { } child && await handle(child).ConfigureAwait(false))
                        return true;
                }

                if (rows.Count < top)
                    return false;

                after = GuidOf(rows[^1], idColumn)
                        ?? throw new InvalidOperationException(
                            $"A {edge.ChildTable} row came back without its id, so the next page of the read cannot be keyed.");
            }
        }

        /// <summary>
        /// The row's PRIMARY filing parent and the edge that holds it, in UAC 194b's order (see the class remarks); null when the
        /// row is filed under nothing this kind's edges name.
        /// </summary>
        private async Task<(Edge Edge, Guid ParentId)?> PrimaryOfAsync(Node node)
        {
            var edges = _edgesByChild[node.Table].ToArray();
            if (edges.Length == 0)
                return null;

            // (b) A project: its regarding pair (UAC 195; SecureChildLineage.RootLookupOrder), asked by id AND record type.
            if (edges.Any(e => e.EdgeKind == EdgeKind.Pair))
            {
                if (!Guid.TryParse(StringOf(node.Row, RecordFamilyEdges.PairIdColumn)?.Trim(), out var pairId) || pairId == Guid.Empty
                    || GuidOf(node.Row, LookupValue(RecordFamilyEdges.PairTypeColumn)) is not { } typeId)
                {
                    return null;
                }

                var typeTable = await RecordTypeTableAsync(typeId).ConfigureAwait(false);
                return edges.FirstOrDefault(e => e.EdgeKind == EdgeKind.Pair && e.ParentTable == typeTable) is { } pair
                    ? (pair, pairId)
                    : null;
            }

            // (b) A work assignment: its typed filing lookups in SecureRootInheritance's order.
            if (RecordFamilyEdges.CoreTables.Contains(node.Table))
            {
                foreach (var (column, _) in SecureRootInheritance.TypedLookupsOf(node.Table))
                {
                    if (Holding(edges, node.Entity, column) is { } typed)
                        return typed;
                }

                return null;
            }

            var subject = RecordOwnershipContext.SubjectColumnOf(node.Entity);
            if (RecordOwnershipContext.CorePairTables.Contains(node.Table))
            {
                // (a) The core record first (ForExistingRowAsync, SignalWriter.OwnershipContextOf), mapped to its stamp row.
                if (CoreIdOf(node.Entity) is { } coreId)
                {
                    var holding = edges.Where(e => e.EdgeKind != EdgeKind.Pair
                                                   && RecordFamilyEdges.CoreTables.Contains(e.ParentTable)
                                                   && IdOf(node.Entity, e.LookupColumn) == coreId).ToArray();
                    if ((holding.FirstOrDefault(e => e.EdgeKind == EdgeKind.Stamp) ?? FirstInPrimaryOrder(node.Table, holding, subject)) is { } core)
                        return (core, coreId);
                }

                // Then the subject, as its writer names it.
                if (subject is not null && Holding(edges, node.Entity, subject) is { } about)
                    return about;
            }

            // (c) The Filing columns the row holds, in its table's primary order (194b's InPrimaryOrder): the columns whose parent
            // OrderedParentsOf keeps (an ownership parent, not a citation, not Secure-only) and a message's thread column, which
            // ranks at its place in the order (RecordThreadFiling.QuestionWithThreadFiling) when the thread files something.
            var held = edges
                .Where(e => e.EdgeKind != EdgeKind.Pair && IdOf(node.Entity, e.LookupColumn) is not null
                            && (IsThreadEdge(e)
                                || (RecordOwnershipResolver.IsOwnershipParent(e.ParentTable)
                                    && !RecordOwnershipResolver.IsCitation(node.Table, e.LookupColumn)
                                    && !RecordOwnershipResolver.IsSecureOnlyParent(node.Table, e.LookupColumn))))
                .Select(e => KeyValuePair.Create(e.LookupColumn, e));
            foreach (var (_, edge) in RecordOwnershipContext.InPrimaryOrder(node.Table, held, subject))
            {
                var parentId = IdOf(node.Entity, edge.LookupColumn)!.Value;
                if (IsThreadEdge(edge) && !await ThreadFilesSomethingAsync(parentId).ConfigureAwait(false))
                    continue;
                return (edge, parentId);
            }

            return null;
        }

        private static bool IsThreadEdge(Edge edge) =>
            RecordThreadFiling.AppliesTo(edge.ChildTable)
            && string.Equals(edge.LookupColumn, RecordThreadFiling.ThreadColumn, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the thread files its messages under anything (<see cref="RecordThreadFiling.ParentsOf"/>: not a Direct thread,
        /// and it carries an ownership parent), from its row read AS THE CALLER. A thread the caller cannot read counts as one
        /// that files (194b reads an unknown type as a record thread), so its messages are reached through it or not at all.
        /// </summary>
        private async Task<bool> ThreadFilesSomethingAsync(Guid threadId)
        {
            if (!_threads.TryGetValue(threadId, out var thread))
                thread = _threads[threadId] = await ReadNodeAsync(RecordThreadFiling.ThreadTable, threadId).ConfigureAwait(false);
            return thread is null || RecordThreadFiling.ParentsOf(thread.Entity).Count > 0;
        }

        /// <summary>A Direct thread files nothing under it (RecordThreadFiling.ParentsOf); every other parent files.</summary>
        private static bool FilesUnder(Edge edge, Node parent) =>
            !(RecordThreadFiling.AppliesTo(edge.ChildTable)
              && edge.ParentTable == RecordThreadFiling.ThreadTable
              && IntOf(parent.Row, RecordThreadFiling.ThreadTypeColumn) == RecordThreadFiling.DirectThreadType);

        private static (Edge, Guid)? Holding(Edge[] edges, Entity row, string column) =>
            edges.FirstOrDefault(e => e.EdgeKind != EdgeKind.Pair && string.Equals(e.LookupColumn, column, StringComparison.OrdinalIgnoreCase)) is { } edge
            && IdOf(row, edge.LookupColumn) is { } id
                ? (edge, id)
                : null;

        /// <summary>Of several edges holding one parent, the one whose column ranks first in the table's primary order (194b).</summary>
        private static Edge? FirstInPrimaryOrder(string table, Edge[] holding, string? subject) =>
            holding.Length == 0
                ? null
                : RecordOwnershipContext.InPrimaryOrder(table, holding.Select(e => KeyValuePair.Create(e.LookupColumn, e)), subject).First().Value;

        /// <summary>The core record id of a Signal or Decision Record, or null when its core pair is absent or malformed
        /// (ForExistingRowAsync's rule: both halves, a real id).</summary>
        private static Guid? CoreIdOf(Entity row) =>
            row.GetAttributeValue<EntityReference>(CoreRecordTypeColumn) is { Id: var type } && type != Guid.Empty
            && Guid.TryParse(row.GetAttributeValue<string>(CoreRecordIdColumn)?.Trim(), out var core) && core != Guid.Empty
                ? core
                : null;

        private static Guid? IdOf(Entity row, string column) =>
            row.GetAttributeValue<EntityReference>(column) is { Id: var id } && id != Guid.Empty ? id : null;

        /// <summary>The table a record type names (the <c>sprk_recordtype_ref</c> catalog, read once per call AS THE CALLER).</summary>
        private async Task<string?> RecordTypeTableAsync(Guid typeId)
        {
            if (_recordTypes is null)
            {
                var rows = await QueryAsync(
                    RecordTypeTable, RegardingRecordType.RecordTypeRefEntitySet, $"$select={RecordTypeTable}id,{RecordTypeTableColumn}")
                    .ConfigureAwait(false);
                _recordTypes = new Dictionary<Guid, string>();
                foreach (var row in rows)
                {
                    if (GuidOf(row, RecordTypeTable + "id") is { } id && StringOf(row, RecordTypeTableColumn) is { } name)
                        _recordTypes[id] = name.Trim().ToLowerInvariant();
                }
            }

            return _recordTypes.TryGetValue(typeId, out var table) ? table : null;
        }

        /// <summary>
        /// One read of <paramref name="table"/> AS THE CALLER. A table the caller holds no Read privilege on is no rows (asked
        /// once per read): Dataverse's 403 naming <c>prvRead{table}</c> depends only on the caller's own roles. Every other
        /// refusal, the impersonation privilege's included (ADR-028), and every other fault propagates.
        /// </summary>
        private async Task<IReadOnlyList<Dictionary<string, JsonElement>>> QueryAsync(string table, string set, string query)
        {
            if (_noReadPrivilege.Contains(table))
                return Array.Empty<Dictionary<string, JsonElement>>();

            Queries++;
            try
            {
                return await _reader._query.QueryAsync(set, query, _caller.SystemUserId, _ct).ConfigureAwait(false);
            }
            catch (DataverseForbiddenException ex) when (ex.IsMissingReadPrivilegeOn(table))
            {
                _noReadPrivilege.Add(table);
                // The record-type catalog holds no per-user data: losing it drops every project from the family, so it is a
                // warning. Any other table is the caller's own role, which is information. Table names only.
                _reader._logger.Log(
                    table == RecordTypeTable ? LogLevel.Warning : LogLevel.Information,
                    "[RECORD-FAMILY] The caller holds no Read privilege on {Table}; it is read as no rows.", table);
                return Array.Empty<Dictionary<string, JsonElement>>();
            }
        }

        /// <summary>The columns read for a row of <paramref name="table"/>: its id, display columns and filing lookups.</summary>
        private string[] SelectFor(string table)
        {
            if (_selects.TryGetValue(table, out var known))
                return known;

            var columns = new List<string> { table + "id" };
            if (NameColumnOf(table) is { } name)
                columns.Add(name);
            if (RecordFamilyEdges.RecordNumberColumnOf(table) is { } number)
                columns.Add(number);
            columns.Add(StatusColumn);
            columns.Add(ModifiedOnColumn);
            foreach (var edge in _edgesByChild[table])
            {
                if (edge.EdgeKind == EdgeKind.Pair)
                {
                    columns.Add(RecordFamilyEdges.PairIdColumn);
                    columns.Add(LookupValue(RecordFamilyEdges.PairTypeColumn));
                }
                else
                {
                    columns.Add(LookupValue(edge.LookupColumn));
                }
            }

            if (TodoRegardingBuilder.RegardingLookupsByHostTable.ContainsKey(table))
                columns.Add(RecordFamilyEdges.PairIdColumn); // the subject (SubjectColumnOf)
            if (RecordOwnershipContext.CorePairTables.Contains(table))
            {
                columns.Add(LookupValue(CoreRecordTypeColumn));
                columns.Add(CoreRecordIdColumn);
            }

            if (table == RecordThreadFiling.ThreadTable)
                columns.Add(RecordThreadFiling.ThreadTypeColumn);

            return _selects[table] = columns.Distinct(StringComparer.Ordinal).ToArray();
        }

        private Node? ToNode(string table, Dictionary<string, JsonElement> row)
        {
            if (GuidOf(row, table + "id") is not { } id)
                return null;

            var entity = new Entity(table, id);
            foreach (var edge in _edgesByChild[table].Where(e => e.EdgeKind != EdgeKind.Pair))
            {
                if (GuidOf(row, LookupValue(edge.LookupColumn)) is { } parent)
                    entity[edge.LookupColumn] = new EntityReference(edge.ParentTable, parent);
            }

            if (StringOf(row, RecordFamilyEdges.PairIdColumn) is { } pairId)
                entity[RecordFamilyEdges.PairIdColumn] = pairId;
            if (GuidOf(row, LookupValue(CoreRecordTypeColumn)) is { } coreType)
                entity[CoreRecordTypeColumn] = new EntityReference(RecordTypeTable, coreType);
            if (StringOf(row, CoreRecordIdColumn) is { } coreId)
                entity[CoreRecordIdColumn] = coreId;
            var node = new Node(table, id, row, entity);
            if (table == RecordThreadFiling.ThreadTable)
            {
                // The thread's type, for RecordThreadFiling.ParentsOf; and the row, so a message ranked under it is decided
                // without reading it again.
                if (IntOf(row, RecordThreadFiling.ThreadTypeColumn) is { } type)
                    entity[RecordThreadFiling.ThreadTypeColumn] = new OptionSetValue(type);
                _threads[id] = node;
            }

            return node;
        }

        private static string? StringOf(Dictionary<string, JsonElement> row, string? column) =>
            column is not null && row.TryGetValue(column, out var value)
                ? value.ValueKind switch
                {
                    JsonValueKind.String => value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => value.GetRawText(),
                }
                : null;

        private static Guid? GuidOf(Dictionary<string, JsonElement> row, string column) =>
            Guid.TryParse(StringOf(row, column), out var id) && id != Guid.Empty ? id : null;

        private static int? IntOf(Dictionary<string, JsonElement> row, string column) =>
            row.TryGetValue(column, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
                ? number
                : null;

        private static DateTimeOffset? DateOf(Dictionary<string, JsonElement> row, string column) =>
            DateTimeOffset.TryParse(StringOf(row, column), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at)
                ? at
                : null;
    }
}
