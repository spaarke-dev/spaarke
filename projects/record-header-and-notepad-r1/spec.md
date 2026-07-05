# Record Header + Notepad — R1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-07-02
> **Source**: `design.md` (2026-07-02, Ralph Schroeder)
> **Project ID**: `record-header-and-notepad-r1`
> **Hot-path**: BFF=N · SpaarkeAi=N · CI-workflows=N · Skill-directives=N · Root-CLAUDE=N

---

## Executive Summary

Replace the OOB "record header" section on the Matter main form with a compact 5-field summary card plus a three-action toolbar (AI summary popover, related to-dos, Notepad). Ship reusable primitives in `@spaarke/ui-components` (`HeaderToolbar`, `RecordHeaderShell`, `FieldGrid`, field renderers, hooks including `useRecordHeaderToolbarActions`) plus a standalone entity-agnostic Notepad code page at `src/solutions/Notepad/`, so v2+ per-entity thin PCFs (`ProjectHeaderPcf`, `InvoiceHeaderPcf`, …) can be added in ~80 LOC each. **v1 ships shared library + Notepad code page + `MatterHeaderPcf`; Matter form binding is a follow-on maker task**. Zero BFF surface added; all Dataverse I/O via `Xrm.WebApi`. Notepad memo CRUD uses the existing `PolymorphicResolverService` in the shared lib for ADR-024 dual-field compliance.

> **Revised 2026-07-02 (post task 001)**: Field names + schema truth verified via Dataverse MCP. See [`notes/design-alignment-corrections.md`](notes/design-alignment-corrections.md) for the full accounting. Owner clarification O1 was incomplete; actual `sprk_memo` schema is ADR-024 compliant (no exception needed). `sprk_recordsummary` is a MULTILINE TEXT **field on Matter** (not a separate entity) — sparkle popover reads the field value inline.

---

## Scope

### In Scope

- **S-01** `HeaderToolbar` shared component at `src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/`
- **S-02** `RecordHeaderShell` + `FieldGrid` + field renderers (`TextField`, `LookupField`, `OptionSetField`, `TextareaField`) at `src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/`
- **S-03** Shared hooks: `useRecordFieldValues`, `useRelatedCount`, `useRecordHeaderToolbarActions`, `toolbarLaunchDefaults` at `src/client/shared/Spaarke.UI.Components/src/hooks/`
- **S-04** `MatterHeaderPcf` at `src/client/pcf/MatterHeader/` (thin PCF, ~80 LOC composition of shared primitives)
- **S-05** Notepad code page at `src/solutions/Notepad/` (Vite React 18 SPA, entity-agnostic launch contract)
- **S-06** `useSprkMemoRepository` shared repository hook (initially inside Notepad solution; promotion to `@spaarke/ui-components` when a second consumer adopts)
- **S-07** Unit tests (hooks, field renderers) + integration test (toolbar action wiring) + entity-agnostic Notepad launch test (FR-19)
- **S-08** Docs: `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` + `.claude/patterns/ui/record-header-composition.md`

### Out of Scope

- **OS-01** Matter form binding (add `MatterHeaderPcf` to the Matter main form header) — **moved to follow-on maker task per owner clarification O3**. R1 ships the PCF + library; form deployment is a separate concern.
- **OS-02** Any second entity PCF (`ProjectHeaderPcf`, `InvoiceHeaderPcf`, `EventHeaderPcf`, etc.) — each is its own thin PCF project in a follow-on.
- **OS-03** VisualHost `CardChrome` migration to consume the new `HeaderToolbar` — separate follow-on.
- **OS-04** SpaarkeAi widget adoption of `RecordHeaderShell` / `FieldGrid` — separate follow-on.
- **OS-05** Inline field editing — v1 fields are read-only.
- **OS-06** `CurrencyField`, `DateField`, `StatusField` — Matter's 5 fields don't need them; add when a consumer requires.
- **OS-07** BFF endpoints — zero. Refresh-summary wiring (see FR-08a) is a follow-on BFF project.
- **OS-08** Notepad rich-text formatting, attachments, mentions, sharing.
- **OS-09** Notepad list sorting/filtering beyond "most recent first."
- **OS-10** Retiring or replacing any existing header component on Matter.
- **OS-11** `MemoSection.tsx` in EventDetailSidePane adopting `useSprkMemoRepository` — follow-on cleanup.

### Affected Areas

