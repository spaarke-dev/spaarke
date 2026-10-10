using Azure.Core;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Sprk.Bff.Api.Infrastructure.Auth;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The Signal writer's DEDICATED Dataverse connection — authenticates as <c>mi-ontology-writer-dev</c> (task
/// 006) through EXACTLY ONE code path: <see cref="OntologyWriterCredentialFactory"/>'s tenant-pinned
/// <see cref="Azure.Identity.DefaultAzureCredential"/>, locked to the writer's own user-assigned managed identity
/// (every other source excluded; amended 2026-10-07, owner).
/// </summary>
/// <remarks>
/// <para><b>ADR-010 — Path C, comply (task 030 rework F10).</b> The original design wrapped this in a
/// single-implementation interface (<c>IOntologyWriterDataverseClient</c>) purely so DI could distinguish it
/// from the shared sysadmin <c>IGenericEntityService</c> singleton. Deleted: this class is <c>public sealed</c>
/// and registered + injected as the CONCRETE type — no interface of this project's own exists in the DI graph
/// for it. The typed seam tests use instead is <see cref="IOrganizationServiceAsync2"/> — an EXISTING
/// Dataverse-SDK interface <see cref="ServiceClient"/> already implements (also used this way in
/// <c>Services/Dataverse/FetchService.cs</c>), not a new interface this project introduces; ADR-010's
/// single-implementation concern is about THIS project minting throwaway abstractions, not about depending on
/// a third-party one.</para>
/// <para><b>F7 / F13 / F14 — the credential path is not a branch, it is the only code here (code-review
/// finding).</b> Contrast <see cref="Spaarke.Dataverse.DataverseServiceClientImpl"/>, which chooses between a
/// managed-identity branch and a <c>TENANT_ID</c>/<c>API_APP_ID</c> + <c>IConfidentialClientProvider</c> branch
/// based on the GLOBAL <c>Graph:ManagedIdentity:Enabled</c> flag — a flag that describes the SYSADMIN identity's
/// configuration, not the writer's. This class deliberately does NOT wrap
/// <see cref="Spaarke.Dataverse.DataverseServiceClientImpl"/> and contains no such branch: there is no code
/// path by which a future change could route the writer through <c>TENANT_ID</c>/<c>API_APP_ID</c> and
/// authenticate it as <c>SDAP-BFF-SPE-API</c> (the shared BFF application user), because that branch does not
/// exist in this file. <see cref="BuildClient"/> calls <see cref="OntologyWriterCredentialFactory.Create"/>
/// unconditionally; an unset <c>Ontology:Writer:ManagedIdentityClientId</c> throws before a
/// <see cref="ServiceClient"/> is ever constructed — fail closed, never a fallback identity.</para>
/// <para><b>R3 (second independent review, 2026-10-04) — lock-guarded, cache-ON-SUCCESS-only, not
/// <c>Lazy&lt;T&gt;(PublicationOnly)</c>.</b> <c>PublicationOnly</c> lets every concurrently-blocked caller run
/// the (possibly failing, possibly expensive) factory independently and in parallel — a burst of concurrent
/// first-use failures would log/meter the SAME underlying connect failure once PER CONCURRENT CALLER, not once
/// per attempt actually made. <see cref="GetOrganizationService"/> instead double-checks under a plain
/// <c>lock</c>: only ONE thread ever builds at a time; a thread that loses the race for the lock simply WAITS
/// and then reads the (by-then-published) cached value, rather than redundantly repeating the SAME attempt.
/// Nothing is ever cached on failure — a thread that acquires the lock after a prior failure makes its OWN
/// real attempt, which is correctly logged once for that attempt.</para>
/// <para><b>R3 — disposes the underlying connection.</b> <see cref="Dispose"/> disposes the built
/// <see cref="ServiceClient"/> (via its <see cref="IDisposable"/> surface on <see cref="IOrganizationServiceAsync2"/>);
/// registered as a DI singleton (<c>SignalsModule</c>), so the host's <c>ServiceProvider</c> disposes it
/// automatically at shutdown. <see cref="BuildClient"/> also disposes a not-ready <see cref="ServiceClient"/>
/// before throwing, rather than leaking the half-connected instance.</para>
/// </remarks>
public sealed class OntologyWriterDataverseClient : IDisposable
{
    private readonly object _connectLock = new();
    private readonly Func<IOrganizationServiceAsync2> _organizationServiceFactory;
    private readonly ILogger<OntologyWriterDataverseClient> _logger;
    private IOrganizationServiceAsync2? _organizationService;
    private bool _disposed;

