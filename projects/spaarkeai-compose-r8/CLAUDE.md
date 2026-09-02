# CLAUDE.md — `spaarkeai-compose-r8`

> Project context for Claude Code. Loads with every task in this project.
> **Root rules still apply** — this file adds project-specific context, it does not override
> [`/CLAUDE.md`](../../CLAUDE.md).

---

## 🚨 MANDATORY: Task Execution Protocol

**Every task in this project MUST be executed via the `task-execute` skill.** Do not read POML files and
implement manually. See root CLAUDE.md §4.

Trigger phrases → `task-execute`: "work on task X" · "continue" / "next task" · "resume task X" ·
"pick up where we left off" (load `current-task.md` first).

---

## What this project is

Compose's **eighth** release. Two failures, fixed in order:

1. **Users cannot reliably save** — client-contract, lifecycle and storage-boundary defects. No architecture
   decision required. Ships first, alone (Track S).
2. **Saves that land silently destroy Word formatting** — the renderer rebuilds all 40 pages from a five-node
   editor view. Fixed by copying across the blocks the user never touched (Track A).

Plus: AI edits located by prose matching instead of the anchor we already capture (Track C), session files
that die at 24h while their conversation lives 90 days (Track B), and five god classes (Track D).

---

## The one thing to understand before touching the write path

**This is the third swing of a pendulum. Do not swing it again.**

- **R4** = surgical byte-patch anchored `(paraId, runIndex, offset)`. Real fidelity → **HTTP 422 treadmill**.
- **R6** = render-on-save, rebuild the body from a thin model. 422s gone → **silent fidelity loss**.
- **R8** = keep R6's control flow, **add the base side**: re-project the retained baseline at save time and
  **clone the blocks that did not change**.

Two facts that killed the prior attempts, both spec-cited — do not re-derive them:

- **`w14:paraId` is NOT a durable file key.** [MS-DOCX] permits duplicates across `mc:AlternateContent`
  (how Word writes every text box); uniqueness is *part*-scoped; Word regenerates ids on save
  (Open-XML-SDK #925). It **is** authoritative **within a live session**, because we mint it.
- **`body.Descendants<Paragraph>()` interleaves text-box paragraphs into the body sequence.** Walk direct
  `w:body` children; treat `w:txbxContent` / `mc:Choice` / `mc:Fallback` as opaque.

---

## Binding invariants (the ADR-049 third amendment codifies these)

1. Every save terminates in a **defined outcome** — never an undefined content-refusal.
2. **Untouched blocks are preserved.**
3. The **projection is the only coordinate system** — nothing else resolves document positions.
4. `paraId` is a hint in the *file*, authoritative within a *session*.
5. Concurrency = **last-writer-wins with a warning**, enforced by `If-Match`.
6. **One edit-capture mechanism** — keystroke or model, same anchor capture + rebasing.
7. **Deterministic information available at capture time MUST be carried, not re-derived.**

> (7) is the general rule beneath three of the four root causes. If a design re-derives something it already
> had, that is the bug.

---

## Reuse, do not rebuild (CLAUDE.md §11)

`ComposeBaselineParaIdStamper` · `ComposeFormatChange` opaque carry · `ComposeBlockAtom` +
`opaqueAtomNode.ts` · `ResolveSaveBaselineAsync` · `SpeAdminGraphService` chunked upload ·
`UploadSessionManager` `If-Match` overload · `ComposeFidelityGateHarnessTests` + `ComposeCorpusFixtureLocator`
· R7's `SAVE_DEGRADATION_COPY` / banner stack / `ApiError` contract · `SessionRestoreService` + R7 re-attach ·
`AnnotationReanchorService` (KEEP — the sanctioned return-from-Word fuzzy case) · `ComposeOrigin`.

**Do not create**: a second body author · a parallel content model · a second fidelity harness · a new
degradation-copy layer · a new session-restore surface.

---

## Applicable ADRs

**ADR-049** (governing; R8 amends — Path B) · ADR-007 · ADR-009 · ADR-010 (**≤15 DI registrations — binds
Track D**) · ADR-013 · ADR-014/015 · ADR-021/050 · ADR-028 · ADR-029 · ADR-032 · ADR-038 · ADR-039/040 ·
**ADR-041** (assess FR-C05 as a Gate) · **ADR-043** (**names "compose edit"** — assess before Track C code).

---

## Constraints

- **BFF Hygiene (root §10)** — Placement Justification per new surface; publish ≤60 MB, and the delta measured against a **fresh `origin/master` publish zipped with the same tool on the same day** — NOT against a recorded baseline (root §10 bullet 4, corrected 2026-09-02; the stale-baseline comparison overstated this project's contribution 46×). Master @ `a826cf347` = **45.42 MB** incl. PDBs via Compress-Archive; no new HIGH CVE; tests updated. **No new NuGet on Track A.**
- **God-class ratchet** — five Compose files are frozen. Track D removes them; **delete each waiver** as its
  file drops below 2,000. Never silently re-baseline.
- **`parallel-safe: false` on the entire Compose spine.** `/conflict-check` before EVERY BFF PR.
- **Deploy BFF + `sprk_spaarkeai` together** (NFR-05). Never build from a net8 tree.
- **NEVER delete `docxBridge.ts`.**
- Freeze rule: **no new feature lands in the save path until the Phase-3 gate is green.**

---

## Phase 3 is a real gate

Phase 4 does not start until the merge prototype hits **100% near-tier / ≥95% overall preservation with zero
hard-fails** on the corpus. A miss is an owner escalation (root §6/§6.5), not an improvisation. The corpus,
not the argument, picks the architecture — that is the only thing that stops an R9.

---

## Key files

**Server** — `Services/Compose/{ComposeService,ComposeDocumentRenderer,ComposeDocxProjectionBuilder,ComposeContentModel,ComposeBaselineParaIdStamper,ComposeEditValidator,CitationResolver,ComposeShadowPatchEngine}.cs`
· `Api/ComposeEndpoints.cs` · `Infrastructure/Graph/UploadSessionManager.cs` · `Services/Ai/{Sessions,Chat}/**`

**Client** — `Spaarke.Compose.Components/src/{utils/docxBridge.ts,widgets/ComposeWorkspace.tsx,widgets/ComposeEditor.tsx,widgets/ComposeAiToolbar.tsx,widgets/opaqueAtomNode.ts,widgets/hooks/usePendingRedline.ts}`
· `Spaarke.Auth/src/authenticatedFetch.ts`

**Tests** — `tests/integration/seam/Compose/**` · `tests/fixtures/compose-corpus/**` ·
`tests/Spaarke.ArchTests/GodClassGuardTests.cs`

---

## Evidence base

[`design.md`](design.md) · [`spec.md`](spec.md) · [`notes/`](notes/) ·
[`../spaarkeai-compose-r7/notes/uat-issues.md`](../spaarkeai-compose-r7/notes/uat-issues.md) (UAT-01…26 + the
2026-08-18 hidden-issue audit) · [`ADR-049`](../../.claude/adr/ADR-049-compose-shadow-document.md)
