# Task 073 — container-keyed app-only write routes: RETIRED, not gated

> **Status**: shipped
> **Date**: 2026-08-26
> **Phase**: 0c Secure Documents — Wave 1
> **Outcome**: deviated from the POML's prescribed fix (gate) to its stronger sibling (retire), on evidence. No container→record mapping was built, so **task 075's seam remains the only one**.

---

## 1. The one-line answer

The POML asked me to authorize `PUT /api/containers/{containerId}/files/{*path}` against the owning record. I **deleted it instead**, along with its two siblings, because a repo-wide sweep found **zero callers** — and gating would have required duplicating the container→owning-record mapping that task 075 is building in parallel.

Retired (whole file `Api/UploadEndpoints.cs` deleted):

| Route | Why it had to go |
|---|---|
| `PUT /api/containers/{containerId}/files/{*path}` | Wrote the request body into a caller-named drive **as the managed identity** |
| `POST /api/containers/{containerId}/upload` | Returned a Graph **pre-authenticated** `UploadUrl` — a bearer-free write credential |
| `PUT /api/upload-session/chunk` | No resource key at all; a **stub** that wrote nothing and logged the pre-auth URL |

---

## 2. Why retire rather than gate — the evidence, in order

### 2a. The POML's stated premise is false

The POML constraint reads:

> *"Check client callers before tightening — **this route is used by upload flows**; a refusal that breaks legitimate uploads will be reverted, which reopens the hole."*

It is **not** used by upload flows. Every live upload flow calls the **OBO sibling**, `PUT /api/obo/containers/{id}/files/{*path}`:

| Caller | File:line |
|---|---|
| 11 wizard call sites (all `Create*Wizard` + `DocumentUploadWizard`) | `src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts:493` |
| `@spaarke/sdap-client` | `src/client/shared/Spaarke.SdapClient/src/operations/UploadOperation.ts:27` |
| document-upload service | `src/client/shared/Spaarke.UI.Components/src/services/document-upload/SdapApiClient.ts:101` |

Searched for the non-OBO routes across `src/client/**`, `src/solutions/**` (incl. `.js` web resources), `src/dataverse/**`, `src/server/**`, `tests/**`, `scripts/**`, `infrastructure/**`, the single `.http` file, and the only Postman collection — using literals, template-literal joins (`containers/${`, `+ "containers`), and route-builder constant maps. Also checked for a proxy/rewrite that could turn `/api/obo/...` into `/api/...`: the only proxy is `src/client/external-spa/vite.config.ts:113-122`, a dev-only pass-through with **no** `rewrite`, and there is no `MapWhen`/`UsePathBase`/rewrite middleware server-side.

**Result: zero real callers for all three.** The only non-OBO `/api/containers` references in client code are two adapter calls to `/api/containers/{entityName}/{entityId}` (`xrmUploadServiceAdapter.ts:177`, `bffUploadServiceAdapter.ts:192`) — a 2-segment GET that matches **no registered route** and would 404 today. They are not callers of these routes. (See §6, finding 3.)

### 2b. Gating would have forked task 075's mapping

The authorization subject for a container-keyed write is the **owning record**, which requires container→record resolution. That mapping is task 075's deliverable, and task 075's constraints are explicit:

> *"Bidirectional: record → container AND container → owning record, from one mapping, so task 073 consumes it rather than reimplementing."*
> *"Do NOT build a second container-to-record mapping."*

Task 075 is being built in a **different worktree** and its seam does not exist here. So gating had exactly two options — duplicate the mapping (forbidden) or block on 075. **Deletion needs neither.** Acceptance criterion "exactly one container-to-record mapping exists in the codebase, shared with task 075" is satisfied by my having added **zero**.

### 2c. Deletion is the guard's own prescribed remedy

`RouteAuthorizationGuardTests.EveryGovernedRouteCarriesPerResourceAuthorizationOrANamedWaiver` lists remedies in preference order. Remedy **#2**:

> *"If the route should not exist under broker-only, DELETE it. That was task 071's preferred outcome for the OBO drive-keyed routes."*

Task 071 set the precedent in this same project — it deleted four zero-caller OBO routes and left `tests/integration/regression/OboDriveKeyedRouteRetirementTests.cs` asserting their absence. This task follows that shape exactly.

### 2d. It is the stronger security outcome

A gate answers "may *this* caller write here?". Deletion answers "**no caller can write here at all**". For a route with no legitimate users, the second is strictly better and cannot be misconfigured later.

---

## 3. Two severity escalations the POML did not anticipate

### 3a. `POST /api/containers/{containerId}/upload` was worse than the route the task was named after

