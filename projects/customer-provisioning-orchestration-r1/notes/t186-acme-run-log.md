# T186 — ACME Corporation demo stamp: run log and lessons

> **Purpose** (owner 2026-10-14): T186 is the first live end-to-end provisioning run. It must leave a process that is
> **fully documented and repeatable**. Every step, every special instruction and every surprise is recorded here AS IT
> HAPPENS. Each lesson names the operator document it changes; the run is not closed until every lesson row says
> `applied` (the doc carries it) or `not needed` (with the reason).
>
> Operator documents this run feeds: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` (the procedure),
> `docs/guides/PROVISIONING-PREREQUISITES.md` + `scripts/provisioning-prereqs/prereqs.yaml` (prerequisites),
> `.claude/skills/provision-environment/SKILL.md` (the skill), `docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md`.

## 1. Run facts

| Item | Value |
|---|---|
| customerId | `acme` |
| Display name | ACME Corporation (owner D35 — not Dewey Cheatham, which reads as a law firm; Spaarke's focus is corporations) |
| Subscription | DEMO `2ff9ee48-6f1d-4664-865c-f11868dd1b50` (in management group `spaarke-customers`) |
| Stamp resource group | `rg-spaarke-acme-{env}` (created by H2a) |
| Dataverse environment | `spaarke-acme` — created by the owner (PRQ-C-09) |
| Environment security group | `sprk-acme-users` — Entra `7de5a154-2604-438b-b1cd-462406a869a9` (PRQ-C-10) |
| Solution package | SpaarkeMaster **managed** (D34) |
| Customer workforce directory | ACME Workforce tenant — created by the owner (PRQ-C-13) |

## 2. Prerequisites — who, when, evidence

| Id | What | Who | Status / evidence |
|---|---|---|---|
| PRQ-S-00 | Dedicated subscription | Spaarke | met — DEMO sub holds no other customer's `rg-spaarke-*` |
| PRQ-S-03 | Resource providers | Spaarke | met; `Microsoft.SignalRService` also registered 2026-10-14 (see L4) |
| PRQ-S-04 | L2 Worker UAMI (`38f7693f-…`) Owner on the subscription | Spaarke | **done 2026-10-14** — `az deployment sub create … controlplane-subscription-rbac.bicep principalId=38f7693f-…` → Succeeded; role list shows Owner |
| PRQ-S-06 | Subscription in `spaarke-customers` | Spaarke | met |
| PRQ-C-10 | Group `sprk-acme-users` | Spaarke | **created 2026-10-14** (`az ad group create`, security-enabled, assigned, no members). Owner selects it when creating the environment |
| PRQ-C-09 | Dataverse environment `spaarke-acme` + L2 Worker as System Administrator application user | owner (env) / Spaarke (app user) | env created 2026-10-14 (Production, United States, Managed Environment; env id `db2ccd38-d2ba-ebe3-a3db-4d061aa71104`, org id `3377ca0f-f5c7-f111-be87-00224806e01c`) with URL `spaarkedemo` and security group "Spaarke Demo Users". Both fail the run's checks → the owner changed them to `spaarke-acme` / `sprk-acme-users` (see L8); verified via the BAP admin API (domain `spaarke-acme`, securityGroupId `7de5a154…`, no billing policy yet). App user **done 2026-10-14**: `pac admin assign-user --environment https://spaarke-acme.crm.dynamics.com --user 965a4a01-01e1-442b-97a6-6a98308018b3 --role "System Administrator" --application-user` (965a4a01 = client id of `sprk-controlplane-dev-uami`); the recipe check returns 1 enabled app user holding System Administrator |
| PRQ-C-11 | PAYG billing policy on the DEMO sub, linked to the environment | owner | pending |
| PRQ-C-12 | Guest access on (`restrictGuestUserAccess = false`) | Spaarke | **done 2026-10-14**: PATCH `organizations(3377ca0f-…)` `{"restrictGuestUserAccess": false}` → 204; read back `false` (before: `true`, the new-environment default). Found defect L9 doing it |
| PRQ-C-08 | Exchange group `Spaarke-AppAccess-acme` | owner | pending |
| PRQ-C-13 | ACME workforce tenant id(s) | owner | pending |
| PRQ-C-14 | ACME outbound B2B attestation | owner (as ACME's Entra admin) | pending |
| PRQ-E-13 | Registry placeholder row on spaarkedev1 | Spaarke (skill Step 1) | pending |
| — | SpaarkeMaster release carrying `contact.sprk_externalinvitecodehash` (1.2.3.0) | Spaarke | **published 2026-10-14** (owner OK): PR #1860 → master `bd52b6290`, CI run 38603706393; manifest reads 1.2.3.0, managed sha256 `e84cd5d9…3f662e`. T186's H6 imports this version |

## 3. Pre-run clean-up of the old demo (owner D35)

The old `spaarke-demo` stack predates the Model 1 dedicated-stamp design and is not reused: its Dataverse environment
held SpaarkeMaster **unmanaged**, so H6 would refuse the managed import (D34).

| Item | Action | When |
|---|---|---|
| Dataverse environment `spaarke-demo` | deleted by the owner | 2026-10-14 |
| `spaarke-bff-demo`, `spaarke-demo-plan` | deleted | 2026-10-14 |
| `spaarke-demo-sbus`, `spaarke-openai-demo`, `spaarke-docintel-demo`, `spaarke-cosmos-demo-ai`, `spaarke-bff-redis-demo`, `spaarke-demo-insights` | deleted (0 calls/messages in 30 days beforehand) | 2026-10-14 |
| `mi-bff-api-demo` (no role assignments), `spaarke-demo-logs` (no diagnostic setting of a kept resource pointed at it; 14-day soft delete), apps "Spaarke BFF API - Demo" `da03fe1a…` (object `9956f721`) / "Spaarke UI - Demo" `8d356f5b…` (object `1db66fd4`; Entra deleted items, 30 days) | deleted | 2026-10-14 |
| 10 test accounts `@demo.spaarke.com` (`ralph.logotest`, `e2e.test`, `e2e.test2`, `test.user`, `test.user2`, `demo.final`, `ribbon.test`, `ralph.test`, `final.test`, `full.e2e`) | deleted (Entra recycle bin, 30 days) | 2026-10-14 |
| Registry row "Demo 1" | already inactive — kept (rows are never deleted) | — |
| **Kept** | `WestUS2Plan`, `spaarke-linkedin-refresh`, `spaarke-content-reminder`, `sprkdemosa`, `sprk-demo-kv`, Smart Detection action group (website functions); Syntex account `1f3cfdfb` (owner); group "Spaarke Demo Users" `745bfdf6` (dev BFF demo approvals); real users Jessica, Lori, Chelsea; `demo@` (approved sender mailbox); `uac.child.user` (UAC test user) | — |
| Undecided | `jake.schroeder`, `rachel.schroeder`, `sarah.johnson`, `jane.smith`, `alex.demo` | owner |

## 4. Lessons

| # | Lesson | Changes | Status |
|---|---|---|---|
| L1 | A pre-existing environment that holds SpaarkeMaster unmanaged cannot be adopted for a managed stamp; every stamp starts from a NEW environment. | Deployment guide (environment creation step): say "new environment, never one that held Spaarke solutions" | open |
| L2 | Legacy resource groups mix products: `rg-spaarke-demo` also hosts the website's functions. Inventory consumers (app settings, code references, metrics) before deleting anything, and delete resource by resource, never the group. | Deployment guide (decommission / environment-retirement note) | open |
| L3 | The website demo-request flow is independent of customer stamps: website → `spaarke-bff-dev` → `sprk_registrationrequest` in spaarkedev1 → "Approve Demo Access" → the registry row "Dev" (spaarkedev1, BU "Spaarke Demo"). It adds users to group `745bfdf6`. Retargeting it to a stamp needs a code change (it ignores the row's `sprk_securitygroupid`) and a cross-stamp identity decision. | Deployment guide § demo; owner decision | open |
| L4 | `Microsoft.SignalRService` is not required: SignalR is conditional (`signalrEnabled`, default false) and ARM registers it on demand. Registering it early is harmless. | none — not needed | not needed |
| L6 | Retiring an environment leaves its SharePoint Embedded root container (old demo: `b!FzmtPrWQ…Pdl3-EGQK-D8YZ9tcZp`), and a container cannot be removed once its owning app is gone. A decommission must first list each SPE container with its container type and owning app, and decide container by container, BEFORE it deletes any app registration. Here the container sits under the type billed by Syntex `1f3cfdfb`, which the owner keeps (2026-10-14), so nothing more is done. | Deployment guide (decommission note, with L2) | open |
| L7 | Repo references to a retired environment are scattered across config, ~20 scripts, a web resource and ~10 docs. Retiring an environment includes a repo sweep (done for the old demo in `chore/retire-old-demo-refs`, merged 2026-10-14). | Deployment guide (decommission note) | open |
| L8 | Creating the environment from a prose description yielded the wrong URL (`spaarkedemo`) and the wrong security group (an existing group). Both are enforced (POST /api/runs `dataverse-env-url-invalid`; H11 `userprov-security-group-rejected`), and the wrong group would also admit unrelated users. The operator needs ONE card of exact values to type: name, URL `spaarke-{customerId}`, region, type Production, security group `sprk-{customerId}-users`, plus "check the URL after save: a taken domain silently gets a digit". | Deployment guide (environment-creation step as a value card); skill Step 1 prints the card with the customerId filled in | open |
| L9 | **Defect, fixed before the run.** H11 and the PRQ-C-12 recipe read `organization.restrictguestuseraccess`; the column's logical name is camel-case `restrictGuestUserAccess` and Web API property names are case-sensitive, so the live GET is a 400 and H11 would have failed every run (`userprov-guest-access-restricted` path never reachable). The unit tests faked the response with the same wrong name, so they proved nothing; the researcher note of 2026-10-07 had the right name. Fix: code, recipe, Pester rule, docs; a negative test now rejects the lower-case key. Rule: a Dataverse property name in code is copied from metadata (`EntityDefinitions(...)/Attributes`) or a live response, never typed from prose. Needs a dev control-plane redeploy before T186. | `DataverseWebApiGuestUserWriter.cs`, `H11GuestAccessSeamsTests.cs`, `prereqs.yaml`, `Prereqs-Recipes.Tests.ps1`, docs | applied (code); redeploy pending |
| L10 | The PRQ-C-11 recipe put the billing policy in "the stamp resource group", which does not exist until H2a creates it during the run. The policy now gets its own resource group, `rg-ppbilling-{customerId}`, created before the run and outside the stamp's lifecycle. The recipe also names the current admin-center path, Licensing → Pay-as-you-go plans. | `prereqs.yaml` PRQ-C-11 remediation | applied |
| L11 | Creating an ACME-style test org (the customer's home directory) is an operator step with no recipe. Write a short procedure for creating a Workforce tenant for test or demo customers: the Entra admin center path, its domain, test users, the tenant id for PRQ-C-13 and the outbound B2B default for PRQ-C-14. | Deployment guide, test or demo customer appendix | open |
| L5 | A read-only agent asked to list app settings printed secret values into its transcript. Prompts for settings reads must say "names only; never print values" and filter with `--query "[].name"`. | `.claude/constraints/agent-cost.md` or provisioning.md agent-prompt note | open |
