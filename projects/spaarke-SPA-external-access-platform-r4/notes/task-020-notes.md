# Task 020 notes — Review detail (082c) at four render points; held-Accepted badge (FR-11, FR-12)

Status: code and tests done, uncommitted (wave rules). No live Azure/Dataverse read or write was made. No file under `src/server`, no `.claude/`.

## 1. Step 1 — what master already has (both escalation triggers checked)

| Question | Answer | Evidence |
|---|---|---|
| Is ebilling's `sprk_reviewdetail` on the **invoice** `ReadableColumns` on master? | Yes | `ExternalAccessModule.cs:606`, inside the `invoices` module block (descriptor with `PrimaryNameAttribute = "sprk_name"`, scope dimensions matter / project / `sprk_regardingworkassignment`; the neighbouring entries are `statuscode`, `sprk_totalamount_base`). |
| Live on dev? | Yes (main session: dev BFF `5ae2804884` contains master `fe51619cfc`) | Not re-checked live here (no live reads allowed). |
| Does the server already gate the value? | Yes, defence in depth | `Infrastructure/ExternalAccess/ExternalInvoiceReviewDetail.cs`: the column is returned null (value and `@formattedValues` label) unless `statuscode` is 1; and it **refuses a filter or order on the column** (`UnfilterableColumns`, R4-1). |
| 082c-3's held signal on master? | Yes | `InvoiceSubmissionOutcome.tsx`: `view.warning` on an Accepted view → `held` → badge "In Review" / `warning`. |
| Option values and texts vs design §7 | **Identical** | `InvoiceReviewDetail.cs:47-60` (659490000 / 659490001 / 659490002 and the three labels) matches design §7 line 199; the explanation lines are design §7 lines 200-203, copied verbatim into `reviewDetail.ts` and written out again, independently, in the test. |

So the column IS in the read projections (Invoices section list `INVOICE_COLUMNS`, the Invoice record read through the read description). The only thing not done is the **grid column** (live Dataverse config write, task 028).

## 2. Decisions

1. **Helper location:** `src/client/external-spa/src/components/record/reviewDetail.ts` (the POML said `components/detail/`; since task 005 the template, its header and its sections live in `components/record/`). Pure, no React. `RecordDetailFields.tsx` (in `detail/`) imports it; it imports only a type back (no runtime cycle).
2. **What each render point shows.** The helper returns `{ text, explanation } | null`:
   - three values, status 1: `text` = the option label, `explanation` = the approved sentence;
   - blank value, status 1: `text = null`, `explanation = "The client is reviewing this invoice."`;
   - unknown value, or any status other than 1 (including a missing / unreadable status; a string label such as "In Review" is never read): `null` = blank.
   - Grid cell, Invoices-section list line, header secondary line: the **label only** (they have no room for a sentence, and a blank In Review value would put the same "reviewing" line on every normal In Review invoice). Information field: the label, with the explanation line under it; for a blank In Review value, the reviewing line alone. The POML's acceptance wording ("Information field shows 'The client is reviewing this invoice.'") is met; if the owner wants the reviewing line in the other three places too, it is a one-line change in the helper's row functions.
