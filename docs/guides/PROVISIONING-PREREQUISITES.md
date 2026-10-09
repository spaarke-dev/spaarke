# PROVISIONING-PREREQUISITES — canonical prerequisite reference

> **Version**: 9 · **Last Updated**: 2026-10-09
> **Machine-parseable source of truth**: [`scripts/provisioning-prereqs/prereqs.yaml`](../../scripts/provisioning-prereqs/prereqs.yaml) (manifest_version 11)
> **Owner**: `customer-provisioning-orchestration-r1` task 202
> **Consumers**: `/provision-environment` skill Step 0.5 (via task 203 wiring); human operators reading this file.
>
>
> **v11 (2026-10-09, `customer-provisioning-orchestration-r1` T262 — G36, owner approval 2026-10-09)**: `PRQ-S-06`
> **added** — the customer's subscription is a direct member of the `spaarke-customers` management group, where the
> common customer policy (`infrastructure/bicep/customer-policy.bicep`, ADR-027) is assigned. New recipe token
> `{customerManagementGroupId}` (`spaarke-constants.yaml` `management_groups.customers.id`). `PRQ-S-00`'s remediation
> names the group. `validate.ps1`'s `grep -i -F` lint (listed under v10) was inert until this version — its code sat on
> a comment line — and now runs.
>
> **v10 (2026-10-08, `customer-provisioning-orchestration-r1` T206 + T207 re-measured)**: every check recipe now decides by
> exit code and asserts the condition its `expect` field describes (before: 33 of 36 active recipes had no `exit 1`, so a
> failing check printed a result and passed). Recipes that were wrong rather than merely unguarded: `PRQ-E-01` /
> `PRQ-E-02` / `PRQ-E-09` named resources that do not exist (`sprk{env}artifacts`, `sprk{env}acr`, `sprk-{env}-kv`; the
> real names are `sprkcpartifacts{env}`, `sprkcontrolplane{env}acr`, `sprk-controlplane-{env}-kv`); `PRQ-E-07` queried
> the Graph service principal's own `appRoleAssignments` instead of the L2 identity's; `PRQ-E-08` / `PRQ-E-13` used `pac`
> commands and a column (`sprk_environmentid`) that do not exist; `PRQ-C-01` / `PRQ-C-02` still checked `gpt-5` /
> `GlobalStandard` instead of the three `DataZoneStandard` pins in `openai.bicep` / `PinnedModelCatalog.cs`;
> `PRQ-C-07` ran PowerShell `Select-String` under `bash -c`. **Rescoped** `once_per_tenant` -> `once_per_env`:
> `PRQ-T-01` … `PRQ-T-05` (they use `{containerTypeId}` / `{env}`, which are unknown while skill Step 0.5 runs the tenant
> scope, so they could never resolve there). `validate.ps1` now lints every recipe for the defect classes above
> (token available at the step that runs its scope, no undefined bash variable, explicit `exit 1`, no PowerShell cmdlet
> under bash, no parenthesis in an `az` argument, no `grep -i -F`), and `tests/scripts/Prereqs-Recipes.Tests.ps1` runs
> the recipes against fake `az` / `curl` / `pac` and proves each exits 1 on the defect it exists to catch.
> **Same version, 2026-10-09:** `PRQ-T-05` (Copilot bot app — superseded by the T257 design: one shared OAuth client
> app, no bot) and `PRQ-T-06` (Power BI service principal — BI is out of scope for r1) **retired**. The skill now RUNS
> the `once_per_customer` recipes: Step 1e-ter, after the intake names the customer and before Step 1f writes anything,
> with the same HARD STOP as Step 0.5 (previously no step ran them).
> **Same version, T255 — INCOMING-141 (2026-10-09):** `PRQ-C-13` **added** — the customer's workforce tenant id(s),
> intake `customerWorkforceTenantIds` (required for every run). H4b writes them as `WorkforceIdentity__CustomerTenantIds__N`
> on both BFF slots; without them the stamp denies every first sign-in of a customer employee.
> **Same version, T253 — G38 (2026-10-08):** `PRQ-C-07` **retired** — the CI-built
> SpaarkeMaster needs no Power BI Extensions and H6 installs no application. `PRQ-C-06`: H6 applies the org-settings
> contract through the Dataverse Web API (the L2 Worker host has no pac).
>
> **v9 (2026-10-07, `customer-provisioning-orchestration-r1` T232 — owner D2 + PAYG decision 2026-10-07)**: Model 1
> users are B2B guests whose access Spaarke pays pay-as-you-go. `PRQ-C-10` **added** (the environment's security group
> `sprk-{customerId}-users` — intake `environmentSecurityGroupId`), `PRQ-C-11` **added** (the environment linked to a
> pay-as-you-go billing policy on the stamp subscription), `PRQ-C-12` **added** (guest access allowed —
> `restrictguestuseraccess` off). All three are checked by the skill (Step 1e-bis) as the operator — including that the
> group is the one SET ON the environment (Power Platform admin API). H11 re-checks the group's name and guest access
> server-side; the group binding and the billing link are visible only to a Power Platform admin, which L2 is not.
>
> **v8 (2026-10-06, `customer-provisioning-orchestration-r1` T228 — owner D4 / Q1)**: the operator creates the customer's
> subscription and Dataverse environment; L2 creates neither. `PRQ-S-00` **added** (the customer's own subscription —
> Enabled, holding no other customer's `rg-spaarke-*`), `PRQ-C-09` **added** (the operator-created environment, domain
> `spaarke-{customerId}`, with the L2 Worker identity as a System Administrator application user), `PRQ-C-04` **retired**
> (environment-creation rate — L2 no longer creates environments). `PRQ-S-04` is now **Owner** (owner decision
> 2026-10-06 — customer.bicep writes role assignments), granted with `modules/controlplane-subscription-rbac.bicep` at the
> customer's subscription. With one subscription per customer, every `PRQ-S-*` is `once_per_customer` and reads
> `{stampSubscriptionId}` — never the operator's current `az account`; like every `once_per_customer` entry, skill Step 0.5
> does not run them (H1 and H2a enforce what matters server-side).
>
> **v7 (2026-10-06, `customer-provisioning-orchestration-r1` T227a)**: `PRQ-C-05` **retired** — Model 2 only (out of scope, D-12), and its check read a shared BFF app id that no longer exists (each customer's BFF app registration is created by H3 during the run).
>
> **v6 (2026-10-04, `customer-provisioning-orchestration-r1` T251)**: `PRQ-E-15` **added** — the Exchange admin app
> (`Spaarke Exchange Admin`, federated credential trusting the L2 Worker UAMI, no secret or certificate) and its narrowed
> Exchange role; one-time per tenant. `PRQ-C-08` reworded: the group now scopes H14a's Exchange RBAC for Applications
> role assignments, not an ApplicationAccessPolicy.
>
> **v5 (2026-10-02, `customer-provisioning-orchestration-r1` T225b)**: `PRQ-E-14` **added** — the registry schema on
> the admin environment, including the new `sprk_credentialmode` column. Every new stamp is secret-free by default
> since T225b, so H4 always records the A38a marker; without the column H4 fails after writing the vault. The column
> is added by the existing idempotent `Extend-DataverseEnvironmentSchema-v3.3.ps1` (applied to `spaarkedev1` 2026-10-03 with the operator identity — the column now exists).
>
> **v4 (2026-10-02, `customer-provisioning-orchestration-r1` T225a)**: the Model 1 shared stack is retired (files
> deleted). `PRQ-E-05` moved `once_per_env` → `once_per_customer` and now targets the stamp's own BFF — it is an H2a
> postcondition (`customer.bicep` emits the grant), documented for operators, not run by Step 0.5b. `PRQ-E-06`,
> `PRQ-E-10` and `PRQ-C-02` no longer name the retired stack (PRQ-C-02's pins live in `modules/openai.bicep`).
>
> **v3 addendum (2026-10-01, `customer-provisioning-orchestration-r1` T245c)**: `PRQ-C-08` **added** — the
> Exchange mail-enabled security group that scopes H14a's Exchange mailbox roles (RBAC for Applications since T251). The Exchange admin of the
> stamp's tenant creates it before the run (owner decision 2026-10-01: its membership is the customer's access
> decision, so L2 never creates it); its id is the required intake value `exchangePolicyScopeGroupId`.
>
> **v3 (2026-09-30, `customer-provisioning-orchestration-r1` T226)**: `PRQ-E-06` **retired** — it granted the L2
> identity key-reading roles on the shared source services for the H4-shared handler, which T226 retired
> (every customer secret now comes from the customer's own resources). `PRQ-E-13` is scoped
> `once_per_customer` (its check needs `{customerId}`; the skill already did not run it). `validate.ps1`
> accepts retired entries without a `check_recipe`, and the skill's Step 0.5b skips them.
>
> **v2 (2026-09-28, `unified-access-control-r2`)**: `PRQ-T-07` **retired** per D-12 + D-13 — there is no
> shared/multitenant BFF app registration in either deployment model; the BFF app registration is per
> customer and is created by H3 during the run. `PRQ-C-05` wording corrected. Applied to the YAML manifest
> in the same change.

---

## §11 Component Justification (per root CLAUDE.md §11 three-question template)

**Q1 — Existing:** What does this overlap with? [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](./SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) §2 (Prerequisites) already documents tooling, identity/access, and information-to-collect for human operators. Verified via grep: §2.1 (Tooling) + §2.2 (Identity+access) + §2.3 (Information) + §2.4 (Lead-time) + §2.5 (Naming collision check).

**Q2 — Extension:** Can I extend the existing instead? Partially. The deployment guide is prose narrative optimized for **human reading**. It intentionally does NOT enumerate every prereq with a machine-parseable programmatic check recipe — that would bloat the guide beyond usability. The `/provision-environment` skill's Step 0.5 (planned by task 203) needs a **codified list** it can iterate programmatically — grep-parsing markdown tables is brittle and drifts. So the codified list belongs in a sibling **YAML manifest** (this file's `.yaml` sibling) alongside human narrative in this markdown, mirroring the proven `scripts/canonical-secret-catalog/manifest.yaml` + `docs/…` pattern (task 084/FR-36).

**Q3 — Cost-of-doing-nothing:** Name a concrete behavior that fails without this file. Without codified prereqs, `/provision-environment` skill Step 0.5 cannot programmatically verify prerequisites before Step 2 preflight. Operators hit F1–F9-class errors mid-Bicep-deploy (e.g., `ServiceModelDeprecated`, `InsufficientQuota - gpt-5.x`, `RequestConflict - not terminal`) instead of Step 0.5 fail-fast. SESSION 2 fresh-sub Model 1 Prod standup demonstrated this directly — 20+ live failures caught only after resources partially provisioned. Cost of absence: measured in operator hours (SESSION 2 spent 2 full standup sessions iterating around prereqs); cost of authoring: this file (~200 lines) + YAML (~350 lines) + task 203 skill wiring.

**Decision:** authorize new file(s). Format chosen: **YAML source of truth + sibling markdown rendering**. YAML because per-prereq fields (id, scope, owner, check_recipe.cli, check_recipe.expect, remediation, related_findings) are ~11 columns × ~27 rows — a data payload, not document metadata. Frontmatter-in-markdown is optimized for document-level metadata, not row-of-data content. Sibling YAML matches the `scripts/canonical-secret-catalog/**` precedent (FR-36 §6.2 of the deployment guide).

---

## What counts as a "prerequisite"?

A **prerequisite** is a manual (human or one-time-scripted) step that must happen **outside** a provisioning run. If it can be automated inside a handler and run every time, it belongs in the handler catalog, not here. If it fails and blocks a run, its failure surfaces at Step 0.5 (fail-fast) rather than mid-Bicep-deploy or mid-Dataverse-import.

Prereqs are grouped by **scope**:

| Scope | Meaning | Frequency |
|---|---|---|
| `once_per_tenant` | Spaarke Model 1 tenant (or customer tenant Model 2) | Once, ever |
| `once_per_subscription` | Fresh Azure subscription | Once per sub |
| `once_per_env` | Spaarke platform env (`dev` / `prod`) | Once per env (drift → re-apply) |
| `once_per_customer` | Per new customer stamp | Verified at Step 2 preflight of each run |

**BINDING owner directive 2026-08-24**: "don't remove container types — creating new container types are one prerequisite outside the provisioning automation." SPE container-type entries (`PRQ-T-01`, `PRQ-T-02`) are `never_delete: true`. Provisioning MUST NOT recreate or delete them even on drift detection. See design.md §7.9 BINDING pre-check pattern.

---

## Summary — 42 prereqs across 3 scopes (35 active)

| Scope | Count | IDs |
|---|---|---|
| `once_per_tenant` | 0 active (+2 retired) | ~~`PRQ-T-06`~~ **retired 2026-10-09** (BI out of scope for r1); ~~`PRQ-T-07`~~ **retired 2026-09-28 per D-12 + D-13** (`PRQ-T-01` … `PRQ-T-05` are `once_per_env` since 2026-10-08) |
| `once_per_env` | 16 active (+2 retired) | `PRQ-T-01` … `PRQ-T-04` (rescoped 2026-10-08), `PRQ-E-01` … `PRQ-E-12` except `PRQ-E-05`, plus `PRQ-E-14` (T225b) and `PRQ-E-15` (T251); ~~`PRQ-T-05`~~ **retired 2026-10-09** (T257: no bot), ~~`PRQ-E-06`~~ **retired 2026-09-30 (T226)** |
| `once_per_customer` | 19 active (+3 retired) | `PRQ-S-00` … `PRQ-S-05` (T228: one subscription per customer), `PRQ-S-06` (T262: the subscription is in the `spaarke-customers` management group), `PRQ-C-01` … `PRQ-C-13` (~~`PRQ-C-04`~~, ~~`PRQ-C-05`~~, ~~`PRQ-C-07`~~ retired; C-10 … C-12 T232; C-13 T255), `PRQ-E-13` (id kept; scope corrected 2026-09-30), `PRQ-E-05` (id kept; scope corrected 2026-10-01, T225a) |
| **Total** | **42** (35 active) | Authoritative count: `validate.ps1` over the YAML |

### Prereqs the owner explicitly named (SESSION 5 verbatim directive)

Owner enumeration: "SPE container-types, Office add-in apps, Copilot bot apps, Power BI SPs, Azure sub, OpenAI TPM bumps, provider registration."

| Owner-named prereq | Codified as |
|---|---|
| SPE container-types (KEEP) | `PRQ-T-01`, `PRQ-T-02` |
| Office add-in Entra apps | `PRQ-T-03` (Outlook), `PRQ-T-04` (Word) |
| Copilot bot Entra apps | `PRQ-T-05` |
| Power BI service principals | `PRQ-T-06` |
| Azure subscription (EA/MCA) | `PRQ-S-01` (billing type), `PRQ-S-02` (support plan) |
| OpenAI TPM bumps for frontier models | `PRQ-C-01` (per-run headroom check) |
| Resource-provider registration on fresh subs | `PRQ-S-03` |

Additional prereqs surfaced during task 202 audit (from `lessons-learned-model1-prod-standup-2026-08-22.md` F1-F20 + `post-authoring-audit-2026-08-20.md` audit gaps + `r1-gap-analysis-2026-08-18.md` c-series gaps): 20 more entries covering the BFF app-reg (since **retired** as `PRQ-T-07` — see below), operator + L2 UAMI RBAC coverage, platform artifacts storage + ACR, Graph app-role grants, Path X Dataverse App User, per-run env quotas, Dataverse org-settings + required applications.

---

## Prereq reference — full table

Grouped by scope. Programmatic check recipes in the YAML.

### Once-per-tenant (0 active, 2 retired) — plus PRQ-T-01 … PRQ-T-05, which are `once_per_env` since 2026-10-08 and listed here for continuity

| ID | Prereq | Owner | Consequence of absence |
|---|---|---|---|
| PRQ-T-01 | SPE container-type registered | Spaarke platform admin | H8 fails 404; downstream file-storage broken. **never_delete: true** |
| PRQ-T-02 | SPE container-type application permissions granted | Customer tenant admin (M2) / Spaarke (M1) | H8 succeeds but subsequent 403 |
| PRQ-T-03 | Office Outlook add-in Entra app-reg | Spaarke platform admin | Outlook add-in deploy fails; email intake broken |
| PRQ-T-04 | Office Word add-in Entra app-reg | Spaarke platform admin | Word add-in deploy fails |
| ~~PRQ-T-05~~ | **RETIRED 2026-10-09** — was *Copilot bot Entra app-reg* (superseded by T257: one shared OAuth client app, no bot) | — | None |
| ~~PRQ-T-06~~ | **RETIRED 2026-10-09** — was *Power BI service principal* (BI is out of scope for r1) | — | None |
| ~~PRQ-T-07~~ | 🔴 **RETIRED 2026-09-28 (D-12 + D-13)** — was *"Multitenant BFF app-reg (Model 1 tier only)"*, `never_delete: true` | — | **None.** See the retirement note below. |

> **PRQ-T-07 retirement note (2026-09-28, `unified-access-control-r2`)**
>
> `PRQ-T-07` required a **shared multitenant BFF app registration** for a "Model 1 tier" and protected it
> with `never_delete: true`. **D-13 forbids exactly that artifact**: the BFF Entra app registration is **per
> customer in both models** — in Spaarke's tenant under Model 1, in the customer's own tenant under Model 2.
> `SharedBffAppRegistrationId` is a retired artifact. A shared app registration would put **one credential
> behind every customer's Dataverse application user**, and would hit the unraisable 20-federated-identity-
> credential cap at customer 21.
>
> The per-customer app registration is **created by provisioning automation (H3) on every run**, so by this
> guide's own definition it belongs in the handler catalog, not here — it is not re-homed to another prereq
> scope. The ID is retained in the manifest, marked `status: retired`, so references do not dangle.
>
> Machine-readable source updated in the same change: `scripts/provisioning-prereqs/prereqs.yaml`
> (manifest_version 2).

### The customer's subscription (7 — `once_per_customer` since T228)

| ID | Prereq | Owner | Consequence of absence |
|---|---|---|---|
| PRQ-S-00 | The customer's own subscription exists (operator creates it; its id is the intake value `subscriptionId`), Enabled, holding no other customer's `rg-spaarke-*` | Spaarke admin (Model 1) | POST /api/runs 400 `subscription-id-required`; H1 `subready-subscription-not-dedicated` / `-unreachable` |
| PRQ-S-01 | Azure subscription billing-agreement type | Spaarke admin | Cost-envelope estimation unreliable |
| PRQ-S-02 | Support Plan (Basic minimum) | Spaarke admin | F9 — auto-support-ticket path unavailable when quota bump denied |
| PRQ-S-03 | Resource-provider registration (H1 also registers them) | Spaarke admin | F6 — `az deployment sub create` fails on unregistered provider |
| PRQ-S-04 | L2 UAMI **Owner** on the customer's subscription (`modules/controlplane-subscription-rbac.bicep`; owner decision 2026-10-06) | Spaarke admin | H1 `subready-subscription-listing-failed`; H2a cannot write customer.bicep's role assignments |
| PRQ-S-05 | Operator has Owner OR Contributor+UAA on the customer's subscription | Sub owner | F15/F18 — operator KV data-plane bootstrap 403 |
| PRQ-S-06 | The customer's subscription is a direct member of the `spaarke-customers` management group (ADR-027 common customer policy; T262). Place it with `az account management-group subscription add --name spaarke-customers --subscription <id>` (the group itself comes from the one-time `scripts/provisioning/Deploy-ManagementGroups.ps1 -Apply`) | Spaarke admin (management-group write on `spaarke-customers` + Owner on the subscription) | Nothing in the run fails; the stamp does not inherit `customer-policy.bicep` (regions, resource-group tags, storage HTTPS + TLS 1.2, Key Vault RBAC + purge protection), so its drift is never reported |

### Once-per-env (11 active + 1 retired)

| ID | Prereq | Owner | Consequence of absence |
|---|---|---|---|
| PRQ-E-01 | Platform artifacts storage account | Spaarke admin (via IaC) | H2a + H9 publish workflows fail cleanly (empty account name) |
| PRQ-E-02 | Platform ACR (for L2 sidecar image) | Spaarke admin | Sidecar image cannot be pushed; H14a permanently blocked |
| PRQ-E-03 | L2 UAMI Storage Blob Data Reader on artifacts storage | Spaarke admin | H2a/H9 artifact download 403 |
| PRQ-E-04 | L2 UAMI AcrPull on platform ACR | Spaarke admin | Sidecar image pull fails at H14a dispatch |
| ~~PRQ-E-06~~ | 🔴 **RETIRED 2026-09-30 (T226)** — was *"L2 UAMI service-specific RBAC on 6 shared source services"* (for the H4-shared handler) | — | **None.** No handler reads a shared service's keys; H4b never depended on these roles. |
| PRQ-E-07 | L2 UAMI Graph app-role grants — `ControlPlaneGraphAppRoles.cs` (task 261), granted by `Grant-ControlPlaneIdentity.ps1` | Spaarke admin (script) | H3/H10/H11/H14b 403 on their Graph calls (H3 and H10 need `Application.ReadWrite.OwnedBy` / `AppRoleAssignment.ReadWrite.All`) |
| PRQ-E-08 | L2 UAMI Dataverse App User (Path X) on admin env | Spaarke admin | L2 cannot read/write `sprk_dataverseenvironment` registry rows |
| PRQ-E-09 | Platform KV secrets pre-seeded | Spaarke admin (script) | L2 config validation returns garbage strings (T1-family silent fail) |
| PRQ-E-10 | L2 UAMI KV Secrets User on platform + per-tenant KVs | Spaarke admin (Bicep) | F16 — `@Microsoft.KeyVault(...)` refs silently unresolvable |
| PRQ-E-11 | L2 UAMI SB Data Sender + Data Receiver | Spaarke admin (Bicep) | Dispatcher DOA — cannot enqueue or dequeue |
| PRQ-E-12 | Provisioning SB queue with sessions + dedup | Spaarke admin (Bicep + ceremony) | Session receiver throws on `StartProcessingAsync`; §4C retries lost |
| PRQ-E-15 | Exchange admin app `Spaarke Exchange Admin` (federated credential trusting the L2 Worker UAMI, `Exchange.ManageAsApp`) + narrowed Exchange role `Spaarke App RBAC Admin` (T251) | Spaarke admin — deployment guide §4.2.1 | H14a / H13 T4 fail on first use; the stamp's Mail.* calls 403 (T4) |
| PRQ-E-14 | Registry schema current on the admin env — v3.3 columns + `sprk_credentialmode` (T225b) + `sprk_bffappid` / `sprk_copilotauthconfigid` (T257) | Spaarke admin (`Extend-DataverseEnvironmentSchema-v3.3.ps1`, idempotent) | H4 fails `kvsecrets-secret-free-marker-apply-failed` after writing the vault; without `sprk_bffappid` H13's promoted-columns PATCH is rejected (registry stale) |

### Once-per-customer (9 active + 2 retired, besides the subscription entries above)

| ID | Prereq | Owner | Consequence of absence |
|---|---|---|---|
| PRQ-C-01 | OpenAI regional TPM headroom for the pinned deployments (gpt-4o 150, gpt4.1-mini 200, text-embedding-3-large 350 — DataZoneStandard) | Spaarke admin | `InsufficientQuota - <model> - DataZoneStandard` on a subscription without the grant |
| PRQ-C-02 | OpenAI model GA per region for pinned versions | Spaarke admin | `ServiceModelDeprecated` at H2a deploy |
| PRQ-C-03 | Global resource-name availability (SB / Cog Svc / Storage) | Spaarke admin | F10 — `NamespaceUnavailable` mid-deploy (~16m35s) |
| ~~PRQ-C-04~~ | **Retired 2026-10-06 (T228).** Dataverse environment-creation rate — L2 no longer creates environments (PRQ-C-09). | — | — |
| ~~PRQ-C-05~~ | **Retired 2026-10-06 (T227a).** Model 2 only (out of scope, D-12), and the customer's BFF app registration does not exist before the run (H3 creates it), so no pre-run check could hold its id. | — | — |
| PRQ-C-06 | Dataverse org-settings contract (`maxuploadfilesize ≥ 25MB`) — H6 applies it before the import (Dataverse Web API `organization` PATCH, T253) | Spaarke admin (via H6) | F14 — SpaarkeMaster import fails 5min in |
| ~~PRQ-C-07~~ | **Retired 2026-10-08 (T253).** Required applications (Power BI Extensions) — the CI-built SpaarkeMaster depends on no application a fresh environment lacks (`SpaarkeMasterApplicationDependencyTests`); H6 installs none. | — | — |
| PRQ-C-09 | The customer's Dataverse environment, created by the operator: domain `spaarke-{customerId}` (or `-{environmentName}`), URL = intake `dataverseEnvUrl`; the L2 Worker identity is its System Administrator application user (T228) | Spaarke admin (Power Platform admin) | POST /api/runs 400 `dataverse-env-url-invalid`; H5 `worker-not-app-user` / `env-health-check-failed` |
| PRQ-C-10 | The environment's security group `sprk-{customerId}-users` (Entra security group, assigned membership) exists and is set on the Dataverse environment; its object id is the intake value `environmentSecurityGroupId` (T232) | Spaarke admin (Entra + Power Platform admin) | POST /api/runs 400 `userprov-missing-security-group-id`; H11 `userprov-security-group-rejected`; **not set on the environment → every user of Spaarke's tenant (other customers' guests included) is admitted** |
| PRQ-C-11 | The environment is linked to a pay-as-you-go billing policy on the customer's stamp subscription (Spaarke pays guest access; no per-user licences — owner 2026-10-07, T232) | Spaarke admin (Power Platform admin) | Guests are refused at sign-in; L2 cannot detect it (billing is not visible to Dataverse) |
| PRQ-C-12 | Guest access allowed in the environment (`organization.restrictguestuseraccess = false`; new environments default to restricted) (T232) | Spaarke admin (System Administrator of the environment) | H11 `userprov-guest-access-restricted` before any invitation |
| PRQ-C-13 | The customer's workforce tenant id(s) — the tenant(s) its staff sign in from (Model 1: their HOME tenant, never Spaarke's) — intake `customerWorkforceTenantIds`, every run (T255, INCOMING-141) | Spaarke admin, with the customer's IT | POST /api/runs 400 `workforce-tenants-required` / `-invalid` / `-ciam-tenant` / `-spaarke-tenant`; a wrong but valid tenant → the stamp denies every customer employee's first sign-in (`workforce_tenant_not_customer`) |
| PRQ-C-08 | Exchange mail-enabled security group scoping the stamp identity's Exchange mailbox roles (direct members only) — its id is the intake value `exchangePolicyScopeGroupId` (T245c; RBAC for Applications since T251) | Exchange admin of the stamp's tenant | `POST /api/runs` 400 `h14a-missing-policy-scope-group-id`; a wrong id → H14a fails, Mail.* calls 403 (T4) |
| PRQ-E-05 | L2 UAMI Website Contributor on the customer stamp's BFF App Service (id kept; scope corrected to `once_per_customer` 2026-10-01, T225a — an H2a postcondition: `customer.bicep` emits it — **Model 1** stamps only since T249; a Model 2 stamp is reached through Lighthouse) | Spaarke admin (Bicep) | H4b Kudu docker-log fetcher degraded to generic diagnostic |
| PRQ-E-13 | `sprk_dataverseenvironment` placeholder record with `sprk_environmentid` (id kept; scope corrected to `once_per_customer` 2026-09-30) | Operator (skill Step 1) | L2 `POST /api/runs` returns 400 |

---

## How `/provision-environment` skill will consume this (planned by task 203)

Task 203 will extend the skill's Step 0.5 (currently 0a-0f) with a new **External Prerequisites Verification** phase that iterates `prereqs.yaml` and runs each `check_recipe.cli` against expected output. Failures HARD STOP with actionable remediation (the `remediation` field). Per-run archival: `provisioning-runs/{customerId}-{runId}/prerequisites-check.md` (see `notes/provisioning-run-structure-design.md`).

**Skill contract**:
- Step 0.5 iterates prereqs filtered by `scope <= run's tier` (e.g. `once_per_customer` for every run; `once_per_env` only if operator flags `--verify-env-prereqs`).
- Each recipe timeout: 60s default; longer via per-recipe field (future extension).
- HARD STOP: any `once_per_customer` failure. WARN: `once_per_env` failure (assumes admin has recently verified).
- Output: table `{id, name, status: OK/FAIL, output, remediation-if-failed}` archived to `prerequisites-check.md`.

## Extending this file

New prereqs added by future projects/waves:
1. Add YAML entry to `scripts/provisioning-prereqs/prereqs.yaml` under appropriate `scope` group; increment `manifest_version`.
2. Add row to the appropriate scope table above.
3. If prereq requires code (e.g. new script), file as task in current owning project (customer-provisioning-orchestration-r1 or successor).
4. Cross-ref lesson ID(s) in `related_findings` for traceability.

Deprecating a prereq: mark with `deprecated: true` in YAML; do NOT delete from history (audit trail).

## See also

- [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](./SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — human operator narrative
- [`.claude/skills/provision-environment/SKILL.md`](../../.claude/skills/provision-environment/SKILL.md) — the operator skill that consumes this file (task 203 wires Step 0.5)
- [`projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md`](../../projects/customer-provisioning-orchestration-r1/notes/task-202-punch-list.md) — full unapplied-lesson inventory that shaped this file
- [`scripts/canonical-secret-catalog/manifest.yaml`](../../scripts/canonical-secret-catalog/manifest.yaml) — sibling manifest (task 084 / FR-36); this file mirrors its shape
