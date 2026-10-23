# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-22 ~22:00Z (85% checkpoint). Issue-fix batch in progress on the R3 branch (tip 769989e349, pushed). Dev BFF still 1283c41817. Dev SPA 3531ed65e.
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **R3 issue-fix batch** (owner approved the triage on 2026-10-22; `notes/issue-triage-2026-10-22.md`). Merged into the R3 branch and pushed: ISS-001 (#1623; 2 adversarial passes + UAC OK), ISS-004 (#1650; review + re-check), ISS-007 (#1839, cherry-pick 663fb2e8fb), ISS-011 (#1886; review + UAC OK + doc nits), ISS-012 (#1930; rework 1b36a72281, the real fix was UAC 446c83d927). In flight: **ISS-003** (#1636) on branch `fix/r3-iss-003-email-sanitizer-review` at d400a108f9, in worktree `C:/code_files/spaarke/.claude/worktrees/agent-a859f9fa2df63f3fe` (it has node_modules). Two passes done. The second pass left 3 small items NOT YET APPLIED: (1) INDIRECT_CSS ~:334 becomes /(?:var|env|attr)\s*\(/ with a test; (2) with allowRemoteImages, keep src only if /^https?:\/\// (hardenNode ~:434) with a test; (3) add flexShrink:0 to RecordMessages.tsx bodyHtml ~:202. Then run jest sanitizeEmailHtml.test.ts in that worktree, commit, and merge d400a108f9 plus the fix into R3. **ISS-005** (#1673): F1 own-env commit 5d46895f1f on `fix/r3-iss-005-own-env-binder`; owner-policy agent RUNNING on `fix/r3-iss-005-owner-policy` (ContactCreateOwner, K1 dry run, K3 reflection test, docs); SPA message done at 86e23886d3 on `fix/r3-iss-005-spa-owner-unconfigured`. Next: combine, send UAC the diff, run the 2nd adversarial pass (the approach changed). Do NOT merge before the owner confirms "top-level BU". **ISS-002** (#1633): waiting on the full-suite run (bash task, detached worktree C:/wtr3-suite, trx in scratchpad/suite). An agent full run showed 189 TaskCanceled/Timeout failures under machine load (list in agent-a923f5a9542380563/TestResults/iss012-failed-names.txt); triage before blaming code. |
| **Status** | **Dev BFF = `1283c41817`**, deployed 2026-10-22 12:49–12:50:18Z; owner: "yes, deploy 1283c41817". All six peers said OK. It adds to a7b956fb55: UAC #2234 (round 122 provisional own-Restricted release), #2239 (AI profile writer limited to 6 ACT-011 columns), field-mapping 018a (dormant), #2244 and #2249 (app-only refusal covers BFF-owned document, owner and creator columns) and #2248 (archive verifies attachment item). **BFF rollback:** `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261022T124709Z` (= a7b956fb55). Flag `ExternalAccess__ImpersonatedRootSets__Enabled` has been ON since 2026-10-20 23:51Z. **Dev SPA = `3531ed65e`** (rollback tag `external-spa-dev-rollback-28274e04e`). Deploy records: `notes/task-078-notes.md` (one section per deploy). |
| **Next Action** | Finish the ISS-003 3 small fixes (above), then merge. When the owner-policy agent reports: combine the ISS-005 branches, send UAC the diff, run the 2nd pass. Read the suite results, then ISS-002. Then publish-size check + PR to master + a dev deploy request by SHA, naming: the UAC #1830 container steps (`notes/next-deploy-uac-1830-container-steps.md`), field-mapping 018b (live Office quick-create change; send field-mapping the SHA afterwards), and ISS-007 re-index (read-only count query + per-user send-to-index; owner approval). |

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and three untracked researcher files belong to the OWNER. Never commit or discard them. Commit by pathspec only.

### Critical Context
- **Deploy rule** (memory `shared-dev-bff-deploy-coordination`):
  - Fresh DIRECT owner approval naming the exact SHA; relayed approval doesn't count.
  - The peers (UAC, field-mapping, word-add-in -c4, ontology, ebilling, SPE admin) each say OK.
  - Then `phaseA.ps1` (live DLL read, Kudu zip snapshot), a detached worktree `C:\wtbffe-<sha3>`, `Deploy-BffApi.ps1 -Environment dev`, and `readback.ps1`.
  - Then the 30-minute reading with `exc<N>.ps1` and `recon2.ps1`.
  - Scratchpad scripts are in this session's scratchpad (`phaseA.ps1`, `readback.ps1`, `recon2.ps1`, `exc6.ps1`). App Insights is `spe-insights-dev-67e2xz` in rg `spe-infrastructure-westus2`. The admin token resource is `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`.
  - KQL via az on Windows rejects some compound queries (nested iff, mixed-type coalesce): keep queries simple, one count each.
  - ebilling's OK carries over automatically when the candidate moves only with UAC/R3 changes that don't touch finance or invoice data.
- **Review policy:** auth, security and tenant-isolation changes get two top-tier passes; anything else gets one.
- **Test runs:** filtered, with absolute csproj paths. Run SPA vitest files one at a time.
- **Teams package:** scratchpad `teams-dev-39489551799\appPackage.zip`. The SPA serves only the Dewey Cheatham home tenant.
- **Temporary build worktrees to remove** (`scripts/worktree/Remove-WorktreeSafe.ps1` only, with the owner's OK): `C:\wtbffe-a10`, `-842`, `-583`, `-f1b`, `-a7b`, `-6b3`, `-128`.

## Deploys 2026-10-21/22 (all owner-approved here; records in task-078-notes)
`a10e814d02` (UAC #2169, #2150) → `84291ecea8` (#2161, #2177, #2205) → `583f90c1d9` (#2191 leak fix, #2182) → `f1b3dadcf5` (D7 T7 #2170, slice D #2186; UAC then applied Restrict, field security, DocumentOperations.js 1.28.2) → `a7b956fb55` (#2223, fm-016, UAC 190 sweep disabled) → `1283c41817`. Every 30-minute gate so far has been clean; the only exceptions are the Graph "Invalid hostname" noise (ISS-006).

## Open owner items
- **D7 not exercised live:** there have been no partner document reads. Optional owner check: sign in as contact 8e9918a9 or 2e419a4f. Matter b68299c6 should list empty, matter 86335ce3 should list its 3 documents, and a download of aadbb740 should be refused (not-under-parent). ISS-015 closes once D7 is seen working.
- **#2177 .eml saves** (UAC is asking the owner): one identified, one unidentifiable; word-add-in reads the rows back.
- **#2191** Office quick-create live smoke (field-mapping).
- **UAC 190 sweep Apply** (29 links pending): owner decision.
- **SPA 3531ed65e smoke**, then close #2166.
- **Teams package upload** in Dewey Cheatham → 070 → 071.
- **002:** column and label list per tab.
- **074 remaining probes.**
- **R4 design §9:** 8 questions.
- **073** is blocked (ACME gate).
- **C-1:** portal settings in Dewey Cheatham.
- **Issue triage approved 2026-10-22** (`notes/issue-triage-2026-10-22.md`). R3 fixes ISS-001, 002, 003, 004, 005, 007, 011 and 012.
  - **Running** in isolated-worktree implementer agents, each on a local branch: `fix/r3-iss-012-*`, `fix/r3-iss-011-hasfile`, `fix/r3-iss-004-*`, `fix/r3-iss-001-*`.
  - **Queued** (4-agent cap): ISS-005 (brief agreed with UAC), then ISS-003, ISS-007 and ISS-002.
  - **Review:** auth/security/tenant-isolation (001, 005, 003) gets two adversarial passes; UAC reviews the 001 and 005 diffs before merge.
  - **ISS-005 must not merge until the owner confirms** that "top-level BU" = `ExternalAccess:CustomerBusinessUnitId` (the customer unit under root), not root.
  - **ISS-005 behaviour change:** first sign-in refuses when the setting is unset. Provisioning writes the setting in faf36207d, which is on their branch only; #2265 and task 265 do the read-back.
  - **Board:** #1830 → UAC task 192; #1864 and #2125 → UAC T191g; #2127 closed (fixed by #2170); #1829 → ebilling, closes when #1911 merges; #2001 → provisioning 264. ISS-017, ISS-019 and DEF-003 are in R4 design.md lines 30-32.
  - **Owner question open:** the issue-owner-label convention (`.claude/skills` edit needs approval).
- **PR #2264** merged 2026-10-22 16:42Z.
- **OWNER QUESTIONS OPEN (asked in chat):**
  1. ISS-005: "top-level BU" = ExternalAccess:CustomerBusinessUnitId (the customer unit under root)?
  2. Issue-owner label convention (`.claude/skills` edit, needs approval).
  3. ADR-028 snippet at lines 833-841 is stale after ISS-001 (`.claude/adr` edit, needs approval).
  4. ISS-004: should staff delete refuse rows with an unverifiable pointer (unclaimed container, unbound or mismatched item)? Merged as "refuse".
  5. Should the owner setting refuse a ROOT BU (UAC K5)?
  6. #2270, foreign-environment contact owner: (a) registry row BU, or (b) the systemuser BU (I lean b).
- **UPDATE:** ISS-003 is MERGED into R3 as 70d9ebc689 (2 security passes; the 2nd-pass items were applied in 02d00604cf). The in-flight ISS-003 note in Quick Recovery is superseded.
- **Filed today (R3 label):** #2269 (binder faults 403→503), #2270 (foreign-environment ownerless contacts), #2271 (lock 409 not ProblemDetails), #2275 (SPA test cold-start flake).
- **Peers:** provisioning finished ISS-013 (#2001, task 264) and #2265 (faf36207d + 527162e5d) on their branch only. R3 owes an ISS-013 regression check once their branch merges. ACME needs a one-off PATCH, waiting for the owner via provisioning.
- **Stale agent worktrees** under `C:/code_files/spaarke/.claude/worktrees/agent-*` (and C:/wtr3-suite) can be cleaned up after the batch, via Remove-WorktreeSafe.ps1 with the owner's OK.

## Peer threads in flight
- **Ontology:** #2233 (157, earlier-invoice detail hidden) waits on #2252 (ArchTests CI timeout 5→10 min). It will be in the next candidate.
- **ebilling:** 015 (e0df77339) only with the #1911 merge, not close. The `sprk_invoicestatus` delete stays blocked until a live BFF contains it. The 088 dev schema run (sprk_storedfile, sprk_restrictedbysubmission, statuscode Superseded) comes soon; master's BFF ignores it.
- **UAC round 113:** Dataverse-side enforcement for flagged-external systemusers. R3 listed the paths it carries; still waiting on UAC.
- **Field-mapping 018b** (ef8a91a157, PR #2261) is on master and rides the next candidate. It is a LIVE change to Office quick-create of Matter and Project:
  - Creates through RecordCreationService as the caller.
  - Owner is record-first: a Project gets its matter's BU team, or Secure Record Owners when the matter is secure.
  - Field mapping is fill-only.
  - A named assignee needs AppendTo, otherwise 403 OFFICE_009.
  - The defaults endpoint no longer offers an assignee the caller can't AppendTo.
  - After the deploy, R3 sends field-mapping the SHA. Field-mapping asks the owner to make the Office creates (a Matter, plus a Project under an ordinary matter and one under a secure matter), then checks the results read-only. R3 does not run this smoke.
- **UAC #1830 config for the next dev deploy:** remove `b!MVasATu…` (the dead container of secure project 65a3fab2) from `SharePointEmbedded__OwnedContainerIds`, keep `b!vzGD…`, then re-provision 65a3fab2's container.
  - Batch it with #2253, #2258 and #2268 (one restart). UAC sends the exact steps.
  - UAC says the owner approved it in UAC's session. That does NOT count here, so name it explicitly in R3's owner approval request, with the rollback (re-add the id).
- **Round 122:** shipped in 1283c41817. Known limit: a single-attachment save stays Restricted.

## Standing dependencies and gates
- **059 rollback order:** F (SPA) → E (`gh variable delete` ×2) → D → C → B → A. Never roll the BFF back below 077 while 1e40baad is on v2.
- **ebilling-r1 merge (later):** schema scripts (D-104, D-113, D-121, D-124, D-125, D-127) must run on dev before a BFF built from that master.
- **UAC:** tell UAC before any BFF change to external write routes, ExternalAccessModule.cs or AccessibleRecordSetService.cs.
- **Ontology:** do NOT merge their docs branch into R3.
- **Session names change on restart:** re-run ListAgents (word-add-in is now `-c4`).
