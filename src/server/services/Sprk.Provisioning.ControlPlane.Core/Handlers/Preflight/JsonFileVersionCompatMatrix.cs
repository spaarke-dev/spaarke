// -----------------------------------------------------------------------------
// JsonFileVersionCompatMatrix.cs
//
// Production <see cref="IVersionCompatMatrix"/> (Wave G-8 Batch 10 — closes
// FR-34 defect #24). Loads the runtime JSON mirror of
// docs/deployment/version-compatibility-matrix.md and answers cell lookups
// for H0 upgrade-mode preflight.
//
// MATRIX SOURCE (two-tier, deterministic):
//   1. Explicit file path (ctor / `Preflight:VersionCompatMatrixPath` config)
//      — operator override for hotfix scenarios where re-shipping the L2
//      binary just to amend the matrix would be disproportionate.
//   2. Embedded resource `...Handlers.Preflight.version-compat-matrix.json`
//      (default) — ships INSIDE the Core assembly, so there is no publish
//      item-flow risk (NFR-05 class of failure: content files silently
//      missing from the publish output). Same precedent as the H12b
//      chart-def JSON mirrors (EmbeddedResource in Core.csproj).
//
// VALIDATION (fail-loud at first query, cached thereafter):
//   - matrixVersion non-empty; >= 1 cell; every cell has non-empty
//     bffVersion + solutionVersion + a parseable verdict; no duplicate
//     (bffVersion, solutionVersion) keys (case-insensitive).
//   - A corrupt/missing source throws InvalidOperationException — H0
//     catches it and fails the run Resumable with rejection code
//     `upgrade-compat-matrix-unavailable` (operator fixes the matrix file /
//     deployment + resumes; no external side effect has happened).
//
// QUERY SEMANTICS (matrix doc §2):
//   Verdict = cell (target BFF, target Solution-set). A target pair ABSENT
//   from the matrix is a Red result ("unknown pair = unsupported until the
//   release manager appends the cell per doc §6") — NOT an exception,
//   because it is a domain outcome the operator remediates by publishing
//   the cell, not an infrastructure fault. The CURRENT pair is checked for
//   presence too: absence only annotates the diagnostic (registry rows
//   predating matrix adoption), never changes the verdict.
// -----------------------------------------------------------------------------

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <inheritdoc cref="IVersionCompatMatrix"/>
public sealed class JsonFileVersionCompatMatrix : IVersionCompatMatrix
{
    /// <summary>Logical name of the embedded default matrix resource.</summary>
    internal const string EmbeddedResourceName =
        "Sprk.Provisioning.ControlPlane.Handlers.Preflight.version-compat-matrix.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string? _matrixFilePath;
    private readonly ILogger<JsonFileVersionCompatMatrix> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private LoadedMatrix? _loaded;

