# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2 "Binding rules", §3 "Owner directives", §6 "Gotchas" (one dated line each). Decisions + superseded rules → `notes/decisions.md`. Session narrative → checkpoint commit messages. History: git + `notes/handoff-history/` (do not load on recovery). Review limits: task-execute Step 9.5.

> **Last Updated**: 2026-10-08 SESSION 44 — owner OKs: leftover tenant resource deleted; control-plane swap reset + Bicep slot fix (`21b96bf1e`) + both sites deployed and healthy; #1401 → T218f; **218e approved — in progress**, then T235.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **T218** — the complete Dataverse package. Plan `notes/t218-plan.md`; POMLs `tasks/218a…218e`, `257`. 218a ✅ `8b878d655`; 218b ✅ `3f41aab33`/`4f6c579ed`/`d3aa70726`; 218c ✅ `48b16bcb8`/`3eb20566e`/`3d280df7d`. |
| **Step** | **218e in progress** (owner OK 2026-10-08 for the SpaarkeMaster changes in spaarkedev1). |
| **Status** | in-progress. |
| **Next Action** | On the owner's OK: `task-execute` 218e — (1) `Test-SolutionCompleteness.ps1 -FailOnDrift:$false -SkipInventory` (read-only) → classify the 53 missing (ship / exclude with reason; the 4 April PCFs by form usage); (2) remove from SpaarkeMaster what is OUTSIDE THE RULE (5 Microsoft tables, 8 env-var values) + the Provisioning Registry role; (3) `Assemble-SpaarkeMasterSolution.ps1 -WhatIf`, then without (adds + re-adds the 8 shells, bumps the version); (4) `Export-SpaarkeMasterSource.ps1` (pac auth with access to spaarkedev1) → commit src/dataverse/solutions/SpaarkeMaster; then 218d (CI pack + publish). Without the OK: continue with T235. |
| **Branch** | `work/customer-provisioning-orchestration-r1`, pushed, clean. **9+ behind master**: merge master before T186, before any BFF deploy from this branch, and before PR #1365 merges; after the merge grep the BFF for `.ForApp(` and run `SpeAppOnlyContainerGuardTests`. |
| **Order** | 218e → T235 (owner: continue with it) → 218d → 218f → T250 → 213.7/207/208/209 → **T253** (G38) → **T255** (INCOMING-141; also H3's `acct` claim) → **T256** (waits on #1364) → T240b (owner's live re-test with package 1.1.2) → **T240c (approved 2026-10-08)** → T240d → T257 → T186. word-add-in-r1's first production deploy to addins.spaarke.com waits for T240c (their task 114). |

### T218 facts for 218e/218d (details in 218c's POML notes)
- Live SpaarkeMaster in dev (2026-10-08, read-only): 520 in scope, 18 excluded, **53 missing**, **8 sprk_ tables packaged as shells** (incl. sprk_signal, sprk_noaccessentry, sprk_policy), Provisioning Registry role packaged though excluded, **13 outside the rule** (5 Microsoft tables dragged in by the 2026-08-23 rebuild, 8 env-var values).
- Roles: "Spaarke Basic User" (H11 default) EXISTS and is in SpaarkeMaster; "Secure Record Owner" is a root role in the child unit "Secure Record". (Two earlier wrong notes came from 20-row-capped MCP queries — pass `top` or use the scripts.)
- The store `sprkcpartifactsdev/provisioning-artifacts` holds the hand-made old-format manifest → a real run fails at H6 (missing-solution-zips) until 218d publishes. Manifest format: `{"solutions":{"SpaarkeMaster":{"version","managedBlobName","unmanagedBlobName"}}}`.

### T240b (unblocked 2026-10-08)
The AADSTS700016 consent failure is fixed on the add-in side: word-add-in-r1 task 115 (master `849bac800`, owner decision 2026-10-08) ships package **1.1.2 without `webApplicationInfo`**; both add-in apps (dev `c1258e2d…`, production `1958aec2…`) stay single-tenant. Next (owner, live): upload 1.1.2 in the test tenant's Integrated apps, then the guest runs the Diagnostics view in Outlook/Word (expect `acct` 1 = guest) and a BFF call. Customer BFFs pre-authorize `1958aec2` (Worker Bicep, `4d41cb026`) — still needed for NAA without a consent prompt.

### Test organization
Tenant **Dewey Cheatham & Howe PC** (`deweycheatham.onmicrosoft.com`, `bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b`); guest `ralph@deweycheatham.onmicrosoft.com` (object `bc596ecd-b61c-43f7-8664-0f27a2267a67`) Accepted in Spaarke's tenant and in dev Dataverse. Business Basic only (web Office). CLI: private `AZURE_CONFIG_DIR` + device code (CLAUDE.md §6). Add-in package: `C:\Users\RalphSchroeder\Downloads\spaarke-addin-package\spaarke-addin-1.1.1.zip`.

## Owner items

