# Current Task — Spaarke Compose R6

> Active-task tracker for context recovery. Reset per root `CLAUDE.md` §7 as tasks complete.

## Status: Phase 0 ✅ · RE-SEQUENCED (model-first) ✅ · Active task → **020 GATES COMPLETE** (Steps 0–3 + all gates + Step 9.5 ✅ · review fixes applied · next: decide 020-done vs. 021-seam extras)

- **Project**: `spaarkeai-compose-r6`
- **Branch**: `work/spaarkeai-compose-r6` (pushed; 0 behind master as of 2026-08-05)
- **Active task**: **020** — Canonical document model hub. FULL, **opus**/high (executed on Fable 5). deps 001, 004 (both ✅).
- **Status**: projector shipped (`f3179b819`) + **ALL GATES PASSED** + **Step 9.5 DONE** (adr-check PASS 0-violations; code-review APPROVE-WITH-MINORS → 10 findings FIXED in follow-up commit, 2 routed for operator sign-off to 025/026, rest accepted/documented). **Design + gates + triage = [notes/020-canonical-hub-design.md](notes/020-canonical-hub-design.md) §8-§9** (READ FIRST on resume).
- **Gates**: publish 46.88 MB (−1.37 vs baseline, ≤60 ✅) · no NEW HIGH CVE ✅ · ADR-013 ArchTest PASSES (4 pre-existing master fails documented) · conflict-check CLEAN ✅.
- **⚠️ Operator sign-off pending (flatten-tier decisions, no user impact until 010/011 wire the caller):** R4 ins+del→"barfoo" (→025) · R5 U+FFFD persisted for unmapped w:sym (→026).

### 🧭 Task 020 design (locked this session — key decisions)
- **Hub = `ComposeContentModel` (body, widened by 021–025) + server-retained source package ("carrier" = styles/numbering/hdr-ftr/theme/sectPr).** EXTENSION, not a parallel model → does NOT trip escalation trigger #1.
- **Render-on-save unified** (`ComposeService.cs:714`): Authored → `SynthesizeDocument` (blank carrier, unchanged); Imported → open retained carrier, **replace body** with rendered model, preserve all other parts (generalize `AppendSection:191` from append→replace-body = task 011). No text-search / no anchoring → does NOT trip trigger #2.
- **020's core new work** = the `docx→ComposeContentModel` projector: lenient/**total** (never throws; unrecognized → flatten-by-omission), reuse `ComposeDocxProjectionBuilder` traversal + `NumberingComputationEngine`. NDA saves no-422 comes FREE from render-on-save; fidelity built up by 021–026 before the 010 cutover.
- Confirmed: `SynthesizeDocument:102` = `WordprocessingDocument.Create` (blank); `AppendSection:191` = `.Open` (preserves parts). `ComposeDocxProjectionBuilder.Build:92` emits **HTML**, not a ComposeContentModel (the gap).

### Task 020 — steps completed
- [x] Step 0: rigor FULL/opus/high declared; deps 001+004 ✅; /conflict-check CLEAN (Services/Compose window clear)
- [x] Step 1: code map (ComposeContentModel.cs, ComposeDocxProjectionBuilder structure, SynthesizeDocument + AppendSection)
- [x] Step 2: canonical-hub design → notes/020-canonical-hub-design.md
- [x] Step 3: **projector SHIPPED** — `ComposeDocxProjectionBuilder.BuildContentModel` (total/lenient; AlternateContent handled loudly; F-03 parity guard) + `ComposeCanonicalModelProjection` envelope + shared `ResolveHyperlinkHref(h, MainDocumentPart)` refactor + seam slice `ComposeCanonicalModelRoundTripSeamTests` (12/12 green: corpus round-trip block-kind stability; NDA never refused; NDA rendered output = unique paraIds ⇒ count-gate condition impossible; fail-closed). Full Compose suite 407/409 — the 2 fails are PRE-EXISTING NDA-fixture reds since task 004 (stash-verified at HEAD, §F.3), routed to 026/027 (details in notes §8, incl. the NEW AppendSection dup-paraId I-4 finding).
- [ ] Step 4: SaveAsync render-out wiring — SCOPED OUT of 020 per re-sequence (belongs to 010 cutover after 011+026); documented as directional deviation in notes §8
- [ ] NEXT: model-shape seams for 021–025 only as needed; Step 6: publish size + delta vs 48.25 MB; re-run /conflict-check; Placement Justification
- [ ] Step 7: Tier-1 NetArchTest verify (build + seam already green)
- [ ] Step 9.5: code-review + adr-check (FULL/BFF)
- **New critical path**: `001 → 020 → {011, 021–026} → 010 → 012 → {013, 027} → 014 → 060 → 061 → 090`.
- **Conflict-check**: CLEAN window (no open-PR overlap; no sibling branch has unmerged commits on ComposeService.cs / ComposeDocumentRenderer.cs / ComposeBaselineParaIdStamper.cs). Verified 2026-08-05 (task-execute run 2). **Re-run before 020's BFF PR.**

