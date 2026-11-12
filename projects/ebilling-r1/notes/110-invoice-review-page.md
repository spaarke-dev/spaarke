# Task 110 - Invoice Review code page

Code page: `src/solutions/InvoiceReview/` (web resource **`sprk_invoicereviewpage`**). Rigor: FULL (declared STANDARD in the POML; raised because the task writes `.tsx`). Model: Opus, effort high.

Commits on the task branch (from `7b4fd99b58`, work/ebilling-r1):

- `b2c76e4692` feat: the page.
- `2285000174` fix: self-review findings (forms close on 403 or 503 incomplete; Reload on 409; two vacuous test assertions).

## What the page does

| Area | Built from |
|---|---|
| Header: firm, firm invoice number, tracking number, status, core record link (opens the matter, project or work assignment through Xrm navigation), period, invoice date | read model `header`; `StatusBadge` |
| Totals: Billed, Line adjustments, Invoice adjustments, Approved total | read model `totals`, re-read after every write (never computed, I-1) |
| Count filters: All lines (with "n of m decided" progress), Processing, To decide, Clean, Decided | `MetricCardRow layout="wide" ariaLabel="Line filters"` fed `MetricCardConfig[]` from a page-local builder (`services/reviewModel.ts buildLineCountCards`, the `buildLaneCountCards` shape). All stays enabled; the other cards use `disableWhenEmpty`; choosing the selected card returns to All. |
| Lines grid: tick, Status, Date, Timekeeper (unmatched mark), Narrative (AI-observation mark), Hours, Amount, Findings badge (severity and count; hovering lists the rules), Approved; low-confidence mark on a value | Fluent v9 `DataGrid` primitive (see "Grid decision") |
| Clicking a value selects the line and boxes that value on the PDF; clicking the row selects the line | `highlightFor` builds 108's `{page, row, field, label}`; the row box is the union of the line's field boxes on that page |
| By rule: groups by rule, most severe first, then count; "Apply suggestion to n lines…" | `groupByRule`, `planSuggestion`; ONE call to `POST .../lines/decisions` with each line's own approved amount (billed − suggested reduction) |
| Right pane, Review tab: findings (rule, message, suggestion; "Why this was flagged" in a Fluent `Accordion`: policy, version, clause location, quoted clause, values read as `EvidenceLine`), line findings, "Your decision" (Fluent `RadioGroup`, suggestion first and preselected, one Confirm, then the next line to decide is selected), "What was read" (`EvidenceLine` rows with ✎ Correct and, on a low-confidence value, Confirm), the decided state (`StatusBar` with `statusOverride`), and the invoice PDF (`PdfHighlightViewer`) below | `components/LineReviewPane.tsx` |
| With no line selected: the invoice-level findings and "Start with the first line to decide" | |
| Action bar: Accept all clean lines (n), or with ticked rows Accept… / Disallow… (FormModal, one reason, one request), progress ("n to decide · n processing") | |
| Forms: Correct value (line or header field) and the bulk decision | shared `FormModal` (ADR-050) |
| Slots for task 111 | `paneTabs` (Context, Related work, History), `headerExtras` (Awaiting approvers), `banners` (blocked submission), `invoiceActions` (Decide invoice…); each receives `{ review, canWrite, reload }` |

Every route of 100 and 101 is called: `GET review`, `POST lines/{id}/decision`, `POST lines/decisions`, `POST lines/accept-clean`, `POST lines/{id}/corrections` (value and `confirm`), `POST header/corrections`, `GET corrections` (shown as "Corrected: ai → final" under a value). 102's adjustment lines and totals arrive in the read model; adding an adjustment is 111's approve wizard (FR-32, as in the prototype). The PDF bytes come from `GET /api/finance/files/{invoicePdfFileId}/content`.

### Server truth and errors

