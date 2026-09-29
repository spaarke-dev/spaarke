using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// #781 item 4b — runtime health probe for the Compose save-identity alternate key
/// (<c>sprk_graphitemid_uk</c> on <c>sprk_document</c>). Reports the key's
/// <c>EntityKeyIndexStatus</c> at startup (log) and on every <c>/healthz/catalog</c> probe.
/// </summary>
/// <remarks>
/// <para>
/// <b>WHY THIS EXISTS, AND WHY IT BECAME NECESSARY RATHER THAN MERELY NICE.</b> The key can only be
/// <c>Active</c> while <c>sprk_graphitemid</c> is unique across the table. On 2026-08-17 dev
/// accumulated 105 duplicated values (417 excess rows), the index sat in <c>Failed</c>, and every
/// Compose save that routed through the FR-07(d) atomic upsert threw — a loud, unmistakable,
/// user-facing 500.
/// </para>
/// <para>
/// The #781 item-2 self-heal (<c>ComposeRecordResolution.TryFindDocumentByGraphItemIdAsync</c>) is
/// what makes this probe load-bearing rather than redundant. That heal resolves a broken key by
/// column query, so saves of EXISTING documents now succeed — which means the condition no longer
/// announces itself. A degraded environment that used to shout now whispers into a warning log, and
/// stays degraded until someone creates a NEW document and fails. Fixing the symptom without
/// restoring the signal would have been a net loss in operability, so the signal is restored here.
/// </para>
/// <para>
/// <b>Relationship to the two scripts.</b> <c>scripts/Verify-ComposeIdentityKey.ps1</c> (#4a) is the
/// DEPLOY-time gate and <c>scripts/Repair-ComposeIdentityKey.ps1</c> (#3) is the repair. Neither runs
/// between deploys, and the duplication that breaks the key accumulates from ordinary writes — the
/// dev incident was data drift, not a bad deploy. This check is the between-deploys watch.
/// </para>
/// <para>
/// <b>DEGRADED, never Unhealthy — deliberately different from its sibling</b>
/// <see cref="Sprk.Bff.Api.Services.Ai.PublicContracts.RoutingConsumerTypeHealthCheck"/>, which
/// reports Unhealthy on catalog drift. The difference is real, not an inconsistency: catalog drift
/// means the platform will misbehave, whereas a broken identity key means the platform is
/// COMPENSATING — existing documents save correctly via the item-2 heal, and only the creation of a
/// new document is affected. Reporting Unhealthy would overstate the blast radius, and a data
/// condition should never be able to fail a deploy gate that exists to catch code/catalog problems.
/// </para>
/// <para>
/// <b>Tagged <c>catalog</c> so it lands on <c>/healthz/catalog</c>, NOT on <c>/healthz</c>.</b> That
/// tag reads as AI-flavoured, but its operational meaning in
/// <c>EndpointMappingExtensions</c> is precisely "diagnostic checks that must not influence App
/// Service liveness" — <c>/healthz</c> filters on <c>!Tags.Contains("catalog")</c>. A degraded
/// identity key must never recycle instances, so the routing is exactly right even though the tag
/// name is inherited. The <c>compose</c> / <c>identity-key</c> tags are for filtering.
/// </para>
/// <para>
/// <b>Never false-fails.</b> No <c>IDataverseService</c>, or a ServiceClient that is not ready (test
/// host, local run without Dataverse, MI propagation lag) ⇒ Healthy-with-description. An unreadable
/// key is reported as unknown, never as broken — this check must not be the thing that cries wolf.
/// </para>
/// <para>
/// <b>ADR-010 / §11</b>: no new interface and no new Dataverse capability. It reuses the existing
/// <see cref="DataverseServiceClientExtensions.TryUnwrapServiceClient"/> best-effort unwrap and the
/// <c>protected virtual</c> fetch seam established by <c>MembershipFieldDiscoveryService</c>, so
/// tests subclass rather than mock a transport (ADR-038 B1). Widening
/// <c>IGenericEntityService</c> for a Compose diagnostic would have broken five hand-written test
/// doubles to serve one caller.
/// </para>
/// <para><b>ADR-015 tier-1 safe</b>: the description carries schema identifiers and an index status
/// only — never GUIDs, user data, or document content.</para>
/// </remarks>
public class ComposeIdentityKeyHealthCheck : IHostedService, IHealthCheck
{
    internal const string DocumentEntityLogicalName = "sprk_document";
    internal const string IdentityKeyLogicalName = "sprk_graphitemid_uk";
    private const string ActiveStatus = "Active";

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ComposeIdentityKeyHealthCheck> _logger;

