// -----------------------------------------------------------------------------
// FicExchangeOutcomeClassifier.cs
//
// Task 205b (row A42, FR-C4) — C# port of the FIC token-exchange outcome
// classification + propagation-retry semantics that master
// `Register-EntraAppRegistrations.ps1` ships in `Test-SpaarkeFicTokenExchange`
// (script :609-787) + `$script:PropagationErrorCodes` (:287). This is one half
// of the A42 contract-parity resolution (path (b), owner Q5 disposition
// 2026-08-25): the C# estate and the PS `-FicOnly` estate MUST classify
// exchange outcomes identically, pinned by the parity tests in
// A42FicReconciliationTests.cs and by the written contract at
// projects/customer-provisioning-orchestration-r1/notes/decisions/
// 205b-a42-fic-parity-contract.md.
//
// WHO CONSUMES THIS. NOT the L2 creation-time path: L2's Worker runs under
// L2's own platform UAMI and cannot mint an assertion as the customer/shared
// BFF UAMI (GraphAppRegistrationProvisioner GOTCHA 2 / SF-4), so H3 can never
// perform the exchange this classifier judges — its creation-time result is
// the exit-2 equivalent (FicVerificationState.PendingPostAppServiceVerification).
// The consumers are the exchange-CAPABLE hosts that discharge that pending
// marker: H13/T4 post-App-Service verification, the task-186 E2E runner, and
// the (Q11) BFF warmup self-proof. They MUST use this classifier rather than
// re-deriving retry codes — a re-derivation is exactly the divergence FR-C4
// exists to prevent (a future retry-code change landing in one estate only
// leaves half the fleet retrying correctly and the other half asserting the
// OPPOSITE verdict after burning its budget — SF-6).
//
// SEMANTICS PORTED VERBATIM FROM THE SCRIPT (all MEASURED 2026-08-21, not
// assumed — see the script's own comments):
//   - Propagation codes {70021, 70025} matched EXACTLY against the numeric
//     `error_codes` array. 70025 is the code the live propagation window
//     actually produced (~8 intermittent failures over ~130s — auth-v4 §11
//     invariant 2); 70021 was never observed live and is retained only
//     because Microsoft documents it.
//   - NEVER substring-match. "AADSTS70021" `-match` also matched AADSTS700211
//     (wrong issuer) and AADSTS700213 (wrong subject) — genuine config faults
//     that were being retried for the full budget and then reported as ruled
//     out (auth-v4 code-review critical C1 / SF-6). The non-JSON fallback
//     regex uses a negative lookahead for the same reason.
//   - AADSTS700213 = the wrong-subject signature (subject set to the UAMI's
//     clientId instead of its principalId — auth-v4 §11 invariant 1). Named
//     explicitly in the detail so an operator looks at the FIC subject, not
//     at propagation.
//   - Authorization-layer errors (invalid_scope / invalid_resource /
//     invalid_target / access_denied / insufficient_scope) and AADSTS500011
//     are ACCEPTANCE evidence: Entra evaluates the resource only AFTER
//     accepting the client credential, so they prove the FIC works even on a
//     freshly provisioned app-reg with zero grants.
//   - Unknown errors classify as credential faults (fail fast) — parity with
//     the script's terminal else-branch.
//   - Retry cadence: 5s doubling, capped at 30s; budget check is
//     `elapsed + nextDelay > maxWait` (the script's exact guard); default
//     budget 600s (script `-PropagationRetrySeconds` default).
// -----------------------------------------------------------------------------

using System.Text.Json;

namespace Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;

/// <summary>Verdict for a single FIC token-exchange attempt's outcome.</summary>
public enum FicExchangeVerdict
{
    /// <summary>Entra accepted the client credential (token issued, or an authorization-layer rejection that is only evaluated after credential acceptance). The FIC works.</summary>
    Accepted,

    /// <summary>A propagation-class error (AADSTS70021 / AADSTS70025 exact-match) — the credential is fine, the directory has not converged. Retry within budget.</summary>
    RetryPropagation,

    /// <summary>The credential itself was rejected (incl. AADSTS700213 wrong-subject, AADSTS700211 wrong-issuer, AADSTS7000215) or the error is unrecognized. Fail fast — retrying only delays the report.</summary>
    RejectedCredentialFault,
}

/// <summary>One classified exchange outcome: verdict + operator-facing detail.</summary>
public sealed record FicExchangeClassification(FicExchangeVerdict Verdict, string Detail);

/// <summary>The raw result of one exchange attempt, fed to the classifier. <see cref="TokenIssued"/> true short-circuits to Accepted; otherwise <see cref="ErrorBody"/> is Entra's response body (JSON or not).</summary>
public sealed record FicExchangeAttempt(bool TokenIssued, string? ErrorBody);

