# Shared Client Libs (Spaarke.*) — Quality Assessment & Remediation Design

> **Slug**: `shared-client-libs` (a **surface workstream** of `code-quality-and-assurance-r3`, not a standalone project)
> **Surface**: `src/client/shared/**` — the 15 Spaarke.* npm packages (AI.Context, AI.Outputs, AI.Widgets, Auth, Communication.Components, Compose.Components, DailyBriefing.Components, DocumentOperations, Events.Components, LegalWorkspace, Notifications, SdapClient, SmartTodo.Components, UI.Components, Visuals)
> **Date**: 2026-08-14
> **Method**: quality-assessment workflow: 11-dimension fan-out + Fable adversarial verification. Findings below are the **verified set only** (r3 spec NFR-05 input discipline) — every item survived the mandatory Fable refutation pass; refuted first-pass claims are quarantined in §3 and MUST NOT drive remediation.
> **Read-only statement**: this assessment modified **no code**. This design.md is the sole write output (r3 NFR-03). Remediation is task-created separately, operator-gated.
> **Status**: DRAFT — for owner review before `/task-create`.
> **Program**: executes in the r3 worktree on `work/code-quality-and-assurance-r3` per the single-worktree decision; the A/B tranche split (r3 NFR-04) governs PR sequencing off that branch, `/conflict-check` before each.

---

## 0. Summary & verdict

