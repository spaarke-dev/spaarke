# Task 032 — SAVE-path write cutover (Path A) — COMPLETION

> **Date**: 2026-07-22 · **Rigor**: FULL (opus/high, prescriptive) · **Status**: ✅ complete (re-scoped Path A)
> Supersedes the BLOCKED state (`notes/task-032-pushannotations-scope.md`). The push-annotations surface
> deletion is deferred to **task 036** (owner Path B/C product decision).

## What landed (the atomic SAVE-path hard-replace)

**A. Server** — `ComposeService.SaveAsync` + `ComposeEndpoints` now accept the task-003 **operation-log
contract** (`operationLog` + base version) and apply it via **`ComposeShadowPatchEngine.Apply(...)`** onto the
retained-original baseline, replacing the `ComposeEditedParagraph` paragraph-diff payload AND the save-path
`DocxAnnotation` payload. `SaveComposeDocumentRequest` gained `OperationLog` (`ComposeOperationLog`) + `Comments`
(`ComposeAnchoredComment[]`, engine `ApplyComment`), and dropped `EditedParagraphs` + save-path `Annotations`.
A save submitting the retired `editedParagraphs` shape → **400 ProblemDetails** ("Outdated Save Payload"), and a
Patch-Engine refusal → typed ProblemDetails per `ComposePatchErrorKind` (400/409/422) — never a 500.

**B. Client** — `ComposeEditor` now wires a production **`RebasedOperationLog`** (task 020/022) into the editor
(the previously-bare `COMPOSE_R4_STEP_INTERCEPTOR` is replaced by a configured rebased-op-log plugin supplying
the `onStructuralStep`/`onUnrepresentableStep`/`onRefusedAtomEdit` callbacks — never-silently-dropped) and
exposes `serializeOperationLog()` on the handle. The log `reset()`s on every fresh load (all mount paths) and
after each serialize. `ComposeWorkspace.triggerSave` sends the ordered rebased op-log + base version
(`baselineVersionId`) and **no longer calls `collectEditedParagraphs`** (that function survives for task-023
cleanup). Fetch stays on `@spaarke/auth` (ADR-028). Flagged (`deletedContentFlag`) ops are excluded from what
is applied.

**C. Retire `ComposeParagraphRedlineSynthesizer` ONLY** — the class + its DI registration are deleted; grep
shows **zero call sites** (remaining mentions are historical XML-doc `<see cref>` in unrelated files, not code).

**D. Push-annotations untouched (Path-A boundary)** — `DocxAnnotationWriter`, `DocxAnnotation.TargetText`,
`PushAnnotationsAsync`, endpoints (9)/(9b), and the client `useComposePushAnnotations` are intact and compile.
The SAVE path no longer routes through `DocxAnnotationWriter`; the push path keeps using it unchanged.

## Coverage check (Step 2) — every SAVE-path behavior is covered by the engine

| Legacy save-path behavior | Old writer | Engine coverage (confirmed) |
|---|---|---|
| Word-diff redline of an edited paragraph | `ComposeParagraphRedlineSynthesizer` | step-level `insertText`/`deleteRange`/`replaceRange` ops (client emits granular ops) |
| Whole-paragraph strike | synthesizer sentinel | `ApplyDeleteParagraph` |
| Inserted/deleted/comment native OOXML | `DocxAnnotationWriter.Annotate` (save path) | `ApplyInsertText`/`ApplyDeleteRange`/`ApplyComment` (`ComposeAnchoredComment`) |
| EDGE-1..4 (comment-before-trackchange, w:delText, monotonic ids) | `DocxAnnotationWriter` | migrated into `PatchSession` (task 030), grep-verified |

No genuine SAVE-path gap remained → cutover proceeded (no BLOCKED).

## §6.5 Path-A decision — save-path comment BAKING deferred (not a regression)

The client's session comments are **text-anchored** (`DocxAnnotation.TargetText` / `anchor.textPattern`).
Converting them to op-anchored `ComposeAnchoredComment` server-side is the I-7-forbidden whole-doc text-search;
converting them client-side is the same anchoring problem the push-annotations surface owns → **task 036**. The
server SaveAsync FULLY supports op-anchored `Comments` (engine `ApplyComment`, seam-testable); the client sends
none yet. **No silent regression**: session comments still persist as mutable UI state via the FR-29
`POST /sessions/{id}/annotations` endpoint (unchanged, rehydrated on Load) and bake to native OOXML via
push-annotations (FR-24, unchanged). This extends the existing documented §6.5 Path-A two-byte-author exception;
it closes when task 036 lands (Success Criterion 7 / I-5).

## ADR / project tensions

- **R3 paragraph-diff Path-B supersession (design §9 row 1 / ADR-049)**: this cutover replaces the R3 decision
  "dirty save = paragraph-diff synthesizer onto retained original" with step-level operational deltas applied by
  the single `ComposeShadowPatchEngine` (D1/D5). Cite in the PR description.
- **ADR-007**: `Services/Compose/` stays `byte[]`-in/`byte[]`-out; no `Microsoft.Graph` added. Tier-1
  `ADR007_GraphIsolation` has a **pre-existing** failure (offenders in `Services.Communication.*` /
  `Api.Office.Errors` / `Infrastructure.Errors` — none in `Services.Compose`); not introduced by this task.
- **ADR-013**: no AI internals injected; **`ADR013_ComposeFacade` Tier-1 NetArch PASSES**.
- **ADR-038**: seam save/load slices migrated to the op-log contract (KEEP category); retired-path-only unit
  tests deleted per §7 build-vs-maintain; no banned mock/DI/ctor test types added.
- **ADR-028**: client fetch stays on `@spaarke/auth`. **NFR-03**: MIT only (no new package; op-log capture uses
  the MIT `@tiptap/pm/transform` surface already bundled).

## Placement Justification (root §10 / `.claude/constraints/bff-extensions.md`)

Save/patch orchestration stays in `Services/Compose/` — this task **consolidates onto one write path**
(`ComposeShadowPatchEngine`) and **deletes** a service (`ComposeParagraphRedlineSynthesizer`); it adds no new
endpoint, no DI blob, and **no NuGet package**. The op-log DTO lives on the existing `SaveComposeDocumentRequest`
(Home: request contract), not a new surface. Belongs in BFF (latency + SPE-write transactional coupling; not
event-driven). No new CRUD→AI dependency; the engine is a pure `byte[]` transform.

## Verification

- BFF API build: **0 errors**. `dotnet test --filter Compose`: **524 passed, 0 failed** (seam slices migrated).
- Tier-1 `ADR013_ComposeFacade`: PASS. Client typecheck: no new errors vs baseline (pre-existing monorepo
  module-resolution only). Client jest: interceptor 31/31 pass; resolvable Compose suites 22/22 (17 suites fail
  at import on pre-existing unbuilt `@spaarke/ui-components/dist` — not this change).
- **Publish size: 47.48 MB compressed** (↓ vs ~49.63 MB baseline; well under the 60 MB ceiling). CVE:
  `System.Security.Cryptography.Xml` HIGH is a **pre-existing transitive** advisory — **zero packages added** by
  this task, so no new HIGH CVE.
- `/conflict-check`: **no conflicts** — no open PR touches any Compose file (BFF-touching PRs are dependabot
  bumps with zero overlap).
