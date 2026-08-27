# Current Task State — record-header-and-notepad-r2

> **Last Updated**: 2026-08-27 (after the RecordHeader inline-lookup swap — v1.1.11 packed, awaiting UAT)
> **Recovery**: Read "Quick Recovery" first. Then [`CLAUDE.md`](CLAUDE.md), then [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **033 + 034** complete — `Spaarke.Records.RecordHeader` **v1.1.11** packed, not yet imported |
| **Phase** | 3 ✅ complete. Phase 5 rollout unblocked. The inline-lookup follow-on is **code-complete**. |
| **Status** | ✅ **v1.1.11 imported and UAT-PASSED on the Project form** (owner, 2026-08-27): "the PCF is working - looks good". |
| **Next Action** | **Phase 5 form bindings** — [`notes/rollout-form-binding-cheatsheet.md`](notes/rollout-form-binding-cheatsheet.md) has the verified JSON + per-entity add/hide lists. 050 is already done. |
| **Blocked by** | Nothing in code. Task 001 ✅ closed 2026-08-27. Phase 5 is maker work. |
| **Working tree** | clean · **pushed** to `origin/work/record-header-and-notepad-r2` · no PR opened |

### ⚠️ Packed and IMPORTED — UAT passed

`Solution/bin/RecordHeaderPcf_v1.1.11.0.zip` is built from the current tree (bundle **116,422 B**,
46% of the 250 KB NFR-02 ceiling — up from 99,068 B at v1.1.3, which is the inline lookup's cost).

```
pac solution import --path "src/client/pcf/RecordHeader/Solution/bin/RecordHeaderPcf_v1.1.11.0.zip" --publish-changes
```

Import to **`spaarkedev1`** — never `spaarke-model1-prod`. Hard-refresh (Ctrl+Shift+R) and confirm
the footer reads **v1.1.11**.

### UAT v1.1.11 — ✅ PASSED (owner, 2026-08-27). Retained as the regression checklist

1. **Project Type opens an INLINE dropdown under the field**, not the right-side pane.
2. **The magnifier on the right of the field is clickable** — it drops the full list with no typing.
3. **The list scrolls** (thin modern scrollbar) with **Advanced** pinned bottom-right; Advanced opens
   the OOB dialog. There is **no "+ New"** — deliberate.
4. **The input has no border box** — gray fill, brand-blue underline on focus, placeholder reads
   "Look for Project Type" (v1.1.11).
5. **Everything else is unchanged** — date still saves as date-only, priority still a toggle, 14px
   edit text, sparkle still present.

### Remaining OOB deltas — CLOSED by owner 2026-08-27

| delta | decision |
|---|---|
| per-row 16×16 entity icon | ❌ **dropped for good** — "not critical, cleaner without it" |
| `Project Types` group header | ❌ **dropped for good** — same |
| `+ New` in the footer | ❌ **excluded** — taxonomy targets users cannot add to |
| per-row secondary line (`3/5/2026 11:31 AM`) | ⏸️ **open** — this is the record's modified timestamp. OOB shows it to disambiguate same-named records; our taxonomy targets have unique names, so it buys little. Would cost one extra `$select` column + a two-line row. **Recommend dropping**; not built |

Do not "restore parity" on the first three. They are decisions, not gaps.

If a lookup renders as plain text with no search box, open the console: the per-cell diagnostic now
prints `picker: 'inline' | 'display'` per field. `'display'` means one of the two required halves is
missing — read `readOnly` and `targets` on the same line.

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

## ✅ Form transport VERIFIED (task 001, 2026-08-27)

`SpaarkeMaster` contains the `sprk_project` **entity** with **"Include Subcomponents"**, so the
Project main form and its `layoutJson` transport with it. Confirmed by a read-only export and
byte-comparison: the 3 form-factor copies come through at 402 bytes vs 401 live, differing in
**one byte** — the trailing newline normalised LF→CRLF. All 400 content bytes identical; JSON
semantically equal. The project CLAUDE.md portability assumption is **realised**, not just possible.

> ⚠️ **A first pass got this wrong** and claimed the form was in no shippable solution. The owner
> corrected it. The error: querying `solutioncomponent` for the **form's own** objectid only finds
> solutions where the form was added **explicitly**. An entity added with `Include Subcomponents`
> carries its forms with **no** row of their own. **To ask "does this asset transport?", query the
> ENTITY's component row and its `rootcomponentbehavior`** — an asset-level query is a false negative
> for every entity added with subcomponents, which is the normal case.

**The one real maker trap this found**: the classic designer stores a **separate copy of
`layoutJson` per form factor** (Web / Tablet / Phone — three today, currently byte-identical). Edit
one and the others silently diverge, so a layout change must be applied to all three.

---

## 📋 Phase 5 is unblocked — verified cheat sheet ready

[`notes/rollout-form-binding-cheatsheet.md`](notes/rollout-form-binding-cheatsheet.md) — every field
in all five layouts validated against LIVE `spaarkedev1`, plus per-entity add/hide lists.

**All 39 fields exist.** The real risk is different from what the POMLs assume: they say "MOVE the
raw fields", but **15 fields across four entities are NOT on their form at all** and must be ADDED
first — otherwise every edit to them throws `Field '<name>' not on form`. Agreement is the heaviest
(5 of 6 missing) and has no records to QA against.

`sprk_recordsummary` exists on all five and correctly stays OFF the forms — it is read-only.

---

## ✅ DECIDED — task 002 baseline (owner, 2026-08-27)

**Capture the baseline from the CURRENTLY DEPLOYED MatterHeaderPcf. Do NOT rebuild it first.**

An earlier note here recommended rebuilding so the diff would be apples-to-apples. The owner
challenged it and was right: a parity baseline should capture the status quo users actually see, and
rebuilding changes the "before" side to a build that never shipped — a worse baseline, not a better
one. The secondary justification ("it verifies the shared component") was also weak: RecordHeader
already proved that component in a PCF host through UAT; the genuinely untested surface is the 12
**Code Page** wizard consumers, about which MatterHeader says nothing.

**Expect exactly one known delta at 080**: the deployed MatterHeaderPcf bundles the pre-2026-08-27
shared `LookupField`, so its lookups lack the browse button, the overlaying dropdown and the Advanced
footer. Caused by a shared-library upgrade, not by the migration. Both 002 and 080 carry this inline.

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

**Shipped surfaces that must be rebuilt to pick it up — 12 solutions + 1 PCF.**

> ⚠️ **Corrected 2026-08-27.** A first pass listed only 5 solutions; a full repo scan found **12**.
> Every one below was verified to import wizard components from `src/`, not merely mention them.
> Do not trust a narrow grep for this — the radius is wider than it looks.

| surface | why |
|---|---|
| `src/solutions/CreateWorkAssignmentWizard` | Code Page hosting the wizard steps — **the owner's screenshot** |
| `src/solutions/CreateMatterWizard` | ditto |
| `src/solutions/CreateProjectWizard` | ditto |
| `src/solutions/CreateEventWizard` | ditto |
| `src/solutions/CreateInvoiceWizard` | ditto |
| `src/solutions/CreateReportCardWizard` | ditto |
| `src/solutions/CreateTodoWizard` | ditto |
| `src/solutions/WorkspaceLayoutWizard` | hosts wizard components |
| `src/solutions/SpaarkeAi` | `QuickStartModal`, `ContextPaneController` launch the wizards |
| `src/solutions/LegalWorkspace` | `CreateProject/CloseProjectDialog` |
| `src/solutions/SmartTodo` | `AddTodoBar` → CreateTodo path |
| `src/solutions/Notepad` | `hooks/discoverMemoNavProps` |
| **`src/client/pcf/MatterHeader`** | ⚠️ imports `dist/components/LookupField/LookupField` **directly** (`MatterHeaderView.tsx:61`) — **its lookups change appearance on next rebuild**, relevant to task 080 parity |
| **`src/client/pcf/RecordHeader`** | ✅ **DONE in v1.1.11** — now renders the inline component on the editable path (packed, not yet imported) |

**None of these are urgent** — the change is additive and every consumer keeps working unchanged
except that its lookup gains the right-side browse icon and the modern scrollbar. But a reviewer
asking "what does this PR touch?" should be given this list, not the short one.

⚠️ **`MatterHeader` is the one with a schedule attached.** Task 080 baselines Matter parity against
`MatterHeaderPcf` v1.0.20 — so rebuild it BEFORE capturing that baseline, or the diff will attribute
the shared component's new browse button and scrollbar to the RecordHeader migration.

⚠️ **PCFs bundle `dist/`, not source.** Rebuild the shared lib first (`ensure-dist-fresh` prebuild
handles it for wired PCFs). A stale `dist/` silently ships old code.

---

## ✅ DONE: RecordHeader lookup swap (v1.1.11, 2026-08-27)

`RecordHeaderView`'s `case 'lookup'` now renders the inline `components/LookupField` when the field
is editable **and** a target resolved; read-only or target-less lookups keep the display renderer.

**Two components, still easy to confuse** (project CLAUDE.md warns about this):

| path | what | used by |
|---|---|---|
| `components/LookupField/LookupField.tsx` | inline type-ahead — **now the editable path** | RecordHeader · MatterHeader · 12 wizard steps |
| `components/RecordHeader/fields/LookupField.tsx` (alias `RecordHeaderLookupField`) | display + navigate | RecordHeader's read-only / target-less path |

### How the three costed items landed

1. **`onSearch`** — new shared `RecordHeader/lookupSearch.ts`. It resolves the TARGET entity's
   `primaryIdAttribute` / `primaryNameAttribute` with a second `retrieveEntityMetadata` call
   (page-session cached, so one round trip per distinct target however many cells use it). The
   non-uniform convention — `sprk_projecttype_ref` → `sprk_name` but `sprk_mattertype_ref` →
   `sprk_mattertypename` — is pinned by a test that would fail if either were inferred.
2. **`span`** — added as a **prop on the shared component** (the preferred option), not a wrapper in
   the view. Omitted ⇒ no `gridColumn` emitted at all, so the flex-laid-out wizard consumers are
   byte-identical; both branches are guarded by tests.
3. **`onAdvanced`** — routed through `openAdvancedLookup`, now the **only** `lookupObjects` call site
   in the shared library. `RecordHeaderLookupField` was refactored to delegate to it. That
   consolidation is the point: **G-14 recurred precisely because the lesson lived in one copy and
   not the other.**

### Also done

- **Docs amended, not diverged** (CLAUDE.md §6.5 path B): `spec.md` FR-15a (rewritten) + FR-26 +
  scope bullet + criterion 15; `design.md` §6.5 kept verbatim under an AMENDED banner with a new
  **§6.5a** carrying the reversal, its evidence, and an honest cost ledger.
- **Task 080 parity IMPROVED** — the "identical except lookups" caveat is **withdrawn**. Both
  controls now render the same shared component, so Matter parity is compared unqualified. ⚠️ But
  rebuild `MatterHeaderPcf` before baselining: it picks up the browse button + scrollbar too.

### Tests

| suite | result |
|---|---|
| `lookupSearch.test.ts` (**new**, 22 tests) | ✅ pure builder · both name conventions · `this`-sensitive `lookupObjects` stub |
| `components/LookupField` (15 → **17**) | ✅ +2 for `span` present/absent |
| `RecordHeader/*` shared (12 suites) | ✅ 477 |
| `RecordHeader` PCF | ✅ **96** (2 rewritten, 1 added — see below) |
| shared lib full run | 3206 passed · **8 red suites, all pre-existing and outside R2's scope** |

⚠️ **Two PCF tests were re-expressed, not deleted.** They pinned "targets resolve → editable" via
`data-editable="true"` on the display renderer. The regression is unchanged but its observable moved
to "the inline field rendered". A third test was ADDED for the read-only branch, so both halves of
the editable gate are now pinned. The 8 red suites were verified unrelated: a workspace `rowHeight`
regression, a locked-hash guard, a registry mismatch, a BU-chain mismatch.

### Settled — do NOT re-investigate

Hosting the OOB inline control is **not possible**. `ComponentFramework.Factory` has exactly two
members (`getPopupService`, `requestRender`). `lookupObjects` is callable only because it is a plain
function opening the **dialog**; the inline lookup is a class the form runtime owns, with no public
constructor. `MscrmControls.AdvancedLookupWrapper` wraps the dialog. `MscrmTools/PCF-Controls` is
**GPL-3.0** and renders its own dropdown anyway. Full evidence in `design.md` §6.5a.

---

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
