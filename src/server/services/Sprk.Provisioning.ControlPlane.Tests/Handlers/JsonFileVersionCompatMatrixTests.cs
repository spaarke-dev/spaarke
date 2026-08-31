// -----------------------------------------------------------------------------
// JsonFileVersionCompatMatrixTests.cs
//
// Unit tests over JsonFileVersionCompatMatrix (Wave G-8 Batch 10 — FR-34
// defect #24: H0 upgrade-mode version-compat matrix query).
//
// ADR-038 CATEGORY:
//   Path #1 — pure C# unit test. File IO only against per-test temp files +
//   the embedded default resource inside the Core assembly (no live Azure /
//   Cosmos / network).
//
// COVERAGE:
//   M1  Embedded default matrix loads; the published v1 baseline cell
//       (BFF 1.0.0-net10 x S2026.08) returns Green — proves the shipped
//       version-compat-matrix.json is valid + query-able as embedded.
//   M2  Unknown TARGET pair → Red with "NOT present" diagnostic (unsupported
//       until the release manager appends the cell per doc §6).
//   M3  Yellow cell → Yellow verdict + U-CB classes + cell note surfaced.
//   M4  Red cell → Red verdict.
//   M5  Version matching is case-insensitive + whitespace-trimmed.
//   M6  Unknown CURRENT pair only annotates the diagnostic; verdict still
//       comes from the target cell.
//   M7  Explicit file path that does not exist → InvalidOperationException.
//   M8  Corrupt JSON → InvalidOperationException citing invalid JSON.
//   M9  Duplicate (bff, solution) cells → InvalidOperationException.
//   M10 Unrecognized verdict string → InvalidOperationException.
//   M11 Zero cells → InvalidOperationException.
//   M12 Matrix is loaded once + cached (file deleted after first query;
//       second query still answers).
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Xunit;

namespace Sprk.Provisioning.ControlPlane.Tests.Handlers;

public sealed class JsonFileVersionCompatMatrixTests : IDisposable
{
    private static readonly VersionPair Baseline = new("1.0.0-net10", "S2026.08");
    private readonly List<string> _tempFiles = new();

    public void Dispose()
    {
        foreach (var file in _tempFiles.Where(File.Exists))
        {
            File.Delete(file);
        }
    }

    // ---------- M1 embedded default ----------

    [Fact]
    public async Task EmbeddedDefault_BaselinePair_ReturnsGreen()
    {
        var matrix = new JsonFileVersionCompatMatrix(
            matrixFilePath: null, NullLogger<JsonFileVersionCompatMatrix>.Instance);

        var result = await matrix.CheckPairAsync(Baseline, Baseline, CancellationToken.None);

        result.Verdict.Should().Be(VersionCompatVerdict.Green);
        result.UcbClasses.Should().BeEmpty();
        result.Diagnostic.Should().Contain("1.0.0-net10").And.Contain("S2026.08");
    }

    // ---------- M2 unknown target pair ----------

    [Fact]
    public async Task UnknownTargetPair_ReturnsRed_WithNotPresentDiagnostic()
    {
        var matrix = new JsonFileVersionCompatMatrix(
            matrixFilePath: null, NullLogger<JsonFileVersionCompatMatrix>.Instance);

        var result = await matrix.CheckPairAsync(
            Baseline, new VersionPair("9.9.9", "S2099.01"), CancellationToken.None);

        result.Verdict.Should().Be(VersionCompatVerdict.Red);
        result.Diagnostic.Should().Contain("NOT present").And.Contain("9.9.9").And.Contain("S2099.01");
        result.UcbClasses.Should().BeEmpty();
    }

    // ---------- M3/M4 yellow + red cells ----------

    [Fact]
    public async Task YellowCell_SurfacesUcbClassesAndNote()
    {
        var matrix = FromJson("""
        {
          "matrixVersion": "test-v2",
          "sourceDocument": "docs/deployment/version-compatibility-matrix.md",
          "cells": [
            { "bffVersion": "1.0.0", "solutionVersion": "S2026.08", "verdict": "Green" },
            { "bffVersion": "1.1.0", "solutionVersion": "S2026.09", "verdict": "Yellow",
              "ucbClasses": ["U-CB-3"], "note": "solution must upgrade first" }
          ]
        }
        """);

        var result = await matrix.CheckPairAsync(
            new VersionPair("1.0.0", "S2026.08"),
            new VersionPair("1.1.0", "S2026.09"),
            CancellationToken.None);

        result.Verdict.Should().Be(VersionCompatVerdict.Yellow);
        result.UcbClasses.Should().ContainSingle().Which.Should().Be("U-CB-3");
        result.Diagnostic.Should().Contain("U-CB-3").And.Contain("solution must upgrade first");
    }

