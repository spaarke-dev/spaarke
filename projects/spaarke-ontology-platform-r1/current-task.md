# Current Task State — Spaarke Ontology Platform R1

> **Last Updated**: 2026-10-15 (main session). State only — history is in git log, task `<completion>` blocks and
> `notes/handoff-history/2026-10.md`. Standing rules: [`CLAUDE.md`](CLAUDE.md) §3 (D-106 critical path; D-123 secure family;
> D-126 deploys from master; D-127 MVP first; D-129/D-130 team ownership; **D-138 owner approvals; D-139; D-140**) and §6 gotchas.
> Decisions: [`notes/decisions.md`](notes/decisions.md) (D-1..D-140). Deferred work: one self-contained GitHub issue per item,
> label `ontology-deferred`, plus a DI-nn row in `notes/defer-issues.md` (next free: **DI-88**).
> Sub-agents never edit this file.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | Post-deploy: 035 follow-ups + lanes 149/150/151. |
| **Blocker** | None. |
| **Next Action** | Wait for the four agents (149 sonnet/xhigh `C:\wts-149`; 150 opus `C:\wts-150`; 151 opus `C:\wts-151`; 035 follow-ups + 144/148 report-only, sonnet `C:\wts-035`). Then: send the report-only output to uac-r2; run 035's negative alert test AFTER R3 redeploys (R3 sends its SHA); send PRs 149/150/151 to uac-r2 (+ #1355), merge on CLEAN + green. |

## Done 2026-10-15

- **#1111 merged to master `f292ad119`** (D-140). ebilling-r1 has the SHA.
- **035 deploy:** master `f292ad119` on spaarke-bff-dev at ~05:00Z. Package 37.1 MB, 4/4 SHA-256 match, `/healthz`, `/healthz/dataverse` and signed-in `/api/v1/signals/worklist` all 200. word-add-in, R3 and uac-r2 notified before and after. R3 redeploys R3 + `f292ad119` on top.
- Owner decisions D-141..D-144 recorded. Tasks 150/151 created, 149 amended.

## Deploy plan (D-126, D-138 approved)

**Merging to master does NOT deploy** — `deploy-bff-api.yml` lost its `push: master` trigger on 2026-08-11; deploys are
operator-driven (`/bff-deploy`, `scripts/Deploy-BffApi.ps1`) or `workflow_dispatch`. The schema pre-flight passed on dev (2026-10-14).

1. ~~035 BFF deploy~~ done. Any later dev BFF deploy: notify word-add-in (`spaarke-wt-spaarkeai-word-add-in-r1-ea`), R3 (`spaarke-wt-spa-external-access-platform-e0`) and uac-r2 before AND after.
2. 035 follow-ups: job registered; alerts in `infrastructure/bicep/modules/alerts.bicep` (`ontology.writer.failures > 0` / 15 min, unfiltered; no successful `ontology.evaluator.runs`); negative alert test (blank the writer client id on dev, restore); deploy-log note; mark 035 done.
3. Re-run `SignalReadRouteLiveTests` as the non-Console user (DI-83 #1882: `prvReadsprk_Policy` Deep added to Spaarke Basic User on dev 2026-10-14) → close #1882.
4. Run 144's stamp job and 148's reconciler **report-only**, send to uac-r2. If CLEAN, enable on dev (D-138): `CoreAncestor__StampReconciliation__ReportOnlyTables=","` (= `scripts/Invoke-OntologyStampBackfill.ps1 -Apply`) and `Ontology__TodoAssigneeOwner__WritesEnabled=true`.
5. **146** live gate (after the 6 h DR metadata cache) → **059** (worklist widget; render `withheld: true` evidence lines, not "No value recorded") → **055** Console deploy → 049, 064, 105.
6. **149** (D-139 gate DR queue job, idempotent). Also carry: raise the empty-worklist refusal log (`SignalReadService.cs:526-531`) from Information to Warning (uac-r2 K3 on #1891).

## Owner decisions to implement (2026-10-15, after the deploy)

- **D-141** (DI-86 #1883): Do items private to the assignee — scope per-Signal routes (`SignalCoreRecordAccess`) to the worklist "mine" set.
- **D-142** (DI-80 #1873): hide evidence detail when the caller cannot read the witness — read-time batched caller read per witness table; match the 145 write-time rule.
- **D-144** (DI-84 #1876): unfiled-communication gate DRs owned by the BU default team — with task 149; uac-r2 reviews.
- D-143: audit stays on (DI-81 #1879 closed). Still open: DI-82 #1881 (`sprk_assignedto` vs `sprk_assignedtointernal`).

## Coordination

- uac-r2 owns #1894 (typed read-rows wrapper for the walker seed; K2 of #1892). Post ontology review requests on #1355 too.
- ebilling-r1 (`spaarke-wt-ebilling-r1-47`): send the #1111 master SHA; their PRs (FR-15 schema, FR-06 `sprk_invoicestatus` retirement) follow under the agreed conditions; FR-06 supersedes D-137 Q24 (`CountedInvoiceStatuses` → statuscode).
