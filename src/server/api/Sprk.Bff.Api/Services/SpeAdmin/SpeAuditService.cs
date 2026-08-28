using System.Security.Claims;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Authentication;

namespace Sprk.Bff.Api.Services.SpeAdmin;

/// <summary>
/// Audit logging service for SPE Admin operations.
/// Writes structured audit entries to the sprk_speauditlog Dataverse table.
///
/// Every mutation performed through the SPE Admin endpoints is recorded with:
/// - Operation name and category
/// - Target resource identifier
/// - HTTP response status
/// - Business unit, environment config, and container type config lookup references
/// - Identity of the performing user (extracted from JWT claims)
/// - UTC timestamp
///
/// Registration: Scoped (per-request) so that HttpContext identity is captured
/// correctly for each request. See SpeAdminModule for DI wiring.
///
/// Failure policy: audit failures are logged but never thrown to the caller.
/// The primary operation must always complete regardless of audit outcome.
/// </summary>
public class SpeAuditService
{
    private const string AuditLogEntitySet = "sprk_speauditlogs";

    // sprk_category option-set values, verified against the live Dataverse schema 2026-08-21 (task 005).
    internal const int CategoryContainerType = 100000000;
    internal const int CategoryContainer = 100000001;
    internal const int CategoryPermission = 100000002;
    internal const int CategoryFile = 100000003;
    internal const int CategorySearch = 100000004;
    internal const int CategorySecurity = 100000005;

    private readonly DataverseWebApiClient _dataverseClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SpeAuditService> _logger;

    public SpeAuditService(
        DataverseWebApiClient dataverseClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SpeAuditService> logger)
    {
        _dataverseClient = dataverseClient ?? throw new ArgumentNullException(nameof(dataverseClient));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Logs an SPE Admin operation to the sprk_speauditlog Dataverse table.
    /// </summary>
    /// <param name="operation">
    ///   Short name identifying the operation, e.g. "CreateContainerTypeConfig", "DeleteEnvironment".
    /// </param>
    /// <param name="category">
    ///   Logical category grouping operations, e.g. "Configuration", "Permission", "Credential".
    /// </param>
    /// <param name="targetResource">
    ///   Identifier of the resource acted upon (GUID string, name, or URL path).
    /// </param>
    /// <param name="responseStatus">
    ///   HTTP status code returned to the caller (200, 201, 400, 500, etc.).
    /// </param>
    /// <param name="configId">
    ///   Optional: GUID of the sprk_specontainertypeconfig record associated with the operation.
    /// </param>
    /// <param name="environmentId">
    ///   Optional: GUID of the sprk_speenvironment record associated with the operation.
    /// </param>
    /// <param name="businessUnitId">
    ///   Optional: GUID of the businessunit record associated with the operation.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task LogOperationAsync(
        string operation,
        string category,
        string targetResource,
        int responseStatus,
        Guid? configId = null,
        Guid? environmentId = null,
        Guid? businessUnitId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var performedBy = ResolvePerformingUser();

            var auditRecord = BuildAuditRecord(
                operation,
                category,
                targetResource,
                responseStatus,
                performedBy,
                configId,
                environmentId,
                businessUnitId);

            var auditId = await _dataverseClient.CreateAsync(AuditLogEntitySet, auditRecord, cancellationToken);

            _logger.LogDebug(
                "Audit log created: {AuditId} | Operation={Operation} Category={Category} Target={Target} Status={Status} User={User}",
                auditId,
                operation,
                category,
                targetResource,
                responseStatus,
                performedBy);
        }
        catch (Exception ex)
        {
            // Audit failures must never propagate to the caller.
            // The primary operation has already completed; a logging failure is non-fatal.
            _logger.LogError(
                ex,
                "Failed to write audit log entry. Operation={Operation} Category={Category} Target={Target} Status={Status}",
                operation,
                category,
                targetResource,
                responseStatus);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Private helpers
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the performing user's identity from the current HTTP context claims.
    /// Falls back to "system" when no authenticated user is present (e.g. background jobs).
    /// </summary>
    private string ResolvePerformingUser()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user == null || !user.Identity?.IsAuthenticated == true)
        {
            _logger.LogDebug("No authenticated user in HttpContext — using 'system' as audit actor");
            return "system";
        }

