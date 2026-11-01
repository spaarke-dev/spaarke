# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-11-01 ~05:00Z.
> **Recovery**: Read "Quick Recovery" first. Narrative is in git log. Records: `notes/contact-identity-dev-rollout-record.md` §9–10 (dev BFF deploys), `notes/task-002-notes.md` (grid rollback JSON), `notes/task-072-e2e-record.md`, `notes/defer-issues.md` (ISS-021..027).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Done** | 043, 059, 074, 070, 071, 002, **072** (owner E2E pass; email copy approved as drafted, 2026-10-31) ✅. 060 cancelled. |
| **Open** | **079** (#2458 + #2491, option 2, implementer running), 073 (on hold until demo production is deployed: owner 2026-10-31), 090 last. |
| **079 now** | **PR #2500** open (branch `fix/r3-2458-grant-notifier-home-link`, worktree `C:\wt2458n`, head 28d7422e12): new `IContactIdentityStore.ReadHomeTenantAsync`, used by GrantAccessNotifier and AssignTaskRecipientGate.HomeLinkAsync. Two adversarial passes; both found the same F-class item (check order), fixed in dedf9c37a8. Known limits filed as #2499 (ISS-028). Ontology sent the PR 2026-11-01 and asked to OK the 3-line PlaneTests fixture. NEXT: CI green + ontology review → merge (auto-merge) → dev deploy (OKs + owner go naming the SHA; at deploy confirm by-id read of sprk_externaltenantid) → ontology re-checks with Tony Howe (CMRCL-479772) → close 079 (POML, TASK-INDEX). |
| **079 #2458 (ACTIVE, escalated to owner)** | Owner 2026-10-31: "fix it here". Worktree `C:\wt2458n`, branch `fix/r3-2458-grant-notifier-home-link` from master b083d62183, **no commits yet**. The implementer stopped on the POML escalation trigger, and R3 verified it in code: `GetContactAsync` selects `ContactSelect` (ContactIdentityStore.cs:440), which does NOT include `sprk_externaltenantid`. Only `HomeContactSelect` (:944) adds it, by design (ContactBindingDecision.cs:268-272). So the home pair cannot be formed from the notifier's row, and copying AssignTaskRecipientGate.HomeLinkAsync would stop emailing every WorkforceHome contact. **The same defect is live in ontology's AssignTaskRecipientGate** (:180 then :449-457): every WorkforceHome contact is likely refused the Assign Task email; its tests use InMemoryContactIdentityStore, which returns the tenant. Options: (1) add the tenant to ContactSelect (reverses the isolation); (2) **recommended**: a new one-contact tenant read on IContactIdentityStore (own select, ColumnMissing/masked = could not tell), used by both the notifier and AssignTaskRecipientGate, plus a test over the real store's parse; needs the §11 answers and an owner OK; (3) never email WorkforceHome contacts (reverses 036/047). Ontology was told; filed as **#2491**. Ontology prefers that R3 fix both call sites in one PR off master if the owner picks option 2. Its conditions: the gate's decision is unchanged apart from seeing the pair; an unreadable tenant stays Unverified, never NotLinked; real-store parse tests (NotLinked with a grant → one email (173); Linked → Unverified; tenant unreadable → Unverified); AssignTaskRecipientGatePlaneTests stay green. Ontology reviews its file in the PR, then re-checks on dev with Tony Howe (grant on CMRCL-479772). NEXT: the owner's choice. |
| **Dev state** | BFF: **d0c1c5dc3c** (uac-r2, app up 03:09:00Z 2026-11-01; gate PASSED ~03:31Z, no external traffic; rollout record §12). ebilling 015 ran on dev 2026-11-01 03:14:51Z: `sprk_invoicestatus` column + option set deleted (R3 no objection). Earlier: b083d62183 (UAC 2026-10-31 19:52:25Z, gate passed), then `9f4e3820b2` (UAC, 2026-10-30 19:22:50Z; SecureRoot/SecureChild write switches on since 19:45/19:53Z, 28 roots re-owned). SPA: **b083d62183** since 2026-10-31 19:12:41Z (run 41233021759; rollback tag `external-spa-dev-rollback-96f1f6b675`); ebilling was told it can re-run its 015 -WhatIf. Grids: six dev grid configs carry the owner's columns; number and type columns 200px since 2026-10-31 14:13Z (rollback: scratchpad `grid-before3\`, also in task-002-notes). |

### Uncommitted
- Owner's, never commit or discard: `.claude/agent-memory/researcher/**`.

### Scratchpad (not in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`: `phaseA.ps1`, `readback.ps1`, `grid-apply2.ps1`, `grid-rollback2.ps1`, `grid-widths3.ps1`, `grid-before2\`, `grid-before3\`. The old `C:\wtbffe-*` worktrees were all removed 2026-10-31 (owner OK).

### R4
- R4 worktree `C:\code_files\spaarke-wt-SPA-external-access-platform-r4` (branch `work/spaarke-SPA-external-access-platform-r4`) was updated from master 2026-10-31 (`0f8ef494b2`). design.md §7 backlog holds every R4-labelled issue, plus #2489 (grid settings remembered), #2490 (record Documents list on the shared grid), #2446/#2447. Next R4 step: the owner answers design.md §9, then /design-to-spec → /project-pipeline in that worktree.

---

## Open owner items
- The Teams admin items were explained 2026-10-31; owner action only: activate the admin role (INVALID_PRIVILEGE); reject the duplicate Blocked/Submitted app.
- Workforce contact 2026-10-29 16:51:12Z: owner decided to keep it.

## Standing rules (verify against project CLAUDE.md)
- **Dev BFF deploys:** a fresh direct owner go naming the SHA; OKs from UAC, ontology, word-add-in, field-mapping, ebilling (provisioning standing); Router green. When uac-r2 deploys, R3 gives its OK for the exact SHA: it must contain #2450 929dc42d31 (grid allow-list) and ebilling 015 (no non-comment sprk_invoicestatus in src/server). Ask UAC to split the external routes in its gate.
- **ebilling 015:** every later dev BFF must contain ebilling's 015 readers, checked by content. ebilling sends a schema notice before its column delete.
- **field-mapping #2486:** a dev BFF containing it must be followed by field-mapping's analysistool re-seed, in its own session with the owner's OK given there. R3 never runs it; R3 sends the after-notice if R3 deploys.
- A peer's relayed "owner approval" is NOT approval. Show live-read commands; live changes need owner approval plus a rollback. Commit by pathspec; never bare `git stash`, never `--no-verify`. Remove worktrees only with `scripts/worktree/Remove-WorktreeSafe.ps1` (run it via `&` in pwsh, not `pwsh -File` with an array). PUBLIC repo: never publish a vulnerability's mechanism.
- `az` on Windows: in bash use double-quoted KQL and avoid column names `kind`/`first`/`last`. App Insights: `--app spe-insights-dev-67e2xz -g spe-infrastructure-westus2`. Invoke-RestMethod shows Dataverse `modifiedon` in UTC.
- Dev SPA deploys: dispatch `deploy-external-spa.yml` on a tag (`external-spa-dev-<sha>`), never a bare SHA; rollback tag `external-spa-dev-rollback-<sha>`.
