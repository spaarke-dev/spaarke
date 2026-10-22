# Task 090 — Finance file store service, internal download route, reconcile job

> Project: ebilling-r1 (model `procedure-and-file-model.md` §4; EB-D-148; uac-r2 Q5 conditions). Branch `task/ebill-090-file-store` from `work/ebilling-r1` @ `162369cdf5`. Built and tested only: **no deploy, no blob write to dev, no schema change.** Schema is task 088's (live on dev); storage is task 091's (authored, not deployed).

## 1. What was built

| Piece | File | Role |
|---|---|---|
| Contracts | `Services/Finance/Files/StoredFileContracts.cs` | `StoredFileSchema` (columns, containers, parent tables), `StoredFileIds` (deterministic ids), `StoredFileSourceVersion` (one canonical row-version string), `StoredFileSize` (checked `long → int`), `StoredFileNotFound.Is` |
| Blobs | `Services/Finance/Files/StoredFileBlobs.cs` | Name building and parsing (`{tenant}/{parent table}/{parent id}/{file id}`), write-once stage, SHA-256 verify, scan-tag read, ETag-pinned read, delete. Every call is refused for a name outside this tenant and the three containers (`RequireOwnBlob`). Writes the `environment` metadata marker. **Never writes index tags.** |
| Service | `Services/Finance/Files/StoredFileService.cs` | `StageAsync`, `BuildRow`, `CreateForAppAsync`, `UploadForCallerAsync`, `FlipToServableAsync`, `OpenForProcessingAsync`, `JudgeServableAsync` (internal), `StoredFileAudit` |
| Route | `Api/Finance/StoredFileEndpoints.cs` | `GET /api/finance/files/{fileId}/content` (`GetStoredFileContent`), internal callers only |
| Filter | `StoredFileAccessFilter` (in the endpoints file) | Step 0 (shared with `FinanceAuthorizationFilter.EvaluateStepZeroAsync`), then parent Read, then source-record Read for copies, all before the handler |
| Job | `Services/Finance/Files/StoredFileReconcileJob.cs` | ADR-036 `IScheduledJob`, hourly: (A) parent gone → blob, then row; (B) blob with no row, older than the grace, this environment's, submission terminal or absent → delete; (C) detector for rows stuck Not yet servable (reports, never repairs) |
| Gateway | `Infrastructure/Storage/BlobGateway.cs` | Extracted from `SessionFileBlobStore` (shared, MI). `IsAlreadyExists` maps 409 BlobAlreadyExists and 412 ConditionNotMet |
| Options | `Configuration/FinanceOptions.cs` (`FileStore`) + validator | `BlobEndpoint`, `TenantId`, `MalwareScanningEnabled`, `OrphanGraceMinutes` (≥ `SubmissionRetryHorizon`), `NotServableAlertMinutes` |
| Registration | `FinanceModule` | `StoredFileBlobs`, `StoredFileService`, `AddScheduledJob<StoredFileReconcileJob>` — unconditional (ADR-032), one separate block (081 merges first; 090 rebases) |

## 2. API for 092 / 093 / 094 / 150

- **App-created file (092 source versions, 094 attachments, 150 exports):** `CreateForAppAsync(StoredFileRowSpec spec, Stream content, Guid? requestedBy, ct)`. The id comes from `spec`; use `StoredFileIds.ForSourceVersion(submissionId, version)` / `ForAttachment(submissionId, identity)` / `ForUpload(submissionId)` so a retry lands on the same name. A retry is accepted when path and SHA-256 match (the blob is reused, not rewritten). **Keep the spec fixed per submission**: a retry with different bytes is refused, not overwritten.
- **Caller upload (firm SPA, later):** `UploadForCallerAsync(...)` — same rules, creator person recorded.
- **Two-step (stage, then a row in a larger transaction):** `StageAsync(spec, content, ct)` → `BuildRow(spec, staged, owner, creatorPersonId)` → caller's own create → `FlipToServableAsync(fileId, ct)`. The flip re-verifies the blob (exists at its own name, SHA-256 matches) and writes Servable with `UpdateIfRowVersionMatchesAsync`.
- **Processing read (092 extraction, 094):** `OpenForProcessingAsync(fileId, ct)` — app-only, needs Servable and a clean scan exactly like a download; no caller checks.
- **Row version:** `sprk_sourceversion` and `StoredFileIds.ForSourceVersion` both use `StoredFileSourceVersion.ToCanonical(long)` (invariant culture). Parse Dataverse's row version with `TryParseRowVersion`; an unusable one is refused.
- **Size:** `sprk_sizebytes` is an Integer. `StoredFileSize.TryToColumnValue` refuses a length above `int.MaxValue` or below 0, **before any storage call**.
- **Audit (093):** one `StoredFileAudit.Downloaded` event (EventId 9090 `StoredFileDownloaded`) per served download, logged before the first byte; route `internal`/`firm`, caller `systemuser`/`contact`.
- **Route outcomes:** 200 stream (`nosniff`, `no-store`, CSP sandbox; allow-listed media types inline, anything else `application/octet-stream` + `attachment`); uniform 404 for unknown id, no access, not Servable, path not its own, communication-only copy; 409 `file_scan_pending` (no clean verdict yet, scan older than Last-Modified, or an ETag change mid-read); 503 not configured or a store/Dataverse fault.

