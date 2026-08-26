# Design Study DS-1 — Handler Runtime Environment

> **Project**: customer-provisioning-orchestration-r1
> **Date**: 2026-08-18
> **Status**: DECISION STUDY — owner to choose. No code changed by this study.
> **Question**: Where and how do the ~19 provisioning handlers ACTUALLY execute in production, given that ~13 of them shell out to `pwsh`/`az`/`pac` and the deployed L2 App Service is a stock Linux .NET runtime (`linuxFxVersion: 'DOTNETCORE|10.0'`, `controlplane-app-service.bicep:104`) with none of those tools and zero scripts in its publish payload?
> **Feeds**: gap-analysis C1.3 (execution environment) — coupled to C1.1 (dispatcher, which does not exist yet and will live wherever this decision says handlers run).

---

## 1. Grep-verified fact base

All claims below verified against the working tree on 2026-08-18.

### 1.1 Which handlers shell out (the affected set)

25 collaborator files contain `ProcessStartInfo`/`Process.Start` (grep, `Handlers/**`, 5,529 LOC total). Mapped to handlers:

| HandlerId | Shell-out collaborators | Tool(s) | Script(s) invoked (LOC) |
|---|---|---|---|
| **H0** | `PowerShellPreflightProbe` (×4 probe instances) | pwsh | `scripts/preflight/Test-*.ps1` — 239+216+212+227 = 894 |
| **H2a** | `ProvisionCustomerScriptBicepDeployRunner`, `AzCliArmKeyVaultRefProbe`, `AzCliUpgradeDriftDetector` | pwsh + az | `Provision-Customer.ps1` (1,632) + reads `infrastructure/bicep/**` on disk |
| **H2b** | `DeployAllIndexesScriptProvisioner` | pwsh | `ai-search/Deploy-AllIndexes.ps1` (637) |
| **H3** | `RegisterEntraAppRegScriptProvisioner` | pwsh | `Register-EntraAppRegistrations.ps1` (982) |
| **H4** | `AzCliKvSecretsWriter`, `AzCliAppServiceIdentityPatcher`, `AzCliSlotIdentityRoleGranter` | az | (direct az invocations, no script) |
| **H5** | `PacAdminDataverseEnvCreator` | pac | (direct `pac admin` invocation) |
| **H6** | `DeployDataverseSolutionsScriptImporter`, `PacCliSolutionVerifier` | pwsh + pac | `Deploy-DataverseSolutions.ps1` (847) + solution ZIPs on disk |
| **H8** | `CreateNewContainerTypeScriptProvisioner`, `SpeContainerAppOnlyVerifier`, `AzCliSpeContainerIdKvWriter` | pwsh + az | `Create-NewContainerType.ps1` (299), `Get-SpeContainerMetadata-AppOnly.ps1` (123) |
| **H9** | `DeployBffApiScriptRunner`, `DotnetR3GateVerifier`, `AzCliAppServiceSlotSwapper` | pwsh + dotnet + az | `Deploy-BffApi.ps1` (597) — **runs `dotnet publish` at line 221, i.e. builds the BFF from repo source at provision time** |
| **H12a** | `InvokeSeedManifestScriptRunner` | pwsh | `scripts/seed-data/Invoke-SeedManifest.ps1` + on-disk seed manifests |
| **H12b** | `PowerShellAppConfigSeeder` (2 of 4 scopes) | pwsh | DataGrid + workspace-layout seed scripts |
| **H13** | `ValidateDeployedEnvironmentScriptRunner`, `NamingConformanceScriptRunner`, `AzCliCostEnvelopeChecker` | pwsh + az | `Validate-DeployedEnvironment.ps1` (532) + naming script |
| **H14** | `ExchangePolicyScriptApplier` (H14a), `AzCliKvSecretReader` | pwsh + az | `Set-ExchangeApplicationAccessPolicy.ps1` (231) — **requires the `ExchangeOnlineManagement` PowerShell module** |

**Shell-out handlers: 13** — H0, H2a, H2b, H3, H4, H5, H6, H8, H9, H12a, H12b, H13, H14.
**In-process-capable handlers: 6** — H0.5, H1 (once its Null probe is made real — its real form is an ARM REST call), H7, H10, H11, H12c. These use `HttpClient` + `DefaultAzureCredential` and need only credentials/config (gap C5.8/C3.10), not tools.

