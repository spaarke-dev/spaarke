using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// The Office add-in's Dataverse READS: the "File to" entity picker, the create form's reference lists (task 100), and the
/// <c>sprk_recordtype_ref</c> lookup the To Do writer stamps onto its regarding fields.
/// </summary>
/// <remarks>
/// <para>
/// Extracted from <see cref="OfficeService"/> by spaarkeai-word-add-in-r1 task 059. The members were moved
/// verbatim; the only edits are the null-fallback branches that could no longer run once their dependencies
/// became required (every one is registered unconditionally). <see cref="OfficeService"/> keeps the
/// <see cref="IOfficeService"/> search members and delegates here, so the interface is unchanged.
/// </para>
/// <para>
/// The split follows a reason to change, not a line count (CLAUDE.md §11.5). Search changes with the picker
/// (ranking, impersonation, the per-row filing affordance task 084 adds); the save, job and To Do writers in
/// <see cref="OfficeService"/> change with the write-path invariants. After the move,
/// <see cref="OfficeService"/> issues no Dataverse read of its own.
/// </para>
/// <para>
/// Concrete and scoped (ADR-010), registered unconditionally in <c>OfficeModule</c> because the routes that
/// reach it are mapped unconditionally.
/// </para>
/// </remarks>
public class OfficeSearchService
{
    // Retained for the matter-type and record-type reference lists: small non-customer lookup tables that stay
    // app-only. The ENTITY search does not use it (task 062).
    private readonly DataverseWebApiClient _dataverseClient;

    // F1 / task 062: the caller-scoped read seam for /office/search/entities. Runs the search query AS the
    // calling user (MSCRMCallerID impersonation), so Dataverse applies row-level security natively and the
    // picker returns only records the caller may read. Reused rather than reinvented (CLAUDE.md §11): this is
    // the same unconditionally-registered singleton seam the Communication read path uses — its QueryAsync
    // takes an entity set + an OData query string and is entity-agnostic despite the Communication-specific
    // interface name.
    private readonly IImpersonatedCommunicationQuery _impersonatedQuery;

    // Task 084 (#1037): the save's own per-record rights probe (OBO RetrievePrincipalAccess, as the caller), so
    // the picker can mark each record it offers with whether the save would accept it. See
    // EvaluateFilingAccessAsync.
    private readonly CallerRecordAccessProbe _accessProbe;

    private readonly ILogger<OfficeSearchService> _logger;

    /// <summary>
    /// Most records one request has filing access evaluated for (task 084). The picker asks for one type at
    /// <c>top=10</c>, so it never reaches this; a larger request gets <c>null</c> ("not checked") past it.
    /// </summary>
    internal const int MaxFilingAccessChecks = 50;

    public OfficeSearchService(
        DataverseWebApiClient dataverseClient,
        IImpersonatedCommunicationQuery impersonatedQuery,
        CallerRecordAccessProbe accessProbe,
        ILogger<OfficeSearchService> logger)
    {
        _dataverseClient = dataverseClient;
        _impersonatedQuery = impersonatedQuery;
        _accessProbe = accessProbe;
        _logger = logger;
    }