## 3. Scan rule

Served only when the blob index tag **`Malware Scanning scan result` = `No threats found`** (key matched ignoring case, value exactly) **and** `Malware Scanning scan time UTC` ≥ the blob's Last-Modified. `Malicious`, absent, or any other value refuses. `MalwareScanningEnabled=false` (dev only, until Defender is on) serves untagged blobs; absent means on.

**The scan-time key name is unconfirmed** (Microsoft Learn names the result tag; the time tag's exact key was not verified on a live account). A missing or unparsable time **refuses** (fails closed), so if the key is wrong every download on a scanning-on environment returns 409 — visible, not unsafe. Live item L1.

## 4. Environment marker

Blobs are written with metadata `environment` = the lower-case host of `Dataverse:EnvironmentUrl`. Reconcile (B) deletes a row-less blob only when the marker is this deployment's, so two environments sharing one storage account (dev slots, a Model 1 shared stack) never delete each other's in-flight blobs. A blob with no marker is kept and counted (`otherEnvironments=`).

## 5. Placement and §11

Placement: in the BFF (ADR-052 — short, event-free, Dataverse + blob I/O on the BFF's own identity; the hourly sweep is an ADR-036 `IScheduledJob`, no hand-rolled timer). No new package, no csproj change.

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `StoredFileBlobs` / `StoredFileService` | `SessionFileBlobStore` (AI session files: one container, no rows, no scan) | Its blob I/O was extracted into `BlobGateway` and reused; the Finance rules (tenant path, write-once, scan, row lifecycle) do not belong in an AI store | EB-D-148 files have no store; 092/094/150 cannot persist bytes |
| Download route + `StoredFileAccessFilter` | `FinanceAuthorizationFilter` (record routes, no bytes) | Its step 0 was extracted (`EvaluateStepZeroAsync`) and reused; the parent/source checks differ per file | No internal reviewer can open a submitted invoice file |
| `StoredFileReconcileJob` | `SignalReevaluationJob` pattern (ADR-036) | It is a separate concern (files, not signals) | Orphan blobs and parentless rows accumulate; stuck rows go unseen |
| `IFinanceStepZeroFilter` + `FinanceStepZeroResult` | none | — | The step-0 ArchTest cannot recognise a second filter that runs step 0; closed set enforced (`StepZeroMarkersAreAClosedSet`) |
| `CallerExternality.ClassifyCallerAsync` | inline code in `FinanceAuthorizationFilter` | Extracted, not duplicated | Two copies of the internal/external rule |
| `StoredFileNotFound.Is` | the same inner-exception walk in 6 other Finance/Dataverse files (K3 below) | A shared helper would touch 6 files outside this task | A wrapped not-found from `DeleteAsync` reads as a fault; reconcile would never finish a delete |
| `BlobGateway.IsAlreadyExists` | the 409 check in `SessionFileBlobStore` | Moved into the gateway | A large-upload 412 reads as a fault instead of "already there" |
| `environment` metadata | none | — | Shared accounts cross-delete in-flight blobs |

Registry: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5 row **I-21** (added by this task).

## 6. Decisions and readings (POML vs code)

- Code is split Contracts / Blobs / Service rather than one file — three reasons to change (schema/ids, storage, lifecycle).
- Caller checks live in the filter (repo convention, ADR-008), not the handler; the handler re-judges servability.
- `TenantId` comes from config (`Finance:FileStore:TenantId`), not the token — see open question Q1.
- A1-3 (job idempotence) is met by idempotent deletes (blob then row; not-found is done), not by a lease.
- Added beyond the POML on review: CSP sandbox, content-type allow-list, environment marker, communication-only copy → 404.

## 7. Seeding proofs (tests fail without the code)

Each guard was removed in turn, the endpoint tests run, and the file restored with `git checkout`:
- step 0 removed → must-fire (a) and its variants fail (7 of 36 endpoint tests);
- parent Read removed → (d) fails (2 of 36);
- copy rule removed → (e) fails (1 of 36);
- scan-time comparison removed → `Scan_CleanThenModifiedLater_IsRefused` fails.

Pass-2 reviewer independently confirmed that removing the ETag pin or the scan-time check fails a test. The size guard has an explicit must-fire/must-not-fire pair plus a source scan (`NoUncheckedIntCastOfASize`) rather than a mutation run.

**Pinned tests (uac-r2):**
- sizebytes: `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/Files/StoredFileServiceTests.cs:49` (`Size_OutOfRange_IsRefused`: int.MaxValue+1, long.MaxValue, -1, long.MinValue), `:59` (`Size_InRange_IsWrittenExactly`: 0, 1, int.MaxValue), `:66` / `:79` (stage refused before any storage call, over-max and negative), `:91` (create: no blob, no row), `:104` (`BuildRow` at int.MaxValue written, +1 throws), `:118` (source scan: no unchecked int cast of a size).
- row version: `:144` (`SourceVersion_StoredEqualsHashed_UnderAnyCulture`), `:181`.
- scan time: `:657` (`Scan_CleanThenModifiedLater_IsRefused`); route level `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/StoredFileEndpointsTests.cs:229` (`ScanPending_409`).

## 7a. Verification at the final code tip (f9bfb7942c)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Unit + contract + tenant (compiled into `Sprk.Bff.Api.Tests`), filter `StoredFile|SessionFile|Finance`: 2962 passed, 3 skipped, 0 failed.
- `tests/Spaarke.ArchTests`: 1355 / 1355.
- `tests/integration/Shared/**` not touched (the five-project build rule does not apply).
- Publish size (`dotnet publish -c Release -r linux-x64 --self-contained false`, zipped Optimal, fresh worktrees): master `3891cc098f` 39,582,191 B (37.75 MB); base `162369cdf5` 40,157,067 B (38.30 MB); tip 40,214,881 B (38.35 MB). **Task delta +57,814 B (+0.06 MB)**; vs master +0.60 MB (the rest is earlier ebilling-r1 work). Under every §10 threshold.
- CVE: `dotnet list package --vulnerable --include-transitive` — none; no csproj change.
- `/conflict-check`: only shared file is `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (UAC PRs #2258, #2253 edit row I-16; this task adds I-21 — different lines). 081 shares `FinanceModule` (planned: 081 merges first, 090 rebases; separate block).

## 8. Reviews

Two adversarial passes (opus), code-review, adr-check.

| Finding | Class | Handling |
|---|---|---|
| Pass 2: reconcile treated a masked (field-secured) row, a parent hidden from a set query, and an unknown answer as "parent gone" | F1 | Fixed (3dd762609e): readable sentinel (Status, Purpose, BlobPath); parents confirmed by `RetrieveAsync` not-found; faults → kept, `UnitFaults++` |
| Pass 2: wrapped not-found from `DeleteAsync` read as a fault | F2 | Fixed: `StoredFileNotFound.Is` walks inner exceptions |
| Pass 2: query not paged (5000-row cap silently truncated) | F2 | Fixed: paging cookie, `MaxPages`; truncated runs keep unread rows' blobs |
| Pass 2: no heartbeat on a failed attempt | F3 | Fixed: heartbeat `failed=True`, then rethrow |
| Pass 2: (A) could delete another row's blob via a stored path | F1 | Fixed: own-path parse must name this row |
| Pass 2: create failure deleted a reused blob | F3 | Fixed: delete only a newly written blob when the re-read says no row |
| Pass 1: shared-account cross-environment deletes | F3 | Fixed: environment marker (f9bfb7942c) |
| Pass 1: stored content type served inline (HTML/SVG) | F4 | Fixed: allow-list + octet-stream attachment + CSP sandbox |
| Pass 1: step-0 marker interface open to any implementer | F4 | Fixed: closed-set ArchTest |
| Pass 1: blob calls accepted names outside this tenant | F4 | Fixed: `RequireOwnBlob` on every call |
| Pass 1 K7: copy with only a source communication | K→fixed | Fail closed: 404 |
| Pass 1 K6: timing difference between unknown id and no access | K1 | Known limit: the gate variant equalises the rights call; the remaining difference is one Dataverse read |
| Pass 2: `ReadAllAsync` near 2 GiB | K2 | Known limit: Finance files are capped far below by the size check and upload limits |
| Pass 2: retry accepted on path + SHA only | K2 | Known limit, documented in §2 for consumers |
| Pass 2: 412 on large (block-list) uploads | K2 | Mitigated by `IsAlreadyExists`; live item L3 |
| Code review K3 (found in passing): the inner-exception not-found walk is duplicated in `CoreAncestorResolver`, `InvoiceFamilyCoreRecordGuard`, `InvoiceSubmissionAfterSteps`, `InvoiceSubmissionPersister`, `InvoiceSubmissionProcessingJobHandler`, `SignalReevaluationJob` | K3 | Reported, not fixed here (six files outside scope); candidate for one shared helper |
| adr-check: ADR-002 WP-1 registry row missing | F4 | Fixed: I-21 |

## 9. Live steps left for the main session

- **L1** On an environment with Defender malware scanning on: upload a file, read its index tags, confirm the scan-time key is exactly `Malware Scanning scan time UTC` and its format parses. If not, change `StoredFileBlobs.ScanTimeTagKey`.
- **L2** Dev app settings (task 079, after 091 deploys): `Finance__FileStore__BlobEndpoint`, `Finance__FileStore__TenantId`, `Finance__FileStore__MalwareScanningEnabled=false` until Defender is on.
- **L3** Upload a file over 256 MB twice with the same id; confirm the second is treated as already-there (412 path).
- **L4** As the BFF application user, read a `sprk_storedfile` row and confirm the field-secured columns (`sprk_filestatus`, `sprk_blobpath`, parent lookups) come back; if masked, reconcile keeps everything (safe) and downloads 404.

## 10. Open questions

- **Q1** On a Model 1 shared stack, is `Finance:FileStore:TenantId` one value per Dataverse environment (current reading: yes — it names the customer's folder in the shared account), or must it come from the caller's token?