1. **218d**: first CI publish to `sprkcpartifactsdev` (ask when reached).
2. **T240b live re-test** with add-in package 1.1.2 (owner, in the test tenant).
3. **G36** (ADR-027 management group) and **G31** (H10 tenant-wide Directory/User write roles) — awaiting decisions; do NOT act.
4. Board Status "Active" vs Status Reason "On hold" on Issue #438.
5. **`sprk_solutionversion` format** changed to `SpaarkeMaster {version} ({type})` (supersedes owner D17's fingerprint; matrix doc v3) — inform, no action.

Done 2026-10-08 (owner OK): `spaarketestpartner.onmicrosoft.com` Azure link resource deleted (REST api 2025-08-01-preview; az's default version failed with AADB2C90063 because the tenant was already gone); control-plane API swap reset, Bicep `s44`/`s44b` Succeeded, Deploy-ControlPlane Both from `35c20287c` — Worker + Api healthy, Worker has `EntraAppRegOptions__SpaarkeTenantId` + `PreAuthorizedClientAppIds__0=1958aec2`.

## Cross-project deliveries (track until delivered)

- **UAC-r2**: relay message handed to the owner 2026-10-07 (INCOMING-141/145 ack, #1364 `sprk_noaccessentry`, #1363 demo-grant marker, §13.9(b) do-not-mint, task-171 merge notes). Open: their answer on #1364 (T256/T218/T186 wait) and the #1363 fix.
- **word-add-in-r1**: all four messages delivered and acted on (tasks 113 diagnostics, 114 production identity, 115 package 1.1.2). Open: their first production deploy to `addins.spaarke.com` waits for our T240c.
- **external-access-r3**: second message delivered (design.md `9f8a61e2e`: external.spaarke.com, NAA only, 240d positions). Owes its Teams client app id and the T240d CIAM design session.

## Live state

- Prod client sites (Standard SWAs, `rg-spaarke-shared-prod`, subscription `cd95fcec-6b89-49ea-8339-c2b579b12587`): `swa-spaarke-office-addins-prod` → `https://addins.spaarke.com`; `swa-spaarke-external-spa-prod` → `https://external.spaarke.com`. Both Ready, managed certs, empty.
- Dev BFF `spaarke-bff-dev` runs branch build `0911515d7`; future dev BFF deploys only from master ≥ `c8b93b294`. T227d not on dev yet (OwnedContainerIds is set). Dev BFF MI holds application `full` on the Model 1 container type (owner option A).
- Filed: #1376 (ISS-003), #1377 (ISS-004), #1401 (ISS-005 → T218f).

## Open items (no task yet)

- `RoutingConsumerTypeHealthCheck FAILED: AI catalog drift` on the dev BFF — reported, no outcome recorded.
- BFF MI lacks `SecurityEvents.Read.All` (§6B Security tab).
- Dev Redis memory trend — recheck `performanceCounters | where name has 'Private Bytes'` on `spe-insights-dev-67e2xz`.
- No alert on `failed_open_auth` (Prompt Shield).
- Latent: the BFF does not boot without `AzureOpenAI:Endpoint` + `ChatModelName`; stamps always set both.
- Customer onboarding doc (240c): the customer's IT installs the Spaarke add-in in its own tenant (owner decision 2026-10-07).

## T186 (first live E2E) — open questions + live checks

- Target customer undefined after D-12/T228. Needs the customer's own subscription, Dataverse environment `spaarke-{customerId}`, container type id. Do NOT use `runs/trial1-intake.json`.
- H12a seeds 4 of 12 artifacts (playbooks + consumers pending — task 150).
- Live checks: (T228) H5 WhoAmI as the Worker; H1 RG listing under Owner; H6 sign-in right after H10. (T218b) ImportSolutionAsync/StageAndUpgradeAsync + asyncoperations poll against a real environment; managed import over a fresh environment. (T227e/g/d) env-var definitions read; marker PATCHes; `businessunit.sprk_containerid` PATCH; deleted-container marker. (T251) W1 group Name vs DisplayName. (T230b) keyless proof all `proved`, E-2 measurement, `appRoleAssignedTo` POST, token `roles` only. (T240a) H3 client-access PATCH + adoption check against live Graph; stamp BFF starts with the two CORS literals.

## Notes for later tasks

- T250 likely superseded by master `bb8ba7251` (SPE Admin as the BFF MI) — raise with the owner when reached.
- T242c re-scope per D27 (demo = next env; `rg-spaarke-demo` has no AI Search; demo lacks `AiSafety__ContentSafety__Endpoint`).
- T241 lists the shared tier's non-Azure leftovers; owner decision first on `spaarke-model1-prod` references. Legacy app `spaarke-bff-api-prod` (no owner, 1 client secret) — H3 now refuses to adopt it.
- Parked: T246 a–f, T247 (a)–(c) (pin refresh before ~2027-01-14), T244, G34, T252.
