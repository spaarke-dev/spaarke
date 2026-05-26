# SpaarkeAi Dashboard and Widget Model

> **Purpose**: Authoritative architectural framing of the SpaarkeAi two-wrapper model. Defines the three surfaces, the two intentionally-retained widget wrappers, the four mount sources, the dual-use pattern, and the LegalWorkspace-as-dashboard-engine framing. Future widget authors should be able to read this doc alone and pick the correct wrapper before writing code.
>
> **Status**: New architectural reference doc introduced in R4 (DR-01 / W-1).
> **Last reviewed**: 2026-05-26 (R4 task 010 / W-1 — initial publication).
> **Audience**: Widget authors, architects evaluating new placements, anyone wiring a new mount source.
> **Predecessor framing**: This doc supersedes any earlier ad-hoc references to "one workspace pattern" / "the workspace widget" by codifying that there are intentionally **two** wrappers.
>
> **Required reading before this doc**:
> - [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — the cold-load → widget render pipeline this model sits on top of.
> - [`SPAARKEAI-COMPONENT-MODEL.md`](./SPAARKEAI-COMPONENT-MODEL.md) — the inventory of `@spaarke/*` shared libs the wrappers consume.
> - [`SPAARKEAI-COMPONENTIZATION-AUDIT.md`](./SPAARKEAI-COMPONENTIZATION-AUDIT.md) — §2A (Calendar canonical Pattern D) is referenced here.

---

## 1. Three surfaces (Assistant / Workspace / Context)

SpaarkeAi's three-pane shell is the host for every Spaarke AI experience. Each pane is a distinct **surface** with a distinct interaction model, audience, and event-channel contract:

| Surface | Pane | Audience-facing role | Primary PaneEventBus channel | Owns |
|---|---|---|---|---|
| **Assistant** | Left | Conversation — the user talks to the AI, the AI hands work back as widgets | `conversation` (in), `workspace` (dispatches `widget_load`) | Chat thread, slash commands, playbook selection, first-message stage transition |
| **Workspace** | Center | Where work lives — tabs of active workspaces and AI outputs | `workspace` (primary) | `WorkspaceTabManager` (FIFO at 8 tabs), tab strip, the active widget's render area, the workspace dropdown |
| **Context** | Right | Where context comes from — Get Started cards, playbook gallery, wizards, tool views | `context`, dispatches to `workspace` | Get Started, semantic search, playbook gallery, Context-pane wizards (e.g. Create Project) |

The three surfaces communicate through the typed `PaneEventBus` (see [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) §3.3 + [`SPAARKEAI-COMPONENT-MODEL.md`](./SPAARKEAI-COMPONENT-MODEL.md) §9). The bus is the only mechanism a non-Workspace surface should use to mount a widget into the Workspace pane — direct cross-pane calls are forbidden.

The Workspace pane is the only surface that mounts widgets. The Assistant and Context surfaces **produce widget-load intents**; they do not host widgets themselves (except for their own pane-local UI like the chat composer or the Get Started panel).

---

## 2. Two wrappers (intentionally retained)

The Spaarke widget ecosystem has **two distinct wrappers** for mounting something into the Workspace pane. Both are intentionally retained — they serve disjoint use cases. This was operator-finalized as **OC-R4-06** during 2026-05-25 scoping; do not propose unifying them.

### 2.1 The Dashboard wrapper — `LegalWorkspaceApp` (via `WorkspaceLayoutWidget`)

**What it is**: A composable dashboard surface that renders a `sprk_workspacelayout` (rows + sections JSON) by mounting `LegalWorkspaceApp` in embedded mode. The actual widget type registered in `WorkspaceWidgetRegistry` is **`'workspace'`** (registration #16 in `register-workspace-widgets.ts`) and its component is `WorkspaceLayoutWidget` — a thin wrapper that does exactly one thing:

```tsx
<LegalWorkspaceApp version="embedded" embedded initialWorkspaceId={data.layoutId} />
```

`LegalWorkspaceApp` then fetches the layout from the BFF, parses `sectionsJson`, and renders **N section factories** (Quick Summary + Documents + Smart To Do + ... or a single section like Calendar or Daily Briefing) inside the layout's row/column grid.

**Use the Dashboard wrapper when**:
- The user wants to see **multiple sections composed into one tab**: Corporate Workspace (Get Started + Quick Summary + Latest Updates + To Do + Documents), or a user-built custom layout combining (say) Calendar + Documents + Smart To Do List.
- The unit of mount is a **Dataverse layout record** (`sprk_workspacelayout` row, system or user-owned) with its own `sectionsJson`.
- You want the workspace to be re-orderable / re-mixable by users through the `WorkspaceLayoutWizard`.

**Identity**: One Workspace tab = one `LegalWorkspaceApp` mount = N sections inside it.

**Mount data**: `{ layoutId: string }` — the Dataverse layout GUID. Everything else (sections, layout template, owner) is resolved server-side via `GET /api/workspace/layouts/{id}`.

**Examples in production today**: Corporate Workspace (hard-coded GUID), Daily Briefing (single-section layout), Smart To Do List, My Work, Documents, Calendar (R3 task 115), any user-created custom layout.

### 2.2 The Direct widget wrapper — `WorkspaceWidgetRegistry`

**What it is**: The lazy-factory widget registry in `@spaarke/ai-widgets` (`registry/WorkspaceWidgetRegistry.ts`). Each registration is a `(widgetType, metadata, factory)` triple where `factory` is a `() => import(...).then(m => ({ default: m.MyWidget }))` dynamic import. The Workspace pane's `widget_load` handler resolves the registry, mounts the resulting component as a new tab, and acks via `widget_load { tabId }`.

The Dashboard wrapper described in §2.1 IS one of these registrations (the `'workspace'` type). The other 15 registrations (today, R4 baseline) are **direct widgets** — sophisticated single-purpose React components that own their own data, UX, and any nested chrome.

**Use the Direct widget wrapper when**:
- The unit of mount is a **single sophisticated experience** with its own internal state and data fetching: a redline viewer, an embedded wizard, an AI tool output viewer, a search results pane.
- You do NOT want this experience to be re-composable with other sections.
- The widget's lifecycle (open, close, evict) does not need to be tied to a Dataverse-stored layout record.

**Identity**: One Workspace tab = one `WorkspaceWidgetComponent` instance.

**Mount data**: Widget-type-specific. E.g. `RedlineViewerWidget` receives the document pair; `CreateProjectWizardWidget` receives the wizard's initial context; AI-output widgets receive the orchestrator's payload.

**Examples in production today** (15 of the 16 registrations are direct widgets):
- AI output widgets: `BudgetDashboard`, `SearchResults`, `AnalysisEditor`, `ContractComparison`, `StatusSummary`, `Recommendation`, `ActionPlan`.
- Document widgets: `redline-viewer`, `DocumentViewer` (planned), document upload flow.
- Wizard-launcher widgets: `create-matter-wizard`, `document-upload-wizard`, `search-select-wizard`, `email-compose`, `meeting-schedule`, `create-project-wizard`, `find-similar-wizard`.

### 2.3 Why both — the operator-stated intent (OC-R4-06)

The two wrappers were considered for unification during 2026-05-25 scoping and **explicitly retained** because they encode different things:

| Concern | Dashboard wrapper | Direct widget wrapper |
|---|---|---|
| Composition model | N sections in row/column grid (Dataverse-driven) | One single-purpose component (code-driven) |
| User-customizable? | YES — via WorkspaceLayoutWizard | NO — code change required |
| Re-orderable? | YES — sections re-arranged by user | NO |
| Data unit | `sprk_workspacelayout` row + `sectionsJson` | Widget-type-specific payload |
| Lifecycle | Driven by layout GUID | Driven by the dispatching surface |
| Multiple instances? | Yes — many tabs can mount different layouts | Per registration's `allowMultiple` flag |

Unifying them would force every widget to be either (a) a Dataverse-stored layout (heavyweight, requires the layout-builder UI, requires server-side fetch), or (b) a code-only widget with no user composition. Neither is acceptable: users genuinely want to compose dashboards AND we genuinely need single-purpose widgets like the redline viewer.

**The two-wrapper model is the architecture.** This doc is the authoritative source for that claim; downstream docs (W-2, C-2) consume this terminology.

---

## 3. Mount sources (four)

A "mount source" is **where the intent to mount a widget originates**. SpaarkeAi supports four mount sources today, each dispatching to the Workspace pane's `widget_load` channel:

### 3.1 User picker — the workspace dropdown (always present)

**Trigger**: User clicks a layout from `WorkspacePaneMenu` (the "Switch Workspace" dropdown in the Workspace pane header).
**Dispatches**: `widget_load` on `workspace` channel with `widgetType: 'workspace'` + `widgetData: { layoutId }`.
**Wrapper picked**: Dashboard wrapper (§2.1) — every dropdown entry IS a `sprk_workspacelayout` record.
**Code reference**: `src/solutions/SpaarkeAi/src/components/workspace/WorkspacePaneMenu.tsx`.
**Status in R4**: Already shipping; no R4 changes.

### 3.2 Assistant pane → Workspace (R4 W-4 first end-to-end demo)

**Trigger**: The user uploads a file or invokes a chat action in the Assistant pane that produces a widget-mountable result. Operator-recommended R4 demo: PDF upload in chat → DocumentViewer widget mounts as a workspace tab.
**Dispatches**: `widget_load` on `workspace` channel — `widgetType` is whatever the result calls for (direct widget for `DocumentViewer`, or `'workspace'` if the result is a constructed layout).
**Wrapper picked**: Usually Direct widget wrapper (§2.2). Could be Dashboard wrapper if the Assistant returns a layout-shaped result.
**Code reference (R4 W-4)**: `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` + the chat-event-to-bus translation in `AiSessionProvider`.
**Status in R4**: First end-to-end wiring lands in W-4 (FR-02). Final demo scenario is operator-chosen at task time (OC-R4-07).

### 3.3 Context pane → Workspace (R4 W-5 second wiring)

**Trigger**: User completes a Context-pane wizard (recommended R4 demo: Create Project wizard's final "Add result to Workspace" step) and the wizard dispatches a widget-load on completion.
**Dispatches**: `widget_load` on `workspace` channel — `widgetType` is the wizard-result widget (e.g. a project-summary direct widget, or a constructed workspace layout).
**Wrapper picked**: Usually Direct widget wrapper. Could be Dashboard wrapper if the wizard produces a layout.
**Code reference (R4 W-5)**: `src/solutions/SpaarkeAi/src/components/context/ContextPaneController.tsx` + the per-wizard final-step handler.
**Status in R4**: First end-to-end wiring lands in W-5 (FR-03).

### 3.4 Workspace dropdown — workspace-builder result

**Trigger**: A user builds a new custom workspace via `WorkspaceLayoutWizard` (Add new workspace flow), saves it to Dataverse, and the new layout appears in the dropdown. Subsequent mounts use mount source §3.1.
**Dispatches**: After save, `widget_load` on `workspace` channel with `widgetType: 'workspace'` + the new `{ layoutId }`. The dispatch happens automatically (auto-open on save) OR the user later picks it from the dropdown.
**Wrapper picked**: Always Dashboard wrapper — the wizard's whole purpose is to produce a `sprk_workspacelayout` row with N sections.
**Code reference**: `src/solutions/WorkspaceLayoutWizard/src/App.tsx` (the standalone Code Page invoked by the dropdown's "+ New Workspace" affordance).
**Status in R4**: Already shipping. R4 W-3 fixes the wizard's catalog-drift bug so all 7 sections are pickable (FR-01).

### 3.5 Mount source matrix

| Mount source | Wrapper most commonly used | Typical widget type | R4 status |
|---|---|---|---|
| §3.1 User picker (dropdown) | Dashboard | `'workspace'` (with `layoutId`) | Shipping |
| §3.2 Assistant → Workspace | Direct (sometimes Dashboard) | `DocumentViewer`, AI outputs, or `'workspace'` if result is a layout | R4 W-4 (FR-02) |
| §3.3 Context → Workspace | Direct (sometimes Dashboard) | wizard-result widget or `'workspace'` | R4 W-5 (FR-03) |
| §3.4 Workspace dropdown — builder result | Dashboard | `'workspace'` | Shipping; R4 W-3 fixes catalog (FR-01) |

A mount source is NOT a wrapper — it's the **dispatcher**. The wrapper is selected by the dispatched `widgetType`. The bus is symmetric: any mount source can dispatch either wrapper.

---

## 4. Dual-use pattern (Calendar + Daily Briefing worked examples)

Some widgets are **dual-use**: they ship as both (a) a section inside a Dashboard wrapper layout AND (b) a standalone tab via a Direct widget wrapper, with the same component reused in both shapes. The dual-use pattern is the canonical "shared-lib widget + thin LW shim" — proven first by Calendar (R3 task 115, Round 9) and articulated as Pattern D in [`BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §1.

### 4.1 Calendar — the canonical dual-use example (R3 task 115)

**The widget proper**: `CalendarWorkspaceWidget` (~1100 LOC) lives in `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/`. It composes Calendar/Grid/Filter components from `@spaarke/events-components` (the shared lib hoisted in R3 task 114). Zero LegalWorkspace coupling: no `FeedTodoSyncContext`, no LW-local `DataverseService`, no LW hooks. All data access is `Xrm.WebApi` via shared services.

**Use #1 (Dashboard wrapper)**: The `Calendar` system workspace layout. `sprk_workspacelayout` row has `sectionsJson` referencing `calendar` (sectionId). The LegalWorkspace section registration in `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` is a **62-line shim** that renders `<CalendarWorkspaceWidget />` inside a `ContentSectionConfig`. The whole Calendar workspace, when picked from the dropdown, mounts via Dashboard wrapper → `LegalWorkspaceApp(embedded)` → `WorkspaceShell` → Calendar section's `renderContent` → `CalendarWorkspaceWidget`.

**Use #2 (Direct widget wrapper)**: A future Calendar-as-standalone-tab scenario could register `CalendarWorkspaceWidget` directly in `WorkspaceWidgetRegistry` with `widgetType: 'calendar'`. The Assistant or Context surfaces could then dispatch `widget_load { widgetType: 'calendar' }` and the same component mounts as its own tab (no `LegalWorkspaceApp` wrapper). The widget is dual-use-ready because it has zero host coupling — the same render call works in both wrappers.

**Why dual-use is desirable**: The component is the single source of truth. A bug fix or feature improvement to `CalendarWorkspaceWidget` benefits **every** mount it has (Dashboard or Direct). The R3 polish rounds (R10–R13) all landed once in `@spaarke/events-components` and propagated to both the standalone EventsPage AND the Calendar workspace automatically.

**Architectural significance**: Pattern D is the model for new widgets going forward. See [`SPAARKEAI-COMPONENTIZATION-AUDIT.md`](./SPAARKEAI-COMPONENTIZATION-AUDIT.md) §2A and §7.7. The Calendar case proves you can ship a workspace section that reuses the LegalWorkspaceApp pipeline WITHOUT becoming LW-internal — just put the implementation in a shared lib and let the LW-side registration be a one-screen shim.

### 4.2 Daily Briefing — the older / partially-dual example

**The widget proper**: `DailyBriefingSection` factory + `useDailyBriefing` hook + component all hoisted to `@spaarke/ui-components/components/WorkspaceShell/sections/dailyBriefing/` in R3 task 069. LegalWorkspace's `sections/dailyBriefing/dailyBriefing.registration.ts` is now a re-export shim (R3 task 086).

**Use #1 (Dashboard wrapper)**: The `Daily Briefing` system workspace layout. `sectionsJson` references `daily-briefing` (sectionId). Mounts via Dashboard wrapper as a single-section layout (this IS the layout's only section). On a multi-section custom layout, it can be one of several sections.

**Use #2 (Direct widget wrapper)**: Not currently registered as a Direct widget, but the hoisted component CAN be — its dependency closure is portable (calls BFF via `authenticatedFetch`, takes injected `trackEvent`). If the Assistant pane ever needs to dispatch a "show today's briefing as a tab" widget, the component is dual-use-ready.

**Difference from Calendar**: Daily Briefing's data path is BFF-backed (`/api/ai/dailybriefing`) rather than Xrm.WebApi. This is fine — the dual-use property is about component portability, not channel uniformity. See §6.

### 4.3 Pattern recognition — when to design for dual-use

A widget is a **dual-use candidate** when ANY of:
- It surfaces user-relevant content that a user might compose into a custom workspace dashboard (good fit for Dashboard wrapper).
- It is also valuable as a standalone tab launched by an AI action or wizard result (good fit for Direct wrapper).
- It can be implemented with zero coupling to the LegalWorkspace-internal context providers (`FeedTodoSyncContext`, LW `DataverseService`).

A widget is NOT a dual-use candidate when:
- It is intrinsically single-purpose and not composable with other sections (e.g. a 12-step embedded wizard, a redline viewer that occupies the whole tab).
- It depends on LegalWorkspace-local context that other hosts can't realistically provide (a section that hooks into `FeedTodoSyncContext` cross-section badge counts is Pattern A only).

**Heuristic**: Build the widget in a shared lib (`@spaarke/<lib>-components`) from day one if it could ever be useful outside LegalWorkspace. The Calendar precedent is the gold standard.

---

## 5. LegalWorkspace as the dashboard engine

R4 retires the **standalone LegalWorkspace code page** (`sprk_corporateworkspace`) per scoping decision **OC-R4-05** + W-6. **The LegalWorkspace components are retained as a library.** This section explains the resulting framing.

### 5.1 What's retired vs retained

| Item | R4 status | Why |
|---|---|---|
| `sprk_corporateworkspace` Code Page deploy | **Retired** (W-6) | The standalone surface is no longer the canonical home for LegalWorkspace functionality; SpaarkeAi embeds it. Maintaining two deployed entry points (standalone + embedded) doubles the surface area for theme / auth / data drift. |
| `LegalWorkspaceApp` component | **Retained as library** | This IS the dashboard engine — it parses `sectionsJson`, mounts section factories, drives `WorkspaceShell` rendering. SpaarkeAi's `WorkspaceLayoutWidget` embeds it. |
| `@spaarke/legal-workspace` package | **Retained** | The package barrel exports `LegalWorkspaceApp` for SpaarkeAi consumption. |
| `src/solutions/LegalWorkspace/` source tree | **Retained** | Section factories, hooks (`useWorkspaceLayouts`, `useDailyBriefing`, etc.), local components, contexts (`FeedTodoSyncContext`), runtime-config singleton all live here. Deploy script for the standalone Code Page is removed in W-6; the source tree is otherwise untouched. |
| R3 FR-25 / NFR-10 ("standalone LegalWorkspace continues to function identically") | **Superseded by W-6** | No longer applies forward. Standalone-vs-embedded parity is no longer an architectural constraint because there is no standalone. |

### 5.2 LegalWorkspace-as-dashboard-engine framing

Going forward, view LegalWorkspace as **the dashboard rendering engine of SpaarkeAi**, not as a peer surface. It is the implementation behind the Dashboard wrapper (§2.1) and nothing else:

```
Dashboard wrapper (the abstraction)
   = the 'workspace' widget registration in WorkspaceWidgetRegistry
   = mounts WorkspaceLayoutWidget
       = mounts <LegalWorkspaceApp embedded initialWorkspaceId={layoutId} />
           = the engine — fetches layout, parses sectionsJson, mounts section factories
```

Everything in `src/solutions/LegalWorkspace/src/` that isn't a deploy artifact is **library code consumed by the engine**. Sections, hooks, contexts, the runtime-config singleton — all of it is dashboard-engine internals.

A future host that wanted to embed a workspace from outside SpaarkeAi (e.g. Outlook side pane, Teams app) would do exactly what SpaarkeAi does: install `@spaarke/legal-workspace` + `setLegalWorkspaceRuntimeConfig(config)` + render `<LegalWorkspaceApp embedded initialWorkspaceId={layoutId} />`. The retirement of the standalone Code Page does not constrain this — it just means LegalWorkspace is no longer self-deployed.

### 5.3 What this means for new widget authors

- **Do NOT build for LegalWorkspace standalone parity.** That contract no longer applies (R3 FR-25 / NFR-10 superseded).
- **Do build sections that work in `LegalWorkspaceApp(embedded)`.** That IS the dashboard surface today and going forward.
- **Prefer Pattern D (shared-lib widget + thin LW shim)** for new sections — the Calendar precedent shows this is clean, reusable, and immune to LW-internal coupling.
- **If your section MUST use LegalWorkspace-internal context** (`FeedTodoSyncContext`, LW `DataverseService`, etc.), it's a Pattern A section — that's still legal, just be aware it locks the section to LegalWorkspace embedding for now (until those contexts are hoisted, which is currently deferred — see audit §2 + §2A).

### 5.4 Forward implications for the SpaarkeAi shell

- **No new Code Page entry points for LegalWorkspace functionality.** Use the SpaarkeAi `sprk_spaarkeai` Code Page (the only deployed entry point) + the embedded LW engine.
- **Deploy script consolidation.** Per W-6, `scripts/Deploy-LegalWorkspace.ps1` (or the LW step in `Deploy-Codepages.ps1`) is removed/skipped. Only `Deploy-SpaarkeAi.ps1` remains for the user-facing shell. `Deploy-SystemWorkspaceLayouts.ps1` still seeds layout records — that's Dataverse data, not a Code Page deploy.
- **CLAUDE.md System Entry Points pointer** updates by W-6 to reflect the consolidation.

---

## 6. Author walk-through — picking the right wrapper

This section validates the doc by walking three hypothetical widget designs through the decision process. A future widget author should be able to use this walk-through alone (no back-channel needed) to pick the right wrapper.

### 6.1 Hypothetical Widget A — "Risk Dashboard" (composable, multi-section)

**Description**: An operator wants a "Risk Dashboard" workspace that combines (a) a Quick Summary of high-risk matters, (b) a Latest Updates feed filtered to risk-tagged items, and (c) a To Do list of risk-mitigation tasks.

**Walk-through**:
1. **Surfaces affected**: Workspace pane (where the dashboard mounts), no Assistant or Context dependency.
2. **Composition?** YES — three sections in one tab. Reuses existing sections (Quick Summary, Latest Updates, Smart To Do) with new filter criteria.
3. **User-customizable?** YES — operator wants users to be able to add/remove sections via the workspace builder.
4. **Unit of mount?** A `sprk_workspacelayout` row with `sectionsJson: { rows: [{ sections: [quick-summary, latest-updates, todo] }] }` — filter criteria stored in section-config JSON.
5. **Wrapper picked**: **Dashboard wrapper** (§2.1) — `'workspace'` widget type, `widgetData: { layoutId }` referencing the new system layout. Add a system layout entry to `scripts/system-layouts.json` (Pattern A path) + seed Dataverse + ship.
6. **Mount source**: §3.1 user picker (dropdown). If the operator wanted it also auto-opened from a chat action, add §3.2 Assistant → Workspace dispatch later.
7. **Dual-use?** Sections are reused from existing implementations; no new widget-as-direct registration needed.

**Pattern**: Pattern A (Dashboard wrapper, LW-internal sections that already exist). [`BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §2 covers the file edits.

### 6.2 Hypothetical Widget B — "PDF Viewer" (single-purpose, AI-launched)

**Description**: The user uploads a PDF in the chat (Assistant pane). The AI agent extracts and returns the text. The user clicks "Open as workspace tab" → a PDF viewer widget mounts in the Workspace pane.

**Walk-through**:
1. **Surfaces affected**: Assistant pane (dispatcher), Workspace pane (mount target).
2. **Composition?** NO — single sophisticated component. The PDF viewer owns the whole tab.
3. **User-customizable?** NO — viewer chrome is fixed.
4. **Unit of mount?** A widget instance with `{ documentId, blobUrl, mimeType, ... }` payload — no Dataverse layout record.
5. **Wrapper picked**: **Direct widget wrapper** (§2.2) — register `'pdf-viewer'` in `WorkspaceWidgetRegistry`, build `PdfViewerWidget` component, dispatch `widget_load { widgetType: 'pdf-viewer', widgetData: { ... } }` from the Assistant pane.
6. **Mount source**: §3.2 Assistant → Workspace (this IS the R4 W-4 demo scenario, OC-R4-07).
7. **Dual-use?** Could become dual-use later if a "documents" Dashboard layout section also wants to embed the viewer — at that point, lift `PdfViewerWidget` into `@spaarke/ui-components` (or similar shared lib) and add a LW section shim. Until then, Direct-only is fine.

**Pattern**: Pattern C (Direct widget, AI-output style). [`BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §4 covers the registration + component pattern.

### 6.3 Hypothetical Widget C — "Matter Snapshot" (dual-use, reusable in dashboards AND as standalone tab)

**Description**: A "Matter Snapshot" component that shows a single matter's metadata, recent activity, and key documents. Could be (a) a section in a "Matter Workspace" custom layout that combines Snapshot + Documents + Calendar, OR (b) a standalone tab opened from the Context pane's Create Matter wizard final step.

**Walk-through**:
1. **Surfaces affected**: Workspace (mount target), Context (dispatcher for §3.3 wizard-completion case), Assistant (could also dispatch from a "show matter" chat action — future).
2. **Composition?** Both — sometimes composed inside a layout, sometimes standalone.
3. **User-customizable?** Composition yes (as a section); standalone version no.
4. **Unit of mount**:
   - As section: section ID in `sectionsJson` + parameters (matterId, etc.) — Dashboard wrapper path.
   - As standalone: widget payload `{ matterId }` — Direct wrapper path.
5. **Wrapper picked**: **Both** (dual-use, §4).
6. **Implementation**:
   - Build `MatterSnapshotWidget` in `@spaarke/legal-domain-widgets` (or wherever matter-specific shared logic lives).
   - Register a `'matter-snapshot'` Direct widget type in `WorkspaceWidgetRegistry` for standalone case.
   - Write a 60-line LW section shim in `src/solutions/LegalWorkspace/src/sections/matter-snapshot.registration.ts` that wraps `MatterSnapshotWidget` in a `ContentSectionConfig`.
   - Add a system layout entry (optional — only if a default Matter Workspace ships).
7. **Mount sources**: §3.1 dropdown (Dashboard case), §3.3 Context wizard (Direct case), §3.2 chat action (future, Direct case).
8. **Dual-use**: YES — explicitly designed for both wrappers from day one.

**Pattern**: Pattern D (shared-lib widget + thin LW shim). [`BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §1 + §5 (Calendar worked example).

### 6.4 Decision summary

| Question | If YES | If NO |
|---|---|---|
| Does the widget compose multiple sections in one tab? | Dashboard wrapper | Direct wrapper (proceed) |
| Should users be able to re-mix / add to a layout? | Dashboard wrapper | Direct wrapper (proceed) |
| Is the mount unit a Dataverse layout record? | Dashboard wrapper | Direct wrapper (proceed) |
| Could the widget ever ship as both a section AND standalone? | Dual-use (Pattern D — shared lib + LW shim + Direct registration) | Single-wrapper (Pattern A, B, or C) |

If you cannot answer these clearly, re-read §2 and §4. The two wrappers are intentionally distinct; picking wrong leads to expensive rework (e.g. writing a Direct widget that then needs to be refactored into a section because users want composition).

---

## 7. Cross-references

### 7.1 Architecture docs

- [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — cold-load → widget render pipeline this model sits on (§2 cold-load, §3 component model, §5 BFF surface, §10 cross-references). **This doc is now cross-linked from §10 of the architecture doc.**
- [`SPAARKEAI-COMPONENT-MODEL.md`](./SPAARKEAI-COMPONENT-MODEL.md) — inventory of `@spaarke/ui-components`, `@spaarke/ai-widgets`, `@spaarke/auth`, `@spaarke/legal-workspace`, `@spaarke/events-components` — the libraries that implement the wrappers.
- [`SPAARKEAI-COMPONENTIZATION-AUDIT.md`](./SPAARKEAI-COMPONENTIZATION-AUDIT.md) — honest reuse assessment. §2A is the Calendar canonical Pattern D reference. §3 explains why `WorkspaceLayoutWidget` is hard-wired to `LegalWorkspaceApp` (intentional COST of the Dashboard wrapper choice — see §8 remediation backlog).
- [`LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md`](./LEGALWORKSPACE-EMBEDDED-MODE-CONTRACT.md) — host requirements for embedding `LegalWorkspaceApp` (introduced by R4 C-2). Read this before any code that mounts `<LegalWorkspaceApp embedded ... />` outside SpaarkeAi.
- [`LEGALWORKSPACE-RETIREMENT.md`](./LEGALWORKSPACE-RETIREMENT.md) — records the W-6 retirement of the standalone Code Page (introduced by R4 W-6). Companion to §5 of this doc.
- [`AI-ARCHITECTURE.md`](./AI-ARCHITECTURE.md) — broader AI pipeline (orchestration, tool catalog). Relevant when designing Assistant-pane widget-load dispatches (§3.2).

### 7.2 Operational guides

- [`../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) — **operator-side companion to this doc, rewritten in R4 W-2 (2026-05-26) around the two-wrapper decision tree codified here.** §1 of the guide is the wrapper-and-archetype decision tree; §4 is the Calendar (Pattern D dual-use) worked example; §8 is the anti-patterns list ("do not invent a third wrapper", "do not duplicate section code"). Read this model doc first, then the guide for hands-on implementation.
- [`../guides/SHARED-UI-COMPONENTS-GUIDE.md`](../guides/SHARED-UI-COMPONENTS-GUIDE.md) — `@spaarke/ui-components` consumption guide.

### 7.3 ADRs

- **ADR-012** — Shared component library structure. Pattern D (§4) is ADR-012-compliant by construction.
- **ADR-022** — React 19 for Code Pages. Both wrappers are React 19.
- **ADR-025** (NEW per R4 A-2) — PaneEventBus pattern. The mount sources (§3) all conform.
- **ADR-026** (NEW per R4 A-2) — Stage lifecycle + heavy library handling.
- **ADR-028** — Spaarke Auth v2. Both wrappers consume `@spaarke/auth` for any BFF calls. Direct widgets that don't need BFF (e.g. pure Xrm.WebApi widgets like Calendar) still inherit auth via Xrm.

### 7.4 Project artifacts (R4)

- [`../../projects/spaarke-ai-platform-unification-r4/spec.md`](../../projects/spaarke-ai-platform-unification-r4/spec.md) — R4 spec. DR-01 mandates this doc.
- [`../../projects/spaarke-ai-platform-unification-r4/plan.original.md`](../../projects/spaarke-ai-platform-unification-r4/plan.original.md) — R4 WBS. §4 Phase 1 has the W-1 task definition.
- [`../../projects/spaarke-ai-platform-unification-r4/backlog.md`](../../projects/spaarke-ai-platform-unification-r4/backlog.md) — Per-item rationale + 2026-05-25 scoping decisions (OC-R4-05, OC-R4-06).

---

## 8. Glossary

| Term | Definition |
|---|---|
| **Surface** | One of the three SpaarkeAi panes: Assistant (left), Workspace (center), Context (right). §1. |
| **Wrapper** | The mounting pattern that translates a `widget_load` intent into a rendered React component. Two exist: Dashboard (via `LegalWorkspaceApp`) and Direct (via `WorkspaceWidgetRegistry`). §2. |
| **Widget** | A registered renderable inside the Workspace pane. Either a Dashboard wrapper (one widget covers all layouts via `widgetType: 'workspace'`) or a Direct wrapper (one registration per single-purpose widget). |
| **Section** | A child unit inside a Dashboard wrapper layout. Sections are registered in `SECTION_REGISTRY` and composed via `sectionsJson`. §2.1, §5. |
| **Mount source** | The originator of a `widget_load` dispatch. Four exist today: user picker, Assistant, Context, workspace dropdown. §3. |
| **Channel** | A typed event stream on `PaneEventBus`. Four channels: `workspace`, `context`, `conversation`, `safety`. Workspace mounts always dispatch on the `workspace` channel. See [`SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](./SPAARKEAI-WORKSPACE-ARCHITECTURE.md) §3.3. |
| **Pattern A/B/C/D** | The four widget-design patterns codified in [`BUILD-A-NEW-WORKSPACE-WIDGET.md`](../guides/BUILD-A-NEW-WORKSPACE-WIDGET.md) §1. A = LW-internal section; B = Code Page dispatcher widget; C = AI-output widget; D = shared-lib widget + thin LW shim (Calendar canonical). |
| **Dashboard engine** | What `LegalWorkspaceApp(embedded)` is — the library code that parses `sectionsJson` and renders sections. §5. |
| **OC-R4-05** | 2026-05-25 scoping decision: standalone LegalWorkspace Code Page retired; components retained as library. §5. |
| **OC-R4-06** | 2026-05-25 scoping decision: two wrappers intentionally retained; do not propose unification. §2.3. |

---

## 9. Document changelog

- **2026-05-26 (R4 task 010 / W-1)**: Initial publication. Established the three surfaces, two-wrapper model with intentional-retention rationale (OC-R4-06), four mount sources, dual-use pattern (Calendar canonical + Daily Briefing example), LegalWorkspace-as-dashboard-engine framing (OC-R4-05). Authored to be the canonical reference for downstream R4 docs (W-2 widget-author guide rewrite, C-2 embedded-mode contract).
