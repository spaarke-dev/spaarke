# Task 087 — Runtime /api/config endpoint (FR-36): Deviations & Notes

> **Task**: `087-config-json-runtime-endpoint.poml`
> **Author**: task-execute (Opus 4.7)
> **Date**: 2026-08-18
> **Baseline commit at start**: `3db061864`
> **Wave**: 4 Batch 4E — first serial task
> **Rigor**: FULL (BFF-touching + auth + deploy)

---

## Summary of changes

Implemented `GET /api/config` (Anonymous, short-cached 60s + ETag) returning the FR-36 public config bundle `{ bffUrl, msalClientId, tenantId, featureFlags }`. Backed by a new Tier-1 `PublicConfigOptions` with `ValidateOnStart()` per the r3 task 061 fail-fast pattern. Migrated two code-page bootstraps (SpaarkeAi + LegalWorkspace) to fetch the bundle after `setRuntimeConfig(...)` and before `ensureAuthInitialized()`, storing the flags in a page-scoped singleton with a `getFeatureFlag(name, default)` helper.

### Files created (5)

| File | Purpose |
|---|---|
| `src/server/api/Sprk.Bff.Api/Configuration/PublicConfigOptions.cs` | Tier-1 options class (`BffUrl`/`MsalClientId`/`TenantId` string; `FeatureFlags` open dictionary). Requiredness enforced by the validator below (r3 task 061 pattern), NOT by bare `[Required]` DataAnnotations. |
| `src/server/api/Sprk.Bff.Api/Configuration/PublicConfigOptionsValidator.cs` | Custom `IValidateOptions<PublicConfigOptions>` — env-aware fail-fast (Production/Staging/Demo/QA) with short-circuit in Development/Testing per §F.2.1 Testing allow-list stance. See D4 below for rationale. |
| `tests/integration/contract/Api/ConfigContractTests.cs` | 7 contract tests (anonymous 200, response shape, zero-secrets grep, Cache-Control, strong ETag, If-None-Match 304, mismatched-ETag 200). Compiled into `Sprk.Bff.Api.Tests` via the existing `Compile Include="..\..\integration\contract\**\*.cs"` glob. |
| `src/solutions/SpaarkeAi/src/config/publicConfig.ts` | Page-scoped feature-flag store + `fetchPublicConfig(bffBaseUrl)` + `getFeatureFlag(name, default)`. |
| `src/solutions/LegalWorkspace/src/config/publicConfig.ts` | Sibling of the SpaarkeAi helper (per-page copy to keep blast-radius contained; see §Cross-worktree coordination below for the reason the shared `@spaarke/auth` lib was NOT extended). |
| *(this file)* | Deviations record. |

### Files modified (5)

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/ConfigEndpoints.cs` | Extended the existing file (already hosts `/api/config/client`) with `MapPublicConfigEndpoint` mapping `GET /api/config` — Anonymous, rate-limited, produces `PublicConfigResponse` (camelCase JSON) with `Cache-Control: public, max-age=60` + strong `ETag` (`SHA256(body)`). Honors `If-None-Match` with `304 Not Modified`. Placement per §11 justification: extends the neighbor rather than adding a duplicate-concern sibling. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ConfigurationModule.cs` | Registered `PublicConfigOptions` binding + `ValidateDataAnnotations()` + `ValidateOnStart()`. |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` | Wired `app.MapPublicConfigEndpoint()` alongside the existing `MapMsalConfigEndpoints()` call in `MapHealthEndpoints`. |
| `src/server/api/Sprk.Bff.Api/appsettings.template.json` | Added a new `PublicConfig` section with placeholder tokens (`#{BFF_BASE_URL}#`, `#{API_APP_ID}#`, `#{TENANT_ID}#`) + inline `_PublicConfig_comment` documenting the Tier-1 fail-fast + zero-secrets invariants. |
| `src/solutions/SpaarkeAi/src/main.tsx` + `src/solutions/LegalWorkspace/src/main.tsx` | Import `fetchPublicConfig` and call it in the bootstrap sequence AFTER `setRuntimeConfig(config)` (so the BFF URL is known) and BEFORE `ensureAuthInitialized()` (per POML acceptance criterion "consumers fetch /api/config at bootstrap before MSAL init"). Non-blocking on error (helper catches internally). |

