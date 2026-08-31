// -----------------------------------------------------------------------------
// PackagedScriptTenantLiteralInvariantVerifier.cs
//
// Real <see cref="IE2EInvariantVerifier"/> impl for the I1 branch only
// (§4D "no hardcoded tenant" — FR-28). Scans the DEPLOYED PowerShell provisioning
// scripts on-disk (i.e. the packaged content that ships with the L2 App Service
// publish output) for tenant-shaped GUID DEFAULTS on a <c>[string]$*Tenant*</c>
// Param() and returns:
//   * <see cref="InvariantVerificationOutcome.Passed"/>       — no offender file
//     found (and the scan actually ran against a non-empty scripts directory).
//   * <see cref="InvariantVerificationOutcome.Failed"/>       — one or more
//     offender files found. Diagnostic enumerates <c>{relPath}:{line}</c>
//     entries so the operator can jump straight to the offender.
//   * <see cref="InvariantVerificationOutcome.InfraFault"/>   — the scripts
//     directory does not exist / contained no PS1 files / an IO failure
//     prevented the scan from running. Handler classifies Resumable.
//
// SCOPE (task 170, POML "I1 first"):
//   Only I1 is verified with real logic here. The other four invariants
//   (I2 AI Search filter · I3 Cosmos partition key · I4 SPE container resolver ·
//   I5 Graph token tenant) each require their OWN wave-G7 sibling task
//   (173 / 174 / 176 / 179) to author their real verifier. Until those land,
//   THIS class returns the SAME <see cref="InvariantVerificationOutcome.InfraFault"/>
//   the retired <see cref="PlaceholderInvariantVerifier"/> returned — the
//   H13 handler still classifies Resumable for those four invariants,
//   unchanged from prior behavior.
//
// PATTERN PARITY (per POML constraint):
//   The regex + Param-block extraction shape MIRRORS the Wave-C6 ArchTest
//   <c>I1_NoHardcodedTenantTests.cs</c> (task 066) — but that ArchTest scans
//   the REPO source tree (<c>scripts/</c> in git), whereas THIS verifier scans
//   the RUNTIME payload (<c>scripts/</c> in the L2 App Service publish output).
//   The two enforcement points complement each other:
//     * ArchTest catches offenders at CI time (source scan).
//     * H13 runtime probe catches offenders at acceptance-gate time (payload
//       scan) — closes the "someone bypassed CI + published a hand-modified
//       script" residual risk. See design.md §4D I1 rationale.
//   The scan logic is intentionally re-implemented in production code (not
//   referenced from the test assembly) so the L2 runtime does not depend on
//   any test project — see notes/task-170-i1-runtime-probe-design.md.
//
// COMPONENT JUSTIFICATION (CLAUDE.md §11):
//   Existing:      <see cref="PlaceholderInvariantVerifier"/> is the ONLY
//                  IE2EInvariantVerifier impl; returns InfraFault for every
//                  invariant unconditionally — the H13 acceptance gate cannot
//                  return Success until at least one probe is real.
//   Extension:     This class is a drop-in replacement for the placeholder
//                  (same interface, same wire contract, same DI registration
//                  slot). Wave-G7 sibling tasks (173/174/176/179) will each
//                  swap in ONE more real probe by extending THIS class or
//                  composing over it (chosen at sibling-task authoring time).
//   Cost-of-doing-nothing: without a real I1 probe, H13 can never classify
//                  the invariant catalog as green — Setup Status transitions
//                  to Ready are IMPOSSIBLE (POML "acceptance-target transition
//                  is blocked on EVERY probe individually landing").
//
// ADR ALIGNMENT:
//   * ADR-010 (DI minimalism): still ≥2 impls of IE2EInvariantVerifier — this
//     production class + the FakeInvariantVerifier used by H13 handler tests.
//   * ADR-032 (unconditional registration): E2EAcceptanceModule swaps the
//     concrete without introducing a feature-flag branch.
//   * ADR-004 (job contract): H13 handler surface unchanged — this class is
//     purely a swap of the collaborator that fills the IE2EInvariantVerifier
//     slot.
//   * ADR-038 (integration-heavy test pyramid): all unit tests in this class's
//     test file exercise the scan via temp-directory .ps1 files (real IO
//     against a real ephemeral directory) — matching the ArchTest's own
//     regression-seed pattern in <c>I1_NoHardcodedTenantTests.
//     ScanForI1Offenders_TempScriptWithTenantDefault_ReportsFileAndLine</c>.
//
// SILENT-FAIL DISCIPLINE (Waves G-4..G-6 pattern applied proactively):
//   * Missing scripts directory   → InfraFault (LOUD; not silent-Pass).
//   * Zero *.ps1 files under dir  → InfraFault (LOUD; not silent-Pass —
//                                   a scan that observed no candidates is
//                                   NOT evidence of compliance).
//   * IO exception during scan    → InfraFault naming the offending file
//                                   (LOUD; not silent-Pass — a partial scan
//                                   is not evidence of compliance).
//   * Regex catches an offender   → Failed (LOUD; §4D CATASTROPHIC).
// -----------------------------------------------------------------------------