### ✅ Re-sequence applied 2026-08-05 (owner-authorized — see [notes/task-010-resequence-decision.md](notes/task-010-resequence-decision.md))
Task 010's Step-2 code trace found a dependency inversion: render-from-model needs a faithful canonical-model source
for imported docs (020's docx→model projection) + hard-tier accept-flatten (026) before the cutover (010/012) can run
without re-shipping the fixed UAT #1A SEV-1 regression. Fixed by moving 020+026 before 010. Edits: plan.md §5/§6,
TASK-INDEX.md (deps/critical-path/groups/high-risk), POMLs 010/011/012/014/020/027 (deps+gates), decision note. ADR-049
amendment (001) unchanged. All 8 touched POMLs re-validated well-formed.

### 🔑 Resume 020 — do these FIRST
1. `task-execute` Step 0/0.5 for `tasks/020-canonical-model.poml`: rigor FULL, **opus**/high, /conflict-check for Services/Compose.
2. 020 is estimated **1–2 weeks** (the architectural anchor) — build it incrementally with checkpoints every 3 steps; do NOT attempt in one pass.
3. Step 1 (code map, likely done already this session): projection is `ComposeDocxProjectionBuilder.Build(docx)` → **HTML/atoms** (read path), NOT ComposeContentModel. The task's core new work = a docx→`ComposeContentModel` projection (the missing "source") that `SynthesizeDocument` can render back. Reuse `NumberingComputationEngine` (`ComposeDocxProjectionBuilder.cs:1357`). NEVER delete `docxBridge.ts`.
4. 020's own escalation trigger: if it needs a **parallel/second model type** (not an extension) or **text-search/surgical anchoring** on the save path → STOP, escalate (§6.5).

### 🔔 BLOCKER — task 010 dependency inversion (surfaced 2026-08-05, Step 2 code trace)

**Finding (high confidence, code-verified):** Task 010 "route Imported saves through `SynthesizeDocument`" cannot be
faithfully implemented as scoped, because the render-from-model path has **no faithful canonical-model source for
imported docs** — and forcing one re-ships a fixed SEV-1 fidelity regression.

Evidence:
1. `SynthesizeDocument(ComposeContentModel)` renders from a **client-authored** model that only represents
   Paragraph/Heading/ListItem/Table + b/i/u/hyperlink (`ComposeContentModel.cs`). It CANNOT represent the NDA's
   constructs (text boxes, `mc:AlternateContent`, signature blocks, headers/footers, tracked-changes, comments).
2. The client (`ComposeWorkspace.tsx:1443-1473`) **deliberately** routes imported/loaded docs through the
   op-log/patch path, NOT `contentModel` — with a documented rationale: re-authoring imported docs from the model
   *"drops headers/footers/styles on rich docs and violates ADR-049 I-1/I-2/I-4"* (`:1432`). The dirty-imported-render
   path was an explicit **UAT #1A SEV-1 regression** ("plain untracked runs → NO redline in Word") fixed by routing
   imported docs to the op-log path (`:1384-1389`).
3. There is **no OOXML→ComposeContentModel projector** server-side. `ComposeDocxProjectionBuilder.Build(docx)`
   produces read/browse HTML (`ComposeDocxProjection`), not a `ComposeContentModel`. Building one = the read/reference
   path — an explicit **STOP** in 010's own `<escalation><trigger>`.
4. Making render-on-save faithful for imported docs requires: (a) a **widened canonical model** (headers/footers,
   tracked-changes, comments, hard-tier accept-flatten) = **Phase 2 tasks 020–026**, currently sequenced AFTER
   Phase 1; (b) a **client change** to serialize edited imported docs into it (outside 010's `ComposeService.cs`-only
   scope). The critical path `001→010→011→…→020` has the dependency **backwards**.

**Recommended resolution (owner's call): RE-SEQUENCE — build the hub before flipping the switch.**
Move canonical-model generalization + hard-tier graceful degradation (020–026) BEFORE the save-path cutover
(010–012). New critical path: `001 → 020 → {021–026} → 010 → 011 → 012 → 013 → 014 → 027 → …`. The cutover then
flips only once the model can faithfully carry — or accept-flatten with a warning (026) — the NDA's rich constructs.
The ADR-049 amendment (001) stays correct; only the WBS ordering changes. This is a plan re-sequencing, not an ADR change.

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
