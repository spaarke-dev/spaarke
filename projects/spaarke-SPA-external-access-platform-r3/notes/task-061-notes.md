# Task 061 notes - shared DataGrid shows @formattedValues labels

Rigor FULL, Sonnet/high (as the POML declares). Owner check 2 issue 1: the Service Requests Status column showed raw JSON.

## Change

`src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx`

- New exported `formattedLabelOf(row, column)`: returns `row['@formattedValues'][column]` when it is a non-blank string, else `undefined`.
- `renderCellValue(value, renderer, formattedLabel?)`: new optional third argument. When present it is returned before the renderer switch. An empty/null raw value still renders `''`.
- The non-primary, non-custom-renderer branch of `renderCell` passes `formattedLabelOf(item, col.name)`.

Precedence: `@formattedValues[col]` -> `.name` -> primitive -> `JSON.stringify` (the last three are the old `default` branch, untouched). No styling change (ADR-021). No BFF change; `/api/dataverse/fetch` contract untouched.

## Deliberate scope edges (one-line justifications)

- Custom `columnRenderers` are unchanged (POML constraint) - they receive the raw value as before.
- The primary-name link keeps `renderCellValue(value, 'default')` - it is a name string; no label exists for it and the link behaviour is not part of this task.
- Date renderers (`date`, `dateonly`, `datetime`) ignore the label. `FetchService.ProjectEntity` also formats DateTime attributes (server culture/zone); honouring that would regress the task 098 calendar-day rule and the local-time rendering that consumers have today. Option-set, lookup and Money are the cases that render as JSON / `[object Object]`.
  This narrows the POML's literal "label wins" wording for date columns only; flag if the owner wants server-formatted dates.
- Tests cover the stated contract plus: blank / empty / non-string / null label (fall back), lookup name overridden by label, Money under the `currency` renderer, date renderers unchanged, row without `@formattedValues` unchanged (lookup, primitive, object JSON).

## Tests

`src/components/DataGrid/__tests__/DataGrid.formattedValues.test.tsx` (new, 17 tests). Before the fix: 11 failed / 6 passed (the 6 are the unchanged-behaviour controls). After: 17 passed.

## Verification

- Shared library, `npx jest src/components/DataGrid`: 10 suites, 100 tests passed.
- External SPA (`src/client/external-spa`): `npm run typecheck` clean; `npm test` 35 files / 993 tests passed; `npm run build` OK (existing chunk-size warning only).
- Shared library had no `node_modules`; installed with `npm install --legacy-peer-deps --no-audit --no-fund` (no lockfile change).

## Deploy

The fix reaches dev with the next SPA deploy (owner-approved). UI checks (Service Requests status label; dark mode) are not run here - no live commands in this session.

## Review round (adversarial pass, 2026-10-13; fixes by the main session)

| Finding | Class | Action |
|---|---|---|
| Sorting an option-set/lookup column compared `"[object Object]"` (pre-existing, live path) | F1 | Fixed: `compare` sorts on the displayed string (label first); dates keep the raw ISO compare; numbers stay numeric. Test: "sorting the Status column orders rows by the label". |
| Integration test declared Status/Lookup columns as `String`, so they rendered via `'default'`, not the production `'badge'`/`'link'` | F3 | Fixed: metadata uses `Status` and `Lookup`. |
| A server label overrode an explicit `percentage` renderer and a numeric `currency` column | K2 | Fixed anyway (cheap): `percentage` is client-formatted; `currency` uses the label only for a Money object. Test added. |
| The primary-name link ignored the label | K1 | Fixed: the link passes `formattedLabelOf`. |
| `FetchService.cs:134-135` comment says `"FormattedValues"`; code writes `"@formattedValues"` | K4 | Left: comment-only drift in the BFF, not worth a BFF touch here. |

Shared library DataGrid: 10 suites, 102 tests pass (formatted-values file 19/19).
