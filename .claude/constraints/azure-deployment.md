# Azure Deployment Constraints

> **Domain**: Azure App Service Configuration, Deployment Safety
> **Last Updated**: 2026-02-18
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Verified

---

## When to Load This File

Load when:
- Deploying to Azure App Service
- Modifying appsettings or configuration
- Troubleshooting 500.30 startup errors
- Setting up new environments

---

## MUST Rules

### Deployment Safety

- **MUST** configure all required settings in Azure App Settings (not in deployed files)
- **MUST** exclude `appsettings.template.json` from publish output (already configured in .csproj)
- **MUST** verify required settings exist before deployment
- **MUST** use Key Vault references for secrets (`@Microsoft.KeyVault(SecretUri=...)`)

### CORS Configuration (CRITICAL)

- **MUST** configure `Cors__AllowedOrigins__N` in Azure App Settings for Production environments
- **MUST** include both `.crm.dynamics.com` and `.api.crm.dynamics.com` origins

---

### Publish & Packaging

- **MUST** publish to `deploy/api-publish/` (outside the project source tree) to avoid recursive artifact nesting
- **MUST** set `stdoutLogEnabled="true"` in the published `web.config` before packaging (dotnet publish resets it to false)
- **MUST** set `<RuntimeIdentifier>linux-x64</RuntimeIdentifier>` + `<SelfContained>false</SelfContained>` in `Sprk.Bff.Api.csproj` — framework-dependent Linux publish (FR-A1 per `sdap-bff-api-remediation-fix` project). Eliminates the entire `runtimes/` directory tree (10 RIDs → eliminated on Linux App Service) and matches the target App Service OS.
- **MUST** exclude wwwroot sourcemaps from publish via `<Content Update="wwwroot\**\*.js.map" CopyToPublishDirectory="Never" />` in `Sprk.Bff.Api.csproj` (FR-A2). Sourcemaps remain in the source tree for local debugging but never ship.
- **MUST** verify zip entry count (~215) and size (~45 MB) before deploying — oversized zips indicate stale publish dirs in source. Current baseline is **44.96 MB compressed incl. PDBs** (2026-08-13, `dotnet-10-upgrade-r1` task 031, on the .NET 10 framework-dependent linux-x64 publish; 44.05 MB excl. PDBs — state the PDB convention when reporting). History: 72.9 MB (2026-05-19) → 45.65 (2026-05-26) → 49.63 (2026-07-08 net8) → 44.96 (2026-08-13 net10) → **45.42 (2026-09-02, `origin/master` @ `a826cf347`)**. ⚠️ **Re-measure master; do not diff against the number above.** Master grew +0.46 MB in the three weeks from 2026-08-13 to 2026-09-02 purely from other projects' merges — enough for a project contributing +0.01 MB to report +0.47 and open a spurious investigation (`spaarkeai-compose-r8`, 2026-09-02). The recorded figure is a sanity check; the measurement is a fresh `origin/master` publish zipped with the SAME tool on the SAME day. Note also that Python `shutil.make_archive` yields **44.11 MB** for the byte-identical folder Compress-Archive yields 45.42 — a 1.31 MB tool delta, and the likeliest mechanism behind the 2026-08-27 three-agent 1.29 MB spread recorded below.
- **MUST** use `az webapp deploy --type zip` or Kudu zipdeploy API for deployment (ensures atomic replacement)

> **Phase 5 demo deploy verified** the framework-dependent linux-x64 publish removes the entire `runtimes/` directory tree (10 RIDs → eliminated). See `projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md` Phase 4 Outcome A for evidence.

### BFF Publish-Size Per-Task Verification Rule (NFR-01)

**Binding workflow rule. Operationalizes ADR-029 (BFF publish hygiene). Added 2026-05-26 per R4 NFR-01 / F-3.**

Every task that touches `src/server/api/Sprk.Bff.Api/` (or `Spaarke.Core` / `Spaarke.Dataverse` consumed by BFF) — including endpoint additions, service additions, DI registration changes, NuGet package additions/upgrades, and background-job work — **MUST**:

