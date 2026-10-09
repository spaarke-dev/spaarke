// -----------------------------------------------------------------------------
// DataverseRootBusinessUnitReader.cs
//
// unified-access-control-r2 task 165, owner round 35 item 1: H8 stamps the
// container it creates with its owning business unit — the ROOT business
// unit of the customer's Dataverse environment (H5 output
// InterStepState.DataverseEnvUrl; under D-12 every customer has a dedicated
// environment, so its root unit IS the customer). This seam reads that id.
// Moved from Handlers/SpeContainerType/ when that folder was retired by
// customer-provisioning-orchestration-r1 task 214 (batch-4 integration).
//
// SEAM JUSTIFICATION (ADR-010 / CLAUDE.md §11): two implementations from day 1
// (production Web API reader; the H8 handler tests' fake). The same lookup
// exists privately inside H10's DataverseWebApiAppUserCreator, which needs it
// mid-way through its own token-scoped upsert; making H8 depend on H10's app-user
// creator would couple two handlers' collaborators for one GET, so H8 gets this
// one-method reader with the SAME token idiom (DefaultAzureCredential with an
// explicit TenantId — ADR-028 MI-outbound, §4D I5) and the SAME query.
//
// WHICH IDENTITY (task 165 round 41 item 5 — checked in live gate (d)): the
// same DefaultAzureCredential(TenantId) and {env}/.default audience as H5's
// DataverseWebApiHealthProbe. H5 completes only on a Reachable WhoAmI and H8 is
// dispatched only after H5's CompletedPhase, so this identity has just been
// answered by the environment; H10 writes app users with it too. A missing
// security role (WhoAmI does not prove one) surfaces as Resumable
// spe-root-business-unit-unresolved naming the HTTP status — never a silent stall.
//
// TASK 227e: ReadRecordedContainerIdAsync reads the customer's container as the environment records it — the value of
// the environment variable sprk_SharePointEmbeddedContainerId, which H7 writes from H8's container at the end of the
// customer's first run. A later run of the same customer reuses that container instead of creating a second one (H4b
// would otherwise repoint the BFF at an empty container). Same identity and token idiom as the root-unit read. Only an
// environmentvariablevalue counts — a definition's defaultvalue is solution content, not this customer's container.
//
// NOT under test in the CI unit suite (real Dataverse Web API calls) — parity
// with DataverseWebApiAppUserCreator / DataverseWebApiHealthProbe. The handler
// tests substitute a fake IDataverseRootBusinessUnitReader.
// -----------------------------------------------------------------------------

