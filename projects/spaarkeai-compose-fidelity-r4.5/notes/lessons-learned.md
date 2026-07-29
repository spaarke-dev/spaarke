# Lessons learned — spaarkeai-compose-fidelity-r4.5

> Read/reference legal fidelity for Compose. 22 tasks, merged to master + deployed to dev 2026-07-28, archived 2026-07-29.

## What went well

- **A tight design → spec produced clean, literal tasks.** The design doc already carried closed success-criteria, ADR Tensions, and a §11 table; the spec inherited them, and the POMLs decomposed cleanly. Nearly every task landed first-try with 0 build errors and green tests.
- **The write↔read round-trip test earned its keep.** The 24-case golden corpus was *right but incomplete* — every corpus doc used a single `numId` per `abstractNum`, so it never exercised two-lists-separated-by-prose. The round-trip test (WS-3 task 033) authored via the real write-side renderer and caught **DEF-03** (the engine keyed its counter by `abstractNumId` instead of `numId`, mis-numbering the 2nd list). **Lesson: a golden corpus proves what it contains; an author↔reader agreement test proves the model.** Pair them.
- **Widget-decoration for the number-atom was the right structural call.** Rendering the computed number as a ProseMirror *view decoration* (not a doc node) made "non-editable, read-time-only, can't shift offsets" a structural guarantee rather than a convention — no way to accidentally leak it into the tracked-edit stream (that's R5 G3).
- **Refusing to fabricate beat guessing.** For `w:sym`/footnote/endnote numbers with no safe mapping, the engine emits a warned placeholder rather than an algorithmic guess. For a legal tool, *a wrong number is worse than an absent one* — this principle recurred across WS-2 and WS-3 and kept the fidelity honest.
- **Reuse-first paid off repeatedly.** Upload/browse reused the Load-path projection shape (one `MapProjectionResponse`); the reference map reused the existing `ChatSession`/`StoredSession` stack (no new store); the citation resolver is a pure function over the existing map (no endpoint/DI). Zero new runtime packages, ~0 MB publish delta.
- **Autonomous parallel execution worked because the graph was honest.** Most tasks were `parallel-safe:false` (shared `Services/Compose/` files), so the "parallelism" was really WS-5 research running alongside the sequential main line — and the plan said exactly that. Build-verify-between-tasks + commit-per-task made the long run fully recoverable.

## What was tricky / would do differently

- **Audit-before-implement saved a redundant task.** WS-1 task 012 (transient mount) turned out to be already satisfied by 010+011; the audit-first instruction turned it into a regression-guard task instead of duplicate work. **Lesson: for "wire up X" tasks, instruct the agent to prove X isn't already wired first.**
- **The null-projection path was a real design nuance, not a mechanical delete.** Removing mammoth meant `projection:null` (browse + BFF unreachable) had *no reader*. The fix (an explicit error state, browse now depends on the server projection per T-2) was the right call but wasn't obvious from "delete the fallback." **Lesson: deleting a fallback is a behavior change on the fallback's callers — reconcile them explicitly.**
- **`sed` on Unicode tables / `python` cp1252 on Windows** repeatedly bit status-table edits (`✅`/`≤`/`§`). Use the Edit tool for Markdown tables, not `sed`; set `PYTHONIOENCODING=utf-8` if scripting.
- **The main-repo local-master sync got blocked by another project's uncommitted docs.** Correct handling was to *surface, not clobber* — the R5 project owned those files. **Lesson: never force a housekeeping sync through another project's uncommitted work.**

## Cross-project / follow-ons

- **WS-4's value shows up at the consumer.** The reference/citation layer is backend data until a UI consumes it. Dev-UAT immediately surfaced the payoff: review-note cards cited "Para 3" (doc-order) instead of "Sec 2" (computed) — fixed in task 043 by having `ndaClauseLocation` read `computedNumber`. **Lesson: ship at least one consumer of a new data layer in the same project, or its value is invisible.**
- **DEF-01** (advisory-comment *placement* target-resolution) is distinct from the label — handed to `ai-advanced-capabilities-agreements-r1` (nda-r1 closed), along with the `ndaClauseLocation` naming generalization (the logic is already document-agnostic).
- **WS-5 as decision-only** was the right scope. Page/line isn't in the `.docx`; the honest ceiling is "Word-Online-identical." Deferring implementation (with measured LibreOffice divergence + the NFR-03 licensing path recorded) avoided over-promising 100% fidelity.

## Process notes
- Portfolio: this project was never portfolio-registered (README had no pointer at init → the `/project-pipeline` hook no-op'd). Archived via local `.archived` marker; `/devops-project-register --from-folder` would retrofit a Project Issue if wanted.
- Test-diet: 0 scaffolding, 0 deletions — maintain-class by construction (seam/KEEP-path, golden-value, real fixtures). See `notes/test-diet-report.md`.