Script LOC directly referenced by handlers: **~6,800** (13 named scripts). Every executable name is already configurable (`PwshExecutable`/`AzCliExecutable`/`PacCliExecutable` options properties — one hardcoded exception: `AzCliKvSecretReader.cs:92` `FileName = "az"`).

### 1.2 What the scripts actually depend on (smaller than feared)

- **PowerShell module dependencies across all 13 handler scripts: exactly one** — `ExchangeOnlineManagement` (H14a, `Connect-ExchangeOnline` app-only cert auth at `Set-ExchangeApplicationAccessPolicy.ps1:155`). No `Az.*` modules, no `Connect-MgGraph`, no `Connect-AzAccount` anywhere in the handler-referenced set (grep).
- **az subcommand surface** (grep across handler scripts): `az ad app` (22), `az keyvault secret` (16), `az vm list-usage` (12), `az cognitiveservices usage` (9), `az account get-access-token` (7), `az webapp deploy/deployment` (9), `az rest` (4+), `az deployment sub` (1), `az login`-related (precondition checks only — **no script performs its own login**; all assume an ambient az session, e.g. `Provision-Customer.ps1:336` throws "Run 'az login' first").
- **pac**: assumed pre-authenticated (`pac auth create` referenced only in error-guidance text, `Deploy-DataverseSolutions.ps1:408`).
- **Publish payload**: `Sprk.Provisioning.ControlPlane.csproj` contains zero `<Content>`/`<None>` items for `scripts/**`, `infrastructure/bicep/**`, solution ZIPs, or seed manifests. Default script roots resolve to `AppContext.BaseDirectory` — i.e. the publish folder that doesn't contain them.

### 1.3 Reference point: the BFF

`Sprk.Bff.Api.csproj` proves the SDK-everywhere posture is achievable in this codebase for a large surface: `Microsoft.Azure.Cosmos`, `Azure.Messaging.ServiceBus`, `Azure.Security.KeyVault.Secrets`, `Azure.Storage.Blobs`, `Azure.Search.Documents`, `Microsoft.Graph` 6.5.0, `Azure.Identity` — zero shell-outs. But note what the BFF does NOT do: ARM deployments, Entra app-reg creation, Dataverse environment creation, solution import, Exchange policy management. The BFF's SDK set covers *data-plane* operations; the provisioning handlers are dominated by *management-plane* operations the BFF has never needed.

### 1.4 Prior decision context

design.md B2 (v3): "Hosting: **App Service**. Parity with the BFF... **Container Apps was rejected**" — but that decision was about where the L2 *API* runs, made before the runtime-environment gap was understood. Nothing in B2 prescribes the *image* the App Service runs. App Service for Containers keeps every property B2 valued (same deploy tooling family, MI patterns, App Insights, slot semantics).

---

## 2. Options

A shared premise for all options: the C1.1 dispatcher (SB consumer) does not exist yet. Whatever is decided here, the dispatcher will be built as a `BackgroundService` **inside the process that has the handler runtime**, because handlers are in-process C# classes — the dispatcher and the handlers are inseparable. This decision therefore also decides where the dispatcher lives.

---

### Option A — Custom container image (pwsh + az + pac + scripts baked in), same App Service

**What it is.** Flip the existing L2 App Service from code-based (`DOTNETCORE|10.0`) to a custom Linux container (`DOCKER|<acr>/sprk-provisioning-controlplane:<tag>`). Image = `mcr.microsoft.com/dotnet/aspnet:10.0` + PowerShell 7 + az CLI + pac CLI (cross-platform .NET tool) + `ExchangeOnlineManagement` module + the published L2 app + `scripts/**`, `infrastructure/bicep/**`, solution ZIPs, and seed manifests copied in at build. Handlers keep their current shell-out pattern unchanged; the C1.1 dispatcher is added to the same process.

**How handlers execute.** Dispatcher `BackgroundService` receives the SB envelope → resolves handler by HandlerId → handler invokes its collaborator → collaborator `Process.Start`s `pwsh`/`az`/`pac` against `/app/scripts/...`. Auth chain: container startup (or a lazy one-time initializer) runs `az login --identity --username {uami-clientId}` and `pac auth create --applicationId ... --clientSecret ...`; scripts' existing "ambient session" assumption is then satisfied. H14a uses app-only cert `Connect-ExchangeOnline` (already headless-capable as written).

