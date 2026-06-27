# Project Plan: Daily Briefing — SpaarkeAi Pattern D Migration (R2)

> **Last Updated**: 2026-06-18
> **Status**: Ready for Tasks
> **Spec**: [spec.md](spec.md) | **Design**: [design.md](design.md)

---

## 1. Executive Summary

**Purpose**: Migrate the Daily Briefing widget to canonical Pattern D dual-use, fix 4 verified consumer-layer/prompt defects, and de-duplicate 3 high-proximity files — without touching the verified-healthy producer layer.

**Scope**:
- One shared component package `@spaarke/daily-briefing-components` consumed by standalone + SpaarkeAi
- `useNotificationData` decomposed into 3 single-responsibility hooks
- Hybrid aggregation UX (narrative + per-item sub-list)
- BFF `/narrate` prompt + server-side `primaryEntityId` validation
- `appnotification.data.actions[]` for MDA native bell deep-links
- DD: `MicrosoftToDoIcon`, `authInit`, `runtimeConfig` hoisted

**Estimated Effort**: 30–45 POML tasks across 6 workstreams; estimated 10–14 implementation days assuming parallel-group execution.

---

## 2. Architecture Context

### Design Constraints

**From ADRs** (must comply):

- **ADR-001** — BFF Minimal API: P2b (`/narrate`) and P3 (`CreateNotificationNodeExecutor`) stay inside `Sprk.Bff.Api`; no Azure Functions, no separate services.
- **ADR-006** — Code Page pattern: standalone Daily Briefing remains Vite + React 19 + Fluent v9 Code Page; not converted to PCF.
- **ADR-008** — Endpoint-filter auth: `/narrate` modifications follow existing endpoint-filter convention.
- **ADR-010** — DI minimalism: no new BFF DI registrations needed.
- **ADR-012** — Shared components: Pattern D dual-use canonical shape; new package per Calendar + Smart Todo precedent.
- **ADR-013** — AI features extend BFF: `/narrate` change stays in BFF; no new AI endpoint.
- **ADR-021** — Fluent v9 exclusively; semantic tokens; dark mode required.
- **ADR-024** — Multi-entity regarding resolution: `TODO_REGARDING_CATALOG` preserved in `useInlineTodoCreate` during hoist.
- **ADR-026** — Code Page build standard: Vite + `vite-plugin-singlefile` + React 19 (for the standalone shell post-shrink).
- **ADR-027** — Subscription isolation: `appnotification` is CORE; no new schema.
- **ADR-028** — Auth contract: `@spaarke/auth` is the canonical entry point; new `createCodePageAuthInitializer` factory standardizes consumption.

**From Spec**:
- Producer layer is verified healthy and out of scope (explicitly preserved)
- Standalone Daily Briefing code page MUST be preserved as working surface (not retired)
- BFF publish-size delta ≤ +1 MB compressed (NFR-04)
- BFF baseline: ~45.65 MB per 2026-05-26 §10 NFR-01 measurement
- No new HIGH-severity CVEs after BFF changes (NFR-06)
- All hoist + DD PRs MUST pass `code-review` + `adr-check` at Step 9.5 (FULL rigor)

### Key Technical Decisions

| Decision | Rationale | Impact |
|----------|-----------|--------|
| New package `@spaarke/daily-briefing-components` (not extend `@spaarke/ui-components`) | Calendar + Smart Todo precedent; Spaarke convention is one package per dual-use widget | New `src/client/shared/Spaarke.DailyBriefing.Components/` directory; new `package.json` mirroring `@spaarke/events-components` shape |
| Split `useNotificationData` into `useBriefingNotifications` + `useBriefingPreferences` + `useBriefingActions` | Single Responsibility; independent cache lifetimes; re-render isolation | 3 export entries from `./hooks` subpath; consumer-layer effect coordinates refetches on preferences change |
| Server-side validation of returned `primaryEntityId` against supplied `regardingId` set | Defense in depth; prevents dead links across LLM regressions | New validation pass in `DailyBriefingEndpoints.ParseChannelBullets` post-processing; logs warning on mismatch |
| Per-item sub-row links use supplied `regardingId` (no AI involvement) | Deterministic; eliminates entire class of dead-link defects | `NarrativeBullet` reads `regardingId` from item directly; no LLM-derived field used for sub-row links |
| `data.actions[]` only populated when `toasttype` is visible | Hidden-toast notifications have no bell surface; population is unnecessary noise | `CreateNotificationNodeExecutor` conditional on `toasttype != Hidden` |
| Effect-based cross-hook coordination (Option A) at consumer layer | Idiomatic React; explicit; traceable; no hidden coupling | `DailyBriefingApp` runs `useEffect([preferences.disabledChannels], () => refetch())` |
| Relaxed FR-25/NFR-10 byte-stability | Owner decision; avoids blocking improvements on standalone parity | Document any non-trivial visual deviation in PR |

