# Current Task State

> **Last Updated**: 2026-10-30 ~15:45Z (context-handoff). Work branch origin/work/ebilling-r1 = 932efea9cc (+ local docs commit for 015 notes / this file). eBilling is ON in dev.

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Work branch** | origin/work/ebilling-r1 = 932efea9cc: 101, 104, twin test, 150 + fixes + batch-minimum relax, 152, 097a, 097b. Gate on that tip: ArchTests 1858/0, unit filter 10014/0/7, integration 199/0/8 (one 097b×097a test fixed in 932efea9cc). |
| **Running** | **097c** (opus, branch task/ebill-097c-supersede-commit from 932efea9cc): persister supersedes the old invoice in the commit; undo reopens it; re-index. |
| **Next action** | On 097c's report: merge into work, gate (5 builds, ArchTests, unit filter, integration), push; then launch 097d (after-step closes the old invoice's Signals; resume/idempotency; TWO adversarial passes — tagged auth). |
| **Waiting on owner** | 151 UI surface: new "AP Export" code page under Work in Matter Management (recommended) vs invoice views + ribbon buttons. Explanation was given; no answer yet. |
| **Master** | eaa0d44f7f (#1911) + later peers. Next ebilling PR to master: must include 097b AND 097c together (ontology-r1 condition), the full integration counts, ontology-r1 review. |

## Dev state (spaarkedev1 / spaarke-bff-dev)
- BFF 9f4afe82ac (R3 deploy, UAC gate passed 15:12Z). EB-D-173 settings applied 15:21Z: `Ebilling__Enabled=true`, `Ebilling__StoredFileReconcileEnabled=false`, `Finance__FileStore__BlobEndpoint` (sprkspaarkedevebillsa), `Finance__FileStore__TenantId` = dev OrganizationId 0c3e6ad9-…, `Finance__FileStore__MalwareScanningEnabled=false`. healthz 200; only pre-existing exceptions (#2163). Rollback: remove the five settings.
- `Deploy-EbillingInvoiceTotalsRange.ps1` run on dev 2026-10-30 (owner OK): 7 Money minimums widened; re-run skips all 7.
- **015 column delete** (D-97): BFF attestation 9f4afe82ac passes; SPA 96f1f6b675 refused (JSDoc mention; 015 notes §12). Waits for R3's next dev SPA deploy from master (≥ e0df773395; R3 confirmed master qualifies; needs owner go in R3's session). Then: `-WhatIf`, schema notice to R3 + uac-r2, live `-Delete`.
- word-add-in told 095 is live on dev; it keeps Correction off until 097b+097c+097e reach master.

## Open tasks after 097c
097d → 097e (prefill read; send word-add-in its shape before building). 151 (after owner's UI answer). 161 (calibration; needs dev pipeline runs — now possible). 153 live steps (index deploy, `AzureAd:TenantId` — already set on dev, DI setting). 086/089/092 branches. 096 waits on word-add-in. Close #2455 after work → master.

## Peer agreements this session
- ontology-r1: 097a loader OK; 097b loader OK + K6 (noted) + ship 097b only with 097c.
- word-add-in-r1: 097b contract agreed.
- field-mapping (017): reviewed f758480f3c OK; they protect all six family tables from mapping (columns from the guard) and refuse submission/approval/correction/external-identifier in chat create.

## Standing rules (see project CLAUDE.md)
No bare git stash, no --no-verify. Live writes and role changes need the owner's OK; schema runs need notices. StoredFileReconcileJob stays off until F4 merges and DEF-044 is fixed. After merges: 5 builds, ArchTests, broad filter before pushing (ISS-005 is flaky, #2469: re-run alone). Remove worktrees only with Remove-WorktreeSafe.ps1. Max 4 concurrent subagents; every agent launch states a model. No bare jq, no grep -P.
