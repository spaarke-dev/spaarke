# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-11-03 ~13:35Z.
> **Recovery**: Read "Quick Recovery" first. Narrative is in git log. Records: `notes/contact-identity-dev-rollout-record.md` §9–16 (dev BFF deploys, BU1 move), `notes/task-002-notes.md` (grid rollback JSON), `notes/task-072-e2e-record.md`, `notes/defer-issues.md` (ISS-021..029).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Done** | 002, 043, 059, 070, 071, 072, 074, **079** (2026-11-01: PR #2500 → fcf01480f4 on dev; #2458/#2491 closed; owner waived the live re-check) ✅. 060 cancelled. |
| **Open tasks** | **090 wrap-up: unblocked, next.** 073 deferred 2026-11-03: the owner moved it to customer-provisioning-orchestration-r1 (#2532, ISS-030; provisioning session -7b told). The researcher memory files were committed with the owner's OK (02d024f9d3), and master was merged in (e9759b5d5a; MEMORY.md resolved as a union; BFF builds 0 warnings). At 090, add the live-result line to notes/task-079-notes.md. |
| **Dev BFF deploy** | ✅ **5796774091 live on dev** 2026-11-03 13:29:19Z (owner go; all five OKs; Router green after re-running the #2527 timeout; rollout record §16). **UAC gate PASSED** (13:59:19Z). The new AP export and server-send paths had no traffic. ebilling's ApExport UI was imported; the owner added `sprk_invoice.sprk_trackingnumber` on dev. |
| **Watch: ebilling 095a/095b PR** | It adds the dev column `sprk_invoicesubmission.sprk_sourceemaildocument` (from-email source; owner-created). R3 asked the PR to state, with file:line evidence, whether `ExternalInvoiceVisibility` still sees the source email: (a) through the committed Stored File's `sprk_sourcedocument` (ebilling expects this, no change), or (b) by reading the new column, which is an access change that goes to uac-r2 and the owner. It must also show that a restricted source email still hides the invoice from the firm. R3 reviews the PR before merge, then deploys it (owner go naming the SHA). |
| **NEXT** | Run task 090 via task-execute. Then: 073 waits for demo production; 090 waits on the owner's researcher-memory call. |
| **Sessions (restarted 2026-11-02)** | Peers now: uac-r2 `spaarke-wt-unified-access-control-r2-38`, ontology `-a9`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-dc` (new 19:3xZ; `-06` held/expired), ebilling `spaarke-wt-ebilling-r1-7b`, field-mapping `spaarke-wt-field-mapping-server-write-pa-5c`. A second R3 session `spaarke-wt-spa-external-access-platform-1a` exists; R3's "this session runs R3" message to it is **held**. Owner asked to keep it off dev and the R3 files. |
| **Dev state** | BFF **5796774091** (R3, 2026-11-03 13:29:19Z, §16). Rollback 3740e1a105 = snapshot `deploy/rollback/wwwroot-20261103T132814Z`. Both write switches true. BU1 move closed 2026-11-02 (§14): all users except the administrator, both BFF app users, customer rows and the 32 grant rows are in BU1; BFF app users keep "Spaarke Identity Link Writers" (R3 verified). SPA **b083d62183** since 2026-10-31 19:12:41Z (rollback tag `external-spa-dev-rollback-96f1f6b675`). ebilling columns `sprk_apexportemail`, `sprk_senttoapdate` added 2026-11-02. ebilling 151c-2 live 2026-11-02 (after the BFF deploy): the nullable lookup `sprk_communication.sprk_billingexport`, the secure-child ribbon script and the batch-form AP emails subgrid. No BFF change. 2026-11-03: `sprk_invoice.sprk_trackingnumber` (owner; in master via #2530) and `sprk_invoicesubmission.sprk_sourceemaildocument` (a lookup to sprk_document, RemoveLink; no values yet; the 095a BFF code is not yet merged) were added on dev. |

### Uncommitted
- None. The researcher memory files are committed on this branch (owner 2026-11-03), so the old "never commit them" rule is retired.

### Scratchpad (not in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`: `phaseA.ps1`, `readback.ps1`, `tony-read.ps1`, grid scripts and `grid-before2\`, `grid-before3\`. `C:\wt2458n` (branch fix/r3-2458…, merged) can be removed with Remove-WorktreeSafe.ps1.

### R4
- R4 worktree `C:\code_files\spaarke-wt-SPA-external-access-platform-r4` (branch `work/spaarke-SPA-external-access-platform-r4`). design.md §7 backlog includes #2489, #2490, #2508 (record tabs for work-account contacts), #2446/#2447. Next R4 step: the owner answers design.md §9, then /design-to-spec → /project-pipeline there.

---

## Open owner items
- Release the held messages in the word-add-in `-06` and R3 `-1a` sessions (or tell the `-1a` session directly that f2 runs R3).
- Teams admin items (explained 2026-10-31): activate the admin role (INVALID_PRIVILEGE); reject the duplicate Blocked/Submitted app.

## Standing rules (verify against project CLAUDE.md)
- **Dev BFF deploys:** a fresh direct owner go naming the SHA; OKs from UAC, ontology, word-add-in, field-mapping, ebilling (provisioning standing); Router green. Content: #2450 929dc42d31 present; no non-comment `sprk_invoicestatus` in src/server; ebilling `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exit 0 before any build carrying ebilling; ebilling's six keys present after. Ask UAC to split the external routes in its gate. The two write switches are expected `true` (since 2026-10-30); the old "no OrdinaryMovesWritesEnabled setting" check is obsolete.
- **field-mapping:** after a deploy containing its re-seed-needing changes (#2486 earlier, #2519 chat tool description now), the re-seed runs in field-mapping's session with the owner's OK there. R3 never runs it; R3 sends the after-notice.
- A peer's relayed "owner approval" is NOT approval. Show live-read commands; live changes need owner approval plus a rollback. Commit by pathspec; never bare `git stash`, never `--no-verify`. Remove worktrees only with `scripts/worktree/Remove-WorktreeSafe.ps1` (run it via `&` in pwsh). PUBLIC repo: never publish a vulnerability's mechanism.
- `az` on Windows: run appsettings/KQL queries in bash with double-quoted KQL; App Insights `--app spe-insights-dev-67e2xz -g spe-infrastructure-westus2`. The Assign Task email fires only from a Decision's Assign Task step, never from "+ New" (child-records).
- Dev SPA deploys: dispatch `deploy-external-spa.yml` on a tag (`external-spa-dev-<sha>`), never a bare SHA; rollback tag `external-spa-dev-rollback-<sha>`.