The shared client nucleus is **functionally healthy but institutionally unenforced**. The strongest dimensions are exactly the ones that matter most at runtime: correctness (A-) — auth token chains, SSE streaming, polling, and reconnection logic are deterministic and defensively edged, and the one broken-by-construction path (SdapClient's empty-Bearer legacy methods) is verified unreachable in every production wiring; and security (B+) — the XSS/sanitization boundary is genuinely hardened, with only latent (not live) token-path debt.

The weakest dimension is **ALM/build hygiene (D+)**: verification established that **zero of the 15 packages are blocking-gated in CI** (the 6-package typecheck chain runs in a `continue-on-error` job), **no shared-package Jest suite has ever executed in any workflow** (425 test files providing zero CI signal), no shared package is linted anywhere, and `npm ci` is abandoned repo-wide so committed lockfiles are non-authoritative. Everything else — the copy-paste debt (D5), the console-only observability (D9), the untested `authenticatedFetch` (D7), the fabricated front-door doc (D11) — persists precisely *because* nothing in CI would ever catch it.

Two first-pass architectural claims and one security-doc claim were **refuted** by verification (PaneEventBus placement and SprkChat-in-nucleus are both ADR-sanctioned; ADR-028 does exist) — D1 and D6 moved **up** accordingly; D2 moved up on the reachability verification; D10 moved **down** on the worsened CI evidence.

**53 verified findings**: 5 HIGH · 21 MEDIUM · 25 LOW · 2 INFO. ~440 LOC provably dead/deletable; ~900 LOC of consolidatable duplication; ~11,200 LOC concentrated in four God-modules.

### Per-area grade table (re-adjudicated, verified findings only)

| Dim | Area | Grade | First pass | Movement | One-line basis |
|---|---|---|---|---|---|
| D1 | Architecture & boundaries | **B-** | C+ | ▲ (2 refutations) | Phantom deps in AI.Widgets (HIGH, 8-LOC fix); God components 2.4–4.0k LOC; monolithic import-time registration |
| D2 | Correctness & reliability | **A-** | B+ | ▲ (severity lowered) | Only 2 LOW survive; broken SdapClient path unreachable in all production wiring |
| D3 | Security | **B+** | B+ | = | XSS boundary strong; latent (not live) auth-by-omission on SdapClient mutations; Bearer convention unenforced |
| D4 | Performance & scalability | **A-** | A- | = | 2 LOW + 1 INFO; async correctness + lazy-loading exemplary |
| D5 | DRY / dead code | **B-** | B- | = (1 scope-narrowed) | escapeHtml ×6 (one divergent), byte formatter ×7, RecipientField ×3; small dead set |
| D6 | Consistency & conventions | **B-** | C+ | ▲ (sole finding refuted) | No dedicated finding survived; graded on verified cross-dim convention evidence (logging, lint-config, Bearer) |
| D7 | Testability & test quality | **C+** | C+ | = | authenticatedFetch has zero behavior tests anywhere; OfficeNaaStrategy untested; timing-assert scaffolding |
| D8 | Dependency hygiene | **B-** | B- | = (1 severity lowered) | No HIGH CVE asserted; SdapClient toolchain a major behind (unsupported ts-eslint6+TS5.9); Fluent floor spread |
| D9 | Observability | **C** | C | = | UPN in production console; 638 raw console.* bypass the designated logger; no correlation ID; no telemetry sink |
| D10 | ALM / build hygiene | **D+** | C- | ▼ (findings worsened) | ZERO blocking gates for 15 packages; no CI jest/eslint anywhere; npm ci abandoned, lockfiles non-authoritative |
| D11 | Knowledge/doc accuracy | **C+** | C+ | = | Front-door CLAUDE.md teaches a nonexistent API (StatusBadge/formatters/usePagination); ADR-012 accurate |

### Composed surface grade (rubric §4.2)

- Equal-weight mean of grade points: (2.7 + 3.7 + 3.3 + 3.7 + 2.7 + 2.7 + 2.3 + 2.7 + 2.0 + 1.3 + 2.3) / 11 = **2.67 → B-**
- Gating cap (non-waivable): min(B-, D2 = A-, D3 = B+) = **B-** — the cap is **not** binding (mean is already ≤ both gates).
- **Surface grade: B-**

---

## 1. Problem statement

The 15 shared packages are the client nucleus for every Spaarke surface — PCF controls, Code Pages, the SpaarkeAi workspace, Office add-ins, and the external SPA all compose from them. They grew package-by-package across ~15 projects with no workspace root, no shared lint/test enforcement, and no CI gate: each package resolves its own dependency tree from a lockfile CI never honors, runs tests CI never invokes, and (in 6 of 15 cases) has a "lint" script that is actually `tsc --noEmit`. The result is exactly what this assessment found: runtime code that is mostly excellent (the parts humans exercised hard — auth, streaming, sanitization) surrounded by unenforced hygiene that silently accumulates — duplicated helpers with divergent security behavior, a PII console log in the auth hot path, an untested token-attach wrapper with ~429 consumers, and a front-door doc teaching an API that does not exist.

**Evidence base**: 11-dimension parallel read-only fan-out + a Fable adversarial-verification pass over every claim. Verification refuted 4 claims (§3), lowered 2 severities, narrowed 1 scope, and **worsened** 2 findings (D10-03, D10-06) — all reflected here.

---

## 2. Current-state inventory (verified findings)

Every finding below is Fable-CONFIRMED. Columns: severity · anchor `file:line` (paths relative to `src/client/shared/` unless noted) · LOC estimate · effort (S ≤ ½ day / M 1–3 days / L multi-day) · risk of the remediation · remediation. Tranche assignment (A/B) per §5.

### D1 — Architecture & boundaries (B-)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D1-01 | HIGH | `Spaarke.AI.Widgets/src/providers/AiSessionProvider.tsx:48` | 8 | S | low | A | Declare `"@spaarke/auth": "file:../Spaarke.Auth"` (genuine **runtime** phantom — value imports in 6 files) and `"@spaarke/ai-context": "file:../Spaarke.AI.Context"` (type-only phantom) in AI.Widgets `package.json`. AI.Widgets is the **sole outlier** — 7 sibling packages all declare `@spaarke/auth` as a `file:` dep. |
| D1-03 | MEDIUM | `Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx:1` | 3,980 | L | med | B | Decompose ComposeWorkspace (3,980) + ComposeEditor (3,562) into focused hooks/subcomponents (state orchestration, toolbar, editor surface, persistence). Siblings also oversized: stepOperationInterceptor 1,685 (note: lives in `widgets/`, not `services/`), ComposeCommentGutter 1,253, ComposeFormatToolbar 1,105, ComposeAiToolbar 1,004. No ADR sanctions these sizes. |
| D1-05 | MEDIUM | `Spaarke.AI.Widgets/src/widgets/workspace/StructuredOutputStreamWidget.tsx:1` | 2,429 | L | med | B | Split streaming / section-render / dispatch into hooks + presentational subcomponents. Siblings: FilePreviewContextWidget 1,351, CreateAnalysisWizardWidget 1,272, RedlineViewerWidget 974, ExecutionTraceWidget 805. |
| D1-06 | LOW | `Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts:433` | 1,230 | M | med | B | Co-locate each widget's registration with its module (or group by feature); compose from a thin barrel; remove import-time global side effects (28 module-level register calls; the exported `registerWorkspaceWidgets()` is self-documented as a no-op). Real downstream cost proven: ComposeEditor deep-imports `@spaarke/ai-widgets/events` specifically to dodge the side effects. **Pre-check**: widget type strings (`'redline-viewer'` etc.) must keep matching server-emitted `widgetType` values when relocated (§4). |

### D2 — Correctness & reliability (A-)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D2-01 | LOW | `Spaarke.SdapClient/src/auth/TokenProvider.ts:14` | 190 | S | low | A | Delete the dead legacy `uploadFile`/`downloadFile`/`deleteFile`/`getFileMetadata` methods + the empty-string `TokenProvider` shim (retain only the authenticatedFetch-backed `indexFile`), **or** require an injected `AuthenticatedFetchFn` for all ops. Verification: **no consumer anywhere in the repo reaches the broken methods** (the finding's external-spa claim was falsified — DocumentUploadPage uses `createBffUploadService`); package is `"private": true`. Also fix `README.md:49-104`, which documents the broken methods as primary usage — any future consumer following it gets silent 401s. |
| D2-02 | LOW | `Spaarke.UI.Components/src/utils/adapters/bffNavigationServiceAdapter.ts:173` | 20 | S | low | A | The non-Xrm dialog-poll fallback resolves only inside a 250 ms `setInterval` gated on `dialogWindow.closed` — no timeout, no abort, no lifecycle cleanup → unbounded interval + never-settling Promise if the popup is abandoned. Add a bounded max-wait (resolve `{confirmed:false}` or reject) and/or an abort. No current in-repo caller, but it is the documented Power Pages SPA "minimal setup" path. |

### D3 — Security (B+)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D3-01 | MEDIUM | `Spaarke.SdapClient/src/auth/TokenProvider.ts:14` | 20 | S | low | A | Same code as D2-01, security framing: auth-by-omission on data-**mutation** paths (PUT upload, DELETE) — one import away from a live unauthenticated-mutation path in a published shared package. Delete the empty-token ops or route through injected `authenticatedFetch` and **fail closed** (throw) when no token is available. One production `new SdapApiClient` exists (`EntityCreationService.ts:34`) but calls only `indexFile` — latent, not live. |
| D3-02 | LOW | `Spaarke.UI.Components/src/services/document-upload/SdapApiClient.ts:106` | 8 | S | low | A | Inline `Authorization: Bearer ${token}` at :106/:139/:180/:277 bypasses the ADR-028 authenticatedFetch seam, with no `// Auth v2 (D-AUTH-7):` exception comment. The Bearer-template lint rule exists in **two** packages (Spaarke.Auth + Spaarke.Notifications `.eslintrc.json` — verification corrected "only Notifications") and nowhere else. Promote the rule to the shared eslint config; migrate this client onto authenticatedFetch. Token is functionally injected → convention gap, not a live hole. |
| D3-03 | LOW | `Spaarke.UI.Components/src/services/renderMarkdown.ts:285` | 5 | S | low | A | Raw-HTML anchors surviving DOMPurify can carry `target="_blank"` without `rel="noopener noreferrer"` (only markdown-link tokens get stamped). Add an `afterSanitizeAttributes` hook mirroring `sanitizeEmailHtml.hardenNode` (`utils/sanitizeEmailHtml.ts:235-238`). Limited to reverse-tabnabbing; `javascript:`/`data:` remain blocked. |

### D4 — Performance & scalability (A-)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D4-01 | LOW | `Spaarke.Events.Components/src/hooks/useEventsBulkActions.ts:256` | 8 | S | low-med | A | Bulk archive issues 2 sequential `updateRecord` calls per record (2N round-trips). Combine into one PATCH (`{sprk_eventstatus, statecode, statuscode}`). **Pre-check first** (§4): verify no `sprk_event` plugin step filtered on `sprk_eventstatus` depends on receiving two separate update messages. |
| D4-02 | LOW | `Spaarke.UI.Components/src/services/EventTypeService.ts:128` | 6 | S | low | A | Exported singleton constructed with `enableCache=false` → default path re-queries `sprk_eventtype` on every call. Enable the existing 5-min-TTL cache on the singleton (verified: no test or doc pins the no-cache default). |
| D4-03 | INFO | `Spaarke.UI.Components/src/services/ConfigurationService.ts:25` | 4 | S | low | — | Unbounded Maps in ConfigurationService / EventTypeService / `_navPropCache` — bounded by schema/config cardinality in practice. Optional LRU/sweep only if keying ever becomes user/record-cardinality. No action required. |

### D5 — DRY / dead code (B-)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D5-01 | MEDIUM | `Spaarke.UI.Components/src/components/EmailComposer/EmailComposer.tsx:226` | 30 | S | med | A | `escapeHtml` defined 6× across 3 packages; the EmailComposer.tsx copy **omits the single-quote escape** (divergent, weaker XSS variant) while its byte-identical siblings escape `&<>"'`. Promote one shared util; delete the five copies; the divergent copy must not silently under-escape. Cross-dim D3. |
| D5-02 | MEDIUM | `Spaarke.UI.Components/src/components/FileUpload/FileUploadZone.tsx:136` | 35 | S | low | A | Byte formatter defined 7× (4 byte-identical) despite the exported canonical `formatFileSize` (`RichFilePreview.tsx:391`, currently consumed only by its sibling dialog). Consolidate; delete six copies. |
| D5-03 | MEDIUM | `Spaarke.Visuals/src/components/GradeMetricCard.tsx:128` | 229 | S | low | A | Deprecated, superseded by MetricCardMatrix, **zero repo consumers** (barrel export + comments only). Delete file + barrel export; move the icon-map note to MetricCardMatrix. Verified safe: VisualHost dispatch is by numeric `sprk_visualtype` enum, never component-name string → no Dataverse check needed. |
| D5-04 | MEDIUM | `Spaarke.UI.Components/src/components/EmailComposer/subcomponents/RecipientField.tsx:1` | 400 | M | med | B | Three live near-identical copies (454 / 528 / 396 lines; WizardFollowOns header self-describes as "Ported from…"). Extract one entity-agnostic RecipientField (injected search callbacks); migrate the three call sites. |
| D5-05 | MEDIUM | `Spaarke.UI.Components/src/components/EmailStep/LookupField.tsx:1` | 300 | M | med | B | **Scope corrected by verification**: reconcile the EmailStep copy (380 lines, 111-line diff vs canonical) onto the exported canonical `LookupField` only. `RecordHeader/fields/LookupField.tsx` is a **different component** (read-only Xrm renderer sharing the name) — EXCLUDE it. CreateMatterWizard's 25-line wrapper is fine. |
| D5-06 | LOW | `Spaarke.UI.Components/src/components/CreateMatterWizard/handoffSeedMapping.ts:1` | 90 | M | low | B | Three wizard mappers duplicate the scaffolding (identical `firstString()` helper redefined 3×, identical confidence-gating). Factor shared key-normalization + gating helpers; keep only per-wizard field tables. |
| D5-07 | LOW | `Spaarke.UI.Components/src/hooks/useAiSummary.ts:567` | 13 | S | low | A | `enqueueIncomplete` deprecated no-op (console.warn body), zero callers repo-wide incl. built bundles. Remove from hook API + delete. |
| D5-08 | LOW | `Spaarke.Compose.Components/src/index.ts:167` | 6 | S | low | A | Deprecated `NdaReviewSummaryPanel(-Props)` aliases have zero code imports anywhere. Drop the aliases; fix the stale `PaneEventTypes.ts:1015` JSDoc (points at a renamed file) — cross-dim D11. |

### D6 — Consistency & conventions (B-)

No dedicated D6 finding survived verification (the sole first-pass claim was a placeholder refuted as anchored to a nonexistent file). D6 is graded on verified **cross-dimension** convention evidence, remediated in the owning dimensions: 638 raw `console.*` calls bypassing the designated logger (D9-02) and a divergent parallel logger implementation (D9-05) — "uniform logging" unmet; the ADR-028 Bearer convention lint-enforced in only 2 of 15 packages (D3-02); eslint 8-vs-9 + split-vs-meta packaging (D8-04) and 6 mislabeled `"lint": "tsc --noEmit"` scripts (D10-04) — non-uniform tooling conventions. No separate remediation rows; fixing the home-dimension items closes D6.

### D7 — Testability & test quality (C+)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D7-01 | HIGH | `Spaarke.Auth/src/authenticatedFetch.ts:27` | 114 | M | low | A | The live ADR-028 token-attach wrapper (Bearer attach :39, 401 clearCache+backoff retry :50-55, `auth_exhausted` :67, ProblemDetails :71-76) has **zero behavior tests** — verification confirmed every one of ~30 test files that touch it **mocks** it (`grep requireActual` = 0); the only direct assertion is `typeof === 'function'`. Add integration-style tests (real fetch double): Bearer attach from provider token; 401 → clearCache + retry → `AuthError('auth_exhausted')`; non-401 → `ApiError` with parsed ProblemDetails. Cross-dim D3. |
| D7-02 | MEDIUM | `Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts:1` | 418 | M | low | A | Live (constructed by both Outlook + Word add-ins via `AuthService.ts:78`) yet entirely untested while sibling BrowserMsalStrategy has a suite. Add strategy tests mirroring `BrowserMsalStrategy.test.ts`, doubling only the Office.js NAA boundary. |
| D7-03 | MEDIUM | `Spaarke.DailyBriefing.Components/test/DailyBriefingApp.smoke.test.tsx:180` | 400 | S–M | low | A | Two `describe.skip` suites still target the **removed** /narrate pipeline (R7 Wave 12 cutover, commit ad53af431). These are the only skips in the entire shared-lib suite. Rewrite against the current `/render` path (assert rendered bullet count == Σ channelNarratives bullets) or delete both files. |
| D7-04 | MEDIUM | `Spaarke.AI.Outputs/src/output-widgets/__tests__/BudgetDashboardWidget.test.tsx:29` | 300 | M | low | A | Systematic scaffolding: `performance.now() < 200` wall-clock asserts in 14 test files (13 in AI.Outputs + 1 StreamingInsertPlugin) — ADR-038 §5 clock-noise; `container.firstChild).toBeTruthy()` ×40 across 13 files incl. a mislabeled "renders loading spinner" test that never asserts a spinner. Delete timing asserts; replace render-only truthiness with content/behavior assertions. |
| D7-05 | LOW | `Spaarke.AI.Context/src/services/ChatApiClient.ts:1` | 227 | M | low-med | B | Package ships runtime code (ChatApiClient 227 LOC + 3 hooks) with **zero tests**, and verification proved every external import of `@spaarke/ai-context` is **type-only** (multiline grep for non-type imports = 0). Decide: if live, add behavior tests; if dead (evidence says yes), remove the runtime exports rather than test dead code. Cross-dim D5. |
| D7-06 | LOW | `Spaarke.AI.Widgets/src/__tests__/registration-contract-enforcement.test.ts:302` | 20 | S | low | A | `@ts-expect-error` fixture wrapped in a runtime `expect(typeof …)` — ADR-038 language-feature-redundancy scaffolding. Drop the runtime subtest (the compile-time check needs no Jest wrapper). Rest of the file is legitimate. |

### D8 — Dependency & supply-chain hygiene (B-)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D8-01 | LOW | `Spaarke.Visuals/package.json:46` | 0 | S | low | A | Visuals dev-pins React 18 (lock 18.3.1) vs 19.2.x everywhere else. Verification substantiated the deliberate-host-alignment reading: **VisualHost PCF is the only React-18 PCF and is Visuals' production host**. Remediation = **document the rationale** in the package (or notes) so the drift is recorded as intentional; do NOT blind-align to 19. |
| D8-02 | MEDIUM | `Spaarke.SdapClient/package.json:24` | 0 | M | low | A | Sole package on jest 29 / @types/node 18 / @typescript-eslint 6 / eslint 8 while declaring TS ^5.8.3 (lock 5.9.3) — ts-eslint 6 supports TS <5.4, an **unsupported pairing**. Bump to nucleus baseline (jest 30, @types/jest 30, @types/node 22, ts-eslint 8 / eslint 9). Manifest/lock are npm-ci-viable → staleness, not breakage. |
| D8-03 | LOW | `package.json:14` (repo root) | 0 | L | high | B | No `workspaces` field; all 15 packages resolve independent trees from their own lockfiles — structural root cause of the React/Fluent/toolchain drift and the D10-01 lockfile abandonment. Owner decision: adopt an npm/pnpm workspace at `src/client/shared` (one hoisted deduped graph, one lockfile) vs. document the linked-lib design as intentional. Sequence AFTER D10-01 groundwork. |
| D8-04 | LOW | `Spaarke.Auth/package.json:34` | 0 | M | low | B | eslint floors split ^8.57.0/^8.0.0 vs ^9.17.0 across packages; typescript-eslint packaged 3 ways (meta ^8.20 / split ^8.0 / split ^6.0). Standardize on eslint 9 + the `typescript-eslint` meta package (ideally one shared flat config package). Batch with D10-04. |
| D8-05 | LOW | `Spaarke.UI.Components/package.json:55` | 0 | M | low | B | `@fluentui/react-components` floors span three minor lines (9.46.2 / 9.66.10 / 9.73.2; icons 2.0.200→2.0.320) with independent resolution per package. Pin one agreed floor across all shared libs (trivial post-workspace; do as part of B2 either way). |
| D8-06 | LOW | `Spaarke.UI.Components/package.json:97` | 0 | S | low | A | `mammoth` declared as runtime dep in both UI.Components (^1.12.0, live via dynamic import in `useChatFileAttachment.ts:368`) and Compose (^1.8.0). Verification **strengthened** this: Compose has zero mammoth imports (its client reader was deliberately deleted per `docxBridge.ts:8-17`) → simply remove Compose's declaration. |

### D9 — Observability (C)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D9-01 | MEDIUM | `Spaarke.Auth/src/strategies/BrowserMsalStrategy.ts:166` | 6 | S | low | A | `console.info(... hint=${loginHint})` logs the user's **UPN/email** unguarded in production (same at `OfficeNaaStrategy.ts:207-211`); most surfaces do not strip console in prod builds, and the same file sets MSAL `piiLoggingEnabled:false` — the intent this violates. Redact: log `hint=${loginHint ? 'present' : 'none'}` (mirrors `resolveRuntimeConfig.ts:404-408` truncation discipline). Sink is the user's own browser console → MEDIUM, but this is the surface's one true PII-in-logs violation; fix first. |
| D9-02 | MEDIUM | `Spaarke.UI.Components/src/utils/logger.ts:54` | 638 | L | med | B | `createLogger`/`ISpaarkeLogger` has effectively **zero consumers repo-wide** (3 files, all internal) while 638 raw `console.*` calls across 163 files bypass it; no shared eslint config restricts console. Phased: (1) add `no-console` (warn) to the shared flat config with narrow allow for logger/AppInsights wrappers; (2) migrate services/hooks package-by-package; or explicitly delete `createLogger` if a different sink is chosen. Cross-dim D6. |
| D9-03 | MEDIUM | `Spaarke.SdapClient/src/SdapApiClient.ts:167` | 40 | M | med | B | Only sinks are AppInsightsService + `reportClientError`, wired solely into error boundaries + `safeRegister.ts`. Upload/download failures, token acquisition, Dataverse fetch, and AI streaming all emit console-only output that vanishes in production. Route critical-path failures through `reportClientError`/`trackException` (+ `trackEvent` milestones where cheap). |
| D9-04 | MEDIUM | `Spaarke.UI.Components/src/services/BffDataverseClient.ts:311` | 20 | M | med | B | No client-seeded correlation ID on any request (verified: zero `X-Correlation-Id`/`traceparent` **senders** in all of shared incl. @spaarke/auth); correlationId only read back from ProblemDetails errors. Generate per-request `crypto.randomUUID()` in the authenticatedFetch wrapper and send as `X-Correlation-Id`; log alongside client telemetry. **Sequence after D7-01** so the hot auth path is under test before modification. |
| D9-05 | LOW | `Spaarke.Visuals/src/utils/logger.ts:24` | 58 | S | low | A | Divergent parallel logger (unconditional info/warn/error vs UI.Components' dev-guarded levels; different method names/prefix), one consumer (ErrorBoundary.tsx). Consolidate on `@spaarke/ui-components` createLogger; delete the local copy; align dev-guard policy. Cross-dim D5/D6. |
| D9-06 | LOW | `Spaarke.UI.Components/src/hooks/useAiSummary.ts:251` | 25 | S | low | A | Unguarded `console.log` on hot streaming paths (incl. the full `docs` array at :494; SprkChat logs docId + result.url at :2014). Downgrade to dev-guarded `logDebug` or remove; never log full document/response objects in production. Opaque GUIDs/URLs, not PII → LOW. |

### D10 — ALM / build hygiene (D+)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D10-01 | HIGH | `.github/workflows/sdap-ci.yml:339` | 0 | L | med-high | B | `npm ci` invoked **nowhere** in 18 workflows; all installs are `npm install --legacy-peer-deps`; the orchestrator comment forbids `npm ci` because lockfiles drift; all 15 packages commit lockfiles that nothing enforces → builds are non-deterministic. Regenerate + pin the per-package lockfiles, then switch CI + `Build-AllClientComponents.ps1` to `npm ci`. **Coordinate**: `.github/workflows` edits are owned by `ci-cd-unit-test-remediation-r1`. |
| D10-02 | HIGH | `.github/workflows/sdap-ci.yml:433` | 0 | M | med | B | No workflow runs jest/`npm test`/`test:ci` for ANY client package (grep of all 18 workflows = zero, including .NET jobs, which use `dotnet test`) — **425 shared-lib test files provide zero CI signal**, and no consumer jest config reaches them indirectly. Add a blocking CI job running `npm run test:ci` per shared package (or a workspace aggregate post-D8-03). |
| D10-03 | HIGH | `.github/workflows/sdap-ci.yml:381` | 0 | M | med | B | ESLint runs only for `src/client/pcf`, and verification **strengthened** this: the containing `client-quality` job is `continue-on-error: true`, so even PCF lint is advisory. No Spaarke.* package is linted anywhere. Run `npm run lint` per shared package in a blocking job (`--max-warnings 0` once baseline is clean). |
| D10-04 | MEDIUM | `Spaarke.Visuals/package.json:17` | 0 | M | low | A | Exactly 6 of 15 packages (Visuals, Events, SmartTodo, Communication, DailyBriefing, LegalWorkspace) have **no eslint at all** — their `"lint"` is `tsc --noEmit`. Add eslint + the shared flat config; keep `tsc --noEmit` as a separate `typecheck` script. Cross-dim D6. |
| D10-05 | MEDIUM | `Spaarke.Events.Components/package.json:21` | 0 | S | low | A | Events.Components + SmartTodo.Components define no `test` script and no jest config — yet **both contain committed test files** (CalendarFilterPane.test.ts; SmartTodoWidget.test.tsx) that are uninvokable by any runner, direct or indirect. Add jest config + `test`/`test:ci` scripts. Cross-dim D7. |
| D10-06 | MEDIUM | `.github/workflows/sdap-ci.yml:297` | 0 | M | med | B | Verification **worsened** the first-pass claim: the 6-package typecheck chain lives in the `continue-on-error: true` `client-quality` job → **ZERO of 15 packages are typecheck-gated in blocking CI** (6 advisory, 9 nothing; standalone breaks surface only at deploy-time/nightly). Wire `Build-AllClientComponents.ps1` (or per-package builds) into a blocking job. |
| D10-07 | LOW | `Spaarke.SdapClient/package.json:28` | 0 | M | low | B | Toolchain drift across co-built packages (SdapClient eslint 8/ts-eslint 6/jest 29/TS 5.8 vs UI.Components eslint 9/jest 30/TS 5.3; Auth/Notifications on legacy `.eslintrc.json` — Auth's eslint floor is at line **34**, 2-line drift from evidence). Converge on one TS, one Jest major, one ESLint major + flat config. Overlaps D8-02/D8-04 — execute as one batch. |
| D10-08 | LOW | `Spaarke.Communication.Components/package.json:22` | 0 | S | low-med | B | `prebuild`/`prelint` silently rebuild `../Spaarke.Auth` + `../Spaarke.UI.Components` dist — non-hermetic single-package builds; orchestrator documents it as intentional "standalone safety" (so: redundant rebuilds, not a live race). Remove once orchestrator/workspace topological ordering is guaranteed (sequence with D8-03/D10-01). |

### D11 — Knowledge/doc accuracy (C+)

| ID | Sev | Anchor | LOC | Effort | Risk | Tranche | Remediation |
|---|---|---|---:|---|---|---|---|
| D11-01 | MEDIUM | `src/client/shared/CLAUDE.md:84` | 45 | S | low | A | Front-door doc presents a **StatusBadge** component (structure diagram, full props/impl example, barrel-export snippet, test example) that does not exist anywhere in the 15-package surface (only an unrelated local helper + a copy of the same example in `src/client/pcf/CLAUDE.md:59`). Rewrite the doc; also fix the pcf CLAUDE.md copy when touched. |
| D11-02 | MEDIUM | `src/client/shared/CLAUDE.md:124` | 30 | S | low | A | Documents `utils/formatters.ts` + the import `{ DataGrid, StatusBadge, formatters }` — neither `formatters` nor `StatusBadge` resolves from the barrel (zero case-insensitive matches in UI.Components/src). Remove the section; correct the import example to real symbols. |
| D11-03 | LOW | `src/client/shared/CLAUDE.md:156` | 20 | S | low | A | Documents a `usePagination` hook that does not exist (hooks/ has exactly 25 hooks, none named that). Remove or replace with a real hook. |
| D11-04 | LOW | `src/client/shared/CLAUDE.md:3` | 0 | S | low | A | Doc (Last Updated Dec 3 2025) frames the surface as ONE package with ~2 components; reality is 15 packages / 72 component folders, accurately described by ADR-012 (amended 2026-07-12). Refresh to the multi-package reality; defer inventory to ADR-012. D11-01..04 are **one rewrite task** on one file. |
| D11-05 | LOW | `docs/guides/SHARED-UI-COMPONENTS-GUIDE.md:15` | 0 | S | low | A | States Code Pages use React 18, contradicting ADR-012:11 ("React 19"), `react-versioning.md:17`, the code (react ^19.2.6), and even its own line 75. Update to React 19. |
| D11-06 | LOW | `.claude/constraints/react-versioning.md:16` | 0 | S | low | A† | Prescribes `@spaarke/ui-components/src/pcf-safe` — a form **zero** production consumers use (sole consumer imports `/dist/pcf-safe`; the src form appears only in pcf-safe.ts's own header). Document both forms (src for source-consuming PCFs, dist for built). †`.claude/` path → **main-session write only** (root CLAUDE.md §3). |
| D11-07 | INFO | `.claude/adr/ADR-012-shared-components.md:197` | 0 | S | low | A† | "Hooks (18)" is a hard under-count (actual 25; services/types satisfy their "+"). Update counts or defer to the full ADR. †main-session write only. |

---

## 3. Refuted by verification (do NOT act on) + explicit KEEPs

Record-only appendix so future passes don't re-claim these. **None of these are findings; none drove grades; none may generate remediation items.**

| First-pass claim | Refutation |
|---|---|
| **D1-02** — "ADR-030 PaneEventBus contract trapped inside feature package AI.Widgets; deep subpath import is accidental coupling" | The placement is the **documented, ADR-sanctioned architecture**: `docs/adr/ADR-030-pane-event-bus.md:21` mandates exactly this home per ADR-012's placement rules; the cross-package graph and cycle-avoidance are engineered (`PaneEventTypes.ts:38-49`); the deep import is a documented deliberate choice (`ComposeEditor.tsx:165-169`, task 093). Moving the contract would contradict accepted ADRs. |
| **D1-04** — "SprkChat (3,079 LOC) in the context-agnostic nucleus contradicts ADR-012" | ADR-012 itself **enumerates SprkChat as a member** of Spaarke.UI.Components (full ADR structure diagram :264; concise inventory table :180 with declared "Code Pages only" scope). A component cannot violate an ADR that lists it. A pure size/God-component claim could be re-filed without the ADR premise — note the size facts were verified (SprkChat.tsx 3,079; types.ts 2,306). |
| **D3-04** — "Code cites ADR-028 but no ADR-028 document exists" | `.claude/adr/ADR-028-spaarke-auth-architecture.md` exists (Accepted, 2026-05-19) and is exactly the cited token contract. Residual record-only nit (NOT a finding): its "Full version" link points at a `docs/adr/` file that is absent — a broken cross-link, not an undocumented security contract. |
| **D6-01** — "test" (placeholder) | Anchored to a nonexistent file (`a.ts` matches nothing in the repo); placeholder title/evidence with no real construct. Discarded entirely; D6 re-graded on cross-dimension evidence (§2 D6). |

**Explicit KEEPs (verified intentional — remediation MUST preserve):**
- **PaneEventBus in `@spaarke/ai-widgets`** + the `@spaarke/ai-widgets/events` deep-import seam (ADR-030/ADR-012 sanctioned; see D1-02 refutation).
- **SprkChat inside `@spaarke/ui-components`** with "Code Pages only" declared scope (ADR-012; see D1-04 refutation). D1-scale decomposition of oversized Compose/AI.Widgets files must not relocate SprkChat across packages.
- **Spaarke.Visuals React-18 dev pin** — matches its production host (VisualHost is the only React-18 PCF). Remediation is *documentation*, not migration (D8-01).
- **`safeRegisterWidget` hardening** in register-workspace-widgets.ts — deliberate brittleness guard; D1-06 restructures around it, does not remove the guard semantics.
- **ADR-012 export-stability alias pattern** generally — D5-08 deletes one specific alias pair with zero consumers; it does not license bulk alias removal.

---

## 4. Data-driven-dispatch pre-check list (NFR-08)

**No verified finding carries `requiresDataverseCheck=true`** — the mandatory Dataverse pre-check list is satisfied vacuously for this surface (client npm exports are not dispatched by `sprk_*` rows; D5-03's VisualHost dispatch was explicitly verified to key on a numeric enum, never a component-name string).

Two verification-noted pre-checks that remediation MUST still run before the touching task (recorded here so they are not lost):

| Finding | Pre-check (run BEFORE the edit) |
|---|---|
| **D4-01** (merge the two `updateRecord` calls) | Query plugin-step registrations on `sprk_event` **update**: confirm no step's filtering attributes on `sprk_eventstatus` depends on receiving two separate update messages (Dataverse check via `sdkmessageprocessingstep` for the sprk_event entity). If a filtered step exists, keep the split or adjust the step. |
| **D1-06** (relocate widget registrations) | The registry keys (`'redline-viewer'`, etc.) must continue to match **server-emitted `widgetType` values** exactly after relocation — grep the BFF widget-emission path and diff the string set before/after. Not a Dataverse row check, but the same not-grep-provable-dispatch class. |

---

## 5. Proposed workstreams → phases (A/B tranches per r3 NFR-04)

**Tranche A — low-contention bugs & hygiene first.** Package-local edits, deletions, test-only work, and doc fixes; near-zero collision risk with active worktrees. Small PRs off the r3 branch, `/conflict-check` each.

- **A1 — Security/PII quick fixes (do first)**: D9-01 (redact UPN — both strategies), D3-03 (noopener hook), D5-01 (escapeHtml consolidation — kills the under-escaping divergent copy), D3-02 (promote the Bearer lint rule to shared config; migrate document-upload SdapApiClient onto authenticatedFetch).
- **A2 — SdapClient + dead code**: D2-01/D3-01 as one task (delete legacy ops + TokenProvider shim + fix README), D2-02 (bounded dialog poll), D5-03 (GradeMetricCard), D5-07, D5-08, D8-06 (drop Compose mammoth).
- **A3 — Small perf + logging noise**: D4-01 (after its §4 pre-check), D4-02, D5-02 (formatFileSize), D9-05 (logger consolidation), D9-06 (dev-guard hot-path logs).
- **A4 — Test quality (test-only edits, zero prod contention)**: D7-01 (authenticatedFetch behavior tests — prerequisite for B4's D9-04), D7-02 (OfficeNaaStrategy), D7-03 (skipped /narrate suites), D7-04 (timing/truthiness scaffolding), D7-06.
- **A5 — Package-local hygiene**: D1-01 (declare the phantom deps — 8 LOC, do early), D10-04 (eslint into 6 packages), D10-05 (test runners for Events/SmartTodo), D8-02 (SdapClient toolchain bump), D8-01 (document Visuals React-18 rationale).
- **A6 — Docs**: one task rewriting `src/client/shared/CLAUDE.md` (D11-01/02/03/04), D11-05 (guide React 19), D11-06 + D11-07 (**main-session-only writes** — `.claude/` paths, root CLAUDE.md §3).

**Tranche B — wide/contested edits for a quiet window.** CI/workflow changes (coordinate with `ci-cd-unit-test-remediation-r1`, which owns `.github/workflows`), cross-package structural moves, and heavily-consumed UI components (EmailComposer/Compose/AI.Widgets files are touched by multiple active worktrees — `/conflict-check` is load-bearing here).

- **B1 — CI enforcement ladder** (highest program value; sequence within the phase): D10-06 (make typecheck blocking for all 15) → D10-02 (blocking jest job) → D10-03 (blocking eslint, `--max-warnings 0` once A5 lands) → D10-01 (lockfile regeneration + `npm ci` switch, incl. orchestrator).
- **B2 — Toolchain/dependency convergence**: D8-04 + D10-07 as one batch (eslint 9 + meta ts-eslint + flat config everywhere), D8-05 (single Fluent floor), D10-08 (drop prebuild sibling rebuilds once ordering guaranteed), then the **D8-03 workspaces decision** (owner call; structural root cause — do last, after B1 proves the lockfiles).
- **B3 — Component dedup (contested UI)**: D5-04 (RecipientField ×3 → one), D5-05 (EmailStep LookupField only — RecordHeader excluded per scope correction), D5-06 (handoffSeedMapping helpers).
- **B4 — Observability wiring**: D9-02 (phased no-console + createLogger adoption), D9-03 (telemetry on critical paths), D9-04 (correlation ID in authenticatedFetch — **requires A4/D7-01 tests first**).
- **B5 — God-module decomposition + AI.Context decision**: D1-03 (ComposeWorkspace/ComposeEditor), D1-05 (StructuredOutputStreamWidget), D1-06 (registration co-location, with its §4 pre-check), D7-05 (AI.Context: delete runtime exports or test them).
- **B6 — Wrap-up**: `/test-diet` gate (A4/B4 add tests → TEST-MODIFYING rigor override applies), doc-drift re-audit of `src/client/shared/CLAUDE.md`, SCORECARD delta note.

**Hot-path declaration** (parity with §G convention): `<bff>N</bff> <spaarkeai>N</spaarkeai> <ci-workflows>Y</ci-workflows> <skill-directives>N</skill-directives> <root-claude-md>N</root-claude-md>` — ci-workflows=Y for Tranche B1 only; coordinate with `ci-cd-unit-test-remediation-r1` before any workflow edit.

---

## 6. SCORECARD row inputs (for `notes/SCORECARD.md` — appended by the invoking task, NOT by this design)

**Row**: Shared client libs (Spaarke.*) — D1 **B-** · D2 **A-** · D3 **B+** · D4 **A-** · D5 **B-** · D6 **B-** · D7 **C+** · D8 **B-** · D9 **C** · D10 **D+** · D11 **C+** → mean 2.67 (B-) → gating cap min(B-, A-, B+) = **B-** (cap not binding) → **Surface grade: B-**

Evidence bullets (one per dimension):

- **D1 B-** — Two first-pass architectural claims refuted (PaneEventBus placement + SprkChat-in-nucleus both ADR-sanctioned); surviving debt: AI.Widgets phantom deps — sole outlier vs 7 siblings' `file:` deps (D1-01 HIGH, `AiSessionProvider.tsx:48`), God modules ComposeWorkspace 3,980 / ComposeEditor 3,562 / StructuredOutputStreamWidget 2,429 LOC (D1-03/05), 1,230-LOC import-time registration module (D1-06).
- **D2 A-** — Only two LOWs survive verification: SdapClient's empty-Bearer legacy methods are broken-by-construction but **unreachable in all production wiring** (D2-01, severity lowered; external-spa claim falsified), and an unbounded dialog-poll Promise in an export with no current in-repo caller (D2-02); sampled auth/streaming/poll paths deterministic + defensively edged.
- **D3 B+** — XSS boundary strong (hardened closed-allow-list sanitizer + DOMPurify); debt is latent, not live: auth-by-omission on SdapClient PUT/DELETE one import from live (D3-01, `TokenProvider.ts:14`), ADR-028 Bearer rule lint-enforced in only 2 of 15 packages (D3-02), missing rel=noopener stamping on raw-HTML anchors (D3-03).
- **D4 A-** — Async correctness + lazy loading exemplary; only 2N bulk-archive round-trips (D4-01), a no-cache default on the EventTypeService singleton (D4-02), and cardinality-bounded unbounded Maps (D4-03 INFO).
- **D5 B-** — Active copy-paste through the nucleus: escapeHtml ×6 with one under-escaping divergent copy (D5-01, `EmailComposer.tsx:226`), byte formatter ×7 despite an exported canonical (D5-02), RecipientField ×3 (D5-04), EmailStep LookupField copy (D5-05, scope corrected to exclude RecordHeader); dead set small (GradeMetricCard 229 LOC + two stubs).
- **D6 B-** — Sole first-pass claim refuted (placeholder on a nonexistent file); graded on verified cross-dimension convention evidence: 638 raw console.* vs the designated logger (D9-02), a divergent parallel logger (D9-05), Bearer convention unenforced in 13/15 packages (D3-02), eslint 8/9 + config-format split (D8-04) and 6 mislabeled lint scripts (D10-04).
- **D7 C+** — authenticatedFetch, the live ADR-028 token path, has **zero behavior tests anywhere** — every consumer mocks it, `requireActual` = 0 hits (D7-01 HIGH); OfficeNaaStrategy 418 LOC live via both Office add-ins yet untested (D7-02); two describe.skip suites still target the removed /narrate pipeline (D7-03); performance.now() timing asserts in 14 files + 40 render-only truthiness checks (D7-04).
- **D8 B-** — No HIGH CVE asserted (runtime deps resolve patched); consistency debt: SdapClient a toolchain major behind with an unsupported ts-eslint6+TS5.9 pairing (D8-02), Fluent floors span three minor lines (D8-05), 15 independent lockfiles with no workspaces (D8-03); Visuals' React-18 pin verified as deliberate host alignment (D8-01 lowered to LOW).
- **D9 C** — The user's UPN/email logged unguarded to production console in both auth strategies (D9-01, `BrowserMsalStrategy.ts:166`); structured logger effectively unadopted — 638 raw console.* across 163 files (D9-02); no telemetry sink on upload/auth/streaming critical paths (D9-03); zero client-seeded correlation headers repo-wide (D9-04).
- **D10 D+** — Verification worsened the first pass: **ZERO of 15 packages blocking-gated** (the 6-package tsc chain sits in a continue-on-error job, D10-06); no workflow ever runs a shared-package Jest suite — 425 test files give zero CI signal (D10-02); no shared package linted anywhere and even PCF lint is advisory (D10-03); npm ci abandoned repo-wide, committed lockfiles non-authoritative (D10-01).
- **D11 C+** — Front-door `src/client/shared/CLAUDE.md` actively teaches a nonexistent API — StatusBadge, `formatters`, usePagination, and an import example where 2 of 3 symbols don't resolve (D11-01/02/03) — and frames the 15-package surface as one package (D11-04); ADR-012 itself is accurate; secondary drift: React 18-vs-19 guide contradiction (D11-05), pcf-safe import-path form (D11-06).

---

## 7. Risks & coordination

| Risk | Mitigation |
|---|---|
| `.github/workflows` ownership collision (Tranche B1) | `ci-cd-unit-test-remediation-r1` owns workflow edits — coordinate before any B1 PR; land as their-repo-pattern-conformant changes |
| Shared UI files touched by many active worktrees (EmailComposer, Compose, AI.Widgets) | Tranche B split exists for exactly this; `/conflict-check` before every PR against `projects/INDEX.md` |
| Widget-registry relocation breaks server-driven widget dispatch (D1-06) | §4 pre-check: diff registry key set vs server-emitted `widgetType` values before/after |
| Merging the two archive updates trips a filtered plugin step (D4-01) | §4 pre-check on `sprk_event` `sdkmessageprocessingstep` filtering attributes |
| Correlation-ID change destabilizes the untested auth hot path (D9-04) | Hard sequencing: D7-01 behavior tests (A4) land first |
| Lockfile regeneration churns transitive versions (D10-01) | Regenerate per-package in isolated PRs; diff resolved-version deltas; only then flip CI to `npm ci` |
| Workspaces adoption (D8-03) invalidates per-package build assumptions | Owner-gated; do LAST after B1 proves deterministic per-package installs; treat as its own mini-design |
| `.claude/` doc fixes attempted by sub-agents (D11-06/07) | Main-session-only writes per root CLAUDE.md §3 — tag the tasks accordingly |

---

*Produced by the r3 quality-assessment workflow (synthesis stage) on 2026-08-14. Read-only assessment; this file is the sole artifact. The SCORECARD row is appended by the invoking task from §6.*
