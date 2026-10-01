using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// The Office add-in's Dataverse READS: the "File to" entity picker, the matter-type list, and the
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

    private readonly ILogger<OfficeSearchService> _logger;

    public OfficeSearchService(
        DataverseWebApiClient dataverseClient,
        IImpersonatedCommunicationQuery impersonatedQuery,
        ILogger<OfficeSearchService> logger)
    {
        _dataverseClient = dataverseClient;
        _impersonatedQuery = impersonatedQuery;
        _logger = logger;
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
    internal sealed record EntitySearchMeta(string EntitySet, string IdField, string NameField, string? RefField, string? DescField);

    private static readonly IReadOnlyDictionary<AssociationEntityType, EntitySearchMeta> _searchMeta =
        new Dictionary<AssociationEntityType, EntitySearchMeta>
        {
            [AssociationEntityType.Matter] = new("sprk_matters", "sprk_matterid", "sprk_mattername", "sprk_matternumber", "sprk_matterdescription"),
            [AssociationEntityType.Project] = new("sprk_projects", "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "sprk_projectdescription"),
            [AssociationEntityType.Invoice] = new("sprk_invoices", "sprk_invoiceid", "sprk_name", "sprk_invoicenumber", "sprk_description"),
            [AssociationEntityType.Account] = new("accounts", "accountid", "name", "accountnumber", "description"),
            [AssociationEntityType.Contact] = new("contacts", "contactid", "fullname", null, "jobtitle"),
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

    /// <inheritdoc cref="IOfficeService.GetMatterTypesAsync"/>
    public async Task<MatterTypeListResponse> GetMatterTypesAsync(
        CancellationToken cancellationToken = default)
    {
        var rows = await _dataverseClient.QueryAsync<Dictionary<string, JsonElement>>(
            "sprk_mattertype_refs",
            filter: "statecode eq 0",
            select: "sprk_mattertype_refid,sprk_mattertypename,sprk_mattertypecode",
            top: 50,
            cancellationToken: cancellationToken);

        var options = new List<MatterTypeOption>(rows.Count);
        foreach (var row in rows)
        {
            var mapped = MapMatterTypeRow(row);
            if (mapped is not null)
                options.Add(mapped);
        }

        return new MatterTypeListResponse
        {
            Results = options.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList()
        };
    }

    /// <summary>
    /// Maps one Dataverse Web API JSON row from <c>sprk_mattertype_refs</c> to a <see cref="MatterTypeOption"/>,
    /// or null when the row has no name or no parseable id (never surface an unusable reference row). Pure —
    /// unit-tested (mirrors <see cref="MapSearchRow"/>'s shape).
    /// </summary>
    internal static MatterTypeOption? MapMatterTypeRow(Dictionary<string, JsonElement> row)
    {
        var name = GetJsonString(row, "sprk_mattertypename");
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var id = Guid.TryParse(GetJsonString(row, "sprk_mattertype_refid"), out var g) ? g : Guid.Empty;
        if (id == Guid.Empty)
            return null;

        return new MatterTypeOption
        {
            Id = id,
            Name = name!,
            Code = GetJsonString(row, "sprk_mattertypecode")
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
