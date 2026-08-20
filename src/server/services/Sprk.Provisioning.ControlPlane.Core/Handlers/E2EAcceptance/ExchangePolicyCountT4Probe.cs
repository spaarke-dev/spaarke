// -----------------------------------------------------------------------------
// ExchangePolicyCountT4Probe.cs
//
// Task 180 (Phase C'' Wave G-7 -- pipelined with H14a / task 114 + task 161).
// REAL T4 probe replacing the single-outcome InfraFault branch in
// PlaceholderTrapVerifier for TrapKind.T4ExchangePolicyCount. Per DS-4 s6 T4
// row: "Sidecar call -- extend the H14a sidecar with a read-only GET
// /policies route wrapping Get-ApplicationAccessPolicy (the ONE H13 probe
// that is not pure .NET; keep it in the same sidecar, same envelope)".
//
// STANDALONE CLASS (deliberately no shared interface dependency):
//   6 sibling Wave-G-7 trap tasks (171 T1, 177 T2, 178 T3, 180 T4 [this task],
//   172 T5, 175 T6) each author a per-probe class to disjoint files. Assembly
//   task 185 is the terminal owner that decides the composition surface --
//   whether a shared ITrapProbe seam or a monolithic swap of
//   PlaceholderTrapVerifier -- after ground-truthing what all 6 sibling
//   probes look like. Deliberately no `: ITrapProbe` here so task 185 has
//   architectural flexibility (parity with sibling probes' original standalone
//   posture -- see KeyVaultReferenceIdentityT1Probe.cs / DataverseAppUserPairT2Probe.cs /
//   T5SlotMiKvRbacTrapProbe.cs / T6SpeConfidentialClientTrapProbe.cs headers).
//
// PROBE CONTRACT:
//   The T4 post-condition H14a establishes: EXACTLY 2 ApplicationAccessPolicy
//   entries exist on the customer tenant, one per expected AppId (BFF
//   app-registration id + UAMI client id). H13's T4 probe re-verifies this
//   post-condition INDEPENDENTLY (R7 "assert EFFECTS not intentions") -- H10
//   or H14a might have reported success while Exchange silently landed a
//   different shape (0 policies for one AppId, 3 policies with wrong scope,
//   etc.).
//
//   Passed shapes:
//     * EXACT PARITY -- the observed AppId set contains EXACTLY the 2
//       expected AppIds, no more, no less. This is the shipped-good T4 state.
//     * OBSERVED SUPERSET (both expected AppIds present + one or more
//       additional AppIds present) is still Passed for T4 count-parity per
//       DS-4 s6 T4 row's own framing ("Exchange policy COUNT" -- the >=2
//       count is what the trap counts; additional entries for other apps in
//       the tenant are outside spaarke's scope. Callers that need EXACT set
//       parity should treat that as a distinct check on top of this probe's
//       shape.) The diagnostic is emitted at INFO log level so the extra
//       AppIds are still auditable.
//
//   Failed shapes -- the T4 silent-fail traps this probe catches (per POML +
//   spec.md FR-33 T4 + design.md s4B T4):
//     * MISSING BOTH       -- 0 policies for both expected AppIds. H14a
//                             never landed, OR its own action-and-verify
//                             silently reported success without the writes
//                             completing (the exact task-143-class silent-
//                             fail-at-write shape at a different SDK). BFF
//                             calls into any mailbox will fail closed.
//     * MISSING ONE        -- Exactly 1 of the 2 expected AppIds has a
//                             policy; the other has 0. Half-applied is the
//                             classic partial-mutation shape (H14a's
//                             action-and-verify wrote 1 but the second
//                             New-ApplicationAccessPolicy transiently
//                             failed + the retry didn't re-run). BFF calls
//                             for one AppId route through; the other 403s
//                             silently.
//     * NO EXPECTED (BUT OTHERS PRESENT) -- 0 policies for either expected
//                             AppId, but 1+ policies exist for OTHER AppIds
//                             (the sidecar's read enumerated a tenant with
//                             a different provisioning history entirely).
//                             Same downstream symptom as MISSING BOTH but
//                             the diagnostic distinguishes so operators
//                             know to check H10 / H3 AppId flow.
//
//   InfraFault -- probe could not verdict (H13 Resumable):
//     * Missing TenantId / BffAppRegId / UamiClientId from the request
//       (defense-in-depth over H13's own parameter guards; upstream
//       handlers must have run to populate these).
//     * IExchangePolicyReadClient.ReadAsync returned Failure (transport,
//       auth, KV, cert-fetch, EXO connect, Get-ApplicationAccessPolicy
//       failure inside the sidecar -- the entire read leg didn't reach a
//       conclusive answer). Pass-through diagnostic surfaces the specific
//       cause.
//     * IExchangePolicyReadClient THREW (contract violation -- the seam's
//       own contract is to return Failure rather than throw; a throw
//       means the client itself is broken, not a probe verdict).
//
// SEAM REUSE + PATTERN PARITY:
//   Injects <see cref="IExchangePolicyReadClient"/> (this task's own new
//   sibling read client) -- delegates ALL HTTP transport, KV shared-secret
//   read, and X-Sidecar-Auth header discipline to that client. Unit tests
//   substitute a fake, exactly the way T2 sibling probe (task 177)
//   substitutes IDataverseAppUserVerifier -- one point-of-change for the
//   sidecar wire contract, and the probe's decision logic is exercised in
//   isolation.
//
// PLACEMENT JUSTIFICATION (CLAUDE.md s11):
//   Existing -- IE2ETrapVerifier / TrapVerificationOutcome / TrapKind
//     (task 055) + PlaceholderTrapVerifier's InfraFault-per-kind branch for
//     T4 + this task's own new IExchangePolicyReadClient. This probe REUSES
//     the T2/T5 sibling probe DI pattern verbatim (inject a
//     read/verify-only seam; unit tests substitute a canned fake).
//   Extension -- ONE per-probe class covering ONE T4 branch; sibling probes
//     cover their own kinds; assembly task 185 composes.
//   Cost-of-doing-nothing -- H13's T4 branch returns InfraFault
//     UNCONDITIONALLY (Resumable) without this probe -- H13's acceptance
//     gate cannot go green for T4 (DS-4 s6 verbatim); the acceptance-target
//     transition (task 184/185/186) is blocked on EVERY probe individually
//     landing. Concrete downstream failure this probe catches: any BFF or
//     Graph mailbox call for the missing AppId 403s silently at Exchange
//     until an operator manually creates the missing ApplicationAccessPolicy.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real T4 (Exchange policy count) probe per DS-4 s6. Produces exactly one
/// <see cref="TrapVerificationOutcome"/> for <see cref="TrapKind.T4ExchangePolicyCount"/>.
/// Assembly task 185 composes this + 5 sibling probes into the aggregate
/// <see cref="IE2ETrapVerifier"/> implementation that replaces
/// <see cref="PlaceholderTrapVerifier"/>'s DI registration.
/// </summary>
public sealed class ExchangePolicyCountT4Probe
{
    /// <summary>Which s4B trap this probe verdicts. Exposed for task 185's aggregate composition to route by kind.</summary>
    public TrapKind Kind => TrapKind.T4ExchangePolicyCount;