    /// <summary>
    /// For each target, whether <c>POST /api/office/save</c> would accept it as the record to file to:
    /// <c>true</c> or <c>false</c>, or <c>null</c> when it was not checked (past <see cref="MaxFilingAccessChecks"/>).
    /// </summary>
    /// <param name="targets">
    /// Each record as the type SPELLING the client will send in the save's <c>TargetEntity.EntityType</c>
    /// (the friendly name, e.g. <c>Matter</c>; a logical name also resolves) and its id.
    /// </param>
    /// <param name="callerBearerToken">The caller's bearer token from the inbound request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// <para><b>"Pickable equals savable" (task 084, #1037).</b> The picker's search returns every record the
    /// caller can READ; the save authorizes its target by APPENDTO. Without this, a user holding Read but not
    /// AppendTo on a record (realistically a secure record shared view-only) can pick it, and the save is then
    /// refused. The owner decided to show such a record disabled, with the reason, rather than hide it.</para>
    ///
    /// <para><b>The save's evaluator, not a copy of it.</b> Each verdict is made by the same three pieces the
    /// save's <see cref="Api.Filters.EntityAccessFilter"/> uses:
    /// <list type="number">
    ///   <item><description><see cref="Api.Filters.EntityAccessFilter.TryResolveEntitySet"/>, the one type →
    ///   entity-set map. A type it cannot resolve is <c>false</c>, because the save refuses it (400
    ///   <c>OFFICE_002</c>);</description></item>
    ///   <item><description><see cref="CallerRecordAccessProbe"/>, the same OBO <c>RetrievePrincipalAccess</c> as
    ///   the caller;</description></item>
    ///   <item><description><see cref="OperationAccessPolicy.HasRequiredRights"/> for
    ///   <see cref="Api.Filters.EntityAccessFilter.AssociateOperation"/>, so the required right is read from the
    ///   one place that defines it.</description></item>
    /// </list>
    /// Any probe failure answers <see cref="AccessRights.None"/>, which is <c>false</c> here and a refusal at the
    /// save. A doubtful record is never shown as fileable.</para>
    ///
    /// <para><b>Cost.</b> One OBO exchange and one <c>WhoAmI</c> per call, then one <c>RetrievePrincipalAccess</c>
    /// per record, at most <see cref="CallerRecordAccessProbe.MaxConcurrentRecordLookups"/> at once. Only callers
    /// that ask pay it; the To Do assignee search does not.</para>
    /// </remarks>
    public async Task<IReadOnlyList<bool?>> EvaluateFilingAccessAsync(
        IReadOnlyList<(string EntityType, Guid RecordId)> targets,
        string? callerBearerToken,
        CancellationToken cancellationToken = default)
    {
        var verdicts = new bool?[targets.Count];
        var lookupIndexByTarget = new int[targets.Count];
        var lookups = new List<(string EntitySet, Guid RecordId)>();

        for (var i = 0; i < targets.Count; i++)
        {
            lookupIndexByTarget[i] = -1;
            var (entityType, recordId) = targets[i];

            // The save refuses both of these before any rights question (400 OFFICE_002 / an empty target id),
            // so they are not fileable whatever the caller holds.
            if (recordId == Guid.Empty || !EntityAccessFilter.TryResolveEntitySet(entityType, out var entitySet))
            {
                verdicts[i] = false;
                continue;
            }

            if (lookups.Count >= MaxFilingAccessChecks)
                continue; // null: not checked

            lookupIndexByTarget[i] = lookups.Count;
            lookups.Add((entitySet, recordId));
        }

        if (lookups.Count == 0)
            return verdicts;

        var rights = await _accessProbe
            .GetCallerRightsForRecordsAsync(callerBearerToken, lookups, cancellationToken)
            .ConfigureAwait(false);

        for (var i = 0; i < targets.Count; i++)
        {
            if (lookupIndexByTarget[i] >= 0)
            {
                verdicts[i] = OperationAccessPolicy.HasRequiredRights(
                    rights[lookupIndexByTarget[i]], EntityAccessFilter.AssociateOperation);
            }
        }

        return verdicts;
    }

    /// <summary>
    /// Returns <paramref name="response"/> with each result's <see cref="EntitySearchResult.CanFile"/> set by
    /// <see cref="EvaluateFilingAccessAsync"/>. Used for <c>GET /api/office/search/entities?access=file</c>.
    /// </summary>
    /// <remarks>
    /// Each row is evaluated on its FRIENDLY type (<see cref="EntitySearchResult.EntityType"/>, e.g. <c>Matter</c>),
    /// which is exactly the string the pane sends to the save as <c>TargetEntity.EntityType</c>.
    /// </remarks>
    public async Task<EntitySearchResponse> ApplyFilingAccessAsync(
        EntitySearchResponse response,
        string? callerBearerToken,
        CancellationToken cancellationToken = default)
    {
        if (response.Results.Count == 0)
            return response;

        var verdicts = await EvaluateFilingAccessAsync(
            response.Results.Select(r => (r.EntityType.ToString(), r.Id)).ToList(),
            callerBearerToken,
            cancellationToken).ConfigureAwait(false);

        var annotated = response.Results
            .Select((row, i) => row with { CanFile = verdicts[i] })
            .ToList();

        _logger.LogInformation(
            "Filing access evaluated for {Count} search result(s): {Fileable} fileable, {NotFileable} not, {Unchecked} unchecked",
            annotated.Count,
            annotated.Count(r => r.CanFile == true),
            annotated.Count(r => r.CanFile == false),
            annotated.Count(r => r.CanFile is null));

        return response with { Results = annotated };
    }

