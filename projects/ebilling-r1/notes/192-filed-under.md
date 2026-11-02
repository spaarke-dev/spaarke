# Task 192: the from-email 409 "filed under another record" names the record

Branch `task/ebill-192-filed-under`, from `origin/task/ebill-191-reader-preflight` @ `d4849acbe6` (it builds on 191's changes to the same files). Owner-approved 2026-10-31, asked for by word-add-in-r1 after the first dev UAT: the user could not tell which record the email belonged to. Wire contract: `notes/095-from-email-contract.md` section 6 (the 409 row).

## Change

The from-email 409 `ebilling.submission.document_core_record_mismatch` for "the communication is filed under another core record" gets the optional top-level member `filedUnder: { entityType, entityId, name }`. It is named only when the caller can open the record.

| Case | Body |
|---|---|
| The record is not a secure record, exactly one other record is involved, the caller has Read on it, its name was read | the 409 plus `filedUnder` |
| Any other case: a secure record, no Read, a fault reading the rights or the name, an unknown record type, several other records | exactly today's 409: same status, code and detail, no `filedUnder` |

The 409 with the field (`entityId` varies):

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Conflict","status":409,"detail":"The email is filed under a different record than the one named, so it cannot be submitted for this record. Nothing was saved.","errorCode":"ebilling.submission.document_core_record_mismatch","filedUnder":{"entityType":"Matter","entityId":"ed229f1b-a586-4246-9cfb-e91735435c21","name":"Acme v. Roe"}}
```

Without the field:

```json
{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Conflict","status":409,"detail":"The email is filed under a different record than the one named, so it cannot be submitted for this record. Nothing was saved.","errorCode":"ebilling.submission.document_core_record_mismatch"}
```

(Both captured from the contract suite's real pipeline; the first test asserts the named body minus `filedUnder` equals the unnamed one member for member.)

| Area | File | Change |
|---|---|---|
| Candidate | `Services/Finance/Submission/FromEmailSubmissionService.cs` | `CheckCommunicationFilingAsync` (the one place that builds this 409, used by the main path and the duplicate path) collects the distinct other records the filing chain names and, for exactly one, asks `FiledUnderCandidateAsync`. That asks `ExistingDocumentIsolationCheck.CheckCommunicationForFilingAsync`, the secure-record test step A files by: any verdict but Allowed, or a fault (a Warning), is no candidate. The candidate rides on the outcome as `FromEmailSubmissionOutcome.FiledUnder`. The constructor takes the (already registered, scoped) check. |
| Read and name | `Api/Finance/InvoiceSubmissionFromEmailHttp.cs` | `FiledUnderAsync`: the caller's own Read through `FinanceAuthorizationFilter.CallerHoldsRightsAsync` (operation `finance.read`, the caller's token, as `FindReplacementAsync` does for `replacedBy`), then `ReadNameAsync` (app-only, only after Read passed). Any refusal or fault leaves the field out (a Warning for a fault). `ReadNameAsync` takes an optional label for its log lines (default unchanged). The handler takes `IGenericEntityService` (already registered). |
| Wire type | `Services/Finance/Submission/FromEmailSubmissionContracts.cs` | `FromEmailFiledUnder(EntityType, EntityId, Name)`. |
| Tests | `tests/integration/contract/Api/Finance/InvoiceSubmissionFromEmailContractTests.cs`, `tests/unit/.../FromEmailWorld.cs` | 7 cases (below). The world builds the service over the real isolation check. |
| Contract | `notes/095-from-email-contract.md` | Section 6 409 row and a changes line. |

## Decisions

- **Same secure-record test as step A**, not a copy: `CheckCommunicationForFilingAsync` walks the communication's lineage (its regarding columns, its thread, every ownership parent) to its secure roots and tests the Secure Record owner team. A communication filed under a record that is a secure record is not Allowed there, so it is never named. A fault in that read is no candidate, never a changed 409.
- **Read is the caller's own, through the one seam.** `CallerHoldsRightsAsync` is the Finance filter's own Record-path decision, already shared with `replacedBy`. The record is read app-only for its name only after Read passed; a test asserts the name read never happens for a caller without Read.
- **Several other records: no field.** If the filing chain names more than one other record (a communication regarding one record on another's thread), the 409 names none. One record is unambiguous; a list would be a new shape. The core record the request named is not counted among "other".
- **Unknown record type:** the handler maps `InvoiceCoreRecordKind` to the three names with an explicit default of "leave it out". Today the enum has exactly three values, so this is unreachable without a cast; no test (see below).
- **`name`:** the record's primary name column, as 097e's `ReadNameAsync` reads it (an id string for a blank name).
- **The step-A secure 409 is untouched.** That refusal comes from the operation (`SourceFiling`) as a `FromEmailSubmitResult`, never goes through `CheckCommunicationFilingAsync`, and carries no candidate.
- **Idempotency:** the response cache stores successful responses only, so a per-caller 409 body is never replayed to another caller.

## CLAUDE.md §11: the new field, type and constructor parameter

1. **Existing.** Overlaps `replacedBy` (097e: the same Read check and name read for a second record) and `CorrectionPrefillCoreRecord` (the same three members). It reuses `CallerHoldsRightsAsync`, `ReadNameAsync`, `ExistingDocumentIsolationCheck` and `InvoiceSubmissionEndpoints.CoreEntitySet`.
2. **Extension.** The check and the name read are reused as they are. The wire type is new rather than reusing the prefill's record because that one is named for, and versioned with, the prefill contract; a second three-member record keeps the two contracts independent. The service takes one more existing scoped dependency; no new interface, endpoint, DI registration, package or column.
3. **Cost of doing nothing.** The user whose submit is refused as "filed under a different record" is not told which one (first dev UAT, word-add-in-r1), so cannot fix the filing or pick the right record.

## BFF §10

Placement: `Sprk.Bff.Api`, `Api/Finance` and `Services/Finance/Submission`, beside the 409 it extends; in-request work; no background work, package, endpoint or DI registration (the isolation check was already registered scoped, `FinanceModule`). No AI facade involved.

**Publish size**, 2026-11-01, as 191 measured it: `git archive` exports at short paths (`C:\w192m` `origin/master` @ `b50301a219`, `C:\w192b` this head @ `46665c686e`), `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o <dir>\deploy\api-publish`, PDBs included, `Compress-Archive -CompressionLevel Optimal` over `api-publish\*`, 192 files on both sides.

| Side | Zipped | `Sprk.Bff.Api.dll` |
|---|---|---|
| master `b50301a219` | 40,980,975 B (39.08 MiB) | 24,243,200 B |
| this head | 41,096,472 B (39.19 MiB) | 24,528,384 B |

Delta against the fresh master build: **+115,497 B (+0.11 MB)**. It includes everything `work/ebilling-r1` holds beyond master (191 measured +112,145 B for its head), so this task adds about +3.3 KB. Far under the +5 MB trigger and the 55 MB review line. `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. Both folders deleted.

## Tests (7 in `InvoiceSubmissionFromEmailContractTests`, the real pipeline, filters and services; doubles at Dataverse, rights and the operation)

| Test | Pins |
|---|---|
| `FiledUnderAnotherMatter_CallerCanRead_NamesIt_AndNothingElseChanges` | present for a Matter; the same request without Read gives the same body minus the field, member for member |
| `FiledUnderAWorkAssignment_CallerCanRead_UsesTheIntakeVocabulary` | `entityType` is `WorkAssignment`; no logical name in the body |
| `FiledUnderAnotherMatter_CallerCannotRead_LeavesTheFieldOut` | absent for a caller without Read; the record's id and name appear nowhere; no Warning; the name is never read |
| `FiledUnderASecureRecord_LeavesTheFieldOut_EvenWithRead` | absent for a secure record even with Read |
| `AFaultReadingTheRightsOrTheName_LeavesTheFieldOut_AsTheSame409` (2 rows) | absent, the same 409 (not a 503), a Warning; one row per fault the brief names |
| `StepASecureRefusal_IsUnchanged_NoFiledUnder` | the step-A secure 409: same code, detail and members, no field |

The brief's closed set, nothing beyond. Not tested, by design: the unknown record type (unreachable without casting the enum) and the several-other-records case (the same branch as "no candidate"); both are one-line guards visible in the diff.

**Fail-before** (the handler's `if (outcome.FiledUnder ...)` forced off, the pre-change behaviour): 4 of the 7 rows fail (the two "present" tests and the two fault rows, which need the Warning); the other three pass by design, because the pre-change body is what they assert.

**Seed proof** (the Read check, a disclosure guard): `if (!mayRead)` changed to `if (!mayRead && false)`. Two tests went red: `FiledUnderAnotherMatter_CallerCannotRead_LeavesTheFieldOut` and `FiledUnderAnotherMatter_CallerCanRead_NamesIt_AndNothingElseChanges` (the "without Read" half); 82 passed. Mutation reverted; 84 of 84 in the file pass.

## Gate

Five test projects build with 0 warnings and 0 errors. `Sprk.Bff.Api.Tests` unit filter `Submission|Invoice|Finance|FromEmail`: 6,295 passed, 0 failed, 5 skipped (live tests). `Spaarke.ArchTests` full: 1,869 passed, 0 failed (ISS-005 did not flake). `Sprk.Bff.Api.IntegrationTests`: 199 passed, 8 skipped (live). `Spe.Integration.Tests`: 353 passed, 23 skipped (live). `Sprk.Provisioning.ControlPlane.LoadTests`: build only.

## Known limits

- The field is for the add-in's message; it is not a way to look a record up. A caller who can open the record learns only what they can already read there (its type, id and primary name).
- A communication filed under the requested record and also under one other record (by thread) names that other record, as it is the one that conflicts.