    /// <summary>
    /// Constructs the matrix service.
    /// </summary>
    /// <param name="matrixFilePath">
    /// Optional explicit path to a matrix JSON file (operator override /
    /// tests). Null/empty → the embedded default resource is used.
    /// </param>
    /// <param name="logger">Structured logger.</param>
    public JsonFileVersionCompatMatrix(
        string? matrixFilePath,
        ILogger<JsonFileVersionCompatMatrix> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _matrixFilePath = string.IsNullOrWhiteSpace(matrixFilePath) ? null : matrixFilePath;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<VersionCompatCheckResult> CheckPairAsync(
        VersionPair current,
        VersionPair target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.BffVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(target.SolutionVersion);

        var matrix = await LoadAsync(cancellationToken).ConfigureAwait(false);

        var currentKnown = matrix.Cells.ContainsKey(CellKey(current.BffVersion, current.SolutionVersion));
        var currentNote = currentKnown
            ? string.Empty
            : $" WARNING: the CURRENT pair (BFF {current.BffVersion} x {current.SolutionVersion}) is not " +
              $"present in matrix {matrix.MatrixVersion} — the registry row may predate matrix adoption or " +
              "the historical cell was never published (doc §6 forbids deleting historical cells).";

        if (!matrix.Cells.TryGetValue(CellKey(target.BffVersion, target.SolutionVersion), out var cell))
        {
            return new VersionCompatCheckResult(
                VersionCompatVerdict.Red,
                $"Target pair (BFF {target.BffVersion} x {target.SolutionVersion}) is NOT present in " +
                $"version-compat matrix {matrix.MatrixVersion} ({matrix.SourceDocument}) — an unknown pair is " +
                "UNSUPPORTED until the release manager appends the cell (doc §6 update cadence: matrix edit " +
                $"ships in the SAME commit as the release-tag). Current pair: BFF {current.BffVersion} x " +
                $"{current.SolutionVersion}.{currentNote}",
                Array.Empty<string>());
        }

        var diagnostic = cell.Verdict switch
        {
            VersionCompatVerdict.Green =>
                $"Version-compat matrix {matrix.MatrixVersion}: (BFF {target.BffVersion} x {target.SolutionVersion}) " +
                $"is Green — upgrade from (BFF {current.BffVersion} x {current.SolutionVersion}) proceeds normally." +
                currentNote,
            VersionCompatVerdict.Yellow =>
                $"Version-compat matrix {matrix.MatrixVersion}: (BFF {target.BffVersion} x {target.SolutionVersion}) " +
                $"is Yellow — compatible but REQUIRES the operator manual step(s) for " +
                $"[{string.Join(", ", cell.UcbClasses)}] per {matrix.SourceDocument} §5 before H2a/H6/H9 proceed. " +
                $"Current pair: BFF {current.BffVersion} x {current.SolutionVersion}." +
                (string.IsNullOrWhiteSpace(cell.Note) ? string.Empty : $" Cell note: {cell.Note}") +
                currentNote,
            _ =>
                $"Version-compat matrix {matrix.MatrixVersion}: (BFF {target.BffVersion} x {target.SolutionVersion}) " +
                $"is RED — incompatible pair, do NOT deploy. The customer (currently BFF {current.BffVersion} x " +
                $"{current.SolutionVersion}) requires an intermediate release first; see {matrix.SourceDocument} §5 " +
                $"remediation for [{string.Join(", ", cell.UcbClasses)}]." +
                (string.IsNullOrWhiteSpace(cell.Note) ? string.Empty : $" Cell note: {cell.Note}") +
                currentNote,
        };

        return new VersionCompatCheckResult(cell.Verdict, diagnostic, cell.UcbClasses);
    }

    private static string CellKey(string bffVersion, string solutionVersion)
        => $"{bffVersion.Trim()}|{solutionVersion.Trim()}".ToUpperInvariant();

    private async Task<LoadedMatrix> LoadAsync(CancellationToken cancellationToken)
    {
        if (_loaded is not null)
        {
            return _loaded;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_loaded is not null)
            {
                return _loaded;
            }

            MatrixDocument document;
            string source;
            if (_matrixFilePath is not null)
            {
                source = _matrixFilePath;
                if (!File.Exists(_matrixFilePath))
                {
                    throw new InvalidOperationException(
                        $"Version-compat matrix file not found at '{_matrixFilePath}' " +
                        "(Preflight:VersionCompatMatrixPath override). Fix the path or remove the override " +
                        "to fall back to the embedded default matrix.");
                }
                await using var fileStream = File.OpenRead(_matrixFilePath);
                document = await DeserializeAsync(fileStream, source, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                source = $"embedded:{EmbeddedResourceName}";
                await using var resourceStream =
                    typeof(JsonFileVersionCompatMatrix).GetTypeInfo().Assembly
                        .GetManifestResourceStream(EmbeddedResourceName)
                    ?? throw new InvalidOperationException(
                        $"Embedded version-compat matrix resource '{EmbeddedResourceName}' is missing from the " +
                        "Core assembly — verify the EmbeddedResource item in Sprk.Provisioning.ControlPlane.Core.csproj.");
                document = await DeserializeAsync(resourceStream, source, cancellationToken).ConfigureAwait(false);
            }

            _loaded = Validate(document, source);
            _logger.LogInformation(
                "Version-compat matrix loaded: source={Source} matrixVersion={MatrixVersion} cellCount={CellCount}",
                source, _loaded.MatrixVersion, _loaded.Cells.Count);
            return _loaded;
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private static async Task<MatrixDocument> DeserializeAsync(
        Stream stream, string source, CancellationToken cancellationToken)
    {
        try
        {
            return await JsonSerializer
                       .DeserializeAsync<MatrixDocument>(stream, SerializerOptions, cancellationToken)
                       .ConfigureAwait(false)
                   ?? throw new InvalidOperationException(
                       $"Version-compat matrix at '{source}' deserialized to null (empty document).");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Version-compat matrix at '{source}' is not valid JSON: {ex.Message}", ex);
        }
    }

    private static LoadedMatrix Validate(MatrixDocument document, string source)
    {
        if (string.IsNullOrWhiteSpace(document.MatrixVersion))
        {
            throw new InvalidOperationException(
                $"Version-compat matrix at '{source}' is missing 'matrixVersion'.");
        }
        if (document.Cells is null || document.Cells.Count == 0)
        {
            throw new InvalidOperationException(
                $"Version-compat matrix at '{source}' has no cells — at least the current baseline pair is required.");
        }

        var cells = new Dictionary<string, MatrixCell>(StringComparer.Ordinal);
        foreach (var raw in document.Cells)
        {
            if (string.IsNullOrWhiteSpace(raw.BffVersion) || string.IsNullOrWhiteSpace(raw.SolutionVersion))
            {
                throw new InvalidOperationException(
                    $"Version-compat matrix at '{source}' contains a cell with an empty bffVersion/solutionVersion.");
            }
            if (!Enum.TryParse<VersionCompatVerdict>(raw.Verdict, ignoreCase: true, out var verdict))
            {
                throw new InvalidOperationException(
                    $"Version-compat matrix at '{source}' cell (BFF {raw.BffVersion} x {raw.SolutionVersion}) has " +
                    $"unrecognized verdict '{raw.Verdict}' — expected Green | Yellow | Red.");
            }

            var key = CellKey(raw.BffVersion, raw.SolutionVersion);
            if (!cells.TryAdd(key, new MatrixCell(verdict, (IReadOnlyList<string>?)raw.UcbClasses ?? Array.Empty<string>(), raw.Note)))
            {
                throw new InvalidOperationException(
                    $"Version-compat matrix at '{source}' contains duplicate cell " +
                    $"(BFF {raw.BffVersion} x {raw.SolutionVersion}).");
            }
        }

        return new LoadedMatrix(
            document.MatrixVersion,
            string.IsNullOrWhiteSpace(document.SourceDocument)
                ? "docs/deployment/version-compatibility-matrix.md"
                : document.SourceDocument,
            cells);
    }

    private sealed record LoadedMatrix(
        string MatrixVersion,
        string SourceDocument,
        IReadOnlyDictionary<string, MatrixCell> Cells);

    private sealed record MatrixCell(
        VersionCompatVerdict Verdict,
        IReadOnlyList<string> UcbClasses,
        string? Note);

    /// <summary>JSON document shape of version-compat-matrix.json.</summary>
    private sealed class MatrixDocument
    {
        [JsonPropertyName("matrixVersion")]
        public string? MatrixVersion { get; set; }

        [JsonPropertyName("sourceDocument")]
        public string? SourceDocument { get; set; }

        [JsonPropertyName("cells")]
        public List<MatrixCellDocument>? Cells { get; set; }
    }

    /// <summary>JSON shape of one matrix cell.</summary>
    private sealed class MatrixCellDocument
    {
        [JsonPropertyName("bffVersion")]
        public string? BffVersion { get; set; }

        [JsonPropertyName("solutionVersion")]
        public string? SolutionVersion { get; set; }

        [JsonPropertyName("verdict")]
        public string? Verdict { get; set; }

        [JsonPropertyName("ucbClasses")]
        public List<string>? UcbClasses { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }
    }
}
