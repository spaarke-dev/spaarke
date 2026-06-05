using System.Diagnostics;
using System.Xml;
using System.Xml.Linq;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse.FetchXml;
using Sprk.Bff.Api.Services.Dataverse.Models;

namespace Sprk.Bff.Api.Services.Dataverse;

/// <summary>
/// Executes the FetchXML passthrough query for <c>POST /api/dataverse/fetch</c> (FR-BFF-04).
/// </summary>
/// <remarks>
/// <para>
/// Per task 010 + spec.md, this service:
/// </para>
/// <list type="bullet">
///   <item><description>Performs NO caching — every request hits Dataverse (real-time, FR-BFF-04).</description></item>
///   <item><description>Targets &lt;500ms p50 roundtrip on a default tenant; achieved by reusing the singleton ServiceClient (already authenticated) and avoiding per-request token negotiation.</description></item>
///   <item><description>Translates Dataverse <see cref="EntityCollection"/> into a JSON-projection-friendly shape (<see cref="IReadOnlyDictionary{TKey,TValue}"/>) so the BFF endpoint can return it via the default <c>System.Text.Json</c> serializer.</description></item>
///   <item><description>Injects the caller-supplied paging cookie into the FetchXML root before execution (Dataverse expects <c>page</c> + <c>paging-cookie</c> attributes on <c>&lt;fetch&gt;</c>).</description></item>
///   <item><description>Throws <see cref="XmlException"/> on malformed FetchXML so the endpoint can return 400 ProblemDetails (NOT 500). The authorization filter typically catches this first; this is defense-in-depth.</description></item>
/// </list>
/// <para>
/// Authorization is enforced by <c>DataverseAuthorizationFilter</c> with
/// <c>EntitySource.FromFetchXmlBody</c> (task 011); the cross-entity privilege check runs
/// before this service executes. By the time <see cref="ExecuteAsync"/> is called, every
/// entity referenced in the FetchXML has been verified.
/// </para>
/// <para>
/// Scoped lifetime — depends on <see cref="IDataverseService"/> which is scoped/singleton
/// depending on host registration; FetchService itself holds no per-request state beyond
/// the parameters it receives.
/// </para>
/// </remarks>
internal sealed class FetchService
{
    private readonly IDataverseService _dataverseService;
    private readonly ILogger<FetchService> _logger;

    public FetchService(
        IDataverseService dataverseService,
        ILogger<FetchService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes the supplied FetchXML against Dataverse and returns the projected rows
    /// plus paging information.
    /// </summary>
    /// <param name="request">The FetchXML payload (validated by the authorization filter).</param>
    /// <param name="ct">Cancellation token from the request.</param>
    /// <returns>Rows, paging cookie, and a flag indicating whether more pages remain.</returns>
    /// <exception cref="FetchXmlParseException">
    /// Thrown when <c>FetchXml</c> is not well-formed XML or the paging cookie cannot be
    /// injected. The endpoint maps this to 400 ProblemDetails
    /// (<c>errorCode=DV_FETCHXML_MALFORMED</c>). Matches the contract used by
    /// <see cref="IFetchXmlEntityExtractor"/> so the endpoint has a single catch path.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the underlying <see cref="IDataverseService"/> is not a
    /// <see cref="DataverseServiceClientImpl"/> (defensive guard — the only supported
    /// implementation for this codebase).
    /// </exception>
    public async Task<FetchResponseDto> ExecuteAsync(FetchRequestDto request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.FetchXml))
        {
            // Defense-in-depth — the endpoint should have rejected this; we treat empty
            // input as malformed so it surfaces as 400 not 500.
            throw new FetchXmlParseException("FetchXML cannot be null, empty, or whitespace.");
        }

        // The privilege checker pattern (UserPrivilegeChecker) shows ServiceClient is
        // reached via DataverseServiceClientImpl.OrganizationService. We follow the same
        // pattern instead of taking a hard dependency on the concrete type at compile time.
        if (_dataverseService is not DataverseServiceClientImpl impl)
        {
            // This is a deployment misconfiguration, not a request error — surface as 500.
            throw new InvalidOperationException(
                $"FetchService requires {nameof(DataverseServiceClientImpl)} but {_dataverseService.GetType().FullName} is registered.");
        }

        var serviceClient = impl.OrganizationService;
        if (serviceClient is null || !serviceClient.IsReady)
        {
            throw new InvalidOperationException(
                "Dataverse ServiceClient is not ready; cannot execute FetchXML.");
        }

        // Inject paging cookie into the FetchXML root if supplied.
        // Dataverse expects <fetch page="N" paging-cookie="…"> for paged retrieval.
        var fetchXmlToExecute = string.IsNullOrEmpty(request.PagingCookie)
            ? request.FetchXml
            : InjectPagingCookie(request.FetchXml, request.PagingCookie);

        var sw = Stopwatch.StartNew();

