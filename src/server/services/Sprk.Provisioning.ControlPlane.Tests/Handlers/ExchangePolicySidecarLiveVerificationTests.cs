// -----------------------------------------------------------------------------
// ExchangePolicySidecarLiveVerificationTests.cs
//
// L2 CONTROL-PLANE end-to-end LIVE verification of ExchangePolicySidecarClient
// (task 161) against a REAL running Exchange policy sidecar container
// (task 114's Listener.ps1). This is task 162's INFRASTRUCTURE deliverable:
// the test that PROVES the C# client can talk to the real container over the
// real HTTP wire, and that the shared-secret rejection path fires
// deterministically. Complements the fake-transport contract suite in
// ExchangePolicySidecarClientContractTests.cs — that file proves the C# client
// serializes/parses correctly against a mock; this file proves the client
// actually reaches a real Listener.ps1 process and observes the real wire
// behaviour.
//
// ENV-GUARD: Skipped by default. Set SIDECAR_LIVE_VERIFY_URL to the base URL
// of a running sidecar (e.g. `http://127.0.0.1:8091/` for a local `docker run`
// against the task 114 image, or an operator-established Kudu SSH tunnel URL
// against the dev L2 Worker's sitecontainer) AND SIDECAR_LIVE_VERIFY_SECRET to
// the shared secret the sidecar is booted with (its own
// SIDECAR_SHARED_SECRET env-var value). Optional overrides:
//   SIDECAR_LIVE_VERIFY_TENANT_ID          (default: all-zero GUID — safe;
//                                           Listener.ps1's validation reaches
//                                           it but the script call further in
//                                           returns Failure without touching
//                                           any real Exchange tenant)
//   SIDECAR_LIVE_VERIFY_POLICY_GROUP_ID    (default: all-zero GUID — same)
//   SIDECAR_LIVE_VERIFY_APP_ID_1           (default: all-zero GUID)
//   SIDECAR_LIVE_VERIFY_APP_ID_2           (default: all-ones GUID)
//
// The default all-zero-GUID payload is intentionally SAFE against a real
// sidecar: Listener.ps1 accepts it (the validation is shape-only — 2 entries),
// forwards to Set-ExchangeApplicationAccessPolicy.ps1, which will either
// fail at Connect-ExchangeOnline (bad tenantId) OR at the get-before-set
// against the empty policy scope — either way the response is a wire Failure
// with a diagnostic, NOT a real Exchange mutation. This means the test's
// AUTH-REJECTION and HEALTH-CHECK checks are always deterministic regardless
// of what tenant/policy the operator points it at, while the ROUND-TRIP check
// merely verifies the client + wire path work end-to-end (a Failure outcome
// from the sidecar is a PASS for that check — it proves the sidecar reached
// the script and returned a structured envelope, which is what the client
// needs to consume).
//
// Operators wanting to exercise a real Exchange mutation should point
// SIDECAR_LIVE_VERIFY_TENANT_ID / _POLICY_GROUP_ID / _APP_ID_* at a safely-
// scoped test tenant + a mail-enabled test group + real BFF app-reg + real
// UAMI GUIDs. That is a live-ceremony operation, NOT this test's default.
//
// NOT a unit test — do NOT add this file to a "unit-tests-only" glob.
// ADR-038 KEEP category: integration / live-verification, env-gated. Same
// posture as CosmosSmokeTests.cs (which uses the same env-guard pattern).
//
// COVERAGE (task 162 acceptance criteria — the checks this file can perform
// from a workstation; the sitecontainer-network-namespace public-reachability
// check + the Kudu container-status check must be performed by the operator
// via the PowerShell script scripts/provisioning/Verify-Sidecar-Live.ps1 —
// they cannot be exercised from a xUnit process in the general case):
//
//   AC-CLIENT-1  ExchangePolicySidecarClient reaches the live sidecar and
//                returns a structured ExchangePolicyApplyOutcome (not a
//                transport exception) with a valid shape.
//   AC-CLIENT-2  A request with the correct X-Sidecar-Auth header is accepted
//                by the sidecar (does NOT produce a 401 → HTTP 401 → terminal
//                Failure with the "sidecar rejected X-Sidecar-Auth" diagnostic).
//   AC-CLIENT-3  A request with a WRONG shared secret is rejected by the
//                sidecar (HTTP 401 → terminal Failure — critical security
//                property; if this test PASSES with a Success outcome the
//                auth path is broken and MUST be escalated per POML
//                <escalation> trigger #2).
//   AC-CLIENT-4  A GET /healthz against the sidecar returns 200 "ok"
//                (proves the container is running + the port is bound
//                inside the private network namespace, from the caller's
//                network vantage — a workstation running `docker run` sees
//                the container's port; the Azure sitecontainer topology
//                verifies a different vantage via the operator PS script).
// -----------------------------------------------------------------------------

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

