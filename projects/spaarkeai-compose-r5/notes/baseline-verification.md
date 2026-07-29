# Baseline Verification — spaarkeai-compose-r5 Wave 0 (task 001)

> Short-form summary. Full detail (evidence, per-class test counts, acceptance-criteria table): [`baseline-confirmation.md`](baseline-confirmation.md).

**Verdict: GREEN.** R5 implementation cleared to proceed.

- **R4.5 outputs present**: `NumberingComputationEngine` — nested `internal sealed class` in `ComposeDocxProjectionBuilder.cs:1357`. `CitationResolver.cs` present at `Services/Compose/CitationResolver.cs:43` with `Resolve`/`ResolveCitation`. `docxBridge.ts`: `docxToTipTapHtml` gone, `buildContentModel`/`stampParaIds` present, file untouched.
- **Tests**: `dotnet test tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj --filter "FullyQualifiedName~Compose"` → **739/739 passed**. Seam-only subset (`tests/integration/seam/Compose/`, compiled via `LinkBase="SeamTests"`) → **208/208 passed**. Corpus byte-diff harness (`ComposeShadowPatchEngineByteDiffSeamTests` + `ComposeNoOpRoundTripByteDiffSeamTests`) → **24/24 passed** (current corpus = 8 docs; task prompt's "28/28" was a stale headline number from an earlier corpus size — not a regression).
- **Publish size**: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` succeeded, zero errors. Compressed: **47.53 MB incl. PDBs / 46.70 MB excl. PDBs** (Compress-Archive -CompressionLevel Optimal, matching repo convention). Ceiling ≤60 MB — well clear. Matches spec NFR-04's ~46.11 MB reference baseline closely (excl.-PDB figure).
- **No source file modified.** Confirm-only task.
