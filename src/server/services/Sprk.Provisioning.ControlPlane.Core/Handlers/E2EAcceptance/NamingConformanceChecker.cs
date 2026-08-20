// -----------------------------------------------------------------------------
// NamingConformanceChecker.cs
//
// Pure-C# port of scripts/naming-conformance-check.ps1 (r3 task 063) -- the
// KV-secret / Azure-resource naming-conformance gate. ZERO Azure SDK / REST /
// ProcessStartInfo / shell-out dependency: the entire check is a local file
// read + regex evaluation, mirroring the ps1 rules verbatim.
//
// TASK: 182 (Phase C'' Wave G-7 Batch G-7A1). Ports NamingConformanceScriptRunner
// to satisfy DS-4 section 6 (this script has 0 az/REST calls -- pure convention
// checks) so H13 can execute under Option D's zero-shell L2 Worker host without
// a pwsh prerequisite. The canonical rules live in
// docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md section "KV-Secret &
// Resource Naming Standard (Conformance-Gated)"; the .ps1 script remains on
// disk as the r3-owned CI-time gate (see .github/workflows/sdap-ci.yml
// naming-conformance step + .github/workflows/deploy-bff-api.yml Gate step) --
// this file is the PROVISIONING-time (H13) sibling with identical rule
// semantics.
//
// RULES PORTED (highest-signal first -- parity with the ps1 header):
//   R1  env-token-in-name    A KV-secret name contains an environment token
//                            (DEV/DEMO/PROD/PRODUCTION/UAT/TEST/STAGING/STAGE/
//                            SANDBOX/QA), matched as a WHOLE WORD after
//                            splitting the name on both delimiters (-_.) AND
//                            camelCase boundaries. Catches SPRK-DEV-DATAVERSE-
//                            URL (kebab) + ProdBffSecret (camelCase) while NOT
//                            false-flagging Development (word Development !=
//                            token DEV).
//   R2  casing-drift         The SAME logical secret appears under >1 casing
//                            across scanned files (e.g. BFF-API-ClientSecret
//                            vs bff-api-client-secret) -- a rotation hazard.
//   R3  vault-name-drift     A vault name other than the canonical form
//                            sprk-{env}-kv appears, EXCEPT the codified legacy
//                            dev exception spaarke-spekvcert (DO-NOT-RENAME
//                            per naming doc).
//
// SCAN SCOPE (parity with the ps1's default enumeration):
//   - src/server/api/Sprk.Bff.Api/appsettings.template.json
//   - src/server/api/Sprk.Bff.Api/appsettings.tokens.md
//   - scripts/Seed-ProductionKeyVault.ps1
//   - config/environments.json
//   - all *.bicep files under infrastructure/bicep/ (recursive)
//   The scan root is derived from H13AcceptanceOptions.NamingConformanceScriptPath
//   by walking up ONE level from its directory (i.e. <root>/scripts/*.ps1 =>
//   <root>) -- parity with the ps1's Split-Path -Parent $PSScriptRoot. This
//   keeps ONE configuration knob for both the retired ps1 runner (still on
//   disk, unregistered) and this pure-C# checker; a live L2 publish overrides
//   E2EAcceptance:NamingConformanceScriptPath to the App Service publish
//   layout and this checker picks up the same root.
//
// FAIL-CLOSED (parity with the ps1's "no scannable files" branch): if the
// scan enumeration returns zero readable files, this returns
// NamingConformanceOutcome.Failure with a sentinel exit code -- a moved /
// renamed input MUST NOT silently greenlight the gate.
//
// COMPONENT JUSTIFICATION (CLAUDE.md section 11):
//   Existing:   The convention rules already exist, correctly, in
//               naming-conformance-check.ps1 (r3 task 063) + AZURE-RESOURCE-
//               NAMING-CONVENTION.md -- this file mirrors both.
//   Extension:  A trivial mechanical translation. The .ps1 stays as the CI-
//               time enforcement surface; this handles the runtime H13 call
//               without a pwsh dependency in the L2 Worker's publish. No
//               redesign, no new rules -- 1:1 port.
//   Cost of doing nothing: H13's naming-conformance gate (spec.md SC #17) is
//               unenforceable under DS-4 section 6's zero-shell target state;
//               a live customer environment could transition to Setup Status
//               = Ready with env-token-baked secret names still present.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="INamingConformanceChecker"/>
public sealed class NamingConformanceChecker : INamingConformanceChecker
{
    /// <summary>
    /// Env tokens that MUST NOT appear as a whole word inside a replicated
    /// KV-secret name. Mirrors <c>$EnvTokens</c> in naming-conformance-check.ps1.
    /// </summary>
    private static readonly IReadOnlyList<string> EnvTokens = new[]
    {
        "DEV", "DEMO", "PROD", "PRODUCTION", "UAT", "TEST",
        "STAGING", "STAGE", "SANDBOX", "QA",
    };