`UploadSessionDto` is `(string UploadUrl, DateTimeOffset ExpirationDateTime)` — and `UploadUrl` is a Graph **pre-authenticated** upload URL. Its holder can `PUT` bytes to that drive **with no token at all**, from anywhere, outside the BFF entirely, until it expires.

So the route did not perform one unauthorized write; it **minted a durable bearer-free write credential** into a caller-named container, app-only, behind a policy that resolved document rights from a container id. This is the same category of escalation task 072 recorded for share-links (a handle that outlives revocation), and it was sitting on the route the task treated as secondary.

### 3b. The named route's blast radius was drives, not containers

`UploadEndpoints.cs:58` passed the route's `containerId` straight into `SpeFileStore.UploadSmallAsync(string driveId, …)`, which reaches `graphClient.Drives[driveId]` (`UploadSessionManager.cs:45`). The parameter was **misnamed at the call site**: the value was used as a **drive** id. The reachable set was therefore any drive the managed identity could address, not merely the SPE containers of the container type.

Note the asymmetry with its sibling: `POST .../upload` correctly resolves container→drive first (`UploadSessionManager.cs:111`); the `PUT` route did not.

### 3c. Bonus: the chunk route logged a credential

`UploadEndpoints.cs:193` logged the caller-supplied `Upload-Session-Url` at **information** level. That URL is the pre-authenticated credential from §3a. Pre-auth upload URLs in App Insights are a credential-at-rest problem. Verified the **OBO sibling does not do this** (`OBOEndpoints.cs:137-198` logs no session URL), so this was confined to the retired stub.

---

## 4. What shipped

