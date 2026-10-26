# SDAP Development Scripts

**Purpose:** Utility scripts for SDAP development, deployment, and operations.

**Last Updated:** April 4, 2026

---

## Script Registry

This registry tracks all scripts in this directory, their purpose, usage frequency, and lifecycle status.

### Legend

**Usage Frequency:**
- 🟢 **Active** - Used regularly (weekly/monthly)
- 🟡 **Occasional** - Used as needed (quarterly/annually)
- 🔴 **One-time** - Historical/setup scripts (rarely used)

**Lifecycle Status:**
- ✅ **Maintained** - Actively maintained and updated
- ⚠️ **Deprecated** - Superseded by better tooling, kept for reference
- 📦 **Archive** - One-time setup scripts, kept for new environments

---

## AI Playbook & Scope Provisioning

### `dataverse/Seed-PlaybookConsumers.ps1`
**Purpose:** Seed / export / round-trip-verify the `sprk_playbookconsumer` **Binding table** — the single AI routing surface (ADR-039). Data-driven: rows live in the mirror file `infra/dataverse/sprk_playbookconsumer-rows.json` (all FR-P0-03 extended columns: ucid, disposition, risk, captureMode, surfaces, chipTransitions, onEventBindings, matchConditions, toolDescription); the script is a faithful projection tool of the live table.
**Usage:** 🟡 Occasional - `-DiffOnly` any time (read-only round-trip proof); `-Export` after deliberate live catalog edits; Seed mode when standing up a new environment.
**Lifecycle:** ✅ Maintained — REGENERATED 2026-07-07 by ai-architecture-redesign-r1 task 051 (FR-P4-02). The 2026-06-24 chat-routing-redesign-r1 version (7 playbook-only rows, hard-coded GUIDs, no extended columns) is superseded.
**Dependencies:** Azure CLI (`az login`); alternate key `sprk_ConsumerTypeCodeEnvironment`; target Action rows (`sprk_actioncode`) + playbooks (`sprk_name`) must exist in the target environment — lookups are resolved per environment at seed time (no GUIDs in the mirror).
**Owner:** AI Team

