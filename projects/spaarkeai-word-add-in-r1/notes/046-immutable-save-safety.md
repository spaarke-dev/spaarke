# Task 046: Email/Attachment saves never delete another document's file

> Base: `f8c20b7c2` (project branch head). Rigor FULL, opus @ high, directional.
> Reproduce-first (root CLAUDE.md §10 §F.3): every test below was run against the unchanged production code before
> the fix. Symbols are named by symbol, not by line.

## 0. Summary

| Defect | Reproduced? | Fixed? | Failing → passing test |
|---|---|---|---|
| **(a)** The immutable dedup cleanup deletes a document's own SPE file | **Yes**, plus a second shape (a hash-linked copy's file) | **Yes** | `OfficeImmutableSaveFileSafetyTests.ByteIdenticalAttachmentSave_UnderTheCanonicalsNameInItsContainer_LeavesTheCanonicalsFileIntact`, `…_UnderAHashLinkedCopysName_LeavesThatCopysFileIntact`, `DuplicateAttachmentCleanup_WhenTheFileReferenceCannotBeChecked_DeletesNothing` |
| **(b)** Two same-subject, same-date emails get one `.eml` name; the second overwrites the first | **Yes** | **Yes, 2026-09-15** (owner decision B2; §0.1). First escalated (§4). | `OfficeEmailSaveNamingTests.TwoSystemNamedEmailSaves_WithTheSameSubjectAndDate_ToTheSameContainer_ProduceTwoDistinctFiles` |

Guard that passes both before and after: `…_UnderTheCanonicalsNameInItsContainer_IsStillSuppressedAsADuplicate` (AC2).

## 0.1 Update 2026-09-15: defect (b) implemented (owner decision B2)

**Owner decisions.**
1. Only a SYSTEM-DERIVED `.eml` name gets the short unique suffix. A name the user typed in the pane's Document Name
   box is never changed. Typed-name collisions are left exactly as today until task 025's refuse-and-ask prompt.
2. Most emails arrive through the Exchange/Graph integration, and their names are system-derived, so they need the
   protection too.

**Inbound investigation (done first).** The premise behind decision 2 does not hold, in the safe direction: the
server-side paths are already collision-proof, by a different mechanism.
- **(a) Generator.** The inbound path (`IncomingCommunicationProcessor.ArchiveEmlAsync`) does NOT use
  `OfficeEmailEnricher.GenerateEmlFileName`. It uses its own `GraphMessageToEmlConverter`
  (`{subject≤50}_{yyyyMMdd_HHmmss}.eml`). The outbound archive (`CommunicationService.ArchiveToSpeAsync`) uses
  `EmlGenerationService`, with the same shape. The old Email-to-Document `.eml` builder was deleted 2026-08-14.
- **(b) Upload.** Both are the same path-keyed `UploadSmallAsync` (`Replace`), but the path is
  **`{communicationId:N}_{fileName}`**. The communication id is a fresh `sprk_communication` per message, so the
  stored name is already unique per email. The inbound document's `sprk_documentname` is `"Archived: {subject}"`.
- **(c) Collision today.** None. Two inbound emails with the same subject and date get different communication ids,
  so they get different paths. The outbound twin is pinned by
  `SpeFlatUploadPathTests.ArchiveExisting_ForTwoCommunicationsWithTheSameSubject_PersistsBothAndOverwritesNeither`.
  The inbound prefix is verified by reading the code only: its sole harness (`InboundPipelineTests`) mocks Graph
  request builders, so a pin there would be almost all plumbing. A small pinning test is recommended for email-r2.
- **Not changed.** Moving these paths onto the Office generator would change shipped, test-pinned email-r2 names
  (the cross-project STOP case) and buys no protection they lack. **Only the Office pane and ribbon path stored a bare
  `{date}_{subject}.eml`**, and that is the only path changed.