1. **MUST** run `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` AFTER changes land and BEFORE merge.
2. **MUST** measure compressed size of `deploy/api-publish/` (or the resulting zip if packaging) and report the absolute size + diff vs the prior measured baseline in the task notes / PR description.
3. **MUST** compare against the binding **ceiling of ≤60 MB compressed** (per spec NFR-01). The current measured baseline as of 2026-08-13 is **44.96 MB incl. PDBs** (`dotnet-10-upgrade-r1` task 031, Compress-Archive Optimal over `deploy/api-publish/*`, .NET 10 framework-dependent linux-x64 — state the PDB convention when reporting; 44.05 MB excl. PDBs). Prior net8 baseline: 49.63 MB incl. PDBs (2026-07-08 task 055). Tasks pushing toward 60 MB MUST flag the trajectory in code review.
4. **MUST** verify no new HIGH-severity CVEs via `dotnet list package --vulnerable --include-transitive` if NuGet packages were added or upgraded.
5. **MUST** cross-reference CLAUDE.md §10 in the task notes / PR description (e.g., "BFF Hygiene §10 + NFR-01 verified: publish size = X MB, delta = Y MB, no new HIGH CVEs").

### ⚠️ Measurement convention is BINDING, not descriptive (added 2026-08-27 by `unified-access-control-r2` Wave A)

**The number is meaningless unless it was produced the same way as the number you compare it to.** Report **all five** of these alongside the size, every time:

| Must state | Canonical value for the baseline |
|---|---|
| Command | `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` |
| RID / deployment mode | **framework-dependent linux-x64** (NOT self-contained, NOT win-x64) |
| Configuration | **Release** (a Debug publish is not comparable) |
| Compression | **`Compress-Archive -CompressionLevel Optimal`** over `deploy/api-publish/*` |
| PDBs | **included** for the 44.96 figure; 44.05 excludes them — say which |

**Why this became binding — a real incident, not a hypothetical.** On 2026-08-27 three sub-agents on the **identical base commit**, each stating *"compressed incl. PDBs"*, reported **45.07 / 45.07 / 43.78 MB** — a **1.29 MB spread on the same tree**. Cause: the POML corpus carried **two baseline clusters** (~43.65–43.71 MB across 24 POMLs and 44.96 MB across 31), so each agent compared against whichever its own POML cited, computed a small delta (+0.11, +0.11, +0.09), and correctly concluded "within ceiling". *Every individual report was internally consistent and correct.* The set was incoherent, and the defect was visible only by comparing reports across agents.

**Consequence, and why it matters even far below the ceiling**: the ≤60 MB HARD STOP still bounds the worst case regardless of convention. But the **≥+5 MB single-task escalation below is a drift detector**, and a 1.3 MB convention gap in circulation means a genuine regression can be absorbed as a convention artifact — or a convention change misread as a regression. The gate keeps its floor and loses its sensitivity, which is the half it was actually added for.

**Therefore**:
- **MUST** state all five fields above. A bare "45.07 MB" is an incomplete report and a reviewer should reject it.
- **MUST NOT** compare across conventions. If the cited baseline's convention is unstated or differs, re-measure the baseline yourself on the merge-base commit rather than differencing two incomparable numbers.
- When authoring POMLs, cite **44.96 MB incl. PDBs** (the canonical baseline above). POMLs citing the ~43.7 cluster are **stale** and should be re-baselined on next touch.

**Threshold for escalation**:
- Diff ≥ +5 MB single-task: explicit justification required in PR description; reviewer must explicitly accept.
- Cumulative size ≥ 55 MB: escalate to architecture review BEFORE merging the task that would tip it over.
- Cumulative size ≥ 60 MB: HARD STOP. Roll back or extract; do not exceed the ceiling without an ADR amendment to ADR-029.

**Why this rule exists**: The 2026-05-19 publish-size jump (65 → 75+ MB) and the 2026-05-20 BFF AI extraction assessment surfaced ~20 inbound CRUD→AI direct dependencies that accumulated unnoticed. Per-task verification with explicit diff reporting catches accumulation before it compounds. See [`bff-extensions.md`](bff-extensions.md) for the broader §10 pre-merge checklist and [`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md`](../../docs/assessments/bff-ai-extraction-assessment-2026-05-20.md) for the evidence base.

**Source of truth**: Spec NFR-01 in `projects/spaarke-ai-platform-unification-r4/spec.md`; root [`CLAUDE.md`](../../CLAUDE.md) §10 item 4 (strengthened in R4 F-3); ADR-029 BFF publish hygiene.

### Minimal API Endpoints

- **MUST** use MapPost/MapPut/MapPatch for endpoints that accept body parameters (complex types)
- **MUST NOT** use MapGet/MapDelete with handler parameters that would be inferred as body — this compiles but crashes at startup

### BackgroundService Dependencies

- **MUST** use `IServiceProvider` for lazy resolution of external-connecting singletons (Dataverse, OpenAI) in BackgroundService constructors
- **MUST NOT** inject eagerly-connecting singletons directly into BackgroundService constructors — a connection failure kills the host

