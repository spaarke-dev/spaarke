# UAC-r2 merge report + findings in THIS project's code — 2026-09-30

> From the live `unified-access-control-r2` session, after it merged master into its branch (`ee5b82147`). Their
> findings came from a 197-agent audit, each confirmed by 3 skeptics; **finding (a) was re-verified here from
> source** before recording (below). The others are recorded as reported and routed to the task that owns them.

## 1. The #1029 merge kept OUR fixes wherever we overlapped (owner-approved)

| Surface | Kept | Dropped |
|---|---|---|
| `/office/search/entities` | our **062** (impersonation) | their 126 (OBO) |
| `POST /office/todo` | our **064** `TodoSourceAccessFilter` (4 ids gated + the CoreAncestorResolver harvest) | their 128 |
| synthetic job + `JobOwnershipFilter` fail-open | our **067** | — |

`OfficeService.cs` and `JobOwnershipFilter.cs` are **our versions byte-for-byte** on their branch, except one comment
word. Comment-only edits they made in our files (re-applying their task-121 BU rename): `OfficeService.cs:464`,
`RecordOwnershipResolver.cs:165`/`:170`, `OfficeSaveNoTargetContainerContractTests.cs:219` ("Secure Projects" →
"Secure Record" as a BU name; "Secure Project" as a feature name untouched).

## 2. ⚠️ RETRACTION — the six OfficeService.cs source-scan dependencies are GONE

They deleted `OfficeEntitySearchSecurityTrimmingTests` (it asserted their superseded search). **Task 059 may move
`QuerySearchEntityAsync` freely.** Still binding: in `CommunicationsEndpoints.cs` (their task-127 code), **no code
line may contain `entityService` or `IGenericEntityService`** (`///` doc lines exempt).

## 3. Their route census now GOVERNS `Api/Office/*` — and a standing rule for us

`RouteAuthorizationGuardTests` never measured `Api/Office/*` on master; it does now. At the merge it caught:
- `TodoSourceAccessFilter` + `QuickCreateSourceAccessFilter` were invisible — Rule A's `FilterMarker` requires
  **"Authorization" in the filter's name**. Credited in `ExplicitlyCreditedFilterTypeNames`, pinned in the
  positive-control test.
- `GET /search/matter-types` had no gate and no waiver → given a **Permanent reference-data waiver**.
- The old `/quickcreate` Permanent waiver deleted (its "nothing to authorize" premise went stale once our filter
  gated the source read).

