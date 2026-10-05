// -----------------------------------------------------------------------------
// ArmSubscriptionReadinessProbe.cs
//
// Real ARM-backed impl of ISubscriptionReadinessProbe (task 121, Wave G-2).
// Replaces NullSubscriptionReadinessProbe (task 043 Wave C4 scaffold), which
// returned Passed=true unconditionally with no ARM call — DS-4 §3 classified
// this as the H1 PLACEHOLDER: "POML 043 acceptance criteria required real ARM
// behavior... Criteria were satisfied only via injected test fakes."
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-03: H1
//     acceptance = `az account show` succeeds against target sub; Lighthouse
//     RG scope accessible for CustomerOwned.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1b (Option
//     D hybrid, DS-1b §2): H1 is Class A — pure .NET SDK under
//     DefaultAzureCredential pinned to the L2 UAMI. No ProcessStartInfo /
//     shell-out collaborator.
//   - .claude/adr/ADR-028-spaarke-auth-architecture.md — MI-outbound MUST
//     rule: DefaultAzureCredential only, never account-key credentials.
//   - .claude/adr/ADR-010-di-minimalism.md — probe seam ≥2 impls (this real
//     impl + FakeProbe test doubles in H1SubscriptionReadinessHandlerTests).
//
// TWO ARM CALLS:
//   1. Reachability: ArmClient.GetSubscriptionResource(subscriptionId).GetAsync()
//      — equivalent to `az account show --subscription {id}`.
//   2. Lighthouse delegation (CustomerOwned tenancy branch ONLY — enforced by
//      the H1 handler, not this probe; see H1SubscriptionReadinessHandler §6):
//      ArmClient.GetManagedServicesRegistrationAssignments(subscriptionScope)
//      .GetAllAsync(...) — equivalent to
//      GET /subscriptions/{id}/providers/Microsoft.ManagedServices/
//      registrationAssignments?api-version=2022-10-01. Passed=true iff at
//      least one registrationAssignment exists under the subscription scope.
//
// DOMAIN FAILURE vs INFRASTRUCTURE ERROR (per ISubscriptionReadinessProbe
// contract): a RequestFailedException from either ARM call — subscription
// not found (404), L2 UAMI lacks Reader RBAC (403), or any other ARM-
// rejected request — is caught HERE and translated to Passed=false with a
// rich diagnostic + evidence payload. This is a DOMAIN failure (the existing
// SubscriptionUnreachable / LighthouseDelegationMissing rejection codes
// already say "subscription does not exist, OR the L2 UAMI lacks Reader
// permission, OR ARM is transiently unreachable" — all three map to the same
// operator remediation). Only a genuinely unanticipated exception (a bug in
// this probe, not an ARM-side rejection) propagates to the handler's outer
// catch, which maps it to ProbeInfrastructureError.
//
// CREDENTIAL REUSE (CLAUDE.md §11 Extension answer): this probe does NOT
// register its own TokenCredential / ArmClient DI singleton. It is
// constructed via a factory lambda in Worker/Program.cs that resolves the
// TokenCredential singleton already registered by CosmosModule.AddCosmosModule
// (UAMI-pinned via ManagedIdentity:ClientId) and wraps it in a
// probe-local ArmClient. No second credential chain is built.
// -----------------------------------------------------------------------------

using System.Text.Json;
using Azure;
using Azure.Core;
using Azure.ResourceManager;
using Azure.ResourceManager.ManagedServices;
using Azure.ResourceManager.Resources;
using Azure.ResourceManager.Resources.Models;

namespace Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;

/// <summary>
/// Real ARM-backed <see cref="ISubscriptionReadinessProbe"/> implementation.
/// Constructed with an <see cref="ArmClient"/> so tests can inject one built
/// against a fake <c>HttpClientTransport</c> — the ARM SDK's own request/
/// response marshaling runs unmodified, only the HTTP boundary is faked.
/// </summary>
public sealed class ArmSubscriptionReadinessProbe : ISubscriptionReadinessProbe
{
    /// <summary>
    /// registrationAssignments evidence cap — H1 only needs to prove ≥1
    /// assignment exists; capping avoids materializing an unbounded list for
    /// subscriptions with many Lighthouse delegations from other MSPs.
    /// </summary>
    private const int RegistrationAssignmentEvidenceCap = 10;

    private readonly ArmClient _armClient;
    private readonly ILogger<ArmSubscriptionReadinessProbe> _logger;

