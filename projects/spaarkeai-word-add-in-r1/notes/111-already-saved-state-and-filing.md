# Task 111 — already-saved state + "File to record" (UAT round 11 items 3-6)

## Decision: Option B (coordinator, 2026-10-07)

`GET /api/v1/documents/{id}` cannot decide filed/unfiled: `GetDocumentAsync` selects only `sprk_matter` / `sprk_project` / `sprk_invoice`
(returns `matterId` / `projectId` / `invoiceId` GUIDs; the `*Name` properties are never populated) — no `sprk_workassignment`, no
`sprk_related*`. A document filed only to a work assignment would read as unfiled and be given a second parent by `PUT` (which sets a lookup and
never clears another). So the pane trusts ONLY the URL resolution (`POST /api/documents/resolve-identity`, which reads all four direct slots).
A stamp-only identity is modelled as **record unknown** and is never offered filing.

## How the pane decides

| Signal | Source |
|---|---|
| filed | `identity.kind === 'resolved'`, `relatedRecord !== null` |
| known unfiled | `resolved`, `relatedRecord === null`, `relatedRecordKnown !== false` (URL resolution) |
| unknown | `resolved`, `relatedRecord === null`, `relatedRecordKnown === false` (set by `applyStampPrecedence` for a stamp-only match) |

`useRelatedRecord` outcomes: `associated` / `unassociated` / `unknown` / `absent`. A later server-side resolution of the stamp only needs to
return a record (or `null` without the flag) — no UI change.

## States

| State | Green box | Filed-to area | Footer |
|---|---|---|---|
| Fresh doc (`new`, or no identity) | none | plain create form + picker (unchanged) | Cancel / Open Document (if resolved id) / Save |
| Resolved, filed | "Saved to Spaarke" + "Filed to {record}" · View Document · Copy Link | "Filed to" card | Cancel / Save (no Open Document) |
| Resolved, known unfiled | "Saved to Spaarke" + "Not filed to a record yet." | picker (Matter/Project/Invoice, search, + New) + **File to record** | Cancel / Save |
| Unknown (stamp-only) | "Saved to Spaarke" + "Filing record not available here." | card with the same neutral line; no picker | Cancel / Save |
| After pane save | unchanged (task 088/099) | unchanged | unchanged |
| After "File to record" succeeds | updates in place to "Filed to {record}" | card shows record; picker gone | — |
| "Save as new document" chosen | box hidden (composing a different document) | create form | — |

The box shows only in the default version mode with no save this session. Copy Link = session saved URL, else the sprk_document record link
(`buildOpenRecordUrl`); hidden when neither exists.

## Filing request

`PUT {apiBaseUrl}/api/v1/documents/{documentId}` — body has exactly ONE lookup: `matterLookup` | `projectLookup` | `invoiceLookup`
(only the types `RelatedToPicker` offers; `workAssignmentLookup` is never sent — the picker has no such type). Authorized server-side: write on
the document + AppendTo on the new parent. 403 → "You don't have permission…"; other errors → the server's ProblemDetails message; picker stays.
Success → `onDocumentFiled` → `App` updates `documentIdentity` (`applyFiledRecord`) and `savedContext` (Create To Do regarding).

## Files

`services/documentFilingService.ts` (new) · `services/documentIdentityService.ts` (`relatedRecordKnown`, `applyFiledRecord`) ·
`hooks/useRelatedRecord.ts` (`unknown`) · `components/RelatedRecordCard.tsx` · `components/SaveFlow.tsx` · `components/views/SaveView.tsx` · `App.tsx`.

## Live checks left

- A real quick-saved (stamp-only) document: box + neutral line, no picker.
- A real URL-resolved unfiled document: file to a Matter, then a Project; confirm the record appears and the doc shows under it in Spaarke.
- 403 against a secure record the user cannot AppendTo.
- Follow-up (needs BFF): return all four slots + names on `GET /api/v1/documents/{id}` (or resolve a stamp server-side) so stamp-only documents can be filed.

## Addendum (main session, 2026-10-07) — stamp-only documents now resolved by id (task 112)

The owner approved the server addition. `documentIdentityService.completeStampIdentity` calls `GET /api/documents/{id}/identity` (task 112, same read authorization as resolve-identity) for a stamp-only identity, and `App.tsx` settles its result. A readable stamped document therefore becomes an ordinary known identity: filed → card + "Filed to"; unfiled → picker + "File to record". Any failure (403 — also the unknown-id answer — 404, 503, network) or an answer for a different id keeps the stamp-only outcome, i.e. the neutral "Filing record not available here." with no picker. Tests: `documentIdentityService.stampById.test.ts` (8).
