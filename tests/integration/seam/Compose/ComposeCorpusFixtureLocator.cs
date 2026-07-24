// Task 004 (spaarkeai-compose-r4, NFR-01) — corpus discovery helper for the round-trip byte-diff
// harness. Owner feedback (baked into design per the task brief): do NOT hardcode the 3 sample
// filenames. This locator enumerates every `.docx` under `tests/fixtures/compose-corpus/` at test
// DISCOVERY time (xUnit [MemberData]), so an owner-supplied worst-offender dropped into that
// directory later (see corpus-manifest.md §2 placeholder rows) is automatically picked up by
// ComposeNoOpRoundTripByteDiffSeamTests without any code change.
//
// The corpus lives at the REPO ROOT (`tests/fixtures/compose-corpus/`), not inside this test
// project's own directory — so this locator resolves the repo root the same way
// FinancialCalculatorHandlerTests.ResolveSourcePath does (walk up from AppContext.BaseDirectory
// looking for the canonical `src/server/api/Sprk.Bff.Api/Program.cs` marker). No csproj
// Content-Include is added for the corpus files; this reads them directly off disk from the repo
// checkout, which keeps the existing seam test project (`Sprk.Bff.Api.Tests.csproj`) untouched per
// the task's "reuse the existing seam test project — do NOT edit the .csproj" constraint.
//
// LFS guard: `tests/fixtures/compose-corpus/*.docx` are Git-LFS pointers (`.gitattributes`). If the
// working tree has NOT smudged them (raw pointer text, ~130 bytes, starting with
// "version https://git-lfs.github.com/spec"), fail with a clear, actionable message instead of a
// confusing OpenXml parse error deep inside the comparer.

using System.Text;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

internal static class ComposeCorpusFixtureLocator
{
    private const string LfsPointerPrefix = "version https://git-lfs.github.com/spec";

    /// <summary>
    /// Enumerates every `.docx` under the repo's `tests/fixtures/compose-corpus/` directory,
    /// sorted for deterministic test ordering. Used as xUnit `[MemberData]` — NOT a fixed list of
    /// the 3 seed filenames (task 002's corpus-manifest.md §2 documents future owner-supplied
    /// additions; this glob is what makes them land in the harness with zero code changes).
    /// </summary>
    public static IEnumerable<string> EnumerateDocumentPaths()
    {
        var corpusDir = ResolveCorpusDirectory();
        return Directory.EnumerateFiles(corpusDir, "*.docx", SearchOption.TopDirectoryOnly)
            .OrderBy(p => p, StringComparer.Ordinal);
    }

    /// <summary>
    /// Reads a corpus fixture's bytes and verifies they are the SMUDGED real `.docx` content, not
    /// an un-smudged Git-LFS pointer stub. Throws with a remediation hint (`git lfs pull`) rather
    /// than letting the caller hit an opaque OpenXml "not a valid package" failure.
    /// </summary>
    public static byte[] LoadVerifiedBytes(string path)
    {
        var bytes = File.ReadAllBytes(path);

        // A real .docx is a ZIP archive (starts with "PK"); an un-smudged LFS pointer is short
        // ASCII text. Check both signals for a precise diagnostic.
        var looksLikeZip = bytes.Length > 4 && bytes[0] == 0x50 && bytes[1] == 0x4B;
        if (!looksLikeZip)
        {
            var previewLength = Math.Min(bytes.Length, 64);
            var preview = Encoding.UTF8.GetString(bytes, 0, previewLength);
            if (preview.StartsWith(LfsPointerPrefix, StringComparison.Ordinal) || bytes.Length < 512)
            {
                throw new InvalidOperationException(
                    $"Corpus fixture '{path}' is {bytes.Length} bytes and does not look like a real " +
                    ".docx package (missing ZIP 'PK' signature) — this is almost certainly an " +
                    "un-smudged Git-LFS pointer. Run `git lfs pull` (or `git lfs fetch --all && git lfs checkout`) " +
                    "in this worktree before running the byte-diff harness.");
            }
        }

        return bytes;
    }

    private static string ResolveCorpusDirectory()
    {
        // Walk up from the test assembly's output directory looking for the canonical repo-root
        // marker `src/server/api/Sprk.Bff.Api/Program.cs` — mirrors the proven pattern already used
        // by FinancialCalculatorHandlerTests.ResolveSourcePath in this same test project.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var marker = Path.Combine(dir.FullName, "src", "server", "api", "Sprk.Bff.Api", "Program.cs");
            if (File.Exists(marker))
            {
                var corpusDir = Path.Combine(dir.FullName, "tests", "fixtures", "compose-corpus");
                if (!Directory.Exists(corpusDir))
                {
                    throw new InvalidOperationException(
                        $"Resolved repo root '{dir.FullName}' but 'tests/fixtures/compose-corpus/' does not " +
                        "exist there — has task 002's corpus been removed or relocated?");
                }

                return corpusDir;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the repo root (and thus the compose-corpus fixtures) from test base " +
            $"directory '{AppContext.BaseDirectory}'.");
    }
}
