# Record Header + Notepad — R2

> **Status**: DRAFT — starter design document, seeded during R1 wrap-up 2026-07-05.
> **Project ID**: `record-header-and-notepad-r2`
> **Positioning**: Extend the R1 record-header pattern to four additional entities (Project, Invoice, Work Assignment, Event) using the R1 primitives as the canonical pattern. Bundle DEF-06 (shared-lib `exports` field) and DEF-08 (`useSprkMemoRepository` promotion to shared lib) as structural improvements delivered as part of the same effort.
> **Owner**: Ralph Schroeder
> **Created**: 2026-07-05 (seeded during R1 close-out — NOT yet run through `/design-to-spec`)

<hot-path-declaration>
  <bff>N</bff>
  <spaarke-ai>N</spaarke-ai>
  <ci-workflows>N</ci-workflows>
  <skill-directives>N</skill-directives>
  <root-CLAUDE-md>N</root-CLAUDE-md>
</hot-path-declaration>

<!--
Hot-path declaration inherited from R1: pure host-context surface + shared lib + PCFs. No BFF, no SpaarkeAi widgets, no workflow/skill/root-CLAUDE.md touches. If the shared-lib `exports` field change (§7.1) forces `pcf-scripts/tsconfig_base.json` updates across every PCF, the ci-workflows flag may need to flip to Y depending on CI implications — evaluate during `/design-to-spec`.
-->

---

## 1. Purpose

**Bring the R1 record-header + Notepad pattern to Project, Invoice, Work Assignment, and Event entities**, using the same shared primitives that ship v1.0.19 of `MatterHeaderPcf`. Bundle two structural improvements that reduce ecosystem friction:

1. **Shared-lib `exports` field migration** (DEF-06 reforward) so downstream PCFs and Code Pages get clean subpath imports without the fragile `dist/*` deep-path convention R1 lands with.
2. **Promotion of `useSprkMemoRepository` to `@spaarke/ui-components`** (DEF-08) — a second consumer emerges as soon as any of the four new entity PCFs ships, satisfying the CLAUDE.md §11 "extend existing when a second consumer appears" trigger.

The R1 architecture explicitly forecast this: v1 shipped Matter; v2 brings up Project / Invoice / Work Assignment / Event by shipping a *new thin PCF per entity* (~80 LOC each), all composing the same shared primitives and calling the same shared toolbar-actions hook.

---

## 2. Product Statement

Every main-record entity in scope gets the same compact record-header experience as Matter: **a card of configured fields plus three consistent toolbar actions (AI summary / related to-dos / notepad) with live badge counts.** The Notepad UX and SmartTodo openTodos filter continue to behave identically across every entity — no per-entity Notepad or per-entity Kanban.

**Cross-environment portability** is a first-class R2 requirement. The R1 deployment package (MatterHeaderPcf solution ZIP + Notepad webresource + SmartTodo webresource + `sprk_gridconfiguration` records) transferred cleanly between environments during UAT because R1 avoided all hardcoded environment-specific values (record GUIDs, environment names, tenant IDs). R2 MUST preserve that guarantee across four new PCF solutions and any shared-lib repackaging that ships.

---

## 3. Scope

### 3.1 In scope

**Per-entity PCFs** (in shipping-priority order — reviewer confirms with `/design-to-spec`):

1. **`ProjectHeaderPcf`** — for `sprk_project`
2. **`InvoiceHeaderPcf`** — for `sprk_invoice`
3. **`WorkAssignmentHeaderPcf`** — for `sprk_workassignment`
4. **`EventHeaderPcf`** — for `sprk_event`

Each PCF is a thin composition of the R1 primitives — target ~80 LOC per PCF as R1 demonstrated with `MatterHeaderPcf`.

**Structural improvements** (bundle in same R2 effort):

