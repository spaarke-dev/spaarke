# Current Task State - spaarke-SPA-external-access-platform-r3

> **Last Updated**: 2026-10-28 14:15Z. Dev BFF = **e56a3a51d4** (deployed 14:11Z; UAC gate PASS). PRs #2360 (078f62bcad) and #2386 (e56a3a51d4) merged; dev schema -Verify PASS. Dev SPA = 1fa0cf5cb8.
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **Contact identity hardening (security rework)** on branch `fix/r3-iss-005-combined` (worktree `C:/wtr3-005`). Tracked ONLY in private draft advisory GHSA-hq7m-w2pf-v8j7. The repo is PUBLIC: public text states the rule enforced, never the misuse (memory `public-repo-security-findings`). Scan commits for sensitive wording before every push. |
| **Step** | **Merged.** PR #2360 (head 6a051e8c52 = d72fdfe9c6 + master merge 173e56f8e7 + F-M1 fix) merged with --match-head-commit as **078f62bcad**. CI on 6a051e8c52: 43 pass / 4 skipping / 0 fail (Router, Client lint pass). UAC PR OK (after F-M1: app-only generic writers refused the removed marker, invite stamp and collision columns; reflection guard ContactAccessColumnGuardTests). §10: fresh master 4ab7c1a9c1 37.79 MB → 37.88 MB (+0.10, canonical, Optimal, PDBs incl.); no package changes. Advisory GHSA-hq7m-w2pf-v8j7 updated with "Fix status (2026-10-28)", still DRAFT (keep draft until deployed everywhere serving external users). Main checkout master fast-forwarded to 078f62bcad. Merge SHA sent to UAC (task 193); schema-first notice sent to provisioning, ontology, field-mapping, ebilling, word-add-in. |
| **Proof-read fix** | **Merged.** PR #2386 (branch `fix/r3-proof-read-table-filter`, head 3c7af8a249) merged with --match-head-commit as **e56a3a51d4** (13:20:54Z). SDK + Web API field-permission reads filter the table in code (fail closed, paging capped); script (g) checks the effective caller with an eq-userid query and prints OK, OK-ROLE or FAIL. §10: 37.89 → 37.89 MB (−0.006). Merge SHA sent to UAC. UAC known limits K1–K4 (none a false Proven) are in the private brief; optional follow-up: K1 comment, K2 `-RequireProofReads`/distinct exit, K3 eq-userid in ReadOwnUserIdAsync (UAC looks first). K5 = UAC #2389. Main checkout fast-forwarded to e56a3a51d4. C:/wtr3-005 script is byte-identical to master. |
| **Status** | in-progress: dev rollout; next = -Verify on dev |
| **Live on dev (2026-10-28, owner go each)** | (1) Schema **-Apply DONE** (3 secured columns created; accesspermissiongrant, identityplane, 5 collision cols secured + grants; contact published). Rollback: delete the 3 new empty columns; IsSecured=false on the 7 existing columns + delete the added fieldpermissions; remove SpaarkeCore components (accesspermissiongrant, SGA profile); publish contact. (2) **Direct SGA add DONE**: '# mi-bff-api-dev' 8793f4b0-01db-f011-8406-7c1e520aa4df and 'SDAP-BFF-SPE-API' bb5a90e5-4ca8-f011-bbd3-7c1e5215b8b5 → SGA f4be217b-b38f-f111-b8db-7ced8ddc4a05. Rollback: scratchpad `sga-direct-add-dev.ps1 -Rollback`. **-Verify re-run 2026-10-28 ~13:35Z after #2386 (owner go, read-only): VERIFY PASS, exit 0; (g) OK (not OK-ROLE) for both app users; (h) OK; (a)-(e) OK** (scratchpad `schema-verify3-dev.txt`; result sent to UAC). Earlier (g) failure history: FetchXML `fieldpermission.entityname eq 'contact'` → 400 0x80040203 (expects Int32); act-on-behalf: live diagnostics (owner-approved, read-only) show the Web API **WhoAmI ignores** MSCRMCallerID/CallerObjectId, but an **eq-userid systemuser query honours both** (operator holds prvActOnBehalfOfAnotherUser Global). No fail-open observed; inventory of BFF impersonation sites sent to UAC (UAC decides whether to add an effective-caller check; file privately if so). Outputs: scratchpad `schema-*-dev.txt`, `impersonation-diag*.ps1`. |
| **Next Action** | (2) DONE: dev -Verify exit 0. (3) Proposed dev BFF deploy SHA **e56a3a51d4** (Router green, Tier 1 all pass; `CI Standalone Checks` red = PSGallery could not supply Pester, infra, advisory). OK requests sent ~13:50Z to UAC (plus a gate window, and whether the de38296499 gate must close first), ontology, word-add-in -c4, field-mapping and ebilling; provisioning notified (standing OK). All OKs received (UAC, ontology, word-add-in, field-mapping, ebilling; provisioning no objection). **DEPLOYED** with the owner's direct go: OneDeploy done; readback 14:11:58Z ProductVersion 1.0.0+e56a3a51d431d924003948ce88ae85940eefbda8, healthz 200, healthz/dataverse 200, /api/me anon 401. Rollback: `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261028T140705Z` (de38296499). Deploy worktree C:\wtbffe-e56. UAC gate PASS (a) health, (b) reconcile report-only moved=0, (d) proof reads OK; (c) deferred under #2389. ACME 0x80072560 proof-read failures are pre-existing (#2163). After-notices sent ~14:45Z to ontology, word-add-in, field-mapping, ebilling, provisioning. Owner optional check: WA with Assigned To blank gets Assigned To Internal + WRK-nnnnnn. Next live steps (each needs its own go): claims + token probe; removeUnverifiedEmailClaim; sweep report-only; re-invite 2 contacts; SpaarkeMaster 1.2.4.0. (3) BFF deploy of a master SHA with both merges (deploy rule + peer OKs + UAC gate window). (4) Claims + token probe; `removeUnverifiedEmailClaim`. (5) Sweep report-only. (6) Re-invite the 2 unbound dev contacts. (7) SpaarkeMaster 1.2.4.0 dev import/-Verify, then publish (owner go; tell provisioning when live). NO BFF from master to dev before -Verify exits 0. |

