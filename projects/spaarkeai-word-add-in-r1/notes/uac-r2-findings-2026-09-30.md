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

## 7. UAC-r2's reply to task 080's results (2026-09-30)

- **#1038 blast radius.** UAC-r2 checked all six `ResolveForRecordAsync` callers on master:
  - `ExternalProjectDataEndpoints` (literal `sprk_project`), `ComposeService` (constant `sprk_matter`) and
    `CommunicationContainerResolver` (names come from Dataverse lookups) are safe.
  - The two `OBOEndpoints` record-keyed routes take the name from the route. An alias there fails **closed**
    (409, with a misleading message).
  - **`OfficeService` is the only caller that sends a secure save to the shared container.** It is safe only once
    this branch lands.
- **Dev data.** There is 1 secure project, and it has its own container. **0** documents are linked to any secure
  project, matter or work assignment, so **no Office content reached the shared container in dev.**
- ⚠️ **`spaarke-bff-dev` now runs master `2682e8225`** (UAC-r2's deploy, 2026-09-30). **It still carries #1038 until
  this branch merges.** That is a reason not to leave the branch unmerged for long.
- **Resolver hardening.** UAC-r2 is proposing to their owner that `ResolveForRecordAsync` normalize names through
  `DocumentAssociationMap` and **refuse** a name that is not a real entity logical name. Today an unknown name reads
  as "not securable", which fails open. This is sequenced after our merge. (The map is on master; the
  `ToLogicalName` method it needs is ours.)
- **The membership event's owner semantics (080 F7)** are theirs. They are raising it with their owner; **leave it
  as-is here.**
- **#1037 hide-vs-disable** goes to their owner along with their other decisions. #1034 and #1010 are noted.

## 8. The Secure Record Owner child-privilege gap is OURS — task 082 (2026-09-30)

Each project's notes said the other owned it. Ours (080 §6.12 item 3) said "UAC-r2's C10 adds the child-table
privileges". Theirs (`session27-owner-decisions-and-research.md`) said "fails closed until UAC C10 lands". C10 is the
Secure team's identity plus re-owning documents at provisioning, so the gap had no owner. **The owner assigned it to
this project:** task **082**, ISS-013, [#1046](https://github.com/spaarke-dev/spaarke/issues/1046).

Live role on 2026-09-30 (read-only, 36 privileges):

| Table | Live privileges |
|---|---|
| `sprk_todo` | **none** |
| `sprk_document` | all 8 |
| `sprk_communication` / `sprk_event` / `sprk_memo` | none |
| project / matter / work assignment | all 8 (**drift** from the guide's Read-only design) |
| SharePoint four | Global |

UAC-r2 has been told. The guide (`SECURE-PROJECT-ENVIRONMENT-SETUP.md`) and the NFR-05 census are theirs; 082
coordinates its edits to both.

## 9. UAC-r2's reply, and owner decisions relayed (2026-09-30)

**Task 082 scope (agreed):**
- **Grant on the existing `Secure Record Owner` role.** Their C10 part 1 (task 144, being authored) moves secure
  ownership from the Secure BU's DEFAULT team to a NAMED non-default team in that BU. The role stays, assigned to the
  named team.
- **The drift has no known origin.** UAC-r2 never added the Create/Write/Delete/Assign/Share/Append privileges or the
  SharePoint four. Their design specified Read only (their design.md:301), and their only change to the role was the
  2026-09-29 rename (task 121). Nothing in UAC-r2 depends on the drift, and a memberless owning team needs only Read.
  Removal is an owner call.
- **Guide:** we edit the privilege rows (§5.1 table + child-entities row, §5.3, §5.4, §7). Their tasks 144 (named
  team) and 150 (field-level security on `sprk_issecure`) edit the same guide after our merge and rebase onto it.
- **ONE codified child-table set that both projects extend.** Their C10 part 2 (task 146) re-owns every user- or
  team-owned child of a secure record (their design.md §5.1d: agreement, analysis, billingevent, budget,
  communication, communicationthread, document, event, invoice, kpiassessment, memo, reportcard, servicerequest,
  spendsignal, spendsnapshot, todo, workassignment, plus email). 146 adds each table it re-owns.
- **NFR-05 census (`SecureBuRoleDepthAssertion*`) is THEIRS.** Their task 144 rewrites clause 2 in that file. They add
  the "role lacks Read on a codified table" clause, reading our file. **082 does not edit that file.**

**Owner decisions:**
- **#1037:** option A, show the record disabled with the reason. The picker is ours; no task exists yet (ISS-010).
- **#1044:** the "for person" route, using the EXISTING `sprk_todo.sprk_assignedto` (a contact lookup, verified live)
  plus Created By, with no new column. Only 1 of 8 active dev systemusers has `sprk_primarycontact`.

**Finding we sent back: Created By cannot carry the person for BFF-created To Dos.**
- Those creates are app-only. Verified live: `createdby` = `# mi-bff-api-dev`, and `createdonbehalfby` is empty.
- Our Office writer sets `sprk_assignedto` only for an explicit assignee (`OfficeService.cs:2875`).

**Agreed split:**
- **Our task 083:** default `sprk_assignedto` to the caller's contact, Office path only.
- **Their task 141:** the user↔contact link. They will send us its contract.
- **Their task 152:** the briefing matches Assigned To = the user's contact. Created By counts only when
  `createdby` is a HUMAN systemuser (`systemuser.applicationid` is null). The server generators (TodoGenerationService,
  TaskActionCore and the other #1034 writers) set Assigned To themselves.
- `TaskActionCore` is AI playbook code (`Services/Ai/Nodes/ActionCore`), so it stays with 152.
- Both projects rejected impersonated creates, because they would widen roles.

## 10. Owner answers, 2026-10-01, relayed to UAC-r2 and acknowledged

| Item | Owner's words | Outcome |
|---|---|---|
| The 32 drift privileges on `Secure Record Owner` (082 §4; their F1) | *"yes can remove them if not needed"* | **Done live.** 40 → 8, proven not needed by probes (082 note §4.1). UAC-r2 wrote into their 145: after any `-Apply`, re-run the §5.4 strip, then `-Verify` |
| The hotmail `#EXT#` guest reaching the Secure BU (their F11) | *"this is in dev and we'll change this"* | The owner changes the account. UAC-r2 grants **no census exception**: clause 1 must pass on its own afterwards |
| The trusted-tenant list (their I1) | *"yes proceed"* | Their owner had already accepted option (b) directly (a new explicit per-deployment list; empty = deny). 141 proceeds on it. **141 is not in their current batch** (130/131/134/151); they will send its link contract when it is authored as code, so **083 stays blocked** |
| #1037, disabled with the reason | *"yes write the task"* | Authored as **task 084** |

## 11. Owner decision 2026-10-02 on #1081 — ✅ DELIVERED by the owner to UAC-r2 (2026-10-02)

> Two cross-session sends expired unapproved; the owner posted it to the UAC-r2 project conversation directly.

A cross-session message to the UAC-r2 session (2026-10-02) was **held for its user's approval and expired
unapproved**, so UAC-r2 has not received it. The content is public on
[#1081](https://github.com/spaarke-dev/spaarke/issues/1081#issuecomment-5953351766). Summary to relay:

- The owner assigned **Spaarke Basic User** to the dev root team "Spaarke"; verified live (probe rows on six tables,
  and a real unfiled save through the deployed BFF owned by it). #1081 closed.
- Owner's ruling: users in the root BU are a **dev data artifact**. In production, users sit in the customer's child
  BU and the BFF's application user is placed in the customer BU. Nothing is codified for the root team.
- For their census: Basic User reads at Deep depth, which at the root is the whole org (Secure Record included) for
  every root-team member (171 in dev, mostly application users). Per the ruling, no production user is affected.
- Observed: the "Spaarke Demo" BU's default team holds **System Administrator**.
- Master `5e39f2bea` (incl. task 080's team ownership) is deployed to dev.

**Next**: relay when the UAC-r2 session can accept it, or the owner relays it.
