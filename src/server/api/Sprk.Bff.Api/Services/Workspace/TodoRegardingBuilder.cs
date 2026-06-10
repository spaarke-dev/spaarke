using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Workspace;

/// <summary>
/// Server-side helper that mirrors the semantics of
/// <c>PolymorphicResolverService.applyResolverFields</c> for the <c>sprk_todo</c>
/// entity (smart-todo-decoupling-r3, ADR-024).
/// </summary>
/// <remarks>
/// <para>
/// <c>sprk_todo</c> has 11 entity-specific regarding lookups
/// (<c>sprk_regardingmatter</c>, <c>sprk_regardingproject</c>, <c>sprk_regardingevent</c>,
/// <c>sprk_regardingcommunication</c>, <c>sprk_regardingworkassignment</c>,
/// <c>sprk_regardinginvoice</c>, <c>sprk_regardingbudget</c>, <c>sprk_regardinganalysis</c>,
/// <c>sprk_regardingorganization</c>, <c>sprk_regardingcontact</c>,
/// <c>sprk_regardingdocument</c>) and 4 denormalized resolver fields
/// (<c>sprk_regardingrecordtype</c>, <c>sprk_regardingrecordid</c>,
/// <c>sprk_regardingrecordname</c>, <c>sprk_regardingrecordurl</c>).
/// </para>
///
/// <para>
/// <strong>ADR-024 invariants enforced here:</strong>
/// </para>
/// <list type="bullet">
///   <item>At MOST ONE specific regarding lookup is populated at a time.</item>
///   <item>The four resolver fields are populated atomically alongside the
///         specific lookup — never independently.</item>
/// </list>
///
/// <para>
/// <strong>Portability:</strong> No hard-coded org URLs or tenant ids. Record-URL is
/// built relative to <c>/main.aspx</c>; the model-driven app resolves the host
/// origin at click time. <c>sprk_recordtype_ref</c> resolution goes through the
/// injected <see cref="IDataverseService"/> (which is backed by
/// <c>ServiceClient</c> / <c>IOrganizationService</c>).
/// </para>
/// </remarks>
internal sealed class TodoRegardingBuilder
{
    // ─────────────────────────────────────────────────────────────────────────
    // Field name + entity-set-name table
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Map of supported regarding parent entities to the matching
    /// <c>sprk_todo</c> specific lookup attribute.
    /// </summary>
    /// <remarks>
    /// Mirrors the 11-entity contract from
    /// <c>src/solutions/SpaarkeCore/entities/sprk_todo/entity-schema.md</c>.
    /// </remarks>
    internal static readonly IReadOnlyDictionary<string, string> RegardingLookupByEntity =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_matter"] = "sprk_regardingmatter",
            ["sprk_project"] = "sprk_regardingproject",
            ["sprk_event"] = "sprk_regardingevent",
            ["sprk_communication"] = "sprk_regardingcommunication",
            ["sprk_workassignment"] = "sprk_regardingworkassignment",
            ["sprk_invoice"] = "sprk_regardinginvoice",
            ["sprk_budget"] = "sprk_regardingbudget",
            ["sprk_analysis"] = "sprk_regardinganalysis",
            ["sprk_organization"] = "sprk_regardingorganization",
            ["contact"] = "sprk_regardingcontact",
            ["sprk_document"] = "sprk_regardingdocument",
        };

    /// <summary>
    /// All 11 specific regarding lookup attribute names on <c>sprk_todo</c>.
    /// Used for the multi-lookup guard.
    /// </summary>
    internal static readonly IReadOnlySet<string> AllRegardingLookups =
        new HashSet<string>(RegardingLookupByEntity.Values, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The 4 denormalized resolver field names on <c>sprk_todo</c>.
    /// </summary>
    internal const string FieldRegardingRecordType = "sprk_regardingrecordtype";
    internal const string FieldRegardingRecordId = "sprk_regardingrecordid";
    internal const string FieldRegardingRecordName = "sprk_regardingrecordname";
    internal const string FieldRegardingRecordUrl = "sprk_regardingrecordurl";

    // ─────────────────────────────────────────────────────────────────────────
    // Dependencies
    // ─────────────────────────────────────────────────────────────────────────

    private readonly ICommunicationDataverseService _communicationService;
    private readonly ILogger<TodoRegardingBuilder> _logger;

    /// <summary>
    /// Lazy cache for <c>sprk_recordtype_ref</c> lookups (entity logical name → GUID + display name).
    /// </summary>
    private readonly Dictionary<string, (Guid Id, string DisplayName)?> _recordTypeRefCache = new();

    public TodoRegardingBuilder(
        ICommunicationDataverseService communicationService,
        ILogger<TodoRegardingBuilder> logger)
    {
        _communicationService = communicationService ?? throw new ArgumentNullException(nameof(communicationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Atomically apply the regarding-parent association to a <c>sprk_todo</c>
    /// <see cref="Entity"/> in flight.
    /// </summary>
    /// <param name="todoEntity">The <c>sprk_todo</c> entity being built (mutated in place).</param>
    /// <param name="regardingEntityName">Parent entity logical name (e.g. <c>sprk_matter</c>).</param>
    /// <param name="regardingId">Parent record GUID.</param>
    /// <param name="regardingDisplayName">Parent display name (for <c>sprk_regardingrecordname</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException">Any required argument is null.</exception>
    /// <exception cref="ArgumentException">
    /// Empty <paramref name="regardingId"/>, unsupported <paramref name="regardingEntityName"/>,
    /// or wrong entity (not <c>sprk_todo</c>).
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// A specific regarding lookup is already populated on <paramref name="todoEntity"/>
    /// (mutual-exclusion guard per ADR-024).
    /// </exception>
    public async Task ApplyResolverFieldsAsync(
        Entity todoEntity,
        string regardingEntityName,
        Guid regardingId,
        string regardingDisplayName,
        CancellationToken ct = default)
    {
        if (todoEntity is null)
            throw new ArgumentNullException(nameof(todoEntity));
        if (string.IsNullOrWhiteSpace(regardingEntityName))
            throw new ArgumentException("Regarding entity name is required.", nameof(regardingEntityName));
        if (regardingId == Guid.Empty)
            throw new ArgumentException("Regarding id must be a non-empty GUID.", nameof(regardingId));

        if (!string.Equals(todoEntity.LogicalName, "sprk_todo", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                $"TodoRegardingBuilder only operates on sprk_todo entities. Got: '{todoEntity.LogicalName}'.",
                nameof(todoEntity));
        }

        if (!RegardingLookupByEntity.TryGetValue(regardingEntityName, out var specificLookup))
        {
            throw new ArgumentException(
                $"Entity '{regardingEntityName}' is not a supported sprk_todo regarding parent. " +
                $"Supported: {string.Join(", ", RegardingLookupByEntity.Keys)}.",
                nameof(regardingEntityName));
        }

        // ADR-024: at most ONE specific regarding lookup at a time.
        var alreadySet = todoEntity.Attributes.Keys
            .Where(k => AllRegardingLookups.Contains(k))
            .ToList();
        if (alreadySet.Count > 0)
        {
            throw new InvalidOperationException(
                $"sprk_todo already has a regarding lookup set ({string.Join(", ", alreadySet)}). " +
                "ADR-024 requires at most one specific regarding lookup. " +
                "Clear the existing one before setting a new parent.");
        }

        // 1) Specific lookup
        todoEntity[specificLookup] = new EntityReference(regardingEntityName, regardingId)
        {
            Name = regardingDisplayName
        };

        // 2) Resolver fields (4) — populated atomically
        var cleanId = regardingId.ToString("D").ToLowerInvariant();
        todoEntity[FieldRegardingRecordId] = cleanId;
        todoEntity[FieldRegardingRecordName] = regardingDisplayName ?? string.Empty;
        todoEntity[FieldRegardingRecordUrl] = BuildRecordUrl(regardingEntityName, cleanId);

        var recordTypeRef = await ResolveRecordTypeRefAsync(regardingEntityName, ct);
        if (recordTypeRef.HasValue)
        {
            todoEntity[FieldRegardingRecordType] = new EntityReference(
                "sprk_recordtype_ref", recordTypeRef.Value.Id)
            {
                Name = recordTypeRef.Value.DisplayName
            };
        }
        else
        {
            // Non-fatal: log + continue. Specific lookup + id/name/url still populated.
            // sprk_recordtype_ref is used by cross-entity views; missing it loses the entity
            // type icon but does not break correctness.
            _logger.LogWarning(
                "sprk_recordtype_ref not found for entity '{Entity}'. Resolver type field left unset.",
                regardingEntityName);
        }

        _logger.LogDebug(
            "Applied resolver fields to sprk_todo: Entity={Entity}, Id={Id}, Name={Name}",
            regardingEntityName, cleanId, regardingDisplayName);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolve the <c>sprk_recordtype_ref</c> GUID + display name for an entity
    /// logical name. Cached for the lifetime of the builder instance.
    /// </summary>
    private async Task<(Guid Id, string DisplayName)?> ResolveRecordTypeRefAsync(
        string entityLogicalName, CancellationToken ct)
    {
        if (_recordTypeRefCache.TryGetValue(entityLogicalName, out var cached))
            return cached;

        try
        {
            var record = await _communicationService.QueryRecordTypeRefAsync(entityLogicalName, ct);
            if (record is not null)
            {
                var entry = (
                    Id: record.Id,
                    DisplayName: record.GetAttributeValue<string>("sprk_recorddisplayname") ?? entityLogicalName
                );
                _recordTypeRefCache[entityLogicalName] = entry;
                return entry;
            }
        }
        catch (Exception ex)
        {
            // Non-fatal — log and cache the negative result so we don't retry every call
            _logger.LogWarning(ex,
                "Failed to query sprk_recordtype_ref for '{Entity}'. Caching negative result.",
                entityLogicalName);
        }

        _recordTypeRefCache[entityLogicalName] = null;
        return null;
    }

    /// <summary>
    /// Build a Dataverse model-driven-app record URL for the resolver.
    /// </summary>
    /// <remarks>
    /// Returns a RELATIVE URL — the host origin is resolved by the model-driven
    /// app at click time. No org URL or tenant id is hard-coded here.
    /// </remarks>
    internal static string BuildRecordUrl(string entityLogicalName, string recordId)
    {
        return $"/main.aspx?pagetype=entityrecord&etn={entityLogicalName}&id={recordId}";
    }
}
