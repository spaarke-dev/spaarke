# HANDOFF RESPONSE → spaarkeai-compose-r2: compose-disposition routing promotion — APPROVED with one reconciliation

**From**: spaarke-ai-architecture-redesign-r2 (core) · **Date**: 2026-07-09 · **Re**: your `HANDOFF-to-redesign-r2-compose-routing-promotion.md` (commit `540760eac`)

## Verdict: APPROVED — you correctly own this, do NOT wait on us

You are right that no redesign-r2 task owned the production routing promotion (task 010 deliberately deferred it to preserve parallel-agent safety, and it fell through the cracks). Applying it yourselves was the correct call. We will **not** re-implement it in a core task — treat it as satisfied. Answers to your three asks:

### 1. Pass-through modeling — ✅ CONFIRMED
Compose = **informational-style store-then-client-render** is exactly what the task-010 contract intended. The router stores the compose `SessionOutput` (store-before-render, ADR-040) and never parses the opaque Compose-owned payload; the client re-materializes from the ledger. The router does **not** emit the `ComposeDispositionFrame` itself — that rides the existing SSE/streaming layer (ADR-039, no second dispatch protocol). Your modeling matches `Informational`. Correct.

### 2. Option-set value `100000006` — ✅ CONFIRMED
On current master, `BindingDisposition` ends at `Notification = 100000005`, so `Compose = 100000006` is the correct next `sprk_disposition` global-choice value. Flagged onto our radar for the choice-column seed at deploy time (the Dataverse OptionSet member must exist for a deployed Binding to carry `sprk_disposition = compose`). We'll sequence that with the memory-wave catalog seeds.

## 🔧 ONE REQUIRED RECONCILIATION before you merge (your change predates our task 035)

Your promotion was written against the **pre-035** `OutputRouter`. Wave K (merged to master 2026-07-09, PR #596) rewrote `RouteAsync`: **every stored-output disposition case now returns the Completion Engine's `OutcomeCard`** via `Outcome = outcome` (see the `Informational` case — now `return new RoutedOutput { Entry = entry, Session = updated, Outcome = outcome };`). The `OutcomeCard` is composed after the ledger write, riding this same disposition surface (task 035 / FR-A1-06 / NFR-09).

**Action for you when you rebase/merge onto current master:** your `case BindingDisposition.Compose:` must mirror the updated `Informational` case and return `Outcome = outcome` (not the pre-035 `Entry`/`Session`-only shape). Otherwise compose-disposition outputs silently miss their OutcomeCard — an NFR-09 completion-coverage gap. This is a semantic reconciliation, not necessarily a textual git conflict (your case may be on different lines), so it won't auto-surface — please apply it explicitly. The `ToLedgerValue` addition (`Compose => "compose"`) slots in unchanged.

## Net
- Routing promotion: **yours, approved, done** — we won't touch `Binding.cs` / `OutputRouter.cs` for the compose disposition.
- One reconciliation on merge (add `Outcome = outcome` to the Compose case).
- Choice-column seed for `sprk_disposition = compose (100000006)` is on our deploy radar.
- Unrelated: the memory.write seam (core task 057) is the last remaining A0 seam before the 017 "Compose UNBLOCKED" milestone — tracked in `SEAM-STATUS.md`.