- Every write is one BFF call. On success the page re-reads the review and the corrections and shows the server's state. On a refusal nothing on the page changes and the server's ProblemDetails `detail` is shown (in the form when it came from a form).
- A missing reason is not blocked on the client: the server refuses it (`ebilling.line.reason_required`) and the page shows the refusal (ui-test "Decide a flagged line", step 3).
- 403 on a write: the page turns read-only for the session and says so. 403 / 404 on the read: an access message.
- `ebilling.disabled`: "eBilling is not enabled in this environment.", no Retry.
- 503 `ebilling.decision.incomplete` / `ebilling.correction.incomplete`: the notice offers **Repeat**, which sends the same request with the same decision id (the server records nothing twice).
- 409: the notice offers **Reload**.
- Read-only: `flags.canDecide` false, the invoice not In Review (header status shown, "shown read-only"), or a 403 seen. No ticks, no decision, no Correct, no Accept all clean, no header correction.
- F-1: stored Pending shows **Processing** (spinner) in the grid, the pane and the cards; such a line has no tick and no decision. While any line is Processing the page re-reads every 5 s and stops when none is.

### Line findings (EB-D-175 / 176 / 101 F2)

`lineFindings` entries are shown as reasons on the line (also when the line has no policy finding). The grid badge counts policy findings plus line findings (the server's `findingCount` counts policy findings only); with only line findings the badge severity is Warning. A null or blank message shows a fixed text per code (`LINE_FINDING_FALLBACK` in `services/reviewModel.ts`): `coding.not_returned`, `line.no_description`, `rules.not_checked`, and one generic sentence for an unknown code.

## Decisions and deviations

1. **Web resource name `sprk_invoicereviewpage`** (the POML's directional name). The navigation entry (sitemap, invoice form command or view command) is not added: it is a solution change and belongs to task 112 (escalation trigger 1, recorded, not acted on). `src/dataverse/solutions/SpaarkeMaster/**` is unchanged.
2. **Skeleton copied from `src/solutions/InvoiceFiles/`, not `CommunicationReconciliation/`.** Both have the same `main.tsx` shape (theme, `AppErrorBoundary`, `bootstrapAuth`), and `vite.config.ts` / `tsconfig.json` are identical to RuleReview's. CommunicationReconciliation's package.json lacks `pdfjs-dist` (which `PdfHighlightViewer` needs) and any jest setup, and carries `@spaarke/communication-components`, which this page does not use. InvoiceFiles (task 151e, this project) has exactly the dependency set needed. `package.json` and `package-lock.json` differ from InvoiceFiles only in name, description and the build's output name: **no new npm package** (NFR-12). `ensureNavigatorSidePane()` in a mount effect is copied from CommunicationReconciliation (`main.tsx`).
3. **Launch parsing** uses the shared `parseDataParams` (as InvoiceFiles does) instead of copying `sprk_invoicespage`'s `parseMatterId`; the precedence is the same (an explicit id, then the data envelope). Accepted: `invoiceId`, `id` (+ `typename=sprk_invoice`).
4. **Grid decision:** the Fluent v9 `DataGrid` primitive fed by the read model, no virtualisation (F-5). The Spaarke DataGrid framework is FetchXML / config driven (`DataGrid.tsx` needs a `configId` and an `IDataverseClient`; overrides are cell renderers only) and cannot take 100's read-model rows with row ticks and a per-value click. The row tick is a Checkbox column of the page's own (the primitive's `selectionMode` toggles selection on a row click, which would conflict with "click a row to open it").
5. **PDF route:** the POML background names `GET /api/documents/{documentId}/content`; the read model carries `invoicePdfFileId` (task 092, EB-D-148: invoice PDFs are Finance Stored Files), so the page uses `GET /api/finance/files/{fileId}/content`. An invoice with no servable PDF shows "This invoice has no PDF that can be shown yet."
6. **Correctable fields:** fee lines Date, Hours, Rate, Amount, Narrative, Task code, Activity code; expense lines Date, Amount, Narrative, Expense code; header FirmInvoiceNumber, InvoiceDate, BillingPeriodStart, BillingPeriodEnd, Currency, TotalDue. **Not offered: `TimekeeperAsBilled`.** 101 takes the id of a roster entry of the firm, and no route the page calls lists the roster; offering it needs a roster read (111's Context API or a new route). The unmatched-timekeeper mark is shown.
7. **Prototype behaviour not reproduced:** the left card's third view "Invoice" (the PDF in place of the grid with ‹ › line browse), the 1600 px rule that moves the document to that view, the "Open in viewer" BrowseModal, and the draggable top/bottom split in the pane (F-11: there is no vertical `PanelSplitter`; the reuse constraint forbids a new component). The pane splits line review and document 50/50. The blocked-at-submission banner and Override, the "Awaiting" approvers and the invoice decision wizard are 111's (slots left).
8. **Codes in "What was read"** use `EvidenceLine` tier Interpretation with `AiFieldTag` ("Coded by AI").
9. `current-task.md` was not rewritten: this task ran as a worktree agent while the main session holds that file for other work (the brief said to update the TASK-INDEX row only).

## Verification

- Jest (`npm test` in `src/solutions/InvoiceReview`): **3 suites, 51 tests pass** (`invoiceReviewApi.test.ts` 9, `reviewModel.test.ts` 13, `InvoiceReviewPage.test.tsx` 29). The page suite renders the real shared components through a test seam (`src/__tests__/uiComponentsTestSeam.ts`; only `PdfHighlightViewer` is a stand-in that exposes the highlight it is given), in light and dark themes.
- Fail-before proofs: (a) with `findingsBadge` ignoring line findings and `isDecidable` admitting Pending lines, 5 tests fail; restored, all pass. (b) The two tests added with `2285000174` fail against `b2c76e4692`'s page and pass after. (c) A first run caught a real defect: `toFailure` fell back to the transport message ("HTTP 409") as the shown detail; fixed before the first commit.
- Type-check (`npm run typecheck`, the `tsc-surface-gate`): surface-owned errors **0** (100 pre-existing shared-library errors, deferred by the gate).
- Production build (`npm run build`): one self-contained file, `dist/sprk_invoicereviewpage.html`, 1,876 kB (562 kB gzip), all script inline.
- Lint: no ESLint flat config applies to `src/solutions/*` code pages (same as the sibling pages); prettier ran through lint-staged on commit.
- Source guards (in the page suite): no file imports from the prototype repository; no `Xrm.WebApi` or record writes; `ensureNavigatorSidePane()` is called in a mount effect.

### Quality gates

- Code review: a self-review (the session's subagent cap was reached, so the adversarial reviewer could not be launched). Findings fixed in `2285000174`: (F1) a 403 or 503 incomplete from a form left the modal open over the page notice that carried Repeat; (F1) a 409 left a stale page with no re-read; (F2) two tests asserted a closed dialog with a role query that passed while it was open.
- ADR check: ADR-012 (shared components only; none added; nothing from the prototype), ADR-021 (Fluent tokens only; grep found no colour literals; the PDF page stays light through 108's nested provider; FluentProvider unstyled, surface on a wrapper div, F-10), ADR-028 (`createCodePageAuthInitializer`, `requireSilentOnly`, every call through `authenticatedFetch`; the page waits for auth before it mounts), ADR-002 / I-1 (no Dataverse access, no computed totals), ADR-050 (FormModal for both forms). Clean.

### Not run here (task 112, after deploy)

The POML ui-tests need the deployed page: no browser integration in this session and no deploy allowed. Open for 112:

- The five ui-tests (render + Navigator docked, decide a flagged line, correct a misread value, bulk / by rule / read-only / Processing, dark mode) in light, dark and **high contrast**, including the reused MetricCard count filters, Accordion disclosure, RadioGroup outcome choice and the `statusOverride` StatusBar (task 109 constraint).
- 108's three browser tests (highlight, scroll-to-field, error/retry) on a real invoice PDF, e.g. INV-2025-78232.3 (`0d9178d3-9f32-7f46-ed9a-4dec59f66c1a`: 22 lines, Patent code set, 16 flagged lines).
- **The pdfjs worker CSP.** The shared loader points `GlobalWorkerOptions.workerSrc` at `cdn.jsdelivr.net` when it is empty. If the Dataverse code-page CSP blocks it, the page must preset a same-origin worker (a second web resource carrying `pdf.worker.min.mjs`) before the first load; that is a deploy-side change for 112.
- The 150-line timing (< 3 s). The grid re-renders all rows on a selection change (column definitions depend on the selection); at 150 rows this is expected to be acceptable (F-5 measured 155 ms unmemoised in the prototype's dev build). If it is not, memoise the rows.

## Known limits (K-class)

- K4: grid rows are not memoised per row (see above); measure in 112.
- K2: a poll re-read and a write can overlap; both re-read the server, so the last answer wins and is the server's.
- K4: the field key used for a row box is whatever the read model's `fields` carry; a line with no boxes shows no highlight (the PDF still shows).
- Timekeeper correction not offered (deviation 6).

## Fix round 2 (coordinator review, 2026-11-03)

Done in its own commit, before task 111 (which edits the same files). Jest 65 / 65 (was 51); 13 of the new or strengthened tests fail against the round-1 source.

| # | Class | Fix |
|---|---|---|
| 1 | F2 | A 503 `ebilling.decision.incomplete` on Accept all clean is repeated through `POST lines/decisions` with the same decision id, action accept and the committed line ids. `ApiFailure.committedLineIds` is read from the ProblemDetails (`toFailure`). The old repeat re-sent accept-clean, whose lines were no longer Clean: 200, nothing applied. |
| 2 | F1 | `DecisionForm` is keyed on line id, status, amount and suggestion, so a correction that re-runs the rules cannot leave a stale "Suggested: reduce" choice or amount. "Suggested" with no suggestion decides nothing (Confirm disabled). |
| 3 | F2 | An Accept shows no reason field (single and bulk) and sends `reason: null`; the server drops it. |
| 4 | F1 | On a 503 incomplete the page re-reads the review before it offers Repeat. |
| 5 | F1 | A 409 from Bulk, By rule or Correct value closes the form and shows the page notice with Reload (other refusals stay in the form). |
| 6 | F1 | Polling is an interval (skipped while a re-read is out), so one failed re-read no longer stops it for good. A failed re-read notice has Reload and is cleared by the next successful re-read. |
| 7 | F1 | A write that was saved but whose re-read failed shows "... but the page could not refresh." (warning, with Reload), not plain Saved. Re-reads carry a sequence number; an answer older than the last one applied is dropped. |
| 8 | F1 | A fetch that threw a non-ApiError ("Failed to fetch", a non-JSON body) gives `detail: null`, so the status-0 sentence shows (write and PDF); the raw text goes to `console.warn`. |
| 9 | F3 | New source guard: no hex colour, `rgb(` / `hsl(` or named CSS colour as a style value anywhere in `src` (must-fire and must-not-fire cases); the dark-theme render test now also checks the rows. Tests for 1, 2, 3, 4, 5, 6, 7, 8. |
| 10 | F4 | The stray `rework:` line is removed from this file and from the POML. |

### Known limits recorded by the review (K-class, not fixed)

- K: the PDF worker is loaded from jsdelivr with no SRI (`pdfJsLoader.ts:70-73`), and its fallback constant `6.2.108` is behind the `^6.4.299` the package asks for. Flagged for the CSP check in task 112 (with the same-origin worker option already noted above).
- K: only one notice shows at a time; a later notice replaces an earlier one.
- K: number parsing in the Approved amount and Correct value fields assumes en-US separators.
- K: read-only mode and the eBilling switch only hide buttons; the server stays authoritative and refuses a write regardless.
- K: a Processing (stored Pending) line cannot be corrected; it becomes correctable when it leaves Processing.

