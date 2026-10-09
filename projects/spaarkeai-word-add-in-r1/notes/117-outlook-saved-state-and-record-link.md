# Task 117 — Outlook "already saved" on reopen (O6) + Copy Link = Spaarke record link (O3/W1)

UAT round 12 (2026-10-08), record: `notes/042-uat-round12-2026-10-08.md` items O3, O6, W1.

| Item | Outcome |
|---|---|
| O3 / W1 — Copy Link copies the Spaarke record link | **Done** (client only). |
| O6 — Outlook shows "Saved to Spaarke" on reopen | **Stopped — the existing lookup does not return what the box needs.** No client code written for it; no BFF code added (per brief). Needs a BFF change + owner go. |

## 1. O3 / W1 — what changed

`shared/taskpane/components/SaveFlow.tsx`:

- New `documentRecordLinkOf(documentId)` → `buildOpenRecordUrl(ORG_URL, 'sprk_document', cleanGuid(id), configuredSpaarkeAppName())`, or `null` when ORG_URL is unset. The same builder View Document uses.
- Both box variants use it: `savedBarOf` (after a save this session) and `resolvedBarOf` (a document already in Spaarke, task 111). Before, the post-save box copied `useSaveFlow.savedDocumentUrl` — the job result's SPE `webUrl`, which for a saved .eml is the placeholder `https://aka.ms/spe-openfilelocation` (O3). The resolved box preferred that URL too when one existed.
- No ORG_URL → Copy Link is **not rendered** (both variants; it used to render disabled after a save). Never a fallback to the file URL. Copy Link is gated on ORG_URL only, not on `canOpenRecord` — copying needs no browser tab.
- `SavedBarModel.hideCopyWithoutUrl` removed (always true now). The comment citing task 088 ("copies what it always copied") is rewritten to cite the owner's 2026-10-05 decision (task 098) that links open the Spaarke record.
- Task 116's `copyText` fallback and the copy-by-hand field are unchanged.
- `useSaveFlow.savedDocumentUrl` is still set by the hook but no longer read by `SaveFlow` (left in place; its own hook test asserts it).

Host-agnostic: the box is shared by Word and Outlook, so one change covers both.

## 2. O6 — why it stopped

### What the lookup returns

`GET /api/office/communications/by-message-id/{internetMessageId}` (`Api/Office/CommunicationsEndpoints.cs` `FindByMessageIdAsync`, client `services/communicationLookupService.ts`):

- 200 `{ communicationId, subject }` — `CommunicationLookupResponse`. The query selects only `sprk_communicationid, sprk_subject, sprk_internetmessageid` from `sprk_communication` (delegated, user-OBO).
- 404 when no row exists or the caller can't read it (deliberately the same answer).

### What the green box needs, and what's missing

| Box needs | In the response? | Where it would come from |
|---|---|---|
| The saved .eml's `sprk_document` id (View Document, Copy Link, the box's `documentId`, the "Filed to" read) | **Missing** | `sprk_document` with `sprk_isemailarchive = true` and EITHER `sprk_emailmessageid = {internetMessageId}` (stamped by `OfficeDocumentPersistence` L324-326 on every Office email save) OR `_sprk_relatedcommunication_value = {communicationId}` (the link `LinkDocumentToCanonicalCommunicationAsync` writes, L337; same filter as `CommunicationService.FindExistingArchiveDocumentAsync` L439-459). Read delegated (`IDataverseUserClient`), like the lookup itself. |
| The record it is filed to | **Missing** | Once the document id is known, NO new field is needed: the client reads the .eml document's lookups with the existing `GET /api/v1/documents/{id}` — the read task 111 already uses for a stamp-only identity. (The alternative is the communication's typed `sprk_regarding*` lookup + name, but that is the communication's filing, not the document's.) |

So the one missing field is **the .eml `sprk_document` id**.

### Things the owner should decide before the BFF change

1. **Key the lookup on the document, not the communication.** The communication capture is best-effort. `EmailUploadCaptureService.CaptureAsync` SKIPS it when the records can't be determined, when the owner is refused, or when the filing can't be built — and the save still completes as an archive. Those emails have a .eml `sprk_document` but no `sprk_communication`. Today's lookup answers 404 for them, so the pane would call a saved email "not saved". A document query on `sprk_emailmessageid` + `sprk_isemailarchive` covers every Office-saved email.
2. **More than one .eml per message.** The same email can be saved to two different records, giving two .eml documents. That holds for both query keys, because each archive links to the one canonical communication. The box shows one document, so the BFF must pick one (e.g. the newest the caller can read) or return a list the client chooses from.
3. **Where to put it.** Two options:
   - Extend `CommunicationLookupResponse` with an optional `documentId`. Additive, and the existing callers ignore it.
   - Add a sibling route such as `GET /api/office/documents/by-message-id/{id}`. It returns 404 when there is no .eml.

   The first is smaller. The second follows recommendation 1 (it does not depend on the capture). Either is a BFF change: it needs a contract test, the publish-size check and placement justification (§10).

### What the client side would be once the field exists

This was not built; recorded here for the follow-up.
- `App.tsx` would look the email up on open. The trigger is a capability: the adapter exposes an internet message id and cannot get document content. Never `hostType` (NFR-10).
- It would set `documentIdentity` to `{ kind: 'resolved', documentId, relatedRecordKnown: false }`.
- From there, task 111's existing path does the rest: the GET decides filed or unfiled, then shows the record card or the picker + File to record.
- A 404 or a lookup failure leaves `documentIdentity` untouched, so the form is unchanged and there is no false "saved".

## 3. Tests

- New: `shared/taskpane/components/__tests__/SaveFlow.copyRecordLink.test.tsx` (4 tests):
  - Outlook (.eml whose webUrl is the aka.ms placeholder) and Word each copy the `sprk_document` record link, not the file URL.
  - Without ORG_URL, Copy Link is not rendered in either host.
- Updated for the owner's change:
  - `SaveFlow.buttonFeedback.test.tsx`: the copied and copy-by-hand value is the record link.
  - `SaveFlow.savedState.test.tsx`, AC4: no ORG_URL → no Copy Link; `canOpenRecord` false → Copy Link stays.
  - `SaveFlow.alreadySaved.test.tsx`: test names only.

## 4. Gates (src/client/office-addins, Node 22 local — advisory vs CI Node 20)

- `npx jest`: 98 suites / 1309 tests passed.
- `npm run lint`: 0.
- `npx tsc --noEmit -p .`: 68 `error TS`, all in test files (0 production). None are in the files this task touched.
- Prettier: clean on every touched file.

## 5. Live check (after deploy)

- Outlook (guest and owner): save an email, then click Copy Link. Paste: it should be `{ORG_URL}/main.aspx?appname=…&etn=sprk_document&id={id}&pagetype=entityrecord&navbar=off`, not `aka.ms/spe-openfilelocation`. The link should open the .eml document record.
- Word: same check after a save, and on a document that was already in Spaarke.
- O6 is unchanged until the BFF field lands.
