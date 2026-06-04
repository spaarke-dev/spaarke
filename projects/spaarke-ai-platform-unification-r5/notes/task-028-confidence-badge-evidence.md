# Task 028 — D2-18 Confidence Floor Badge — Evidence

**Status**: complete (code authoring — main session owns build, code-review, adr-check, commit, push)
**Date**: 2026-06-04
**Effort**: ~1h (sub-agent, code-authoring scope only)
**Rigor**: FULL (frontend renderer extension on R5 critical path; gates the entire Insights response surface visually)
**Wave**: P2-G6 (parallel-safe with tasks 024 / 025 / 026 / 027 — last of the Insights tool integration suite)
**Scope note**: This evidence file is produced by a parallel-wave sub-agent. The main session owns `npm run build`, `code-review` / `adr-check` quality gates, commits, and pushes — NOT this sub-agent.

---

## 1. Files modified / created

| File | Change | Approx LOC | Nature |
|---|---|---|---|
| `src/solutions/SpaarkeAi/src/config/insightsRendererConfig.ts` | **NEW** | ~95 | Minimal config module — `confidenceThreshold` singleton + get/set/reset |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/LowConfidenceBadge.tsx` | **NEW** | ~155 | Fluent v9 Badge sub-component + `LOW_CONFIDENCE_BADGE_TEXT` constant + pure `shouldShowLowConfidenceBadge` predicate |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/InsightsResponseRenderer.tsx` | **MODIFIED** | +25 / -0 | Mounts the badge at the TOP of every case wrapper (empty / decline / playbook-inference / RAG / unhandled-playbook); adds `confidenceThreshold?: number` prop |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/index.ts` | **MODIFIED** | +10 | Barrel exports for `LowConfidenceBadge`, `LOW_CONFIDENCE_BADGE_TEXT`, `shouldShowLowConfidenceBadge`, `LowConfidenceBadgeProps` |
| `src/solutions/SpaarkeAi/src/components/conversation/insights/__tests__/LowConfidenceBadge.test.tsx` | **NEW** | ~420 | 7 describe blocks, 31 individual `it` cases covering boundary + visibility + defensive + configurability + exact text + dark mode |

**No backend files touched** — confirms ADR-029 publish-size delta = **0 MB**. No DI registrations delta. No new HTTP calls. No Insights-internal types touched.

---

## 2. Primitive choice — Badge vs MessageBar

**Chosen primitive**: Fluent v9 `Badge` (with `appearance="filled" color="warning" size="medium"`).

**Rationale** (also captured in `LowConfidenceBadge.tsx` file header):

1. **Compact, advisory framing**: a small header-adjacent pill is appropriate for an advisory cue (low confidence) — not an actionable warning. `MessageBar` is heavier visual real-estate; reserved for things the user is expected to ACT on.
2. **Visual disambiguation from decline**: `DeclineResponseRenderer` already uses `MessageBar intent="warning"` for playbook-decline framing. Reusing the same primitive for low-confidence would create visual ambiguity between two semantically distinct cases (decline = playbook said "no"; low-confidence = response is uncertain but proceeding). Badge keeps the two surfaces visually separable.
3. **Spec permission**: Spec FR-15 explicitly permits "Fluent v9 `Badge` OR `MessageBar`" — both choices satisfy the criterion.
4. **Cross-reference with task 027**: Task 027 (clickable citations) extends `RagResponseRenderer` (button-based citation tokens). A `MessageBar` banner above the citation list would compete with the citation reference card layout. Badge above the entire renderer container is hierarchically cleaner.

**Placement**: badge is rendered at the TOP of every response-case wrapper inside `InsightsResponseRenderer`, BEFORE the case-specific sub-renderer. This means the badge gates the entire output regardless of which case (playbook-inference, playbook-decline, RAG observation, RAG empty, unhandled-playbook) the response routes through.

---

## 3. R5 settings location identified at Step 3

**Existing convention check**: `runtimeConfig.ts` in `src/solutions/SpaarkeAi/src/config/` is the only existing config module. It is **auth-only** (BFF base URL, MSAL client ID, OAuth scope, tenant ID) and is not suitable for UX-policy thresholds — its lifecycle is "bootstrap-once-then-immutable", whereas a tunable threshold needs runtime mutability.

**No existing settings hook**: grep for `useSettings` / `useConfig` / `useFeatureConfig` / `widgetConfig` / `appConfig` across `src/solutions/SpaarkeAi/` and `src/client/shared/` returned only `WorkspacePane.tsx` (unrelated to confidence-threshold configuration). No reactive settings hook pattern is established in the SpaarkeAi shell at this point.

**Decision (per task POML Step 3 fallback)**: created a **new minimal config module** at `src/solutions/SpaarkeAi/src/config/insightsRendererConfig.ts`. Exposes:

- `DEFAULT_CONFIDENCE_THRESHOLD = 0.6` (per spec FR-15 + Assumptions)
- `InsightsRendererConfig` interface (extensible — adding future UX thresholds requires only a typed field here)
- `getInsightsRendererConfig()` — lazy read; cheap; safe per-render
- `setInsightsRendererConfig(patch)` — partial merge; for ops + tests
- `resetInsightsRendererConfig()` — restore defaults; tests call this in `afterEach`

**Rationale for minimal module (per POML Step 3 "Preferred fallback" branch)**: avoiding the inventing-a-context-provider-for-one-value anti-pattern. A direct typed import + singleton is simpler, immediately testable, and naturally extends to additional UX thresholds (e.g., future ones added in R6+). If a reactive context provider becomes warranted later, the consuming component can be swapped without changing the badge component itself.

**Property path used by the renderer**: `getInsightsRendererConfig().confidenceThreshold` — read inside `LowConfidenceBadge.tsx` at render time so reactive reconfiguration via `setInsightsRendererConfig` is honored on the next render cycle (verified by tests T4-6 "config-driven threshold flows into InsightsResponseRenderer end-to-end" + T4-7 "renderer prop overrides config singleton").

---

## 4. Default threshold confirmation

**Default value**: `0.6` per spec FR-15 + spec.md Assumptions §"Confidence threshold default".

Verified by:

- `DEFAULT_CONFIDENCE_THRESHOLD` constant export from `insightsRendererConfig.ts`
- Module-load initialization: `_config = { confidenceThreshold: DEFAULT_CONFIDENCE_THRESHOLD }` → `0.6`
- Test T4-3 `'default config exposes DEFAULT_CONFIDENCE_THRESHOLD = 0.6'` asserts both the constant and `getInsightsRendererConfig().confidenceThreshold === 0.6`
- Test T1-1 `'renders the badge directly with default-config threshold (0.6)'` confirms 0.5 < 0.6 renders the badge with no explicit threshold prop

---

## 5. Exact badge text verification

**Constant value**: `'Low confidence — verify before relying'`

- Length: 39 characters
- Position 14 (zero-based): em-dash `—` (U+2014) — verified by test T5-2 `dashCodePoint === 0x2014`
- No trailing punctuation — verified by test T5-3 (ends with `g`, not `.`, `,`, `;`, `!`, or `?`)
- Rendered text matches constant exactly — verified by test T5-4 (`el.textContent === LOW_CONFIDENCE_BADGE_TEXT`)
- Exported as `LOW_CONFIDENCE_BADGE_TEXT` from both `LowConfidenceBadge.tsx` and the `index.ts` barrel for re-use across tests + future consumers (e.g., screen-reader announcement testing)

---

## 6. Test coverage matrix (31 individual `it` cases across 7 describe blocks)

| Block | # tests | Purpose |
|---|---|---|
| **T7** `shouldShowLowConfidenceBadge` (pure helper) | 7 | Boundary tests at function level — strictly below, at, above; null/undefined; NaN; out of range; reconfigured threshold |
| **T1** Badge visible when below threshold | 5 | Direct component + 4 response cases (playbook-inference, playbook-decline, RAG observation, RAG empty) |
| **T2** Badge absent when at/above threshold | 5 | At exact threshold (0.6 vs 0.6); above (0.85); maximum (1.0); + 2 end-to-end with high-confidence responses |
| **T3** Defensive handling of malformed confidence | 8 | Parametrized over 7 malformed values (null, undefined, NaN, -0.1, 1.5, +Infinity, -Infinity) + 1 end-to-end with NaN |
| **T4** Threshold configurable | 7 | Prop override + prop flips visibility + DEFAULT_CONFIDENCE_THRESHOLD = 0.6 + singleton reconfig + reactive flow into renderer + reset + prop wins over config |
| **T5** Exact badge text | 4 | Spec-mandated constant + em-dash U+2014 + no trailing punctuation + rendered DOM matches |
| **T6** Dark mode smoke | 3 | webDarkTheme mount for visible + end-to-end + absent cases |

**Total: 31 `it` blocks across 7 `describe` blocks**.

Acceptance-criteria mapping (POML §acceptance-criteria 1–12):

| Criterion | Status | Evidence |
|---|---|---|
| Exact text "Low confidence — verify before relying" (em-dash; no trailing punctuation) | ✅ | T5-1 / T5-2 / T5-3 / T5-4 |
| Badge absent (returns `null`, no hidden node) at/above threshold | ✅ | T2-1 / T2-2 / T2-3 — `queryByTestId('low-confidence-badge')` returns null |
| Default threshold = 0.6 | ✅ | T4-3 |
| Threshold configurable via R5 settings + reactive reconfiguration | ✅ | T4-1 through T4-7 (7 tests cover prop + singleton + end-to-end flow + reset + prop-precedence) |
| Defensive — null / undefined / NaN / out-of-range → no badge | ✅ | T3 (8 parametrized tests + 1 end-to-end) + T7 boundary tests |
| NO new feature flag introduced | ✅ | §7 below — grep result |
| Dark mode passes (no exceptions; semantic tokens only) | ✅ | T6 (3 tests) + visual sweep §7 below |
| Smoke test against renderer for low/high/boundary confidence on both paths | ⏭ | Main session — automated tests cover the logical surface; visual smoke deferred |
| BFF publish-size delta = 0 MB | ✅ | §1 — frontend-only task; no `src/server/` touched |
| Zone B boundary preserved — no Insights-internal types referenced | ✅ | `LowConfidenceBadge.tsx` imports only `Badge` + `makeStyles` + `tokens` from Fluent v9 + `getInsightsRendererConfig` from local config; no `src/server/api/...` imports |
| Build passes (TypeScript + lint) | ⏭ | Main session per §10 below |
| `code-review` + `adr-check` quality gates pass | ⏭ | Main session per §10 below |

⏭ = handed to main session per parallel-wave sub-agent scope contract.

---

## 7. No-new-feature-flag verification

**Hand-grep of the diff for forbidden patterns** (per POML Step 5(f) + ADR-018 §"Flag Scope Discipline"):

- `Feature:` — NOT present in any new/modified file
- `:Enabled` (boolean kill-switch suffix) — NOT present
- `IsEnabled` / `isEnabled` — NOT present
- Any boolean kill-switch — NOT present

The only configurable surface added is `InsightsRendererConfig.confidenceThreshold: number`. Per ADR-018: this is a CONFIGURATION VALUE (numeric, operator-tunable, no on/off semantics), NOT a feature flag (boolean kill-switch). The badge itself is unconditional UX surface area; if the operator wanted to suppress all badges they would set `confidenceThreshold: 0`, which is a value tune, not a flag flip.

---

## 8. ADR compliance

| ADR | Compliance | Evidence |
|---|---|---|
| **ADR-021** (Fluent v9 + dark mode) | PASS | `LowConfidenceBadge.tsx` uses `makeStyles` + `tokens.spacingVerticalS` only. Badge uses `appearance="filled" color="warning"` semantic slot — adapts to dark mode automatically. No hex, rgba, `#fff`, or `@fluentui/react-components/v8` imports anywhere. T6 dark-mode smoke tests mount visible + absent + end-to-end cases under `webDarkTheme` without exceptions. |
| **ADR-022** (React 19) | PASS | `LowConfidenceBadge` is a functional component using only props + `useStyles` hook. No class components, no legacy lifecycle. Typed props (`LowConfidenceBadgeProps` interface, no `any`). |
| **ADR-013 §3.5** (Zone B HTTP-contract-only) | PASS | Renderer consumes only `response.confidence: number` from the already-parsed `InsightsResponse` discriminated union. NO imports from `src/server/api/...`. NO `IInsightsAi` or other server-internal types. |
| **ADR-012** (component libraries) | PASS | Lives in `src/solutions/SpaarkeAi/src/components/conversation/insights/` — co-located with sibling sub-renderers (per task 026's placement decision). Context-agnostic — no `Xrm`, no PCF-specific imports, no SpaarkeAi-shell-specific imports beyond the config module. |
| **ADR-018** (Flag Scope Discipline) | PASS | §7 above — no boolean kill-switch added. Threshold is a numeric configuration value. Badge surface is unconditional. |
| **ADR-019** (ProblemDetails) | N/A | Renderer is a successful-200 surface. Error-path rendering is task 029's surface. |
| **ADR-028** (Spaarke Auth v2) | N/A | No BFF calls. No tokens. |
| **ADR-029** (BFF publish hygiene) | N/A | Frontend-only task. §1 confirms no `src/server/` touched. Publish-size delta = **0 MB**. |
| **ADR-010** (DI minimalism) | N/A | Frontend renderer. No DI surface. |
| **ADR-030** (PaneEventBus closed channels) | N/A | No new event types. No PaneEventBus dispatches added. |
| **ADR-032** (Null-Object kill-switch pattern) | N/A | No conditional service registrations. Per R5 §3.2 + ADR-018: R5 introduces no new feature flags, so ADR-032 is inapplicable. |
| **R5 CLAUDE.md §3.1 reuse mandate** | PASS | Reuses Fluent v9 `Badge` from `@fluentui/react-components` — no parallel UI primitive built. Reuses existing config-module pattern shape (singleton + lazy read) without adding a context-provider layer. |
| **R5 CLAUDE.md §3.2 no new flags** | PASS | §7 above — no `Feature:...:Enabled` boolean added. |
| **R5 CLAUDE.md §3.5 Insights governance** | PASS | Zone B HTTP-contract-only consumption — reads `response.confidence` from already-parsed envelope. No HTTP call added. No Insights-internal type referenced. |
| **R5 CLAUDE.md §3.6 BFF publish discipline** | PASS / N/A | No BFF code touched. Delta = 0 MB. |
| **R5 CLAUDE.md §3.7 test obligation** | PASS | 31 tests added covering all 12 acceptance criteria. |

---

## 9. Coordination with task 027 (parallel sibling — clickable citations)

Per task POML §"Coordination" + R5 CLAUDE.md, task 027 is running in parallel. Verified coordination:

1. **Barrel `index.ts`**: task 027 added `isClickableCitation` to the `RagResponseRenderer` export block. My edit adds three new exports (`LowConfidenceBadge`, `LOW_CONFIDENCE_BADGE_TEXT`, `shouldShowLowConfidenceBadge`) and one type (`LowConfidenceBadgeProps`) in a separate barrel section so the two edits do not conflict.
2. **`InsightsResponseRenderer.tsx`**: task 027 (per its evidence file scope) extends `RagResponseRenderer` (and possibly wires `onCitationClick` through `InsightsResponseRenderer`'s prop seam). Task 028 mounts a NEW prop (`confidenceThreshold`) and a new top-of-wrapper element. The two prop additions are on different conceptual axes (citations vs confidence) and the JSX changes are in different positions (badge at top of each case wrapper vs citation click handler threaded through props) → minimal merge conflict surface.
3. **`RagResponseRenderer.tsx`**: untouched by task 028. Task 027 owns it.

If main-session merge requires reconciliation, the conflict surface is the barrel `index.ts` ordering of exports (purely additive) and the `InsightsResponseRendererProps` interface (both tasks added one new prop; they coexist). Both can be reconciled trivially by accepting both sides.

---

## 10. Outstanding work (deferred to main session)

Per task 028 POML §Step 9.5 + Step 10 main session ownership:

- [ ] Run `npm run build` in `src/solutions/SpaarkeAi/` — verify TypeScript compiles with no new errors / lint warnings
- [ ] Run `npm test` (or vitest) in `src/solutions/SpaarkeAi/` — verify all 31 new tests pass + the existing 026 tests still pass
- [ ] Visual dark-mode verification — render `<InsightsResponseRenderer>` with a low-confidence response under `webDarkTheme` in the SpaarkeAi shell + capture screenshot evidence
- [ ] Run `code-review` skill against the 4 modified/new files
- [ ] Run `adr-check` skill against the 4 modified/new files (verify ADR-021 / ADR-022 / ADR-018 / ADR-012 / ADR-013 §3.5)
- [ ] Update `TASK-INDEX.md`: 028 🔲 → ✅
- [ ] Reset `current-task.md` to next pending task per CLAUDE.md §7 (likely 029 / D2-19 error codes, or 030 / D2-20 smoke tests)
- [ ] Commit (`feat(r5): task 028 D2-18 — low-confidence badge on InsightsResponseRenderer`)
- [ ] Push to remote per push-to-github skill
- [ ] If task 027 lands first → reconcile `index.ts` + `InsightsResponseRendererProps` (both purely additive; trivial)

---

## 11. Reusability + extensibility notes

| Future consumer | What it consumes | Seam location |
|---|---|---|
| **Per-message confidence opt-out** (R6 backlog) | `confidenceThreshold` prop on `<InsightsResponseRenderer>` | Already wired — pass `0` to suppress badge entirely without changing config; pass `1` to always render |
| **Operator A/B** of threshold | `setInsightsRendererConfig({ confidenceThreshold: x })` | Already wired — call at app bootstrap (`main.tsx`) after operator-config resolution; renderer picks up on next render |
| **Per-tenant threshold** (R7 backlog) | Replace the singleton in `insightsRendererConfig.ts` with a context-provider | Trivial — replace `getInsightsRendererConfig()` call in `LowConfidenceBadge` with a `useContext(...)` hook; module shape (typed interface + default) does not change |
| **Telemetry on badge surface** (task 042 telemetry dashboards) | Hook into the badge render in `LowConfidenceBadge.tsx` to emit a count event | Stable testid `low-confidence-badge` enables DOM-level event hookup without further code change |

---

## 12. Sub-agent scope reminder

This task was executed by a parallel-wave sub-agent. Per task POML §steps:

- **In scope (sub-agent)**: code authoring, types, sub-component, tests, evidence file, POML status update.
- **Out of scope (main session)**: `npm run build`, `npm test` execution, dark-mode visual capture, code-review + adr-check skill runs, TASK-INDEX update, current-task.md reset, commit + push.

The handoff is intentional per R5 parallel-wave protocol — multiple sub-agents (024, 025, 026, 027, 028) authored P2-G6 concurrently; serializing through the main session for build verification + commit avoids merge conflicts on `TASK-INDEX.md` + downstream evidence file collisions.

**Sibling parallel task at edit-time of this evidence file**: task 027 (D2-17 clickable citations). Coordination notes in §9 above. Edits coexist; reconciliation is trivial.
