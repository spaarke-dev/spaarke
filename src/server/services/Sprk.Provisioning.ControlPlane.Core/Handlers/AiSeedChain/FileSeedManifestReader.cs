// -----------------------------------------------------------------------------
// FileSeedManifestReader.cs
//
// Production <see cref="ISeedManifestReader"/> implementation — reads the
// on-disk manifest.yaml, computes its SHA-256 content hash, and scans the
// body for retired-artifact patterns as a defense-in-depth layer beneath
// task 069's Invoke-SeedManifest.ps1 <c>retiredArtifacts</c> enforcement.
//
// DESIGN NOTES:
//   1. Hash is computed over the RAW BYTES (not the parsed structure), so a
//      whitespace-only change forces a new idempotency key. This matches the
//      POML constraint "a change to the manifest changes the key and forces
//      a re-seed on next invocation" without introducing a YAML parser
//      dependency.
//   2. The retired-artifact scan intentionally EXCLUDES the governance-only
//      `retiredArtifacts:` section (which enumerates the forbidden tokens
//      BY DESIGN). The scan enters "in-retired-section" mode when it sees the
//      section header + exits when a non-indented key follows. Only ARTIFACT
//      declarations (top-level `- id:` blocks inside `artifacts:`) are
//      inspected for retired-artifact tokens.
//   3. NO YAML parser dependency. Scanning bytes + line-oriented text is
//      sufficient for both the hash (opaque bytes) and the retired-artifact
//      defense-in-depth check (literal substrings). Task 069's PS orchestrator
//      already runs the full-fidelity YAML retired-artifact scan; this layer
//      is a belt-and-braces string scan against ADR-039 amendment 2026-07-05
//      violations that survive a hand-edit.
// -----------------------------------------------------------------------------

using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <inheritdoc/>
public sealed class FileSeedManifestReader : ISeedManifestReader
{
    private readonly AiSeedChainOptions _options;
    private readonly ILogger<FileSeedManifestReader> _logger;

    /// <summary>Constructs the reader bound to the manifest path in <see cref="AiSeedChainOptions"/>.</summary>
    public FileSeedManifestReader(
        IOptions<AiSeedChainOptions> options,
        ILogger<FileSeedManifestReader> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SeedManifestReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        var path = _options.ManifestPath;
        if (!File.Exists(path))
        {
            _logger.LogWarning(
                "H12a seed manifest not found at {ManifestPath}. Verify scripts/seed-data/ ships in the L2 publish output.",
                path);
            return new SeedManifestReadResult.NotFound(path);
        }

        var bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        var hash = ComputeSha256Hex(bytes);

        var text = System.Text.Encoding.UTF8.GetString(bytes);
        var violation = FindRetiredArtifactViolation(text, _options.RetiredArtifactPatterns);

        if (violation is not null)
        {
            _logger.LogError(
                "H12a manifest defense-in-depth check FAILED: retired-artifact pattern '{Pattern}' matched at line {LineNumber} of {ManifestPath}. Excerpt: {LineExcerpt}",
                violation.Pattern, violation.LineNumber, path, violation.LineExcerpt);
        }

        return new SeedManifestReadResult.Success(hash, violation);
    }

    /// <summary>
    /// Exposed for unit tests — computes the lowercase-hex SHA-256 of the input
    /// byte sequence. Deterministic; identical bytes always produce identical
    /// hash. Same shape as the H2a <c>BuildIdempotencyKey</c> helper.
    /// </summary>
    public static string ComputeSha256Hex(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(bytes, hash);
        return Convert.ToHexString(hash).ToLower(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Exposed for unit tests — line-oriented scan for a retired-artifact
    /// pattern within an ARTIFACT declaration. Skips the governance-only
    /// <c>retiredArtifacts:</c> section (which enumerates the forbidden tokens
    /// BY DESIGN — inspecting it would produce a false positive on every
    /// well-formed manifest).
    /// </summary>
    /// <remarks>
    /// State machine:
    /// <list type="bullet">
    /// <item>Line matches <c>retiredArtifacts:</c> → enter skip mode.</item>
    /// <item>In skip mode: a line with NO leading whitespace + <c>:</c> exits skip mode (next top-level key).</item>
    /// <item>Outside skip mode: scan for each pattern (case-insensitive substring).</item>
    /// </list>
    /// The first hit short-circuits and returns; the caller reports one
    /// violation at a time (fixing the first typically surfaces the next on
    /// re-run — parity with H2a's fail-fast pre-flight branches).
    /// </remarks>
    public static RetiredArtifactViolation? FindRetiredArtifactViolation(
        string manifestText,
        IEnumerable<string> patterns)
    {
        ArgumentNullException.ThrowIfNull(manifestText);
        ArgumentNullException.ThrowIfNull(patterns);

        var patternList = patterns.Where(p => !string.IsNullOrWhiteSpace(p)).ToArray();
        if (patternList.Length == 0)
        {
            return null;
        }

        var lines = manifestText.Split('\n');
        var inRetiredSection = false;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var trimmedStart = line.TrimStart();

            // Enter skip mode when we see the retiredArtifacts: section header.
            if (trimmedStart.StartsWith("retiredArtifacts:", StringComparison.Ordinal))
            {
                inRetiredSection = true;
                continue;
            }

            // Exit skip mode when a new TOP-LEVEL key appears (no leading whitespace + colon).
            // Comment lines (starting with #) and blank lines do NOT count as key boundaries.
            if (inRetiredSection)
            {
                if (line.Length > 0
                    && !char.IsWhiteSpace(line[0])
                    && !line.StartsWith("#", StringComparison.Ordinal)
                    && line.Contains(':', StringComparison.Ordinal))
                {
                    inRetiredSection = false;
                    // Fall through to normal scan of THIS line.
                }
                else
                {
                    // Still inside the governance section — skip.
                    continue;
                }
            }

            // Skip comment lines outside the retired section — they explain intent
            // but MUST NOT be treated as declarations. This also lets the manifest
            // reference retired-artifact names in header docstrings without tripping
            // the scan (e.g. task 069's file header mentions dispatcher retirement).
            if (trimmedStart.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var pattern in patternList)
            {
                if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    var excerpt = line.Length > 200 ? line[..200] + "..." : line;
                    return new RetiredArtifactViolation(
                        Pattern: pattern,
                        LineNumber: i + 1,
                        LineExcerpt: excerpt.Trim());
                }
            }
        }

        return null;
    }
}
