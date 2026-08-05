# Current Task — Spaarke Compose R6

> Active-task tracker for context recovery. Reset per root `CLAUDE.md` §7 as tasks complete.

## Status: Phase 0 ✅ complete · Phase 1 (task 010) LOADED — checkpointed at pre-implementation gate

- **Project**: `spaarkeai-compose-r6`
- **Branch**: `work/spaarkeai-compose-r6` (pushed; 0 behind master as of 2026-08-05)
- **Phase**: 1 Render-on-save core
- **Active task**: 010 — Route Imported docs through render-from-model; drop count-gate (FULL, opus/**xhigh**)
- **Status**: loaded + gated — **no code written yet** (stopped at Step 3 budget check + gate/coupling)
- **Conflict-check**: CLEAN window (no open-PR overlap; no sibling branch has unmerged commits on ComposeService.cs / ComposeDocumentRenderer.cs / ComposeBaselineParaIdStamper.cs).

### 🔑 Resume 010 — do these FIRST (pre-implementation decisions)
1. **Gate**: POML `<gate>` wants the ADR-049 amendment (001) MERGED. It's committed on THIS branch (`511976d7f`), so it merges together with the Phase-1 code — decide whether that "with-code" reading is acceptable or merge 001 to master first (coordinate with sibling Compose worktrees per INDEX.md).
2. **Do 010 + 011 together** (or 011 first): `ComposeDocumentRenderer.SynthesizeDocument` (`ComposeDocumentRenderer.cs:102`) currently only accepts born-in-editor `ComposeContentModel.Blocks`. 010 reroutes the `SaveAsync` Imported branch (`ComposeService.cs:642`, branch `:714`) into it; 011 generalizes the renderer to accept the imported/canonical model. They are one coupled change — plan them jointly.
3. Start a FRESH session (010 is 4–8h opus/xhigh on a 2,915-line file — needs a clean context budget).

### Implementation pointer for 010 (from the POML)
- Reroute `ComposeService.SaveAsync` Authored/Imported branch (`:714`) so **Imported** renders via `SynthesizeDocument` instead of surgical patch.
- Make `ComposeBaselineParaIdStamper` count-gate (`ComposeBaselineParaIdStamper.cs:113`) unreachable from a normal save; **retain** the type (+ `ComposeShadowPatchEngine`) for the transitional clean-apply path. NEVER delete `docxBridge.ts`.
- Keep `ReplaceFileContentAsUserAsync` (→ new SPE version) + Redis eTag stamp + stale-base re-anchor UNCHANGED.
- Gates: build BFF; publish-size vs task-003 baseline (**48.25 MB** compressed incl PDBs; 11.75 MB headroom); no new HIGH CVE (baseline has 1 pre-existing: `System.Security.Cryptography.Xml` 8.0.3); Step 9.5 code-review + adr-check; then task 013 (NDA saves, no 422) + 014 deploy/UAT.
- Escalation trigger: if routing needs the read/reference path, a `Services/Ai` fork, or a new AI dispatch endpoint → STOP, escalate (§6.5).

### Prior phases
- Phase 0 ✅ (001 ADR amendment `511976d7f`; 002 SPE append-only verified + config-dependency flag; 003 baseline 48.25 MB; 004 NDA fixture + row #14). Phase 0 wave committed `e5c1d5f65`.

### Phase 0 — ✅ complete (2026-08-05)
- **001** ✅ ADR-049 R6 Path-B amendment (render-on-save supersedes I-4 + line-40, save path only). Committed `511976d7f`.
- **002** ✅ SPE versioning = append-only (unconditional new-version PUT). `notes/spe-versioning-verify.md`.
  - ⚠️ **Operational dependency**: SPE retention depends on per-container-type `isVersioningEnabled=true` + `majorVersionLimit` (admin config, not a code guarantee) — must be confirmed per environment for FR-07's safety net.
  - 🔔 **Human gate**: live v3-after-v4 byte-intact confirmation in the deployed Documents/SPE surface.
  - FR-07 needs: NEW OBO version-LIST endpoint; can REUSE `DownloadFileVersionAsUserAsync:842` for open-prior.
- **003** ✅ Publish baseline **48.25 MB compressed incl. PDBs** (−1.38 MB vs 49.63 MB ref; 11.75 MB headroom to 60 MB). `notes/publish-baseline.md`. Baseline CVE (pre-existing): `System.Security.Cryptography.Xml` 8.0.3 (5 High) — R6 tasks must not add NEW HIGH CVEs.
- **004** ✅ `AppligentNDA_Signed.docx` moved to `tests/fixtures/compose-corpus/` (LFS) + manifest row #14 (§1.6). Empirically confirmed the 422 root cause: **7 `mc:AlternateContent` pairs, 12 `w:txbxContent`, 3 duplicate `w14:paraId`** in the signature block. `notes/nda-fixture-coverage.md`.
  - Step 9.5 override note: touches `tests/**` but changed no test CODE (binary fixture + manifest markdown only) → code-review/adr-check N/A (documented rationale).

### Phase-1 gating (why autonomous execution stops here)
Phase 1 (010/011/012/013/014) is the render-on-save code pivot: FULL rigor, opus/xhigh, **most-contested BFF surface** (`Services/Compose/`). Before it runs it needs (human-in-loop):
1. The **ADR-049 amendment merged to master** with/before the Phase-1 code (Path-B obligation).
2. **`/conflict-check` before the BFF PR** (active overlap with compose-r5 on `ComposeService.cs`).
3. Real code changes + Step 9.5 code-review/adr-check + a deploy/UAT gate (014) on the NDA.
These are not safe to run fully unattended — resume Phase 1 deliberately.

## Notes
- Scaffold generated 2026-08-05 via `/project-pipeline` (scaffold-and-stop mode; task execution NOT started).
- Task 001 (ADR-049 amendment) MUST merge with or before Phase-1 code (010–014).
- Phase 0 tasks 002 (SPE versioning verify) + 003 (publish-size baseline) are human/verify gates.
- `Services/Compose/` is the most-contested surface — run `/conflict-check` before every BFF PR.

## Steps completed this task
(none — no active task)

## Files modified this task
(none)

## Decisions
(none — see spec.md Owner Clarifications for locked project decisions)
