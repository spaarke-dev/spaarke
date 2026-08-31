# Dataverse Plugins — Quality Assessment & Remediation Design

> **Surface**: Dataverse plugins — `src/dataverse/plugins/Spaarke.CustomApiProxy/` (sole plugin assembly: `Spaarke.Dataverse.CustomApiProxy`, 3 source files ~650 LOC, net462) + its guard tests (`tests/Spaarke.ArchTests/ADR002_PluginTests.cs`) + its knowledge layer (`.claude/patterns/dataverse/plugin-structure.md`, module README/tech-overview).
> **Date**: 2026-08-14
> **Method**: quality-assessment workflow: 11-dimension fan-out + Fable adversarial verification (r3 spec NFR-05). Every finding below survived the mandatory adversarial-verification stage; zero claims were refuted this pass.
> **Read-only statement**: This assessment modified NO code, tests, docs, or `.claude/` content. This design.md is the sole output (r3 NFR-03). The SCORECARD row is appended by the invoking task, not by this document.
> **Program**: Surface workstream #7 (task 015) of [`code-quality-and-assurance-r3`](../../design.md). Executes in the r3 worktree on `work/code-quality-and-assurance-r3`; remediation tasks are created only after owner review of this design (assessment-first, owner decision 2026-08-06).

---

## 0. Summary & verdict

**The surface is one obsolete assembly whose entire reason for existence is the canonical ADR-002 violation** — a Custom API proxy that makes HTTP calls to the BFF and acquires OAuth2 tokens from Azure AD *inside* the Dataverse plugin pipeline. Both classes are honestly self-marked `[Obsolete]` (2026-03-15, R2), the repo-side solution registers nothing, and a fenced arch test freezes the violation count — the debt is *contained*. But the R3 #9 disposition (invert vs decommission) is still unresolved two program cycles later, and the R2 assessment already proved in-sandbox IoC inversion is architecturally impossible (no DI, no IHttpClientFactory, sync-only, no token caching). **Decommission is the only valid disposition, and this design's central recommendation is to execute it.**

What verification added beyond "it's obsolete debt":

1. **The plugin is broken against the current BFF even if live** — the leading-slash relative endpoint drops the `/api` base path (request 404s vs the real route table) while the trace logs a hand-concatenated *wrong* URL (D6-07); and the app-only bearer it sends cannot survive the BFF's OBO exchange (D3-02 verification).
2. **A real secret-at-rest exposure**: the OAuth client secret is a plain-text column on a UserOwned Dataverse table with no Key Vault and no field security — and because the plugin runs under the *invoking user's* OrganizationService, end users of the "Production"-declared Custom API must hold read on the raw secret for the feature to work at all (D3-01).
3. **A HIGH CVE invisible to the repo's CVE gate**: transitive `System.Text.Json 6.0.8` (CVE-2024-43485) resolves live in this net462 graph, which is outside Spaarke.sln, outside every CI workflow, and outside the net10-scoped task-032 "ZERO CVEs" certification (D8-01, D10-01).
4. **The knowledge layer actively teaches the anti-pattern**: a "Status: Verified" pattern file (`.claude/patterns/dataverse/plugin-structure.md`) still tells developers and agents to *extend BaseProxyPlugin* — the exact class the code retired — and the module docs stamp the ADR-002 violator "Production-Ready | ADR-002 Compliant" (D11-01, D1-05).
5. **The guard tests break on the intended remediation**: `ADR002_PluginTests` hard-couples to the on-disk existence of the decommission target (5× `Assert.NotEmpty`) and asserts a stale magic threshold of 6 (actual file-pattern pairs = 5 → slack of 1) — deleting the plugin reds the suite; a new violation would not (D7-01).

### Per-dimension grade table (re-adjudicated at synthesis against rubric §3, verified findings only)

