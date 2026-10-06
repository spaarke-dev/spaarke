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

        if (!Uri.TryCreate(environmentUrl, UriKind.Absolute, out var envUri))
        {
            throw new InvalidOperationException($"Dataverse environment URL '{environmentUrl}' is not a valid absolute URI.");
        }

        var scope = $"{new Uri(envUri, "/").ToString().TrimEnd('/')}/.default";
        var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId });
        var token = await credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), cancellationToken)
            .ConfigureAwait(false);

        // Same query as H10's DataverseWebApiAppUserCreator.FindRootBusinessUnitIdAsync. $top=2 so a second root is
        // detected rather than silently ignored.
        var uri = new Uri(envUri, "/api/data/v9.2/businessunits?$filter=parentbusinessunitid eq null&$select=businessunitid&$top=2");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GET businessunits (root) failed: {(int)response.StatusCode} {response.StatusCode}.");
        }

        await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
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
}
