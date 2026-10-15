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
| **Task** | D-140: #1111 (docs branch → master) then **035** dev BFF deploy from master. |
| **Blocker** | None. Owner questions below do not block the deploy. |
| **Next Action** | Watcher `merge-when-green3.sh 1111 ae9bf62c3… master` is armed. When #1111 merges: send the master SHA to ebilling-r1; message word-add-in, R3 and uac-r2 BEFORE deploying; deploy master via `scripts/Deploy-BffApi.ps1` from a clean worktree at the master merge commit (hash-verify + `/healthz`); message all three AFTER (R3 redeploys its branch on top). |

## Merged 2026-10-14/15 (docs branch)

042 #1878 `6a00d1c47` · 038 #1891 `b0fe65988` · uac K1 walker batching #1892 `ae9bf62c3` (uac-r2 authored; our adversarial review CLEAN). 038/042 marked done.

## Deploy plan (D-126, D-138 approved)

**Merging to master does NOT deploy** — `deploy-bff-api.yml` lost its `push: master` trigger on 2026-08-11; deploys are
operator-driven (`/bff-deploy`, `scripts/Deploy-BffApi.ps1`) or `workflow_dispatch`. The schema pre-flight passed on dev (2026-10-14).

1. **035 BFF** from master (includes #1885 sidecar fix and #1887). Notify before AND after: word-add-in (`spaarke-wt-spaarkeai-word-add-in-r1-ea`, owner UATs add-ins on dev), R3 (`spaarke-wt-spa-external-access-platform-e0`, dev currently runs R3 `dfb4b9a43`; they redeploy on top), uac-r2 (`spaarke-wt-unified-access-control-r2-ac`).
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
- Pending push after #1111 merges (do not push earlier, it restarts #1111 CI): local edits to CLAUDE.md, decisions.md (D-141..D-144), current-task.md, `Roles/Spaarke Basic User.xml` (+`prvReadsprk_Policy` Deep, matches dev) + a `notes/security-roles.md` entry + #1355 note (D-110).

## Coordination

- uac-r2 owns #1894 (typed read-rows wrapper for the walker seed; K2 of #1892). Post ontology review requests on #1355 too.
- ebilling-r1 (`spaarke-wt-ebilling-r1-47`): send the #1111 master SHA; their PRs (FR-15 schema, FR-06 `sprk_invoicestatus` retirement) follow under the agreed conditions; FR-06 supersedes D-137 Q24 (`CountedInvoiceStatuses` → statuscode).
