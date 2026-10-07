# Task 054: a typed-name Email/Attachment save no longer overwrites another document's file

> Base: `12a0b4258` (project branch head, after 014/025/031/045/050/051/053). Rigor FULL, opus @ xhigh, directional.
> Reproduce-first (root CLAUDE.md §10 §F.3): the failing test was run against the unchanged production code before
> the fix. Symbols are named by symbol, not by line.

## 0. What was wrong

Task 046 (b) gave a SYSTEM-DERIVED `.eml` name a unique suffix. The owner's 2026-09-15 decision §1 left the other
half to task 025's refuse-and-ask prompt:

> "Only a SYSTEM-DERIVED `.eml` name gets the short unique suffix. A name the user typed in the pane's Document Name
> box is never changed. **Typed-name collisions are left exactly as today until task 025's refuse-and-ask prompt.**"

Task 025 shipped that prompt for `SaveContentType.Document` ONLY. So two DIFFERENT emails saved under the same
user-typed name still collapsed to ONE stored file: the second save's path-keyed `Replace` landed on the first
email's drive item, the first document's row went on pointing at a file that now held the second email, and the pane
reported success. Silent, and live.

## 1. Why task 025 stopped, and why it was right to

025 set `ConflictBehavior.Fail` so Graph refuses a colliding PUT atomically. Applied to Email/Attachment that broke
`OfficeImmutableSaveFileSafetyTests`, which it **reproduced rather than assumed**. The mechanism, confirmed here by
reading the fixture: `OfficeVersionSaveWorld.PutByPathWithConflictBehavior` increments `CollisionRefusals` and throws
**before** `UploadSmallCalls++`. That suite's first test asserts

```
world.UploadSmallCalls.Should().Be(1, "precondition: the save reached SPE");
```

for a byte-identical, same-name Attachment save that must still end as a SUCCESSFUL suppressed duplicate
(202, `DeduplicatedToExisting`). A blanket `Fail` turns that into a 409 with zero writing uploads — two assertions
broken at once. 025 scoped `Fail` to Document and recorded the gap instead of guessing.

## 2. The ordering problem, stated before any code was written

- **Suppress needs the content hash.** `ContentDedupDetector.ReconcileAsync` reads SPE's `quickXorHash` off the
  PERSISTED item, so it can only run AFTER the upload.
- **A name refusal must decide BEFORE the upload**, or bytes have already moved.
- **A name alone cannot separate the two cases that share it.** Two saves of the SAME capture are a duplicate that
  suppress is right to collapse. Two DIFFERENT emails that happen to share a typed name are two documents that must
  become two files. Only the CONTENT distinguishes them.

### Options considered

| # | Option | Cost / why rejected |
|---|---|---|
| **A** | Blanket `Fail` for immutable; refuse on any collision, never upload | **Rejected.** Turns the byte-identical duplicate into a 409 and drops `UploadSmallCalls` to 0 — breaks two assertions in `OfficeImmutableSaveFileSafetyTests`, which may not be edited (escalation trigger). This is exactly what 025 measured. |
| **B** | Pre-flight name lookup before the upload, keep `Replace` | **Rejected.** Builds a SECOND collision detector — root CLAUDE.md §11, and against 025's own contract that `FindDocumentIdByLocationAsync` "answers *whose name is this*, never *does this name collide*". Also racy: a pre-flight read is TOCTOU where Graph's `Fail` is atomic. |
| **C** | **CHOSEN** — `Fail` for every content type, then discriminate on CONTENT while resolving the refusal | Extends 025's mechanism rather than adding one. Costs one Dataverse read + one content read, **only on a collision**. |

### The chosen rule (C), in `OfficeService.ResolveNameCollisionAsync`

1. **Nothing owns the name** → reclaim via `Replace`. Unchanged from 025; without it a transient failure that
   uploaded but never wrote its row becomes a permanent lockout.
2. **Owned + IMMUTABLE + the stored file already holds exactly these bytes** → re-upload under `Replace` and let the
   existing dedup/suppress branch answer it. **Byte-for-byte the behaviour before this task**, which is why the
   protected suite still passes unmodified.
