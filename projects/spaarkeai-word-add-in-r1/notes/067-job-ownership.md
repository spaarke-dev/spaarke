# Task 067 — F5: `JobOwnershipFilter` failed OPEN, the creator was never persisted, and a production path served a fabricated job

> **Date**: 2026-09-21 · **Project**: `spaarkeai-word-add-in-r1` · **Rigor**: FULL · opus @ xhigh
> **Finding**: Fable review F5 (security reviewer) + architecture reviewer finding 1 — reached independently
> from two directions, so **confirmed, not plausible**.

---

## 1. What was wrong — three defects, one consequence

| # | Defect | Where |
|---|---|---|
| **A** | Ownership guard **fails open** on an unrecorded owner | `JobOwnershipFilter.cs:157` |
| **B** | The **same fail-open one layer down**, in the service | `OfficeService.GetJobStatusAsync` |
| **C** | Creator **never persisted**, so the durable row could never answer "whose job is this?" | `OfficeService` job create + `DataverseServiceClientImpl.GetProcessingJobAsync` |
| **D** | Hard-coded test job served **fabricated status in production** | `OfficeService.cs:1627-1657` |

The guard read:

```csharp
if (!string.IsNullOrEmpty(jobStatus.CreatedBy) && <mismatch>) { refuse; }
```

An **empty** `CreatedBy` satisfies it — and empty is exactly what the Dataverse fallback produced, because
(C) meant no creator was ever written and the fallback mapper set none. So while the in-memory entry lived,
ownership was enforced; once it was evicted — a restart, or simply a second instance taking the request —
**any authenticated caller could poll any job GUID**. ADR-017: *"MUST NOT expose status without
authorization checks."* A check that skips itself when its input is absent does not satisfy that. It is
worse than no check, because it reads as protection.

**Defect B matters independently.** The POML named only the filter. The service-level check
(`userId is not null && job.CreatedBy is not null && job.CreatedBy != userId`) had the identical shape, so
fixing only the filter would have left a second fail-open on the handler path. Both are now closed.

---

## 2. REPRODUCE-FIRST — verbatim

Tests added first, against the shipped code, `dotnet test --filter OfficeEndpointAuthorizationContractTests`:

```
Failed Get_OfficeJobStatus_WhenJobRecordsNoOwner_IsRefused(unownedValue: null) [1 s]
  Expected response.StatusCode not to be HttpStatusCode.OK {value: 200} because a job whose owner was
  never recorded must not be disclosed to an arbitrary authenticated caller (ADR-017), but it is.

Failed Get_OfficeJobStatus_WhenJobRecordsNoOwner_IsRefused(unownedValue: "") [1 s]
  Expected response.StatusCode not to be HttpStatusCode.OK {value: 200} ... but it is.

Failed Get_OfficeJobStatus_ForRetiredTestJobId_IsNotServedFabricatedStatus [19 ms]
  Expected response.StatusCode not to be HttpStatusCode.OK {value: 200} because the hard-coded test job
  must not serve fabricated status from a production code path, but it is.

Failed!  - Failed: 3, Passed: 5, Skipped: 0, Total: 8
```

**A nuance the finding did not state**: the whitespace case `"   "` **already refused**. `IsNullOrEmpty("   ")`
is false, so it fell through to the mismatch comparison and was correctly denied. Only `null` and `""` failed
open. The fix now uses `IsNullOrWhiteSpace`, so all three are handled by one rule rather than two by accident.

---

## 3. The schema decision — **task 060 consumes this**

**No schema change. No new column. No solution edit.**

`sprk_processingjob.sprk_initiatedby` (LOOKUP → `systemuser`) **already exists** — verified live against dev
Dataverse via MCP `describe` — and `Models/ProcessingJob.cs:62` plus `CreateProcessingJobRequest.InitiatedBy`
**already declare it**. It was simply never written. This task populates a field the schema and the model
already have; it does not add one. **Escalation trigger 1 therefore did not fire.**

### One identity namespace: the Entra OID

`JobStatusResponse.CreatedBy` stays the **OID** everywhere, because that is what `OfficeAuthFilter.ExtractUserId`
produces and what both comparison sites already use.