🔴 **STANDING RULE**: any new Office-route filter whose name lacks "Authorization" must be added to
`ExplicitlyCreditedFilterTypeNames`, or Rule A reports the route ungated.
🔴 **058 must still delete the four Pending waivers** (#1023 ×2 `/office/search/documents`, `/office/recent`;
#1024 ×2 `/office/share/links`, `/office/share/attach`) in the same change, or `NoWaiverIsStale` goes red.

## 4. Findings in our code

### 🔴 (a) FUNCTIONAL — job status 404s for the real owner whenever the in-memory entry is gone → **task 060**

**Re-verified here from source (2026-09-30):**

| Fact | Evidence |
|---|---|
| `IProcessingJobService.GetProcessingJobAsync` returns `Task<object?>` | `Spaarke.Dataverse/IProcessingJobService.cs:11` |
| …built as an **anonymous type** | `DataverseServiceClientImpl.cs:1867` — `return new { Id = …, Status = …, … }` |
| Anonymous types are compiled `internal`; `Spaarke.Dataverse` grants `InternalsVisibleTo` **only** `Sprk.Bff.Api.Tests` | `Spaarke.Dataverse.csproj:42` |
| The consumer binds `dynamic` across that boundary | `OfficeService.cs:1701` `dynamic dvJob = processingJob;` · `:1705` `(int?)dvJob.Status` |
| The failure is swallowed | `OfficeService.cs:1750` `catch (Exception ex)` → warning log → "returning not found" |

So from `Sprk.Bff.Api`, `dvJob.Status` throws `RuntimeBinderException` (the runtime binder honours accessibility),
and the Dataverse fallback **never works**. After any restart, or on a second instance, every job-status poll 404s
— the owner's included. Fails closed (not a leak), but save-progress breaks on any scaled-out deployment, and
`JobOwnershipFilter.cs:172-174`'s claim that the creator is "mapped back on the fallback read" is false in practice.

🔴 **Why no test caught it: the ONLY assembly that can see those internals is `Sprk.Bff.Api.Tests`.** Every test of
this path runs where the `dynamic` access succeeds; production runs where it cannot. The test environment masks
the defect by construction — a contract test "through the mock shape" would pass too.

Same `dynamic` pattern: `OfficeDocumentPersistence.cs:897-931`.
**Fix (060)**: return a PUBLIC typed DTO from `Spaarke.Dataverse`, delete the `dynamic`, and test the fallback read
through the real type. This concretely extends 060, whose premise (job status in a `private static` dictionary does
not survive restart) was already right — the fallback meant to cover it is itself broken.

### (b) `JobOwnershipFilter` — comments disagree with the code → **task 060** (same area)
- `:182-185` says the two reason codes tell the caller nothing extra, but `:219` writes `reasonCode` into the
  caller-visible 403 body (distinguishing `ownership_mismatch` from `ownership_unproven`). Low impact.
- Class doc `:42-45` names `sprk_createdby` — the column is `sprk_initiatedby` — and lists a same-tenant access rule
  the fail-closed code does not have.

### (c) `RecordOwnershipResolver` — a stale comment and a contract that contradicts its sibling → **task 080**
- `:250` says "the caller falls back to the acting user" — no longer true; `21969129b` made the caller REFUSE.
- `:270` uses `TopCount = 1` + `FirstOrDefault()` for the oid → systemuser → BU lookup, while
  `RecordContainerResolver` uses `TopCount = 2` and **refuses on ambiguity** ("asking for one would silently pick a
  winner"). The same fact under two contradictory contracts — 080 should adopt the refusing one.

### (d) `TodoSourceAccessFilter.cs:116-121` — conclusion right, premise wrong → **task 079** (record integrity)
It says `EntityAccessFilter` refuses `sprk_todo`, but `EntitySetByType` contains `sprk_todo` and `todo`
(`EntityAccessFilter.cs:139-147`).

## 5. Open risk on 062 — `prvActOnBehalfOfAnotherUser` on the BFF app user is still unverified live

Without it, the impersonated entity picker fails closed. UAC-r2 recorded it as an amendment on their task 036,
which depends on the same privilege. A live read of the BFF application user's roles would close it.
**Still open after the #1029 merge** (UAC-r2 restated it 2026-09-30, §6).

## 6. After #1029 merged — 2026-09-30 (UAC-r2 cross-session message)

**#1029 merged as `2682e8225`; merged into this branch as `26acc00e2`** — no conflicts. Build 0 warnings,
ArchTests 337/337, Office + access-control unit/contract filters 480/0 and 295/0.

- **Our work won the reconciliation.** `OfficeService.cs` and `JobOwnershipFilter.cs` were taken from our
  side wholesale. Our 062 / 064 / 067 superseded their 126 / 128 / 120; their overlapping filter branch and its
  tests were removed. Master's only later edits to our files: `OfficeEndpoints.cs` `/office/save-debug` now
  logs the body LENGTH only (their task 120, #1015), and one comment rename in `OfficeService.cs`.
- **Route census.** `RouteAuthorizationGuardTests` credits `TodoSourceAccessFilter` and
  `QuickCreateSourceAccessFilter` by name. `GET /api/office/search/matter-types` has a **Permanent** waiver;
  the `/quickcreate` waiver is gone. A new Office route with a new filter type must be on the credited list or
  carry a waiver, or ArchTests fail (restates §3).
- **`IDataverseUserClient`** now lives in `Sprk.Bff.Api.Infrastructure.Dataverse` and is registered
  unconditionally in `AddSpaarkeCore` (task 080 already assumes this).
- **BU-name literal renamed** `"Secure Projects"` → `"Secure Record"` in `OfficeService.cs`,
  `RecordOwnershipResolver.cs` and two Office save contract tests (their task 121). Any code 080 writes that
  names that BU must use the new literal.

### 🔔 (e) NEW — the picker trims by **Read**, the save demands **AppendTo** → owner routing needed

062's impersonated entity search returns every record the user can **Read**. The save authorizes the target
by **AppendTo**. A **View-Only** access grant (Read without AppendTo) therefore lets a user pick a record and
then fail the save. Granting AppendTo on the role does not close it: the gap is per-record access, not the
role. Under the owner's access model (team-owned records, BU + children depth, no sharing), the realistic
source is a **secure record** reached through a UAC view-only grant.

Fails closed (no write happens), so this is a correctness/UX defect, not a leak. The long-term fix is to make
**"pickable" equal "savable"**: the picker either hides records without AppendTo, or shows them disabled with
the reason. Which of the two is an owner call (it decides whether a view-only user can see the record exists
in the pane). Filed as **ISS-010 [#1037](https://github.com/spaarke-dev/spaarke/issues/1037)**; not yet assigned
to a task.
