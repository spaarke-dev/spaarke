# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-22 02:55Z. Dev BFF a7b956fb55 (deployed 02:17Z: #2223, fm-016, UAC 190 sweep disabled) on top of f1b3dadcf5 (D7 + slice D) + 036 flag ON; dev SPA 3531ed65e; R4 design.md drafted.
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | No task mid-implementation. Open, owner-driven: 070/071 (Teams), 074 (A9 probes), 002 (grid columns). R4 `design.md` drafted, waiting for owner review. |
| **Status** | **Dev BFF = `a7b956fb55`**, deployed 2026-10-22 02:17Z; owner-approved here. It adds #2223, fm-016 (dormant) and the UAC 190 sweep (disabled, Apply off) to f1b3dadcf5, which added D7 T7 (#2170, the ISS-015 fix with T8 #2177), slice D (#2186) and #2214 to 583f90c1d9. UAC then applied Restrict, field security and sprk_DocumentOperations.js 1.28.2 on dev. Record: `notes/task-078-notes.md`, last section. Flag `ExternalAccess__ImpersonatedRootSets__Enabled` has been **ON since 2026-10-20 23:51Z**; rollback is to delete the setting. BFF rollback is `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261022T021635Z`, which returns to f1b3dadcf5. **Dev SPA = `3531ed65e`**: run 39536100531, 2026-10-21 03:02Z, with the #2166 fix and the UAC #2078/#2122 SPA changes. Rollback tag: `external-spa-dev-rollback-28274e04e`. UAC's 036 flag-on gates passed (uac-r2 0a4fbaa38). |
| **Next Action** | Wait for the owner: (1) smoke of SPA 3531ed65e (list in `notes/task-070-deploy-record.md`, last section), then close #2166; (2) answers to R4 design §9; (3) Teams package upload in the **Dewey Cheatham** tenant (the Spaarke tenant gets "not set up" by design), then 071; (4) 002 columns. |

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and three untracked researcher files belong to the OWNER. Never commit or discard them. Commit by pathspec only. Everything else is committed and pushed (R3 `7db91a09c`, R4 `0239edf9a`).

### Critical Context
- **Deploy rule** (memory `shared-dev-bff-deploy-coordination`): read the live DLL via Kudu VFS, run the ancestor check, get every reachable session's OK and fresh DIRECT owner approval, snapshot via Kudu `api/zip/site/wwwroot/`. SPA deploy: `gh workflow run deploy-external-spa.yml --ref master -f environment=dev`, after pushing a rollback tag of the live build.
- **Review policy:** auth, security and tenant-isolation changes get two top-tier passes; anything else gets one.
- **Test runs:** filtered, with absolute csproj paths. Run SPA vitest files one at a time: a full parallel run flakes in App.signOut, App.teamsHost and DocumentUploadPage.externalUpload, which all pass when run alone.
- The Teams package is at scratchpad `teams-dev-39489551799\appPackage.zip` (run 39489551799, manifest 23610794…, v1.2.0). The SPA only serves tenants listed in VITE_BACKENDS homeTenantIds, which is Dewey Cheatham only.
- Leftovers for a human: `C:\code_files\spaarke\.claude\worktrees\agent-a4775c0edc08ac138` and `agent-aad40bdfd9712fba4` (Permission denied).

