# Intake fields — Step 1 detail (interactive mode)

Moved from SKILL.md Step 1 (2026-11-03). SKILL.md keeps the step ids, the calls and the hard rules; this file keeps
each field's meaning and the explanations to give the operator. The validation code is in
`scripts/provision-environment/Test-ProvisionIntake.ps1` (1a–1e-bis), `Invoke-RegistryStep.ps1` (1a probe, 1f) and
`scripts/provisioning-prereqs/Invoke-PrereqPass.ps1` (1e-ter).

## Contents

- How to ask — the interactive loop
- 1.0 Batch mode — semantics and sample intake
- 1a `customerId` · 1a-bis `displayName` · 1b `tenantId` · 1b-bis `subscriptionId` / `dataverseEnvUrl`
- 1b-ter `tier` / `estimatedMonthlyUsd` · 1b-quater `openAiMonthlyLimitUsd` · 1b-quinquies `solutionPackageType` · 1b-sexies `secureRecordSetupDryRun`
- 1c `tenancyModel` · 1d `environment` · 1e `profile`
- 1e-bis users, Exchange scope group, mailbox, workforce tenants, B2B checks (field table)
- 1e-ter the customer prerequisite pass
- 1f the registry placeholder — fields set, fields NOT set, deprecated fields
- 1g intake summary

## How to ask — the interactive loop

The original skill prompted with `Read-Host` in a loop until each value was valid. The PowerShell tool has no stdin,
so the loop is now: ask the operator in chat → write the answer into `runs/{customerId}-intake.json` (the
`intake.schema.json` shape) → `Test-ProvisionIntake.ps1 -SessionFile $S -Step <id>` → exit 7 prints `FIELD=<name>`
and the reason: show the reason, ask again. Values the original re-prompted for: customerId, displayName, tenantId,
identityPreset, users (an entry is not added until it is valid; a guest email is listed once), environmentSecurityGroupId,
exchangePolicyScopeGroupId, customerWorkforceTenantIds (comma-separated), communicationDefaultMailbox, and the PRQ-C-14
attestation (asked once, y/N). Other failures are HARD STOPs (exit 1), as before.

For users, ask per user: B2BGuest → email (blank to finish), first name (optional — display name only), last name
(optional), company (optional); NativeAccount → first name (blank to finish), last name (required — UPN), email
(optional), company (optional). At least one user; at most 500 per run.

Collect the 4 inputs the L2 REST API requires. If the operator passed `{customerId}` as a slash-command arg, pre-fill it. Otherwise ask.

## 1.0 Batch mode (`--batch <path.json>`) — added by task 203c per punch-list row A03

For automated / non-interactive invocations. Consumes a JSON intake file validated against [`scripts/provisioning-prereqs/intake.schema.json`](../../../../scripts/provisioning-prereqs/intake.schema.json) (JSON Schema Draft 2020-12), pre-fills every field in 1a-1f, and skips all interactive prompts.

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

**Semantics**: when `-BatchIntakeFile` is passed, sub-steps 1a-1e and 1e-bis are non-interactive (values already assigned from the validated JSON; a missing or invalid value is a HARD STOP — the schema refuses it, and 1e-bis re-checks it in case a fallback validator skipped a conditional rule). Sub-step 1f (environmentId auto-create via Dataverse MCP / Web API fallback) still runs when `intake.environmentId` was omitted or null. The `--batch` path also honors `intake.skipExternalPrereqs` as a batch-native `-SkipStep0_5` equivalent (see Step 0.5c) — recorded in Step 7 lessons-learned.md when set.

Sample intake (see [`intake.schema.json`](../../../../scripts/provisioning-prereqs/intake.schema.json) `examples` block for full-fidelity sample):