**Design.**
- **Signal.** New optional request field `EmailMetadata.IsNameSystemDerived` (JSON `isNameSystemDerived`, default
  `false`).
  - The pane sends `!context.documentName`. `documentName` starts empty and is set only by the Document Name box's
    `onChange`, so it is `true` unless the user typed a name.
  - The ribbon quick-save always sends `true`: it files under `item.subject`.
- **Old clients.** A client that does not send the flag is treated as **typed (no suffix)**. An older pane may be
  sending a typed name, which must never be changed. The cost is only that such a client keeps today's collision
  until it is redeployed.
- **One generator for this path.** `OfficeEmailEnricher.GenerateEmlNames(metadata)` returns `(DocumentName,
  StoredFileName)`.
  - `DocumentName` is the unchanged `GenerateEmlFileName` output, `{yyyy-MM-dd}_{subject}.eml`.
  - `StoredFileName` adds `_{8 hex}` before `.eml` for a system-derived name only (random per save,
    `Guid.NewGuid().ToString("N")[..8]`). Otherwise it equals `DocumentName`.
- **Suffix on the stored file only.** The SPE path and `sprk_filename` get the suffix. `sprk_documentname` stays the
  readable name, passed through a new `OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync` overload that
  takes `documentName` separately. The old overload delegates with the same value twice, so its 13 positional
  unit-test call sites are untouched. The dedup notification also uses the readable name, so its text is unchanged.
  `sprk_filename` stays equal to the SPE item's name, so the filename-keyed `AttachmentDocumentAssociationRung` keeps
  its semantics. `sprk_filename` MaxLength is 1000; the longest stored name is 104 characters.
- **Idempotency.** The Email key (`…|messageId-or-subject|…`) does not include the name; it is unchanged.

**Reproduction (at `9b58c6d9b`, no production change).** The parked §1 test, with no flag, failed:
`Expected world.SpeItems.Values.Where(i => i.Name.EndsWith(".eml", …)) to contain 2 item(s) because two different
emails are two files; the second must not overwrite the first, but found 1`. After the fix, the same scenario with
`isNameSystemDerived: true` passes. Without the flag it still produces one file, by design: an absent flag means
"typed".

**Tests.**
- **Server** (`tests/integration/data-mutation/OfficeVersionSave/OfficeEmailSaveNamingTests.cs`, 4 cases):
  - Two system-named saves produce two distinct files. Each has 1 version and its own body, and each row points at
    its own file.
  - A typed name (`false`) and an old client (the field removed from the raw JSON) are stored as exactly
    `2026-09-14_Re Filing.eml`, with the same `sprk_documentname` and `sprk_filename`.
  - A system-named save has a suffixed stored name that equals `sprk_filename`, and a readable `sprk_documentname`.
- **Client:**
  - new `shared/taskpane/hooks/__tests__/useSaveFlow.emailName.test.ts` (own subject → `true`; typed Document Name →
    subject unchanged and `false`);
  - one new case in the gated `quickSaveHelpers.test.ts` (the ribbon sends `true`).

**Consequences to know.**
- **Retry after a post-create failure.** A system-named Email save that fails after its row exists and is retried now
  gets a new name, so it creates a second archive row. Before, it replaced the first file, hit `sprk_graphitemid_uk`,
  errored, and left an orphan pointerless row (025 M7).
- **Retry after an upload with no row.** If the upload succeeded but the save threw before a row existed, a retry
  leaves the first upload as an unreferenced blob.
- **Where the suffixed name shows.** The finalization worker uses the stored name as the RAG index file name and in
  the attachment children's "Email attachment from {parent}" description and ParentFileName.
- **Probability.** Two same-date, same-subject system-named emails in one container share a suffix with probability
  2^-32.

**What stays open.**
- Typed-name collisions: task 025 closes them.
- Old clients: they are protected only after the pane and ribbon are redeployed.
- A test pinning the inbound id-prefix: recommended for email-r2.
- The new pane suite is not in `ci-gated-suites.txt` (not edited here). It is green; recommend promoting it.

**Gates (046b).**

