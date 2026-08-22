# Current Task State — sdap-SPE-admin-app-r2

> **Last Updated**: 2026-08-21 (by `context-handoff`)
> **Recovery**: read "Quick Recovery" first. History lives in [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md).

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Task** | **none in progress** — W1 ✅ · W2 ✅ (004, 010, 040) · **W3: 012 ✅**, 011 🔄 partial, 013 🔲 |
| **Step** | Between tasks. **Next: 013** (Azure config, STANDARD, ~1h — cheapest fix in the project), then Workstream C via **029** / **030** |
| **Status** | clean tree at checkpoint; task 012 committed |
| **Next Action** | Invoke `task-execute` on `tasks/013-security-events-grant.poml`. Note its escalation trigger: **consent is an operator action** — if the executing identity cannot grant tenant admin consent, STOP, don't work around it in code. **Verify the POML's premise first** — 8 of 9 so far have been wrong, incomplete, or aimed at the wrong layer. |

### Files Modified This Session

All committed and pushed to `work/sdap-SPE-admin-app-r2` (draft PR **#811**):

| Commit | Contents |
|---|---|
| `5b3ef6194` | **Task 001** — 60 SpeAdmin error sites routed; `Redact`/`Explain`/`ExtractRequestId`/`ClientStatusFor`; client `describeApiError`; 28 tests |
| `753c9ebc1` | **Build fix** — 4 undeclared deps + 2 vite aliases; SpeAdminApp code page builds again |
| `f3747646b` | **Task 002** — 70-site `catch (ODataError)` inventory; ADR-007 fix in `BulkOperationService` |
| `aa69ce941` | **Task 003** — `SyncHealth`/`ConcernOutcome`; Dataverse-outage-looks-like-OK fixed; 9 tests |
| `356001ee7` | docs refresh |
| `44a239aab` | **Task 005** — Audit Log read **and** write paths repaired; 19 tests |
| `b6ffe09e5` | checkpoint |
| `8e3b954da` | **Task 040** — WireMock Graph fixture; **unblocked WireMock repo-wide**; 10 tests. No `src/` change |
| `b4922d9c1` | **Task 004** — Search repaired: wrong entity type + missing `region` + invalid `contentSources`; 16 tests. **Verified live** |
| `958ceef8b` | **Task 010** — OBO spike ⛔ **UNWORKABLE**; `BLOCKED.md` written; 011/012 blocked |

⚠️ **Separate repo, NOT pushed**: `c:/code_files/spaarke-prototype` has **1 unpushed commit** `a53832a`
(the `spe-admin-r2-uat` harness + shared `_infra` mock fixes) on `feature/uat-harness-framework`. Left
unpushed deliberately — pushing another repo needs the operator's say-so.

### 🔑 Task 012 — do not re-derive

**Entra directory roles are INVISIBLE to the BFF.** `SDAP-BFF-SPE-API` leaves `groupMembershipClaims`
unset, so no `wids` claim is ever emitted. Proven with a **positive control**: a real token for
`aud = api://1e40baad-…`, issued to a **confirmed member** of the tenant's SharePoint Embedded
Administrator role (`1a7d78b6-…`), carried **no `wids` at all** — while `roles` was present.

→ **Claim-absence does not mean role-absence.** Any filter check would tell genuine role holders they
lack the role. **Do not "complete" `SpeAdminAuthorizationFilter` by adding a `wids` check** — the code
says so inline, with the measurement.

The real defect was one layer down and unnamed by the POML: all four container-type ops passed a
hardcoded **500**, so a Graph **403 reached the admin as "Internal Server Error"**. Now: layer 1
(Spaarke app role, visible → filter) and layer 2 (Entra role, only Graph knows → 403-filtered catch),
each speaking only about what it can observe. Layer 2 names the role and what it enables but never
asserts the caller lacks it — 403 also covers unregistered types, consent gaps, wrong-tenant configs.

🔔 **Open operator decision (nothing depends on it)**: set `groupMembershipClaims: DirectoryRole` on
the BFF registration for *proactive* detection? Not taken unilaterally — that registration backs every
Spaarke client surface. See `notes/task-012-completion.md` §5.

`tests/integration/auth/**` was a **dead ADR-038 KEEP path** — README only, compiled by no project.
Now wired; 14 tests live there.

### Critical Context

✅ **Auth resolved — operator chose path A (BFF identity).** Container types run on
`IGraphClientFactory.ForUserAsync`, the BFF's **existing** OBO exchange. **No new `.WithClientSecret`
site** — the BFF already had four; SpeAdmin reuses one. `SpeAdminTokenProvider` is now **dead code on
this path** (it exchanges as `SDAP-PCF-CLIENT`, which exposes no `api://` URI → `AADSTS500011`).

✅ **Tenant isolation shipped** (`325511d5b`). `configId` was a bearer capability — 15 endpoints took
it with zero ownership check. Now `SpeAdminTenantScope` derives the caller's BU from the `oid` claim
(self + descendants) and `SpeAdminTenantScopeFilter` enforces it once on the `/api/spe` group, 404 not
403. ⚠️ **A config with no business unit is treated as accessible** (upgrade compatibility) — so
**every config MUST carry a BU before a shared multi-customer environment counts as isolated.**

🔴 **Outstanding docs debt**: ADR-028 **E-1** still describes a per-customer owning app that does not
exist for SpeAdmin. Amend it or the next project rebuilds on the same false premise.

Every real defect found has the **same shape**, and **none was where its POML said to look**: a lower
layer collapses a failure (or a real value) into an absent/empty result that an upper layer reads as
benign. **Verify a task's premise before implementing to it — seven for seven have now been wrong,
incomplete, or aimed at the wrong layer**, including the spec's own auth hypothesis and the §6.5 gate's.

---

## Full State

### Health at checkpoint

| Gate | Value |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 errors (7 pre-existing warnings) |
| Unit tests | **10,618 passed**, 0 failed, 97 skipped (+82 added this session) |
| ArchTests | 36/36 |
| Publish (compressed, framework-dependent linux-x64) | **43.66 MB** — under the ~44.96 MB baseline, ceiling 60 |
| New NuGet | none |
| CI | **deliberately not tracked** — operator said to disregard at this stage |

### 🔑 The recurring defect shape — three-for-three

| Task | Where the truth was lost |
|---|---|
| **003** | `LoadContainerTypeConfigsAsync` returned `Array.Empty<>()` on a Dataverse exception → indistinguishable from "none registered" → `SyncSucceeded = true` → green dashboard over a broken app |
| **005** | `SpeAuditService` swallowed every write failure → audit table silently **0 rows** for the life of the app |
| **002** | `BulkOperationService` caught raw `ODataError` (ADR-007 leak; fixed) |

**Look for this shape first in 004** — not for error swallowing in the Graph service.

### 🔑 Do not re-derive: the 70 `catch (ODataError)` sites are already correct

Two-layer design — inner `XAsync` catches **only 404** (`when`-filtered) → null/false; outer
`XForConfigAsync` translates everything else to `SpaarkeStorageException`. A 403/429/5xx is never swallowed.

An earlier task-001 note claimed *"28 of 70 swallow — those screens stay silent until 002 lands."* **Wrong**
(it never checked wrapper pairing). Corrected in [`notes/task-001-completion.md`](notes/task-001-completion.md)
and [`notes/odata-catch-inventory.md`](notes/odata-catch-inventory.md).

### Reusable mechanism — do not reinvent

| Helper | Use |
|---|---|
| `GraphErrorTranslator.ToProblemDetails(summary, errorCode, statusCode, traceId, title)` | Graph failures — code, upstream status, request id, traceId |
| `GraphErrorTranslator.ClientStatusFor(ex)` | Upstream→client status; Graph **401 → 502** so the client retry loop cannot swallow it |
| `ProblemDetailsHelper.Explain(summary, ex)` | Non-Graph failures — appends real type + message, redacted |
| `ProblemDetailsHelper.Redact(message)` | **Always** apply before putting upstream text in a payload |
| `GraphCallScope.Run(...)` / `.RunForConfig(...)` | Keeps `ODataError` inside `Infrastructure.Graph` (ADR-007 §1) |
| `SpeDashboardSyncService.DeriveHealth(concerns)` | "A failed concern can never report Healthy" |
| `SpeAuditService.MapCategory(text)` | Free text → `sprk_category` option-set int |
| `describeApiError(err, fallback)` (`speApiClient.ts`) | Client render sites — appends Graph code + request id |

> ⚠️ A summary passed to these **must not name a cause the caught exception did not establish.**

### 🔑 Task 040 done — the harness now exists, and it already earns its keep

`tests/integration/contract/SpeAdmin/GraphWireMockFixture.cs` + `README.md`. Use it for any
Graph-touching change. **Do not build a second one** — two Graph fakes already existed and were
correctly rejected as non-extendable (reasons in `notes/task-040-completion.md` §3a).

```csharp
using var graph = new GraphWireMockFixture();
graph.StubGet("/storage/fileStorage/containers", """{"value":[…]}""");
await sut.ListContainersAsync(graph.CreateGraphClient(), containerTypeId);   // real production method
graph.SelectFieldsFor("/storage/fileStorage/containers").Should().BeEquivalentTo("id", "displayName");
```

**Three facts worth not re-deriving:**

1. **WireMock was dead repo-wide, mislabeled.** Every request 500'd — WireMock.Net 1.5.45 loads
   `MimeKitLite` at runtime and the test csproj had `ExcludeAssets=all` (stripping the *runtime* asset,
   not just the compile-time collision it was added for). Now `compile`. If WireMock ever blanket-500s
   again, **check that first**. The 6 tests in `Integration/GraphApiWireMockTests.cs` sat skipped as
   *"path matching … requires configuration investigation"* — wrong, and it kept the one tool able to
   catch the §3.2 defect class dark for all of R1.
2. **The seam already existed.** 47 `SpeAdminGraphService` methods take `GraphServiceClient` as a
   parameter. The hardcoded `…/beta` is confined to the private `CreateGraphClient*` helpers.
   Escalation trigger evaluated → **did not fire**; **task 021's base-address decision is untouched**.
3. **KEEP path matters.** The POML's `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/` is not one of
   ADR-038's seven. New Graph tests go in `tests/integration/contract/SpeAdmin/`.

### 🔴 Defect handed to task 022 — do not re-derive

`SpeAdminGraphService.cs:4368` guards `deletedDateTime` with `rawDeletedAt is string`, but Kiota stores
a **`System.DateTime`** (probed against the real SDK). The guard can never be true, so **every
recycle-bin row reports a null deletion timestamp**. Found by the fixture on its first run.

Pinned as characterization tests that **must fail and be updated when 022 fixes it** — deleting one
instead would restore the silence. Same for the `StorageUsedInBytes: null` pin (task 024).

### Standing gap — UI verification

`<ui-tests>` from tasks 001 and 003 are still **NOT DONE**. The code page now *builds*, and a local harness
exists, but neither substitutes for a **deployed** app + `--chrome`.

- **Harness** (`spaarke-prototype/projects/spe-admin-r2-uat`, `npm run dev`, port varies — was **5176**)
  render-verifies task 003's four sync-health scenarios against the *real* `DashboardPage`.
- It **cannot** verify task 001's `authenticatedFetch → ApiError → describeApiError` path: the harness
  aliases `@spaarke/auth` to a mock that always returns 200, so that would test the mock, not the product.

This debt compounds through Workstream C, which is heavily UI. Worth a decision before then.

### Carry-forward

1. **🔔 Task 010 can reopen the auth decision.** §6.5 gate resolved as **path C** (comply under ADR-028 E-1),
   but two verified defects mean the owning-app OBO path cannot currently succeed as written
   (`SpeAdminTokenProvider.cs:142` audience; `:306` OBO actor). If 010 shows the shape is unworkable,
   **STOP and re-run the gate** — do not fall back to BFF-identity OBO silently. It is Opus tier / `xhigh`,
   and an `UNWORKABLE` verdict blocks 011 and everything from 020 onward.
2. **God-file serializes waves.** At most ONE task per wave may modify `SpeAdminGraphService.cs`.
3. **Task 004 is uncapped.** Search root cause not isolated; effort provisional.
4. **Live-tenant safety**: destructive tests need a dedicated throwaway container — existing containers hold
   real documents (signed NDAs, Compose drafts, matter files).
5. **A POML's premise can be wrong.** 001's `<relevant-files>` named 5 of 18 endpoint files (real scope 60
   sites, not 41). 002's premise did not hold at all. 003's held only one layer down. 005's pointed at the
   read path when the write path was equally broken. Under `mode="directional"` the `<goal>` binds.
6. **Residual ADR-007**: `BulkOperationService` still holds two `Microsoft.Graph.GraphServiceClient` locals —
   structural work for `speadmingraphservice-decomposition-r1`; recorded in the odata inventory.
7. **Dataverse MCP works** and is how 005's root cause was proven empirically. Reach for it before declaring
   something unverifiable against a live tenant.

### Session notes — key learnings

- **Two mistakes worth not repeating**: (a) `git stash push -- <path>` with nothing to stash creates no
  entry, so a following `git stash pop` pops *someone else's* stash — it dropped another project's WIP into
  this tree (reset, nothing lost); (b) pushing repeatedly cancels your own in-flight CI runs.
- **A confidently-worded wrong comment kept a bug alive for months.** `AuditLogEndpoints.cs:159` asserted
  lookup GUIDs "require single quotes"; 29 of the other 30 lookup filters in `src/` disagreed.