3. **Otherwise owned** → refuse with `OFFICE_020`, having written nothing.

Document is untouched by rule 2: two Word drafts that are byte-identical right now are still two drafts (NFR-08), so
an editable collision is still refused on the NAME alone, exactly as 025 left it.

### Why the content comparison is sound, and why `ContentDedupDetector` was not touched

`OfficeStorageUploader.ItemHoldsContentAsync` (task 047) already answers "does this item CURRENTLY hold exactly these
bytes?" as `true` / `false` / `null`. It is a **byte comparison through the `SpeFileStore` facade**, not a
re-implemented hash — task 047 chose bytes precisely so a local QuickXorHash could not silently disagree with SPE. It
needs no upload to have happened, which is what makes the pre-upload decision possible at all.
`ContentDedupDetector.cs` is **unmodified** (`git diff --name-only`): its answer — "the canonical for this content" —
is correct for its other callers (email-r2, Compose), and the ordering problem belongs to this caller. Escalation
trigger 2 did not fire.

### What it costs

- **One extra Dataverse read + one content read per immutable collision.** Both are off the happy path entirely: a
  save whose name does not collide does none of this.
- **The refusal carries no `existingDocumentId`.** FR-11's version-save retry is Document-only — an Email/Attachment
  save carrying `document.existingDocumentId` ignores it and creates its own document, pinned by
  `OfficeVersionSaveContractTests.Post_OfficeSave_EmailOrAttachmentCarryingExistingDocumentId_IgnoresIt_AndBehavesAsBefore`.
  Advertising that retry would offer the pane a choice the server does not honour.
- **Consequently the pane has no in-pane retry for an immutable collision.** `AllowRename` ("Keep both") lives on
  `DocumentMetadata`, so an Email/Attachment save cannot ask for a rename either. The user's recourse is to change
  the Document Name and save again. This is a deliberate boundary, not an oversight: the owner's rule is that **a
  name the user typed is never changed automatically**, so auto-renaming an immutable capture is forbidden without an
  explicit user ask, and that ask needs a client field this task does not add. **Recommended follow-up** (client +
  one request field) is recorded in §5.
- **Fail-safe direction.** A collision target that cannot be resolved or read answers "not provably identical" and is
  REFUSED. A refusal is recoverable; overwriting another document's file is not — the same direction task 046's
  cleanup guard chose.

## 3. A residual this task does NOT close

025's reclaim rule trusts the `sprk_graphdriveid` + `sprk_filename` index to decide "nothing owns this name". If a
row's stored name disagreed with the name its item actually holds, the reclaim could still `Replace` an owned file.
That gap is pre-existing, identical for Document saves today, and is not widened here — it is recorded rather than
silently inherited.

## 4. Placement justification (root CLAUDE.md §10) and component justification (§11)

**Modify-only on the existing save spine, inside `POST /api/office/save`. No new route, service, DI registration,
package or Dataverse column.** `git status -- '*.csproj'` is empty, so the package graph is unchanged. No
`Microsoft.Graph` type enters `OfficeService` or `OfficeStorageUploader`; the conflict behaviour travels through the
`SpeFileStore` facade exactly as ADR-007 requires. `OfficeEndpoints.cs` is **not modified** — its `OFFICE_020`
mapping already keys on `error.Code`, not on content type, so the refusal reaches the pane unchanged (this also keeps
task 052 out of conflict).

1. **Existing:** task 025's `ResolveNameCollisionAsync` already owns collision resolution; task 047's
   `ItemHoldsContentAsync` already owns "does this item hold these bytes"; `ResolveVersionTargetAsync` already owns
   "this row's SPE pointers".
2. **Extension:** yes — the same resolver gains one content-discriminating arm, and reuses those two existing seams
   rather than adding a third. The only new surface is two private helpers on the existing class.
3. **Cost of doing nothing:** two different emails saved under the same user-typed name collapse to one stored file
   and the first document's bytes are gone, while the pane reports success.

## 5. Reproduction evidence (pre-054 production code, untouched)