/// <summary>
/// Env-guarded live-verification tests over a real running Exchange policy
/// sidecar (task 114). Skipped by default; opt in by setting
/// <c>SIDECAR_LIVE_VERIFY_URL</c> + <c>SIDECAR_LIVE_VERIFY_SECRET</c>.
/// </summary>
[Trait("Category", "LiveVerification")]
[Trait("RequiresLiveResource", "Sidecar")]
public sealed class ExchangePolicySidecarLiveVerificationTests
{
    private const string UrlEnvVar = "SIDECAR_LIVE_VERIFY_URL";
    private const string SecretEnvVar = "SIDECAR_LIVE_VERIFY_SECRET";
    private const string TenantIdEnvVar = "SIDECAR_LIVE_VERIFY_TENANT_ID";
    private const string PolicyGroupIdEnvVar = "SIDECAR_LIVE_VERIFY_POLICY_GROUP_ID";
    private const string AppId1EnvVar = "SIDECAR_LIVE_VERIFY_APP_ID_1";
    private const string AppId2EnvVar = "SIDECAR_LIVE_VERIFY_APP_ID_2";

    // Safe defaults — see file header. Reach the sidecar, exercise the wire,
    // never touch a real Exchange tenant unless the operator overrides.
    private const string DefaultTenantId = "00000000-0000-0000-0000-000000000000";
    private const string DefaultPolicyGroupId = "00000000-0000-0000-0000-000000000000";
    private const string DefaultAppId1 = "00000000-0000-0000-0000-000000000000";
    private const string DefaultAppId2 = "ffffffff-ffff-ffff-ffff-ffffffffffff";

    private readonly ITestOutputHelper _output;
    private readonly string? _sidecarUrl;
    private readonly string? _sharedSecret;

    public ExchangePolicySidecarLiveVerificationTests(ITestOutputHelper output)
    {
        _output = output;
        _sidecarUrl = Environment.GetEnvironmentVariable(UrlEnvVar);
        _sharedSecret = Environment.GetEnvironmentVariable(SecretEnvVar);
    }

    // ========== AC-CLIENT-4 HEALTH CHECK ==========

    [Fact]
    public async Task AC_CLIENT_4_Healthz_ReturnsOk_WhenSidecarIsRunning()
    {
        if (string.IsNullOrWhiteSpace(_sidecarUrl))
        {
            return; // env-guarded skip (CI-safe)
        }

        using var http = new HttpClient { BaseAddress = new Uri(_sidecarUrl!) };
        using var response = await http.GetAsync("/healthz", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync();

        _output.WriteLine($"GET {_sidecarUrl}healthz -> HTTP {(int)response.StatusCode}, body='{body}'");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "task 114 Listener.ps1 line 326-334 documents GET /healthz -> 200 'ok' unauthenticated " +
            "(sitecontainer-private network). A non-200 here means the container is not running, " +
            "not bound to the expected port, or the operator pointed SIDECAR_LIVE_VERIFY_URL at the " +
            "wrong host.");
        body.Trim().Should().Be("ok");
    }

    // ========== AC-CLIENT-1 + AC-CLIENT-2 ROUND-TRIP WITH VALID AUTH ==========

