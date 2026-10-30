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

Served only when the blob index tag **`Malware Scanning scan result` = `No threats found`** (key matched ignoring case, value exactly) **and** `Malware Scanning scan time UTC` ≥ the blob's Last-Modified **and** ≤ now + 5 minutes. `Malicious`, absent, or any other value refuses. `MalwareScanningEnabled=false` (dev only, until Defender is on) serves untagged blobs; absent means on.

The scan time parses only by `TryParseExact` over a closed set — `O`, `yyyy-MM-ddTHH:mm:ss.FFFFFFFK`, `yyyy-MM-dd HH:mm:ssK` — invariant culture, no white space, and an explicit `Z` or `±hh:mm` at the end (checked separately: no `DateTimeStyles` flag refuses a missing `K`). Since uac-r2 round 1 F2.

**The scan-time key and format are not stated on Microsoft Learn** ("Scan time (UTC)" only). The one observed value is `2026-04-17 17:40:38Z` (an `az storage blob tag list` output in a third-party write-up, nineliveszerotrust.com, April 2026), which the set accepts and a must-not-fire test pins (`Scan_RealDefenderSample_IsClean`). A missing, unparsable or future time **refuses** (fails closed), so a wrong key or format makes every download on a scanning-on environment return 409 — visible, not unsafe. Live item L1.

## 4. Environment marker

