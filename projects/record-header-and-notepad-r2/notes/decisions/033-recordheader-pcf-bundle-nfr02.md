# Task 033 — RecordHeader PCF: build record, measurements, and the NFR-02 escalation

> **Task**: 033 `Spaarke.Records.RecordHeader` PCF — new control, new `RecordHeaderPcf` solution
> **Date**: 2026-08-25
> **Status**: code + tests COMPLETE and green · **deployment BLOCKED on an NFR-02 escalation**
> **Sibling**: [`wave1-sideeffects-tree-shaking.md`](wave1-sideeffects-tree-shaking.md) — predicted this exact risk for 033

---

## 1. Confirmed starting version

**1.1.0**, synced across all FIVE ADR-020 locations (verified by grep):

| # | Location | Value |
|---|---|---|
| 1 | `control/ControlManifest.Input.xml` | `version="1.1.0"` |
| 2 | `control/version.ts` | `CONTROL_VERSION = '1.1.0'` |
| 3 | `Solution/solution.xml` | `<Version>1.1.0.0</Version>` |
| 4 | `Solution/Controls/sprk_Spaarke.Records.RecordHeader/ControlManifest.xml` | `version="1.1.0"` (build artifact) |
| 5 | `Solution/pack.ps1` | `$version = "1.1.0.0"` |