- `src/client/shared/Spaarke.UI.Components/src/components/` — new components (`HeaderToolbar/`, `RecordHeader/`)
- `src/client/shared/Spaarke.UI.Components/src/hooks/` — new hooks
- `src/client/shared/Spaarke.UI.Components/src/index.ts` — new exports
- `src/client/pcf/MatterHeader/` — new PCF (created)
- `src/solutions/Notepad/` — new Vite SPA (created)
- `docs/guides/` — one new authoring guide
- `.claude/patterns/ui/` — one new pointer file
- `tests/` — new unit + integration tests
- **NOT touched**: `src/server/api/Sprk.Bff.Api/**` (NFR-07), `src/client/pcf/VisualHost/**`, `src/client/shared/Spaarke.AI.Widgets/**`, `src/solutions/EventDetailSidePane/**`

---

## Requirements

### Functional Requirements

**FR-01** `HeaderToolbar` component accepts `title?: string` and `iconSlots: IHeaderToolbarSlot[]`; renders title left-aligned (ellipsis on overflow) with icons right-aligned. Each slot supports `icon`, `onClick`, `tooltip` (required, a11y label), `badge?: number` (Fluent v9 `<CounterBadge>` overlay; suppressed when `undefined | 0`), `disabled?: boolean`. Fluent v9 semantic tokens only.
   *Acceptance*: rendering matches contract in design §3.2; every slot wrapped in `<Tooltip relationship="label">`; badge suppression when 0/undefined.

**FR-02** `RecordHeaderShell` renders a Fluent v9 card container with a `HeaderToolbar` at top and a body slot for `children`, plus a `loading?: boolean` prop that shows Fluent `Skeleton` placeholders when true.
   *Acceptance*: card padding + corner radius + border color use semantic tokens (ADR-021); skeleton visible when `loading===true`; body slot renders `children` when `loading===false`.

**FR-03** `FieldGrid` renders a CSS grid with configurable `columns` (2 or 3, default 3) and accepts `TextField` / `LookupField` / `OptionSetField` / `TextareaField` children with `span` prop (1..3). Fields flow row-by-row; a `span=3` field starts a new row.
   *Acceptance*: CSS `grid-template-columns: repeat(columns, 1fr)`; each field cell honors its `span`; visual regression covered in tests.

**FR-04** Field renderers implemented and exported:
   - `TextField` — single-line `label` + `value`, ellipsis on overflow, optional `required` marker.
   - `LookupField` — clickable link with entity-icon prefix; on click opens the lookup target via `Xrm.Navigation.navigateTo({ pageType: "entityrecord", entityName, entityId })`.
   - `OptionSetField` — renders label of selected option; renders a hyphen for null.
   - `TextareaField` — multiline value with `max-height` clamp; "show more" affordance opens a Fluent v9 popover with the full text.
   *Acceptance*: each renderer takes `label`, `value`, `span`; behavior matches design §3.3; renderers colocated in `fields/` folder for extension.

**FR-05** `useRecordFieldValues(entity, recordId, fields)` hook returns `{ values, loading, error }`; internally calls `Xrm.WebApi.retrieveRecord` with `$select` built from `fields`. Refetches when `recordId` or `fields` changes.
   *Acceptance*: single Xrm call per mount; `values` typed as `Record<string, unknown>`; error surfaces `Xrm.WebApi` failures.

**FR-06 (REVISED per task 001)** `useRelatedCount(relatedEntity, filter)` hook returns `{ count, loading, error }`; internally calls `Xrm.WebApi.retrieveMultipleRecords` with `?$filter=<filter>&$count=true&$top=0`. Hook is filter-agnostic — the consumer builds the correct OData filter expression per entity.
   *Acceptance*: `count` is `number` when loaded; used for `sprk_todo` count (filter: `_regardingobjectid_value eq {guid}`) and `sprk_memo` count via the **entity-specific lookup for the launched regarding entity** — e.g., `_sprk_regardingmatter_value eq {guid}` for Matter, `_sprk_regardingproject_value eq {guid}` for Project (per ADR-024 dual-field pattern; verified in task 001 via Dataverse MCP).

**FR-07** `useRecordHeaderToolbarActions({ entity, recordId, enabled? })` hook returns a fully-formed `IHeaderToolbarProps` with three wired icon slots (sparkle / checkmark / annotation), each behavior per FR-08/FR-09/FR-10 below. Enabled defaults are all `true`; passing `enabled: { sparkle: false }` etc. removes that slot.
   *Acceptance*: per-entity PCFs consume the hook without re-implementing toolbar wiring; disabling a slot removes it from `iconSlots` entirely (not just hides).