| Gate | Result |
|---|---|
| BFF build | green, 0 warnings, 0 errors |
| Office/Email/Communication/ContentDedup/Idempotency/DuplicateDetection sweep | 1,859 total, 1,836 passed, 0 failed, 23 skipped |
| ArchTests | 191/191 |
| Full `Sprk.Bff.Api.Tests` (6 chunks) | **12,305 total, 12,249 passed, 0 failed, 56 skipped** (+4 = the new cases) |
| Add-in `npm run typecheck` | 111 errors (= baseline). 0 production: the 24 outside test files are all `shared/__mocks__/office-js.ts`. None in touched files. |
| Add-in `npm run build` (placeholder env) | exit 0 |
| Add-in jest | 10 failed / 22 passed suites; 84 failed / 438 passed tests. The 10 failing suites and 84 failing tests are exactly the pre-existing baseline. All 21 gated suites pass by path; the new suite passes. |
| CVE | no vulnerable packages |
| Format | production + new test file clean; contract file's 14 findings pre-existing (two shifted +8 lines by the world addition) |
| Publish (Compress-Archive Optimal, PDBs incl.) | **47,607,901 B**: **+135 B vs `9b58c6d9b`**; +52,016 B vs master `e0a6f87c4` (47,555,885 B) |

**Step 9.5 (046b).** No Critical findings. ADR-001/007/008/010/019/038/044 are compliant; the add-in jest tests fall
under the project's ADR-038 Path A. NFR-07 holds. The request change is additive and optional (absent = today's
behaviour). Warnings: the retry and orphan-blob consequences above (accepted, documented); old clients unprotected
until redeploy (by design); the suffixed name in finalization's descriptive fields (accepted: it is the file's name).
Suggestions: the 2^-32 bound; the added overload (chosen over churning 13 test call sites); the new suite not gated.

## 1. Reproduction evidence (production code at `f8c20b7c2`, untouched)

Run in a throwaway worktree at `f8c20b7c2`, with only the two test files copied in (`git status -- src` empty): 4 tests,
**3 failed, 1 passed**. Verbatim:

| Test | Before |
|---|---|
| (a) canonical's own item | `Expected world.DeletedItemIds {"item-f954cdf1…"} to not contain "item-f954cdf1…" because the upload landed on the canonical's own item, so that item is the canonical's file, not a transient blob.` |
| (a) hash-linked copy | `Expected world.DeletedItemIds to be empty because the upload landed on the linked copy's own item, but found {"item-c66dc2e2…"}.` |
| (a) fail-safe | `Expected world.DeletedItemIds to be empty because a leaked transient blob is recoverable; deleting a file a document points at is not, but found {"item-9528eb94…"}.` |
| AC2 guard | Passed |
| (b) | `Expected world.SpeItems.Values.Where(i => i.Name.EndsWith(".eml", …)) to contain 2 item(s) because two different emails are two files; the second must not overwrite the first, but found 1` |

**After the fix: 4/4 pass.**

The first reproduction run also failed the AC2 guard, with `ObjectDisposedException: Cannot access a closed Stream`.
That was a test bug: the response body was read twice. It was fixed, and the guard was re-proven green against the
unchanged code above, before it was relied on.

### (b) reproduction, kept for whoever implements (b)

```csharp
[Fact]
public async Task TwoEmailSaves_WithTheSameSubjectAndDate_ToTheSameContainer_ProduceTwoDistinctEmlFiles()
{
    var world = new OfficeVersionSaveWorld();
    using var factory = new OfficeVersionSaveTestWebAppFactory(world);
    var client = factory.CreateClient();
    var record = new SaveEntityReference { EntityType = "matter", EntityId = Guid.NewGuid() };
    var sent = new DateTimeOffset(2026, 9, 14, 9, 30, 0, TimeSpan.Zero);
    SaveRequest EmailSave(string messageId, string body) => new()
    {
        ContentType = SaveContentType.Email, TargetEntity = record,
        Email = new EmailMetadata { Subject = "Re: Filing", SenderEmail = "counsel@test.com", SentDate = sent,
                                    InternetMessageId = messageId, Body = body },
    };
    (await client.PostAsJsonAsync("/api/office/save", EmailSave("<first@test.com>", "First reply.")))
        .StatusCode.Should().Be(HttpStatusCode.Accepted);
    await client.PostAsJsonAsync("/api/office/save", EmailSave("<second@test.com>", "Second reply."));
    world.SpeItems.Values.Where(i => i.Name.EndsWith(".eml", StringComparison.OrdinalIgnoreCase))
        .Should().HaveCount(2);
}
```