3. **Blank means no row / no line, not an em dash.** The Information field's "Review detail" row is left out entirely when the helper returns null (a label with an empty value on every Paid or Approved invoice would be noise). Same for the header line and the list segment.
4. **Information field mechanism:** a generic optional `DetailFieldDef.display(record) → { text?, note? } | null` in `RecordDetailFields.tsx`; only the invoice's `sprk_reviewdetail` field uses it. No entity-name branch in the renderer. The field is added to the invoice read description after Status, so the record read carries it (and the status, already read).
5. **Header secondary line:** optional `secondary(record)` on `RecordHeaderFact` (`recordTemplate.types.ts`), rendered by `RecordTemplateHeader` under the value (test id `record-template-fact-secondary`); `RecordTemplateModal` passes `fact.secondary?.(record)`. Only the invoice template's Status fact sets it. The modal stays config-only.
6. **Grid cell:** `createGridWidgetBody(configId, overrides?)` forwards the shared grid's own `overrides` prop; `InvoicesWidget` passes `INVOICE_GRID_OVERRIDES` (`columnRenderers.sprk_reviewdetail` → `reviewDetailCellText(row)`; reads `row.statuscode` raw). The type is read off `ExternalDataGrid`'s props (`React.ComponentProps<typeof ExternalDataGrid>['overrides']`), so GridWidgetBody imports nothing new from the shared grid and `ExternalDataGrid.tsx` is untouched (its text is pinned by `ExternalSpaGridViewSelectorGuardTests`). The renderer is never called until the column exists in the live config.
7. **FR-12: no change.** The held-Accepted behaviour is correct on master and its three states are already pinned in `Spaarke.UI.Components/src/components/InvoiceSubmission/__tests__/InvoiceSubmissionOutcome.warning.test.tsx`: held Accepted → "In Review" / `warning` (:241); overridden Accepted (`ebilling.submission.overridden`, no warning) → "Accepted" / green (:253); plain Accepted → "Accepted" / green (:266). Re-ran the three Outcome suites: 3 suites, 34 tests, all pass. I added no second held signal, no component change and no duplicate test (the "tests missing" set is empty).
8. **Test scope justification** (acceptance: beyond the stated contract needs one line): the extra cases are (a) status given as a numeric string / non-numeric / label (the gate's fail-closed edges), (b) a blank value with the key absent from the row, (c) a control that no other entity gained a gated row, and (d) a control that `createGridWidgetBody` without `overrides` hands the grid none (acceptance: other grids unchanged).

## 3. For task 028 — the grid column (live write; owner approves; NOT done here)

Target: `sprk_gridconfiguration` `3ff4102c-1092-f111-b8dc-7ced8ddc4a05`, field `sprk_configjson`.

**I could not build a complete PATCH body: no live read was allowed, and the config has changed since R3 task 002's snapshot (statuscode moved in on 2026-10-18, `sprk_totalamount_base` and `sprk_name` added by R3).** What 028 applies is three exact mutations to the parsed live `sprk_configjson` (parse JSON, mutate, serialise; never hand-merge a string; STOP and re-plan if the live config does not look as described, per 028's own trigger):