    /// <inheritdoc cref="IOfficeService.SearchEntitiesAsync"/>
    public async Task<EntitySearchResponse> SearchEntitiesAsync(
        EntitySearchRequest request,
        string userId,
        Guid callerSystemUserId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Entity search requested: Query='{Query}', Types={EntityTypes}, Skip={Skip}, Top={Top}, User={UserId}",
            request.Query,
            request.EntityTypes != null ? string.Join(",", request.EntityTypes) : "all",
            request.Skip,
            request.Top,
            userId);

        // Determine which entity types to search
        var typesToSearch = GetEntityTypesToSearch(request.EntityTypes);

        // FORCING FUNCTION (task 062 / finding F1). Every row this method can return comes from an
        // IMPERSONATED query keyed by the caller's systemuserid. Absent it, there is no identity for
        // Dataverse to filter by and the only query we could issue is the tenant-wide app-only
        // enumeration this task exists to close — so refuse loudly rather than serve it. The endpoint
        // resolves and validates it before calling; reaching here without it means the pipeline
        // changed underneath us.
        if (callerSystemUserId == Guid.Empty)
        {
            _logger.LogError(
                "Entity search reached OfficeSearchService without a caller systemuserid ({CallerSystemUserId}) — "
                + "refusing rather than falling back to the app-only enumeration (fail closed).",
                callerSystemUserId);

            throw new InvalidOperationException(
                "Entity search requires an impersonated Dataverse read as the calling user; "
                + "refusing to issue an app-only, security-untrimmed query.");
        }

        // Query each requested entity type with a name/number 'contains' filter, IMPERSONATED as the
        // caller (MSCRMCallerID = their systemuserid) so Dataverse itself applies row-level security —
        // ownership, role depth, business unit, teams, sharing, hierarchy — inside the query. The rows
        // that come back ARE what this caller may read, for every entity type, on every page, at the
        // cost of the same one round trip per type the app-only query took. See the remarks on
        // QuerySearchEntityAsync for why this mechanism was chosen over post-trimming each row.
        //
        // Each type stays best-effort — one entity's failure (missing table, transient 4xx) is logged
        // and skipped, never fails the whole picker — EXCEPT that a run in which every attempted type
        // threw is reported as a failure rather than as "no results": an impersonation privilege that
        // is not configured must not look like an empty tenant.
        var combined = new List<EntitySearchResult>();
        var perTypeTop = Math.Clamp(request.Top, 5, 50);
        var typeFailures = 0;
        var typesAttempted = 0;
        foreach (var type in typesToSearch)
        {
            typesAttempted++;
            try
            {
                combined.AddRange(await QuerySearchEntityAsync(
                    type, request.Query, perTypeTop, callerSystemUserId, cancellationToken));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                typeFailures++;
                _logger.LogWarning(ex, "Entity search failed for type {EntityType}; skipping", type);
            }
        }

        if (typesAttempted > 0 && typeFailures == typesAttempted)
        {
            // The single most likely cause is the go-live prerequisite: the BFF application user does
            // not hold prvActOnBehalfOfAnotherUser, so every impersonated read is rejected. Surfacing
            // that as an empty picker would be a lie that reads as "you have access to nothing".
            throw new InvalidOperationException(
                $"Entity search failed for all {typesAttempted} requested entity type(s). The impersonated "
                + "read may be rejected because the BFF application user lacks the Dataverse Delegate "
                + "privilege prvActOnBehalfOfAnotherUser.");
        }

        // Rank: prefix matches first, then most-recently-modified.
        var ordered = combined
            .OrderByDescending(r => r.Name.StartsWith(request.Query, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.ModifiedOn)
            .ToList();

        return new EntitySearchResponse
        {
            Results = ordered.Skip(request.Skip).Take(request.Top).ToList(),
            TotalCount = ordered.Count + request.Skip,
            HasMore = ordered.Count > request.Skip + request.Top
        };
    }

    /// <summary>Per-entity-type Dataverse Web API search metadata (mirrors RecordSyncJob's catalogue).</summary>
    /// <param name="EmailField">
    /// Task 091 (UAT-2): an additive, DISPLAY-only column — selected in the same impersonated query as every
    /// other field above, never added to the <c>contains(...)</c> search predicate. <c>null</c> for every
    /// entity type except Contact (<c>emailaddress1</c>), where it lets the Assigned-To picker tell apart
    /// two contacts that share a display name.
    /// </param>
    internal sealed record EntitySearchMeta(string EntitySet, string IdField, string NameField, string? RefField, string? DescField, string? EmailField = null);

