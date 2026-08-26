# Phase F Verification Harness — Trap, Invariant, Naming, Cost Checks

> **Project**: customer-provisioning-orchestration-r1
> **Task**: 089 (Phase F E2E Acceptance) — SPLIT MODE scaffolding half
> **Author**: subagent (scaffolding-only dispatch, 2026-08-18)
> **Purpose**: Give the owner exact, copy-paste-ready commands to verify each of the 6 §4B silent-fail traps, 5 §4D tenant-isolation invariants, naming conformance, and cost envelope against the live `trial-2026-08-18` Model 2 (dedicated) stamp.
> **Primary path**: Model 2 (`tenancyModel=Model2Dedicated`) per the 2026-08-18 Path A exception (see task 089 POML `<constraints>`). Where a check differs for Model 1, the Model 1 variant is noted — use it only if the owner performs the discretionary Model 1 run.
> **Placeholders**: `{customerId}` = `trial-2026-08-18` (or actual value used), `{tenantId}` = the customer's Entra tenant GUID supplied at intake, `{env}` = `dev`, `{rg}` = the dedicated resource group name from H1 output (e.g. `rg-trial-2026-08-18-dev`), `{envRecordId}` = the `sprk_dataverseenvironment` record GUID for the customer.
> **Reference**: design.md §4B (trap catalog), §4D (tenant-isolation invariants), §4.1a (Model 1/2 differences), §15 #14 (cost envelope), §7.9 (naming conformance).

---

## How to use this document

Each section below is one verification. Run the "How to verify" command against the live stamp after the corresponding handler completes (traps are owned by specific handlers — see the table). Record the actual output in `notes/phase-f-report-skeleton.md` (or its filled copy `notes/phase-f-e2e-acceptance-2026-08-18.md`) under the matching row.