using System.Net.Http.Headers;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <summary>Reads the root business unit of a Dataverse environment (for H8's container stamp).</summary>
public interface IDataverseRootBusinessUnitReader
{
    /// <summary>
    /// The id of the environment's root business unit (the one with no parent), or null when the environment reports
    /// none. THROWS on a read fault, and when the environment reports more than one root (never guess an owner).
    /// </summary>
    Task<Guid?> ReadRootBusinessUnitIdAsync(string environmentUrl, string tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Task 227e: the customer's container as the environment records it — the value of the environment variable
    /// <c>sprk_SharePointEmbeddedContainerId</c> (H7 writes it from H8's container on the customer's first run). Null when
    /// the variable has no value (a first run, or one before the solution defines it). THROWS on a read fault, and when
    /// the variable carries more than one value (never guess the customer's container).
    /// </summary>
    Task<string?> ReadRecordedContainerIdAsync(string environmentUrl, string tenantId, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IDataverseRootBusinessUnitReader"/>
public sealed class DataverseWebApiRootBusinessUnitReader : IDataverseRootBusinessUnitReader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DataverseWebApiRootBusinessUnitReader> _logger;

    public DataverseWebApiRootBusinessUnitReader(
        HttpClient httpClient,
        IOptions<SpeContainerOptions> options,
        ILogger<DataverseWebApiRootBusinessUnitReader> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _httpClient = httpClient;
        _logger = logger;
        // The same per-call ceiling as H8's Graph calls (one small GET) — no separate option for one request.
        _httpClient.Timeout = options.Value.GraphRequestTimeout;
    }

    /// <inheritdoc/>
    public async Task<Guid?> ReadRootBusinessUnitIdAsync(
        string environmentUrl, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // Same query as H10's DataverseWebApiAppUserCreator.FindRootBusinessUnitIdAsync. $top=2 so a second root is
        // detected rather than silently ignored.
        using var doc = await GetAsync(environmentUrl, tenantId,
            "/api/data/v9.2/businessunits?$filter=parentbusinessunitid eq null&$select=businessunitid&$top=2",
            "GET businessunits (root)", cancellationToken).ConfigureAwait(false);
        var rows = doc.RootElement.GetProperty("value");

        if (rows.GetArrayLength() == 0)
        {
            _logger.LogWarning("Dataverse environment {EnvironmentUrl} reports no root business unit.", environmentUrl);
            return null;
        }

        if (rows.GetArrayLength() > 1)
        {
            throw new InvalidOperationException(
                $"Dataverse environment '{environmentUrl}' reports more than one root business unit — refusing to guess the owner.");
        }

        return Guid.TryParse(rows[0].GetProperty("businessunitid").GetString(), out var root) && root != Guid.Empty
            ? root
            : throw new InvalidOperationException("The root business unit's id is not a GUID.");
    }

    /// <inheritdoc/>
    public async Task<string?> ReadRecordedContainerIdAsync(
        string environmentUrl, string tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        // The definition by schema name with its values — the query H7's DataverseWebApiEnvVarValuesWriter uses to write it.
        var filter = Uri.EscapeDataString(
            $"schemaname eq '{EnvVarValues.H7DataverseEnvVarValuesHandler.SharePointEmbeddedContainerIdSchemaName}'");
        using var doc = await GetAsync(environmentUrl, tenantId,
            $"/api/data/v9.2/environmentvariabledefinitions?$filter={filter}" +
            "&$expand=environmentvariablevalues($select=value)&$select=schemaname",
            "GET environmentvariabledefinitions (sprk_SharePointEmbeddedContainerId)", cancellationToken).ConfigureAwait(false);

        var values = new List<string>();
        foreach (var definition in doc.RootElement.GetProperty("value").EnumerateArray())
        {
            if (!definition.TryGetProperty("environmentvariablevalues", out var rows) || rows.ValueKind != JsonValueKind.Array)
            {
                // The expand did not come back: no verdict — never read as "no container" (H8 would create a second one).
                throw new InvalidOperationException(
                    "GET environmentvariabledefinitions returned the definition without its environmentvariablevalues.");
            }
            foreach (var row in rows.EnumerateArray())
            {
                var value = row.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
                if (!string.IsNullOrWhiteSpace(value))
                {
                    values.Add(value.Trim());
                }
            }
        }

        var distinct = values.Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count switch
        {
            0 => null,
            1 => distinct[0],
            _ => throw new InvalidOperationException(
                $"Dataverse environment '{environmentUrl}' records {distinct.Count} different values for " +
                $"sprk_SharePointEmbeddedContainerId ({string.Join(", ", distinct)}) — refusing to guess the customer's container."),
        };
    }

    /// <summary>One Web API GET as the Worker identity (DefaultAzureCredential, explicit tenant — ADR-028, §4D I5).</summary>
    private async Task<JsonDocument> GetAsync(
        string environmentUrl, string tenantId, string pathAndQuery, string what, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var envUri))
        {
            throw new InvalidOperationException($"Dataverse environment URL '{environmentUrl}' is not a valid absolute URI.");
        }

        var scope = $"{new Uri(envUri, "/").ToString().TrimEnd('/')}/.default";
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
            .ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(envUri, pathAndQuery));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{what} failed: {(int)response.StatusCode} {response.StatusCode}.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
