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

### `Deploy-Playbook.ps1`
**Purpose:** Create complete AI analysis playbooks in Dataverse from definition JSON files — resolves scope codes, creates playbook + nodes, associates N:N scopes, saves canvas layout
**Usage:** 🟢 Active - Create new playbooks on-demand
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Dataverse connection
**Owner:** AI Team
**Last Used:** March 2026

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

### `Refresh-ScopeModelIndex.ps1`
**Purpose:** Regenerate `docs/ai-knowledge/catalogs/scope-model-index.json` from current Dataverse state — keeps the scope catalog in sync for Claude Code
**Usage:** 🟡 Occasional - After adding new scopes to Dataverse
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Dataverse connection
**Owner:** AI Team
**Last Used:** March 2026

**When to Use:**
- After seeding new actions, skills, knowledge, or tools to Dataverse
- After manually creating scopes in Dataverse UI
- To ensure Claude Code has the latest scope catalog for playbook design

**Command:**
```powershell
.\Refresh-ScopeModelIndex.ps1 -Environment dev
```

---

### `Seed-JpsActions.ps1`
**Purpose:** Seed JPS (JSON Prompt Schema) action definitions to Dataverse — creates/updates `sprk_analysisactions` records with full prompt content
**Usage:** 🟡 Occasional - After creating or modifying JPS files
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI, JPS files in `projects/ai-json-prompt-schema-system/notes/jps-conversions/`
**Owner:** AI Team
**Last Used:** March 2026

**Command:**
```powershell
.\Seed-JpsActions.ps1
```

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
# Preview what would be created (recommended first)
.\Load-DemoSampleData.ps1 -DryRun

# Load all sample data
.\Load-DemoSampleData.ps1

# Load into a different environment
.\Load-DemoSampleData.ps1 -EnvironmentUrl "https://myenv.crm.dynamics.com"

# Skip optional steps
.\Load-DemoSampleData.ps1 -SkipAiSeedData -SkipSpeUpload -SkipAiIndexing

# Force recreate existing records
.\Load-DemoSampleData.ps1 -Force
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

## Entra ID & Identity Scripts

### `Register-EntraAppRegistrations.ps1`
**Purpose:** Create production Entra ID app registrations (BFF API + Dataverse S2S) and store credentials in Key Vault
**Usage:** 🔴 One-time - Production environment setup
**Lifecycle:** ✅ Maintained
**Dependencies:** Azure CLI (`az login`), Entra ID admin permissions, Key Vault access
**Owner:** DevOps Team
**Last Used:** March 2026

**When to Use:**
- Setting up production Entra ID app registrations
- Recreating registrations in a new tenant
- After tenant migration

**Command:**
```powershell
# Preview what will be created
.\Register-EntraAppRegistrations.ps1 -DryRun

# Create both registrations
.\Register-EntraAppRegistrations.ps1

# Create only Dataverse S2S (BFF already done)
.\Register-EntraAppRegistrations.ps1 -SkipBffApi
```

**Creates:**
- `spaarke-bff-api-prod` — BFF API with Graph + Dynamics CRM delegated permissions
- `spaarke-dataverse-s2s-prod` — Dataverse server-to-server authentication
- Key Vault secrets: TenantId, BFF-API-ClientId, BFF-API-ClientSecret, BFF-API-Audience, Dataverse-S2S-ClientId, Dataverse-S2S-ClientSecret

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

# Test with explicit credentials
.\Test-EntraAppRegistrations.ps1 -BffApiClientId "abc123" -BffApiClientSecret "secret"

# Test with Dataverse token acquisition
.\Test-EntraAppRegistrations.ps1 -DataverseOrgUrl "https://spaarke-demo.crm.dynamics.com"
```

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
- `-SkipPhase` — Array of phase names to skip: `Build`, `BffApi`, `Solutions`, `WebResources`, `Validation`
- `-SkipBuild` — Shortcut for `-SkipPhase Build`
- `-StopOnFailure` — Stop deploying to remaining environments if a deployment fails (default: `$true`)
- `-ClientSecret` — Service principal client secret for Dataverse solution import (falls back to `SPAARKE_SP_CLIENT_SECRET` env var)

**Pipeline Phases:**
| Phase | Script Called | Description |
|-------|-------------|-------------|
| 0 | (built-in) | Pre-flight checks: git clean, branch, auth, BFF URL validation |
| 1 | `Build-AllClientComponents.ps1` | Build all client components in dependency order |
| 2 | `Deploy-BffApi.ps1` | BFF API deployment (per environment) |
| 3 | `Deploy-DataverseSolutions.ps1` | Dataverse solution import (per environment) |
| 4 | `Deploy-AllWebResources.ps1` | Web resource deployment (per environment) |
| 5 | `Validate-DeployedEnvironment.ps1` | Post-deploy validation (per environment) |
| 6 | (built-in) | Tag release in git |

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

# Preview what would happen when building PCF controls
.\scripts\Build-AllClientComponents.ps1 -Component PCF -WhatIf
```