```json
{
  "customerId": "acme",
  "tenantId": "a221a95e-6abc-4434-aecc-e48338a1b2f2",
  "tenancyModel": "Model1",
  "controlPlaneEnv": "dev",
  "profile": "spaarke-hosted-model2",
  "subscriptionId": "00000000-0000-0000-0000-000000000000",
  "dataverseEnvUrl": "https://spaarke-acme.crm.dynamics.com/",
  "region": "westus2",
  "tier": "dedicated",
  "estimatedMonthlyUsd": 900,
  "confirmationAcknowledgment": "proceed with provisioning",
  "identityPreset": "B2BGuest",
  "users": [{ "firstName": "Ada", "lastName": "Lovelace", "email": "ada@acme.example", "companyName": "Acme" }],
  "environmentSecurityGroupId": "6f1c2b3a-4d5e-4f60-8a7b-9c0d1e2f3a4b",
  "customerWorkforceTenantIds": ["4b6f2c1e-8d3a-4f5b-9c7e-2a1d0e9f8b7c"],
  "exchangePolicyScopeGroupId": "Spaarke-AppAccess-acme@spaarke.onmicrosoft.com",
  "communicationDefaultMailbox": "acme@spaarke.onmicrosoft.com"
}
```

The `confirmationAcknowledgment` literal is REQUIRED for batch dispatch (intake.schema.json `const` + top-level `required[]` — Bucket A HIGH#2 SESSION 18); a missing/wrong value hard-stops Step 1.0. `tier` + `estimatedMonthlyUsd` are REQUIRED for every model (T229) — the schema, POST /api/runs and H0 all refuse a run without them; H0 refuses an estimate above the tier's ceiling, with no override.

Interactive-mode operators skip this section entirely — proceed to 1a.

## 1a. `customerId` (required)

- Format: **the customerId standard** `^[a-z][a-z0-9]{2,7}$` — 3-8 lowercase letters and digits, starting with a
  letter, **no hyphens** ([`AZURE-RESOURCE-NAMING-CONVENTION.md` § "The customerId standard"](../../../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)).
  Max 8 because `customer.bicep`'s Key Vault name `sprk-{customerId}-{env}-kv` must fit 24 characters without
  ending in a hyphen; no hyphens because the storage-account name strips them (`acme-x` and `acmex` would share
  one account). Neither Bicep nor the Dataverse column can enforce the character rule — **this step and
  `POST /api/runs` are the enforcement points.**
- **Abbreviate once, here.** A customer name longer than 8 characters is shortened by the operator now
  (`northwind` → `nwind`) and the full name goes in `displayName` (1a-bis). Nothing downstream re-derives the id.
- Validate before anything else in Step 1 (both modes). Case-sensitive (`-cmatch`) and anchored with `\z`, so
  `Acme` and a value with a trailing newline are rejected rather than repaired:

  *(Code moved to a script — see SKILL.md Step 1 for the call.)*
- **Uniqueness / upgrade detection** (per Wave 0 Decision 2 / SKILL-02 fix, SESSION 15): probe the `sprk_dataverseenvironment` registry via Dataverse MCP alt-key filter on `sprk_customerid`. Earlier drafts of this skill probed a non-existent `GET /api/runs?customerId=` L2 endpoint (that endpoint has never existed — `RunsEndpoints.cs` maps only 7 routes, none of which is list-by-customerId).

  *(Code moved to a script — see SKILL.md Step 1 for the call.)*

  Fallback if Dataverse MCP disconnected: raw Web API GET with the operator's `az` token — `Invoke-RegistryStep.ps1 -Action Probe -UseWebApi`. (The original also named `pac data query`; pac 1.46 has no such command — MOVE-MAP D2.) See Fallback Matrix F1.
- If reused (upgrade-mode) → per FR-34 §14A upgrade model; operator MUST confirm intent
- If new → this is a fresh-provisioning run (proceed to Step 1f placeholder-create)

## 1a-bis. `displayName` (the customer's full name — also its Dataverse business unit)

- The customer's full name, e.g. `Northwind Traders`. Written to `sprk_dataverseenvironment.sprk_name` next to
  `sprk_customerid` by the Step 1f placeholder create, so the id ↔ name decision is **recorded once on the
  registry row** (T237 / INCOMING-CUSTOMERID-STANDARD §3.3). Defaults to `customerId`.
- **T259 (ISS-010, owner 2026-10-09):** also sent to `POST /api/runs` (Step 4.0), where it is REQUIRED — H10 creates the
  customer's own business unit with this name directly under the Dataverse root (a sibling of `Secure Record`) and puts
  the BFF's application users and every guest in it. Rule (same at `POST /api/runs`, `CustomerBusinessUnitIntake`):
  1–160 characters, no leading/trailing whitespace, no control character, never `Secure Record` in any case.
- Batch mode: `intake.displayName` (pre-filled above). Interactive mode:

  *(Code moved to a script — see SKILL.md Step 1 for the call.)*

## 1b. `tenantId` (required per I1 invariant — NEVER default)

- Format: RFC 4122 GUID
- The customer's Entra tenant ID (Model 1: Spaarke's tenant, where every dedicated stamp lives; Model 2 — out of scope now — would be the customer's tenant)
- Do NOT default; do NOT fall back to `az account show` — the operator MUST supply this explicitly. This enforces the §4D I1 tenant-isolation invariant (FR-28).

## 1b-bis. `subscriptionId` and `dataverseEnvUrl` (required for every model — T228)

The operator creates both BEFORE the run; L2 creates neither and nothing defaults them (owner D4 / Q1; ADR-027).

- `subscriptionId` — the customer's OWN Azure subscription (PRQ-S-00), with the L2 identity granted **Owner** on it
  (PRQ-S-04 — `infrastructure/bicep/modules/controlplane-subscription-rbac.bicep`). Never another customer's, never the
  platform subscription, never `az account show`. H1 refuses a subscription in another tenant or one holding another
  customer's `rg-spaarke-*` group.
- `dataverseEnvUrl` — the environment the operator created (PRQ-C-09), e.g. `https://spaarke-acme.crm.dynamics.com/`.
  Its domain MUST be `spaarke-{customerId}` or `spaarke-{customerId}-{environmentName}` — POST /api/runs and H5 refuse
  anything else (the guard against adopting another customer's environment). The L2 Worker identity must be its System
  Administrator application user, or H5 stops with `worker-not-app-user`.

Reject a blank or malformed value here, before Step 2:

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

## 1b-ter. `tier` and `estimatedMonthlyUsd` (required for every model — T229)

Every run deploys ONE dedicated stamp (`customer.bicep`) into the customer's own subscription — Model 1 paid by Spaarke,
Model 2 by the customer — so the cost check applies to both models and has no override (the shared-trial tier and its
`warnAndProceed` waiver are retired).

- `estimatedMonthlyUsd` — the stamp's projected monthly Azure spend in USD (digits, optional `.`). An empty stamp costs
  about **$340/month fixed** (2026-10-06 list prices, westus2: App Service S1 Linux $58, AI Search S1 $245, Managed Redis
  B0 with high availability $23, Service Bus Standard $10); add the customer's expected OpenAI, Document Intelligence,
  Content Safety, Cosmos DB, storage and log volume.
- `tier` — the budget class whose monthly ceiling covers that estimate: `smb` ($700), `enterprise` ($2,500) or
  `dedicated` ($5,000) (`H0Options.DefaultCeilingsUsd`; exact case).

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

## 1b-quater. `openAiMonthlyLimitUsd` (OPTIONAL — T254, owner G37)

No limit is the default: "we do not want to prevent a customer from activating and working, but we do want to have the
ability to have a cap if necessary." Ask only whether the customer should have one; leave it empty unless the owner or
the customer's agreement calls for a cap. When set, H4b writes `AiSpendLimit__MonthlyLimitUsd` on both slots and the
stamp's BFF answers model calls with HTTP 429 (Retry-After = next UTC month start) once its list-price estimate of the
month's OpenAI spend reaches it. It never blocks provisioning. Change or remove it later with
`scripts/Set-AiSpendLimit.ps1` (guide §3.2b) — not by re-running provisioning. On an UPGRADE run, leave it out (the
setting is then left alone) or send the CURRENT value: a re-run that carries it re-applies it, overriding a later script
change or re-adding a removed limit.

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

## 1b-quinquies. `solutionPackageType` (OPTIONAL — T218b, owner D8)

Customer environments receive the Spaarke package (`SpaarkeMaster`) **managed**. Do not ask; set `unmanaged` only when
the owner explicitly instructs it for this environment (ADR-027 §3, amended 2026-10-07). H6 refuses to switch an
environment that already holds the other type, so on an UPGRADE run send the type the environment already has.

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

## 1b-sexies. `secureRecordSetupDryRun` (OPTIONAL — T256)

Do not ask. `true` only when the operator asks to see what H7b (Secure Record setup) would change: H7b reads everything,
writes nothing, records the plan in gate `h7b-secure-setup-plan`, and the run stops at H7b with `secure_setup.dry_run`
(Failed — expected). Apply with a new run without it.

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

## 1c. `tenancyModel` (required)

Choice:

> 🔴 **AMENDED 2026-09-28 (owner decision D-12).** Both models are **dedicated stamps** — dedicated
> Dataverse environment, dedicated Azure resources, and **one Azure subscription per customer**. They differ
> **only** in which **Azure tenant** owns that subscription. The shared trial/SMB tier is **RETIRED** (it was
> documented and partly built in Bicep but **never implemented in the engine**, which always made one Dataverse
> environment per run — since T228 H5 *adopts* the one the operator created). The BFF **Entra app registration is per customer in both
> models** (D-13, BINDING).
>
> ⚠️ **Literals (T223/T224):** L2 and `intake.schema.json` accept exactly `Model1` | `Model2`
> (case-sensitive). The old `Model1Shared` / `Model2Dedicated` spellings are a 400. L2 pairs `Model1` ↔
> `spaarke-hosted-model2` and `Model2` ↔ `customer-owned-model2` (task 225b); any other pair is a 400
> `tenancy-profile-invalid`.

- `Model1` — Spaarke-hosted dedicated stamp (Spaarke's tenant); profile `spaarke-hosted-model2`. Deployable since
  **T228**: the customer's own subscription (PRQ-S-00 + PRQ-S-04) and Dataverse environment (PRQ-C-09) exist first
  (Step 1b-bis), and H2a deploys the same `customer` template as Model 2.
- `Model2` — customer-hosted dedicated stamp (customer's tenant); profile `customer-owned-model2`; Azure
  Lighthouse required. Out of scope for the current project (owner, 2026-09-30).

Explain the trade-off to the operator if they ask.

## 1d. `environment` (required)

Choice: `dev` / `demo` / `prod` — determines which L2 API base + which Bicep parameter file is used (session-level; NOT the L2 `profile` enum below).

## 1e. `profile` (required — L2 API enum, per punch list row A09 / DS-5 c6-1)

Choice — MUST be the profile paired with the Step 1c `tenancyModel` (two literal strings; any drift → L2 400):

- ~~`spaarke-hosted-model1-trial`~~ — 🔴 **RETIRED (D-12).** L2 refuses it as an unknown profile (400
  `tenancy-profile-invalid`).
- `spaarke-hosted-model2` — pair with **`Model1`**: a dedicated stamp in the customer's **own Azure
  subscription**, inside **Spaarke's** Azure tenant. Lighthouse: **not needed**. H0.5 consent: **not needed**.
- `customer-owned-model2` — pair with **`Model2`**: the same dedicated stamp in the **customer's own Azure
  tenant**. Lighthouse: **required**. H0.5 consent: **required**.

⚠️ **The profile names still encode the retired vocabulary.** They are the literals the live L2 API accepts,
so they are correct to *send* until the enum migration (D-12 §6 items 2–3) renames them to
`spaarke-tenant-model1` / `customer-tenant-model2`. Do not "fix" them locally — L2 rejects unknown values
with a 400.

**Reject any other value BEFORE POST /api/runs**. Do not silently substitute or ask the operator to "just try one" — surface the failure with the exact enum choices.

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

Cross-check: `tenancyModel` × `profile` MUST be consistent: `Model1` ↔ `spaarke-hosted-model2`, `Model2` ↔ `customer-owned-model2` (enforced above). Mismatch → L2 400 `tenancy-profile-invalid`.

Since task 225b the pairing is **1:1** — H1 no longer sees a Spaarke-owned subscription under `Model2`, so the P-2 defect (D-12 §5: Lighthouse demanded for Spaarke-owned subscriptions) is closed at intake. H1 maps `Model1` to Spaarke-owned (no Lighthouse).

## 1e-bis. Users, Exchange scope group, Graph resources, default mailbox (required — T245c)

Five values only the operator knows. `POST /api/runs` refuses a run without them, using **the handlers' own rules
and codes** (H11's through the same `UserProvisioningIntake` code H11 runs), so collect them correctly here — intake
is fixed once the run exists. This step runs **before 1f**, so a bad value stops the skill before it writes the
registry placeholder (the same "validate everything, then write" order `POST /api/runs` follows):

| Value | Read by | Rule (same at `POST /api/runs`) |
|---|---|---|
| `identityPreset` | H11 | `B2BGuest` (invite guests; consent gate) or `NativeAccount` (create users in the stamp's tenant) — exact case. **`Model1` takes only `B2BGuest`** (owner D2, T232 — `userprov-model1-requires-b2b-guest`); this step sets it for a Model 1 run |
| `environmentSecurityGroupId` | H11 | **B2BGuest (every Model 1 run)**: object id (GUID) of the environment's security group `sprk-{customerId}-users`, created by the operator and set on the environment before the run (`PRQ-C-10`). H11 adds each redeemed guest to it, then makes the guest a Dataverse user with the Spaarke role — the group keeps other customers' guests out of this environment. The environment must also allow guests (`PRQ-C-12`) and be linked to a pay-as-you-go billing policy on the stamp subscription (`PRQ-C-11` — Spaarke pays guest access PAYG, owner 2026-10-07; no licences are assigned). This step checks all three as the operator |
| `users` → `usersJson` | H11 | 1–500 entries; `NativeAccount`: non-blank `firstName` + `lastName` (the UPN is built from them); `B2BGuest`: `email` (the invitation goes to it; names optional) |
| `exchangePolicyScopeGroupId` | H14a | the mail-enabled security group H14a scopes the stamp identity's Exchange mailbox roles to (Entra object id or email address; only DIRECT members' mailboxes are reachable). **The Exchange admin of the stamp's tenant creates it before the run, named `Spaarke-AppAccess-{customerId}` — prerequisite `PRQ-C-08`.** H14m adds exactly one member, the customer's shared mailbox (T263); every other member is the customer's decision. |
| `communicationDefaultMailbox` | H4 (KV `Communication-DefaultMailbox`); H14m | `local@domain.tld`, ≤ 254 characters — the address of the Spaarke-tenant shared mailbox H14m CREATES (T263): an accepted domain of Spaarke's tenant (e.g. `acme@spaarke.onmicrosoft.com`), not in use by any other mailbox (that quarantines the run) |
| `displayName` | H10 (customer business unit), registry `sprk_name` | **Every run (T259)**: 1–160 characters, no leading/trailing whitespace, no control character, never `Secure Record` (`h10-customer-display-name-required` / `-invalid`); defaults to `customerId` (1a-bis) |
| `customerWorkforceTenantIds` | H4b → `WorkforceIdentity__CustomerTenantIds__N` (both slots); H13 T7 | **Every run (T255, INCOMING-141)**: the CUSTOMER's Entra tenant id(s), 1–10 distinct lowercase GUIDs. Model 1: the customer's HOME tenant (its staff are B2B guests from there) — never Spaarke's tenant / this run's `tenantId`; Model 2: the customer's tenant. POST /api/runs also refuses the CIAM tenant (`workforce-tenants-required` / `-invalid` / `-ciam-tenant` / `-spaarke-tenant`). Prerequisite `PRQ-C-13` |
| `customerOutboundB2BAttested` | PRQ-C-14 (skill-local) | B2BGuest: `true` only after the customer's Entra admin confirmed outbound B2B collaboration to Spaarke's tenant; never sent to L2 |

**Personal data.** The user list (names, emails) is stored in the L2 run document, as the owner accepted on
2026-10-01 (D15). It never goes into git: Step 1.0 refuses a batch intake file git would track, and
`provisioning-runs/{run}/intake.md` (committed) records the user **count** only. Prefer shared/service mailboxes for
the Graph resources and the default mailbox — those values ARE recorded in `intake.md`.

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

## 1e-ter. Customer prerequisites — the `once_per_customer` pass (T207 close-out, 2026-10-09)

Now that the intake names the customer, run the `once_per_customer` recipes (the customer's subscription, its
Dataverse environment, the environment security group, the Exchange scope group, quotas) with the same function and
the same HARD STOP rules as Step 0.5 — BEFORE Step 1f writes anything. Skipped only with `-SkipStep0_5` /
`"skipExternalPrereqs": true` (recorded in Step 7, as for Step 0.5).

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

Recipes that need the customer's subscription (PRQ-S-*, PRQ-C-03, PRQ-C-11) run with the operator's az
sign-in, which must reach that subscription (Model 1: Spaarke's tenant). PRQ-S-06 also needs read on the
`spaarke-customers` management group (Reader or above at or over it). PRQ-E-15's Exchange half and PRQ-C-10's
"group is the one set on the environment" are asserted elsewhere (the Exchange admin; Step 1e-bis).

## 1f. `environmentId` — create placeholder `sprk_dataverseenvironment` record (required — per punch list rows A10 + A11 / DS-5 c6-2 + c6-3)

The L2 API's `POST /api/runs` REQUIRES `environmentId` (the `sprk_dataverseenvironment` record GUID). L2 returns 400 without it (per DS-5 c6-2). This step creates the placeholder record BEFORE the POST so the GUID is available.

**Registry env (until central-managing env exists — 2026-08-26 owner directive)**: use `spaarkedev1` for `environment=dev`. No registry env exists for `environment=demo`: the old `spaarke-demo` environment was retired 2026-10-14 (D35), and the demo is now the ACME stamp, which this skill provisions on the dev control plane (registry `spaarkedev1`). Production registry env is NOT YET provisioned; treat as an r2 follow-on. The Spaarke dev registry env doubles as the engineering dev env — this is intentional for now. See operator memory `feedback_no_central_managing_env_yet`.

**Preferred path — Dataverse MCP** (`mcp__dataverse__create_record`). The payload MUST include the 4 NOT-NULL fields (`sprk_name`, `sprk_environmenttype`, `sprk_dataverseurl`, `sprk_isactive`, `sprk_isdefault`) OR Dataverse returns 400. Registry columns added in task 023 (deployed 2026-08-26 SESSION 13) + `sprk_customerid` added by companion `scripts/Add-CustomerIdColumn.ps1` (task 199 reconciliation) provide the remaining fields.

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

Fallback path (per §4.3a.5) — if MCP disconnected: `Invoke-RegistryStep.ps1 -Action CreateWebApi` (Web API, operator token). The original used `pac data create`, which pac 1.46 does not have (MOVE-MAP D2); its `;`/`=` displayName workaround is not needed with a JSON body.

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

Verify `$environmentId` is a valid GUID **AND** that the row is queryable via alt-key (PRQ-05 addendum, SESSION 15 — PRQ-E-13 is retired in prereqs.yaml because this step creates the row after the customer pass (T186 L13), so the placeholder-record-exists check lives here exclusively):

*(Code moved to a script — see SKILL.md Step 1 for the call.)*

**Fields intentionally NOT set at placeholder-create time** (populated by later handlers, per task 023 constraint "no code path that reads/writes these columns beyond what already exists in DataverseEnvironmentRecord.cs"):
- `sprk_currentrunid` — set by L2 CustomerRunGuard as part of the run enqueue (§4D I5)
- `sprk_azuresubscriptionid`, `sprk_resourcegroupname`, `sprk_appservicename`, `sprk_keyvaultname`, `sprk_containertypeid` — populated by H2a Bicep composition
- `sprk_bffversion`, `sprk_solutionversion` — populated by H9 BFF deploy + H6 solution import
- `sprk_ClientCacheBustToken` — populated by H7 env-var writes
- `sprk_provisionedon` — set by H13 acceptance gate on transition to Ready
- `sprk_setupstatus = 2 (Ready)` — set by H13 via `DataverseRegistrySetupStatusUpdater` at completion (also clears `sprk_currentrunid`)

The placeholder is later promoted to real state by H10/H13 (setup registry update) when the run reaches `Ready`. This creates the audit trail from "run enqueued" → "run complete" in a single sprk_dataverseenvironment lifecycle.

**Fields deliberately NOT in the placeholder** (deprecated / never landed — do NOT re-add these):
- `sprk_profile` — never authored in any script or code; skill previously referenced it but grep-zero across `src/**` (removed 2026-08-26 SESSION 13 per task 199 reconciliation)
- `sprk_upgrademode` — DERIVED from `sprk_provisionedon IS NOT NULL` by H4 KV secrets handler (`IKvSecretsWriter.UpgradeMode`); never persisted as a column

## 1g. Show intake summary

```
INTAKE SUMMARY
  customerId:      acme
  displayName:     Acme Corporation
  tenantId:        12345678-...-...-...  (customer tenant)
  tenancyModel:    Model2
  controlPlaneEnv: dev
  profile:         customer-owned-model2
  subscriptionId:  9f1c...  (the customer's own — PRQ-S-00)
  dataverseEnvUrl: https://spaarke-acme.crm.dynamics.com/  (operator-created — PRQ-C-09)
  environmentId:   a1b2c3d4-...  (placeholder sprk_dataverseenvironment record, sprk_setupstatus=1 InProgress)
  identityPreset:  B2BGuest
  users:           3 entries          (names/emails are NOT printed or written to intake.md)
  env group:       6f1c2b3a-...  sprk-acme-users  (PRQ-C-10; guest access PRQ-C-12 ✓; PAYG PRQ-C-11 confirmed)
  exchange group:  Spaarke-AppAccess-acme@spaarke.onmicrosoft.com  (PRQ-C-08)
  default mailbox: acme@spaarke.onmicrosoft.com  (H14m creates it as sprk-acme-mail)
  L2 API:          https://spaarke-provisioning-controlplane-dev.azurewebsites.net

Proceed to preflight (H0)? (yes/no)
```

Wait for "yes" (bare "y" is insufficient at every gate in this skill — spec §4.3a.4).

**BAT-02 (SESSION 16)** — batch mode SKIPS the Step 1g prompt (the summary is written to stdout for audit, but no operator input is expected). The Step 3 confirmation gate is what covers the intent-to-proceed attestation in batch mode (via `intake.confirmationAcknowledgment` const-string validated at Step 1.0). Skipping Step 1g's yes/no prompt in batch mode is NOT a bypass — it eliminates a stdin read that would block the run indefinitely under `--batch` unattended dispatch:

*(Code moved to a script — see SKILL.md Step 1 for the call.)*
