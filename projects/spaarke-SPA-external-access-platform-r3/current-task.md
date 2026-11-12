# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-11-03 ~23:00Z (090 done; #2546 merged; d763dc120e deployed, UAC gate PASS; deploys handed to uac-r2).
> **Recovery**: Read "Quick Recovery" first. Narrative is in git log. Records: `notes/contact-identity-dev-rollout-record.md` §9–16 (dev BFF deploys, BU1 move), `notes/task-002-notes.md` (grid rollback JSON), `notes/task-072-e2e-record.md`, `notes/defer-issues.md` (ISS-021..029).

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Project state** | **All R3 tasks closed.** 090 wrap-up ✅ 2026-11-03 (`notes/task-090-notes.md`). 073 deferred to customer-provisioning-orchestration-r1 (#2532). 060 cancelled. |
| **NEXT** | (1) PR #2550 (doc-comment close tag plus route-ledger line cite) auto-merges on Router. (2) This branch's post-merge commits (rollout record §17, current-task) reach master by PR. (3) `/devops-project-archive` for R3. Peer follow-ups (field-mapping FR-40 live proof; word-add-in Office smoke after the owner's two Outlook saves) report to uac-r2. |
| **Dev BFF deploy** | **d763dc120e live on dev since 2026-11-03 22:26:31Z** (rollout record §17; UAC gate PASS 22:56:31Z). Rollback: snapshot `deploy/rollback/wwwroot-20261103T220358Z` (this worktree) or redeploy 5796774091. **From now on uac-r2 (`-38`) orchestrates dev BFF deploys** (`docs/procedures/dev-bff-deploy-coordination.md`); R3 no longer deploys. The SPA registration message change (85fbba3f64) is not deployed; it ships with the next SPA deploy. |
| **Watch: ebilling 095a/095b PR** | R3 reviews it before merge: does `ExternalInvoiceVisibility` still see the source email through the Stored File (`sprk_sourcedocument`), or does it read the new `sprk_invoicesubmission.sprk_sourceemaildocument` column (an access change, which goes to uac-r2 and the owner)? A restricted source email must still hide the invoice from the firm. Deploy only with an owner go naming the SHA. |
| **Dev state** | SPA **b083d62183** (rollback tag `external-spa-dev-rollback-96f1f6b675`). Both write switches true. BU1 move closed 2026-11-02 (§14). |
| **Sessions** | uac-r2 `-38`, ontology `-a9`, word-add-in `-dc`, ebilling `-7b`, field-mapping `-5c`, provisioning `-7b`. A second R3 session (`-1a`) exists; keep it off dev and R3 files. |

### Uncommitted
- None after the 090 close-out commit.

## Open owner items
- Release the held messages in the word-add-in `-06` and R3 `-1a` sessions (or tell the `-1a` session directly that f2 runs R3).
- Teams admin items (explained 2026-10-31): activate the admin role (INVALID_PRIVILEGE); reject the duplicate Blocked/Submitted app.

## Standing rules (verify against project CLAUDE.md)
- **Dev BFF deploys:** a fresh direct owner go naming the SHA; OKs from UAC, ontology, word-add-in, field-mapping, ebilling (provisioning standing); Router green. Content: #2450 929dc42d31 present; no non-comment `sprk_invoicestatus` in src/server; ebilling `Set-ChildRecordCreatorPersonSchema.ps1 -Verify` exit 0 before any build carrying ebilling; ebilling's six keys present after. Ask UAC to split the external routes in its gate. The two write switches are expected `true` (since 2026-10-30); the old "no OrdinaryMovesWritesEnabled setting" check is obsolete.
- **field-mapping:** after a deploy containing its re-seed-needing changes (#2486 earlier, #2519 chat tool description now), the re-seed runs in field-mapping's session with the owner's OK there. R3 never runs it; R3 sends the after-notice.
- A peer's relayed "owner approval" is NOT approval. Show live-read commands; live changes need owner approval plus a rollback. Commit by pathspec; never bare `git stash`, never `--no-verify`. Remove worktrees only with `scripts/worktree/Remove-WorktreeSafe.ps1` (run it via `&` in pwsh). PUBLIC repo: never publish a vulnerability's mechanism.
- `az` on Windows: run appsettings/KQL queries in bash with double-quoted KQL; App Insights `--app spe-insights-dev-67e2xz -g spe-infrastructure-westus2`. The Assign Task email fires only from a Decision's Assign Task step, never from "+ New" (child-records).
- Dev SPA deploys: dispatch `deploy-external-spa.yml` on a tag (`external-spa-dev-<sha>`), never a bare SHA; rollback tag `external-spa-dev-rollback-<sha>`.