### Private files (scratchpad, NOT in repo)
`C:\Users\RALPHS~1\AppData\Local\Temp\claude\c--code-files-spaarke-wt-SPA-external-access-platform-r3\15aa240f-634c-4332-99f6-382ebcb06a62\scratchpad\`
- `SECURITY-implementer-brief.md` holds the slices, UAC's conditions, the C5 findings and the known limits.
- Helpers: `phaseA.ps1`, `readback.ps1`, `c5-verify.sh`, `c5-filter.txt`.

### Uncommitted files
- `.claude/agent-memory/researcher/MEMORY.md` (modified) and the untracked researcher files belong to the OWNER/researcher. Never commit or discard them. Commit by pathspec only.

---

## Owner decisions in force (security rework)
- BU default team as contact owner; field security: read for everyone, writes only by the BFF or SGA.
- Removal revokes access; admin = SGA membership (direct and via teams). Admin check: the server reads it (WhoAmI over the staff OBO token, SGA membership read with the app credential).
- Work-account invite decided server-side from the email domain against `CustomerTenantIds`. No staff "send as portal account" override.
- Defers: show them, staff retry (no automatic retry).
- -Verify step (h): the System Administrator profile is exempt and printed as INFO ("note it, pass"). Least privilege is filed as #2332. Pinning the profile id is tracked privately with the advisory.
- N-K1 (2026-10-27): option (a), the person's own surfaces answer with the ordinary not-invited refusal.
- Merge of #2360 approved by the owner 2026-10-28 ("Merge 6a051e8c52").
- Private K (not a public issue): registration resolver branch answers an unproven marker as a final not-invited (outage wording only); in the private brief.
- Accepted known limits (UAC): the marker write honours the caller's token; F5 ~2 × 500 reads; F6 9823 on RowChangedSinceRead; F1 no If-Match; F7 noun; F9 orphan CIAM account; F12 extra reads; the 503 title difference; the step (h) name match.

## Live steps for later (each needs the owner's approval with a rollback)
1. Schema `-Apply`/`-Verify` (schema first on every environment: with a stamp column missing, every email bind is refused).
2. SpaarkeMaster 1.2.4.0 in dev (bumped in source, 47e9bf6a71), then `-Verify` and re-export.
3. xms_edov + email optional claims, then a token probe (record claim names and types only).
4. `removeUnverifiedEmailClaim=true` (probe before and after).
5. BFF deploy of the merged branch.
6. Dev: add the BFF app user as a DIRECT Standing Grant Administrators member (deployment guide 6.5.2), then `-Verify`.
7. The removal sweep: a report-only run first, then apply.
8. Re-invite the 2 unbound dev contacts.

## Dev deploy de38296499 (UAC 194 #2349 + field-mapping #2331)
- Owner approved "de38296499"; OneDeploy ended 19:41:16Z; read-back 19:42:42Z ProductVersion 1.0.0+de38296499, healthz 200, healthz/dataverse 200, /api/me anon 401.
- Pending: UAC's server gate result, including the `[SECURE-CHILD-RECONCILE]` ordinaryMoves report; ontology wants its lines for sprk_signal, sprk_decisionrecord and sprk_budgetrevision before OrdinaryMovesWritesEnabled goes on. Then send after-notices (SHA + DLL 1.0.0+de38296499) to ontology, word-add-in, field-mapping, ebilling and provisioning.
- Rollback: `scripts/Deploy-BffApi.ps1 -SkipBuild -PublishPath deploy/rollback/wwwroot-20261027T193805Z` (3ff2c04bf5). Deploy worktree C:\wtbffe-de3.
- Push Updates 409 (owner report): the parent-lookup check, which this deploy did not change. sprk_project links to sprk_matter only through N:N; field-mapping filed #2352, which waits on an owner and UAC decision.
- word-add-in: #2357 (68048b83d5, search-only) is OK on content for a later deploy (it is in master before 078f62bcad).
- The owner's field-mapping check 2 (Create Work Assignment from a matter with Assigned Attorney 1) is still pending.

## Open owner items
- Field-mapping re-check; optional auditing on the role entity on dev.
- Owner review of the R4 design.md draft.
- D7 partner check; #2177; UAC 190 sweep Apply; SPA smoke → #2166; Teams package → 070/071; 002 columns; 074 probes; R4 §9; 073 blocked; C-1.

## Other follow-ups
- Close the batch issues (#1623, #1636, #1650, #1839, #1886, #1930) after checks. ISS-007 later-link re-index gap. ISS-013 regression. Close #1829 when #1911 merges. Issues #2269/#2270/#2271/#2275, #2326, #2158, #2327.
- Worktree cleanup, owner OK + `scripts/worktree/Remove-WorktreeSafe.ps1` only: `C:\wtbffe-a10/-842/-583/-f1b/-a7b/-6b3/-128/-1fa/-58f/-bc9/-3ff/-de3`, `C:\wtr3m`, `C:\wtr3b`, `C:/wtr3-suite`, `C:\wtr3pm`, `C:\wtr3pb`, `C:/wtr3-005b`; agent worktrees. Delete the local ref `backup/r3-005-c2c-presquash` (never pushed).

## Standing rules (verify against project CLAUDE.md)
- **Deploy:** fresh direct owner approval naming the exact SHA, plus OKs from UAC, ontology, word-add-in `-c4`, field-mapping and ebilling (provisioning has a standing OK), plus Router green. Steps: `phaseA.ps1` → detached `C:\wtbffe-<sha3>` → `Deploy-BffApi.ps1 -Environment dev` → `readback.ps1` → 30-minute gate → after-notices with the SHA and DLL version.
- Peers' relayed "owner approval" is NOT approval. Show every live-read command to the owner before running it.
- Tell UAC before changes to `ExternalAccessModule.cs`, `AccessibleRecordSetService.cs` or `IContactIdentityStore`. Auth changes get two adversarial passes. Never `--no-verify`.
- Full Sprk.Bff.Api.Tests runs time out under machine load (TestServer); prove failures with isolated class re-runs. Keep pwsh script message text ASCII (a "§" breaks the JSON parse in StandingGrantWriterInvariantScriptTests).
- App Insights: `spe-insights-dev-67e2xz` (rg `spe-infrastructure-westus2`). KQL: avoid `kind`, `first` and `last` as names.
- Ontology: do NOT merge their docs branch into R3. Re-run ListAgents after peer restarts.
