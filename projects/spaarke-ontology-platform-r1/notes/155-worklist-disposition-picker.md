# Task 155 - Worklist disposition picker and route fields: notes

Branch `task/ontology-155-worklist-disposition-picker` (PR #1990), worktree `C:\wts-155`. Rigor FULL, Sonnet 5.5 @ high. Code:
`src/client/shared/Spaarke.UI.Components/src/components/Worklist/`. Contracts: task 154's `GET /api/v1/inquiries/for-todo/{todoId}` and route
fields, task 071's `POST /api/v1/inquiries/{serviceRequestId}/disposition` (200 body `{ serviceRequestId, todoCompleted }`).

## Additions beyond the brief (acceptance criterion 6: one line each)
- **"Open the decision wizard instead" button** (in the picker). It is the only way to reschedule or dismiss that To Do from its row, because
  the plan already drops mark-complete from the wizard for an inquiry To Do; it is not a bypass of the picker (the server still refuses
  mark-complete while the inquiry is open).
- **The 400 message** ("That outcome or reply isn't valid..."). Task 071's route returns 400 `inquiry.invalid` for a bad disposition or reply;
  without a case it would fall to the generic try-again, which hides that the person's choice is the problem.
- **The no-replies state** ("No reply you can see has been filed..."). The route can return an inquiry with no readable reply; the reply is
  required by the POST, so the picker says why Record is off rather than leaving it dead. The wording says "you can see" because the list
  holds only replies the caller can read, not only replies that exist.
- **The lookup-failed notice.** A failed lookup (anything but 404) must open nothing, because the To Do might be an inquiry's and the wizard
  cannot complete one; the notice tells the person why nothing opened and that trying again is safe.
- **The recorded-outcome notice and the session no-reopen set.** The server completes the To Do but leaves its Signal open until the nightly
  job (#1991, DI-101), so the row stays after a 200; without the notice the click looks like it did nothing, and without the set a second click
  would open a picker or wizard for finished work. The set is per widget instance and in memory only; it holds the notice each To Do was told
  (`todoCompleted` true or false). Both go when #1991 lands.
- **"Close and refresh" on `inquiry.already_resolved`.** A 409 for an inquiry that already has an outcome can never succeed on retry; Record
  is disabled and the button closes the picker and reloads the lanes.
- **Open sequencing and unmount guards in `WorklistWidget` / the picker.** A lookup answering after a newer open (or after unmount) is dropped, so
  it cannot put a picker on top of the wizard or set state on a gone component.

## Known limits (filed, not fixed here)
- **#1992 (DI-102)** - paging inside the wizard, and Start: the picker opens only from a click on the inquiry To Do's own row. A person who
  opens another Do row and pages to that To Do inside the wizard gets no picker (the plan omits Mark complete, so nothing says to record the
  outcome), and Start on a lane whose first row is the inquiry To Do opens only the picker and loses the walk. It fails safe: the server
  refuses Mark complete while the inquiry is open (409 `inquiry.disposition_required`).
- **#1991 (DI-101)** - the Signal stays open until the nightly run: the server completes the To Do but does not close its Signal in the same call,
  so the row stays in the lane after a recorded outcome. The widget's notice and session set are the client stopgap; the issue removes them.

## Test-to-criterion map
All in `__tests__/` of the Worklist folder.

| Criterion | Tests |
|---|---|
| 1. Picker lists four dispositions and replies (newest preselected); confirm posts `{replyCommunicationId, disposition}` and reloads the lanes | `InquiryDispositionPicker.test.tsx`: "the picker (D-111)" (lists, confirm-posts, each of the four option values, cancel, open-wizard-instead, dark theme); "end to end over the route client"; `inquiryRoutes.test.ts`: the record-disposition POST shape and `todoCompleted` reading |
| 2. 403 / 404 / 409 (each reason code) say why and the item stays open | `InquiryDispositionPicker.test.tsx`: "a refused disposition keeps the To Do open..." (each code, retry after refusal, generic failure), "an inquiry that already has an outcome cannot be recorded again", the 409 end-to-end case; `inquiryRoutes.test.ts`: "the four dispositions and the plain-words failures" |
| 3. Any other To Do or a non-To Do subject: no picker, no lookup | `InquiryDispositionPicker.test.tsx`: "no picker, and no lookup call, for any other row" (other To Do, non-To Do subject, Decide row, no subject reference, closed copy) |
| 4. A row with a sentence shows it in the wizard; null keeps the Missing statement | `inquiryRoutes.test.ts`: "passes the rule sentence through (D-23)" and the null cases; `WorklistWidget.test.tsx` (sentence null is passed as null); `worklistRoute.test.ts` (no sentence invented) |
| 5. No Dataverse or Xrm call from the widget | `InquiryDispositionPicker.test.tsx`: "the widget reads no Dataverse (D-15)..." (source scans: no Xrm / WebApi / storage, the To Do table named in one place only, no colour literal, only the two BFF routes) |
| 6. Tests cover the criteria; additions justified | This note; the additions above map to: lookup-failed notice ("a lookup that could not be answered"), in-flight guard and open race ("while a lookup is in flight"), recorded-outcome notices and no-reopen ("a recorded outcome says so..."), already-resolved ("an inquiry that already has an outcome..."), unmount ("nothing is set after the widget has gone"), no-replies ("an inquiry with no reply filed yet...") |
