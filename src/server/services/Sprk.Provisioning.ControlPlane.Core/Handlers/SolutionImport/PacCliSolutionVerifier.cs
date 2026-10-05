// -----------------------------------------------------------------------------
// PacCliSolutionVerifier.cs
//
// *** RETIRED (task 141, Wave G-4, Option D hybrid, 2026-08-20) ***
// No longer registered in Worker/Program.cs — superseded by
// DataverseWebApiSolutionVerifier.cs (a trivial pure-HttpClient GET
// /api/data/v9.2/solutions?$select=uniquename,version, replacing this class's
// `pac solution list` shell-out). Kept on disk UNREGISTERED per the Wave
// G-2/G-3/G-4 retirement convention. ParseListOutput remains unit-tested
// (H6SolutionImportHandlerTests T27) as a historical reference for the pac-
// CLI-era tabular-output parsing this class implemented; it is NOT reachable
// from any production code path.
//
// Production <see cref="ISolutionVerifier"/> implementation — shells out to
// `pac solution list --environment {envUrl}` and parses the tabular text
// output for the 9 authoritative solution unique-names + installed versions
// + solutionIds.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-09
//       acceptance: "Cosmos interStepState contains manifest of 8 imported
//       solutions with name + version + solutionId" (POML criterion 6).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H6:
//       Verification is the H6 post-condition gate — SUCCESS emitted only
//       when all 8 catalog entries are confirmed present.
//   - .claude/adr/ADR-028-spaarke-auth-architecture.md: pac CLI auth
//       delegates to the operator/UAMI credential chain (script re-uses the
//       auth profile created by the importer).
//
// NON-GOALS:
//   - Does NOT itself invoke `pac auth create` — assumes the importer just
//     did (verifier re-uses the freshly-created auth profile). If auth was
//     torn down between importer + verifier (rare), the pac call returns
//     an auth failure the handler classifies as VerificationFailed.
//   - Does NOT parse the Dataverse Web API `solutions` OData collection —
//     `pac solution list` is a thinner surface that includes version +
//     solutionId in the tabular output (PAC CLI ≥ 1.30).
//
// CI COVERAGE:
//   Not exercised by CI unit suite (pac CLI + real Dataverse). Env-guarded
//   smoke tests can be added; wave C4 uses handler unit tests via stubs.
// -----------------------------------------------------------------------------

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;

/// <summary>
/// Shells out to <c>pac solution list --environment {envUrl}</c> and parses
/// the tabular output for the 9 authoritative solution unique-names +
/// versions + solutionIds.
/// </summary>
public sealed class PacCliSolutionVerifier : ISolutionVerifier
{
    private const int StderrTailBudget = 800;
    private const int StdoutTailBudget = 800;

    // Parse `pac solution list` tabular output. Format varies by PAC version:
    //   ─────────────────────────────────────────────────────────────
    //   Unique Name                Friendly Name         Version    ...
    //   SpaarkeCore                Spaarke Core          1.0.0.0    ...
    //   SpaarkeWebResources        Spaarke Web Resources 1.0.0.0    ...
    // Some PAC versions include a Solution Id column; when present it's a
    // GUID after the version.
    //
    // Parsing strategy — token-based rather than positional regex: per line,
    // split on whitespace + scan tokens for the first-position name, the
    // first token matching a version pattern, and the first token matching
    // a GUID pattern. Robust against multi-word friendly names + column-width
    // drift across PAC versions. Rejects header / separator lines because
    // they contain no version token.
    private static readonly Regex s_versionTokenRegex = new(
        @"^\d+\.\d+\.\d+\.\d+$",
        RegexOptions.Compiled);

    private static readonly Regex s_guidTokenRegex = new(
        @"^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$",
        RegexOptions.Compiled);

    private static readonly char[] s_lineSplit = { '\r', '\n' };
    private static readonly char[] s_tokenSplit = { ' ', '\t' };

    private readonly SolutionImportOptions _options;
    private readonly ILogger<PacCliSolutionVerifier> _logger;

    /// <summary>Constructs the verifier bound to the pac CLI executable + per-call timeout.</summary>
    public PacCliSolutionVerifier(
        IOptions<SolutionImportOptions> options,
        ILogger<PacCliSolutionVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SolutionVerificationOutcome> VerifyAsync(
        SolutionVerificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TargetDataverseUrl);
        if (request.ExpectedCatalog.IsDefaultOrEmpty)
        {
            throw new ArgumentException(
                "ExpectedCatalog must be non-empty — verifier cannot check against an empty catalog.",
                nameof(request));
        }

        var psi = new ProcessStartInfo
        {
            FileName = _options.PacCliExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("solution");
        psi.ArgumentList.Add("list");
        psi.ArgumentList.Add("--environment");
        psi.ArgumentList.Add(request.TargetDataverseUrl);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        _logger.LogInformation(
            "H6 verifier starting pac solution list: envUrl={EnvUrl}",
            request.TargetDataverseUrl);

        var timedOut = false;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_options.VerifierCallTimeout);

            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                timedOut = true;
                TryKill(process);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw;
        }