Do NOT run these commands until `/provision-environment` has advanced past the owning handler for that check — running early will produce a false FAIL (resource doesn't exist yet), not a real failure.

---

## T1 — `keyVaultReferenceIdentity` not PATCHed to UAMI

**What it verifies**: App Service's `keyVaultReferenceIdentity` setting resolves `@Microsoft.KeyVault(...)` app-settings using the UAMI (not the default System-Assigned identity or none) — otherwise all KV-ref settings silently become `null` at BFF runtime.

**How to verify**:

```powershell
az webapp show `
  --resource-group {rg} `
  --name spaarke-bff-{customerId}-{env} `
  --query "keyVaultReferenceIdentity" -o tsv

# Compare against the UAMI resource ID:
az identity show `
  --resource-group {rg} `
  --name uami-{customerId}-{env} `
  --query "id" -o tsv
```

Run for BOTH the production slot and the staging slot (T5 note: current pattern is per-slot MI unless Phase C UAMI migration has landed for this stamp — see T5 below):

```powershell
az webapp deployment slot list `
  --resource-group {rg} `
  --name spaarke-bff-{customerId}-{env} `
  --query "[].name" -o tsv
# for each slot:
az webapp show `
  --resource-group {rg} `
  --name spaarke-bff-{customerId}-{env} `
  --slot {slotName} `
  --query "keyVaultReferenceIdentity" -o tsv
```

**Expected output (PASS)**: Both commands return the SAME resource ID string (the UAMI's `id`). Both slots (if slots exist for this stamp's tier) return a matching, non-null value.

**Failure mode (FAIL)**: `keyVaultReferenceIdentity` is empty/null, `"SystemAssigned"`, or a resource ID that does NOT match the UAMI. Likely means H4's PATCH step didn't run or targeted the wrong identity. Downstream symptom: BFF `/health` fails at boot (post-D20 fail-fast — see T1 secondary check below) OR (pre-D20 behavior) BFF boots but every `Dataverse:ClientSecret`-style config resolves to null and the first Dataverse call 500s.

**Secondary check (post-D20 fail-fast confirms T1 indirectly)**:

```powershell
curl -sf https://spaarke-bff-{customerId}-{env}.azurewebsites.net/healthz
```

Expected: HTTP 200. If T1 is broken, `/healthz` should fail at boot (r3 task 061 `ValidateOnStart()` on Tier-1 IOptions) rather than passing with null secrets — a 200 here is a legitimate T1 PASS signal, but the direct ARM read above is still the primary check.

---

## T2 — MI not registered as Dataverse Application User

**What it verifies**: the customer's dedicated Dataverse environment has a `systemuser` row for the UAMI's application ID — without it, every BFF→Dataverse call 403s (surfaces as 500 to callers).

**How to verify**:

```powershell
# Get the UAMI's application (client) ID
$uamiAppId = az identity show `
  --resource-group {rg} `
  --name uami-{customerId}-{env} `
  --query "clientId" -o tsv

# Query Dataverse (via Dataverse MCP preferred; pac data fallback shown)
pac data query --entity systemusers `
  --filter "applicationid eq '$uamiAppId'" `
  --select systemuserid,fullname,applicationid,isdisabled
```

Or via MCP: `mcp__dataverse__read_query` against `systemusers` with filter `applicationid eq '{uamiAppId}'`.

**Expected output (PASS)**: Exactly 1 row returned; `isdisabled = false`.

**Failure mode (FAIL)**: 0 rows → H10 didn't create the Application User (or targeted the wrong Dataverse environment URL). 2+ rows → duplicate registration, investigate which one is active. Downstream symptom: any BFF endpoint touching Dataverse returns 500 with an underlying 403 in App Insights traces.

---

## T3 — UAMI Graph app-role parity broken

**What it verifies**: all 14 Graph app-roles enumerated in `Infrastructure/Auth/GraphAppRoles.cs` are replicated onto the UAMI's service principal — a partial set means app-only Graph calls (SPE, mail, groups, Teams) 403 despite delegated flow working fine.

**How to verify**:

```powershell
# UAMI's service principal object ID
$uamiSpObjId = az ad sp show --id $uamiAppId --query "id" -o tsv

# Current app-role assignments on the UAMI SP
az rest --method GET `
  --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$uamiSpObjId/appRoleAssignments" `
  --query "value[].appRoleId" -o tsv
```

Compare the returned list against the 14 `AppRoleId` GUIDs in `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs` (all should be non-null per task 005 completion — flag immediately if any GUID in the source file is still null, that is a P0 blocker independent of this stamp).

**Expected output (PASS)**: The returned `appRoleId` list contains all 14 GUIDs from `GraphAppRoles.cs` (order doesn't matter; set equality).

**Failure mode (FAIL)**: Fewer than 14 entries, or entries that don't match the constant. Likely H10's Graph-role-sync step didn't run to completion, hit a 429/throttle mid-sync, or `GraphAppRoles.cs` itself has a stale/null GUID. Downstream symptom: SPE file operations, mail send, or group-membership calls 403 in production despite the BFF passing its delegated-auth smoke test.

---

## T4 — Only one Exchange ApplicationAccessPolicy created

**What it verifies**: BOTH the BFF app-reg AND the UAMI have an Exchange `ApplicationAccessPolicy` entry — a policy covering only one principal means app-only mail calls from the other principal scope-fail.

**How to verify** (requires Exchange Online PowerShell module + admin connection):

```powershell
Connect-ExchangeOnline -Organization {customerTenantDomain} # or app-only cert auth per H14 pattern

Get-ApplicationAccessPolicy | Where-Object {
  $_.AppId -in @($bffAppRegAppId, $uamiAppId)
} | Select-Object AppId, PolicyScopeGroupId, AccessRight
```

**Expected output (PASS)**: Exactly 2 rows returned — one per `AppId` (`$bffAppRegAppId` and `$uamiAppId`), both with the expected `AccessRight` (typically `RestrictAccess` or `AllowAccess` depending on policy design) and matching `PolicyScopeGroupId`.

**Failure mode (FAIL)**: 0 or 1 rows. H14 v3.2 semantics: on 0/1 present, H14 should have CREATED the missing policy rather than just verifying — if this check still fails post-H14, H14 either didn't run or its create-path failed silently. Downstream symptom: Email/Communication module ingestion 403s despite delegated `Mail.Send` working in ad-hoc testing.

---

## T5 — Slot-per-slot System-Assigned MI parity

**What it verifies**: if this stamp's App Service still uses System-Assigned MI (pre-UAMI-migration Bicep), BOTH the production slot's MI and the staging slot's MI have identical KV RBAC grants — otherwise a post-swap cold start fails to resolve KV refs.

**How to verify**:

```powershell
# Get both slots' MI principal IDs
$prodMiId = az webapp identity show --resource-group {rg} --name spaarke-bff-{customerId}-{env} --query principalId -o tsv
$stagingMiId = az webapp identity show --resource-group {rg} --name spaarke-bff-{customerId}-{env} --slot staging --query principalId -o tsv

# Check KV role assignments for both
az role assignment list --assignee $prodMiId --scope /subscriptions/{subId}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/{vaultName} --query "[].roleDefinitionName" -o tsv
az role assignment list --assignee $stagingMiId --scope /subscriptions/{subId}/resourceGroups/{rg}/providers/Microsoft.KeyVault/vaults/{vaultName} --query "[].roleDefinitionName" -o tsv
```

**Expected output (PASS)**: Both queries return the same role (`Key Vault Secrets User` or equivalent). If this stamp's Bicep used the Phase C UAMI module (`uami.bicep`), T5 is **structurally impossible** — a single UAMI spans both slots, so there is only one identity to check; confirm this by verifying `az webapp identity show` returns the SAME `principalId` for both slots (UAMI case) rather than two distinct ones (System-Assigned case).

**Failure mode (FAIL)**: One slot has the RBAC grant and the other doesn't (System-Assigned case only). Downstream symptom: a 503 window immediately after slot swap while the newly-promoted slot's identity resolves KV refs — actually a race, not a hard failure, but shows up as intermittent 5xx in the first ~60s post-swap.

---

## T6 — SPE container creation on delegated token 403s

**What it verifies**: H8's SPE container-type creation used a confidential-client (app-only, cert-based) token rather than a delegated `az login` token — delegated tokens 403 with "public client not allowed" on this specific Graph endpoint.

**How to verify**: indirect — confirm the container-type actually exists (proof the confidential-client path succeeded):

```powershell
$graphToken = # app-only token acquired via the SPE cert from KV (see H8 handler for the exact acquisition helper)
az rest --method GET `
  --uri "https://graph.microsoft.com/beta/storage/fileStorage/containerTypes/{containerTypeId}" `
  --headers "Authorization=Bearer $graphToken"
```

Also inspect H8's run log in the L2 handoff report — the handler should explicitly log which credential type it used.

**Expected output (PASS)**: 200 response with container-type metadata; H8's handler log shows confidential-client (cert-based) token acquisition, no `az login`-flavored delegated call anywhere in the trace.

**Failure mode (FAIL)**: 403 with `public client not allowed`, or the handler log shows a delegated-token acquisition path. Means the confidential-client refactor (task 011) regressed or this stamp's KV doesn't have the `spaarke-spekvcert` (or per-customer equivalent) certificate populated. Downstream symptom: H8 fails outright on a fresh customer with an unhelpful Graph auth error, blocking the whole pipeline.

---

## I1 — No hardcoded default tenant in provisioning scripts

**What it verifies**: every provisioning script that needs a tenant ID requires it as an explicit, mandatory parameter — no fallback default that would silently provision resources into the Spaarke tenant instead of the customer's.

**How to verify**:

```powershell
# Grep-scan every provisioning script for tenant-shaped GUID defaults
Get-ChildItem -Path scripts -Filter *.ps1 -Recurse | ForEach-Object {
  Select-String -Path $_.FullName -Pattern '\[string\]\$TenantId\s*=\s*"[0-9a-fA-F-]{36}"'
}
```

Also confirm via the ArchTest that codifies this (per r3 task 040 pattern / task 064):

```powershell
dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~TenantIsolation&FullyQualifiedName~I1"
```

**Expected output (PASS)**: The `Select-String` grep returns ZERO matches (no hardcoded tenant-shaped default found). The ArchTest passes.

**Failure mode (FAIL)**: Any match — the specific historical instance was `Register-EntraAppRegistrations.ps1:63` (`[string]$TenantId = "a221a95e-6abc-4434-aecc-e48338a1b2f2"`), fixed per commit `1834b77bc`; re-verify it hasn't regressed. A regression here means an operator running the script without `-TenantId` would silently provision into the Spaarke tenant. Severity: HIGH data-bleed risk per design.md §4D.

---

## I2 — All AI Search queries include unconditional `tenantId` filter

**What it verifies**: every BFF AI Search query (regardless of index or query shape) includes a `tenantId eq '{ctx.TenantId}'` OData filter — this stamp's dedicated AI Search service still needs the filter enforced (Model 2 doesn't get a pass just because the service is dedicated; defense in depth).

**How to verify** — sample a live query against the trial stamp's dedicated AI Search service and confirm cross-tenant documents are NOT returned:

```powershell
# Acquire a BFF-scoped token for the trial customer's tenant, then hit an endpoint
# that triggers an AI Search query (e.g. RecordSearchService via a search endpoint)
curl -s -H "Authorization: Bearer $bffToken" `
  "https://spaarke-bff-{customerId}-{env}.azurewebsites.net/api/search?q=test" | jq .

# Independently, query the AI Search service directly and confirm the filter is present
# in the query the BFF actually sent (check App Insights dependency telemetry for the
# outbound Search REST call and inspect its $filter parameter)
```

Also run the ArchTest:

```powershell
dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~TenantIsolation&FullyQualifiedName~I2"
```

**Expected output (PASS)**: App Insights dependency trace for the outbound AI Search call shows `$filter=tenantId eq '{tenantId}'` (or equivalent `search.in`/`eq` clause) present on every call. ArchTest passes (per task 065 audit sweep — 22/22 all 5 I1-I5 ArchTests pass on current branch).

**Failure mode (FAIL)**: A query with no `tenantId` filter, or a filter using the wrong tenant's ID. Severity: CATASTROPHIC per design.md §4D (returns another customer's indexed legal documents).

