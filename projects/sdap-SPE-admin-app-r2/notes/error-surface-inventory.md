# Error-surface inventory — task 001

> **Generated**: 2026-08-21 · **Scope**: `src/server/api/Sprk.Bff.Api/Api/SpeAdmin/**` (18 files) +
> `Infrastructure/Graph/SpeAdminGraphService.cs`
> **Method**: brace-matched catch-block scan (a naive forward-scan regex produced 6 false positives —
> it walked past empty catch blocks into the *next* method's validation guards. Those are recorded in
> §5 so nobody re-introduces them.)

---

## 1. Headline

| Measure | Count |
|---|---|
| `catch` blocks in `Api/SpeAdmin/**` | **117** |
| …that return a response with a **hardcoded string** `detail:` | **43** |
| …that assert a cause **not established** by the caught exception (MISLEADING) | **6** |
| …that assert a *narrowed* cause on a filtered catch (NARROWED) | **2** |
| …that are generic and simply **discard** the real error (DISCARDS) | **33** |
| …that are accurate as written (OK) | **2** |
| `catch (ODataError` sites in `SpeAdminGraphService.cs` | **70** |

**The plumbing to do this right already exists — and the SpeAdmin surface ignores it.**
`GraphErrorTranslator.ToProblemDetails()` (`Infrastructure/Graph/GraphErrorTranslator.cs`) surfaces the real
Graph message + `graphErrorCode`. It has **29 callers** across the document / OBO / upload endpoints — and
**zero** inside `Api/SpeAdmin/**`, where every endpoint hand-writes `Results.Problem(detail: "…")` instead.

> **Correction (2026-08-21).** An earlier draft of this file said "zero callers repo-wide". That was a
> scoping error — the grep behind it was restricted to `Api/SpeAdmin/`. Caught by the compiler when
> changing the signature broke all 29 out-of-scope sites. Consequence for the fix: the parameterless
> overload is **kept as-is** and the richer form added **alongside** it, so nothing outside this task's
> SpeAdmin scope changes.

Likewise `ProblemDetailsHelper.FromGraphException` accepts a `graphRequestId` parameter
(`Infrastructure/Errors/ProblemDetailsHelper.cs:26`) that **no caller ever populates** — repo-wide, nothing
extracted a Graph request id at all. That part was verified against the whole of `src/`.

So this task is not "build an error surface". It is "route the SpeAdmin surface through the one that was
already built, and add the request id it was missing."

---

## 2. The error actually reaches the endpoint — and is thrown away there