using System.Text.RegularExpressions;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <summary>
/// Real <see cref="IE2EInvariantVerifier"/> impl for I1 (no-hardcoded-tenant
/// scripts). Scans the packaged provisioning scripts on-disk. I2-I5 still
/// return <see cref="InvariantVerificationOutcome.InfraFault"/> pending their
/// own real-verifier tasks — behavior unchanged from
/// <see cref="PlaceholderInvariantVerifier"/> for those four.
/// </summary>
public sealed class PackagedScriptTenantLiteralInvariantVerifier : IE2EInvariantVerifier
{
    /// <summary>
    /// Diagnostic string used for I2-I5 InfraFault outcomes — verbatim carry-
    /// over from <see cref="PlaceholderInvariantVerifier"/> so operators see
    /// the SAME deferral message they saw pre-swap for the four invariants
    /// whose real verifier has not yet landed. Only the trailing task-list
    /// pointer differs so the operator knows which sibling task lands each.
    /// </summary>
    internal const string DeferralDiagnosticI2ThroughI5 =
        "H13 invariant live-probe not yet wired in L2 (task 170 landed the I1 " +
        "packaged-scripts on-disk probe; I2-I5 land under Wave G-7 sibling " +
        "tasks 173 / 174 / 176 / 179). Handler returns Resumable for this " +
        "invariant — swap DI registration once the sibling probe seam lands.";

    /// <summary>
    /// GUID shape as it would appear in a PowerShell string literal default
    /// value. Case-insensitive so upper- and lower-case hex both match.
    /// Mirrors the Wave-C6 ArchTest regex verbatim (task 066).
    /// </summary>
    private const string GuidPattern =
        @"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}";