| Stage | Mechanism |
|---|---|
| **Create** | Resolve caller OID → `systemuserid` via the existing `ICallerSystemUserResolver`; write `sprk_initiatedby`. |
| **Fallback read** | `QueryExpression` with a **LEFT OUTER join** `sprk_processingjob` → `systemuser`, selecting `azureactivedirectoryobjectid`. Returns the **OID** — in the same round trip. |
| **Compare** | Unchanged semantics on `CreatedBy`; both sides are OIDs *by construction*, not by coincidence. |
| **Legacy rows** | `sprk_initiatedby` null → `CreatedBy` null → **refused**. |

GUIDs are canonicalized bare-lowercase (`ToString("D")`) per **ADR-044**. Note the POML cited ADR-044 as
"dataverse-query-safety"; its actual title is **GUID canonicalization**, which is more on point here, not less.

### Rejected alternatives

- **A new `sprk_createdbyoid` string column** — needs a solution change, and `sprk_initiatedby` is the field
  that already *means* this. Adding a second creator field would be the §11 anti-pattern.
- **The OID inside `sprk_payload`** — not queryable. Task 060 needs to query jobs by owner; a value buried in
  a JSON blob cannot serve that.
- **Reverse-resolving systemuserid → OID in a second call** — correct but costs a round trip on a polled path.
  The join gets it for free.

### Why the join is LEFT OUTER

So rows with no initiator still return, with a null OID, and the **caller** refuses them. The reader reports
honestly; the authorization decision stays in the authorization layer rather than being smuggled into a query.

---

## 4. Fail-closed, and why it is safe rather than merely strict

Persisting the creator (C) is what makes closing A and B safe. Without it, failing closed would refuse
**every** job after a restart — a total polling outage. With it, refusal is confined to the legacy rows.

**Degradation direction is closed, not open.** `ICallerSystemUserResolver` is an optional ctor dependency; if
it were absent the creator is simply not recorded, and an unrecorded creator is *refused*. There is no path
where a missing dependency widens access.

The resolution deliberately sits **outside** the create's `try/catch`. That catch treats failure as "Dataverse
unavailable" and silently downgrades the job to in-memory-only — so letting a resolver hiccup reach it would
quietly cost **durability** in exchange for an authorization field.

### Two reason codes, one status

| Reason code | Meaning |
|---|---|
| `sdap.office.job.ownership_mismatch` | Caller is not the recorded owner. |
| `sdap.office.job.ownership_unproven` | No owner recorded (legacy row). |

Both emit the **same 403 with the same detail text**. The split is for operators — "these rows predate the
column, and the count drains as jobs expire" is a different signal from "someone is probing other people's
jobs" — and tells the caller nothing extra.

---

## 5. Seeded BOTH directions

| Seed | Result |
|---|---|
| Restore the fail-open guard | **RED 2 / 6** — exactly the two fail-open cases; the backdoor test still passed |
| Restore the deleted backdoor | **RED 1 / 7** — and it returned **403, not 200** |
| Both restored to the fix | **GREEN 11 / 0** (incl. `Issue975_OfficeJobStream` SSE tests) |

**The second seed is worth reading twice.** With the backdoor restored, the endpoint answered **403** rather
than the fabricated 200 — because the now-fail-closed filter refuses it (the filter resolves the job through
the no-caller overload, so `CreatedBy` is null). The two fixes are **independently effective**. The test still
binds to the deletion specifically, because it asserts **404** (the row is absent) rather than merely
"not 200". Had it asserted only "not 200", it would have passed with the backdoor still in the codebase.

---

## 6. Criteria status

| # | Criterion | Status |
|---|---|---|
| 1 | Reproduce-first, verbatim | ✅ §2 |
| 2 | Creator field exists + written at create | ✅ §3 — see wording note below |
| 3 | Filter fails closed | ✅ |
| 4 | Dataverse fallback maps the creator | ✅ |
| 5 | Legacy-row rule stated + tested | ✅ refuse |
| 6 | Test job deleted, grep proves no dependents | ✅ §7 |
| 7 | Seed both directions | ✅ §5 |
| 8 | Pane polling + SSE still work | ✅ suite; ⚠️ not verified against a live host |
| 9 | **061's guard no longer flags the job routes** | ❌ **NOT MET — unreachable** |
| 10 | Full suite / ArchTests / publish / CVE | see §8 |