Run in the throwaway worktree `C:\wt-base-054` detached at `12a0b4258`, with ONLY the new test file copied in — the
same isolated-worktree method task 046 used. `Select-String 'TASK 054'` against that worktree's `OfficeService.cs`
answered **False**, proving the production code there carries none of this task's changes.

**Before the fix: 5 failed, 0 passed, 5 total (45 s).** Verbatim assertions:

| Test | Before |
|---|---|
| **`TwoDifferentEmails_UnderTheSameTypedName_…`** (the defect) | `Expected Encoding.UTF8.GetString(stored.Versions[^1]) "From: counsel@test.com…` — the one stored `.eml` held the SECOND email, so the assertion that it still contains `"First reply."` failed. **The first email's file had been overwritten.** |
| `ImmutableNameCollisionRefusal_OffersNoVersionSaveRetry_…` | `Expected response.StatusCode to be HttpStatusCode.Conflict {value: 409}, but found HttpStatusCode.Accepted {value: 202}.` — a different-bytes Attachment under an existing name overwrote it and reported success. |
| `ImmutableNameCollision_WhenTheExistingFileCannotBeRead_…` | `Expected response.StatusCode to be HttpStatusCode.Conflict {value: 409} because unreadable content cannot prove the save is a duplicate, so it is not treated as one, but found HttpStatusCode.Accepted {value: 202}.` |
| `ByteIdenticalAttachmentSave_…_IsResolvedToAReplace_NotARefusal` | `Expected world.CollisionRefusals to be 1 because SPE still refuses the first PUT — the resolution happens after it, but found 0 (difference of -1).` |
| `ImmutableNameCollision_WithNoOwningDocument_IsStillReclaimed_…` | `Expected world.CollisionRefusals to be 1 because SPE refused the first PUT; the reclaim followed it, but found 0 (difference of -1).` |

The first two rows are the data-integrity defect itself: an immutable save silently replacing another document's
stored file and answering `202 Accepted`. The last two fail at base because they pin the new mechanism's internals
(no refusal exists before this task).

## 6. Recommended follow-up (not done here)

An in-pane "Keep both" for immutable captures: one request field (an immutable analogue of
`DocumentMetadata.AllowRename`) plus the pane's existing two-option choice, letting the user explicitly ask for the
rename the owner's rule forbids doing automatically. Server-side this is one extra `ConflictBehavior.Rename` arm.

