# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-20 19:30Z (owner's 8-item direction; 070 under way; index reconciled)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 070 (dev SPA + Teams package), owner "070-071-072 yes proceed" 2026-10-20. Also open: 074 (A9 live checks, partial), 002 (grid columns, owner question). |
| **Step** | 070: SPA live (master `28274e04e`); dev Teams package built (run `39489551799`, artifact `teams-app-package`, manifest id `23610794…` v1.2.0). **Next:** owner uploads the package in Teams admin center, then the smoke table in `notes/task-070-deploy-record.md`; then 071. |
| **Status** | **Dev BFF = `0b976628e`** (master incl. UAC #2122; 20:37Z, owner "yes deploy"; rollback `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261020T203441Z` = c3ec5d6e5; record `notes/task-078-notes.md`). **Dev SPA = `28274e04e`** (run 39468172374; rollback tag `external-spa-dev-rollback-1908696a8`). |
| **Next Action** | Reply to UAC on #2126 D7 with the code-mapper facts (3a–c: writers of `sprk_relatedcommunication`, the internal-only flag, gaps). Then the owner's 070 package upload and the 074 checks. |

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and three untracked researcher files belong to the OWNER. Never commit or discard them. Commit by pathspec only.

### Critical Context
- **Deploy rule** (memory `shared-dev-bff-deploy-coordination`): read live DLL via Kudu VFS, ancestor check, every reachable session's OK, fresh DIRECT owner approval; snapshot via Kudu `api/zip/site/wwwroot/`.
- **Review policy:** auth/security/tenant-isolation → two top-tier passes; else one.
- **Test runs:** filtered, absolute csproj paths; SPA vitest files one at a time.
- Leftovers for a human: `C:\code_files\spaarke\.claude\worktrees\agent-a4775c0edc08ac138`, `agent-aad40bdfd9712fba4` (Permission denied).

## Owner decisions 2026-10-20
- **R4 (UI/UX):** own project + worktree `C:\code_files\spaarke-wt-SPA-external-access-platform-r4` (branch pushed, at master 28274e04e, no project folder yet). Prototype: `c:\code_files\spaarke-prototype\projects\2026-10-spa-record-modal\` (README → src/template/types.ts → src/recordConfigs.ts → src/template/RecordModal.tsx; untracked in the prototype repo; read-only). Prototype session's open questions: title = type + number; hide not disable; tabs off by default; "Your access" in header. Findings: doc-table overflow fix; child dialogs inside SprkModal; most new info fields are NOT in the external column allow-list; Add task/event need external create routes. Backlog: #2155 (email-missing screen text). Next: `/design-to-spec` in the R4 worktree (new session recommended).
- **ISS-015 #2126:** owner wants it handled with UAC's TrackingFieldTrio / Manage Access on Documents (#2119). UAC proposes **D7**: a child may only NARROW partner visibility; document hidden if own Access Permission = Restricted OR derived from an internal-only communication (read-time via `_sprk_relatedcommunication_value` + the 065 rule, fail closed); one shared predicate in UAC's external read path (list/download/versions/preview/SPE/AI tools); wiring after #2078; needs owner acceptance (amends I-14). No R3 column. UAC asked R3 for facts 3(a)–(c) (code-mapper running).
- **C-1:** owner asked for the target settings (portal). Target: SDAP-BFF-SPE-API enterprise app in Dewey Cheatham keeps its service principal with ZERO permissions; Spaarke Workforce SPA keeps exactly one grant (SDAP-BFF-SPE-API `user_impersonation`, delegated, admin).
- **`spaarke-bff-api-prod` consent question:** dropped (owner: that app is not being kept).
- **074:** partner access still works (owner). Member registration works (Tracy Cheatham). Owner asked what the licensed-user link is (design §5, `notes/a9-bff-design.md`).
- **073:** coordinate with provisioning for the ACME test: gate table in `notes/task-073-gate-acme.md` (from provisioning's facts).
- 079 dropped; BFF + SPA deploys done.

## Next actions (in order)
1. Owner: upload the dev Teams package; smoke (070) → 071 Teams live check.
2. 074 remaining: licensed-user link (Ralph Dewey), resubmit, existing-record bind, unlisted directory → 401, home token on /api/v1/membership → 401, intake as the new contact.
3. 002: owner decides whether the six SPA tabs keep their current `sprk_gridconfiguration` columns.
4. Send UAC the D7 facts; track D7's owner decision.
5. 072 after 002 + 071; 073 blocked on the ACME gate; 090 last.

## Open owner questions
- 002 column lists (or "keep current").
- D7 acceptance (via UAC).
- 073: routing choice (build-time VITE_BACKENDS vs T240c directory endpoint), DNS, prod GitHub environment + TEAMS_APP_ID, customer-tenant consent design (no v1 /adminconsent, C-1).

## Standing dependencies and gates
- **059 rollback order:** F (SPA) → E (`gh variable delete` ×2) → D → C → B → A. Never roll the BFF back below 077 while 1e40baad is on v2.
- **ebilling 015:** delete of `sprk_invoicestatus` blocked until a live dev BFF contains e0df77339 (c3ec5d6e5 does not); every candidate gets the content check.
- **ebilling-r1 merge (later):** schema scripts (D-104, D-113, D-121, D-124, D-125, D-127) must run on dev before a BFF built from that master.
- **UAC:** tell UAC before any BFF change to external write routes, ExternalAccessModule.cs or AccessibleRecordSetService.cs. #2103 reason-code changes come to R3 first.
- **036 live gate:** `ExternalAccess__ImpersonatedRootSets__Enabled=true` only after #2078 merged+deployed AND direct owner confirmation.
- **Ontology:** 042c reaches master via their docs→master sync; do NOT merge their docs branch into R3.
- **Session names change on restart:** re-run ListAgents.