**Effort: MEDIUM.** Dockerfile (~80–120 lines) + GitHub Actions docker build/push (repo already has CI + Trivy culture) + Bicep delta (ACR resource, `linuxFxVersion: DOCKER|...`, AcrPull role for UAMI) + auth-bootstrap component (~150 LOC) + script-root config (`ScriptsDirectory` options already exist) + one hardcoded-"az" fix + smoke test. **Zero changes to the 25 collaborators or 19 handlers.** This is also the natural moment to close C1.7 (no repeatable L2 deploy path) — the image + workflow IS the deploy path.

**Operational trade-offs.**
- *Patching cadence*: you now own a base-image rebuild loop (monthly at minimum; on CVE alerts otherwise). az CLI is a large Python install (~1 GB layer) with its own CVE stream — expect Trivy noise. Image total ~1.5–2 GB.
- *Cold start*: first-pull minutes on scale/restart. Irrelevant at provisioning cadence (design.md: single-digit runs/day) — but keep Always On.
- *Cost*: ACR Basic (~$5/mo) + same App Service Plan. Negligible delta.
- *Observability*: unchanged (same App Insights wiring); script stdout/stderr already captured by the collaborators' output parsing.
- *Drift risk*: scripts are baked at image build — the image tag pins script versions to a commit (arguably an upgrade over "whatever's on the operator's disk").

**Security posture.**
- Attack surface UP: pwsh + az (Python + ~100 deps) + pac inside an internet-facing App Service. Mitigations: the App Service is Operator/Reader-JWT-gated fleet infrastructure, not customer-facing; tools are only reachable through code paths, not exposed endpoints; Trivy gate on image publish.
- Credential handling is a genuine improvement over today's implied model: UAMI (no secret) for everything az-shaped; the SP secrets for pac/Dataverse come from platform KV via app settings — same posture the C5.7/C5.8 gaps require under *every* option.
- Supply chain: az/pac installers pulled at image build — pin versions, build only in CI, sign/scan.

**Handlers affected**: all 13 shell-out handlers become executable with zero code change (H0, H2a, H2b, H3, H4, H5, H6, H8, H9\*, H12a, H12b, H13, H14). \*H9 caveat in §3.1. The 6 in-process handlers are indifferent.

**Reversibility: HIGH — the best of any option.** Handlers/collaborators are untouched, so a later per-collaborator SDK rewrite (→C) just deletes shell-outs one at a time and eventually thins the image. Flipping back to code-based App Service is one Bicep property.

---

### Option B — Publish `scripts/**` + install tools in App Service startup command

**What it is.** Keep stock App Service; add `<Content Include="scripts/**">` items to the csproj; startup command `apt-get install`s pwsh, pip-installs az, dotnet-tool-installs pac before launching the app.

**How handlers execute.** Same as A, if the installs succeed.

**Effort: SMALL to write — but included only to eliminate it explicitly.**

