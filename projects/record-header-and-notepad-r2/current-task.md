# Current Task State — record-header-and-notepad-r2

> **Last Updated**: 2026-08-26 (after task **034** — sparkle wiring, v1.1.4)
> **Recovery**: Read "Quick Recovery" first. Then [`CLAUDE.md`](CLAUDE.md), then [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | **033 + 034** both code-complete — `Spaarke.Records.RecordHeader` **v1.1.4** |
| **Phase** | 3 ✅ **complete** → Phase 5 rollout is now unblocked *by code*, blocked only on UAT |
| **Status** | ⚠️ awaiting owner UAT. **All agent-executable work in this project is done.** |
| **Next Action** | Owner imports `Solution/bin/RecordHeaderPcf_v1.1.4.0.zip` to **`spaarkedev1`** and runs UAT on the Project form — see the checklist below. |
| **Blocked by** | Owner UAT. Plus **task 001**, which needs a classic-designer session no build can substitute for. |
| **Working tree** | **clean**, 21 commits this session |

### What the owner needs to exercise

1. **A `DateOnly` and a `DateAndTime` field** — the v1.1.3 rework; the timezone edge is where it fails if it fails.
2. **Click a lookup cell** — still the open unknown (MS does not document `Targets` on the Client API payload).
3. **The sparkle** — new in v1.1.4. It should appear on Project, and its popover should read **"No summary yet."** (that is CORRECT — a separate project populates the column).
4. **Negative case**: set `summaryField` in `layoutJson` to a nonsense name, publish, reload. The sparkle must **vanish** and the header must **still render every field**. If the header goes blank, the metadata retry (b) did not save it and that is a real defect.

**Everything below is verified by build + tests only.** Three clean builds have shipped a broken
control in this project — treat green as permission to test, not as evidence it works.

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