/// <summary>Terminal result of <see cref="FicExchangeOutcomeClassifier.ExecuteWithPropagationRetryAsync"/> — mirrors the PS function's Accepted/Attempts/Detail shape.</summary>
public sealed record FicExchangeVerificationResult(bool Accepted, int Attempts, string Detail);

/// <summary>
/// Pure classification + retry policy for FIC token-exchange verification.
/// C#-side half of the A42 parity contract with
/// <c>Register-EntraAppRegistrations.ps1</c>'s <c>Test-SpaarkeFicTokenExchange</c>.
/// Static + side-effect-free so it is unit-testable without live Entra.
/// </summary>
public static class FicExchangeOutcomeClassifier
{
    /// <summary>
    /// Propagation error codes, matched EXACTLY as numbers against Entra's
    /// structured <c>error_codes</c> array. Parity with the script's
    /// <c>$script:PropagationErrorCodes = @(70021, 70025)</c>. 70025 is the
    /// MEASURED live propagation code (2026-08-21); 70021 is Microsoft-documented
    /// only. Do NOT widen casually and NEVER substring-match — 700211/700213
    /// are config faults that must fail fast (SF-6).
    /// </summary>
    public static readonly IReadOnlyList<int> PropagationErrorCodes = new[] { 70021, 70025 };

    /// <summary>OAuth2 <c>error</c> values meaning the client credential itself was rejected (script parity).</summary>
    public static readonly IReadOnlyList<string> CredentialLayerErrors =
        new[] { "invalid_client", "unauthorized_client" };

    /// <summary>OAuth2 <c>error</c> values Entra evaluates only AFTER accepting the credential — positive evidence the FIC works (script parity).</summary>
    public static readonly IReadOnlyList<string> AuthorizationLayerErrors =
        new[] { "invalid_scope", "invalid_resource", "invalid_target", "access_denied", "insufficient_scope" };

    /// <summary>The AADSTS code Entra actually returns when no FIC on the app matches the assertion's SUBJECT — the wrong-subject (clientId-instead-of-principalId) signature. Auth-v4 §11 invariant 1.</summary>
    public const int WrongSubjectErrorCode = 700213;

    /// <summary>Retry budget default — parity with the script's <c>-PropagationRetrySeconds</c> default (600s).</summary>
    public static readonly TimeSpan DefaultMaxWait = TimeSpan.FromSeconds(600);

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Classifies one exchange attempt's outcome. Ordering is the script's:
    /// token issued → Accepted; propagation exact-match → RetryPropagation;
    /// authorization-layer → Accepted; everything else (700213 named
    /// explicitly) → RejectedCredentialFault.
    /// </summary>
    public static FicExchangeClassification Classify(FicExchangeAttempt attempt)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (attempt.TokenIssued)
        {
            return new FicExchangeClassification(
                FicExchangeVerdict.Accepted,
                "Entra issued a token. The federated credential is valid and working.");
        }

        var errorBody = attempt.ErrorBody ?? string.Empty;
        var (parsed, oauthError, errorCodes) = ParseEntraErrorBody(errorBody);

        // (1) Propagation — EXACT numeric match against error_codes.
        var isPropagation = errorCodes.Any(code => PropagationErrorCodes.Contains(code));

        // Fallback for a non-JSON body (proxy error page, CLI wrapper text).
        // The negative lookahead is what stops AADSTS700211 from masquerading
        // as AADSTS70021 — script parity (:716-721).
        if (!isPropagation && !parsed)
        {
            isPropagation = PropagationErrorCodes.Any(code =>
                System.Text.RegularExpressions.Regex.IsMatch(errorBody, $"AADSTS{code}(?![0-9])"));
        }

        if (isPropagation)
        {
            return new FicExchangeClassification(
                FicExchangeVerdict.RetryPropagation,
                $"Propagation-class error (codes: {string.Join(",", errorCodes)}) — the credential is fine, the directory has not converged. Retry within budget.");
        }

        // (2) Authorization-layer — credential ACCEPTED, resource rejected.
        if (oauthError is not null && AuthorizationLayerErrors.Contains(oauthError, StringComparer.Ordinal))
        {
            return new FicExchangeClassification(
                FicExchangeVerdict.Accepted,
                $"Assertion ACCEPTED. Entra accepted the client credential and then rejected the requested scope (OAuth2 error '{oauthError}'), which it evaluates only afterwards. The federated credential itself is valid; the app registration simply lacks that grant.");
        }
        if (errorCodes.Contains(500011))
        {
            return new FicExchangeClassification(
                FicExchangeVerdict.Accepted,
                "Assertion ACCEPTED. Entra rejected the requested scope (AADSTS500011: resource principal not found in tenant), which it evaluates only after accepting the client credential. The federated credential itself is valid.");
        }