    private static readonly IReadOnlyDictionary<AssociationEntityType, EntitySearchMeta> _searchMeta =
        new Dictionary<AssociationEntityType, EntitySearchMeta>
        {
            [AssociationEntityType.Matter] = new("sprk_matters", "sprk_matterid", "sprk_mattername", "sprk_matternumber", "sprk_matterdescription"),
            [AssociationEntityType.Project] = new("sprk_projects", "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "sprk_projectdescription"),
            [AssociationEntityType.Invoice] = new("sprk_invoices", "sprk_invoiceid", "sprk_name", "sprk_invoicenumber", "sprk_description"),
            [AssociationEntityType.Account] = new("accounts", "accountid", "name", "accountnumber", "description"),
            [AssociationEntityType.Contact] = new("contacts", "contactid", "fullname", null, "jobtitle", "emailaddress1"),
        };

    /// <summary>
    /// Runs a single entity type's name/number 'contains' query against the Dataverse Web API
    /// IMPERSONATED as <paramref name="callerSystemUserId"/>, and maps the rows to
    /// <see cref="EntitySearchResult"/>. The rows returned are exactly those the caller may read.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why trim INSIDE the query rather than post-trim each row</b> (task 062, finding F1 — the
    /// mechanism choice the acceptance criteria ask to be stated). The alternative, and the one the
    /// sibling record/visualization search surfaces use, is to run the app-only query and then call
    /// <c>AuthorizationService.GetCallerRecordAccessAsync</c> per returned row. Both are correct; they
    /// differ on cost and on coverage:
    /// </para>
    /// <list type="bullet">
    ///   <item><description><b>Cost.</b> This is a keystroke-driven typeahead with a 500 ms contract,
    ///   and the picker asks for up to 50 rows per type across five types. Post-trimming buys one
    ///   Dataverse round trip PER DISTINCT ROW — up to 250 sequential calls for one keystroke.
    ///   Impersonation adds none: Dataverse filters inside the same single query per type. The only
    ///   added round trip on the whole request is the one oid→systemuserid lookup the endpoint already
    ///   performs for quick-create.</description></item>
    ///   <item><description><b>Coverage.</b> Post-trimming can only trim rows it has already fetched,
    ///   so a caller entitled to few records receives a short page indistinguishable from "nothing
    ///   matched" once ranking pushes their matches past the fetched window — the limitation
    ///   <c>RecordSearchEndpoints.AuthorizeRowsAsync</c> documents as follow-up F-4. Filtering inside
    ///   the query has no such window: page N is drawn from the caller's own rows.</description></item>
    ///   <item><description><b>Uniformity.</b> Post-trimming needs an entity-set allow-list, and the
    ///   shared one (<c>SemanticSearchAuthorizationFilter.AuthorizableEntitySets</c>) covers matter,
    ///   project, invoice and work assignment — but NOT account or contact, two of this route's five
    ///   types. Impersonation is entity-agnostic, so all five are covered by the same
    ///   mechanism.</description></item>
    /// </list>
    /// <para>
    /// This is the seam <c>.claude/constraints/auth.md</c> names as genuinely caller-scoped, and the
    /// one the Communication read path already ships on. <b>Operational prerequisite</b>: the BFF
    /// application user must hold the Dataverse Delegate privilege
    /// <c>prvActOnBehalfOfAnotherUser</c>. Without it Dataverse rejects the call — which fails closed,
    /// and which <see cref="SearchEntitiesAsync"/> reports as an error rather than as an empty picker.
    /// </para>
    /// </remarks>
    private async Task<List<EntitySearchResult>> QuerySearchEntityAsync(
        AssociationEntityType type,
        string query,
        int top,
        Guid callerSystemUserId,
        CancellationToken cancellationToken)
    {
        var meta = _searchMeta[type];

        // OData string literal: double single-quotes, then URL-encode the value (the surrounding
        // contains(...) syntax stays literal).
        var value = Uri.EscapeDataString(query.Replace("'", "''"));
        var nameClause = $"contains({meta.NameField},'{value}')";
        var filter = meta.RefField is null
            ? nameClause
            : $"({nameClause} or contains({meta.RefField},'{value}'))";

        var selectFields = new List<string> { meta.IdField, meta.NameField, "modifiedon" };
        if (meta.RefField is not null) selectFields.Add(meta.RefField);
        if (meta.DescField is not null) selectFields.Add(meta.DescField);
        // Task 091: selected (never searched — the contains() predicate above is built from NameField/RefField
        // only) so the Assigned-To picker can tell apart two contacts sharing a display name.
        if (meta.EmailField is not null) selectFields.Add(meta.EmailField);

        var odataQuery =
            $"$filter={filter}&$select={string.Join(",", selectFields)}&$top={top}";

        var rows = await _impersonatedQuery.QueryAsync(
            meta.EntitySet,
            odataQuery,
            callerSystemUserId,
            cancellationToken);

        var results = new List<EntitySearchResult>(rows.Count);
        foreach (var row in rows)
        {
            var mapped = MapSearchRow(type, meta, row);
            if (mapped is not null)
                results.Add(mapped);
        }

        return results;
    }

    /// <summary>
    /// Maps one Dataverse Web API JSON row to an <see cref="EntitySearchResult"/>, or null when the
    /// row has no name (never surface an unnamed record in the picker). Pure — unit-tested.
    /// </summary>
    internal static EntitySearchResult? MapSearchRow(
        AssociationEntityType type,
        EntitySearchMeta meta,
        Dictionary<string, JsonElement> row)
    {
        var name = GetJsonString(row, meta.NameField);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var id = Guid.TryParse(GetJsonString(row, meta.IdField), out var g) ? g : Guid.Empty;
        var refVal = meta.RefField is not null ? GetJsonString(row, meta.RefField) : null;
        var desc = meta.DescField is not null ? GetJsonString(row, meta.DescField) : null;
        var email = meta.EmailField is not null ? GetJsonString(row, meta.EmailField) : null;
        var modified = DateTimeOffset.TryParse(GetJsonString(row, "modifiedon"), out var mo)
            ? mo
            : DateTimeOffset.UtcNow;

        return new EntitySearchResult
        {
            Id = id,
            EntityType = type,
            LogicalName = GetLogicalName(type),
            Name = name!,
            DisplayInfo = !string.IsNullOrWhiteSpace(refVal) ? refVal! : (desc ?? GetLogicalName(type)),
            PrimaryField = !string.IsNullOrWhiteSpace(refVal) ? refVal! : name!,
            // Task 091: additive display-only field; null for every type without an EmailField (i.e. all but
            // Contact) and null for a Contact with no email on file. Never a fallback onto PrimaryField/Name.
            Email = !string.IsNullOrWhiteSpace(email) ? email! : null,
            IconUrl = $"/icons/{type.ToString().ToLowerInvariant()}.svg",
            ModifiedOn = modified
        };
    }

    private static string? GetJsonString(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    /// <summary>
    /// Determines which entity types to search based on the request.
    /// </summary>
    private static HashSet<AssociationEntityType> GetEntityTypesToSearch(string[]? requestedTypes)
    {
        // If no types specified, search all
        if (requestedTypes == null || requestedTypes.Length == 0)
        {
            return new HashSet<AssociationEntityType>(Enum.GetValues<AssociationEntityType>());
        }

        var typesToSearch = new HashSet<AssociationEntityType>();
        foreach (var typeStr in requestedTypes)
        {
            if (Enum.TryParse<AssociationEntityType>(typeStr, ignoreCase: true, out var entityType))
            {
                typesToSearch.Add(entityType);
            }
        }

        // If no valid types were specified, search all
        return typesToSearch.Count > 0
            ? typesToSearch
            : new HashSet<AssociationEntityType>(Enum.GetValues<AssociationEntityType>());
    }

    /// <summary>
    /// Gets the Dataverse logical name for an entity type.
    /// </summary>
    private static string GetLogicalName(AssociationEntityType entityType) => entityType switch
    {
        AssociationEntityType.Matter => "sprk_matter",
        AssociationEntityType.Project => "sprk_project",
        AssociationEntityType.Invoice => "sprk_invoice",
        AssociationEntityType.Account => "account",
        AssociationEntityType.Contact => "contact",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType))
    };