### Discovered Resources

**Applicable Skills** (auto-discovered for task-execute Step 0):
- `.claude/skills/task-execute/` — load knowledge files + run quality gates at Step 9.5
- `.claude/skills/code-review/` — required quality gate (FULL rigor)
- `.claude/skills/adr-check/` — required quality gate (FULL rigor)
- `.claude/skills/fluent-v9-component/` — invoke for hoisted Fluent v9 component tasks (P2)
- `.claude/skills/bff-deploy/` — deploy P2b + P3 BFF changes
- `.claude/skills/code-page-deploy/` — redeploy standalone Daily Briefing after thin-shell migration
- `.claude/skills/push-to-github/` — commit per Spaarke git conventions
- `.claude/skills/merge-to-master/` — final merge with safety checks

**Binding Constraints**:
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — Sections A (MUST checklist), F (test update obligation), F.1 (asymmetric-registration anti-pattern), F.2 (fixture-config-first inspection), F.3 (empirical reproduction)
- [`.claude/patterns/ui/fluent-v9-component-authoring.md`](../../.claude/patterns/ui/fluent-v9-component-authoring.md) — Griffel, semantic tokens, `mergeClasses`
- [`.claude/patterns/ui/fluent-v9-theming.md`](../../.claude/patterns/ui/fluent-v9-theming.md) — FluentProvider, dark mode
- [`.claude/patterns/ui/fluent-v9-host-visual-fit.md`](../../.claude/patterns/ui/fluent-v9-host-visual-fit.md) — Standalone code page thin host shell
- [`.claude/patterns/ui/fluent-v9-portal-gotcha.md`](../../.claude/patterns/ui/fluent-v9-portal-gotcha.md) — Dialog/Popover (relevant if P2a renders sub-list in a Popover)
- [`.claude/patterns/api/endpoint-definition.md`](../../.claude/patterns/api/endpoint-definition.md) — `/narrate` shape
- [`.claude/patterns/api/endpoint-filters.md`](../../.claude/patterns/api/endpoint-filters.md) — auth for modified endpoint
- [`.claude/patterns/api/service-registration.md`](../../.claude/patterns/api/service-registration.md) — feature-module DI (P3 if any new service)
- [`.claude/patterns/dataverse/web-api-client.md`](../../.claude/patterns/dataverse/web-api-client.md) — `useInlineTodoCreate` Xrm.WebApi calls (preserved during hoist)
- [`.claude/patterns/dataverse/polymorphic-resolver.md`](../../.claude/patterns/dataverse/polymorphic-resolver.md) — `TODO_REGARDING_CATALOG` (ADR-024)

**Reusable Code**:
- `src/client/shared/Spaarke.Events.Components/` — Pattern D precedent for new package layout, build config, export surface
- `src/client/shared/Spaarke.SmartTodo.Components/` — Pattern D precedent
- `src/solutions/EventsPage/` — thin host shell precedent for the post-shrink standalone shape
- `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` — 62-line section-shim precedent for the post-hoist Daily Briefing registration
- `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` — P2b target (BuildChannelNarrationPrompt + ParseChannelBullets)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs` — P3 target (`data.actions[]` population)
- `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/sections/dailyBriefing/` — source content to be hoisted (then removed from this location)
- `src/solutions/DailyBriefing/src/` — current standalone implementation; will shrink to thin shell
- `src/solutions/LegalWorkspace/src/sections/dailyBriefing/` — current SpaarkeAi registration; replaced by thin shim
- `useInlineTodoCreate.ts:160-261` — `TODO_REGARDING_CATALOG` + `applyResolverFields` (preserve verbatim during hoist)

---

## 3. Implementation Approach

### Phase Structure

```
P1 — Wiring seam fix (BLOCKS P2 hoist for clean integration)
└─ FR-01, FR-02: loadNotificationContext factory option + SpaarkeAi injection