Blobs are written with metadata `environment` = the lower-case host of `Dataverse:EnvironmentUrl`. Reconcile deletes a blob — row-less (B) or under a parent-gone row (A, since uac-r2 round 1 F1) — only when the marker is this deployment's, so two environments sharing one storage account (dev slots, a Model 1 shared stack, a prod copy in UAT) never delete each other's blobs. A blob with no or another marker is kept and counted (`otherEnvironments=`); under (A) its row is kept too (the row is the blob's only record and cannot be served with its parent gone).

The marker is defence in depth: since uac-r2 round 1 the name segment itself is per environment (§11).

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

Registry: `docs/architecture/DATAVERSE-WRITE-PATH-ARCHITECTURE.md` §5 row **I-23** (added by this task; numbered I-21 on the task branch, renumbered at the 2026-10-22 merge after master I-19 and 081 I-22).

## 6. Decisions and readings (POML vs code)

- Code is split Contracts / Blobs / Service rather than one file — three reasons to change (schema/ids, storage, lifecycle).
- Caller checks live in the filter (repo convention, ADR-008), not the handler; the handler re-judges servability.
- `TenantId` comes from config (`Finance:FileStore:TenantId`), not the token, and must be the Dataverse environment's OrganizationId (Q1, answered by uac-r2; §11).
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
- size scan controls (uac-r2 round 1 F3): `:147` (`SizeCastScan_MustFire`), `:154` (`SizeCastScan_MustNotFire`); the source scan moved to `:126`.
- row version: `:167` (`SourceVersion_StoredEqualsHashed_PinsTheFormat`; it pins the format, it cannot fail on culture), `:204`.
- scan time: `:764` (`Scan_CleanThenModifiedLater_IsRefused`), `:800` (`Scan_Verdicts`), `:810` (`Scan_RealDefenderSample_IsClean`), `:826` (`Scan_FutureAgainstStoreClock_IsPending`); route level `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/StoredFileEndpointsTests.cs:229` (`ScanPending_409`).

## 7a. Verification at the final code tip (f9bfb7942c)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors.
- Unit + contract + tenant (compiled into `Sprk.Bff.Api.Tests`), filter `StoredFile|SessionFile|Finance`: 2962 passed, 3 skipped, 0 failed.
- `tests/Spaarke.ArchTests`: 1355 / 1355.
- `tests/integration/Shared/**` not touched (the five-project build rule does not apply).
- Publish size (`dotnet publish -c Release -r linux-x64 --self-contained false`, zipped Optimal, fresh worktrees): master `3891cc098f` 39,582,191 B (37.75 MB); base `162369cdf5` 40,157,067 B (38.30 MB); tip 40,214,881 B (38.35 MB). **Task delta +57,814 B (+0.06 MB)**; vs master +0.60 MB (the rest is earlier ebilling-r1 work). Under every §10 threshold.
- CVE: `dotnet list package --vulnerable --include-transitive` — none; no csproj change.
- `/conflict-check`: only shared file is `DATAVERSE-WRITE-PATH-ARCHITECTURE.md` (UAC PRs #2258, #2253 edit row I-16; this task adds I-23 (I-21 before the merge) — different lines). 081 shares `FinanceModule` (planned: 081 merges first, 090 rebases; separate block).

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
| adr-check: ADR-002 WP-1 registry row missing | F4 | Fixed: I-23 (I-21 before the merge) |

## 9. Live steps left for the main session

- **L1** On an environment with Defender malware scanning on: upload a file, read its index tags, confirm the scan-time key is exactly `Malware Scanning scan time UTC` and its value matches one of `StoredFileBlobs.ScanTimeFormats` (expected `yyyy-MM-dd HH:mm:ssZ`). If not, change `ScanTimeTagKey` / `ScanTimeFormats` and add the live sample to `Scan_Verdicts`.
- **L2** Dev app settings (task 079, after 091 deploys): `Finance__FileStore__BlobEndpoint`, `Finance__FileStore__TenantId` = **the dev Dataverse environment's OrganizationId** (`organizations` row, or Power Platform admin center → environment → Organization ID; NOT the Entra tenant id), `Finance__FileStore__MalwareScanningEnabled=false` until Defender is on. A wrong value refuses every file-store operation and logs both ids at the first use.
- **L5** Confirm on the first environment copy (prod → UAT) that the target keeps its own OrganizationId. If a copy carried the source's id over, both would share a segment and only the environment marker would separate them (§4).
- **L3** Upload a file over 256 MB twice with the same id; confirm the second is treated as already-there (412 path).
- **L4** As the BFF application user, read a `sprk_storedfile` row and confirm the field-secured columns (`sprk_filestatus`, `sprk_blobpath`, parent lookups) come back; if masked, reconcile keeps everything (safe) and downloads 404.

## 10. Open questions

- **Q1** (answered by uac-r2, review round 1) Per-environment config is right; the caller's token is wrong; a customer's prod and UAT must not share a prefix. Resolved in §11.

## 11. uac-r2 review round 1

### Fixed

| Item | Fix | Commit |
|---|---|---|
| F1 parent-gone pass deleted a blob without the environment-marker check (prod copied to UAT, shared account: UAT's job deletes prod's bytes) | Reads the blob's properties; deletes only a blob with this deployment's marker; otherwise blob AND row kept, `blobsOfOtherEnvironments++`. The row is kept because it is the blob's only record, cannot be served (parent gone), and deleting it would leave bytes no pass may judge again (B refuses an unmarked blob too); kept, it is counted every run | 7c3a42cebf |
| Tenant segment per customer, not per environment | `Finance:FileStore:TenantId` must equal the Dataverse environment's OrganizationId; checked once per process at the first operation (see choice below) | b81c3ef9ae |
| F2 lenient scan-time parse ("12:30", "11/05/2026 10:00:00", "Oct 23" counted Clean) | Exact formats + explicit designator; future time (> now + 5 min) is Pending; `TimeProvider` injected | 3d3ee6e95e |
| F3 size-cast scan exempted `unchecked(` and missed spacing / `Int32` / `(int)(x)` / totals | New pattern, must-fire and must-not-fire controls | 64e04b7ae0 |
| Parentless row deleted without a read | A not-found read of the parent its path names first; alive, unparseable path or fault → kept (`rowsParentlessKept`, `unitFaults`) | df55388b1c |
| Wrong comments (DeleteIfExists on ContainerNotFound; IGenericEntityService lifetime), culture test name | Corrected; test renamed `SourceVersion_StoredEqualsHashed_PinsTheFormat` | af8a64b20a |
| Dead "probe not registered → 503" branch | Removed; the probe is `GetRequiredService` (the service that needs it resolves first) | 57ee0570a3 |

**The segment choice: validate, not derive.** Deriving the segment from the OrganizationId would make it a value read from Dataverse at run time, so every synchronous name build and parse (`BuildRow`, `StoredFileRow.From`, the job's own-path checks) would wait on an async initialisation. Validating keeps it a fixed configuration value — a row's path is stable for its lifetime, start-up validation (which cannot call Dataverse) still checks only the GUID shape — and adds one app-only read of the `organization` row before the first operation. A match is cached for the process; a mismatch refuses everything for the process with an error naming both ids; a fault is not cached. Nothing is stored on dev yet, so no path migrates. §11 (root CLAUDE.md): no new type or registration — a private check inside `StoredFileBlobs`, which now also takes the existing singleton `IGenericEntityService`; without it, two environments of one customer configured with the same value share a prefix again.

### Known limits (recorded, not fixed)

- **Metadata-only operations move Last-Modified without a rescan.** Setting blob metadata or properties changes Last-Modified but does not retrigger Defender, so the scan time is then older than Last-Modified and the file stays 409 `file_scan_pending` until it is rewritten. The store itself never changes a blob after staging; only an out-of-band change does this. Fails closed.
- **A same-second overwrite after a scan passes `>=`.** Last-Modified (an HTTP date) and the observed scan-time format both have one-second resolution, so an overwrite in the same second as the scan compares equal and passes. The ETag pin still serves only the version whose verdict was read; the residue is an overwrite inside that second that Defender has not rescanned yet.
- **The download serves the scan-read version without re-hashing.** The handler streams the blob pinned to the ETag whose verdict the filter read, but does not recompute its SHA-256 against the row (the flip to Servable did). A byte change keeps the ETag only if storage itself is wrong.
- **`ReadAllAsync` buffers in a `MemoryStream`.** A file near the 2 GiB size limit peaks near 2 GiB (plus the final `ToArray` copy), and a length above `Array.MaxLength` throws. Job reads only; Finance upload limits are far below.
- **No range support; a 412 mid-stream truncates a started response.** The route serves whole files only (no `Range`). If the blob changes after the first byte, the pinned read fails with 412 and the client gets a truncated 200, not a 409 — headers are already sent.
- ~~**F4 (the app-only refusal list) is not done here.**~~ Done in round 2 after the merge brought UAC #2253 in (§12).

## 12. uac-r2 review round 1 re-check (fix round 2)

Branch brought up to date first: merge of `origin/work/ebilling-r1` e371dce6bf (master with UAC #2253, task 081) — 3649883be8. Conflicts: the write-path registry (this task's row is now **I-23**: master I-19, invoice I-20/I-21, 081 Procedures I-22); `FinanceModule` usings (both kept); the route ledger (081 141 → 142, then 090 142 → 143; both credited-form sets kept). 090's tests moved from `FinanceStepZeroCaller` to `StatedCaller` (same API). `RecordCreatorPerson.cs`, the creator-person script and its Pester, and the census merged without conflict (090 had not touched them). One test break came with the merge: master's #2253 guard `EveryGenericUpdateWriter_AsksTheAppOnlyRefusal` flagged `StoredFileService.FlipToServableAsync` (`UpdateIfRowVersionMatchesAsync`); it is listed in `GenericWritersThatUpdateNoDocument` (writes only `sprk_filestatus` on `sprk_storedfile`; it cannot ask the refusal, since F4 refuses the table whole and this is its typed writer).

| Item | Change | Commit |
|---|---|---|
| F4 (UAC binding shape) | `StoredFileSchema.Entity` in `InheritedAccessPermission.AccessTables` (refused whole to app-only generic writers), with its reason line: `sprk_blobpath` and the parent lookups decide which bytes are served to whom and the reconcile job deletes by them; typed writer `StoredFileService`; no playbook node, output mapping or field-mapping rule writes it. `AccessTableReaderScanTests` scans `Services/Finance/Files` and lists `sprk_procedureversion`, `sprk_billingexport` and `organization` (the OrganizationId check's read) as not access, each with its reader; `AccessTableAppOnlyWriterTests` names the table in the exact set and two writer cases. Seed: without the entry, 24 integration cases and the reader scan fail | 17b377eb20 |
| Fix-now test gap: production wiring of the OrganizationId check | `Organization_ProductionConstructor_WiresTheCheck` builds `StoredFileBlobs` through its production constructor (dummy MI endpoint, Strict credential, Strict `IGenericEntityService` answering another org id): `StageAsync` → NotConfigured, `EnsureEnvironmentAsync` throws, no token request. Seed: deleting the `_organizationId = …` wiring fails it | 6429fca623 |
| K1 hardening: marker read and delete not tied to one ETag | Parent-gone pass deletes with `If-Match: properties.ETag`; pass B with the listing's ETag (`BlobListingItem` / `StoredBlobListing` carry it; a listing without one keeps the blob). 412 = changed after it was judged: kept (the parent-gone pass keeps the row too), counted `blobsChangedSinceJudged`, logged; not a unit fault. `BlobGateway.DeleteIfExistsAsync(container, name, ifMatch, ct)` follows `OpenReadAsync`; the session store passes null. Two must-fire tests fail on the old job | 56121a443f |
| K2 gating condition | Recorded below and as a constraint in `tasks/079-wp3-deploy.poml` | this commit |
| K4 size-cast scan misses | Recorded below as a known limit | this commit |
| `IOptionsDriftTests` TenantId text | Now says the value must equal the Dataverse environment's OrganizationId and is validated against the organization row before the first operation (boot checks the shape only) | e068ba36db |
| UAC answer to 081 Q5 | `sprk_ruleset` and `sprk_procedureassignment` join `RecordCreatorPerson.StampedChildTables` and the script's `$Tables` (SpaarkeMaster home; 080 created both columns unsecured, so they pass the universal empty-before-securing gate). Rule sets are stamped now (RuleSetService upload, ProcedureService create already call `owner.ApplyTo`); **no BFF writer creates a `sprk_procedureassignment` row yet** — the stamp applies when one does. Census lists both as stamped outside the codified Secure Record Owner child set. UAC #2277's Entity.xml `IsSecured` guard is not on this branch, so no allow-list entry was added | 15fc689ca0 |

### K2 — HARD precondition (binding on 079 and every later environment)

**F4 merged is a hard precondition for enabling `StoredFileReconcileJob` in any environment.** The job runs whenever eBilling is enabled and `Finance__FileStore__BlobEndpoint` is set, so "enabling" it is writing the file-store app settings. They are written only on a BFF whose deployed commit refuses `sprk_storedfile` to app-only generic writers (F4, `ParentLineage.cs` `AccessTables` names `StoredFileSchema.Entity`), verified by content before the endpoint is set. Without F4 a field-mapping rule, playbook output or update node could re-point `sprk_blobpath` or a parent lookup, and the job would delete by it.

### K4 — known limit of the size-cast scan

The source scan for an unchecked narrowing of a file length (F3) misses: `(int)len`, `(int)count`, `(int)lengthOfFile`, `(int)Math.Min(length, x)`, `(int)(a + length)`, a cast split across lines, and `Convert.ToInt32(length)`. It is a tripwire for the common spelling, not a proof; review still owns the rest.


## 13. uac-r2 review round 2 re-check: ACCEPTED (2026-10-23)

uac-r2 accepted 12329abebe..64e39a9d5f with no fix-now findings. It ran an export of 64e39a9d5f:
- build 0/0;
- ArchTests 1522/1522;
- filtered unit and integration 1333/1333;
- Pester 5.7.1 43/43;
- every seed fails as claimed.

Known limits:
- **K1, a merge obligation.** #2277 is not in this branch or in work/ebilling-r1. Once it is, `CreatorPersonColumnSecuredGuardTests.EveryStampedTableDeclaresTheColumnSecured` needs an entry, either a `SecuredOnlyByDeployScript` entry naming `scripts/Set-ChildRecordCreatorPersonSchema.ps1` or Entity.xml `IsSecured` after an export, for these tables:
  - `sprk_ruleset` (no `sprk_createdbyperson` in Entity.xml);
  - `sprk_procedureassignment` (no Entity.xml);
  - 081's `sprk_procedure`, `sprk_procedureversion`, `sprk_policy` and `sprk_policyversion`.

  This is also recorded in the project CLAUDE.md §6.
- **K2:** the ETag pin misses a reused staged blob. DEF-044, #2296. Fix it before StoredFileReconcileJob is enabled anywhere.
- **K3:** the parent-gone pass doesn't check that the blob path's parent is a gone lookup. Defence in depth. DEF-045, #2297.

## Addendum: the job's dedicated off-by-default switch

`StoredFileReconcileJob` now also needs `Ebilling:StoredFileReconcileEnabled` (default false; skips as `reconcile-disabled` before anything is read). K2 above (DEF-044) and fix F4 must both be closed before it is set to true anywhere. See `notes/040-ebilling-flag.md`.
