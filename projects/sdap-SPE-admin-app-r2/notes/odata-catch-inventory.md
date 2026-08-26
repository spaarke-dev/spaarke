# `catch (ODataError)` inventory — task 002

> **Generated**: 2026-08-21 · **Source**: `src/server/api/Sprk.Bff.Api/Infrastructure/Graph/SpeAdminGraphService.cs` (4,911 LOC)
> **Count**: exactly **70** sites, as the POML predicted. **Zero unclassified.**
> Method: brace-matched extraction of every catch site with its enclosing method, return type, `when` filter,
> and body disposition — then a wrapper-pairing analysis across the file.

---

## 🔔 Headline — the task's premise does not hold

Task 002 was scoped on the belief that these 70 sites *"swallow the failure and return null, false, or an
empty collection … which is why a broken screen renders as an empty grid instead of an error."*

**That is not what the code does.** The audit found a deliberate, already-correct two-layer design:

| Layer | Behaviour |
|---|---|
| **Inner** (`XAsync(GraphServiceClient, …)`) | `catch (ODataError) when (status == NotFound)` → `null` / `false`. **404 only** — every one of the 24 is `when`-filtered. |
| **Outer** (`XForConfigAsync(config, …)`) | `catch (ODataError ex) { throw ex.ToSpaarkeStorageException(…); }` → becomes ProblemDetails via task 001. |

A 403, 429 or 5xx is **never** swallowed. It passes the 404-filtered inner catch untouched and is translated
by the outer wrapper. There is no blanket catch anywhere in the file.

**This also corrects a claim I made in task 001's completion note** — that *"28 of the 70 sites swallow the
error … those screens stay silent until 002 lands."* The count was right; the conclusion was wrong. That note
classified dispositions without checking wrapper pairing. Corrected in
[`task-001-completion.md`](task-001-completion.md).

### Consequences

- **Acceptance criterion 4** ("no ABSENT-TOLERANT site retains a blanket catch that would also swallow a 5xx")
  was **already satisfied** by all 24 sites before this task started.
- The empty-grid symptom in spec §2.4 does **not** originate here. Its real sources are owned by other tasks:
  hardcoded `StorageUsedInBytes: null` (**024**), Sync Status (**003**), Search (**004**), Audit Log (**005**).
- No reclassification was warranted. Manufacturing changes to code that already matches its classification
  would have been the wrong outcome.

---

## Summary

| Classification | Count | Meaning |
|---|---|---|
| **PROPAGATE** | 46 | 42 outer-wrapper translations + 4 rethrows (429 retry, 2× 403, 1 search log-and-rethrow) |
| **ABSENT-TOLERANT** | 23 | 404-only → `null`/`false`; non-404 translated by the named wrapper |
| **ABSENT-TOLERANT\*** | 1 | `SoftDeleteContainerAsync` — no `*ForConfigAsync` wrapper (see below) |

---

## The one real defect found — and fixed

`SoftDeleteContainerAsync` (`:4715`) is the sole site with no translating wrapper. Its only caller is
`Services/SpeAdmin/BulkOperationService.cs`, which caught the raw `ODataError` itself.

Functionally that was **not** a silent failure — the per-item error was recorded into
`status.Errors` and surfaced to the polling client. But it put a Microsoft.Graph type in
`Services/SpeAdmin/`, outside the `Infrastructure.Graph` / `SpeFileStore` boundary **ADR-007 §1** defines —
and ADR-007 is an explicit constraint of this task.

**Fixed** by routing both bulk call sites through the existing `GraphCallScope.Run(…)` helper (which exists
for exactly this) and catching `SpaarkeStorageException` instead. The per-item message now also passes
through `ProblemDetailsHelper.Redact` (task 001), closing a secret-leak path that existed because the raw
Graph message went straight into `BulkOperationItemError`.

