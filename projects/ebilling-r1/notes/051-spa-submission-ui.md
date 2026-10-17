# Task 051 - SPA submission UI (shared wizard component) (FR-18, FR-19, FR-45)

Status: built; unit tests green; browser ui-tests not run (see section 6). Rigor STANDARD. Built on 044 (routes and codes) and D-91 (firm invoice
number required at upload).

## 1. What was built

| File | Role |
|---|---|
| `src/client/shared/Spaarke.UI.Components/src/components/InvoiceSubmission/InvoiceSubmissionWizard.tsx` | The shared wizard: Record, File, Details over `WizardShell` + `FileUploadZone`; Finish submits; result screen mounts the Outcome panel |
| `.../InvoiceSubmission/InvoiceSubmissionOutcome.tsx` | Processing / Result screen: polls the host's `getStatus`; Accepted (tracking number), Rejected (reason code + host report slot, placeholder until 134), Failed (retry guidance). Also used alone for resume |
| `.../InvoiceSubmission/types.ts`, `messages.ts`, `index.ts` | Callback contract, `InvoiceSubmissionError`, `isEbillingDisabledError`, message tables by error code |
| `.../components/index.ts` | One barrel line (shared file) |
| `src/client/external-spa/src/pages/InvoiceSubmitPage.tsx` | Route page: availability probe, wizard, or resume view |
| `src/client/external-spa/src/App.tsx` | One `Route` (shared with SPA-r3) |
| `src/client/external-spa/src/widgets/InvoicesWidget.tsx` | "Submit invoice" action above the grid (shared with SPA-r3) |
| `src/client/external-spa/src/api/web-api-client.ts` | `submitInvoice`, `getInvoiceSubmission`, `probeInvoiceSubmission`, `listInvoiceCoreRecords`, `toInvoiceSubmissionError` (appended section; shared with SPA-r3) |

### Hand-off to task 052 (internal entry point)

Mount `<InvoiceSubmissionWizard>` with three callbacks and nothing else is required:

```ts
loadRecords: () => Promise<{ records: InvoiceCoreRecordOption[]; truncated?: boolean }>
submit:      (input: { record, file, firmInvoiceNumber, poNumber? }) => Promise<{ submissionId, trackingNumber, ... }>
getStatus:   (submissionId) => Promise<{ submissionId, status, trackingNumber, outcomeReasonCode, ... }>
```

Reject with `InvoiceSubmissionError({ statusCode, errorCode, detail })` for a server refusal (the SPA adapts `ApiError` in
`toInvoiceSubmissionError`). Optional props: `embedded`, `title`, `onSubmitted`, `renderReport` (the Rejected report slot for 134), `maxFileSizeBytes`
(default 25 MB, the server default; a convenience only), `pollIntervalMs`, `doneLabel`. The internal route's `firmId` query parameter is the host's
concern: `submit` is where it is added.

## 2. How the flow meets the requirements

- **D-91**: the Details step cannot finish while the firm invoice number is blank (whitespace counts as blank); the PO number is optional.
- **FR-18 resume**: after the 202 the page rewrites its address to `/invoices/submit?submissionId={id}` with `history.replaceState` (no router navigation, so the
  wizard stays on its result screen). Reloading or returning to that address renders the Outcome panel alone from `GET .../invoice-submissions/{id}`. Nothing is
  stored in the browser.