    /// <summary>
    /// Codified legacy exception (DO-NOT-RENAME per naming doc): the only live
    /// dev vault. Mirrors <c>$VaultLegacyException</c> in the ps1.
    /// </summary>
    private const string VaultLegacyException = "spaarke-spekvcert";

    // ------- Regex library (compiled -- one-time init, hot per-request). ------
    // Canonical vault name: sprk-{env}-kv. Mirrors $CanonicalVaultRegex.
    private static readonly Regex CanonicalVaultRegex =
        new("^sprk-[a-z0-9]+-kv$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Secret-name extractors -- one pair matches @Microsoft.KeyVault(...SecretName=X),
    // the other matches YAML/JSON/PowerShell key=value forms with quoted value.
    private static readonly Regex SecretNameKvRef =
        new(@"SecretName=([A-Za-z0-9._-]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SecretNameKeyValue =
        new(@"(?:SecretName|-Name)\s*[:=]\s*[""']([A-Za-z0-9._-]+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Vault-name extractors -- parity split with the secret-name pair.
    private static readonly Regex VaultNameKvRef =
        new(@"VaultName=([A-Za-z0-9-]+)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex VaultNameKeyValue =
        new(@"vault[Nn]ame\s*[:=]\s*[""']([A-Za-z0-9-]+)[""']",
            RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Word-splitter: uppercase run (not followed by lowercase) | PascalCase word |
    // lowercase run | digit run. Parity with the ps1's word regex.
    private static readonly Regex WordSplit =
        new(@"[A-Z]+(?![a-z])|[A-Z][a-z]+|[a-z]+|[0-9]+",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Relative paths of the "explicit" canonical secret-name sources -- parity
    // with the ps1's $explicit array.
    private static readonly IReadOnlyList<string> ExplicitScanPaths = new[]
    {
        Path.Combine("src", "server", "api", "Sprk.Bff.Api", "appsettings.template.json"),
        Path.Combine("src", "server", "api", "Sprk.Bff.Api", "appsettings.tokens.md"),
        Path.Combine("scripts", "Seed-ProductionKeyVault.ps1"),
        Path.Combine("config", "environments.json"),
    };

    /// <summary>Relative path (from scan root) of the recursive Bicep scan directory.</summary>
    private const string BicepScanRelativeDirectory = "infrastructure/bicep";

    /// <summary>Sentinel exit code used to classify domain failures -- parity with the ps1's `exit 1`.</summary>
    private const int DomainFailureExitCode = 1;

    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<NamingConformanceChecker> _logger;

    public NamingConformanceChecker(
        IOptions<H13AcceptanceOptions> options,
        ILogger<NamingConformanceChecker> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<NamingConformanceOutcome> CheckAsync(
        NamingConformanceRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var scanRoot = ResolveScanRoot();
        _logger.LogInformation(
            "H13 naming-conformance (pure-C#) scanning: scanRoot={ScanRoot} customerId={CustomerId} runId={RunId}",
            scanRoot, request.CustomerId, request.RunId);

        var fileToText = LoadScanTargets(scanRoot, cancellationToken);
        if (fileToText.Count == 0)
        {
            // FAIL-CLOSED: parity with the ps1's "no scannable files" branch --
            // a naming gate that finds none of its expected inputs must NOT
            // silently greenlight. Reported as a domain Failure (exit 1) so
            // H13 classifies as QuarantineRequired via NamingConformanceFailed.
            var diag =
                $"No scannable files found under '{scanRoot}'. The canonical secret-name " +
                "sources (appsettings.template.json / appsettings.tokens.md / Seed-ProductionKeyVault.ps1 / " +
                "environments.json / infrastructure/bicep) are missing or were renamed; the gate " +
                "cannot verify conformance.";
            _logger.LogWarning("H13 naming-conformance FAIL-CLOSED: {Diagnostic}", diag);
            return Task.FromResult<NamingConformanceOutcome>(
                new NamingConformanceOutcome.Failure(DomainFailureExitCode, diag));
        }

        var violations = ScanForViolations(fileToText);
        if (violations.Count == 0)
        {
            _logger.LogInformation(
                "H13 naming-conformance PASS (pure-C#): {FileCount} file(s) scanned, 0 violations.",
                fileToText.Count);
            return Task.FromResult<NamingConformanceOutcome>(new NamingConformanceOutcome.Success());
        }

        var diagnostic = FormatViolations(violations, fileToText.Count);
        _logger.LogWarning("H13 naming-conformance FAIL (pure-C#): {Diagnostic}", diagnostic);
        return Task.FromResult<NamingConformanceOutcome>(
            new NamingConformanceOutcome.Failure(DomainFailureExitCode, diagnostic));
    }

    /// <summary>
    /// Derives the scan root from <see cref="H13AcceptanceOptions.NamingConformanceScriptPath"/>
    /// by walking up ONE level from its directory (parity with the ps1's
    /// <c>Split-Path -Parent $PSScriptRoot</c>). Falls back to
    /// <see cref="AppContext.BaseDirectory"/> when the option is blank or points
    /// to a path without a computable parent (which shouldn't happen in a
    /// deployed publish but is defended against for test hosts).
    /// </summary>
    internal string ResolveScanRoot()
    {
        var scriptPath = _options.NamingConformanceScriptPath;
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            return AppContext.BaseDirectory;
        }
        var scriptDir = Path.GetDirectoryName(scriptPath);
        if (string.IsNullOrWhiteSpace(scriptDir))
        {
            return AppContext.BaseDirectory;
        }
        var repoRoot = Path.GetDirectoryName(scriptDir);
        return string.IsNullOrWhiteSpace(repoRoot) ? AppContext.BaseDirectory : repoRoot;
    }

    /// <summary>
    /// Enumerates + reads the scan targets. Parity with the ps1's default $Path
    /// enumeration: 4 explicit canonical sources + all *.bicep under
    /// infrastructure/bicep/ (recursive).
    /// </summary>
    internal static IReadOnlyDictionary<string, string> LoadScanTargets(
        string scanRoot, CancellationToken cancellationToken)
    {
        var fileToText = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rel in ExplicitScanPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var abs = Path.Combine(scanRoot, rel);
            if (File.Exists(abs))
            {
                fileToText[abs] = File.ReadAllText(abs) ?? string.Empty;
            }
        }

        var infraDir = Path.Combine(scanRoot, BicepScanRelativeDirectory);
        if (Directory.Exists(infraDir))
        {
            foreach (var bicep in Directory.EnumerateFiles(infraDir, "*.bicep", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                fileToText[bicep] = File.ReadAllText(bicep) ?? string.Empty;
            }
        }

        return fileToText;
    }

    /// <summary>
    /// Applies rules R1 (env-token-in-name), R2 (casing-drift), R3
    /// (vault-name-drift) to the provided file corpus. Exposed <c>internal</c>
    /// so table-driven unit tests can drive it directly against in-memory
    /// fixtures without going through the filesystem.
    /// </summary>
    internal static IReadOnlyList<NamingViolation> ScanForViolations(
        IReadOnlyDictionary<string, string> fileToText)
    {
        var violations = new List<NamingViolation>();
        // R2 collector: lowercase key -> list of (actual casing, source file).
        var seenByCanonical = new Dictionary<string, List<(string Actual, string File)>>(
            StringComparer.OrdinalIgnoreCase);
        // R3 collector.
        var vaultNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kvp in fileToText)
        {
            var file = kvp.Key;
            var text = kvp.Value;
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            // R1 + collect for R2.
            foreach (var name in ExtractSecretNames(text))
            {
                var envToken = FindEnvToken(name);
                if (envToken is not null)
                {
                    violations.Add(new NamingViolation(
                        Rule: "R1 env-token-in-name",
                        Name: name,
                        File: file,
                        Detail: $"contains env token '{envToken}'"));
                }
                var key = name.ToLowerInvariant();
                if (!seenByCanonical.TryGetValue(key, out var occurrences))
                {
                    occurrences = new List<(string, string)>();
                    seenByCanonical[key] = occurrences;
                }
                occurrences.Add((name, file));
            }

            // R3 vault name collection.
            foreach (Match m in VaultNameKvRef.Matches(text))
            {
                vaultNames.Add(m.Groups[1].Value);
            }
            foreach (Match m in VaultNameKeyValue.Matches(text))
            {
                vaultNames.Add(m.Groups[1].Value);
            }
        }

        // R2 casing-drift: same lowercased name under >1 distinct actual casing.
        foreach (var kvp in seenByCanonical)
        {
            var distinct = kvp.Value
                .Select(o => o.Actual)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (distinct.Length > 1)
            {
                violations.Add(new NamingViolation(
                    Rule: "R2 casing-drift",
                    Name: string.Join(" | ", distinct),
                    File: "(multiple)",
                    Detail: $"one logical secret under {distinct.Length} casings"));
            }
        }

        // R3 vault-name-drift.
        foreach (var v in vaultNames)
        {
            if (string.Equals(v, VaultLegacyException, StringComparison.Ordinal))
            {
                continue;
            }
            if (!CanonicalVaultRegex.IsMatch(v))
            {
                violations.Add(new NamingViolation(
                    Rule: "R3 vault-name-drift",
                    Name: v,
                    File: "(vault ref)",
                    Detail:
                        $"not canonical 'sprk-{{env}}-kv' " +
                        $"(and not the codified '{VaultLegacyException}' dev exception)"));
            }
        }

        return violations;
    }

    private static IEnumerable<string> ExtractSecretNames(string text)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in SecretNameKvRef.Matches(text))
        {
            names.Add(m.Groups[1].Value);
        }
        foreach (Match m in SecretNameKeyValue.Matches(text))
        {
            names.Add(m.Groups[1].Value);
        }
        return names;
    }

    /// <summary>
    /// Returns the env token contained as a WHOLE WORD in <paramref name="name"/>
    /// or <c>null</c> if none. Word-splitting is identical to the ps1's regex so
    /// Development (word "Development") is NOT flagged as containing DEV.
    /// </summary>
    internal static string? FindEnvToken(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }
        // Materialize once -- the outer loop scans EnvTokens repeatedly.
        var words = WordSplit.Matches(name)
            .Select(m => m.Value.ToUpperInvariant())
            .ToArray();
        foreach (var tok in EnvTokens)
        {
            foreach (var w in words)
            {
                if (string.Equals(w, tok, StringComparison.Ordinal))
                {
                    return tok;
                }
            }
        }
        return null;
    }

    private static string FormatViolations(IReadOnlyList<NamingViolation> violations, int fileCount)
    {
        var sb = new StringBuilder();
        sb.Append("naming-conformance (pure-C#) FAIL -- ")
          .Append(violations.Count).Append(" violation(s) across ")
          .Append(fileCount).Append(" file(s): ");
        for (var i = 0; i < violations.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(" | ");
            }
            var v = violations[i];
            sb.Append('[').Append(v.Rule).Append("] ")
              .Append(v.Name).Append(" (").Append(v.File).Append(") -- ").Append(v.Detail);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Individual violation record -- mirrors the pscustomobject shape the ps1
    /// emits. Exposed <c>internal</c> so tests can assert against structured
    /// rule/name/file/detail rather than parsing the aggregate diagnostic string.
    /// </summary>
    internal sealed record NamingViolation(string Rule, string Name, string File, string Detail);
}
