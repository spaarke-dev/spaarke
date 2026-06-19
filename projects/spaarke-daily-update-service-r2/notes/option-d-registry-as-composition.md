# Option D — Registry-as-Composition Factory for LegalWorkspace Sections

> **Project**: `spaarke-daily-update-service-r2`
> **Status**: ACTIVE — implementation in progress on PR #396
> **Decision date**: 2026-06-18
> **Supersedes**: the module-mutation slot pattern introduced by R2 task 002 (Wave 8)
> **Reach**: foundational pattern for ALL future SpaarkeAi workspace customization

## Why this document exists

The user explicitly flagged that:
1. The SpaarkeAi workspace is a critical core surface for the entire platform
2. It must support **two-way interaction between Assistant ⇄ Workspace ⇄ Context**
3. The module-mutation pattern landed in R2 task 002 was a band-aid that doesn't scale to that vision
4. We need the COMPREHENSIVE fix now, not the dailyBriefing-only patch

This document captures the problem, the alternatives we evaluated, the chosen design, the cookbook for future widgets, and the speculative-design discipline.

---

## 1. The architectural problem

### 1.1 What broke (functional)

For ~7 weeks before R2 daily-update-service-r2 task 002, the SpaarkeAi workspace pane showed an empty Daily Briefing for users who clearly had unread `appnotification` rows. The widget code worked. The BFF `/narrate` endpoint worked. The standalone Daily Briefing Code Page rendered correctly. But the embedded copy inside SpaarkeAi always rendered the "Nothing to see right now" empty state.

### 1.2 Why it broke (architectural)

The render path inside SpaarkeAi looks like this:

```
SpaarkeAi main.tsx
  → setDefaultWorkspaceRenderer(LegalWorkspaceRenderer)
  → renders <App />
       → renders <WorkspaceLayoutWidget /> from @spaarke/ai-widgets
       → that widget reads the renderer slot, instantiates <LegalWorkspaceRenderer {...props} />
       → LegalWorkspaceApp imports `SECTION_REGISTRY` directly from ./sectionRegistry
            → SECTION_REGISTRY is a `readonly SectionRegistration[]` const
              built at module-load time with a STATIC `dailyBriefingRegistration`
              that has no `loadNotificationContext` option supplied
       → WorkspaceGrid reads SECTION_REGISTRY, finds `dailyBriefingRegistration`,
         mounts DailyBriefingSection which calls useDailyBriefing with no loader
            → useDailyBriefing's empty-payload contract fires
            → BFF /narrate returns empty bullets → empty state UI
```

The root cause is **`SECTION_REGISTRY` is a hard-coded `readonly` const built at module-load time, with NO seam for consumers to inject per-widget options**. Every host that consumes `LegalWorkspaceApp` gets the same static registry, with no way to override per-widget construction.

SpaarkeAi needed to inject ONE function (`loadSpaarkeAiNotificationContext`) for ONE widget (Daily Briefing). The architectural primitive forced us to either:
- Change `SECTION_REGISTRY` (touches every consumer of LegalWorkspace — out of scope)
- Bypass it with a back-channel

R2 task 002 chose the back-channel — a module-mutable slot with a setter + wrapper. **That works for one widget once, but it's not a pattern.**

### 1.3 Why this matters beyond Daily Briefing

The user surfaced the strategic vision: SpaarkeAi is a critical core surface that must support arbitrary widgets and **two-way Assistant ⇄ Workspace ⇄ Context flows**. Concrete examples on the horizon:

- A chat panel widget that needs an `AgentClient` reference (Assistant ↔ Workspace)
- A Context-aware widget that needs a `ContextProvider` for the current matter/record (Context → Workspace)
- Widget-to-widget messaging via `PaneEventBus` (Workspace ↔ Workspace cross-cuts)
- A widget that needs to be configured differently in SpaarkeAi vs standalone LegalWorkspace (e.g., a Calendar that defaults to a different filter)
- Per-host visibility policy (R6 Pillar 9 — `getAgentVisibleState` callbacks)

If we leave the module-mutation pattern in place, **every one of those will require its own bespoke setter/slot/wrapper**. N widgets → N global setters → N opportunities for order-of-initialization bugs → N maintenance debts. The architecture should make adding the next widget cheap and discoverable; today it makes adding the next widget a new ad-hoc piece of infrastructure.