**Round-trip contract (FR-P4-02)**: `-Export` then `-DiffOnly` is always clean; Seed into a clean environment followed by `-DiffOnly` reproduces the mirror exactly; Seed against an already-matching environment converges as a no-op. `sprk_environment` is always a literal (`'*'` = all envs, never null — null can't be addressed by the alternate key).

**Example:**
```powershell
# Round-trip proof (read-only; exit 1 on drift)
.\scripts\dataverse\Seed-PlaybookConsumers.ps1 -DiffOnly

# Refresh the mirror from the live table after deliberate catalog edits
.\scripts\dataverse\Seed-PlaybookConsumers.ps1 -Export

# Seed a new environment from the mirror
.\scripts\dataverse\Seed-PlaybookConsumers.ps1 -DataverseUrl "https://spaarketest1.crm.dynamics.com" -SkipConfirm
```

**Related**: `projects/spaarke-ai-architecture-redesign-r1/notes/catalog-governance.md` (regeneration evidence + reconciliation table); ADR-039; `docs/data-model/sprk-playbookconsumer.md`.

---

### `Deploy-Playbook.ps1`
**Purpose:** Create complete AI analysis playbooks in Dataverse from definition JSON files — resolves scope codes, creates playbook + nodes, associates N:N scopes, saves canvas layout
**Usage:** 🟢 Active - Create new playbooks on-demand
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Dataverse connection
**Owner:** AI Team
**Last Used:** June 2026 (Insights Engine r2 Wave B)

**Lint behavior (Wave B B3 — Insights Engine r2 D-01 path-b)**: Every node in the playbook definition MUST have an `actionCode` field referencing a `sprk_analysisaction` row. The script lints this immediately after JSON load (before any Dataverse writes) and FAILS the deploy with an explicit error listing nodes that lack action-code wiring. This prevents the regression mode described in `projects/ai-spaarke-insights-engine-r2/decisions/D-01-wave-b-root-cause-corrected.md` §2.4 (deployed nodes with no `sprk_actionid` FK → orchestrator falls into the canvas-Designer-clobbered configjson path → dispatch defaults to AiAnalysis(0) → "Node X in batch 1 failed" → defensive scaffold decline).

**When to Use:**
- After designing a playbook with `/jps-playbook-design` skill
- Deploying new playbook definitions from `projects/*/notes/playbook-definitions/`
- Recreating playbooks in new environments

**Command:**
```powershell
# Preview without creating (recommended first)
.\Deploy-Playbook.ps1 -DefinitionFile "path/to/my-playbook.json" -DryRun

# Deploy for real
.\Deploy-Playbook.ps1 -DefinitionFile "path/to/my-playbook.json"

# Overwrite existing playbook
.\Deploy-Playbook.ps1 -DefinitionFile "path/to/my-playbook.json" -Force
```

**Relationship to seed-data/:**
- `scripts/seed-data/` bootstraps base primitives (actions, skills, knowledge, tools) — run once per environment
- `Deploy-Playbook.ps1` creates NEW playbooks from definition files using those existing primitives
- Both use the same Dataverse entities (`sprk_analysisplaybooks`, `sprk_playbooknodes`, etc.)

---

### `Setup-InsightsEngineSchema.ps1`
**Purpose:** Idempotent setup for Insights Engine Dataverse schema + lookup-target dispatch rows. Creates `sprk_executoractiontype` (Whole Number) on `sprk_analysisactiontype` if missing, backfills existing rows to 0 (AiAnalysis), and seeds the 7 Insights ActionType lookup rows (60 - Agent Service, 70 - Grounding Verify, 80 - Live Fact Resolver, 90 - Index Retrieve, 100 - Evidence Sufficiency, 110 - Decline to Find, 120 - Return Insight Artifact).
**Usage:** 🟢 Active - Run on a new Dataverse environment after `git pull` to bring it up to Wave B baseline
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Dataverse connection
**Owner:** AI Team / Insights Engine
**Last Used:** June 2026 (Insights Engine r2 Wave B post-merge protection)

**When to Use:**
- New developer's first-time Dataverse env setup
- After merging an Insights Engine PR that depends on the lookup-target dispatch architecture
- Spinning up a new Spaarke environment (test, demo, customer-prod)
- Re-running after a Dataverse env reset / restore

**Why this exists** (per amended ADR-027 2026-06-02): Spaarke uses unmanaged solutions everywhere — there is no managed-solution promotion path. Schema changes that need to travel between environments use either (a) unmanaged solution export, or (b) idempotent setup scripts like this one. This script implements (b) for the Insights Engine schema dependencies introduced by r2 Wave B.

**Command:**
```powershell
# Preview without modifying (recommended first)
.\scripts\Setup-InsightsEngineSchema.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com" -DryRun

# Apply
.\scripts\Setup-InsightsEngineSchema.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"
```

**What is NOT included:**
- The 7 INS-* `sprk_analysisaction` rows (with JPS prompt content) — see `projects/ai-spaarke-insights-engine-r2/notes/drafts/wave-b-action-codes.md`
- The predict-matter-cost@v1 playbook deploy — use `Deploy-Playbook.ps1 -Force`

---

### `Create-AiPersonaEntity.ps1`
**Purpose:** Idempotent deployment of the `sprk_aipersona` Dataverse entity — R6 Pillar 1's 5th scope library entity. Creates entity + 6 attributes (`sprk_name`, `sprk_personacode`, `sprk_description`, `sprk_systemprompt`, `sprk_scopetype` picklist, `sprk_tags`, `sprk_availableadhoc`) + self-lookup relationship (`sprk_aipersona_parentpersona` adding `sprk_parentpersonaid`) for most-specific-wins inheritance per R6 Q1. Mirrors the canonical 4-scope schema pattern (`sprk_analysisaction`/skill/knowledge/tool) verbatim. SYS-/CUST- prefix enforcement is API-side via existing `OwnershipValidator.cs` (NOT entity-level — matches the canonical Spaarke scope pattern).
**Usage:** 🟡 Occasional — One-time per environment (R6 Phase A); idempotent re-runs safe
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Dataverse connection, PowerShell 7+
**Owner:** AI Team / R6 spaarke-ai-platform-unification-r6
**Last Used:** June 2026 (R6 task 001 — D-A-01)

**When to Use:**
- Bootstrapping a new Dataverse environment for R6 Pillar 1
- Re-running after a Dataverse environment reset / restore
- Verifying schema after a manual change

**Command:**
```powershell
# Preview without modifying (recommended first run)
.\scripts\Create-AiPersonaEntity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -DryRun

# Deploy (idempotent)
.\scripts\Create-AiPersonaEntity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"
```

**Pattern source:** `scripts/Deploy-ChartDefinitionEntity.ps1` (canonical exemplar per `dataverse-create-schema` SKILL.md) + `docs/architecture/scope-architecture.md` (scope schema canonical reference).

**Related R6 tasks:**
- Task 002 — `GET /api/ai/scopes/personas` endpoint (consumes this entity)
- Task 003 — Persona resolver methods in `IScopeResolverService`
- Task 004 — Seed default SYS- persona row (separate script — task-004-specific)
- Task 005 — Wire `SprkChatAgentFactory.CreateAgentAsync` to scope persona

---

### `Refresh-ScopeModelIndex.ps1`
**Purpose:** Regenerate `.claude/catalogs/scope-model-index.json` from current Dataverse state — keeps the scope catalog in sync for Claude Code
**Usage:** 🟡 Occasional - After adding new scopes to Dataverse
**Lifecycle:** ✅ Maintained — REPAIRED 2026-07-07 by ai-architecture-redesign-r1 task 051 (FR-P4-02): the knowledge query 400-failed because `$select` still referenced `sprk_externalid`, which was removed from `sprk_analysisknowledge` (code column is now `sprk_knowledgecode`). Entries now also carry the deployed row GUID (`id`), Action `kind`/`modelTier`, and Tool `toolId`/`sideEffectClass` per the post-redesign closed-catalog taxonomy (ADR-039).
**Dependencies:** Azure CLI (`az login`), Dataverse connection (`-DataverseUrl` or `DATAVERSE_URL`)
**Owner:** AI Team
**Last Used:** July 2026 (task 051 catalog regeneration)

**When to Use:**
- After seeding new actions, skills, knowledge, or tools to Dataverse
- After manually creating scopes in Dataverse UI
- To ensure Claude Code has the latest scope catalog for playbook design

**Command:**
```powershell
.\Refresh-ScopeModelIndex.ps1 -DataverseUrl "https://spaarkedev1.crm.dynamics.com"
```

---

### `Seed-JpsActions.ps1` — ⚠️ RETIRED (2026-07-07)
**Retired by:** ai-architecture-redesign-r1 task 051 (FR-P4-02 catalog governance; deletion deferred from Track-B batch 4 / task 073).
**Why:** Unrunnable — both JPS source directories it read (`projects/ai-json-prompt-schema-system/notes/jps-conversions/`, `projects/jps-server-rollout/notes/jps-conversions/`) no longer exist in the repo. Its job (bulk-refresh `sprk_systemprompt` on the legacy ACT-0xx engine Actions from repo JPS files) belongs to the FROZEN engine; those rows are live in Dataverse and hot-editable there.
**Replacement:** New/changed chat Actions are authored via the `jps-action-create` skill (writes the row directly; JPS artifact mirrored under the owning project's `notes/jps/`; input schemas authored mirror-first at `infra/dataverse/inputschemas/` with CI validation per `CatalogInputSchemaContractTests`). Binding rows round-trip via `dataverse/Seed-PlaybookConsumers.ps1`.

---

### `Seed-AnalysisSkills.ps1`
**Purpose:** Seed enriched analysis skill prompt fragments to Dataverse — updates `sprk_analysisskills` records with structured prompt content
**Usage:** 🟡 Occasional - After updating skill definitions
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI, Dataverse connection
**Owner:** AI Team
**Last Used:** March 2026

**Command:**
```powershell
.\Seed-AnalysisSkills.ps1
```

---

### `Seed-KnowledgeScopes.ps1`
**Purpose:** Seed knowledge source records to Dataverse — creates/updates `sprk_analysisknowledges` with reference content
**Usage:** 🟡 Occasional - After creating new knowledge sources
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI, Dataverse connection
**Owner:** AI Team
**Last Used:** March 2026

**Command:**
```powershell
.\Seed-KnowledgeScopes.ps1
```

---

## Demo & Sample Data Scripts

### `Load-DemoSampleData.ps1`
**Purpose:** Load non-confidential sample data into a Spaarke demo environment — contacts, matters, projects, events, documents, chart definitions, and AI seed data. Orchestrates all data loading steps including optional SPE document upload and AI Search indexing.
**Usage:** 🟡 Occasional - Demo environment setup or refresh
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Dataverse access, Spaarke managed solutions imported
**Owner:** DevOps Team
**Last Used:** March 2026

**When to Use:**
- After provisioning a new demo environment (task 024)
- After importing managed solutions into Dataverse
- Refreshing demo data after a reset
- Setting up demo environments for sales presentations

**Command:**
```powershell
# -EnvironmentUrl is required: the old demo environment was retired 2026-10-14 and the demo is now the
# ACME stamp built by the provisioning control plane (no static default URL).

# Preview what would be created (recommended first)
.\Load-DemoSampleData.ps1 -EnvironmentUrl "https://myenv.crm.dynamics.com" -DryRun

# Load all sample data
.\Load-DemoSampleData.ps1 -EnvironmentUrl "https://myenv.crm.dynamics.com"

# Skip optional steps
.\Load-DemoSampleData.ps1 -EnvironmentUrl "https://myenv.crm.dynamics.com" -SkipAiSeedData -SkipSpeUpload -SkipAiIndexing

# Force recreate existing records
.\Load-DemoSampleData.ps1 -EnvironmentUrl "https://myenv.crm.dynamics.com" -Force
```

**Data Sources:**
- `scripts/demo-data/demo-records.json` — Record definitions (contacts, matters, projects, events, documents, charts)
- `scripts/demo-data/sample-documents/` — 8 sample text documents for SPE upload
- `scripts/seed-data/` — AI seed data (actions, tools, skills, knowledge, playbooks)

**Features:**
- Idempotent: Existing records skipped (matched by name)
- Graceful degradation: If solutions not imported, creates only contacts (standard entity)
- Modular: Each data type can be skipped independently
- DryRun mode for safe preview

---

## Customer Lifecycle Scripts

### `Decommission-Customer.ps1`
**Purpose:** Safely decommission a customer by removing all per-customer resources — BFF tenant registration, SPE containers, Dataverse environment, Azure resource group (Storage, Key Vault, Service Bus, Redis)
**Usage:** 🟡 Occasional - Customer offboarding or test environment teardown
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), PAC CLI (`pac auth`), Contributor role on customer resource group
**Owner:** DevOps Team
**Last Used:** March 2026

**When to Use:**
- Offboarding a customer
- Cleaning up test/demo environments after validation (task 045)
- Tearing down resources after provisioning failure (partial cleanup)

**Command:**
```powershell
# Preview what would be deleted (recommended first)
.\Decommission-Customer.ps1 -CustomerId demo -DryRun

# Decommission with confirmation prompt
.\Decommission-Customer.ps1 -CustomerId demo

# Decommission without prompts (CI/CD)
.\Decommission-Customer.ps1 -CustomerId demo -Force

# Azure resources only (skip Dataverse and SPE)
.\Decommission-Customer.ps1 -CustomerId testcust -SkipDataverse -SkipSpe
```

**Safety Features:**
- DryRun mode lists resources without deleting
- Confirmation prompt requires typing "YES" (unless -Force)
- Platform resource groups are explicitly blocked
- Resource group name must match `rg-spaarke-{id}-{env}` pattern
- Key Vault soft-delete purge to prevent name collisions

---

### `Set-AiSpendLimit.ps1`
**Purpose:** Add, change or remove a customer stamp's OPTIONAL monthly Azure OpenAI spend limit (`AiSpendLimit__MonthlyLimitUsd`) on the BFF's production AND staging slots — customer-provisioning-orchestration-r1 task 254, owner G37 (no limit is the default)
**Usage:** 🟡 Occasional - when a customer's agreement calls for a cap, or to lift one
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login` as the operator); write access to the stamp's App Service
**Owner:** DevOps Team
**Last Used:** October 2026 (new)

**Command:**
```powershell
# Set or change (idempotent — writes only a slot whose value differs)
.\Set-AiSpendLimit.ps1 -SubscriptionId <customer sub> -ResourceGroupName rg-spaarke-acme-prod -AppServiceName spaarke-bff-acme-prod -MonthlyLimitUsd 500

# Remove (back to no limit); -WhatIf previews either
.\Set-AiSpendLimit.ps1 -SubscriptionId <customer sub> -ResourceGroupName rg-spaarke-acme-prod -AppServiceName spaarke-bff-acme-prod -Remove -WhatIf
```

**Notes:** same value rule as intake (`OpenAiMonthlyLimitRule`: plain decimal in (0, 1,000,000]); `--subscription` on every az call; an app-setting change restarts the site. Behaviour of the limit: `docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` §3.2b. Tests: `tests/scripts/Set-AiSpendLimit.Tests.ps1` (Pester 3.4).

---

## Entra ID & Identity Scripts

### `Register-EntraAppRegistrations.ps1`
**Purpose:** Create the production Entra ID app registration (BFF API), store credentials in Key Vault, and create/verify managed-identity federated credentials (MI-FIC)
**Usage:** 🔴 One-time - Production environment setup
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Entra ID admin permissions, Key Vault access
**Owner:** DevOps Team
**Last Used:** August 2026 (FIC extension, task 030)

**When to Use:**
- Setting up production Entra ID app registrations
- Creating a managed-identity federated credential for a new (app-registration, UAMI) pair
- Recreating registrations in a new tenant
- After tenant migration

**Command:**
```powershell
# Preview what will be created
.\Register-EntraAppRegistrations.ps1 -DryRun

# Create the BFF API registration
.\Register-EntraAppRegistrations.ps1
```

**Creates:**
- `spaarke-bff-api-prod` — BFF API with Graph + Dynamics CRM delegated permissions (this app registration is also the single Dataverse Application User)
- Key Vault secrets: TenantId, BFF-API-ClientId, BFF-API-Audience — **and, unless you pass `-SkipClientSecret`, a 24-month `BFF-API-ClientSecret`**

**SPE topology mode** (added 2026-08-30, task 213.4; trimmed by task 227a — creates the Model 1 container-type OWNING app-reg per [SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md §3A](../docs/architecture/SPAARKE-SPE-CONTAINER-TYPE-TOPOLOGY.md#3A)). Per-customer BFF app-regs are created by H3 (`spaarke-bff-api-{customerId}`) and granted container-type access by H8 — there is no shared-tier BFF app (D-12/D-13):

```powershell
# ONE-TIME operator setup (NOT per-customer). Follow the 8-step runbook:
#   docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md

# Owning app-reg (permanent 1:1 with the "Spaarke Model 1" container type)
.\Register-EntraAppRegistrations.ps1 -TenantId $env:AZURE_TENANT_ID -CreateOwningApp Model1
```

Topology-mode behavior (idempotent; safe to re-run):
- The owning app is single-tenant (`AzureADMyOrg`).
- **NO client secret minted** on any topology app-reg (ADR-028 A4 + KV credential-lifecycle rule 1).
- **NO Key Vault writes** (H4 handler owns per-customer KV wiring at provisioning time).
- Not combinable with `-CreateFederatedCredential` / `-FicOnly` / `-AllowClientSecretMint` (throws with actionable message).
- Bypasses the default `spaarke-bff-api-prod` flow (implicit `-SkipBffApi`) + Key Vault pre-flight.

> 🔴 **Pass `-SkipClientSecret` for any new registration (2026-08-24, `spaarke-auth-v4-dataverse-MI` task 033).**
> The BFF identity is **secret-free** per ADR-028 **A4**: it authenticates as a confidential client using a
> federated credential issued to its user-assigned managed identity. `BFF-API-ClientSecret` and its lowercase
> duplicate were deleted from Key Vault on 2026-08-24.
>
> Without the switch, COMBINED mode (`-CreateFederatedCredential` **without** `-FicOnly`) still mints a client
> secret unconditionally — which would re-introduce a per-customer secret on every onboarding. ADR-028 exception
> **E-3** is transitional and explicitly *does not license expansion*. The switch is **opt-in, not the default**,
> because flipping the default would silently change behaviour for every existing caller of an identity-provisioning
> script (notably `customer-provisioning-orchestration-r1` Wave G-3).
>
> Provision the credential instead:
> `-CreateFederatedCredential -UamiResourceId <resourceId>` — subject is the UAMI's **principalId**, *not* its
> clientId (the commonest silent failure), audience exactly `api://AzureADTokenExchange`. Then set
> `Graph__Credentials__Order__0=ManagedIdentityFederated` and `Graph__Credentials__RequireSecretFreeIdentity=true`.

> **Note (2026-08-14, code-quality-and-assurance-r3 task 060):** the separate
> `spaarke-dataverse-s2s-*` app registration + its `Dataverse-S2S-*` Key Vault secrets
> were removed. They had zero code consumers; Dataverse server-to-server access
> consolidated onto the BFF app registration credential (`API_CLIENT_SECRET`) on 2026-01-07.

**Also creates federated identity credentials (MI-FIC)** — added 2026-08-21 by
`spaarke-auth-v4-dataverse-MI` task 030 (spec FR-C4). This is the repo's **only** FIC automation;
before it, every federated credential in the tenant was created by hand.

```powershell
# Create/verify a FIC only (the entry point customer provisioning invokes)
.\Register-EntraAppRegistrations.ps1 -FicOnly `
  -FederatedCredentialAppId <app-reg-id> `
  -UamiResourceId "/subscriptions/<sub>/resourceGroups/<rg>/providers/Microsoft.ManagedIdentity/userAssignedIdentities/<uami>"

# Preview without changing anything
.\Register-EntraAppRegistrations.ps1 -DryRun -CreateFederatedCredential `
  -FederatedCredentialAppId <app-reg-id> -UamiResourceId <arm-id>
```

> **Integrate by INVOKING it, never by dot-sourcing it.** A dot-source runs the script's `param()`
> block in *your* scope, silently replacing your `$TenantId` with this script's production default —
> and a wrong tenant means a wrong FIC issuer, i.e. a credential that creates cleanly and never
> works. `-FicOnly` runs in its own child scope and leaks nothing.

- **Idempotent on the `(issuer, subject, audience)` triple, not the name** — a credential that already
  carries the required triple under a different name is a no-op, not a duplicate or an error.
- **Verified by a real token exchange**, never by "create returned success" — a misconfigured FIC
  creates cleanly and fails only at exchange.
- **Exit codes**: `0` verified · `1` fault · `2` structurally correct but not exchange-provable from
  this host (a workstation cannot mint a managed-identity assertion — pass `-AssertionToken`, or
  `-AllowUnverified` to accept it).
- **Cross-tenant pairs are refused**, not attempted: Entra requires the app registration and the UAMI
  in the same tenant, and a cross-tenant FIC creates successfully then fails silently.
- Behaviour without the new flags is unchanged (verified byte-identical).

> ⚠️ **The client-secret path above is transitional.** It exists under ADR-028 exception **E-3** and is
> scheduled for removal by spec FR-C3 / task 033 of `spaarke-auth-v4-dataverse-MI`. ADR-028 **A4** ranks
> secrets last ("development and testing only"). **New credentials should use MI-FIC**, not the secret.
> The two capabilities above are not co-equal — one is the target state, the other is being retired.

Rationale + verification evidence: [`projects/spaarke-auth-v4-dataverse-MI/notes/decisions/030-fic-automation.md`](../projects/spaarke-auth-v4-dataverse-MI/notes/decisions/030-fic-automation.md)

---

### `Test-EntraAppRegistrations.ps1`
**Purpose:** Verify production Entra ID app registrations — checks app existence, permissions, naming convention, Key Vault secrets, and token acquisition
**Usage:** 🟡 Occasional - After registration or rotation
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Key Vault access
**Owner:** DevOps Team
**Last Used:** March 2026

**When to Use:**
- After running `Register-EntraAppRegistrations.ps1`
- After secret rotation
- Troubleshooting authentication failures in production

**Command:**
```powershell
# Test using Key Vault secrets
.\Test-EntraAppRegistrations.ps1

# Test with explicit credentials (only for a NON-migrated environment — see note)
.\Test-EntraAppRegistrations.ps1 -BffApiClientId "abc123" -BffApiClientSecret "secret"
```

> **Note (2026-08-24, task 033):** the client-credentials token test now reports **SKIPPED** against a migrated
> environment, and that is the correct result — not a missing credential. The BFF identity is secret-free, and a
> managed-identity federated credential cannot be exercised from a workstation (no route to IMDS). To verify the
> live credential, look for `Ordered credential selection active: ManagedIdentityFederated.` in the app's startup
> logs. The suite also now asserts the **inverse**: `BFF-API-ClientSecret` must stay **absent** from Key Vault —
> a re-appearance means a provisioning run without `-SkipClientSecret`.

---

### `Invite-DemoUsers.ps1`
**Purpose:** Configure B2B guest access for demo users — sends Entra ID B2B invitations, guides Dataverse security role assignment, verifies user access end-to-end
**Usage:** 🟡 Occasional - Demo user onboarding, new demo accounts
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Graph API permissions (User.Invite.All), PAC CLI (optional)
**Owner:** DevOps Team
**Last Used:** March 2026

**When to Use:**
- Onboarding new demo users for sales presentations
- Re-inviting users whose invitations expired
- Verifying existing demo user access (`-VerifyOnly`)

**Command:**
```powershell
# Full flow: invite users, assign roles, verify
.\Invite-DemoUsers.ps1

# Preview only (no changes)
.\Invite-DemoUsers.ps1 -WhatIf

# Verify existing access only
.\Invite-DemoUsers.ps1 -VerifyOnly

# Skip invitations (already accepted), just verify roles
.\Invite-DemoUsers.ps1 -SkipInvitations
```

**Configuration:**
- Users defined in `demo-users.json` (same directory)
- Add/remove users by editing the JSON file and re-running

---

## Custom Domain & SSL Scripts

### `Configure-CustomDomain.ps1`
**Purpose:** Configure custom domain (api.spaarke.com), Azure-managed SSL certificate, HTTPS enforcement, and CORS on the production App Service
**Usage:** 🔴 One-time - Production domain setup
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Contributor on resource group, DNS records pre-configured
**Owner:** DevOps Team
**Last Used:** March 2026

**When to Use:**
- Initial production custom domain setup
- Adding custom domain to a new App Service
- Reconfiguring SSL or CORS settings

**Command:**
```powershell
# Show DNS instructions first
.\Configure-CustomDomain.ps1 -ShowDnsInstructions

# Preview what will be configured
.\Configure-CustomDomain.ps1 -DryRun

# Full configuration
.\Configure-CustomDomain.ps1

# Skip DNS check if propagation is slow
.\Configure-CustomDomain.ps1 -SkipDnsCheck
```

---

### `Test-CustomDomain.ps1`
**Purpose:** Verify custom domain, SSL certificate, HTTPS enforcement, and CORS configuration — comprehensive post-setup validation
**Usage:** 🟡 Occasional - After domain setup or troubleshooting
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), network access to custom domain
**Owner:** DevOps Team
**Last Used:** March 2026

**When to Use:**
- After running `Configure-CustomDomain.ps1`
- Troubleshooting domain or SSL issues
- Periodic SSL certificate health checks

**Command:**
```powershell
# Run full verification
.\Test-CustomDomain.ps1

# Test with specific CORS origin
.\Test-CustomDomain.ps1 -TestCorsOrigin "https://my-org.crm.dynamics.com"
```

**Tests:**
- DNS resolution (CNAME and A records)
- App Service hostname binding and SSL state
- HTTPS-only enforcement
- SSL certificate validity and expiry
- HTTPS connectivity
- HTTP to HTTPS redirect
- CORS configuration

---

## Operations & Security Scripts

### `Rotate-Secrets.ps1`
**Purpose:** Rotate secrets in Azure Key Vault for platform and customer vaults — handles Storage keys, Service Bus keys, Redis keys, and Entra ID client secrets with zero-downtime rotation
**Usage:** 🟡 Occasional - Quarterly secret rotation or on-demand
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Key Vault Secrets Officer role, Contributor on target resources
**Owner:** DevOps Team
**Last Used:** March 2026

**When to Use:**
- Scheduled quarterly secret rotation
- After a security incident requiring immediate credential rotation
- When onboarding/offboarding team members with secret access
- Compliance audits requiring rotation evidence

**Command:**
```powershell
# Preview what would be rotated (recommended first)
.\Rotate-Secrets.ps1 -Scope Platform -SecretType All -DryRun

# Rotate all platform secrets
.\Rotate-Secrets.ps1 -Scope Platform -SecretType All

# Rotate customer storage keys
.\Rotate-Secrets.ps1 -Scope Customer -CustomerId demo -SecretType StorageKey

# Rotate everything (platform + all customers)
.\Rotate-Secrets.ps1 -Scope All -SecretType All -Force
```

**Supported Secret Types:**
- `StorageKey` — Storage account key regeneration (customer-level)
- `ServiceBus` — Service Bus access key regeneration
- `Redis` — Redis cache access key regeneration
- `EntraId` — Entra ID app registration client secret rotation (platform-level)
- `All` — All of the above

**Audit:** Produces timestamped JSON audit log in `scripts/logs/secret-rotation-*.log`

---

## Release & Deployment Orchestration

### `Import-SolutionScoped.ps1` and `lib/Publish-SolutionComponents.ps1`
**Purpose:** The ONE way to import a Dataverse solution (task 130, owner decision D-83). Imports without publishing, then POSTs a single `PublishXml` for exactly the solution's components (entities, web resources, option sets, site maps, dashboards, app modules; a PCF control through its `cc_` bundle web resources) and reads web resources and app modules back. `-PlanOnly` is read-only. Replaces every tenant-wide publish (the patterns are listed in the Pester test); `tests/scripts/Publish-SolutionComponents.Tests.ps1` and the `scoped-publish-lint` workflow fail if one comes back. Entity publishes are entity-wide (all pending views and forms of that entity); D-103: the script STOPS before the import or publish when that would publish someone else's pending change, unless `-AllowPendingCollateral` is given.
**Usage:** 🟢 Active - every Dataverse solution import
**Lifecycle:** ✅ Maintained
**Dependencies:** PowerShell 7, pac CLI, `az login`
**Owner:** spaarke-ontology-platform-r1 (task 130)
**Last Used:** October 2026 (read-only plan against spaarkedev1)

### `Package-OfficeAddinUnified.ps1`
**Purpose:** Zips the combined Outlook + Word Spaarke add-in — ONE Microsoft 365 unified-manifest app (schema 1.30) — into the two packages the admin center accepts: `spaarke-addin-<ver>.zip` (production; hides the live XML add-ins on clients that can run it) and `spaarke-addin-<ver>-TEST.zip` (own id, "(TEST)" name, hides nothing). Reads the parts webpack's `SpaarkeUnifiedPackagePlugin` emits into `src/client/office-addins/dist/spaarke/`; writes OUTSIDE `dist/` so the public site does not serve them. **Fails (exit 1)** if the build output is missing or the icons are not 192×192 (color) / 32×32 (outline).
**Usage:** 🟢 Active - every add-in release (run by `deploy-office-addins.yml`, which uploads the zips as the artifact `spaarke-addin-unified-package`)
**Lifecycle:** ✅ Maintained
**Dependencies:** PowerShell 7 (`pwsh`), a prior `npm run build` in `src/client/office-addins`
**Owner:** spaarkeai-word-add-in-r1 (task 078)
**Last Used:** September 2026

**When to Use:**
- Producing the admin-center upload for the unified Spaarke add-in (Integrated apps → Upload custom apps → App type "Teams app")
- Rollout and cutover steps: `projects/spaarkeai-word-add-in-r1/notes/078-manifest-decision.md` §6

**Command:**
```powershell
# After `npm run build` in src/client/office-addins:
.\scripts\Package-OfficeAddinUnified.ps1
```

### `Deploy-Release.ps1`
**Purpose:** Master release orchestrator — deploys the full Spaarke platform to one or more environments. Runs a 7-phase pipeline: pre-flight checks, client build, BFF API deployment, Dataverse solution import, web resource deployment, post-deploy validation, and git release tagging. Environments are processed sequentially; each phase delegates to existing deployment scripts.
**Usage:** 🟢 Active - Production and demo releases
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), PAC CLI (`pac auth`), Node.js + npm, `config/environments.json`, all sub-deployment scripts
**Owner:** DevOps Team
**Last Used:** April 2026

**When to Use:**
- Deploying a full release to production, demo, or staging environments
- Multi-environment sequential rollout (e.g., dev then demo then production)
- End-to-end release with automatic validation and git tagging

**Command:**
```powershell
# Full release pipeline to the dev environment with auto-suggested version tag
.\scripts\Deploy-Release.ps1 -EnvironmentUrl dev

# Deploy to dev then demo, tag as v2.1.0
.\scripts\Deploy-Release.ps1 -EnvironmentUrl dev, demo -Version v2.1.0

# Deploy to demo, skipping build and validation phases
.\scripts\Deploy-Release.ps1 -EnvironmentUrl demo -SkipBuild -SkipPhase Validation

# Preview the full release pipeline without executing anything
.\scripts\Deploy-Release.ps1 -EnvironmentUrl demo -WhatIf
```

**Parameters:**
- `-EnvironmentUrl` — One or more Dataverse environment URLs or names from `config/environments.json` (e.g., `dev`, `demo`, `https://spaarkedev1.crm.dynamics.com`)
- `-Version` — Release version tag in `v{major}.{minor}.{patch}` format; auto-suggests from latest git tag if omitted
- `-SkipPhase` — Array of phase names to skip: `Build`, `BffApi`, `OntologyConfig`, `Solutions`, `WebResources`, `Validation`. Skipping `OntologyConfig` does not block Phase 3 (an explicit operator choice); a failed Phase 2 or Phase 2b always does
- `-SkipBuild` — Shortcut for `-SkipPhase Build`
- `-StopOnFailure` — Stop deploying to remaining environments if a deployment fails (default: `$true`)
- `-ClientSecret` — ignored since T218f (warns): Phase 3 imports SpaarkeMaster with your own az/pac sign-in

**Pipeline Phases:**
| Phase | Script Called | Description |
|-------|-------------|-------------|
| 0 | (built-in) | Pre-flight checks: git clean, branch, auth, BFF URL validation |
| 1 | `Build-AllClientComponents.ps1` | Build all client components in dependency order |
| 2 | `Deploy-BffApi.ps1` | BFF API deployment (per environment) |
| 2b | `Apply-WorklistGridConfiguration.ps1` | Ontology configuration (per environment): apply, then `-Verify -RequireRow`, the worklist grid configuration row (task 164, D-185). Runs after Phase 2 and before Phase 3, and does not run if Phase 2 failed (a skipped Phase 2, `-SkipPhase BffApi`, is an explicit operator choice and still runs it); a failure here, or a failed Phase 2, blocks Phase 3 for that environment even with `-StopOnFailure $false` |
| 3 | `solution-authoring/Import-SpaarkeMasterPackage.ps1` | SpaarkeMaster import — the CI-published package, typed per environment (`solutionPackageType` in `config/environments.json`; `none` skips the authoring environment). T218f retired `Deploy-DataverseSolutions.ps1` |
| 4 | `Deploy-AllWebResources.ps1` | Web resource deployment (per environment) |
| 5 | `Validate-DeployedEnvironment.ps1` | Post-deploy validation (per environment) |
| 6 | (built-in) | Tag release in git |

---

## L2 Control-Plane Provisioning Scripts (`scripts/provisioning/`)

Operator scripts for the customer-provisioning-orchestration-r1 L2 control
plane (`Sprk.Provisioning.ControlPlane.Api` + `.Worker`) — distinct from the
customer-lifecycle scripts above (`Decommission-Customer.ps1` etc.), which
operate on a CUSTOMER's environment, not the control plane's own hosting.

### `provisioning/Deploy-ControlPlane.ps1`
**Purpose:** Repeatable deploy for the L2 control plane. Publishes + zip-deploys BOTH App Service targets (`.Api` -> staging slot with optional `-Swap`; `.Worker` -> slotless stop/deploy/start per DS-3 §3 Option 2), then runs post-deploy verification: `/healthz` on every deployed site, fleet Service Bus queue property check (`requiresSession`/`requiresDuplicateDetection`, task 108), and NFR-05 fail-fast config-key presence check. Sibling pattern to `Deploy-BffApi.ps1`, extended for L2's two-site topology.
**Usage:** 🟢 Active - Every L2 code deploy (dispatcher, handlers, config changes)
**Lifecycle:** ✅ Maintained (added 2026-08-19 by customer-provisioning-orchestration-r1 task 113)
**Dependencies:** Azure CLI (`az login` with Contributor on `rg-spaarke-platform-{env}`), .NET 10 SDK, live Service Bus queue already recreated with sessions+dedup ON (task 108's runbook), `Grant-ControlPlaneIdentity.ps1` already run for the target environment
**Owner:** Platform Team (customer-provisioning-orchestration-r1)
**Last Used:** 2026-08-19 (author-time `-WhatIf` dry-run verification only; not yet run against live Azure)

**When to Use:**
- Deploying any L2 code change (handlers, dispatcher, config) to dev/staging/prod
- Promoting a verified `.Api` staging-slot build to production (`-Swap`)
- Post-deploy sanity check of the fleet queue + NFR-05 config surface without a full redeploy (`-SkipBuild`)

**Command:**
```powershell
# Dev: publish + deploy BOTH targets; .Api lands on staging slot only
.\scripts\provisioning\Deploy-ControlPlane.ps1

# Dev: deploy + swap .Api to production once staging is verified healthy
.\scripts\provisioning\Deploy-ControlPlane.ps1 -Target Api -Swap

# Preview a full run without touching Azure
.\scripts\provisioning\Deploy-ControlPlane.ps1 -WhatIf
```

**Parameters:** `-Environment` (dev/staging/production) · `-Target` (Api/Worker/Both) · `-Swap` (promote `.Api` staging -> production) · `-SkipBuild` · `-SkipQueueVerification` / `-SkipConfigVerification` · `-DryRun` (alias for `-WhatIf`) · full reference in the script's comment-based help (`Get-Help .\scripts\provisioning\Deploy-ControlPlane.ps1 -Full`).

**Known gap (documented in the script's own `.DESCRIPTION`, not hidden):** the `.Worker` App Service's Bicep-declared config-key shape still diverges from `.Api`'s canonical NFR-05 shape (task 109 fixed `.Api` only; the Worker follow-on was filed but has no owning task number as of this writing). The script's config-key check uses the Worker's ACTUAL current key shape so the check reflects reality instead of always failing.

### `provisioning/Deploy-ManagementGroups.ps1`
**Purpose:** Creates the management-group hierarchy (`spaarke-environments` → `spaarke-customers`, `infrastructure/bicep/management-groups.bicep`), assigns the common customer policy at `spaarke-customers` (`infrastructure/bicep/customer-policy.bicep` — built-in definitions, Audit / DoNotEnforce, never blocks a deployment), moves the listed subscriptions and reads every placement back (ADR-027, G36).
**Usage:** 🟡 Occasional - once per tenant (and whenever customer-policy.bicep changes); a new customer subscription is placed with `az account management-group subscription add` (PRQ-S-06)
**Lifecycle:** ✅ Maintained (added 2026-10-09 by customer-provisioning-orchestration-r1 task 262)
**Dependencies:** Azure CLI; `powershell-yaml` (ids from `scripts/provisioning-prereqs/spaarke-constants.yaml` `management_groups`). `-Apply` needs Owner — or Contributor + User Access Administrator (or Resource Policy Contributor) — at `/` (tenant-scope deployment); the plan run needs read only
**Owner:** Platform Team (customer-provisioning-orchestration-r1)
**Last Used:** 2026-10-09 (plan run only; read-only)

**Command:**
```powershell
# Plan (default): read-only; prints the groups, the policy, each subscription's move and the exact az commands
pwsh scripts/provisioning/Deploy-ManagementGroups.ps1

# Apply: create the groups, assign the policy, move Demo -> spaarke-customers and Dev + Shared Production -> spaarke-environments
pwsh scripts/provisioning/Deploy-ManagementGroups.ps1 -Apply

# A new customer subscription (PRQ-S-06) needs no tenant-level right - place it directly:
az account management-group subscription add --name spaarke-customers --subscription <subscription-id>
```
Never runs `az account set`. Tests: `tests/scripts/Management-Groups.Tests.ps1` (Pester 3.4).

### `provisioning/Grant-ControlPlaneIdentity.ps1`
**Purpose:** Idempotently registers the L2 control-plane UAMI as a Dataverse Application User on the admin registry environment (Path X, scoped custom role — NOT System Administrator) and grants the C5.8 Microsoft Graph app-role set on the same UAMI service principal.
**Usage:** 🟡 Occasional - Once per environment (re-run is idempotent / safe)
**Lifecycle:** ✅ Maintained (added 2026-08-18 by customer-provisioning-orchestration-r1 task 111)
**Dependencies:** Azure CLI (`az login`) with Dataverse System Administrator on the admin env + Graph `AppRoleAssignment.ReadWrite.All`, `scripts/Grant-GraphAppRoles.ps1`
**Owner:** Platform Team (customer-provisioning-orchestration-r1)
**Last Used:** 2026-08-18

**When to Use:**
- Bootstrapping a new environment's L2 control plane before its first registry write
- Re-verifying grants after a UAMI rotation or admin-env solution re-import

**Command:**
```powershell
.\scripts\provisioning\Grant-ControlPlaneIdentity.ps1 `
    -TenantId "<tenant-guid>" `
    -AdminEnvironmentUrl "https://spaarkedev1.crm.dynamics.com" `
    -UamiClientId "<uami-appid>" `
    -UamiPrincipalId "<uami-spid>"
```

---

### `Build-AllClientComponents.ps1`
**Purpose:** Build all client-side components in correct dependency order — shared libraries first, then Vite solutions, webpack code pages, PCF controls, and external SPA. Ensures downstream components always build against fresh shared library output.
**Usage:** 🟢 Active - Before any release deployment or when rebuilding client components
**Lifecycle:** ✅ Maintained
**Dependencies:** Node.js + npm, shared library source in `src/client/shared/`
**Owner:** DevOps Team
**Last Used:** April 2026

**When to Use:**
- Before deploying web resources (ensures all artifacts are current)
- Full client rebuild after pulling latest changes
- Building specific components after targeted changes
- Called automatically by `Deploy-Release.ps1` (Phase 1)

**Command:**
```powershell
# Full build of all client components in dependency order
.\scripts\Build-AllClientComponents.ps1

# Build everything except shared libraries (assumes they are already built)
.\scripts\Build-AllClientComponents.ps1 -SkipSharedLibs

# Build only the LegalWorkspace and SmartTodo solutions
.\scripts\Build-AllClientComponents.ps1 -Component LegalWorkspace, SmartTodo

# Preview what would happen when building PCF controls (lists every discovered PCF)
.\scripts\Build-AllClientComponents.ps1 -Component PCF -WhatIf

# Shared libraries, then one PCF (production mode)
.\scripts\Build-AllClientComponents.ps1 -Component SharedLibs, PCF/VisualHost
```

**Parameters:**
- `-SkipSharedLibs` — Skip shared library builds (step 1). Use when shared libs are already built.
- `-Component` — Build only specific components by name. Accepts an array of component names matching directory names (e.g., `LegalWorkspace`, `SemanticSearch`, `PCF`). Special names: `SharedLibs`, `PCF`, `ExternalSPA`. `PCF` selects every PCF; `PCF/<folder>` selects one (bare PCF folder names are not accepted — `DocumentRelationshipViewer` is both a PCF and a code page). `PCF` alone does not build the shared libraries the PCFs import; on a clean checkout use `-Component SharedLibs, PCF`.

**Build Order:**
1. Shared libraries (14 packages in `src/client/shared/`, in dependency order; the `$SharedLibs` list in the script is authoritative)
2. Vite solutions (19 projects in `src/solutions/`)
3. Webpack code pages (3 projects in `src/client/code-pages/`)
4. PCF controls — **one at a time, production mode**. Every git-tracked `src/client/pcf/<name>/package.json` with a `build:prod` script is a PCF (the same discovery rules as `.github/workflows/pcf-build-prod-nightly.yml`: git-tracked, exactly one folder level deep, and an unparseable `package.json` is a reported error in both, never a silent drop); each gets `npm install --legacy-peer-deps --no-audit --no-fund` then `npm run build:prod`, and is judged from its output by `PcfBuildResult.psm1` (`pcf-scripts` exits 0 when webpack fails). Each PCF is its own summary row (`PCF/<name>`), so a failure names the control; discovering zero PCFs is a `FAILED` row, and so is a `-Component` name that matches nothing (e.g. `PCF/Nope`), so the script exits non-zero. *(Until 2026-10 this step ran one aggregate dev-mode `npm run build` at `src/client/pcf`; it never worked from a clean checkout — TS5083 on the controls' relative tsconfig `extends`, then out-of-memory building every control in one process — and nothing consumed its `src/client/pcf/out` output.)*
5. External SPA (`src/client/external-spa/`)

**PowerShell 7 (`pwsh`) is recommended** (`Deploy-Release.ps1` runs it under `pwsh`). The file is ASCII-only, so Windows PowerShell 5.1 parses it too; keep it ASCII-only (no BOM needed).

---

### `Deploy-AllWebResources.ps1`
**Purpose:** Deploy all Spaarke web resources to a single Dataverse environment — orchestrates 7 individual deploy scripts in sequence. Each sub-script handles its own authentication, encoding, and error handling. Failures in individual components do not stop the overall run.
**Usage:** 🟢 Active - After building client components, or as part of a release
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), PAC CLI (`pac auth`), Dataverse connection, built client artifacts
**Owner:** DevOps Team
**Last Used:** April 2026

**When to Use:**
- Deploying all web resources after a full client build
- Deploying to a new environment after solution import
- Called automatically by `Deploy-Release.ps1` (Phase 4)

**Command:**
```powershell
# Deploy all web resources to an environment
.\scripts\Deploy-AllWebResources.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com

# Skip specific components
.\scripts\Deploy-AllWebResources.ps1 -SkipComponent RibbonIcons

# Preview which components would be deployed
.\scripts\Deploy-AllWebResources.ps1 -WhatIf
```

**Parameters:**
- `-DataverseUrl` — Target Dataverse environment URL (falls back to `DATAVERSE_URL` env var)
- `-SkipComponent` — Array of component names to skip: `CorporateWorkspace`, `ExternalWorkspaceSpa`, `SpeAdminApp`, `WizardCodePages`, `EventsPage`, `RibbonIcons`

**Deployment Sequence:**
| # | Script Called | Web Resource |
|---|-------------|--------------|
| 1 | ~~`Deploy-CorporateWorkspace.ps1`~~ (script deleted 2026-10-08) | ~~`sprk_corporateworkspace` (HTML)~~ — **RETIRED 2026-05-26** (R4 task 041 / OC-R4-05; see [`docs/architecture/LEGALWORKSPACE-RETIREMENT.md`](../docs/architecture/LEGALWORKSPACE-RETIREMENT.md)) |
| 2 | ~~`Deploy-ExternalWorkspaceSpa.ps1`~~ | ~~`sprk_externalworkspace` (HTML + inline JS)~~ — **RETIRED 2026-07-20** (spaarke-SPA-external-access-platform-r1 task 041; SPA now served from Azure Static Web Apps via `.github/workflows/deploy-external-spa.yml`) |
| 3 | `Deploy-SpeAdminApp.ps1` | `sprk_speadmin` (HTML) |
| 4 | `Deploy-WizardCodePages.ps1` | 12 wizard/code page web resources (note: `sprk_corporateworkspace` entry retired — see above) |
| 5 | `Deploy-EventsPage.ps1` | `sprk_eventspage.html` |
| 6 | ~~`Deploy-PCFWebResources.ps1`~~ | ~~PCF bundle.js + CSS~~ — **RETIRED 2026-10-04** (spaarke-ontology-platform-r1 task 094): it only ever pushed the `UniversalQuickCreate` bundle, from a hard-coded `C:\code_files\spaarke\src\controls\...` path that no longer exists; the control was deleted 2026-06-22 (`pcf-orphan-cleanup-r1`). Deploy PCFs via the `pcf-deploy` skill. |
| 7 | `Deploy-RibbonIcons.ps1` | 3 SVG ribbon icons |

---

## Active Development Scripts

### Reporting Module Scripts

#### `Deploy-ReportingReports.ps1`
**Purpose:** Deploy Power BI report templates (.pbix) to a customer PBI workspace — imports files via PBI REST API, rebinds datasets to the customer's Dataverse instance, configures scheduled refresh, and creates/updates `sprk_report` records in Dataverse.
**Usage:** 🟡 Occasional - Deploy or update standard product reports per customer
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), service principal with PBI workspace Member/Admin role
**Owner:** Reporting Team
**Last Used:** March 2026 (Task 035 — initial implementation)

**Required Environment Variables:**
- `PBI_TENANT_ID` — Entra ID tenant ID
- `PBI_CLIENT_ID` — Service principal app (client) ID
- `PBI_CLIENT_SECRET` — Service principal client secret
- `PBI_WORKSPACE_ID` — Target PBI workspace ID (overridable via `-WorkspaceId`)
- `PBI_DATAVERSE_DATASOURCE_URL` — Dataverse OData endpoint for dataset rebinding
- `DATAVERSE_URL` — Dataverse org URL for catalog sync

**When to Use:**
- After building or updating `.pbix` templates in `reports/`
- During customer onboarding (provision workspace → deploy reports)
- After a new standard product report is added to the `reports/` folder
- Deploying a new report version to existing customer workspaces

**Command:**
```powershell
# Dry run — preview what would be deployed
.\Deploy-ReportingReports.ps1 -WhatIf

# Deploy standard product reports (reads PBI_WORKSPACE_ID from env)
.\Deploy-ReportingReports.ps1

# Deploy specific version folder
.\Deploy-ReportingReports.ps1 -ReportFolder "reports/v1.2.0"

# Deploy to a specific workspace
.\Deploy-ReportingReports.ps1 -WorkspaceId "00000000-0000-0000-0000-000000000000"

# Deploy to staging environment
.\Deploy-ReportingReports.ps1 -Environment staging -DataverseOrg "https://myorg-stg.crm.dynamics.com"
```

**Deployment Steps:**
1. Validates prerequisites (env vars, .pbix files, Azure CLI)
2. Acquires PBI REST API token via service principal client credentials flow
3. Acquires Dataverse token via Azure CLI
4. For each `.pbix` file: imports to PBI workspace, rebinds dataset to Dataverse, sets refresh schedule
5. Creates or updates `sprk_report` records in Dataverse (catalog sync)
6. Outputs deployment summary table

**Notes:**
- Report category is inferred from the report filename (Financial, Documents, Operational, Compliance, Custom)
- Scheduled refresh defaults to Mon–Fri at 06:00, 12:00, and 18:00 UTC; set `PBI_REFRESH_ENABLED=false` to skip
- Dataset rebind uses `Default.SetAllConnections`; if the dataset uses embedded credentials the warning is non-fatal
- Idempotent: existing `sprk_report` records (matched by PBI report ID) are updated, not duplicated

---

#### `Initialize-ReportingCustomer.ps1`
**Purpose:** End-to-end customer onboarding for the Reporting module — creates a Power BI workspace, creates a service principal profile, adds the SP profile as workspace Admin, deploys standard product reports, enables the `sprk_ReportingModuleEnabled` environment variable, and documents the manual security role assignment step.
**Usage:** 🟡 Occasional - New customer onboarding or re-provisioning after partial failure
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), service principal with PBI tenant-level access, `Deploy-ReportingReports.ps1`
**Owner:** Reporting Team
**Last Used:** March 2026 (Task 040 — initial implementation)

**Required Environment Variables:**
- `PBI_TENANT_ID` — Entra ID tenant ID (overridable via `-TenantId`)
- `PBI_CLIENT_ID` — Service principal app (client) ID
- `PBI_CLIENT_SECRET` — Service principal client secret
- `DATAVERSE_URL` — Dataverse org URL (overridable via `-DataverseOrg`)
- `PBI_DATAVERSE_DATASOURCE_URL` — Dataverse OData endpoint for dataset rebinding

**When to Use:**
- Onboarding a new customer to the Spaarke Reporting module
- Re-running after a partial failure (idempotent — skips already-created resources)
- Setting up the Reporting module in a new Dataverse environment

**Command:**
```powershell
# Dry run — preview all onboarding steps
.\Initialize-ReportingCustomer.ps1 `
    -CustomerId "contoso-legal" `
    -CustomerName "Contoso Legal Services" `
    -DataverseOrg "https://contoso.crm.dynamics.com" `
    -WhatIf

# Full onboarding — shared capacity (dev/test)
.\Initialize-ReportingCustomer.ps1 `
    -CustomerId "contoso-legal" `
    -CustomerName "Contoso Legal Services" `
    -DataverseOrg "https://contoso.crm.dynamics.com"

# Full onboarding — dedicated F2 capacity (production)
.\Initialize-ReportingCustomer.ps1 `
    -CustomerId "fabrikam-corp" `
    -CustomerName "Fabrikam Corporation" `
    -DataverseOrg "https://fabrikam.crm.dynamics.com" `
    -CapacityId "00000000-0000-0000-0000-000000000000"

# Onboarding with user list for security role assignment guidance
.\Initialize-ReportingCustomer.ps1 `
    -CustomerId "woodgrove-legal" `
    -CustomerName "Woodgrove Legal" `
    -DataverseOrg "https://woodgrove.crm.dynamics.com" `
    -SecurityRoleUsers @("alice@woodgrove.com", "bob@woodgrove.com")
```

**Onboarding Steps:**
1. Validates prerequisites (env vars, Azure CLI, `Deploy-ReportingReports.ps1` present)
2. Acquires PBI REST API token via service principal client credentials flow
3. Creates workspace `"{CustomerName} - Reporting"` (or reuses if already exists)
4. Assigns workspace to capacity if `-CapacityId` provided
5. Creates SP profile `"sprk-{CustomerId}"` (or reuses if already exists)
6. Adds SP profile as workspace Admin (skips if already assigned)
7. Calls `Deploy-ReportingReports.ps1` to import `.pbix` files and sync Dataverse catalog
8. Enables `sprk_ReportingModuleEnabled = Yes` in the customer's Dataverse environment
9. Prints security role assignment commands (manual step)
10. Outputs full onboarding summary (customer ID, workspace ID, profile ID, report count, Dataverse URL)

**Notes:**
- Idempotent: workspace lookup by exact name, profile lookup by exact display name — re-running after partial failure is safe
- Security role assignment (`sprk_ReportingAccess`) is a documented manual step; script prints PAC CLI commands
- Pass `-SecurityRoleUsers @("user@domain.com")` to pre-populate assignment commands in the output
- `-CapacityId` is optional for dev/test; required for production (F-SKU F2+ recommended)
- Workspace name pattern: `"{CustomerName} - Reporting"`; SP profile name pattern: `"sprk-{CustomerId}"`
- `CustomerId` must be lowercase with hyphens only (validated by regex); used in profile name and logging

---

#### `Deploy-ReportingCodePage.ps1`
**Purpose:** Build the Reporting Code Page (`src/solutions/Reporting/`) and upload `dist/index.html` to Dataverse as the `sprk_reporting` web resource. Uses `vite-plugin-singlefile` so the output is a single fully self-contained HTML file — no separate asset inlining required.
**Usage:** 🟢 Active - Deploy after Reporting Code Page changes
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Node.js + npm, Dataverse connection
**Owner:** Reporting Team
**Last Used:** March 2026 (Task 016 — initial implementation)

**When to Use:**
- After modifying the Reporting Code Page (`src/solutions/Reporting/src/`)
- Initial setup of the `sprk_reporting` web resource in a new environment
- Deploying updates to the embedded Power BI report viewer UI

**Command:**
```powershell
# Deploy to default environment (reads DATAVERSE_URL from env)
.\Deploy-ReportingCodePage.ps1

# Deploy to a specific Dataverse org
.\Deploy-ReportingCodePage.ps1 -DataverseUrl "https://myorg.crm.dynamics.com"

# Preview without deploying
.\Deploy-ReportingCodePage.ps1 -WhatIf
```

**Deployment Steps:**
1. `npm install` in `src/solutions/Reporting/`
2. `npm run build` — Vite + vite-plugin-singlefile produces `dist/index.html` (all JS/CSS inlined)
3. Verifies `dist/index.html` exists and contains no external asset references
4. Acquires Dataverse access token via Azure CLI
5. Creates or updates `sprk_reporting` web resource via Dataverse Web API
6. Publishes the web resource so changes are live

**Notes:**
- The Reporting Code Page uses `vite-plugin-singlefile`, making the dist output a single file — no manual JS inlining step needed (contrast with the retired `Deploy-ExternalWorkspaceSpa.ps1`, which inlined JS into a Dataverse web resource)
- Idempotent: first run creates the web resource; subsequent runs update and republish it
- Web resource accessible at: `{DataverseUrl}/WebResources/sprk_reporting`

---

### PCF & Custom Page Deployment

#### `Deploy-SpeAdminApp.ps1`
**Purpose:** Deploy SPE Admin App code page (`src/solutions/SpeAdminApp/dist/speadmin.html`) as the `sprk_speadmin` Dataverse web resource. Creates the web resource on first run; updates and publishes on subsequent runs.
**Usage:** 🟢 Active - Deploy after code page build
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Dataverse connection, built artifact at `src/solutions/SpeAdminApp/dist/speadmin.html`
**Owner:** SPE Admin Team
**Last Used:** March 2026 (Task 044 — initial deployment)

**When to Use:**
- After building the SPE Admin App (`npm run build` in `src/solutions/SpeAdminApp/`)
- Deploying updates to the SPE Admin App
- Initial setup of the web resource in a new environment

**Command:**
```powershell
# From repo root
.\scripts\Deploy-SpeAdminApp.ps1
```

**Notes:**
- Web resource ID: `5f86c079-cd1f-f111-88b3-7ced8d1dc988` (dev environment)
- Web resource accessible at: `https://spaarkedev1.crm.dynamics.com/WebResources/sprk_speadmin`
- Creates on first deploy, updates on subsequent deploys (auto-detects)

---

#### `Invoke-PcfBuildProd.ps1`
**Purpose:** Production build of ONE PCF control (`npm run build:prod`) that **fails when the build failed**
**Usage:** 🟢 Active - before packing or importing any PCF
**Lifecycle:** ✅ Maintained
**Dependencies:** Node/npm; the PCF's own `build:prod` script; `PcfBuildResult.psm1`
**Owner:** Development Team
**Added:** 2026-10-04 (spaarke-ontology-platform-r1 task 094)

**Why it exists:** `pcf-scripts build` **exits 0 when webpack fails**. It logs `[build] Failed:` and
`[pcf-1033] [Error] An error occurred compiling or bundling the control.` and returns without rethrowing, so a bare
`npm run build:prod` followed by "copy `bundle.js` and pack" ships the PREVIOUS bundle still sitting in `out/`.
This script judges the result from the output: FAIL on a non-zero exit, `[build] Failed`, a `compiled with N error(s)`
line or `[pcf-1033]`; PASS only on `[build] Succeeded`. The same rule is used by `Build-AllClientComponents.ps1`
(PCF step, which builds every PCF this way, one per summary row) and the nightly CI workflow
`.github/workflows/pcf-build-prod-nightly.yml`.

**Command:**
```powershell
.\scripts\Invoke-PcfBuildProd.ps1 -PcfPath src/client/pcf/SemanticSearchControl          # exits 1 on a failed build
.\scripts\Invoke-PcfBuildProd.ps1 -PcfPath src/client/pcf/SemanticSearchControl -Install # npm install first
```

**Related:** `PcfBuildResult.psm1` (`Get-PcfBuildResult -Output <lines> -ExitCode <n>`) holds the rule for any other script.

#### ~~`Deploy-PCFWebResources.ps1`~~
**RETIRED 2026-10-04** (spaarke-ontology-platform-r1 task 094): it only ever pushed the `UniversalQuickCreate` bundle, from a hard-coded `C:\code_files\spaarke\src\controls\...` path that no longer exists; the control was deleted 2026-06-22 (`pcf-orphan-cleanup-r1`). Deploy PCFs via the `pcf-deploy` skill.

---

#### `Deploy-CustomPage.ps1`
**Purpose:** Deploy custom pages to Dataverse solution
**Usage:** 🟡 Occasional - Deploy new/updated custom pages
**Lifecycle:** ✅ Maintained
**Dependencies:** PAC CLI, solution context
**Owner:** Development Team
**Last Used:** Phase 7 (Custom page implementation)

**When to Use:**
- Creating new custom pages
- Updating custom page metadata
- Deploying custom page bundles

**Command:**
```powershell
.\Deploy-CustomPage.ps1 -PageName "UniversalDocumentUpload" -SolutionName "SpaarkeCore"
```

---

#### `create-custom-page.ps1`
**Purpose:** Scaffold new custom page structure
**Usage:** 🔴 One-time - Create new custom page
**Lifecycle:** 📦 Archive (generator script)
**Dependencies:** None
**Owner:** Development Team

**When to Use:**
- Starting new custom page feature
- Generating boilerplate for custom pages

**Command:**
```powershell
.\create-custom-page.ps1 -Name "MyNewPage" -OutputPath "./src/pages"
```

---

## Data Backfill Scripts

### `Backfill-CoreAncestorStamps.ps1`
**Purpose:** One-time (re-runnable) backfill of FR-26 core-ancestor stamps onto EXISTING child-class Dataverse records (`sprk_todo`, `sprk_communication`, `sprk_event`, `sprk_invoice`, `sprk_document`, `sprk_analysis`) whose regarding target is itself a child-class record — so FR-27 access inheritance ("a contact with Project access sees its To Dos/communications/...") works for records created before the `unified-access-control-r2` FR-26 write-path tasks (050/052) shipped. Discovers ancestor-stamp columns and child-of-child lookups from LIVE Dataverse metadata every run (never a hard-coded list) — see the script header and the companion runbook for a live-metadata finding this caught (`sprk_invoice`/`sprk_document` structurally cannot carry an ancestor stamp under the current schema).
**Usage:** 🔴 One-time (per environment) — re-runnable to fixpoint for deep (2+ hop) chains; safe to run repeatedly (idempotent).
**Lifecycle:** ✅ Maintained (added 2026-09-04 by `unified-access-control-r2` task 053)
**Dependencies:** Azure CLI (`az login`), Dataverse connection, PowerShell 7+
**Owner:** UAC Team (`unified-access-control-r2`)
**Last Used:** Author-time syntax validation only (`[ScriptBlock]::Create`, `Get-Help -Full`, PSScriptAnalyzer, and an isolated local test of the retry/closure helper) — **not yet run against a live environment**; live metadata (`EntityDefinitions`/`ManyToOneRelationships`, read-only) was queried during authoring to verify column names. Execution against a real environment is an explicit operator decision (see the runbook §7).

**When to Use:**
- Once, per environment, after tasks 050/052 (FR-26 write-path convergence) have shipped — to backfill pre-existing child records that predate the stamp.
- Re-run after a partial/interrupted `-Apply` — resumable by construction (the candidate query itself excludes already-stamped rows; no checkpoint file needed).
- Re-run to fixpoint for deep chains (2+ hops of child-of-child) — each pass can resolve one more hop as earlier targets get stamped; converged when a run reports `ToWrite: 0`.

**Command:**
```powershell
# Dry run (default) — zero writes, full candidate summary + log. Always run first.
.\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

# Apply — writes every resolvable stamp (unless the 50k/20%-unresolvable escalation gate fires).
.\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply

# Stage one entity at a time.
.\Backfill-CoreAncestorStamps.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply -Entities sprk_todo
```

**Safety model:** dry-run default (`-WhatIf` also forces preview even combined with `-Apply`); idempotent (compares the derived value against the row's current value, not just null-vs-populated); a disagreeing existing stamp is reported as a `Conflict` and never overwritten; an escalation gate (>50,000 total candidates, or any entity >20% unresolvable) blocks `-Apply` until `-AcknowledgeEscalation` is passed. Full detail: `Get-Help .\Backfill-CoreAncestorStamps.ps1 -Full` and [`projects/unified-access-control-r2/notes/phase3-backfill-runbook.md`](../projects/unified-access-control-r2/notes/phase3-backfill-runbook.md).

### `Invoke-SecureChildBackfill.ps1`
**Purpose:** One-time backfill of the EXISTING children of every secure project, matter and work assignment (C10 part 2): drives the BFF's `secure-child-reconciliation` job, which re-owns each child into the `Secure Record Owners` team by the ownership rule and mirrors the record's sharees. The script computes NOTHING itself — the rule is the BFF's (`SecureChildReconciler` → `IRecordOwnershipResolver` + `SecureChildShareSynchronizer`); it triggers the job through `/api/admin/jobs`, reads each run's report (`resultJson`), repeats until the pass completes, and saves the reports.
**Usage:** 🔴 One-time (per environment) after the BFF carrying task 148 is deployed; re-runnable (idempotent).
**Lifecycle:** ✅ Maintained (added 2026-10-04 by `unified-access-control-r2` task 148)
**Dependencies:** Azure CLI (`az login` as a user the BFF's `SystemAdmin` policy admits; for `-Apply`, rights to change the App Service settings), PowerShell 7+
**Owner:** UAC Team (`unified-access-control-r2`)
**Last Used:** Author-time parse check only — **not run against a live environment** (a manual gate for the main session).

**Command:**
```powershell
# Dry run (default, report-only): the planned changes with each row's current owner — the same plan the apply carries
# out (grandchildren included). A report lists 200 changes per run and counts all; the script warns when it lists fewer,
# and on NeedsF3 rows (isolated rows only Unsecure may release — the sweep never does, owner round 24).
.\Invoke-SecureChildBackfill.ps1 -BffBaseUrl https://<bff-host> -ApiScope api://<bff-app-id>/.default
# Apply: SecureChild__Reconciliation__WritesEnabled=true for the run, removed again afterwards (restarts the app).
.\Invoke-SecureChildBackfill.ps1 -BffBaseUrl https://<bff-host> -ApiScope api://<bff-app-id>/.default -Apply -ResourceGroup <rg> -AppName <app>
# Verify: exit 0 only when a full pass plans zero changes and refuses / fails nothing.
.\Invoke-SecureChildBackfill.ps1 -BffBaseUrl https://<bff-host> -ApiScope api://<bff-app-id>/.default -Verify
```

Runbook: [`docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md`](../docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md) §7c.1.
### `Backfill-SpeContainerBusinessUnitStamp.ps1`
**Purpose:** Binds EXISTING SharePoint Embedded containers to the business unit that owns them (the container custom property
named in `common/SpeContainerBinding.ps1`). The BFF's SPE admin plane authorizes every container route per container and
reaches **no** unbound container (unified-access-control-r2 task 165, owner rounds 20 and 35). The owner is derived only from
authoritative records (`businessunit.sprk_containerid`, a secure root's own container, the admin-plane `CreateContainer`
audit row); a container no record claims is bound only by an explicit `-Bind '<containerId>=<businessUnitId>'`.
**Usage:** 🔴 Per environment — a **manual gate** before task 165's BFF is deployed there, and before a further environment
is onboarded onto a container type another environment already uses (`-Verify` must exit 0).
**Lifecycle:** ✅ Maintained (task 165 f1 2026-10-04; `-Bind` + unbound listing f2)
**Dependencies:** Azure CLI (`az login`: Dataverse + the BFF Key Vault for the owning apps' secrets), PowerShell 7+
**Safety model:** dry run by default (`-WhatIf` forces it); write-ahead reversal manifest (`-RevertManifest <csv> -Apply`
undoes); every write read back; never overwrites a stamp (MISMATCH / FOREIGN / MALFORMED / BIND-CONFLICT are listed);
`-MaxWritesPerRun` samples; `-Verify` lists every still-unbound container and fails on it, on a mismatch, an unreadable
binding or a config whose containers could not be listed. A config whose Key Vault secret name is outside the
allow-list is SKIPPED with its secret never read (round 41 item 4 — repair it with `Repair-SpeConfigSecretName.ps1`, or
bind its containers with `-Bind`). This script is the ONE remaining reader of `sprk_keyvaultsecretname` (round 65 item 1).

```powershell
.\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault>            # dry run
.\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -Apply
.\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -Bind '<containerId>=<buId>' -Apply
.\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -Verify   # must exit 0
.\Backfill-SpeContainerBusinessUnitStamp.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -KeyVaultName <vault> -CustomerId <Customer__Id>          # issue #1889: also mark pre-marker containers (dry run; then -Apply -MaxWritesPerRun 1, -Apply, -Verify)
```

`-CustomerId` (issue #1889) adds the customer-marker pass. It takes the BFF's `Customer__Id` (or, when that is unset, the id
the BFF derives from `rg-spaarke-{customerId}-{env}`) and must match `^[a-z][a-z0-9]{2,7}$` (otherwise exit 2, nothing read).
A container is marked only if it already carried a stamp naming one of this environment's units and its records derive it, or
an explicit `-Bind` names it; one the run stamps from records alone is listed `MARK-NEXT-RUN` and marked by the NEXT run
(review the dry-run list first: the record claim is customer-writable); a stamped container no record claims is
`UNMARKED-NO-CLAIM`. A marker is never overwritten (`MARKER-FOREIGN` / `MARKER-MALFORMED` are listed), underivable and
stamp-disputed containers are never marked; `-Verify` also fails on any unmarked one. The guard caches a missing marker for up
to 10 minutes. Tests: `tests/scripts/Backfill-SpeContainerMarker.Tests.ps1`.

**Shared module:** `common/SpeContainerBinding.ps1` — THE PowerShell constant for the property name and
`Invoke-SpeContainerBindOrRemove` (stamp the business unit AND mark the customer — `-CustomerId`, the BFF's `Customer__Id`,
task 227g — read both back, remove the container if either did not land), used by every script that creates a container
(`New-BusinessUnitContainer.ps1 -CustomerId`, `Provision-Customer.ps1` step 10, `Create-NewContainerType.ps1
-CreateTestContainer -TestContainerBusinessUnitId <bu> -TestContainerCustomerId <id>`). `SpeContainerMarkerParityTests` fails
the build on a call without `-CustomerId`. `SpeAdminContainerBindingGuardTests`
fails the build on a script that creates a container without it — including a URI held in a variable, a splat, `az rest`,
or the Graph PowerShell `New-MgStorageFileStorageContainer` cmdlet (round 41 item 5).

### `Backfill-RecordOwnership.ps1`
**Purpose:** Re-owns EXISTING **app-owned** `sprk_document` / `sprk_todo` rows to a business unit's DEFAULT OWNER TEAM, record-first — the backfill for write-path invariant **I-6**. Before task 080 every BFF-created record was owned by the BFF application user in the ROOT business unit, which no child-business-unit user can read at Deep depth. The team comes from the record the row is filed against (document: `sprk_matter` → `sprk_project` → `sprk_invoice` → `sprk_workassignment`; To Do: record regarding → document → communication), mirroring `RecordOwnershipResolver`. Rows filed against nothing are **reported, never written** — the create was app-only, so the data does not record who made it ("we can't guess").
**Usage:** 🔴 One-time (per environment); idempotent — re-owned rows are no longer candidates.
**Lifecycle:** ✅ Maintained (added 2026-09-30 by `spaarkeai-word-add-in-r1` task 080)
**Dependencies:** Azure CLI (`az login`) with access to the environment, PowerShell 7+
**Owner:** `spaarkeai-word-add-in-r1`
**Last Used:** 2026-09-30 — **dry run only** against `spaarkedev1` (407 app-owned documents: 31 would re-own, 376 unfiled; 10 To Dos: all 10 would re-own). **No `-Apply` has been run** — that is the operator's call.

**When to Use:**
- Once per environment after task 080's writers ship, to fix rows created before them.
- After a `sprk_document` run, run again for `sprk_todo`, so To Dos filed to a document follow that document's new business unit.

**Command:**
```powershell
# Dry run (default) — zero writes; the plan per row + totals in the log. Always run first.
.\Backfill-RecordOwnership.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

# SAMPLE — re-own five rows (each read back), then check them in the app as a child-BU user.
.\Backfill-RecordOwnership.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply -MaxWritesPerRun 5

# Undo a run exactly, from the manifest it printed.
.\Backfill-RecordOwnership.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -RevertManifest .\logs\record-ownership-manifest-<stamp>.csv -Apply
```

**Safety model:** dry-run default (`-WhatIf` forces a preview even with `-Apply`); a **write-ahead reversal manifest** records each row's previous owner before its write, so `-RevertManifest` undoes a run; every assignment is **read back** (Dataverse silently ignores an unrecognised `@odata.bind`); only application-user-owned rows are candidates; an ambiguous or missing default team is reported `Unresolvable`, never guessed. Detail: [`projects/spaarkeai-word-add-in-r1/notes/080-record-ownership.md`](../projects/spaarkeai-word-add-in-r1/notes/080-record-ownership.md) §6.9.

### `Set-FieldMappingRuleSyncMode.ps1`
**Purpose:** Migrates every existing **Copy** field-mapping rule (`sprk_fieldmappingrule.sprk_mapping_type = 0`) to sync mode **Manual Refresh** (`sprk_syncmode = 1`), so `/push` (task 040) can select the rules it re-applies by that value. Default/Concat/Template rules and rules already at 1 are never written; an empty `sprk_syncmode` counts as not migrated; an inactive Copy rule is reported, never touched; an unrecognised mapping type or sync mode stops the run before any write (exit 2). The PATCH body is exactly `{ "sprk_syncmode": 1 }`.
**Usage:** 🔴 One-time (per environment); idempotent — a second `-Apply` reports `ToMigrate: 0`.
**Lifecycle:** ✅ Maintained (added 2026-10-21 by `field-mapping-server-write-path-r1` task 005, FR-05d)
**Dependencies:** Azure CLI (`az login`) with access to the environment, PowerShell 7+
**Owner:** `field-mapping-server-write-path-r1`
**Last Used:** 2026-10-21 — `-SelfTest` PASS; read-only dry run + `-Verify` baseline against `spaarkedev1` (25 Copy rules: 21 would migrate — 20 empty, 1 One-time — 4 already Manual Refresh). **No `-Apply` has been run**: it needs the owner's explicit OK per environment.

**Command:**
```powershell
# Dry run (default) — zero writes; lists each rule that would move, with its current mode, and the totals.
.\Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

# Read-only baseline — exits 1 while any Copy rule is not at Manual Refresh.
.\Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Verify

# Migrate (owner OK required), read each row back, then verify.
.\Set-FieldMappingRuleSyncMode.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply -Verify

# Offline checks of the classifier and idempotency.
.\Set-FieldMappingRuleSyncMode.ps1 -SelfTest
```

**Safety model:** dry-run default (`-WhatIf` forces a preview even with `-Apply`); `If-Match: *` so the PATCH can only update; every write is read back; `-Verify` is read-only and fails on any Copy rule short of Manual Refresh, active or not.

### `Set-DocumentAccessPermissionSchema.ps1`
**Purpose:** Brings `sprk_document.sprk_accesspermission` into source (owner round 81): the column exists, is a Choice bound to the SAME global choice as the roots' Access Permission, and ships in `SpaarkeCore` (membership decided through `scripts/common/DataverseSolutionMembership.ps1`). Task 189 (D7 T7a, #2126) adds the document's own-narrowing record `sprk_document.sprk_accessinheritance` (Multiple lines of text, 4000, audited, **NOT field-secured** — decision D1; see `docs/data-model/access-inheritance.md`). `-Apply` creates each column when absent (or adds it to the solution); it never alters an existing one (`COLUMN_MISMATCH` / `RECORD_MISMATCH` refuse — a secured record column is a mismatch). `-Verify` also asserts the record column is on no `sprk_document` form.
**Usage:** 🟡 Per environment, before `Set-InheritedAccessPermissionFormLock.ps1`, and BEFORE deploying a BFF with task 189 (until the record column exists that BFF writes no document's Access Permission — fail closed); `-Verify` any time.
**Lifecycle:** ✅ Maintained (added 2026-10-08 by `unified-access-control-r2` task 173; record column 2026-10-20, task 189)
**Dependencies:** Azure CLI (`az login`) with customizer rights in the environment, PowerShell 7+
**Owner:** `unified-access-control-r2`
**Last Used:** 2026-10-08 — `-SelfTest` PASS; `-Verify` PASS against `spaarkedev1` (the owner added the column live; it ships in SpaarkeCore with its table).

**Command:**
```powershell
.\Set-DocumentAccessPermissionSchema.ps1 -Verify      # read-only (exit 0 / 1)
.\Set-DocumentAccessPermissionSchema.ps1              # dry run
.\Set-DocumentAccessPermissionSchema.ps1 -Apply       # operator only
```

### `Set-AccessInheritanceSchema.ps1`
**Purpose:** Creates `sprk_accessinheritance` (Multiple lines of text, 4000) on `sprk_workassignment` and `sprk_project`, ships it in `SpaarkeCore`, and LOCKS it with field-level security (task 175, owner round 87; fix round, verifier F1-1). The BFF writes there, as versioned JSON, what each record's stored Secure / Access Permission were derived from: the parents' floor, the parents, and what was set on the record (see `docs/data-model/access-inheritance.md`). A forged value could make a hand-secured child look "inherited", so the column is created SECURED and only task 133's "Spaarke BFF-Managed Field Writers" profile (members = the BFF application users, checked) gets read/create/update; no reader profile. Preconditions: (p1) writer profile in the solution, (p3) its members exactly the `-BffApplicationIds` users, (p6) no field-mapping rule / AI topic row / email update field targets the column, (p7) a pre-existing unsecured column holds no value. `-Verify` also asserts no other profile can read or write it and that each BFF user can READ it (System Administrator, or the platform's `RetrievePrincipalAttributePrivileges`; `-AcceptDerivedReadCheck` accepts the membership derivation when the platform cannot be asked), plus a read probe as the BFF user once a record exists. A grant failure leaves the column secured (fail closed), never reverted. `COLUMN_MISMATCH` / `SOLUTION_MISSING` refuse. No data backfill: the BFF's job writes each record the first time it sees it.
**Usage:** 🟡 Per environment, BEFORE deploying a BFF with task 175 (exit 0 from `-Verify` required); `-Verify` again after the BFF's first job run (read probe).
**Lifecycle:** ✅ Maintained (added 2026-10-09 by `unified-access-control-r2` task 175)
**Dependencies:** Azure CLI (`az login`) with customizer rights in the environment, PowerShell 7+
**Owner:** `unified-access-control-r2`
**Last Used:** 2026-10-09 — `-Apply` then `-Verify` PASS on `spaarkedev1` (main session). `-SelfTest` PASS (29 checks): after creating or securing the column, `-Apply` publishes the table and waits until it reads back as secured before the grant, and it retries a grant refused with "doesn't contain attribute" or 0x8004f508 on every table.

**Command:**
```powershell
.\Set-AccessInheritanceSchema.ps1 -SelfTest                                          # offline
$bff = '5967251e-171c-46fe-a6c2-ef843c90309d,1e40baad-e065-4aea-a8d4-4b7ab273458c'   # dev BFF application ids
.\Set-AccessInheritanceSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds $bff           # dry run
.\Set-AccessInheritanceSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds $bff -Apply    # operator only
.\Set-AccessInheritanceSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds $bff -Verify   # exit 0 / 1
```

### `Set-InheritedAccessPermissionFormLock.ps1`
**Purpose:** Registers the form library `sprk_accesspermission_inherited` (OnLoad `Spaarke.AccessPermissionInherited.onLoad`) on every Main / Quick Create form of To Do, Event, Communication and Document that shows `sprk_accesspermission`, and adds a plain control to the Communication "Message main form" and the "Document main form" (owner round 81: no TrackingFieldTrio on Communication). The library locks the field with "Access permission is inherited from …" while the record has a parent. Since task 175 it also registers the library on every Work Assignment and Project MAIN form that shows `sprk_accesspermission` (the TrackingFieldTrio pill; no plain control is added there). There the lock is a FLOOR (owner round 87): a work assignment or project filed under a matter or project may not go below its parents' Access Permission (a looser pick is put back, a stricter one kept), `sprk_issecure` is disabled while it is filed, and the notification says whether the value is inherited or set on the record. Additive string insertions proven by a parse check; snapshot before every write.
**Usage:** 🟡 Per environment, after the web resource is deployed; `-Verify` any time (fails on a form that shows the field without the lock, or a deployed library that differs from the repo).
**Lifecycle:** ✅ Maintained (added 2026-10-08 by `unified-access-control-r2` task 173; Work Assignment / Project main forms added by task 175)
**Dependencies:** Azure CLI (`az login`) with customizer rights in the environment, PowerShell 7+
**Owner:** `unified-access-control-r2`
**Last Used:** 2026-10-09 — `-SelfTest` PASS (47 checks, task 175). 2026-10-08 — dry run against `spaarkedev1` (refused PREREQ_MISSING until the web resource is deployed — the main session's gate).

**Command:**
```powershell
.\Set-InheritedAccessPermissionFormLock.ps1 -SelfTest   # offline fixtures
.\Set-InheritedAccessPermissionFormLock.ps1             # dry run
.\Set-InheritedAccessPermissionFormLock.ps1 -Apply      # operator only: snapshot, PATCH, publish, read back
.\Set-InheritedAccessPermissionFormLock.ps1 -Verify     # read-only (exit 0 / 1)
.\Set-InheritedAccessPermissionFormLock.ps1 -RestoreFrom .\accesspermission-lock-snapshot-<stamp>.json
```

### `Set-SecureRecordOwnerRolePrivileges.ps1`
**Purpose:** Gives the `Secure Record Owner` role `Read` at User (Basic) depth on every table in [`config/secure-record-owner-role.json`](../config/secure-record-owner-role.json). Without it, Dataverse refuses the Secure Record team as the OWNER of a row ("Read Privilege Check For Owner failed … missing prvRead…"). Write-path invariant I-6 assigns a secure record's children (documents, To Dos, …) to that team, so the role must cover child tables as well as the three `sprk_issecure` roots. The JSON file is the ONE list, also read by the setup guide and by `unified-access-control-r2`'s NFR-05 census.
**Usage:** 🟡 Per environment, at secure-record setup and whenever the JSON gains a table; `-Verify` any time.
**Lifecycle:** ✅ Maintained (added 2026-09-30 by `spaarkeai-word-add-in-r1` task 082)
**Dependencies:** Azure CLI (`az login`) with System Administrator in the environment, PowerShell 7+
**Owner:** `spaarkeai-word-add-in-r1` (the JSON file is extended by `unified-access-control-r2` task 146 too)
**Last Used:** 2026-09-30, `-Apply` against `spaarkedev1`: added todo, communication, event and memo Read (36 → 40, 0 removed); `-Verify` PASS.

**Command:**
```powershell
# Dry run (default): present / missing / "outside the file" (reported, never removed). Zero writes.
.\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

# Add the missing Read privileges at Basic, then read the role back.
.\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply

# Check: exit 0 = the role covers every table in the file; exit 1 names each gap.
.\Set-SecureRecordOwnerRolePrivileges.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Verify
```

**Safety model:**
- Adds only. It never removes or re-depths a privilege; removal is an owner decision (setup guide §5.4).
- Refuses any config asking for other than Read at Basic.
- Resolves the business unit and the role to exactly one each.
- Takes privilege ids from entity metadata and refuses a name mismatch.
- Reads every addition back, and names any privilege the platform injected unasked.
- Dataverse caches principal privileges, so re-probe an assignment until it is stable across 3 polls (setup guide §7).

Detail: [`projects/spaarkeai-word-add-in-r1/notes/082-secure-owner-role.md`](../projects/spaarkeai-word-add-in-r1/notes/082-secure-owner-role.md).

### `Deploy-ExternalShareExpiryViews.ps1`
**Purpose:** Creates two public views on `sprk_externalrecordaccess` in SpaarkeCore. "Active External Shares by Expiration" lists every active row, soonest expiry first. "External Shares Expiring in 30 Days" lists the same rows with an expiry on or before today + 30, using a relative filter that never goes stale. Together they answer what is about to lapse across all external shares, which the per-grant reminders (FR-33) cannot.
**Usage:** 🟡 Per environment, once; `-Verify` any time.
**Lifecycle:** ✅ Maintained (added 2026-10-07 by `unified-access-control-r2` task 101)
**Dependencies:** Azure CLI (`az login`) with System Customizer or System Administrator in the environment, PowerShell 7+
**Owner:** `unified-access-control-r2`
**Last Used:** 2026-10-07, dry run and `-Verify` against `spaarkedev1`. Not applied (operator step).

**Command:**
```powershell
# Dry run (default): checks every column against metadata, runs both queries live, prints the plan. Zero writes.
pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1

# Create, rewrite a same-named view that differs (snapshot to scripts/logs/ first), add to SpaarkeCore if missing, publish when anything was written or is unpublished, then verify.
pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1 -Apply

# Check: exit 0 = both views exist once, are published with the right definition, are in SpaarkeCore, and return the right rows (all pages).
pwsh -File scripts/Deploy-ExternalShareExpiryViews.ps1 -Verify
```

Detail, including the measured semantics of the relative date operators: [`projects/unified-access-control-r2/notes/task-101-expiring-shares-views.md`](../projects/unified-access-control-r2/notes/task-101-expiring-shares-views.md).

### `Set-RecordNumberingSchema.ps1`
**Purpose:** The INTERIM record numbering (owner decisions 2026-10-02, "until we build the numbering function"): Matters get `MAT-######`, Projects `PRJ-######`, sequential, from Dataverse's platform autonumber. `sprk_matternumber` / `sprk_projectnumber` are their tables' PRIMARY NAME, so a record without a number is nameless everywhere (write-path invariant **I-11**). Sets the `AutoNumberFormat`, seeds the sequence from the data (above the highest existing `MAT-`/`PRJ-` value), numbers blank rows oldest first, creates the alternate keys, and confirms `SpaarkeCore` carries the tables.
**Usage:** 🔴 Per environment, after every solution import into a NEW environment (the seed is not carried by an import); `-Verify` any time. Idempotent.
**Lifecycle:** ✅ Maintained — interim (added 2026-10-02 by `spaarkeai-word-add-in-r1` task 076). Retire when the numbering function ships: clear both formats; keep the keys.
**Dependencies:** Azure CLI (`az login`) with System Administrator in the environment, PowerShell 7+
**Owner:** `spaarkeai-word-add-in-r1`
**Last Used:** 2026-10-02, `-Apply` against `spaarkedev1` (owner-approved after the dry run): matter format + key `sprk_MatterNumber`; project format (key pre-existed); 7 nameless wizard-made projects → `PRJ-000001…007`; `-Verify` PASS; a re-run proposes nothing.

**Command:**
```powershell
# Dry run (default): every write it WOULD make, numbers included. Zero writes.
.\Set-RecordNumberingSchema.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"

# Format, seed, backfill, keys, publish.
.\Set-RecordNumberingSchema.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply

# Check: exit 0 = formats set, keys Active, no blank or duplicate numbers, the next number free.
.\Set-RecordNumberingSchema.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Verify
```

**Safety model:**
- Never renumbers a row that has a number; never overwrites a DIFFERENT format (the numbering function may own it).
- Refuses the key while two rows share a value (production: the owner checks first).
- Platform facts measured live and encoded: `GetNextAutoNumberValue` / `GetAutoNumberSeed` are POST actions; GetNext reads one high until a number is issued after a seed (the script reads the seed in that state); `SetAutoNumberSeed` can refuse `0x80060884` just after the format is set (retried).

Detail: [`projects/spaarkeai-word-add-in-r1/notes/076-record-numbering.md`](../projects/spaarkeai-word-add-in-r1/notes/076-record-numbering.md).

### `Repair-SecureFlagNulls.ps1`
**Purpose:** One-time cleanup: every NULL `sprk_issecure` on `sprk_project` / `sprk_matter` / `sprk_workassignment` becomes No, and the column default is No (owner decision Q1). Records the before/after counts and every record id in a JSON report.
**Usage:** 🔴 One-time per environment, BEFORE deploying a BFF carrying task 150 (that BFF refuses an empty flag); idempotent. `-Verify` any time.
**Lifecycle:** ✅ Maintained (added 2026-10-02 by `unified-access-control-r2` task 150, GitHub #1067)
**Dependencies:** Azure CLI (`az login`), PowerShell 7+
**Owner:** `unified-access-control-r2`
**Last Used:** 2026-10-03, `-Apply` against `spaarkedev1` (gate G-0, owner round 11): 42 NULL rows → No (9 projects, 18 matters, 11 work assignments, 4 invoices — it discovers every entity carrying the column); `-Verify` PASS (`projects/unified-access-control-r2/notes/batch4-live-gates-2026-10-03.md`).

```powershell
.\Repair-SecureFlagNulls.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com"            # dry run (ids + counts)
.\Repair-SecureFlagNulls.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply     # PATCH If-Match:*, report
.\Repair-SecureFlagNulls.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Verify    # exit 0 = no NULL, default No
```

### `Set-ExternalFlagForB2BGuests.ps1`
**Purpose:** Data step that sets `sprk_isexternal = Yes` on B2B guest system users (`#EXT#` in `systemuser.domainname`) who are outsiders; nothing else is changed. Since task 114 (owner round 67) a BLANK flag means NOT external — "+ User" and the Assigned-To rule share with the user, internal-only messages reach them, and a Restricted record keeps their share — so outsider guests must be marked explicitly. JSON report of every guest with its class, home directory and action.
**Modes (one is required):** `-CustomerTenantIds <guid[]>` on a **Model 1** stamp (spaarkeai-word-add-in-r1 task 126, owner 2026-10-13; ADR-028 A8/A9) — the stamp's `WorkforceIdentity__CustomerTenantIds__N` list. Each guest's email domain is resolved with Graph `findTenantInformationByDomainName` (the operator's `az login` Graph token); the customer's own staff (a listed directory, or a `systemuserid` in `-InternalUserIds`) are never written, a guest of another directory not already Yes is set to Yes (No is the column default, not a decision), and an unresolvable or inconsistent guest is reported as "review" and never written. Guards: the stamp's own directory or an empty id is refused as a customer directory; `-Apply` is refused when guests resolve to other directories but none to the customer's. Each PATCH uses the row's ETag. `-AllGuestsExternal` where every guest is an outsider (the task 114 behaviour: every guest still blank or No is set).
**Usage:** 🔴 Once per environment after users are provisioned (customer deployment guide §7.7 / §12.2 Phase 7b), and again when guests are added outside the product: dry run → owner review → `-Apply` → `-Verify` (exit 0 only when nothing is left to set or to review). In an **existing** environment run it **before** deploying the BFF that carries task 114 (that BFF reads a blank flag as internal); if the BFF went first, run it at once and allow 10 minutes (the identity resolver's `sprk_isexternal` cache) after `-Apply`. ⚠️ Within 5 minutes of `-Apply` the BFF removes each newly flagged guest's share on every Restricted record.
**Lifecycle:** ✅ Maintained (added 2026-10-06 by `unified-access-control-r2` task 114, GitHub #1003; Model 1 mode 2026-10-13 by `spaarkeai-word-add-in-r1` task 126)
**Dependencies:** Azure CLI (`az login`, Write on systemuser; a Graph token for `-CustomerTenantIds`), PowerShell 7+. Tests: `tests/scripts/Set-ExternalFlagForB2BGuests.Tests.ps1` (Pester 3.4).
**Owner:** `unified-access-control-r2`
**Last Used:** 2026-10-13, dry run only on spaarkedev1 with `-CustomerTenantIds` (task 126): 0 to set, 0 to review. Never applied.

```powershell
.\Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -CustomerTenantIds "<customer-tenant-guid>"           # Model 1 dry run (report)
.\Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -CustomerTenantIds "<customer-tenant-guid>" -Apply    # PATCH If-Match:<row ETag>, report
.\Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -CustomerTenantIds "<customer-tenant-guid>" -Verify   # exit 0 = nothing to set or review
.\Set-ExternalFlagForB2BGuests.ps1 -EnvironmentUrl "<dv-org-url>" -AllGuestsExternal                                                             # not Model 1: every guest is an outsider
```

### `Set-SecureFlagFieldSecurity.ps1`
**Purpose:** Locks `sprk_issecure` on the three secure roots (and `sprk_invoice`, owner round 10 item 11) with field-level security — only the BFF application user(s) can write it, every business unit's default team reads it — REUSING task 133's `Spaarke BFF-Managed Field Readers/Writers` profiles (it never creates them or edits their membership). Lists every System Administrator holder (owner decision F4).
**Usage:** 🔴 One-time per environment (task 150 step 6), AFTER the BFF and the client that stops writing the flag are deployed; `-Apply` refuses unless the profiles, every default team, the writer membership and the NULL cleanup are in place and `-ClientNoLongerWritesFlag` is passed. `-Verify` any time.
**Lifecycle:** ✅ Maintained (added 2026-10-02 by `unified-access-control-r2` task 150, GitHub #1067)
**Dependencies:** Azure CLI (`az login`) with System Administrator, PowerShell 7+; `Set-RecordCreatorPersonSchema.ps1 -Apply` run first
**Owner:** `unified-access-control-r2`
**Last Used:** 2026-10-02, **dry run only** against `spaarkedev1`: both profiles missing (task 133's schema gate not yet run), NULL rows present. **No `-Apply` has been run.**

```powershell
.\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -BffApplicationIds <ids>             # dry run
.\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -BffApplicationIds <ids> -ClientNoLongerWritesFlag -Apply
.\Set-SecureFlagFieldSecurity.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -BffApplicationIds <ids> -Verify
```

Detail: [`docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md`](../docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md) §7d.

### `Migrate-SecureRecordsToNamedOwnerTeam.ps1`
**Purpose:** One-time move of every secure project, matter and work assignment off the Secure Record business unit's DEFAULT owner team and onto its NAMED, non-default, memberless owner team (`Secure Record Owners`). Secure rows outside the business unit are reported as NOT ISOLATED and never touched.
**Usage:** 🔴 One-time per environment, during the setup-guide §4.3 cutover; idempotent (a second run plans nothing). `-Verify` any time.
**Lifecycle:** ✅ Maintained (added 2026-10-01 by `unified-access-control-r2` task 144, GitHub #967)
**Dependencies:** Azure CLI (`az login`) with System Administrator in the environment, PowerShell 7+
**Owner:** `unified-access-control-r2`
**Last Used:** 2026-10-01, **dry run and `-Verify` only** against `spaarkedev1`: plan 0 rows; 1 NOT-ISOLATED test project; STOPs for the named team (not yet created) and the unaccepted Assign-cascade list. **No `-Apply` has been run.**

**Command:**
```powershell
# Dry run (default): every check, the census and the plan. Zero writes. Exit 2 when a STOP applies.
.\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -ReportPath .\logs\secure-owner-migration.json

# Move the planned rows (each owner read back; share counts compared before/after), once the owner accepts the cascade list.
.\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Apply -AcceptedAssignCascade team,sharepointdocumentlocation,sharepointdocument

# Gate: exit 0 = every secure row on the named team, team memberless, BU user-free, role on the named team alone.
.\Migrate-SecureRecordsToNamedOwnerTeam.ps1 -EnvironmentUrl "https://spaarkedev1.crm.dynamics.com" -Verify
```

**Safety model:**
- It never shares to anyone, moves a user, provisions a record or changes a role.
- It refuses `-Apply` in any of these cases:
  - `sharetopreviousowneronassign` is true;
  - the named team or the default team has members;
  - a user sits in the business unit;
  - the named team lacks the role;
  - a root relationship cascades Assign to a child table that is not in `-AcceptedAssignCascade`.
- It stops at the first row whose owner did not land, or whose share count changed.

Detail: [`projects/unified-access-control-r2/notes/task-144-named-secure-owner-team.md`](../projects/unified-access-control-r2/notes/task-144-named-secure-owner-team.md); setup guide §4.3.

---

## Testing & Validation Scripts

### `Test-SpeContainerPermissionPaging.ps1`
**Purpose:** Settles one open question — does `GET /storage/fileStorage/containers/{id}/permissions` actually emit `@odata.nextLink`? `unified-access-control-r2` task 024 fixed both ExternalAccess reads to follow it, but could only verify the CODE defect, not the exploitability: Microsoft documents `$skip`/`$top`/`$orderBy`/`$filter` and **not** `$skiptoken` for this endpoint, and the sample response carries no `nextLink`. **Read-only** — issues GETs, creates/modifies/deletes nothing.
**Usage:** 🔵 One-shot — run once against dev, record the verdict, then the question is closed.
**Lifecycle:** ✅ Maintained (added 2026-09-09 by `unified-access-control-r2` task 024)
**Dependencies:** A Microsoft Graph token whose app holds `FileStorageContainer.Selected` **and** the container-type-level grant for Spaarke's container type. The BFF's own app registration has both.
**Owner:** `unified-access-control-r2` → hands the answer to task 047 (live verification)

**When to Use:**
- Before task 047 asserts anything about live multi-page enumeration — this says whether there is anything to assert
- If SPE container permissions ever start behaving unexpectedly at scale

⚠️ **An `az account get-access-token --resource https://graph.microsoft.com` token will NOT work.** That is the Azure CLI's own delegated identity; it 403s with `accessDenied` — "Caller does not have required permissions for this API" (confirmed 2026-09-09). This is why the question is still open.

💡 **It does not need a container seeded past a page boundary.** The design assumed that was required, which made the check look expensive. `$top` is documented as supported, so capping the page below the total is the condition that produces a `nextLink` if the service pages at all — probe 2 does exactly that, and any container with ≥2 permissions suffices.

**Command:**
```powershell
pwsh scripts/Test-SpeContainerPermissionPaging.ps1 -ContainerId 'b!...' -AccessToken $tok
```

**Related**: `projects/unified-access-control-r2/notes/task-024-spe-paging-parity.md` §1 (record the verdict there); `notes/decisions/spe-paging-and-revoke-honesty-design.md` §0; GitHub #968, #969.

---

### `Test-SpeConfigSecretNames.ps1`
**Purpose:** READ-ONLY. Lists the SPE container-type configs whose `sprk_keyvaultsecretname` is outside the allow-list
(ONE pinned prefix, `spe-owning-app-`; unified-access-control-r2 task 165, owner round 35 item 3). **Since main-session round
65 the BFF reads no secret by this name and refuses no config for it** — SPE Admin authenticates as the BFF's own identity
(master `bb8ba7251`), and the BFF applies the prefix only as a 400 on a SUPPLIED name. The one remaining reader is
`Backfill-SpeContainerBusinessUnitStamp.ps1`, which lists a config's containers as its owning app and skips a config whose
stored name does not conform (it never reads that secret). Run this check before the backfill; a listed config is repaired
with `Repair-SpeConfigSecretName.ps1` below, or its containers are bound with the backfill's explicit `-Bind`.
**Usage:** Per environment, before `Backfill-SpeContainerBusinessUnitStamp.ps1`. **Lifecycle:** ✅ Maintained (task 165 f2,
2026-10-04; narrowed by round 65)

```powershell
.\Test-SpeConfigSecretNames.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -Verify   # exit 1 while any config does not conform
```

**Shared module:** `common/SpeConfigSecretNamePolicy.ps1` — THE PowerShell copy of the BFF's allow-list (`$SpeConfigSecretNamePrefix`,
`Test-SpeConfigSecretNameAllowed`; the expression ends at `\z`, never `$`, which also matches before a trailing newline —
round 41 item 5). Dot-sourced by this script, `Repair-SpeConfigSecretName.ps1` and the container-binding backfill;
`SpeAdminContainerBindingGuardTests` pins it equal to the C# rule and fails on a script that reads a config's secret
without it.

### `Repair-SpeConfigSecretName.ps1`
**Purpose:** Repairs ONE config the check above lists, so the container-binding backfill can list its containers
(unified-access-control-r2 task 165, owner round 41 item 4; since round 65 not a BFF deploy gate — the BFF reads no such
secret): stores
the config's owning-app client secret under a conforming name in the BFF Key Vault(s) and PATCHes the config's
`sprk_keyvaultsecretname` to it (If-Match on its ETag). The value comes from a vault that already holds the name (a repeat
run mints nothing), else `-MintClientSecret` (Graph `addPassword` on the owning app — an Entra write, existing credentials
kept; the keyId is printed, the value never) or `-SourceKeyVaultName`/`-SourceSecretName`. Never deletes the config, never
overwrites a stored secret.
**Usage:** Per non-conforming config the backfill must list (manual step). **Lifecycle:** ✅ Maintained (task 165 f2-v1, 2026-10-05)
**Dependencies:** Azure CLI (`az login` with Dataverse write on the config, Key Vault secret get/set on each vault, and —
for `-MintClientSecret` — rights to add a credential to the owning app), PowerShell 7+
**Safety model:** dry run by default; `-Apply` writes; `-Verify` (read-only) exits 0 only when the config names the secret,
every vault holds the same value, and that value authenticates as the owning app (a client-credentials token request).

```powershell
.\Repair-SpeConfigSecretName.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -ConfigId <config-id> -SecretName spe-owning-app-<name> -KeyVaultName <bff-kv>,<operator-kv>   # dry run
.\Repair-SpeConfigSecretName.ps1 ... -MintClientSecret -Apply
.\Repair-SpeConfigSecretName.ps1 ... -Verify   # must exit 0
```

### `tests/bicep-e2e-dry-run.ps1`
**Purpose:** Wave C2 Bicep integration test — runs `az bicep build` on the 3 Wave C2 stacks (customer.bicep, platform.bicep, platform-controlplane.bicep — stacks/model1-shared.bicep was retired by task 225a) + optional `az deployment sub what-if` against dev + structural assertions on Wave C2 acceptance (UAMI both-slots binding, module count, no CI-workflow edits). Persists a machine-readable notes artifact per run.
**Usage:** 🟡 Occasional - Before any PR that modifies infrastructure/bicep/** or after Wave C2 changes land; recurring pre-Phase F gate per customer-provisioning-orchestration-r1 spec FR-04.
**Lifecycle:** ✅ Maintained (added 2026-08-17 by customer-provisioning-orchestration-r1 task 034)
**Dependencies:** Azure CLI (`az login`), Bicep CLI (`az bicep install`), dev subscription context (for `-Mode DryRun/Full`)
**Owner:** Platform Team (customer-provisioning-orchestration-r1)

**When to Use:**
- Before merging any PR that touches `infrastructure/bicep/**`
- After any Wave C2 task lands (027-033), to verify composition coherence
- As Phase F gate to confirm the 3-stack composition still dry-runs cleanly
- Nightly / scheduled runs (Phase H CI-wiring coordinated PR)

**Command:**
```powershell
# Tier 1 only: fast build check on all 3 stacks (no dev sub needed)
pwsh scripts/tests/bicep-e2e-dry-run.ps1

# Tier 1 + Tier 2: adds live what-if against dev subscription
pwsh scripts/tests/bicep-e2e-dry-run.ps1 -Mode DryRun

# Tier 1 + Tier 2 + Tier 3 (full — recommended): adds structural assertions
pwsh scripts/tests/bicep-e2e-dry-run.ps1 -Mode Full
```

**Related**: `projects/customer-provisioning-orchestration-r1/tasks/034-integration-test-bicep-dry-run.poml` (task POML); `projects/customer-provisioning-orchestration-r1/spec.md` FR-04; `projects/customer-provisioning-orchestration-r1/notes/bicep-e2e-dry-run-*.md` (per-run notes artifacts).

**Retired stack**: `stacks/model1-shared.bicep` was retired by customer-provisioning-orchestration-r1 task 225a (2026-10-01, D-12); the script now builds 3 stacks and must be green (no `EXPECTED_FAILURE` mechanism any more). The old "build fails since 2026-08-17" deferral was a Windows artifact — `az bicep build --stdout` crashing on non-ASCII output — not a broken stack (the script now builds to a temp file).

---

### `Validate-DeployedEnvironment.ps1`
**Purpose:** Comprehensive validation of a deployed Spaarke environment — checks Dataverse Environment Variables (7 required), BFF API health, CORS origin configuration, and dev value leakage detection
**Usage:** 🟢 Active - After deploying to any non-dev environment
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), PAC CLI (`pac auth create`), target Dataverse environment accessible
**Owner:** DevOps Team
**Last Used:** March 2026

**When to Use:**
- After deploying to production, staging, or demo environments
- After updating Dataverse Environment Variable values
- After changing BFF API CORS configuration
- As final gate in the production environment setup checklist

**Command:**
```powershell
# Validate with auto-detected BFF URL (reads from Dataverse env var)
.\Validate-DeployedEnvironment.ps1 -DataverseUrl "https://myorg.crm.dynamics.com"

# Validate with explicit BFF API URL
.\Validate-DeployedEnvironment.ps1 -DataverseUrl "https://myorg.crm.dynamics.com" -BffApiUrl "https://api.spaarke.com"
```

**Checks:**
- Dataverse Environment Variables: sprk_BffApiBaseUrl, sprk_BffApiAppId, sprk_MsalClientId, sprk_TenantId, sprk_AzureOpenAiEndpoint, sprk_ShareLinkBaseUrl, sprk_SharePointEmbeddedContainerId
- BFF API: /healthz and /ping return HTTP 200
- CORS: BFF API allows requests from the Dataverse org URL
- Dev Leakage: No dev-only values (spaarkedev1, spe-api-dev, 67e2xz, etc.) in env var values

**Exit Codes:**
- `0` — All checks pass (or pass with warnings)
- `1` — One or more checks failed

---

### `Test-SdapBffApi.ps1`
**Purpose:** Test SDAP BFF API endpoints (health, auth, file operations)
**Usage:** 🟢 Active - Validate API after deployment
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (az login), API deployed
**Owner:** Development Team

**When to Use:**
- After API deployment
- Troubleshooting API issues
- Validating configuration changes

**Command:**
```powershell
.\Test-SdapBffApi.ps1 -Environment "dev" -Verbose
```

**Tests:**
- Health endpoint
- Authentication flow
- File upload/download
- OBO token exchange

---

### `test-sdap-api-health.js`
**Purpose:** Node.js health check for SDAP API
**Usage:** 🟡 Occasional - Quick health validation
**Lifecycle:** ✅ Maintained
**Dependencies:** Node.js
**Owner:** Development Team

**When to Use:**
- Quick API health check
- Monitoring scripts
- Integration tests

**Command:**
```bash
node test-sdap-api-health.js https://your-api-url.azurewebsites.net
```

---

### `test-dataverse-connection.cs`
**Purpose:** Test Dataverse ServiceClient connectivity
**Usage:** 🔴 One-time - Troubleshoot connection issues
**Lifecycle:** 📦 Archive (diagnostic)
**Dependencies:** .NET 8, Dataverse SDK
**Owner:** Development Team

**When to Use:**
- Debugging Dataverse connection problems
- Validating ServiceClient configuration
- Testing authentication

**Command:**
```bash
dotnet run test-dataverse-connection.cs
```

---

## SharePoint Embedded Setup Scripts

**Status:** 📦 Archive - One-time environment setup

These scripts were used during initial SPE Container Type registration and are kept for reference when setting up new environments.

### Container Type Management

#### `Create-ContainerType-PowerShell.ps1`
**Purpose:** Create SPE Container Type using PowerShell
**Usage:** 🔴 One-time - Initial setup
**Lifecycle:** 📦 Archive
**Last Used:** Phase 1 setup

**When to Use:**
- Setting up new SPE environment
- Creating additional Container Types

---

#### `Create-NewContainerType.ps1`
**Purpose:** Alternative Container Type creation
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive
**Note:** Prefer Microsoft's official tooling

---

#### `Check-ContainerType-Registration.ps1`
**Purpose:** Verify Container Type registration status
**Usage:** 🔴 One-time - Validate setup
**Lifecycle:** 📦 Archive

---

### Registration & Authentication

#### `Register-BffApi-WithCertificate.ps1`
**Purpose:** Register BFF API with SPE using certificate authentication
**Usage:** 🔴 One-time - Initial setup
**Lifecycle:** 📦 Archive
**Last Used:** Phase 1 SPE integration

**When to Use:**
- Initial SPE registration
- Certificate renewal
- New environment setup

---

#### `Register-BffApi-WithCertificate-Direct.ps1`
**Purpose:** Direct certificate registration (alternative approach)
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Register-BffApi-PnP.ps1`
**Purpose:** Register using PnP PowerShell
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive
**Note:** Superseded by direct Graph API approach

---

#### `Register-BffApiWithContainerType.ps1`
**Purpose:** Link BFF API registration to Container Type
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Import-And-Register.ps1`
**Purpose:** Import certificate and perform registration
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

### Diagnostics & Troubleshooting

#### `Find-ContainerTypeOwner.ps1`
**Purpose:** Find Container Type owning application
**Usage:** 🔴 One-time - Troubleshooting
**Lifecycle:** 📦 Archive (diagnostic)

---

#### `Find-ContainerTypeOwner-AzCli.ps1`
**Purpose:** Find owner using Azure CLI
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Get-ContainerMetadata.ps1`
**Purpose:** Retrieve Container Type metadata
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Get-ContainerMetadata-PCFApp.ps1`
**Purpose:** Get metadata from PCF app context
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Debug-RegistrationFailure.ps1`
**Purpose:** Diagnose SPE registration failures
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Decode-SharePointPermissions.ps1`
**Purpose:** Decode SharePoint permission strings
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

#### `Test-SharePointToken.ps1`
**Purpose:** Test SharePoint token acquisition
**Usage:** 🔴 One-time
**Lifecycle:** 📦 Archive

---

## eBilling Scripts

Owned by `ebilling-r1` (`projects/ebilling-r1/`). All are idempotent: a second run reports no change.

The `Deploy-Ebilling*Schema.ps1` scripts share one helper module, `ebilling/EbillingSchema.psm1` (Dataverse calls, re-reads of eventually consistent metadata, create-or-skip with every "already exists" code, required-level helpers, publish locks); a hardening fix is made there once. Required levels: ApplicationRequired is enforced on forms only, SystemRequired on every write (decisions: `projects/ebilling-r1/notes/schema-script-hardening.md`).

### `ebilling/Deploy-EbillingInvoiceLineSchema.ps1`

Additive Dataverse metadata script that turns `sprk_billingevent` into the Invoice Line (FR-04 / FR-05): review, coding, adjustment and extraction columns, `sprk_linestatus`, the work-assignment lookup and the ADR-024 resolver fields. The UTBMS code lookups are deferred until `sprk_billingcode` exists (task 018) and are created on the next run after that (done in dev 2026-10-15). Supports `-WhatIf`; never deletes or renames. Columns: `docs/data-model/sprk_billingevent.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -WhatIf
pwsh scripts/ebilling/Deploy-EbillingInvoiceLineSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
```

### `ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1`

Additive Dataverse metadata script that extends `sprk_invoice` for eBilling (FR-05 / FR-06 / FR-07, D-43): the `sprk_regardingworkassignment` lookup, the five totals (fees, expenses, line adjustments, invoice adjustments, approved), the `sprk_supersedesinvoice` self-lookup, the date-only billing period, and the lifecycle `statuscode` values (In Review, Approved, Sent to AP on statecode 0; Paid, Rejected, Returned to Firm, Cancelled on statecode 1). It counts invoices at statecode 1 first and stops with exit 2, before any write, if any exist (value 2 would be relabelled Cancelled). Supports `-WhatIf`; never deletes or renames by logical name; retries on customization / publish locks and tolerates eventually consistent metadata reads. It does not change `CoreAncestorResolver` (task 013). Columns and status table: `docs/data-model/sprk_invoice.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -WhatIf
pwsh scripts/ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
```

### `ebilling/Deploy-EbillingBillingCodeSchema.ps1`

Additive Dataverse metadata script for the UTBMS reference tables (FR-08 / FR-22): creates `sprk_billingcodeset` and `sprk_billingcode`, the alternate key `sprk_billingcode_codeset_code_key` on (`sprk_codeset`, `sprk_code`) (waits for the index to be Active), and `sprk_defaultbillingcodeset` on `sprk_mattertype_ref` and `sprk_practicearea_ref`. Supports `-WhatIf`; never deletes or renames; retries on customization / publish locks and on a new table still propagating; ends with a metadata read-back of everything it owns. Run it before `Seed-EbillingBillingCodes.ps1`, then re-run `Deploy-EbillingInvoiceLineSchema.ps1` so the three code lookups on the Invoice Line are created. Columns and key: `docs/data-model/sprk_billingcode.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -WhatIf
pwsh scripts/ebilling/Deploy-EbillingBillingCodeSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
```

### `ebilling/Seed-EbillingBillingCodes.ps1`

Seeds the eight UTBMS code sets (litigation, counseling, project, bankruptcy, IP patent prosecution, trademark, activity, expense; 161 codes) from `scripts/seed-data/ebilling/utbms-*.csv`, one CSV per set carrying set name, body, version, source URL and download date. Create-only: a set is matched on its name and a code on its alternate key; a missing row is created and an existing row is **never** updated (an admin's `sprk_guidancetext` edit always survives). A re-run with unchanged CSVs makes zero writes. Validates every CSV before any write (duplicate codes in a set stop it with exit 2). Supports `-WhatIf`. Not part of `scripts/seed-data/manifest.yaml`.

```powershell
pwsh scripts/ebilling/Seed-EbillingBillingCodes.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -WhatIf
pwsh scripts/ebilling/Seed-EbillingBillingCodes.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
```

### `ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1`

Additive Dataverse metadata script (FR-08, `ebilling-r1` task 017) that extends the existing `sprk_timekeeper` stub into the firm roster (`sprk_firm`, date-only `sprk_effectivefrom` / `sprk_effectiveto`, `sprk_contact` shown as "Linked contact", `statuscode` 1 Approved + Pending + 2 Inactive) and creates the user-owned `sprk_ratecard` (firm, optional matter / project / work assignment scope, source document, effective dates, Draft / Published / Inactive) and `sprk_ratecardrate` (parental card lookup, optional timekeeper, approved and rack rate money, effective dates). It counts timekeeper rows at statuscode 1 first and stops with exit 2, before any write, if any exist. It revises and extends the shared global choice `sprk_timekeeperrole` to the 20 LEDES timekeeper classifications (label = LEDES name, ExternalValue = LEDES code; existing values kept; the non-LEDES options it first left in place were deleted by `Deploy-EbillingUserProfileRoleChoice.ps1`, D-84) and creates `sprk_classification` on `sprk_timekeeper` and `sprk_ratecardrate` bound to it. `-RemoveStubInvoiceLookup` deletes the obsolete `sprk_timekeeper.sprk_invoice` lookup (owner-approved; refuses if any row or component uses it). Supports `-WhatIf`; deletes nothing else and renames nothing by logical name; retries on customization / publish locks, MetadataCache lag after a table create, and eventually consistent metadata reads. Columns: `docs/data-model/sprk_ratecard.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1
```

### `ebilling/Deploy-EbillingUserProfileRoleChoice.ps1`

Dataverse metadata script for `ebilling-r1` decision D-84 (follow-up to task 017). It gives `sprk_userprofile.sprk_primaryrole` its own global choice `sprk_userprofilerole` ("User Profile Role", the same 14 values and labels the column always had, so `userProfileService.ts` and `StatedProfileReader.cs` keep working), then deletes the 10 non-LEDES options from `sprk_timekeeperrole` so it holds exactly the 20 LEDES classifications. A column cannot be re-bound in place, so it saves the row values and the form / view placements to `projects/ebilling-r1/notes/d84-userprofile-primaryrole-predrop.json`, takes the column off the form (an empty cell with the same id keeps the position) and publishes, deletes the platform-generated `ais_FormFillFieldOptOut_sprk_userprofile_sprk_primaryrole` skill config that blocks the drop (0x8004f01f), drops the column, recreates `sprk_PrimaryRole` on the new choice, puts it back on the form and restores and verifies the values. A re-run after an interrupted run recreates and restores from the saved file. Exits 2 before the first write on a dependency other than the skill config and the forms / views it handles, on a chart / workflow / personal view that uses the column, and (for the option delete) on any row holding one of the 10 values or any column other than `sprk_contactorganization.sprk_role`, `sprk_billingevent.sprk_timekeeperrole`, `sprk_timekeeper.sprk_classification` and `sprk_ratecardrate.sprk_classification` bound to `sprk_timekeeperrole`. Supports `-WhatIf`; a second run reports no change and does not publish. Columns: `docs/data-model/sprk_ratecard.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingUserProfileRoleChoice.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingUserProfileRoleChoice.ps1
```

### `ebilling/Deploy-EbillingPolicyStoreSchema.ps1`

Additive Dataverse metadata script that extends the ontology-owned policy store for billing rules (FR-09, D-26, D-66): the global option set `sprk_policydomain` (Billing, Ontology, Docketing), `sprk_domain` and the `sprk_ruleset` lookup on `sprk_policy`, `sprk_clausereference` / `sprk_clauselocation` / `sprk_consequence` / `sprk_proposalstatus` on `sprk_policyversion`, and the new user-owned table `sprk_ruleset` (domain, source document, scope, firm, `sprk_visibletofirms` defaulting to No, effective dates, not-converted JSON, statuscode Draft / Published / Retired). Never relabels, deletes or renumbers an existing policy column or option and writes no policy row; stops with exit 2 if a planned name exists with another type, target or option list. Supports `-WhatIf`; retries on customization / publish locks, a new table still propagating and stale option reads; ends with a metadata read-back. Columns and values: `docs/data-model/sprk_ruleset.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -WhatIf
pwsh scripts/ebilling/Deploy-EbillingPolicyStoreSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
```

### `ebilling/Deploy-EbillingRuleRejectionReasonSchema.ps1`

Additive Dataverse metadata script (`ebilling-r1` task 077, D-113) that adds `sprk_rejectionreason` (multiline text, max 2,000, not required) to `sprk_policyversion`: the reason an admin gives when rejecting a drafted billing rule. Written only by the BFF reject route; admin-only, never read by a firm projection. Never deletes or renames; stops with exit 2 if the column exists with another type. Supports `-WhatIf` (reads metadata, writes nothing); a second run reports it as skipped and does not publish. Run it before the BFF build that reads the column is deployed. Column: `docs/data-model/sprk_ruleset.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingRuleRejectionReasonSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingRuleRejectionReasonSchema.ps1
```

### `ebilling/Deploy-EbillingSubmissionSchema.ps1`

Additive Dataverse metadata script that creates `sprk_invoicesubmission` (FR-10, D-37), one row per submission attempt: the user-owned table, its tracking-number / channel / processing-status / findings / notice / PO / received-on / correlation columns, the submitter, firm, document, report-document, resulting-invoice and resubmission-of lookups, the ADR-024 `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment` / `sprk_regardingrecordtype` lookups and four resolver text fields, and the firm-scoped alternate key `sprk_invoicesubmission_firm_trackingnumber` on (`sprk_vendororg`, `sprk_trackingnumber`) (waits for the index to be Active). Supports `-WhatIf`; never deletes or retypes; stops with exit 2 on a conflicting same-name item; publishes entity-scoped only when it changed something; retries on customization / publish locks and tolerates eventually consistent metadata reads. Columns: `docs/data-model/sprk_invoicesubmission.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com -WhatIf
pwsh scripts/ebilling/Deploy-EbillingSubmissionSchema.ps1 -EnvironmentUrl https://spaarkedev1.crm.dynamics.com
```

### `ebilling/Deploy-EbillingApprovalSchema.ps1`

Additive Dataverse metadata script (FR-11 / FR-05 / FR-35, `ebilling-r1` task 021) that creates the user-owned `sprk_invoiceapproval` (invoice x core record): `sprk_approvalstatus` (Pending default / Approved / Rejected / Returned), `sprk_approvedamount`, `sprk_eligibleresolvedon`, `sprk_decidedon`, the `sprk_invoice` lookup (raised to SystemRequired, because Dataverse does not enforce ApplicationRequired on API writes), the `sprk_decisionrecord` and `sprk_decidedby` (contact) lookups, the ADR-024 `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment` / `sprk_regardingrecordtype` lookups with four resolver text fields, and the N:N `sprk_invoiceapproval_eligibleapprover_contact` (the queryable eligible-approver store). It first checks (read-only) that every approver assignment lookup on Matter, Project and Work Assignment targets `contact` and stops with exit 2 before any write if not. Supports `-WhatIf`; never deletes or retypes; stops with exit 2 on a conflicting same-name item; publishes entity-scoped only when it changed something; retries on customization / publish locks and tolerates eventually consistent metadata reads. Columns: `docs/data-model/sprk_invoiceapproval.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingApprovalSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingApprovalSchema.ps1
```

### `ebilling/Deploy-EbillingExportSchema.ps1`

Additive Dataverse metadata script (FR-13 / FR-41 / D-21, `ebilling-r1` task 023) that creates the user-owned AP export batch table `sprk_billingexport` (`sprk_name` 100, `sprk_invoicecount`, `sprk_totalamount`, multi-select `sprk_formats` CSV / JSON / XML, `sprk_exportstatus` Generating / Completed / Failed, `sprk_mappingsnapshot` 1,048,576, `sprk_failurereason`), its `sprk_csvdocument` / `sprk_jsondocument` lookups to `sprk_document`, and the single-valued `sprk_invoice.sprk_billingexport` batch lookup that keeps an invoice in at most one batch. It first checks (read-only) that `sprk_invoice` has no other batch link and stops with exit 2 before any write if it does. Supports `-WhatIf`; never deletes or retypes; stops with exit 2 on a conflicting same-name item; publishes entity-scoped only when it changed something; retries on customization / publish locks and tolerates eventually consistent metadata reads (it also waits for the shortened primary-name length to settle before setting the required level, so the second update cannot undo the first). Columns: `docs/data-model/sprk_billingexport.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingExportSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingExportSchema.ps1
```

### `ebilling/Deploy-EbillingApproverRoleSchema.ps1`

Additive Dataverse metadata script (FR-14 / D-33, `ebilling-r1` task 024) that checks the global choice `sprk_billingapproverrole` and its columns on Matter, Project and Work Assignment (labels compared ignoring case, values preserved), and creates the firm billing contact lookup `sprk_organization.sprk_billingcontact` (to `contact`, delete RemoveLink). By default it only reports the Matter column, which is still single-select in dev (DEF-011, #1941). `-ReplaceMatterApproverRole` drops that column and recreates it as a multi-select on the same global choice, and refuses (exit 2, no change) if any Matter row holds a value or anything other than its auto-generated skill config depends on it; run it only with the owner's OK. Supports `-WhatIf`; publishes entity-scoped only when it changed something.

```powershell
pwsh scripts/ebilling/Deploy-EbillingApproverRoleSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingApproverRoleSchema.ps1
```

### `ebilling/Deploy-EbillingExternalIdentifierSchema.ps1`

Additive Dataverse metadata script (FR-12 / FR-05 / D-12 / D-68, `ebilling-r1` task 022) that creates the user-owned table `sprk_externalidentifier` (typed identifiers such as the firm invoice number, firm client-matter number, law-department matter number and PO as quoted): the eight-value local choice `sprk_identifiertype` (system-required), `sprk_identifiervalue`, `sprk_normalizedvalue` (system-required matching value), `sprk_issuingsystem`, the `sprk_issuingorganization` and `sprk_regardinginvoice` lookups, the ADR-024 `sprk_regarding*` lookups and five resolver fields, and the owner-decided alternate key (firm, type, normalized value, core record id). It waits for the key index to be Active and stops with exit 2, choosing no other key, if Dataverse refuses this one. Supports `-WhatIf`; never deletes or retypes; stops with exit 2 on a conflicting same-name item; publishes entity-scoped only when it changed something; retries on customization / publish locks and tolerates eventually consistent metadata reads. Columns and the key tests: `docs/data-model/sprk_externalidentifier.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingExternalIdentifierSchema.ps1
```

### `ebilling/Deploy-EbillingCorrectionSchema.ps1`

Additive Dataverse metadata script (D-64 / FR-31 / D-47, `ebilling-r1` task 027) that creates the user-owned child table `sprk_invoicecorrection`: one structured record per reviewer confirmation or correction (field name, AI value, final value, AI confidence 0-1, the four-value local choice `sprk_outcometype` Corrected / Confirmed individually / Accepted in bulk / Unreviewed, stored `sprk_islabel`, separate `sprk_sourcecode` / `sprk_sourceversion`, reviewer and role, reviewed on), the SystemRequired `sprk_invoice` parent lookup (cascade delete), optional `sprk_invoiceline`, `sprk_reviewedby` -> systemuser, and the ADR-024 `sprk_regarding*` lookups with resolver fields. Queryable per Action version. A second run reports no change. Supports `-WhatIf`; exit 2 on a conflicting existing item.

```powershell
pwsh scripts/ebilling/Deploy-EbillingCorrectionSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingCorrectionSchema.ps1
```

### `ebilling/Deploy-EbillingSettingsSchema.ps1`

Additive Dataverse metadata script (FR-48 / D-59 / D-47 / D-54 / D-56 / D-69, `ebilling-r1` task 025) that creates the audited organization-owned singleton table `sprk_ebillingsetting` and seeds its one default row: auditing on the table and every custom column; the required one-option choice `sprk_singleton` plus the alternate key `sprk_ebillingsetting_singleton` (a second row is refused by the platform); range-checked decimal (0 to 1) and whole-number columns for the rejection threshold, the confidence weights and caps, the related-work window and the coding chunk size; three multi-selects on the global choice `sprk_billingapproverrole`; the AP export mapping; the firm-notice AI toggle; and the `sprk_defaultcodeset` lookup (printed SKIPPED when `sprk_billingcodeset` does not exist; re-run after task 018). The seed row is written only when the table is empty, so an admin edit is never overwritten; the weights and caps are provisional (D-69). Runs only against the dev environment, stops with exit 2 if organization auditing is off, supports `-WhatIf`, publishes entity-scoped only when it changed something, and ends with a metadata read-back. Columns and values: `docs/data-model/sprk_ebillingsetting.md`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingSettingsSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingSettingsSchema.ps1
```

### `ebilling/Remove-EbillingInvoiceLineItem.ps1`

Retires the superseded invoice-line-item table (`ebilling-r1` task 011, D-18: `sprk_billingevent` is the Invoice Line). **Read-only by default**: prints the usage and dependency inventory (rows, forms, views, relationships, app modules, AI skill configs, `RetrieveDependenciesForDelete`) and a READY / BLOCKED verdict. **`-Delete -OwnerApproval '<date and who>'` is destructive** and needs the owner's explicit OK (project `CLAUDE.md` section 5). It refuses with exit 2, changing nothing, if the table holds any row (`-DeleteRows` lifts that only for the four known test rows, after a JSON backup) or any dependency is not an expected one. It then removes the form subgrids and navigation entries, the `sprk_timekeeper` lookup, the AI skill configs and the app module registrations, re-checks that no dependency is left, and deletes the table. Verifies that the entity reads 404 and the `sprk_billingevent`, `sprk_invoice` and `sprk_timekeeper` forms load.

```powershell
pwsh scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1
pwsh scripts/ebilling/Remove-EbillingInvoiceLineItem.ps1 -Delete -OwnerApproval '<date and who>' -WhatIf
```

### `ebilling/Deploy-EbillingFormsViews.ps1`

Builds the WP1 forms, views and navigation in dev (`ebilling-r1` task 029; also the procedure later serial tasks reuse: add the new column names to the `$Spec` table and re-run). Rewrites the stub main form and the Active view of the 11 new WP1 tables (read-only controls, rates / codes / eligible-approver subgrids), adds a `section_ebilling` to the existing `sprk_timekeeper`, `sprk_invoice` and `sprk_billingevent` main forms (the five invoice totals and `sprk_billingevent.sprk_amount` are disabled), creates the "Invoices to review" view on `sprk_invoice`, adds the eBilling sitemap entries, then publishes only the touched entities, the two sitemaps and the two app modules (never PublishAllXml). Idempotent: a second run changes nothing and publishes nothing. `-WhatIf` reads everything and writes nothing. Refuses to publish when another pending form / view of a touched entity would go out too, unless `-AllowPendingCollateral`. Dev only in R1. It cannot add tables to an app (the API silently ignores it); it prints that manual app-designer step.

```powershell
pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingFormsViews.ps1
```

### `ebilling/Deploy-EbillingRolePrivileges.ps1`

Grants table privileges on the eBilling tables for `ebilling-r1` decision D-90 (follow-up to task 029 finding B: non-admins got 0x80040220 on `sprk_invoiceapproval`). For `Spaarke Core User`, and for `Spaarke Basic User` while it holds `sprk_invoice` privileges, it reads the role's own privileges on `sprk_invoice` and mirrors them: on the working tables (`sprk_invoiceapproval`, `sprk_invoicesubmission`, `sprk_invoicecorrection`, `sprk_externalidentifier`, `sprk_billingexport`) the same privileges at the same depth; on the configuration tables (`sprk_ebillingsetting`, `sprk_ratecard`, `sprk_ratecardrate`, `sprk_billingcodeset`, `sprk_billingcode`, `sprk_ruleset`, `sprk_timekeeper`) Read at the invoice Read depth (Global on the organization-owned tables, which offer nothing else) plus AppendTo only where a working table's lookup targets the table (none today). It only ever adds (`AddPrivilegesRole` on the root-business-unit role): a privilege already held at or above the target is skipped, nothing is removed or lowered, and the script stops with exit 2 if a read-back shows a lowered privilege. It reads the added privileges back and checks the per-business-unit role copies picked them up. Supports `-WhatIf` (prints each privilege it would add); a second run reports no change. No publish is involved; the role source under `src/dataverse/solutions/SpaarkeMaster/Roles` is updated from an export (`projects/ebilling-r1/notes/029-wp1-apply-and-export.md`, "Role privileges (D-90)").

```powershell
pwsh scripts/ebilling/Deploy-EbillingRolePrivileges.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingRolePrivileges.ps1
```

### `ebilling/Deploy-EbillingProcedureSchema.ps1`

Creates the Procedure schema for `ebilling-r1` task 080 (EB-D-148, `projects/ebilling-r1/notes/procedure-and-file-model.md` sections 3, 5.2, 6.3, 7.2, 13; replaces the never-run `Deploy-EbillingRuleSetScopeSchema.ps1`). Five global choices (`sprk_proceduretype`, `sprk_procedureappliesto`, `sprk_procedurestatus`, `sprk_procedureversionstatus`, `sprk_publishstate`); the user-owned, audited tables `sprk_procedure`, `sprk_procedureversion` (delete-only cascade to its Procedure, not parental; alternate key on Procedure + version number, both SystemRequired; DateOnly effective dates, `sprk_notconverted`) and `sprk_procedureassignment` (one typed lookup per core table (RemoveLink, nothing cascades: the core-record delete planner of task 117 deletes the rows), mode Follow latest / Pinned / Excluded); on `sprk_ruleset` the record lookups (RemoveLink), the `Record` 100000002 and `Firm record` 100000003 options of `sprk_scope`, `sprk_procedure` and the unsecured `sprk_createdbyperson`; on `sprk_policyversion` `sprk_procedureversion`, `sprk_publishstate` and `sprk_basedonversion`; on `sprk_ratecard` `sprk_procedureversion`; and `sprk_billingfirm` / `sprk_billingresponsible` on every core table. **The core-table list is not typed in the script**: it is read at run time from the `FilingCores` initializer in `InvoiceFamilyCoreRecordGuard.cs` (override with `-GuardSourcePath`), and the script stops with exit 2 and writes nothing when it cannot parse a non-empty list. Restrict only on lookups to a Procedure, a Procedure Version or a firm, never to a core record; an existing lookup with another delete behaviour is not changed (exit 2). Exactly one lookup per table is Delete Cascade (Dataverse counts that as parental and allows one per table): `sprk_procedureassignment.sprk_excludedpolicy` and `sprk_procedureversion.sprk_procedure`, each delete-only (assign, share, unshare, reparent, merge, rollup view NoCascade), read back member by member; an existing core lookup that a first run left Cascade is repaired in place (PUT, read back) before `sprk_excludedpolicy` is created; `sprk_createdbyperson` is also added (unsecured) on `sprk_policy` and `sprk_policyversion`; the verify fails if any role held by a `sprk_isexternal` user (direct or through a team) has a privilege on the three new tables. Field-secures 18 BFF-only columns (reader: Read for every user; writer: the BFF application users only), reusing the existing "Spaarke BFF-Managed Field Readers / Writers" pair without changing any profile's members (`-CreateProfiles` makes an ebilling-owned pair instead). Idempotent check-then-create, entity-scoped publish, SpaarkeMaster header, dev only. Never creates a banner column, `sprk_isexceptions`, a business-unit column or N:N, `sprk_ratecard.sprk_scope` or `sprk_storedfile`. **Needs ontology-r1's full schema-review PASS, the before-notices and the owner's OK before a real run.** `-WhatIf` reads everything (reads only), writes nothing, prints the lookup plan with relationship names and delete behaviours, and lists every item it would create, change or skip. The `FilingCores` parse and the plan are tested by `tests/scripts/Deploy-EbillingProcedureSchema.Tests.ps1`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingProcedureSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingProcedureSchema.ps1
```

### `ebilling/Deploy-EbillingStoredFileSchema.ps1`

Creates the Stored File schema for `ebilling-r1` task 088 (EB-D-148, `projects/ebilling-r1/notes/procedure-and-file-model.md` sections 4.1, 4.2, 5.2, 9.1, 10). F1: the global choice `sprk_storedfilepurpose` (Procedure source, Invoice PDF, Invoice supporting file, Export file; 100000000-100000003). F2: the user-owned, audited table `sprk_storedfile` (primary `sprk_name` = file name, 400) with the parent lookups `sprk_procedureversion`, `sprk_invoice`, `sprk_billingexport` (RemoveLink: the orphan reconcile of task 090 handles a parent deleted outside the BFF), `sprk_purpose`, `sprk_firmvisible` (default No), the local choice `sprk_filestatus` (Not yet servable, Servable, Removed), the source lookups `sprk_sourcedocument` / `sprk_sourcecommunication` (Restrict), `sprk_sourceattachmentidentity` (1000), `sprk_sourceversion` (text 20) and `sprk_sizebytes` (whole number 0..2147483647; BigInt cannot be field-secured, run 1 2026-10-22), `sprk_sourcesha256` / `sprk_sha256` (64), `sprk_contenttype` (255), `sprk_blobpath` (1024) and the unsecured `sprk_createdbyperson` (task 086 secures it); no lookup cascades assign, share, unshare or reparent. F4: the submission marker `sprk_document.sprk_restrictedbysubmission` (two options, default No; written only by task 095's submission path). F5: `sprk_invoice` status reason `Superseded` = 659490005 at statecode 1 (set only by task 097); the pre-flight stops (exit 2, nothing written) when that value or label exists with another meaning or the seven task-012 values differ. F3: field security on every F2 column except `sprk_name` and `sprk_createdbyperson`, and on F4, reusing the "Spaarke BFF-Managed Field Readers / Writers" pair (permission rows only, never a member); `sprk_blobpath` gets the writer row only (server-only). Every column is checked securable before any is secured. The pre-flight stops (exit 2) when `sprk_procedureversion` (task 080) does not exist. Idempotent check-then-create, entity-scoped publish, SpaarkeMaster header, dev only; creates no form, view or role privilege and writes no row. **Needs ontology-r1's full schema-review PASS, the before-notices to ontology-r1 and uac-r2, the owner's OK and task 080's real run before a real run.** `-WhatIf` reads only. The plan and the F5 decision are tested by `tests/scripts/Deploy-EbillingStoredFileSchema.Tests.ps1`.

```powershell
pwsh scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1 -WhatIf
pwsh scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1
```

### `content-understanding/Deploy-Analyzers.ps1`

Deploys Azure AI Content Understanding analyzers from `infrastructure/content-understanding/*.json` (GA API 2025-11-01, Entra token, no keys). Creates a missing analyzer, does nothing when the deployed one matches, and refuses to overwrite a differing one unless `-Replace` is passed. Analyzer ids allow only letters, digits, `.` and `_` (`sprk_invoice_v1`). Supports `-WhatIf`.

## Script Maintenance

### Adding New Scripts

**Process:**
1. Create script with clear naming convention
2. Add entry to this README
3. Include inline documentation (synopsis, parameters, examples)
4. Tag with usage frequency and lifecycle status
5. Update when usage patterns change

**Naming Convention:**
- **Action-Target-Method.ps1** (e.g., `Deploy-SpeAdminApp.ps1`)
- Use PascalCase for PowerShell scripts
- Use kebab-case for JavaScript/Node scripts

### Deprecating Scripts

**When to deprecate:**
- Superseded by better tooling (PAC CLI, official tools)
- No longer relevant to current architecture
- One-time setup completed

**Process:**
1. Update script header with deprecation notice
2. Mark as ⚠️ Deprecated in this README
3. Document replacement tool/process
4. After 6 months with no usage: move to `archive/` or delete

### Archiving Scripts

**Criteria:**
- One-time setup scripts (SPE registration, etc.)
- Historical reference value
- May be needed for new environments

**Process:**
1. Mark as 📦 Archive in this README
2. Keep in `scripts/` folder for easy discovery
3. Note when it was last used

### Removing Scripts

**Criteria:**
- No historical value
- Can be recreated from documentation
- Superseded with no reference value

**Process:**
1. Verify in git history (permanent record)
2. Remove from `scripts/` folder
3. Remove from this README
4. Update any references in documentation

---

## Usage Patterns

### By Development Phase

**Development:**
- `Invoke-PcfBuildProd.ps1` - Production PCF build that fails on a failed build (then the `pcf-deploy` skill)
- `Test-SdapBffApi.ps1` - Validate API changes

**Deployment:**
- `Deploy-Release.ps1` - Full platform release (build + deploy + validate + tag)
- `Build-AllClientComponents.ps1` - Build all client artifacts before deployment
- `Deploy-AllWebResources.ps1` - Deploy all web resources to Dataverse
- `Deploy-CustomPage.ps1` - Deploy custom pages
- CI/CD workflows (preferred over manual scripts)

**Troubleshooting:**
- `test-sdap-api-health.js` - Quick health checks
- `Test-SdapBffApi.ps1` - Comprehensive API validation

**New Environment Setup:**
- SPE Container Type scripts (one-time)
- Registration scripts (one-time)

### By Tool Category

**Deployment Tools:**
- PCF deployment scripts
- Custom page deployment scripts

**Testing Tools:**
- API health checks
- Connection validation scripts

**Setup Tools:**
- SPE registration scripts
- Container Type creation scripts

**Diagnostic Tools:**
- Debug and troubleshooting scripts
- Metadata inspection scripts

---

## Script Dependencies

### Required Tools

**PowerShell Scripts:**
- PowerShell 7+ (cross-platform)
- Azure CLI (`az`)
- PAC CLI (Power Platform CLI)
- .NET 8 SDK

**JavaScript/Node Scripts:**
- Node.js 18+
- npm packages (per script)

**C# Scripts:**
- .NET 8 SDK
- Required NuGet packages

### Credentials & Permissions

Most scripts require:
- Azure AD authentication (`az login`)
- Power Platform admin consent
- Appropriate RBAC roles:
  - Dataverse System Administrator
  - Azure AD Application Administrator (for SPE setup)
  - SharePoint Embedded Administrator

---

## Migration to Better Tooling

### Recommended Replacements

**Instead of custom deployment scripts:**
- Use **PAC CLI** directly
- Use **Power Platform Build Tools** in Azure DevOps/GitHub Actions
- Use **Solution Packager** for solution management

**Instead of custom testing scripts:**
- Use **automated test suites** (xUnit, Playwright)
- Use **API testing frameworks** (Postman, REST Client)
- Use **monitoring tools** (Application Insights, Azure Monitor)

**Instead of setup scripts:**
- Document setup in `docs/setup/`
- Use **Infrastructure as Code** (Bicep, ARM templates)
- Use **official Microsoft tooling** when available

---

## Related Documentation

- **Development Procedures:** `docs/development/`
- **Deployment Guide:** `docs/deployment/`
- **SPE Setup Guide:** `docs/setup/spe-setup.md`
- **API Documentation:** `docs/api/`

---

## Change Log

- **2025-12-02:** Initial script registry created. Removed 21 scripts (deprecated, bash, docs). Established maintenance process.
- **2026-03-31:** Added Deploy-ReportingReports.ps1 — Power BI report template deployment for the Reporting module (Task 035).
- **2026-03-31:** Added Initialize-ReportingCustomer.ps1 — end-to-end customer onboarding for the Reporting module: PBI workspace, SP profile, report deployment, Dataverse module enablement (Task 040).
- **2026-03-31:** Added Deploy-ReportingCodePage.ps1 — builds and deploys the Reporting Code Page (sprk_reporting web resource) to Dataverse using vite-plugin-singlefile single-file output (Task 016).
- **2026-04-04:** Added Release & Deployment Orchestration section with three new scripts: Deploy-Release.ps1 (master release orchestrator), Build-AllClientComponents.ps1 (dependency-ordered client build), Deploy-AllWebResources.ps1 (all web resources to Dataverse) — Task PRPR-032.
- **2026-09-04:** Added new "Data Backfill Scripts" section with Backfill-CoreAncestorStamps.ps1 — one-time FR-26 core-ancestor stamp backfill for existing child records (`unified-access-control-r2` task 053). Discovers ancestor-stamp + child-of-child lookup columns from live Dataverse metadata every run rather than a hard-coded list, after a live-metadata check during authoring found the project's own prior notes stale on which columns `sprk_todo` carries, and found `sprk_invoice`/`sprk_document` structurally cannot carry an ancestor stamp under the current schema (filed for owner decision, not fixed by this script).
- **2026-10-01:** Added Migrate-SecureRecordsToNamedOwnerTeam.ps1. It moves secure roots off the Secure Record business unit's default team onto its named, memberless owner team, with a dry run by default, read-back, share-count comparison and a `-Verify` gate (`unified-access-control-r2` task 144, #967).
- **2026-10-02:** Added Set-RecordNumberingSchema.ps1 — interim `MAT-`/`PRJ-` platform autonumber on the matter/project primary-name columns, data-derived seed, blank-row backfill, alternate keys, `-Verify` (`spaarkeai-word-add-in-r1` task 076, write-path invariant I-11).
- **2026-09-30:** Added Backfill-RecordOwnership.ps1 — re-owns existing app-owned `sprk_document`/`sprk_todo` rows to a business unit default owner team, record-first, with a write-ahead reversal manifest (`spaarkeai-word-add-in-r1` task 080, write-path invariant I-6).
- **2026-10-15:** Added the eBilling Scripts section: Deploy-EbillingInvoiceLineSchema.ps1 (task 010) and content-understanding/Deploy-Analyzers.ps1 (task 004), `ebilling-r1`.
- **2026-10-15:** Added ebilling/Deploy-EbillingInvoiceHeaderSchema.ps1 (sprk_invoice header columns and lifecycle statuscode values, `ebilling-r1` task 012).
- **2026-10-15:** Added ebilling/Deploy-EbillingBillingCodeSchema.ps1 and ebilling/Seed-EbillingBillingCodes.ps1 (UTBMS code set tables, alternate key, default-code-set lookups, eight seeded sets; `ebilling-r1` task 018).
- **2026-10-15:** Added ebilling/Deploy-EbillingPolicyStoreSchema.ps1 (policy store domain / rule set / clause / consequence columns, `sprk_policydomain`, `sprk_ruleset` table; `ebilling-r1` task 019).
- **2026-10-15:** Added ebilling/Deploy-EbillingTimekeeperRateCardSchema.ps1 (sprk_timekeeper roster columns and statuscode, sprk_ratecard, sprk_ratecardrate; classification held pending an owner decision; `ebilling-r1` task 017).
- **2026-10-15:** Added ebilling/Deploy-EbillingSubmissionSchema.ps1 (sprk_invoicesubmission table, ADR-024 regarding lookups, firm-scoped tracking-number alternate key; `ebilling-r1` task 020).
- **2026-10-15:** Added ebilling/Deploy-EbillingApprovalSchema.ps1 (sprk_invoiceapproval table, ADR-024 regarding lookups, SystemRequired invoice lookup, eligible-approver N:N to contact; `ebilling-r1` task 021).
- **2026-10-15:** Added ebilling/Deploy-EbillingExportSchema.ps1 (sprk_billingexport AP batch table, CSV / JSON document lookups, single-valued sprk_invoice.sprk_billingexport batch lookup; `ebilling-r1` task 023).
- **2026-10-15:** Added ebilling/Deploy-EbillingExternalIdentifierSchema.ps1 (sprk_externalidentifier table, ADR-024 regarding lookups, D-68 alternate key on firm, type, normalized value and core record id; `ebilling-r1` task 022).
- **2026-10-15:** Added ebilling/Deploy-EbillingCorrectionSchema.ps1 (sprk_invoicecorrection structured correction record table, four-value outcome type, SystemRequired invoice lookup, ADR-024 regarding lookups; `ebilling-r1` task 027).
- **2026-10-15:** Added ebilling/Deploy-EbillingSettingsSchema.ps1 (sprk_ebillingsetting audited singleton table: alternate-key singleton, range-checked threshold / weights / caps / window / chunk size, approver-role multi-selects, AP mapping, default code set lookup, seeded default row; `ebilling-r1` task 025).
- **2026-10-16:** Added ebilling/Deploy-EbillingFormsViews.ps1 (WP1 main forms, Active views, "Invoices to review" view and eBilling sitemap entries, entity-scoped publish; `ebilling-r1` task 029).
- **2026-10-21:** Added Set-FieldMappingRuleSyncMode.ps1 — one-time, idempotent migration of existing Copy field-mapping rules to sync mode Manual Refresh (`sprk_syncmode = 1`), dry-run by default with `-Apply`, `-Verify` and `-SelfTest` (`field-mapping-server-write-path-r1` task 005, FR-05d).
