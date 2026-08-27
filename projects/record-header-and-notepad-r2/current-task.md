# Current Task State — record-header-and-notepad-r2

> **Last Updated**: 2026-08-27 (by `context-handoff` — after the shared `LookupField` OOB-parity work)
> **Recovery**: Read "Quick Recovery" first. Then [`CLAUDE.md`](CLAUDE.md), then [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **033 + 034** complete — `Spaarke.Records.RecordHeader` **v1.1.8**, UAT-passed on Project |
| **Phase** | 3 ✅ complete. Phase 5 rollout unblocked. One **in-flight follow-on** (below). |
| **Status** | ✅ Project UAT passed. Shared `LookupField` upgraded to OOB parity + committed. **The RecordHeader has NOT yet been switched to it.** |
| **Next Action** | **Switch the RecordHeader lookup cell to the inline `LookupField`** — 3 concrete work items, fully specified in "RecordHeader lookup swap" below. |
| **Blocked by** | Nothing in code. **Task 001** still needs a classic-designer session no build can substitute for. |
| **Working tree** | clean · 35 commits · **pushed** to `origin/work/record-header-and-notepad-r2` · no PR opened |

### ⚠️ Un-deployed work exists

`fff55ef3b` changed the shared library. **Nothing has been rebuilt or imported since.** The
RecordHeader zip on disk (`v1.1.8`) predates it. See "Update radius" for who is affected.

### UAT outcome — 5 rounds, all closed

| round | defect | root cause |
|---|---|---|
| 1 | every cell an em-dash | metadata never reached the resolver (2 causes) |
| 1 | `layoutJson` capped at 100 chars | `SingleLine.Text` → `Multiple` |
| 2 | bundle +51% over NFR-02 | date picker bundled a 2nd Fluent copy → native `Input` |
| 3 | DateOnly rendered a time picker | Client API returns **no** `Format` — read from the form |
| 3 | lookup inert | Client API returns **no** `Targets` — read from the form |
| 3 | boolean was an em-dash, not a toggle | Switch now always visible when editable |
| 4 | edit text 12px vs 14px | every editor passed `size="small"` |
| 4 | fields not saving | **not on the form** — the binding recipe's MOVE-don't-delete rule |
| 5 | lookup picker threw silently | **`this` detached** — `const f = xrm.Utility.lookupObjects` (G-14) |

---

## 🔵 IN FLIGHT — inline lookup (started 2026-08-27)

Owner raised [`ISSUE-lookup-picker-ux-side-pane.md`](notes/issues/ISSUE-lookup-picker-ux-side-pane.md):
the header lookup opens the platform **side-pane**, but OOB uses an **inline type-ahead**. Owner chose
inline, and specified it should carry the OOB look and feel.

### Settled first: you CANNOT host the OOB inline control

Do not re-investigate this. Evidence, all verified 2026-08-27:

- **`ComponentFramework.Factory` has exactly two members** — `getPopupService`, `requestRender`
  (`@types/powerapps-component-framework@1.3.18`, on disk). No `createComponent`, no `bindDOMElement`.
- `Xrm.Utility.lookupObjects` is a **callable function** → any host can open the *advanced dialog*.
  The **inline** lookup is a **control class the form runtime owns** → no public constructor. That
  asymmetry is Microsoft's choice, not a gap in our code.
- `MscrmControls.AdvancedLookupWrapper` (found in the org's `customcontrol` catalog) wraps the
  **advanced dialog** — the surface `lookupObjects` already opens. Not the inline dropdown.
- **`MscrmTools/PCF-Controls` is GPL-3.0** → cannot be a dependency in a commercial product. Its
  "Lookup as Dropdown" renders its **own** dropdown rather than hosting the platform control —
  independent corroboration from the XrmToolBox author.

**Conclusion**: reproduce the OOB *shape* with supported primitives and escalate to the real OOB
dialog for Advanced. That is the "proprietary browse + OOB escalation" pattern already sanctioned by
[`MODAL-DECISION-CRITERIA.md`](../../docs/standards/MODAL-DECISION-CRITERIA.md).

### ✅ DONE — `components/LookupField` upgraded (commit `fff55ef3b`)

The shared search-as-you-type now matches the OOB inline shape. **All four owner points landed:**

| owner spec | implementation |
|---|---|
| right-side lookup icon | moved `contentBefore` glyph → `contentAfter` **Button** |
| click icon → all values drop down | fetches with the empty term, opens; second click toggles; `aria-expanded` |
| modern scroll | options scroll independently — `scrollbar-width: thin` + WebKit fallback, semantic tokens only; ~5.5 rows |
| **Advanced**, right-aligned footer | pinned below the scroll region; new **opt-in** `onAdvanced` prop |
| **no “+ New”** | absent by owner decision — targets are taxonomy tables users cannot add to. **Guard test pins it.** |

Also: the list now elevates (`shadow8`) over the following field instead of pushing it down.

**`onAdvanced` is opt-in on purpose** — wizard consumers run in Code Pages where `lookupObjects` may
not exist (the BFF nav adapter implements `openLookup` as a no-op).

**Wrote the component's FIRST test suite** — it had 12 consumers and zero tests, which is the wrong
place to change behaviour blind. 15 tests. Full run **826/826 across 52 suites**.

---

## 📡 Update radius — who else gets this

`components/LookupField` is **shared**. The change is live for every consumer on their next build.
Nothing below has been rebuilt yet.

**12 direct consumers** (all `Create*Wizard` steps in the shared lib): `CreateEventWizard/CreateEventStep` ·
`CreateInvoiceWizard/CreateInvoiceStep` · `CreateMatterWizard/AssignResourcesStep` ·
`CreateMatterWizard/CreateRecordStep` · `CreateMatterWizard/LookupField` (25-line AI-badge wrapper, not a
duplicate) · `CreateProjectWizard/CreateProjectStep` · `CreateRecordWizard/steps/AssignResourcesStep` ·
`CreateReportCardWizard/CreateReportCardStep` · `CreateTodoWizard/CreateTodoStep` ·
`CreateWorkAssignmentWizard/AssignWorkStep` · `CreateWorkAssignmentWizard/CreateFollowOnEventStep` ·
`CreateWorkAssignmentWizard/EnterInfoStep` ← **the owner's screenshot**

**Shipped surfaces that must be rebuilt to pick it up:**

| surface | why |
|---|---|
| `src/solutions/CreateMatterWizard` | Code Page bundling the wizard steps |
| `src/solutions/CreateProjectWizard` | ditto |
| `src/solutions/CreateWorkAssignmentWizard` | ditto — the screenshot surface |
| `src/solutions/SpaarkeAi` | `QuickStartModal`, `ContextPaneController` launch the wizards |
| `src/solutions/LegalWorkspace` | `CreateProject/CloseProjectDialog` |
| **`src/client/pcf/MatterHeader`** | ⚠️ imports `dist/components/LookupField/LookupField` directly (`MatterHeaderView.tsx:61`) — **its lookups change appearance on next rebuild**, relevant to task 080 parity |
| `src/client/pcf/RecordHeader` | not yet — it still uses the *other* component (see below) |

⚠️ **PCFs bundle `dist/`, not source.** Rebuild the shared lib first (`ensure-dist-fresh` prebuild
handles it for wired PCFs). A stale `dist/` silently ships old code.

---

## 🎯 NEXT: RecordHeader lookup swap — exactly what remains

**Goal**: `RecordHeaderView`'s `case 'lookup'` renders the inline `components/LookupField` instead of
the side-pane `RecordHeaderLookupField`, with **Advanced** escalating to the existing (working)
`lookupObjects` call.

**Two components, do not confuse them** (project CLAUDE.md warns about this):

| path | what | used by |
|---|---|---|
| `components/RecordHeader/fields/LookupField.tsx` (alias `RecordHeaderLookupField`) | side-pane picker | RecordHeader **today** |
| `components/LookupField/LookupField.tsx` | inline type-ahead, **just upgraded** | 12 wizard steps + MatterHeader |

### The three work items

**1. `onSearch` — the real work.** The inline component needs a search callback; the header has none.
It must query the TARGET table by its primary name:

```
$select={targetPrimaryId},{targetPrimaryName}
&$filter=contains({targetPrimaryName},'{escaped}')
&$orderby={targetPrimaryName} asc&$top=10
```

- `targets[0]` already resolves (v1.1.6, via `getControl(name).getEntityTypes()` — proven in UAT).
- ⚠️ **The target's `primaryNameAttribute` is NOT known yet.** Today only the HOST entity's metadata is
  fetched. This needs a **second `retrieveEntityMetadata` call for the target entity** — page-session
  cached already, so cheap, but it is a genuine new fetch, not a prop change.
- ⚠️ **The naming convention is non-uniform** — `sprk_projecttype_ref` and `sprk_eventtype_ref` use
  `sprk_name`, while `sprk_mattertype_ref` uses `sprk_mattertypename`. **Read it from metadata; do not
  infer.** (design.md §621, live-verified.)
- R1's `MatterHeaderView.searchLookup` (lines ~181-197) is a working reference for the OData shape —
  but it hard-codes `LOOKUP_META`, which R2 must not do.

**2. `span`** — `components/LookupField` has **no `span` prop**, and `FieldGrid` requires each cell to
self-apply `gridColumn` (FR-03). R1 hand-rolled `<div style={{ gridColumn: 'span 1' }}>` around it
(`MatterHeaderView.tsx:278-287`). Either add a `span` prop to the shared component (cleaner, benefits
everyone) or wrap it in the view. **Prefer the prop.**

**3. `onAdvanced`** — pass a callback that opens `xrm.Utility.lookupObjects({ entityTypes: [targets[0]] })`
and stages the pick. **Call it directly on `xrm.Utility`** — never via a local alias (FAILURE-MODES
**G-14**; that exact bug cost UAT round 5, and R1 four releases before it).

### Also decide

- **Read-only lookups** keep using the display-only `RecordHeaderLookupField` — only the editable path
  changes. Do not delete that component.
- **FR-15a / design.md §6.5 say "OOB picker (modal)"** — this reverses that. Per CLAUDE.md §6.5 this
  is a **path B** change: update `spec.md` FR-15a + `design.md` §6.5 in the same PR, don't silently
  diverge.
- **Task 080 parity**: after the swap, RecordHeader and R1's MatterHeader render the *same* inline
  component, so the §6.5 parity caveat ("identical except lookups open the OOB picker") can be
  **deleted** rather than qualified. Good outcome — note it in 080.

### The lesson from five rounds

Every defect from round 3 on was "a platform surface did not return what we assumed", and each cost a
full build → import → UAT cycle to disprove. Two would have been minutes, not days:

- **Read the shipped `.d.ts`.** `@types/xrm` declares `Metadata.AttributeMetadata` as exactly six
  members — no `Format`, no `Targets`. That settled in seconds what three rounds of inference did not.
- **Instrument before theorising.** The diagnostic added in v1.1.7 found the round-5 bug on the first
  click. It should have been round 1's move.
- **A mock more permissive than the real API tests nothing.** `jest.fn()` needs no receiver, so 19
  green tests coexisted with a picker that threw on every click in production.

### 🔴 Read this before touching the bundle

**Two "clever" NFR-02 fixes have already failed. Do not re-derive them.**

1. **Lazy-load `DateField`** → measured *larger*. `pcf-scripts` emits a single chunk; `import()` inlines back. Code splitting is not a lever in a PCF.
2. **Externalize granular `@fluentui/*` onto the platform global** → built clean, passed static symbol verification, then **crashed at runtime with Minified React error #31**. It splits Fluent's slot machinery across two live copies. `webpack.config.js` now carries a ⛔ comment. RecordHeader must match the **standard triad** every other PCF uses — it was the only PCF in the repo deviating.

**A successful build proves nothing about a PCF's runtime.** Both failures built green.

### ✅ NFR-02 is resolved — option B landed

`DateField` now edits through Fluent `<Input type="date">` / `type="datetime-local"`, reusing the
pattern already shipping at
`Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/EnterInfoStep.tsx:338-340`.
`Input`/`Field` are in `@fluentui/react-components`, which the platform externalizes → zero bundle cost.

| | bundle | vs 250,000 ceiling |
|---|---|---|
| before | 378,457 B | ❌ +51% |
| **after (v1.1.3)** | **99,068 B** | ✅ **40%** |

`@fluentui/react-datepicker-compat` removed from the shared lib (`DateField.tsx` was its only
consumer there; `EventDetailSidePane` keeps its own and is off-limits to R2).

**Timezone handling is the fragile part — do not "simplify" it.** All conversion goes through LOCAL
calendar fields, never UTC: `toISOString().slice(0,10)` shifts the day, and `new Date('2026-08-21')`
is UTC midnight per ES2015+. A real bug was fixed here — a bare `yyyy-MM-dd` (what the Web API
returns for `DateOnly` attributes) previously rendered as the PREVIOUS day west of UTC. Seven tests
drive the pure converters directly so they hold under any `TZ`; CI is UTC, dev machines are not.

---

## Where things stand

**17 of 30 tasks ✅ · Phase 5 rollout is operator/maker work · Phase 3 complete.**

| Phase | State |
|---|---|
| 0 — spike + baseline | 001 operator-gated (see below); 002 static half captured |
| 1 — renderers | ✅ all six + 91-test FR-10 contract suite |
| 2 — metadata/machinery | ✅ 020, 021, 022, 023, 024 |
| 3 — resolver + control | ✅ 030, 031, 032, **033**, **034** — all code-complete |
| 4 — schema drift | ✅ 040 (shipped v1.0.21), 041 |
| 5 — rollout | ready — **maker work**, not agent-automatable |

**Tests**: RecordHeader PCF **72/72**; `recordHeader.integration` **11/11** (task 034 rewrote it —
that suite had been red since v1.0.10 and is now closed). Repo-wide known-red is **8 suites**, down
from 9, all outside R2's file scope. Do not report the project "green" without that caveat.

**There is no agent-executable task left.** 050–081 are form bindings and QA in the maker portal;
085/086/090 are docs that should follow a *shipped, UAT-passed* control, not precede it.

---

## Session decisions

| Decision | Why |
|---|---|
| **RS-1 hotfix shipped as v1.0.21** (owner: option A) | Matter header 400'd on every record; R2's replacement was weeks out |
| **Fluent pinned to exactly 9.68.0** across 19 PCFs + 4 PCF-consumed shared libs | Caret ranges floated to 9.74.x while the host serves 9.68.0. Fluent is *externalized*, so any post-9.68 API is `undefined` at runtime. Vite solutions deliberately NOT pinned — they bundle their own |
| **`layoutJson` → `of-type="Multiple"`** | Classic designer caps a `SingleLine.Text` static value at **100 chars**; a real layout is ~310 B |
| **DateField → option B** (`Input type="date"`) | Reuses a shipping in-repo pattern; zero bundle cost; the two clever alternatives failed |
| **Binding recipe: MOVE, don't delete** | Form-buffer staging needs `getAttribute`, which is null for a field with no control on the form. Verified against the shipped R1 `formxml` |
| **Sparkle: `AiSummaryPopover` gained an optional `emptyText`** (task 034) | The POML demanded "No summary yet" + testid `sparkle-popover-empty`; the shipped component had **neither**, and the POML's cited evidence was the known-red suite. An optional prop defaulting to the old string leaves all **nine** existing callers byte-identical; changing the default globally would have reworded six document surfaces |
| **Metadata request names BOTH summary candidates** (task 034) | `sprk_recordsummary` is on no rollout entity's FORM. Without this the existence gate fails on every entity and the sparkle is invisible everywhere, silently. See [`notes/decisions/034-sparkle-existence-gate.md`](notes/decisions/034-sparkle-existence-gate.md) |

---

## ⚠️ Open items for the owner

| # | Item | Notes |
|---|---|---|
| 1 | **UAT v1.1.4** (DateField rework + sparkle) | Import to **`spaarkedev1`** — never `spaarke-model1-prod`. Four checks listed in Quick Recovery |
| 2 | **Task 001** still operator-gated | Needs a classic-designer session: does `Multiple` give a multi-line editor, and does ~310 B round-trip through form XML? No build or query can verify this |
| 3 | **Lookup picker check** at UAT | MS does not document `Targets` on the Client API payload. Display works regardless; the OOB picker depends on `targets[0]`. If it misbehaves, the fix is a scoped `LookupAttributeMetadata` call (verified HTTP 200) |
| 4 | ~~Sparkle is task 034~~ | ✅ **shipped in v1.1.4.** "No summary yet." is the CORRECT popover state — a separate project populates the column |
| 5 | 14 other PCFs carry the Fluent pin but were not rebuilt | No redeploy needed — the pin is preventive; the umbrella is externalized so it is not in any shipped bundle |
| 6 | **Shared-lib change ships to more than RecordHeader** | Task 034 touched `AiSummaryPopover` + `HeaderToolbar`, which **nine** surfaces consume. The change is additive and all 21 relevant suites pass, but any PCF rebuilt from now on picks up the new `dist/` |

---

## Traps for the next session

1. **Never `git add -A` while agents run.** It swept in-flight renderer work into an unrelated commit (`f85258f75`) once already.
2. **`npm run build:prod`**, never `npm run build`.
3. **Rebuild the shared lib before any PCF** — PCFs bundle `dist/`, not source.
4. **Task 086 is main-session-only** (writes `.claude/`, where sub-agents cannot).
5. **Pinning can prune `scheduler`** (a react-dom 16.14 transitive), producing a misleading
   `@fluentui/react-context-selector` jest error. Fix: `rm -rf node_modules package-lock.json`, reinstall.
6. **`grep <package-name> bundle.js` proves nothing** — minification strips package paths; it reads 0
   whether or not the code is present. Use a class prefix such as `fui-Calendar`.

---

## New cross-cutting knowledge (already in `.claude/FAILURE-MODES.md`)

- **AP-8** — never amend a failing test to match the source without checking the source against the vendor contract. Task 020 did exactly that and destroyed the only signal for the metadata bug that later broke UAT.
- **G-12** — a Dataverse `$select` is all-or-nothing; one bad column blanks the whole control. Three occurrences; now guarded generically by a no-`$select` retry in `useRecordFieldValues`.
- **G-13** — `Xrm.Utility.getEntityMetadata` returns the **Client API** shape (numeric `AttributeType`, string `DisplayName`), not the Web API shape. Parsing only Web-API shapes degraded every attribute to `String`.

---

## Resume commands

| Intent | Say |
|---|---|
| Continue | `where was I?` → `project-continue` |
| Task board | open [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md) |
| Bundle history | [`notes/decisions/033-nfr02-externals-runtime-failure.md`](notes/decisions/033-nfr02-externals-runtime-failure.md) |
| UAT root cause | [`notes/decisions/033-def1-metadata-never-reached-resolver.md`](notes/decisions/033-def1-metadata-never-reached-resolver.md) |
