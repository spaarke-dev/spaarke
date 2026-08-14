# Configuration & Deployment Architecture (#1 KV Federation) — Assessment & Remediation Design

> **Surface**: Configuration & deployment architecture (#1 KV federation) — `appsettings.template.json` + `appsettings.tokens.md`, `scripts/{Seed-ProductionKeyVault,Configure-ProductionAppSettings,Deploy-BffApi,Rotate-*}.ps1`, `infrastructure/bicep/**`, BFF config/DI modules (`Infrastructure/DI/*Module.cs`, `Configuration/*`), KV-consuming services (`SpeAdminGraphService`, `SpeAdminTokenProvider`, `TrackingTokenSigner`, `KnowledgeDeploymentService`), deploy workflows, and their docs/constraints.
> **Slug**: a **surface workstream** of [`code-quality-and-assurance-r3`](../../design.md) (r3 task 017 deliverable per owner decision 2026-08-13: "#1 KV federation → assess-first"). Executes in the r3 worktree on `work/code-quality-and-assurance-r3` as small PRs; NOT a standalone project.
> **Date**: 2026-08-14
> **Method**: quality-assessment workflow: 11-dimension fan-out + Fable adversarial verification (r3 spec NFR-05). Every finding below survived the mandatory adversarial-verification pass; 2 first-pass claims were refuted and are recorded in §3 as record-only.
> **Read-only statement**: This assessment modified NO code, tests, docs, or `.claude/` content. This design.md is the sole output (r3 NFR-03). The SCORECARD row is appended by the invoking task, not by this document.

---

## 0. Summary & verdict

The config/deploy/KV-federation surface is **operationally functional but naming-fractured and gate-defeated**, with **one live security failure it inherits from the BFF surface**. The DI/Options composition core is genuinely strong (Options\<T\> + ValidateOnStart, platform-resolved KV references, singleton pinned-UAMI credential, TTL-bounded caches, publish well under budget) — but around that core:

1. **Gating D3 = F.** The anonymous Dataverse-write + financial-disclosure endpoint (`FinanceRollupEndpoints.cs:29,45`) is live and unconditionally mapped — the rubric §3 *named* F exemplar ("an unauthenticated data-mutation endpoint", shipped and live). Remediation is already owner-decided (2026-08-06: `@spaarke/auth`, BFF workstream task 023) but **unlanded at HEAD**, so it gates this surface too. Compounded by no fallback authorization policy (auth-by-omission) and a credentialed CORS allow on all of `*.azurestaticapps.net`.
2. **Gating D2 = D+.** The deploy script's health-check poller **livelocks** (no retry increment, no sleep) whenever `/healthz` returns HTTP 200 `Degraded` — a reachable state via the in-pipeline Redis check, on the default live-dev deploy path.
3. **The FR-29 naming drift is fully confirmed and worse than assumed.** Four+ vault-naming conventions (none matching the only live vault `spaarke-spekvcert`), 3 aliases in 3 casing styles for one AI Search key, 6+ secret-name casing conventions in one template, two KV-reference token schemes (one undocumented), a 5-source hand-synced secret catalog with proven divergence, and an IaC path whose secret names and app-setting keys are naming-orphaned relative to the app's config schema.
4. **The build gate is silently off.** `Sprk.Bff.Api.csproj:9` overrides `TreatWarningsAsErrors=false`, defeating the exact centralized policy the blocking CI job trusts; the shipped Release configuration is never built by any blocking gate.

### Per-dimension grade table (re-adjudicated against `docs/standards/CODE-QUALITY-RUBRIC.md` §3, verified findings only)

| Dim | Area | Grade | Points | Movement vs first pass | One-line basis |
|---|---|---|---:|---|---|
| D1 | Architecture & boundaries | **B–** | 2.7 | unchanged | 4,910-LOC God class (`SpeAdminGraphService`); 2–3-spelling config-key fallbacks; minor split/stub debris |
| D2 | Correctness & reliability | **D+** | 1.3 | unchanged (anchor survived; one reinforcement refuted) | Deploy health-check livelock on `Degraded` — confirmed latent broken path on the live dev deploy path |
| D3 | Security | **F** | 0.0 | unchanged | Live anonymous Dataverse-write + financial disclosure endpoint — rubric §3's named F exemplar |
| D4 | Performance & scalability | **A–** | 3.7 | unchanged | Only minor hygiene (warn-only size guard, SizeLimit-less IMemoryCache, one unpinned-credential outlier with env-var mitigation) |
| D5 | DRY / dead code | **C** | 2.0 | unchanged | Copy-pasted KV fetch helper ×3; 3-alias secret; 5-source drifted catalog; superseded migration service; committed tarballs |
| D6 | Consistency & conventions | **C+** | 2.3 | unchanged | 4 vault-naming conventions, none matching live; IaC vs script secret-name divergence; 6+ casing styles in one template |
| D7 | Testability & test quality | **C+** | 2.3 | unchanged (D7-02 premise narrowed by verification, severity already MEDIUM) | Untested live `/api/config/client`; 25 unconditionally-skipped KV tests; banned wiring/reflection patterns |
| D8 | Dependency & supply-chain | **A** | 4.0 | **UP from A–** (sole finding refuted) | Zero verified findings; net10 baseline `dotnet list --vulnerable --include-transitive` = zero (see caveat §1) |
| D9 | Observability | **B+** | 3.3 | unchanged | 52 `Console.WriteLine` startup diagnostics bypass OTel; otherwise disciplined correlation/PII handling |
| D10 | ALM / build hygiene | **D+** | 1.3 | unchanged | Warnings-as-errors gate silently defeated on the deploy artifact; Debug-only CI; tarballs committed; IaC/live vault split |
| D11 | Knowledge/doc accuracy | **C** | 2.0 | unchanged | Both source anchors stale in the primary deploy constraint; retired App Service, fictional `Options/`, wrong dev vault documented |

**Composition (rubric §4.2)**: equal-weight mean = 24.9 / 11 ≈ **2.26 → C+**. Gating cap = min(C+, D2 = D+, D3 = **F**) = **F**.

> ## **Surface grade: F (gating cap applied — D3)**
>
> The mean (C+) is the honest maintainability read; the **F** is the honest security read. Per rubric §4.2 the cap is not waivable. **The single fastest grade-recovery action for this surface is landing BFF task 023** (Finance auth closure): with D3-01 closed, D3 re-scores on its residual findings (fallback policy, CORS suffix, secret drift, partial MI migration ≈ C+/B–), and the surface grade becomes min(C+, D+ , ~C+) = **D+**, then rises with the D2 livelock fix (S-effort) to the C band.

---

## 1. Grade re-adjudication notes (input discipline, NFR-05)

- **Only verified findings drove grades.** The two refuted claims (§3) drove nothing.
- **D2 stayed D+** even though refutation removed one of its three reinforcements (the APPLICATIONINSIGHTS_CONNECTION_STRING claim): the band anchor D2-01 (health-check livelock) survived intact and is precisely the rubric's D-band exemplar ("a latent broken path") *on this surface's own subject matter* (the deploy tooling). D+ (top of band) already priced in its edge-triggered, tooling-scoped nature; C-band language ("approaching but not yet a defect") does not fit a confirmed livelock.
- **D3 = F is deliberate and rubric-mandated.** D3-01 matches rubric §3's F row verbatim ("an unauthenticated data-mutation endpoint", shipped and live) and §3's gating note names "an unauthenticated Dataverse-write endpoint" as the lived-experience exemplar. Note the earlier BFF-surface row scored the same defect D3 **B–** — that row was produced 2026-08-05/06, *before* the rubric was published (task 001, 2026-08-14); under the now-standing ruler this defect is an F wherever it is scored. That tension is for task 016's re-baseline to note; this design does not rewrite the BFF row.
- **D8 moved UP A– → A**: its only finding (D8-01) was a refuted schema-test placeholder, leaving zero verified findings, corroborated by the net10 handoff (`dotnet list package --vulnerable --include-transitive` = zero across the graph; deliberate shared-lib pins documented; deferred majors tracked in GitHub #772). **Caveat**: the D8 finder produced only a placeholder, so this dimension's assessment depth is thin — the letter rests on the absence of verified findings plus the program-level net10 evidence, not on a dedicated deep pass. Task 032 (dependency/CVE) is the standing owner of this dimension; if it surfaces material findings, the SCORECARD row should be revised.
- **D7-02's premise was partially falsified in verification** (unit tests DO cover Dedicated/CustomerOwned routing with mocked SecretClient; only *live* KV/cross-tenant integration runs nowhere) — the MEDIUM severity in the verified record already reflects the narrower gap; no further discount.
- **Severity overrides and corrected file:lines from the verification pass are already applied** in the inventory below (e.g., D5-01's duplicate lives at `Infrastructure/Graph/SpeAdminGraphService.cs:4136`, not the Services/SpeAdmin stub; D5-03's vault default is at Seed L32).

---

## 2. Current-state inventory (verified findings)

Every row survived Fable adversarial verification. Effort: S (<½ day) / M (½–2 days) / L (>2 days). Risk = execution/regression risk of the remediation, incl. worktree contention (13/17 active worktrees touch BFF). Tranche per §5 (A = low-contention bugs/hygiene now; B = wide/contested edits, quiet window).

### 2.1 D3 — Security (gating; fix-first)

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D3-01 | **CRITICAL** | Anonymous Dataverse-write + financial-disclosure endpoint live and unconditionally mapped; doc-comment falsely claims it mirrors the auth'd Scorecard sibling; rate limiter partitions per-IP only (weak throttle, not auth) | `src/server/api/Sprk.Bff.Api/Api/Finance/FinanceRollupEndpoints.cs:29` (+`:45`) | 30 | S (code) / M (caller migration) | med | **A (gating — cross-owned)** | **Owned by BFF workstream task 023** (owner decision 2026-08-06: `.RequireAuthorization()` + migrate `sprk_subgrid_parent_rollup.js` to `@spaarke/auth`; NOT HMAC). This surface's action: verify closure at HEAD when 023 lands, then re-score D3. Do not duplicate the task. |
| D3-02 | MEDIUM | No fallback authorization policy — auth is opt-in per endpoint; D3-01 is the realized instance of auth-by-omission | `Infrastructure/DI/AuthorizationModule.cs:171` | 5 | M (5-LOC change + full AllowAnonymous endpoint sweep) | **high** | **B** | Set `options.FallbackPolicy = RequireAuthenticatedUser` after inventorying every intentionally-anonymous endpoint (config/client, healthz, webhooks) and marking each `.AllowAnonymous()` explicitly. Converts auth-by-omission to auth-by-exception. Test the full route table (401-by-default contract test). |
| D3-03 | MEDIUM | Credentialed CORS allows any `*.azurestaticapps.net` (attacker-registrable shared domain), also `*.powerappsportals.com`, with AllowCredentials + AllowAnyMethod | `Infrastructure/DI/CorsModule.cs:100` | 4 | S | med | **B** | Replace the suffix rule with the explicit external-SPA origin(s) via `Cors:AllowedOrigins` / `#{EXTERNAL_SPA_ORIGIN}#`; verify live SWA origins before removal. |
| D3-04 | MEDIUM | KV secret-name drift: 2 casing conventions + 2 reference syntaxes + 2 vault tokens in one template (FR-29 census) | `appsettings.template.json:132` (et al.) | 40 | M | med | **B** | This IS r3 task 063's input: publish the canonical secret-name standard (single casing, single reference syntax, single vault token) + current→canonical rename map; enforce via conformance gate. |
| D3-05 | MEDIUM | Dataverse still authenticates via long-lived `BFF-API-ClientSecret` (backing Graph/AzureAd/Dataverse/AgentToken) despite registered UAMI TokenCredential — ADR-028 partial migration | `appsettings.template.json:80` | 6 | L | high | **B (cross-owned)** | **Owned by NG1 / task 011 (#3b)** — shared-lib `ClientSecret`→MI migration (identity-attribution change; removing the secret today crashes startup). This surface's action: none beyond keeping the KV secret seeded until 011 lands. |
| D3-06 | LOW | Live App Insights connection string (InstrumentationKey + ApplicationId) hardcoded in committed seed script (unauthenticated ingestion abuse); tenant GUID also hardcoded | `scripts/Seed-ProductionKeyVault.ps1:190` (+`:82`) | 2 | S | low | **A** | Parameterize (env/`az` query) and rotate the exposed instrumentation key if the resource is live. (Same item as D9-03; remediate once.) |
| D3-07 | LOW | Six KV secrets referenced by the template are never provisioned by the seed inventory (orphan references; fail-closed so safe, but irreproducible) | `scripts/Seed-ProductionKeyVault.ps1:211` | 8 | S | low | **A** (interim) → **B** (canonical manifest) | Interim: add the six missing `Set-VaultSecret` entries (or document intentionally-out-of-band). Durable: drive seeder + template + tokens.md from one canonical manifest (D5-03). |

### 2.2 D2 — Correctness & reliability (gating)

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D2-01 | **HIGH** | Health-check poller livelocks on HTTP 200 `Degraded` — `$retryCount++`/`Start-Sleep` live only in the `catch`; the Redis check returns Degraded→200 on the included `/healthz` path; hits all 4 call sites incl. the default live-dev deploy | `scripts/Deploy-BffApi.ps1:183` (loop 180–195) | 18 | S | low | **A** | Increment + sleep on every non-Healthy outcome (move out of catch / add else); treat `Degraded` as a distinct terminal or acceptable-with-warning state; cap total wall-clock. |
| D2-03 | MEDIUM | `AiSearch:ApiKeySecretName='AzureAISearchApiKey'` is never created by the seeder; 3 divergent aliases documented for one value → guaranteed missing-secret lookup on a freshly seeded vault (CustomerOwned path) | `appsettings.template.json:258` | 8 | M | med | **B** | Collapse to one canonical secret name (task 063 standard), seed exactly that name, remove alias fan-out. **Pre-check LIVE App Service settings + KV first** (dev provisions out-of-band; deployment configs slated for Dataverse persistence — see §4). |
| D2-04 | MEDIUM | Template mixes two KV reference token schemes; `#{KEY_VAULT_NAME}#` absent from the co-located token doc → substitution driven off the documented list leaves 4 fail-closed webhook secrets as literal broken references | `appsettings.template.json:353` (+355, 388, 390) | 6 | S | low | **A** (doc) / **B** (standardize) | Now: add `#{KEY_VAULT_NAME}#` + dev value to `appsettings.tokens.md`. Durable: standardize on the SecretUri form (D6-05). ⚠ Tooling note: ripgrep's default ignores skip `appsettings.template.json` in this worktree — use `git grep` when editing/verifying. |
| D2-05 | LOW | `Email-WebhookSigningKey` / `communication-webhook-signing-key` required by the template are set by neither the app-settings script nor the seeder; the Configure script provisions the `[Obsolete]` `Email__WebhookSecret` instead of the required key | `scripts/Configure-ProductionAppSettings.ps1:107` | 6 | S | low | **A** | Add the signing-key app settings + KV secrets (or document as intentionally out-of-band); drop the obsolete `Email__WebhookSecret` line. |

### 2.3 D1 — Architecture & boundaries

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D1-01 | HIGH | God class: `SpeAdminGraphService` = 4,910 LOC single sealed class carrying KV-secret resolution + Dataverse config reads + Graph-client construction + TTL caching (singleton) | `Infrastructure/Graph/SpeAdminGraphService.cs:27` | 4,910 | L | **high** | **B** | Extract cohesive collaborators behind narrow interfaces (KV-secret resolver — pairs with D5-01's `IKeyVaultSecretReader`; container-type-config reader; Graph-client cache) until well under the 800-LOC bar. Heavily contested file (BFF MF-5 census) — quiet-window only, incremental PRs. |
| D1-02 | MEDIUM | Same logical setting read under 2–3 key spellings across the boundary (tenant/client/secret, UAMI clientId, vault URI, Redis, TENANT_ID) — no canonical key contract (code-side FR-29) | `Infrastructure/Graph/GraphClientFactory.cs:53` (+ManagedIdentityCredentialFactory.cs:31, SpeAdminModule.cs:39, CacheModule.cs:59, CommunicationEnrichmentService.cs:379 ×5) | 15 | M | med | **B** | Define one canonical key per logical value (Options\<T\> binding), collapse fallbacks, add the task-063 conformance gate. **Pre-check live App Service settings before removing any fallback spelling** — a live env may be feeding the alternate key. |
| D1-03 | LOW | Half-finished split: `Infrastructure/Cache` vs `Infrastructure/Caching` both own caching (both live, imported side-by-side) | `Infrastructure/DI/CacheModule.cs:5` | 0 | S | med | **B** | Consolidate under one namespace/folder (mechanical but multi-file → conflict-check) or document the intended distinction. |
| D1-04 | LOW | Orphaned 3-line comment-only migration stub from the completed Task-006 relocation; deletion safety statically proven (no type declared, no symbol name for `sprk_*` rows to reference) | `Services/SpeAdmin/SpeAdminGraphService.cs:1` | 3 | S | low | **A** | Delete the single file only — NOT the folder (siblings `SpeAdminTokenProvider`/`SpeAdminOptions` are live). |

### 2.4 D4 — Performance & scalability

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D4-01 | LOW | Deploy zip guard warns-only at 100 MB — 40 MB above the binding 60 MB HARD-STOP ceiling; CI deploy path has no size check at all | `scripts/Deploy-BffApi.ps1:264` | 4 | S | low | **A** | Fail (exit 1) at ≥60 MB, warn at ≥55 MB, expected ~45 MB — mirroring CLAUDE.md §10 thresholds. |
| D4-02 | LOW | Shared `IMemoryCache` has no `SizeLimit`; ConsumerRoutingService keys on open-cardinality mime/docType (TTL-only bound); `Size = 1` annotations elsewhere are inert | `Infrastructure/DI/CacheModule.cs:177` | 1 | S (document) / M (cap) | med | **A** (document) / **B** (cap) | Either document TTL-only as policy + remove the misleading `Size` annotations, or set `SizeLimit` **with a full entry-audit** (entries without `Size` throw once a limit exists — do NOT flip the switch alone). |
| D4-03 | INFO | Anonymous `/api/config/client` recomputes per request; no Cache-Control/OutputCache (static-per-deploy payload) | `Api/ConfigEndpoints.cs:60` | 40 | S | low | **A** (optional) | Add `Cache-Control: public, max-age=300` (or OutputCache policy). Bundle with D7-01/D9-02 which touch the same handler. |
| D4-04 | LOW | SpeAdmin `SecretClient` built with an inline unpinned `DefaultAzureCredential` instead of the DI UAMI-pinned TokenCredential — the exact 2026-05-24 multi-MI failure mode; mitigated today only by a manual `AZURE_CLIENT_ID` checklist step (not IaC, not startup-validated) | `Infrastructure/DI/SpeAdminModule.cs:44` | 1 | S | low | **A** | `sp => new SecretClient(new Uri(keyVaultUri), sp.GetRequiredService<TokenCredential>())` — same fix closes D5-08. Load-bearing consumer set: SpeAdminTokenProvider, SpeAdminGraphService, CiamGraphClientFactory, KnowledgeDeploymentService, ExternalAccessModule. |

### 2.5 D5 — DRY / dead code

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D5-01 | MEDIUM | KV secret-fetch helper copy-pasted near-verbatim across two services + a third partial variant; five independent `GetSecretAsync` call sites; no shared reader abstraction | `Services/SpeAdmin/SpeAdminTokenProvider.cs:318` (dup at `Infrastructure/Graph/SpeAdminGraphService.cs:4136`; variant `Services/Ai/KnowledgeDeploymentService.cs:461`) | 42 | M | med | **B** | Extract `IKeyVaultSecretReader.GetRequiredSecretAsync(name, ct)` with the shared 404/403/empty handling; inject into all five consumers. Natural first slice of the D1-01 decomposition. |
| D5-02 | MEDIUM | AI Search admin key stored/referenced under 3 KV secret names mirroring one value; deploy script writes two app settings from one secret | `appsettings.template.json:258` (+`:150`; tokens.md:53-55; `scripts/ai-search/Deploy-AllIndexes.ps1:617,624`) | 6 | M | med | **B** | Collapse to canonical `AiSearch--AdminKey` (or the task-063 canonical form); delete the two aliases; repoint `ApiKeySecretName` + `DocumentIntelligence:AiSearchKey`. ⚠ **requiresDataverseCheck — see §4 before deleting either alias.** |
| D5-03 | MEDIUM | KV secret catalog + vault identity re-declared across 5 hand-maintained sources with proven divergence (Seed omits 6 template-required secrets; 3 vault-naming schemes across the scripts; tokens.md self-inconsistent) | `scripts/Seed-ProductionKeyVault.ps1:32` | 60 | M/L | med | **B** | One canonical secret-catalog manifest (name + purpose + env) → generate seeder, app-settings script, and tokens doc from it; parameterize vault name by environment everywhere. This is the durable fix for D3-07/D2-03/D2-05. |
| D5-04 | LOW | `ai-openai-endpoint` / `ai-search-endpoint` each bound under two config keys in two option classes (rotation must touch two places) | `appsettings.template.json:292` (+`:132`, `:149`; Configure script) | 4 | S | med | **B** | One options class per external service; DocumentIntelligence consumes it; delete mirrored keys (both sides currently live — update consumers together). |
| D5-05 | LOW | ServiceBus + Redis connection settings duplicated across parallel sections, each with live consumers in different modules (silent-divergence surface) | `appsettings.template.json:30` (+`:16-17`, `:23`, `:85-86`) | 8 | S | med | **B** | Pick one binding location per resource (typed options OR ConnectionStrings); update the divergent consumers (`JobProcessingModule` vs `OfficeWorkersModule`; `CacheModule`). |
| D5-06 | LOW | Vault URL concept spread across 4 config keys + 2 token shapes; two readers use opposite fallback precedence | `Infrastructure/DI/SpeAdminModule.cs:39` (+TrackingTokenSigner.cs:194, AnalysisOptions.cs:205) | 6 | S | med | **B** | Standardize on a single `KeyVaultUri` key + one token shape; bind all consumers to it. |
| D5-07 | LOW | Superseded one-time embedding-migration BackgroundService retained + unconditionally registered behind a permanently-off flag whose own comment declares the migration complete; no ADR-032 seam, not data-driven dispatch | `Services/Ai/Jobs/EmbeddingMigrationService.cs:111` (reg `JobProcessingModule.cs:70`) | 120 | S | low | **A** | Delete service + options + registration + template block (`appsettings.template.json:175-176`), or document a concrete retention reason. |
| D5-08 | LOW | Second credential-construction path for the same KV client — diverges from the ADR-028 "reuse, never new a credential" pattern TrackingTokenSigner documents | `Infrastructure/DI/SpeAdminModule.cs:44` | 3 | S | low | **A** | Same one-line fix as D4-04 (single remediation). |
| D5-09 (=D10-02) | LOW | Two ~15.9 MB deployment tarballs tracked in the BFF source root (stale ~8 months; zero workflow/script consumers; pure committed build output) | `src/server/api/Sprk.Bff.Api/deployment.tar.gz:1` (+`spe-bff-api-deployment.tar.gz`) | 0 | S | low | **A (cross-owned)** | **Owned by BFF workstream task 027** (untrack both + `.gitignore`). This surface adds: update the manual-procedure mentions in `docs/SPE.BFF.API-TECHNICAL-OVERVIEW.md:469-490` alongside (pairs with D6-07). |

### 2.6 D6 — Consistency & conventions

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D6-01 | HIGH | Four divergent vault-naming conventions; none can produce the only live vault (`spaarke-spekvcert`); the bicep name is a hardcoded `var` (not a param) so IaC can never round-trip to the live env | `infrastructure/bicep/platform.bicep:100` (+customer.bicep:101, scripts, tokens.md, TECH-OVERVIEW) | 8 | M | med | **B** | Task 063: one canonical vault-naming standard + current→canonical alias map; make bicep accept the vault name as a param; purge `spaarke-kv-dev` and `kv-sdap-{env}` from docs. Honor the naming doc's "Dev DO NOT RENAME" guidance — codify `spaarke-spekvcert` as the explicit legacy exception. |
| D6-02 | HIGH | IaC path sets `OPENAI_API_KEY`/`AI_SEARCH_API_KEY`/`DOC_INTELLIGENCE_KEY` from KV secrets `openai-api-key`/`aisearch-admin-key`/`docintel-key` — different secret names AND app-setting keys than the script/template path; zero code binds the bicep-set flat keys (naming-orphaned) | `infrastructure/bicep/platform.bicep:178` (+180, 182) | 6 | M | med | **B** | Align platform.bicep to the canonical secret names + `__` app-setting keys, or delete its redundant AI-key app settings. |
| D6-03 | MEDIUM | One secret (AI Search admin key), 3 aliases, 3 casing styles, ≥2 live-consumed simultaneously (rotation hazard) | `appsettings.tokens.md:53` | 3 | M | med | **B** | Same remediation as D5-02 (single canonical name); see §4 pre-check. |
| D6-04 | MEDIUM | 4+ secret-name casing conventions inside one template; no standards doc arbitrates KV secret naming | `appsettings.template.json:16` | 12 | M | med | **B** | Task 063 canonical convention (e.g. kebab-case) + FR-29 rename map; add the KV-naming rule to `docs/standards/`. |
| D6-05 | MEDIUM | Two KV-reference syntaxes driven by two vault tokens for one vault; `#{KEY_VAULT_NAME}#` undocumented in the co-located token doc (the infra README that documents it covers a different template) | `appsettings.template.json:353` | 4 | S | low | **A** (doc) / **B** (standardize) | Same split as D2-04: document now, converge on SecretUri in the tranche-B canonicalization. |
| D6-06 | LOW | tokens.md example vault (`spaarke-kv-dev`) is fictional and contradicts the real dev value (`spaarke-spekvcert`) 85 lines later in the same file | `appsettings.tokens.md:16` | 1 | S | low | **A** | Fix the example to the real vault (or a clearly-fictional placeholder consistent with the standard). |
| D6-07 | LOW | Technical overview documents a fifth vault convention (`kv-sdap-{env}`) + secrets (`Graph-ClientSecret`, `Dataverse-ClientSecret`) that exist nowhere in the authoritative template | `src/server/api/Sprk.Bff.Api/docs/SPE.BFF.API-TECHNICAL-OVERVIEW.md:234` (+302-404) | 6 | S | low | **A** | Reconcile with the canonical template + rename map, or mark the section superseded (pairs with D5-09's doc touch). |
| D6-08 | LOW | Webhook secret names inconsistently separated within sibling blocks (`communication-webhook-signing-key` vs `compose-webhook-signingkey`); the run-together forms are documented live PROD secret names | `appsettings.template.json:388` | 4 | S | med | **B** | Rename to consistent separation under the canonical convention as part of the rename map (env-coordinated; prod currently decommissioned → cheap window). |

### 2.7 D7 — Testability & test quality

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D7-01 | HIGH | Live anonymous `/api/config/client` MSAL-bootstrap endpoint (branching authority/500/scope logic) has ZERO tests — missing ADR-038 "every endpoint ≥1 integration test" KEEP | `Api/ConfigEndpoints.cs:60` | 50 | S/M | low | **A** | Add a contract test: 200 shape (BffBaseUrl/MsalClientId/authority/scopes), organizations fallback for common/organizations, 500 when `AzureAd:ClientId` unset. TEST-MODIFYING rigor. |
| D7-02 | MEDIUM | 25 KV-deployment-model integration tests are 100% compile-time `[Fact(Skip=...)]` — run in NO environment (the in-file claim that an env var enables them is false); zero-signal coverage illusion. (Verification: unit-level routing coverage EXISTS in `KnowledgeDeploymentServiceTests`; the gap is live-KV/cross-tenant only) | `tests/integration/Spe.Integration.Tests/RagDedicatedDeploymentTests.cs:72` (+RagSharedDeploymentTests.cs) | 25 | M | low | **A** | Either convert to env-gated integration (runtime skip, not compile-time) with in-memory doubles at the module boundary, or delete both files and record the live-KV gap. `/test-diet` classifier applies. |
| D7-03 | MEDIUM | KV secret-name validation on live POST/PUT `/api/spe/configs` (the FR-29 enforcement logic) — empty/127-char/charset branches dead to the test suite | `Api/SpeAdmin/ConfigEndpoints.cs:468` | 20 | S | low | **A** | Contract test: empty, 128-char, underscore-containing `keyVaultSecretName` → 400 ProblemDetails with the specific messages. |
| D7-04 | MEDIUM | Production Redis-on cache branch permanently `[Fact(Skip)]` (connect-at-registration `AbortOnConnectFail=true` at `CacheModule.cs:97`); only dev fallback is exercised; live branch deferred to a manual PS harness | `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/CacheModuleTests.cs:76` | 25 | M | med | **B** | Extract the connect-time behavior behind a seam so the registration is testable without live Redis. ⚠ Testcontainers would add a NuGet package — r3's "no new NuGet" constraint makes the seam approach the default; a package needs explicit sign-off. |
| D7-05 | MEDIUM | Banned ADR-038 B3+B8 pattern: DI-resolution type assertion + private `_inner` reflection; decorator behavior (Meter emissions) already proven elsewhere but only via manual construction | `tests/integration/Sprk.Bff.Api.IntegrationTests/Cache/MetricsDistributedCacheRegistrationTests.cs:66` (+CacheModuleTests.cs:130) | 45 | S | low | **A** | Route the existing MeterListener behavior assertions (`TenantCacheMetricsTests`) through the DI-resolved `IDistributedCache`; drop the reflection + type-shape assertions. |
| D7-06 | LOW | ADR-038 B9/B11/B14/B16 scaffolding fillers (fluent-return, enum-count, default-value mirror) in options tests; surrounding binding/behavior tests are legitimate KEEPs | `tests/unit/.../Membership/MembershipOptionsTests.cs:113` (+AiOptionsTests.cs:11, :233) | 30 | S | low | **A** | Delete the three filler tests; retain the binding + behavior tests (incl. the production-0-results regression test). |

### 2.8 D9 — Observability

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D9-01 | MEDIUM | 52 `Console.WriteLine` config-toggle diagnostics across 5 startup DI modules (AnalysisServicesModule=43) bypass ILogger/OTel — invisible in App Insights; StartupDiagnostics.cs establishes the correct convention they violate | `Infrastructure/DI/TelemetryModule.cs:75` (+AnalysisServicesModule, JobProcessingModule:89-94, EmailServicesModule, TodoSyncModule) | 52 | M | med | **B** | Replace with module-scope `ILoggerFactory.CreateLogger` (StartupDiagnostics pattern) so toggle diagnostics flow through OTel with levels + correlation. AnalysisServicesModule is contested — quiet window. |
| D9-02 | LOW | Anonymous client-config bootstrap endpoint's 500 (missing `AzureAd:ClientId`) path is unlogged, no correlationId — a bootstrap-breaking misconfig is invisible server-side | `Api/ConfigEndpoints.cs:71` | 10 | S | low | **A** | Inject ILogger; warn on the 500 path; add `context.TraceIdentifier` as correlationId extension (SpeAdmin ConfigEndpoints convention). Bundle with D7-01 + D4-03. |
| D9-03 | INFO | (= D3-06, telemetry lens) Live App Insights connection string + tenant GUID committed in the seed script | `scripts/Seed-ProductionKeyVault.ps1:190` | 2 | — | — | **A (dedup)** | Remediated once under D3-06 (parameterize + rotate). Recorded here so the telemetry-config lens isn't lost. |

### 2.9 D10 — ALM / build hygiene

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D10-01 | **HIGH** | `TreatWarningsAsErrors=false` csproj override silently defeats the centralized warnings-as-errors policy that the tier-1 BLOCKING CI job and the Release publish explicitly trust (no `-warnaserror` passed by design); the `WarningsNotAsErrors` taxonomy is inert for the BFF | `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj:9` | 1 | M (1-line flip + clear/annotate the 21 pre-existing warnings) | med | **B** | Remove the override so the BFF inherits `Directory.Build.props` policy; move genuinely-tolerated codes into `WarningsNotAsErrors`. Coordinate with r3 task 041 (analyzers baseline; CS0618 ×3 known) — flip only after the warning inventory is cleared. |
| D10-02 | LOW | (= D5-09) Committed deployment tarballs | see D5-09 | 0 | S | low | **A (cross-owned)** | BFF task 027. |
| D10-03 | MEDIUM | `.gitignore` covers `*.zip` but not `*.tar.gz`/`*.tgz` — the root cause that let the tarballs in; team history shows recurring artifact-hygiene relapses | `.gitignore:63` | 1 | S | low | **A** | Add `*.tar.gz` + `*.tgz` beside the `*.zip` rule (same PR as the task-027 untrack). |
| D10-04 | MEDIUM | Three-way vault-name divergence: bicep provisions `sprkshareddev-kv`, live is `spaarke-spekvcert`, the standard mandates `sprk-{env}-kv` — a fresh IaC run stands up a vault holding none of the live secrets | `infrastructure/bicep/stacks/model1-shared.bicep:82` (+`:43`; config/environments.json:13 vs :105; naming doc :168/:305) | 4 | M | med | **B** | Derive the canonical `sprk-{env}-kv` in bicep AND codify `spaarke-spekvcert` as the explicit dev exception (per the naming doc's DO-NOT-RENAME guidance) — do not recreate the live vault. Wire a naming-conformance check (task 063) across IaC/config/standard. |
| D10-05 | MEDIUM | `deploy-bff-api.yml` targets only decommissioned prod infra; the `dev` dispatch input's sole consumer is the concurrency-group name — selecting "dev" deploys to the prod app service | `.github/workflows/deploy-bff-api.yml:30` (+148-149, 250-251, 282) | 6 | S/M | med | **B (coordinate)** | Parameterize RG/AppService/health URLs by the environment input, or retire the workflow (header already says operator-driven). **`.github/workflows` edits are owned by `ci-cd-unit-test-remediation-r1` — coordinate, don't unilaterally edit.** |
| D10-06 | LOW | Release configuration is compiled by no blocking gate (Debug-only CI matrix since 2026-06-24); shipped artifact is Release; nightly-health Release run is advisory-only post-merge | `.github/workflows/sdap-ci.yml:92` (+ci-tier1-blocking.yml:220) | 1 | M | med | **B (coordinate)** | Restore a Release matrix entry, isolating the flaky timing tests via the reliability registry rather than dropping the config. Same CI-ownership coordination as D10-05. |

### 2.10 D11 — Knowledge/doc accuracy

| ID | Sev | Finding | File:line | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---|---:|---|---|---|---|
| D11-01 | MEDIUM | Both Source Code References in the primary deployment constraint point at Program.cs lines that don't exist (file is 246 lines; CORS/ServiceBus validation moved to CorsModule/JobProcessingModule/StartupValidationService) | `.claude/constraints/azure-deployment.md:171` | 3 | S | low | **A** | Repoint to `Infrastructure/DI/CorsModule.cs` + `Infrastructure/DI/JobProcessingModule.cs:55-59`; prefer symbol anchors over fixed line numbers. (`.claude/` write = main-session-only per root §3.) |
| D11-02 | MEDIUM | Retired dev App Service `spe-api-dev-67e2xz` still documented in the matrix guide + deployment constraint (migrated 2026-05-27 → `spaarke-bff-dev`) | `docs/guides/CONFIGURATION-MATRIX.md:24` (+azure-deployment.md:108, :165) | 4 | S | low | **A** | Replace with `spaarke-bff-dev`; also sweep the stale echoes in `Create-TestSession.ps1:50` / `Import-And-Register.ps1:100`. |
| D11-03 | MEDIUM | Docs describe a `Configuration/` + `Options/` dual-directory layout; `Options/` does not exist (0 files on disk and in git; the two cited `Options/*.cs` paths actually live in `Configuration/`) | `docs/guides/CONFIGURATION-MATRIX.md:23` (+configuration-architecture.md:41-42, :75) | 3 | S | low | **A** | Document the single `Configuration/` directory + per-module options convention. |
| D11-04 | MEDIUM | Matrix guide names the AI-Foundry vault (`sprkspaarkedev-aif-kv`) as the dev BFF Key Vault; the BFF dev vault is `spaarke-spekvcert` (87 files corroborate) | `docs/guides/CONFIGURATION-MATRIX.md:25` | 2 | S | low | **A** | Set Dev Key Vault = `spaarke-spekvcert`; reserve the aif-kv for a distinct AI-Foundry row. |
| D11-05 | LOW | Naming-convention doc's disposition row still premised on "until dev gets proper Key Vault" — stale; dev has `spaarke-spekvcert` (secrets, not just certs) | `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md:292` | 1 | S | low | **A** | Update the disposition; mark `sprk-platform-prod-kv` per decommissioned-prod status. |
| D11-06 | LOW | Options-class inventory materially incomplete: docs say "21"/"20+"; actual ≈37+ options classes in `Configuration/` (~79 `class *Options` repo-wide); table omits ~15+ live classes | `.claude/constraints/config.md:122` (+configuration-architecture.md:13) | 2 | S/M | low | **A** | Regenerate the inventory from `Configuration/*.cs` or replace the fixed count with a generated reference. |

---

## 3. Explicit KEEPs / refuted claims (record-only — do NOT act on)

Per r3 spec NFR-05 input discipline, these were **refuted by the Fable verification pass**. They MUST NOT drive grades, appear as findings, or generate remediation items. Recorded so future passes don't re-claim them (model: the BFF pass's KEEPs section).

| ID | Refuted claim | Why refuted — and what future passes must know |
|---|---|---|
| D2-02 | "APPLICATIONINSIGHTS_CONNECTION_STRING is never set under the key the guard/UseAzureMonitor consume" | **FALSE.** The repo's own IaC sets the flat key at App-Service provisioning level: `infrastructure/bicep/platform.bicep:185` (+ `stacks/model1-shared.bicep:203`, `stacks/model2-full.bicep:211`, `byok/main.bicep:377`, slot-sticky at `modules/deployment-slot.bicep:31-37`). `az webapp config appsettings set` merges (never deletes), so the Bicep-provisioned key survives the Configure pass and `AzureMonitorGuard.ShouldWireExporter` receives a value. Residual observation (record-only, NOT a finding, NOT actionable here): the template's nested `ApplicationInsights:ConnectionString` (:216-218) and the Configure script's `ApplicationInsights__ConnectionString` (:103) have zero code consumers — dead config only. If a future pass wants to claim it, it must file and verify it fresh. |
| D8-01 | "test" (Directory.Packages.props:13) | Placeholder finding from a schema-mechanism test — no verifiable claim; cited evidence text absent from the file. Context for future D8 passes: `Directory.Packages.props:3` sets `ManagePackageVersionsCentrally=false`, so that file's `PackageVersion` items are not consumed by builds. |

**Deletion-safety KEEP notes carried from verification** (these are scope guards on the *verified* findings, binding on remediation):
- **D1-04**: delete the stub FILE only — `Services/SpeAdmin/` folder is alive (`SpeAdminTokenProvider`, `SpeAdminOptions` are real consumers of the `using Sprk.Bff.Api.Services.SpeAdmin` import).
- **D5-07**: `EmbeddingMigrationService` is config-flag-driven, NOT `sprk_*`-row-dispatched — no Dataverse pre-check needed; and it is NOT an ADR-032 seam (its own config comment declares the one-time migration complete).
- **D4-04/D5-08**: `docs/guides/auth-deployment-setup.md:158-163`'s manual `AZURE_CLIENT_ID` step currently masks the unpinned-credential defect in correctly-provisioned envs — do not mistake a working dev env for proof the code is right, and do not remove that guide step until the DI fix lands.
- **D10-04**: the naming doc's §"Dev Environment (DO NOT RENAME)" is intentional — remediation codifies the `spaarke-spekvcert` exception; it does NOT recreate/rename the live dev vault.

---

## 4. Data-driven-dispatch pre-checks (NFR-08 — run BEFORE the flagged remediation)

Dataverse `sprk_*` rows are not grep-provable. One verified finding carries `requiresDataverseCheck=true`:

| Finding | Remediation step at risk | Exact pre-check to run first |
|---|---|---|
| **D5-02** (collapse the 3 AI Search admin-key aliases; delete `ai-search-key` + `AzureAISearchApiKey` KV secrets) | Deleting either alias could break a runtime lookup that is configured in DATA, not code: `KnowledgeDeploymentService` resolves `config.ApiKeySecretName` per-tenant at runtime (`KnowledgeDeploymentService.cs:453`), deployment configs are slated for Dataverse persistence (`sprk_aiknowledgedeployment`, GitHub #229 — see comments at `KnowledgeDeploymentService.cs:96`), and the AI Search index surface is already Dataverse-driven (`sprk_aisearchindex`). | Before deleting or repointing either alias: (1) query LIVE Dataverse (spaarkedev1) for all `sprk_aiknowledgedeployment` rows (if the entity exists yet) and any `sprk_aisearchindex` rows whose config could carry a secret name — confirm no row stores `ai-search-key` or `AzureAISearchApiKey` as an `ApiKeySecretName`/equivalent value; (2) list LIVE App Service settings on `spaarke-bff-dev` (`az webapp config appsettings list`) for any setting still referencing either alias (dev provisions AiSearch out-of-band — repo grep is insufficient); (3) list the LIVE vault's secrets (`az keyvault secret list --vault-name spaarke-spekvcert`) to establish which aliases actually exist before touching any. Record the results in the remediation task's notes. |

Adjacent live-state pre-checks (not Dataverse, same spirit — repo grep is insufficient because live envs are provisioned out-of-band):
- **D1-02 / D5-04 / D5-05 / D5-06** (collapsing config-key fallbacks/mirrors): list live App Service settings first — a live env may feed the alternate spelling the code would stop reading.
- **D3-03** (CORS suffix removal): enumerate the live SWA origins actually in use before narrowing to explicit origins.
- **D3-06** (rotate App Insights key): confirm whether resource `bbbe0468-...` is live before rotation.

---

## 5. Proposed workstreams → phases (A/B tranche split per r3 NFR-04)

**Tranche A** = low-contention bug fixes + hygiene (scripts/, docs/, tests/, `.gitignore`, single-file src edits) — safe to run now as small PRs off the r3 branch, `/conflict-check` each. **Tranche B** = wide or contested edits (template/secret canonicalization, cross-module renames, auth-policy defaults, CI gates, `SpeAdminGraphService`) — batch for a quiet window; several are the substance of r3 task 063 (naming standard + conformance gate).

### Phase 0 — Gating closure (cross-owned; verify, don't duplicate)
- **0a. D3-01** — confirm BFF task 023 lands `.RequireAuthorization()` on `FinanceRollupEndpoints` + `@spaarke/auth` caller migration; re-score D3 at HEAD. *(The surface's F cap lifts here — highest-leverage item in this design.)*
- **0b. D3-05** — no independent action; tracked by task 011 (NG1 #3b).

### Phase 1 — Tranche A: correctness + hygiene (low contention, immediate)
- **1a. D2-01** — fix the deploy health-check livelock (retry/sleep on every non-Healthy outcome; Degraded terminal handling). *The one confirmed D2 latent-broken-path — cheap, isolated, high-value.*
- **1b. D3-06/D9-03** — parameterize the seeded App Insights connection string + tenant GUID; rotate the key if live.
- **1c. D2-05 + D3-07** — reconcile the seeder + Configure script against the template's full secret set (add the 6 orphans + 2 signing-key settings, or document out-of-band); drop the obsolete `Email__WebhookSecret` line.
- **1d. D2-04/D6-05 (doc half)** — add `#{KEY_VAULT_NAME}#` to `appsettings.tokens.md` with dev value (stops the unsubstituted-token failure mode until tranche B standardizes the syntax).
- **1e. D4-01** — deploy size guard: fail ≥60 MB, warn ≥55 MB.
- **1f. D4-04/D5-08** — SpeAdminModule SecretClient over the injected DI TokenCredential (one line; closes the 2026-05-24 failure-mode recurrence).
- **1g. D1-04** — delete the 3-line stub file.
- **1h. D5-07** — delete `EmbeddingMigrationService` + options + registration + template block.
- **1i. D5-09/D10-02/D10-03** — with BFF task 027: untrack tarballs, add `*.tar.gz`/`*.tgz` to `.gitignore`; update the TECH-OVERVIEW manual-procedure mentions.
- **1j. D4-02 (document half)** — record TTL-only bounding as policy; remove the inert `Size = 1` annotations (or explicitly defer to the tranche-B cap option).

### Phase 2 — Tranche A: tests + docs (additive/isolated; TEST-MODIFYING rigor where tests change)
- **2a. D7-01 + D9-02 + D4-03** — one PR on `/api/config/client`: contract tests (200 shape / organizations fallback / 500), ILogger + correlationId on the 500 path, optional Cache-Control.
- **2b. D7-03** — secret-name-validation contract tests (empty / 128-char / charset → 400).
- **2c. D7-05** — replace the DI-reflection cache test with DI-resolved MeterListener behavior assertions.
- **2d. D7-06** — delete the three ADR-038 scaffolding fillers.
- **2e. D7-02** — decide per file: env-gated runtime skip with in-memory doubles, or delete + record the live-KV gap (`/test-diet` classifier).
- **2f. D11-01..06 + D6-06 + D6-07** — doc-drift sweep: repoint azure-deployment.md anchors; `spaarke-bff-dev`; kill fictional `Options/`; correct dev vault to `spaarke-spekvcert`; fix the tokens.md example; reconcile/supersede the TECH-OVERVIEW naming scheme; regenerate the options inventory. (`.claude/` files are main-session-only writes per root §3.)

### Phase 3 — Tranche B: FR-29 canonicalization (the task-063 body of work; quiet window; live-state pre-checks per §4)
- **3a. Naming standard** — publish the canonical KV secret-name convention + vault-naming standard + single reference syntax/token (resolves D3-04, D6-01, D6-04, D6-05/D2-04 syntax half, D6-08); add the KV rule to `docs/standards/`.
- **3b. Canonical secret-catalog manifest** — one generated source for seeder + Configure script + tokens.md (D5-03; durably closes D2-03/D2-05/D3-07 class).
- **3c. Alias collapse** — AI Search key to one canonical name (D5-02/D6-03/D2-03) — **§4 Dataverse + live-state pre-check FIRST**; dual-bound endpoints/sections (D5-04, D5-05, D5-06); config-key fallback collapse via Options\<T\> (D1-02).
- **3d. IaC alignment** — platform.bicep secret names/app-setting keys to canonical (D6-02); vault name as parameter deriving `sprk-{env}-kv` with the codified `spaarke-spekvcert` dev exception (D6-01, D10-04).
- **3e. Conformance gate** — the task-063 check that new code/config cannot reintroduce alternate spellings, syntaxes, or vault forms.

### Phase 4 — Tranche B: auth + platform hardening (quiet window; behavioral blast radius)
- **4a. D3-02** — fallback authorization policy: inventory + explicitly mark every intentional `.AllowAnonymous()`, then set `FallbackPolicy=RequireAuthenticatedUser`; add the 401-by-default contract test. (Sequence AFTER Phase 0a so the Finance endpoints are already explicit.)
- **4b. D3-03** — replace the `*.azurestaticapps.net` credentialed suffix allow with explicit origins (live-origin check first).
- **4c. D9-01** — Console.WriteLine → ILogger sweep across the 5 DI modules (AnalysisServicesModule contested — `/conflict-check`).
- **4d. D4-02 (cap option, if chosen)** — `SizeLimit` + full entry `Size` audit.

### Phase 5 — Tranche B: build/CI gates (coordinate with ci-cd-unit-test-remediation-r1, which owns `.github/workflows`)
- **5a. D10-01** — clear/annotate the 21 pre-existing warnings (with task 041), then remove the `TreatWarningsAsErrors=false` override; tolerated codes → `WarningsNotAsErrors`.
- **5b. D10-06** — restore Release to the blocking matrix (flaky tests via the reliability registry).
- **5c. D10-05** — parameterize or retire `deploy-bff-api.yml` (dev selector is a no-op; targets decommissioned infra).
- **5d. D7-04** — seam-extract the Redis connect-at-registration behavior for automated coverage (no new NuGet without sign-off).

### Phase 6 — Tranche B: structural (largest, most contested — last)
- **6a. D5-01** — extract `IKeyVaultSecretReader` (shared 404/403/empty handling; 5 consumers).
- **6b. D1-01** — decompose `SpeAdminGraphService` (KV-secret resolver → 6a; container-type-config reader; Graph-client cache) in incremental behavior-preserving PRs until <800 LOC.
- **6c. D1-03** — consolidate `Infrastructure/Cache` vs `Infrastructure/Caching`.
- **Wrap-up** — `/test-diet` gate (phases 2/5 modify tests), doc-drift audit, re-score the SCORECARD row post-remediation.

**Sequencing rationale (NFR-04)**: Phase 0 lifts the F cap (owned elsewhere — verify only); Phase 1 removes the D2 anchor and all cheap isolated risk; Phases 2 is additive; Phase 3 is the surface's chartered FR-29 work and must precede 4a/5 (canonical names first, gates second); Phase 6 is deliberately last — biggest diff, most contested files, zero user-visible behavior change.

---

## 6. Cross-surface ownership & dedup (do not double-task)

| Item | Owner | This design's role |
|---|---|---|
| D3-01 Finance anonymous write | BFF workstream **task 023** (owner-decided `@spaarke/auth` path) | Verify closure; re-score D3; the gating cap lifts |
| D3-05 ClientSecret→MI (shared-lib Dataverse camp, #3b) | **Task 011** (NG1 assess-then-decide) | No independent remediation; keep the secret seeded until 011 lands |
| D5-09/D10-02 committed tarballs | BFF **task 027** | Add `.gitignore` `*.tar.gz` (D10-03) + TECH-OVERVIEW doc touch in the same window |
| D10-05/D10-06 workflow edits | **ci-cd-unit-test-remediation-r1** owns `.github/workflows` | Coordinate; provide findings + acceptance criteria |
| D10-01 warnings baseline | r3 **task 041** (analyzers-as-errors baseline) | Sequence the csproj flip after 041's warning inventory |
| D8 dimension ownership | r3 **task 032** (dependency/CVE) + GitHub #772 backlog | The A letter stands on zero verified findings + net10 zero-CVE; 032 may revise |
| Naming standard + conformance gate | r3 **task 063** | Phase 3 here IS that work's verified input (FR-29 census complete) |

---

## 7. SCORECARD row inputs (for the invoking task to append to `notes/SCORECARD.md`)

**Surface**: Configuration & deployment architecture (#1 KV federation) · **Assessed**: 2026-08-14 · **Method**: quality-assessment workflow (11-dimension fan-out + Fable adversarial verification) · 41 verified findings, 2 refuted.

**Row**: D1 **B–** · D2 **D+** · D3 **F** · D4 **A–** · D5 **C** · D6 **C+** · D7 **C+** · D8 **A** · D9 **B+** · D10 **D+** · D11 **C** → mean ≈ 2.26 (**C+**) → **Surface grade F** (gating cap: min(C+, D2 D+, D3 F) = F; not waivable per rubric §4.2).

Evidence bullets:
- **D1 B–** — 4,910-LOC God-class `SpeAdminGraphService` concentrating KV-secret resolution + Dataverse config reads + Graph-client build/cache (`Infrastructure/Graph/SpeAdminGraphService.cs:27`, ~6× the 800-LOC bar), plus 2–3-spelling config-key fallbacks with no canonical contract (`GraphClientFactory.cs:53-55` et al.); DI/Options composition core itself is clean.
- **D2 D+** — confirmed latent broken path: `Deploy-BffApi.ps1:183` health-check poller livelocks on HTTP 200 `Degraded` (retry/sleep only in catch; Redis check reaches Degraded on the included `/healthz`), on the default live-dev deploy path; secret-seeding split-brain (D2-03) + undocumented `#{KEY_VAULT_NAME}#` token (D2-04) reinforce; the App-Insights-key claim was refuted and did not drive this grade.
- **D3 F** — live anonymous Dataverse-write + financial-disclosure endpoint `POST /api/finance/{matters,projects}/{id}/recalculate` (`FinanceRollupEndpoints.cs:29,45`, unconditionally mapped, per-IP rate limit only) — the rubric §3 named F exemplar, shipped and live at HEAD; compounded by no fallback auth policy (`AuthorizationModule.cs:171`) and credentialed CORS on all `*.azurestaticapps.net` (`CorsModule.cs:100`). Remediation owner-decided (BFF task 023) but unlanded.
- **D4 A–** — only minor hygiene against an otherwise-bounded surface: warn-only 100 MB deploy guard vs the 60 MB HARD-STOP (`Deploy-BffApi.ps1:264`), SizeLimit-less shared IMemoryCache with inert `Size` annotations (`CacheModule.cs:177`), unpinned `DefaultAzureCredential` outlier on the SpeAdmin SecretClient (`SpeAdminModule.cs:44`, mitigated by the manual `AZURE_CLIENT_ID` step), INFO-level missing cache header on `/api/config/client`.
- **D5 C** — KV secret-fetch helper copy-pasted ×2 + third variant with five independent `GetSecretAsync` sites and no shared reader (`SpeAdminTokenProvider.cs:318` / `SpeAdminGraphService.cs:4136` / `KnowledgeDeploymentService.cs:461`); one AI Search key under 3 hand-synced secret names; the secret catalog re-declared across 5 drifted sources; superseded migration service still registered; ~31 MB of tarballs tracked.
- **D6 C+** — four vault-naming conventions none producing the live `spaarke-spekvcert` (bicep name is a hardcoded var — `platform.bicep:100`); the IaC and script deploy paths reference different KV secret names AND different app-setting keys for the same secrets, with the bicep-set flat keys bound by zero code (`platform.bicep:178` vs `Configure-ProductionAppSettings.ps1:91-96`); 4+ casing styles in one template with no arbitrating standard.
- **D7 C+** — live anonymous `/api/config/client` has zero tests (ADR-038 endpoint KEEP missing; `ConfigEndpoints.cs:60`); KV secret-name validation branches dead to the suite (`SpeAdmin/ConfigEndpoints.cs:468`); 25 KV-deployment tests 100% compile-time-skipped (zero signal); banned B3/B8 DI-reflection pattern persists — held above C by a genuinely behavior-first core (AzureMonitorGuard, MembershipOptions regression tests).
- **D8 A** — zero verified findings (sole first-pass claim was a refuted schema placeholder); net10 baseline `dotnet list package --vulnerable --include-transitive` = zero with deliberate pins documented and deferred majors tracked (#772); caveat: thin dedicated assessment — task 032 owns revision.
- **D9 B+** — 52 `Console.WriteLine` config-toggle startup diagnostics bypass the OTel pipeline the surface itself guards (`TelemetryModule.cs:75`, AnalysisServicesModule ×43), and the anonymous bootstrap 500 path is unlogged (`ConfigEndpoints.cs:71`); offset by disciplined correlation-ID scoping, PII refusal, masked config logging, and fail-fast AzureMonitorGuard.
- **D10 D+** — the blocking warnings-as-errors gate is silently defeated for the exact project it compiles (`Sprk.Bff.Api.csproj:9` override vs `Directory.Build.props:6`; no CI path passes `-warnaserror`; Release built by no blocking gate), tarballs committed via the `*.tar.gz` `.gitignore` gap, three-way IaC/live/standard vault-name divergence, and a deploy workflow whose dev selector is a no-op targeting decommissioned infra.
- **D11 C** — both source anchors in the primary deployment constraint point at non-existent Program.cs lines (`azure-deployment.md:171-172` vs the 246-line file); docs still name the retired `spe-api-dev-67e2xz` App Service, a fictional `Options/` directory, and the wrong dev Key Vault (`CONFIGURATION-MATRIX.md:23-25`) — pervasive operator-misdirecting drift, none mandating an anti-pattern.

---

*Assessment complete. This design is the verified input for remediation task creation (operator-gated per the r3 assessment-first decision). The SCORECARD row above is appended to `notes/SCORECARD.md` by the invoking task.*