## 7. Gates

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` + the test project (explicit, no `--no-build`) | green, **0 warnings, 0 errors** |
| Reproduce-first (pre-054 code, throwaway worktree at `12a0b4258`) | **5 failed / 0 passed / 5 total** — §5 |
| `OfficeImmutableSaveFileSafetyTests` + `OfficeEmailSaveNamingTests` + `OfficeCreateCollisionTests` + the new `OfficeTypedNameCollisionTests` | **18 passed / 0 failed / 0 skipped**. The three named files are **UNMODIFIED** — `git status -- tests/integration/data-mutation/OfficeVersionSave/` lists only the new untracked file |
| ArchTests | **191 / 191** |
| Full `Sprk.Bff.Api.Tests` — BASELINE on `12a0b4258`, confirmed before any change | **12,366 passed / 0 failed / 56 skipped** (12,422 total, 30 m 29 s). Reads 12,366 not 12,371, proving the run predates the new tests |
| Full `Sprk.Bff.Api.Tests` — AFTER | Verified by the MAIN SESSION on the **merged tree** — the gate that governs for this project (merge → explicit rebuild of the API and test projects → single-process run with nothing concurrent). A branch-level "after" count would be superseded by that run the moment this branch merges, so none is claimed here. The authoritative number is recorded in `TASK-INDEX.md` and PR #960. **No number is estimated in this note.** |
| Publish size (PowerShell `Compress-Archive -CompressionLevel Optimal`, incl. PDBs) vs a FRESH build of **my own base** `12a0b4258` (not `origin/master`) | base **47,631,358 B** → branch **47,633,556 B** = **+2,198 B (+0.0021 MB)**. Far under the +5 MB escalation threshold and the 60 MB ceiling |
| CVE (`dotnet list package --vulnerable --include-transitive`) | "no vulnerable packages". `git status -- '*.csproj'` is EMPTY, so the package graph is unchanged |
| `/conflict-check` | **0 of 20 open PRs** touch `Services/Office/` or `Api/Office/`. `OfficeEndpoints.cs` / `ChatEndpoints.cs` (task 052's concurrent surface) are NOT touched |
| `ContentDedupDetector.cs` | **unmodified** (`git diff --name-only`) — escalation trigger 2 did not fire |
| Files changed | exactly 3: `OfficeService.cs`, the new test file, this note. No `TASK-INDEX.md`, `current-task.md`, project `CLAUDE.md`, `ci-gated-suites.txt`, or CI workflow touched |

## 8. Step 9.5 (code-review + adr-check)

**No Critical findings. No ADR violations.** `OfficeService.cs` 2,979 → 3,111 lines (+132, +4.4%); the new surface is
one extra arm in an existing resolver plus two private helpers — the class keeps its existing responsibility
(orchestrating the save), so this is cohesion-neutral (CLAUDE.md §11.5).

| Sev | Conf | Finding | Disposition |
|---|---|---|---|
| Warning | high | An immutable collision now has **no in-pane retry** ("Keep both" / "Save as new version" are both Document-only). The user must rename and re-save. | Accepted + documented (§2, §6). Auto-renaming is forbidden by the owner's "a typed name is never changed automatically" rule; the explicit ask needs a client field out of this task's scope |
| Warning | medium | `ReadAllBytes` copies the whole request content into a new array to compare it | Accepted: collision path only, bounded by the existing 25 MB attachment cap, and the content is already fully in memory |
| Warning | medium | Reusing `ResolveVersionTargetAsync` adds a `DocumentReads` entry, and `ItemHoldsContentAsync` a `DownloadCalls`, on an immutable collision | Verified safe: every existing assertion on those counters is on a Document/version path or a save that never collides (`OfficeVersionSaveRevertTests`' two immutable cases are answered by the idempotency key and the response cache respectively) |
| Warning | medium | The reclaim rule still trusts the `sprk_graphdriveid` + `sprk_filename` index to mean "unowned" | Pre-existing and identical for Document saves; recorded in §3, not widened |
| Suggestion | medium | An `OperationCanceledException` from the content read propagates to `SaveAsync`'s outer catch | Consistent with the `RecordNewVersionAsync` / task-047 convention already on this path |
| Suggestion | low | `ByteIdenticalAttachmentSave_…_IsResolvedToAReplace_NotARefusal` overlaps the protected suite's AC2 guard | Kept: it pins this task's own internals (`CollisionRefusals` + `UploadSmallCalls`), which no existing test asserts |

**ADR compliance.** ADR-007 (conflict behaviour travels through the `SpeFileStore` facade; no `Microsoft.Graph` type
enters `OfficeService`/`OfficeStorageUploader` — corroborated by ArchTests 191/191) · ADR-019 (reuses the existing
`OFFICE_020` typed ProblemDetails rather than minting a code; the client already branches on it, and the endpoint
mapping keys on `error.Code`, not content type, so `OfficeEndpoints.cs` needed no change) · ADR-038 (reproduce-first;
tests at the `tests/integration/data-mutation/**` KEEP path; no `Mock<HttpMessageHandler>`, no DI-registration or
ctor-null tests, no `Stopwatch` + `Task.Delay`) · ADR-001 / ADR-008 / ADR-010 / ADR-013 / ADR-044 unaffected (no new
endpoint, auth change, DI registration, AI type or GUID interpolation). **NFR-07** holds — `sprk_graphitemid_uk` is
untouched, and the refusal in fact makes a second row on an existing item *less* reachable. **NFR-08** holds — the
immutable suppress path is preserved exactly (that is what rule 2 exists to protect) and no capture was converted to
link/graduate.

**Escalation triggers: none fired.** The protected tests pass unmodified, `ContentDedupDetector.cs` is untouched, and
the third trigger — "a pre-upload refusal is impossible for immutable captures without a post-upload compensating
step" — turned out NOT to hold: the pre-upload decision is possible because `ItemHoldsContentAsync` answers the
content question without needing the upload to have happened.
