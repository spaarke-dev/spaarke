# ISSUE — `OutputOrchestratorService` output mapping is unreachable for every playbook

> **Found**: 2026-08-25 by `record-header-and-notepad-r2` task **041** (RS-2), while tracing the
> consumer of `extraction.aiSummary`
> **Status**: 🔴 open — **out of R2 scope**, filed for evaluation as a focused fix
> **Severity**: high-confidence latent defect; silent (no error, no log, no failure)
> **Sibling issues**: [`README.md`](README.md) — Event · Daily Briefing · Work Assignment schema drift

---

## Summary

`OutputOrchestratorService.ParseOutputMapping` short-circuits to `null` for **every** playbook,
because the playbook loader never fetches the column the mapping lives in. Any `outputMapping`
authored in `sprk_configjson` is therefore dead configuration — it parses nothing and writes nothing,
without surfacing an error.

This was found incidentally. It is **not** the RS-2 defect (that one is fixed — see
[`rs2-registry-fix.md`](../rs2-registry-fix.md)); it sits one layer beneath it in a **different**
dispatch path.

## The chain

1. `PlaybookService.GetPlaybookAsync` (`Services/Ai/PlaybookService.cs:204`) builds its `$select`
   from the `PlaybookEntity` shape (class at ~line 1011). **`sprk_configjson` is not among the
   selected columns** — verified by grep: the string `sprk_configjson` does not appear anywhere in
   `PlaybookService.cs`.
2. `PlaybookResponse.ConfigJson` therefore always holds its default `"{}"`.
3. `OutputOrchestratorService.ParseOutputMapping` (`Services/Ai/OutputOrchestratorService.cs:317-346`)
   sees `"{}"`, finds no `outputMapping` key, returns `null`.
4. `ApplyOutputMappingAsync` no-ops. Nothing is written; nothing is logged as wrong.

Independently, the live data is empty too: the only Invoice-named playbook in `spaarkedev1`
("Finance Invoice Processing", `1e657651-9308-f111-8407-7c1e520aa4df`) has
`sprk_configjson = null`. So even a fixed loader would currently read nothing.

## Two distinct mechanisms — do not conflate them

This is the trap that made the original task-041 step-4 instruction wrong:

| | `OutputRouter` | `OutputOrchestratorService` |
|---|---|---|
| Config source | `sprk_aitopicregistry` row (`sprk_targetfield`) | playbook `sprk_configjson` → `outputMapping` |
| Status | **working** — RS-2 retargeted it to `sprk_recordsummary` | **unreachable** — this issue |
| Consumes `extraction.aiSummary`? | No | Yes (declared only in seed data) |

They are structurally different classes on different paths. The RS-2 registry fix does **not** give
`extraction.aiSummary` a destination.

## Consequence for `extraction.aiSummary`

`InvoiceExtractionJobHandler.cs:236` publishes `extraction.aiSummary` as a context variable.
**It has no live consumer at all.** Its only declared mapping lives in the seed reference file
`scripts/seed-data/playbooks.json` (not live data), targeting `sprk_aisummary` on `sprk_invoice` —
a column that does **not exist**. `sprk_invoice` carries `sprk_recordsummary`.

So the summary is generated on every invoice extraction and discarded. The handler's doc comment was
corrected in task 041 to say so plainly rather than name a destination that isn't real.

## What a fix would need to decide

1. Should `PlaybookService.GetPlaybookAsync` select `sprk_configjson`? (If yes, every existing
   playbook's `outputMapping` suddenly becomes live — that is a **behaviour change with blast
   radius**, not a trivial add. It must be audited before enabling.)
2. Should `extraction.aiSummary` be routed to `sprk_recordsummary` on `sprk_invoice` via the
   registry mechanism instead — i.e. retired from the `outputMapping` path entirely?
3. Is `outputMapping` still the intended mechanism at all, or has the topic-registry path superseded
   it? If superseded, the right fix is deletion, not repair.

Question 3 should be answered first. Repairing a superseded mechanism would be worse than removing it.

## Evidence

- `grep -n "sprk_configjson" Services/Ai/PlaybookService.cs` → no matches (re-verified in main
  session, not taken on trust from the sub-agent report)
- `Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs:236` → `context.SetVariable("extraction.aiSummary", …)`
- Live query of `spaarkedev1` playbook `1e657651-9308-f111-8407-7c1e520aa4df` → `sprk_configjson: null`
- `scripts/seed-data/playbooks.json` → the only `outputMapping` naming `extraction.aiSummary`

## Not in scope for R2

R2 is a client-side PCF project ([`CLAUDE.md`](../../CLAUDE.md): "MUST NOT add any endpoint, service,
or DI registration to `Sprk.Bff.Api`"). Task 041's constraint limited BFF changes to a comment block,
which is all that was made. This issue is filed so the finding is not lost — same treatment as the
three schema-drift issue docs.