    /// <summary>
    /// Constructs the real ARM-backed subscription-readiness probe.
    /// </summary>
    /// <param name="armClient">
    /// ARM client. Production DI (Worker/Program.cs) constructs this from the
    /// shared UAMI-pinned <c>TokenCredential</c> singleton (ADR-028
    /// MI-outbound). Tests construct one against a fake HTTP transport.
    /// </param>
    /// <param name="logger">Structured logger.</param>
    public ArmSubscriptionReadinessProbe(
        ArmClient armClient,
        ILogger<ArmSubscriptionReadinessProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(armClient);
        ArgumentNullException.ThrowIfNull(logger);
        _armClient = armClient;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SubscriptionReadinessCheckResult> CheckSubscriptionReachableAsync(
        string subscriptionId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var resourceId = SubscriptionResource.CreateResourceIdentifier(subscriptionId);
        var subscriptionResource = _armClient.GetSubscriptionResource(resourceId);

        try
        {
            var response = await subscriptionResource.GetAsync(cancellationToken).ConfigureAwait(false);
            var data = response.Value.Data;

            var evidence = JsonSerializer.SerializeToElement(new
            {
                subscriptionId = data.SubscriptionId,
                displayName = data.DisplayName,
                tenantId = data.TenantId?.ToString(),
                state = data.State?.ToString(),
            });

            // §4D I1 strengthening: reachability alone is not enough — a
            // subscription can be ARM-reachable under the L2 UAMI (e.g. via
            // an unrelated cross-tenant grant) while belonging to a DIFFERENT
            // tenant than the run declares. Cross-check the ARM-reported
            // tenantId against the caller-supplied tenantId so a misconfigured
            // run parameter (wrong tenantId for this subscription) fails here
            // instead of silently proceeding into H2a against the wrong
            // tenant context. Guid comparison is case/format-insensitive.
            if (data.TenantId is { } armTenantId
                && Guid.TryParse(tenantId, out var expectedTenantId)
                && armTenantId != expectedTenantId)
            {
                _logger.LogWarning(
                    "ArmSubscriptionReadinessProbe: subscription {SubscriptionId} belongs to tenant " +
                    "{ArmTenantId}, but run declared tenantId={ExpectedTenantId} — treating as unreachable",
                    subscriptionId, armTenantId, tenantId);

                return new SubscriptionReadinessCheckResult(
                    Passed: false,
                    Diagnostic:
                        $"Subscription '{subscriptionId}' is reachable via ARM but belongs to tenant " +
                        $"'{armTenantId}', not the run's declared tenantId '{tenantId}'. Remediation: " +
                        "verify the run's tenantId parameter matches the customer's actual Entra tenant " +
                        "for this subscription (§4D I1 — no hardcoded/mismatched tenant).",
                    Evidence: evidence);
            }

            _logger.LogInformation(
                "ArmSubscriptionReadinessProbe: subscription {SubscriptionId} reachable via ARM " +
                "(state={State})", subscriptionId, data.State);

            return new SubscriptionReadinessCheckResult(
                Passed: true,
                Diagnostic: $"Subscription '{subscriptionId}' reachable via ARM (state={data.State}).",
                Evidence: evidence);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(
                ex,
                "ArmSubscriptionReadinessProbe: subscription {SubscriptionId} NOT reachable via ARM " +
                "(status={Status} errorCode={ErrorCode})",
                subscriptionId, ex.Status, ex.ErrorCode);

            var evidence = JsonSerializer.SerializeToElement(new
            {
                subscriptionId,
                armStatus = ex.Status,
                armErrorCode = ex.ErrorCode,
                armMessage = ex.Message,
            });

            return new SubscriptionReadinessCheckResult(
                Passed: false,
                Diagnostic:
                    $"ARM subscription-show failed for '{subscriptionId}' " +
                    $"(HTTP {ex.Status}, {ex.ErrorCode ?? "no-error-code"}): {ex.Message}. " +
                    "Remediation: verify the subscription id is correct, verify the L2 control-plane " +
                    "UAMI has been granted Reader RBAC on this subscription " +
                    "(scripts/provisioning/Grant-ControlPlaneIdentity.ps1 or a manual `az role " +
                    "assignment create`), and verify the subscription has not been disabled or deleted.",
                Evidence: evidence);
        }
    }

    /// <inheritdoc/>
    public async Task<SubscriptionReadinessCheckResult> CheckLighthouseDelegationAsync(
        string subscriptionId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var scope = new ResourceIdentifier($"/subscriptions/{subscriptionId}");

        try
        {
            var assignmentIds = new List<string>();
            var collection = _armClient.GetManagedServicesRegistrationAssignments(scope);
            var pageable = collection.GetAllAsync(
                expandRegistrationDefinition: false,
                filter: null,
                cancellationToken: cancellationToken);

            await foreach (var assignment in pageable.ConfigureAwait(false))
            {
                assignmentIds.Add(assignment.Id.ToString());
                if (assignmentIds.Count >= RegistrationAssignmentEvidenceCap)
                {
                    break;
                }
            }

            var evidence = JsonSerializer.SerializeToElement(new
            {
                subscriptionId,
                registrationAssignmentCount = assignmentIds.Count,
                registrationAssignmentIds = assignmentIds,
            });

            if (assignmentIds.Count == 0)
            {
                _logger.LogWarning(
                    "ArmSubscriptionReadinessProbe: NO Microsoft.ManagedServices/registrationAssignments " +
                    "found for subscription {SubscriptionId} — CustomerOwned tenancy requires Lighthouse " +
                    "delegation", subscriptionId);

                return new SubscriptionReadinessCheckResult(
                    Passed: false,
                    Diagnostic:
                        $"No Microsoft.ManagedServices/registrationAssignments found under subscription " +
                        $"'{subscriptionId}'. Remediation: the customer's tenant admin must accept " +
                        "Spaarke's Lighthouse delegation offer for this subscription before " +
                        "provisioning can continue.",
                    Evidence: evidence);
            }

            _logger.LogInformation(
                "ArmSubscriptionReadinessProbe: Lighthouse delegation confirmed for subscription " +
                "{SubscriptionId} ({Count} registrationAssignment(s))",
                subscriptionId, assignmentIds.Count);

            return new SubscriptionReadinessCheckResult(
                Passed: true,
                Diagnostic:
                    $"Lighthouse delegation confirmed for subscription '{subscriptionId}' " +
                    $"({assignmentIds.Count} registrationAssignment(s) found).",
                Evidence: evidence);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(
                ex,
                "ArmSubscriptionReadinessProbe: Lighthouse registrationAssignments query failed for " +
                "subscription {SubscriptionId} (status={Status} errorCode={ErrorCode})",
                subscriptionId, ex.Status, ex.ErrorCode);

            var evidence = JsonSerializer.SerializeToElement(new
            {
                subscriptionId,
                armStatus = ex.Status,
                armErrorCode = ex.ErrorCode,
                armMessage = ex.Message,
            });

            return new SubscriptionReadinessCheckResult(
                Passed: false,
                Diagnostic:
                    $"Lighthouse registrationAssignments query failed for subscription '{subscriptionId}' " +
                    $"(HTTP {ex.Status}, {ex.ErrorCode ?? "no-error-code"}): {ex.Message}. Remediation: " +
                    "verify the L2 control-plane UAMI has Reader RBAC on this subscription and that the " +
                    "Microsoft.ManagedServices resource provider is registered.",
                Evidence: evidence);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// HANDLER-04 (Wave 2 pre-dispatch remediation 2026-08-27) — F6 verbatim
    /// absorption. For each provider namespace:
    ///   1. GET providers/{ns} — read observed registrationState.
    ///   2. If already "Registered": record + continue.
    ///   3. Else POST providers/{ns}/register (idempotent server-side).
    ///   4. Poll GET providers/{ns} every <paramref name="pollInterval"/>
    ///      until state flips to "Registered" or the shared deadline
    ///      derived from <paramref name="totalTimeout"/> elapses.
    /// The shared deadline means a slow-registering RP does not steal the
    /// budget from later RPs — they simply share the remaining window.
    /// </remarks>
    public async Task<SubscriptionReadinessCheckResult> RegisterAndPollRequiredProvidersAsync(
        string subscriptionId,
        string tenantId,
        IReadOnlyList<string> requiredProviders,
        TimeSpan pollInterval,
        TimeSpan totalTimeout,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentNullException.ThrowIfNull(requiredProviders);
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(pollInterval));
        if (totalTimeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(totalTimeout));

        if (requiredProviders.Count == 0)
        {
            return new SubscriptionReadinessCheckResult(
                Passed: true,
                Diagnostic: $"No required resource providers configured for subscription '{subscriptionId}' (no-op).",
                Evidence: JsonDocument.Parse("{}").RootElement.Clone());
        }

        var subscriptionResource = _armClient.GetSubscriptionResource(
            SubscriptionResource.CreateResourceIdentifier(subscriptionId));
        var providerCollection = subscriptionResource.GetResourceProviders();

        var deadline = DateTimeOffset.UtcNow.Add(totalTimeout);
        var perProviderOutcome = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var failedProviders = new List<string>();

        foreach (var ns in requiredProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var outcome = await RegisterAndPollOneAsync(
                providerCollection, ns, pollInterval, deadline, cancellationToken).ConfigureAwait(false);
            perProviderOutcome[ns] = outcome;
            if (!string.Equals(outcome, "Registered", StringComparison.OrdinalIgnoreCase))
            {
                failedProviders.Add(ns);
            }
        }

        var evidence = JsonSerializer.SerializeToElement(new
        {
            subscriptionId,
            requiredProviderCount = requiredProviders.Count,
            perProvider = perProviderOutcome,
            totalTimeoutSeconds = (int)totalTimeout.TotalSeconds,
            pollIntervalSeconds = (int)pollInterval.TotalSeconds,
        });

        if (failedProviders.Count == 0)
        {
            return new SubscriptionReadinessCheckResult(
                Passed: true,
                Diagnostic:
                    $"All {requiredProviders.Count} required resource providers are Registered on " +
                    $"subscription '{subscriptionId}'.",
                Evidence: evidence);
        }

        return new SubscriptionReadinessCheckResult(
            Passed: false,
            Diagnostic:
                $"Provider registration did NOT reach 'Registered' within {(int)totalTimeout.TotalSeconds}s for " +
                $"{failedProviders.Count} of {requiredProviders.Count} required providers on subscription " +
                $"'{subscriptionId}': " +
                $"{string.Join(", ", failedProviders.Select(p => $"{p}={perProviderOutcome[p]}"))}. " +
                "Remediation: escalate via `az provider register --namespace <ns>` under an elevated identity " +
                "(the L2 UAMI must have Contributor RBAC on this subscription); investigate ARM if the " +
                "Registering state does not converge server-side. F6 verbatim from the 2026-08-27 pre-dispatch audit.",
            Evidence: evidence);
    }

    /// <summary>
    /// Register-and-poll one provider namespace within the shared deadline
    /// budget. Returns the observed <c>registrationState</c> string
    /// ("Registered" / "Registering" / "NotRegistered" / "PollDeadlineExceeded"
    /// / "ArmError-{status}" ). Never throws on ARM domain rejections; only
    /// propagates <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<string> RegisterAndPollOneAsync(
        ResourceProviderCollection providerCollection,
        string providerNamespace,
        TimeSpan pollInterval,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        // Read current state first — if already Registered, skip the register call entirely.
        try
        {
            var initial = await providerCollection.GetAsync(providerNamespace, expand: null, cancellationToken).ConfigureAwait(false);
            var initialState = initial.Value.Data.RegistrationState ?? "(unknown)";
            if (string.Equals(initialState, "Registered", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "ArmSubscriptionReadinessProbe: provider {Ns} already Registered", providerNamespace);
                return "Registered";
            }

            // POST register — idempotent server-side. Property bag is empty for the RP-side default consent.
            _logger.LogInformation(
                "ArmSubscriptionReadinessProbe: registering provider {Ns} (observed state '{State}')",
                providerNamespace, initialState);
            await initial.Value.RegisterAsync(new ProviderRegistrationContent(), cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogWarning(ex,
                "ArmSubscriptionReadinessProbe: provider {Ns} initial read / register failed " +
                "(status={Status} errorCode={ErrorCode})", providerNamespace, ex.Status, ex.ErrorCode);
            return $"ArmError-{ex.Status}";
        }

        // Poll until Registered or deadline elapses.
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
                var poll = await providerCollection.GetAsync(providerNamespace, expand: null, cancellationToken).ConfigureAwait(false);
                var state = poll.Value.Data.RegistrationState ?? "(unknown)";
                if (string.Equals(state, "Registered", StringComparison.OrdinalIgnoreCase))
                {
                    return "Registered";
                }
            }
            catch (RequestFailedException ex)
            {
                _logger.LogWarning(ex,
                    "ArmSubscriptionReadinessProbe: provider {Ns} poll failed transiently " +
                    "(status={Status} errorCode={ErrorCode}) — will retry until deadline",
                    providerNamespace, ex.Status, ex.ErrorCode);
                // Continue polling — transient ARM faults during registration are common.
            }
        }

        // Final read after deadline so the diagnostic carries the last observed state.
        try
        {
            var final = await providerCollection.GetAsync(providerNamespace, expand: null, cancellationToken).ConfigureAwait(false);
            return final.Value.Data.RegistrationState ?? "PollDeadlineExceeded";
        }
        catch
        {
            return "PollDeadlineExceeded";
        }
    }
}
