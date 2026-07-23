# Task 037 — born-in-editor SAVE-PATH UNIFICATION is BLOCKED (§6.5 escalation)

> **Date**: 2026-07-23
> **Task**: 037 (folds 033). Owner chose **Path C — IMPORT-ONLY tables** (`notes/owner-decisions-036-037.md`).
> **Status of the two halves of task 037**:
> - ✅ **Half 1 — insertTable gating (import-only tables)**: DONE. Client-only, owner-approved, landed.
> - 🔔 **Half 2 — born-in-editor save unified onto the op model (retire the 2nd byte-author)**: **BLOCKED — escalated per root §6.5.** This is the exact condition the orchestrator's own directive and the POML `<escalation>` clause told me to STOP and surface for: *"If any born-in-editor construct OTHER than tables cannot be represented as an operation onto an empty package, STOP and escalate."*

---

## What the owner's Path C assumed vs. what the code actually is

Path C's rationale (owner): *"Import-only lets one-byte-author (I-5) hold **literally with minimal risk** and ships R4 fastest."* The premise was that the ONLY thing blocking born-in-editor unification was the missing **table** primitive — remove table authoring and the remaining constructs (paragraphs / headings / lists / marks) "just" become an insert-everything op set onto an empty package (spec FR-09).

**That premise is incorrect.** Removing tables removes ONE of *several* unrepresentable constructs. After tables are gone, born-in-editor content STILL cannot be authored faithfully onto an empty shadow package through `ComposeShadowPatchEngine.Apply`, for four independent reasons:

### 1. `insertText` / `insertParagraph` emit TRACKED CHANGES, not clean content
`ComposeShadowPatchEngine` is a **tracked-change applier**. `ApplyInsertText` wraps every insertion in `NewInsertedRun()` → `w:ins`; `ApplyInsertParagraph`/`ApplySplitParagraph` stamp a tracked `w:ins` para-mark (`MarkParagraphMark(inserted:true)`). There is **no clean-insert primitive** in the closed 10-op schema. Authoring a whole born-in-editor document via the op log would therefore produce a document in which **every paragraph is a pending tracked insertion attributed to "Spaarke Compose"** — a lawyer opening a freshly-drafted contract would see the entire body as un-accepted redlines. That is a **fidelity/semantic regression** versus today's clean output, and directly contradicts the reason `ComposeDocumentRenderer` exists (its docstring: *"degrading an AI-drafted LEGAL document the moment it was first saved"* — the renderer was built specifically to STOP that).

### 2. `setBlockAttr` — the ONLY op for heading-style / list / alignment — is UNIMPLEMENTED in the engine
Headings, ordered/bullet lists, and paragraph alignment are all expressed by the `setBlockAttr` op (`attr ∈ {Style, ListOrdered, ListLevel, Alignment}` — see `compose-operations.ts`). In the engine, `SetBlockAttrOperation` throws `ComposePatchErrorKind.StructuralOpNotYetImplemented` (it "routes to its own later applier extension"). So a born-in-editor doc with **any** heading, list item, or aligned paragraph cannot be authored via ops at all today.

### 3. An empty shadow package has NO styles / numbering parts — FR-27 (the "keystone") is lost
`ComposeDocumentRenderer` authors a `StyleDefinitionsPart` (Normal + Heading1-6 + ListParagraph) and a `NumberingDefinitionsPart` with the **style-linked multi-level clause scheme** (1 / 1.1 / 1.1.1) — FR-27, described in the renderer as *"the keystone"* and *"MUST be instance-clean + style-linked AT BIRTH."* An empty package that ops are applied onto has none of this. Even if `setBlockAttr(Style=Heading1)` were implemented, it would reference a non-existent style → unstyled, unnumbered clauses. Reproducing FR-27 fidelity behind the op log means **porting the renderer's style + numbering machinery into the engine.**

### 4. The client currently emits NO op-log for born-in-editor
Born-in-editor sends `{ contentModel }` (`ComposeWorkspace.tsx` ~L1094; `docxBridge.buildContentModel`). Unifying onto ops also requires a NEW content-model→op-log translation path (client or server) that does not exist.

## Net scope to complete Half 2 faithfully (NOT "minimal risk")

