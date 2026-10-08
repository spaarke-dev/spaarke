# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2 "Binding rules", §3 "Owner directives", §6 "Gotchas" (one dated line each). Decisions + superseded rules → `notes/decisions.md`. Session narrative → checkpoint commit messages. History: git + `notes/handoff-history/` (do not load on recovery). Review limits: task-execute Step 9.5.

> **Last Updated**: 2026-10-08 SESSION 43 (task-execute checkpoint) — 218a ✅ ADR-027 amended; 218b ✅ H6 = one SpaarkeMaster, managed by default; 218c ✅ package scope rule + drift + export (read-only runs); next 218e needs the owner's OK for live dev changes.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **T218** — the complete Dataverse package. Plan `notes/t218-plan.md`; POMLs `tasks/218a…218e`, `257`. 218a ✅ `8b878d655`; 218b ✅ `3f41aab33`/`4f6c579ed`/`d3aa70726`; 218c ✅ `48b16bcb8`/`3eb20566e`/`3d280df7d`. |
| **Step** | Next: **218e** — content gaps + first source export. Every step writes to spaarkedev1's SpaarkeMaster → **owner OK first** (owner item 9). |
| **Status** | waiting on the owner's OK for 218e's live dev changes. |
| **Next Action** | On the owner's OK: `task-execute` 218e — (1) `Test-SolutionCompleteness.ps1 -FailOnDrift:$false -SkipInventory` (read-only) → classify the 53 missing (ship / exclude with reason; the 4 April PCFs by form usage); (2) remove from SpaarkeMaster what is OUTSIDE THE RULE (5 Microsoft tables, 8 env-var values) + the Provisioning Registry role; (3) `Assemble-SpaarkeMasterSolution.ps1 -WhatIf`, then without (adds + re-adds the 8 shells, bumps the version); (4) `Export-SpaarkeMasterSource.ps1` (pac auth with access to spaarkedev1) → commit src/dataverse/solutions/SpaarkeMaster; then 218d (CI pack + publish). Without the OK: continue with T235. |
| **Branch** | `work/customer-provisioning-orchestration-r1`, pushed, clean (after this checkpoint). **9+ behind master** (2026-10-08): merge master before T186, before any BFF deploy from this branch, and before PR #1365 merges; after the merge grep the BFF for `.ForApp(` and run `SpeAppOnlyContainerGuardTests` (CLAUDE.md §6). PR #1365 open. |
| **Order** | 218c → 218e → 218d → T235 → T250 → 213.7/207/208/209 → **T253** (G38) → **T255** (INCOMING-141; also H3's `acct` claim) → **T256** (waits on #1364) → T240c (owner OK for the service) → T240d → T257 → T186. T240b when word-add-in-r1 answers. |

### T218 facts for 218e/218d (details in 218c's POML notes)
- Live SpaarkeMaster in dev (2026-10-08, read-only): 520 in scope, 18 excluded, **53 missing**, **8 sprk_ tables packaged as shells** (incl. sprk_signal, sprk_noaccessentry, sprk_policy), Provisioning Registry role packaged though excluded, **13 outside the rule** (5 Microsoft tables dragged in by the 2026-08-23 rebuild, 8 env-var values).
- Roles: "Spaarke Basic User" (H11 default) EXISTS and is in SpaarkeMaster; "Secure Record Owner" is a root role in the child unit "Secure Record". (Two earlier wrong notes came from 20-row-capped MCP queries — pass `top` or use the scripts.)
- The store `sprkcpartifactsdev/provisioning-artifacts` holds the hand-made old-format manifest → a real run fails at H6 (missing-solution-zips) until 218d publishes. Manifest format: `{"solutions":{"SpaarkeMaster":{"version","managedBlobName","unmanagedBlobName"}}}`.

### T240b (blocked)
Deploying the add-in package in another organization fails at consent (`AADSTS700016`): the add-in apps are single-tenant — dev `c1258e2d…` AND production `1958aec2…` (word-add-in-r1 created it 2026-10-07, master `cbee69b68`). Customer BFFs now pre-authorize `1958aec2` (Worker Bicep, `4d41cb026`). Message `notes/coordination/2026-10-08-to-word-add-in-r1-4.md` (+ addendum for the production app).

### Test organization (owner-created 2026-10-07)
- Tenant **Dewey Cheatham & Howe PC**, `deweycheatham.onmicrosoft.com`, id `bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b`; Global Admins `admin@` and `ralph@`. Licenses: Business Basic ×2 (web Office only) — desktop Outlook/Word need Business Standard (owner, optional).
- Guest `ralph@deweycheatham.onmicrosoft.com` in Spaarke's tenant: Accepted, `federated/ExternalAzureAD`; owner added him to dev Dataverse (license, Spaarke Core User + Spaarke Add In User).
- CLI access to the test tenant: `$env:AZURE_CONFIG_DIR = <scratchpad>\azcfg-testorg` (session scratchpad, private cache — never the shared az context). A new session must sign in again: `az login --tenant deweycheatham.onmicrosoft.com --allow-no-subscriptions --use-device-code` in a fresh private config dir; the owner completes the code in a private browser window.

## Owner items

1. **Relay messages** (owner has the paths): `notes/coordination/2026-10-07-to-word-add-in-r1-2.md` (if not sent), `…-to-word-add-in-r1-3.md` (prod site ready), `2026-10-08-to-word-add-in-r1-4.md` (multi-tenant finding **+ addendum: production app `1958aec2` is single-tenant too**), `2026-10-07-to-external-access-r3-2.md` (owner decisions). Replies go into `notes/coordination/2026-10-0x-from-*.md`.
2. **Delete the mistaken External ID tenant** `spaarketestpartner.onmicrosoft.com` (Entra admin center, then its leftover Azure resource in `rg-spaarke-dev`; NEVER `spaarkeextid` beside it). Offer to remove the Azure resource with OK.
3. **Approve the multi-tenant change** on `c1258e2d…` (dev) and `1958aec2…` (production) once word-add-in-r1 agrees.
4. **T240c**: OK for a new shared-prod directory service (recommendation in `notes/t240-plan.md`).
5. **Next control-plane deploy** carries T240a's Worker Bicep (`EntraAppRegOptions__SpaarkeTenantId`, `PreAuthorizedClientAppIds`) — needs OK; W7 is blocked by the Api site's pending slot swap (every `platform-controlplane` deploy fails its Api module until it resolves).
6. **G36** (ADR-027 management group) and **G31** (H10 tenant-wide Directory/User write roles) — awaiting decisions; do NOT act.
7. Board Status "Active" vs Status Reason "On hold" on Issue #438.
8. **ISS-005 / #1401 (escalation from 218b)**: `Deploy-Release.ps1` (deploy-new-release skill, Spaarke's own envs) and the legacy `Provision-Customer.ps1` still call `Deploy-DataverseSolutions.ps1`, whose list names 6 nonexistent solutions. Recommendation: package type per Spaarke environment in `config/environments.json`, then retire the PS list in favour of the CI-published SpaarkeMaster zips.
9. **218e live steps (ASK NOW)**: changes to SpaarkeMaster in spaarkedev1 — add the missing components, remove the 5 Microsoft tables + 8 env-var values + Provisioning Registry role, re-add the 8 shell tables, bump the version, export to git. **218d**: first CI publish to `sprkcpartifactsdev` (ask when reached).
10. **`sprk_solutionversion` format** changed to `SpaarkeMaster {version} ({type})` (supersedes owner D17's fingerprint; matrix doc v3) — inform, no action.

## Cross-project deliveries (track until delivered)

- **UAC-r2**: relay message handed to the owner 2026-10-07 (INCOMING-141/145 ack, #1364 `sprk_noaccessentry`, #1363 demo-grant marker, §13.9(b) do-not-mint, task-171 merge notes). Open: their answer on #1364 (T256/T218/T186 wait) and the #1363 fix.
- **word-add-in-r1**: diagnostics build (acct/idp/tid/oid/iss/aud), prod deployment to `addins.spaarke.com`, multi-tenant app decision, admin guide drift (`spe-office-addins-prod` never existed).
- **external-access-r3**: adopted the requirements into its design.md (starts 2026-10-07); owes its Teams client app id and the T240d CIAM design session.

## Live state

- Prod client sites (Standard SWAs, `rg-spaarke-shared-prod`, subscription `cd95fcec-6b89-49ea-8339-c2b579b12587`): `swa-spaarke-office-addins-prod` → `https://addins.spaarke.com`; `swa-spaarke-external-spa-prod` → `https://external.spaarke.com`. Both Ready, managed certs, empty.
- Dev BFF `spaarke-bff-dev` runs branch build `0911515d7`; future dev BFF deploys only from master ≥ `c8b93b294`. T227d not on dev yet (OwnedContainerIds is set). Dev BFF MI holds application `full` on the Model 1 container type (owner option A).
- Filed: #1376 (ISS-003), #1377 (ISS-004), #1401 (ISS-005, Deploy-Release Phase 3 — 2026-10-07).

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
- Tasks without a POML: create from `notes/model1-dedicated-remediation-plan.md` §7 (copy `tasks/228-…poml`), add TASK-INDEX rows (7 columns).
- Parked: T246 a–f, T247 (a)–(c) (pin refresh before ~2027-01-14), T244, G34, T252.
