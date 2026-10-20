# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-20 17:20Z (interim merge PR #2141 MERGED 28274e04e; R4 worktree created; dev BFF live 21184ecd4)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | No POML task in progress. Interim merge done (owner request 2026-10-20): PR #2141 merged 17:12:51Z as `28274e04e` (43 checks pass, 3 skipped); main checkout fast-forwarded; comment on #2099. |
| **Step** | Step 2.5 done: branch merged with origin/master (`3ebb747d8`, pushed; build 0/0; master's 2 new commits were provisioning PS only). **Next: Step 3 Path A** — `gh pr create --base master --head work/spaarke-SPA-external-access-platform-r3` (body: R3 since #2086 = 058/075/076/077/078, 059 records, SPA #2128 detail, ISS-014..016 filings; verification: build 0/0, ArchTests 1152/1152, unit filter 6758/6758, deployed to dev as 21184ecd4), then `gh pr merge <N> --auto --merge`, Monitor until MERGED, gate on full `gh pr checks` (zero fail/pending; mergeStateStatus CLEAN), then A.6 (main checkout ff-only with HEAD guard; never reset/stash/checkout there). |
| **Status** | **Dev BFF = `c3ec5d6e5`** (master incl. UAC #2138/#2133; deployed 18:12Z, owner "approved bff deploy"; read-back ProductVersion 1.0.0+c3ec5d6e5…; healthz/dataverse 200; rollback `Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261020T180912Z` = 21184ecd4; record in notes/task-078-notes.md). **Dev SPA = `28274e04e`** (master, run 39468172374, 17:40Z; bundle has `workforce_guest_use_home_signin`; UAC told; rollback tag `external-spa-dev-rollback-1908696a8`). Branch head `3ebb747d8`. |
| **Next Action** | **R4 worktree CREATED**: `C:\code_files\spaarke-wt-SPA-external-access-platform-r4`, branch `work/spaarke-SPA-external-access-platform-r4` (pushed) at master `28274e04e`; no project folder yet. Waiting for the owner's requirements + prototype → `/design-to-spec` then `/project-pipeline` there. R3 is 1 merge commit behind master (sync before new R3 work). (Original plan:) create **R4 project** `spaarke-SPA-external-access-platform-r4` (own worktree `C:\code_files\spaarke-wt-SPA-external-access-platform-r4`, branch `work/spaarke-SPA-external-access-platform-r4` from master AFTER the R3 PR merges) — owner will provide idea/requirements + prototype reference; then `/design-to-spec` → `/project-pipeline` in that worktree. |

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and three untracked researcher files belong to the OWNER. Never commit or discard them. Commit by pathspec only.

### Critical Context
- **Deploy rule** (memory `shared-dev-bff-deploy-coordination`): read live DLL via Kudu VFS, ancestor check, every reachable session's OK, fresh DIRECT owner approval; snapshot via Kudu `api/zip/site/wwwroot/`.
- **Parallel pattern:** BFF work in isolated worktrees; main session merges/cherry-picks. Agents never edit TASK-INDEX, this file, POML status or `.claude/`.
- **Review policy:** auth/security/tenant-isolation → two top-tier passes; else one.
- **Test runs:** filtered, absolute csproj paths; full unit filter has an intermittent failure that passes on re-run; SPA vitest files time out when run together under load — run them one at a time.
- Leftovers for a human: deregistered empty folders `C:\code_files\spaarke\.claude\worktrees\agent-a4775c0edc08ac138`, `agent-aad40bdfd9712fba4` (Permission denied); `C:\wt078b` is an older non-git folder (not mine).

## Owner decisions 2026-10-20 (this session)
- **R4:** UI/UX changes go to a new project + worktree `SPA-external-access-platform-r4`; owner supplies requirements + prototype.
- **ISS-015 #2126:** hide, broadened: if an email is internal-only, every document saved from it (.eml + attachments) is set internal; same for Secure records. `sprk_document` has NO internal-only column today → design point (new column set by writers vs derive from related communication). NOTE2 answered on #2126 from code: add-in docs inherit the record's owner team + container (Secure follows the secure root); no doc-level internal flag; non-secure matter docs visible to all partners on the matter. **Not yet scheduled as a task** (R3 or R4: propose).
- **C-1:** revoke approved, low priority (dev only). Needs the owner's `az login --tenant bc3aa7f4…` or the portal: delete every oauth2 grant + app-role assignment whose client is SDAP-BFF-SPE-API in DC; keep the SP and the Workforce SPA → user_impersonation grant. Record §11 stage C.
- **079 dropped:** the Assistant is internal; licensed users' writes are Dataverse-security's job (#1864 comment).
- **BFF deploy:** done (above).

## Next actions (in order)
1. Finish merge-to-master (Quick Recovery).
2. Create the R4 worktree/project after the PR merges; wait for the owner's requirements + prototype.
3. Propose where ISS-015's document internal flag lands (R3 task vs R4) and its design options.
4. C-1 revoke when the owner is ready (give exact commands).
5. UAC: #2138/#2133 live on dev (c3ec5d6e5); UAC runs the #2128/#2130 gates. Tracy Cheatham (DC) registration fails `registration_email_missing`: her DC-tenant user likely has no `mail`; owner checking (her Spaarke guest account is irrelevant to that path). UAC runs the #2110 gate and 186 on the new builds.
6. 074 → 070/071/072 → 090.

## Open owner questions
- ISS-015 implementation placement (R3 vs R4) and design (column vs derived).
- Observation: `spaarke-bff-api-prod` has tenant-wide consent (`SDAP.Access`) to the DEV BFF app 1e40baad — intended? (059 R3)

## Standing dependencies and gates
- **059 rollback order:** F (SPA: tag `ebc74b331` + run workflow on the tag) → E (`gh variable delete` ×2) → D → C → B → A. Never roll the BFF back below 077 while 1e40baad is on v2.
- **ebilling 015:** once ebilling deletes `sprk_invoicestatus`, every dev BFF candidate must pass the CONTENT check (FinanceBaseValues/FinanceRollupService/InvoiceReviewService contain no `sprk_invoicestatus`; FinanceBaseValues contains `InvoiceStatusCodeColumn`); delete blocked until a live BFF contains e0df77339.
- **ebilling-r1 merge (later):** schema scripts (D-104, D-113, D-121, D-124, D-125, D-127) must run on dev before deploying a BFF built from that master.
- **UAC:** tell UAC before any BFF change to external write routes, ExternalAccessModule.cs or AccessibleRecordSetService.cs. #2103 reason-code changes come to R3 first.
- **036 live gate:** `ExternalAccess__ImpersonatedRootSets__Enabled=true` only after #2078 merged+deployed AND direct owner confirmation; show the command first.
- **Ontology:** 042c reaches master via their docs→master sync; do NOT merge their docs branch into R3.
- **Session names change on restart:** re-run ListAgents.