    [Fact]
    public async Task AC_CLIENT_1_And_2_RoundTrip_ReturnsStructuredEnvelope_WithValidSharedSecret()
    {
        if (string.IsNullOrWhiteSpace(_sidecarUrl) || string.IsNullOrWhiteSpace(_sharedSecret))
        {
            return; // env-guarded skip (CI-safe)
        }

        var client = NewClient(_sidecarUrl!, _sharedSecret!);
        var request = BuildRequest();

        var outcome = await client.ApplyAsync(request, CancellationToken.None);

        _output.WriteLine($"ApplyAsync -> {outcome.GetType().Name}: {DescribeOutcome(outcome)}");

        // AC-CLIENT-1: outcome MUST be a structured envelope, NOT a transport
        // fault masquerading as a "wire" outcome. Any of the three subtypes
        // proves the client reached the sidecar + parsed a real response body.
        outcome.Should().Match(o =>
            o is ExchangePolicyApplyOutcome.Applied
            || o is ExchangePolicyApplyOutcome.Drift
            || o is ExchangePolicyApplyOutcome.Failure,
            "the client must return one of the three ExchangePolicyApplyOutcome subtypes — any of them " +
            "proves reachability + envelope parse succeeded end-to-end against the live sidecar. A " +
            "transport exception surfacing anywhere else would fail this Should().Match.");

        // AC-CLIENT-2: if the outcome IS a Failure, its diagnostic MUST NOT
        // be the "sidecar rejected X-Sidecar-Auth (HTTP 401" one — that would
        // indicate the shared secret this test sent doesn't match what the
        // sidecar was booted with (operator misconfig, not a code defect).
        if (outcome is ExchangePolicyApplyOutcome.Failure failure)
        {
            failure.Diagnostic.Should().NotContain("HTTP 401",
                "AC-CLIENT-2: the shared secret the test sent (from SIDECAR_LIVE_VERIFY_SECRET) must " +
                "match what the sidecar was booted with (its SIDECAR_SHARED_SECRET env var value). " +
                "A 401 here means those two values disagree — check the operator setup. " +
                $"Full diagnostic: {failure.Diagnostic}");
        }
    }

    // ========== AC-CLIENT-3 AUTH REJECTION (SECURITY-CRITICAL) ==========

    [Fact]
    public async Task AC_CLIENT_3_WrongSharedSecret_IsRejected_By_Sidecar()
    {
        if (string.IsNullOrWhiteSpace(_sidecarUrl) || string.IsNullOrWhiteSpace(_sharedSecret))
        {
            return; // env-guarded skip (CI-safe)
        }

        // Deliberately wrong secret. Listener.ps1 line 346's constant-time
        // Test-SecretEqual returns false; the request MUST get an HTTP 401.
        var wrongSecret = "definitely-not-the-real-secret-" + Guid.NewGuid().ToString("N");
        var client = NewClient(_sidecarUrl!, wrongSecret);
        var request = BuildRequest();

        var outcome = await client.ApplyAsync(request, CancellationToken.None);

        _output.WriteLine($"ApplyAsync (wrong secret) -> {outcome.GetType().Name}: {DescribeOutcome(outcome)}");

        // SECURITY-CRITICAL — if this Should().BeOfType fails with an Applied
        // outcome, the sidecar's shared-secret rejection path is broken and
        // the entire H14a Exchange-admin-capability quarantine posture is
        // compromised. Escalate per POML <escalation> trigger #2.
        outcome.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>(
            "SECURITY-CRITICAL: a request with a WRONG X-Sidecar-Auth header MUST be rejected by " +
            "the sidecar (Listener.ps1 line 346-352: 401 with 'Missing or invalid X-Sidecar-Auth " +
            "header'). If this assertion fails with an Applied outcome, the shared-secret check has " +
            "regressed and the sidecar is accepting unauthenticated requests — escalate per " +
            "POML <escalation> trigger #2 (root CLAUDE.md §6 security-sensitive verification).");

        var failure = (ExchangePolicyApplyOutcome.Failure)outcome;
        failure.Diagnostic.Should().Contain("HTTP 401",
            "the client's HTTP 401 handler must fire and produce a diagnostic naming HTTP 401 — " +
            "this is what H14a's log stream shows when an operator investigates a secret-rotation " +
            "drift. If the diagnostic doesn't say HTTP 401, either the sidecar returned a " +
            "different status (unexpected) or the client's status-code branching regressed.");
    }

    // ========== AC-CLIENT-3b MISSING SHARED SECRET (defense-in-depth) ==========