    public OntologyWriterDataverseClient(IConfiguration configuration, ILogger<OntologyWriterDataverseClient> logger)
        : this(() => BuildClient(configuration, logger), logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
    }

    /// <summary>
    /// Test seam (task 030 rework F10: "tests construct it with a fake factory"). <c>internal</c>, exposed to
    /// <c>Sprk.Bff.Api.Tests</c> via the project's existing <c>InternalsVisibleTo</c> (the same ADR-010 testing
    /// convention used across this codebase — see e.g. <c>NoAccessListReader.cs</c>,
    /// <c>ExternalParticipationService.cs</c>). The production constructor above is the only other caller.
    /// </summary>
    internal OntologyWriterDataverseClient(Func<IOrganizationServiceAsync2> organizationServiceFactory, ILogger<OntologyWriterDataverseClient> logger)
    {
        ArgumentNullException.ThrowIfNull(organizationServiceFactory);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _organizationServiceFactory = organizationServiceFactory;
    }

    /// <summary>
    /// Returns the cached connection, or builds it — under a lock, so a burst of concurrent first callers
    /// produces ONE real attempt, not N. See the class remarks (R3) for why this replaced
    /// <c>Lazy&lt;T&gt;(PublicationOnly)</c>.
    /// </summary>
    private IOrganizationServiceAsync2 GetOrganizationService()
    {
        // Fast path: already connected. No lock needed to READ a reference that, once published below, never
        // changes again (publish-once-on-success).
        if (_organizationService is { } cached)
        {
            return cached;
        }

        lock (_connectLock)
        {
            if (_organizationService is { } cachedAfterLock)
            {
                return cachedAfterLock;
            }

            ObjectDisposedException.ThrowIf(_disposed, this);

            // Only the thread holding the lock makes this attempt. If it throws, nothing is cached — the
            // NEXT caller (whether it was waiting on this same lock or arrives later) makes its own real
            // attempt, which is correctly logged/metered once for THAT attempt.
            var built = _organizationServiceFactory();
            _organizationService = built;
            return built;
        }
    }

    private static ServiceClient BuildClient(IConfiguration configuration, ILogger logger)
    {
        var dataverseUrl = configuration["Dataverse:ServiceUrl"];
        if (string.IsNullOrWhiteSpace(dataverseUrl))
        {
            throw new InvalidOperationException(
                "Dataverse:ServiceUrl configuration is required for the Signal writer's dedicated connection.");
        }

        // F7/F13/F14: the ONLY credential resolution this class performs. Throws (fail closed) if
        // Ontology:Writer:ManagedIdentityClientId is unset -- see that factory's own remarks.
        //
        // Owner directive (task 030 rework, 2026-10-04): "the writer fails closed by design, so a broken
        // credential or a refused write must not look like 'no conditions found'". A fail-closed throw that
        // nobody can see IS a silent failure by another name, so every refusal here is logged at Error with a
        // stable EventId + a bounded-cardinality `reason` BEFORE it is rethrown -- never swallowed, never
        // downgraded to Warning. No fact values or sentence content ever reach this class, so there is
        // nothing matter-sensitive to redact here.
        TokenCredential credential;
        try
        {
            credential = OntologyWriterCredentialFactory.Create(configuration);
        }
        catch (Exception ex)
        {
            logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                "Signal writer refused: credential unresolvable (reason={Reason}).",
                Telemetry.OntologyWriterFailureReason.CredentialUnresolvable);
            Telemetry.OntologyWriterTelemetry.RecordFailure(Telemetry.OntologyWriterFailureReason.CredentialUnresolvable);
            throw;
        }

        var instanceUri = new Uri(dataverseUrl);
        // Token scope = the Dataverse environment-root authority, matching DataverseServiceClientImpl's and
        // ManagedIdentityCredentialFactory's own derivation (#3b): the ServiceClient token provider is invoked
        // with the full SOAP endpoint URL, which is not a valid AAD resource.
        var scope = instanceUri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/.default";

        logger.LogInformation(
            "Connecting the Signal writer's dedicated ServiceClient (managed identity, scope {Scope}).", scope);

        // R8 (second independent review): the Dataverse Client SDK SWALLOWS a tokenProviderFunction exception
        // internally and surfaces it only via IsReady/LastError -- it does not rethrow out of the ServiceClient
        // constructor. Logging inside the lambda AND at the IsReady check below would therefore double-count
        // ONE underlying failure. This flag is the single source of truth for which happened, so exactly ONE
        // of the two logs below fires per connect attempt (later per-call token refreshes, after this method
        // has already returned, are unaffected -- they log normally inside the lambda each time it runs).
        var tokenAcquisitionFailedDuringConnect = false;