---

## 2. Alternatives evaluated

### Option A — Replace SpaarkeAi's section registration entry directly

What it would look like: SpaarkeAi imports the SECTION_REGISTRY array, swaps out the entry it wants, registers the modified copy.

**Why we rejected it**: `SECTION_REGISTRY` is `readonly` and `as const`. It can't be mutated. Even if we made it mutable, we'd be exposing array slot positions as a public API — fragile.

### Option B — Module-mutable slot + setter (R2 task 002 — what we shipped first)

What we did: LegalWorkspace shim file holds `let _globalNotificationLoader`. SpaarkeAi `main.tsx` calls `setLegalWorkspaceDailyBriefingNotificationLoader(loader)` at bootstrap. The default `dailyBriefingRegistration` const wraps a `lateBoundNotificationLoader` function that consults the slot at every fetch.

**Why it's a band-aid**:
- Global singleton — exactly one consumer can register at a time
- Order-dependent — if anything renders before main.tsx's setter call, the bullet renders empty (silent failure)
- 3 layers of indirection (slot + setter + wrapper) to deliver one function value
- Doesn't scale — every future widget needs its own setter
- It's a service-locator anti-pattern bolted onto a system that already has proper DI primitives (the factory option `loadNotificationContext` exists; we're bypassing it)

### Option C — React Context for the loader

What it would look like: Define `NotificationLoaderContext` in `@spaarke/daily-briefing-components`. `useDailyBriefing` reads `useContext`. SpaarkeAi `main.tsx` wraps its tree in `<NotificationLoaderContext.Provider value={loadSpaarkeAiNotificationContext}>`.