- **FR-45 / the 503 probe**: `probeInvoiceSubmission` calls `GET /api/v1/external/invoice-submissions` (the caller's own list). Only a 503 whose
  `errorCode` is `ebilling.disabled` means "off". The escalation question (can the probe tell "disabled" from an outage?) is answered by the code: 044's other 503s
  carry `ebilling.submission.record_unavailable`, `.firm_unverifiable` or `ebilling.tracking.unavailable`, and any non-`ebilling.disabled` failure leaves the entry
  visible (tests cover an outage 503 and a 403). The page runs the same probe first and replaces itself with "Invoice submission is not available" when off.
- **ADR-002 / FR-17**: the client creates no `sprk_invoice` / `sprk_invoicesubmission` and decides no authorization. A refusal shows the server's ProblemDetails `detail`;
  with no detail, a fixed sentence by `errorCode`, then by HTTP status. The wizard stays on Details and the user can retry.
- **Standing constraint (uac-r2 #2047)**: no upload or storage route taking `sprk_billingevent` as parent was added. The vitest suite asserts that every request the
  flow makes is the probe, a record read, or an `invoice-submissions` route on matters / projects / work assignments.
- **ADR-012**: the shared component imports no SPA auth, Xrm or BFF URL (a jest test scans its imports).
- **ADR-021**: Fluent v9 tokens only; a vitest test scans the new UI files for hex / rgb / hsl literals. The dark theme comes from the existing FluentProvider.
- **NFR-12**: no `package.json` change in either package.

## 3. Deviations from the POML and from what the brief assumed

1. **`firmId` is a query parameter, not a form field.** The POML says multipart `firmId`; 044 (the code) reads it from the query. The SPA sends none: the server takes the
   caller's only firm.
2. **Record picker data.** There is no external list route for matters or work assignments. Matters and work assignments are read through the Tier-2 module read seam
   (`gridDataverseClient.retrieveMultipleRecords`, FetchXML limited to the columns `ExternalAccessModule` admits: id, name, number); projects use `GET /projects`.
   If one source fails the others are still offered and the picker says the list is partial.
3. **Client-side PDF check needed a validator.** `FileUploadZone` accepts a file whose extension OR MIME type is in its accepted set, and the Word / Excel MIME types are
   always in it, so `acceptedExtensions: ['.pdf']` alone let a `.docx` through (a jest test failed on it). The wizard passes a `customValidator`. The shared zone itself
   was not changed.
4. **Phone width.** `WizardShell` draws a fixed 200 px step list beside the content, unusable at 375 px, and the modal preset is 62vw wide. The SPA page mounts the wizard
   `embedded`, and the wizard hides the step list under 600 px with a media-query rule on its own wrapper (each step shows "Step n of 3"). `WizardShell` /
   `WizardStepper` were NOT changed (other wizards use them); a shared responsive mode would be the better long-term fix and is for the owner of that component.
5. **Result screen title is fixed.** `WizardShell`'s success config has a static icon and title, so the result screen titles itself "Invoice received" (true for every
   outcome: a tracking number was issued) and the status badge and messages below it carry Processing / Accepted / Rejected / Failed.
6. **`AiProgressStepper`** (named in the POML's composition list) was not used: it is an AI-run progress view; a spinner with "Processing" and the status badge fit this
   screen. `StatusBadge`, `WizardShell`, `FileUploadZone`, `UploadedFileList` were reused.
7. **Dev mock** (`mock-service.ts`) was not extended for the new routes (not in the POML's file list).
8. **TASK-INDEX.md** was not updated (brief: the main session owns it).

## 4. For SPA-r3 (shared files touched)

- `src/client/external-spa/src/widgets/InvoicesWidget.tsx`: the grid binding is unchanged (`createGridWidgetBody(CONFIG_ID)`), now wrapped in a column with a right-aligned
  "Submit invoice" button above it, rendered only inside a router and only after the probe has not answered `ebilling.disabled`. The export is still
  `InvoicesWidgetBody` (named and default), so `widgetRegistry.ts` and `DetailDocumentsDelete.test.tsx` are untouched.
- `src/client/external-spa/src/App.tsx`: one import and one `<Route path="/invoices/submit">` beside `/upload`.
- `src/client/external-spa/src/api/web-api-client.ts`: header comment lines, three new imports (`ApiError`, `gridDataverseClient`, the shared InvoiceSubmission module) and
  an appended "Invoice submission" section before the `webApiClient` object. No existing export changed.
- No open PR touched these files when checked (2026-10-17); the SPA-r3 worktree had no uncommitted change under `src/client/external-spa`.

## 5. Open items / known limits

- **Multi-firm callers.** 044 answers 400 `firm_required` when the caller belongs to several firms and the request names none. The SPA has no read of the caller's
  firm memberships, so it cannot offer a firm choice: such a caller sees the `firm_required` message ("contact the team") and cannot submit. A single-firm caller (the
  expected case) is unaffected. A firm list on `/api/v1/external/me` (or a small route) plus a firm step would close it; filed as a question for the owner.
- **View-only records.** The picker lists every readable project / matter / work assignment; a record where the caller lacks Create answers 403 `record_forbidden` at
  submit (shown by code). The server stays the authority (ADR-002).
- **Reason codes** are shown verbatim ("Reason: duplicate_invoice") until 049 / 134 define user-facing text and the report slot.
- **Lost 202.** 044 does not recognise a repeated upload (INV.2): after a network failure with no response the wizard says the submission could not be confirmed and to check
  the invoices list before sending again.
- The record read asks for 200 rows per matter / work-assignment source and flags a longer list as partial.

## 6. Evidence

- Shared jest (`InvoiceSubmission`): 29 passed (2 suites). SPA vitest: `InvoiceSubmitPage.test.tsx` 18 passed; whole SPA suite 37 files / 1037 passed.
- BFF arch guards that read the SPA: `ExternalSpaGridViewSelectorGuardTests` 231 passed, `ClientUploadRouteAgreementTests` 6 passed. The guard first FAILED on the widget
  (`useInRouterContext` imported outside App.tsx: any router name is refused); the widget now reads `UNSAFE_NavigationContext` (null outside a router) instead, so the
  unchanged `DetailDocumentsDelete.test.tsx`, which renders the grid body with no router, still passes.
- Seeding proof: with the probe made to answer "disabled" for every failure, the outage-503 page test and the outage-503 / 403 widget test fail (2 failed / 16 skipped
  run); restored, all 18 pass. The `.docx` client-side check failed before the custom validator was added.
- Typecheck (`tsc --noEmit`) and lint (`eslint src tests`) clean in external-spa; `eslint` clean and `tsc` free of new errors in the shared library (its existing errors are
  the unbuilt `@spaarke/auth` / `@spaarke/sdap-client` sibling packages, also behind 6 jest suites that fail to load there on this machine); `vite build` succeeds.
- Not run: the browser ui-tests (happy path against a BFF, phone width at 375 px, dark mode walk-through). No sign-in, BFF or browser tool was available in this
  session. What stands in for them: tokens-only file scans, the emitted `@media (max-width: 600px)` rule asserted in jest, and wire-level vitest tests.