## 2. Defect (a): the root cause, as reproduced

`OfficeService.SaveAsync`'s suppress branch deleted `(driveId, itemId)`, which is the item the upload returned. The
premise was that this item is always a transient blob (`OfficeStorageUploader.DeleteFromSpeAsync`'s own doc said
"never the canonical's own item"). The Office create upload is PATH-keyed under `ConflictBehavior.Replace`, so a
name that already exists in the container lands on that existing item. Two shapes were reproduced:
1. **The canonical's own item.** `ContentDedupDetector`'s canonical lookup does not exclude the probed item's row, so
   the canonical it returns can own the very item that was uploaded to. This is the reported M9 / R3.
2. **A hash-linked copy's item (not reported).** The detector excludes hash-linked copies and resolves the TRUE
   canonical elsewhere. The item the upload landed on belongs to the copy, and a guard comparing only against the
   canonical's `sprk_graphitemid` would still have deleted it.

A third shape is closed by the same guard and reasoned, not tested: a C4 upload (same name, different bytes) whose
new bytes happen to match some other canonical. The victim's item would have been deleted outright.

## 3. The guard (design, and why it is caller-side)

- **`OfficeDocumentPersistence.IsUploadUnreferencedAsync(itemId, canonicalDocumentId, ct)`** returns `true` ONLY when
  a successful query shows that NO `sprk_document`, in any state, has `sprk_graphitemid == itemId`. That includes the
  canonical row's own pointer, which is the POML's comparison, widened to every row because of shape 2. Inactive rows
  are included because an inactive row still points at its file. `canonicalDocumentId` is used only to word the log
  ("the canonical" vs "another document").
- **Fail-safe.** A failed lookup, or no generic seam (the bare test constructor), answers `false`, and nothing is
  deleted. A leaked transient blob is recoverable; a deleted document file is not. Every skipped cleanup logs a
  Warning with the item id.
- **`OfficeService.SaveAsync`** deletes only when the guard returns `true`. Everything else in the branch is unchanged:
  no row is created, finalization is skipped, the job completes `DeduplicatedToExisting` naming the canonical, and the
  detector has already notified the user.
- **Why a query and not the `sprk_graphitemid_uk` alternate key.** `RetrieveByAlternateKeyAsync` reports "not found"
  by throwing, which cannot be told apart from a failed read. A fail-safe guard needs the three answers kept distinct:
  empty, found, and failed. `RetrieveMultipleAsync` gives exactly those. No production query of this shape existed
  before (grep over `src/server`).
- **Why caller-side.** `ContentDedupDetector` is shared with email-r2 and Compose, and its answer, "the canonical for
  this content", is correct for all of its callers. The defect is the Office caller's delete assuming the uploaded item
  could not be a document's file. The only `DeleteFromSpeAsync` caller, and the only `ReconcileAsync` caller, are on
  this path. `ContentDedupDetector.cs` is unmodified (`git diff --name-only`). Escalation trigger (a) did not fire.
- **Unchanged by construction.** The editable (Word Document) path never reaches the suppress branch, and the version
  path never uploads by path. `IsEditableContent`, the create/version idempotency keys, link/graduate and the version
  write are all untouched.

### Placement (root CLAUDE.md §10) and component justification (§11)