---

## I3 — All Cosmos reads/writes include `/tenantId` (or `/customerId`) partition-key predicate

**What it verifies**: no cross-partition Cosmos query against tenant-scoped containers (AI sessions, prompts, audit, or the `runs` container itself) — every read/write specifies an explicit partition key.

**How to verify**:

```powershell
# Inspect the provisioning run record for this customer directly via a partition-key-scoped read
az cosmosdb sql query `
  --resource-group {rg} `
  --account-name cosmos-spaarke-{env} `
  --database-name spaarke-provisioning `
  --container-name runs `
  --query-text "SELECT * FROM c WHERE c.customerId = '{customerId}'" `
  --partition-key-value "{customerId}"
```

Also run the ArchTest that scans for missing `PartitionKey` on Cosmos SDK calls:

```powershell
dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~TenantIsolation&FullyQualifiedName~I3"
```

**Expected output (PASS)**: The partition-key-scoped query returns the run record with a single RU charge consistent with a single-partition read (check `x-ms-request-charge` header — should be low, single-digit RUs, not a fan-out). ArchTest passes.

**Failure mode (FAIL)**: A query executed without `--partition-key-value` succeeds anyway (meaning `[AllowCrossPartitionScan]` was applied somewhere it shouldn't be) or the ArchTest fails. Severity: HIGH (returns another customer's AI conversation history / PII).

