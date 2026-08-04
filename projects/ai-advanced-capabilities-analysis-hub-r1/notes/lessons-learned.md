# Lessons learned — `ai-advanced-capabilities-analysis-hub-r1`

**Closed**: 2026-08-03 · **Outcome**: Analysis platform shipped (durable `sprk_analysis` spine + session binding + hub widget + per-type wizard + clean `sprk_analysisworkspace` retirement). ≈80% reuse composition.

---

## Corrections (things we got wrong first, then fixed)

1. **A1 lookup attribute name — shipped the wrong logical name.** First shipped `_sprk_agreementtypeid_value` / columnName `sprk_agreementtypeid`. That is the **reference table's PK**, not the lookup. The lookup attribute on `sprk_analysis` is `sprk_agreementtype` (OData `_sprk_agreementtype_value`). Caught by agreements-r1's MCP review, fixed in `7e022e7dd`.
   **Rule**: for a Dataverse lookup, the attribute logical name ≠ the target table's `{table}id` PK. Verify against metadata (`_{attr}_value`), don't infer from the table name.

2. **Headless modal opened the full workspace.** The first headless-open (`openSpaarkeAi` target:2) mounted every accumulated workspace tab, not just the clicked analysis. Fixed (`2a1615304`) by early-returning from tab-restore when `analysisLaunch?.mode === 'existing'` so a focused open loads ONLY that analysis (Compose + its history).
   **Rule**: "open this record" and "restore my workspace" are different intents — a focused/record-driven open must bypass accumulated-tab restore.

3. **Deployed un-merged work to a shared env → silently overwritten.** Deployed the focused-open fix before merging; another project's later master-build deploy overwrote it (SHA-256 mismatch caught it). Fixed by merging to master FIRST, then deploying from master, then hash-verifying the live web resource.
   **Rule (now in current-task + memory)**: on `spaarkedev1` (shared, very actively deployed), always **merge → build-from-master → deploy → hash-verify**. Never deploy un-merged to a shared env. See memory `[[handoff-requires-earned-context]]`.

4. **Q2 promote silent-FK gap.** `PromoteSessionToAnalysisAsync` ignored the bool from `BindSessionToAnalysisAsync`; when the `sprk_aichatsummary` row was missing it returned 201 with no durable FK → the session was invisible to `by-analysis`. Fixed (`2f8f11123`) by having `BindSessionToAnalysisAsync` **create** the anchor row with the FK when missing. agreements-r1 wrote `PromoteDurableFkVisibilityTests.cs` as an independent regression against the fix — it passes.

---

## Confirmed non-obvious approaches (worked; reuse next time)

1. **Platform ↔ review-machine split with an earned handoff, not a casual one.** Durable agreement-review work stayed with agreements-r1 because it already holds the classifier/schema/memo/compose-fidelity context — this project provides the substrate + a written contract (asks A1–A6, answers Q1–Q5), not a code dump. The two projects **cross-validate**: agreements-r1's `WorkspacePane.subdomain-envelope.test.tsx` (consumer of this project's A3) and `PromoteDurableFkVisibilityTests.cs` (regression for Q2) both pass against this project's code. See memory `[[analysis-hub-vs-agreements-split]]`.
   **Rule (CLAUDE.md §3 spirit)**: hand off only when the receiver is *better positioned by its existing context*, and always with a written contract + cross-checks — never to offload.

2. **Lookup-to-a-reference-table over an option set for the sub-domain registry.** Chose `sprk_agreementtype` (data-driven table; new type = add a row, no publish) over an option set (publish per type). Ownership split cleanly: this project owns identity columns (`sprk_key`/`sprk_name`/`sprk_isfallback`/`sprk_isselectable`/`sprk_sortorder`), agreements-r1 owns behavior columns (`sprk_knowledgepackref`/`sprk_classificationcue`/`sprk_confidencethreshold`). Alt-key on `sprk_key` gives stable seed/upsert.

3. **Server-minted session GUIDs → fork logic belongs on the server (UQ-1 Option B).** Because `ChatSessionManager` mints the sessionId server-side, fork-on-analysis is an atomic BFF endpoint (`POST /api/ai/analysis/fork`), not a client dance. Confirmed the right seam under §6.5 Path A (documented ADR-013/§10 exception).

4. **Retirement = repoint-before-delete, ordered and prescriptive.** The `sprk_analysisworkspace` retirement (tasks 060→064) repointed all deep-links/launch-points first, then deleted web resources + the `AnalysisWorkspace/` tree + the deploy script, then reconciled 4-name casing. `sprk_chathistory` has legit non-legacy writers — provenance was confirmed before touching it. Grep-clean verified at close (SC#7).

5. **`useOptionalPaneEventBus` to detect hosting.** The hub widget dispatches the headless-open intent only when a bus is present (`bus ? onRecordOpen : undefined`), so the same widget is safe both inside the workspace and standalone.

---

## Handoff / coordination artifacts (for the next round)

- `notes/COORDINATION-hub-r1-TO-agreements-r1.md` — reverse coordination (asks A1–A6, corrected naming).
- `notes/COORDINATION-hub-r1-ANSWERS-to-agreements-r1-Q1-Q5.md` — answers to their questions.
- `notes/test-diet-report.md` — project-close test reconciliation (6 files MAINTAIN, 0 scaffolding).

## Deferred (owner-gated, out of this project's code scope)

- **071 environment work**: importing the 4 ribbon buttons via `/ribbon-edit` + deleting the retired `sprk_analysisworkspace` web resources in the env, and the Matter/Project form subgrid placement. Code + ribbon scripts shipped; the Dataverse-environment application is an owner action. UAT-confirmed by owner 2026-08-03.
