# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-11-02 ~19:00Z.
> **Recovery**: Read "Quick Recovery" first. Narrative is in git log. Records: `notes/contact-identity-dev-rollout-record.md` §9–15 (dev BFF deploys, BU1 move), `notes/task-002-notes.md` (grid rollback JSON), `notes/task-072-e2e-record.md`, `notes/defer-issues.md` (ISS-021..029).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Done** | 002, 043, 059, 070, 071, 072, 074, **079** (2026-11-01: PR #2500 → fcf01480f4 on dev; #2458/#2491 closed; owner waived the live re-check) ✅. 060 cancelled. |
| **Open tasks** | 073 (on hold until demo production is deployed: owner 2026-10-31). 090 last. **Blocker for 090:** merging origin/master into this branch is refused because master also changes `.claude/agent-memory/researcher/MEMORY.md`, which holds the owner's uncommitted edits (never commit/stash/discard them). Owner decides how before 090. At 090, add the live-result line to notes/task-079-notes.md (it is on master). |
| **Batched dev BFF deploy** | ✅ **3740e1a105 live on dev** 2026-11-02 18:52:25Z (owner go; OKs ontology/uac/field-mapping/ebilling; word-add-in request expired undelivered; rollout record §15). **UAC gate PASSED** (19:22:25Z). The word-add-in after-notice expired undelivered too. uac-r2 is checking with field-mapping a ROOT-OWNER census +1 (a project without a matter link row; information only). |
| **NEXT** | Nothing active in R3: 073 waits for demo production; 090 waits on the owner's researcher-memory call. |
| **Sessions (restarted 2026-11-02)** | Peers now: uac-r2 `spaarke-wt-unified-access-control-r2-38`, ontology `-a9`, word-add-in `spaarke-wt-spaarkeai-word-add-in-r1-dc` (new 19:3xZ; `-06` held/expired), ebilling `spaarke-wt-ebilling-r1-7b`, field-mapping `spaarke-wt-field-mapping-server-write-pa-5c`. A second R3 session `spaarke-wt-spa-external-access-platform-1a` exists; R3's "this session runs R3" message to it is **held**. Owner asked to keep it off dev and the R3 files. |
| **Dev state** | BFF **3740e1a105** (R3, 2026-11-02 18:52:25Z, §15). Rollback fcf01480f4 = snapshot `deploy/rollback/wwwroot-20261102T185123Z`. Both write switches true. BU1 move closed 2026-11-02 (§14): all users except the administrator, both BFF app users, customer rows and the 32 grant rows are in BU1; BFF app users keep "Spaarke Identity Link Writers" (R3 verified). SPA **b083d62183** since 2026-10-31 19:12:41Z (rollback tag `external-spa-dev-rollback-96f1f6b675`). ebilling columns `sprk_apexportemail`, `sprk_senttoapdate` added 2026-11-02. |

### Uncommitted
- Owner's, never commit or discard: `.claude/agent-memory/researcher/**`.

### Scratchpad (not in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`: `phaseA.ps1`, `readback.ps1`, `tony-read.ps1`, grid scripts and `grid-before2\`, `grid-before3\`. `C:\wt2458n` (branch fix/r3-2458…, merged) can be removed with Remove-WorktreeSafe.ps1.

### R4
- R4 worktree `C:\code_files\spaarke-wt-SPA-external-access-platform-r4` (branch `work/spaarke-SPA-external-access-platform-r4`). design.md §7 backlog includes #2489, #2490, #2508 (record tabs for work-account contacts), #2446/#2447. Next R4 step: the owner answers design.md §9, then /design-to-spec → /project-pipeline there.

---

## Open owner items
- Release the held messages in the word-add-in `-06` and R3 `-1a` sessions (or tell the `-1a` session directly that f2 runs R3).
- Before 090: how to handle the uncommitted researcher memory files (they block merging master into this branch).
- Teams admin items (explained 2026-10-31): activate the admin role (INVALID_PRIVILEGE); reject the duplicate Blocked/Submitted app.

## Standing rules (verify against project CLAUDE.md)
- **Dev BFF deploys:** a fresh direct owner go naming the SHA; OKs from UAC, ontology, word-add-in, field-mapping, ebilling (provisioning standing); Router green. Content: #2450 929dc42d31 present; no non-comment `sprk_invoicestatus` in src/server; ebilling `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exit 0 before any build carrying ebilling; ebilling's six keys present after. Ask UAC to split the external routes in its gate. The two write switches are expected `true` (since 2026-10-30); the old "no OrdinaryMovesWritesEnabled setting" check is obsolete.
- **field-mapping:** after a deploy containing its re-seed-needing changes (#2486 earlier, #2519 chat tool description now), the re-seed runs in field-mapping's session with the owner's OK there. R3 never runs it; R3 sends the after-notice.
- A peer's relayed "owner approval" is NOT approval. Show live-read commands; live changes need owner approval plus a rollback. Commit by pathspec; never bare `git stash`, never `--no-verify`. Remove worktrees only with `scripts/worktree/Remove-WorktreeSafe.ps1` (run it via `&` in pwsh). PUBLIC repo: never publish a vulnerability's mechanism.
- `az` on Windows: run appsettings/KQL queries in bash with double-quoted KQL; App Insights `--app spe-insights-dev-67e2xz -g spe-infrastructure-westus2`. The Assign Task email fires only from a Decision's Assign Task step, never from "+ New" (child-records).
- Dev SPA deploys: dispatch `deploy-external-spa.yml` on a tag (`external-spa-dev-<sha>`), never a bare SHA; rollback tag `external-spa-dev-rollback-<sha>`.