---

## Design decisions

### D1 — Extend the existing `ConfigEndpoints.cs` (not a new file)

The BFF already hosts `GET /api/config/client` (AIPU-091) for the MSAL bootstrap fallback. Per root CLAUDE.md §11 (Component Justification):

- **Existing**: `MapMsalConfigEndpoints` in `Api/ConfigEndpoints.cs` — the neighbor.
- **Extension**: adding a sibling `Api/PublicConfigEndpoints.cs` would duplicate the "anonymous non-sensitive config" concern in two files with two owners. Extending the existing file with a second `MapPublicConfigEndpoint` method keeps a single owner + one shared response-model class location.
- **Cost of doing nothing**: two files diverge over time; consumers face "which endpoint do I want" ambiguity. The extension is one method + two record types added to a 111-line file — no cost.

The two endpoints stay logically distinct (`/api/config/client` = MSAL-bootstrap fallback with `authority` + `scopes` derived from `AzureAd:*`; `/api/config` = FR-36 canonical bundle backed by Tier-1 `PublicConfigOptions`), but co-locating them in the same file makes it obvious that a future refactor should merge/deprecate one of the two once consumers migrate. See §Follow-ups for the consolidation plan.

### D2 — Strong ETag via SHA256 (not weak / not W/"…")

The response body is byte-stable for a given options snapshot (JSON is deterministic; `Dictionary<string,bool>` iteration order under `System.Text.Json` follows insertion order which comes from configuration binding). A strong ETag is correct per RFC 7232 §2.1. Implementation:

```csharp
var payload = JsonSerializer.SerializeToUtf8Bytes(response, ResponseJsonOptions);
var etag = "\"" + Convert.ToBase64String(SHA256.HashData(payload)) + "\"";
```

Cache-Control is `public, max-age=60` because feature flags SHOULD propagate within a minute (POML acceptance criterion "short cache reduces load without preventing cache-bust"). If a customer needs faster propagation, the operator can reduce this to `max-age=10` per-env — but 60s is the shipped default.

### D4 — Env-aware startup validation (r3 task 061 pattern) rather than bare `[Required]` + `.ValidateDataAnnotations()`

**Problem surfaced on the first full-suite run**: adding bare `[Required]` DataAnnotations + `.ValidateDataAnnotations()` broke **495 pre-existing tests** because the BFF has 30+ custom `WebApplicationFactory<Program>` fixtures (per `tests/integration/contract/Api/**` per-endpoint test files) that each maintain their own `ConfigureHostConfiguration` dictionary. None of them provided `PublicConfig:*` — so `ValidateOnStart()` failed at host build for every one of them.

**Options considered**:

1. **Add `PublicConfig:*` to every fixture** (per §F.2 fixture-config-first): ~30-40 files, mechanical sweep, high review cost, no safety benefit (the fail-fast still catches every deployed-env misconfiguration).
2. **Add `PublicConfig` defaults to `appsettings.Testing.json`**: only works for fixtures using `UseEnvironment("Testing")`; most fixtures use `UseEnvironment("Development")` so appsettings.Testing.json is not loaded.
3. **Env-aware custom validator**: enforce Required fields in Production/Staging/Demo/QA, short-circuit in Development/Testing. Chosen. Matches the AgentServiceOptionsValidator pattern (r3 task 061) — a custom `IValidateOptions<T>` that reads state (the env) and returns Success when short-circuiting.

**Implementation** (`PublicConfigOptionsValidator.cs`): checks `IHostEnvironment.IsDevelopment()` OR `EnvironmentName == "Testing"` (case-insensitive per §F.2.1) and returns `Success` when either is true. Bare `[Required]` was REMOVED from `PublicConfigOptions.cs` — the validator is the single source of truth (mirrors AgentServiceOptions where bare `[Required]` would break the disabled-boot path).

**Trade-off**: PublicConfig missing in a local `dotnet run` will boot cleanly but `/api/config` will return `{"bffUrl":"","msalClientId":"","tenantId":"","featureFlags":{}}`. Acceptable — the endpoint is DEV-only surfaces that never call it, and Prod/Staging enforce fail-fast. If a local-dev consumer needs valid values, they populate `PublicConfig:*` in user-secrets or a local `appsettings.Development.json`.