## Done this session (2026-10-21)
- **#2166 / ISS-018:** the Partner (CIAM) plane now counts only CIAM-tenant accounts (`isCiamAccount`, accountFilter). This fixed `authority_mismatch`. PR #2175 had two opus reviews; follow-ups were applied, including a deploy-workflow guard that VITE_MSAL_TENANT_ID equals the authority's tenant. Deployed. The issue is commented and stays open until the owner smoke.
- **074:** Ralph Dewey's licensed-user link works on dev; it took 2 popup tries. The first failure was an Entra page ("not in Spaarke tenant"), cause not captured. The owner can check the Entra sign-in logs for 2026-10-21 02:10–02:20Z. Filed as ISS-019 #2192 (R4).
- **ebilling firm invoice-file route:** owner decisions, plus the rule agreed with UAC and ebilling, are in `notes/decisions.md` (2026-10-21). ebilling owns the BFF change: it adds `/invoices/{id}/files`, retires the four invoice-document routes and changes submission storage. R4 owns the SPA side. Deploy BFF then SPA, back to back, through R3. The 051 wizard is on work/ebilling-r1 (#1911), so R4 doesn't plan its own.
- **R4 design.md drafted:** R4 branch `0239edf9a`. It is a template for six types, per-record actions, two new event routes (matters, WAs), a field/allow-list table checked against the schema (several prototype fields don't exist; task 157 dropped others), an icon map, and backlog items DEF-003 preview, #2155, #2192 and invoice files. §9 has 8 owner questions.
- ISS-015's status was updated to "D7 accepted; fix = UAC task 188 + word-add-in task 130".

## Open owner questions
- **R4 design §9:**
  1. Prototype decisions.
  2. The owner's prototype change list (not received).
  3. Fields, including whether internal people's names show to partners.
  4. Doc and SR on the template.
  5. Toolbar shared or local.
  6. Icon map.
  7. "Submit invoice" in the toolbar.
  8. Decide 002 together with the fields.
- **002:** the six tabs keep their current columns or change (notes/task-002-notes.md "Live capture 2026-10-20").
- **073:** routing (VITE_BACKENDS vs the T240c directory), DNS, prod environment + TEAMS_APP_ID, consent design.
- **C-1:** the owner applies the portal settings in Dewey Cheatham.

## Peer threads in flight
- **uac-r2 round 113** reverses #2179(c): a flagged-external systemuser must not reach internal-only records, in Dataverse or through SPA/BFF. UAC is designing the Dataverse-side enforcement. R3 replied:
  - The firm invoice route does NOT 403 systemusers; they are served the 036 way, as agreed.
  - It listed the SPA/BFF paths that carry systemusers: root sets, documents (task 188), messages, the notification feed, events and to-dos (no internal-only filter known; asked UAC to check), ISS-014 #2125 and ISS-016 #2127.
  - Awaiting UAC's answer.
- **UAC D7 T7 (task 188)** and word-add-in task 130 (T8) will need an R3-coordinated dev BFF deploy later.
- **ebilling:** will send its PR when ready (after D-148). Deploy it paired with the R4/R3 SPA change.
- **84291ecea8 follow-up:** UAC gate item 4: the owner does two .eml saves, or it relies on real saves. word-add-in reads the rows back. Ontology re-checks its logs at the 02:30Z nightly run. D7 is live but not exercised: no partner document reads yet. The optional owner partner check is in task-078-notes (f1b3dadcf5 section). ISS-015 can close once D7 is proven live. On master, not deployed: ontology #2233 (earlier-invoice detail hidden on read). Next deploys: UAC round-122 PR when merged; ebilling 015 (e0df77339) when ebilling asks.

## Next actions (in order)
1. Owner smoke of SPA 3531ed65e, then close #2166.
2. Owner: upload the Teams package in Dewey Cheatham → 070 smoke → 071.
3. 074 remaining: resubmit, existing-record bind, unlisted directory → 401, home token on /api/v1/membership → 401, intake as the new contact.
4. 002 decision → 072 (both-plane E2E) → 090. 073 stays blocked on the ACME gate.
5. After the owner answers R4 §9: update the R4 design.md, then a new R4 session runs `/design-to-spec`.

## Standing dependencies and gates
- **059 rollback order:** F (SPA) → E (`gh variable delete` ×2) → D → C → B → A. Never roll the BFF back below 077 while 1e40baad is on v2.
- **ebilling 015:** deleting `sprk_invoicestatus` is blocked until a live dev BFF contains e0df77339. Every candidate gets the content check.
- **ebilling-r1 merge (later):** schema scripts (D-104, D-113, D-121, D-124, D-125, D-127) must run on dev before a BFF built from that master.
- **UAC:** tell UAC before any BFF change to external write routes, ExternalAccessModule.cs or AccessibleRecordSetService.cs. #2103 reason-code changes come to R3 first.
- **Ontology:** 042c reaches master via their docs→master sync. Do NOT merge their docs branch into R3.
- **Session names change on restart:** re-run ListAgents.