To actually retire `ComposeDocumentRenderer.SynthesizeDocument` for born-in-editor without regressing:
1. Add a **clean-authoring mode** to the engine (author onto an empty/baseline-less package as clean `w:r`/clean para-marks, not `w:ins`).
2. Implement the **`setBlockAttr` applier** (Style / Alignment / ListOrdered / ListLevel).
3. **Author Heading1-6 styles + the FR-27 style-linked multi-level clause numbering + list-num instances** into the (previously empty) package — i.e. port the renderer's keystone into the engine.
4. Add a **content-model→op-log** translation path + seed an empty shadow package.
5. Retire `SynthesizeDocument`; re-prove fidelity (esp. FR-27 clause numbering) across the corpus.

This is a multi-day, high-blast-radius change on the shared spine that **reintroduces exactly the FR-27 fidelity complexity Path C was chosen to avoid.** Doing it silently would be a §6.5 violation (scope expansion far beyond the owner's signed-off "minimal risk" Path C).

---

## 🔔 ADR / Scope Conflict — Resolution Required (root §6.5 format)

- **Decision in question**: Path C (owner, 2026-07-22) — *"unify born-in-editor onto the op model (no table case), retiring the second byte-author path for born-in-editor"* on the stated basis of **minimal risk**.
- **Conflict**: The op model + engine cannot author clean, styled, clause-numbered born-in-editor content onto an empty package. Faithful unification requires porting the renderer's clean-authoring + FR-27 numbering into the engine + a new content-model→op-log path (multi-day, high-risk) — not the minimal change Path C assumed. The naive "insert-everything op set" reading produces an **all-tracked-changes, unstyled, unnumbered** document (regression).
- **Proposed paths for the owner**:
  - **(C-revised) Keep the renderer as a documented, narrow exception.** Land Half 1 (insertTable gating, done). Leave born-in-editor on `ComposeDocumentRenderer.SynthesizeDocument` as an explicitly-scoped **§6.5 Path-A interim exception** (a second *clean-authoring* byte-author — NOT a text-search author; I-7 is fully honored, the I-5 "one byte-author" invariant carries a cited born-in-editor exception). This is the current state; it ships R4 with zero fidelity regression. **Recommended.**
  - **(B-full) Fund the faithful unification** as its own opus-tier task (the 5-step scope above), with a corpus fidelity re-proof gate. Only worth it if a genuinely-single byte-author is a hard R4 exit requirement.
  - **(hybrid) Move the renderer BEHIND the engine** — expose `ComposeShadowPatchEngine` as the single public byte-author that internally delegates born-in-editor to the renderer's authoring core. Satisfies "one byte-author class" cosmetically without the op-log translation, but is refactor-for-its-own-sake and does not make born-in-editor "go through the op log" as FR-09 literally states.
- **Impact if C-revised is accepted**: `SynthesizeDocument` + client `buildContentModel` are RETAINED (they have no other blocker); the born-in-editor `<owner-decision-resolved>` note on the 037 POML and FR-09's "no separate full-render path remains" acceptance need amending to carve out the born-in-editor clean-authoring exception (I-5 exception, cited). Task 033 does NOT complete as "unified"; it converts to "born-in-editor kept on the clean renderer under a cited I-5 exception."
- **Alternative considered (and rejected)**: implementing B-full silently under this task — rejected because it is a large, high-risk spine change the owner explicitly did NOT sign off (Path C was chosen for the opposite reason), and §6.5 forbids silent scope expansion.

## What I-5 / I-7 actually require (why C-revised is honest, not a cheat)
I-7 (no write-path text-search) is the load-bearing correctness invariant and is **fully satisfied** — `SynthesizeDocument` authors from a paraId-keyed content model, zero text-search (it was the *deterministic replacement* for the text-search authors). I-5 ("a single Patch Engine writes the package") is the invariant with the born-in-editor tension; C-revised keeps a SECOND *clean-authoring* byte-author for the born-in-editor surface only, cited as a Path-A exception — materially different from the text-anchored second author (`DocxAnnotationWriter`) that task 036 correctly retired.

## Verification I DID run (Half 1)
- Client build (`npm run build`, `Spaarke.Compose.Components`) + the touched vitest files — see the task report / commit.
- No BFF change in this task ⇒ publish size + CVE unchanged (no delta); no seam slice for born-in-editor-through-engine (that path is blocked — see above).