    /// <summary>
    /// The reference lists the pane's "+ New" form loads (task 100; matter types since task 038), keyed by the
    /// <c>{list}</c> route segment of <c>GET /api/office/search/{list}</c>. A CLOSED table: a key that is not here is
    /// a 404, so the route can never be pointed at an arbitrary table. Columns verified against live metadata
    /// (spaarkedev1, 2026-10-05): <c>sprk_projecttype_ref</c> has no code column and names its rows <c>sprk_name</c>.
    /// </summary>
    internal static readonly IReadOnlyDictionary<string, OfficeReferenceList> ReferenceLists =
        new Dictionary<string, OfficeReferenceList>(StringComparer.OrdinalIgnoreCase)
        {
            ["matter-types"] = new("sprk_mattertype_refs", "sprk_mattertype_refid", "sprk_mattertypename", "sprk_mattertypecode"),
            ["practice-areas"] = new("sprk_practicearea_refs", "sprk_practicearea_refid", "sprk_practiceareaname", "sprk_practiceareacode"),
            ["project-types"] = new("sprk_projecttype_refs", "sprk_projecttype_refid", "sprk_name", null),
        };

    /// <summary>The reference list a <c>{list}</c> route segment names, or false for one that is not offered.</summary>
    public static bool TryGetReferenceList(string? key, out OfficeReferenceList list)
    {
        if (!string.IsNullOrWhiteSpace(key) && ReferenceLists.TryGetValue(key.Trim(), out var found))
        {
            list = found;
            return true;
        }

        list = null!;
        return false;
    }