Modify-only on the existing save spine. There is no new route, service, DI registration, package or Dataverse column.
The only new surface is one public method on the existing `OfficeDocumentPersistence`, which already owns this path's
`sprk_document` reads.
1. **Existing:** `GraduateLinkedCopyIfDivergedAsync` reads by the same key, but for a different purpose, and its
   throw-means-absent contract cannot be fail-safe (above).
2. **Extension:** yes. It extends the existing persistence class and the existing suppress branch.
3. **Cost of doing nothing:** a byte-identical Attachment save to another record, under the canonical's name, deletes
   the canonical's file while reporting success.

## 4. Defect (b): 🔔 escalation trigger fired

The trigger reads: *"If the `.eml` suffix would change the name of any file the USER typed … STOP and escalate."*
The owner decision (2026-09-14) rests on the premise *"the `.eml` name is system-generated … never typed by the user."*
**That premise is false for the Outlook pane**, verified by code:
- `SaveFlow.tsx` renders the free-text **Document Name** box under `{!isVersionMode && (`, with no host condition. An
  Outlook save is never a version save, so the box is shown in Outlook.
- `useSaveFlow.ts` sends `email.subject: effectiveDocumentName || 'Untitled Email'`, where
  `effectiveDocumentName = context.documentName || context.itemName`. A typed Document Name REPLACES the subject.
- `OfficeEmailEnricher.EnrichEmailFromGraphAsync` overwrites Body, IsBodyHtml and Attachments, never Subject.
  `GenerateEmlFileName` builds `{yyyy-MM-dd}_{subject}.eml` from it.
- `CreateDocumentRequest.Name = fileName` → `sprk_documentname`. The suffix would therefore also appear in the
  record's **visible name**, not only in the SPE file name.

So a suffix in `GenerateEmlFileName` changes a name the user typed. It was not implemented; the generator is unchanged.
The ribbon quick-save (`outlook/commands/index.ts` sends `item.subject`) is system-named.

Options for the owner:
- **B1:** suffix every generated `.eml` name. The owner accepts that a typed Document Name is already composed with a
  system date prefix, sanitisation and an extension, so a suffix is part of that system composition. Also decide
  whether `sprk_documentname` carries it, or whether the record keeps the unsuffixed name and only the SPE file is
  suffixed.
- **B2:** suffix only system-derived names. The client would send whether the name was typed, which touches the pane
  (`useSaveFlow.ts` / `SaveFlow.tsx`). Typed names fall to 025's `OFFICE_020` prompt.
- **B3:** no suffix. All email collisions go through 025's `Fail` + `OFFICE_020`.

## 5. What this task does NOT close

- **The overwrite half of the goal.** The goal says no save may *"delete or overwrite"* another document's file. The
  guard closes the delete. A direct-API Attachment save of **different** bytes under an existing name (C4) still
  `Replace`-overwrites that file before dedup runs. That is R1, whose fix is task 025's `ConflictBehavior.Fail`, not
  this task's. For Email, E-C4 (same subject and date) is what (b) was meant to close, and it is open pending §4.
- **Orphan blobs.** Fail-safe skips leave unreferenced SPE items (logged, no sweep exists).
- Under `Replace`, a same-name byte-identical save still adds one identical SPE version to the existing item.