**FR-08 (REVISED per owner clarification O2 + task 001 correction)** Sparkle icon on click opens a **Fluent v9 popover** (not a modal) anchored to the sparkle button. Popover reads the **`sprk_recordsummary` field value directly from the current record** (already fetched by `useRecordFieldValues` as part of the 5-field header payload). `sprk_recordsummary` is a MULTILINE TEXT field on Matter (and, in future, on Project/Event/etc. — populated by an external service outside R1 scope). Popover content:
   - If `record.sprk_recordsummary` is non-empty → render the summary body inline within the popover (scrollable if long, maxWidth ~480px).
   - If empty or null → render an empty-state message ("No summary yet").
   - Popover header contains a **refresh icon** (see FR-08a).
   *Acceptance*: no separate `Xrm.WebApi` call for the summary (field is fetched with the rest of the record); no `Xrm.Navigation.navigateTo` call for sparkle in R1; popover renders summary content when present; empty-state renders otherwise. Sparkle icon has no badge.

**FR-08a (NEW per owner clarification O2 refresh answer)** A **refresh icon** is rendered inside the sparkle popover header. In R1 the refresh icon is **rendered but not wired** — click is a no-op (or displays a tooltip "Refresh available in a follow-on release"). The icon exists to establish the UI contract for a follow-on project that will wire it to a new BFF endpoint (`Sprk.Bff.Api` endpoint TBD; explicitly deferred out of R1 per NFR-07).
   *Acceptance*: refresh icon visible in popover; click does not trigger any BFF or Dataverse write in R1; tooltip states the deferral.

**FR-09** Checkmark icon on click opens the existing **SmartTodo code page** (`sprk_smarttodo_page` webresource) via `Xrm.Navigation.navigateTo` in a **Layout 1 modal (85% × 85%)**, passing `regardingEntity=<entity>` and `regardingId=<recordId>` URL parameters. Badge = live `sprk_todo` count for this record (from `useRelatedCount`).
   *Acceptance*: modal opens at 85%×85%; SmartTodo receives the params; badge count matches actual related `sprk_todo` count on mount.

**FR-10** Annotation icon on click opens the **Notepad code page** (`sprk_notepad_page` webresource) via `Xrm.Navigation.navigateTo` in a **70% × 80% modal** (specialized editor, smaller than Layout 1), passing `regardingEntity=<entity>` and `regardingId=<recordId>` URL parameters. Badge = live `sprk_memo` count for this record.
   *Acceptance*: modal opens at 70%×80%; Notepad receives the params; badge count matches actual related `sprk_memo` count on mount.

**FR-11** Badge counts refresh on component mount AND on window-focus (best-effort, no server-push). No polling interval.
   *Acceptance*: `useRelatedCount` re-queries when the browser window receives focus; measured via test.

**FR-12** `MatterHeaderPcf` reads `recordId` from `context.mode.contextInfo.entityId` (with a manifest `recordId` property override), then composes:
   ```tsx
   const entity = "sprk_matter";
   const toolbar = useRecordHeaderToolbarActions({ entity, recordId });
   const { values, loading } = useRecordFieldValues(entity, recordId, [
     "sprk_matternumber", "sprk_mattername", "sprk_mattertype",
     "sprk_practicearea", "sprk_matterdescription",
     "sprk_recordsummary", // FR-08 sparkle popover reads this
   ]);
   return (
     <RecordHeaderShell toolbar={toolbar} loading={loading}>
       <FieldGrid columns={3}>
         <TextField      span={1} label="Matter Number"      value={values.sprk_matternumber} required />
         <TextField      span={2} label="Matter Name"        value={values.sprk_mattername} />
         <LookupField    span={1} label="Matter Type"        value={values.sprk_mattertype} />
         <LookupField    span={1} label="Practice Area"      value={values.sprk_practicearea} />
         <TextareaField  span={3} label="Matter Description" value={values.sprk_matterdescription} />
       </FieldGrid>
     </RecordHeaderShell>
   );
   ```
   *Acceptance*: PCF class ≤~30 LOC (init/updateView/render/destroy per ADR-006/022); view ≤~40 LOC; total ≤ NFR-02 (100 LOC excluding shared primitives).

