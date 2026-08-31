# Design Study DS-1b — Option D (Hybrid SDK + Residual PowerShell) Deep Dive

> **Project**: customer-provisioning-orchestration-r1
> **Date**: 2026-08-18
> **Status**: DECISION STUDY — owner to choose. No code changed by this study.
> **Question**: DS-1 recommended Option A (fat container). Owner directed a rigorous Option D design before committing, under two directives: (1) recommend best practice for scale/maintainability, not the fastest ship; (2) verify whether Exchange `ApplicationAccessPolicy` — DS-1's sole claimed PowerShell-only residual — is truly still PowerShell-only in 2026.
> **Feeds**: gap-analysis C1.3 (execution environment), C1.1 (dispatcher), C3.1/C3.2 (H13 probes). Companion to `design-study-ds1-handler-runtime-environment.md`.

---

## 0. The load-bearing verification: Exchange policy scoping is STILL PowerShell-only (2026)

Owner Directive 2 asked whether Microsoft Graph now supports `applicationAccessPolicy` (or a successor), because H14a uses it to narrow the BFF's `Mail.Read`/`Mail.Send` from tenant-wide to a per-customer mail-enabled security group (T4 — real security hardening).

**Finding: NO Graph API exists for either the legacy or the successor mechanism. Verified 2026-08-18.**