---

## I4 — SPE container IDs always tenant-scoped-derived, never fallback default

**What it verifies**: this stamp's BFF resolves its SPE container ID exclusively through `ITenantContainerResolver` from the current tenant context — no hardcoded container-ID string literal anywhere in the code path that could leak a customer's files into another customer's container.

**How to verify**:

```powershell
# Confirm the trial customer's container ID is populated + distinct from any other customer's
az keyvault secret show --vault-name {vaultName} --name customer-{customerId}-spe-container-id --query value -o tsv

# Confirm the Dataverse env-var matches
pac data query --entity environmentvariablevalues `
  --filter "_environmentvariabledefinitionid_value eq {sprk_SharePointEmbeddedContainerId_defId}" `
  --select value
```

Also run the ArchTest that scans for SPE container-ID string literals:

```powershell
dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~TenantIsolation&FullyQualifiedName~I4"
```

**Expected output (PASS)**: KV secret + Dataverse env-var both return the SAME container ID, and that ID is unique to this customer (spot-check against another customer's stamp if one exists in dev). ArchTest passes (I4 already passes cleanly per task 065 — 22/22 baseline).

**Failure mode (FAIL)**: KV secret and Dataverse env-var disagree, or the container ID matches another customer's — means H8's write-back to KV/Dataverse targeted the wrong customer record. Severity: CATASTROPHIC (privileged legal documents land in the wrong customer's container).

---

## I5 — Graph token acquisition is per-tenant scoped

**What it verifies**: `GraphClientFactory` (and any other Graph token acquisition path — broadened scope now includes `Infrastructure/Graph/**` + `Infrastructure/Auth/**` per the 2026-08-18 drift-1 fix) always passes an explicit `tenantId` on every token acquisition — never relies on `DefaultAzureCredential()`'s ambient tenant.

**How to verify**:

```powershell
dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~TenantIsolation&FullyQualifiedName~I5"
```

Also spot-check via the nightly Graph parity test (task 067) if it has run against this stamp, or manually trigger a Graph call from the BFF and inspect the App Insights trace for the `tid` claim used:

```powershell
# Decode the access token used in a recent Graph call (from App Insights dependency trace or a
# deliberately-triggered call) and confirm its `tid` claim matches {tenantId}, not the Spaarke tenant
```

**Expected output (PASS)**: ArchTest passes (per task 065 + drift-1 fix — I5 broadened + `ManagedIdentityCredentialFactory.cs` now sets explicit `TenantId`). Decoded token `tid` claim == `{tenantId}` for Model 2, or == Spaarke shared tenant only where Model 1 intentionally shares tenant context.

**Failure mode (FAIL)**: ArchTest fails, or a decoded token's `tid` doesn't match the expected tenant. Severity: CATASTROPHIC (returns another tenant's Graph resources — SPE files, mail, group membership).

---

## Naming Conformance

**What it verifies**: every resource name + KV secret name created by this provisioning run follows the canonical Phase G naming standard (`scripts/naming-conformance-check.ps1`) — no orphan alias names, no drift from the manifest.

**How to verify**:

```powershell
pwsh -File scripts/naming-conformance-check.ps1 -Scope r1-owned
echo "Exit code: $LASTEXITCODE"
```

If the script supports a customer-scoped filter, prefer scoping it to the trial stamp specifically:

```powershell
pwsh -File scripts/naming-conformance-check.ps1 -Scope r1-owned -CustomerId {customerId}
```

**Expected output (PASS)**: Exit code 0. No `[FAIL]` lines in output. Any `spaarke-spekvcert` DO-NOT-RENAME dev exception is explicitly acknowledged in output, not silently skipped.

**Failure mode (FAIL)**: Non-zero exit code. Output enumerates specific non-conforming resource/secret names — cross-reference against `scripts/canonical-secret-catalog/manifest.yaml` (task 084) to determine whether the fix is a rename (apply BINDING pre-check protocol first — never delete `Dataverse-ClientSecret` / `BFF-API-ClientSecret`) or a manifest gap.

---

## Cost Envelope

**What it verifies**: the trial stamp's actual/projected Azure spend conforms to design.md §15 #14 targets. Primary target for the Model 2 primary path: **empty-environment Azure floor ≤ $400/mo**. (Model 1 targets — marginal ≤$430/mo + shared platform floor ≤$400/mo — only apply if the discretionary Model 1 run is performed.)

**How to verify**:

```powershell
# H0 preflight's own cost estimate (available immediately, before any spend accrues)
# — captured in the L2 run's preflight response body; also echoed in the skill's
#   "PREFLIGHT (H0) RESULT" console output during the run.

# Actual cost after 24-48h of the resource group existing (Cost Management needs time to ingest usage):
az costmanagement query `
  --type ActualCost `
  --timeframe MonthToDate `
  --scope "/subscriptions/{subId}/resourceGroups/{rg}" `
  --dataset-aggregation '{"totalCost":{"name":"PreTaxCost","function":"Sum"}}' `
  --dataset-grouping name=ResourceId type=Dimension
```

If `az costmanagement` extension isn't installed: `az extension add --name costmanagement`.

For a rough same-day estimate before Cost Management data lands, use the Azure Pricing Calculator export from H0's preflight quota-check step, or sum the SKUs directly:

```powershell
az resource list --resource-group {rg} --query "[].{name:name, type:type, sku:sku.name}" -o table
```

**Expected output (PASS)**: H0 preflight's estimated cost ≤ $400/mo (Model 2 primary target). Actual 24-48h-extrapolated cost (via `az costmanagement`) also within ≤ $400/mo ± reasonable estimation variance. Any deviation >20% from the $400/mo target is flagged as cost drift per §15 #14 (this is a WARN, not a hard block — H13's cost-drift check is advisory-warn by default per task 055).

**Failure mode (FAIL / DRIFT)**: Estimated or actual cost exceeds $400/mo by more than 20% (i.e., > $480/mo). Document which SKU(s) are the outlier (commonly: AI Search tier, OpenAI TPM commitment, App Service Plan tier) and whether it's a one-time provisioning cost (e.g., first-month AI Search minimum commitment) vs. a genuine ongoing overrun. Escalate per CLAUDE.md §6.5 if the overrun looks structural rather than a one-time artifact.

---

## Summary checklist (for quick copy into the report)

| # | Check | Owning handler | Command family |
|---|---|---|---|
| T1 | keyVaultReferenceIdentity == UAMI | H4 | `az webapp show` + `az identity show` |
| T2 | Dataverse App User for MI | H10 | `pac data query` / MCP `systemusers` |
| T3 | UAMI Graph app-role parity (14/14) | H10 | `az rest` Graph `appRoleAssignments` |
| T4 | Exchange ApplicationAccessPolicy (2 entries) | H14 | `Get-ApplicationAccessPolicy` |
| T5 | Slot-parity KV RBAC (or UAMI-spans-both-slots) | H4 | `az webapp identity show` + `az role assignment list` |
| T6 | SPE conf-client cert (no delegated 403) | H8 | `az rest` Graph containerType GET |
| I1 | No hardcoded tenant default | scripts + ArchTest | `Select-String` grep + `dotnet test` |
| I2 | AI Search tenantId filter | BFF services + ArchTest | App Insights trace + `dotnet test` |
| I3 | Cosmos partition-key predicate | L2/BFF + ArchTest | `az cosmosdb sql query` + `dotnet test` |
| I4 | SPE container-id resolver | BFF + ArchTest | `az keyvault secret show` + `pac data query` + `dotnet test` |
| I5 | Graph per-tenant token | GraphClientFactory + ArchTest | `dotnet test` + token `tid` decode |
| Naming | naming-conformance-check.ps1 exits 0 | Phase G | `pwsh -File scripts/naming-conformance-check.ps1` |
| Cost | ≤$400/mo Model 2 floor | H0 + H13 | `az costmanagement query` |
