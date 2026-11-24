# iss052 — the SPA "Invoice received" screen links to the invoice and its record (ISS-052, #3082)

Task: `tasks/iss052-spa-received-links.poml`. Rigor FULL. Branch `task/ebilling-iss052` (base `8b33de45ae`).

## Before → after

| | Before | After |
|---|---|---|
| SPA wizard result screen and `?submissionId=` resume view, Accepted submission, invoice visible to the firm | Status, tracking number, report; no way to open the invoice or the record | Also "Open invoice" and "Open {matter\|project\|work assignment}", each opening that record in the shell's record view (`useOpenRecord`) |
| Same, Rejected / Failed / Received / Processing | No links | "Open {record}" only (the record is known whatever the status); never "Open invoice" |
| Record modal's Submit invoice result | No links | "Open {record}" (this record) closes the dialog back to the record behind it; "Open invoice" closes the dialog and opens the invoice as a child record over the record, as an Invoices-section row does |
| `GET /api/v1/external/invoice-submissions/{id}` | `invoiceId` never in the JSON | `invoiceId` present only for an Accepted submission with an invoice, on this route only. Every other answer is byte-for-byte as before |
| Internal `GET /api/finance/invoice-submissions/{id}`, the external list, the internal `CreateInvoiceWizard` | — | Unchanged (pinned by tests) |

## What changed

- BFF (additive, no new endpoint, service, DI registration, package or check):
  - `Services/Finance/Submission/InvoiceSubmissionContracts.cs`: `InvoiceSubmissionView.OpenableInvoiceId`, serialized as `invoiceId`, `WhenWritingNull`. `InvoiceId` stays `[JsonIgnore]` (server-side only). The new property is set in ONE place, so every other serializer of the view (internal route, list, assistant reads) still emits nothing: fail closed by default rather than by clearing.
  - `Api/ExternalAccess/ExternalInvoiceSubmissionEndpoints.cs`: `GetAsync` answers `ExternalReadView(shown, decided)` = `ExternalView(shown)` plus `OpenableInvoiceOf(decided)`. `OpenableInvoiceOf` is `Status == "Accepted"` and a non-empty `InvoiceId`. `DocumentId`, `ChannelValue` and `ResubmissionOfId` are not exposed.
