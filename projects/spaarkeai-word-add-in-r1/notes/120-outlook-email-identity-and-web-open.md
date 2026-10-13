# Task 120 — Outlook knows an email is already saved (O6) + "Open in Spaarke" on the web

UAT round 12 (2026-10-08), record: `notes/042-uat-round12-2026-10-08.md` item O6, and task 119's residual risk (`notes/119-…md` Part A). The owner approved the server addition and a dev BFF deploy on 2026-10-08. The main session deploys.

## 0. A finding that shaped the route: what `sprk_emailmessageid` actually holds

`OfficeDocumentPersistence` (L324-326) stamps `sprk_emailmessageid` with whatever the save request carried as `email.internetMessageId`. The two save paths send different values:

| Save path | Sends as `internetMessageId` | Source |
|---|---|---|
| Ribbon Quick Save | the RFC 5322 Message-ID | `item.internetMessageId` (`outlook/commands/index.ts`) |
| Task pane | the Exchange **item id** | `useSaveFlow` L1089 `internetMessageId: context.itemId`, where `itemId` = `getItemId()` = `item.itemId` |

Measured live, read-only, 2026-10-08: of the newest 15 `.eml` documents, every pane save stores an `AAMk…` / `AQMk…` item id, and none stores an RFC Message-ID except two seeded rows. Several rows have no value at all.

So a lookup keyed only on the RFC Message-ID would answer "not saved" for every pane-saved email, which is exactly the O6 case. The route therefore takes both keys. The body is `{ internetMessageId, exchangeItemId? }`, and the row matches when `sprk_emailmessageid` equals either key. This is one optional field more than the brief's `{ internetMessageId }`, and the measurement above is why it is there.

The item id only matches a save made from the caller's own mailbox. The RFC id matches across mailboxes.

**Defect, reported, not fixed here:** the pane's save sends the Exchange item id in a field named and documented as the RFC Message-ID (`SaveRequest.cs` `InternetMessageId`, "RFC 2822"). This has three effects:
- FR-C4 cross-path linking cannot match pane saves: `CrossPathLink.FindAndLinkArchiveDocumentsAsync` keys on the real Message-ID.
- `OfficeDocumentPersistence.GetCommunicationByInternetMessageIdAsync` (L797-804) cannot match them either.
- The `.eml` gets an `X-Exchange-Item-Id` header instead of a `Message-ID` (`OfficeEmailEnricher` L291-308).

It was not fixed here because the field is also the Graph fallback's message key (`OfficeEmailEnricher` L51, used when the add-in cannot read the body). A fix therefore changes the save contract. The long-term fix is to send both values in separate fields. The main session should file it, using `/defer` or a new task.

## 1. Server — `POST /api/documents/resolve-email-identity`

### Contract