| Dim | First pass | **Adjudicated** | Movement rationale |
|---|---|---|---|
| D1 Architecture & boundaries | C– | **D+** | Verified HIGH material boundary defect (D1-01: cross-boundary HTTP/AAD inside the plugin pipeline; whole surface is the ADR-002 violation; disposition R3 #9 unresolved per D1-02). Rubric §3 D-band = "a material defect on this dimension"; containment ([Obsolete], empty Solution.xml, arch-test fence) holds it at D+ — latent, not live — but does not lift a confirmed defect into the C band. |
| D2 Correctness & reliability | B– | **C+** | The D2 finder's "no latent broken path" premise is superseded by verified cross-dimension finding D6-07: leading-slash endpoint drops `/api` → 404 against the current BFF route table while the trace logs the wrong URL — a latent broken path. Plus non-deterministic `Entities[0]` config pick (D2-03) and an unreachable null-guard (D2-01). Calibrated to the BFF precedent (D2 = C+ with live-reachable broken paths; this one sits in contained obsolete code). |
| D3 Security (**gating**) | D | **D** | Confirmed: plain-text OAuth secret in a UserOwned table, no KV, no field security, readable by invoking users (D3-01 HIGH — verification *strengthened* this); caller identity never forwarded → per-user UAC cannot bind (D3-02 HIGH); error-body echo (D3-03); capability URL persisted in audit log (D3-04). Material auth/secret gaps = D band; not F because liveness is unproven and the current BFF build fails closed on the app-only token. **Caps the surface.** |
| D4 Performance & scalability | C+ | **C+** | Five confirmed MEDIUMs (no token cache, per-execution HttpClient, Thread.Sleep retries, avoidable RetrieveMultiple, 300s timeout > 120s sandbox limit) + two LOWs — dense, self-acknowledged anti-patterns, bounded by the sandbox ceiling and a low-frequency path. |
| D5 DRY / dead code | C | **C** | The whole ~650-LOC assembly is superseded-but-retained across two program cycles (D5-01 MEDIUM); dead DTO members (D5-02); permanently-throwing ManagedIdentity stub (D5-03). Managed, tracked debt — not provably dead at scale (registration is data-driven). |
| D6 Consistency & conventions | C+ | **C+** | Docs/comments assert the opposite of code ("Production-Ready \| ADR-002 Compliant" vs `[Obsolete]` — D1-05 dup); stale "eliminates ILMerge" comment vs a live ILRepack script (D6-02); POST/async docs vs GET/sync code (D6-04); misleading hand-concatenated URL trace (D6-07); naming-convention nits (D6-05/06). |
| D7 Testability & test quality | B– | **B–** | Only source-text arch guards, no behavioral tests (observation per ADR-038, not a defect); real debt: guards hard-coupled to the decommission target's on-disk existence + stale magic threshold 6 with slack 1 (D7-01), tautological scaffolding test (D7-02). |
| D8 Dependency & supply-chain hygiene | D | **D** | HIGH CVE open in the live resolved graph (System.Text.Json 6.0.8, CVE-2024-43485) and unmonitored by any repo gate (D8-01) — rubric §3 names "a CVE left open" as D band; no lockfile/transitive pinning (D8-02); stale ILRepack manifest (D8-03). Not F: not demonstrably exercised (Newtonsoft-only source) and not provably deployed. |
| D9 Observability | B– | **B–** | Correlation ID + structured `sprk_proxyauditlog` telemetry genuinely exist; debt: ephemeral preview capability URL persisted unredacted in the audit payload (D9-01), base-class traces omit the correlation ID (D9-02), validation failures emit no audit row (D9-03), raw downstream error bodies traced (D9-04). |
| D10 ALM / build hygiene | C | **D+** | Two verified HIGHs are material defects on this dimension per §3: zero CI — absent from Spaarke.sln and every workflow, so no build/analyzer/CVE gate ever runs (D10-01); packed solution is an empty shell — deployment is manual, non-declarative, non-reproducible (D10-03). Local `Directory.Build.props` also shadows the root analyzers-as-errors/Nullable gate (D10-02). Consistent with BFF D10 = D+ for lesser issues. |
| D11 Knowledge/doc accuracy | D+ | **D+** | Worst-category drift confirmed: a "Verified" pattern file steers new plugins to extend the `[Obsolete]` BaseProxyPlugin (D11-01 HIGH); fictional non-compiling doc samples (D11-04); unimplemented token-caching claim (D11-05); broken links (D11-03/07/08); `.claude`-internal contradiction on plugin HTTP (D11-09). Held at D+ (not lower) because the code itself is honestly self-marked. |

### Composed surface grade (rubric §4.2)

- Grade points: D1 1.3 + D2 2.3 + D3 1.0 + D4 2.3 + D5 2.0 + D6 2.3 + D7 2.7 + D8 1.0 + D9 2.7 + D10 1.3 + D11 1.3 = 20.2 → equal-weight mean **1.84 ≈ C–**.
- Gating cap: `min(C–, D2 = C+, D3 = D)` = **D**.
- **Surface grade = D (gating cap applied — D3 Security gates the surface below its mean, exactly as the rubric intends).** Provisional until remediation; decommission (Phase B1) + knowledge-layer fixes (Phase A1) lift D1/D3/D4/D5/D8/D10/D11 essentially for free.

---

## 1. Central recommendation — execute the decommission (R3 #9 disposition)

- **Inversion is not viable.** The R2 assessment (`projects/code-quality-and-assurance-r2/notes/baseplugin-adr002-assessment.md:112-149`, independently re-verified) establishes the plugin sandbox cannot support IHttpClientFactory, DI, async, or token caching — "full IoC inversion" is architecturally impossible. R2's owner decision deliberately stopped at `[Obsolete]`-marking; R3 must finish the job.
- **The flow already belongs to the BFF.** The BFF endpoint `GET /api/documents/{id}/preview-url` exists (`FileAccessEndpoints.cs:30-42`); clients (PCF/code pages) should call it directly with user-context auth — which also fixes the confused-deputy gap (D3-02) that the plugin *cannot* fix (it has no user token to forward).
- **Decommission moots ~70% of the findings** (everything inside the module folder: D1-01/03/04/05, D2-01/02, D3-01/03/04, D4-01..07, D5-01/02/03, D6-02..07, D8-01/02/03, D9-01..04, D10-02..06, D11-04/05/07/08). What survives deletion and must be fixed regardless: the `.claude/` knowledge layer (D11-01/03/09), the guard-test coupling (D7-01/02), the CI-visibility decision (D10-01), and the live-environment cleanup (deregistration + secret rotation + audit-payload purge).
- **Deletion is gated by the live Dataverse pre-check** (§4 — NFR-08): registration is data-driven (`PluginAssembly`/`plugintype`/`CustomAPI` rows, absent from the checked-in solution), the module README claims "Production", and grep cannot prove liveness either way.

---

## 2. Current-state inventory (all findings Fable-CONFIRMED)

Effort: S ≤ ½ day · M ≤ 2 days · L > 2 days. Risk = remediation risk (regression/coordination), not defect severity. "Moot on decommission" = resolved by Phase B1 deletion; listed remediation applies only under the Phase B2 contingency (owner keeps the flow).

### D1 — Architecture & boundaries (adjudicated **D+**)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D1-01 | HIGH | Cross-boundary remote I/O (HTTP→BFF + OAuth2→AAD) inside the plugin transaction pipeline — the definitional ADR-002 violation. Contained: `[Obsolete]`, empty Solution.xml, arch-test fence. | `BaseProxyPlugin.cs:138` (client :140, token :176-197; sync GET `GetFilePreviewUrlPlugin.cs:101`) | 356 | L | med | **Decommission the assembly** (Phase B1) after the §4 pre-check; preview-URL flow is already served by the BFF endpoint. Do not remediate in place. |
| D1-02 | MED | R3 #9 disposition unresolved — obsolete-but-compiled assembly left in src; deprecation half-finished since 2026-03-15. | `BaseProxyPlugin.cs:15` | 480 | S | low | Record **decommission** as the R3 #9 disposition in this design + the wrap-up notes; Phase B1 executes it. |
| D1-03 | MED | Per-request services stored in mutable instance fields; Dataverse pools/reuses plugin instances across threads → cross-execution contamination hazard. | `BaseProxyPlugin.cs:18-20` (assigned :35-38) | 25 | S | low | Moot on decommission. Contingency: resolve `ITracingService`/`IOrganizationService`/`IPluginExecutionContext` as method locals (ADR-002 example pattern, `docs/adr/ADR-002-no-heavy-plugins.md:104-119`). |
| D1-04 | LOW | Plugin hand-parses the exact BFF envelope keys (`data`/`metadata`/`previewUrl`/…) with no shared contract — silent break on any BFF envelope change. | `GetFilePreviewUrlPlugin.cs:125-142` | 20 | S | low | Moot on decommission (clients consume the BFF endpoint directly; no plugin owns a copy of the envelope). |
| D1-05 | LOW | README asserts "ADR-002 Compliant / ✅ Production" for the canonical ADR-002 violator; broken Technical Overview link. (= D6-01 = D11-02) | `README.md:13,23,43` (+ tech overview :5-9, 78-117) | 5 | S | low | Deleted wholesale by Phase B1. If B1 slips a sprint, apply the interim honesty patch (Phase A1 optional item): status → "OBSOLETE — decommission pending (ADR-002)". |

### D2 — Correctness & reliability (**C+**, gating)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D2-01 | LOW | Null-guard on `ExecutionContext` in `ValidateRequest` is unreachable — already dereferenced at :38 outside the try before the guard at :86 can run (raw NRE instead of the intended `InvalidPluginExecutionException`). | `BaseProxyPlugin.cs:86` | 6 | S | low | Moot on decommission. Contingency: validate `serviceProvider.GetService` results before first use. |
| D2-02 | LOW | Retry documented "Exponential backoff" is linear/arithmetic (`retryDelay * (i + 1)` → 1000/2000/3000 ms). | `BaseProxyPlugin.cs:337` (comment :336, XML doc :305) | 3 | S | low | Moot on decommission. Contingency: fix comment or implement `retryDelay * 2^i` — but the durable fix removes in-plugin retry entirely (D4-03). |
| D2-03 | LOW | `GetServiceConfig` takes `Entities[0]` with no OrderBy — non-deterministic if duplicate enabled `sprk_name` rows exist (no uniqueness constraint provable in repo). ⚠ Dataverse pre-check §4. | `BaseProxyPlugin.cs:112` (filter :104-105) | 4 | S | low | Moot on decommission. Contingency: deterministic OrderBy + fail-fast on `Count > 1` + alternate key on `sprk_name`. |
| D2-04 | INFO | Reachability context: registration is external to version control (empty `<SolutionPluginAssemblies />`); latent broken paths here can be proven neither live nor dead from source. ⚠ Dataverse pre-check §4. | `src/Other/Customizations.xml:13` | 0 | — | med | Resolved by the §4 pre-check + Phase B1 disposition. Bounds severity of all other findings (nothing on this surface grades F). |
| (D6-07 facet) | MED | **Latent broken path (drives the C+):** leading-slash relative endpoint discards the `/api` BaseAddress path → request 404s against the real BFF route (`FileAccessEndpoints.cs:30-42` requires the prefix) while the trace logs the wrong hand-concatenated URL. | `GetFilePreviewUrlPlugin.cs:98` (+ `BaseProxyPlugin.cs:140-144`) | 4 | S | med | See D6-07 row. Moot on decommission; live `sprk_baseurl` inspection first (§4). |

### D3 — Security (**D**, gating — caps the surface)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D3-01 | HIGH | OAuth client secret + API key stored as plain-text columns on UserOwned `sprk_externalserviceconfig`; no Key Vault, empty `<FieldSecurityProfiles />`; the stored value IS the live secret. Verification strengthened: config is read under the *invoking user's* OrganizationService (:38, :107), so end users of the "Production" Custom API must hold read on the raw secret. | `BaseProxyPlugin.cs:121` (:123 apikey; used :158, :182-186) | 15 | M | med | **Decommission + rotate the exposed client secret + delete/neutralize the config row** (Phase B1 live-cleanup step — the secret must be treated as exposed regardless of disposition). Contingency: KV-reference column + field security profile. |
| D3-02 | HIGH | Caller identity never forwarded to the BFF (app-only bearer + `X-Correlation-Id` only); plugin never checks the user can read the target `sprk_document` → confused-deputy/IDOR shape. Verified: the BFF preview path is OBO-only with no per-document filter, so per-user UAC *cannot* bind on this call; current in-repo BFF would fail the OBO exchange fail-closed. | `GetFilePreviewUrlPlugin.cs:96` (:67, :98; `BaseProxyPlugin.cs:151-152,169`) | 10 | M | med | **Decommission** — clients call the BFF endpoint directly with user-context auth (the gap is intrinsic to the plugin: it has no user token to forward). Contingency: user-context access check on `sprk_document` before call-out. |
| D3-03 | MED | Raw upstream/internal error bodies echoed to the Custom API caller (`InvalidPluginExecutionException` carries the unfiltered BFF body; `Execute` rethrows raw `ex.Message`) — contradicts the module's own stated rule. | `GetFilePreviewUrlPlugin.cs:108-109` (:105; `BaseProxyPlugin.cs:72`; :157) | 6 | S | low | Moot on decommission. Contingency: generic client message + correlation id; detail via `TracingService` only. |
| D3-04 | MED | Ephemeral SPE preview capability URL persisted unredacted in `sprk_responsepayload` — the denylist redactor (`secret`/`password`/`token`/`filecontent`) never matches `PreviewUrl`; bearer-capability retained beyond its ~10-min life for anyone with read on the audit table. | `BaseProxyPlugin.cs:253` (redactor :281-301; output `GetFilePreviewUrlPlugin.cs:78`) | 8 | S | low | Moot on decommission — but **purge/redact existing `sprk_proxyauditlog.sprk_responsepayload` values in the live env** (Phase B1 cleanup; §4). Contingency: allowlist of loggable fields. (= D9-01) |

### D4 — Performance & scalability (**C+**)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D4-01 | MED | OAuth token fetched fresh on every execution — `expires_in` deserialized but never used; 2 outbound HTTP round-trips per call where one ~1h token would do. | `SimpleAuthHelper.cs:19` (:81-82; caller `BaseProxyPlugin.cs:182`) | 25 | M | low | Moot on decommission. Contingency: static cache keyed (tenant, client, scope) with skewed expiry. |
| D4-02 | MED | `new HttpClient` per execution wrapped in `using` — classic socket-exhaustion pattern; named as a defect by the class's own `[Obsolete]`. ⚠ pre-check §4. | `BaseProxyPlugin.cs:140` (`GetFilePreviewUrlPlugin.cs:93`) | 35 | S | low | Resolved by decommission (the ADR-002 fix). Contingency: static HttpClient + per-request headers. |
| D4-03 | MED | `Thread.Sleep` in the retry loop blocks a scarce sandbox worker thread (~3s pure sleep on a failing call, atop HTTP waits). ⚠ pre-check §4. | `BaseProxyPlugin.cs:338` (:307-343) | 37 | S | low | Resolved by decommission (retry belongs in the async BFF/Service Bus worker). |
| D4-04 | MED | Avoidable `RetrieveMultiple` to re-find the audit row whose Guid `Create` already returned — 3 Dataverse ops per execution where 2 suffice; compounded by `ColumnSet(true)`. | `BaseProxyPlugin.cs:243` (Create :222 discarded; query :239-243; Update :266) | 40 | S | low | Moot on decommission. Contingency: capture the Create Guid, update by Id. |
| D4-05 | MED | HTTP timeout default 300s exceeds the ~120s sandbox abort — unreachable fallback that lets a slow BFF hold the sandbox thread/transaction to the platform kill. | `BaseProxyPlugin.cs:143` | 5 | S | low | Moot on decommission. Contingency: cap ≤ 30s (the token leg already uses 30s — `SimpleAuthHelper.cs:47`). |
| D4-06 | LOW | `ColumnSet(true)` over-fetch of the whole config row (incl. secret columns) per execution; pattern recurs at :240. | `BaseProxyPlugin.cs:102` | 38 | S | low | Moot on decommission. Contingency: explicit ColumnSet. |
| D4-07 | LOW | Synchronous outbound HTTP (`GetAwaiter().GetResult()`) holds the sandbox thread/transaction for the full network duration — the core ADR-002 scalability problem; sync idiom itself unavoidable in-sandbox. ⚠ pre-check §4. | `GetFilePreviewUrlPlugin.cs:101` (:105, :112) | 30 | — | med | Resolved by decommission (move the call out of the plugin per ADR-002). |

### D5 — DRY / dead code (**C**)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D5-01 | MED | Entire assembly superseded/obsolete but retained: sole concrete IPlugin is `[Obsolete]`; no non-obsolete consumer in src/ or tests/ (arch test is text-scan only); repo solution registers nothing; R2 says "not currently used in production". Not provably dead — registration is data-driven. ⚠ pre-check §4. | `BaseProxyPlugin.cs:15` (`GetFilePreviewUrlPlugin.cs:43`) | 650 | L | med | **Phase B1 decommission**: delete the plugin project + module folder + scripts + solution shell; update the arch-test fence (D7-01) in the same PR. |
| D5-02 | LOW | `TokenResponse.ExpiresIn`/`TokenType` deserialized, never read — dead members on a private DTO (and the reason no cache exists). | `SimpleAuthHelper.cs:82,85` | 4 | S | low | Moot on decommission. |
| D5-03 | LOW | `GetManagedIdentityToken` / AuthType=2 is a stub that only ever throws — a documented-but-never-implemented auth path. ⚠ pre-check §4 (removing `case 2` flips live authtype=2 rows from fail-loud to silent no-auth fall-through). | `BaseProxyPlugin.cs:199-204` (case :154-155; doc :381) | 8 | S | med | Moot on decommission. Contingency: remove case 2 **only after** confirming no live `sprk_authtype = 2` rows (§4). |

### D6 — Consistency & conventions (**C+**)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D6-01 | (dup) | Docs claim ADR-002 compliance the code self-refutes — merged into **D1-05**. | `README.md:13,43` | — | — | — | See D1-05. |
| D6-02 | MED | `SimpleAuthHelper` comment says the design "eliminates ILMerge", yet `build-and-merge.ps1` still ILRepacks a phantom Azure.Identity list the csproj no longer references — two contradictory build stories, script unwired from any CI/doc. | `SimpleAuthHelper.cs:11` (script :1-2, :61-78) | 122 | S | low | Delete `build-and-merge.ps1` (Phase B1; standalone-safe as an A-tranche quick kill if B1 slips). (= D8-03 = D10-04) |
| D6-03 | MED | README links `docs/TECHNICAL-OVERVIEW.md`; actual file is `docs/CUSTOM-API-PROXY-TECHNICAL-OVERVIEW.md` (wrong name reinforced inside the tech overview at :569). (= D11-06) | `README.md:17` | 1 | S | low | Deleted wholesale by Phase B1 (in-module doc). |
| D6-04 | MED | Tech overview documents HTTP **POST + async/await**; code does **GET + sync-over-async** — verb and async model both drift; doc samples cannot compile. | `CUSTOM-API-PROXY-TECHNICAL-OVERVIEW.md:41` (:96, :158 vs `GetFilePreviewUrlPlugin.cs:101`) | 5 | S | low | Deleted wholesale by Phase B1. |
| D6-05 | LOW | `SERVICE_NAME` SCREAMING_SNAKE_CASE violates CODING-STANDARDS #15 (PascalCase for C# constants; snake-case rule is TS-scoped). Rename does not touch the Dataverse lookup string value. | `GetFilePreviewUrlPlugin.cs:46` | 1 | S | low | Moot on decommission. |
| D6-06 | LOW | Assembly/namespace `Spaarke.Dataverse.CustomApiProxy` vs solution/folder `Spaarke.CustomApiProxy` — identifier drift. ⚠ pre-check §4: the assembly-name binding lives ONLY in live `pluginassembly` rows; renaming orphans registrations. | `Spaarke.Dataverse.CustomApiProxy.csproj:5` | 2 | M | high | **Do NOT rename in place.** Fold into Phase B1 (deregistration removes the binding). |
| D6-07 | MED | BaseAddress ends `/api` but the relative endpoint starts with `/` → `/api` silently dropped at request time (404 vs the real BFF route) while the trace logs the wrong URL. Cross-dim: **D2 latent broken path** (drives the D2 adjudication), D9 misleading log. ⚠ pre-check §4 (live `sprk_baseurl`). | `GetFilePreviewUrlPlugin.cs:98` (:99; `BaseProxyPlugin.cs:140-144`; route `FileAccessEndpoints.cs:30-42`) | 4 | S | med | Moot on decommission. Contingency: drop the leading slash + log the resolved absolute `RequestUri`. |

### D7 — Testability & test quality (**B–**)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D7-01 | MED | Arch guards hard-couple to the decommission target: 5× `Assert.NotEmpty(sourceFiles)` red the suite when the plugin is deleted; `const knownViolationCount = 6` is a stale non-contract magic value (actual file-pattern pairs = 5 → **slack of 1**: a new violation slips through); loop counts files-containing-pattern, not occurrences. | `tests/Spaarke.ArchTests/ADR002_PluginTests.cs:249` (guards :99,132,165,217,225; loop :236-244) | 40 | M | low | **Phase A2 (do BEFORE B1)**: gate the scans on plugin existence (empty dir = PASS, "nothing to violate"), delete the count-threshold test or make it count actual occurrences against the enumerated KnownViolations; then Phase B1 removes the fence entries entirely. |
| D7-02 | LOW | `PluginSourceFilesShouldExist` is a tautological scaffolding test (asserts files exist on disk; fully redundant with the guard inside every scanning test) — ADR-038 §7 scaffolding class. | `ADR002_PluginTests.cs:210-218` | 9 | S | low | Delete in Phase A2 (same PR as D7-01). |
| D7-03 | LOW | Zero behavioral tests on the surface (redaction, BFF-envelope parsing, retry classification, auth dispatch all untested) — observation-grade under ADR-038 (coverage ≠ gate), not a defect, given the decommission track. ⚠ pre-check §4. | `ADR002_PluginTests.cs:94` (targets `BaseProxyPlugin.cs:281-302,307-371,138-174`; `GetFilePreviewUrlPlugin.cs:121-159`) | 0 | — | low | Resolved by decommission. Contingency only: pure-logic tests for `RedactSensitiveData` + `ParseBffResponse`. |

### D8 — Dependency & supply-chain hygiene (**D**)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D8-01 | HIGH | HIGH CVE live in the resolved net462 graph: transitive `System.Text.Json 6.0.8` (GHSA-8g4q-xg66-9fp4 / CVE-2024-43485, fixed 6.0.10) via CrmSdk 9.0.2.56 — **outside every repo CVE gate** (not in Spaarke.sln → nightly vuln-scan blind; Trivy blind, no lockfile; task 032 "ZERO" is net10-scoped). Present-but-not-exercised (source is Newtonsoft-only). | `Spaarke.Dataverse.CustomApiProxy.csproj:14` (resolved `obj/project.assets.json:325`) | 1 | S | low | **Resolved by Phase B1 deletion (preferred).** Interim only if B1 slips a sprint (Phase A3): explicit `PackageReference System.Text.Json 6.0.10+` + add the csproj to the CVE-scan surface. Do NOT conflict with the net10 no-re-pin rule — this is a net462 island, out of that rule's scope. |
| D8-02 | MED | No lockfile, no transitive pinning (`ManagePackageVersionsCentrally=false`, no `RestorePackagesWithLockFile`, zero `packages.lock.json` repo-wide) — the mechanism that let 6.0.8 float in unchecked. | `csproj:8` (+ `Plugins/Directory.Build.props:3`) | 2 | S | low | Resolved by Phase B1 deletion. Interim (A3, with D8-01): enable lockfile + commit it. |
| D8-03 | MED | ILRepack manifest is stale supply-chain fiction: merges a dozen Azure.Identity/IdentityModel DLLs the graph no longer produces, silently self-filters to (nearly) the bare DLL, omits the actual runtime dep Newtonsoft.Json. (= D6-02 = D10-04) | `build-and-merge.ps1:61` (:81 filter) | 20 | S | low | Delete the script (Phase B1 / A-tranche quick kill). |

### D9 — Observability (**B–**)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D9-01 | MED | Ephemeral pre-authenticated preview URL persisted unredacted in the audit payload — the substring denylist never matches `PreviewUrl`. (= D3-04; counted once in composition, material to both dimensions.) | `BaseProxyPlugin.cs:254` (redactor :285-299; `GetFilePreviewUrlPlugin.cs:78`) | 5 | S | low | See D3-04 (incl. live audit-payload purge). |
| D9-02 | LOW | Correlation ID omitted from (nearly) all base-class traces (~17 sites) while the derived plugin prefixes every trace — audit-row joinability lost. (Verification: :224 does include the ID; per-execution trace records limit the interleaving impact.) | `BaseProxyPlugin.cs:40` (+ :60,65,98,130,159-201,228,268,273,323-334) | 20 | S | low | Moot on decommission. Contingency: store the ID once, prefix via a Trace helper. |
| D9-03 | LOW | Validation-path failures produce no audit row: `ValidateRequest` (:48) throws before `LogRequest` (:51) mints the correlation ID, and the catch guard (:67) skips `LogResponse` — a whole failure class leaves only an un-correlated trace line. | `BaseProxyPlugin.cs:48` | 6 | S | low | Moot on decommission. Contingency: mint ID + open audit row before validation. |
| D9-04 | LOW | Raw downstream BFF error body traced verbatim (redaction applies only to audit payloads, never to `Trace`) — un-sanitized upstream content in the plugin trace log. | `GetFilePreviewUrlPlugin.cs:106` (:105) | 3 | S | low | Moot on decommission. Contingency: status + truncated summary. |

### D10 — ALM / build hygiene (**D+**)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D10-01 | HIGH | Assembly built by NO CI workflow and absent from Spaarke.sln — no automated build, analyzer, warning, or CVE gate ever executes on this surface (verified against every workflow's build/restore step). | `Spaarke.Dataverse.CustomApiProxy.csproj:1` | 0 | S | low | **Phase A2**: document decommission-tracked exclusion in the r3 notes (this design is that record) so the gap is a tracked decision, not an accident; Phase B1 deletion closes it permanently. Do NOT invest in wiring CI for a decommission target. |
| D10-02 | MED | Local `Plugins/Directory.Build.props` shadows the repo-root quality gate — no TreatWarningsAsErrors, no Nullable (Deterministic is SDK-default, immaterial); MSBuild nearest-file semantics confirmed (exactly 2 props files repo-wide). | `Plugins/Directory.Build.props:1` | 5 | S | low | Moot on decommission. Contingency: `<Import>` the parent props. |
| D10-03 | HIGH | Packed solution source is an empty shell — no PluginAssembly/PluginType/CustomAPI/SdkMessage nodes anywhere; cdsproj packs a ZIP that deploys nothing; real registration is manual Plugin Registration Tool (`build-and-merge.ps1:120`). Non-reproducible deployment. | `src/Other/Customizations.xml:13` (`Solution.xml:91`) | 0 | M | med | Resolved by Phase B1 (delete the shell WITH the assembly; live deregistration per §4). Contingency: declarative registration in solution source. |
| D10-04 | MED | Stale self-filtering ILRepack build script (= D8-03/D6-02 — same artifact, ALM facet). | `build-and-merge.ps1:61` (:81) | 18 | S | low | Delete the script. |
| D10-05 | LOW | Static `<Version>1.0</Version>`, no AssemblyVersion/FileVersion anywhere — no monotonic solution-version discipline. | `src/Other/Solution.xml:11` | 0 | S | low | Moot on decommission. |
| D10-06 | LOW | Strong-name key `SpaarkePlugin.snk` committed (verification downgraded MED→LOW: strong names are assembly identity, not security; committed keys are the conventional pattern and required for stable update registration). Residual issue = undocumented accept-risk. | `SpaarkePlugin.snk:1` (csproj :9-10) | 0 | S | low | Moot on decommission. This design records the accept-as-conventional decision. |

### D11 — Knowledge/doc accuracy (**D+**)

| ID | Sev | Finding | Anchor | LOC | Effort | Risk | Remediation |
|---|---|---|---|---:|---|---|---|
| D11-01 | HIGH | "Status: Verified" pattern file steers new plugins to the retired anti-pattern: "extend BaseProxyPlugin, implement ExecuteProxy()" — load-bearing (referenced by patterns INDEX ×2, task-execute, task-create, constraints/plugins.md). Worst-category D11 drift. | `.claude/patterns/dataverse/plugin-structure.md:23` (:11-12) | 15 | S | low | **Phase A1 (main session — sub-agents cannot write `.claude/`)**: rewrite the Custom-API-Proxy exemplar section → "deprecated; use BFF API + Service Bus worker (ADR-002)"; re-date + re-review. Highest-priority fix on the surface — it actively regenerates the defect. |
| D11-02 | (dup) | "Production-Ready \| ADR-002 Compliant" doc claims — merged into **D1-05**. | `README.md:43` | — | — | — | See D1-05. |
| D11-03 | MED | Pattern file references a test file that does not exist anywhere in the repo (`tests/unit/Spaarke.Plugins.Tests/ValidationPluginTests.cs`; no ValidationPlugin class exists either). | `plugin-structure.md:13` | 1 | S | low | Phase A1 (same edit as D11-01): remove the dead reference. |
| D11-04 | MED | Tech overview documents a fictional API: members that don't exist (`_httpClient`, `GenerateCorrelationId`, `CallBffApiAsync`, `GetBffApiTokenAsync`), an `override void Execute` + `await` pattern that cannot compile against the non-virtual base, an instance SimpleAuthHelper vs the real static class. | `CUSTOM-API-PROXY-TECHNICAL-OVERVIEW.md:135` (:88-101, :191-237, :293-335) | 60 | M | low | Deleted wholesale by Phase B1 (in-module doc). Do not regenerate. |
| D11-05 | MED | Docs claim token caching ("in-memory, per plugin execution"; "avoid auth call every execution") that does not exist anywhere in the assembly. | `CUSTOM-API-PROXY-TECHNICAL-OVERVIEW.md:277` (:509) | 4 | S | low | Deleted wholesale by Phase B1. |
| D11-06 | (dup) | Broken Technical Overview link — merged into **D6-03**. | `README.md:17` | — | — | — | See D6-03. |
| D11-07 | LOW | ADR-002 link points at a non-existent filename (`docs/adr/ADR-002-thin-plugins.md`; real slug is `-no-heavy-plugins`; relative depth also wrong). Notes the mild `.claude`/`docs` ADR-002 slug divergence. | `CUSTOM-API-PROXY-TECHNICAL-OVERVIEW.md:550` | 1 | S | low | Deleted wholesale by Phase B1. The cross-tree slug divergence is a program-level nit for the doc-drift horizontal, not this surface. |
| D11-08 | LOW | Stale pre-rename BFF path (`Spe.Bff.Api`; only `Sprk.Bff.Api` exists). | `CUSTOM-API-PROXY-TECHNICAL-OVERVIEW.md:551` | 1 | S | low | Deleted wholesale by Phase B1. |
| D11-09 | LOW | `.claude`-internal contradiction: pattern file carves an HTTP exception "only Custom API Proxy → BFF" that `constraints/plugins.md:35,40` ("no exceptions") and the code's own `[Obsolete]` both reject. Both files marked Verified, same review date. | `plugin-structure.md:17` | 3 | S | low | Phase A1 (same edit as D11-01): remove the carve-out; align with constraints/plugins.md. |

---

## 3. Explicit KEEPs & refuted-claims appendix (record-only — do NOT act on)

**Refuted by verification (do NOT act on): NONE.** Zero first-pass claims were refuted this pass — all 44 reported findings (38 unique after cross-dimension dedup) survived Fable adversarial verification. Future passes should not expect a refutation list here; the section exists to match the BFF model and to prevent re-claiming.

**Verification corrections absorbed into the findings above (do not re-open):**
- **D10-06 severity MED → LOW**: committed `.snk` is the conventional Microsoft pattern (strong names are identity, not security; stable key required for update registration). Do NOT re-file "secret material in VCS" against `SpaarkePlugin.snk` — the residual item is only the undocumented accept-risk, recorded in §2.
- **D7-01 strengthened, not weakened**: the magic threshold 6 is stale (current file-pattern pairs = 5) — slack of 1 means the fence under-guards. Do not "fix" by bumping the constant to 5; restructure per Phase A2.
- **D9-02 minor citation fix**: `BaseProxyPlugin.cs:224` DOES include the correlation ID (1 of ~18 cited sites was wrong); interleaving impact bounded by per-execution trace records. Finding stands at LOW.
- **D8-01 evidence nuance**: assets show System.Text.Json 6.0.8 as a *direct* CrmSdk dependency (not only via System.ServiceModel.*). Claim unchanged.
- **D8-03/D10-04 nuance**: ILRepack output is not strictly "bare" (CrmSdk transitives would merge) — but the manifest omits the load-bearing Newtonsoft.Json, confirming staleness.
- **D10-02 nuance**: `Deterministic` is SDK-default-true — the real losses are analyzers-as-errors + Nullable only.
- **D10-03 citation fix**: README.md (43 lines) contains no registration instructions — the manual Plugin Registration Tool step is documented only at `build-and-merge.ps1:120`.

**Standing KEEPs / MUST-NOTs for remediation tasks:**
1. **Do NOT delete the plugin before the §4 live pre-check** — README claims "Production"; registration is Dataverse data, not grep-provable.
2. **Do NOT delete/decommission before Phase A2 decouples `ADR002_PluginTests`** — otherwise the ArchTests suite goes red on the intended remediation (D7-01).
3. **Do NOT remove the AuthType=2 `case 2` in isolation** — a live `sprk_authtype = 2` row would flip from fail-loud throw to silent no-auth fall-through (D5-03 verification).
4. **Do NOT rename assembly/namespace in place** (D6-06) — the name binding lives solely in live `pluginassembly`/`plugintype` rows; rename = orphaned registrations.
5. **Do NOT re-pin net10 framework-provided packages** anywhere else on the strength of D8-01 — the interim System.Text.Json pin (if needed) is scoped to this net462 island only, per the r3 net10 handoff rules.
6. **Do NOT touch `Xrm`-facing string values** when doing any contingency rename: `"SDAP_BFF_API"` (config lookup key) and `sprk_GetFilePreviewUrl` (Custom API unique name) are data-bound.
7. **`.claude/` writes are main-session-only** (root CLAUDE.md §3) — the Phase A1 pattern-file fixes cannot be delegated to sub-agents.

---

## 4. Data-driven-dispatch pre-check list (NFR-08 — run BEFORE any remediation executes)

Every finding flagged `requiresDataverseCheck`. One consolidated live-environment query pass (dev — `spaarke-dev` is the only live env per the 2026-08-14 handoff) satisfies all of them; output goes to `notes/dataverse-precheck-plugins.md` (created by the future remediation task, not by this assessment).

| Finding(s) | Exact live Dataverse check to run first |
|---|---|
| D1-01, D1-02, D5-01, D2-04, D4-02, D4-03, D4-07 (decommission gate) | (a) `pluginassembly` where `name` = `Spaarke.Dataverse.CustomApiProxy` — exists? version? (b) `plugintype` rows for `Spaarke.Dataverse.CustomApiProxy.GetFilePreviewUrlPlugin`; (c) `customapi` where `uniquename` = `sprk_GetFilePreviewUrl` (+ its `plugintypeid` binding); (d) any `sdkmessageprocessingstep` bound to those plugin types; (e) `sprk_proxyauditlog` rows with `createdon` in the last 90 days (usage evidence). If (a)–(d) are all absent and (e) is empty → decommission is a pure repo deletion. If any exist → Phase B1 includes live deregistration in dependency order (steps → custom api → plugin types → assembly). |
| D3-01 (secret exposure — run regardless of disposition) | `sprk_externalserviceconfig` where `sprk_name` = `SDAP_BFF_API`: does a row exist with populated `sprk_clientsecret`/`sprk_apikey`? If yes → treat the client secret as exposed: **rotate it in Azure AD** and delete/neutralize the row at decommission; enumerate security roles with read on the entity for the exposure record. |
| D3-04 / D9-01 (audit payload purge) | Count `sprk_proxyauditlog` rows where `sprk_responsepayload` contains `PreviewUrl`; purge or redact those payloads (URLs are expired, but the rows evidence the leak pattern and may hold other response data). |
| D2-03 (duplicate config rows) | Count enabled `sprk_externalserviceconfig` rows per `sprk_name` (duplicates?); check `EntityKeys` metadata for an alternate key on `sprk_name`. Only relevant under the Phase B2 contingency. |
| D5-03 (AuthType=2 rows) | `sprk_externalserviceconfig` where `sprk_authtype` = 2 — must be zero before any contingency removal of the ManagedIdentity stub (fail-loud → silent fall-through risk). |
| D6-06 (assembly-name binding) | Same `pluginassembly`/`plugintype` query as the decommission gate — confirms the name binding that forbids in-place rename. |
| D6-07 (live BaseUrl / timeout) | Read the live `SDAP_BFF_API` row's `sprk_baseurl` (does it end in `/api`?) and `sprk_timeout` — establishes whether the `/api`-drop 404 and the 300s fallback are live-manifest or documentation-only. Contingency-path input only. |
| D7-03 (regression-test need) | The customapi/step liveness result above decides: if the API is registered anywhere, add the `RedactSensitiveData` + `ParseBffResponse` pure-logic tests BEFORE decommission lands; if unregistered, skip. |

---

## 5. Proposed workstreams → phases (A/B tranche split per r3 NFR-04)

Contention profile: no other active worktree touches `src/dataverse/plugins/**` (grep-verified during assessment) — repo-side edits are low-contention. The contested/wide items are (i) the `.claude/patterns` file (load-bearing for task-create/task-execute; main-session write), (ii) `tests/Spaarke.ArchTests` (shared suite), and (iii) the **live Dataverse environment change** (deregistration + secret rotation) — those are the B-tranche/quiet-window items.

### Phase 0 — Live Dataverse pre-check (gate; read-only against the env)
- Run the full §4 query pass against `spaarke-dev`; write `notes/dataverse-precheck-plugins.md`.
- Output decides B1's shape (pure repo deletion vs deletion + live deregistration) and whether D7-03's regression tests are needed.
- Effort S. MINIMAL rigor (read-only).

### Tranche A — low-contention hygiene (can start immediately, parallel to Phase 0)

**Phase A1 — Knowledge-layer truth fixes (main session; `.claude/` write boundary).**
- Rewrite `plugin-structure.md`: kill the "extend BaseProxyPlugin" exemplar (D11-01), the HTTP carve-out (D11-09), the dead test reference (D11-03); point to ADR-002's BFF + Service Bus worker pattern; re-date.
- Optional interim honesty patch on the module README if B1 is not imminent (D1-05): status → OBSOLETE / decommission pending.
- Effort S. STANDARD rigor. Highest-priority item on the surface — the pattern file actively regenerates the anti-pattern.

**Phase A2 — Guard-test decoupling (BEFORE B1; touches tests/ → TEST-MODIFYING rigor, code-review + adr-check unconditional).**
- Restructure `ADR002_PluginTests`: existence-gate the scans (missing plugin dir = pass), delete `PluginSourceFilesShouldExist` (D7-02), replace the stale magic-6 count test with an occurrence count over the enumerated KnownViolations or delete it (D7-01).
- Record the D10-01 CI-exclusion decision (decommission-tracked; no CI investment) in the PR description.
- Effort M. Risk low.

**Phase A3 — Interim CVE containment (CONDITIONAL — only if B1 slips beyond one sprint).**
- Pin `System.Text.Json` ≥ 6.0.10 in the csproj (D8-01); enable `RestorePackagesWithLockFile` + commit the lockfile (D8-02); note the surface in the task-032 CVE ledger so the net462 island is no longer invisible.
- Effort S. Skip entirely if B1 lands promptly — deletion is the better fix.

### Tranche B — wide/contested edits (quiet window; owner sign-off)

**Phase B1 — Decommission execution (the R3 #9 disposition).** FULL rigor; prescriptive steps (irreversible env change).
1. Confirm Phase 0 results + Phase A2 merged.
2. Live deregistration (if Phase 0 found registrations): delete `sdkmessageprocessingstep` → `customapi` (`sprk_GetFilePreviewUrl`) → `plugintype` rows → `pluginassembly`, in that order.
3. Live data cleanup: rotate the exposed AAD client secret (D3-01 — do this even if no registration exists, as long as the config row holds a secret); delete/neutralize the `SDAP_BFF_API` config row; purge `sprk_proxyauditlog` response payloads (D3-04/D9-01). Decide fate of the now-orphaned `sprk_externalserviceconfig`/`sprk_proxyauditlog` tables (recommend: hand to the Dataverse-model surface's remediation for entity retirement).
4. Repo deletion: `git rm -r src/dataverse/plugins/Spaarke.CustomApiProxy/` (assembly, snk, scripts, cdsproj, solution shell, README, tech overview — moots D1-01/03/04/05, D2-01/02, D3-01/03/04, D4-01..07, D5-01/02/03, D6-02..07, D8-01/02/03, D9-01..04, D10-02..06, D11-04/05/07/08 in one commit).
5. Remove the `ADR002_PluginTests` KnownViolations fence entries (now scanning an empty set that passes by A2's design); full test suite green.
6. Sweep dangling references: `.claude/patterns/dataverse/INDEX.md`, `.claude/patterns/INDEX.md`, `constraints/plugins.md:109`, `ADR010_DITests.cs:68` path filter (harmless but tidy), R2 assessment cross-links (leave historical notes intact).
7. Record the disposition in the r3 wrap-up notes + `defer-issues.md` closure of R3 #9.
- Effort L overall (mostly checklist; code delta is deletions). Risk med (live env step) — that's why it is the quiet-window tranche.

**Phase B2 — Contingency ONLY (owner keeps the flow live — NOT recommended).**
If Phase 0 finds active production usage AND the owner rejects decommission: execute the per-finding contingency column of §2 (identity forwarding or user-context access check D3-02; KV/field security D3-01; sanitized errors D3-03; redaction allowlist D3-04/D9-01; leading-slash fix D6-07; timeout cap D4-05; Create-Guid capture D4-04; token cache D4-01; behavior tests D7-03) — and file the retained ADR-002 violation through the §6.5 conflict protocol (path A exception or path B amendment). This is materially more work than decommission for a worse architecture; the design recommends against it.

### Rigor & model notes for `/task-create`
- Phase A2 + B1 modify `tests/**` → TEST-MODIFYING override (code-review + adr-check unconditional).
- Phase B1 steps are `mode="prescriptive"` (irreversible live-environment ordering).
- Phase A1 is main-session-only (`.claude/` write boundary — root CLAUDE.md §3).
- No BFF code is touched → no publish-size check applies; `/conflict-check` still runs before each PR per program rules.

---

## 6. Hot-path declaration (informational — this workstream touches none of the hot paths)

```xml
<hot-path-declaration>
  <bff>N</bff>                 <!-- read-only references to FileAccessEndpoints for evidence; no BFF edits -->
  <spaarkeai>N</spaarkeai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives> <!-- Phase A1 edits a patterns file, not a skill; main-session-only regardless -->
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

---

## 7. SCORECARD row inputs (for the invoking task to append to `notes/SCORECARD.md` — do not edit that file from this synthesis)

**Row**: Plugins (`Spaarke.CustomApiProxy`) | D1 **D+** | D2 **C+** | D3 **D** | D4 **C+** | D5 **C** | D6 **C+** | D7 **B–** | D8 **D** | D9 **B–** | D10 **D+** | D11 **D+** | **Overall D** (mean ≈ 1.84 → C–; gating cap `min(C–, D2 C+, D3 D)` = **D** — D3 caps) | Assessed 2026-08-14 (Fable-verified) | Source: this design.

Evidence bullets (one per dimension):

- **D1 D+** — Entire assembly is the canonical ADR-002 violation: HTTP→BFF + OAuth2→AAD inside the plugin pipeline (`BaseProxyPlugin.cs:138-197`, sync GET `GetFilePreviewUrlPlugin.cs:101`); contained (`[Obsolete]`, empty Solution.xml, arch-test fence) but the R3 #9 disposition is unresolved and per-request state in pooled instance fields adds a cross-thread hazard (D1-01/02/03).
- **D2 C+** — Verified latent broken path: leading-slash endpoint drops the `/api` BaseAddress segment → 404 against the real BFF route while the trace logs the wrong URL (D6-07, `GetFilePreviewUrlPlugin.cs:98`); plus non-deterministic `Entities[0]` config pick (D2-03) and an unreachable null-guard (D2-01).
- **D3 D** — OAuth client secret plain-text in a UserOwned table, no KV, no field security — invoking users need read on the raw secret (D3-01, `BaseProxyPlugin.cs:121`); caller identity never forwarded so per-user UAC cannot bind (D3-02); upstream error bodies echoed to callers (D3-03); capability URL persisted in the audit log (D3-04). **Gates the surface.**
- **D4 C+** — Per-execution token fetch with no cache (D4-01), per-execution `HttpClient` (D4-02), `Thread.Sleep` retries on a sandbox thread (D4-03), avoidable `RetrieveMultiple` (D4-04), 300s timeout vs the 120s sandbox abort (D4-05).
- **D5 C** — Whole ~650-LOC assembly superseded-but-retained across two program cycles (D5-01); dead DTO members (D5-02); always-throwing ManagedIdentity stub (D5-03).
- **D6 C+** — Module docs assert the opposite of code ("Production-Ready | ADR-002 Compliant" vs `[Obsolete]`, D1-05); "eliminates ILMerge" comment vs a live stale ILRepack script (D6-02); POST/async docs vs GET/sync code (D6-04); misleading hand-concatenated URL trace (D6-07).
- **D7 B–** — Only source-text arch guards, no behavioral tests; guards hard-couple to the decommission target's on-disk existence (5× `Assert.NotEmpty`) and assert a stale magic threshold 6 (actual pairs 5 — slack of 1) (D7-01); tautological scaffolding test (D7-02).
- **D8 D** — HIGH CVE open in the live resolved graph: transitive System.Text.Json 6.0.8 (CVE-2024-43485) via CrmSdk, invisible to the sln-scoped nightly scan, Trivy, and the net10-scoped task-032 ZERO-CVE cert (D8-01); no lockfile/transitive pinning (D8-02).
- **D9 B–** — Correlation ID + structured `sprk_proxyauditlog` telemetry exist, but the ephemeral preview capability URL persists unredacted in the audit payload (D9-01), ~17 base-class traces omit the correlation ID (D9-02), and validation failures emit no audit row (D9-03).
- **D10 D+** — Zero CI: absent from Spaarke.sln and every workflow — no build/analyzer/CVE gate ever runs (D10-01 HIGH); packed solution is an empty shell so deployment is manual and non-reproducible (D10-03 HIGH); local props shadow the root analyzers-as-errors/Nullable gate (D10-02).
- **D11 D+** — "Verified" pattern file steers new plugins to extend the `[Obsolete]` BaseProxyPlugin (D11-01 HIGH, `plugin-structure.md:23`); fictional non-compiling doc samples (D11-04); unimplemented token-caching claim (D11-05); broken links + `.claude`-internal contradiction on plugin HTTP (D11-03/07/08/09).

---

*Assessment complete. Remediation tasks are created only after owner review of this design (assessment-first). The fastest grade recovery on this surface is Phase B1 decommission: it closes D1, D3, D4, D5, D8, and most of D6/D9/D10/D11 in a single deletion + live-cleanup pass, leaving only the knowledge-layer (A1) and guard-test (A2) fixes — both of which should land first.*