        if (timedOut)
        {
            var diagnostic =
                $"pac solution list for '{request.TargetDataverseUrl}' timed out after {_options.VerifierCallTimeout}. " +
                $"Stderr so far: {Truncate(stderr.ToString(), StderrTailBudget)}";
            // Treat as ALL missing — the caller (handler) maps to VerificationFailed → QuarantineRequired.
            return new SolutionVerificationOutcome.Missing(
                request.ExpectedCatalog.Select(e => e.SolutionUniqueName).ToImmutableArray(),
                diagnostic);
        }

        var stdoutText = stdout.ToString();
        var stderrText = stderr.ToString();

        _logger.LogInformation(
            "H6 verifier pac solution list exited: envUrl={EnvUrl} exitCode={ExitCode}",
            request.TargetDataverseUrl, process.ExitCode);

        if (process.ExitCode != 0)
        {
            var diagnostic =
                $"pac solution list exited {process.ExitCode} against '{request.TargetDataverseUrl}'. " +
                $"Stderr: {Truncate(stderrText, StderrTailBudget)} Stdout: {Truncate(stdoutText, StdoutTailBudget)}";
            return new SolutionVerificationOutcome.Missing(
                request.ExpectedCatalog.Select(e => e.SolutionUniqueName).ToImmutableArray(),
                diagnostic);
        }

        return ParseListOutput(stdoutText, request.ExpectedCatalog);
    }

    /// <summary>
    /// Parses the tabular pac solution list output + cross-references against
    /// the expected catalog. Exposed <c>internal</c> for unit testing.
    /// Token-based (not positional) to survive column-width drift + multi-word
    /// friendly names.
    /// </summary>
    internal static SolutionVerificationOutcome ParseListOutput(
        string stdoutText,
        ImmutableArray<CanonicalSolutionEntry> expectedCatalog)
    {
        var installed = new Dictionary<string, (string Version, string SolutionId)>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in (stdoutText ?? string.Empty).Split(s_lineSplit, StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            // Reject pure-separator lines (dashes only).
            if (line.All(c => c == '-' || c == '─' || c == '=')) continue;

            var tokens = line.Split(s_tokenSplit, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) continue;

            // Version token: first token in "\d+\.\d+\.\d+\.\d+" form.
            var version = tokens.FirstOrDefault(t => s_versionTokenRegex.IsMatch(t));
            if (version is null) continue;   // header lines, prose, etc.

            // Name = first token on the row. This is the DATAVERSE UNIQUE NAME
            // per pac's tabular output. Multi-word friendly names occupy
            // subsequent tokens which we ignore.
            var name = tokens[0];

            // Optional solutionId — first GUID token on the row.
            var solutionId = tokens.FirstOrDefault(t => s_guidTokenRegex.IsMatch(t)) ?? string.Empty;

            // First match wins if PAC ever duplicates (defensive).
            installed.TryAdd(name, (version, solutionId));
        }

        var manifest = ImmutableArray.CreateBuilder<ImportedSolutionRecord>(expectedCatalog.Length);
        var missing = ImmutableArray.CreateBuilder<string>();

        foreach (var expected in expectedCatalog)
        {
            if (installed.TryGetValue(expected.SolutionUniqueName, out var found))
            {
                manifest.Add(new ImportedSolutionRecord(
                    SolutionUniqueName: expected.SolutionUniqueName,
                    Version: found.Version,
                    SolutionId: found.SolutionId,
                    Tier: expected.Tier));
            }
            else
            {
                missing.Add(expected.SolutionUniqueName);
            }
        }

        if (missing.Count > 0)
        {
            var diagnostic =
                $"pac solution list did not return the following expected solutions after import: " +
                $"{string.Join(", ", missing)}. Stdout tail: {Truncate(stdoutText, StdoutTailBudget)}";
            return new SolutionVerificationOutcome.Missing(missing.ToImmutable(), diagnostic);
        }

        return new SolutionVerificationOutcome.AllPresent(manifest.ToImmutable());
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Process already exited between HasExited check + Kill — race is benign.
        }
    }

    private static string Truncate(string? s, int max)
        => string.IsNullOrEmpty(s) || s.Length <= max
            ? s ?? string.Empty
            : s[..max] + "...[truncated]";
}