**Criterion 2 wording.** It says "a creator-**OID** field". The field stores a **systemuserid**, because the
pre-existing Dataverse field is a lookup. The *creator OID* is what the read returns, recovered by join. The
goal — the creator is persisted and recoverable — is met; the literal wording is not, and that is stated
rather than glossed.

**Criterion 9 is not achievable in this task.** It depends on task **061**, which is DEFERRED on a
census-ledger conflict with `unified-access-control-r2` PR #950. `RouteAuthorizationGuardTests.GovernedFiles`
contains no `Api/Office/*` entry, so nothing flags these routes today and nothing would notice a filter being
detached tomorrow. Same unmet criterion as tasks 062, 063 and 064, for the same reason. **It closes when 061
closes, not before.**

---

## 7. Grep evidence — nothing depended on the test job

`grep -rn "00000000-0000-0000-0000-000000000001" src/` returns **~70 hits, and none of them is a job id.**
That GUID is a conventional "first test record" placeholder used all over the repo, so the honest claim is
*"zero references as a JOB id"*, not *"zero references"* — the distinction matters, because a count alone
would look alarming and prove nothing.

The hits break down as: matter / session / user / layout / playbook / `configId` / `appid` / `baseviewid`
placeholders in tests and doc comments, a Cosmos built-in role id, and `SystemWorkspaceLayouts.cs:21` (the
Corporate Workspace layout id). The two worth naming explicitly because they sit closest to this surface:

- `src/client/office-addins/outlook/taskpane/index.tsx:69` — `regardingRecordId`, **not** a job id.
- `src/server/api/Sprk.Bff.Api/Services/Office/OfficeService.cs:1685` — the tombstone comment added by this
  task, recording that the backdoor was deleted.

**No client or spec polled the job.** The pane obtains job ids from the save response; no shipped code path
could have produced this one.

### 🔴 But two TESTS did depend on it — and my first grep missed them

I ran the grep against `src/` only and wrote "no fixture referenced the job". **That was wrong.** The full
suite found two failures in `OfficeEndpointsContractTests`:

```
Get_OfficeJobStatus_WithKnownJob_Returns200   [FAIL]
Get_OfficeJobStream_ReturnsSSEContentType     [FAIL]
Failed: 2, Passed: 12413, Skipped: 56, Total: 12471
```

Both fetched `00000000-…-0001` and asserted success — so **the test suite pinned the backdoor as the
contract**. That is the more interesting finding: the fabricated job was not merely unused leftovers, it was
*load-bearing for the tests that were supposed to prove job status worked*. `Get_OfficeJobStream_ReturnsSSEContentType`
in particular would have kept passing if the SSE route had broken for every real job, because the only job it
ever streamed was invented.

Both were rewritten to use a job that genuinely exists **and is owned by the caller**, via the existing
`OfficeJobOwnershipTestWebAppFactory`. The coverage intent (200 for a known job; `text/event-stream`) is
preserved and now actually binds to the shipped surface.

One further trap while rewriting the SSE test: a loose mock yields `null` for `IAsyncEnumerable`, and
`await foreach` over null throws — surfacing as a **500** that reads like an authorization failure. Stubbed
with an empty stream, since the test asserts the response *shape*, not the frames.

**Lesson for the criterion's wording**: "a grep proves nothing depends on it" is only as good as the grep's
scope. `src/` is not the codebase.

---

## 8. Gates

| Gate | Result |
|---|---|
| Build (`Sprk.Bff.Api`) | **0 warnings / 0 errors** |
| ArchTests | **191 / 191** |
| CVE (`--vulnerable --include-transitive`) | **no vulnerable packages** |
| Publish size | branch **45.54 MB** vs fresh-master **45.46 MB** = **+0.08 MB** |
| Full BFF suite | **12,415 passed / 0 failed / 56 skipped / 12,471 total** (9 m 48 s) |

**Count reconciled, not merely asserted:**

| | Passed |
|---|---|
| After tasks 063 + 064 | 12,403 |
| + task 065 (`OfficeSaveNoTargetContainerContractTests.cs` + 2 modified) | +8 |
| + task 067 (1 `[Fact]` + 1 `[Theory]` × 3 `[InlineData]`) | **+4** |
| **= this run** | **12,415** ✅ |

