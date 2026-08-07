# Decision record — Task 010 re-sequence (render-on-save: model-first)

> **Date**: 2026-08-05 · **Type**: Plan re-sequencing (WBS ordering) · **Trigger**: task-execute run 2, task 010 Step-2 code trace
> **Authorized by**: owner (AskUserQuestion "Re-sequence (Recommended)", 2026-08-05) · **Protocol**: root CLAUDE.md §6 / §6.5 escalation
> **Not an ADR change** — ADR-049 Path-B amendment (task 001) is unchanged and correct.

## What was found

Task 010 ("route Imported saves through `ComposeDocumentRenderer.SynthesizeDocument`") **could not be implemented as
scoped** — a genuine dependency inversion in the pipeline-generated WBS. Confirmed by reading the actual save path
(not just the POML):

1. **No faithful model source for imported docs.** `SynthesizeDocument(ComposeContentModel)` renders from a
   **client-authored** model (`ComposeContentModel.cs`) that only represents Paragraph/Heading/ListItem/Table +
   bold/italic/underline/hyperlink. It cannot represent the NDA's text boxes, `mc:AlternateContent`, signature blocks,
   headers/footers, tracked-changes, or comments.
2. **The client deliberately routes imported docs away from `contentModel`.** `ComposeWorkspace.tsx:1443-1473` sends
   imported/loaded docs down the op-log/patch path, NOT `contentModel`, with a documented rationale: re-authoring them
   from the model *"drops headers/footers/styles on rich docs and violates ADR-049 I-1/I-2/I-4"* (`:1432`). The
   dirty-imported-render path was an explicit **UAT #1A SEV-1 regression** ("plain untracked runs → NO redline in
   Word") — the fix was to route imported docs to the op-log path (`:1384-1389`).
3. **No OOXML→`ComposeContentModel` projector exists.** `ComposeDocxProjectionBuilder.Build(docx)` produces read/browse
   HTML (`ComposeDocxProjection`), not a `ComposeContentModel`. Building one is the read/reference path — an explicit
   STOP in 010's own `<escalation><trigger>`.

Net: executing 010 as written would either 422 (count-gate/patch stays) or silently flatten the NDA's rich constructs
(render from the thin model) — re-shipping the exact regression the client was hardened against. The critical path
`001→010→011→…→020` had the dependency **backwards**.

## Decision: re-sequence — build the hub, then flip the switch

Move the canonical-model hub + fidelity/hard-tier work (Phase 2: 020, 021–026) **before** the save-path cutover
(010, 012). Task **020 already scopes** the docx→canonical-model projection (the imported-doc "source") + render-out
wiring; task **026 already scopes** the NDA hard-tier accept-flatten (no-422). So re-sequencing closes the dependency
with the existing task set — no new tasks required.

### New binding execution order (critical path)

`001 → 020 → {011, 021–026} → 010 → 012 → {013, 027} → 014 → 060 → 061 → 090`

### Dependency edits applied

| Task | Was | Now | Why |
|---|---|---|---|
| 020 | deps 014 (gate dep:014) | deps 001, 004 (startable) | The anchor; builds the imported-doc model source. Was gated on the very cutover that depends on it. |
| 011 | deps 001, 010 | deps 001, 020 | Renderer generalization pairs with the model build; precedes the cutover. |
| 010 | deps 001, 004 | deps 011, 026 | Finalizes the Imported cutover only after the model (020/011) + hard-tier degradation (026) exist. |
| 012 | deps 001, 011 | deps 010 | Retire surgical from the save path only after the cutover makes render-from-model the default. |
| 013 | deps 004, 012 | (unchanged) | NDA no-422 regression on the shipped path. |
| 027 | deps 021–026 | deps 021–026, **012** | Fidelity seam suite tests the shipped (post-cutover) path. |
| 014 | deps 013 | deps 013, **027** | Cutover + fidelity ship together (anti-clobber); deploy gates on both suites. |

Downstream (030/040 dep 020; 050 dep 002; 060 dep 027,004; 090 dep 014/027/033/042/052/061) unchanged — their
prerequisites still land in the correct order.

## Alternatives considered & rejected

- **(2) Render-on-save only where the model is faithful, keep patch as fallback** — collapses into this re-sequence,
  because the faithful model doesn't exist until 020–026. No net difference.
- **(3) Narrow 010 = only remove the count-gate** — cannot meet 010's own acceptance criteria (Imported rendered via
  `SynthesizeDocument`; NDA saves no-422); removing only the gate moves the 422 into the patch engine (duplicate paraId).

## Reversibility

Task IDs and POML bodies are unchanged (stable references); only dependency edges, gates, and the critical path moved.
To revert: restore the deps/gates in the seven POMLs + plan.md §5/§6 + TASK-INDEX.md to their pre-2026-08-05 values.

## Artifacts changed

`plan.md` (§5 banner + phase annotations, §6 critical path) · `tasks/TASK-INDEX.md` (registry deps, critical path,
dependency notes, parallel groups, high-risk) · POMLs 010/011/012/014/020/027 (deps + gates + dependency blocks) ·
`current-task.md` (active task → 020) · this note.