1. **Legacy `ApplicationAccessPolicy`** — managed only via `New-ApplicationAccessPolicy` / `Get-ApplicationAccessPolicy` Exchange Online PowerShell cmdlets. Microsoft now labels the page "[Application Access Policies (legacy)](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies)" and steers new configuration away from it. There is no `applicationAccessPolicy` resource in Graph v1.0 **or** beta — the Graph concept doc ([auth-limit-mailbox-access](https://learn.microsoft.com/en-us/graph/auth-limit-mailbox-access)) describes the *effect* on Graph calls but routes all *management* through EXO PowerShell.
2. **Successor: RBAC for Applications** ([application-rbac](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac), page updated 2026-03-16, frontmatter `ms.devlang: powershell`) — the replacement Microsoft designates for AAP. Its entire management surface is EXO PowerShell: `New-ServicePrincipal`, `New-ManagementScope`, `New-ManagementRoleAssignment -App … -CustomResourceScope …`, `Test-ServicePrincipalAuthorization`. The page's own FAQ: *"we surface these permissions granted in Exchange Online in a Microsoft Entra admin experience. This feature is upcoming, stay tuned."* — i.e., not even an admin-portal surface yet, let alone a Graph API.
3. Community/practitioner coverage through Feb 2026 ([office365itpros](https://office365itpros.com/2026/02/17/mail-send-rbac-for-applications/), [Mindcore](https://blog.mindcore.dk/2026/02/microsoft-graph-remembered-to-restict-mail-send-application-permission-app-access-policies/)) confirms both mechanisms remain cmdlet-managed.

**Consequences for this study:**
- The middle branch of the owner's decision tree applies: **a minimal PowerShell runtime is needed for H14a alone**. Option D cannot collapse to pure-SDK.
- The residual is not a stopgap awaiting an imminent Graph API — Microsoft's own *successor* keeps the PS-only management surface. Plan for the sidecar to live **years**, and design H14a's collaborator seam (`IExchangePolicyApplier`) so the eventual AAP→App-RBAC migration (design.md R22) is a sidecar-script change, not a handler change.
- One mitigating fact from the script itself: `Set-ExchangeApplicationAccessPolicy.ps1` is already get-before-set idempotent (`Get-ApplicationAccessPolicy` at line 169 → conditional `New-ApplicationAccessPolicy` at 195 → re-verify at 205) and already headless (app-only cert `Connect-ExchangeOnline`, line 155). It is the *best-behaved* script in the fleet — 231 lines, one module, structured JSON result envelope.

---

## 1. Per-collaborator SDK coverage matrix

Method: every `ProcessStartInfo` collaborator in `Handlers/**` (25 files, grep-verified) mapped to its exact tool invocation (cited), then to the .NET SDK package + method, REST endpoint, or "no equivalent." Scripts' *internal* az/pac calls were profiled per script (grep counts below are per-script `az`/`pac` subcommand tallies from the working tree).

Legend: ✅ SDK = mature .NET SDK · ✅ REST = documented REST API, no SDK wrapper needed (plain `HttpClient` + `DefaultAzureCredential`, the pattern H7/H10/H11/H12c already use in-process) · 🟡 = equivalent exists with a caveat · ❌ = no equivalent, PowerShell-only.

### H0 — Preflight (4 collaborators, all `PowerShellPreflightProbe` instances; pwsh at `PowerShellPreflightProbe.cs:125`)

| # | Collaborator → script | What it actually calls | .NET equivalent |
|---|---|---|---|
| 1 | → `Test-AzureOpenAiTpmHeadroom.ps1` (239 L) | `az cognitiveservices usage list` ×9 | ✅ SDK `Azure.ResourceManager.CognitiveServices` — `SubscriptionResource.GetUsagesAsync(location)` (or `CognitiveServicesAccountResource.GetUsages()`) |
| 2 | → `Test-DataverseEnvCreationRate.ps1` (216 L) | `pac admin list` ×7 (+ `pac auth` session mgmt that an SDK path deletes entirely) | ✅ REST — BAP admin API `GET https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version=2021-04-01` (token audience `https://service.powerapps.com/`) |
| 3 | → `Test-SubscriptionVCpuQuota.ps1` (227 L) | `az vm list-usage` ×12 | ✅ SDK `Azure.ResourceManager.Compute` — `SubscriptionResource.GetUsagesAsync(location)` |
| 4 | → `Test-SpeCertBootstrap.ps1` (212 L) | `az keyvault secret` ×8 | ✅ SDK `Azure.Security.KeyVault.Secrets.SecretClient` (already in BFF) |

### H2a — Bicep infra deploy (3 collaborators)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 5 | `ProvisionCustomerScriptBicepDeployRunner` → `Provision-Customer.ps1` (1,632 L) | `az deployment sub create` ×1, `az group exists/create`, `az keyvault secret` ×3, `az account get-access-token` ×5 | 🟡 SDK `Azure.ResourceManager.Resources` — `SubscriptionResource.GetArmDeployments().CreateOrUpdateAsync()` + `GetResourceGroups().CreateOrUpdateAsync()` + `SecretClient`. **Caveat**: ARM SDK deploys ARM *JSON*, not `.bicep` — CI pre-compiles `customer.bicep` → JSON artifact (one workflow step), or bundle the self-contained `bicep` binary. **Discount**: only steps 1–3 of the script's 13 steps are H2a's job (validate/RG/bicep-deploy, lines 318–500); steps 4–10 (lines 501–1150) duplicate H4/H5/H6/H7/H8's handlers — the effective port surface is ~450 lines, not 1,632 |
| 6 | `AzCliArmKeyVaultRefProbe` (`az webapp [deployment slot] show --query keyVaultReferenceIdentity`, `AzCliArmKeyVaultRefProbe.cs:81-103`) | az | ✅ SDK `Azure.ResourceManager.AppService` — `WebSiteResource.Data.KeyVaultReferenceIdentity` / `WebSiteSlotResource` |
| 7 | `AzCliUpgradeDriftDetector` (`az deployment sub what-if`, `AzCliUpgradeDriftDetector.cs:67-86`) | az | ✅ SDK `Azure.ResourceManager.Resources` — `ArmDeploymentResource.WhatIfAtSubscriptionScopeAsync()` (structured `WhatIfChange[]` replaces stdout parsing — a strict upgrade for drift classification) |

### H2b — AI Search indexes (1 shell-out collaborator)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 8 | `DeployAllIndexesScriptProvisioner` → `ai-search/Deploy-AllIndexes.ps1` (637 L) | `az search admin-key show` ×2, `az keyvault secret` ×1, `az webapp config` ×1; index bodies pushed via REST inside the script | ✅ SDK `Azure.Search.Documents.Indexes.SearchIndexClient` (already a BFF dependency) with **UAMI RBAC auth — deletes the admin-key handling entirely**; index JSON definitions become embedded resources/content files |

### H3 — Entra app registrations (1 shell-out collaborator + 1 placeholder)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 9 | `RegisterEntraAppRegScriptProvisioner` → `Register-EntraAppRegistrations.ps1` (982 L) | `az ad app` ×19, `az ad sp` ×2, `az rest` (Graph) ×3, `az keyvault secret` ×3, `pac admin assign-app-to-environment` ×1 | ✅ SDK `Microsoft.Graph` 6.x (BFF ships 6.5.0) — `Applications.PostAsync`, `ServicePrincipals`, `AppRoleAssignedTo`, `Oauth2PermissionGrants`; KV via `SecretClient`; the lone `pac admin assign-app-to-environment` → Dataverse Web API app-user creation — **the exact operation H10's collaborators already perform in-process via `HttpClient`** (`Handlers/DataverseAppUserGraphParity/`, all-REST per gap-analysis B.2) |
| — | `NullAdminConsentVerifier` (placeholder, C3.6) | — | Its *real* implementation is a Graph `oauth2PermissionGrants` query — SDK-native; owed under every option |

### H4 — KV secrets population (3 collaborators)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 10 | `AzCliKvSecretsWriter` (`keyvault secret set/show/delete` + `account show`, `AzCliKvSecretsWriter.cs:129-231`) | az | ✅ SDK `SecretClient` (`SetSecretAsync`/`GetSecretAsync`/`StartDeleteSecretAsync`); `account show` becomes `ArmClient.GetDefaultSubscription()` |
| 11 | `AzCliAppServiceIdentityPatcher` (`webapp update` / `webapp deployment slot update`, `AzCliAppServiceIdentityPatcher.cs:58,78`) | az | ✅ SDK `Azure.ResourceManager.AppService` — `WebSiteResource.UpdateAsync(new SitePatchInfo { KeyVaultReferenceIdentity = uamiId })`, same on `WebSiteSlotResource` (T1 owner) |
| 12 | `AzCliSlotIdentityRoleGranter` (`webapp show` + `role assignment create`, `AzCliSlotIdentityRoleGranter.cs:103-154`) | az | ✅ SDK `Azure.ResourceManager.Authorization` — `RoleAssignmentCollection.CreateOrUpdateAsync(roleAssignmentName, principalId, roleDefinitionId)` |

### H5 — Dataverse environment creation (1 collaborator)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 13 | `PacAdminDataverseEnvCreator` (`pac admin create-environment --name/--region/--type/--tenant/--domain --json`, `PacAdminDataverseEnvCreator.cs:120-132`) | pac | ✅ REST — BAP admin API `PUT/POST …/scopes/admin/environments` + async operation polling (also exposed via the newer Power Platform API `api.powerplatform.com`). **In-house precedent already exists**: `Provision-Customer.ps1` STEP 5 (line 589) is literally titled "Creating Dataverse environment via Power Platform Admin API" — the script itself abandoned pac for REST; port that REST sequence to `HttpClient` |

### H6 — Solution import (2 collaborators)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 14 | `DeployDataverseSolutionsScriptImporter` → `Deploy-DataverseSolutions.ps1` (847 L) | `pac solution import` ×3, `pac solution list` ×2, pack/delete/auth | ✅ SDK/REST — Dataverse Web API `ImportSolution` / `StageAndUpgrade` actions with `ImportJob` polling, or `Microsoft.PowerPlatform.Dataverse.Client` (`ServiceClient`). Dependency-ordered 8-solution sequence + retry semantics port as C# control flow. **Invariant under EVERY option**: solution ZIPs must become versioned build artifacts in the runtime payload — the fat container has this exact same problem |
| 15 | `PacCliSolutionVerifier` (`pac solution list --environment`, `PacCliSolutionVerifier.cs:113-116`) | pac | ✅ REST — Dataverse Web API `GET /api/data/v9.2/solutions?$select=uniquename,version` (trivial) |

### H8 — SPE container type (3 collaborators)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 16 | `CreateNewContainerTypeScriptProvisioner` → `Create-NewContainerType.ps1` (299 L) | `az ad app` ×3, `az keyvault secret` ×2 + container-type creation | ✅ SDK `Microsoft.Graph` — `POST /storage/fileStorage/containerTypes` (`fileStorageContainerType`, in Graph v1.0) under `ClientCertificateCredential` (T6 cert from KV); app-reg reads via Graph `Applications` |
| 17 | `SpeContainerAppOnlyVerifier` → `Get-SpeContainerMetadata-AppOnly.ps1` (123 L) | `az login` + `az account get-access-token` then Graph REST | ✅ SDK — `ClientCertificateCredential` + Graph GET; the script is 123 lines of token ceremony around one GET |
| 18 | `AzCliSpeContainerIdKvWriter` (`keyvault secret set/show`, `AzCliSpeContainerIdKvWriter.cs:85-108`) | az | ✅ SDK `SecretClient` |

### H9 — BFF deploy (3 collaborators; all conditional on Rider E3, which is required under EVERY option)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 19 | `DeployBffApiScriptRunner` → `Deploy-BffApi.ps1` (597 L) | `az webapp deploy` ×5, `az webapp deployment` ×4, log/stop/start, **`dotnet publish` at line 221** | 🟡 — as-is it is broken under every option (builds BFF from repo source at provision time; DS-1 §3.1). Post-E3 (CI artifact): ✅ REST — fetch versioned artifact + Kudu zip-deploy (`POST https://{app}.scm…/api/zipdeploy` with MI token) or `Azure.ResourceManager.AppService` `WebSiteResource` deploy; stop/start = ARM `StopAsync`/`StartAsync` |
| 20 | `DotnetR3GateVerifier` (dotnet + pwsh gate scripts; interim "Skipped" posture per C3.9) | dotnet/pwsh | 🟡 — post-E3 the r3 gates run in CI against the artifact; the runtime collaborator degrades to an artifact-metadata check (✅ pure C#) |
| 21 | `AzCliAppServiceSlotSwapper` (`az webapp deployment slot swap`, `AzCliAppServiceSlotSwapper.cs:62-75`) | az | ✅ SDK `Azure.ResourceManager.AppService` — `WebSiteSlotResource.SwapSlotAsync(...)` (long-running operation with proper polling — better than the CLI's fire-and-parse) |

### H12a — AI seed chain (1 collaborator)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 22 | `InvokeSeedManifestScriptRunner` → `seed-data/Invoke-SeedManifest.ps1` (536 L) | `Install-Module powershell-yaml` (lines 162-165) + Dataverse Web API `Invoke-RestMethod` under ambient az token | ✅ SDK — YamlDotNet (manifest parse) + Dataverse Web API `HttpClient` (**the exact pattern H12c's `RuntimeReferencesWriter` already uses in-process**); seed manifests become content files in the payload (needed under every option) |

### H12b — App-config seed (2 shell-out collaborators + 2 no-op placeholders)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 23 | `PowerShellAppConfigSeeder`(DataGrid) → `seed-reconciliation-gridconfig.ps1` (default per `AppConfigSeedOptions.cs:68-69`) | `az account get-access-token` (line 7) + `Invoke-RestMethod` GET/PATCH/POST on `sprk_gridconfigurations` (lines 24-43) | ✅ REST — ~40 lines of Dataverse Web API upsert; near-mechanical port |
| 24 | `PowerShellAppConfigSeeder`(WorkspaceLayout) → `Deploy-SystemWorkspaceLayouts.ps1` (424 L; 2 az/REST call sites) | same pattern | ✅ REST — same Dataverse Web API port |
| — | `DeferredAppConfigSeeder` ×2 (field-mapping + chart-def, C3.8) | no-op | Must be **authored from scratch anyway** under every option — author directly as C# seeders, zero double-work |

### H13 — E2E acceptance (3 shell-out collaborators + the placeholder probe families)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 25 | `ValidateDeployedEnvironmentScriptRunner` → `Validate-DeployedEnvironment.ps1` (532 L) | `az rest` ×2, `az login`, `pac auth create`, `Invoke-RestMethod` ×3 — probe-style GETs | ✅ REST — C# `HttpClient` probes. **Convergence bonus**: C3.1/C3.2 already oblige writing 11 real trap/invariant probes in C# (T1 ARM read, T2/T3 Dataverse+Graph, T5 role-assignment read, T6 app-only SPE GET…). The script port and the placeholder replacement are the *same work done once* |
| 26 | `NamingConformanceScriptRunner` → `naming-conformance-check.ps1` (200 L; **0** az/REST calls — pure local string/convention checks) | pwsh only as interpreter | ✅ pure C# port (trivial) |
| 27 | `AzCliCostEnvelopeChecker` (`az costmanagement query --type ActualCost --timeframe MonthToDate`, `AzCliCostEnvelopeChecker.cs:65-74`) | az | ✅ REST `POST /subscriptions/{id}/providers/Microsoft.CostManagement/query?api-version=2023-11-01` (or SDK `Azure.ResourceManager.CostManagement`) |

### H14 — Integration wiring (2 shell-out collaborators; H14b/c already in-process REST)

| # | Collaborator | What it calls | .NET equivalent |
|---|---|---|---|
| 28 | `ExchangePolicyScriptApplier` (H14a) → `Set-ExchangeApplicationAccessPolicy.ps1` (231 L) | `Import-Module ExchangeOnlineManagement` (line 147); app-only cert `Connect-ExchangeOnline` (155-157); `Get-/New-ApplicationAccessPolicy` (169/195/205) | ❌ **No equivalent — must remain in PowerShell** (verified §0; successor App-RBAC is also PS-only) |
| 29 | `AzCliKvSecretReader` (`keyvault secret show`, `AzCliKvSecretReader.cs:48`; hardcoded `FileName = "az"` at line 92 — the fleet's one unconfigurable executable) | az | ✅ SDK `SecretClient` |

### Matrix corrections to DS-1's fact base

- **DS-1 §1.2 "exactly one PowerShell module" is wrong.** `Invoke-SeedManifest.ps1:162-165` requires **`powershell-yaml`** (a second module; also used by the H4 catalog generator's `Invoke-CatalogGenerator.ps1:174-179`). Immaterial under D (YamlDotNet replaces it) but Option A's image must install it — DS-1's image spec was incomplete.
- **DS-1 §2-C's "pac is not the residual; Exchange is" is confirmed and strengthened**: every pac call site maps to BAP REST or Dataverse Web API, and the repo's own `Provision-Customer.ps1` STEP 5 already made that swap for env-creation.

---

## 2. Post-migration handler classification

| Class | Definition | Handlers | Count |
|---|---|---|---|
| **A — Pure .NET** | Every collaborator has an SDK/REST equivalent | H0, H2a, H2b, H3, H4, H5, H6, H8, H9 (post-E3), H12a, H12b, H13 | **12** |
| **B — Sidecar-required (fully)** | No collaborator has an equivalent | — | **0** |
| **C — Mixed** | One residual PS collaborator among SDK-capable ones | H14 (H14a only; H14b/c already REST, `AzCliKvSecretReader` → SDK) | **1** |

**Tally: 12 Class A · 0 Class B · 1 Class C.** Of ~29 collaborators across the 13 shell-out handlers, **exactly one** (`ExchangePolicyScriptApplier`, 231-line script, one module, one cmdlet family) cannot be expressed in .NET. The 6 in-process handlers (H0.5, H1, H7, H10, H11, H12c) are indifferent and already prove the `HttpClient + DefaultAzureCredential` pattern the Class-A ports converge on.

---

## 3. Sidecar design (for the single Class-C residual)

Because the residual is one idempotent, headless, JSON-enveloped script, the sidecar is designed to the floor:

**Image.** `mcr.microsoft.com/powershell:7.4-mariner-2.0` (~110–130 MB compressed) + `Save-Module ExchangeOnlineManagement -RequiredVersion <pinned>` baked at build (~70–90 MB) + `Set-ExchangeApplicationAccessPolicy.ps1` + a ~60-line `pwsh` HTTP listener (`HttpListener` or a `pode`-free minimal loop). **Total ≈ 200–230 MB compressed** — honest number; the "~50 MB" floor is not achievable with the EXO module, which is the point of the container. No Az module, no az CLI, no pac, no dotnet. Trivy surface = pwsh runtime + one signed Microsoft module.

**Topology.** **Sidecar container in the SAME App Service** via Linux App Service *sitecontainers* (GA; multi-container on one Plan). Same Plan, same UAMI, same App Insights workspace — preserves every property design.md B2 valued and adds **zero** new Azure resources beyond an ACR repo tag. Rejected alternatives: ACA Job (reopens B2's Container-Apps rejection for one call/run); separate App Service (a whole second host for one cmdlet); ACI (cold-start + separate networking/identity story).

**Message contract.** Synchronous HTTP on the sitecontainer-private localhost network (sitecontainers share the network namespace; the sidecar port is NOT exposed on the app's public front end):

```
POST http://localhost:8091/apply-policy
  { "tenantId", "expectedAppIds": [], "policyScopeGroupId", "descriptionPrefix",
    "correlationId": "<RunId>", "timeoutSeconds": 300 }
→ 200 { "outcome": "Success|Failure|AlreadyCompliant", "policiesApplied": [], "diagnostic" }
```

This mirrors the script's existing `Write-ResultJson` envelope (`Set-ExchangeApplicationAccessPolicy.ps1:142,163,225`) — the C# `ExchangePolicySidecarClient : IExchangePolicyApplier` maps it onto `HandlerResult` exactly as `ExchangePolicyScriptApplier` maps exit codes today. No Service Bus, no shared-volume file drop: one caller, one callee, request/response semantics, and SB would re-introduce the very envelope/dispatch machinery C1.1 exists to centralize.

**Auth.** Two independent legs:
- *Main → sidecar*: localhost-only binding + a per-boot shared-secret header sourced from the platform KV (defense-in-depth; the network namespace is already private).
- *Sidecar → Exchange*: app-only `Connect-ExchangeOnline` with the Exchange cert. Sidecar fetches the PFX from platform KV at call time using the **same UAMI** (sitecontainers can reach the App Service MSI endpoint) and passes `-Certificate` (an `X509Certificate2`) instead of `-CertificateThumbprint` — a ~10-line script amendment; thumbprint-mode assumes a Windows cert store that a Linux container doesn't have. Same-UAMI is correct here: a second identity would add rotation surface while protecting nothing (the sidecar necessarily holds Exchange-admin capability either way; blast-radius control is that ONLY this container has the EXO module and ONLY one route reaches it).
- L2's UAMI needs no new grants beyond what H14a requires under every option (KV secret get for the cert): C5.8 applies identically.

**Idempotency + retry.** No new idempotency layer. The script is internally get-before-set idempotent (§0); the handler's L3 dedup (CompletedPhases) and the §4C taxonomy stay authoritative. Sidecar HTTP failures map: connection-refused/timeout → `InfraFault` (Resumable — reconciler re-enqueues); structured `Failure` envelope → classified by existing H14 logic. One retry with backoff inside the client for transient EXO throttling; everything else defers to the run-level retry machinery, exactly like every other collaborator.

**Observability.** `correlationId = RunId` in every request; sidecar logs one structured JSON line per request to stdout → App Service log stream → same Log Analytics workspace. The C# client logs request/response at the same points `ExchangePolicyScriptApplier` logs process output today, so `runs/{id}/logs` (A1's logs endpoint) needs no changes.

**Deployment/patching.** Sidecar image built in the same GitHub Actions workflow as the main image (both ACR-pushed, Trivy-gated); monthly rebuild cadence — but the rebuild loop covers pwsh + one module, not az CLI's ~100-package Python tree.

---

## 4. Head-to-head: Option A vs Option D

| Dimension | Option A (fat container) | Option D (hybrid: SDK + minimal EXO sidecar) |
|---|---|---|
| Container image(s) | 1 image, ~1.5–2 GB (aspnet:10 + pwsh + az(≈1 GB Python layer) + pac + EXO + powershell-yaml + scripts/bicep/ZIPs/manifests) | Main: **stock `DOTNETCORE\|10.0` App Service — no custom image at all** (solution ZIPs/manifests/ARM-JSON as publish content, ~tens of MB). Sidecar: ~200–230 MB |
| Cold start | Minutes on first pull (mitigated by Always On) | Main: none (code-based). Sidecar: seconds |
| Patching cadence | Monthly full-image rebuild; az CLI's Python dependency tree is the CVE fire-hose; expect sustained Trivy noise | Main: NuGet Dependabot flow the repo already runs. Sidecar: pwsh + 1 signed module — a quiet rebuild |
| Attack surface | pwsh + az + pac + module installers inside the internet-facing control plane; any in-process code path can reach any tool | Main has **zero shells**. The only shell lives in a non-routable sidecar reachable by one localhost route, containing one module |
| Auth model | One ambient `az login --identity` session + `pac auth` SP session usable by *anything* in-process; stdout-parsed results | Scoped credential objects per SDK client; no ambient sessions; structured exceptions. §3.2's auth-parity spike mostly dissolves (DefaultAzureCredential needs the same grants but fails loudly and testably) |
| Handler/collaborator LOC | ~0 changed (13 handlers keep shell-outs) | 24 collaborator rewrites behind existing interfaces (handler cores untouched); ~5–7k LOC new C# replacing ~6.8k script LOC + fragile stdout parsing |
| Failure-signal quality | Exit codes + stdout regex (T-trap silent-fail *class* stays alive) | Typed SDK exceptions/LRO results → §4C classification is exact |
| Ops complexity | 1 container + ACR + auth-bootstrap component | 1 stock App Service + 1 sidecar + ACR; localhost IPC (no cross-host networking) |
| Engineering effort | ~8–12 person-days | ~55–75 person-days (see §5) |
| Reversibility | "HIGH" per DS-1 — but only as *potential*: converging to SDK is a second, never-scheduled project; the image is a standing incentive to never do it | Already at the end-state for 12/13 handlers; residual is one bounded seam with a designed R22 migration path |
| Cost (Azure) | ACR Basic + same Plan | Same (ACR Basic + same Plan; sidecar shares the Plan) |
| H9 / artifacts | E3 re-scope + ZIPs/manifests as payload — required under BOTH | Same requirement — not a differentiator |
| Fit to owner Directive 1 | Optimizes ship-speed | Optimizes scale + long-term maintainability |

## 5. Effort estimate

Class-A collaborator migration (24 collaborators, grouped):

| Work block | Estimate (person-days) |
|---|---|
| Thin az one-liners → SDK: #6, #7, #10, #11, #12, #18, #21, #27, #29 (9 collaborators) | 7–9 |
| H0 probes ×4 (#1–#4; ARM/BAP/KV reads + threshold logic port) | 4–6 |
| H2b SearchIndexClient + RBAC-auth swap (#8) | 2–3 |
| H5 BAP REST env-create + async polling (#13; port of Provision-Customer STEP 5) | 3–4 |
| H12b Dataverse upsert seeders ×2 (#23, #24) | 2 |
| H12a YamlDotNet manifest engine + Dataverse writes (#22) | 4–5 |
| H13 validate/naming ports (#25, #26) **net of C3.1/C3.2 overlap** (11 probes owed anyway) | 3–4 |
| H3 Graph app-reg orchestration (#9; 982 L, 14 grants, consent flow — the single heaviest port) | 8–10 |
| H6 solution import via Web API + ImportJob polling + artifact packaging (#14, #15) | 6–8 |
| H2a ARM deployment + RG + what-if (effective ~450 L after the 13-step discount) (#5) | 5–6 |
| Bicep→ARM-JSON CI pre-compile step | 1–2 |
| H9 post-E3 artifact-fetch + zip-deploy + swap (#19–#21) — E3 itself is owed under every option | 2–3 |
| **Class A subtotal** | **47–62** |
| Sidecar: image + CI + sitecontainer Bicep + HTTP client/listener + cert-mode script amendment + tests | 5–8 |
| Main-container simplification / payload content items (vs A's Dockerfile+bootstrap: a wash) | 2–3 |
| **Option D total** | **≈ 55–75 person-days** |
| **Option A total (DS-1 MEDIUM: Dockerfile, CI, Bicep delta, auth bootstrap, smoke)** | **≈ 8–12 person-days** |

**Timeline delta**: Option A reaches a runnable E2E attempt ~6–9 weeks sooner (one engineer) / ~3–5 weeks (two parallel, since collaborator ports are independent behind their interfaces). Three deflators of D's headline number: (i) C3.1/C3.2's 11 real probes, C3.8's two seeders, and E3 are **owed under Option A too** (~10–14 days of the delta is not really Option-D-specific); (ii) DS-1's Option-D estimate assumed inventing a cross-process protocol for a large residual — with a 1-collaborator residual and localhost HTTP, that tax collapsed; (iii) ~9 of the 24 ports are sub-day mechanical swaps of stdout-parsing for an SDK call.

## 6. Failure modes each option catches / misses

**Option D catches, Option A misses:**
- *Stdout-parse false-positives* — the T1–T6 silent-fail trap class the project exists to kill: an az/pac call that "succeeds" with a warning, format change, or partial result parses as success. SDK/LRO results are typed; ARM what-if returns `WhatIfChange[]`, not text. Option A preserves 25 stdout parsers indefinitely.
- *Ambient-session confusion* — under A, one process-wide `az login --identity` + `pac auth` session backs every operation; a handler acting against the wrong subscription/tenant via stale session state is invisible. Under D each client is constructed with explicit subscription/tenant — wrong-scope becomes a compile-time/DI-time property.
- *Version skew* — az/pac auto-update semantics inside a hand-rebuilt image can change output formats out from under the parsers; NuGet pins are lockfile-audited in CI.
- *Boot-time drift* — A's auth-bootstrap (login at startup) failing lands the container in a half-authed state discovered mid-run; D's clients fail per-call, classified by §4C.

**Option A catches, Option D misses:**
- *Script-vs-port divergence* — A executes the battle-tested scripts byte-for-byte; every D port risks dropping an undocumented behavior (H3's consent retry timing, H6's ordering edge cases). Mitigation: port acceptance tests asserting parity against recorded script outputs; the heavy ports (H3, H6) are sequenced last.
- *Operator debuggability* — an operator can run the same script A runs, by hand. D's SDK paths need the L2 logs endpoint instead (already built, A1).

**Auth surface:** identical *grants* required (C5.8 gates both). Different *shape*: A concentrates capability in two ambient sessions inside one big image; D distributes scoped credentials and quarantines the sole shell into a non-routable container. D's shape is strictly better; §3.2's flip-risk ("a class of operations that cannot run under workload identity") applies to both equally and is *smaller* under D because SDK calls surface authorization failures as typed exceptions in tests.

**Rollback semantics:** unchanged in either — §4C/`RollbackTransitions` operate on `HandlerResult` above the collaborator seam. D improves *classification fidelity* (typed errors → correct Retriable/Resumable/Fatal branch); A leaves classification hostage to exit-code fidelity of 13 scripts.

## 7. Recommendation

**Option D — hybrid, with the minimal EXO sidecar — executed in two waves.**

Under DS-1's framing (deliverable speed dominant) A was defensible. Under the owner's stated decision criterion — *best practice for scale and long-term maintainability, cost the trade-offs honestly* — the matrix decides it: **the residual that justified a fat tools container is one 231-line idempotent script**. Building a ~2 GB image carrying az CLI's Python CVE stream, two ambient auth sessions, and 25 stdout parsers as permanent fleet infrastructure — to avoid rewriting collaborators of which nine are one-day SDK swaps — inverts the cost-benefit at fleet scale. Every provisioned customer multiplies the years the control plane lives; the patching loop, parser fragility, and ambient-session blast radius are *recurring* costs, while D's rewrite cost is *one-time* and partially owed anyway (§5 deflators). A's celebrated reversibility is a plan to do D later without a scheduled later.

**Wave sequencing** (keeps E2E pressure honest):
1. **Wave D-1** (~2–3 weeks): dispatcher (C1.1) on the stock App Service + sidecar + the 9 thin swaps + H0/H2b/H5/H12a/H12b/H13 ports + E3. This already makes ~10 of 13 shell-out handlers executable.
2. **Wave D-2** (~3–4 weeks): H3, H6, H2a heavy ports with parity acceptance tests. If a hard commercial date lands mid-wave, the *bounded* fallback is running those two-three handlers' scripts in the sidecar temporarily (it has pwsh; add nothing but the scripts) — a contained concession, not a re-architecture.

**Honest cost statement per Directive 1**: this is ~6–9 engineer-weeks slower to first E2E than Option A, not 1–2. The recommendation stands because the delta buys the permanent elimination of the image-supply-chain, patching, parser-silent-fail, and ambient-auth risk classes.

**The ONE thing that would flip this recommendation**: a **committed customer provisioning date inside ~6 weeks** (the Model 2 commitment trigger in project CLAUDE.md's escalation list). Under that constraint, ship Option A *explicitly labeled as interim*, with the A→D thinning ledger (this document's §1 matrix) attached as scheduled backlog — because A-without-a-ledger is how the 20-CVE fat container becomes permanent. A second, smaller flip: if the D-1 spike finds a BAP/Dataverse operation that genuinely cannot run under the UAMI/SP credential set (§3.2's class), the affected collaborators join H14a in the sidecar — which degrades D gracefully rather than invalidating it.

---

**Sources** (Exchange verification, §0):
- [Application Access Policies (legacy) — Microsoft Learn](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-access-policies)
- [Role Based Access Control for Applications in Exchange Online — Microsoft Learn](https://learn.microsoft.com/en-us/exchange/permissions-exo/application-rbac) (updated 2026-03-16; PowerShell-only management surface)
- [Limiting application mailbox access — Microsoft Graph concept doc](https://learn.microsoft.com/en-us/graph/auth-limit-mailbox-access)
- [Control Graph Mail.Send Permission with RBAC for Applications — Office 365 IT Pros (2026-02-17)](https://office365itpros.com/2026/02/17/mail-send-rbac-for-applications/)
- [App Access Policies replaced by RBAC for Applications — Icewolf (2025-12-03)](https://blog.icewolf.ch/archive/2025/12/03/exchange-online-app-access-policies-are-replaced-by-RBAC-for-applications/)

*Analysis-only artifact. Evidence: grep/read against the working tree + live Microsoft Learn fetches as cited inline; no code, config, or Azure state modified.*
