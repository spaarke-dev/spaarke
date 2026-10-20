# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-20 15:40Z (078 review round 1 merged; scoped re-check running; 059 stages E+F done, dev SPA live with A9)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **078** — ISS-010 quick fix (task-execute, FULL, Opus; POML `tasks/078-read-query-internal-only-guard.poml`) |
| **Step** | 9.5. Implemented (`ef87f6edc`, notes `a33c17652`). 2 Opus adversarial passes done. Review round 1 fixed by implementer and cherry-picked: `7d23af15b` (code) + `e7ddca3ae` (notes). Build 0 errors on branch. **Running: scoped Sonnet re-check of `git diff 06b867a6b 7d23af15b`** (adversarial-reviewer). |
| **Status** | Dev BFF = `00aba6d99` (unchanged). **Dev SPA = `1908696a8`** (run 39442677693, 2026-10-20 14:48:58Z; A9 registration live; bundle check passed). Branch head `42061494e`, pushed. |
| **Next Action** | When the re-check reports: fix any F-class (implementer or main session), then code-review + adr-check (Step 9.5), POML status completed + TASK-INDEX ✅ + `pwsh scripts/check-task-status-drift.ps1`, remove `C:\wt078m` and `C:\wtr3b` with `scripts/worktree/Remove-WorktreeSafe.ps1` (and the stale non-git `C:\wt078b` after a look), tell UAC (ISS-010 is shared). |

### Task 078 state
- Guard: `Services/Ai/Handlers/Dataverse/ExternalCallerCommunicationGuard.cs`; handlers read_query / search_data / describe. External = systemuser `sprk_isexternal` or unresolved (no principal/oid/systemuser); **lookup fault ("lookup-failed") → tool error** (round 1).
- Round 1 added: `sprk_document` rows linked to a communication follow its rule (all 3 tools); refused content tables `sprk_emailartifact`, `sprk_attachmentartifact`, `sprk_emailreviewlog`, `sprk_affinity`; K1 in-query clause `sprk_CommunicationThread/sprk_privacystate eq 100000000` (nav name confirmed in `src/dataverse/solutions/SpaarkeMaster/Other/Relationships/sprk_CommunicationThread.xml`); uniform not-found in describe; per-row flag re-check. Seeding proofs S1–S19 in notes.
- **Deploy-time live check needed:** an external caller's `read_query` on `sprk_communication` (the K1 navigation clause has not run against live Dataverse).
- Publish-size: +4,812 B (branch+master `0f218f8ce` vs master `2b07480ab`, 192 files both; Compress-Archive). Round 1 adds no file/package/registration.
- Filed from 078: ISS-014 #2125 (grid_overview), ISS-015 #2126 (partner doc copies — **owner decision**), ISS-016 #2127 (RAG retrieval, chat archive ingest, sprk_analysis/analysisoutput). Write-tool gap recorded in ISS-010 (#1864 comment).

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and three untracked researcher files belong to the OWNER. Never commit or discard them. Commit by pathspec only.

### Critical Context
- **Deploy rule** (memory `shared-dev-bff-deploy-coordination`): re-read the live build, ancestor check, every reachable session's OK, fresh DIRECT owner approval.
- **Parallel pattern:** BFF work in isolated worktrees under `C:\code_files\spaarke\.claude\worktrees\agent-*`; main session merges or cherry-picks. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation → two top-tier passes; else one. Max 4 agents.
- **Test runs:** filtered runs, absolute csproj paths; rebuild before trusting results.

## Next actions (in order)
1. Finish 078 (above).
2. **UAC #2128** (owner chose option 1: retire the Spaarke-tenant guest auto-create). UAC builds it with a new 403 reasonCode on /me (recommended `sdap.access.deny.workforce_guest_use_home_signin` + actionable `detail`). **R3 follow-up:** add that code to `deniedDetailOf()` in `src/client/external-spa/src/components/auth/BffAccessState.tsx` (+ test, optional browser "sign in with company account" action) once UAC sends the final string; ships in the next owner-approved dev SPA deploy. UAC is counting existing guest-bound contacts (read-only).
3. UAC runs the 186 gate (Tracy home sign-in) on the new SPA; owner browser checks (CIAM, DC profile form, licensed link) hand over to 074.
4. 074 → 070/071/072 → final R3 PR (090).
5. Close DEF-004 (#1829) as superseded when ebilling 051 merges.

## Open owner questions
1. **ISS-015 #2126:** should partners see a document copy of an internal-only email once it is filed to the record? (a) hide (recommended) / (b) keep showing.
2. **059 finding C-1 (SECURITY):** stage C's combined consent granted the dev BFF app's own Graph APPLICATION permissions in Dewey Cheatham. Proposed: revoke every grant whose client is SDAP-BFF-SPE-API in DC (keep the SP and the Workforce SPA → user_impersonation grant). Record §11 stage C.
3. Observation: `spaarke-bff-api-prod` has tenant-wide consent (`SDAP.Access`) to the DEV BFF app 1e40baad — intended? (059 R3)
4. **Next dev BFF deploy** (needs approval + coordination round): R3 078, UAC #2110 (`2b07480ab`), ontology 042c, UAC #2122 if merged. Every candidate must contain 077 (`283466d40`). Include the 078 live check.

## Standing dependencies and gates
- **059 rollback order:** F (SPA: tag `ebc74b331` + run workflow on the tag) → E (`gh variable delete` ×2) → D → C → B → A. Never roll the BFF back below 077 while 1e40baad is on v2 (or within 90 min of reverting it).
- **ebilling 015 `sprk_invoicestatus` retirement:** once ebilling deletes the column, every dev BFF candidate must pass the CONTENT check (FinanceBaseValues/FinanceRollupService/InvoiceReviewService contain no `sprk_invoicestatus`; FinanceBaseValues contains `InvoiceStatusCodeColumn`). Then remove it from ReadableColumns and both test arrays. Keep ebilling's retirement-marker line in the ExternalAccessModule invoice block.
- **ebilling-r1 merge (later):** schema scripts (D-113 rejection reason, D-104 Pending Spend) must run on dev before deploying a BFF built from that master.
- **UAC:** after 186, every new `/api/v1/external` route declares a ceiling requirement, handler check or exemption (`WorkforceCeilingEndpointFilter.cs`). Tell UAC before any BFF change to the external write routes, ExternalAccessModule.cs or AccessibleRecordSetService.cs. UAC #2103 reason-code/status changes on the revoke routes come to R3 before merge (check `IssuedGrantsList.tsx`).
- **036 live gate:** `ExternalAccess__ImpersonatedRootSets__Enabled=true` on spaarke-bff-dev only after #2078 is merged and deployed AND the owner confirms DIRECTLY here. Show the command first. Rollback: delete the setting.
- **Session names change on restart:** re-run ListAgents. UAC: `spaarke-wt-unified-access-control-r2-ac` (pipe `…2ffff52008418f5786c63341b6bae1f0`).
- Older items (verify before relying): Copilot agent still on `access_as_user`; operator mailbox/Exchange RBAC (H14a); BFF app user `prvDelete` on `sprk_servicerequest`; ISS-001…ISS-005; DEF-001 #1736, DEF-002 #1737, DEF-003 #1828.