    [Fact]
    public async Task AC_CLIENT_3b_EmptySharedSecretConfig_ShortCircuits_Before_Http()
    {
        if (string.IsNullOrWhiteSpace(_sidecarUrl))
        {
            return; // env-guarded skip (CI-safe)
        }

        // Empty vault name simulates a boot-time misconfiguration where the
        // Worker's IntegrationWiring:SidecarSharedSecret* app-settings did not
        // resolve. The client MUST short-circuit BEFORE issuing an HTTP call —
        // proven by the fact that the FakeReader below is never invoked.
        var options = NewOptions(_sidecarUrl!);
        options.SidecarSharedSecretVaultName = string.Empty;
        options.SidecarSharedSecretSubscriptionId = string.Empty;
        options.SidecarSharedSecretName = string.Empty;

        var neverInvokedReader = new NeverInvokedKvReader();
        var http = new HttpClient { BaseAddress = new Uri(_sidecarUrl!) };
        var client = new ExchangePolicySidecarClient(
            http, neverInvokedReader, Options.Create(options),
            NullLogger<ExchangePolicySidecarClient>.Instance);

        var outcome = await client.ApplyAsync(BuildRequest(), CancellationToken.None);

        _output.WriteLine($"ApplyAsync (empty config) -> {outcome.GetType().Name}: {DescribeOutcome(outcome)}");

        outcome.Should().BeOfType<ExchangePolicyApplyOutcome.Failure>();
        var failure = (ExchangePolicyApplyOutcome.Failure)outcome;
        failure.Diagnostic.Should().Contain("Sidecar shared secret config missing",
            "the LOUD-FAIL discipline in ExchangePolicySidecarClient.ResolveSharedSecretAsync must " +
            "produce this exact diagnostic when the vault/subscription/name triple is empty — " +
            "never proceed with an empty X-Sidecar-Auth header, and never issue the HTTP call at all.");
        neverInvokedReader.CallCount.Should().Be(0,
            "empty config MUST short-circuit BEFORE the KV read is attempted (defense-in-depth: an " +
            "empty vault name reaching the KV reader would surface as a different, more confusing " +
            "diagnostic; the client's own guard catches it first).");
    }

    // ========== helpers ==========

    private static ExchangePolicyApplyRequest BuildRequest()
    {
        var tenantId = Environment.GetEnvironmentVariable(TenantIdEnvVar) ?? DefaultTenantId;
        var policyGroupId = Environment.GetEnvironmentVariable(PolicyGroupIdEnvVar) ?? DefaultPolicyGroupId;
        var appId1 = Environment.GetEnvironmentVariable(AppId1EnvVar) ?? DefaultAppId1;
        var appId2 = Environment.GetEnvironmentVariable(AppId2EnvVar) ?? DefaultAppId2;
        return new ExchangePolicyApplyRequest(
            tenantId,
            new[] { appId1, appId2 },
            policyGroupId,
            DescriptionPrefix: "Spaarke-Provisioning-AppAccessPolicy-LiveVerify",
            CorrelationId: $"live-verify-{Guid.NewGuid():N}");
    }

    private static IntegrationWiringOptions NewOptions(string baseUrl) => new()
    {
        SidecarBaseUrl = baseUrl,
        SidecarRequestTimeout = TimeSpan.FromMinutes(6),
        SidecarTransientRetryDelay = TimeSpan.FromSeconds(1),
        SidecarSharedSecretVaultName = "live-verify-vault",
        SidecarSharedSecretSubscriptionId = "live-verify-subscription",
        SidecarSharedSecretName = "Sidecar-Shared-Secret",
    };

    private static ExchangePolicySidecarClient NewClient(string baseUrl, string sharedSecretValue)
    {
        var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
        var reader = new CannedKvSecretReader(new KvSecretReadResult.Success(sharedSecretValue));
        return new ExchangePolicySidecarClient(
            http, reader,
            Options.Create(NewOptions(baseUrl)),
            NullLogger<ExchangePolicySidecarClient>.Instance);
    }

    private static string DescribeOutcome(ExchangePolicyApplyOutcome outcome) => outcome switch
    {
        ExchangePolicyApplyOutcome.Applied a => $"Applied(CreatedCount={a.CreatedCount}, Observed=[{string.Join(",", a.ObservedAppIds)}])",
        ExchangePolicyApplyOutcome.Drift d => $"Drift(Expected=[{string.Join(",", d.ExpectedAppIds)}], Observed=[{string.Join(",", d.ObservedAppIds)}])",
        ExchangePolicyApplyOutcome.Failure f => $"Failure({f.Diagnostic})",
        _ => outcome.ToString() ?? "(null)",
    };

    private sealed class CannedKvSecretReader : IKvSecretReader
    {
        private readonly KvSecretReadResult _result;
        public CannedKvSecretReader(KvSecretReadResult result) { _result = result; }
        public Task<KvSecretReadResult> ReadSecretAsync(string vaultName, string subscriptionId, string secretName, CancellationToken cancellationToken)
            => Task.FromResult(_result);
    }

    private sealed class NeverInvokedKvReader : IKvSecretReader
    {
        public int CallCount { get; private set; }
        public Task<KvSecretReadResult> ReadSecretAsync(string vaultName, string subscriptionId, string secretName, CancellationToken cancellationToken)
        {
            CallCount++;
            throw new InvalidOperationException(
                "NeverInvokedKvReader was called — the client did NOT short-circuit before the KV " +
                "read. Test assertion above will catch this via CallCount check.");
        }
    }
}
