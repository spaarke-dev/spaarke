# Record Header + Notepad — R1

> **Status**: DRAFT — design document. Not yet a committed spec.
> **Project ID**: `record-header-and-notepad-r1`
> **Positioning**: Reusable record-header shell + field primitives + a shared toolbar-actions hook, consumed by per-entity thin PCFs (v1: `MatterHeaderPcf`). Plus a standalone Notepad code page usable from any surface.
> **Owner**: Ralph Schroeder
> **Created**: 2026-07-02

<hot-path-declaration>
  <bff>N</bff>
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-CLAUDE-md>N</root-CLAUDE-md>
</hot-path-declaration>

<!--
Hot-path declaration rationale (per CICD-061 / bff-extensions.md §G):
- bff=N: no `src/server/api/Sprk.Bff.Api/**` touches. All Dataverse reads/writes go through `Xrm.WebApi` (host-context). See §7 Placement Justification.
- spaarke-ai=N: no `@spaarke/ai-widgets` touches. This is a form-embedded PCF + a code-page modal, not a workspace widget. (The RecordHeader primitives MAY be adopted by SpaarkeAi widgets in a follow-on project — that would flip spaarke-ai to Y in that project's declaration, not this one.)
- ci-workflows=N: no `.github/workflows/**` touches.
- skill-directives=N: no `.claude/skills/**` modified. May add one `.claude/patterns/ui/record-header-composition.md` pointer file at wrap-up — pattern pointer, not a skill.
- root-CLAUDE-md=N: no changes to root CLAUDE.md expected.
-->

---

## 1. Purpose

**Replace the OOB "record header" section of Matter (and, in follow-on projects, every other main-record entity) with a compact 5-field summary card plus a three-action toolbar (AI summary, related to-dos, notepad).**

Today the Matter form's header shows platform-default field cells. Users have three unrelated actions scattered across the form and the ribbon:

- Open the AI-generated `sprk_recordsummary` for the record
- See related to-dos
- Take unstructured notes about the record

This project consolidates those actions into one compact toolbar bolted to a compact field card at the top of every main-record form. **v1 ships for Matter as `MatterHeaderPcf`.** The architecture is designed so a v2 project brings up Event, Project, Invoice, etc. by shipping a *new thin PCF per entity* (`ProjectHeaderPcf`, `InvoiceHeaderPcf`, …), each ~80 LOC, all composing the same shared primitives and calling the same shared toolbar-actions hook.

---

## 2. Product Statement

Every main-record entity gets the same compact header experience: **a card of configured fields plus three consistent toolbar actions with live badge counts, plus one Notepad UX that behaves identically on every record.** v1 ships Matter. Each future entity gets its own thin PCF (self-documenting manifest name, type-safe JSX, entity-native layout freedom) — all sharing the exact same primitives, toolbar behavior, and Notepad.

**Explicitly, the Notepad code page is not purpose-built for this PCF.** It is a standalone, entity-agnostic code page. Any Spaarke surface — ribbon buttons, workspace widgets, other PCFs, other code pages — can launch it against any entity + record via a stable URL contract.

---

## 3. Architecture

### 3.1 Component split

Shared primitives + shared toolbar-actions hook + standalone Notepad live once in the platform. Per-entity PCFs are ~80 LOC each and compose them.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                    @spaarke/ui-components (shared library)                    │
├──────────────────────────────────────────────────────────────────────────────┤
│  HeaderToolbar                    │ Generic title + icon slots with badges    │
│  RecordHeaderShell                │ Card chrome + toolbar slot + body slot    │
│  FieldGrid                        │ CSS-grid layout, span-aware children      │
│  fields/TextField                 │ Label + value renderer                    │
│  fields/LookupField               │ Clickable link with entity icon           │
│  fields/OptionSetField            │ Selected-option label renderer            │
│  fields/TextareaField             │ Multiline with "show more" affordance     │
│  hooks/useRecordFieldValues       │ Read record via Xrm.WebApi                │
│  hooks/useRelatedCount            │ Badge count for related rows              │
│  hooks/useRecordHeaderToolbar-    │ Fully-formed IHeaderToolbarProps for the  │
│         Actions                   │ 3 invariant actions (sparkle/check/anno)  │
└──────────────────────────────────────────────────────────────────────────────┘
      │                                              │
      │ composed by (thin ~80 LOC per entity)        │ launched from any surface
      ▼                                              ▼
┌────────────────────────┐          ┌───────────────────────────────────────────┐
│ MatterHeaderPcf (v1)   │          │ Notepad code page                          │
│ src/client/pcf/        │          │ src/solutions/Notepad/                     │
│   MatterHeader/        │          │                                            │
│                        │          │ Standalone SPA. Entity-agnostic URL       │
│ (future)               │          │ contract:                                  │
│ ProjectHeaderPcf       │          │   ?regardingEntity=X&regardingId=Y         │
│ InvoiceHeaderPcf       │          │                                            │
│ …one PCF per entity    │          │ Consumed by the 3 RecordHeader PCFs above │
│                        │          │ AND by any other surface: ribbon buttons,  │
│                        │          │ workspace widgets, other code pages, etc. │
└────────────────────────┘          └───────────────────────────────────────────┘
```

**Why per-entity PCFs, not one PCF with a `variant` property or one PCF with a JSON schema property**: manifest names are self-documenting to the maker; the maker cannot pick the wrong variant; each entity's layout is native, type-checked JSX (not a runtime DSL); per-entity deploys decouple release risk. Overhead is one-time-per-entity (~30 LOC of PCF boilerplate) and headers change rarely. See §8 Component Justification for the full analysis.

### 3.2 `HeaderToolbar` — generic reusable toolbar

**Location**: `src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/`

**Contract**:

```typescript
export interface IHeaderToolbarProps {
  title?: string;                    // optional left-side text
  iconSlots: IHeaderToolbarSlot[];   // 0..N right-aligned icons
}

export interface IHeaderToolbarSlot {
  key: string;                       // stable id for React keys
  icon: React.ReactNode;             // Fluent v9 icon component
  onClick: () => void | Promise<void>;
  tooltip: string;                   // required — a11y label
  badge?: number;                    // undefined | 0 = no badge; >0 = badge
  disabled?: boolean;
}
```

**Rendering**:
- Title truncates with ellipsis.
- Icons right-aligned, `subtle` appearance, small size, `spacingHorizontalXS` gap.
- Badge = Fluent v9 `<CounterBadge>` overlaid top-right of icon button. Suppressed when `undefined | 0`.
- Every slot wrapped in `<Tooltip>` with `content={tooltip}` and `relationship="label"` for a11y.

**Styling**: ADR-021 (Fluent v9 semantic tokens only, no hex).

**Not exported from anywhere else**: this is the ONE shared toolbar. VisualHost `CardChrome` is a separate follow-on migration candidate (see §8).

### 3.3 `RecordHeaderShell` + `FieldGrid` + `fields/` — composable primitives

**Location**: `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/`

These are the *composition primitives* per-entity PCFs use. No schema, no DSL — just typed React components.

**`RecordHeaderShell`** — outer card chrome + toolbar slot + body slot.

```typescript
export interface IRecordHeaderShellProps {
  toolbar: IHeaderToolbarProps;      // fully-formed toolbar props
  loading?: boolean;                 // shows skeleton grid when true
  children: React.ReactNode;         // typically a FieldGrid
}
```

Rendering: Fluent v9 card container, `HeaderToolbar` rendered at the top, body slot below. Loading state shows Fluent `Skeleton` placeholders. Card padding, corner radius, border color: semantic tokens per ADR-021.

**`FieldGrid`** — CSS-grid layout wrapper for field cells.

```typescript
export interface IFieldGridProps {
  columns?: 2 | 3;                   // default 3
  children: React.ReactNode;         // field renderer children
}
```

Rendering: CSS grid with `grid-template-columns: repeat(columns, 1fr)`; each field cell reads its own `span` prop (see below) via a shared style hook. Fields flow row-by-row; a `span=3` field starts a new row.

**Field renderer components** (each takes `label`, `value`, `span`):

- `TextField` — single-line label + value, ellipsis on overflow.
- `LookupField` — clickable link with entity-icon prefix, opens the lookup target via `Xrm.Navigation.navigateTo({ pageType: "entityrecord", ... })`.
- `OptionSetField` — label of the selected option (or a hyphen for null).
- `TextareaField` — multiline value, `max-height` clamp with "show more" affordance opening a Fluent v9 popover with the full text.

Additional field renderers (`CurrencyField`, `DateField`, `StatusField`) are **not shipped in v1** (Matter's 5 fields don't need them) but are trivial to add when v2 entities require them.

**Why "field renderer components" instead of a switch inside FieldGrid**: adding a new field type in the future is "add a new component file" — no central enum, no schema validator to update, no test-matrix inflation.

### 3.4 `useRecordHeaderToolbarActions` — shared toolbar-actions hook

**Location**: `src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts`

The three toolbar actions (sparkle → `sprk_recordsummary`, checkmark → related to-dos, annotation → Notepad) are **invariant across every entity**. Extracted to a shared hook so per-entity PCFs never re-implement toolbar wiring.

**Contract**:

```typescript
export interface IUseRecordHeaderToolbarActionsOptions {
  entity: string;                    // logical name, e.g. "sprk_matter"
  recordId: string;                  // GUID
  enabled?: {                        // opt out of specific icons if needed
    sparkle?: boolean;               // default true
    checkmark?: boolean;             // default true
    annotation?: boolean;            // default true
  };
}

export function useRecordHeaderToolbarActions(
  options: IUseRecordHeaderToolbarActionsOptions
): IHeaderToolbarProps;              // ready to hand to RecordHeaderShell
```

**Implementation** (summary):
- Fetches badge counts for todos (`sprk_todo`) and memos (`sprk_memo`) via `useRelatedCount`, refreshed on mount and on window-focus.
- Builds three `IHeaderToolbarSlot` entries with wired `onClick` handlers:
  - **Sparkle**: query `sprk_recordsummary` where regarding = this record; if found, `Xrm.Navigation.navigateTo` opens it at 85% × 85% (Layout 1 per R2 modal standard); if not found, opens create mode with regarding pre-set. Sparkle icon has no badge.
  - **Checkmark**: `Xrm.Navigation.navigateTo` opens the SmartTodo code page (existing webresource) with `regardingEntity` + `regardingId` params. Badge = live `sprk_todo` count.
  - **Annotation**: `Xrm.Navigation.navigateTo` opens the Notepad code page (`sprk_notepad_page`) at 70% × 80% with `regardingEntity` + `regardingId` params. Badge = live `sprk_memo` count.

**Per-entity PCFs** use it as:

```tsx
const toolbar = useRecordHeaderToolbarActions({ entity, recordId });
return <RecordHeaderShell toolbar={toolbar}> ... </RecordHeaderShell>;
```

If a future entity needs to hide one of the actions (unlikely but possible), it passes `enabled: { sparkle: false }`.

**Layout constants** for the modals opened by the hook live in a small `toolbarLaunchDefaults.ts` module in the shared lib — matching R2's Layout 1 (85% × 85%) for sparkle/checkmark; Notepad uses 70% × 80% because it's a specialized proprietary editor, not an entity form.

### 3.5 Per-entity thin PCF pattern (v1: `MatterHeaderPcf`)

**Location**: `src/client/pcf/MatterHeader/`

**Manifest properties**:

| Property | Type | Usage | Purpose |
|---|---|---|---|
| `recordId` | `SingleLine.Text` | `input` | Optional override; defaults to `context.mode.contextInfo.entityId`. |

No `entityName` property — this PCF is entity-specific (`sprk_matter` is compile-time-fixed in the React component). No `fieldSchema` property — fields are typed JSX.

**Class shape** (matches ADR-006 / ADR-022):

```typescript
export class MatterHeader implements ComponentFramework.StandardControl<IInputs, IOutputs> {
  private root: HTMLDivElement;
  private context: ComponentFramework.Context<IInputs>;

  public init(context, notifyOutputChanged, state, container) {
    this.root = container;
    this.context = context;
  }
  public updateView(context) {
    this.context = context;
    this.render();
  }
  private render() {
    const recordId = this.context.parameters.recordId?.raw
                  || this.context.mode.contextInfo.entityId;
    ReactDOM.render(
      React.createElement(FluentProvider, { theme: webLightTheme },
        React.createElement(MatterHeaderView, { recordId })
      ),
      this.root
    );
  }
  public destroy() {
    ReactDOM.unmountComponentAtNode(this.root);
  }
}
```

**`MatterHeaderView.tsx`** — the entity-specific React component:

```tsx
export interface IMatterHeaderViewProps { recordId: string; }

export const MatterHeaderView: React.FC<IMatterHeaderViewProps> = ({ recordId }) => {
  const entity = "sprk_matter";
  const toolbar = useRecordHeaderToolbarActions({ entity, recordId });
  const { values, loading } = useRecordFieldValues(entity, recordId, [
    "sprk_matternumber", "sprk_name", "sprk_mattertype",
    "sprk_practicearea", "sprk_description",
  ]);

  return (
    <RecordHeaderShell toolbar={toolbar} loading={loading}>
      <FieldGrid columns={3}>
        <TextField      span={1} label="Matter Number"      value={values.sprk_matternumber} required />
        <TextField      span={2} label="Matter Name"        value={values.sprk_name} />
        <LookupField    span={1} label="Matter Type"        value={values.sprk_mattertype} />
        <LookupField    span={1} label="Practice Area"      value={values.sprk_practicearea} />
        <TextareaField  span={3} label="Matter Description" value={values.sprk_description} />
      </FieldGrid>
    </RecordHeaderShell>
  );
};
```

Roughly 40 LOC of view + 30 LOC of PCF class + ~10 LOC of `version.ts` = ~80 LOC total for a new entity. Future `ProjectHeaderPcf` and `InvoiceHeaderPcf` follow the identical shape — a new folder under `src/client/pcf/`, a new manifest, a new `<Entity>HeaderView.tsx`.

**No auth bootstrap needed**: PCFs use `Xrm.WebApi` only (host-context) — no BFF calls. `@spaarke/auth` is NOT imported. See §7 Placement Justification.

**Version footer** (per `src/client/pcf/CLAUDE.md`): rendered subtly at bottom-right of the card, style matches other PCFs.

### 3.6 Notepad code page — standalone, reusable, entity-agnostic

**Location**: `src/solutions/Notepad/` (Vite React 18 SPA, follows `src/solutions/SmartTodo/` pattern).

**This is explicitly not purpose-built for `MatterHeaderPcf`.** It is a general-purpose Spaarke code page any surface can invoke to take unstructured notes about any record. Its contract is entity-agnostic.

**Launch contract**:

```
URL:  <power-apps-host>/main.aspx?pagetype=webresource&webresourceName=sprk_notepad_page&data=regardingEntity=<logical>%26regardingId=<guid>
```

Any surface may call it via:

```typescript
Xrm.Navigation.navigateTo(
  { pageType: "webresource", webresourceName: "sprk_notepad_page",
    data: `regardingEntity=${entity}&regardingId=${recordId}` },
  { target: 2, position: 1, width: {value: 70, unit: '%'}, height: {value: 80, unit: '%'} }
);
```

Expected consumers today and in the future:
- The RecordHeader PCFs (via `useRecordHeaderToolbarActions`) — this project's primary use
- Any form ribbon button ("Take a note on this record")
- Any workspace widget ("Notes" side action)
- Other code pages that want to embed a "take a note" link
- Any Spaarke surface with an entity + record context

If either URL param is missing, Notepad renders a MessageBar error and offers to close.

**Data model — one memo per topic, appended**:

- Uses `sprk_memo` entity. Assumed to have (needs verification — §12 open question O1):
  - `sprk_memoid` (PK)
  - `sprk_body` (Multiple lines of text, ~1MB)
  - `regardingobjectid` (polymorphic lookup) OR a specific `_sprk_regardingid_value`
  - `createdby`, `createdon`, `modifiedon` (system fields)
- "Note" in the UI = one `sprk_memo` record.
- First line of `sprk_body` = derived title (never persisted separately).
- Save = Ctrl+Enter (default) commits current buffer to the current memo's `sprk_body` via `Xrm.WebApi.updateRecord`. Enter inserts newline.

**UI shape**:
- Top bar: title (derived from first line, or "Untitled" if empty).
- Body: `<textarea>` bound to current memo body.
- Left/top action row: `+` (new memo), `list` (dropdown of prior memos on this record, click to switch).
- Subtle `i` info button opens a Fluent v9 popover with "Created by {name} on {date}".
- Auto-save on blur AND on Ctrl+Enter. Debounced save (1s idle) while typing to reduce write pressure.

**No BFF calls**: all reads/writes via `Xrm.WebApi`. No `@spaarke/auth`.

**Shared repository hook — `useSprkMemoRepository`**: extracted so this code page AND the existing [`MemoSection.tsx`](../../src/solutions/EventDetailSidePane/src/components/MemoSection.tsx) can eventually share the CRUD logic. In v1 the hook lives inside the Notepad solution folder; if `EventDetailSidePane` adopts it in a follow-on, we promote to `@spaarke/ui-components/hooks/`.

**Version footer**: rendered subtly at bottom-right of the code page.

### 3.7 Matter form binding (v1 consumer)

- Add `MatterHeaderPcf` to the Matter main form's header section (or replace the entire header — TBD spec) with `recordId` bound to the form's primary id.
- No other Matter-form change in v1.

---

## 4. Scope Summary

### In scope (v1)

| # | Deliverable | Location |
|---|---|---|
| 4.1 | `HeaderToolbar` shared component | `src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/` |
| 4.2 | `RecordHeaderShell` + `FieldGrid` + `fields/` primitives | `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/` |
| 4.3 | `useRecordFieldValues` + `useRelatedCount` hooks | `src/client/shared/Spaarke.UI.Components/src/hooks/` |
| 4.4 | `useRecordHeaderToolbarActions` shared hook | `src/client/shared/Spaarke.UI.Components/src/hooks/` |
| 4.5 | `MatterHeaderPcf` | `src/client/pcf/MatterHeader/` |
| 4.6 | Notepad code page (entity-agnostic) | `src/solutions/Notepad/` |
| 4.7 | Matter form binding | Matter unmanaged solution |
| 4.8 | Unit tests (hooks, field renderers) + integration test (toolbar wiring) | `tests/` |
| 4.9 | Documentation: `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` + `.claude/patterns/ui/record-header-composition.md` | Docs |

### Out of scope

- **Any second entity PCF** (`ProjectHeaderPcf`, `InvoiceHeaderPcf`, etc.). v1 ships Matter only. Each is a separate ~80 LOC follow-on project.
- **VisualHost `CardChrome` migration** to consume the new `HeaderToolbar`. Separate follow-on.
- **SpaarkeAi widget adoption** of `RecordHeaderShell` / `FieldGrid`. Separate follow-on project.
- **Field editing** — v1 fields are read-only. Inline editing is v2+.
- **`CurrencyField`, `DateField`, `StatusField`** — not needed by Matter's 5 fields; add when a consumer needs them.
- **BFF surface changes**. Zero. See §7 Placement Justification.
- **Notepad rich-text formatting**, attachments, mentions, sharing. v1 is plaintext-only.
- **Notepad list sorting / filtering** beyond "most recent first."
- **Retirement of any existing header component** on Matter. This project ADDS the new header alongside; the maker chooses when to swap.
- **Adopting `useSprkMemoRepository` in `EventDetailSidePane`** — extract now, adopt elsewhere later.

### Follow-on (out of scope for R1, tracked)

- **v2 entity expansion** — `ProjectHeaderPcf`, `InvoiceHeaderPcf`, `EventHeaderPcf`, etc. Each is its own thin PCF project, ~80 LOC + boilerplate. Follows the `MatterHeaderPcf` template.
- **VisualHost `CardChrome` → `HeaderToolbar` migration** — removes one duplicate toolbar per §8.
- **`useSprkMemoRepository` promotion** to shared lib once a second consumer adopts it.
- **Notepad rich text / attachments** — if users ask for it.
- **Inline field editing** — save via `Xrm.WebApi.updateRecord`.
- **`CurrencyField`, `DateField`, `StatusField`** — with the first entity that needs them (probably Invoice).

---

## 5. Goals / Non-goals

### Goals

- G1. One shared toolbar in `@spaarke/ui-components` that other components can (and will) reuse.
- G2. One shared record-header composition kit (shell + field grid + field renderers) that per-entity PCFs compose in typed JSX.
- G3. One shared toolbar-actions hook so per-entity PCFs never re-implement toolbar wiring.
- G4. A Notepad UX that is standalone and entity-agnostic — the PCF's use of it must not encode assumptions that prevent other surfaces from launching it.
- G5. Zero BFF surface additions.
- G6. Ship Matter v1 quickly, without over-designing for entities we haven't started using yet.

### Non-goals

- NG1. **Not** a comprehensive form redesign. Only the header region.
- NG2. **Not** a replacement for OOB inline field editing. v1 is read-only.
- NG3. **Not** a rich-text editor. Notepad is plaintext.
- NG4. **Not** an entity-management framework. This project surfaces existing entities (`sprk_recordsummary`, `sprk_todo`, `sprk_memo`); it doesn't create new ones.
- NG5. **Not** dependent on `set-regarding-and-field-mapping-resolver-r1`. v1 ships without it.
- NG6. **Not** a runtime-configurable framework. Each entity's header is a considered developer artifact, not a Power Apps maker knob.

---

## 6. Requirements

### Functional Requirements (draft — `/design-to-spec` will formalize)

**FR-01** `HeaderToolbar` accepts `title?`, `iconSlots: IHeaderToolbarSlot[]`, renders title-left + icons-right with badge support and Fluent v9 semantic tokens only.

**FR-02** `RecordHeaderShell` renders a card container with a `HeaderToolbar` at top and a body slot for `children`, with a loading state showing Fluent Skeleton placeholders.

**FR-03** `FieldGrid` renders a CSS grid with configurable `columns` (2 or 3, default 3) and accepts `TextField` / `LookupField` / `OptionSetField` / `TextareaField` children with `span` (1..3) props.

**FR-04** Field renderers: `TextField`, `LookupField` (clickable, opens lookup target via `Xrm.Navigation.navigateTo`), `OptionSetField` (label of selected option), `TextareaField` (multiline w/ show-more affordance).

**FR-05** `useRecordFieldValues(entity, recordId, fields)` returns `{ values, loading, error }`; internally calls `Xrm.WebApi.retrieveRecord` with `$select` built from `fields`.

**FR-06** `useRelatedCount(entity, recordId, relatedEntity)` returns `{ count, loading, error }`; internally calls `Xrm.WebApi.retrieveMultipleRecords` with `$count=true&$top=0`.

**FR-07** `useRecordHeaderToolbarActions({ entity, recordId, enabled? })` returns a fully-formed `IHeaderToolbarProps` with three wired icon slots (sparkle / checkmark / annotation) matching the behaviors in §3.4. Enabled defaults are all `true`.

**FR-08** Sparkle icon opens the `sprk_recordsummary` record for the current record via Layout 1 modal (Xrm.Navigation.navigateTo, 85%×85%). If no summary record exists, opens create mode with regarding pre-set.

**FR-09** Checkmark icon opens the SmartTodo code page filtered to this record's to-dos via Layout 1 modal (webresource pageType, 85%×85%). Badge = live `sprk_todo` count.

**FR-10** Annotation icon opens the Notepad code page for this record via a 70%×80% webresource modal. Badge = live `sprk_memo` count.

**FR-11** Badge counts refresh on component mount and on window-focus (best-effort — no server-push).

**FR-12** `MatterHeaderPcf` reads `recordId` from `context.mode.contextInfo.entityId` (with prop override), calls `useRecordHeaderToolbarActions({ entity: "sprk_matter", recordId })` and `useRecordFieldValues("sprk_matter", recordId, [...5 fields])`, and renders `RecordHeaderShell` + `FieldGrid` + typed field children per §3.5.

**FR-13** Notepad code page reads `regardingEntity` + `regardingId` from URL params. If either missing, renders MessageBar error and offers to close.

**FR-14** Notepad lists all `sprk_memo` for the launched (entity, recordId) most-recent-first; opens the most-recent for edit by default.

**FR-15** Notepad `+` creates a new empty `sprk_memo` (regarding = the launched record) and switches focus to it.

**FR-16** Notepad `list` icon dropdown shows prior memos with derived-title preview; click switches to that memo.

**FR-17** Notepad saves on Ctrl+Enter (immediate), on blur (immediate), and on 1s idle typing (debounced). Enter inserts newline.

**FR-18** Notepad `i` icon popover shows createdby (name) + createdon (formatted via user's timezone) for the current memo.

**FR-19** Notepad launch contract MUST be entity-agnostic — verified by a second launcher wired in a follow-on (e.g. a ribbon button on a non-Matter entity). Design surface tested by launching Notepad against a synthetic non-Matter record during QA.

**FR-20** Matter form binding: `MatterHeaderPcf` added to the Matter main form header section, `recordId` bound to primary id.

### Non-functional Requirements (draft)

**NFR-01** Header card render TTI (from PCF `init` to first paint) ≤ 300ms on cached load, ≤ 800ms on cold load.

**NFR-02** Per-entity PCF LOC ≤ 100 (excluding shared primitives). Enforcement: manual code review.

**NFR-03** All UI uses Fluent v9 semantic tokens exclusively (ADR-021). Zero hex/rgb literals in components.

**NFR-04** `MatterHeaderPcf` bundle size ≤ 250KB minified (React + Fluent + shared components tree-shaken).

**NFR-05** No `@spaarke/auth` imports in this project — this is a host-context PCF (ADR-028 doesn't apply to `Xrm.WebApi`-only surfaces).

**NFR-06** All new components React 16/17 compatible (ADR-022). No React 18-exclusive APIs in components consumed by PCFs.

**NFR-07** Zero new BFF endpoints. Verified via `grep` in code review.

**NFR-08** Zero new NuGet or npm packages beyond what the shared library already ships.

**NFR-09** Notepad launch contract stability: URL params `regardingEntity` + `regardingId` MUST NOT change name or shape across releases (contract for external launchers). Any change is a breaking API bump.

---

## 7. Placement Justification (per CLAUDE.md §10)

**No BFF surface is added by this project.** This section is included per the §10 requirement to make the placement decision explicit even when the answer is "not in BFF."

**Decision**: All Dataverse reads and writes route through `Xrm.WebApi` (host-context, form-embedded and code-page-embedded).

**Reasoning against a BFF surface**:
1. **No cross-cutting logic**. Reading a record's fields, counting related rows, reading/writing a memo record is native to the model-driven runtime; a BFF hop adds no value.
2. **No auth extension needed**. `Xrm.WebApi` uses the form/code-page's authenticated context. Adding a BFF path would require `@spaarke/auth` bootstrap, adding ~40KB to the bundle for zero user benefit.
3. **`sprk_recordsummary`, `sprk_todo`, `sprk_memo` are Dataverse entities** with direct Web API access. No AI orchestration, no Graph, no Service Bus, no third-party integration — none of the BFF's reason-to-exist applies.
4. **Latency**. `Xrm.WebApi` is ~20-50ms; BFF hop adds ~100-300ms of network + auth overhead. For a header that renders on every form load, this matters.

**Consulted**: `.claude/constraints/bff-extensions.md` decision criteria. All four criteria for "belongs in BFF" fail for this workload:
- ❌ Requires AI or cross-service orchestration
- ❌ Requires Graph, SharePoint Embedded, or third-party API
- ❌ Requires app-only auth
- ❌ Requires job queueing / background work

---

## 8. Component Justification (per CLAUDE.md §11)

For every new surface added by this project, answering the three-question template.

### 8.1 `HeaderToolbar` (new shared component)

- **Existing overlap**: `CardChrome` in `src/client/pcf/VisualHost/control/components/CardChrome.tsx` — visually and behaviorally similar (title + right-icon slots with tooltip + expand + AI-sparkle contract), but explicitly marked `INTERNAL to Visual Host. MUST NOT be exported from @spaarke/ui-components` (per FR-VH-05, comment lines 9–11).
- **Extension**: No. `CardChrome`'s contract is contract-locked to VisualHost's per-chart drill-through semantics. Reusing it would break its "internal, contract-not-yet-stable" invariant and pull VisualHost-specific concerns (`showAiSparkle`, `onAiSummary`, chart-def wiring) into unrelated consumers.
- **Cost-of-doing-nothing**: Every future header/toolbar consumer duplicates the same DOM structure. Concrete failure mode: this project ships a toolbar; a future SpaarkeAi widget refresh ships a second parallel toolbar; VisualHost keeps its third. Three overlapping toolbars is the anti-pattern §11 catches. **Extract now, once.**
- **Follow-on**: `CardChrome` migration to consume `HeaderToolbar` — tracked as follow-on.

### 8.2 `RecordHeaderShell` + `FieldGrid` + `fields/*` (new shared primitives)

- **Existing overlap**: None. No current component surfaces "form-header replacement with configurable fields." OOB Power Apps form header is the only comparable surface and it's not a React component.
- **Extension**: N/A — nothing to extend.
- **Cost-of-doing-nothing**: Without shared primitives, `MatterHeaderPcf` reimplements card chrome + grid layout + field renderers inline; `ProjectHeaderPcf` copy-pastes it; `InvoiceHeaderPcf` copy-pastes it. Concrete failure mode: three copies of card chrome and grid layout — the exact §11 anti-pattern.
- **Design shape**: composable primitives, not a schema-driven framework. Per-entity PCFs compose them in typed JSX. Adding a new field renderer is "add a new component file" — no schema enum to keep in sync.

### 8.3 Shared hooks — `useRecordFieldValues`, `useRelatedCount`, `useRecordHeaderToolbarActions`

- **Existing overlap**: Ad-hoc `Xrm.WebApi.retrieveRecord` and `retrieveMultipleRecords` calls scattered across widget components. `useLaunchContext` (SmartTodo) is a similar shape for URL parsing but not for record reads.
- **Extension**: The ad-hoc calls could stay ad-hoc, but they'd stay ad-hoc — no single hook. This project centralizes.
- **Cost-of-doing-nothing**: Every future record-read consumer writes its own `useEffect` + `Xrm.WebApi.retrieveRecord` + error/loading state. Concrete failure mode: Notepad + each RecordHeader PCF + badge counts all need the same hook shape; without extraction, three copies today, N copies over time.
- **`useRecordHeaderToolbarActions` specifically**: The three toolbar actions are 100% invariant across every entity. Extracting them means every RecordHeader PCF becomes `const toolbar = useRecordHeaderToolbarActions({ entity, recordId });` — zero re-implementation risk across `MatterHeaderPcf` / `ProjectHeaderPcf` / `InvoiceHeaderPcf` / …

### 8.4 `MatterHeaderPcf` (new PCF) and the per-entity PCF pattern

- **Existing overlap**: None. VisualHost is chart-focused; other PCFs are entity-specific (SemanticSearchControl, DocumentRelationshipViewer, etc.). None substitute the OOB form header.
- **Extension**: N/A.
- **Cost-of-doing-nothing**: Without a PCF, `RecordHeaderShell` and `FieldGrid` can't render on a model-driven form. Concrete failure mode: the shared primitives would be shelf-ware.
- **Per-entity PCFs vs. one PCF with a variant selector or a JSON schema**: chose per-entity because manifest names are self-documenting to makers, layouts are native typed JSX (no DSL), and per-entity deploys decouple release risk. Overhead is ~30 LOC of PCF class boilerplate per new entity — one-time-per-entity cost, headers change rarely. See §3.1 for the fuller analysis. This decision changes as the entity count scales past ~10 with uniform layouts and maker-driven configurability — reassess then.

### 8.5 Notepad code page (`src/solutions/Notepad/`)

- **Existing overlap**: `MemoSection.tsx` in `src/solutions/EventDetailSidePane/src/components/MemoSection.tsx` reads/writes `sprk_memo` — same entity, single-memo model, tightly coupled to EventDetailSidePane's side-pane layout.
- **Extension**: `MemoSection` isn't shaped as a reusable code page (single memo, no list, no `+`, no dropdown, no popover, embedded not launched). Extracting its CRUD calls into a shared hook (`useSprkMemoRepository`) is worth doing — Notepad and EventDetailSidePane can both consume the same hook. The UI surface (list + editor + toolbar) is genuinely new.
- **Cost-of-doing-nothing**: Users have no "take notes about a record" flow on any entity except Event. Concrete failure mode: this project would either not ship the notepad or fork MemoSection. Forking is the anti-pattern.
- **Reusability discipline**: the Notepad code page's launch contract is entity-agnostic (§3.6 URL contract). Its use inside `useRecordHeaderToolbarActions` is one consumer among many possible. NFR-09 pins the launch contract as an external API surface. FR-19 requires validating the entity-agnostic launch by a second launcher during QA.

### 8.6 What we're NOT adding

- **No new Dataverse entity**. `sprk_recordsummary`, `sprk_todo`, `sprk_memo` all exist.
- **No new BFF endpoint**. `Xrm.WebApi` covers everything.
- **No new NuGet or npm package**.
- **No "one PCF, N variants"** design. Each entity gets its own thin PCF.
- **No JSON schema DSL**. Fields are typed JSX; layouts are native React.

---

## 9. ADR Tensions (per CLAUDE.md §6.5)

No ADR conflicts anticipated. Adjacent ADRs and how this project satisfies each:

| ADR | Rule | How satisfied |
|---|---|---|
| ADR-006 | PCF over webresources | `MatterHeaderPcf` is a PCF (each future entity gets its own PCF too). ✅ |
| ADR-012 | Shared component library | `HeaderToolbar`, `RecordHeaderShell`, `FieldGrid`, `fields/`, hooks all in `@spaarke/ui-components`. ✅ |
| ADR-021 | Fluent v9 semantic tokens only | Enforced in all new components. ✅ |
| ADR-022 | PCF platform libraries (React 16/17 compat) | No React 18-exclusive APIs in shared components consumed by PCFs. Notepad code page can use React 18 (it's a standalone SPA, not PCF). ✅ |
| ADR-024 | 11-entity regarding relationship | Notepad uses `sprk_memo`'s regarding relationship. Depends on whether `sprk_memo` is one of the 11 or uses a dedicated lookup — see §12 open question O1. |
| ADR-028 | Spaarke Auth v2 | Not applicable — no BFF surface. Explicitly no `@spaarke/auth` imports (NFR-05). ✅ |
| ADR-032 | BFF Null-Object kill-switch | Not applicable — no BFF surface. |
| ADR-038 | Testing strategy | Unit tests for field renderers + hooks; integration test for toolbar action wiring. ✅ |

If implementation surfaces an ADR conflict (e.g. `sprk_memo` regarding turns out to require a custom scaffold), invoke §6.5 A/B/C protocol.

---

## 10. File Surface

### New files

**Shared library** (`src/client/shared/Spaarke.UI.Components/`):

```
src/components/HeaderToolbar/
  HeaderToolbar.tsx
  types.ts
  index.ts
  README.md
  __tests__/HeaderToolbar.test.tsx

src/components/RecordHeader/
  RecordHeaderShell.tsx
  FieldGrid.tsx
  fields/
    TextField.tsx
    LookupField.tsx
    OptionSetField.tsx
    TextareaField.tsx
    index.ts
  types.ts
  index.ts
  README.md
  __tests__/
    RecordHeaderShell.test.tsx
    FieldGrid.test.tsx
    fields.test.tsx

src/hooks/
  useRecordFieldValues.ts
  useRelatedCount.ts
  useRecordHeaderToolbarActions.ts
  toolbarLaunchDefaults.ts
  __tests__/
    useRecordFieldValues.test.ts
    useRelatedCount.test.ts
    useRecordHeaderToolbarActions.test.ts

src/index.ts   (edit to add exports)
```

**PCF** (`src/client/pcf/MatterHeader/`):

```
ControlManifest.Input.xml
control/
  index.ts               (PCF class)
  MatterHeaderView.tsx   (React composition)
  version.ts
Solution/
  solution.xml
  Controls/sprk_Spaarke.Records.MatterHeader/ControlManifest.xml
  pack.ps1
```

**Code page** (`src/solutions/Notepad/`):

```
package.json
vite.config.ts
tsconfig.json
index.html
src/
  main.tsx
  App.tsx
  components/
    NotepadShell.tsx
    MemoList.tsx
    MemoEditor.tsx
    CreatedByPopover.tsx
  hooks/
    useSprkMemoRepository.ts
    useLaunchContext.ts   (adapt from SmartTodo)
  types/
    memo.ts
  utils/
    deriveTitle.ts
```

**Documentation**:

```
docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md   (new — how to build a new per-entity PCF)
.claude/patterns/ui/record-header-composition.md    (new pointer)
```

### Modified files

- `src/client/shared/Spaarke.UI.Components/src/index.ts` — add exports.
- `src/client/shared/Spaarke.UI.Components/package.json` — no new deps expected.
- Matter unmanaged solution form XML (add `MatterHeaderPcf` to the header section).

### Files NOT to touch

- `src/client/pcf/VisualHost/**` — `CardChrome` stays internal; its migration to `HeaderToolbar` is follow-on, not R1.
- `src/server/api/Sprk.Bff.Api/**` — zero touches (NFR-07 enforcement).
- `src/client/shared/Spaarke.AI.Widgets/**` — SpaarkeAi widget adoption is a separate follow-on.
- `src/solutions/EventDetailSidePane/src/components/MemoSection.tsx` — leave untouched in R1; MemoSection adoption of `useSprkMemoRepository` is a follow-on cleanup.

---

## 11. Risks

| # | Risk | Mitigation |
|---|---|---|
| R1 | `sprk_memo` schema doesn't support the "regarding = Matter" pattern we assume | §12 O1 — verify entity schema before implementation begins. Falls back to §6.5 path B (ADR-024 amendment) or path C (use OOB `annotation`). |
| R2 | Per-entity PCFs drift out of sync with shared primitives (e.g. `MatterHeaderPcf` locks to shared-lib v1.0, `ProjectHeaderPcf` bumps to v1.1) | Shared lib is a workspace-local dep, not a published package — all PCFs build against the same version at build time. Enforcement: shared lib version bump requires rebuild of all consuming PCFs. |
| R3 | Layout 1 modal (85%×85%) not appropriate for `sprk_recordsummary` — it's a small entity | Reassess after playing with the UX. May use a smaller Layout 1 size or a Fluent v9 popover instead. Not blocking. |
| R4 | Existing SmartTodo webresource URL isn't clean to construct from `useRecordHeaderToolbarActions` | Verify smart-todo entry-point contract during design of §3.4. May need a small adapter. |
| R5 | Notepad `sprk_memo` write frequency (debounced + Ctrl+Enter + blur) causes throttling on active users | 1s debounce + on-Ctrl+Enter is standard; measure in test. Falls back to on-Ctrl+Enter-only if measured throttling. |
| R6 | Bundle size creep for the shared lib | NFR-04 sets per-PCF bundle ceiling. Shared lib additions measured against baseline in test. |

---

## 12. Open Questions

**O1. `sprk_memo` schema — what is the regarding relationship?**
- Is it a polymorphic `regardingobjectid` (like `sprk_todo`, per ADR-024)?
- Or a dedicated `sprk_regardingid` lookup?
- What's the exact `sprk_body` field type — Multiple lines of text (unlimited)?
- **Action**: verify by reading Dataverse metadata OR by inspecting `MemoSection.tsx` in EventDetailSidePane before `/design-to-spec` runs.

**O2. Sparkle behavior when no `sprk_recordsummary` exists yet**
- Open in create mode with regarding pre-set? Or show a "generate" affordance?
- **Action**: decide during spec.

**O3. Where does Matter form binding actually ship?**
- Include in this project as a Matter unmanaged solution update? Or leave as a follow-on maker task?
- Argument for including: v1 shipping without a binding = no real ship.
- Argument for excluding: form updates are a separate deployment concern.
- **Action**: decide during spec — leaning include.

**O4. Version-footer placement inside `RecordHeaderShell` / `MatterHeaderView`**
- PCFs display a version footer at bottom-right per `src/client/pcf/CLAUDE.md`.
- The card is quite compact; a footer might visually clutter.
- Alternative: show only when in dev/harness mode; hide in prod.
- **Action**: decide during spec.

**O5. Confirm Ctrl+Enter (save) vs. Enter (save) for Notepad**
- Ctrl+Enter for save + Enter for newline = current design leaning (matches VSCode-like note-taking apps).
- Some users may expect Enter-to-save (Slack/Teams style).
- **Action**: decide during spec; probably Ctrl+Enter default.

---

## 13. Rollout

1. **Phase 1** — Shared library: `HeaderToolbar`, `RecordHeaderShell`, `FieldGrid`, `fields/*`, `useRecordFieldValues`, `useRelatedCount`, `useRecordHeaderToolbarActions`. Landable as a standalone PR with unit tests.
2. **Phase 2** — `MatterHeaderPcf` + Matter form binding. Landable as a PR after Phase 1 merges.
3. **Phase 3** — Notepad code page. Landable as a PR after Phase 2 or in parallel (independent surface). Includes an entity-agnostic launch test (FR-19).
4. **Phase 4** — Documentation + pattern pointer. Landable as a docs PR any time after Phase 1.

Each phase is independently mergeable per the R2 precedent.

---

## 14. Success Criteria

- Matter form renders `MatterHeaderPcf` with 5 fields as typed JSX composition in `MatterHeaderView.tsx`.
- Sparkle opens `sprk_recordsummary` for the current Matter in a Layout 1 modal.
- Checkmark opens the SmartTodo code page filtered to this Matter's to-dos, badge shows accurate count.
- Annotation opens the Notepad code page in a modal, badge shows accurate `sprk_memo` count for this Matter.
- Notepad: user can create a memo, type body, save on Ctrl+Enter/blur/debounce, switch to another memo via list dropdown, see createdby/createdon via `i` popover.
- Notepad launched from a non-Matter surface (test-only wiring) with a synthetic `regardingEntity` + `regardingId` renders and behaves identically — proves the launch contract is entity-agnostic.
- Every new file is Fluent v9, no v8 imports.
- Zero BFF surface added — verified in code review by grepping `src/server/api/Sprk.Bff.Api/` for new endpoints (expect 0).
- `HeaderToolbar`, `RecordHeaderShell`, `FieldGrid`, field renderers, and hooks exported from `@spaarke/ui-components` for future reuse.
- Authoring Guide published enabling a developer to ship a new per-entity RecordHeader PCF (`ProjectHeaderPcf`) in a follow-on project without re-reading this design.

---

## 15. References

- Root [`CLAUDE.md`](../../CLAUDE.md) — repo-wide operational rules
- [`CLAUDE.md §10 — BFF Hygiene`](../../CLAUDE.md#10-bff-hygiene--binding-governance-read-before-adding-to-sprkbffapi) — placement decision framework
- [`CLAUDE.md §11 — Component Justification`](../../CLAUDE.md#11-component-justification--default-to-reuse-binding) — three-question template
- [`CLAUDE.md §6.5 — ADR Conflict Resolution`](../../CLAUDE.md#65-adr-conflict-resolution-protocol-binding--added-2026-06-29) — A/B/C protocol
- [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — BFF decision criteria
- [`docs/standards/MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md) — Layout 1 / Layout 2 modal patterns
- [`src/client/pcf/CLAUDE.md`](../../src/client/pcf/CLAUDE.md) — PCF module rules
- [`src/client/pcf/VisualHost/control/components/CardChrome.tsx`](../../src/client/pcf/VisualHost/control/components/CardChrome.tsx) — the closest existing toolbar; the "extract now" precedent for `HeaderToolbar`
- [`src/solutions/EventDetailSidePane/src/components/MemoSection.tsx`](../../src/solutions/EventDetailSidePane/src/components/MemoSection.tsx) — existing `sprk_memo` consumer
- Related project: [`projects/set-regarding-and-field-mapping-resolver-r1/design.md`](../set-regarding-and-field-mapping-resolver-r1/design.md) — future field-mapping resolver, not required by v1
- Related project: [`projects/ai-spaarke-ai-workspace-UI-r2/design.md`](../ai-spaarke-ai-workspace-UI-r2/design.md) — Layout 1 / Layout 2 modal standard used by toolbar actions