| File | Change |
|---|---|
| `src/server/api/Sprk.Bff.Api/Api/UploadEndpoints.cs` | **DELETED** (all three routes) |
| `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs` | `MapUploadEndpoints()` call removed; replaced by a retirement comment stating what was removed, why, the zero-caller evidence, and the supported alternatives |
| `tests/integration/regression/MiContainerKeyedWriteRouteRetirementTests.cs` | **NEW** — 7 tests (5 absence + 2 positive controls) |
| `tests/unit/Sprk.Bff.Api.Tests/EndpointGroupingTests.cs` | Removed 3 tests whose subject no longer exists (all were already `Skip`'d) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/InlineNotificationIntegrationPointsTests.cs` | Dropped `typeof(UploadEndpoints)` from the integration-point list; renamed `AllFour…` → `AllRemaining…` |

### The load-bearing assertion is not a status code

Per the verification bar: *a 403 returned after the upload was issued is not a denial.* The primary assertion is `RetiredMiWriteRoutes_AreAbsentFromTheEndpointTable`, which enumerates the composed `EndpointDataSource` and proves **no handler exists** to reach the write. That is unfakeable by any fixture or status-code mapping, and it is strictly stronger than a spy: a spy proves one request did not reach the write; endpoint enumeration proves none ever can.

It is guarded against vacuity two ways: an explicit `endpoints.Should().NotBeEmpty()` precondition, and two positive controls on the surviving OBO route.

**This design choice was vindicated by the perturbation run (§5): with the routes restored, an authenticated caller got `403`.** A naive "assert 403" test would have **passed against the vulnerable code** — exactly the trap the verification bar warns about.

The behavioural half asserts 404 both **without** a bearer (ASP.NET Core routes before it authorizes, so a present route answers 401 — 404 proves absence) and **with** a bearer (proving the 404 is not itself an auth artifact).

---

## 5. Perturbation verification

Restored `UploadEndpoints.cs` from git + re-added `app.MapUploadEndpoints()`, rebuilt, re-ran:

```
Failed: 5, Passed: 2, Total: 7
```

| Test | Perturbed result | Correct? |
|---|---|---|
| `RetiredMiWriteRoutes_AreAbsentFromTheEndpointTable` | FAIL — named all 3 re-registered routes | ✅ |
| `RetiredMiSmallFileUploadRoute_WithoutBearer_Returns404NotRouted` | FAIL | ✅ |
| `RetiredMiSmallFileUploadRoute_WithValidBearer_Returns404AndNeverReachesTheWrite` | FAIL | ✅ |
| `RetiredMiUploadSessionCreateRoute_WithValidBearer_Returns404NotRouted` | FAIL — **found 403** | ✅ (see §4) |
| `RetiredMiUploadChunkRoute_WithValidBearer_Returns404NotRouted` | FAIL | ✅ |
| `SurvivingOboUploadRoute_WithoutBearer_Returns401NotFound` | **PASS** | ✅ control held |
| `SurvivingOboUploadRoute_WithValidBearer_IsRoutedAndNot404` | **PASS** | ✅ control held |

Then fully reverted (file re-deleted, call site restored to the comment-only form) and re-verified green.

---

## 6. Open items handed back — NOT fixed here

### 1. `RouteAuthorizationGuardTests.cs` needs 4 edits (main-session-owned file)

My change turns **5** Task-074 guard tests red. All 5 are the ratchet working as designed — the census message itself says *"If a file was REMOVED, bump the count down and delete any waivers that pointed into it."* Exact remediation:

| # | Edit | Fixes |
|---|---|---|
| 1 | Delete the `GovernedFile("Api/UploadEndpoints.cs", Scope.RouteLevelGate, …)` entry | the 4 `FileNotFoundException` failures (`ScanFile` does `File.ReadAllText`) |
| 2 | `ExpectedEndpointFileCount` **111 → 110** | census: *"expected 111, found 110"* |
| 3 | Delete the 3 Pending waivers for the retired routes | hygiene — **not test-enforced** (see below) |
| 4 | Remove those same 3 strings from `PolicyOnlyRoutes` | `TheSetOfPolicyOnlyRoutesIsPinned` (`removed` ≠ 0 once edit 1 lands) |

⚠️ **Edit 3 is not caught by any test.** `NoWaiverIsStale` fires only when a waived route becomes **gated**, never when it is **deleted** — the file's own comment already flags this gap (*"Worth extending"*). So three waivers describing routes that no longer exist will sit there silently unless deleted by hand. The file calls that state *"worse than noise"*.

### 2. Task 073 owns **five** waivers, not four — and two are untouched

The two I did **not** address, both in `Api/DocumentsEndpoints.cs` (outside this POML's declared output, and each has a real caller):

| Route | Registered | Caller | Status |
|---|---|---|---|
| `PUT /api/drives/{driveId}/upload` | `DocumentsEndpoints.cs:37` | `src/dataverse/webresources/spaarke_documents/DocumentOperations.js:309-318` | Unreachable today — the preceding `GET /api/containers/{id}/drive` was deleted 2026-08-25 and the code throws at `:302` |
| `DELETE /api/drives/{driveId}/items/{itemId}` | `DocumentsEndpoints.cs:98` | same file, `:578-581`, reached from `deleteFile` **and** `replaceFile` | **Reachable** if deployed — takes `driveId`/`itemId` from form attributes, so it does not depend on the deleted route |

I deliberately did not touch these. The DELETE is a **destroy** path with a live-looking caller whose deployment status I cannot verify from the repo: `DocumentOperations.js` has **no in-repo wiring** (`src/dataverse/solutions/spaarke_documents/` is an empty `.gitkeep` skeleton; no RibbonDiff references `Spaarke.Documents.*`), yet its README claims a ribbon binding. Deleting a reachable destroy path on that evidence would be the "refusal that breaks legitimate flows and gets reverted" the POML warns about. **Recommend a follow-up task** that first resolves whether `DocumentOperations.js` is deployed.

### 3. `getContainerIdForEntity` calls a route that does not exist

`xrmUploadServiceAdapter.ts:177` and `bffUploadServiceAdapter.ts:192` build `${baseUrl}/api/containers/${entityName}/${entityId}` and issue a real GET. **No such route is registered anywhere.** Both adapters are live-wired (`useWizardPageBootstrap.ts:138`, `CreateMatterWizard/src/main.tsx:52`, `CreateProjectWizard/src/main.tsx:52`, `external-spa/src/pages/DocumentUploadPage.tsx:187`), but nothing calls `getContainerIdForEntity` — only JSDoc, the interface, and a jest mock reference it. So it is dead-but-wired code that would 404 on first use. **Relevant to task 076**, which routes container-resolution call sites: this is a call site that must be pointed at the real resolver, not merely left.

Related: the comment at `DocumentsEndpoints.cs:25` claims *"the upload adapters call `/api/containers/{entityName}/{entityId}`, a different route in UploadEndpoints"*. Half stale — the adapters do build that URL, but there is **no such route in UploadEndpoints** (and now no UploadEndpoints at all).

### 4. Still-unowned container-keyed read

`GET /api/v1/containers/{containerId}/documents` carries a Pending/`UNOWNED` waiver suggesting it *"fold into task 073, which already owns the container-keyed surface."* I left it: this task's goal is **writes**, and that route is a collection **read** whose correct control is result trimming (Wave 3 / `AccessibleRecordSetService`), not a per-resource gate. Folding it in would have blurred the task. **Needs an owner.**

### 5. ~45 stale doc references still advertise the deleted route

`PUT /api/containers/{id}/files/{path}` is still documented as *the* upload endpoint in, among others, `src/server/api/Sprk.Bff.Api/docs/SPE.BFF.API-TECHNICAL-OVERVIEW.md:73,689,695`, `src/client/shared/Spaarke.SdapClient/SDAP-CLIENT-V2-PACKAGE-OVERVIEW.md:140`, `SDAP-CLIENT-V2-FUTURE-PROJECT-BRIEFING.md:438`, `docs/architecture/sdap-overview.md:129`, `docs/guides/HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md:445`. Two scripts print it as operator guidance (`scripts/Create-NewContainerType.ps1:202`, `scripts/Register-BffApiWithContainerType.ps1:115` — `Write-Host` only, no request).

This is the **most likely re-introduction vector** and is called out in the regression test's *"what would break if this file were deleted"* block. Left for `doc-drift-audit` rather than expanded into a 45-file docs sweep here.

### 6. The upload notification went away with the route

The deleted `PUT` handler raised the *"New document uploaded"* notification via `NotificationService`. Since the route had zero callers, **no notification any user actually received was lost**. If the OBO upload path should raise one, that is a deliberate new behaviour to add there — recorded in the header of `InlineNotificationIntegrationPointsTests.cs`.

---

## 7. Placement Justification (root CLAUDE.md §10) + Component Justification (§11)

**Net effect on the BFF is subtraction.** No new endpoint, service, DI registration, package, or background work. Three routes and one file removed; one route-mapping call removed.

- **Existing** — nothing overlaps, because nothing was added.
- **Extension** — n/a; the supported paths already exist (`/api/obo/**` for user-context; the in-process `SpeFileStore` facade for app-only, used by `Workers/Office/UploadFinalizationWorker`, `Services/Email/EmailAttachmentProcessor`, `Services/Communication/*`, `Services/Ai/WorkingDocumentService`).
- **Cost-of-doing-nothing** — the concrete failure: any caller holding `canwritefiles` could write arbitrary content into any drive the MI can address, and mint a bearer-free pre-authenticated write credential into any container, with no per-resource check and no SPE-side defence.
- **No `OperationAccessPolicy` key was needed.** Retirement removes the gate rather than adding one, so no new operation string, and no risk of the A-3/A-20 unregistered-key 403.
- **No SPE container permission granted to any user.** `GrantMembershipAsync` stays at zero callers; broker-only intact.

---

## 8. Verification summary

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | ✅ 0 warnings, 0 errors (explicit build before every test run) |
| `dotnet build tests/unit/Sprk.Bff.Api.Tests/` | ✅ 0 warnings, 0 errors |
| Full suite | ✅ **11,179 passed / 0 failed / 79 skipped** (baseline 11,172 / 0 / 82) |
| Test delta | **+7** new (mine), **−3** removed (all previously `Skip`'d) — exact, no hidden changes |
| Perturbation | ✅ 5 red / 2 controls green, then reverted and re-verified green |
| ArchTests | 14 failed — **9 = master's known baseline**, **+5 = Task 074 guard**, all remediated by §6.1 |
| Publish size | ✅ **45.08 MB** compressed incl. PDBs — **Δ 0.00 MB** vs baseline; ceiling 60 |
| CVE (`--vulnerable --include-transitive`) | ✅ no vulnerable packages, any severity |

---

## 9. Residual risk, stated plainly

1. **An out-of-repo caller may exist.** The sweep covers this repository. A deployed Dataverse web resource, a Power Automate flow, or an external integration could call `PUT /api/containers/{id}/files/{*path}` — the ~45 doc references in §6.5 are exactly how someone would have learned of it. Such a caller now gets **404**.
   *Why I accept it:* any such caller is itself the vulnerability — it writes as the managed identity into a caller-named drive with no per-resource check. Under broker-only that must stop, and ADR-003 as cited by this task is explicit: *"Never fall through to 'upload anyway'."* A 404 is a deny. Migration target is `/api/obo/containers/{id}/files/{*path}`.

2. **The two `Api/DocumentsEndpoints.cs` waivers remain open** (§6.2), one of them a **reachable destroy path**. This task did not narrow that hole and did not claim to.

3. **Three dead waivers will linger** until the main session deletes them (§6.1 edit 3), because no test catches a waiver whose route was deleted rather than gated. Until then the Wave 1 work list overstates remaining work on this surface.

4. **`ExpectedEndpointFileCount` must go to 110 in the same PR**, or CI stays red on the census. This is the one item that will block a merge.

5. **The route deletion is not a substitute for tasks 075/076.** The OBO twin `PUT /api/obo/containers/{id}/files/{*path}` is still ungated and still waived to 073/075/076. What I removed is the app-only path with no SPE-side defence; the user-context path retains SPE's own check (which, under broker-only, no user satisfies — hence "latent", per the waiver). **The record-aware gate is still required.**