The spec assumption (new identity carrying R1's shipped feature set) is confirmed and adopted. `MatterHeaderPcf` stays at v1.0.21 and is retired separately at task 081.

---

## 2. 🔴 NFR-02 bundle measurement — CEILING EXCEEDED

`npm run build:prod` (production mode, never `npm run build`):

| Measurement | Bytes | vs NFR-02 (250 KB) |
|---|---|---|
| **`out/controls/control/bundle.js` (shipped)** | **377,680** | ❌ **+51%** — 368.83 KiB |
| R1 `MatterHeader` baseline | 63,898 (62.4 KiB) | ✅ |
| **Diff vs R1 baseline** | **+313,782 B (+491%)** | |

### Attribution — measured, not assumed

Rebuilt with `DateField` swapped out (nothing else changed):

| Configuration | `bundle.js` |
|---|---|
| All 7 renderers reachable (shipped) | **377,680 B** |
| Same, minus `DateField` → `@fluentui/react-datepicker-compat` | **92,706 B** |
| **Cost of the date picker** | **284,974 B (~278 KiB) = 75% of the bundle** |

**Everything R2 actually built is 92,706 B — comfortably inside the ceiling.** The overage is one dependency.

### Root cause

`DateField` imports `DatePicker` from `@fluentui/react-datepicker-compat`, which pulls
`@fluentui/react-calendar-compat` (1.96 MB on disk) + `@fluentui/react-datepicker-compat` (487 KB).
The manifest's `<platform-library name="Fluent" version="9.46.2"/>` externalizes
**`@fluentui/react-components` only** — the `*-compat` packages are separate npm packages outside
that boundary, so the entire calendar implementation is bundled. `sideEffects: false` (wave-1 fix)
is working correctly; there is simply nothing unreachable left to shake once a date renders.

FR-06 requires "edit uses a Fluent date picker", and Fluent v9 ships no native one — the compat
package is the official answer. So the requirement and the ceiling are in direct tension.

### ⚠️ The documented fallback does NOT work — tested, not assumed

`wave1-sideeffects-tree-shaking.md` names the fallback as "a deep-path import of `DateField` only,
loaded lazily per resolved layout". **Empirically this is not available:**

| Attempt | Result |
|---|---|
| `React.lazy(() => import('.../DateField'))` + single emitted asset | **379,095 B — 1,415 B LARGER** |

`pcf-scripts` emits a **single** `bundle.js` (webpack `id hint: vendors`, one asset). A dynamic
`import()` is inlined straight back into the main chunk, so lazy loading adds the `React.lazy` +
`Suspense` machinery and saves nothing. Code-splitting is not a lever available to a PCF, because a
PCF is served as one web resource with no chunk-loading `publicPath`.

### 🔔 Escalation — owner decision required

Per the task's own instruction ("if you exceed ~250,000 bytes, STOP and escalate"), the solution was
**packed but NOT imported** to `spaarkedev1`. Options, none of which task 033 can choose unilaterally:

| # | Option | Cost |
|---|---|---|
| **A** | **Accept a raised NFR-02 ceiling for this control** (e.g. 400 KB). NFR-02's 250 KB was set against R1's date-free MatterHeader; R2's FR-06 mandate changed the input. | Doc/spec change only. Bundle is one-time-cached per version; TTI impact to be measured in Phase 5. |
| **B** | **Replace `DatePicker` with a native `<input type="date">`** styled with Fluent tokens, in `DateField` (task 010 territory). | ~285 KB saved → ~93 KB total. Loses the Fluent calendar UX; re-opens a closed task's decision. |
| **C** | **Split the date renderer into a second control** bound only on date-bearing forms. | Contradicts the one-configurable-control design (FR-01). Not recommended. |
| **D** | Ship over budget as a knowing, time-boxed exception with B scheduled. | Hybrid of A + B. |

**Recommendation: A or D.** The 250 KB figure predates the requirement that broke it, the non-date
cost is 93 KB (excellent), and B silently downgrades a shipped renderer that tasks 010/015 already
tested and closed.

---

## 3. Deviations from the task plan (each deliberate, each verified)

| # | Deviation | Why |
|---|---|---|
| 1 | `layoutJson` ships as `of-type="SingleLine.Text"` | Task 001 (operator-gated) has not run. `spec.md:289` names `SingleLine.Text` the **proven** fallback and states the spike "is an ergonomics check, not a gate"; `spec.md:309` says it blocks nothing in the design. Escalation trigger 1 is pre-resolved, not fired. Switching to `Multiple` later is a one-attribute change in two files. |
| 2 | **Extended shared `useRecordHeaderFields` with `saveValue` / `displayValue`** | Task 022 staged **text + lookup only**, but FR-06/07/08/09 all require EDIT modes whose `onSave` hands back `Date \| null`, `number \| null`, `boolean`. Escalation trigger 2 forbids re-implementing staging **in the PCF layer** — so the behavior landed in the shared hook beside its two siblings, sharing the same `requireFormAttribute` throwing gate (FR-14). **CLAUDE.md §11**: *existing* — `useRecordHeaderFields` (no other stager exists); *extension* — yes, third buffer in the same hook, not a new component; *cost of doing nothing* — every non-text renderer is permanently read-only, and FR-09's "`sprk_invoicestatus` is editable" (success criterion 13) cannot be met. Shared suites: **473/473 green** after the change. |
| 3 | FR-12 helper moved to `control/entityContext.ts` | `pcf-scripts` rejects a second export from the manifest entry module: `[pcf-1023] Control source code defines more than one export.` **Not in `pcf-build-scaffold.md` — new gotcha, recommend adding as #11.** |
| 4 | View imports per-hook deep paths, not the `dist/hooks` barrel | That barrel re-exports `useForceSimulation` → `d3-force` (ESM). Webpack shakes it out, but it makes the module unloadable under ts-jest and puts a chart library in the graph for no reason. |
| 5 | Jest maps `@spaarke/ui-components/dist/*` → shared-lib **source**, + pins one React copy | MatterHeader `jest.mock()`s every shared subpath. Wrong here: config resolution / span clamp / renderer derivation ARE the behavior under test, so mocking them would leave the suite asserting its own mocks. Production still consumes `dist/` (NFR-08 untouched). |
| 6 | `pack.ps1` uses absolute paths | `Set-Location` does not update the .NET process CWD, so `ZipFile::Open` on a relative path fails from any other directory. **MatterHeader's `pack.ps1` carries the same latent bug** (it only ever ran from its own folder). |
| 7 | Lookup cell gets no `required` prop | `fields/LookupField` deliberately omits it — `rendererContract.test.tsx` holds this renderer **out** of the FR-10 suite by design. `*` is TextField-only (D-10), so it is inert everywhere else. Not a gap. |
| 8 | `entityDisplayName` left `undefined` | `EntityMetadata` (shared `IDataverseClient` projection) does not carry it; the resolver humanizes the logical name instead, and both `layoutJson.title` and the manifest `title` outrank it. Widening a cross-consumer projection for a fallback-of-a-fallback fails CLAUDE.md §11 q2. |

---

## 4. Answers to the two questions handed forward

### Polymorphic lookups — **none. `targets[0]` is sufficient for the entire rollout.**

All seven lookups across the six §9 layouts are **single-target** (design.md §6.5 live table, 2026-08-25):

| Lookup | Target |
|---|---|
| Matter → `sprk_mattertype` | `sprk_mattertype_ref` |
| Matter → `sprk_practicearea` | `sprk_practicearea_ref` |
| Project → `sprk_projecttype_ref` | project-type table |
| Event → `sprk_eventtype_ref` | event-type table |
| Agreement → `sprk_agreementtype` | agreement-type table |
| Agreement → `sprk_regardingmatter` | `sprk_matter` |
| WA → `sprk_assignedto` | `contact` |

Invoice's layout carries no lookup cell at all. **Task 023's `targets[0]` limitation does not bite R2.**

**Residual risk (bounded, not blocking)**: nothing *stops* a maker naming a genuinely multi-table
lookup — `ownerid` (systemuser + team) or a Customer column — in a future `layoutJson`. The picker
would then silently restrict to `targets[0]`. No such field is in any shipped layout; worth a
disambiguation story only if UAT surfaces it.

### `String` + `format: 'TextArea'` → `text` — **does not bite the six layouts.**

Every multiline field in every §9 layout is Dataverse type **`Memo`**, and
`rendererFromAttributeType` maps `Memo → 'textarea'` correctly:

`sprk_projectdescription` · `sprk_description` (WA / Invoice / Event) · `sprk_agreementdescription` ·
`sprk_matterdescription` — all Memo. **No `String`+`TextArea` attribute appears in any layout**, so
no explicit `"renderer": "textarea"` override is needed anywhere in the shipped configs.

The 031 behavior is correct as designed; the gap is real but unreachable from the current rollout. If
an entity ever puts a multiline `String` (Format=TextArea) in a header, the maker sets the renderer
explicitly — which the schema already supports.

---

## 5. Verification results

| Check | Result |
|---|---|
| `npm run build:prod` | ✅ Succeeded (3 webpack size warnings — see §2) |
| PCF unit tests | ✅ **35/35** — valid / absent / malformed `layoutJson`, footer, FR-12, metadata order, helpers |
| Shared-lib RecordHeader + hook suites | ✅ **473/473** across 12 suites (no regression from the §3.2 extension) |
| `pcf-scripts lint` | ✅ Succeeded |
| Apostrophes in attribute values, BOTH manifests | ✅ **0** |
| Every `@spaarke/ui-components` import deep-path `dist/*` | ✅ 8 of 8 |
| Direct `@fluentui/react-icons` imports in PCF layer | ✅ **0** |
| Entity name / GUID / env name in `control/` source | ✅ **0** (only prose in doc comments) |
| ADR-021 hex/rgb/hsl in authored styles | ✅ **0** |
| ADR-022 banned React 18 APIs (`use(`, `useSyncExternalStore`, `createRoot`) | ✅ **0** |
| `@spaarke/auth` / raw `fetch` / BFF | ✅ **0** |
| `updateRecord` / post-save `refresh()` in PCF layer | ✅ **0** (the R1 v1.0.7 flash bug cannot recur) |
| `RecordHeaderView.tsx`: no `Xrm.Page` access, no OData search builder | ✅ **0** |
| Solution ZIP packs, lowercase root entries | ✅ `RecordHeaderPcf_v1.1.0.0.zip`, 6 entries |
| Import to `spaarkedev1` | ⛔ **NOT RUN — blocked on the §2 escalation** |
| ui-tests (6) | ⛔ **NOT RUN — require the deployed control** |

TTI (NFR-01) is not measurable until the control is bound on a form; it is formally a Phase-5
per-wave measurement. Note the bundle finding above is a direct input to cold TTI.