    /// <summary>
    /// Matches a single tenant-shaped parameter DEFAULT inside a PowerShell
    /// Param block. Captured groups:
    ///  (1) Parameter name (drops the leading <c>$</c>).
    ///  (2) GUID literal that was assigned as the default.
    ///
    /// Deliberately narrowed to <c>[string]</c>-typed parameters whose name
    /// contains <c>Tenant</c> (case-insensitive) — same false-positive-avoidance
    /// discipline the ArchTest uses (subscription / RG / principal IDs can
    /// legitimately be per-env defaults on non-tenant parameters).
    /// </summary>
    private static readonly Regex TenantIdDefaultInParam = new(
        @"\[string\]\s*\$(\w*[Tt]enant\w*)\s*=\s*['""](" + GuidPattern + @")['""]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    /// <summary>
    /// Matches a PowerShell <c>param(</c> block opening — we only scan inside
    /// these blocks; function-body variable assignments outside <c>param(</c>
    /// are ignored (false-positive discipline mirrored from the ArchTest).
    /// </summary>
    private static readonly Regex ParamBlock = new(
        @"(?ms)\bparam\s*\(",
        RegexOptions.Compiled);

    private readonly ILogger<PackagedScriptTenantLiteralInvariantVerifier> _logger;

    public PackagedScriptTenantLiteralInvariantVerifier(
        ILogger<PackagedScriptTenantLiteralInvariantVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<InvariantCatalogVerificationResult> VerifyAllAsync(
        InvariantVerificationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var i1Outcome = ProbeI1(request.ProvisioningScriptsDirectory);

        // I2-I5 remain deferred to their own sibling probe tasks.
        var outcomes = new InvariantVerificationOutcome[]
        {
            i1Outcome,
            new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I2AiSearchTenantFilter, DeferralDiagnosticI2ThroughI5),
            new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I3CosmosPartitionKey, DeferralDiagnosticI2ThroughI5),
            new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I4SpeContainerResolver, DeferralDiagnosticI2ThroughI5),
            new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I5GraphTokenTenant, DeferralDiagnosticI2ThroughI5),
        };

        _logger.LogInformation(
            "H13 I1 packaged-scripts probe: customerId={CustomerId} runId={RunId} " +
            "scriptsDir={ScriptsDir} outcome={Outcome}",
            request.CustomerId, request.RunId, request.ProvisioningScriptsDirectory,
            i1Outcome.GetType().Name);

        return Task.FromResult(new InvariantCatalogVerificationResult(outcomes));
    }

    /// <summary>
    /// Runs the I1 grep scan against <paramref name="scriptsDirectory"/>.
    /// Internal so unit tests can exercise the probe without constructing a
    /// full <see cref="InvariantVerificationRequest"/>.
    /// </summary>
    internal static InvariantVerificationOutcome ProbeI1(string scriptsDirectory)
    {
        if (string.IsNullOrWhiteSpace(scriptsDirectory))
        {
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I1NoHardcodedTenant,
                "I1 probe skipped — ProvisioningScriptsDirectory was empty/whitespace " +
                "on the request. The extended-validate script probably could not resolve " +
                "AppContext.BaseDirectory/scripts. Handler classifies Resumable.");
        }
        if (!Directory.Exists(scriptsDirectory))
        {
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I1NoHardcodedTenant,
                $"I1 probe skipped — scripts directory does not exist at '{scriptsDirectory}'. " +
                "The L2 App Service publish output should contain a 'scripts/' folder alongside " +
                "the DLL bin. Handler classifies Resumable so the operator can investigate the " +
                "deploy layout without a false Pass.");
        }

        string[] candidateFiles;
        try
        {
            candidateFiles = Directory.GetFiles(scriptsDirectory, "*.ps1", SearchOption.AllDirectories);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I1NoHardcodedTenant,
                $"I1 probe skipped — enumerating '{scriptsDirectory}' threw " +
                $"{ex.GetType().Name}: {ex.Message}. Handler classifies Resumable.");
        }

        if (candidateFiles.Length == 0)
        {
            // Empty scripts/ is a suspicious deploy layout (the L2 publish should always
            // ship at least Provision-Customer.ps1 and its callees). Fail-LOUD — a scan
            // that observed zero candidates is NOT evidence of compliance.
            return new InvariantVerificationOutcome.InfraFault(
                InvariantKind.I1NoHardcodedTenant,
                $"I1 probe skipped — scripts directory '{scriptsDirectory}' exists but " +
                "contains ZERO .ps1 files. A packaged L2 publish should always contain " +
                "provisioning scripts (Provision-Customer.ps1 + callees). Handler classifies " +
                "Resumable so the operator investigates the deploy layout instead of " +
                "silently accepting a hollow scan.");
        }

        var offenders = new List<string>();
        foreach (var file in candidateFiles)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return new InvariantVerificationOutcome.InfraFault(
                    InvariantKind.I1NoHardcodedTenant,
                    $"I1 probe aborted — reading '{RelativizeUnder(scriptsDirectory, file)}' threw " +
                    $"{ex.GetType().Name}: {ex.Message}. A partial scan is NOT evidence of " +
                    "compliance — handler classifies Resumable so the operator investigates.");
            }

            var paramMatches = ParamBlock.Matches(text);
            if (paramMatches.Count == 0) continue;

            foreach (Match paramMatch in paramMatches)
            {
                var paramBlockText = ExtractBalancedParamBlock(text, paramMatch.Index);
                if (paramBlockText is null) continue;

                foreach (Match hit in TenantIdDefaultInParam.Matches(paramBlockText))
                {
                    var lineNumber = LineNumberFor(text, paramMatch.Index + hit.Index);
                    var rel = RelativizeUnder(scriptsDirectory, file);
                    var paramName = hit.Groups[1].Value;
                    var guid = hit.Groups[2].Value;

                    offenders.Add(
                        $"{rel}:{lineNumber} — Param [string]${paramName} = '{guid}' " +
                        $"(GUID-shaped tenant DEFAULT; §4D I1 / FR-28 CATASTROPHIC).");
                }
            }
        }

        if (offenders.Count == 0)
        {
            return new InvariantVerificationOutcome.Passed(InvariantKind.I1NoHardcodedTenant);
        }

        var diag =
            $"§4D I1 CATASTROPHIC — packaged provisioning script(s) under '{scriptsDirectory}' " +
            $"contain a tenant-shaped GUID default on a [string]$*Tenant* Param. Running any " +
            $"such script for a new customer without an explicit -TenantId would provision " +
            $"against the WRONG tenant (cross-tenant identity leak). " +
            $"Remove the default; mark the parameter [Parameter(Mandatory=$true)]. " +
            $"Offenders ({offenders.Count}): " +
            string.Join(" | ", offenders.OrderBy(o => o, StringComparer.Ordinal));
        return new InvariantVerificationOutcome.Failed(InvariantKind.I1NoHardcodedTenant, diag);
    }

    /// <summary>
    /// Extracts the text between the opening <c>(</c> of a <c>param(...)</c>
    /// block and its matching closing <c>)</c>, tracking paren depth so
    /// nested parentheses do not terminate the block prematurely. Returns
    /// <c>null</c> if no balancing paren is found (malformed script).
    /// Mirrors the ArchTest's own helper verbatim (task 066).
    /// </summary>
    private static string? ExtractBalancedParamBlock(string source, int paramStart)
    {
        var openParen = source.IndexOf('(', paramStart);
        if (openParen < 0) return null;

        int depth = 1;
        for (int i = openParen + 1; i < source.Length; i++)
        {
            switch (source[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    depth--;
                    if (depth == 0) return source[(openParen + 1)..i];
                    break;
            }
        }
        return null;
    }

    private static int LineNumberFor(string source, int charIndex)
    {
        if (charIndex < 0 || charIndex > source.Length) return 1;
        int line = 1;
        for (int i = 0; i < charIndex; i++)
        {
            if (source[i] == '\n') line++;
        }
        return line;
    }

    /// <summary>
    /// Reports the file path RELATIVE to the scripts root so the diagnostic
    /// stays operator-friendly (<c>Register-EntraAppRegistrations.ps1:63</c>)
    /// rather than dumping an absolute App Service publish path.
    /// </summary>
    private static string RelativizeUnder(string root, string absolutePath)
    {
        if (string.IsNullOrEmpty(root)) return absolutePath;
        var trimmedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (absolutePath.StartsWith(trimmedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return absolutePath.Substring(trimmedRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
        }
        if (absolutePath.StartsWith(trimmedRoot + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return absolutePath.Substring(trimmedRoot.Length + 1).Replace(Path.DirectorySeparatorChar, '/');
        }
        return absolutePath.Replace(Path.DirectorySeparatorChar, '/');
    }
}