**Why it is eliminated.**
1. The blessed-image filesystem outside `/home` is ephemeral — every restart/scale/platform-patch re-runs a 5–15-minute multi-hundred-MB install from live package repos. Boot time becomes nondeterministic and network-dependent; a transient PyPI/apt outage bricks the control plane.
2. Installs at boot = unpinned, unscanned supply chain executing as the app identity on every restart — strictly worse security than A with none of A's controls.
3. Health probes and slot swaps race the install window.
4. It is the documented anti-pattern for App Service ("if you need tools, use a custom container" is Microsoft's own guidance).

**Reversibility**: trivially reversible, but there is nothing worth reversing from. **REJECTED.**

---

### Option C — Rewrite all shell-outs against .NET SDKs / REST

**What it is.** Replace all 25 shell-out collaborators with in-process implementations: `Azure.ResourceManager.*` (ARM deployments, App Service PATCH/slot-swap, role assignments, quota/usage reads, cost), `Azure.Security.KeyVault.Secrets` (H4), `Azure.Search.Documents` (H2b), `Microsoft.Graph` (H3 app-reg + H8 SPE containerType via `fileStorageContainerType`), Power Platform Admin/BAP REST (H5 env creation), Dataverse Web API `ImportSolution`/StageAndUpgrade (H6), Dataverse Web API (H12a/H12b seeds). Stock App Service; no tools.

**How handlers execute.** Pure in-process C# under `DefaultAzureCredential` (UAMI). No processes spawned; the interfaces (`IBicepDeployRunner`, `IKvSecretsWriter`, ...) already exist as seams, so handler cores don't change — only collaborator implementations.

**Effort: VERY LARGE.** The 13 scripts encode ~6,800 LOC of sequenced logic (14 Graph grants + consent handling in H3's 982 lines; dependency-ordered 8-solution import with retry semantics in H6's 847; the 13-step `Provision-Customer.ps1` at 1,632). Realistic estimate: 10–15k LOC of new C# + tests, multi-week-to-months — against a project whose 78-task WBS is otherwise ~96% complete.

**SDK gaps (the decisive facts).**
1. **H14a has NO .NET/REST equivalent.** Exchange `ApplicationAccessPolicy` is manageable ONLY via the `ExchangeOnlineManagement` PowerShell module (design.md R22 tracks the eventual RBAC-for-Apps successor — also PS-managed today). **Pure Option C is therefore impossible**; it degenerates into Option D no matter what.
2. **Bicep compilation**: `Azure.ResourceManager.Resources` deploys ARM JSON, not `.bicep`. Closable (bundle the self-contained `bicep` binary — it runs on the stock glibc image — or pre-compile to JSON in CI), but it's another moving part.
3. `pac` is fully replaceable (BAP/Power Platform Admin REST for env creation; Dataverse Web API for solution ops) — contrary to the option-D framing in the tasking, **pac is not the residual; Exchange is**.

**Operational trade-offs**: best-in-class once done — no image ownership, no CLI CVE stream, structured errors instead of stdout parsing, smallest attack surface. **Security posture: best** (no shells, UAMI-only, no ambient CLI sessions).

**Handlers affected**: all 13, each individually rewritten.

**Reversibility**: N/A — it's the end-state, but reaching it up-front delays the E2E deliverable by the largest margin of any option, and 9 of the 19 handlers still contain placeholder collaborators (gap Cat 3) that ALSO need real implementations first.

---

### Option D — Hybrid: SDK where mature, residual pwsh runtime for the rest

**What it is.** Rewrite the "easy" majority per Option C; keep a pwsh-capable runtime (sidecar container in the same Plan, or ACA Job, invoked via SB/HTTP) for the residual. Given §1.2, the *genuine* residual is exactly one sub-handler: **H14a** (Exchange). A pragmatic-residual variant keeps the 3 heaviest scripts too (H3, H6, H2a) and rewrites only the thin az one-liners.

**How handlers execute.** Most in-process (stock App Service); residual handlers serialize a request to the sidecar, which runs the script and returns a result envelope — a NEW cross-process protocol (contract, auth, timeout, log-plumbing, failure taxonomy mapping) invented for, at minimum, one 231-line script.

**Effort: LARGE** — strictly more than A (you still build and own a tools container for the residual) plus most of C, plus the invocation protocol nobody else needs.

**Operational trade-offs**: two runtimes to patch/deploy/observe instead of one; split-brain failure modes (sidecar down ≠ control plane down). **Security**: good (tools quarantined off the API host), but the sidecar still holds the same credentials, so isolation gain is modest.

**Handlers affected**: all 13 (rewritten) + H14a (+optionally H3/H6/H2a) on the sidecar.

**Reversibility**: fine, but it's the highest-complexity steady state. **As a *destination* D is sound; as the *first move* it maximizes cost and delay.** Note also: Option A already IS "D with residual = everything" — A can converge to D collaborator-by-collaborator without ever building the second runtime, by thinning the single image instead.

---

### Option E — Surfaced alternatives

- **E1: ACA Jobs / KEDA-scaled executor** (dispatcher + handlers in a tools-image Container Apps Job triggered per SB message; L2 App Service stays stock as API+reconciler). Genuine merits (per-run process isolation; no lock-renewal-vs-Always-On tension) but it reopens design.md B2's explicit Container-Apps rejection, splits the codebase across two hosts, and adds an ops surface for single-digit runs/day. Not recommended for r1; legitimate r2 evolution if handler workloads ever need burst isolation.
- **E2: CI-runner-as-executor** (handlers dispatch GitHub Actions workflow runs that have all tools preinstalled). Rejected: state/reconciler integration inverts (polling GH from L2), credentials move into GH secrets (weaker than UAMI), and the §4C failure taxonomy can't round-trip cleanly through workflow conclusions.
- **E3 (orthogonal, recommend REGARDLESS of A–D): make H9 artifact-based.** See §3.1.

---

## 3. Cross-cutting findings that bound every option

### 3.1 H9 is broken under EVERY option as designed
`DeployBffApiScriptRunner` → `Deploy-BffApi.ps1` runs `dotnet publish` (line 221) — it **builds the BFF from repo source at provision time**. That needs the full repo + dotnet SDK (+ Node for client assets) in the provisioning runtime — bloating the Option A image by gigabytes and being flatly impossible under C. The correct fix is independent of this decision: CI produces a versioned BFF publish artifact (it already builds the BFF); H9 becomes "fetch artifact → zip-deploy to staging slot → r3 gates → swap" — pure REST/SDK, no build. H9 should be re-scoped this way whichever option wins; until then, exclude `Deploy-BffApi.ps1`'s build step from any runtime plan.

### 3.2 Auth is the real risk, not tools
Scripts don't log in; they assume an ambient az session (§1.2). Under A, that ambient session must come from `az login --identity` (UAMI) — which must then satisfy every call site: `az ad app` writes need Graph `Application.ReadWrite.All` app-role on the UAMI; `az account get-access-token --resource` for Dataverse/Graph needs the corresponding grants; `pac` needs an SP secret (or MI support, to be verified); H14a needs the EXO cert in KV. This is gap C5.8 and it gates **all** options equally — SDK code under `DefaultAzureCredential` needs the exact same grants. A 1–2 day headless-auth spike (run H4's az calls + one `az ad app` write + one `pac admin` call under the UAMI from a container) retires the biggest unknown before committing.