- Shared library (`components/InvoiceSubmission/`):
  - `types.ts`: `InvoiceSubmissionStatusView.invoiceId?` and a typed `coreRecord?` (`InvoiceSubmissionStatusCoreRecord`, `kind` as the BFF's enum number 0/1/2 or a name); `InvoiceSubmissionRecordRef`; `invoiceSubmissionRecordOf` and `invoiceSubmissionInvoiceIdOf` (GUID-checked; the wire is untrusted).
  - `InvoiceSubmissionOutcome.tsx`: optional `onOpenInvoice(invoiceId)` and `onOpenRecord(record)`. Each button shows only when its callback is passed and the server named the target. Without callbacks nothing renders (the internal wizard passes none). No id is rendered as text.
  - `InvoiceSubmissionWizard.tsx`: passes both through to its result screen.
- SPA:
  - `components/ebilling/submissionLinks.ts` (new, type-only imports): the kind → entity map and the record-view targets.
  - `pages/InvoiceSubmitPage.tsx`: both callbacks on the wizard and the resume view, through `useOpenRecord` (precedent `intake/intakeSuccess.tsx`).
  - `components/record/actions/SubmitInvoiceAction.tsx`: the in-modal callbacks (see Decisions).

## Why exposing the id is safe (tenant-isolation reasoning)

- **No new decision.** `GetAsync` runs only after `ExternalInvoiceSubmissionAuthorizationFilter.DecideReadOneAsync` put the submission on the scope. It does that only when `VisibleSubmissionsAsync` kept it. For a submission with an invoice, that function puts EXACTLY that invoice id to the caller-aware invoice seam (`ExternalInvoiceHiding`: the D-70 / 130-0 firm-match ruling plus the (a') predicate) and withholds the submission when the invoice is hidden. So a hidden invoice is the uniform 404 and a faulted decision is the retryable 503 before the handler runs; neither can carry an id. The handler reuses that outcome and adds no check.
- **Status gate.** A Rejected submission also has an invoice (D-101). Only Accepted carries the id, per the POML.
- **Opening it still goes through the record read and its checks.** The SPA's record view reads `sprk_invoice` through the Tier-2 module read (`POST /api/v1/external/api/dataverse/fetch`), which applies the same (a') predicate per row (task 098 surface (a)). The single-record route on the invoices module still answers 403 for every invoice id. A link can only ASK to open; a record the caller cannot see shows the modal's "You don't have access" state.
- **Nothing new is disclosed.** The same caller already sees this invoice's `sprk_invoiceid` in the record's Invoices section and the Invoices grid, through that module read.

## Decisions (POML asks to say which)

- **Inside the record modal, "Open {record}" for the record the modal shows closes the Submit invoice dialog back to it** (not hidden). The record is the one behind the dialog, so closing is opening it, and nothing is stacked. A different record (unreachable: the wizard is locked to this record and the server files against the route's record) would open as a child record.
- **Inside the record modal, "Open invoice" uses the modal's child-record opener (`useChildRecordOpener`), not `useOpenRecord`.** The POML says both "wire to useOpenRecord" and "the invoice opens the same way the record's Invoices section opens one"; the Invoices section opens an invoice as a child record over the record (R4 task 013), so the second rule decides. `useOpenRecord` would REPLACE the record shown (when the shell's opener owns the modal) or add a second top-level dialog over a grid's own record modal. The dialog closes first (a close after a submission re-reads the Invoices section), and the child names section `invoices`, so its close re-reads the section and `refocusChildRecordRow` puts focus on the new invoice's row. Inside a child record there is no child opener and no "Open invoice" is offered.
- **The page (wizard and resume view) uses `useOpenRecord`**; the page stays mounted under the record view.
- **The list route does not carry the id**: the SPA needs only the single read (the resume view and the wizard's poll).
- **Internal `CreateInvoiceWizard`: not wired.** Worth a small follow-up only if the owner wants it: the internal route would need its own `invoiceId` (a separate contract decision; the internal caller's Read on the invoice is a different check) and an Xrm navigation callback in `CreateInvoiceWizard`. Not built.

## Tests (failing before, passing after)

- BFF `InvoiceSubmissionEndpointsContractTests` (`tests/integration/contract/Api/Finance/`, compiled into `Sprk.Bff.Api.Tests`), 8 new:
  - Accepted + visible: `invoiceId` = the invoice, `coreRecord` present, one invoice decision (must fire);
  - Received, Processing, Rejected, Failed (visible invoice): no `invoiceId`, `coreRecord` still present;
  - Accepted + another firm's invoice: uniform 404, no id;
  - Accepted + decision read fault: retryable 503, no id;
  - internal read and external list of the same Accepted submission: no `invoiceId`.
  - Before (BFF source reverted): 1 failed (the must-fire), 7 passed (the negatives pin the old behaviour). After: class 286 / 286 passed.
- Shared `InvoiceSubmissionOutcome.links.test.tsx`, 16 new (actions shown / not shown per status, id, kind and callback; the wizard passes them; no callbacks = unchanged). Before (shared source reverted): 13 failed, 3 passed. After: `src/components/InvoiceSubmission` 170 / 170 (11 suites).
- SPA (vitest): `InvoiceSubmitPage.test.tsx` 4 new (wizard result and resume view call `openRecord` with `sprk_invoice` / `sprk_matter` / `sprk_project` and the ids; Rejected shows no "Open invoice"; no id, no record = no links), `RecordTemplateSubmitInvoice.test.tsx` 3 new ("Open matter" closes back with one dialog and no new matter read; "Open invoice" opens a child record that reads `sprk_invoice` with the id; Rejected shows no "Open invoice"). Before (SPA sources reverted): 6 failed, 1 passed. After: 70 / 70 and 50 / 50.
- ArchTests by filter: `ExternalInvoiceVisibilityGuardTests`, `SubmissionSourceEmailLinkNotFilingGuardTests`: 21 / 21. Route metadata unchanged, so `RouteAuthorizationGuardTests` was not run.

## Builds

- `dotnet build src/server/api/Sprk.Bff.Api/ --no-incremental`: 0 warnings, 0 errors. No package change, so no publish-size delta measured (POML).
- Shared library `npm run build` (tsc): clean, after building its `file:` siblings `Spaarke.Auth` and `Spaarke.SdapClient` in this fresh worktree (without them, 13 pre-existing TS2307 / TS18046 errors in untouched files).
- SPA `npm run build` (vite): succeeds; `npm run typecheck`: 0 errors. eslint and prettier clean on the touched files.

## Placement (root §10 / §11)

- No new endpoint, service, DI registration, package or background work. New surface: one JSON member (`invoiceId`) on an existing route's response, and two `internal static` helpers in the existing endpoint class.
- Existing: `InvoiceSubmissionView.InvoiceId` (read, `[JsonIgnore]`). Extension: the view is extended with a property set only by this route, rather than un-ignoring `InvoiceId`, which would put the id on the internal route and every other serializer of the view unless each cleared it.
- Cost of doing nothing: the firm cannot reach the invoice or its record from the result screen (owner request, checklist Q).

## Known limits

- K4: the BFF writes `coreRecord.kind` as the enum's number (no global string-enum converter). The client reads a number or a name; the contract is unchanged.
- K2: a submission with an invoice whose status is not Accepted never offers "Open invoice", even if the firm could see the invoice elsewhere (the POML's rule).
- K4: in the record modal, "Open {record}" for a record other than the shown one opens a child record; unreachable while the wizard is locked to the record.
- Observation, not changed: the shared Rejected bar shows `Reason: {outcomeReasonCode}` as text (task 051, pre-existing, asserted by existing SPA and shared tests; EB-D-210 shows reason codes to firms). This task adds no code or id as text.

## Copies

spa-r4 wants a copy of anything that changes the SPA invoice or record-modal UI; the main session sends it (this task sends nothing).