**Test evidence**: full BFF unit suite = **10,484 passed / 0 failed / 97 skipped** post-fix (baseline was 10,477 passed → +7 new `ConfigContractTests`; zero regressions).

### D3 — Feature flags stored as `Dictionary<string, bool>` (open shape)

The POML task shows two example flags (`featureA`, `featureB`) but leaves the shape open. Chosen `Dictionary<string, bool>` because:

- Simplest wire shape — no schema coupling between BFF and client bundles.
- New flags can be added via app settings without a code deploy.
- **NOT** a substitute for security policy: the invariant "server always enforces its own policies" is stated in `PublicConfigOptions.cs`. Client-side flags are advisory; if a flag needs to gate security-sensitive behavior, the enforcement lives on the server.

If a downstream project wants typed feature-flag definitions (e.g. `IFeatureFlags.EmailDraftingEnabled`), the extension seam is: add typed properties to a wrapper class, populate from the dictionary in a `FeatureFlagsAccessor` service. Out of scope for this task.

---

## Cross-worktree coordination — external-spa DEFERRED (BINDING per POML)

Per POML `<parallel-safe>false</parallel-safe>` + `<parallel-reason>` + this dispatch's coord constraint:

> External-spa surface is shared with `spaarke-SPA-external-access-platform-r1/r2` worktrees; must coordinate via `/conflict-check` before merge to avoid conflict; not parallel-safe with other worktrees editing external-spa bootstrap.

**This dispatch's scope was explicitly bounded to BFF + code-pages ONLY. External-spa was NOT touched. External-spa migration is a manual owner-coord action, not a subagent action.**

**Touchpoint description for owner coord** (what an external-spa PR needs to do):

1. **Fetch site**: external-spa's Vite entry (search paths under `external-spa/**` — worktree owners know the exact path). Fetch `/api/config` from the configured BFF origin at bootstrap, BEFORE MSAL init.
2. **Wire shape**: `{ bffUrl, msalClientId, tenantId, featureFlags }` — camelCase. Bff URL for a Spaarke Dev-tier env: `https://spaarke-bff-dev.azurewebsites.net`.
3. **Cache**: honor `Cache-Control` + `ETag`. Second call on the same page should be a browser-cache hit (or `304` if the browser revalidates).
4. **Non-blocking**: bootstrap MUST NOT hard-fail on a `/api/config` error — external-spa's config resolution should have a fallback (localStorage cache or build-time default) mirroring the pattern in `src/solutions/SpaarkeAi/src/main.tsx` L370–L396.
5. **CORS**: the anonymous endpoint already permits GET from every configured allowed origin (per `CorsModule` — `Cors:AllowedOrigins:N` entries). If external-spa's SWA origin is already listed there (it is, as of r3 task 030 / FR-17), no CORS change is needed.

Recommended coord message to `spaarke-SPA-external-access-platform-r1` + `r2` owners:

> _"customer-provisioning-orchestration-r1 task 087 has landed `GET /api/config` on the BFF (Anonymous, 60s + ETag) returning `{ bffUrl, msalClientId, tenantId, featureFlags }`. Code-pages LegalWorkspace + SpaarkeAi are migrated. External-spa migration deferred to your worktrees per hot-path overlap. Suggest wiring `fetchPublicConfig(bffBaseUrl)` in the external-spa bootstrap after config resolution and before MSAL init, then storing flags in a page-scoped module. See `src/solutions/SpaarkeAi/src/config/publicConfig.ts` for the reference impl."_

---

## Follow-ups (documented for r2 / operator backlog)

### F1 — SmartTodo + 30+ other code-pages not migrated

SmartTodo (`src/solutions/SmartTodo/src/main.tsx`) currently has NO bootstrap runtime-config path — `resolveRuntimeConfig()` is called from inside `SmartTodoApp.tsx` (component-scoped, deferred until mount). Adding the `/api/config` fetch to SmartTodo requires either:

- Moving `resolveRuntimeConfig()` into `main.tsx` first (broader refactor), OR
- Fetching `/api/config` from inside `SmartTodoApp.tsx` after the first `resolveRuntimeConfig()` succeeds — feasible but adds render coupling that the other code-pages don't have.