        // ServiceClient implements IOrganizationServiceAsync2 which gives us a
        // CancellationToken-aware overload of RetrieveMultipleAsync. The FetchExpression
        // type is the SDK-canonical way to pass FetchXML through this API.
        var fetchExpression = new FetchExpression(fetchXmlToExecute);
        var result = await serviceClient.RetrieveMultipleAsync(fetchExpression, ct);

        sw.Stop();

        // Log the request metadata (entity name, row count, elapsed ms). We deliberately
        // DO NOT log the FetchXML body — it may contain sensitive filter values per task
        // 010 §10 implementation note 5.
        _logger.LogInformation(
            "FetchService executed: entity={EntityName}, rows={RowCount}, moreRecords={MoreRecords}, elapsedMs={ElapsedMs}",
            request.EntityName,
            result.Entities.Count,
            result.MoreRecords,
            sw.ElapsedMilliseconds);

        // Project Entity → IReadOnlyDictionary so the response serializer can emit
        // a uniform JSON shape regardless of attribute type. Entity.Attributes is itself
        // an IDictionary<string, object> but exposing it directly leaks SDK types into
        // the wire contract; converting here is cheap (O(rows × attrs)) and keeps the
        // DTO honest.
        var rows = new List<IReadOnlyDictionary<string, object?>>(result.Entities.Count);
        foreach (var entity in result.Entities)
        {
            rows.Add(ProjectEntity(entity));
        }

        return new FetchResponseDto(
            Entities: rows,
            MoreRecords: result.MoreRecords,
            PagingCookie: string.IsNullOrEmpty(result.PagingCookie) ? null : result.PagingCookie);
    }

    /// <summary>
    /// Returns a dictionary view of an <see cref="Entity"/>'s attributes plus its
    /// formatted-value cache (under the synthetic key
    /// <c>"FormattedValues"</c>) and primary id (under the entity logical name's
    /// <c>id</c> attribute, already in <see cref="Entity.Attributes"/>).
    /// </summary>
    /// <remarks>
    /// The default <see cref="System.Text.Json"/> serializer can project SDK types
    /// (<see cref="EntityReference"/>, <see cref="OptionSetValue"/>, <see cref="Money"/>,
    /// <see cref="AliasedValue"/>) via their public properties; we don't flatten further
    /// here so the client can recover the typed values. The client (
    /// <c>BffDataverseClient</c>) is responsible for normalizing this shape to its
    /// preferred type.
    /// </remarks>
    private static IReadOnlyDictionary<string, object?> ProjectEntity(Entity entity)
    {
        // Copy to a new dictionary so we don't return a live reference into the SDK's
        // internal collection (the SDK reuses Entity instances internally).
        var projection = new Dictionary<string, object?>(entity.Attributes.Count + 2, StringComparer.OrdinalIgnoreCase);

        foreach (var attribute in entity.Attributes)
        {
            projection[attribute.Key] = attribute.Value;
        }

        // Include the FormattedValues collection separately — it carries the display
        // strings for OptionSet, EntityReference, DateTime, Money attributes when the
        // FetchXML <fetch> element has the default formatted-value behavior enabled.
        // The client uses these for grid display without re-resolving lookup labels.
        if (entity.FormattedValues is not null && entity.FormattedValues.Count > 0)
        {
            var formatted = new Dictionary<string, string>(entity.FormattedValues.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in entity.FormattedValues)
            {
                formatted[kvp.Key] = kvp.Value;
            }
            projection["@formattedValues"] = formatted;
        }

        // Also expose the entity logical name so the client can disambiguate when an
        // aliased subquery returns mixed entity types.
        projection["@logicalName"] = entity.LogicalName;

        return projection;
    }

    /// <summary>
    /// Mutates the FetchXML root <c>&lt;fetch&gt;</c> to carry the supplied paging cookie
    /// and (if missing) sets <c>page="2"</c> so the cookie is interpreted correctly by
    /// Dataverse. If the FetchXML already specifies a <c>page</c> attribute, that value
    /// is preserved.
    /// </summary>
    /// <exception cref="FetchXmlParseException">If the FetchXML is malformed.</exception>
    private static string InjectPagingCookie(string fetchXml, string pagingCookie)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(fetchXml);
        }
        catch (XmlException ex)
        {
            throw new FetchXmlParseException(
                "FetchXML payload is not well-formed XML.", ex);
        }

        var root = document.Root
            ?? throw new FetchXmlParseException("FetchXML has no root <fetch> element.");

        if (!string.Equals(root.Name.LocalName, "fetch", StringComparison.Ordinal))
        {
            throw new FetchXmlParseException(
                $"FetchXML root element must be <fetch> but was <{root.Name.LocalName}>.");
        }

        root.SetAttributeValue("paging-cookie", pagingCookie);

        // If the caller didn't already specify a page, default to 2 since cookie => paging.
        // Dataverse server-side treats page=1 + cookie as a server error; explicit page=N
        // is the contract.
        if (root.Attribute("page") is null)
        {
            root.SetAttributeValue("page", "2");
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }
}