    /// <inheritdoc cref="IOfficeService.GetReferenceListAsync"/>
    public async Task<ReferenceListResponse> GetReferenceListAsync(
        OfficeReferenceList list,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(list);

        var select = list.CodeColumn is null
            ? $"{list.IdColumn},{list.NameColumn}"
            : $"{list.IdColumn},{list.NameColumn},{list.CodeColumn}";

        var rows = await _dataverseClient.QueryAsync<Dictionary<string, JsonElement>>(
            list.EntitySet,
            filter: "statecode eq 0",
            select: select,
            top: 50,
            cancellationToken: cancellationToken);

        var options = new List<ReferenceListOption>(rows.Count);
        foreach (var row in rows)
        {
            var mapped = MapReferenceRow(row, list);
            if (mapped is not null)
                options.Add(mapped);
        }

        return new ReferenceListResponse
        {
            Results = options.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    /// <summary>
    /// Maps one Dataverse Web API JSON row of a reference list to a <see cref="ReferenceListOption"/>, or null when
    /// the row has no name or no parseable id (never surface an unusable reference row). Pure — unit-tested
    /// (mirrors <see cref="MapSearchRow"/>'s shape).
    /// </summary>
    internal static ReferenceListOption? MapReferenceRow(Dictionary<string, JsonElement> row, OfficeReferenceList list)
    {
        var name = GetJsonString(row, list.NameColumn);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var id = Guid.TryParse(GetJsonString(row, list.IdColumn), out var g) ? g : Guid.Empty;
        if (id == Guid.Empty)
            return null;

        return new ReferenceListOption
        {
            Id = id,
            Name = name!,
            Code = list.CodeColumn is null ? null : GetJsonString(row, list.CodeColumn)
        };
    }

    /// <summary>
    /// Resolves the <c>sprk_recordtype_ref</c> id for a target entity logical name (mirrors the client
    /// <c>PolymorphicResolverService.resolveRecordType</c>). Best-effort — returns null on any failure or when the
    /// reference table / row is absent, so the To Do create proceeds with the typed lookup alone.
    /// </summary>
    public async Task<Guid?> ResolveRegardingRecordTypeIdAsync(string logicalName, CancellationToken cancellationToken)
    {
        try
        {
            var value = Uri.EscapeDataString(logicalName.Replace("'", "''"));
            var rows = await _dataverseClient.QueryAsync<Dictionary<string, JsonElement>>(
                "sprk_recordtype_refs",
                filter: $"sprk_recordlogicalname eq '{value}' and statecode eq 0",
                select: "sprk_recordtype_refid",
                top: 1,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var idString = rows.Count > 0 ? GetJsonString(rows[0], "sprk_recordtype_refid") : null;
            return Guid.TryParse(idString, out var id) ? id : null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Record-type ref lookup failed for {LogicalName}; To Do regarding will omit it.", logicalName);
            return null;
        }
    }
}

/// <summary>
/// One reference list served by <c>GET /api/office/search/{list}</c> (task 100): the Web API entity set to read and
/// the columns that become a <see cref="ReferenceListOption"/>. Only the rows of
/// <see cref="OfficeSearchService.ReferenceLists"/> exist — the route cannot name a table of its own.
/// </summary>
/// <param name="EntitySet">Web API entity set (e.g. <c>sprk_practicearea_refs</c>).</param>
/// <param name="IdColumn">Primary key column.</param>
/// <param name="NameColumn">Display-name column.</param>
/// <param name="CodeColumn">Short-code column, or <see langword="null"/> when the table has none.</param>
public sealed record OfficeReferenceList(string EntitySet, string IdColumn, string NameColumn, string? CodeColumn);