Neither option is in-scope for a single task. **Recommendation**: file as a follow-up refactor once the first feature flag actually needs to reach SmartTodo. Until then, `getFeatureFlag(name, defaultValue)` returns the default and SmartTodo behaves exactly as it does today.

The remaining code-pages under `src/solutions/*` (30+) similarly do not yet have a bootstrap that could integrate `/api/config`. Each is a separate migration.

### F2 — Consolidate `/api/config/client` + `/api/config`

The two endpoints now share `ConfigEndpoints.cs`. Once code-pages migrate to consume `/api/config` exclusively (both for MSAL bootstrap AND feature flags), `/api/config/client` becomes redundant and can be deprecated. This is a broader r2/r3 project (touches every code-page's `main.tsx`) — out of scope here.

### F3 — Shared `@spaarke/auth` helper instead of per-page copy

The `publicConfig.ts` helper is duplicated between SpaarkeAi and LegalWorkspace (byte-identical modulo the `errorLabel` string). Extracting to `@spaarke/auth` would be cleaner but expands blast-radius (every consumer rebuilds). Deferred pending SmartTodo + other code-page migrations that would benefit from the shared helper.

---

## §10 BFF Hygiene evidence

Per root CLAUDE.md §10 + `.claude/constraints/bff-extensions.md`:

- [x] **Placement decision**: extended existing `ConfigEndpoints.cs` (per D1 above). Rejected: separate `PublicConfigEndpoints.cs` file (duplicated concern).
- [x] **Feature-module DI convention**: registered via `AddConfigurationModule` (existing feature module). No new DI module added.
- [x] **Endpoint uses Minimal API** (`app.MapGet`), Anonymous, rate-limited (`.RequireRateLimiting("anonymous")`).
- [x] **No new package references** — uses `Microsoft.Net.Http.Headers` (already transitively present via `Microsoft.AspNetCore`), `System.Security.Cryptography` (BCL), `System.Text.Json` (BCL). CVE risk delta: zero.
- [x] **No CRUD→AI dep** — no `Services/Ai/*` types injected.
- [x] **Test update obligation**: 7 contract tests added at KEEP path `tests/integration/contract/Api/` per `tests/CLAUDE.md`.
- [x] **Placement Justification** (this section) — cited in commit message + this deviations doc.
- [x] **Publish-size delta**: reported in §Verification below (measured post-change).
- [x] **CVE scan**: zero new HIGH-severity CVEs (see §Verification).

---

## Verification

### Build (0 warnings / 0 errors)

```
dotnet build src/server/api/Sprk.Bff.Api/
Build succeeded. 0 Warning(s) 0 Error(s)
```

### Unit tests

- **New tests (task 087)**: 7 passing, 0 failing (see `ConfigContractTests` — `dotnet test --filter FullyQualifiedName~ConfigContractTests`).
- **Full BFF unit test suite**: **10,484 passed / 0 failed / 97 skipped** (baseline 10,477 + 7 new = 10,484 — zero regressions). Duration ~3m24s locally.

### Publish size (per NFR-01)

- **Baseline** (task 086 shrink, per POML): 43.64 MB compressed
- **Post-task-087**: **43.67 MB compressed** (45,788,930 bytes) — measured via `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/` then zip-of-output-dir with DEFLATED compression, matching the r1 measurement convention.
- **Delta**: **+0.03 MB** (~30 KB). Well under the ≥+5 MB single-task escalation threshold. No new package references were added (the endpoint uses only BCL types + `Microsoft.Net.Http.Headers` which was already transitively present).

### CVE scan (`dotnet list package --vulnerable --include-transitive`)

`The given project 'Sprk.Bff.Api' has no vulnerable packages given the current sources.` — zero HIGH-severity CVEs.

---

## Task-execute audit trail

- Rigor level: **FULL** declared at task start.
- ADRs consulted: ADR-010 (DI minimalism — feature-module extension), ADR-013 (AI architecture — no CRUD→AI dep introduced), ADR-001 (Minimal API), ADR-008 (endpoint filters — not applicable to anonymous endpoint but validated absence is correct).
- Sub-agent write boundary: none of the changed files live under `.claude/**` — no boundary violations.
- Escalation triggers: none fired. External-spa coord DEFERRED per this dispatch's binding scope; documented above for owner action.
