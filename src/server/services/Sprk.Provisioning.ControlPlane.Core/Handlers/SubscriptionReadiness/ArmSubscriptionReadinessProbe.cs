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
}