**Parameters:**
- `-SkipSharedLibs` — Skip shared library builds (step 1). Use when shared libs are already built.
- `-Component` — Build only specific components by name. Accepts an array of component names matching directory names (e.g., `LegalWorkspace`, `AnalysisWorkspace`, `PCF`). Special names: `SharedLibs`, `PCF`, `ExternalSPA`.

**Build Order:**
1. Shared libraries (`Spaarke.Auth`, `Spaarke.SdapClient`, `Spaarke.UI.Components`)
2. Vite solutions (20 projects in `src/solutions/`)
3. Webpack code pages (4 projects in `src/client/code-pages/`)
4. PCF controls (`src/client/pcf/`)
5. External SPA (`src/client/external-spa/`)

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
.\scripts\Deploy-AllWebResources.ps1 -SkipComponent RibbonIcons, PCFWebResources

# Preview which components would be deployed
.\scripts\Deploy-AllWebResources.ps1 -WhatIf
```

**Parameters:**
- `-DataverseUrl` — Target Dataverse environment URL (falls back to `DATAVERSE_URL` env var)
- `-SkipComponent` — Array of component names to skip: `CorporateWorkspace`, `ExternalWorkspaceSpa`, `SpeAdminApp`, `WizardCodePages`, `EventsPage`, `PCFWebResources`, `RibbonIcons`

**Deployment Sequence:**
| # | Script Called | Web Resource |
|---|-------------|--------------|
| 1 | `Deploy-CorporateWorkspace.ps1` | `sprk_corporateworkspace` (HTML) |
| 2 | `Deploy-ExternalWorkspaceSpa.ps1` | `sprk_externalworkspace` (HTML + inline JS) |
| 3 | `Deploy-SpeAdminApp.ps1` | `sprk_speadmin` (HTML) |
| 4 | `Deploy-WizardCodePages.ps1` | 12 wizard/code page web resources |
| 5 | `Deploy-EventsPage.ps1` | `sprk_eventspage.html` |
| 6 | `Deploy-PCFWebResources.ps1` | PCF bundle.js + CSS |
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
- The Reporting Code Page uses `vite-plugin-singlefile`, making the dist output a single file — no manual JS inlining step needed (contrast with `Deploy-ExternalWorkspaceSpa.ps1`)
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

#### `Deploy-PCFWebResources.ps1`
**Purpose:** Deploy PCF control web resources to Dataverse
**Usage:** 🟢 Active - Deploy after PCF build
**Lifecycle:** ✅ Maintained
**Dependencies:** PAC CLI, Dataverse connection
**Owner:** Development Team
**Last Used:** Phase 8 (File Viewer deployment)

**When to Use:**
- After building PCF control (`npm run build`)
- Deploying updates to existing controls
- Testing PCF changes in Dataverse environment

**Command:**
```powershell
.\Deploy-PCFWebResources.ps1 -ControlName "UniversalQuickCreate" -Environment "dev"
```

**Alternatives:**
- PAC CLI: `pac pcf push`
- Power Platform Build Tools (CI/CD)

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

## Testing & Validation Scripts

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

## Script Maintenance

### Adding New Scripts

**Process:**
1. Create script with clear naming convention
2. Add entry to this README
3. Include inline documentation (synopsis, parameters, examples)
4. Tag with usage frequency and lifecycle status
5. Update when usage patterns change

**Naming Convention:**
- **Action-Target-Method.ps1** (e.g., `Deploy-PCFWebResources.ps1`)
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
- `Deploy-PCFWebResources.ps1` - After PCF changes
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