**FR-13** Notepad code page reads `regardingEntity` + `regardingId` from URL parameters. If either is missing, renders a Fluent v9 `MessageBar` with error text "Missing regarding context" and a "Close" button that dismisses the modal via `window.parent.postMessage` (or equivalent code-page close mechanism).
   *Acceptance*: missing params → MessageBar rendered; no CRUD attempted; Close button dismisses.

**FR-14 (REVISED per task 001)** Notepad queries `sprk_memo` records via the **entity-specific lookup for the launched regarding entity** (e.g., `_sprk_regardingmatter_value eq {guid}` for Matter, `_sprk_regardingproject_value eq {guid}` for Project — per ADR-024 dual-field pattern). `$select` includes `sprk_memoid, sprk_name, sprk_memobody, createdby, createdon, modifiedon`. Orders by `createdon desc`. Opens the most-recent record in the editor by default. If none exist, editor is empty and note-list dropdown is empty.
   *Acceptance*: query uses correct entity-specific lookup for the URL `regardingEntity` parameter; sort correct; body field is `sprk_memobody` (verified via Dataverse MCP); empty state renders cleanly.

**FR-15 (REVISED per task 001)** Notepad `+` (new memo) button creates a new `sprk_memo` row via `Xrm.WebApi.createRecord`. Payload MUST include: `sprk_name = "Untitled"` (required — NOT NULL), `sprk_memobody = ""`, and all ADR-024 resolver fields populated via **`PolymorphicResolverService.applyResolverFields()`** from `@spaarke/ui-components` (populates entity-specific lookup binding + `sprk_regardingrecordid` + `sprk_regardingrecordname` + `sprk_regardingrecordurl` + `sprk_regardingrecordtype@odata.bind` in one call). Immediately switches the editor to the new record.
   *Acceptance*: create call uses `PolymorphicResolverService.applyResolverFields()`; `sprk_name` present in payload; new record's id captured for subsequent saves. Supported parent entities: Matter, Project, Event, Invoice, Budget, WorkAssignment (per schema).

**FR-16 (REVISED per task 001)** Notepad `list` icon opens a dropdown showing prior memos (most-recent first), each item showing `sprk_name` (if non-empty) OR a derived title (first non-empty line of `sprk_memobody`, truncated to ~60 chars) + `createdon` date. Clicking an item switches the editor to that memo.
   *Acceptance*: dropdown lists all memos for this record; title-resolution logic (name-then-derived) in `utils/deriveTitle.ts`; click switches editor and flushes any pending debounced save first (per FR-17).

**FR-17 (REVISED per task 001)** Notepad saves the current `sprk_memobody` (and optionally `sprk_name` if user renames) to the current `sprk_memo` via `Xrm.WebApi.updateRecord`:
   - Immediately on **Ctrl+Enter** (explicit save).
   - Immediately on **blur** of the textarea.
   - Debounced (1 second idle) while typing.
   - **Enter** key inserts a newline (does NOT save). Per owner clarification O5.
   *Acceptance*: keybinding matches; debounce timer resets on each keystroke; three trigger paths verified in test.

