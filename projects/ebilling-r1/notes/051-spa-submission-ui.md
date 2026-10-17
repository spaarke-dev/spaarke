# Task 051 - SPA submission UI (shared wizard component) (FR-18, FR-19, FR-45)

Status: built, including the "Choose firm" step (section 7); unit tests green; browser ui-tests deferred to task 053, the WP2 dev deploy (section 6). Rigor STANDARD.
Built on 044 (routes and codes) and D-91 (firm invoice number required at upload).

**Decision (owner, 2026-10-17): add a firm step.** A caller who belongs to several firms chooses which one the invoice is from, in a required step.

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

Mount `<InvoiceSubmissionWizard>` with three callbacks and nothing else is required (a fourth, `loadFirms`, is optional):

```ts
loadRecords: () => Promise<{ records: InvoiceCoreRecordOption[]; truncated?: boolean }>
submit:      (input: { record, file, firmInvoiceNumber, poNumber?, firmId? }) => Promise<{ submissionId, trackingNumber, ... }>
getStatus:   (submissionId) => Promise<{ submissionId, status, trackingNumber, outcomeReasonCode, ... }>
loadFirms?:  () => Promise<{ firmId: string; name: string }[]>   // omit it and there is no firm step
```

`firmId` arrives on `submit`'s input only when the user chose one (more than one firm); the host sends it as the `firmId` query parameter of the route.

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

1. **`firmId` is a query parameter, not a form field.** The POML says multipart `firmId`; 044 (the code) reads it from the query. The SPA sends it only when the user
   chose a firm (several firms, section 7); a single-firm caller sends nothing and the server takes the only firm. 044's contract is unchanged.
2. **Record picker data.** There is no external list route for matters or work assignments. Matters and work assignments are read through the Tier-2 module read seam
   (`gridDataverseClient.retrieveMultipleRecords`, FetchXML limited to the columns `ExternalAccessModule` admits: id, name, number); projects use `GET /projects`.
   If one source fails the others are still offered and the picker says the list is partial.
3. **Client-side PDF check needed a validator.** `FileUploadZone` accepts a file whose extension OR MIME type is in its accepted set, and the Word / Excel MIME types are
   always in it, so `acceptedExtensions: ['.pdf']` alone let a `.docx` through (a jest test failed on it). The wizard passes a `customValidator`. The shared zone itself
   was not changed.
4. **Phone width.** `WizardShell` draws a fixed 200 px step list beside the content, unusable at 375 px, and the modal preset is 62vw wide. The SPA page mounts the wizard
   `embedded`, and the wizard hides the step list under 600 px with a media-query rule on its own wrapper (each step shows "Step n of N"). `WizardShell` /
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

- **Firm list unknown.** If `/me` carries no firm list (an older BFF, eBilling off, a failed membership or name read) or the read fails, the wizard shows no firm step; a
  several-firm caller then still gets 044's 400 `firm_required` message on submit. Unknown is never treated as "no firm" on the server (`Firms` is `null`, not `[]`).
- **View-only records.** The picker lists every readable project / matter / work assignment; a record where the caller lacks Create answers 403 `record_forbidden` at
  submit (shown by code). The server stays the authority (ADR-002).
- **Reason codes** are shown verbatim ("Reason: duplicate_invoice") until 049 / 134 define user-facing text and the report slot.
- **Lost 202.** 044 does not recognise a repeated upload (INV.2): after a network failure with no response the wizard says the submission could not be confirmed and to check
  the invoices list before sending again.
- The record read asks for 200 rows per matter / work-assignment source and flags a longer list as partial.

## 6. Evidence

Original build (before the firm step): shared jest 29 passed; SPA `InvoiceSubmitPage.test.tsx` 18 passed, whole SPA suite 37 files / 1037 passed. After the firm step: section 7.

- BFF arch guards that read the SPA: `ExternalSpaGridViewSelectorGuardTests` 231 passed, `ClientUploadRouteAgreementTests` 6 passed. The guard first FAILED on the widget
  (`useInRouterContext` imported outside App.tsx: any router name is refused); the widget now reads `UNSAFE_NavigationContext` (null outside a router) instead, so the
  unchanged `DetailDocumentsDelete.test.tsx`, which renders the grid body with no router, still passes.
- Seeding proof: with the probe made to answer "disabled" for every failure, the outage-503 page test and the outage-503 / 403 widget test fail (2 failed / 16 skipped
  run); restored, all 18 pass. The `.docx` client-side check failed before the custom validator was added.
- Typecheck (`tsc --noEmit`) and lint (`eslint src tests`) clean in external-spa; `eslint` clean and `tsc` free of new errors in the shared library (its existing errors are
  the unbuilt `@spaarke/auth` / `@spaarke/sdap-client` sibling packages, also behind 6 jest suites that fail to load there on this machine); `vite build` succeeds.
- **ui-tests deferred to task 053 (the WP2 dev deploy).** The browser ui-tests (happy path against the real BFF, phone width at 375 px, dark mode walk-through, now
  including the Choose firm step for a several-firm contact) need the SPA and the BFF deployed, which is what 053 does; they were not run here. What stands in for them
  until then: tokens-only file scans, the emitted `@media (max-width: 600px)` rule asserted in jest, and wire-level vitest tests.

## 7. Follow-up: the "Choose firm" step (owner, 2026-10-17: add a firm step)

**What changed**

