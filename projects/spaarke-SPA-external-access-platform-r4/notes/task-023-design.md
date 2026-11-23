# Task 023: design for Documents on the shared DataGrid on every record type

> Status: DRAFT for owner approval (2026-11-22). Owner direction 2026-11-22: *"We want to use a shared grid component across all record types. If this is option D plus option C plus option B that's fine. We want the design and build to support these new requirements and flexibility for future requirements."*
> This replaces the step 1 escalation in `task-023-notes.md`. No code yet.

## 1. Goal

The Documents list on **every** record type (Project, Matter, Work Assignment and Service Request) renders on the shared `DataGrid`. That covers:
- column resize and ellipsis with a tooltip on truncated cells;
- no overlap at 1000px;
- the same rows, rights and actions as today: Download, Version history, Delete own, Upload, and the `hasFile` hiding.

The shared grid gains two generic seams that any later list can reuse: a row source and a row-actions column. Candidates are the record Invoices section, the Events list, invoice Files and internal surfaces.

## 2. What the mapping found (2026-11-22, this branch)

| Fact | Where |
|---|---|
| The grid takes its rows only from FetchXML through `IDataverseClient` and `useLazyLoad`. There is no host-supplied row seam. | `DataGrid.tsx`, `useLazyLoad.ts` |
| `onRecordAction` is accepted but never wired (`_onRecordAction`). `overrides.columnRenderers` can only replace a cell of a column that is already in the configuration. | `DataGrid.tsx:1209`, `:1236` |
| `resolveConfig` is a pure function over a configuration object. The configuration comes from `configId` through `fetchConfigRecord`. | `configResolution.ts:252`, `DataGrid.tsx:~800` |
| Resizing is native, and widths are fitted to the container. Cells have no tooltip for truncated text. | `DataGrid.tsx:~1260-1310` |
| 34 non-test files mount `<DataGrid>`, so every change must be additive and every new prop optional. | grep |
| The record route list (`ListDocumentsForParent`) is the authorised path. It does several things: <ul><li>Read on the parent;</li><li>the secure-roots rule;</li><li>invoice-linked hiding (`HideInvoiceLinkedAsync`);</li><li>`truncated`;</li><li>`canCreate` from the upload route's own check plus the workforce ceiling;</li><li>it covers service requests (`sprk_relatedservicerequest`).</li></ul> Its DTO already carries `sprk_summary` and `hasFile`. | `ExternalProjectDataEndpoints.cs:644-687`, `ExternalProjectDtos.cs:162-200` |
| The `documents` module fetch has no service-request dimension, does not read `sprk_summary`, and is a different authorisation path. | `ExternalAccessModule.cs:524-556` |
| A grid configuration is readable in the SPA only if its id is in `OutsideCounselGridConfigurationIds`. | `ExternalAccessModule.cs:58`, `:676` |

## 3. Design

**Rows come from the host, not from FetchXML.** That means no BFF scope or allow-list change. The original option C would have moved the list onto the module fetch:
- add a service-request scope dimension;
- add `sprk_summary` to the allow-list;
- review whether the module path gives the same rows.

That changes the authorisation path of a list that works today, and it needs UAC review of three surfaces. Reading the record route through the grid keeps every server check the list has now, and it works for service requests unchanged. Option C shrinks to a new grid configuration (§3.3).

### 3.1 Shared DataGrid, option B (additive, every prop optional)

1. **`rowSource`** is a host page loader:

   ```ts
   rowSource?: (req: { page: number; pageSize: number; signal: AbortSignal }) =>
     Promise<{ rows: Record<string, unknown>[]; hasMore: boolean; truncated?: boolean }>;
   rowSourceKey?: string; // change it to reload page 1 (refresh after upload/delete)
   ```

   When `rowSource` is given:
   - the grid still resolves columns, labels and renderers from `configId` (configuration plus entity metadata), as today;
   - it does not run the FetchXML query, chips or host-filter overlays;
   - paging, the sentinel and lazy scroll stay as they are (ADR-051);
   - `truncated` shows the grid's notice.

   When it is absent, behaviour is unchanged, byte for byte, and a test proves it.

   It is a callback: no Xrm, no fetching inside the shared lib, which is ADR-012's own rule ("data fetching stays with the host").
2. **`rowActions`** is a host actions column, last in the grid:

   ```ts
   rowActions?: ReadonlyArray<{ id: string; label: string; icon: ReactElement;
     isVisible?: (record) => boolean }>;
   ```

   - It wires the existing `onRecordAction(actionId, recordId, record, ctx)`.
   - Hide, don't disable: an action whose `isVisible` returns false is not rendered.
   - Each action renders as an icon button whose tooltip equals its aria-label.
   - The column has a fixed width and is not resizable.
   - Several visible actions fold into a "More" menu past an inline limit.
3. **Truncated-cell tooltip (option D):** text cells use ellipsis, and a cell whose text overflows shows a Fluent `Tooltip` with the full value. It is opt-in through `display.truncateWithTooltip` in the configuration, or a prop. It is off by default, so the other 33 consumers do not change. Every later grid can switch it on by configuration alone.
4. The existing native resize stays. Widths persisted per browser and Reset columns stay task 032, and they compose with this design: `rowSource` takes no sort, and the sort is still the grid's.

### 3.2 SPA (`DocumentLibrary` and the record Documents section)