**FR-18** Notepad `i` (info) icon opens a Fluent v9 popover for the current memo showing `createdby` (name) and `createdon` (formatted in the user's locale/timezone via `Intl.DateTimeFormat`).
   *Acceptance*: popover renders both fields; formatting respects browser locale.

**FR-19 (REVISED per task 001)** Notepad launch contract (`?regardingEntity=<logical>&regardingId=<guid>`) is **URL-entity-agnostic** but memo-create is **schema-limited to the 6 supported parent entities**: `sprk_matter`, `sprk_project`, `sprk_event`, `sprk_invoice`, `sprk_budget`, `sprk_workassignment` (schema-verified via Dataverse MCP task 001). If `regardingEntity` is unsupported, Notepad renders a Fluent v9 MessageBar: "Notepad does not support memos for entity type '{X}'. Contact your admin." Verified by a test-only launcher wiring a synthetic non-Matter but supported entity + record during QA (e.g. `regardingEntity=sprk_project&regardingId=<synthetic-guid>`); Notepad must render and behave identically.
   *Acceptance*: QA test documented in the authoring guide; contract fields do NOT include Matter-specific assumptions; unsupported-entity error renders correctly.

**FR-20 (REMOVED per owner clarification O3)** Matter form binding is now a follow-on maker task, not part of R1 deliverables.

### Non-Functional Requirements

**NFR-01** Header card render TTI (from PCF `init` to first paint) ≤ **300ms cached** / ≤ **800ms cold**. Measurement: manual dev-tools timing in dev/harness mode during Phase 4 QA. Not automated in CI.

**NFR-02** Per-entity thin PCF LOC ≤ **100** (excluding shared primitives). Enforcement: manual code review at PR time.

**NFR-03** All UI uses **Fluent v9 semantic tokens exclusively** (ADR-021). Zero hex/rgb literals in new components. Enforcement: `code-review` skill + reviewer eyeball.

**NFR-04** `MatterHeaderPcf` bundle size ≤ **250 KB minified** (React + Fluent + tree-shaken shared components). Measurement: `npm run build:prod` output inspected at merge time.

**NFR-05** **No `@spaarke/auth` imports** in this project — this is a host-context surface (`Xrm.WebApi` only). Enforcement: grep in code review.

**NFR-06** Shared components consumed by PCFs are **React 16/17 compatible** (ADR-022). No React 18-exclusive APIs in `HeaderToolbar`, `RecordHeaderShell`, `FieldGrid`, or field renderers. The Notepad code page (standalone SPA) MAY use React 18.

**NFR-07** **Zero new BFF endpoints** in R1. Verified via `grep` in code review of `src/server/api/Sprk.Bff.Api/`. (Refresh-summary wiring in FR-08a is deferred to a follow-on project that WILL add a BFF endpoint — out of R1 scope.)

**NFR-08** **Zero new NuGet or npm packages** beyond what `Spaarke.UI.Components` and `src/solutions/*` templates already ship. Enforcement: `package.json` diff review.

**NFR-09** Notepad launch-contract URL parameter names (`regardingEntity`, `regardingId`) are **external API surface**. Any change to name or shape is a breaking API bump and MUST be surfaced with a migration plan. Enforcement: `.claude/patterns/ui/record-header-composition.md` documents the contract; PR reviewer verifies no change without justification.

---

## Technical Constraints

### Applicable ADRs

- **ADR-006** — PCF over webresources. `MatterHeaderPcf` complies; each future entity gets its own PCF.
- **ADR-012** — Shared component library. All shared primitives + hooks live in `@spaarke/ui-components`.
- **ADR-021** — Fluent v9 semantic tokens only. Enforced across all new components (NFR-03).
- **ADR-022** — PCF platform libraries (React 16/17 compatibility for PCFs). Shared components consumed by PCFs comply (NFR-06).
- **ADR-024** — Polymorphic Resolver Pattern. `sprk_memo` fully complies (task 001 verified via Dataverse MCP): 6 entity-specific lookups (Matter/Project/Event/Invoice/Budget/WorkAssignment) + 5 resolver fields. **All memo creates MUST use `PolymorphicResolverService.applyResolverFields()`** from the shared library (populates entity-specific lookup + all 4 resolver fields in one call). Coordination with sibling project `set-regarding-and-field-mapping-resolver-r1` (adds 5th resolver field `sprk_regardingrecordnumber` — transparent to Notepad if that project ships first).
- **ADR-028** — Spaarke Auth v2. Not applicable (no BFF surface; NFR-05 forbids `@spaarke/auth`).
- **ADR-032** — BFF Null-Object kill-switch. Not applicable (no BFF surface).
- **ADR-038** — Testing strategy. Unit tests for field renderers + hooks; integration test for toolbar action wiring; test-modifying tasks trigger FULL rigor per CLAUDE.md §8.

### MUST Rules

- ✅ **MUST** use `Xrm.WebApi` for all Dataverse reads/writes; NEVER `@spaarke/auth` or BFF.
- ✅ **MUST** use Fluent v9 semantic tokens exclusively; NEVER hex/rgb literals.
- ✅ **MUST** keep shared components React 16/17-compatible (no `use()`, no `useSyncExternalStore` w/o polyfill, no React 18-exclusive concurrent APIs).
- ✅ **MUST** honor the Notepad launch contract (`regardingEntity`, `regardingId`) as an external API surface.
- ✅ **MUST** render the refresh icon (FR-08a) as unwired in R1; NEVER add a BFF endpoint to support it in R1.
- ✅ **MUST** use `PolymorphicResolverService.applyResolverFields()` for memo creation (ADR-024 dual-field compliance); NEVER re-implement resolver field wiring inside `useSprkMemoRepository`.
- ❌ **MUST NOT** add any endpoint, service, or DI registration to `src/server/api/Sprk.Bff.Api/**`.
- ❌ **MUST NOT** modify `src/client/pcf/VisualHost/**` (CardChrome migration is follow-on).
- ❌ **MUST NOT** modify `src/solutions/EventDetailSidePane/**` (MemoSection adoption is follow-on).
- ❌ **MUST NOT** re-implement toolbar wiring inside per-entity PCFs — MUST use `useRecordHeaderToolbarActions`.

### Existing Patterns to Follow

- **PCF class shape**: `src/client/pcf/CLAUDE.md` (init/updateView/render/destroy lifecycle; version footer convention).
- **Vite code-page shape**: `src/solutions/SmartTodo/` (Vite React 18 SPA; `useLaunchContext.ts` for URL param parsing is directly adaptable).
- **`sprk_memo` CRUD reference**: `src/solutions/EventDetailSidePane/src/components/MemoSection.tsx` (existing Xrm.WebApi usage against `sprk_memo`; adapt into `useSprkMemoRepository`).
- **Modal size + navigation**: `docs/standards/MODAL-DECISION-CRITERIA.md` — Layout 1 (85%×85%) for entity records (checkmark → SmartTodo); custom size (70%×80%) for specialized editors (annotation → Notepad).
- **VisualHost `CardChrome`**: `src/client/pcf/VisualHost/control/components/CardChrome.tsx` — the "closest existing toolbar" and the extract-now precedent for `HeaderToolbar`. Do NOT reuse directly (internal to VisualHost per FR-VH-05 comment); use as reference for behavior only.

---

## ADR Tensions (per CLAUDE.md §6.5)

**Revised 2026-07-02 (post task 001 schema verification)**: No ADR tensions surfaced. The originally-declared ADR-024 Path A exception was based on the incomplete owner clarification O1 ("`sprk_memo` uses a plain-text regarding field"). Task 001 verified via Dataverse MCP that `sprk_memo` **fully complies with ADR-024's dual-field polymorphic resolver pattern** (6 entity-specific lookups + 5 resolver fields). No exception needed. All ADRs (006, 012, 021, 022, 024, 028, 032, 038) apply without exception.

Path C (comply) is in effect for ADR-024: `useSprkMemoRepository` MUST use `PolymorphicResolverService.applyResolverFields()` from the shared library on create — no ad-hoc resolver logic.

See [`notes/design-alignment-corrections.md`](notes/design-alignment-corrections.md) for the full accounting of what changed and why.

---

## Success Criteria

1. [ ] `HeaderToolbar` component renders per FR-01 and is exported from `@spaarke/ui-components/src/index.ts` — Verify: import + render test in unit tests.
2. [ ] `RecordHeaderShell` + `FieldGrid` + four field renderers render per FR-02/FR-03/FR-04 — Verify: unit tests per component + one integration test composing all four in a `FieldGrid`.
3. [ ] `useRecordFieldValues`, `useRelatedCount`, `useRecordHeaderToolbarActions`, `toolbarLaunchDefaults` implemented and exported — Verify: hook unit tests (mock `Xrm.WebApi`), integration test asserting `useRecordHeaderToolbarActions` returns three correctly-wired slots.
4. [ ] `MatterHeaderPcf` builds via `npm run build:prod` and produces a solution ZIP importable to a Dataverse dev environment — Verify: PCF build + `pcf-deploy` skill dry run; solution ZIP is < NFR-04 bundle ceiling.
5. [ ] Sparkle icon click opens a Fluent v9 popover with the record's `sprk_recordsummary` body (or empty-state) and a rendered-but-unwired refresh icon (FR-08 + FR-08a) — Verify: manual QA in Matter form harness against a record with a summary and against a record without.
6. [ ] Checkmark icon opens SmartTodo code page at 85%×85% with correct URL params and shows a badge matching the actual `sprk_todo` count (FR-09) — Verify: manual QA against a Matter with N related todos.
7. [ ] Annotation icon opens Notepad code page at 70%×80% with correct URL params and shows a badge matching the actual `sprk_memo` count (FR-10) — Verify: manual QA against a Matter with N related memos.
8. [ ] Notepad supports: create memo, type body, save on Ctrl+Enter / blur / 1s idle, switch via list dropdown, view createdby/createdon via `i` popover (FR-13 through FR-18) — Verify: QA scenario walkthrough documented in the authoring guide.
9. [ ] Notepad launched with a synthetic non-Matter `regardingEntity` + `regardingId` renders and behaves identically (FR-19) — Verify: test-only launcher wiring in QA; documented in the authoring guide.
10. [ ] Every new file uses Fluent v9 exclusively; zero v8 imports; zero hex/rgb literals (NFR-03) — Verify: `code-review` skill + grep.
11. [ ] Zero BFF surface added — Verify: `git diff --stat src/server/api/Sprk.Bff.Api/` returns empty at merge.
12. [ ] `HeaderToolbar`, `RecordHeaderShell`, `FieldGrid`, field renderers, and hooks are exported from `@spaarke/ui-components` — Verify: consumer test imports each symbol.
13. [ ] `docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md` published; enables a developer to ship a new per-entity RecordHeader PCF (e.g. `ProjectHeaderPcf`) in ~80 LOC without re-reading this spec — Verify: guide walks through the pattern with a runnable example.
14. [ ] `.claude/patterns/ui/record-header-composition.md` pointer created and links to the shared-lib entry points — Verify: file present, ≤ 25 lines, follows pointer-file convention.

---

## Dependencies

### Prerequisites

- `@spaarke/ui-components` shared library must build cleanly at branch start (baseline).
- `sprk_memo` entity + `sprk_regardingrecordid` text field must exist in the target Dataverse environment (verified via owner clarification O1; format = GUID-only string).
- `sprk_recordsummary` entity must exist in the target Dataverse environment.
- `sprk_todo` entity must exist (already used by SmartTodo).
- `sprk_smarttodo_page` webresource must exist (existing SmartTodo deployment).
- `sprk_notepad_page` webresource must be registered — created by this project as part of S-05.
- Matter unmanaged solution must be present in the dev environment for QA of `MatterHeaderPcf` (but form binding itself is deferred per OS-01).

### External Dependencies

- None. No new NuGet, no new npm, no new Azure resources, no new BFF endpoints, no new Foundry deployments, no new Graph scopes, no new Service Bus queues.

---

## Owner Clarifications

*Answers captured during design-to-spec interview 2026-07-02:*

| Topic | Question | Answer | Impact |
|---|---|---|---|
| **O1 (REVISED post task 001)** `sprk_memo` regarding | How is a memo's regarding record modeled? | Owner initial answer (2026-07-02): text field `sprk_regardingrecordid`. **Corrected via Dataverse MCP verification (task 001)**: full ADR-024 dual-field pattern — 6 entity-specific lookups (Matter/Project/Event/Invoice/Budget/WorkAssignment) + 5 resolver fields (`sprk_regardingrecordtype`, `sprk_regardingrecordid`, `sprk_regardingrecordname`, `sprk_regardingrecordnumber`, `sprk_regardingrecordurl`). Owner accepted correction 2026-07-02. | **FR-06/14/15/17 all revised**. Memo queries use entity-specific lookup filter. Memo creates use `PolymorphicResolverService.applyResolverFields()`. Path C compliance with ADR-024 (was previously — incorrectly — Path A). Also body field is **`sprk_memobody`** (not `sprk_body`) and title field is **`sprk_name`** NOT NULL. |
| **O1a (REVISED)** `sprk_recordsummary` | Where does sparkle popover content come from? | Owner correction (2026-07-02): `sprk_recordsummary` is a **MULTILINE TEXT field on Matter** (populated by external service; to be added to other entities in future). Not a separate entity. VisualHost consumes similar pattern. | FR-08 revised — sparkle popover reads `record.sprk_recordsummary` inline from `useRecordFieldValues` results (no separate Xrm.WebApi call). |
| **O2** sparkle empty state | Sparkle behavior when no `sprk_recordsummary` exists? | **Click opens a Fluent v9 popover (not a modal)** showing the summary body if present or an empty-state message if not; add a **refresh icon** in the popover. | **FR-08 REVISED** — no longer opens Layout 1 modal. **FR-08a NEW** — refresh icon rendered. Sparkle popover replaces the original modal-open behavior entirely. |
| **O2a** refresh wiring | Where does refresh call regeneration? | **Not wired in R1** — icon rendered as unwired UI stub; follow-on project will add a BFF endpoint. | Preserves NFR-07 strictly in R1. FR-08a documents the deferral. Refresh tooltip states unavailability. |
| **O3** Matter form binding | Include Matter form binding in R1? | **Follow-on maker task** — NOT in R1. | **FR-20 REMOVED**. R1 scope tightens to shared lib + PCF + code page + docs. Form deployment becomes a separate maker-facing task. |
| **O5** Notepad save key | Ctrl+Enter save + Enter newline, or Enter save + Shift+Enter newline? | **Ctrl+Enter saves, Enter inserts newline** (VSCode-style). | FR-17 unchanged from design leaning; captured explicitly here for reviewer clarity. |

---

## Assumptions

*Proceeding with these assumptions where owner did not specify — flag any wrong ones during Phase 1:*

- **Notepad debounce interval**: 1 second idle — matches design §3.6. If measured throttling of `sprk_memo` writes appears during QA (R5 risk), fall back to on-Ctrl+Enter + on-blur only.
- **Badge refresh triggers**: mount + window-focus, no polling — matches design FR-11. If users complain of stale badge counts, revisit in a follow-on.
- **Notepad max body size**: `sprk_body` is assumed to be Multiple Lines of Text with the default ~1 MB limit. If the field turns out smaller, adjust the client-side textarea limit to match; report during Phase 1 discovery.
- **Version footer placement (O4)**: rendered subtly at bottom-right of `RecordHeaderShell` in **dev/harness mode only** (hidden in production) — reduces visual clutter on compact cards. Notepad code page renders version footer bottom-right unconditionally (matches other code pages). If production QA needs the footer visible on `MatterHeaderPcf`, flip a build flag; no code change.
- **TTI measurement (NFR-01)**: manual DevTools timing during Phase 4 QA against the dev environment; not automated in CI. If a future project needs automation, add a Playwright timing script then.
- **Modal size for Notepad (70%×80%)**: matches design §3.4. If users find it too small/large, revisit in a follow-on.
- **`sprk_smarttodo_page` webresource name**: assumed exact name; verify against the existing deployment during Phase 2. If different, update the launch URL in `useRecordHeaderToolbarActions`.
- **Entity-icon prefix in `LookupField`**: use the entity's OOB icon URL (`context.mode.contextInfo.entityImage` at Xrm level) or a Fluent v9 fallback icon if none. Fallback icon = `<Person24Regular>` or similar generic depending on entity family.

---

## Unresolved Questions

*Still need answers during implementation — do NOT block Phase 1 start unless flagged as BLOCKS:*

- [ ] **U-01** — Notepad "Close" mechanism when URL params missing (FR-13): does Power Apps expose a code-page-close API from within the SPA? If not, we render a "Close" button that instructs the user to close the modal manually. **Blocks**: nothing — cosmetic; discover during Phase 3.
- [ ] **U-02** — Exact entity image resolution for `LookupField` icon prefix: does `Xrm.Utility.getEntityMetadata(logicalName).then(md => md.EntityIcon)` work in the PCF context, or do we need to hardcode a mapping for common entity types? **Blocks**: nothing — falls back to a generic icon during Phase 1; refine before QA.
- [ ] **U-03** — Sparkle popover width when summary body is very long: does the Fluent v9 popover need a `maxWidth` + inner scroll, or should we truncate + link out to something else? **Blocks**: nothing — start with `maxWidth: 480px` + inner scroll; UX review during Phase 2.
- [ ] **U-04** — Refresh icon tooltip exact copy for R1's unwired state (FR-08a): "Refresh coming soon" vs "Refresh available in a follow-on release" vs no tooltip. **Blocks**: nothing — start with "Refresh available in a follow-on release" and adjust based on reviewer feedback.
- [ ] **U-05** — Should `useSprkMemoRepository` be extracted from Notepad into `@spaarke/ui-components/hooks/` at R1 wrap-up, or wait until EventDetailSidePane's MemoSection is refactored to consume it? **Blocks**: nothing — default is "wait for second consumer" per design §3.6; revisit at wrap-up.
- [ ] **U-06** — CI/CD: does `MatterHeaderPcf` need a new solution/pack pipeline entry, or does the existing PCF pack workflow cover a new PCF folder automatically? **Blocks**: solution ZIP delivery in Phase 2. Discover during Phase 2 kickoff by reading `src/client/pcf/CLAUDE.md` + inspecting existing `Solution/pack.ps1` conventions.

---

*AI-optimized specification. Original design: `design.md` (2026-07-02).*