P2 — Pattern D hoist (BLOCKS P2a; serial within phase)
├─ FR-03: New @spaarke/daily-briefing-components package scaffold
├─ FR-04, FR-09: Hoist components + briefingService
├─ FR-05, FR-06: Hoist + decompose hooks (3-way split)
├─ FR-07: Abstract dependencies (props/parameters)
├─ FR-08: Standalone code page shrink to thin host shell
└─ FR-10: Subpath exports contract

P2a — Hybrid aggregation UX (after P2)
├─ FR-11: NarrativeBullet renders per-item sub-list when itemIds.length > 1
├─ FR-12: Sub-row entity link via supplied regardingId
├─ FR-13: Sub-row Add-to-To-Do creates concrete sprk_todo
├─ FR-14: Sub-row Dismiss marks only the specific underlying row
└─ FR-14a: Aggregated Dismiss cascades to all itemIds[]

P2b — BFF /narrate fix (PARALLEL with P3, DD)
├─ FR-15: Emit regardingId in prompt
├─ FR-16: Updated rule list for LLM
└─ FR-17: Server-side validation of primaryEntityId

P3 — MDA native bell deep-links (PARALLEL with P2b, DD)
└─ FR-18: Populate data.actions[] when toasttype is visible

DD — De-duplication (PARALLEL with P2b, P3)
├─ FR-19: MicrosoftToDoIcon → @spaarke/ui-components/icons/
├─ FR-20: authInit → createCodePageAuthInitializer factory in @spaarke/auth
└─ FR-21: runtimeConfig → @spaarke/auth singleton

Phase 7 — Deployment + verification
├─ BFF deploy (after P2b + P3 merge)
├─ Code-page deploy (after P2 + P2a + DD merge)
└─ SC1–SC14 verification in spaarkedev1

