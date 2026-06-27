# 🚨 RESTART POINT — Helper not running despite correct deploy

> **Created**: 2026-06-08 ~17:40 EDT (21:40 UTC)
> **Branch**: `work/spaarke-multi-container-multi-index-r1`
> **Latest commit**: `80099da5` (docs) — code commits behind: `10157523`, `22faac8d`, `d65dcc2f`
> **BFF deploy state**: Latest code deployed via `Deploy-BffApi.ps1` ~17:40 EDT (hash-verified, /healthz 200)

---

## TL;DR — the mystery

**My `IPostUploadIndexingEnqueuer.EnqueueIfApplicableAsync` is NOT being invoked from `OBOEndpoints.cs` despite the source code clearly calling it.** Files upload to SPE successfully (PUT returns 200) but the helper never runs → no sync OBO indexing → file never reaches `spaarke-file-index`.

### Evidence

1. **Source code IS correct.**
   - [OBOEndpoints.cs:114](src/server/api/Sprk.Bff.Api/Api/OBOEndpoints.cs#L114) contains `await postUploadIndexingEnqueuer.EnqueueIfApplicableAsync(indexingRequest, ctx, ct);`
   - Only ONE `MapPut` for `/api/obo/containers/{id}/files/{*path}` — no duplicate routes
   - No `#if` directives in the file
   - The `if (item is not null && !string.IsNullOrWhiteSpace(item.Id))` block at line 86 enters when `item.Id` is non-empty (which it IS — Graph returns it)
2. **Deploy IS current.**
   - `Deploy-BffApi.ps1` hash-verified all 4 critical files including `Sprk.Bff.Api.dll`
   - Build clean: `dotnet build src/server/api/Sprk.Bff.Api/` → 0 errors
3. **The PUT works, returns 200 in ~1.1s.** User confirmed: 200 OK, Bearer auth present, all Phase 3b query params correct.
4. **App Insights traces show the endpoint executing, BUT skipping the helper:**

   ```
   21:00:54.342 | Executing endpoint 'HTTP: PUT /api/obo/containers/{id}/files/{*path}'
   21:00:54.342 | OBO upload starting - Container: ... Path: ...OATH.pdf
   21:00:55.440 | OBO upload successful - DriveItemId: 01MJSXLZGQUG5EXD4PCBBL7LJRMTD2HGUA
   21:00:55.440 | Setting HTTP status code 200.              ← NO HELPER TRACE BETWEEN!
   21:00:55.440 | Writing value of type 'FileHandleDto'
   21:00:55.440 | Executed endpoint
   ```

   Between "OBO upload successful" (line 79 in my code) and "Setting HTTP status code 200" (return at line 117), there should be helper invocation + helper's own logs. There aren't any. **The if-block contents at lines 86-115 appear to be SKIPPED.**

5. **Even the SECOND test (21:32 UTC) after a fresh redeploy showed the same pattern** — OPTIONS preflight logged, no PUT logged at all that time, file landed in SPE somehow, document record created.

### What's been ruled out

- ❌ App Insights sampling (other Info traces appear in same timeframe)
- ❌ Duplicate route registration (only one MapPut found)
- ❌ Conditional compilation (#if directives absent)
- ❌ Wizard not calling endpoint (confirmed PUT happens with all query params)
- ❌ Bearer auth (user confirmed token present, PUT returns 200)
- ❌ CORS preflight blocking (OPTIONS returns 204 with proper headers)
- ❌ Stale wizard bundle (Matter cascade fix IS working — same deploy)
- ❌ Stale BFF worker (hash-verified deploy + restart)

### Active hypothesis

The deployed `Sprk.Bff.Api.dll` is built from a source file version that DOESN'T include the helper call, despite my source on disk being correct. This could be:
- Build/publish pipeline using a cached intermediate
- `dotnet publish` skipping the file because of a stale `.csproj` reference
- Razor source mismatch (unlikely — no Razor here)

### Next diagnostic step

**Add an explicit `Console.WriteLine` / `logger.LogWarning` literal RIGHT BEFORE line 86 (the `if (item is not null...)` check)** with a unique marker like `"PHASE3A-V2-MARKER"`. Deploy. Have user retest. If marker appears in App Insights → code IS deployed but if-block somehow falsy → debug if-block. If marker doesn't appear → deploy pipeline is shipping stale code → investigate publish/build.

---

## Files in flight (clean local working tree)

All code committed + pushed. No uncommitted changes apart from .husky noise.

### Commits today (this session)

| Commit | Description |
|---|---|
| `2c9b9e73` | Pre-session DI fix (BFF startup unblock) |
| `17bc9e5b` | Design doc + implementation checklist + current-task pointer |
| `fd9dda7d` | Phase 1 — IPostUploadIndexingEnqueuer + DI + 21 unit tests |
| `71b1af57` | Phase 2 — refactor 3 post-upload enqueue sites |
| `d65dcc2f` | Phase 3a — wire helper into 3 BFF upload endpoints |
| `22faac8d` | Matter wizard cascade fix (Project DI pattern) + Phase 3b query params |
| `10157523` | Helper refactor to sync OBO (Pattern 4) — TWO methods (OBO + AppOnly) |
| `80099da5` | Doc updates — Pattern 4 writer-identity rule + new pattern pointer |

### Key files

- [src/server/api/Sprk.Bff.Api/Services/Ai/IPostUploadIndexingEnqueuer.cs](src/server/api/Sprk.Bff.Api/Services/Ai/IPostUploadIndexingEnqueuer.cs) — 2-method interface
- [src/server/api/Sprk.Bff.Api/Services/Ai/PostUploadIndexingEnqueuer.cs](src/server/api/Sprk.Bff.Api/Services/Ai/PostUploadIndexingEnqueuer.cs) — `EnqueueIfApplicableAsync` (sync OBO via `IFileIndexingService.IndexFileAsync`) + `EnqueueAppOnlyIfApplicableAsync` (Service Bus)
- [src/server/api/Sprk.Bff.Api/Api/OBOEndpoints.cs](src/server/api/Sprk.Bff.Api/Api/OBOEndpoints.cs) — Phase 3a/3b wiring of helper at line 114
- [src/server/api/Sprk.Bff.Api/Api/UploadEndpoints.cs](src/server/api/Sprk.Bff.Api/Api/UploadEndpoints.cs) — sibling wiring
- [src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs](src/server/api/Sprk.Bff.Api/Api/Ai/ChatDocumentEndpoints.cs) — SprkChat persist wiring
- [src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts](src/client/shared/Spaarke.UI.Components/src/components/CreateMatterWizard/matterService.ts) — wizard cascade fix + Phase 3b indexingContext
- [src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts](src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts) — `uploadFilesToSpe` builds the query-param URL
- [src/solutions/CreateMatterWizard/src/main.tsx](src/solutions/CreateMatterWizard/src/main.tsx) — `resolveUserBuDefaults` prop wiring (Project pattern)

### Deployments today

| Asset | Last deploy | Verified |
|---|---|---|
| BFF (`spaarke-bff-dev`) | ~17:40 EDT 2026-06-08 | hash-verify ✅, /healthz 200 |
| `sprk_creatematterwizard` (1048 KB) | 09:43 + 13:44 EDT 2026-06-08 (twice) | cascade fix in bundle, `applyUserBuDefaults` x3, no `_tryGetCurrentUserId` |

---

## Test data accumulated this session

### Documents created via wizard (all in `b!vzGDfDp...` Spaarke container)

| Doc ID | Filename | createdon (EDT) | `sprk_searchindexname` | In `spaarke-file-index`? |
|---|---|---|---|---|
| `c006c474-...` | `1 MRT.P0043WO01_US2024029650-IASR.pdf` | 11:05 | `spaarke-file-index` | ✅ (via manual "Send to Index") |
| `81912e2f-...` | `2 MRT.P0043WO01_...ISR-20250308-2294.pdf` | (pre-fix Matter) | ... | ✅ (manual) |
| `33b25435-...` | `1 MRT.P0043WO01_US2024029650-IASR.pdf` | 12:15 | `null` | ❌ |
| `c39e527b-...` | `51414_19183531_2025-07-11_OATH.pdf` | 15:23 | `null` | ❌ |
| `d9569222-...` | `51414_19183531_2025-07-11_OATH.pdf` | 17:00 | `null` | ❌ |
| `782cef03-...` | `51414_19183531_2025-07-03_SRNT.pdf` | 16:45 | `null` | ❌ |
| `766ad390-...` | `51414_19183531_2025-07-03_1449.pdf` | 17:32 | `null` | ❌ |

**Note**: `sprk_searchindexname = null` on all documents is a SECOND bug — `EntityCreationService.createDocumentRecords` doesn't propagate the field. Separate from the helper-not-running issue. Lower priority since the indexing is intended to happen via the BFF's sync OBO path which has the `searchIndexName` from the upload's query param.

### Matter records

- `42f7ac71-4d63-f111-ab0c-000d3a4d8152` "Programmable E3 Ligase Identification and Characterization" — `sprk_searchindexname = spaarke-file-index` ✅
- `e9638630-6a63-f111-ab0c-000d3a582930` "Targeted Protein Degradation Patent Issue Fee" — `sprk_searchindexname = spaarke-file-index` ✅
- (latest) "Targeted Protein Degradation Patent Application" — `sprk_searchindexname = spaarke-file-index` ✅

**Matter cascade fix is working.** The wizard correctly populates the field on Matter records via the proven Project DI pattern.

---

## Step-by-step recovery instructions for next session

### Step 1: Read this restart point + the latest user trace

The user's last test was at 21:32 EDT — uploaded `51414_19183531_2025-07-03_1449.pdf`. App Insights shows OPTIONS preflight only, no PUT logged. sprk_document created at 21:32:42 EDT with itemId `01MJSXLZDW42NJOXA3ARH2WE4GXGJ4NNOU`. File NOT in `spaarke-file-index`.

### Step 2: Add diagnostic marker

Edit `src/server/api/Sprk.Bff.Api/Api/OBOEndpoints.cs` to add explicit logs around line 79-114:

```csharp
logger.LogInformation("OBO upload successful - DriveItemId: {ItemId}", item?.Id);

// DIAGNOSTIC MARKER (remove after debugging)
logger.LogWarning("MARKER-A: about to check item.Id={ItemId} item-null={IsNull}",
    item?.Id, item is null);

if (item is not null && !string.IsNullOrWhiteSpace(item.Id))
{
    logger.LogWarning("MARKER-B: entered if-block, about to call helper");
    var tenantId = configuration["TENANT_ID"] ?? ...
    // ... existing code ...
    logger.LogWarning("MARKER-C: about to await helper, request built");
    await postUploadIndexingEnqueuer.EnqueueIfApplicableAsync(indexingRequest, ctx, ct);
    logger.LogWarning("MARKER-D: helper returned");
}
else
{
    logger.LogWarning("MARKER-X: if-block SKIPPED, item.Id={ItemId}", item?.Id);
}
```

Deploy (`pwsh -ExecutionPolicy Bypass -File scripts/Deploy-BffApi.ps1`). Verify hash-verify passes. Ask user to retest. Query App Insights for `MARKER-A` / `MARKER-B` / `MARKER-C` / `MARKER-D` / `MARKER-X`.

### Step 3: Decide based on results

| Markers seen | Conclusion | Action |
|---|---|---|
| NONE (no markers at all) | Deploy pipeline is shipping stale code OR conditional compilation is excluding the block | Investigate dotnet publish step — try `dotnet publish -c Release` directly, compare assembly hash, check for ProjectReference issues |
| MARKER-A only | The if-block check fails for some reason | Inspect `item` properties at runtime — maybe `item.Id` is whitespace? |
| MARKER-A + MARKER-X | item.Id is empty | Check Graph SDK return — maybe Spaarke's SpeFileStore strips the ID |
| MARKER-A + MARKER-B + MARKER-C, no MARKER-D | Helper throws/hangs | Check helper's IFileIndexingService.IndexFileAsync — may have its own bug |
| All markers visible | Helper IS running but failing silently inside | Look at helper's WARN/error logs for the swallowed exception |

### Step 4: After fix

- Remove diagnostic markers
- Retest end-to-end
- Verify file in `spaarke-file-index`
- Address the OTHER bug: `EntityCreationService.createDocumentRecords` doesn't set `sprk_searchindexname` on documents (low priority — indexing routing already handled via Phase 3b query params)
- Proceed to Phase 4 (delete `uploadOrchestrator.triggerRagIndexing` in DocumentUploadWizard) once helper proven working

---

## Files for context

- [design doc](projects/spaarke-multi-container-multi-index-r1/notes/upload-indexing-centralization-design.md) — canonical spec, 4 phases
- [implementation checklist](projects/spaarke-multi-container-multi-index-r1/notes/upload-indexing-IMPLEMENTATION-CHECKLIST.md) — checkbox tracker
- [current-task.md](projects/spaarke-multi-container-multi-index-r1/current-task.md) — entry point for recovery
- [auth patterns INDEX](.claude/patterns/auth/INDEX.md) — includes new `spe-writer-identity-matching.md`

---

## Open user questions

1. The `sprk_document.sprk_searchindexname` field is null on all wizard-created documents. The cascade only sets it on the Matter; `EntityCreationService.createDocumentRecords` doesn't propagate. **Fix**: extend `createDocumentRecords` payload to include `sprk_searchindexname`. Defer this until helper-running mystery is resolved — once helper runs and uses Phase 3b query param's `searchIndexName`, the indexing goes to the right place regardless of what's on the doc record.
