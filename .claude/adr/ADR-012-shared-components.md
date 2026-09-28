# ADR-012: Shared Component Library (Concise)

> **Status**: Accepted (Amended 2026-07-12, 2026-09-04)
> **Domain**: Frontend Architecture
> **Last Updated**: 2026-09-04 (Amendment: closed 15-package enumeration + the three promotion-evaluation questions — see below)

---

## Decision

Maintain a shared TypeScript/React component library at `src/client/shared/Spaarke.UI.Components/` as the **single source of truth** for all reusable UI — components, hooks, services, types, and themes. The library is consumed by PCF controls (React 16/17), Code Pages (React 19), and the Power Pages SPA.

**Rationale**: Prevents code duplication, ensures consistent UX, and centralizes maintenance. All wizard, dialog, shell, and grid components belong in the shared library — not duplicated per-solution.

---

## Amendment (2026-07-12): `@spaarke/visuals` — Governed Presentational Sibling Package

> Path B amendment per [CLAUDE.md §6.5](../../CLAUDE.md). Introduced by the `visual-host-version-update` project. Sanctions a SECOND governed shared package alongside `@spaarke/ui-components` and restates the anti-fragmentation boundary for data-visualization primitives.

**Decision**: `src/client/shared/Spaarke.Visuals/` (`@spaarke/visuals`) is a **sanctioned, canonical shared package** — the single home for reusable **presentational data-visualization primitives** (KPI/metric cards, bar/line/donut charts, gauges, distribution bars, calendar grid, due-date cards, mini-table). It is a **sibling** to `@spaarke/ui-components`, not a fork of it.

**Why a separate package instead of folding into `@spaarke/ui-components`** — the default per root [CLAUDE.md §11](../../CLAUDE.md) is to *extend, not add*; this addition clears that bar for three concrete reasons:

1. **Heavyweight-dependency quarantine.** The viz primitives require `@fluentui/react-charting` (a large dependency). Folding them into `@spaarke/ui-components` would force **every** ui-components consumer (wizards, dialogs, grids) to pull `react-charting` transitively. Isolating it in `@spaarke/visuals` keeps the charting weight opt-in.
2. **Strict presentational purity.** `@spaarke/visuals` is **data-agnostic**: no `Xrm` / `WebAPI` / `ComponentFramework` / FetchXML — the **host binds data** and passes it in as props. This is a tighter contract than `@spaarke/ui-components` (which sanctions abstracted-I/O services via `IDataService`). Mixing the two contracts in one package blurs the boundary.
3. **`@types/react@18` pin for cross-surface JSX safety.** The package pins `@types/react@18`. React 18's `ReactNode` is a subset of React 19's, so an `@18`-typed component is assignable into **both** a React-16 PCF host and a React-19 Code Page with **no `TS2786`** JSX skew — the VisualHost extraction needed zero cast workarounds. (Contrast: importing `@spaarke/ui-components` **source**, typed `@types/react@19`, into a PCF triggers the drift documented in [ADR-022](ADR-022-pcf-platform-libraries.md#shared-library-react-version-drift).)

### Restated anti-fragmentation boundary (BINDING)

- **`@spaarke/visuals` is THE canonical home for data-viz primitives.** No solution, Code Page, or PCF may spin up a competing/ad-hoc visualization library or re-implement chart/card primitives locally. Extend `@spaarke/visuals` instead.
- **Data binding stays with the host.** Consumers fetch data (PCF `webAPI`, Code Page `Xrm.WebApi` / BFF) and pass it to `@spaarke/visuals` components as props. Fetch / query / FetchXML logic MUST NOT enter the package.
- **Card chrome, tool registries, and drill-through stay host-side** and config-driven (e.g. the VisualHost PCF's `CardChrome` + `ClickActionHandler`), composing shared components where they exist.

### When a NEW governed sibling package is justified (vs extending an existing one)

Adding another shared package is a high bar. **All three** MUST hold — otherwise extend `@spaarke/ui-components` or `@spaarke/visuals`:

1. **Distinct contract** — a cohesive capability whose contract differs from the existing packages' (e.g. data-agnostic presentational viz vs abstracted-I/O UX services).
2. **Quarantine-worthy dependency** — pulls a heavyweight/specialized dependency that existing-package consumers should not inherit transitively.
3. **Cross-surface reuse** — consumed (or credibly about to be) by 2+ surfaces (PCF / Code Page / SPA).

Absent all three, the root [CLAUDE.md §11](../../CLAUDE.md) three-question reuse test applies: extend the existing package.

**Sanctioned shared packages**: see the **closed enumeration** in the 2026-09-04 amendment below. New siblings require an ADR amendment.

---

## Amendment (2026-09-04): closed enumeration + promotion questions

> **Path B amendment per [CLAUDE.md §6.5](../../CLAUDE.md#65-adr-conflict-resolution-protocol-binding--added-2026-06-29-by-spaarkeai-compose-r1).** Introduced by `code-quality-and-assurance-r4` (spec FR-01 + FR-03), **pre-declared in that project's `spec.md` ADR Tensions table** — not a silent edit.
>
> - **Rule challenged**: the 2026-07-12 amendment's sanctioned-set sentence — *"…plus the domain component libraries (`@spaarke/events-components`, **etc.**). New siblings require an ADR amendment."*
> - **Conflict**: the two halves of that sentence contradict each other. "etc." leaves the sanctioned set **unbounded**, so any new sibling can be claimed as already covered — which makes the amendment requirement in the very next clause **unenforceable**, and unenforceable by construction rather than by neglect. An ADR cannot require an amendment to join a set whose membership it declines to state.
> - **Path**: **B (amend)** — the rule is no longer correct as written. Path C (comply) is not available: there is nothing to comply *with* while the set is open. Path A (project exception) would leave the defect in place for everyone else.
> - **Rationale**: a closed set is what makes the existing amendment rule mean something, and it is what `SharedPackageCensusTests` (FR-02) asserts against. Enumeration is also the cheaper half of the fix — the set is small and already exists on disk.
> - **Scope of the amendment**: enumeration + evaluation questions **only**. No package is added, removed, deprecated, or regressed; the 2+ consumer trigger is **unchanged**; the three-part bar for a new sibling package (above) is unchanged.

### Sanctioned shared packages — closed enumeration

**Exactly 15**, one per directory under `src/client/shared/` (enumerated from the filesystem 2026-09-04). This list is **closed**: a directory that is not on it is not sanctioned, and adding one requires an amendment to this ADR. There is deliberately no "etc."

| # | Directory | Package | Why it is in the shared library |
|---|---|---|---|
| 1 | `Spaarke.UI.Components` | `@spaarke/ui-components` | The primary library — Fluent v9 UX components, hooks, and abstracted-I/O services consumed across PCF, Code Pages, and the SPA. The default destination; every other package needs a reason not to be this one. |
| 2 | `Spaarke.Visuals` | `@spaarke/visuals` | Presentational data-viz primitives, quarantining the heavyweight `@fluentui/react-charting` dependency from every `ui-components` consumer (2026-07-12 amendment). |
| 3 | `Spaarke.Auth` | `@spaarke/auth` | Token acquisition, authenticated fetch, and the token bridge — the one auth path every surface shares (ADR-028). |
| 4 | `Spaarke.SdapClient` | `@spaarke/sdap-client` | The typed BFF/SDAP API client. Shared so request shapes and error handling cannot drift per surface. |
| 5 | `Spaarke.Events.Components` | `@spaarke/events-components` | Events/Tasks surfaces — dual-use (Pattern D): the standalone EventsPage code page **and** the SpaarkeAi Calendar workspace widget. The canonical dual-use precedent. |
| 6 | `Spaarke.SmartTodo.Components` | `@spaarke/smart-todo-components` | Smart To Do — dual-use across the LegalWorkspace section shim and SpaarkeAi widget registration. |
| 7 | `Spaarke.DailyBriefing.Components` | `@spaarke/daily-briefing-components` | Daily Briefing — dual-use across the standalone code page and the SpaarkeAi workspace widget. |
| 8 | `Spaarke.Communication.Components` | `@spaarke/communication-components` | Communications — dual-use across the `communications-list` workspace widget and the standalone `sprk_communicationspage`. |
| 9 | `Spaarke.Compose.Components` | `@spaarke/compose-components` | The TipTap-based Compose drafting workspace (editor, toolbar, workspace), mounted by the LegalWorkspace section shim. React 19; not PCF-safe. |
| 10 | `Spaarke.DocumentOperations` | `@spaarke/document-operations` | Cross-surface document verbs — Open-in-Word web/desktop, download, delete, email-link, send-to-index — shared by SemanticSearch and Compose so the verbs behave identically in both. |
| 11 | `Spaarke.AI.Context` | `@spaarke/ai-context` | AI context providers, service clients, and hooks — the shared entry to AI features for React 19 surfaces. |
| 12 | `Spaarke.AI.Widgets` | `@spaarke/ai-widgets` | Workspace and context widgets for the three-pane shell. |
| 13 | `Spaarke.AI.Outputs` | `@spaarke/ai-outputs` | Output-pane and source-pane widgets plus their component registries — the render half of the AI surface, kept separate from the context half. |
| 14 | `Spaarke.Notifications` | `@spaarke/notifications` | The host-agnostic notification-spine client: SignalR negotiate/connect, envelope routing by kind, poll fallback on disconnect. |
| 15 | `Spaarke.LegalWorkspace` | `@spaarke/legal-workspace` | A **package boundary**, not a component set — it exists to eliminate the SpaarkeAi ← LegalWorkspace source-alias trap by re-exporting a barrel over files that stay under `src/solutions/LegalWorkspace/src/`. Source-only; excluded from the build orchestrator by design (peer deps are unresolvable standalone; consumers type-check it). |

**Anticipatory promotion is legitimate and no package here is a removal candidate.** `@spaarke/visuals` was promoted ahead of broad demand and today has **one** declared consumer (the VisualHost PCF); that is a normal state for a package promoted on the strength of its contract, not a defect and not grounds for un-promotion. Likewise `@spaarke/legal-workspace` and `@spaarke/ai-context` each have a single declared consumer today. **This enumeration is not a cleanup list** — nothing on it is deprecated, regressed, or marked for removal.

### The 2+ consumer trigger is a trigger to EVALUATE, not a mandate

Two or more consumers is the point at which you **stop and ask** whether something belongs in the shared library. It is **not** a threshold that promotes automatically, and it is **not** a bar that must be cleared before promoting — anticipatory promotion at zero or one consumer remains legitimate (see `@spaarke/visuals`).

When the trigger fires, answer three questions in writing:

1. **Is the API stable across the consumers, or would each need its own branching?** Branch-per-consumer inside a shared component is a sign the commonality is shallower than it looks.
2. **Is it testable in isolation, without standing up consumer fixtures?** If the only way to test it is through a host, it is probably still host code.
3. **Is the commonality semantic or coincidental?** Two surfaces that render similar markup for unrelated reasons will diverge on the next requirement; two that express the same domain concept will not.

> **None of these three is a gate.** They are prompts for a written judgment, not criteria to clear. **A component that answers badly on any or all of them may still be promoted, provided the reason is stated.** Conversely, a component that answers well on all three is not thereby *required* to be promoted. Anyone reading this list as a checklist to pass has misread it — the output of this evaluation is a sentence of reasoning, not a score.

---

## Constraints

### MUST

- **MUST** use Fluent UI v9 components exclusively
- **MUST** import shared components via `@spaarke/ui-components`
- **MUST** use semantic tokens for theming (no hard-coded colors)
- **MUST** support dark mode and high-contrast
- **MUST** match model-driven app interaction patterns
- **MUST** export TypeScript types alongside components
- **MUST** achieve 90%+ test coverage on shared components
- **MUST** author components to be React 19-compatible (used in Code Pages)
- **MUST** verify React 16/17 compatibility for components consumed by PCF
- **MUST** use **runtime-context abstractions** for platform-specific operations (see Service Architecture below)

### MUST NOT

- **MUST NOT** mix Fluent UI versions (v9 only)
- **MUST NOT** reference PCF-specific APIs (`ComponentFramework.*`) in shared components
- **MUST NOT** hard-code Dataverse entity names or schemas as string literals (use configurable entity maps)
- **MUST NOT** use custom CSS (Fluent tokens only)
- **MUST NOT** use React 18/19-only APIs (`useTransition`, `useDeferredValue`) in components intended for PCF
- **MUST NOT** export components without tests
- **MUST NOT** call `Xrm.WebApi` or other platform APIs directly — accept via abstraction

---

## Service Architecture: What Belongs in Shared Library

The original constraint ("zero service dependencies, callback-based only") was too rigid. The shared library already contains services (`CommandRegistry`, `FetchXmlService`, `EntityCreationService`, etc.) and this is correct — services with **abstracted dependencies** are portable and testable.

### Service Portability Tiers

| Tier | What It Means | OK in Shared Library? | Examples |
|------|--------------|----------------------|----------|
| **Pure logic** | No I/O, no platform APIs, no side effects | Yes | Validators, formatters, transformers, reducers |
| **Abstracted I/O** | Accepts data service interface via props/constructor; never calls platform APIs directly | Yes | Wizard orchestrators, entity creation services, upload services |
| **Platform-bound** | Calls `Xrm.WebApi`, `ComponentFramework`, `window.parent.Xrm` directly | **No** — keep in consumer | PCF `index.ts`, Code Page `main.tsx`, ribbon scripts |

### The IDataService Pattern

Services that need to read/write data accept an abstraction, not a concrete API. Three core interfaces live in `types/`:

| Interface | Methods |
|-----------|---------|
| `IDataService` | `createRecord`, `retrieveRecord`, `retrieveMultipleRecords`, `updateRecord`, `deleteRecord` |
| `IUploadService` | `uploadFile`, `getContainerIdForEntity` |
| `INavigationService` | `openRecord`, `openDialog`, `closeDialog` |

Supporting types: `UploadOptions`, `UploadResult`, `DialogOptions`, `DialogResult`.

**Pre-built adapters** in `utils/adapters/` wire interfaces to concrete platforms:

| Adapter Factory | Target Platform |
|----------------|-----------------|
| `createXrmDataService()` | Xrm.WebApi (Code Pages in Dataverse) |
| `createXrmUploadService(bffBaseUrl)` | BFF API upload endpoints |
| `createXrmNavigationService()` | Xrm.Navigation |
| `createBffDataService(authenticatedFetch, bffBaseUrl)` | BFF API (Power Pages SPA) |
| `createBffUploadService(authenticatedFetch, bffBaseUrl)` | BFF API (SPA) |
| `createBffNavigationService(navigate?)` | SPA router |
| `createMockDataService()` | jest.fn() stubs for unit tests |
| `createMockUploadService()` | jest.fn() stubs |
| `createMockNavigationService()` | jest.fn() stubs |

Consumers call the factory in their `main.tsx` and pass the result as props — shared components never touch platform APIs directly.

### What Goes Where

| In Shared Library | In Consumer (Code Page / PCF / SPA) |
|-------------------|-------------------------------------|
| Wizard step components and orchestration | `main.tsx` entry point with platform init |
| Entity-specific form components (CreateMatterStep, etc.) | `Xrm.WebApi` / BFF adapter implementation |
| Upload service logic (dedup, validation, progress) | Platform-specific auth (`@spaarke/auth` init) |
| Business rules (validation, field defaults) | `navigateTo` / dialog opening code |
| Shell components (WizardShell, PlaybookLibraryShell) | Theme detection bootstrap |
| Shared service interfaces (`IDataService`, `IUploadService`) | Concrete service implementations |

---

## Component Inventory (v2.0.0+)

### Shell Components

| Component | Description | PCF Safe? |
|-----------|-------------|-----------|
| WizardShell / Stepper / SuccessScreen | Multi-step wizard frame | Yes |
| CreateRecordWizard | Record-creation boilerplate (file upload, follow-on steps) | Yes |
| PlaybookLibraryShell (planned) | Playbook browsing, selection, execution | Code Pages only |

### Domain Wizard Components (extracted from LegalWorkspace)

| Component | Files | PCF Safe? |
|-----------|-------|-----------|
| CreateMatterWizard | 17 files | Yes (uses CreateRecordWizard) |
| CreateProjectWizard | 10 files | Yes (uses CreateRecordWizard) |
| CreateEventWizard | 5 files | Yes (uses CreateRecordWizard) |
| CreateTodoWizard | 5 files | Yes (uses CreateRecordWizard, todoflag=true) |
| CreateWorkAssignmentWizard | 10 files | Yes (uses WizardShell directly) |
| SummarizeFilesWizard | 9 files | Code Pages only |
| PlaybookLibraryShell | 2 files | Code Pages only (intent pre-selection, browse/execute) |
| DocumentUploadWizard | — | Yes |
| FindSimilarDialog | — | Yes |

### Utilities (utils/)

| Utility | Description |
|---------|-------------|
| `resolveCodePageTheme()` | 3-level cascade: localStorage → URL param → navbar DOM → light default (no OS fallback) |
| `setupCodePageThemeListener(callback)` | Reactive theme change listener (cross-tab + custom event, no OS listener) |
| `syncThemeFromDataverse(webApi, userId)` | Async cross-device theme sync from Dataverse |
| `persistThemeToDataverse(webApi, userId, theme)` | Async theme persistence to Dataverse |
| `parseDataParams()` | Parse Xrm.Navigation.navigateTo data envelope + raw URL params |

### UI Components (16 groups)

| Component | Description | PCF Safe? |
|-----------|-------------|-----------|
| SprkButton | Button with tooltip | Yes |
| DatasetGrid (Grid/Card/List/Virtualized) | Multi-view dataset | Yes |
| ViewSelector | View mode switcher | Yes |
| CommandToolbar | Action bar | Yes |
| PageChrome | Page header (OOB parity) | Yes |
| ChoiceDialog | Simple choice dialog | Yes |
| SidePaneShell | Slide-in side panel | Yes |
| DiffCompareView | AI diff viewer | Yes |
| LookupField | Search-as-you-type lookup | Yes |
| SendEmailDialog | Email composition | Yes |
| AiSummaryPopover | AI summary with lazy fetch | Yes (deep import) |
| FindSimilarDialog | Iframe dialog shell | Yes (deep import) |
| RichTextEditor | Lexical WYSIWYG | **No** (needs jsx-runtime) |
| SprkChat | SSE streaming chat | Code Pages only |
| FileUpload | Drag-and-drop upload zone | Yes |
| AiFieldTag | AI badge for pre-filled fields | Yes |

### Code Page Wrappers (in src/solutions/, NOT in shared library)

| Wrapper | Web Resource | Description |
|---------|-------------|-------------|
| CreateMatterWizard | `sprk_creatematterwizard` | ~40 LOC thin wrapper |
| CreateProjectWizard | `sprk_createprojectwizard` | ~40 LOC thin wrapper |
| CreateEventWizard | `sprk_createeventwizard` | ~40 LOC thin wrapper |
| CreateTodoWizard | `sprk_createtodowizard` | ~40 LOC thin wrapper |
| CreateWorkAssignmentWizard | `sprk_createworkassignmentwizard` | ~40 LOC thin wrapper |
| SummarizeFilesWizard | `sprk_summarizefileswizard` | ~40 LOC thin wrapper |
| FindSimilarCodePage | `sprk_findsimilar` | Direct iframe rendering |
| PlaybookLibrary | `sprk_playbooklibrary` | Supports intent param |

### Hooks (18), Services (19+), Types (14+)

See full inventory in [docs/adr/ADR-012-shared-component-library.md](../../docs/adr/ADR-012-shared-component-library.md).

---

## PCF Import Pattern (Critical)

PCF controls **must use deep imports** to avoid pulling in Lexical/RichTextEditor which requires `react/jsx-runtime` (unavailable in React 16):

```typescript
// ✅ PCF — deep import
import { FindSimilarDialog } from "@spaarke/ui-components/dist/components/FindSimilarDialog";
import { AiSummaryPopover } from "@spaarke/ui-components/dist/components/AiSummaryPopover";

// ❌ PCF — barrel import pulls in ALL components
import { FindSimilarDialog } from "@spaarke/ui-components";

// ✅ Code Pages — barrel is safe (React 19 has jsx-runtime)
import { FindSimilarDialog, WizardShell } from "@spaarke/ui-components";
```

> **React-version type drift at this boundary**: because the shared lib carries `@types/react` 19, a PCF importing shared **source** can inherit React-19 `ReactNode` types and fail with `TS2786`. Keep the PCF-imported surface **slim** (inline small utils where only the PCF needs them) and **re-cast at the import boundary** (`x as unknown as React.ComponentType<Props>`). Full rule: [ADR-022 § Shared-Library React-Version Drift](ADR-022-pcf-platform-libraries.md#shared-library-react-version-drift).

---

## When to Add to Shared Library

> The left column lists **triggers to evaluate**, not conditions that promote automatically. When one fires, answer the [three promotion-evaluation questions](#the-2-consumer-trigger-is-a-trigger-to-evaluate-not-a-mandate) in writing — none of which is a gate.

| Add to Shared Library | Keep in Consumer |
|----------------------|------------------|
| Used by 2+ modules/surfaces (**a trigger to evaluate — not a mandate, and not a bar to clear**) | Truly module-specific rendering logic |
| Core Spaarke UX pattern (wizard, shell, grid) | Platform bootstrap code (`main.tsx`, PCF `index.ts`) |
| Service with abstracted dependencies (`IDataService`) | Concrete platform API calls (`Xrm.WebApi`) |
| Entity-specific wizard content (steps, forms) | One-off experimental UI |
| Business rules (validation, defaults, transformations) | — |

---

## Integration with Other ADRs

| ADR | Relationship |
|-----|--------------|
| [ADR-006](ADR-006-pcf-over-webresources.md) | Code Pages are default surface; shared library consumed by both |
| [ADR-021](ADR-021-fluent-design-system.md) | All components use Fluent v9 tokens |
| [ADR-022](ADR-022-pcf-platform-libraries.md) | PCF uses React 16 (platform); Code Pages use React 19 (bundled) |
| [ADR-026](ADR-026-full-page-custom-page-standard.md) | Build tooling for Code Pages (Vite + singlefile) |
| [ADR-028](ADR-028-spaarke-auth-architecture.md) | BFF service factories accept `authenticatedFetch` from `@spaarke/auth` per function-based contract |

---

## Source Documentation

- **Full ADR**: [docs/adr/ADR-012-shared-component-library.md](../../docs/adr/ADR-012-shared-component-library.md)
- **Developer Guide**: [docs/guides/SHARED-UI-COMPONENTS-GUIDE.md](../../docs/guides/SHARED-UI-COMPONENTS-GUIDE.md)

---

**Lines**: ~254