Phase 8 — Wrap-up
└─ Project wrap-up task (README status → Complete; lessons-learned.md; archive)
```

### Critical Path

**Blocking Dependencies:**
- P1 BLOCKS P2 hoist (clean integration requires the seam in place)
- P2 BLOCKS P2a (sub-list lives in hoisted `NarrativeBullet`)
- P2b independent — can parallelize with P2 hoist
- P3 independent — can parallelize with P2 hoist
- DD-MicrosoftToDoIcon independent — can parallelize
- DD-authInit + DD-runtimeConfig should serialize relative to each other (both touch each solution's `main.tsx`)

**Critical path**: P1 → P2 (scaffold → components/hooks split → standalone shrink → LegalWorkspace shim) → P2a → BFF deploy → code-page deploy → SC verification → wrap-up

**High-Risk Items:**
- P2 component hoist: must preserve standalone code page behavior (NFR-02 relaxed but visual regression risk). Mitigation: smoke test + manual visual comparison.
- DD-authInit: touches auth init order in 3 solutions. Mitigation: factory preserves call sequence; one solution at a time; build verification between.

---

## 4. Phase Breakdown

### Phase P1: Wiring Seam Fix

**Objectives:**
1. Add `loadNotificationContext?: () => Promise<NarrateRequest | null>` factory option to Daily Briefing section registration
2. SpaarkeAi `main.tsx` injects `loadSpaarkeAiNotificationContext` via factory

**Deliverables:**
- [ ] Factory signature in current registration (or its successor in the new package — coordinated with P2)
- [ ] SpaarkeAi `main.tsx` injection wired
- [ ] Cold-load of SpaarkeAi workspace pane renders TL;DR + non-empty bullets for `ralph.schroeder@spaarke.com` with N>0 unread

**Critical Tasks:**
- Decide P1-vs-P2 sequencing: implement the seam either in the existing pre-hoist location (then carry forward during P2) OR land P2 scaffold first and implement the seam directly in the new package. Spec assumes the former.

**Inputs**: `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/sections/dailyBriefing/dailyBriefingRegistration.ts`, `src/solutions/SpaarkeAi/src/main.tsx`, existing `loadSpaarkeAiNotificationContext`

**Outputs**: Modified `dailyBriefingRegistration` (factory option); modified SpaarkeAi `main.tsx`; smoke test

---

### Phase P2: Pattern D Hoist into `@spaarke/daily-briefing-components`

**Objectives:**
1. Create new package mirroring `@spaarke/events-components` + `@spaarke/smart-todo-components` shape
2. Hoist all Daily Briefing components, hooks, services, types
3. Decompose `useNotificationData` into 3 independent hooks
4. Reduce standalone code page (`src/solutions/DailyBriefing/`) to thin host shell
5. Replace SpaarkeAi/LegalWorkspace registration with thin shim consuming the new factory

**Deliverables:**
- [ ] `src/client/shared/Spaarke.DailyBriefing.Components/` directory with `package.json`, `tsconfig.json`, build config
- [ ] All components hoisted (`DailyBriefingApp`, `TldrSection`, `ActivityNotesSection`, `ChannelHeading`, `NarrativeBullet`, `PreferencesDropdown`, `CaughtUpFooter`, `DigestHeader`, `EmptyState`)
- [ ] All hooks hoisted + decomposed (`useBriefingNotifications`, `useBriefingPreferences`, `useBriefingActions`, `useBriefingNarration`, `useInlineTodoCreate`)
- [ ] `briefingService` hoisted under `./services`
- [ ] Subpath exports: `./components`, `./widgets`, `./hooks`, `./services`, `./types`
- [ ] `src/solutions/DailyBriefing/src/` reduced to host-binding plumbing only (no business logic)
- [ ] `src/solutions/LegalWorkspace/src/sections/dailyBriefing/` is a thin shim consuming the factory
- [ ] Unit tests for the 3 split hooks
- [ ] Smoke test mounts `DailyBriefingApp` with mocked Xrm + asserts BFF `/narrate` fires with non-empty payload
- [ ] `npm run build` in new package succeeds; consumers can `import` from subpaths

**Critical Tasks:**
- New-package scaffold MUST come first; subsequent tasks depend on the build target existing
- `useNotificationData` decomposition is the deepest refactor — split before consumer-layer effect coordination
- Standalone shrink (FR-08) is the last P2 task — confirm both hosts work before deleting old files

**Inputs**: All Daily Briefing component/hook/service files in current locations; `@spaarke/events-components` precedent for layout

**Outputs**: New shared package; thin standalone host shell; LegalWorkspace section shim; unit + smoke tests

---

### Phase P2a: Hybrid Aggregation UX

**Objectives:**
1. `NarrativeBullet` renders per-item sub-list when `itemIds.length > 1`
2. Each sub-row has own entity link (supplied `regardingId`, no AI), own Add-to-To-Do, own Dismiss
3. Aggregated bullet Dismiss cascades to all underlying `itemIds[]`

**Deliverables:**
- [ ] `NarrativeBullet` enhanced with sub-list rendering (compact indented rows beneath narrative)
- [ ] Sub-row entity link uses `regardingEntityType` + `regardingId` from underlying `NotificationItem` (FR-12)
- [ ] Sub-row entity link opens record in Dataverse modal dialog via `Xrm.Navigation.navigateTo(...)` (target: 2, 80%/80%)
- [ ] Sub-row Add-to-To-Do creates concrete `sprk_todo` (real title, body, regarding) via `useInlineTodoCreate` (FR-13)
- [ ] Sub-row Dismiss marks only the specific `appnotification` as read; sub-row fades (FR-14)
- [ ] Aggregated Dismiss marks all `itemIds[]` as read; entire bullet hides (FR-14a)
- [ ] Visual tests for sub-list rendering (single-item bullets render unchanged)
- [ ] Dark-mode parity verified

**Inputs**: `NarrativeBullet.tsx` (post-hoist location), `useInlineTodoCreate`, `useBriefingActions`

**Outputs**: Enhanced `NarrativeBullet`; updated unit tests

---

### Phase P2b: BFF `/narrate` Prompt Fix + Server-Side Validation

**Objectives:**
1. `BuildChannelNarrationPrompt` emits `regardingId=` per item line
2. Rule list instructs LLM to use `regardingId` (not notification ID) for `primaryEntityId`
3. Server validates `primaryEntityId` against supplied `regardingId` set; nulls + logs on mismatch

**Deliverables:**
- [ ] `BuildChannelNarrationPrompt` emits format: `- [id={item.Id} regardingId={item.RegardingId}] Title | ... | regarding: Name (Type) | ...` (FR-15)
- [ ] Prompt rule list updated per FR-16 ("Set primaryEntityType to ... Set primaryEntityId to ... Do NOT use the [id=...] notification ID. Do NOT invent IDs.")
- [ ] `ParseChannelBullets` post-processing validates each bullet's `primaryEntityId` against `item.RegardingId` union; on mismatch: nulls `primaryEntityType` + `primaryEntityId` + `primaryEntityName`; logs warning (FR-17)
- [ ] Unit test: mocked LLM returns hallucinated `primaryEntityId` → response has nulled fields
- [ ] Unit test: prompt builder output contains `regardingId=` for items that have one
- [ ] Test added per BFF §10 bullet 6 (Test Update Obligation) in `tests/unit/Sprk.Bff.Api.Tests/`
- [ ] No new HIGH-severity CVE (`dotnet list package --vulnerable --include-transitive`)
- [ ] BFF publish-size delta verified (≤ +1 MB)

**Inputs**: `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs`, existing prompt builder + parser

**Outputs**: Modified prompt builder + parser; unit tests; publish-size baseline diff in PR

---

### Phase P3: MDA Native Bell Deep-Links

**Objectives:**
1. `CreateNotificationNodeExecutor` populates `appnotification.data.actions[]` for visible-`toasttype` notifications

**Deliverables:**
- [ ] `data.actions = [{ title: "Open", data: { url: actionUrl } }]` populated when `actionUrl` is present AND `toasttype != Hidden` (FR-18)
- [ ] Both `iterateItems` per-item creation and standard single-notification creation paths covered
- [ ] `customData.actionUrl` populated regardless of `toasttype` (unchanged from current behavior)
- [ ] Unit test: visible-toast notification has `data.actions[0].data.url == customData.actionUrl`
- [ ] Unit test: hidden-toast notification has `data.actions` null or absent
- [ ] E2E verification: trigger a notification (e.g., upload a document); MDA bell shows clickable "Open" button
- [ ] Test added per BFF §10 bullet 6
- [ ] BFF publish-size delta verified (≤ +1 MB)

**Inputs**: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/CreateNotificationNodeExecutor.cs`, `appnotification` schema

