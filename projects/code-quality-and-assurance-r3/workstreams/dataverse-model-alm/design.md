# Dataverse Data Model + Solution ALM — Cleanup & Remediation Design

> **Slug**: `dataverse-model-alm` (a **surface workstream** of `code-quality-and-assurance-r3`, not a standalone project)
> **Surface**: Dataverse data model + solution ALM — `src/dataverse/**` (plugins, solutions, forms, webresources), `src/solutions/**` (Code Page solution folders + manifests + webresources), `docs/data-model/**` (this surface's designated reference docs)
> **Status**: DRAFT — for owner review before `/design-to-spec`
> **Date**: 2026-08-14
> **Method**: quality-assessment workflow: 11-dimension fan-out + Fable adversarial verification. Read-only assessment — **no code was modified**; this design + the SCORECARD row inputs are the sole outputs.
> **Input discipline (r3 NFR-05)**: every finding below survived the mandatory Fable adversarial-verification stage. Refuted first-pass claims appear ONLY in the record-only appendix (§8) and drive no grades or remediation.
> **Program**: executes **in the r3 worktree on `work/code-quality-and-assurance-r3`** per the single-worktree decision (r3 design §4/§4A). Remediation is task-created only after owner review of this design (assessment-first decision, 2026-08-06).

---

## 0. Summary & verdict

The Dataverse data-model core is genuinely healthy — consistent `sprk_` publisher prefix, coherent entity naming, and the live client surfaces route auth through the canonical `@spaarke/auth` cascade (ADR-028). The debt is concentrated in four clusters:

1. **One obsolete, self-flagged ADR-002-violating proxy plugin** (`Spaarke.CustomApiProxy`) that drags nine findings across six dimensions (architecture, thread-safety, secrets-in-Dataverse, token chattiness, error-body leaks, zero tests). It is `[Obsolete]`-annotated, ArchTest-allowlisted as "tracked for deprecation", and likely unregistered — but its live status is data-driven and must be checked in Dataverse before removal (§9).
2. **Half-finished solution segmentation** — two of three `src/dataverse/solutions` folders are `.gitkeep` facades, the populated one is named `spaarke_containers` but IS the `spaarke_document_management` solution, and SpaarkeCore is a non-standard hybrid with duplicated manifests.
3. **A dormant client test suite** — 152 Jest test files under `src/solutions` are never executed by any CI workflow or git hook.
4. **Copy-paste boilerplate + observability gaps** across the ~30 Code Page solutions (144-line verbatim `xrmProvider` clone, 7× ThemeProvider / 8× authInit wrappers, telemetry on ~5 of 35 solutions, PII-bearing console dumps, a hardcoded dev App Insights fallback).

Nothing found is a **confirmed shipped-and-live broken path or live security exposure** — the gating dimensions (D2, D3) both hold at B-. The surface is "solid with real, schedulable debt": **B-**.

### Per-area grade table (re-adjudicated, verified findings only)

| Dim | Area | Grade | One-line basis |
|---|---|---|---|
| D1 | Architecture & boundaries | **B-** | ADR-002-violating proxy plugin retained ([Obsolete], allowlisted, likely unregistered); segmentation half-finished; folder/UniqueName drift |
| D2 | Correctness & reliability | **B-** | KPI rollup swallows failures + inconsistent credentials (≥1 path wrong); latent plugin race; competing "sole" rollup scripts — no confirmed live broken data path |
| D3 | Security | **B-** | Secrets as plaintext Dataverse columns (obsolete plugin); cookie-only cross-origin writes in DocumentOperations.js; error-body leak; innerHTML hygiene — all deployment-caveated / low-exploitability |
| D4 | Performance & scalability | **B-** | No absolute bundle budget anywhere; 29/30 solutions untracked; plugin token/query chattiness (obsolete path) |
| D5 | DRY / dead code | **B** | One real 144-line clone + wrapper boilerplate; heavy lifting IS centralized; the 7-way runtimeConfig claim was REFUTED (moved up from first-pass B-) |
| D6 | Consistency & conventions | **B-** | 857-line legacy JS webresource vs ADR-006; duplicated manifests; casing drift (files + schema names) |
| D7 | Testability & test quality | **C+** | Entire 152-file solutions Jest suite has zero automated execution; skipped placeholder + disabled behavioral tests; untested plugin |
| D8 | Dependency & supply-chain | **B+** | Sole finding was a refuted placeholder (finder failure); 30 fresh tracked lockfiles + clean spot-check; no affirmative CVE audit → top-of-B, flagged for the dependencies horizontal sweep |
| D9 | Observability | **C+** | Telemetry on ~5/35 solutions (corrected); hardcoded dev conn-string fallback; PII payloads/queries in production console logs |
| D10 | ALM / build hygiene | **C+** | Segmentation is a folder facade; plugin csproj shadowed from all repo hygiene props; private .snk in git; superseded ILRepack script; hybrid SpaarkeCore layout |
| D11 | Knowledge/doc accuracy | **C+** | Both designated "start here" data-model docs embed the superseded pre-R7 AI schema; INDEX simultaneously declares it purged and routes readers to it |

**Composed surface grade (rubric §4.2)**: equal-weight mean = (2.7+2.7+2.7+2.7+3.0+2.7+2.3+3.3+2.3+2.3+2.3)/11 = **2.636 → B-**. Gating cap min(B-, D2 B-, D3 B-) = **B-** (cap did not reduce the composed grade). **Surface grade: B-.**

### Grade moves vs first pass (re-adjudication record)

| Dim | First pass | Final | Why |
|---|---|---|---|
| D5 | B- | **B** | D5-05 (runtimeConfig 7-way duplication, ~294 LOC) refuted — shared factory exists, 6/7 solutions already consume it; remediation already implemented. Worst surviving finding is one MEDIUM. |
| D8 | D | **B+** | First-pass grade + rationale were placeholders ("Test rationale."); the only D8 finding was refuted as a placeholder with no verifiable defect. Zero verified findings remain. Graded on cross-dimension evidence (30 tracked lockfiles = fresh per-project pins; pdfjs-dist version-consistency spot-check clean; residual supply-chain notes live under D10-03/D10-05). Not A-band: no affirmative CVE audit of the 30 npm graphs was performed — **this dimension needs a real pass in the dependencies horizontal sweep**. |
| all others | — | unchanged | Every other dimension's finding set survived verification intact (several strengthened); no basis to move. |

---

## 1. Problem statement

This surface accumulated three eras of practice side by side: (a) a 2025-era Custom API proxy plugin that predates the ADR-002 "no heavy plugins" boundary and the BFF+Service Bus pattern that replaced it; (b) an aspirational solution-segmentation layout (`spaarke_core` / `spaarke_documents` / `spaarke_containers`) that was scaffolded but never completed, leaving the on-disk tree misleading about which solution owns what; and (c) ~30 modern Vite Code Page solutions that grew fast with copy-paste bootstrap boilerplate, no shared bundle budgets, no CI test execution, and patchy telemetry. Meanwhile the two cross-entity reference docs that `docs/data-model/INDEX.md` designates as "start here" still document the pre-R7 AI schema that the same INDEX declares purged.

None of this is rot on a live data path. It is **ambiguity debt**: a maintainer (or agent) reading this tree today cannot tell which solution folders are authoritative, whether the proxy plugin is live, which KPI rollup script is the real one, or which data-model doc to trust. That ambiguity is exactly what breeds the next regression.

---

## 2. Goals

- **G1** — Resolve the obsolete `Spaarke.CustomApiProxy` plugin per ADR-002: Dataverse pre-check → delete the assembly (+ its solution wrapper, build script, .snk) and update the ArchTest allowlist — or, if it must remain live, remediate its secrets/thread-safety/leak findings and add tests.
- **G2** — Make the KPI grade-recalc reliable: one credential pattern, surfaced failures, exactly one rollup web resource per form.
- **G3** — Finish or abandon the solution segmentation honestly: populate or delete the stub folders; rename `spaarke_containers` to match its UniqueName; relocate the orphaned cross-domain relationship file; make SpaarkeCore's layout self-describing.
- **G4** — Make the solutions Jest suite load-bearing: wire changed-solution-scoped Jest execution into CI (coordinate with `ci-cd-unit-test-remediation-r1`, which owns `.github/workflows`).
- **G5** — Close the observability gaps: fail-closed telemetry (no hardcoded dev fallback), shared `AppInsightsService` rollout, strip PII console dumps and debug residue.
- **G6** — Consolidate the verified client-code duplication (xrmProvider, xrm.ts types, ThemeProvider, authInit) into the shared libs that already exist for the purpose.
- **G7** — Build hygiene: bundle budgets, plugin csproj analyzer/determinism parity, .snk out of git, superseded scripts deleted, manifest duplicates removed.
- **G8** — Refresh the two stale `docs/data-model` cross-entity docs to the R7 model (or delegate their AI sections to the maintained per-entity docs).

### Non-goals

- **NG1** — Re-architecting the preview-URL capability beyond what ADR-002 already prescribes (BFF + Service Bus pattern exists; this project deletes the obsolete path, it does not design a new one).
- **NG2** — Migrating `DocumentOperations.js` to a Code Page/PCF in this workstream if the ribbon audit shows it live — that is an ADR-006 migration project of its own; here we either fix its auth pattern or document the KEEP exception (owner decision, §7).
- **NG3** — Full npm-workspaces monorepo conversion (D10-08). Evaluated as a candidate; adopting it is a build-infrastructure project. This workstream only files the evaluation.
- **NG4** — Any change to live Dataverse schema (entities, columns, relationships) beyond the relationship-file relocation — schema truth lives in Dataverse and is out of scope for a repo-side quality pass (NFR-08 caveats in §9).
- **NG5** — A real D8 dependency/CVE audit of the 30 npm graphs — owned by the r3 dependencies horizontal sweep (task 032 family), not this surface workstream.

---

## 3. Current-state inventory (verified findings)

Every finding below is verdict=CONFIRMED from the Fable adversarial-verification stage. Severity/lines reflect verification corrections. Effort: S (<½ day) / M (½–2 days) / L (>2 days). Risk = execution risk of the remediation. ⚠DV = requires the live Dataverse pre-check in §9 before remediation.

### 3.1 D1 — Architecture & boundaries

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D1-01 ⚠DV | HIGH | `src/dataverse/plugins/Spaarke.CustomApiProxy/Plugins/Spaarke.Dataverse.CustomApiProxy/BaseProxyPlugin.cs:138` | 650 | M | med | Sandbox plugin makes synchronous outbound HTTP to BFF (`new HttpClient` per execution :140-144; blocking `GetAsync(...).GetAwaiter().GetResult()` `GetFilePreviewUrlPlugin.cs:101`; `Thread.Sleep` backoff :338; per-call blocking token fetch `SimpleAuthHelper.cs:44-73`) — the exact coupling ADR-002 forbids; self-flagged `[Obsolete]` at :15. **Remediation**: after §9 pre-check, delete the assembly + cdsproj + wrapper and route preview-URL via the BFF per the `[Obsolete]` note; ALSO update `tests/Spaarke.ArchTests/ADR002_PluginTests.cs` KnownViolations allowlist (lines 36-49, count :249) — verification found it allowlists exactly these 6 violations. |
| D1-02 ⚠DV(via D5-07) | MEDIUM | `src/dataverse/solutions/spaarke_documents/Entities/sprk_document/.gitkeep:1` | 0 | S–M | low | Half-finished segmentation: `spaarke_core` + `spaarke_documents` are .gitkeep-only (13 tracked files, all placeholders; no Solution.xml) while `sprk_document` ships as a segmented-shell RootComponent (behavior=1) in `spaarke_containers/Other/Solution.xml:82` with a MissingDependency on it (:84-89). Populate the segmented exports OR delete the stub trees. (Also reported by D6/D10.) |
| D1-03 | MEDIUM | `src/dataverse/solutions/spaarke_containers/Other/Solution.xml:4` | 0 | S | low | Folder `spaarke_containers` holds UniqueName `spaarke_document_management` (LocalizedName :6, v1.0.0.2 :9). Verification confirmed NO script/CI keys on the folder path → rename folder to `spaarke_document_management` is source-safe. (Also D6/D10.) |
| D1-04 ⚠DV | LOW | `src/dataverse/solutions/spaarke_containers/Other/Relationships/sprk_kpiassessment_matter.xml:1` | 0 | S | med | Cross-domain relationship (`sprk_kpiassessment`↔`sprk_matter`, :8-9) parked in the document-management solution folder. Verification: it appears in NEITHER Customizations.xml NOR the Relationships.xml index — an **orphaned deploy artifact** (provenance: `projects/x-matter-performance-KPI-r1/notes/006-deployment-guide.md:46`), not an active packing leak (severity lowered HIGH→LOW at verification). Move it to the KPI/Matter domain home after the §9 membership check. |
| D1-05 | LOW | `src/solutions/SpaarkeCore/solution.xml:84` | 0 | S | low | Manifest RootComponents = 7 env vars (type 380) only, zero type-1 entities, while `entities/` holds 8 hand-curated entity fragment folders. Verification: confirmed hand-maintained maker-portal fragments, NOT packager output. Reconcile the manifest or add a README documenting the fragments-not-packable intent. |

### 3.2 D2 — Correctness & reliability (gating)

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D2-01 | MEDIUM | `src/solutions/webresources/sprk_matter_kpi_refresh.js:273` | 30 | M | med | Three rollup web resources POST the authenticated `recalculate-grades` BFF endpoint (`ScorecardCalculatorEndpoints.cs:28/:46` `.RequireAuthorization()`) with NO credentials option (`:273-278`, `sprk_kpi_subgrid_refresh.js:298-303`, `sprk_subgrid_parent_rollup.js:278-283`) while sibling `sprk_kpiassessment_quickcreate.js:273` sends `credentials:'include'` + 3-attempt retry — cross-origin, so ≥1 pattern is wrong regardless of deployed auth mode; all failures collapse to `console.warn` (stale grades, zero user surface). **Remediation**: one shared BFF-call helper with the correct credential per the deployed auth model (owner decision 2026-08-06: `@spaarke/auth` bearer, per the BFF workstream's task-023 web-resource migration — coordinate, same files) + surface/retry non-2xx. |
| D2-02 ⚠DV | LOW | `.../BaseProxyPlugin.cs:35` | 20 | S | low | Per-request state in instance fields (:18-20 assigned in Execute :35-38) — shared-instance race under Dataverse's cached-plugin model. Latent (no in-repo registration; `[Obsolete]`). Moot if D1-01 deletes the assembly; else refactor to per-execution context object. |
| D2-03 ⚠DV | LOW | `src/solutions/webresources/sprk_kpi_subgrid_refresh.js:14` | 15 | M | med | Both `sprk_kpi_subgrid_refresh.js:14` and `sprk_matter_kpi_refresh.js:12-15` claim to be "the ONLY web resource needed"; three near-identical implementations target the same `subgrid_kpiassessments` control with independent debounce timers in separate namespaces — co-registration double-fires the recalc POST + form refresh. **Remediation**: consolidate to the generic entity-agnostic script, delete the other two, after the §9 form-library audit. |
| D2-04 | LOW | `src/dataverse/forms/sprk_matter/insightCardMount.js:33` | 7 | S | low | Header falsely documents a live always-400 pre-warm ("INCORRECT shape… silent failure today") — the shipped `insightWidgetOnLoad.js` v0.2.0 sends the CORRECT `{ question }` shape (:178; fix documented :69-74). Update the stale header + the second stale `{ topic, mode }` comment at `insightWidgetOnLoad.js:135` in the same pass. |

### 3.3 D3 — Security (gating)

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D3-01 ⚠DV | MEDIUM | `.../BaseProxyPlugin.cs:121` | 5 | M | med | External-service ClientSecret (:121 `sprk_clientsecret`) + ApiKey (:123 `sprk_apikey`) read as plaintext Dataverse string columns (readable via advanced find/OData), passed to token acquisition (:182-187) and request header (:158) — contra the secrets-in-KV bar; no field-level security found in-repo. **Remediation**: resolved by D1-01 deletion; then drop/empty the credential columns in the live `sprk_externalserviceconfig` rows (§9). If the plugin must stay: Key Vault + managed identity. |
| D3-02 ⚠DV | MEDIUM | `src/dataverse/webresources/spaarke_documents/DocumentOperations.js:118` | 30 | M | med | `getAuthToken()` deliberately returns null (:130); every BFF call (incl. PUT upload :309-317, DELETE :578-581) is cookie-only `credentials:'include'` cross-origin (`*.crm.dynamics.com` → `spe-api-*.azurewebsites.net`) with zero Authorization usage — diverges from the ADR-028 bearer pattern every code page uses; README calls it "Production-Ready" (:90). Ribbon binding is Dataverse-data-driven (no in-repo wiring — all 29 grep hits are namesakes). **Remediation**: after the §9 ribbon audit — attach a BFF-scoped bearer via `@spaarke/auth`, or retire with the D6-02 decision. |
| D3-03 | LOW | `.../GetFilePreviewUrlPlugin.cs:108` | 6 | S | low | Raw downstream BFF error body rethrown verbatim to the Custom API caller (:105-109); `BaseProxyPlugin.cs:72` and `ParseBffResponse` (:156-157) surface inner exception messages. Moot on D1-01 deletion; else generic correlation-tagged error + trace-only detail. |
| D3-04 | LOW | `src/solutions/LegalWorkspace/src/main.tsx:117` | 8 | S | low | Unescaped `${err.message}` interpolated into `rootElement.innerHTML` in 4 bootstrap catch handlers (also `SpaarkeAi/src/main.tsx:721`, `sprk_communicationconversationpage/src/main.tsx:84-88`, `Reporting/src/main.tsx:85-89`). Low exploitability (config-origin errors) but a genuine XSS-boundary gap. **Remediation**: `textContent` (or static message + console detail). |

### 3.4 D4 — Performance & scalability

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D4-01 | MEDIUM | `.github/workflows/nightly-health.yml:213` | 0 | M | low | NO absolute bundle budget exists anywhere: nightly-health measures only 2 artifacts (:212-213) with a ±10% drift check (:209/:235-237, unbounded growth under +10%/night); zero `chunkSizeWarningLimit`/`size-limit`/`bundlesize` across all 30 solutions; heaviest bundles (LegalWorkspace: lexical+mammoth+dompurify+11 file: libs, single-chunk by design `vite.config.ts:199/:203`) unmeasured. Single-file bundling itself is a required Dataverse constraint — **KEEP**; the finding is the absent budget. **Remediation**: per-solution absolute byte ceiling (size-limit entry or shared postbuild stat gate) + extend the nightly baseline map to all 30. Workflow edits → coordinate with `ci-cd-unit-test-remediation-r1`. |
| D4-02 | LOW | `.../SimpleAuthHelper.cs:19` | 15 | S | low | Fresh OAuth token via blocking HTTPS POST per invocation (per retry attempt, verification notes); `ExpiresIn` parsed (:82) and discarded; `HttpClient` per execution (`BaseProxyPlugin.cs:140`). Moot on D1-01 deletion; else cache token keyed (clientId, scope) + static HttpClient. |
| D4-03 | LOW | `.../BaseProxyPlugin.cs:243` | 10 | S | low | Audit row re-queried via full-ColumnSet RetrieveMultiple on correlation-id (:239-243) immediately after Create (:222, Guid discarded) purely to Update (:266). Moot on D1-01 deletion; else capture the Create Guid + Update by id. |

### 3.5 D5 — DRY / dead code

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D5-01 | MEDIUM | `src/solutions/SmartTodo/src/services/xrmProvider.ts:1` | 288 | M | med | 144-line file byte-identical (minus log labels) in SmartTodo + LegalWorkspace, plus a 71-line partial in `sprk_communicationconversationpage` ("Mirrors LegalWorkspace") — non-trivial cross-frame getXrm/getUserId/SPE-container-BU-expand logic. Canonical getXrm already exists in `@spaarke/ui-components` (`xrmGlobal.ts:19`, `xrmContext.ts:236`) and both solutions already depend on it. **Remediation**: consume the shared getXrm; lift the SPE-container lookup into one shared code-page helper; delete the copies. |
| D5-02 | LOW | `src/solutions/SmartTodo/src/types/xrm.ts:1` | 58 | S | low | `xrm.ts` byte-identical (same md5) in LegalWorkspace + SmartTodo; both live (4+ importers each). Move to a single shared d.ts. |
| D5-03 | LOW | `src/solutions/CalendarSidePane/src/providers/ThemeProvider.ts:18` | 280 | M | low | 40-line re-export wrapper copy-pasted across 7 solutions (only the app-name doc comment differs); underlying logic already central in `@spaarke/ui-components`. Export one `createCodePageThemeProvider`/`setupThemeListener` from the shared lib; delete the 7 wrappers. (Importers are each app's `App.tsx`; EventsPage copy has no further consumer — strengthens the case.) |
| D5-04 | LOW | `src/solutions/CommunicationReconciliation/src/services/authInit.ts:33` | 520 | M | med | `authInit.ts` factory-wrapper boilerplate copy-pasted across 8 solutions (58-101 lines each, "mirrors EmailPage exactly"); real logic central in `@spaarke/auth` `createCodePageAuthInitializer.ts` but the parameterized per-app module does NOT yet exist. Provide `createCodePageAuthModule({clientId,bffBaseUrl,scope,logLabel})` in `@spaarke/auth`; each app calls it with config. |
| D5-06 | LOW | `src/dataverse/forms/sprk_matter/insightCardMount.ts:1` | 200 | S | low | Three .ts/.js pairs dual-tracked in git (`insightCardMount`, `insightWidgetOnLoad`, `communicationsGridOnLoad`; .js = tsc output of the adjacent .ts; not gitignored). Treat .js as build output: gitignore + generate at deploy, or enforce CI regeneration. (Also D10.) |
| D5-07 ⚠DV | INFO | `src/dataverse/solutions/spaarke_core/Other/.gitkeep:1` | 0 | S | low | Empty scaffolds (see D1-02). NOT asserted dead — the Dataverse solution NAME `spaarke_core` is a live deploy target in many scripts (`Create-AiChatContextMapEntity.ps1:35` etc.); repo scaffold vs live solution must not be conflated. Human confirms roadmap-vs-leftover after the §9 live check. |

### 3.6 D6 — Consistency & conventions

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D6-02 ⚠DV | MEDIUM | `src/dataverse/webresources/spaarke_documents/DocumentOperations.js:1` | 857 | S–L | high | 857-line no-framework legacy JS webresource with business logic — violates CODING-STANDARDS rules 55/56 + ANTI-PATTERNS #10 + ADR-006, inconsistent with the sibling TS-compiled `forms/sprk_matter/*.ts` convention; no KEEP exception documented; inventory lists it deployed. **Remediation (owner decision, §7)**: document a KEEP legacy exception (S) or migrate per ADR-006 (L, separate project per NG2) — after the §9 ribbon audit. |
| D6-03 | MEDIUM | `src/solutions/SpaarkeCore/customizations.xml:1` | 233 | S | low | Root `customizations.xml`+`solution.xml` are byte-identical duplicates of `Other/Customizations.xml`+`Other/solution.xml` (full-file diff identical); siblings keep manifests only under `Other/`; no tooling references the root copies. Delete the root duplicates. |
| D6-04 | LOW | `src/solutions/SpaarkeCore/Other/solution.xml:1` | 0 | S | low | Sole lowercase `solution.xml` outlier vs 13 sibling `Other/Solution.xml` (git stores the case; breaks case-sensitive runners). `git mv` to canonical capitalization. |
| D6-06 | LOW | `src/dataverse/solutions/spaarke_containers/Entities/sprk_Document/Entity.xml:3` | 0 | S | low | Schema-name casing drift: exported PascalCase folders (`sprk_Document`, `sprk_Container`, `sprk_Matter`, `sprk_Precedent`) vs lowercase scaffold/doc folders (`sprk_document`, `sprk_matter`…) — "Document" appears as both. Align scaffold folder casing to the actual SchemaName (repo-side only; Entity.xml records the live name). |
| D6-07 | LOW | `.../GetFilePreviewUrlPlugin.cs:46` | 1 | S | low | `SERVICE_NAME` SCREAMING_SNAKE_CASE violates C# rule 15 (PascalCase). Moot on D1-01 deletion; else rename the identifier ONLY — the string value `"SDAP_BFF_API"` is a Dataverse config lookup key and must not change. |

### 3.7 D7 — Testability & test quality

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D7-01 | HIGH | `.github/workflows/deploy-spaarke-ai.yml:112` | 0 | M | med | The entire src/solutions Jest suite — **152** test files (verified count; 118 under SpaarkeAi), 7 package.json `"test": "jest"` scripts — is executed by NO workflow (0/18) and NO husky hook (pre-commit = lint-staged/prettier/eslint only). Deploys gate on a grep-based HTML smoke test (:112-136), never the tests. **Remediation**: changed-solution-scoped Jest step in ci-tier1/tier2 or a dedicated client-tests workflow; gate solution deploys on it. Workflows owned by `ci-cd-unit-test-remediation-r1` — coordinate. Expect first-run failures to triage (suites may have rotted). |
| D7-02 | MEDIUM | `src/solutions/WorkspaceLayoutWizard/src/__tests__/rowHeight.test.tsx:268` | 22 | S | low | `describe.skip` wraps 3 assertion-free placeholder bodies reserving coverage names ("scaffold the test names so the coverage exists") — textbook ADR-038 §7 B10/B13 scaffolding; `void within;` (:292) silences the lint. Delete the block; if the JSON boundary matters, export `buildSectionsJson` and write a real test. |
| D7-03 | MEDIUM | `src/solutions/SmartTodo/src/components/Toolbar/__tests__/ToolbarActions.test.ts:281` | 40 | S | low | Real mailto-compose behavior `it.skip`'d (full body + real assertions :309-316) because jsdom blocks `window.location`; seam fix never applied (`ToolbarActions.ts:297` still assigns `window.location.href` directly); contract lives on manual UAT only. Introduce an injectable navigation seam (or jest-location-mock) and un-skip. |
| D7-04 ⚠DV | LOW | `.../BaseProxyPlugin.cs:281` | 60 | S | low | Zero tests for security-relevant plugin logic: `RedactSensitiveData` (:281-302), token acquisition (:176-197), `IsTransientError` (:348-371); no test project in Spaarke.sln (13 projects, none plugin); ci-tier2 builds but never tests it. Preferred path: D1-01 deletion removes the untested code. If the §9 check shows it live-registered: small pure-logic unit tests for Redact + IsTransientError. |

### 3.8 D8 — Dependency & supply-chain hygiene

No verified findings. The sole first-pass claim (D8-01) was a refuted placeholder — see §8. Positive evidence on record: 30 tracked, per-project `package-lock.json` files (D10-08 verification); `pdfjs-dist` version consistency across SpaarkeAi ↔ shared lib (D8-01 refutation analysis). Residual supply-chain notes are filed under their owning dimensions: committed private .snk (D10-05), unpinned build-time ILRepack fetch in a superseded script (D10-03), caret ranges mitigated by lockfiles (D10-08). **Action**: a real dependency/CVE audit of the 30 npm graphs belongs to the r3 dependencies horizontal sweep (NG5).

### 3.9 D9 — Observability

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D9-01 | MEDIUM | `src/solutions/DocumentUploadWizard/src/main.tsx:92` | 0 | M | low | Telemetry coverage is ~**5 of 35** solutions (verification-corrected from "2 of 30": LegalWorkspace, SpaarkeAi, EmailPage, CommunicationReconciliation, DailyBriefing — the latter three no-op without build-time `VITE_APP_INSIGHTS_KEY`); the rest, incl. the anchor upload path, are console-only. A shared `AppInsightsService` ALREADY exists in `@spaarke/ui-components` (`AppInsightsService.ts:39`). **Remediation**: roll out the existing shared service (NOT a new mirror of LegalWorkspace telemetry.ts) with trackEvent/trackException on create/upload/load critical paths. |
| D9-02 | MEDIUM | `src/solutions/LegalWorkspace/src/services/telemetry.ts:48` | 6 | S | low | Real InstrumentationKey (`09a9beed-…`) + westus2 endpoints hardcoded (:43-46) as universal fallback (`envKey ?? DEV_CONNECTION_STRING` :48); resolver swallows all errors → null (:118/:122/:136-138), so any env-var miss silently routes prod/UAT telemetry to the dev resource; the fail-closed guard (:49-52) is unreachable dead code. Live at bootstrap (`main.tsx:22/:29`). Fail closed + remove the embedded key. |
| D9-03 | MEDIUM | `src/solutions/LegalWorkspace/src/components/CreateMatter/matterService.ts:221` | 15 | S | low | Full matter-create payload JSON-dumped to console on every create (:221, incl. contact/attorney/org lookups); raw user search queries logged (:391/:444/:481/:600/:638); full `result.entities` arrays (:447/:484); zero env gating (grep: no `import.meta.env`/`NODE_ENV`/DEBUG) — ships to production, violating "no PII in logs". Remove or gate behind a dev-only debug flag. |
| D9-04 | LOW | `src/dataverse/webresources/spaarke_documents/DocumentOperations.js:838` | 6 | S | low | Unconditional debug residue in a production webresource: API base URL echo (:71), every-call method+URL log (:164), "Testing namespace access" probe (:838-839); no build/strip step exists (no package.json under webresources). Strip; keep console.error on failures. Correlation-ID forwarding (:161-162) is the D9 positive — keep. |
| D9-05 | LOW | `.../BaseProxyPlugin.cs:40` | 5 | S | low | Base-class lifecycle traces (:40/:60/:65/:98/:130) omit the correlation ID generated at :208, while derived traces include it — lifecycle entries can't join the audit rows. Moot on D1-01 deletion; else prefix traces with the correlation ID. |
| D9-06 | LOW | `.../GetFilePreviewUrlPlugin.cs:106` | 3 | S | low | Full unbounded downstream BFF error body traced verbatim into the plugin trace log (:105-106) and rethrown to the caller (:108-109); RedactSensitiveData does not cover this path. Moot on D1-01 deletion; else truncate/scrub. |

### 3.10 D10 — ALM / build hygiene

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D10-03 | MEDIUM | `.../Spaarke.Dataverse.CustomApiProxy/build-and-merge.ps1:1` | 120 | S | low | Superseded ILRepack merge script: merges Azure.Identity/MSAL/S.T.Json that the csproj never references (merge silently no-ops via the Test-Path filter :81); fetches ILRepack 2.0.18 ad hoc at build time (unpinned `Invoke-WebRequest` :41-44 + global tool install :29); `SimpleAuthHelper.cs:11-12` documents the no-ILMerge design; zero repo references. Delete. |
| D10-04 | MEDIUM | `.../Spaarke.Dataverse.CustomApiProxy.csproj:3` | 17 | S | low | No TreatWarningsAsErrors / Deterministic / AssemblyVersion / Nullable / analyzers; `LangVersion=latest`. **Verification strengthened**: the nearest `Plugins/Directory.Build.props` sets only `ManagePackageVersionsCentrally=false` with no chain-import, **shadowing ALL root hygiene props** (root sets TWAE/Deterministic/Nullable). Chain-import the parent via `GetPathOfFileAbove` + add the missing properties — or moot via D1-01 deletion. |
| D10-05 | MEDIUM | `.../SpaarkePlugin.snk:1` | 0 | M | med | Full RSA strong-name key PAIR committed (596 B = key pair, not the ~160 B public token); csproj signs with it (:9-10); no `*.snk` gitignore entry. Untrack + gitignore; source from CI secret if signing must continue. ⚠ If the plugin stays live-registered, re-signing with a NEW key changes the assembly's strong-name identity → Dataverse re-registration required (couple with the §9 check / D1-01 decision). History purge is an owner call (key grants identity-spoofing, not code execution). |
| D10-06 | MEDIUM | `src/solutions/SpaarkeCore/solution.xml:1` | 0 | M | low | Non-standard hybrid: duplicated root+Other manifests, hand-written `*-schema.md` docs intermixed under `entities/`, no Entity.xml anywhere — cannot round-trip with `pac solution pack`. Separate design docs from packable source; keep one canonical PAC layout (with D1-05's intent README as the minimum fix). |
| D10-07 | LOW | `src/solutions/EventCommands/solution export/solution.xml:1` | 0 | S | low | Raw unpacked environment export committed in a space-named folder beside hand-authored ribbon JS/HTML; no tooling consumes it. Keep authored source only; regenerate exports at build time; no spaces in solution paths. |
| D10-08 | LOW | `src/solutions/SmartTodo/package.json:1` | 0 | L | med | 30 standalone npm projects (30 lockfiles — per-project installs ARE pinned), root package.json has no `workspaces`, no turbo/nx graph; `file:` shared-lib links create uncaptured build-order deps. Evaluate npm workspaces / turbo (NG3 — file as evaluation, don't convert in this workstream). |

Cross-reported into D10: D1-02 (segmentation facade), D1-03 (folder/UniqueName), D5-06 (committed build artifacts), D4-01 (no budgets), D7-01 (dormant suite in CI).

### 3.11 D11 — Knowledge/doc accuracy

| ID | Sev | File:line | LOC | Effort | Risk | Finding → remediation |
|---|---|---|---|---|---|---|
| D11-01 ⚠DV | MEDIUM | `docs/data-model/entity-relationship-model.md:104` | 40 | M | low | Models the AI domain as pre-R7 `sprk_analysis`→`sprk_analysisaction` parent-child with 8 child tables + `sprk_actiontypeid`→`sprk_analysisactiontype` typing (:24-32/:90-117/:167-177/:202); zero occurrences of `sprk_playbookconsumer`/`sprk_kind`/`sprk_inputschema`/`sprk_modeltier`. Shipped code reads the R7 columns (`ConsumerRoutingService.cs:102-117/:884-895`); `sprk_analysisaction.md:15` records the pre-R7 columns DROPPED in R7 Wave 4. Refresh the AI sections to R7 (or delegate to `sprk_analysisaction.md`/`sprk-playbookconsumer.md`) after the §9 live-schema confirm. |
| D11-02 ⚠DV | MEDIUM | `docs/data-model/field-mapping-reference.md:435` | 90 | M | low | AI/Analysis tables (:404-493) document dropped `sprk_actiontypeid` (:435), legacy allows* toggles (:441-444), `sprk_playbookmode` (:455) — and NONE of the live R7 execution columns (grep: 0 hits); doc dated 2026-04-05, pre-R7; INDEX still lists it as primary. Update the tables to the R7 column set or replace with a pointer to the maintained live-schema docs. |
| D11-03 | LOW | `docs/data-model/INDEX.md:68` | 5 | S | low | INDEX declares the pre-R7 AI snapshots purged (:48) while listing both 2026-04-05 docs as Status "New" (:16-17) and routing cross-entity readers to them first (:68). After D11-01/02 land: bump Last Reviewed dates + note that AI-domain relationships are owned by the per-entity live-schema docs. |

**Cross-reported into D11**: D2-04 (stale comment claiming a live broken path), D1-05 (manifest-vs-content intent undocumented).

---

## 4. Design principles

1. **The obsolete plugin is one decision, nine findings.** D1-01, D2-02, D3-01, D3-03, D4-02, D4-03, D6-07, D7-04, D9-05, D9-06 all collapse into "delete the `Spaarke.CustomApiProxy` assembly after the Dataverse pre-check". Execute it as one task-cluster with one pre-check, not ten fixes.
2. **Data-driven dispatch is not grep-provable (NFR-08).** Every ⚠DV finding runs its §9 live-org check BEFORE any rename/delete/consolidate. No exceptions.
3. **Repo-side first.** Everything that is provably source-only (doc refresh, manifest dedupe, comment fixes, log scrubs, csproj hygiene, script deletion) lands in Tranche A without waiting on live-org access.
4. **Reuse the shared lib that already exists.** Verification proved the canonical implementations exist (`xrmGlobal.ts`, `AppInsightsService.ts`, `createCodePageAuthInitializer.ts`) — consolidation means consuming them, not authoring rivals (CLAUDE.md §11).
5. **Coordinate the two workflow owners.** `.github/workflows` edits (D7-01 Jest, D4-01 budgets) are owned by `ci-cd-unit-test-remediation-r1`; the KPI web-resource auth fix (D2-01) shares files with BFF workstream task 023. `/conflict-check` before every PR.
6. **Small, reviewable, revertible commits** off the one r3 branch; behavior-preserving by default (only D2-01/D3-02 change behavior, deliberately).

---

## 5. Proposed workstreams → phases (A/B tranche split per r3 NFR-04)

**Tranche A = low-contention bugs/hygiene** (source-only, no live-org dependency, minimal collision risk with the 19 active worktrees). **Tranche B = wide or contested edits** (Dataverse pre-checks, multi-solution touch, shared-file collisions, workflow ownership) — schedule for a quiet window with `/conflict-check`.

### Phase 1 — Tranche A: docs, comments, logs (S efforts, low risk)
- 1a. D2-04: fix the two stale insight pre-warm comments (`insightCardMount.js` header; `insightWidgetOnLoad.js:135`).
- 1b. D11-01 + D11-02 + D11-03: refresh the two cross-entity data-model docs to R7 (or delegate AI sections) + reconcile INDEX. Run the §9 live-schema confirm first (read-only describe — safe anytime).
- 1c. D9-03: strip/gate the matterService payload + query console dumps. D9-04: strip DocumentOperations debug residue (log-only edit; no ribbon dependency).
- 1d. D9-02: telemetry fail-closed; remove the embedded instrumentation key.
- 1e. D3-04: innerHTML → textContent in the 4 bootstrap handlers.

### Phase 2 — Tranche A: repo-side build/ALM hygiene
- 2a. D6-03 + D6-04: delete SpaarkeCore root manifest duplicates; `git mv` the casing outlier.
- 2b. D10-03: delete `build-and-merge.ps1`.
- 2c. D10-04 (+D6-07 if plugin retained): chain-import the parent `Directory.Build.props`; add TWAE/Deterministic/Version/pinned LangVersion. (If Phase 3 deletes the plugin, 2c/D6-07 shrink to nothing — sequence 2c AFTER the Phase-3 decision if convenient.)
- 2d. D10-05: untrack `SpaarkePlugin.snk` + add `*.snk` to .gitignore (history purge = owner decision; re-sign coupling noted in §3.10).
- 2e. D10-07: remove the EventCommands raw export folder. D5-06: gitignore the generated form-script .js (or CI-regenerate rule).
- 2f. D7-02: delete the skipped placeholder block. D7-03: injectable navigation seam + un-skip the mailto test.
- 2g. D1-05 (+D10-06 minimum fix): README documenting SpaarkeCore's fragments-not-packable intent (full restructure = Tranche B, 4d).

### Phase 3 — Tranche B: the obsolete-plugin decision cluster (one pre-check, nine findings)
- 3a. **§9 pre-check battery** against the live org (read-only; can run any time; results to `notes/dataverse-precheck-model-alm.md`).
- 3b. If unregistered (expected): delete `Spaarke.CustomApiProxy` (3 .cs + csproj + cdsproj + wrapper Solution.xml + .snk + script) → closes D1-01, D2-02, D3-03, D4-02, D4-03, D6-07, D7-04, D9-05, D9-06; update `ADR002_PluginTests.cs` KnownViolations (36-49, count :249) to zero. Then D3-01 follow-up: clear/drop `sprk_clientsecret`/`sprk_apikey` on the live `sprk_externalserviceconfig` rows.
- 3c. If LIVE-registered (escalate 🔔): owner chooses ADR-002 completion timeline; interim = KV secret resolution (D3-01), redaction/trace fixes (D3-03/D9-05/D9-06), token cache (D4-02), pure-logic tests (D7-04).

### Phase 4 — Tranche B: solution segmentation & structure (quiet window)
- 4a. D1-02 + D5-07: after §9 live-solution check — populate the segmented exports OR delete both stub trees (recommend delete; the live solutions remain the source of truth and the facade misleads).
- 4b. D1-03: rename `spaarke_containers/` → `spaarke_document_management/` (verified source-safe; grep + fix the 5 project-notes references).
- 4c. D1-04: relocate `sprk_kpiassessment_matter.xml` to the KPI/Matter domain home after the §9 membership check.
- 4d. D10-06 + D6-06: SpaarkeCore restructure (docs out of packable source) + scaffold-casing alignment.

### Phase 5 — Tranche B: client consolidation, reliability, CI (wide/multi-owner)
- 5a. D2-01 + D2-03: KPI rollup — §9 form-library audit → consolidate to ONE generic script; standardize on `@spaarke/auth` bearer per the owner's 2026-08-06 decision; surface failures. **Coordinate with BFF workstream task 023 (same web-resource files).**
- 5b. D3-02 + D6-02: DocumentOperations — §9 ribbon audit → owner decision (§7): bearer-auth fix + documented KEEP exception, or ADR-006 migration project (NG2).
- 5c. D5-01 + D5-02: xrmProvider/xrm.ts consolidation into shared lib (3 solutions + shared helper).
- 5d. D5-03 + D5-04: ThemeProvider factory export + `createCodePageAuthModule` in `@spaarke/auth`; delete 15 wrapper files across 8+ solutions (wide touch — one PR per shared-lib change + mechanical consumer sweep).
- 5e. D7-01: wire changed-solution-scoped Jest into CI — **coordinate with `ci-cd-unit-test-remediation-r1` (owns `.github/workflows`)**; triage first-run failures.
- 5f. D4-01: absolute bundle budgets (size-limit or shared postbuild gate) + nightly baseline map extension (same workflow-owner coordination).
- 5g. D9-01: shared `AppInsightsService` rollout to the untelemetered solutions' critical paths.
- 5h. D10-08: file the npm-workspaces/turbo evaluation (NG3 — evaluation only).

### Phase 6 — Wrap-up
- SCORECARD row hand-off (invoking task appends — this design only supplies §10 inputs); `/test-diet` if `tests/**`-modifying tasks ran; doc-drift audit; re-grade check on remediated dimensions.

---

## 6. Effort & impact roll-up

| Tranche | Phases | Findings closed | Dominant effort | Risk profile |
|---|---|---|---|---|
| A | 1–2 | 16 (all doc/log/hygiene) | S | low — source-only, no live-org dependency |
| B | 3 | 9–10 (plugin cluster + D3-01) | M (delete path) | med — gated on one Dataverse pre-check |
| B | 4 | 5 (segmentation/structure) | S–M | low-med — folder ops after live checks |
| B | 5 | 10 (consolidation, reliability, CI, telemetry) | M–L | med — wide multi-solution touch + 2 owner coordinations |

Total verified findings: **46** (2 HIGH, 20 MEDIUM, 23 LOW, 1 INFO). ~9-10 of them close with the single plugin-deletion decision.

---

## 7. Owner decisions required

🔔 **Decision 1 — `DocumentOperations.js` (D3-02/D6-02/D9-04)**: after the ribbon audit shows it live or dead: (A) dead → delete; (B) live → bearer-auth fix (`@spaarke/auth`) + documented KEEP legacy exception, migration deferred (NG2); (C) live → commission the ADR-006 migration now. **Recommendation: (B)** — closes the auth divergence cheaply; migration is a real project.
🔔 **Decision 2 — plugin found LIVE-registered** (Phase 3c contingency): interim hardening vs immediate ADR-002 completion. **Recommendation**: immediate completion — the BFF preview path already exists per the `[Obsolete]` note.
🔔 **Decision 3 — .snk history purge (D10-05)**: untrack-only vs git-history rewrite. **Recommendation**: untrack-only if Phase 3b deletes the plugin (the key then signs nothing); rewrite is disruptive across 19 worktrees.
🔔 **Decision 4 — segmentation stubs (D1-02/D5-07)**: populate vs delete. **Recommendation: delete** — the live org is the schema source of truth; empty scaffolds imply an ALM discipline that does not exist.

---

## 8. Explicit KEEPs + refuted claims (record-only — do NOT act on, do NOT re-claim)

### KEEPs (verified intentional — MUST NOT be "fixed")

| Item | Why it stays |
|---|---|
| Single-file code-page bundling (`manualChunks: undefined`, `assetsInlineLimit`) | Required Dataverse code-page constraint. D4-01 targets the absent budget, NOT the chunking. |
| Plugin `GetAwaiter().GetResult()` sync-over-async | Unavoidable under `IPlugin.Execute`'s synchronous contract — deliberately NOT flagged. |
| Dataverse `PhysicalName`(Pascal)/`LogicalName`(lower) attribute pairs | Standard platform behavior — deliberately NOT flagged (D6-06 scope excludes them). |
| `tests/Spaarke.ArchTests/ADR002_PluginTests.cs` KnownViolations guard | A guard test corroborating D1-01, not a live consumer. It stays — but its allowlist MUST be updated to zero when Phase 3b lands. |
| `runtimeConfig.ts` thin factory-call shims (6 solutions) | Deliberate singleton-per-solution seams over the canonical `createRuntimeConfigStore` (`@spaarke/auth`), per FR-21/ADR-028 — see refuted D5-05. Only `Reporting`'s hand-rolled copy (80 lines) is a legitimate future migrate-to-factory nit. |
| `SERVICE_NAME` string VALUE `"SDAP_BFF_API"` | Dataverse External Service Config lookup key — if D6-07 renames the identifier, the value must not change. |
| Correlation-ID generation + `X-Correlation-Id` forwarding in DocumentOperations.js (:161-162) | The one D9 positive in that file — preserve through any D9-04 log strip. |

### Refuted by verification (record-only — MUST NOT appear as findings, drive grades, or generate remediation)

| ID | Claim | Refutation |
|---|---|---|
| D5-05 | "runtimeConfig.ts accessor module duplicated across 7 Code Page solutions (~294 LOC)" | **REFUTED.** The claimed missing primitive already exists (`src/client/shared/Spaarke.Auth/src/createRuntimeConfigStore.ts`) and 6 of the 7 files are deliberate thin factory-call + re-export shims consuming it (self-documented FR-21/ADR-028 singleton-per-solution seams, e.g. `EmailPage/src/config/runtimeConfig.ts:16`). Only `Reporting/src/config/runtimeConfig.ts` hand-rolls the skeleton — a single not-yet-migrated app. The suggested remediation is already implemented. |
| D8-01 | "test" (placeholder finding vs `SpaarkeAi/package.json:77`) | **REFUTED.** Placeholder with no verifiable defect; cited evidence text exists nowhere in the file. Under the only plausible interpretation (unused/conflicting dep), falsified: `pdfjs-dist` is load-bearing (imported via the shared `useChatFileAttachment.ts`), declared at the identical range in the `file:`-linked shared package — required for bundle-time resolution and version-consistent. |

---

## 9. Data-driven-dispatch pre-check list (NFR-08 — run BEFORE remediation)

Dataverse `sprk_*` registration/config rows are not grep-provable. Each ⚠DV finding's remediation is **gated** on the exact live-org check below (all read-only; batch into one pre-check task, results to `notes/dataverse-precheck-model-alm.md`).

| Finding(s) | Exact Dataverse check to run first |
|---|---|
| **D1-01, D2-02, D7-04** (+ moot-on-delete: D3-03, D4-02, D4-03, D6-07, D9-05, D9-06) | Query `pluginassembly` for `Spaarke.Dataverse.CustomApiProxy`; `customapi` for `sprk_GetFilePreviewUrl`; `sdkmessageprocessingstep` for any step bound to that assembly/type. Unregistered → Phase 3b delete. Registered → Phase 3c escalation. |
| **D3-01** | Same registration check, PLUS: query `sprk_externalserviceconfig` rows (esp. name `SDAP_BFF_API`) for non-empty `sprk_clientsecret`/`sprk_apikey`; check field-level security profiles on those columns. Determines whether live secrets must be rotated/cleared and whether the columns can be dropped. |
| **D1-04** | Query solution-component membership of relationship `sprk_matter_kpiassessment` (`solutioncomponent` where componenttype=10/relationship, joined to `solution.uniquename`) — establish which live solution actually owns it before move + re-export. |
| **D2-03** (and the D2-01 consolidation) | Export/inspect live FormXml for Matter + Project main forms: which of `sprk_matter_kpi_refresh` / `sprk_kpi_subgrid_refresh` / `sprk_subgrid_parent_rollup` are registered as form libraries + OnLoad handlers, and whether any form carries more than one. Determines the consolidation target and confirms/refutes double-wiring. |
| **D3-02, D6-02** (and D9-04's file) | Query live ribbon customizations (`ribbondiff`/RibbonDiffXml on `sprk_document` + related entities) and `webresource` for `sprk_DocumentOperations.js` (or equivalent name): which commands invoke `Spaarke.Documents.*` functions. Determines live vs dead before auth-fix/migrate/delete. |
| **D5-07, D1-02** | Query `solution` for uniquenames `spaarke_core` + `spaarke_documents` (+ `spaarke_document_management`): do they exist, version, component counts (`solutioncomponent` per solution). Confirms the live solutions are the source of truth before deleting the repo scaffolds — repo deletion must not be misread as solution retirement. |
| **D11-01, D11-02** | Read-only metadata describe of `sprk_analysisaction` (confirm `sprk_actiontypeid` absent; `sprk_kind`/`sprk_inputschema`/`sprk_modeltier`/`sprk_workflowclass`/`sprk_actioncode` present) and existence of `sprk_playbookconsumer` — final confirmation the R7 doc refresh matches the live org, not just shipped code. |

---

## 10. SCORECARD row inputs (for the invoking task — do not append here)

**Surface**: Dataverse data model + solution ALM
**Dimension letters**: D1 **B-** · D2 **B-** · D3 **B-** · D4 **B-** · D5 **B** · D6 **B-** · D7 **C+** · D8 **B+** · D9 **C+** · D10 **C+** · D11 **C+**
**Composed surface grade**: equal-weight mean 2.636 → B-; gating cap min(B-, D2 B-, D3 B-) = **B-** (cap not applied — mean already at the gate).

**Evidence bullets (one per dimension):**

- **D1 B-** — ADR-002-violating sync-HTTP proxy plugin retained in-tree ([Obsolete] `BaseProxyPlugin.cs:15`, allowlisted in `ADR002_PluginTests.cs:36-49`, registration data-driven); segmentation half-finished (`spaarke_documents` stubs vs `spaarke_containers/Other/Solution.xml:82` shell) and folder ≠ UniqueName (`Solution.xml:4`).
- **D2 B-** — Live KPI rollup swallows all non-2xx to console.warn with inconsistent credential handling (≥1 path wrong: `sprk_matter_kpi_refresh.js:273-278` vs `sprk_kpiassessment_quickcreate.js:273`); latent instance-field race only on the obsolete unregistered plugin; no confirmed live broken data path.
- **D3 B-** — Secrets architected as plaintext Dataverse columns (`BaseProxyPlugin.cs:121-123`, obsolete plugin) and cookie-only cross-origin BFF writes (`DocumentOperations.js:118-135` vs ADR-028), both deployment-caveated; error-body leak + innerHTML hygiene LOWs; no confirmed live unauthenticated mutation.
- **D4 B-** — No absolute bundle budget anywhere; 29/30 code-page solutions have zero size tracking and the only gate is a ±10% drift check on 2 artifacts (`nightly-health.yml:209-237`); plugin token/query chattiness confined to the obsolete path.
- **D5 B** — One real 144-line verbatim clone (`xrmProvider.ts` ×2 + partial third) reimplementing the shared-lib getXrm, plus 7×/8× wrapper boilerplate; heavy lifting IS centralized and the 7-way runtimeConfig duplication claim was refuted (factory exists, 6/7 consume it) — moved up from first-pass B-.
- **D6 B-** — 857-line legacy JS webresource with business logic contradicting ADR-006/rules 55-56 (`DocumentOperations.js`), byte-identical duplicate manifests in SpaarkeCore, and casing drift (sole lowercase `solution.xml`; `sprk_Document` vs `sprk_document` sibling folders).
- **D7 C+** — The entire 152-file src/solutions Jest suite is executed by no workflow (0/18) and no hook (pre-commit = lint-staged only); deploys gate on an HTML grep smoke test (`deploy-spaarke-ai.yml:112-136`); plus ADR-038 B10/B13 skipped placeholders and a disabled real behavioral test.
- **D8 B+** — Zero verified findings (sole claim was a refuted placeholder — finder failure); 30 fresh tracked lockfiles + clean pdfjs-dist consistency spot-check; graded on thin cross-dimension evidence with no affirmative CVE audit → real D8 pass deferred to the dependencies horizontal sweep.
- **D9 C+** — Telemetry on ~5/35 solutions (verification-corrected; shared `AppInsightsService` exists but unadopted); hardcoded dev App Insights connection string as universal fallback with unreachable fail-closed guard (`telemetry.ts:43-52`); full matter payloads + raw search queries console-logged ungated (`matterService.ts:221` et al.).
- **D10 C+** — Solution segmentation is a folder facade (2 of 3 solutions .gitkeep-only); plugin csproj shadowed from ALL root hygiene props by a non-chaining `Directory.Build.props`; full private .snk key pair committed (596 B, no gitignore); superseded ILRepack script with unpinned build-time downloads.
- **D11 C+** — Both designated "start here" data-model docs (`entity-relationship-model.md`, `field-mapping-reference.md`, dated 2026-04-05) embed the dropped pre-R7 AI schema (`sprk_actiontypeid`, 0 hits for any R7 routing/execution column) while `INDEX.md:48` declares that schema purged and `:68` still routes readers to them first.

---

## 11. Risks

| Risk | Mitigation |
|---|---|
| Plugin/webresource assumed dead is live-registered in the org | §9 pre-check battery is a hard gate before every ⚠DV remediation; Phase 3c escalation path defined |
| KPI web-resource edits collide with BFF workstream task 023 (same files) | Single owner for the web-resource auth migration; `/conflict-check` + explicit sequencing between the two workstreams |
| `.github/workflows` edits collide with `ci-cd-unit-test-remediation-r1` ownership | D7-01/D4-01 delivered as proposals/PRs INTO that project's ownership, not unilateral edits |
| First CI Jest run surfaces rotted suites (red wall) | Changed-solution scoping + advisory-tier introduction before blocking-tier promotion |
| Folder rename (4b) breaks an unknown consumer | Verification already grepped ps1/sh/yml/json/csproj/cdsproj = zero path consumers; re-grep at execution + fix the 5 project-notes references |
| Re-signing after .snk rotation changes plugin assembly identity | Couple D10-05 with the Phase-3 decision; if the plugin is deleted, the key signs nothing |
| Multi-solution wrapper deletion (5d) churns 8+ bundles at once | One shared-lib PR first, then per-solution mechanical sweeps in small PRs |

---

## 12. Acceptance criteria (draft — for `/design-to-spec` to close)

- [ ] §9 pre-check battery executed and recorded in `notes/dataverse-precheck-model-alm.md` BEFORE any ⚠DV remediation merges.
- [ ] Plugin cluster resolved per the pre-check (deleted + ArchTest allowlist at zero, or 3c hardening set complete).
- [ ] Exactly one KPI rollup web resource remains; all BFF calls from web resources carry the owner-decided credential; non-2xx surfaced to the user.
- [ ] `src/dataverse/solutions` folder names match manifest UniqueNames; zero .gitkeep-only solution facades remain (or populated exports committed).
- [ ] Changed-solution-scoped Jest executes in CI; a bundle-size ceiling exists for every deployed code page.
- [ ] No hardcoded telemetry connection string; no ungated payload/query console dumps in production paths (grep-verified).
- [ ] `entity-relationship-model.md` + `field-mapping-reference.md` AI sections match the live R7 schema; INDEX reconciled.
- [ ] No `*.snk` tracked; plugin csproj (if retained) inherits root hygiene props.
- [ ] `/conflict-check` clean (or coordinated) before every PR touching web resources, workflows, or shared libs.

---

*Assessment complete 2026-08-14. This file is the sole write output of the synthesis stage (r3 NFR-03). The SCORECARD row is appended by the invoking task from §10.*