        // (3) Credential fault — 700213 named explicitly (wrong-subject
        // signature, auth-v4 §11 invariant 1) so the operator inspects the
        // FIC subject rather than chasing propagation.
        string layer;
        if (errorCodes.Contains(WrongSubjectErrorCode))
        {
            layer = "AADSTS700213: no federated credential on this app matches the assertion's SUBJECT. " +
                    "The assertion was minted by a different identity than the one this credential trusts, " +
                    "or the credential's subject is not this UAMI's principalId (it may be the clientId — " +
                    "the designated silent-failure mode).";
        }
        else if (oauthError is not null && CredentialLayerErrors.Contains(oauthError, StringComparer.Ordinal))
        {
            layer = $"The OAuth2 error '{oauthError}' means the credential itself was rejected.";
        }
        else
        {
            layer = $"The OAuth2 error '{oauthError}' is not a known propagation or authorization-layer code, so it is treated as a credential fault.";
        }

        return new FicExchangeClassification(
            FicExchangeVerdict.RejectedCredentialFault,
            $"Entra REJECTED the assertion. {layer} This is not propagation — retrying would only delay the report.\n{errorBody}");
    }

    /// <summary>
    /// Drives repeated exchange attempts under the script's propagation-retry
    /// policy: retry ONLY on <see cref="FicExchangeVerdict.RetryPropagation"/>,
    /// 5s delay doubling capped at 30s, stop when <c>elapsed + nextDelay</c>
    /// would exceed <paramref name="maxWait"/> (script guard :758). Accepts a
    /// <see cref="TimeProvider"/> so tests exercise the ~130s flap window
    /// (auth-v4 §11 invariant 2) without real sleeps.
    /// </summary>
    public static async Task<FicExchangeVerificationResult> ExecuteWithPropagationRetryAsync(
        Func<CancellationToken, Task<FicExchangeAttempt>> exchangeAttempt,
        TimeSpan maxWait,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(exchangeAttempt);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var started = timeProvider.GetTimestamp();
        var delay = InitialDelay;
        var attempt = 0;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;

            var outcome = await exchangeAttempt(cancellationToken).ConfigureAwait(false);
            var classification = Classify(outcome);

            if (classification.Verdict == FicExchangeVerdict.Accepted)
            {
                return new FicExchangeVerificationResult(true, attempt, classification.Detail);
            }

            if (classification.Verdict == FicExchangeVerdict.RejectedCredentialFault)
            {
                return new FicExchangeVerificationResult(false, attempt, classification.Detail);
            }

            // RetryPropagation.
            var elapsed = timeProvider.GetElapsedTime(started);
            if (elapsed + delay > maxWait)
            {
                return new FicExchangeVerificationResult(
                    false, attempt,
                    $"A propagation-class error (AADSTS70021 / 70025) persisted for {(int)elapsed.TotalSeconds}s " +
                    $"across {attempt} attempt(s) (limit {(int)maxWait.TotalSeconds}s). The structural check " +
                    "already rules out a wrong subject (that surfaces as AADSTS700213, not this code — measured " +
                    "2026-08-21). Likeliest: propagation genuinely slower than the budget — re-run with a larger " +
                    "budget before investigating anything else.");
            }

            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            var doubled = delay * 2;
            delay = doubled < MaxDelay ? doubled : MaxDelay;
        }
    }

    private static (bool Parsed, string? OauthError, IReadOnlyList<int> ErrorCodes) ParseEntraErrorBody(string errorBody)
    {
        if (string.IsNullOrWhiteSpace(errorBody))
        {
            return (false, null, Array.Empty<int>());
        }

        try
        {
            using var doc = JsonDocument.Parse(errorBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return (false, null, Array.Empty<int>());
            }

            string? oauthError = null;
            if (doc.RootElement.TryGetProperty("error", out var errorProp)
                && errorProp.ValueKind == JsonValueKind.String)
            {
                oauthError = errorProp.GetString();
            }

            var codes = new List<int>();
            if (doc.RootElement.TryGetProperty("error_codes", out var codesProp)
                && codesProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in codesProp.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var code))
                    {
                        codes.Add(code);
                    }
                }
            }

            return (true, oauthError, codes);
        }
        catch (JsonException)
        {
            return (false, null, Array.Empty<int>());
        }
    }
}