**Outputs**: Modified executor; unit tests; E2E verification note

---

### Phase DD: De-Duplication

**Objectives:**
1. Hoist `MicrosoftToDoIcon.tsx` to `@spaarke/ui-components/src/icons/` (FR-19)
2. Consolidate `authInit.ts` → `createCodePageAuthInitializer` factory in `@spaarke/auth` (FR-20)
3. Consolidate `runtimeConfig.ts` → `@spaarke/auth` singleton (FR-21)

**Deliverables:**
- [ ] Single canonical `MicrosoftToDoIcon` in `@spaarke/ui-components`; three solution-local copies deleted; consumers import from shared
- [ ] `createCodePageAuthInitializer(config)` factory returns `{ ensureAuthInitialized, authenticatedFetch, getTenantId }`; three solution-local `authInit.ts` deleted; each `main.tsx` calls factory
- [ ] Shared `resolveRuntimeConfig` singleton; three solution-local `runtimeConfig.ts` deleted; each solution imports from shared
- [ ] Verification: `find src/solutions -name "MicrosoftToDoIcon.tsx" -o -name "authInit.ts" -o -name "runtimeConfig.ts"` returns zero
- [ ] Visual diff null; build green; auth flow unchanged; runtime config resolution unchanged

**Critical Tasks:**
- DD-authInit and DD-runtimeConfig both touch each solution's `main.tsx`. Run them in series (one solution at a time, build between) to keep merge cost low.
- DD-MicrosoftToDoIcon is fully parallel-safe with everything (different files entirely).

**Inputs**: 3 copies each of `MicrosoftToDoIcon.tsx`, `authInit.ts`, `runtimeConfig.ts`; `@spaarke/ui-components` + `@spaarke/auth` shared packages