    private readonly IExchangePolicyReadClient _readClient;
    private readonly ILogger<ExchangePolicyCountT4Probe> _logger;

    public ExchangePolicyCountT4Probe(
        IExchangePolicyReadClient readClient,
        ILogger<ExchangePolicyCountT4Probe> logger)
    {
        ArgumentNullException.ThrowIfNull(readClient);
        ArgumentNullException.ThrowIfNull(logger);
        _readClient = readClient;
        _logger = logger;
    }

    /// <summary>
    /// Runs the T4 probe: reads the customer tenant's full
    /// ApplicationAccessPolicy list via the sidecar's GET /policies route,
    /// then verifies both expected AppIds (BFF app-reg + UAMI) are present.
    /// Never throws for probe-level fault modes -- returns
    /// <see cref="TrapVerificationOutcome.InfraFault"/> instead so H13 can
    /// classify Resumable per its s4C rollback table.
    /// </summary>
    public async Task<TrapVerificationOutcome> ProbeAsync(
        TrapVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        // (1) Input guards -- defense-in-depth over H13's own parameter
        //     guards. Missing input surfaces as InfraFault (Resumable) rather
        //     than throwing (parity with sibling T1 / T2 / T5 probes).
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T4 probe: request.TenantId is empty. H1 subscription-readiness / H3 tenant-scope MUST " +
                "populate before H13 can scope the sidecar read to the correct tenant (s4D I1 -- never " +
                "rely on ambient default-tenant).");
        }
        if (string.IsNullOrWhiteSpace(request.BffAppRegId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T4 probe: request.BffAppRegId is empty. H3 app-registration MUST populate this parameter " +
                "-- one of the 2 expected AppIds T4 verifies.");
        }
        if (string.IsNullOrWhiteSpace(request.UamiClientId))
        {
            return new TrapVerificationOutcome.InfraFault(Kind,
                "T4 probe: request.UamiClientId is empty. H2a Bicep infra deploy MUST populate this " +
                "parameter -- one of the 2 expected AppIds T4 verifies.");
        }

        _logger.LogInformation(
            "T4 probe starting: customerId={CustomerId} runId={RunId} tenantId={TenantId} " +
            "bffAppRegId={BffAppRegId} uamiClientId={UamiClientId}",
            request.CustomerId, request.RunId, request.TenantId,
            request.BffAppRegId, request.UamiClientId);

        // (2) Sidecar read leg -- delegated to IExchangePolicyReadClient
        //     (this task's own new sibling client). Any transport/auth/KV/
        //     cert/EXO failure surfaces as ExchangePolicyReadOutcome.Failure
        //     which we map to InfraFault (Resumable). A THROW from the seam
        //     is a contract violation -- caught defensively so a broken
        //     client can't tear down the whole H13 run.
        ExchangePolicyReadOutcome readOutcome;
        try
        {
            readOutcome = await _readClient.ReadAsync(
                new ExchangePolicyReadRequest(request.TenantId, request.RunId),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw; // Caller-driven cancellation propagates.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "T4 probe InfraFault -- IExchangePolicyReadClient threw {ExType}: customerId={CustomerId}",
                ex.GetType().Name, request.CustomerId);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T4 verdict deferred: IExchangePolicyReadClient threw unexpected {ex.GetType().Name}: " +
                $"{ex.Message}. Seam contract requires Failure return -- a throw means the client itself is " +
                "broken (transport, config, or bug), not a T4 verdict.");
        }

        if (readOutcome is ExchangePolicyReadOutcome.Failure readFail)
        {
            _logger.LogWarning(
                "T4 probe InfraFault -- sidecar read failed: customerId={CustomerId} diagnostic={Diagnostic}",
                request.CustomerId, readFail.Diagnostic);
            return new TrapVerificationOutcome.InfraFault(Kind,
                $"T4 verdict deferred: sidecar GET /policies did not reach a conclusive answer. " +
                $"{readFail.Diagnostic}");
        }

        var success = (ExchangePolicyReadOutcome.Success)readOutcome;
        var observedAppIds = success.ObservedAppIds ?? Array.Empty<string>();

        // (3) Verdict: both expected AppIds must appear in observedAppIds
        //     (case-insensitive membership -- AppIds are GUIDs but ARM /
        //     Graph sometimes upper-cases their string form; be lenient on
        //     casing since the underlying identity is the value not the
        //     glyph). See ClassifyOutcome for the classified failure shape.
        var verdict = ClassifyOutcome(
            expectedBff: request.BffAppRegId,
            expectedUami: request.UamiClientId,
            observed: observedAppIds);

        switch (verdict.Kind)
        {
            case T4VerdictKind.Passed:
                _logger.LogInformation(
                    "T4 probe PASSED: both expected AppIds have ApplicationAccessPolicy entries " +
                    "on tenant '{TenantId}'. observedAppIdCount={ObservedCount} totalPoliciesEnumerated={PolicyCount}",
                    request.TenantId, observedAppIds.Count, success.Policies?.Count ?? 0);
                return new TrapVerificationOutcome.Passed(Kind);

            case T4VerdictKind.PassedWithExtras:
                _logger.LogInformation(
                    "T4 probe PASSED (with extras): both expected AppIds have ApplicationAccessPolicy " +
                    "entries on tenant '{TenantId}'. observedAppIds also include AppIds for other tenant " +
                    "apps outside spaarke's scope (bff={BffAppRegId}, uami={UamiClientId}, " +
                    "observedAppIdCount={ObservedCount}, totalPoliciesEnumerated={PolicyCount}).",
                    request.TenantId, request.BffAppRegId, request.UamiClientId,
                    observedAppIds.Count, success.Policies?.Count ?? 0);
                return new TrapVerificationOutcome.Passed(Kind);

            default:
                var diagnostic = BuildFailureDiagnostic(request, observedAppIds, success.Policies ?? Array.Empty<ExchangePolicyEntry>(), verdict);
                _logger.LogWarning("T4 probe FAILED: {Diagnostic}", diagnostic);
                return new TrapVerificationOutcome.Failed(Kind, diagnostic);
        }
    }

    /// <summary>
    /// Classifies the T4 verdict from (expectedBff, expectedUami, observed).
    /// Exposed internal so unit tests can pin the classifier's rules directly.
    /// </summary>
    internal static T4Verdict ClassifyOutcome(
        string expectedBff,
        string expectedUami,
        IReadOnlyList<string> observed)
    {
        var observedSet = new HashSet<string>(observed, StringComparer.OrdinalIgnoreCase);
        var bffPresent = observedSet.Contains(expectedBff);
        var uamiPresent = observedSet.Contains(expectedUami);
        var expectedSet = new HashSet<string>(
            new[] { expectedBff, expectedUami }, StringComparer.OrdinalIgnoreCase);
        var extras = observedSet.Where(o => !expectedSet.Contains(o)).ToArray();

        if (bffPresent && uamiPresent)
        {
            return extras.Length == 0
                ? new T4Verdict(T4VerdictKind.Passed, MissingBff: false, MissingUami: false, Extras: extras)
                : new T4Verdict(T4VerdictKind.PassedWithExtras, MissingBff: false, MissingUami: false, Extras: extras);
        }

        // At least one expected AppId is missing.
        if (!bffPresent && !uamiPresent)
        {
            return observedSet.Count == 0
                ? new T4Verdict(T4VerdictKind.FailedMissingBoth, MissingBff: true, MissingUami: true, Extras: extras)
                : new T4Verdict(T4VerdictKind.FailedNoExpectedButOthersPresent, MissingBff: true, MissingUami: true, Extras: extras);
        }

        return new T4Verdict(
            T4VerdictKind.FailedMissingOne,
            MissingBff: !bffPresent,
            MissingUami: !uamiPresent,
            Extras: extras);
    }

    private static string BuildFailureDiagnostic(
        TrapVerificationRequest request,
        IReadOnlyList<string> observedAppIds,
        IReadOnlyList<ExchangePolicyEntry> policies,
        T4Verdict verdict)
    {
        var observedJoined = observedAppIds.Count == 0
            ? "(none)"
            : string.Join(", ", observedAppIds);
        var shapeLabel = verdict.Kind switch
        {
            T4VerdictKind.FailedMissingBoth =>
                "MISSING BOTH -- 0 ApplicationAccessPolicy entries exist for either expected AppId (BFF or UAMI). " +
                "H14a's action-and-verify either never landed OR silently reported success without the underlying " +
                "New-ApplicationAccessPolicy writes completing.",

            T4VerdictKind.FailedMissingOne =>
                verdict.MissingBff
                    ? "MISSING ONE (BFF) -- only the UAMI expected AppId has an ApplicationAccessPolicy entry; the " +
                      "BFF app-registration is unprotected. H14a's second New-ApplicationAccessPolicy write did not " +
                      "land (transient EXO throttle without retry, or a partial mutation)."
                    : "MISSING ONE (UAMI) -- only the BFF expected AppId has an ApplicationAccessPolicy entry; the " +
                      "UAMI client is unprotected. H14a's second New-ApplicationAccessPolicy write did not land.",

            T4VerdictKind.FailedNoExpectedButOthersPresent =>
                $"NO EXPECTED (BUT OTHERS PRESENT) -- 0 policies for either expected AppId, but the tenant has " +
                $"policies for {observedAppIds.Count} OTHER AppId(s) (a different provisioning history entirely; " +
                "verify H3 app-registration + H10 App-User pair are pointing at the same customer tenant).",

            _ => "UNKNOWN FAILURE SHAPE -- classifier returned an unmapped verdict; this is a probe bug.",
        };

        var extrasNote = verdict.Extras.Length == 0
            ? string.Empty
            : $" Other observed AppIds outside expected set: [{string.Join(", ", verdict.Extras)}].";
        var policiesNote = policies.Count == 0
            ? "policies enumerated: 0."
            : $"policies enumerated: {policies.Count}.";

        return
            $"T4 Exchange policy count VIOLATED on tenant '{request.TenantId}' (customerId='{request.CustomerId}'): " +
            $"expected an ApplicationAccessPolicy entry for BOTH {{BFF app-reg={request.BffAppRegId}, " +
            $"UAMI client={request.UamiClientId}}}. Observed AppIds: [{observedJoined}]. {shapeLabel}{extrasNote} " +
            $"{policiesNote} Downstream: any BFF or Graph mailbox call for the unprotected AppId 403s silently at " +
            "Exchange (spec.md FR-33 T4 / design.md s4B T4). Operator: re-run H14a or execute " +
            "New-ApplicationAccessPolicy -AccessRight RestrictAccess for the missing AppId(s) by hand.";
    }

    /// <summary>
    /// Per-verdict classification. Exposed public so tests can pin the classifier.
    /// </summary>
    public enum T4VerdictKind
    {
        /// <summary>Both expected AppIds present; no additional AppIds.</summary>
        Passed = 0,

        /// <summary>Both expected AppIds present + one or more additional AppIds present (tenant has policies for other apps).</summary>
        PassedWithExtras = 1,

        /// <summary>Zero expected AppIds present; observed set is entirely empty.</summary>
        FailedMissingBoth = 10,

        /// <summary>Exactly one expected AppId is missing.</summary>
        FailedMissingOne = 11,

        /// <summary>Zero expected AppIds present, but the tenant DOES have policies for other AppIds.</summary>
        FailedNoExpectedButOthersPresent = 12,
    }

    /// <summary>Classifier output. Exposed public so tests can construct + assert.</summary>
    public sealed record T4Verdict(
        T4VerdictKind Kind,
        bool MissingBff,
        bool MissingUami,
        string[] Extras);
}