    [Fact]
    public async Task RedCell_ReturnsRed_WithIntermediateReleaseDiagnostic()
    {
        var matrix = FromJson("""
        {
          "matrixVersion": "test-v2",
          "cells": [
            { "bffVersion": "1.0.0", "solutionVersion": "S2026.08", "verdict": "Green" },
            { "bffVersion": "2.0.0", "solutionVersion": "S2026.08", "verdict": "Red",
              "ucbClasses": ["U-CB-1"] }
          ]
        }
        """);

        var result = await matrix.CheckPairAsync(
            new VersionPair("1.0.0", "S2026.08"),
            new VersionPair("2.0.0", "S2026.08"),
            CancellationToken.None);

        result.Verdict.Should().Be(VersionCompatVerdict.Red);
        result.Diagnostic.Should().Contain("intermediate release").And.Contain("U-CB-1");
    }

    // ---------- M5 case-insensitivity + trimming ----------

    [Fact]
    public async Task Lookup_IsCaseInsensitiveAndTrimmed()
    {
        var matrix = new JsonFileVersionCompatMatrix(
            matrixFilePath: null, NullLogger<JsonFileVersionCompatMatrix>.Instance);

        var result = await matrix.CheckPairAsync(
            Baseline,
            new VersionPair(" 1.0.0-NET10 ", "s2026.08"),
            CancellationToken.None);

        result.Verdict.Should().Be(VersionCompatVerdict.Green);
    }

    // ---------- M6 unknown current pair annotates only ----------

    [Fact]
    public async Task UnknownCurrentPair_AnnotatesDiagnostic_ButVerdictComesFromTargetCell()
    {
        var matrix = new JsonFileVersionCompatMatrix(
            matrixFilePath: null, NullLogger<JsonFileVersionCompatMatrix>.Instance);

        var result = await matrix.CheckPairAsync(
            new VersionPair("0.9.0-legacy", "S2025.12"), Baseline, CancellationToken.None);

        result.Verdict.Should().Be(VersionCompatVerdict.Green, "verdict is the target cell's");
        result.Diagnostic.Should().Contain("WARNING").And.Contain("0.9.0-legacy");
    }

    // ---------- M7-M11 source validation ----------

    [Fact]
    public async Task MissingOverrideFile_Throws()
    {
        var matrix = new JsonFileVersionCompatMatrix(
            Path.Combine(Path.GetTempPath(), $"no-such-matrix-{Guid.NewGuid():N}.json"),
            NullLogger<JsonFileVersionCompatMatrix>.Instance);

        var act = () => matrix.CheckPairAsync(Baseline, Baseline, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task CorruptJson_Throws()
    {
        var matrix = FromJson("{ this is not json ]");

        var act = () => matrix.CheckPairAsync(Baseline, Baseline, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not valid JSON*");
    }

    [Fact]
    public async Task DuplicateCells_Throws()
    {
        var matrix = FromJson("""
        {
          "matrixVersion": "test",
          "cells": [
            { "bffVersion": "1.0.0", "solutionVersion": "S2026.08", "verdict": "Green" },
            { "bffVersion": "1.0.0", "solutionVersion": "s2026.08", "verdict": "Red" }
          ]
        }
        """);

        var act = () => matrix.CheckPairAsync(Baseline, Baseline, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*duplicate cell*");
    }

    [Fact]
    public async Task UnrecognizedVerdict_Throws()
    {
        var matrix = FromJson("""
        {
          "matrixVersion": "test",
          "cells": [ { "bffVersion": "1.0.0", "solutionVersion": "S2026.08", "verdict": "Purple" } ]
        }
        """);

        var act = () => matrix.CheckPairAsync(Baseline, Baseline, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Purple*");
    }

    [Fact]
    public async Task EmptyCells_Throws()
    {
        var matrix = FromJson("""{ "matrixVersion": "test", "cells": [] }""");

        var act = () => matrix.CheckPairAsync(Baseline, Baseline, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no cells*");
    }

    // ---------- M12 load-once caching ----------

    [Fact]
    public async Task Matrix_IsLoadedOnceAndCached()
    {
        var path = WriteTempFile("""
        {
          "matrixVersion": "test",
          "cells": [ { "bffVersion": "1.0.0", "solutionVersion": "S2026.08", "verdict": "Green" } ]
        }
        """);
        var matrix = new JsonFileVersionCompatMatrix(path, NullLogger<JsonFileVersionCompatMatrix>.Instance);

        var first = await matrix.CheckPairAsync(
            Baseline, new VersionPair("1.0.0", "S2026.08"), CancellationToken.None);
        File.Delete(path); // Source disappears — cached parse must still answer.
        var second = await matrix.CheckPairAsync(
            Baseline, new VersionPair("1.0.0", "S2026.08"), CancellationToken.None);

        first.Verdict.Should().Be(VersionCompatVerdict.Green);
        second.Verdict.Should().Be(VersionCompatVerdict.Green);
    }

    // ---------- helpers ----------

    private JsonFileVersionCompatMatrix FromJson(string json)
        => new(WriteTempFile(json), NullLogger<JsonFileVersionCompatMatrix>.Instance);

    private string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"version-compat-matrix-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }
}