### 3.3 The dispatcher decision is downstream of this one
C1.1 should be specified as "BackgroundService in the L2 process" and inherit this study's runtime answer. Building the dispatcher first against stock App Service would strand 13 of 19 handlers.

---

## 4. Recommendation

**Option A — custom container image — now, with two riders:**

1. **Rider 1 (E3)**: re-scope H9 to artifact-based deploy (no build at provision time) — required under every option; do it as part of the same wave.
2. **Rider 2 (A→C drift, post-E2E)**: after the E2E acceptance target is green, opportunistically replace the *thin* az-CLI collaborators (H4's three writers, `AzCliKvSecretReader`, `AzCliAppServiceSlotSwapper`, quota probes) with `Azure.ResourceManager`/`KeyVault` SDK calls — they are the stdout-parsing-fragile ones and each is a small, independently shippable deletion of shell surface. The heavy scripts (H3, H6, H2a) and H14a stay script-based until there's a concrete reason.

**Rationale.**
- The deliverable is E2E provisioning — full stop (owner directive). A is the only option that makes all 13 shell-out handlers executable with **zero handler/collaborator code change**, preserving ~96% of delivered work, and it simultaneously closes C1.7 (repeatable L2 deploy).
- Pure C is **impossible** (H14a has no REST equivalent) and would add 10–15k LOC before the first E2E run; D builds two runtimes to avoid one; B is an anti-pattern eliminated on operational-determinism grounds.
- A is the most reversible: it converges to C/D collaborator-by-collaborator by *thinning one image*, never by re-platforming.
- A is consistent with design.md B2's actual reasoning (App Service parity) — B2 chose a host, not an image.

**The ONE assumption that would flip this recommendation**: **headless auth parity** — that `az login --identity` (UAMI) + SP-based `pac auth` + cert-based `Connect-ExchangeOnline` can satisfy every operation the 13 scripts perform, with grantable app-roles (no operation secretly requiring a *delegated operator* identity beyond the already-gated manual steps like H3 admin consent). If the §3.2 spike shows a class of operations that cannot run under a workload identity, those collaborators must be rewritten (→D) or demoted to operator-gated steps regardless of runtime — and if that class is large, Option D becomes the honest architecture and A's "zero code change" advantage evaporates. Run the spike before committing the wave plan.

---

*Analysis-only artifact. Evidence: grep/read against the working tree as cited inline; no code, config, or Azure state modified.*
