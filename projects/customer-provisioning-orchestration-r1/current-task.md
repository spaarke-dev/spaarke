# Current Task State — `customer-provisioning-orchestration-r1`

> **Format**: CURRENT state only, REWRITTEN at each checkpoint (≤ 10 KB) — never prepend. Standing rules → project `CLAUDE.md` §2 "Binding rules", §3 "Owner directives", §6 "Gotchas" (one dated line each). Decisions + superseded rules → `notes/decisions.md`. Session narrative → checkpoint commit messages. History: git + `notes/handoff-history/` (do not load on recovery). Review limits: task-execute Step 9.5.

> **Last Updated**: 2026-10-08 SESSION 42 (context-handoff) — T240a ✅; prod client sites live; external test org set up; T240b blocked on the add-in app being single-tenant.

## 🎯 Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **T240b** — live test: a work-account B2B guest uses the Spaarke add-in from their own company's Outlook/Word and gets a Spaarke-tenant token for the dev BFF. POML `tasks/240b-…poml`; plan `notes/t240-plan.md` ("Test organization", findings). |
| **Step** | Test org ready; first deployment attempt FAILED (finding below). Waiting on spaarkeai-word-add-in-r1. |
| **Status** | blocked. Next unblocked task: **T218** (complete solution package). |
| **Next Action** | `task-execute` T218, unless the owner relays word-add-in-r1's answer first. When the add-in app `c1258e2d…` is multi-tenant (owner OK + word-add-in-r1 agreement; reversible PATCH of `signInAudience` to `AzureADMultipleOrgs`), the owner re-uploads `C:\Users\RalphSchroeder\Downloads\spaarke-addin-package\spaarke-addin-1.1.1.zip` in Dewey Cheatham's admin center (Integrated apps → Upload custom apps → app type "Teams app" → user Ralph), then test Outlook + Word on the web as Ralph; check dev BFF App Insights for Ralph's guest oid `bc596ecd-b61c-43f7-8664-0f27a2267a67` / tid `a221a95e…`. |
| **Branch** | `work/customer-provisioning-orchestration-r1` @ `a684795e2`, pushed, clean. **9 behind master** (2026-10-08): merge master before T186, before any BFF deploy from this branch, and before PR #1365 merges; after the merge grep the BFF for `.ForApp(` and run `SpeAppOnlyContainerGuardTests` (CLAUDE.md §6). PR #1365 open. |
| **Order** | T240a ✅ → T240b (blocked) → T218 → T235 → T250 → 213.7/207/208/209 → **T253** (G38) → **T255** (INCOMING-141; also owns H3's `acct` optional claim) → **T256** (waits on #1364) → T240c (directory; owner OK for the new shared-prod service) → T240d (stamps serve CIAM external contacts; owner said yes) → T186. T252 carries the Bicep "shared BFF app-reg" description fix. T242c / T241 when the owner wants them. |

### T240b finding (2026-10-08)
Deploying the unified add-in package in another organization fails at consent: `AADSTS700016 — Application c1258e2d… was not found in the directory bc3aa7f4…`. The package's `webApplicationInfo.id` is the add-in app, which is single-tenant, so **no customer IT can deploy the add-in today**. Recommendation (to word-add-in-r1, owner relays): make it multi-tenant. Message: `notes/coordination/2026-10-08-to-word-add-in-r1-4.md`. The same applies to the Teams client app external-access-r3 will create — include it in the next message to them.

### Test organization (owner-created 2026-10-07)
- Tenant **Dewey Cheatham & Howe PC**, `deweycheatham.onmicrosoft.com`, id `bc3aa7f4-3ca3-47e6-84e7-fea35f5c245b`; Global Admins `admin@` and `ralph@`. Licenses: Business Basic ×2 (web Office only) — desktop Outlook/Word need Business Standard (owner, optional).
- Guest `ralph@deweycheatham.onmicrosoft.com` in Spaarke's tenant: Accepted, `federated/ExternalAzureAD`; owner added him to dev Dataverse (license, Spaarke Core User + Spaarke Add In User).
- CLI access to the test tenant: `$env:AZURE_CONFIG_DIR = <scratchpad>\azcfg-testorg` (session scratchpad, private cache — never the shared az context). A new session must sign in again: `az login --tenant deweycheatham.onmicrosoft.com --allow-no-subscriptions --use-device-code` in a fresh private config dir; the owner completes the code in a private browser window.

## Owner items

1. **Relay messages** (owner has the paths): `notes/coordination/2026-10-07-to-word-add-in-r1-2.md` (if not sent), `…-to-word-add-in-r1-3.md` (prod site ready), `2026-10-08-to-word-add-in-r1-4.md` (multi-tenant finding), `2026-10-07-to-external-access-r3-2.md` (owner decisions). Replies go into `notes/coordination/2026-10-0x-from-*.md`.
2. **Delete the mistaken External ID tenant** `spaarketestpartner.onmicrosoft.com` (Entra admin center, then its leftover Azure resource in `rg-spaarke-dev`; NEVER `spaarkeextid` beside it). Offer to remove the Azure resource with OK.
3. **Approve the multi-tenant change** on `c1258e2d…` once word-add-in-r1 agrees.
4. **T240c**: OK for a new shared-prod directory service (recommendation in `notes/t240-plan.md`).
5. **Next control-plane deploy** carries T240a's Worker Bicep (`EntraAppRegOptions__SpaarkeTenantId`, `PreAuthorizedClientAppIds`) — needs OK; W7 is blocked by the Api site's pending slot swap (every `platform-controlplane` deploy fails its Api module until it resolves).
6. **G36** (ADR-027 management group) and **G31** (H10 tenant-wide Directory/User write roles) — awaiting decisions; do NOT act.
7. Board Status "Active" vs Status Reason "On hold" on Issue #438.

## Cross-project deliveries (track until delivered)

- **UAC-r2**: relay message handed to the owner 2026-10-07 (INCOMING-141/145 ack, #1364 `sprk_noaccessentry`, #1363 demo-grant marker, §13.9(b) do-not-mint, task-171 merge notes). Open: their answer on #1364 (T256/T218/T186 wait) and the #1363 fix.
- **word-add-in-r1**: diagnostics build (acct/idp/tid/oid/iss/aud), prod deployment to `addins.spaarke.com`, multi-tenant app decision, admin guide drift (`spe-office-addins-prod` never existed).
- **external-access-r3**: adopted the requirements into its design.md (starts 2026-10-07); owes its Teams client app id and the T240d CIAM design session.

## Live state

- Prod client sites (Standard SWAs, `rg-spaarke-shared-prod`, subscription `cd95fcec-6b89-49ea-8339-c2b579b12587`): `swa-spaarke-office-addins-prod` → `https://addins.spaarke.com`; `swa-spaarke-external-spa-prod` → `https://external.spaarke.com`. Both Ready, managed certs, empty.
- Dev BFF `spaarke-bff-dev` runs branch build `0911515d7`; future dev BFF deploys only from master ≥ `c8b93b294`. T227d not on dev yet (OwnedContainerIds is set). Dev BFF MI holds application `full` on the Model 1 container type (owner option A).
- Filed this session: #1376 (ISS-003, one registration per customer vs a second environment), #1377 (ISS-004, any Spaarke-tenant user can get any customer BFF token).

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
- Live checks: (T228) H5 WhoAmI as the Worker; H1 RG listing under Owner; H6 sign-in right after H10. (T227e/g/d) env-var definitions read; marker PATCHes; `businessunit.sprk_containerid` PATCH; deleted-container marker. (T251) W1 group Name vs DisplayName. (T230b) keyless proof all `proved`, E-2 measurement, `appRoleAssignedTo` POST, token `roles` only. (T240a) H3 client-access PATCH + adoption check against live Graph; stamp BFF starts with the two CORS literals.

## Notes for later tasks

- T250 likely superseded by master `bb8ba7251` (SPE Admin as the BFF MI) — raise with the owner when reached.
- T242c re-scope per D27 (demo = next env; `rg-spaarke-demo` has no AI Search; demo lacks `AiSafety__ContentSafety__Endpoint`).
- T241 lists the shared tier's non-Azure leftovers; owner decision first on `spaarke-model1-prod` references. Legacy app `spaarke-bff-api-prod` (no owner, 1 client secret) — H3 now refuses to adopt it.
- T218: replace the hand-uploaded H6 artifacts in `sprkcpartifactsdev/provisioning-artifacts`; ship `sprk_noaccessentry` (T256).
- Tasks without a POML: create from `notes/model1-dedicated-remediation-plan.md` §7 (copy `tasks/228-…poml`), add TASK-INDEX rows (7 columns).
- Parked: T246 a–f, T247 (a)–(c) (pin refresh before ~2027-01-14), T244, G34, T252.