Request (JSON body, never in the URL, because the keys identify a message in someone's mailbox):

```json
{ "internetMessageId": "<…@…>", "exchangeItemId": "AAMk…" }   // exchangeItemId optional
```

| Situation | Result |
|---|---|
| A saved `.eml` exists and the caller may read the NEWEST one | 200 `DocumentIdentityResponse`, the same shape resolve-identity returns: `resolved:true`, `documentId` (bare lowercase), `documentName`, `fileName`, `relatedRecord` from the four direct slots, `reason:null` |
| No saved `.eml` carries either key | 200 `{ resolved:false, documentId:null, …, reason:"not_saved" }`. This is the same not-resolved shape resolve-identity uses. The authorization filter never runs, because there is nothing to authorize. |
| The newest copy is not readable by the caller | 403 problem+json from `DocumentAuthorizationFilter`, with no id, name or record in the body. The handler never runs. |
| `internetMessageId` missing or blank | 400 `internet_message_id_required` |
| A key over 998 characters | 400 `internet_message_id_too_long` / `exchange_item_id_too_long`. The value is never echoed. |
| No credentials | 401 |
| Dataverse cannot answer | 503 `identity_resolution_unavailable`. It is never reported as "not saved". |

### Authorization

- `DocumentEmailIdentityFilter` runs the lookup app-only through `IGenericEntityService`, the same seam the URL path uses:
  - **Match:** `sprk_isemailarchive = true` AND (`sprk_emailmessageid` = RFC id OR = item id).
  - **Order:** `createdon` desc, then `sprk_documentid` desc as a deterministic tie-break.
  - **Result:** `TopCount = 1`, the same `LookupColumns`.
  - It writes the newest row's id into the route values as `documentId`, the key `DocumentAuthorizationFilter` reads. It uses the same `HttpContext.Items` key as resolve-identity.
- `.AddDocumentAuthorizationFilter("read")` then decides. It is the same filter and operation as resolve-identity and `/{id}/identity`. There is no new rule, and the filter is unchanged.
- **An older readable copy is not searched.** When the newest copy is not readable, the answer is 403. Trying rows until one authorizes would make the route an oracle over which copies exist. It would also cost one authorization per candidate on a single request. The pane treats 403 like "not saved" and shows the ordinary form.
- A 403 tells a caller who already holds the email in their own mailbox only that it is saved somewhere in Spaarke.
- The message id is never logged (the 503 warning omits it) and never echoed in a 400.
- The route is registered unconditionally. resolve-identity and `/{id}/identity` are unchanged.

### Code

- `Services/Documents/DocumentUrlIdentityResolution.cs`:
  - adds `ReasonNotSaved`, `MaxEmailKeyLength` (998, the same cap as `EmailMetadata.InternetMessageId`), `EmailKeys`, `ParseEmailKeys` and `ResolveByEmailMessageIdAsync`;
  - the row → `Resolution` step is the existing shared `ToResolution`.
- `Api/Filters/DocumentEmailIdentityFilter.cs` (new): the email counterpart of `DocumentUrlIdentityFilter`, with the same route-value guard.
- `Api/FileAccessEndpoints.cs`: the route mapping, plus the `ResolveEmailIdentity` handler. The handler calls `BuildIdentityResponseAsync` (task 112).
- `Models/FileOperationModels.cs`: `ResolveEmailIdentityRequest`. The `Reason` doc lists `not_saved`.
- `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`:
  - the governed-file text now says twelve routes;
  - `AddEndpointFilter<DocumentEmailIdentityFilter>` is listed as a not-an-authorization form, like `DocumentUrlIdentityFilter`.

### Placement Justification (bff-extensions.md §A) — for the PR

**In the BFF, as an extension of the existing document-identity surface.**
- It is a synchronous, sub-second request/response read behind the BFF's per-document authorization filter, with no background work. ADR-052 places nothing outside the BFF for it.
- It extends the `/api/documents` group, its two-filter resolve-then-authorize shape, `DocumentAuthorizationFilter`, `DocumentIdentityResponse`, `BuildIdentityResponseAsync` and the slot logic in `DocumentUrlIdentityResolution`.
- It adds no new service class, DI registration, package or configuration. The one new type is an endpoint filter, which ADR-008 requires for an id that exists only after a lookup.
- There is no CRUD→AI dependency. It follows Minimal API (ADR-001), endpoint-filter authorization (ADR-008) and ProblemDetails errors (ADR-019).

**Three questions (CLAUDE.md §11):**
- **Existing:**
  - resolve-identity (URL → identity) and `GET /{id}/identity` (id → identity);
  - `GET /api/office/communications/by-message-id/{id}`, which returns the communication only, with no document id. The communication capture is also best-effort, so a saved email can have no communication at all (notes/117 §2).
- **Extension:** yes. Same group, filter, DTO and builder; only the id source differs. Extending the communication lookup was rejected because it keys on a record the save may not have created.
- **Cost of doing nothing:** Outlook cannot tell that the open email is saved (O6). The user re-saves it and gets a duplicate `.eml`, and after a reopen the box, Find and To Do have no document.

## 2. Pane — Outlook knows an email is already saved

- **Capability (NFR-10):**
  - `HostCapabilities.canResolveEmailIdentity` is optional, and absent means false. It is true in `OutlookAdapter` read mode and false in Word.
  - `IHostAdapter.getEmailIdentityKeys?()` is optional and implemented by Outlook. It returns `{ internetMessageId, exchangeItemId }` exactly as Office reports them, or `null` without a Message-ID.
- **Service** (`documentIdentityService.ts`):
  - `resolveEmailIdentity(keys)` → the resolved outcome (the same `toResolvedOutcome` resolve-identity uses), or `null` for every other answer (not saved, 403, 503, 400, network, no keys). It never throws.
  - `identityOfSavedDocument(id)` → task 112's by-id read via `completeStampIdentity`. On failure the outcome keeps the id with the record unknown.
- **App:**
  - **Seeding:** `documentIdentity` starts as `'checking'` when `canResolveEmailIdentity` is true.
  - **Lookup after sign-in:** the email is looked up once. A saved copy becomes the identity and is merged into `savedContext` through `applyDocumentIdentityOutcome`, so Find, To Do and Send Email see it. Any other answer clears it to `undefined`, which is today's form. An email may legitimately be saved again, so an undetermined answer never blocks Save.
  - **Retry:** "Try again" and the return-from-Spaarke refresh re-run the email lookup in Outlook. The task 116 gate still keeps `getDocumentUrl` from being called.
  - **After a pane save in Outlook:** `onComplete` calls `identityOfSavedDocument(docId)`, so the identity becomes the saved `.eml`. `savedContext` is only updated from a fully read identity. A failed read must not overwrite the record `handleSaved` set.
  - **Shared attempt counter:** all of these share the attempt counter, so a stale answer never wins.
  - **Word:** unchanged. The email lookup is never attempted, and a save is never re-read by id.
- **SaveFlow / SaveModeSection — no version mode for an email:**
  - `resolveSaveMode(identity, choice, { versionable: canSaveNewVersion })`. When an item with no document bytes resolves, the `version` view is kept, because the item is in Spaarke, but `target` is null and nothing is sent by default. The only save is the explicit `'new'` choice. A version is never offered.
  - `isExistingMode` (`view === 'version' && choice !== 'new'`) replaces `isVersionMode` for the UI. For Word it is identical; for an email it now covers the target-less already-saved state.
  - **Email already-saved state:**
    - the green box (Filed to … / Not filed to a record yet., View Document, Copy Link);
    - the "Filed to" card, or the filing picker + File to record;
    - the read-only name with the hint "This email is saved in Spaarke. To save it again, for example to another record, save it as a new document." and the link **"Save again as a new document"** (collapsed by default, and the same `handleSaveAsNewInstead` path as Word's link);
    - no attachment picker, no "Related to" picker, and no footer (no Save, no Cancel).
  - **After "Save again":**
    - the ordinary email form (name, picker, attachments, Cancel / Save), whose Save is a CREATE;
    - the hint "Save will store this email in Spaarke again, as a separate document. The saved copy is not changed." with **"Don't save again"** (back to the box).
  - The same read-only name and link now also appear in Outlook's post-save state. Before, that state had no way to save the email again; task 099 had removed Cancel there.
  - Copy: the checking line says "this email"; the announcements say "This email is already saved in Spaarke." / "Not saving again…".

## 3. Open in a normal browser tab on the web

- `openRecordLauncher.ts`:
  - `isOpenBrowserWindowSupported()` is the same runtime requirement check the adapters make.
  - `openInBrowserTab(url)` uses `openBrowserWindow` when supported. Otherwise it uses `window.open(url, '_blank')` from the click, through `openFileUrl`'s existing fallback. That fallback detects a blocked pop-up and detaches `opener`, which the `noopener` feature would prevent: with `noopener`, `window.open` always returns `null`.
  - `openRecord`'s default opener is now `openInBrowserTab`, and it returns `{ opened:false, reason }` for a blocked pop-up. An injected opener that returns nothing still counts as opened.
- **The one gate:**
  - `canOpenSpaarkeRecords(capabilities)` = ORG_URL set AND (`canOpenBrowserWindow` OR `window.open` exists).
  - App feeds it to every `canOpenRecord` prop (To Do, Find, Email), and SaveView uses it for View Document, Open Document and the "Filed to" card.
  - The views keep their own ORG_URL check; that is harmless.
- **Collision prompt:** SaveFlow's "Open in browser" now calls `openInBrowserTab`. It used to pass `canOpenRecord` as the opener choice, which would now be true on the web, where `openBrowserWindow` does not exist.

## 4. Tests

- **Server:**
  - Unit `tests/unit/Sprk.Bff.Api.Tests/Services/Documents/DocumentEmailIdentityResolutionTests.cs` (18): input rules, no echo, the query shape (both keys OR'd, archive flag, newest first, tie-break, one row, all slots), absent → null, filed/unfiled, 503, cancellation.
  - Contract `tests/integration/contract/Api/Documents/DocumentEmailIdentityContractTests.cs` (8): 200 filed with the same shape as resolve-identity, 200 unfiled, 200 `not_saved` with no authorization call, 403 with no metadata and a single lookup, 400 ×2 with no echo, 401, 503.
  - Both are in namespaces the CI office-scope filter already selects.
- **Client** (new suites for `ci-gated-suites.txt`):
  - `shared/taskpane/__tests__/App.emailIdentity.test.tsx` (9): Outlook lookup with both keys, no identity on null or on a key failure, post-save adoption, retry, Find/To Do receive the `.eml`, Word never looks up, the open gate on the web and without ORG_URL.
  - `shared/taskpane/components/__tests__/SaveFlow.emailAlreadySaved.test.tsx` (5).
  - `shared/taskpane/services/__tests__/documentIdentityService.emailIdentity.test.ts` (14).
  - `shared/taskpane/services/__tests__/openRecordLauncher.webOpen.test.ts` (9): web → `window.open(recordUrl,'_blank')`, desktop → `openBrowserWindow(recordUrl)`, blocked pop-up, gate cases.
- **Updated:**
  - `SaveView.test.tsx`: the "true capabilities" case sets ORG_URL, which is now part of the gate.
  - `SaveFlowCollision.test.tsx`: the web case drives the opener from the runtime requirement check.

## 5. Gates (2026-10-08)

- **Build:** `dotnet build src/server/api/Sprk.Bff.Api/` → 0 warnings, 0 errors.
- **Targeted server tests:** filter `DocumentEmailIdentity | DocumentIdentityByIdContractTests | DocumentIdentityByIdResolutionTests | DocumentIdentityContractTests | DocumentUrlIdentity | DocumentAuthorizationFilter` → 126 passed, 0 failed. The 26 new tests were confirmed by name.
- **CI office-scope selection:** the exact `TEST_FILTER` from `office-addins-tests.yml` → **4830 passed, 0 failed, 22 skipped (4852)**.
- **ArchTests:** `RouteAuthorizationGuard` → 86 passed.
- **Publish size (root CLAUDE.md §10):**
  - Method: fresh detached worktrees. `C:\wt120m` = `origin/master` @ `23c359eaf`. `C:\wt120b` = the same commit plus this task's four server files. Those files are byte-identical between the branch HEAD and master, so this isolates the task.
  - Each side: `dotnet publish -c Release`, then PowerShell `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`.
  - Master **38,001,426 B (36.24 MB)** vs branch **38,004,272 B (36.24 MB)** → **+2,846 B (≈ +0.003 MB)**, including PDBs.
  - File counts 192 = 192 (4 PDBs each). No MSB3030 or errors. Both worktrees were removed.
- **CVE:** `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages on either side. No package or csproj change.
- **Client (`src/client/office-addins`, Node 22 local; CI pins Node 20, so these local runs are advisory):**
  - `npx jest` → 102 suites / 1347 tests passed;
  - `npm run lint` → 0;
  - `npx tsc --noEmit -p .` → 68 `error TS`, 0 production, and the set is identical to the pre-task baseline;
  - prettier clean on every touched file.

## 6. Live checks (after the BFF deploy and an add-in deploy)

1. Outlook (owner and guest), on an email saved earlier from the pane: close and reopen the pane.
   - Expected: the green "Saved to Spaarke" box, Filed to {record} or "Not filed to a record yet." with the picker, View Document, Copy Link, and no Save.
2. The same for an email saved by Quick Save. This matches on the RFC Message-ID.
3. **"Save again as a new document"** → pick another record → Save → a second `.eml`. Reopen and the box names the newest copy.
4. **Office on the web (guest):**
   - View Document, Find rows and To Do "Open in Spaarke" each open the record in a new browser tab.
   - If the browser blocks it, the button shows "Couldn't open".
5. **Desktop:** the same affordances still open through `openBrowserWindow`.
6. **Word:** no change. The resolved/filed box, the version Save and Find are as before.
7. **Not saved:** an email never saved shows the ordinary form after a brief "Checking whether this email is already in Spaarke…".