        var client = new ServiceClient(
            instanceUrl: instanceUri,
            tokenProviderFunction: _ =>
            {
                try
                {
                    var token = credential.GetToken(new TokenRequestContext(new[] { scope }), CancellationToken.None);
                    return Task.FromResult(token.Token);
                }
                catch (Exception ex)
                {
                    tokenAcquisitionFailedDuringConnect = true;
                    logger.LogError(OntologyWriterEvents.WriteRefused, ex,
                        "Signal writer refused: token acquisition failed (reason={Reason}).",
                        Telemetry.OntologyWriterFailureReason.TokenAcquisitionFailed);
                    Telemetry.OntologyWriterTelemetry.RecordFailure(Telemetry.OntologyWriterFailureReason.TokenAcquisitionFailed);
                    throw;
                }
            },
            useUniqueInstance: true);

        if (!client.IsReady)
        {
            // R3: dispose the not-ready client before throwing -- it is never returned, so nothing else will.
            client.Dispose();

            if (tokenAcquisitionFailedDuringConnect)
            {
                // Already logged once, above, under TokenAcquisitionFailed -- do not log again (R8).
                throw new InvalidOperationException(
                    $"The Signal writer's dedicated ServiceClient failed to acquire a token during connect: {client.LastError}");
            }

            // R7: a distinct reason from CredentialUnresolvable -- the credential itself resolved fine; the
            // ServiceClient's connect handshake (network, org lookup, etc.) is what failed.
            var connectEx = new InvalidOperationException(
                $"The Signal writer's dedicated ServiceClient failed to connect: {client.LastError}");
            logger.LogError(OntologyWriterEvents.WriteRefused, connectEx,
                "Signal writer refused: ServiceClient connect failed (reason={Reason}).",
                Telemetry.OntologyWriterFailureReason.ConnectFailed);
            Telemetry.OntologyWriterTelemetry.RecordFailure(Telemetry.OntologyWriterFailureReason.ConnectFailed);
            throw connectEx;
        }

        logger.LogInformation(
            "Signal writer ServiceClient connected to {OrgName} ({OrgId}).",
            client.ConnectedOrgFriendlyName, client.ConnectedOrgId);

        return client;
    }

    public Task<Guid> CreateAsync(Entity entity, CancellationToken ct = default) =>
        GetOrganizationService().CreateAsync(entity, ct);

    public Task<Entity> RetrieveAsync(string entityLogicalName, Guid id, string[] columns, CancellationToken ct = default) =>
        GetOrganizationService().RetrieveAsync(entityLogicalName, id, new ColumnSet(columns), ct);

    /// <summary>
    /// Retrieve-by-alternate-key via the SDK's <c>RetrieveRequest</c> targeted at an
    /// <see cref="EntityReference"/> built from <paramref name="alternateKeyValues"/> — the same pattern
    /// <c>DataverseServiceClientImpl.RetrieveByAlternateKeyAsync</c> uses.
    /// </summary>
    public async Task<Entity> RetrieveByAlternateKeyAsync(
        string entityLogicalName, KeyAttributeCollection alternateKeyValues, string[]? columns, CancellationToken ct = default)
    {
        var columnSet = columns is { Length: > 0 } ? new ColumnSet(columns) : new ColumnSet(false);
        var request = new RetrieveRequest
        {
            Target = new EntityReference(entityLogicalName, alternateKeyValues),
            ColumnSet = columnSet,
        };

        var response = (RetrieveResponse)await GetOrganizationService().ExecuteAsync(request, ct).ConfigureAwait(false);
        return response.Entity;
    }

    public Task UpdateAsync(string entityLogicalName, Guid id, Dictionary<string, object> fields, CancellationToken ct = default)
    {
        var entity = new Entity(entityLogicalName, id);
        foreach (var (key, value) in fields)
        {
            entity[key] = value;
        }

        return GetOrganizationService().UpdateAsync(entity, ct);
    }

    /// <summary>R3: disposes the underlying connection (if one was ever built). Safe to call more than once.
    /// Registered as a DI singleton, so the host disposes this automatically at shutdown.</summary>
    public void Dispose()
    {
        lock (_connectLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_organizationService is IDisposable disposableClient)
            {
                _logger.LogDebug("Disposing the Signal writer's dedicated ServiceClient.");
                disposableClient.Dispose();
            }
        }
    }
}
