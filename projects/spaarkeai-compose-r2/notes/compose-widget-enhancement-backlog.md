# Compose Widget — Enhancement Backlog

> **Created**: 2026-07-14
> **Status**: Scoped backlog (not yet prioritized/scheduled). Candidate for a future compose-r2 round or a follow-on project.
> **Source of ideas**: transferable patterns from the Feb-2026 `projects/sdap-WORD-studio-r2/design.md` (a sibling *standalone* Document Studio design). Only the DOCX/round-trip/redline patterns are imported — NOT its AI-orchestration wiring (see stale caveat below).

---

## 🚨 CRITICAL binding constraint — no TipTap product features

**We MUST NOT embed any TipTap "product feature" extension — paid OR unpaid — to avoid IP / source-code entanglement and vendor lock-in.** This is an owner-set hard rule (2026-07-14).

- **Allowed**: the MIT-licensed base editor only — `@tiptap/core`, `@tiptap/react`, `@tiptap/starter-kit`, and the free MIT extensions already in use (underline, link, image, table, task-list, character-count, text-align). MIT is permissive (attribution only; no copyleft/source obligation).
- **Forbidden**: `@tiptap-pro/*` and any TipTap feature product — **TrackChanges, Comments**, AI/Content-AI, Collaboration/Hocuspocus, Pages, Import/Export converters, etc. Do not add these under any circumstance.
- **Current state (verified 2026-07-14)**: build uses ONLY MIT `@tiptap/*` packages; **zero** `@tiptap-pro` / collaboration references across `Spaarke.Compose.Components` and `SpaarkeAi`.

> **Open policy question (only if owner intends the stricter reading)**: the rule above bans TipTap *feature products*. If the intent is instead "**no TipTap-authored code at all, including the MIT base editor**," that is a much larger decision — it means replacing the entire editing surface (direct ProseMirror or another engine). Not recommended now (MIT base carries no source/IP obligation today), but flagged for an explicit decision if desired. **Default working assumption: ban TipTap feature products; keep the MIT base.**

---

## E4 — Track changes & comments: ALREADY SATISFIED (home-grown, no Pro) ✅

Track changes and comments are implemented **entirely in Spaarke code**, not via any TipTap feature:
- Client: custom marks → `DocxAnnotationInput` contract; `redlineMarksToDocxAnnotations` + `anchoredAnnotationsToDocxAnnotations` bridges (`useComposeWordShuttle.ts`, `docxBridge.ts`, `ComposeEditor.tsx`).
- Server: `DocxAnnotationWriter.cs` emits **native OOXML** `w:ins` / `w:del` / `w:comment`; `DocxAnnotationReader.cs` + `AnnotationReanchorService.cs` round-trip.
- **Action**: keep home-grown. Do NOT migrate to TipTap Pro. This item exists to record the decision, not to build anything.

---

## Enhancement candidates (from WORD-studio transferable patterns)

### E1 — Retained-original-OOXML + delta export (fidelity keystone) — HIGHEST VALUE
The strongest idea in the Studio design: the server keeps the **original OOXML** (session-cached) and export **applies edits as a delta to that original**, preserving styles/headers/footers/formatting TipTap can't represent.
- **Why**: directly targets the round-7 fidelity bug class (redlines→Word, accepted-text-lost).
- **Action**: verify our Save applies annotations to a **retained true original baseline**, not a reconstruction from TipTap JSON. If it reconstructs, fidelity is fragile by design → harden to the retained-original model. (Needs a gap-check of `ComposeService.SaveAsync` + `DocxAnnotationWriter` baseline handling.)

### E2 — paragraphId ↔ OOXML paragraph-ID mapping
Preserve OOXML paragraph IDs on import so redlines/comments anchor to exact OOXML positions on export.
- **Why**: precise redline/comment placement on round-trip; avoids offset drift.
- **Action**: assess whether our annotation anchoring already carries stable paragraph identity (`AnnotationReanchorService` suggests partial coverage) and close gaps.

### E3 — Redline structured-contract enrichment
Enrich the redline payload with `{ reason, confidence, startOffset, endOffset, paragraphId }` and surface **inline accept/reject + a reasoning tooltip** — building on the EXISTING home-grown redline bridge (no TipTap feature needed).
- **Why**: sharper placement + better "offer a suggestion, insert with track changes" UX (the owner's robust-bridge edit model).
- **Action**: extend our `DocxAnnotationInput` / redline mark contract; render reason/confidence in the bubble UI.

---

## ⚠️ Stale-architecture caveat (do NOT import)
The WORD-studio design's AI-execution wiring (§6.1 — `PlaybookExecutionEngine`, `/api/ai/analysis/execute` with playbook scope) **predates the playbook→Action migration**. The playbook engine is being decommissioned. Any Compose AI execution MUST use the **Action spine (ADR-043)** — `IActionResolver` → `IActionRunner` — not the playbook path. Take the DOCX/round-trip/redline *patterns* from that doc; ignore its orchestration plumbing.

---

## Next step
Prioritize E1–E3 with owner (E1 first — it's the fidelity keystone and maps to the round-7 pain). E4 is decision-only (keep home-grown). None of these should start before compose-r2's current UAT closes + merges.