**Outputs**: Hoisted canonical sources; deleted duplicates; updated imports in all 3 solutions

---

### Phase 7: Deployment + Verification

**Objectives:**
1. Deploy BFF (P2b + P3) to spaarkedev1
2. Redeploy standalone Daily Briefing code page
3. Verify all 14 success criteria

**Deliverables:**
- [ ] BFF deployed via `bff-deploy` skill; publish-size delta reported in deploy log
- [ ] Standalone Daily Briefing code page redeployed via `code-page-deploy` skill
- [ ] SC1–SC14 verified end-to-end in spaarkedev1
- [ ] Architecture docs updated (SPAARKEAI-COMPONENT-MODEL, SPAARKEAI-WORKSPACE-ARCHITECTURE, BUILD-A-NEW-WORKSPACE-WIDGET)

**Inputs**: Merged code on `work/spaarke-daily-update-service-r2`; spaarkedev1 access

**Outputs**: Deployed BFF + code page; verified SC checklist; updated docs

---

### Phase 8: Project Wrap-Up

**Objectives:**
1. Update README status → Complete
2. Author lessons-learned.md
3. Archive project artifacts per `repo-cleanup` skill

**Deliverables:**
- [ ] README status updated
- [ ] `notes/lessons-learned.md` written (gap-fill since R1 didn't author one)
- [ ] `repo-cleanup` skill validates structure compliance

**Inputs**: Completed phases 1–7

**Outputs**: Final README; lessons-learned; clean repo state

---

## 5. Dependencies

### External Dependencies

| Dependency | Status | Risk | Mitigation |
|------------|--------|------|------------|
| Azure OpenAI (for `/narrate`) | Production | Low | Existing dependency; no changes to integration shape |
| Microsoft Graph / Dataverse Web API (`Xrm.WebApi`) | Production | Low | Existing dependency |
| spaarkedev1 environment | Ready | Low | Required for E2E verification of SC1, SC3, SC7 |

### Internal Dependencies

| Dependency | Location | Status |
|------------|----------|--------|
| Producer layer (7 playbooks + scheduler + executor) | `src/server/api/Sprk.Bff.Api/Services/Jobs/` + `Services/Ai/Nodes/` | Verified healthy 2026-06-18 |
| `@spaarke/ui-components` | `src/client/shared/Spaarke.UI.Components/` | Production; receives hoisted `MicrosoftToDoIcon` |
| `@spaarke/auth` | `src/client/shared/Spaarke.Auth/` | Production; receives new `createCodePageAuthInitializer` factory + shared `runtimeConfig` |
| `@spaarke/events-components` (Calendar) | `src/client/shared/Spaarke.Events.Components/` | Production; Pattern D precedent reference only |
| `@spaarke/smart-todo-components` | `src/client/shared/Spaarke.SmartTodo.Components/` | Production; Pattern D precedent reference only |
| ADR-012, ADR-021, ADR-024, ADR-028 | `docs/adr/` | Current |

---

## 6. Testing Strategy

**Unit Tests** (target: NFR-05 coverage for new package):
- 3 split hooks (`useBriefingNotifications`, `useBriefingPreferences`, `useBriefingActions`) — independent test fixtures
- `useInlineTodoCreate` preserved-coverage post-hoist
- `BuildChannelNarrationPrompt` — assert `regardingId=` emission per item
- `ParseChannelBullets` validation — assert hallucinated `primaryEntityId` → nulled fields + warning logged
- `CreateNotificationNodeExecutor` — assert `data.actions[]` populated for visible-toast; null for hidden-toast

**Integration Tests**:
- Smoke test: mount `DailyBriefingApp` with mocked Xrm; assert BFF `/narrate` call fires with non-empty payload (fills the test gap identified in the assessment)
- BFF `/narrate` endpoint: round-trip test with stubbed Azure OpenAI returning a valid `primaryEntityId` from the supplied set

**E2E Tests** (in spaarkedev1):
- SC1: SpaarkeAi workspace pane renders Daily Briefing with real notifications
- SC2: Same `DailyBriefingApp` component renders in standalone code page and SpaarkeAi widget
- SC3: 10 random hyperlink clicks → 10/10 open correct record
- SC4: Aggregated bullets show per-item sub-list
- SC5: Sub-row Add-to-To-Do creates concrete `sprk_todo`
- SC7: MDA native bell shows clickable "Open" buttons
- SC12: Dark-mode parity for hoisted components

---

## 7. Acceptance Criteria

### Technical Acceptance

**Per Phase**: see deliverables checklist in each Phase Breakdown section above.

**Project-wide** (mirror of README graduation criteria):

- [ ] SC1: SpaarkeAi workspace pane renders Daily Briefing with real notifications
- [ ] SC2: Same component renders in both hosts
- [ ] SC3: No dead hyperlinks (10/10)
- [ ] SC4: Aggregated bullets surface action items
- [ ] SC5: Add-to-To-Do creates concrete `sprk_todo`
- [ ] SC6: BFF prompt includes `regardingId` + server-side validation works
- [ ] SC7: MDA bell shows clickable "Open" buttons
- [ ] SC8: Standalone code page behavior unchanged for users
- [ ] SC9: Zero `MicrosoftToDoIcon.tsx` / `authInit.ts` / `runtimeConfig.ts` under `src/solutions/`
- [ ] SC10: `useNotificationData` split (3 hooks); monolithic deleted
- [ ] SC11: New package builds; subpath imports resolve
- [ ] SC12: Dark mode unaffected
- [ ] SC13: BFF publish-size delta ≤ +1 MB
- [ ] SC14: Architecture docs updated

### Business Acceptance

- [ ] SpaarkeAi users can see and act on their Daily Briefing notifications (was: empty state regression)
- [ ] Hyperlinks in briefings work (was: ~every aggregated bullet linked to nonexistent record)
- [ ] Action items in "Multiple X" bullets are visible and individually actionable (was: hidden behind narrative)
- [ ] MDA native bell shows actionable notifications (was: bell-only, no link)
- [ ] Future Daily Briefing changes ship to both surfaces in one PR (was: two divergent trees)

---

## 8. Risk Register

| ID | Risk | Probability | Impact | Mitigation |
|----|------|------------|---------|------------|
| R1 | P2 hoist regresses standalone code page rendering (visual) | Low | Medium | Relaxed FR-25 (NFR-02); smoke test + manual visual comparison; document non-trivial deviations in PR |
| R2 | Three-hook split changes cache invalidation semantics | Medium | Medium | Effect-based coordination at consumer layer (Option A); explicit `refetch()` on preferences change; document pattern in PR; unit test independent hook behavior |
| R3 | MDA bell `data.actions[]` schema mismatch with Dataverse | Low | Low | Spec assumption FR-18 documents expected shape; verify on first deployed notification in spaarkedev1; coordinate with platform docs |
| R4 | BFF publish-size delta exceeds +1 MB | Low | Low | Pure-code P2b/P3 changes; no new NuGet deps expected; baseline captured pre-implementation; verify per §10 NFR-01 |
| R5 | DD-authInit hoist breaks auth init order in one solution | Low | High | `createCodePageAuthInitializer` factory preserves exact call sequence; one solution at a time; build green between |
| R6 | LLM continues to hallucinate `primaryEntityId` despite prompt fix | Medium | Medium | Server-side validation (FR-17) nulls invalid IDs and logs warning; defense in depth; monitor warning rate post-deploy |
| R7 | Pattern D dual-use migration breaks SpaarkeAi mount during parallel work | Low | High | P1 lands first; smoke test in spaarkedev1 between P1 merge and P2 start |
| R8 | DD-runtimeConfig hoist exposes solution-specific config drift | Medium | Low | Audit each solution's `runtimeConfig` before hoist; verify byte-identity (spec assumption); reconcile divergence during DD task |

---

## 9. Next Steps

1. **Review this plan.md** with team (or self-review if solo)
2. **Run** `/task-create projects/spaarke-daily-update-service-r2` to generate POML task files
3. **Begin** Phase P1 implementation via `task-execute`

---

**Status**: Ready for Tasks
**Next Action**: Generate POML task files via `task-create` skill (Step C of project-pipeline)

---

*For Claude Code: This plan provides implementation context. Load relevant sections when executing tasks.*
