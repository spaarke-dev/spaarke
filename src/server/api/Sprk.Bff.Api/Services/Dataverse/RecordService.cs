using System.ServiceModel;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Service for fetching a single Dataverse record with caller-supplied field projection.
/// Backs <c>GET /api/dataverse/record/{entityLogicalName}/{id}</c> (FR-BFF-05).
/// </summary>
/// <remarks>
/// <para>
/// Per FR-BFF-05 (Spaarke DataGrid Framework R1): record reads are real-time — NO caching.
/// Each request hits Dataverse directly. The endpoint exists for chip "current value" lookups
/// and host extension code that needs to fetch one record's projected fields on demand.
/// </para>
/// <para>
/// Authorization is handled by <c>DataverseAuthorizationFilter</c> (ADR-008) before the handler
/// is invoked; this service does NOT enforce privilege checks. Row-level access is enforced
/// server-side by Dataverse via the impersonated <c>CallerId</c> path on the underlying
/// <see cref="IDataverseService"/> ServiceClient — a row the caller cannot see surfaces as
/// <see cref="FaultException"/> which we translate to <see cref="RecordNotFoundException"/>.
/// </para>
/// </remarks>
internal sealed class RecordService
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<RecordService> _logger;

    public RecordService(
        IDataverseService dataverseService,
        ILogger<RecordService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves a single record's projected fields.
    /// </summary>
    /// <param name="entityLogicalName">Logical name of the Dataverse entity (e.g., <c>sprk_matter</c>).</param>
    /// <param name="id">Record id.</param>
    /// <param name="selectFields">
    /// Field names to project. When <c>null</c> or empty, the service resolves the entity's
    /// primary id + primary name attributes from metadata and projects only those.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A read-only attribute-keyed dictionary of the record's projected fields.</returns>
    /// <exception cref="RecordNotFoundException">
    /// Thrown when the record does not exist OR the caller cannot read the row.
    /// Caller maps to 404 ProblemDetails.
    /// </exception>
    public async Task<IReadOnlyDictionary<string, object?>> GetRecordAsync(
        string entityLogicalName,
        Guid id,
        string[]? selectFields,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            throw new ArgumentException("Entity logical name must be provided.", nameof(entityLogicalName));
        }

        var columns = (selectFields is { Length: > 0 })
            ? selectFields
            : await ResolveDefaultColumnsAsync(entityLogicalName, ct);

        try
        {
            var entity = await _dataverseService.RetrieveAsync(entityLogicalName, id, columns, ct);

            if (entity is null)
            {
                throw new RecordNotFoundException(entityLogicalName, id);
            }

            _logger.LogDebug(
                "RecordService: retrieved {EntityType} {RecordId} with {ColumnCount} columns",
                entityLogicalName, id, columns.Length);

            return ProjectEntityToDictionary(entity);
        }
        catch (RecordNotFoundException)
        {
            throw;
        }
        catch (FaultException ex) when (IsRecordNotFound(ex.Message))
        {
            _logger.LogInformation(
                "RecordService: {EntityType} {RecordId} not found in Dataverse (FaultException)",
                entityLogicalName, id);
            throw new RecordNotFoundException(entityLogicalName, id, ex);
        }
        catch (Exception ex) when (IsRecordNotFound(ex.Message))
        {
            // Some Dataverse SDK code paths surface not-found as a non-typed Exception.
            _logger.LogInformation(
                "RecordService: {EntityType} {RecordId} not found in Dataverse: {Error}",
                entityLogicalName, id, ex.Message);
            throw new RecordNotFoundException(entityLogicalName, id, ex);
        }
    }

    /// <summary>
    /// Resolves the entity's primary id + primary name attributes from Dataverse metadata.
    /// Used when the caller does not pass <c>$select</c> — the minimal default projection
    /// matches what the framework's chip "current value" lookups need.
    /// </summary>
    private async Task<string[]> ResolveDefaultColumnsAsync(string entityLogicalName, CancellationToken ct)
    {
        var serviceClient = GetServiceClientOrThrow();

        var request = new RetrieveEntityRequest
        {
            LogicalName = entityLogicalName,
            EntityFilters = EntityFilters.Entity
        };

        try
        {
            var response = (RetrieveEntityResponse)await Task.Run(
                () => serviceClient.Execute(request), ct);

            var metadata = response.EntityMetadata;
            var primaryId = metadata.PrimaryIdAttribute;
            var primaryName = metadata.PrimaryNameAttribute;

            // PrimaryNameAttribute is null for some entities (e.g., relationship rows). Filter nulls.
            var columns = new List<string>(2);
            if (!string.IsNullOrWhiteSpace(primaryId)) columns.Add(primaryId);
            if (!string.IsNullOrWhiteSpace(primaryName) && !string.Equals(primaryName, primaryId, StringComparison.OrdinalIgnoreCase))
            {
                columns.Add(primaryName);
            }

            if (columns.Count == 0)
            {
                _logger.LogWarning(
                    "RecordService: metadata for {EntityType} returned no PrimaryIdAttribute or PrimaryNameAttribute; falling back to full column projection",
                    entityLogicalName);
                // Defensive: use a wildcard sentinel ColumnSet via a 1-element placeholder.
                // The Spaarke.Dataverse RetrieveAsync builds new ColumnSet(columns) so we
                // cannot pass true; an empty array would also produce no columns. The safest
                // recovery is to log + throw an InvalidOperationException so the endpoint
                // surfaces a 500 rather than silently fetching nothing.
                throw new InvalidOperationException(
                    $"Cannot resolve a default column projection for entity '{entityLogicalName}': metadata exposes neither PrimaryIdAttribute nor PrimaryNameAttribute.");
            }

            return columns.ToArray();
        }
        catch (Exception ex) when (IsEntityNotFound(ex.Message))
        {
            _logger.LogWarning(
                "RecordService: entity '{EntityType}' not found in Dataverse metadata (default-column resolution)",
                entityLogicalName);
            throw new RecordNotFoundException(entityLogicalName, Guid.Empty, ex);
        }
    }

    /// <summary>
    /// Projects an <see cref="Entity"/> into a dictionary suitable for JSON serialisation.
    /// Unwraps the common boxed Dataverse value types (<see cref="EntityReference"/>, <see cref="OptionSetValue"/>,
    /// <see cref="Money"/>, <see cref="AliasedValue"/>) so the JSON consumer sees primitives rather than
    /// nested objects with SDK-internal properties.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ProjectEntityToDictionary(Entity entity)
    {
        var dict = new Dictionary<string, object?>(entity.Attributes.Count + 1, StringComparer.OrdinalIgnoreCase);

        // Always include the record id (the SDK does not put it in Attributes by name).
        dict["id"] = entity.Id;

        foreach (var kvp in entity.Attributes)
        {
            dict[kvp.Key] = UnwrapAttributeValue(kvp.Value);
        }

        return dict;
    }

    private static object? UnwrapAttributeValue(object? value) => value switch
    {
        null => null,
        EntityReference er => new Dictionary<string, object?>(3, StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = er.Id,
            ["logicalName"] = er.LogicalName,
            ["name"] = er.Name
        },
        OptionSetValue osv => osv.Value,
        Money money => money.Value,
        AliasedValue alias => UnwrapAttributeValue(alias.Value),
        _ => value
    };

    /// <summary>
    /// Casts the composite <see cref="IDataverseService"/> to the concrete
    /// <see cref="DataverseServiceClientImpl"/> to access the underlying <see cref="ServiceClient"/>.
    /// Matches the pattern established by <c>UserPrivilegeChecker</c> (task 011).
    /// </summary>
    private ServiceClient GetServiceClientOrThrow()
    {
        if (_dataverseService is not DataverseServiceClientImpl impl)
        {
            throw new InvalidOperationException(
                $"RecordService requires IDataverseService to be DataverseServiceClientImpl (actual: {_dataverseService.GetType().FullName}); cannot access ServiceClient for metadata lookup.");
        }

        var serviceClient = impl.OrganizationService;
        if (serviceClient is null || !serviceClient.IsReady)
        {
            throw new InvalidOperationException("Dataverse ServiceClient is not ready for metadata lookup.");
        }

        return serviceClient;
    }

    /// <summary>
    /// Detects "record does not exist" error patterns surfaced by the Dataverse SDK.
    /// </summary>
    private static bool IsRecordNotFound(string? message) =>
        !string.IsNullOrEmpty(message) &&
        (message.Contains("0x80040217", StringComparison.OrdinalIgnoreCase) ||   // ObjectDoesNotExist
         message.Contains("Does Not Exist", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("With Id =", StringComparison.OrdinalIgnoreCase) ||    // "{Entity} With Id = {Guid} Does Not Exist"
         (message.Contains("does not exist", StringComparison.OrdinalIgnoreCase) &&
          !message.Contains("attribute", StringComparison.OrdinalIgnoreCase)));

    /// <summary>
    /// Detects "entity logical name not found in metadata" patterns.
    /// </summary>
    private static bool IsEntityNotFound(string? message) =>
        !string.IsNullOrEmpty(message) &&
        (message.Contains("Could not find", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Thrown by <see cref="RecordService"/> when the requested record does not exist OR the caller
/// cannot read it. Endpoint maps to 404 ProblemDetails per ADR-019.
/// </summary>
/// <remarks>
/// Carrying the entity logical name + id lets the endpoint construct a stable ProblemDetails payload
/// without re-parsing the route values.
/// </remarks>
public sealed class RecordNotFoundException : Exception
{
    public string EntityLogicalName { get; }
    public Guid RecordId { get; }

    public RecordNotFoundException(string entityLogicalName, Guid recordId)
        : base(BuildMessage(entityLogicalName, recordId))
    {
        EntityLogicalName = entityLogicalName;
        RecordId = recordId;
    }

    public RecordNotFoundException(string entityLogicalName, Guid recordId, Exception inner)
        : base(BuildMessage(entityLogicalName, recordId), inner)
    {
        EntityLogicalName = entityLogicalName;
        RecordId = recordId;
    }

    private static string BuildMessage(string entityLogicalName, Guid recordId) =>
        recordId == Guid.Empty
            ? $"Entity '{entityLogicalName}' was not found in Dataverse."
            : $"Record '{entityLogicalName}' with id '{recordId:D}' was not found in Dataverse.";
}