## 6. Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | green, 0 warnings, 0 errors |
| Task 046 tests | 4/4 (were 3 failed + 1 passed guard before) |
| Office + ContentDedup + Idempotency + DuplicateDetection sweep | 390 total, 380 passed, 0 failed, 10 skipped (039 recorded 368/0/10) |
| ArchTests | 191/191 |
| Full `Sprk.Bff.Api.Tests` (6 chunks partitioning the suite exactly; the last is the complement filter) | **12,301 total, 12,245 passed, 0 failed, 56 skipped**. `Services.` 6,915/0/24 · `Seam.` 1,685/0/0 · `Api.Ai.` 607/0/14 · `Api.Office.` 150/0/9 · other `Api.` 717/0/0 · the rest 2,227/0/9. The 56 skips match 039's 56. |
| CVE (`dotnet list … package --vulnerable --include-transitive`) | "no vulnerable packages" |
| `dotnet format whitespace --verify-no-changes` | production files clean; the new test file clean after EOL normalisation. `OfficeEndpointsContractTests.cs` has 14 findings (lines 1160–1171, 1299–1300), **identical at `f8c20b7c2`**, so they are pre-existing and outside this diff. They were left alone. |
| Publish (PowerShell `Compress-Archive` Optimal, PDBs incl., Release, short output path) | fresh `origin/master` `e0a6f87c4` = **47,555,885 B**; project head `f8c20b7c2` = **47,605,912 B**; this branch = **47,607,766 B**. **Task delta +1,854 B**; branch vs master +51,881 B. |
| `/conflict-check` | Soft warn. No open PR touches this diff's files except UAC-r2 **#950** on `OfficeService.cs`, whose hunks sit at ~1696–1840 (stub generators); this diff is the suppress branch, ~540–590. No overlap. Task 026 (concurrent) owns the identity resolver / `GetDocumentAsync` / pane files, none of which are touched. |
| `ContentDedupDetector.cs` | unmodified |

## 7. Step 9.5 (code-review + adr-check)

No Critical findings. ADR-001/007/008/010/013/019/028/038 compliant; ADR-044 and ADR-049 are not applicable. NFR-07
(`sprk_graphitemid_uk` untouched) and NFR-08 (the editable path is unchanged) hold.

| Sev | Conf | Finding | Disposition |
|---|---|---|---|
| Warning | high | The goal's "overwrite" half is open for Attachment C4 (and Email pending (b)) | Reported (§5); owned by 025 |
| Warning | medium | An `OperationCanceledException` during the guard now propagates to `SaveAsync`'s outer catch (job Failed, `OFFICE_INTERNAL`). The old cleanup swallowed everything. | Accepted: truthful, matches the `RecordNewVersionAsync` convention, and the item is never deleted on that path |
| Warning | medium | The guard's correctness needs the app-only `IGenericEntityService` to see every `sprk_document` (not security-trimmed) | Reasoned: it is the same service as the detector's tenant-wide canonical lookup. **Unverified live** |
| Warning | high | Fail-safe skips leak transient blobs | Accepted (§3); logged with the item id |
| Suggestion | high | `canonicalDocumentId` is used only for log wording | Kept; documented on the parameter |
| Suggestion | medium | The world's new `RetrieveMultiple` branch answers any `sprk_document` query with a `sprk_graphitemid` Equal condition and no hash condition, so it would shadow a future query of that shape | Documented in the world's summary; none exists today |
| Suggestion | high | `SpeWriteSinkContainerProvenanceGuardTests`'s description of this delete site ("never the canonical item") is now enforced by the guard, not merely claimed | ArchTest left untouched (keyed on method name + ordinal; 191/191) |
| Suggestion | high | AC2's "user is still notified" is not observable in this fixture (the test caller's oid is not a GUID, so the detector's notification degrades to a log) | The notification runs inside `ReconcileAsync`, unchanged and before the guard; the test asserts the job-level report instead. **Unverified by test** |
| Info | — | `OfficeService.cs` 2,939 → 2,951 lines, 120 → 121 branches (pre-existing large file; 039 noted `SaveAsync`'s breadth). `OfficeDocumentPersistence.cs` 738 → 810 lines (mostly the rationale doc), 33 → 36 branches, one new public method. It stays cohesive: the class's Dataverse reads for the save path. | Accepted |

## 8. Unverified

- Live Dataverse: that the app-only query sees every `sprk_document`, and that string equality on `sprk_graphitemid`
  is case-insensitive (the world models it that way).
- Live SPE: that `Replace` on an existing name writes to the same item (025 M2). The guard does not depend on it: it
  checks references regardless.
- The detector's user notification on the suppressed save (see §7).
- No live Outlook/Word host run.
