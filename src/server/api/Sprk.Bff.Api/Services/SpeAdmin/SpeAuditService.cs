using System.Security.Claims;
using System.Text.Json.Serialization;
using Spaarke.Dataverse;

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
            ?? user.FindFirstValue("oid")
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier)
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
            Operation = operation,
            Category = category,
            TargetResource = targetResource,
            ResponseStatus = responseStatus,
            PerformedBy = performedBy,
            PerformedOn = DateTimeOffset.UtcNow
        };

        // Bind lookup references using OData @odata.bind syntax.
        // The Dataverse REST API requires this format for navigation properties:
        //   "sprk_ContainerTypeConfigId@odata.bind": "/sprk_specontainertypeconfigs(guid)"
        if (configId.HasValue)
        {
            record.ContainerTypeConfigBind = $"/sprk_specontainertypeconfigs({configId.Value})";
        }

        if (environmentId.HasValue)
        {
            record.EnvironmentBind = $"/sprk_speenvironments({environmentId.Value})";
        }

        if (businessUnitId.HasValue)
        {
            record.BusinessUnitBind = $"/businessunits({businessUnitId.Value})";
        }

        return record;
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
        [JsonPropertyName("sprk_operation")]
        public string Operation { get; set; } = string.Empty;

        [JsonPropertyName("sprk_category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("sprk_targetresource")]
        public string TargetResource { get; set; } = string.Empty;

        [JsonPropertyName("sprk_responsestatus")]
        public int ResponseStatus { get; set; }

        [JsonPropertyName("sprk_performedby")]
        public string PerformedBy { get; set; } = string.Empty;

        [JsonPropertyName("sprk_performedon")]
        public DateTimeOffset PerformedOn { get; set; }

        [JsonPropertyName("sprk_ContainerTypeConfigId@odata.bind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ContainerTypeConfigBind { get; set; }

        [JsonPropertyName("sprk_EnvironmentId@odata.bind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? EnvironmentBind { get; set; }

        [JsonPropertyName("businessunitid@odata.bind")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? BusinessUnitBind { get; set; }
    }
}