**Residual, deliberately not fixed here**: `BulkOperationService` still holds two
`Microsoft.Graph.GraphServiceClient?` locals (`:243`, `:346`). Removing those requires giving the service a
config-scoped bulk API rather than passing a pre-resolved client through a loop — a structural change beyond
this task, and natural work for `speadmingraphservice-decomposition-r1`. NetArchTest does not flag it (the
enforced rule covers endpoints); it is recorded here so the follow-on inherits it rather than rediscovering it.

---

## Full classification — all 70 sites

| # | Line | Enclosing method | Returns on catch | Filter | Classification | Reason |
|---|---|---|---|---|---|---|
| 1 | `:1113` | `GetContainerAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by GetContainerForConfigAsync |
| 2 | `:1169` | `UpdateContainerAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by UpdateContainerForConfigAsync |
| 3 | `:1216` | `ActivateContainerAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by ActivateContainerForConfigAsync |
| 4 | `:1261` | `LockContainerAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by LockContainerForConfigAsync |
| 5 | `:1307` | `UnlockContainerAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by UnlockContainerForConfigAsync |
| 6 | `:1338` | `ListContainerItemsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 7 | `:1346` | `CreateFolderForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 8 | `:1354` | `ListContainersPageForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 9 | `:1362` | `CreateContainerForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 10 | `:1370` | `GetContainerForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 11 | `:1378` | `UpdateContainerForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 12 | `:1386` | `ActivateContainerForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 13 | `:1394` | `LockContainerForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 14 | `:1402` | `UnlockContainerForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 15 | `:1410` | `GetCustomPropertiesForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 16 | `:1418` | `UpdateCustomPropertiesForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 17 | `:1426` | `GetFileVersionsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 18 | `:1434` | `GetFileThumbnailsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 19 | `:1442` | `CreateSharingLinkForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 20 | `:1450` | `CreateSharingLinkForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 21 | `:1458` | `GetPreviewUrlForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 22 | `:1466` | `DeleteDriveItemForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 23 | `:1474` | `ListContainerPermissionsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 24 | `:1482` | `GrantContainerPermissionForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 25 | `:1490` | `UpdateContainerPermissionForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 26 | `:1498` | `RevokeContainerPermissionForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 27 | `:1506` | `ListColumnsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 28 | `:1514` | `CreateColumnForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 29 | `:1522` | `UpdateColumnForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 30 | `:1530` | `DeleteColumnForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 31 | `:1538` | `UploadFileToContainerForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 32 | `:1546` | `SearchContainersForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 33 | `:1554` | `ListContainerTypesForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 34 | `:1562` | `GetContainerTypeForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 35 | `:1570` | `CreateContainerTypeForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 36 | `:1578` | `ListConsumingTenantsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 37 | `:1586` | `RegisterConsumingTenantForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 38 | `:1594` | `UpdateConsumingTenantForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 39 | `:1602` | `RemoveConsumingTenantForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 40 | `:1610` | `GetContainerTypePermissionsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 41 | `:1618` | `UpdateContainerTypeSettingsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 42 | `:1626` | `ListDeletedContainersForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 43 | `:1634` | `RestoreContainerForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 44 | `:1642` | `PermanentDeleteContainerForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 45 | `:1650` | `GetSecurityAlertsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 46 | `:1658` | `GetSecureScoreForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 47 | `:1666` | `SearchItemsForConfigAsync` | → SpaarkeStorageException | `—` | **PROPAGATE** | outer wrapper: translates every ODataError to SpaarkeStorageException |
| 48 | `:1768` | `GetCustomPropertiesAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by GetCustomPropertiesForConfigAsync |
| 49 | `:1855` | `UpdateCustomPropertiesAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by UpdateCustomPropertiesForConfigAsync |
| 50 | `:2123` | `CreateSharingLinkAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by CreateSharingLinkForConfigAsync |
| 51 | `:2184` | `GetPreviewUrlAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by GetPreviewUrlForConfigAsync |
| 52 | `:2229` | `DeleteDriveItemAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by DeleteDriveItemForConfigAsync |
| 53 | `:2416` | `UpdateContainerPermissionAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by UpdateContainerPermissionForConfigAsync |
| 54 | `:2461` | `RevokeContainerPermissionAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by RevokeContainerPermissionForConfigAsync |
| 55 | `:2598` | `UpdateColumnAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by UpdateColumnForConfigAsync |
| 56 | `:2637` | `DeleteColumnAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by DeleteColumnForConfigAsync |
| 57 | `:3147` | `SearchContainersAsync` | rethrow | `—` | **PROPAGATE** | logs then rethrows to the translating wrapper |
| 58 | `:3324` | `GetContainerTypeAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by GetContainerTypeForConfigAsync |
| 59 | `:3516` | `ListConsumingTenantsAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by ListConsumingTenantsForConfigAsync |
| 60 | `:3586` | `RegisterConsumingTenantAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by RegisterConsumingTenantForConfigAsync |
| 61 | `:3670` | `UpdateConsumingTenantAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by UpdateConsumingTenantForConfigAsync |
| 62 | `:3710` | `RemoveConsumingTenantAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by RemoveConsumingTenantForConfigAsync |
| 63 | `:3811` | `GetContainerTypePermissionsAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by GetContainerTypePermissionsForConfigAsync |
| 64 | `:3976` | `UpdateContainerTypeSettingsAsync` | null | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> null; non-404 translated by UpdateContainerTypeSettingsForConfigAsync |
| 65 | `:4258` | `CreateGraphClientFromBearerToken` | rethrow | `status==TooManyRequests` | **PROPAGATE** | 429 retry/backoff; rethrows after MaxRetries |
| 66 | `:4432` | `RestoreContainerAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by RestoreContainerForConfigAsync |
| 67 | `:4483` | `PermanentDeleteContainerAsync` | false | `status==NotFound` | **ABSENT-TOLERANT** | 404 only -> false; non-404 translated by PermanentDeleteContainerForConfigAsync |
| 68 | `:4560` | `GetSecurityAlertsAsync` | rethrow | `status==Forbidden` | **PROPAGATE** | 403 logged then rethrown; endpoint 403-filter renders it |
| 69 | `:4646` | `GetSecureScoreAsync` | rethrow | `status==Forbidden` | **PROPAGATE** | 403 logged then rethrown; endpoint 403-filter renders it |
| 70 | `:4715` | `SoftDeleteContainerAsync` | false | `status==NotFound` | **ABSENT-TOLERANT*** | 404 only -> false; NO translating wrapper - caller handles ODataError directly |