- **DEF-06 reforward** — `exports` field on `@spaarke/ui-components/package.json` + `pcf-scripts/tsconfig_base.json` migration to `moduleResolution: "bundler"`. R1 attempted this and reverted because the migration ripples across every PCF in the repo. R2 embraces the ripple — the four new PCFs land alongside the migration.
- **DEF-08** — promote `useSprkMemoRepository` from `src/solutions/Notepad/src/hooks/` to `@spaarke/ui-components/hooks/` once a second consumer emerges. All four new entity PCFs need this consumer surface if they inherit R1's annotation icon behavior.

**Cross-environment portability requirements** (universal MUST):

- No hardcoded record GUIDs anywhere in shipped PCF bundles / webresources.
- Any `sprk_gridconfiguration` records or similar env-scoped Dataverse rows MUST be created by the deployment procedure, not implicit.
- Deployment package MUST work against a brand-new environment with only the R2 solution ZIP + accompanying webresource files + a documented `sprk_gridconfiguration` seed.
- All environment-dependent values (BFF URL, tenant ID, etc. — none currently in R1 scope but future-proof against R3+) MUST be sourced from Power Apps configuration surfaces, not baked in.

### 3.2 Out of scope for R2

- BFF endpoints of any kind (NFR-07 continues to hold — sparkle refresh still renders unwired; see R2 sequencing note in §7.3).
- Retiring or replacing any existing header component beyond what R1 already retired.
- VisualHost `CardChrome` migration to consume `HeaderToolbar` (DEF-03 — remains in-code pointer only; separate R2B project when someone touches VisualHost).
- EventDetailSidePane `MemoSection` adoption of the promoted `useSprkMemoRepository` (DEF-04 — remains documented; separate R2B project when someone touches EventDetailSidePane).
- Rich-text formatting, attachments, mentions, or sharing on the Notepad (out of R1 too; still out).

### 3.3 Not-in-scope — natural boundary

- Any entity beyond the four listed here. The pattern makes adding future entities easy; a future R3 / R4 handles those on demand.

---

## 4. R1 primitives consumed (canonical pattern)

All R2 PCFs consume the following R1 outputs verbatim. **No forking, no per-entity re-implementations of primitives.** If a primitive is missing behavior, that behavior lands in the shared lib, then all four PCFs pick it up.

### 4.1 Shared library — `@spaarke/ui-components`