**Why we rejected it** (the user's correction — they were right):
- Solves Daily Briefing's specific instance of the problem; doesn't address the broader pattern
- Every future widget needs its own Context type and its own Provider wrap — scales linearly with widget count
- Doesn't naturally model two-way comms (Context is read-only injection, not bidirectional)
- No type-level discoverability of "what host options does this widget accept?"
- Forces the wiring to live INSIDE the widget package rather than at the workspace assembly layer where it belongs

It's strictly better than Option B for Daily Briefing alone, but it sets the wrong precedent for the codebase.

### Option D — Registry-as-Composition Factory ← CHOSEN

What it looks like:

- `LegalWorkspace` exports `createLegalWorkspaceSectionRegistry(options: LegalWorkspaceSectionRegistryOptions): readonly SectionRegistration[]` — a factory function
- `LegalWorkspaceSectionRegistryOptions` is a typed contract — the canonical interface for "what knobs are configurable per widget"
- `SECTION_REGISTRY` (the existing top-level const) becomes `createLegalWorkspaceSectionRegistry()` with no arguments — preserves all current behavior for standalone consumers byte-identically
- `LegalWorkspaceApp` accepts an optional `sections?: readonly SectionRegistration[]` prop, defaulting to the bare const
- SpaarkeAi `main.tsx` builds its own registry: `createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext: loadSpaarkeAiNotificationContext } })` — passes it to the renderer via a small wrapper function registered through the existing `setDefaultWorkspaceRenderer` slot
- The module-mutation slot from Option B is DELETED entirely

**Why this is the correct architecture**:

1. **One pattern, all widgets**: every per-widget customization SpaarkeAi (or any future host) needs becomes an entry in `LegalWorkspaceSectionRegistryOptions`. No new setters, no new global state, no new contexts.

2. **Type-safe discoverability**: `LegalWorkspaceSectionRegistryOptions` IS the contract. A maintainer adding a new widget sees the interface and knows exactly what knobs exist. Reviewing a host bootstrap, you see one `createLegalWorkspaceSectionRegistry(...)` call and immediately know what's customized.

3. **Standalone behavior preserved**: `createLegalWorkspaceSectionRegistry()` with no options returns a registry byte-identical to today's `SECTION_REGISTRY`. Standalone LegalWorkspace bundle is unchanged (FR-25 / NFR-10).

4. **Composable**: SpaarkeAi can override Daily Briefing only and take defaults for Calendar, Smart Todo, etc. Or it can override several. Or none. The default is the no-options registry.

5. **Aligns with the strategic vision**: when the first concrete consumer needs `paneEventBus` or `agentClient` or `contextProvider`, those become NEW entries in `LegalWorkspaceSectionRegistryOptions`. The pattern absorbs them without architectural change. We don't speculatively add them today (see §5 below).

6. **No global state, no order-dependency**: the registry is built when the consumer constructs it, then passed as a prop. There's no module mutation, no setter sequence to remember.

7. **Test-friendly**: a test can build a registry with mocks (`createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext: () => Promise.resolve(mockEnvelope) } })`) and pass it to a rendered `<LegalWorkspaceApp sections={testRegistry} />`. Today's module-mutation pattern requires test-fixture coordination on the setter call order.

8. **Removes 3 layers of indirection**: the slot, the setter, the late-bound wrapper all go away. The factory option is consumed directly via dependency injection at construction time, which is what it was designed for.

---

## 3. The design (target file shapes)

### 3.1 `src/solutions/LegalWorkspace/src/sectionRegistry.ts`

```ts
import type { SectionRegistration, SectionCategory, NarrateRequest } from "@spaarke/ui-components";
import { SECTION_METADATA_CATALOG } from "@spaarke/ui-components";
import { createLegalWorkspaceDailyBriefingRegistration } from "./sections/dailyBriefing/dailyBriefing.registration";
import { calendarRegistration } from "./sections/calendar.registration";
// ... other registrations stay imported as static consts (they don't have per-host knobs YET)

/**
 * Per-widget customization options for the LegalWorkspace section registry.
 *
 * Each property targets ONE widget's factory and exposes ONLY the knobs that
 * widget's factory accepts. Adding a new knob = add it here + thread it through.
 *
 * Speculative-design discipline: only widgets that have a real consumer-driven
 * customization need appear here. When the first concrete consumer demands a
 * new knob, add it. Until then, the widget's static registration suffices.
 */
export interface LegalWorkspaceSectionRegistryOptions {
  /**
   * Daily Briefing customization. Currently exposes the notification-context
   * loader — used by SpaarkeAi to flow `loadSpaarkeAiNotificationContext` into
   * the BFF /narrate envelope so the embedded copy renders real bullets.
   *
   * Standalone consumers omit this → empty-payload contract preserved.
   */
  dailyBriefing?: {
    loadNotificationContext?: () => Promise<NarrateRequest | null>;
  };

  // Future per-widget customizations live here. Examples we are NOT adding
  // today (no real consumer):
  //   calendar?: { initialFilter?: CalendarFilter; onDayClick?: (date: Date) => void };
  //   smartTodo?: { /* future knobs */ };
  //
  // Future cross-widget primitives (e.g. paneEventBus, agentClient,
  // contextProvider) also live here when the first concrete consumer arrives.
}

/**
 * Build a LegalWorkspace section registry, optionally customizing per-widget
 * construction. With no options, returns a registry byte-identical to the
 * standalone-LegalWorkspace SECTION_REGISTRY (preserves FR-25 / NFR-10).
 */
export function createLegalWorkspaceSectionRegistry(
  options: LegalWorkspaceSectionRegistryOptions = {},
): readonly SectionRegistration[] {
  const registry: readonly SectionRegistration[] = [
    getStartedRegistration,
    quickSummaryRegistration,
    latestUpdatesRegistration,
    todoRegistration,
    documentsRegistration,
    mattersRegistration,
    projectsRegistration,
    invoicesRegistration,
    workAssignmentsRegistration,
    createLegalWorkspaceDailyBriefingRegistration(options.dailyBriefing ?? {}),
    calendarRegistration,
  ];

  // Dev-mode duplicate / metadata-drift guards run for every constructed
  // registry (not just the default one) so custom registries are also checked.
  if (process.env.NODE_ENV !== "production") {
    runRegistryDevGuards(registry);
  }

  return registry;
}

/**
 * The default LegalWorkspace section registry — built once with no overrides.
 * Standalone LegalWorkspace imports this directly; embedding consumers
 * (SpaarkeAi) build their own via `createLegalWorkspaceSectionRegistry({...})`.
 */
export const SECTION_REGISTRY: readonly SectionRegistration[] =
  createLegalWorkspaceSectionRegistry();

// Lookup helpers continue to operate on SECTION_REGISTRY (default).
export function getSectionById(id: string): SectionRegistration | undefined { ... }
export function getSectionsByCategory(category: SectionCategory): SectionRegistration[] { ... }

// runRegistryDevGuards(registry) extracted from the existing dev-guard block
// at the module bottom — now reusable for any custom registry built via the
// factory (custom registries get the same drift detection as the default).
```

### 3.2 `src/solutions/LegalWorkspace/src/sections/dailyBriefing/dailyBriefing.registration.ts`

```ts
// REMOVE:
//   - _globalNotificationLoader: ... | null = null;
//   - export function setLegalWorkspaceDailyBriefingNotificationLoader(...)
//   - function lateBoundNotificationLoader(): Promise<NarrateRequest | null>
//
// KEEP:
//   - CreateLegalWorkspaceDailyBriefingRegistrationOptions (unchanged)
//   - routeRateLimitTelemetry (unchanged)
//   - createLegalWorkspaceDailyBriefingRegistration(options) (unchanged)
//
// CHANGE:
//   - dailyBriefingRegistration: stop wrapping lateBoundNotificationLoader;
//     just call the factory with no loader (no options)

export const dailyBriefingRegistration: SectionRegistration =
  createLegalWorkspaceDailyBriefingRegistration();  // no loader → empty payload
```

### 3.3 `src/solutions/LegalWorkspace/src/index.ts`

```ts
// REMOVE:
//   - export { setLegalWorkspaceDailyBriefingNotificationLoader } from ...
//
// ADD:
//   - export {
//       createLegalWorkspaceSectionRegistry,
//       SECTION_REGISTRY,
//       type LegalWorkspaceSectionRegistryOptions,
//     } from "./sectionRegistry";
//   - re-export SectionRegistration type for ergonomics
```

### 3.4 `src/solutions/LegalWorkspace/src/LegalWorkspaceApp.tsx`

```ts
// Add to ILegalWorkspaceAppProps:
interface ILegalWorkspaceAppProps {
  // ... existing props
  /**
   * Optional custom section registry. When omitted, the default
   * SECTION_REGISTRY is used (standalone LegalWorkspace behavior).
   * Embedding consumers (SpaarkeAi) pass a registry built via
   * createLegalWorkspaceSectionRegistry({...}).
   */
  sections?: readonly SectionRegistration[];
}

// Thread sections down to WorkspaceGrid. WorkspaceGrid currently imports
// SECTION_REGISTRY directly — change it to accept the registry as a prop.
```

### 3.5 `src/solutions/LegalWorkspace/src/components/Shell/WorkspaceGrid.tsx`

```ts
// REMOVE:
//   - import { SECTION_REGISTRY } from "../../sectionRegistry";
//
// CHANGE: accept sections as a prop (or via a new shell context if many
// nested components consume it). Default to the imported SECTION_REGISTRY
// at the entry point.
```

### 3.6 `src/solutions/SpaarkeAi/src/main.tsx`

```ts
// REMOVE:
//   - import { setLegalWorkspaceDailyBriefingNotificationLoader } from "@spaarke/legal-workspace";
//   - setLegalWorkspaceDailyBriefingNotificationLoader(loadSpaarkeAiNotificationContext);
//
// ADD:
//   import { createLegalWorkspaceSectionRegistry, LegalWorkspaceApp } from "@spaarke/legal-workspace";
//   import type { WorkspaceRenderer } from "@spaarke/ui-components";
//
//   const sectionsForSpaarkeAi = createLegalWorkspaceSectionRegistry({
//     dailyBriefing: {
//       loadNotificationContext: loadSpaarkeAiNotificationContext,
//     },
//   });
//
//   const SpaarkeAiWorkspaceRenderer: WorkspaceRenderer = (props) => (
//     <LegalWorkspaceApp {...props} sections={sectionsForSpaarkeAi} />
//   );
//
//   setDefaultWorkspaceRenderer(SpaarkeAiWorkspaceRenderer);
```

---

## 4. Cookbook — adding a new widget under this pattern

When a future widget (Calendar, Smart Todo, future chat panel, future document viewer, etc.) needs per-host customization:

1. **Author the widget's factory** in the shared lib or its own package. The factory accepts a typed options object and returns a `SectionRegistration`. Example: `createCalendarRegistration(options)`.

2. **Wrap it in a LegalWorkspace shim** (mirrors `dailyBriefing.registration.ts`) — closes over LegalWorkspace-local concerns (auth, telemetry) and exposes a `createLegalWorkspaceXxxRegistration(options)` factory.

3. **Add an entry to `LegalWorkspaceSectionRegistryOptions`** in `sectionRegistry.ts`:
   ```ts
   export interface LegalWorkspaceSectionRegistryOptions {
     dailyBriefing?: { loadNotificationContext?: ... };
     calendar?: { initialFilter?: CalendarFilter; onDayClick?: (date: Date) => void };
     // ... newly added
   }
   ```

4. **Thread the option through** in `createLegalWorkspaceSectionRegistry`:
   ```ts
   createLegalWorkspaceCalendarRegistration(options.calendar ?? {})
   ```

5. **No other changes needed**. SpaarkeAi (or any embedding host) can immediately customize the new widget by adding to its `createLegalWorkspaceSectionRegistry({...})` call. Standalone LegalWorkspace continues to work — it just uses the no-options default.

6. **Tests**: add one test asserting that the new widget's customization option threads through to its registration entry.

---

## 5. Speculative-design discipline — what we do NOT add today

The CLAUDE.md "Don't add features, refactor, or introduce abstractions beyond what the task requires" guidance applies. We add the registry-as-composition pattern (one factory + one options interface) because daily-update-r2 demands it. We do NOT add speculative entries that have no real consumer:

| Not added today | Why not | When to add |
|---|---|---|
| `calendar?: { ... }` | No SpaarkeAi-side Calendar customization is requested in current scope | When the first SpaarkeAi feature request needs Calendar customization, or when ai-spaarke-ai-workspace-UI-r1 starts adding Calendar knobs |
| `smartTodo?: { ... }` | Same — no current consumer | Same — when first concrete consumer needs it |
| `paneEventBus?: PaneEventBus` (cross-widget) | The bus may not exist yet as a typed primitive in any package; speculative | When PaneEventBus lands as a real cross-widget primitive (R6 Pillar 6 + future work) |
| `agentClient?: AgentClient` (Assistant flows) | The agent-client interface for widget-level binding doesn't exist yet | When the first chat-aware widget needs construction-time agent binding |
| `contextProvider?: ContextProvider` (Context flows) | Context flows still resolve via Xrm frame-walk + StandaloneAiProvider's useEntityResolver | When the first widget needs a host-supplied context provider instead of Xrm fallback |
| Per-widget `visibilityPolicy` (R6 Pillar 9 hook) | Pillar 9 currently lives at `WorkspaceWidgetRegistry` (a different registry, AI-widgets layer); coupling them is premature | When R6+ wants section-level visibility policy to compose with the AI-widgets layer |

**The interface is the contract**. When any of those concrete needs arrives, the change is small and additive — one new property in `LegalWorkspaceSectionRegistryOptions` + one new thread-through call. The architecture absorbs new requirements without restructuring; today's no-options default keeps all existing consumers byte-identical.

---

## 6. R6 coordination

R6 (`spaarke-ai-platform-unification-r6`) is in flight via open PR #395. Verified 2026-06-18:

- R6 makes ZERO commits touching `src/solutions/SpaarkeAi/src/main.tsx`
- R6 makes ZERO commits touching `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/`
- R6's Pillar 9 (`getAgentVisibleState`) operates on `WorkspaceWidgetRegistry` (a sibling registry in `@spaarke/ai-widgets`), NOT `SECTION_REGISTRY` in LegalWorkspace
- R6 makes no assumption about `SECTION_REGISTRY` being a const

**File-level overlap with Option D: zero.** Both PRs can merge in any order with no conflict resolution.

Two non-blocking forward-design questions worth asking the R6 team (R7+ planning, not coordination blockers):

1. Once Option D ships, should the `dailyBriefing.notificationLoader` option pattern be propagated to other per-widget options the R6 team would add? (Answer: yes — that's the whole point of the pattern.)
2. Should R6 Pillar 9's `getAgentVisibleState` callback eventually compose into `LegalWorkspaceSectionRegistryOptions` so hosts can configure per-widget visibility policy at registry-construction time? (Open design question for R7+.)

---

## 7. Test coverage

Two new unit tests in the new package (Jest 30 suite added by task 019):

### Test 1: no-options factory returns standalone-equivalent registry

```ts
test('createLegalWorkspaceSectionRegistry() returns same widget IDs as SECTION_REGISTRY const', () => {
  const fromFactory = createLegalWorkspaceSectionRegistry();
  expect(fromFactory.map(r => r.id)).toEqual(SECTION_REGISTRY.map(r => r.id));
  expect(fromFactory.length).toBe(SECTION_REGISTRY.length);
});
```

### Test 2: dailyBriefing.loadNotificationContext threads through to the entry's factory

```ts
test('createLegalWorkspaceSectionRegistry({ dailyBriefing: { loadNotificationContext } }) threads loader', async () => {
  const loader = jest.fn().mockResolvedValue({ /* mock NarrateRequest */ });
  const registry = createLegalWorkspaceSectionRegistry({
    dailyBriefing: { loadNotificationContext: loader }
  });
  const dailyBriefingEntry = registry.find(r => r.id === 'dailyBriefing');
  expect(dailyBriefingEntry).toBeDefined();
  // The entry's factory should have wired the loader through. The exact
  // assertion depends on what shape SectionRegistration exposes — either
  // verify by mounting and observing the fetch call, or by inspecting
  // a captured options object on the registration.
});
```

A third test verifies that R2 task 002's module-mutation API is GONE:

```ts
test('legacy setLegalWorkspaceDailyBriefingNotificationLoader API is removed', () => {
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const legacyApi = (require('@spaarke/legal-workspace') as any).setLegalWorkspaceDailyBriefingNotificationLoader;
  expect(legacyApi).toBeUndefined();
});
```

---

## 8. Documentation alignment tasks (follow-up POMLs)

Three new tasks land in this PR to ensure the pattern is documented for reuse:

- **Task 070** — Pattern doc: `.claude/patterns/ui/workspace-section-registry-composition.md` codifying this pattern as the canonical Spaarke approach for per-host workspace customization
- **Task 071** — Architecture docs: update `docs/architecture/SPAARKEAI-DASHBOARD-AND-WIDGET-MODEL.md` and `docs/guides/BUILD-A-NEW-WORKSPACE-WIDGET.md` to describe the composition factory + cookbook for adding a new widget
- **Task 072** — ADR: draft (or extend) an ADR codifying the decision. Most likely a new ADR ("Workspace Section Registry as Composition Factory") because this is a genuinely new architectural decision separate from ADR-006 (UI Surface Architecture), ADR-012 (Shared Component Library), and ADR-022 (React 19). The ADR captures: problem, considered alternatives (A/B/C), decision (D), consequences, related ADRs

These tasks are NOT in PR #396's commit (the implementation is). They are scheduled as Phase 7b follow-ups so the docs land separately and can be reviewed by the team holistically.

---

## 9. References

- **PR #396**: `feat(daily-update-r2): Daily Briefing — SpaarkeAi Pattern D Migration (R2)` — landing the implementation
- **R2 task 002 (Wave 8)**: the Option B module-mutation pattern this supersedes — commit `189f668eb`
- **R2 task 018 (Wave 7)**: the LegalWorkspace shim that replaced the static `SectionRegistration` const — commit `2d5eefb90`
- **R6 PR #395**: `spaarke-ai-platform-unification-r6` — verified no conflict with this work
- **R4 task 052 / C-4**: introduced `setDefaultWorkspaceRenderer` slot pattern — the same renderer-slot mechanism Option D piggybacks on for SpaarkeAi's custom-renderer-wrapper
- **ADR-006**: UI Surface Architecture — Pattern D dual-use baseline
- **ADR-012**: Shared Component Library — the per-widget factory pattern that Option D builds on
- **`notes/task-002-blocker.md`**: the original analysis from R2 Wave 2a that surfaced the architectural problem