Traced end-to-end for the Container Types screen (spec §2.4's worked example):

```
Graph 4xx/5xx
  → ODataError
  → SpeAdminGraphService.ListContainerTypesForConfigAsync (:1553)
      catch (ODataError ex) { throw ex.ToSpaarkeStorageException("ListContainerTypes"); }   ← preserved
  → SpaarkeStorageException { StatusCode, ErrorCode, Message }                              ← preserved
  → ContainerTypeEndpoints.cs:183
      catch (SpaarkeStorageException sse) { … logs sse … }
      return Results.Problem(detail: "Failed to retrieve container types from the Graph API.
                                      Check the app registration credentials in the config.");  ← DISCARDED
```

The exception is logged with its real content and then **replaced** in the user-visible payload. The
admin is told to check credentials that the Containers screen is using successfully at the same moment.

This also means the fix is confined to the endpoint layer for these paths — no GraphService change is
needed to make the real error visible.

### Not in scope here — the 70 `catch (ODataError` sites (task 002)

| Behaviour | Count | Consequence |
|---|---|---|
| translate → `SpaarkeStorageException` | **42** | error reaches the endpoint intact ✅ |
| SWALLOW → `return null` | **13** | caller cannot distinguish *absent* from *failed* |
| SWALLOW → return empty/default | **11** | same |
| rethrow (other) | **4** | needs case-by-case review |

The 28 swallowing sites are the absent-vs-failed conflation — **task 002's job**, per this task's POML
note. Recorded here because it bounds what task 001 can achieve: on a swallowing path there is no error
for task 001 to surface, so those screens stay silent until 002 lands.

---

## 3. Classification — all 43 hardcoded-detail sites

### 3a. MISLEADING (6) — asserts a cause the exception does not establish

These catch **unfiltered** `SpaarkeStorageException` / `HttpRequestException` (any status, any code) and
name a specific cause regardless.

| Site | Asserted cause | Why it is not established |
|---|---|---|
| `ContainerTypeEndpoints.cs:183` | "Check the app registration credentials in the config." | Fires on 403/404/429/500 alike. Real cause per spec §3.1 is that the API does not support application permissions at all |
| `ContainerTypeEndpoints.cs:293` | same | same |
| `ContainerTypeEndpoints.cs:444` | same | same |
| `ContainerTypePermissionEndpoints.cs:171` | same | same |
| `ContainerTypeSettingsEndpoints.cs:186` | same | same |
| `ContainerTypeEndpoints.cs:666` | "Verify the sharePointAdminUrl and app registration credentials." | Unfiltered `HttpRequestException` — covers DNS, TLS, timeout, 5xx |

### 3b. NARROWED (2) — filtered catch, but still names one cause among several

| Site | Filter | Assertion |
|---|---|---|
| `SecurityEndpoints.cs:151` | `when (ex.StatusCode == 403)` | "Ensure the app registration has SecurityEvents.Read.All permission." |
| `SecurityEndpoints.cs:269` | `when (ex.StatusCode == 403)` | same |

The 403 filter makes this a *reasonable* inference, not an established one — a 403 can also be conditional
access, a tenant policy, or a different missing role, and the Graph error code says which. **Treatment**:
keep the actionable hint (it is usually right, and task 013 grants exactly this permission), but stop
presenting it as the whole story — surface the Graph code and message alongside it.

### 3c. DISCARDS (33) — no false cause, but the real error is dropped

All are `catch (Exception)` (31), `catch (HttpRequestException)` (2). Uniform shape:
`"An unexpected error occurred while {verb}."` / `"Failed to {verb}."` The exception is logged and then
excluded from the payload, so an admin sees nothing actionable and cannot quote anything to support.

<details><summary>Full list (33)</summary>

`AuditLogEndpoints.cs:130` · `BusinessUnitEndpoints.cs:65` · `ConfigEndpoints.cs:139, :199, :287, :371, :441` ·
`ContainerCustomPropertyEndpoints.cs:178, :329` · `ContainerEndpoints.cs:222, :344, :479, :610` ·
`ContainerTypeEndpoints.cs:200, :310, :461, :685` · `ContainerTypePermissionEndpoints.cs:188` ·
`ContainerTypeSettingsEndpoints.cs:205` · `DashboardEndpoints.cs:92, :149` ·
`EnvironmentEndpoints.cs:142, :188, :263, :339, :421` · `RecycleBinEndpoints.cs:163, :294, :427` ·
`SearchContainersEndpoints.cs:151` · `SearchItemsEndpoints.cs:175` · `SecurityEndpoints.cs:170, :288`

</details>

### 3d. OK (2) — accurate as written, left alone

`DashboardEndpoints.cs:85` and `:142` — `catch (OperationCanceledException)` → "Request was cancelled."
The exception type *does* establish the cause. No change.

---

## 4. Sites already surfacing the real error (no change needed)

| Shape | Count |
|---|---|
| `catch (SpaarkeStorageException)` → `detail: ex.Message` or interpolated with it | 23 |
| `catch (SpeAdminGraphService.ConfigNotFoundException)` → interpolated with the id | 12 |
| `catch (HttpRequestException)` → interpolated | 4 |
| `catch (…)` → log/rethrow/continue, no response emitted | 34 |

These are upgraded only insofar as they gain `graphRequestId` / `traceId` for free where they route
through the shared helper; their `detail` text was already honest.

---

## 5. False positives — do NOT "fix" these

A forward-scanning regex attributed these to catch blocks. They are **validation guards at the top of the
next method**, and they are correct:

`ConsumingTenantEndpoints.cs:450` ("The 'typeId' path parameter is required.") ·
`ContainerColumnEndpoints.cs:375, :453` · `ContainerPermissionEndpoints.cs:269, :372, :459`
("permissionId/containerId path parameter is required.")

Verified by reading `ContainerPermissionEndpoints.cs:248-275` — the string sits inside
`UpdatePermissionAsync`'s `if (string.IsNullOrWhiteSpace(permissionId))` guard.

---

## 6. Client side (`src/solutions/SpeAdminApp/`)

Better than expected — the transport does **not** discard the detail:

- `authenticatedFetch.ts:71-76` constructs `ApiError(problemDetails?.detail ?? problemDetails?.title ??
  \`HTTP {status}\`, status, problemDetails)`. So `ApiError.message` **is** the ProblemDetails `detail`,
  and the full payload is retained on `.problemDetails`.
- 34 render sites across 12 files do `err instanceof ApiError ? err.message : …` — they render the detail.

What **is** lost: the extensions. `graphErrorCode`, `graphRequestId`, and `traceId` sit on
`err.problemDetails` (which has an `[key: string]: unknown` index signature) and **no screen reads them**.
An admin cannot quote a request id to Microsoft support.

One latent defect: 4 sites read `err.detail` (`ConsumingTenantsPanel.tsx:260, :303, :339, :367`).
`ApiError` has no `detail` property — it is always `undefined` and falls through to `err.message`. Dead
code that reads as if it were doing something.

---

## 7. What task 001 changes

1. `SpaarkeStorageException` gains `GraphRequestId`.
2. `GraphErrorTranslator.ToSpaarkeStorageException` extracts it (`InnerError.RequestId` →
   `InnerError.ClientRequestId` → `request-id` / `client-request-id` response headers).
3. `GraphErrorTranslator.ToProblemDetails` gains a friendly-summary + errorCode + traceId surface and
   emits `graphErrorCode`, `graphRequestId`, `traceId`.
4. `ProblemDetailsHelper` gains `FromUnexpected(...)` for the non-Graph paths + a `Redact(...)` pass so no
   token/secret value can reach a payload.
5. All 41 MISLEADING/NARROWED/DISCARD sites route through those helpers. The 2 OK sites are untouched.
6. Client: a `describeApiError()` helper appends the Graph code + request id; the 4 dead `err.detail`
   reads are removed.

**No new error type, no new service, no new package** — §11 satisfied by extension.