| Primitive | Purpose | R1 file |
|---|---|---|
| `HeaderToolbar` | Generic title + icon slots with badges | [`src/components/HeaderToolbar/HeaderToolbar.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/HeaderToolbar/HeaderToolbar.tsx) |
| `RecordHeaderShell` | Card chrome wrapping toolbar + field grid | [`src/components/RecordHeader/RecordHeaderShell.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/RecordHeaderShell.tsx) |
| `FieldGrid` | 5-field grid layout | [`src/components/RecordHeader/FieldGrid.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/FieldGrid.tsx) |
| Field renderers (`TextField`, `LookupField`, `OptionSetField`, `TextareaField`) | Type-safe read-only cells | [`src/components/RecordHeader/fields/*.tsx`](../../src/client/shared/Spaarke.UI.Components/src/components/RecordHeader/fields) |
| `useRecordFieldValues` | Batched Xrm.WebApi field-value fetcher | [`src/hooks/useRecordFieldValues.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRecordFieldValues.ts) |
| `useRelatedCount` | Related-record count for badges — reads `entities.length` (NOT `@odata.count`; see [pattern doc](../../.claude/patterns/pcf/xrm-webapi-related-count.md)) | [`src/hooks/useRelatedCount.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRelatedCount.ts) |
| `useRecordHeaderToolbarActions` | Fully-wired toolbar props for the three canonical actions | [`src/hooks/useRecordHeaderToolbarActions.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/useRecordHeaderToolbarActions.ts) |
| `toolbarLaunchDefaults` | `SUPPORTED_MEMO_PARENTS`, `SUPPORTED_TODO_PARENTS`, LAYOUT constants | [`src/hooks/toolbarLaunchDefaults.ts`](../../src/client/shared/Spaarke.UI.Components/src/hooks/toolbarLaunchDefaults.ts) |
| `AiSummaryPopover` | Shared AI-summary popover for sparkle icon | [`src/components/AiSummaryPopover/`](../../src/client/shared/Spaarke.UI.Components/src/components/AiSummaryPopover) |
| `themeStorage` (dark mode) | Cross-frame theme resolution | [`src/utils/themeStorage.ts`](../../src/client/shared/Spaarke.UI.Components/src/utils/themeStorage.ts) |

### 4.2 Standalone code pages

| Code page | Purpose | R1 file |
|---|---|---|
| Notepad (`sprk_notepad`) | Entity-agnostic note-taking modal | [`src/solutions/Notepad/`](../../src/solutions/Notepad) |
| SmartTodo (`sprk_smarttodo`) with openTodos filter | Kanban modal pre-filtered by regarding record | [`src/solutions/SmartTodo/`](../../src/solutions/SmartTodo) |

Both code pages are **entity-agnostic**. R2's per-entity PCFs launch them with the same URL contracts R1 uses — no per-entity modifications.

### 4.3 Reference PCF (canonical R1 implementation)

- **`MatterHeaderPcf` v1.0.19** — [`src/client/pcf/MatterHeader/`](../../src/client/pcf/MatterHeader) — the ~80-LOC composition each R2 PCF mirrors.

### 4.4 Reference documents

- **Authoring guide** — [`docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md`](../../docs/guides/RECORD-HEADER-PCF-AUTHORING-GUIDE.md) — the step-by-step recipe for a new entity PCF.
- **Pattern pointer** — [`.claude/patterns/ui/record-header-composition.md`](../../.claude/patterns/ui/record-header-composition.md) — 25-line pointer into the R1 code.
- **PCF build scaffold** — [`.claude/patterns/pcf/pcf-build-scaffold.md`](../../.claude/patterns/pcf/pcf-build-scaffold.md) — the 10 build gotchas captured across R1 UAT.
- **Related-count pattern** — [`.claude/patterns/pcf/xrm-webapi-related-count.md`](../../.claude/patterns/pcf/xrm-webapi-related-count.md) — the `@odata.count` trap + client-side counting fix.
- **R1 lessons-learned** — [`../record-header-and-notepad-r1/notes/lessons-learned.md`](../record-header-and-notepad-r1/notes/lessons-learned.md) — 12 lessons captured through v1.0.19 including the 6 Phase-6 addendum lessons.
- **R1 spec** — [`../record-header-and-notepad-r1/spec.md`](../record-header-and-notepad-r1/spec.md) — 21 FRs / 9 NFRs; the R2 spec inherits/extends this.

---

## 5. Per-PCF requirements

The following four subsections are the **starter requirement set per entity**. Each entity's `sprk_recordsummary` field, related-record entities (`sprk_todo`, `sprk_memo`), and layout preferences will be fully specified during `/design-to-spec`.

### 5.1 `ProjectHeaderPcf` — `sprk_project`

- **Bound entity**: `sprk_project`
- **Fields to display** (5-field card, TBD-CONFIRM in `/design-to-spec`):
  - `sprk_projectname` (primary name)
  - `sprk_projectstatus` (option set)
  - `sprk_projectowner` (lookup → systemuser)
  - `sprk_startdate` (date)
  - `sprk_targetenddate` (date)
- **Sparkle popover source**: `sprk_recordsummary` field on `sprk_project` (verify field exists in target env during discovery)
- **Checkmark badge**: `sprk_todo` count where `_sprk_regardingproject_value eq <projectId>` (ADR-024 lookup — already supported by R1's `SUPPORTED_TODO_PARENTS`)
- **Annotation badge**: `sprk_memo` count where `_sprk_regardingproject_value eq <projectId>` (already supported)
- **Notepad launch**: `regardingEntity=sprk_project&regardingId=<id>`
- **SmartTodo launch**: `action=openTodos&regardingType=sprk_project&regardingId=<id>`
- **Form binding target**: main form of `sprk_project`
- **Estimated effort**: 4–6 hours (per R1 authoring-guide estimate)

### 5.2 `InvoiceHeaderPcf` — `sprk_invoice`

- **Bound entity**: `sprk_invoice`
- **Fields to display** (5-field card, TBD-CONFIRM):
  - `sprk_name` (primary name — verify per R1's task-001 pattern before locking scope)
  - `sprk_invoicenumber` (text)
  - `sprk_invoiceamount` (currency)
  - `sprk_invoicestatus` (option set)
  - `sprk_duedate` (date)
- **Sparkle popover source**: `sprk_recordsummary` field on `sprk_invoice`
- **Checkmark badge**: `_sprk_regardinginvoice_value eq <invoiceId>` (supported by R1)
- **Annotation badge**: same
- **Notepad launch**: `regardingEntity=sprk_invoice&regardingId=<id>`
- **SmartTodo launch**: `action=openTodos&regardingType=sprk_invoice&regardingId=<id>`
- **Form binding target**: main form of `sprk_invoice`
- **Estimated effort**: 4–6 hours

### 5.3 `WorkAssignmentHeaderPcf` — `sprk_workassignment`

- **Bound entity**: `sprk_workassignment`
- **Fields to display** (5-field card, TBD-CONFIRM):
  - `sprk_name` (primary name — verify)
  - `sprk_assignmentstatus` (option set)
  - `sprk_assignedto` (lookup)
  - `sprk_startdate` (date)
  - `sprk_estimatedhours` (number)
- **Sparkle popover source**: `sprk_recordsummary` field on `sprk_workassignment`
- **Checkmark badge**: `_sprk_regardingworkassignment_value eq <id>` (supported by R1)
- **Annotation badge**: same
- **Notepad launch**: `regardingEntity=sprk_workassignment&regardingId=<id>`
- **SmartTodo launch**: `action=openTodos&regardingType=sprk_workassignment&regardingId=<id>`
- **Form binding target**: main form of `sprk_workassignment`
- **Estimated effort**: 4–6 hours

### 5.4 `EventHeaderPcf` — `sprk_event`

- **Bound entity**: `sprk_event`
- **Fields to display** (5-field card, TBD-CONFIRM):
  - `sprk_eventname` (primary name)
  - `sprk_eventtype` (option set or lookup — verify)
  - `sprk_eventstart` (date+time)
  - `sprk_eventend` (date+time)
  - `sprk_eventlocation` (text)
- **Sparkle popover source**: `sprk_recordsummary` field on `sprk_event`
- **Checkmark badge**: `_sprk_regardingevent_value eq <id>` (supported by R1)
- **Annotation badge**: same
- **Notepad launch**: `regardingEntity=sprk_event&regardingId=<id>`
- **SmartTodo launch**: `action=openTodos&regardingType=sprk_event&regardingId=<id>`
- **Form binding target**: main form of `sprk_event`
- **Estimated effort**: 4–6 hours

### 5.5 Common per-PCF acceptance criteria

Each of the four PCFs MUST:

- Consume the R1 shared-lib primitives verbatim (no forked field renderers, no reimplemented toolbar hook).
- Bundle-size ceiling: same as `MatterHeaderPcf` — ≤250 KB minified (R1 shipped 62.4 KiB; per-entity should hit the same order of magnitude).
- Emit a version footer identical to `MatterHeaderPcf`'s convention.
- Ship a solution ZIP that imports cleanly to a fresh environment with only the R1 shared-lib dist prerequisites already imported.

---

## 6. Structural improvements bundled

### 6.1 DEF-06 reforward — `exports` field + `moduleResolution: bundler`

**Why now**: R2 ships four new PCFs at once. Doing the `exports` migration once, before the four new PCFs land, means all four benefit from clean subpath imports (e.g., `@spaarke/ui-components/hooks/useRelatedCount` instead of `@spaarke/ui-components/dist/hooks/useRelatedCount`). Retrofitting after the four ship is 5× the work.

**Scope of change**:
1. `@spaarke/ui-components/package.json` — add `exports` map covering every consumer-facing subpath. Fully enumerated (no wildcard fallback — R1's attempted wildcard collided with Webpack's directory-index resolution).
2. `pcf-scripts/tsconfig_base.json` — bump `moduleResolution` from `"node"` to `"bundler"` (or `"node16"`). This propagates to every PCF in the repo.
3. Every existing PCF's imports of `@spaarke/ui-components/dist/*` — migrate to the clean subpath.
4. Every existing Code Page's imports of `@spaarke/ui-components/dist/*` — same.

**Rollout risk**: touches every PCF in the repo. Before merging, ALL PCF solution ZIPs must be rebuilt + smoke-tested. Coordinate with any other active worktrees so nobody's mid-flight when the migration lands.

**Reference**: R1's attempt is documented in `../record-header-and-notepad-r1/plan-extension.md` § "DEF-06 reverted 2026-07-04" and the R1 lessons-learned. Read those before starting.

### 6.2 DEF-08 — promote `useSprkMemoRepository` to `@spaarke/ui-components`

**Why now**: The four new PCFs need Notepad launch behavior — but do they need the *repository hook* directly? Only IF they render inline memo display (like the retired R1 CreatedByPopover). If not, this promotion may be premature.

**Decision to confirm during `/design-to-spec`**: Do any of the four PCFs render memo content inline? If YES, promote the hook. If NO, defer to the next second-consumer emergence.

**Alternative trigger**: EventDetailSidePane's `MemoSection.tsx` (DEF-04). If EventDetailSidePane refactor lands in the same window, that IS the second consumer.

**Scope of change if promoted**:
1. Move `src/solutions/Notepad/src/hooks/useSprkMemoRepository.ts` → `src/client/shared/Spaarke.UI.Components/src/hooks/useSprkMemoRepository.ts`.
2. Move `src/solutions/Notepad/src/hooks/discoverMemoNavProps.ts` → same shared-lib target.
3. Update Notepad to import from the shared lib.
4. Add exports to shared-lib `index.ts` (and, if §6.1 lands first, the `exports` map).
5. Cover with shared-lib tests (currently the tests live in `src/solutions/Notepad/src/hooks/__tests__/`).

---

## 7. Cross-environment portability (BINDING for every deliverable)

### 7.1 Universal rule

**No hardcoded environment-specific identifiers in any shipped bundle or webresource.** R1 was already clean on this axis — R2 preserves it. Concretely:

- No literal record GUIDs (e.g., matter IDs used as test data during dev).
- No literal environment names (e.g., "dev-spaarke", "prod-spaarke").
- No literal tenant IDs or subscription IDs.
- No literal user IDs, contact IDs, or business unit IDs.

### 7.2 Configuration surface

Where per-environment values are genuinely needed (currently none in R2 scope), source them from:

- Power Apps environment variables (`sprk_variableName`) — the canonical Dataverse-hosted config surface.
- PCF manifest parameters exposed to the maker at form-binding time.
- Runtime discovery via `Xrm.Utility.getGlobalContext()`.

**Not acceptable**: `window.SPAARKE_*` globals baked into the bundle, or `.env` files inlined at build time.

### 7.3 Deployment package portability check

Every R2 deliverable ships with a portability check step in its `/task-execute` protocol:

- Take the produced solution ZIP + webresource files + any accompanying `sprk_gridconfiguration` seed records.
- Import to a fresh (or refreshed) environment.
- Verify the PCF renders, toolbar actions launch, badge counts fetch — with no additional environment-specific configuration.
- If ANY step of that verification fails without a fresh manual configuration, that deliverable has hardcoded state and MUST be fixed before merge.

---

## 8. Applicable ADRs (inherited from R1 + new considerations)

Inherited from R1 spec:
- **ADR-006** — Prefer PCF over webresources.
- **ADR-011** — Dataset PCF over subgrids (principle only; not directly applicable to these thin PCFs).
- **ADR-012** — Shared component library — all primitives + hooks in `@spaarke/ui-components`.
- **ADR-021** — Fluent UI v9 semantic tokens only.
- **ADR-022** — PCF platform libraries; shared lib stays React 16/17-safe.
- **ADR-024** — Polymorphic resolver pattern; `sprk_memo` uses ADR-024 Path C dual-field.
- **ADR-028** — Spaarke Auth v2 (N/A — host-context only).
- **ADR-038** — Testing strategy; integration-heavy pyramid.

New potential ADR touch (evaluate during `/design-to-spec`):
- If §6.1 forces a `moduleResolution` bump across every PCF, that may warrant its own ADR documenting the migration decision. Not a violation of any existing ADR; just enshrines the choice.

---

## 9. Explicit non-goals

- No changes to the R1 shipped surface (MatterHeaderPcf v1.0.19). R2 adds; it does not modify R1 code.
- No changes to SmartTodo internals beyond what R1 already did (openTodos consumer wiring).
- No introduction of any BFF surface (NFR-07 continues).
- No modification of VisualHost or EventDetailSidePane (see §3.2 Out of scope — those remain R2B candidates).

---

## 10. Risks & Mitigations (starter set)

| Risk | Impact | Likelihood | Mitigation |
|---|---|---|---|
| The four entities' `sprk_recordsummary` field is not populated by any current process | Sparkle popover shows empty state on every record | Med | Verify during `/design-to-spec` per-entity discovery; either seed data OR document as follow-on. |
| DEF-06 migration breaks a PCF outside R2 scope | Regression in an unrelated PCF | Med | Full rebuild + smoke of every PCF in the repo before merge (formalized in the task-execute protocol). |
| Per-entity form binding requires maker access we don't have during dev | PCF ships but nobody sees it | Low | R1 already handled this with the maker checklist (`../record-header-and-notepad-r1/notes/matter-form-binding-instructions.md`). Replicate per entity. |
| One of the four entities' `sprk_recordsummary` semantics differs from Matter's | Sparkle refresh behavior needs per-entity awareness | Low | R1 sparkle refresh is intentionally UNWIRED; no per-entity behavior differs today. When Insights Engine wires refresh, per-entity nuance is that project's problem. |
| Bundle-size ceiling hit by some entity's field set | PCF exceeds NFR-04 250 KB | Very low | R1 shipped 62.4 KiB; the shared-lib footprint is dominant, not per-entity code. |

---

## 11. Next steps (from R1 → R2 pipeline)

1. **Discovery pass** — verify each entity's schema via Dataverse MCP:
   - Primary name field, primary-key field, `sprk_recordsummary` field existence, related-entity lookups.
2. **Run `/design-to-spec`** on this document — produces `spec.md` with numbered FRs / NFRs and per-entity acceptance criteria.
3. **Run `/project-pipeline`** to seed the R2 worktree + task list.
4. **Confirm §6.2 DEF-08 promotion decision** during `/design-to-spec` — does any of the four PCFs render memo content inline?
5. **Schedule §6.1 DEF-06 migration** as the FIRST task in R2, since it affects every subsequent PCF build.

---

## 12. Related projects / deferrals

- **DEF-01** (sparkle refresh → BFF regen endpoint) — absorbed by future **Insights Engine / AI Summary** project. Not in R2 scope.
- **DEF-03** (VisualHost CardChrome migration) — R2B candidate when someone touches VisualHost. In-code pointer added to R1's pattern doc during wrap-up.
- **DEF-04** (EventDetailSidePane MemoSection adoption) — R2B candidate when someone touches EventDetailSidePane. In-code pointer added to `useSprkMemoRepository.ts` JSDoc during R1 wrap-up.

---

*Seeded 2026-07-05 by R1 wrap-up. Author elaborates via `/design-to-spec` when starting R2 execution.*