    public ComposeIdentityKeyHealthCheck(
        IServiceProvider serviceProvider,
        ILogger<ComposeIdentityKeyHealthCheck> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>What one probe concluded. <paramref name="Status"/> is null when the key's status
    /// could not be read at all (which is NOT the same as the key being broken).</summary>
    protected internal sealed record KeyProbe(string? Status, string? SkippedReason)
    {
        // `protected internal` (not `internal`): the fetch seam below is `protected virtual` so tests
        // can subclass it, and a return type may not be less accessible than the method returning it.
        internal bool IsActive => string.Equals(Status, ActiveStatus, StringComparison.OrdinalIgnoreCase);
    }

    // ── IHostedService: the startup surface ───────────────────────────────────────────────────

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            var probe = await ProbeAsync(cancellationToken).ConfigureAwait(false);

            if (probe.SkippedReason is not null)
            {
                _logger.LogInformation(
                    "ComposeIdentityKeyHealthCheck skipped: {Reason}.", probe.SkippedReason);
                return;
            }

            if (probe.IsActive)
            {
                _logger.LogInformation(
                    "ComposeIdentityKeyHealthCheck: {Key} = Active. Compose create-on-save identity is healthy.",
                    IdentityKeyLogicalName);
                return;
            }

            // Warning, not Error: the platform still saves existing documents (the item-2 heal).
            // The message names the remedy because the person reading a startup log at 3am is not
            // going to go and find this issue.
            _logger.LogWarning(
                "ComposeIdentityKeyHealthCheck DEGRADED: {Key} = {Status} (expected Active). " +
                "Creating NEW Compose documents will fail; saves of existing documents are being " +
                "self-healed by column query (#781 item 2) and will succeed. Remedy: run " +
                "scripts/Repair-ComposeIdentityKey.ps1 (report mode first) to dedupe " +
                "sprk_graphitemid and reactivate the key.",
                IdentityKeyLogicalName, probe.Status ?? "unreadable");
        }
        catch (OperationCanceledException)
        {
            throw; // Shutdown during startup.
        }
        catch (Exception ex)
        {
            // Fail-soft: a health probe must never be the reason the host does not start.
            _logger.LogInformation(
                ex,
                "ComposeIdentityKeyHealthCheck skipped due to a transient error (Dataverse unreachable, " +
                "MI propagation lag, or similar). /healthz/catalog reports the status once it can be read.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    // ── IHealthCheck: the between-deploys watch ───────────────────────────────────────────────

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var probe = await ProbeAsync(cancellationToken).ConfigureAwait(false);

            if (probe.SkippedReason is not null)
            {
                return HealthCheckResult.Healthy(
                    $"Compose identity-key check skipped ({probe.SkippedReason}) — an environment without " +
                    "Dataverse is not a broken key.");
            }

            if (probe.IsActive)
            {
                return HealthCheckResult.Healthy(
                    $"{IdentityKeyLogicalName} = Active — Compose create-on-save identity is healthy.");
            }

            if (probe.Status is null)
            {
                // Read the metadata, found no key. Either the solution import is incomplete or the
                // key was deleted. Reported as its own case because "absent" and "Failed" have
                // different remedies, and telling an operator to dedupe a key that does not exist
                // wastes the one thing they are short of.
                return HealthCheckResult.Degraded(
                    $"{IdentityKeyLogicalName} was NOT FOUND on {DocumentEntityLogicalName}. Creating new " +
                    "Compose documents will fail (the FR-07(d) upsert keys on it); existing documents " +
                    "still save via the #781 item-2 self-heal. Remedy: re-import the Compose solution, " +
                    "then run scripts/Verify-ComposeIdentityKey.ps1.");
            }

            return HealthCheckResult.Degraded(
                $"{IdentityKeyLogicalName} = {probe.Status} (expected Active). A unique key cannot build " +
                "over duplicate sprk_graphitemid values. Creating new Compose documents will fail; saves " +
                "of existing documents are self-healed (#781 item 2) and succeed. Remedy: run " +
                "scripts/Repair-ComposeIdentityKey.ps1 in report mode, then with -Apply -ReactivateKey.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A Dataverse blip is not a broken key. Degrade with the reason rather than assert a
            // fault we did not observe.
            return HealthCheckResult.Degraded(
                "Compose identity-key status could not be read (Dataverse unreachable, MI propagation lag, " +
                "or similar transient error). Key status unknown until the next probe.",
                ex);
        }
    }

    // ── Probe core (shared by both surfaces) ──────────────────────────────────────────────────

    private async Task<KeyProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dataverse = scope.ServiceProvider.GetService<IDataverseService>();
        if (dataverse is null)
        {
            return new KeyProbe(null, "IDataverseService not registered (test host or a run without Dataverse)");
        }

        return await FetchKeyStatusAsync(dataverse, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Reads <c>sprk_graphitemid_uk</c>'s <c>EntityKeyIndexStatus</c> from Dataverse entity metadata.
    /// <c>protected virtual</c> so tests subclass it with a canned status instead of standing up a
    /// <c>ServiceClient</c> — the same seam <c>MembershipFieldDiscoveryService</c> established, and
    /// the reason no transport is mocked here (ADR-038 B1).
    /// </summary>
    protected virtual Task<KeyProbe> FetchKeyStatusAsync(
        IDataverseService dataverse,
        CancellationToken cancellationToken)
    {
        var serviceClient = dataverse.TryUnwrapServiceClient(nameof(ComposeIdentityKeyHealthCheck), _logger);
        if (serviceClient is null)
        {
            return Task.FromResult(new KeyProbe(
                null, "Dataverse ServiceClient is not ready (test host, local run, or MI propagation lag)"));
        }

        // EntityFilters.Entity carries the entity's Keys collection. Retrieved as-if-published so a
        // key that has just been reactivated is visible without waiting for a publish.
        var request = new RetrieveEntityRequest
        {
            LogicalName = DocumentEntityLogicalName,
            EntityFilters = EntityFilters.Entity,
            RetrieveAsIfPublished = true,
        };

        var response = (RetrieveEntityResponse)serviceClient.Execute(request);
        var key = response.EntityMetadata?.Keys?
            .FirstOrDefault(k => string.Equals(k.LogicalName, IdentityKeyLogicalName, StringComparison.OrdinalIgnoreCase));

        // A null key means "read the metadata, the key is not there" — reported as absent, which is a
        // real and differently-remedied condition. It is NOT conflated with an unreadable probe: that
        // path returns a SkippedReason instead.
        return Task.FromResult(new KeyProbe(key?.EntityKeyIndexStatus.ToString(), SkippedReason: null));
    }
}