- **BFF, `GET /api/v1/external/me`** gains `firms: [{ firmId, name }]` (additive; `contactId`, `email`, `projects`, `systemUserId` unchanged in name, type and value).
  The set is the caller contact's CONFERRING organization memberships, read through `ExternalInvoiceSubmissionAuthorizationFilter.ReadCallerFirmsAsync`: the one definition
  044's firm resolution now also calls (a pure extraction, same behaviour), so the SPA is offered exactly the set `firmId` is validated against. Names come from
  `sprk_organization.sprk_name`; an inactive or missing firm is left out (a submission for it is refused anyway). `firms` is `[]` for a caller with no firm, and `null`
  (unknown, never "no firm") when eBilling is off (nothing is read), when the membership read or a name read faults, or when the contact is unreadable. `/me` never fails
  for it. Only the caller's own ids are ever looked up, so no other firm's id or name can leave.
- **Why `/me` and no new route** (CLAUDE.md §11): (1) Existing: `/me` already returns the caller's own access context and is caller-scoped. (2) Extension: it can carry one
  more caller-scoped list; a new route would add a ledger entry and a second round trip for no reason. (3) Cost of nothing: a several-firm caller gets 400 `firm_required` and
  cannot submit. Same route, same ledger entry (`GET /api/v1/external/me`, `ExpectedEndpointFileCount` stays 136). The route is mapped to a new
  `ExternalUserContextEndpoint.HandleAsync`, which calls the unchanged `Handle` and adds `Firms`; the ledger's cited lines (:45, :74) did not move.
- **Submission**: 044 reads `firmId` from the QUERY (`?firmId=`), accepts one of the caller's firms (403 `firm_forbidden` otherwise, 400 `firm_invalid` if malformed), and with
  none named uses a single firm or answers 400 `firm_required` for several. The SPA sends `?firmId={chosen}` only when the user chose one; a single-firm caller sends nothing
  (as before). 044's contract is unchanged.
- **Wizard**: new optional `loadFirms` callback and `firmId` on the submit input. The "Choose firm" step is the first step and exists only when `loadFirms` resolves with more
  than one firm; a firm must be chosen to advance (and the wizard refuses to submit without one). With no callback, no firm, one firm, or a failed load, there is no step and no
  `firmId`. The shell is not mounted until the firm list is known (the shell fixes its steps at mount). Step captions are now "Step n of N" (3 or 4); Details shows the chosen
  firm.
- **SPA**: `ExternalUserContextResponse.firms` type, `listInvoiceFirms()` (reads `/me`), `submitInvoice` appends `?firmId=` (GUID-checked), the page passes `loadFirms`.

**Files touched in this follow-up, by owner**

| Owner | Files |
|---|---|
| uac-r2 (task 136) / external-access-r3 (task 054) / teams-app-r1 (task 025): `/me` | `src/server/api/Sprk.Bff.Api/Api/ExternalAccess/ExternalUserContextEndpoint.cs` (new `HandleAsync` appended after `Handle`), `.../Dtos/ExternalUserContextResponse.cs` (optional trailing `Firms`, new `ExternalFirmEntry`), `.../ExternalAccessEndpoints.cs` (one token: the `/me` route maps `HandleAsync`) |
| ebilling-r1 (044) | `.../ExternalInvoiceSubmissionAuthorizationFilter.cs` (extracted `ReadCallerFirmsAsync`; behaviour unchanged), `.../Services/Finance/Submission/InvoiceSubmissionService.cs` (`ReadActiveFirmNamesAsync`), `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalUserContextFirmsTests.cs` (new) |
| ebilling-r1 (051) | shared `.../InvoiceSubmission/` (`InvoiceSubmissionWizard.tsx`, `types.ts`, `index.ts`, wizard jest), `src/client/external-spa/src/pages/InvoiceSubmitPage.tsx`, `src/client/external-spa/tests/InvoiceSubmitPage.test.tsx` |
| SPA-r3 (shared) | `src/client/external-spa/src/api/web-api-client.ts` (`listInvoiceFirms`, `?firmId=` in `submitInvoice`, import of `getExternalUserContext`), `src/client/external-spa/src/auth/bff-client.ts` (optional `firms` on `ExternalUserContextResponse`) |

**Evidence**

- BFF (`-m:1`, affected classes only): `ExternalUserContextFirmsTests` 16 + `CallerPrincipalTests` (the existing `/me` tests through the unchanged `Handle`) = 39 passed. Before the
  change the new tests did not compile (no `HandleAsync`). ArchTests: `RouteAuthorizationGuard*` and `ADR010` 92 passed; `ADR001`, `ADR008` and Finance guards 15 passed. The whole unit project was not run (brief).
  The tests cover: several, one and zero firms; another firm's id and name never read or serialized; inactive firm omitted; eBilling off (null, no read); unreadable and thrown
  membership reads and a faulted name read (null); unreadable contact; a workforce caller with no contact; existing fields' values and camelCase wire names.
- Shared jest `InvoiceSubmission`: 38 passed (9 new). Against the previous wizard, 5 of the new "Choose firm" tests fail and the 4 "no step" cases pass, as they should.
- SPA vitest: `InvoiceSubmitPage.test.tsx` 25 passed (7 new: several firms send `?firmId=`, Next blocked until a pick, one / none / null / failed `/me` send none, a refused firm is
  shown); whole suite 37 files, 1043 of 1044 passed on the full run; the one failure (`DocumentUploadPage.externalUpload` 401 case, unrelated to this change) passed on its own (25/25), a
  load-sensitive timing flake. `tsc --noEmit` and `eslint src tests` clean in external-spa; `eslint` clean and no `tsc` error in `InvoiceSubmission` in the shared library.