1. `source.fetchXml`: add `<attribute name='sprk_reviewdetail'/>` inside the invoice `<entity>` (the `statuscode` attribute must already be selected: the gate reads the raw status from the same row. If it is not, add `<attribute name='statuscode'/>` too).
2. `source.layoutXml`: insert `<cell name="sprk_reviewdetail" width="200" />` **immediately after** the `statuscode` cell (the "Review detail" column after Status, FR-11a).
3. `columns`: add `"sprk_reviewdetail": { "label": "Review detail" }`. Do NOT set a `renderer` (the SPA's own cell renderer is installed through the grid `overrides`).

Resulting PATCH (Dataverse Web API, `PATCH [org]/api/data/v9.2/sprk_gridconfigurations(3ff4102c-1092-f111-b8dc-7ced8ddc4a05)`, `If-Match: *` not needed, body is the whole updated column only):

```json
{ "sprk_configjson": "<the parsed live JSON with mutations 1 to 3 applied, re-serialised to a single string>" }
```

**Precondition found while reading the server (a 028 check, not code):** the BFF **refuses a filter or order on `sprk_reviewdetail`** (`ExternalInvoiceReviewDetail.UnfilterableColumns`). The grid's filter chips in `filterChips.mode = "auto"` make a chip of every option-set column, and a local choice is one, so an auto chip on the new column would 400 on use. Before the PATCH, read `filterChips` in the live JSON:
- `auto` (or `denylist`): add `"sprk_reviewdetail"` to a `denylist` (switch `mode` to `denylist` with `denylist: [...existing, "sprk_reviewdetail"]` if it was `auto`);
- `allowlist` / `explicit`: nothing to do (the column is not listed).
Header-click sorting in the grid is client-side (it sorts the rendered text), so it does not reach the server.

**Rollback slot** (028 fills it before the PATCH; the same shape as R3's `ROLLBACK 3.5`):

```text
ROLLBACK 3.5 / 3ff4102c (raw sprk_configjson, modifiedon, versionnumber)
<to be captured by task 028 from the read-only GET, before the PATCH>
```
Rollback = PATCH `sprk_configjson` back to the saved raw string (the SPA renderer is inert without the column, so the SPA deploy needs no rollback for this).

## 4. Sequencing risk to carry into 028 / later deploys

The Invoices section list (`INVOICE_COLUMNS`) and the Invoice record read now request `sprk_reviewdetail`. That is safe on dev (entry live). It is **not** safe to deploy this SPA build against any BFF that lacks the 082c-2 entry: every Invoice record read and every Matter / Project Invoices section would answer 400 `DV_FETCHXML_COLUMN_NOT_PERMITTED`. The deploy gate for any other environment is "082c-2 live there first".

## 5. Files changed

- `src/client/external-spa/src/components/record/reviewDetail.ts` (new): the gate and the approved texts.
- `src/client/external-spa/src/components/detail/RecordDetailFields.tsx`: `DetailFieldDisplay`, optional `DetailFieldDef.display`, the invoice "Review detail" field after Status, its renderer branch and `note` style.
- `src/client/external-spa/src/components/record/recordTemplate.types.ts`: optional `secondary` on `RecordHeaderFact`.
- `src/client/external-spa/src/components/record/RecordTemplateHeader.tsx`: optional `secondary` on a fact and its line.
- `src/client/external-spa/src/components/record/RecordTemplateModal.tsx`: passes `fact.secondary?.(record)` to the header (one line).
- `src/client/external-spa/src/components/record/recordTemplates.ts`: the invoice Status fact names `reviewDetailSecondaryLine`.
- `src/client/external-spa/src/components/record/sections/InvoicesSection.tsx`: `sprk_reviewdetail` in `INVOICE_COLUMNS`; the list line shows the gated label after the status.
- `src/client/external-spa/src/widgets/GridWidgetBody.tsx`: optional `overrides` parameter, forwarded to the grid (`GridOverrides` type).
- `src/client/external-spa/src/widgets/InvoicesWidget.tsx`: `INVOICE_GRID_OVERRIDES` passed to the grid body.
- `src/client/external-spa/tests/ReviewDetail.test.tsx` (new): 30 tests.

## 6. Verification

- `npx vitest run tests/ReviewDetail.test.tsx`: 30 / 30 pass. Mutation check: with the status gate disabled, **9 tests fail** (the must-not-fire pair at the helper, grid cell, Invoices section, Information field and header), then restored; with the new files absent every test fails (module not found).
- `npx vitest run` (whole SPA, includes the parallel task 022's in-flight files): 68 files, 1908 tests, all pass.
- `npx tsc --noEmit -p .` in `src/client/external-spa`: exit 0.
- Shared lib `InvoiceSubmissionOutcome*` suites (jest): 3 suites, 34 tests pass (FR-12 verification; no change made there).
- Not run, by the wave rules: `npm run build`, the C# `ExternalSpaGridViewSelectorGuardTests` (source scan; the new `overrides={overrides}` is a plain JSX prop and the new type is read off the wrapper's props like `GridHostCommands`, so the guard's no-new-import / JSX-tag-only rules are untouched), `/conflict-check` (no shared file changed: `InvoiceSubmissionOutcome.tsx` was not touched), `scripts/check-task-status-drift.ps1` (no status edits by me).

## 7. Out-of-scope observations

None that are defects. (The POML's step 8 status/index edits were left to the main session, per the wave rules.)

## 8. Wave 4 review (main session, 2026-11-20)

- One `adversarial-reviewer` (opus) pass over 020 + 022; `adr-check` in the main session (no violations). All task 020 criteria met; the visible grid column waits on the live config PATCH in task 028.
- Fix: a comment in `reviewDetail.ts` read `relabelled from "Active"`, which `ExternalSpaGridViewSelectorGuardTests`' import scanner parsed as an import; reworded. ArchTests 2724/2724.
- Deploy gate copied into task 028's notes: any environment this build goes to needs ebilling's 082c-2 allow-list entry first.
- Open owner wording question (K4): a blank Review detail while In Review shows "The client is reviewing this invoice." in the Information field only (the POML criterion); header, grid and section stay blank.

rework: fix-round-1 task 020