- **PROPAGATE**: 46
- **ABSENT-TOLERANT**: 23
- **ABSENT-TOLERANT***: 1

---

## Verification status

| Acceptance criterion | Status |
|---|---|
| All 70 classified with a reason; zero unclassified | ✅ table above |
| Forced 403/500 on a list endpoint renders an error, not an empty grid | ⚠️ **verified by code trace, not empirically** — see below |
| Graph 404 for an optional resource still returns null/empty with no spurious error | ✅ all 24 tolerant sites are `when`-filtered to `NotFound` |
| No ABSENT-TOLERANT site keeps a blanket catch | ✅ already true before this task |
| Graph SDK types do not leak above the facade (ADR-007) | ✅ improved — `BulkOperationService` catch sites fixed; 2 client locals recorded as residue |
| `dotnet build` 0 errors; publish size within ceiling | ✅ |

**On the empirical criterion**: proving "403 → error, not empty grid" needs a forced Graph failure at the HTTP
boundary. `Mock<HttpMessageHandler>` is banned by ADR-038, and the sanctioned mechanism — WireMock — is
**task 040**, which has not run yet. The propagation path is verified by trace
(inner 404-filter → outer `ToSpaarkeStorageException` → task-001 `ToProblemDetails`), and task 040/041 should
add the executable proof. Recorded rather than claimed.