The 56 skips are the pre-existing set, unchanged. While reconciling I first concluded "065 added no tests" —
wrong: `git show --stat` elides long path prefixes with `...`, so a `grep "tests/"` over its output matches
nothing even though three test files changed. Worth remembering, because that mistake silently turns a clean
reconciliation into an unexplained +8.

**Publish-size honesty (CLAUDE.md §10).** `origin/master` is still at **`99cdfe2ea`** — the exact commit this
project's tasks 062/063/064 published fresh. The hazard §10 warns about is the baseline *ageing because master
moved*; it has not moved, so that side of the comparison is a fresh-master number for the same commit, not a
recorded figure being trusted blind. Both sides via PowerShell `Compress-Archive -CompressionLevel Optimal`,
incl. PDBs — the method `scripts/Deploy-BffApi.ps1` uses.

**The +0.08 MB is the BRANCH's cumulative delta, not this task's.** Tasks 062–064 measured the same 45.54 MB
total. **Task 067's own marginal contribution is ~0.00 MB**: no new package reference, ~150 lines of C#.
Attributing the whole +0.08 to this task would be the exact misattribution §10 exists to prevent.

---

## 8b. 🔴 PRE-DEPLOY REQUIREMENT — the new Dataverse query is NOT verified against real Dataverse

`push-to-github` Step 1.7 (real-Dataverse smoke) **fires on this change** and the honest answer is
**"exercised only against the mocked test host."** `GetProcessingJobAsync` was rewritten from a plain
`RetrieveAsync` into a `QueryExpression` with a `LinkEntity` (`EntityAlias = "initiator"`) plus an
`AliasedValue` unwrap. That is exactly the shape a mock cannot validate — the R4 `sprk_contact`-vs-OOB-`contact`
regression is the precedent: a harness passing proves nothing about real Dataverse's schema.

**What IS verified against real dev Dataverse (2026-09-22):**
- `sprk_processingjob.sprk_initiatedby` exists and is selectable (MCP `describe` + a live `SELECT`).
- `systemuser.azureactivedirectoryobjectid` exists and is selectable.
- The join returns **zero rows** — and that is "nothing to join", NOT a broken join: live rows exist
  (jobs dated 2026-09-18) and **`sprk_initiatedby` is null on every one of them**.

**What is NOT verified**: the SDK `QueryExpression` + `LinkEntity` + `AliasedValue` path itself. MCP uses a
different API surface, so it validates the *schema* half and not the *SDK* half. If `EntityAlias` handling or
the relationship name is wrong at runtime, the join silently yields no aliased value, `CreatedBy` stays null,
and **every post-restart poll 403s** — fail-closed, so it is safe, but it is an outage-shaped safety.

**Before deploy**: save a document as a real user, restart (or force the Dataverse fallback), and confirm the
owner can still poll the job. That single round trip exercises create → `sprk_initiatedby` → join → OID.

**Also true and worth stating**: every EXISTING job row in dev has no initiator, so under fail-closed they are
all refused. They date to 2026-09-18 and are mostly Failed/In Progress, so nothing of value is lost — but
"all pre-existing jobs now 403" is the expected behaviour, not a regression to chase.

## 9. Follow-ups for other tasks

- **Task 060** — consumes §3. The creator is now queryable (`sprk_initiatedby`), which is what a durable
  store needs to enforce ownership without the in-memory map.
- **Task 059** — this adds an **8th optional ctor dependency** to `OfficeService` (19 params → 20). That
  worsens the exact signal 059 is scoped to fix. It was the lesser evil: the alternative was changing
  `IOfficeService.SaveAsync`'s signature, which has 9 consumers and sits on the save spine that task 065 has
  already destabilised.
- **`CheckForExistingJobAsync`** (`OfficeDocumentPersistence.cs:922`) still hard-codes `CreatedBy = null`. It
  feeds the duplicate-save response, not an authorization decision, so it is **out of scope here** — but if a
  future change routes that record into the job store, it would arrive unowned and be refused. Named so it is
  not discovered by accident.
- **Residual existence oracle**: 403 (job exists, not yours) vs 404 (no such job) still discloses existence.
  Pre-existing and unchanged in kind by this task; closing it is the shape of work task 064 did for
  `/office/todo`. Not in this task's criteria, so not silently widened into it.