## MUST NOT Rules

- **MUST NOT** deploy `appsettings.json` files with configuration values
- **MUST NOT** deploy `appsettings.template.json` (contains unresolved placeholders)
- **MUST NOT** hardcode secrets in any deployed files
- **MUST NOT** use `ASPNETCORE_ENVIRONMENT=Production` without CORS settings
- **MUST NOT** publish to a directory inside the project source tree (causes recursive nesting in subsequent publishes)

---

## Required Azure App Settings

These settings MUST exist for the app to start in Production:

### Core Settings

| Setting | Example Value | Notes |
|---------|---------------|-------|
| `ASPNETCORE_ENVIRONMENT` | `Development` or `Production` | Production requires CORS |
| `TENANT_ID` | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` | Azure AD tenant |
| `API_APP_ID` | `xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx` | BFF API app registration |

### CORS Settings (Required for Production)

| Setting | Value |
|---------|-------|
| `Cors__AllowedOrigins__0` | `https://{org}.crm.dynamics.com` |
| `Cors__AllowedOrigins__1` | `https://{org}.api.crm.dynamics.com` |

**Dev Environment (`spe-api-dev-67e2xz`):**
```
Cors__AllowedOrigins__0 = https://spaarkedev1.crm.dynamics.com
Cors__AllowedOrigins__1 = https://spaarkedev1.api.crm.dynamics.com
```

### Connection Strings (Key Vault References)

| Setting | Format |
|---------|--------|
| `ConnectionStrings__ServiceBus` | `@Microsoft.KeyVault(SecretUri=https://{vault}.vault.azure.net/secrets/ServiceBus-ConnectionString)` |
| `ConnectionStrings__Redis` | `@Microsoft.KeyVault(SecretUri=https://{vault}.vault.azure.net/secrets/Redis-ConnectionString)` |

### AI Services (Optional)

| Setting | Purpose |
|---------|---------|
| `DocumentIntelligence__Enabled` | Enable/disable AI features |
| `DocumentIntelligence__OpenAiEndpoint` | Azure OpenAI endpoint |
| `DocumentIntelligence__OpenAiKey` | Key Vault reference |

---

## Startup Failure Modes

The app will fail to start (HTTP 500.30) if:

1. **CORS missing in Production**: `Cors:AllowedOrigins` empty when `ASPNETCORE_ENVIRONMENT != Development`
2. **ServiceBus missing**: `ConnectionStrings:ServiceBus` is null or empty
3. **Wildcard CORS**: `Cors:AllowedOrigins` contains `*`
4. **GET endpoint with body parameter**: A Minimal API GET handler accepts a complex type that gets inferred as a body parameter. Compiles but crashes at startup during endpoint metadata build. Fix: use MapPost, or restructure as query parameters.
5. **BackgroundService with eager singleton**: `AddHostedService<T>` resolves constructor deps at `IHost.StartAsync()`. If a dep (e.g., `DataverseServiceClientImpl`) connects eagerly and fails, the host crashes. Fix: inject `IServiceProvider` and resolve lazily in `ExecuteAsync()`.

---

## Deployment Verification Checklist

Before deploying:

- [ ] Azure App Settings include all required CORS origins
- [ ] `ASPNETCORE_ENVIRONMENT` matches target environment
- [ ] Connection strings reference Key Vault (not plain text)
- [ ] Publish output does NOT contain appsettings.json files

After deploying:

- [ ] Health check passes: `GET /healthz` returns 200
- [ ] Ping endpoint works: `GET /ping` returns `pong`
- [ ] Stdout log shows `Configuration validation successful` (not a startup exception)
- [ ] No `500.30` on initial page load in browser

---

## Environment Reference

| Environment | App Service | Dataverse Org | CORS Origins |
|-------------|-------------|---------------|--------------|
| Dev | `spe-api-dev-67e2xz` | `spaarkedev1` | `https://spaarkedev1.crm.dynamics.com`, `https://spaarkedev1.api.crm.dynamics.com` |

---

## Source Code References

- CORS validation: [Program.cs:731-736](src/server/api/Sprk.Bff.Api/Program.cs#L731)
- ServiceBus check: [Program.cs:593-600](src/server/api/Sprk.Bff.Api/Program.cs#L593)
- Publish exclusion: [Sprk.Bff.Api.csproj](src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj)

---

**Lines**: ~100
**Purpose**: Prevent deployment failures from missing Azure configuration
