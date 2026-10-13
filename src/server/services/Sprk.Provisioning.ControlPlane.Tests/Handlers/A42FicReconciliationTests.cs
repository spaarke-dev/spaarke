// -----------------------------------------------------------------------------
// A42FicReconciliationTests.cs
//
// Task 205b (row A42, FR-C4) — parity tests pinning the C# FIC estate
// (GraphAppRegistrationProvisioner + H3EntraAppRegHandler) to the master
// `Register-EntraAppRegistrations.ps1 -FicOnly` contract, per the written
// parity contract at projects/customer-provisioning-orchestration-r1/notes/
// decisions/205b-a42-fic-parity-contract.md (path (b), owner Q5 disposition
// 2026-08-25).
//
// ADR-038 CATEGORY: Path #1 — pure C# unit tests. NO live Graph / Entra /
// Azure. The provisioner's Graph-calling bodies stay un-unit-tested (project
// precedent, see GraphAppRegistrationProvisioner.cs header); A42 extracted
// the CONTRACT-bearing decision logic into pure internal statics
// (AssertFicTenancy / ResolveUamiTenantId / FindEquivalentByTriple) and a
// pure classifier (FicExchangeOutcomeClassifier) precisely so the parity
// semantics are testable without live Entra. Handler-level tests use local
// fakes (same idiom as H3EntraAppRegHandlerTests; that task-130 file is
// deliberately NOT modified — its I6 assertions are the regression baseline
// this suite must leave green).
//
// COVERAGE MAP (POML step 8 criteria a-g):
//   (a) A42a_* — idempotency keyed by the (issuer, subject, audience) TRIPLE;
//       a name-collision-only fixture MUST NOT satisfy it (SF-7).
//   (b) A42b_* — AADSTS70025/70021 propagation retry classified by EXACT
//       numeric error_codes match; 700211/700213 fail fast (SF-6, C1).
//   (c) A42c_* — script exit-2 equivalent marks
//       InterStepState.FicPendingPostAppServiceVerification=true; never
//       terminal exchange-verified success (SF-8).
//   (d) A42d_* — cross-tenant refusal via CrossTenantFicRefusedException +
//       distinct rejection code (SF-5; Assert-SpaarkeFicTenancy port).
//   (e) A42e_* — Model 1 NEVER invokes the FIC-creating provisioner even with
//       Model-2-shaped parameters present (I6 regression guard; task-130's
//       AcM1_1/AcI6_1/AcI6_2 ProvisionCallCount==0 assertions preserved).
//   (f) A42f_* — auth-v4 §11 invariant 1: wrong subject (clientId instead of
//       principalId) surfaces as AADSTS700213 and is named as a wrong-subject
//       credential fault, not retried.
//   (g) A42g_* — auth-v4 §11 invariant 2: fresh-FIC propagation flap (8
//       failures, ~130s-order window) retried to acceptance within the 600s
//       budget, on a virtual clock (no real sleeps).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class A42FicReconciliationTests
{
    private const string CustomerId = "acme";
    private const string RunId = "01j7q3zp-a42-run";
    private const string SpaarkeTenantId = "11111111-1111-1111-1111-111111111111";
    private const string CustomerTenantId = "22222222-2222-2222-2222-222222222222";
    private const string UamiPrincipalId = "ffffffff-1111-2222-3333-000000000000";
    private const string UamiClientId = "eeeeeeee-9999-8888-7777-000000000000";
    private const string Audience = "api://AzureADTokenExchange";
    private const string CanonicalFicName = "spaarke-uami-trust";
    private const string KeyVaultName = "sprk-acme-prod-kv";
    private const string BffAppRegId = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";

    private static readonly string Issuer =
        $"https://login.microsoftonline.com/{SpaarkeTenantId}/v2.0";

    // =====================================================================
    // (a) Idempotency by (issuer, subject, audience) triple — SF-7
    // =====================================================================

    // Deliberately not shaped like a real MI name — the FR-B4 CredentialGuardTests scan on
    // src/server/** substring-matches "mi-bff-api-dev" and "spaarke-bff-identity" to catch runtime
    // NAME-based MI resolution (decoy risk per notes/decisions/023-identity-conflation.md §2). This
    // test project happens to live under src/server/services/Sprk.Provisioning.ControlPlane.Tests/
    // (non-standard placement — most test projects live under tests/), so it falls inside the guard's
    // scope. The semantic here is "any name that is NOT the canonical FIC name" — the literal value
    // is arbitrary — so a non-MI-shaped fixture constant serves the test without triggering the
    // guard on a value that isn't a real runtime identity binding. See task 220 (2026-09-28) notes.
    private const string NonCanonicalFicName = "differently-named-fic-fixture";

    [Fact]
    public void A42a_FindEquivalentByTriple_MatchingTripleUnderDifferentName_IsSatisfied()
    {
        var differentlyNamed = Fic(NonCanonicalFicName, Issuer, UamiPrincipalId, Audience);

        var result = GraphAppRegistrationProvisioner.FindEquivalentByTriple(
            new[] { differentlyNamed }, Issuer, UamiPrincipalId, Audience);

        result.Should().BeSameAs(differentlyNamed,
            "the FIC name is a label — Entra matches assertions against the triple, so an " +
            "equivalent triple under ANY name already satisfies the request (SF-7)");
    }

    [Fact]
    public void A42a_FindEquivalentByTriple_NameCollisionOnly_IsNotSatisfied()
    {
        // The canonical name is present, but the subject is a DIFFERENT
        // principal — a name-only idempotency check would wrongly report
        // "already satisfied". This fixture MUST fail the equivalence test,
        // proving the key is the triple, not the name.
        var nameOnlyCollision = Fic(CanonicalFicName, Issuer, "99999999-0000-0000-0000-999999999999", Audience);

        var result = GraphAppRegistrationProvisioner.FindEquivalentByTriple(
            new[] { nameOnlyCollision }, Issuer, UamiPrincipalId, Audience);

        result.Should().BeNull(
            "a FIC that merely shares the canonical NAME but carries a different triple does NOT " +
            "satisfy the request — name-only idempotency is the SF-7 defect");
    }

    [Fact]
    public void A42a_FindEquivalentByTriple_SubjectIsClientIdNotPrincipalId_IsNotSatisfied()
    {
        var wrongSubject = Fic(CanonicalFicName, Issuer, UamiClientId, Audience);

        var result = GraphAppRegistrationProvisioner.FindEquivalentByTriple(
            new[] { wrongSubject }, Issuer, UamiPrincipalId, Audience);

        result.Should().BeNull(
            "subject=clientId is the designated wrong-subject silent failure (auth-v4 §3.1 / " +
            "AADSTS700213) — it must never be treated as equivalent to the principalId subject");
    }

    [Fact]
    public void A42a_FindEquivalentByTriple_WrongIssuer_IsNotSatisfied()
    {
        var wrongIssuer = Fic(CanonicalFicName,
            $"https://login.microsoftonline.com/{CustomerTenantId}/v2.0", UamiPrincipalId, Audience);

        GraphAppRegistrationProvisioner.FindEquivalentByTriple(
            new[] { wrongIssuer }, Issuer, UamiPrincipalId, Audience).Should().BeNull();
    }

    [Fact]
    public void A42a_FindEquivalentByTriple_TwoAudiences_IsNotSatisfied()
    {
        // Script parity: Find-SpaarkeEquivalentFederatedCredential requires
        // EXACTLY ONE audience equal to the exchange audience.
        var twoAudiences = new FederatedIdentityCredential
        {
            Name = CanonicalFicName,
            Issuer = Issuer,
            Subject = UamiPrincipalId,
            Audiences = new List<string> { Audience, "api://SomethingElse" },
        };

        GraphAppRegistrationProvisioner.FindEquivalentByTriple(
            new[] { twoAudiences }, Issuer, UamiPrincipalId, Audience).Should().BeNull();
    }

    [Fact]
    public void A42a_FindEquivalentByTriple_NullOrEmptyCandidates_ReturnsNull()
    {
        GraphAppRegistrationProvisioner.FindEquivalentByTriple(
            null, Issuer, UamiPrincipalId, Audience).Should().BeNull();
        GraphAppRegistrationProvisioner.FindEquivalentByTriple(
            Array.Empty<FederatedIdentityCredential>(), Issuer, UamiPrincipalId, Audience).Should().BeNull();
    }

    // =====================================================================
    // (b) AADSTS70025 propagation retry — EXACT numeric match (SF-6 / C1)
    // =====================================================================

    [Fact]
    public void A42b_Classifier_ErrorCodes70025_ExactMatch_RetryPropagation()
    {
        var outcome = FicExchangeOutcomeClassifier.Classify(
            new FicExchangeAttempt(false, EntraError("invalid_client", 70025)));

        outcome.Verdict.Should().Be(FicExchangeVerdict.RetryPropagation,
            "70025 is the MEASURED live propagation code (auth-v4 2026-08-21) and must be retried");
    }

    [Fact]
    public void A42b_Classifier_ErrorCodes70021_ExactMatch_RetryPropagation()
    {
        FicExchangeOutcomeClassifier.Classify(
                new FicExchangeAttempt(false, EntraError("invalid_client", 70021)))
            .Verdict.Should().Be(FicExchangeVerdict.RetryPropagation,
                "70021 is retained on Microsoft's documentation (never observed live)");
    }

    [Fact]
    public void A42b_Classifier_ErrorCodes700211_WrongIssuer_FailsFast_NoRetry()
    {
        var outcome = FicExchangeOutcomeClassifier.Classify(
            new FicExchangeAttempt(false, EntraError("invalid_client", 700211)));

        outcome.Verdict.Should().Be(FicExchangeVerdict.RejectedCredentialFault,
            "700211 (unrecognised issuer) is a genuine config fault — a substring matcher on " +
            "'70021' would wrongly retry it for the whole budget (the SF-6 defect)");
    }

    [Fact]
    public void A42b_Classifier_ErrorCodes700213_WrongSubject_FailsFast_NoRetry()
    {
        var outcome = FicExchangeOutcomeClassifier.Classify(
            new FicExchangeAttempt(false, EntraError("invalid_client", 700213)));

        outcome.Verdict.Should().Be(FicExchangeVerdict.RejectedCredentialFault,
            "700213 (no FIC matches the assertion's subject) is a genuine config fault — a " +
            "substring matcher on '70025'/'70021' families must never absorb it");
    }

    [Fact]
    public void A42b_Classifier_NonJsonBody_NegativeLookahead_SeparatesPropagationFromConfigFaults()
    {
        // Non-JSON fallback (proxy page / CLI wrapper text) — regex with a
        // negative lookahead, script parity (:716-721).
        FicExchangeOutcomeClassifier.Classify(new FicExchangeAttempt(false,
                "proxy error page: AADSTS70025: request could not be completed"))
            .Verdict.Should().Be(FicExchangeVerdict.RetryPropagation);

        FicExchangeOutcomeClassifier.Classify(new FicExchangeAttempt(false,
                "proxy error page: AADSTS700213: no matching federated identity record found"))
            .Verdict.Should().Be(FicExchangeVerdict.RejectedCredentialFault,
                "AADSTS700213 must NOT match the AADSTS70021 fallback pattern (negative lookahead)");

        FicExchangeOutcomeClassifier.Classify(new FicExchangeAttempt(false,
                "proxy error page: AADSTS700211: unrecognised issuer"))
            .Verdict.Should().Be(FicExchangeVerdict.RejectedCredentialFault);
    }

    [Fact]
    public void A42b_Classifier_AuthorizationLayerErrors_AreAcceptanceEvidence()
    {
        // Entra evaluates the resource only AFTER accepting the client
        // credential — these prove the FIC works even with zero grants.
        FicExchangeOutcomeClassifier.Classify(new FicExchangeAttempt(false,
                EntraError("invalid_scope", 70011)))
            .Verdict.Should().Be(FicExchangeVerdict.Accepted);

        FicExchangeOutcomeClassifier.Classify(new FicExchangeAttempt(false,
                EntraError("invalid_request", 500011)))
            .Verdict.Should().Be(FicExchangeVerdict.Accepted,
                "AADSTS500011 (resource principal not found) is evaluated after credential acceptance");
    }

    [Fact]
    public void A42b_Classifier_UnknownError_TreatedAsCredentialFault()
    {
        FicExchangeOutcomeClassifier.Classify(new FicExchangeAttempt(false,
                EntraError("temporarily_unavailable", 90033)))
            .Verdict.Should().Be(FicExchangeVerdict.RejectedCredentialFault,
                "script parity: unknown codes are treated as credential faults, never silently retried");
    }

    // =====================================================================
    // (c) Exit-2 equivalent — PendingPostAppServiceVerification marker (SF-8)
    // =====================================================================

    [Fact]
    public void A42c_FicVerificationState_ExitCodeCorrespondence_IsPinned()
    {
        // Only PendingPostAppServiceVerification carries a NUMERIC exit-code
        // correspondence (value 2 == script exit 2, deliberately). The other
        // values map by NAME, not number: ExchangeVerified carries exit-0
        // SEMANTICS (its value 1 is NOT exit 1 — script exit 1 is the fault
        // path, deliberately unrepresentable as a success state), and
        // NotApplicable has no script analog and no live producer post-task-222
        // (D-13 — both models now run the FIC path). Retained as a defensive
        // enum default. Pinned so a re-order never silently re-maps the exit-2
        // marker semantics H13/T4 depends on.
        ((int)FicVerificationState.NotApplicable).Should().Be(0);
        ((int)FicVerificationState.ExchangeVerified).Should().Be(1);
        ((int)FicVerificationState.PendingPostAppServiceVerification).Should().Be(2,
            "value 2 IS the script exit-2 correspondence — the one numeric mapping that is load-bearing");
    }

    [Fact]
    public async Task A42c_Handler_FicPending_SetsInterStepMarker_OnConsentVerified()
    {
        var run = BuildModel2Run();
        var repo = new FakeRepository(run, "etag-a42c-1");
        var provisioner = FakeProvisioner.Success(BuildOutputs(FicVerificationState.PendingPostAppServiceVerification));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>(
            "exit-2 is the NORMAL off-Azure/L2 creation-time result — the run advances");
        repo.LastWrittenRun!.InterStepState.FicPendingPostAppServiceVerification.Should().BeTrue(
            "the exit-2 equivalent MUST be recorded in ProvisioningRun state so H13/T4 discharges " +
            "the real exchange verification — it is never silently absorbed as verified (SF-8)");
    }

    [Fact]
    public async Task A42c_Handler_FicPending_SetsInterStepMarker_OnConsentPendingToo()
    {
        var run = BuildModel2Run();
        var repo = new FakeRepository(run, "etag-a42c-2");
        var provisioner = FakeProvisioner.Success(BuildOutputs(FicVerificationState.PendingPostAppServiceVerification));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Pending());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        result.Should().BeOfType<HandlerResult.Success>();
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.WaitingOnGate);
        repo.LastWrittenRun.InterStepState.FicPendingPostAppServiceVerification.Should().BeTrue(
            "the FIC's verification debt is independent of the admin-consent gate — record it on " +
            "the WaitingOnGate path too");
    }

    [Fact]
    public async Task A42c_Handler_FicNotApplicable_LeavesMarkerNull()
    {
        var run = BuildModel2Run();
        var repo = new FakeRepository(run, "etag-a42c-3");
        var provisioner = FakeProvisioner.Success(BuildOutputs(FicVerificationState.NotApplicable));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified());

        await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        repo.LastWrittenRun!.InterStepState.FicPendingPostAppServiceVerification.Should().BeNull(
            "no FIC state was produced — the marker must not be invented");
    }

    // =====================================================================
    // (d) Cross-tenant refusal — Assert-SpaarkeFicTenancy port (SF-5)
    // =====================================================================

    [Fact]
    public void A42d_AssertFicTenancy_CrossTenant_ThrowsDistinctException()
    {
        var act = () => GraphAppRegistrationProvisioner.AssertFicTenancy(
            CustomerTenantId, SpaarkeTenantId, "spaarke-hosted-model2");

        var ex = act.Should().Throw<CrossTenantFicRefusedException>(
                "a cross-tenant (app-reg, UAMI) pair CREATES successfully and fails only at token " +
                "exchange weeks later — it must be refused loudly at provisioning time (SF-5)")
            .Which;
        ex.AppRegistrationTenantId.Should().Be(CustomerTenantId);
        ex.UamiTenantId.Should().Be(SpaarkeTenantId);
        ex.Profile.Should().Be("spaarke-hosted-model2");
        ex.Message.Should().Contain("CROSS-TENANT").And.Contain("REFUSED");
    }

    [Fact]
    public void A42d_AssertFicTenancy_SameTenant_DoesNotThrow_CaseInsensitive()
    {
        var act = () => GraphAppRegistrationProvisioner.AssertFicTenancy(
            SpaarkeTenantId.ToUpperInvariant(), SpaarkeTenantId, "spaarke-hosted-model2");

        act.Should().NotThrow("tenant GUIDs compare case-insensitively (PS `-ne` parity)");
    }

    [Fact]
    public void A42d_ResolveUamiTenantId_CustomerOwnedModel2_IsRequestTenant_GuardStructurallyInert()
    {
        // §9.2 reading (a) (owner-ratified Q2, 2026-08-25): a customer-owned
        // stamp federates its OWN stamp UAMI — the derivation makes the pair
        // intra-tenant by construction, so the guard passes (inert protection).
        var uamiTenant = GraphAppRegistrationProvisioner.ResolveUamiTenantId(
            "customer-owned-model2", CustomerTenantId, SpaarkeTenantId);

        uamiTenant.Should().Be(CustomerTenantId);
        var act = () => GraphAppRegistrationProvisioner.AssertFicTenancy(
            CustomerTenantId, uamiTenant!, "customer-owned-model2");
        act.Should().NotThrow();
    }

    [Fact]
    public void A42d_ResolveUamiTenantId_SpaarkeHosted_IsSpaarkeTenant_MismatchedRunRefused()
    {
        var uamiTenant = GraphAppRegistrationProvisioner.ResolveUamiTenantId(
            "spaarke-hosted-model2", CustomerTenantId, SpaarkeTenantId);

        uamiTenant.Should().Be(SpaarkeTenantId);

        // A spaarke-hosted profile dispatched with a CUSTOMER tenantId is the
        // exact misconfiguration SF-5 warned about — before A42 this created
        // a cross-tenant FIC silently; now it refuses at provisioning time.
        var act = () => GraphAppRegistrationProvisioner.AssertFicTenancy(
            CustomerTenantId, uamiTenant!, "spaarke-hosted-model2");
        act.Should().Throw<CrossTenantFicRefusedException>();
    }

    [Fact]
    public async Task A42d_Handler_CrossTenantRefusal_MapsToDistinctRejectionCode()
    {
        var run = BuildModel2Run();
        var repo = new FakeRepository(run, "etag-a42d-1");
        var provisioner = FakeProvisioner.Throws(
            new CrossTenantFicRefusedException(CustomerTenantId, SpaarkeTenantId, "spaarke-hosted-model2"));
        var handler = BuildHandler(repo, provisioner, FakeVerifier.Verified());

        var result = await handler.HandleAsync(BuildEnvelope(), CancellationToken.None);

        var failure = result.Should().BeOfType<HandlerResult.Failure>().Subject;
        failure.Class.Should().Be(FailureClass.Resumable,
            "operator corrects the run's tenant/profile configuration and resumes");
        failure.RejectionCode.Should().Be(EntraAppRegRejectionCodes.CrossTenantFicRefused,
            "the refusal is machine-routable — never a generic provisioning failure");
        failure.Diagnostic.Should().Contain("CROSS-TENANT");
        repo.LastWrittenRun!.Status.Should().Be(RunStatus.Failed);
    }

    // =====================================================================
    // (e) RETIRED 2026-09-29 (task 222 per D-13): the "Model 1 makes zero
    // FIC calls" I6 regression guard was DELETED with the shared-app-reg
    // branch. Post-D-13, both tenancy models create ONE per-customer app-reg
    // + FIC unconditionally — the premise "Model 1 creates no FIC" is
    // invalidated by design. See task 222 POML + INCOMING-D12-D13-REMEDIATION
    // §5 Item 1 for the retirement rationale. The other A42* rows
    // (idempotency, propagation, cross-tenant, invariants 1+2) apply to
    // both models post-D-13 and remain unchanged.
    // =====================================================================

    // =====================================================================
    // (f) §11 invariant 1 — wrong subject → AADSTS700213 detected
    // =====================================================================

    [Fact]
    public async Task A42f_Invariant1_WrongSubject_AADSTS700213_NamedAsWrongSubjectFault_NotRetried()
    {
        // Auth-v4 §11 invariant 1: a FIC whose subject is the UAMI's clientId
        // (not principalId) creates cleanly and dies at exchange with
        // AADSTS700213. The classifier must (1) fail fast — no retry-budget
        // burn, and (2) NAME the wrong-subject cause so the operator inspects
        // the FIC subject rather than chasing propagation.
        var classification = FicExchangeOutcomeClassifier.Classify(
            new FicExchangeAttempt(false, EntraError("invalid_client", 700213)));

        classification.Verdict.Should().Be(FicExchangeVerdict.RejectedCredentialFault);
        classification.Detail.Should().Contain("700213").And.Contain("SUBJECT")
            .And.Contain("principalId");

        // Through the retry driver: a single attempt, no retries.
        var time = new VirtualTimeProvider();
        var attempts = 0;
        var result = await FicExchangeOutcomeClassifier.ExecuteWithPropagationRetryAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(new FicExchangeAttempt(false, EntraError("invalid_client", 700213)));
            },
            FicExchangeOutcomeClassifier.DefaultMaxWait, time, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        result.Attempts.Should().Be(1, "config faults fail fast — retrying only delays the report");
        attempts.Should().Be(1);
        time.Elapsed.Should().Be(TimeSpan.Zero, "no retry delay may be spent on a credential fault");
    }

    // =====================================================================
    // (g) §11 invariant 2 — fresh-FIC propagation flap retried within budget
    // =====================================================================

    [Fact]
    public async Task A42g_Invariant2_FreshFicFlap_EightFailures_RetriedToAcceptance_WithinBudget()
    {
        // Auth-v4 §11 invariant 2, as MEASURED live 2026-08-21: a freshly
        // created FIC flapped with AADSTS70025 — 8 intermittent failures over
        // ~130s — before converging. The retry policy must absorb a window of
        // that magnitude within the default 600s budget.
        var time = new VirtualTimeProvider();
        var attempts = 0;
        var result = await FicExchangeOutcomeClassifier.ExecuteWithPropagationRetryAsync(
            _ =>
            {
                attempts++;
                return Task.FromResult(attempts <= 8
                    ? new FicExchangeAttempt(false, EntraError("invalid_client", 70025))
                    : new FicExchangeAttempt(true, null));
            },
            FicExchangeOutcomeClassifier.DefaultMaxWait, time, CancellationToken.None);

        result.Accepted.Should().BeTrue("the flap is propagation, not a fault — retry must outlast it");
        result.Attempts.Should().Be(9, "8 propagation failures + the converged success");
        // 5+10+20+30+30+30+30+30 = 185s of virtual delay — the same order of
        // magnitude as the measured ~130s window, well inside the 600s budget.
        time.Elapsed.Should().Be(TimeSpan.FromSeconds(185));
        time.Elapsed.Should().BeLessThan(FicExchangeOutcomeClassifier.DefaultMaxWait);
    }

    [Fact]
    public async Task A42g_RetryBudgetExhausted_ReportsPropagationTimeout_NotCredentialFault()
    {
        var time = new VirtualTimeProvider();
        var result = await FicExchangeOutcomeClassifier.ExecuteWithPropagationRetryAsync(
            _ => Task.FromResult(new FicExchangeAttempt(false, EntraError("invalid_client", 70025))),
            TimeSpan.FromSeconds(60), time, CancellationToken.None);

        result.Accepted.Should().BeFalse();
        // Attempts at t=0, 5, 15, 35; at t=35 the next 30s delay would exceed
        // the 60s budget (35+30 > 60) — the script's exact stop condition.
        result.Attempts.Should().Be(4);
        result.Detail.Should().Contain("propagation-class error").And.Contain("persisted");
    }

    // ---------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------

    private static FederatedIdentityCredential Fic(string name, string issuer, string subject, string audience)
        => new()
        {
            Name = name,
            Issuer = issuer,
            Subject = subject,
            Audiences = new List<string> { audience },
        };

    private static string EntraError(string error, int code)
        => $$"""{"error":"{{error}}","error_description":"AADSTS{{code}}: test fixture","error_codes":[{{code}}]}""";

    private static ProvisioningRun BuildModel2Run()
    {
        var run = new ProvisioningRun
        {
            RunId = RunId,
            CustomerId = CustomerId,
            EnvironmentId = "env-guid",
            TenancyModel = nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2),
            Status = RunStatus.Running,
            Profile = "spaarke-hosted-model2",
        };
        run.Parameters.NonSecret[H3EntraAppRegHandler.TenantIdParameterKey] = SpaarkeTenantId;
        // T240a: H3 derives the code pages' SPA redirect from the intake environment URL.
        run.Parameters.NonSecret[IntakeParameterCatalog.DataverseEnvUrl] = $"https://spaarke-{CustomerId}.crm.dynamics.com/";
        // Task 245a (G25): the customer vault name is H2a's output, not a run parameter.
        run.InterStepState.KeyVaultName = KeyVaultName;
        run.InterStepState.MiObjectId = UamiPrincipalId;
        return run;
    }

    private static HandlerEnvelope BuildEnvelope() => new()
    {
        HandlerId = H3EntraAppRegHandler.HandlerIdentifier,
        RunId = RunId,
        CustomerId = CustomerId,
        ParametersJson = "{}",
        EnqueuedAt = DateTimeOffset.UtcNow,
    };

    private static EntraAppRegOutputs BuildOutputs(FicVerificationState ficState) => new()
    {
        BffAppRegId = BffAppRegId,
        BffClientSecretKvUri = GraphAppRegistrationProvisioner.BuildKvUriReference(
            KeyVaultName, GraphAppRegistrationProvisioner.ClientSecretName),
        PendingKvWrites = Array.Empty<PendingKvSecretWrite>(),
        FicVerification = ficState,
    };

    private static H3EntraAppRegHandler BuildHandler(
        FakeRepository repo,
        FakeProvisioner provisioner,
        FakeVerifier verifier)
    {
        var options = Options.Create(new EntraAppRegOptions
        {
            ExpectedDelegatedScopeCount = 5,
        });
        return new H3EntraAppRegHandler(
            repo, provisioner, verifier, options, NullLogger<H3EntraAppRegHandler>.Instance);
    }

    /// <summary>
    /// Deterministic virtual clock for the retry driver: timestamps are
    /// TimeSpan ticks; Task.Delay(delay, timeProvider, ct) advances the clock
    /// by the requested dueTime and fires synchronously — no real sleeps.
    /// </summary>
    private sealed class VirtualTimeProvider : TimeProvider
    {
        private long _ticks;

        public TimeSpan Elapsed => TimeSpan.FromTicks(Interlocked.Read(ref _ticks));

        public override long GetTimestamp() => Interlocked.Read(ref _ticks);

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            if (dueTime > TimeSpan.Zero)
            {
                Interlocked.Add(ref _ticks, dueTime.Ticks);
            }
            callback(state);
            return new NoopTimer();
        }

        private sealed class NoopTimer : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    /// <summary>Repository fake — records last written run (same idiom as H3EntraAppRegHandlerTests' private fake; local because that fake is private to its file).</summary>
    private sealed class FakeRepository : IProvisioningRunRepository
    {
        private ProvisioningRun? _run;
        private string? _etag;
        public ProvisioningRun? LastWrittenRun { get; private set; }

        public FakeRepository(ProvisioningRun? run, string? etag)
        {
            _run = run;
            _etag = etag;
        }

        public Task<ProvisioningRunReadResult?> ReadRunAsync(string customerId, string runId, CancellationToken ct)
            => Task.FromResult(_run is null || _etag is null
                ? null
                : new ProvisioningRunReadResult(_run, _etag));

        public Task<ProvisioningRunReadResult> CreateRunAsync(ProvisioningRun run, CancellationToken ct)
            => throw new NotImplementedException();

        public Task<ReplaceRunResult> ReplaceRunAsync(ProvisioningRun run, string ifMatchEtag, CancellationToken ct)
        {
            LastWrittenRun = run;
            _run = run;
            _etag = ifMatchEtag + "-next";
            return Task.FromResult<ReplaceRunResult>(new ReplaceRunResult.Success(run, _etag));
        }
    }

    /// <summary>Provisioner fake — canned outcome + throwing variant; records ProvisionCallCount. The shared-verify variant + VerifySharedAsync implementation were removed in task 222 per D-13 (shared-app-reg branch retired; both models now provision per-customer).</summary>
    private sealed class FakeProvisioner : IEntraAppRegProvisioner
    {
        private readonly EntraAppRegOutcome? _provisionOutcome;
        private readonly Exception? _provisionThrows;

        public int ProvisionCallCount { get; private set; }

        private FakeProvisioner(
            EntraAppRegOutcome? provisionOutcome,
            Exception? provisionThrows)
        {
            _provisionOutcome = provisionOutcome;
            _provisionThrows = provisionThrows;
        }

        public static FakeProvisioner Success(EntraAppRegOutputs outputs)
            => new(new EntraAppRegOutcome.Success(outputs), null);

        public static FakeProvisioner Throws(Exception ex)
            => new(null, ex);

        public Task<EntraAppRegOutcome> ProvisionAsync(EntraAppRegRequest request, CancellationToken ct)
        {
            ProvisionCallCount++;
            if (_provisionThrows is not null)
            {
                throw _provisionThrows;
            }
            return Task.FromResult(_provisionOutcome!);
        }

        public Task<string?> CommitPendingSecretsAsync(IReadOnlyList<PendingKvSecretWrite> pendingWrites, CancellationToken ct)
            => Task.FromResult<string?>(null);
    }

    /// <summary>Admin-consent verifier fake.</summary>
    private sealed class FakeVerifier : IAdminConsentVerifier
    {
        private readonly AdminConsentVerificationResult _result;

        private FakeVerifier(AdminConsentVerificationResult result) => _result = result;

        public static FakeVerifier Verified()
            => new(new AdminConsentVerificationResult.Verified(5, 5, null));

        public static FakeVerifier Pending()
            => new(new AdminConsentVerificationResult.Pending(0, 5, "consent not yet granted", null));

        public Task<AdminConsentVerificationResult> VerifyAsync(
            string bffAppRegId, string tenantId, int expectedDelegatedScopeCount, CancellationToken ct)
            => Task.FromResult(_result);
    }
}