        // Prefer the UPN (email) claim; fall back to OID then NameIdentifier
        return user.FindFirstValue("preferred_username")
            ?? user.FindFirstValue("upn")
            ?? user.FindFirstValue(ClaimTypes.Upn)
            ?? CallerResolution.ResolveObjectId(user)
            ?? user.Identity?.Name
            ?? "unknown";
    }

    /// <summary>
    /// Builds the anonymous payload for the Dataverse Web API POST.
    /// Lookup fields use the @odata.bind syntax as required by the Dataverse REST API.
    /// </summary>
    private static object BuildAuditRecord(
        string operation,
        string category,
        string targetResource,
        int responseStatus,
        string performedBy,
        Guid? configId,
        Guid? environmentId,
        Guid? businessUnitId)
    {
        var record = new SpeAuditLogPayload
        {
            // sprk_name is NOT NULL in Dataverse and was never populated — on its own that rejected
            // every create. Primary-name column, so it must read well in a Dataverse grid.
            Name = BuildPrimaryName(operation, targetResource),
            Operation = operation,
            Category = MapCategory(category),
            TargetResourceId = targetResource,
            ResponseStatus = responseStatus,
            PerformedBy = performedBy,
            PerformedOn = DateTimeOffset.UtcNow
        };

        // Bind lookup references using OData @odata.bind syntax. The navigation property is the LOOKUP
        // name from the table schema — `sprk_containertypeconfig`, not `sprk_ContainerTypeConfigId`.
        // ADR-044: the GUID in a key predicate is bare-lowercase ("D" format), which Guid.ToString() gives.
        if (configId.HasValue)
        {
            record.ContainerTypeConfigBind = $"/sprk_specontainertypeconfigs({configId.Value:D})";
        }

        if (environmentId.HasValue)
        {
            record.EnvironmentBind = $"/sprk_speenvironments({environmentId.Value:D})";
        }

        if (businessUnitId.HasValue)
        {
            record.BusinessUnitBind = $"/businessunits({businessUnitId.Value:D})";
        }

        return record;
    }

    /// <summary>
    /// Builds the required <c>sprk_name</c> primary-name value.
    /// </summary>
    private static string BuildPrimaryName(string operation, string targetResource)
    {
        var name = string.IsNullOrWhiteSpace(targetResource)
            ? operation
            : $"{operation} — {targetResource}";

        // sprk_name is NVARCHAR(850); truncate rather than let Dataverse reject an over-length value.
        return name.Length <= 850 ? name : name[..850];
    }

    /// <summary>
    /// Maps a caller's free-text category onto the <c>sprk_category</c> option set.
    /// </summary>
    /// <remarks>
    /// <c>sprk_category</c> is a Dataverse CHOICE, not a string. Callers pass free text
    /// ("ContainerTypeRegistration", "Configuration", "RecycleBin", "FileUploaded", …) and only one of
    /// those — "Permission" — happens to match an option name, so every other write was rejected on type
    /// alone. Matching is by prefix because the caller vocabulary is finer-grained than the option set
    /// (e.g. "ContainerCreated"/"ContainerUpdated" both belong to <c>Container</c>).
    /// <para>
    /// Unmapped input falls back to <see cref="CategorySecurity"/> rather than throwing: this runs inside a
    /// best-effort audit path, and losing the row entirely is worse than filing it under a coarse category.
    /// </para>
    /// </remarks>
    internal static int MapCategory(string? category)
    {
        var value = category?.Trim() ?? string.Empty;

        // Order matters: "ContainerType*" must be tested before "Container*".
        if (value.StartsWith("ContainerType", StringComparison.OrdinalIgnoreCase)) return CategoryContainerType;
        if (value.StartsWith("Container", StringComparison.OrdinalIgnoreCase)) return CategoryContainer;
        if (value.StartsWith("Permission", StringComparison.OrdinalIgnoreCase)) return CategoryPermission;
        if (value.StartsWith("File", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("RecycleBin", StringComparison.OrdinalIgnoreCase)) return CategoryFile;
        if (value.StartsWith("Search", StringComparison.OrdinalIgnoreCase)) return CategorySearch;

        return CategorySecurity;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Payload DTO (internal — not exposed from this assembly)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Serialization payload for sprk_speauditlog Dataverse records.
    /// Property names match Dataverse logical attribute names.
    /// Nullable properties are excluded from serialization when null to avoid
    /// sending empty values for optional lookup fields.
    /// </summary>
    private sealed class SpeAuditLogPayload
    {
        /// <summary>Required primary-name column (NVARCHAR 850, NOT NULL).</summary>
        [JsonPropertyName("sprk_name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("sprk_operation")]
        public string Operation { get; set; } = string.Empty;

        /// <summary>Option-set value, NOT a string — see <c>SpeAuditService.MapCategory</c>.</summary>
        [JsonPropertyName("sprk_category")]
        public int Category { get; set; }

        /// <summary>The real column is <c>sprk_targetresourceid</c>; <c>sprk_targetresource</c> never existed.</summary>
        [JsonPropertyName("sprk_targetresourceid")]
        public string TargetResourceId { get; set; } = string.Empty;

        [JsonPropertyName("sprk_responsestatus")]
        public int ResponseStatus { get; set; }

        [JsonPropertyName("sprk_performedby")]
        public string PerformedBy { get; set; } = string.Empty;

        [JsonPropertyName("sprk_performedon")]
        public DateTimeOffset PerformedOn { get; set; }

        [JsonPropertyName("sprk_containertypeconfig@odata.bind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ContainerTypeConfigBind { get; set; }

        [JsonPropertyName("sprk_environment@odata.bind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EnvironmentBind { get; set; }

        [JsonPropertyName("sprk_businessunit@odata.bind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BusinessUnitBind { get; set; }
    }
}