- The record Documents section mounts `<ExternalDataGrid configId={RECORD_DOCUMENTS_GRID} rowSource={…} rowActions={…}>`. The loader is the existing `listDocuments(root, id)`, so all four roots, service requests included, work unchanged.
- Actions:
  - Download and Version history: `isVisible = hasFile !== false`.
  - Delete: `isCreatedByCaller` plus the delete right, through the existing handlers and dialogs.
  - Upload: stays on the section, following `listCreateRightOf`.
- The row name opens the Document record (task 013 behaviour) through `onRecordOpen`.
- **D-23 #2928:** hoist one `readFromParents(parents, read)` helper. Download and `DocumentPreview` both use it, which closes #2928.
- The Fluent `DataGrid` table code and the D-12 width classes in `DocumentLibrary.tsx` are removed. `ExternalDataGrid.tsx` is not edited.

### 3.3 Grid configuration, option C (small)

- **New `sprk_gridconfiguration` "External record documents":**
  - entity `sprk_document`;
  - columns Name (primary), Type, Date Added (`createdon`), AI Summary (`sprk_summary`, truncate + tooltip);
  - `display.truncateWithTooltip = true`.

  Its FetchXML source is never executed by the SPA, because the rows come from the host. It is still valid, so the configuration stays usable by any other host.

  Writing it live is a Dataverse write: it needs owner approval and a rollback (delete the record), and it is done in task 028 with the other grid PATCH. The configuration JSON is committed in the project as the source of truth.
- **BFF (one line plus a test):** add its id to `OutsideCounselGridConfigurationIds`, so the SPA can read it. The PR goes to uac-r2 for review, and the dev deploy goes through uac-r2 with the owner's go per SHA. No scope, allow-list or route change is needed.
- **Fallback, if the owner prefers no BFF change:** accept an inline `config` object prop instead of `configId`. It is not recommended, because columns would then live in code rather than in the platform's configuration model.

## 4. Task split (replaces 023)

| Task | Scope | Gate | Model |
|---|---|---|---|
| **023a** | Shared DataGrid: `rowSource`, `rowActions` (wires `onRecordAction`), truncate-with-tooltip, exported types, tests (seam must-fire / props-absent must-not-fire for each) | Owner approval of the prop shape for the dataset-grid owners (the 2026-11-13 precedent) | opus / high |
| **023b** | Grid configuration JSON (committed) + BFF id admission + test; PR → uac-r2 review | 023a shape agreed | sonnet / high |
| **023c** | SPA: Documents section and `DocumentLibrary` on the shared grid for all four roots; actions; 1000px no-overlap test; D-23 hoist (closes #2928); FR-15 checks | 023a merged into the branch | sonnet / xhigh |
| 028 (existing) | adds: create the configuration live (owner go + rollback); the BFF deploy via uac-r2 comes before the SPA deploy | 023b deployed | — |

023a and 023b can run in parallel once the shape is agreed. 023c starts after 023a.

**Follow-on adoption (not in R4 scope unless the owner adds it):** the record Invoices section, the Events list and the invoice Files list move onto the same seams. Each needs its own configuration. These are filed as a DEF entry and an issue when 023c closes.

## 5. ADR and rule check

- **ADR-012:** additive optional props; callbacks only; no Xrm or entity literals; types exported; tests. Path C (comply).
- **ADR-051:** paging and lazy scroll are kept, and the `rowSource` API pages. Today's documents route answers one capped page with `truncated`, as the list does now. Paging that route is a possible later BFF change, not needed now. Path C.
- **ADR-021:** Fluent v9, tokens; the tooltip is a Fluent `Tooltip`. **ADR-038:** KEEP paths; every must-fire test has its must-not-fire pair.
- **Root §11, for each new surface:**
  - `rowSource`. Existing: `useLazyLoad`. Extension: the grid keeps its loader and adds a host path. Without it, a list whose rows come from a route other than the module fetch cannot use the shared grid; service requests and the record-route checks fail FR-16.
  - `rowActions`. Existing: `onRecordAction`, accepted but unwired. Extension: this wires it. Without it, Download, Versions and Delete cannot appear in a shared-grid list, which fails FR-16's "all existing document actions still work".
  - Truncate-tooltip. Existing: native resize only. Without it, #2490's truncated cells stay unreadable.
  - Configuration id admission. Existing: `OutsideCounselGridConfigurationIds`. Extension: one id is added. Without it, the SPA cannot read the configuration, and the grid answers 403/404.
- **Spec:** FR-16 is unchanged ("renders on the shared `DataGrid`"). FR-15 is unchanged.
- **Overlap:**
  - Task 032 (persisted widths and sort) builds on the same grid. Sort stays in the grid, so there is no conflict.
  - Task 033 (Money sort and export) is independent.
  - Task 021 (invoice Files, done) can adopt it later.

## 6. Open questions for the owner

1. Approve this design and the 023a/b/c split? **Open.** On 2026-11-22 the owner asked whether it affects the model-driven app grids. Answer given: the out-of-the-box views and subgrids are not affected. Custom pages in the MDA that use the shared `DataGrid` (for example the Invoices, Communications, KPI Assessments and Events pages) get the options, but nothing changes for them unless they opt in.
2. Grid configuration in Dataverse with a one-line BFF change (recommended), or an inline configuration in code (no BFF)? **Open.** The owner asked whether this means a grid configuration record. Answer: yes, a new `sprk_gridconfiguration` row. Record 3af4102c is not edited, because it also backs the workspace Documents widget.
3. Approve the new DataGrid props on behalf of the dataset-grid owners? **Approved 2026-11-22:** *"If you mean other project owners then yes I approve"*.
