# Task 002 — BFF matter/WA event routes, per-record canCreate, delete-409 ProblemDetails (FR-08, FR-09)

- **Rigor:** FULL (tags bff-api, endpoints, authorization; `.cs` changes). **Model:** Opus @ high (sub-agent).
- **Status:** completed 2026-11-11. Uncommitted — the main session verifies the build and commits after wave P1-A.
- **UAC review: uac-r2 PASS (FR-08, FR-24), 2026-11-12, after F4-1** (see "For uac-r2" below).

## What changed

| Area | Change |
|---|---|
| `ExternalProjectDataEndpoints.cs` | `POST /api/v1/external/{matters|workassignments}/{id}/events` mapped with the project route's filters (`CallerPrincipalAuthorizationFilter`, `.RequireWorkforceCeiling`). One handler `CreateEventForRoot(rootKind, …)` serves all three roots: Read gate (`RightsForRoot`), `RightsForRootWriteAsync(… EventEndpoints.CreateEventPrivilege …)`, 503 on an unanswered Dataverse question, 403 `sdap.access.deny.insufficient_rights`, the 400 checks, `ResolveChildOwnerAsync` on the root's entity, creator stamp from the principal. Parent from the route only; the DTO is unchanged. |
| same | `canCreate` for every caller on the to-do and event lists via `CanCreateUnderRootAsync` (rights + Dataverse confirmation + the create route's workforce ceiling); `CreateTodoCeiling` / `CreateEventCeiling` / `UploadCeiling` maps shared by the route and its list. Documents list `canCreate` now also applies the upload ceiling (same class of defect). |
| `ExternalMessageEndpoints.cs` | Thread `canCreate` = the POST's decision (`CanPostAsync`: ceiling `PostMessageOn`, `CanStampCreator`, `RightsFromGate`, Dataverse Create on record + `sprk_communication`), reusing the ONE gate evaluation the read already made (`EvaluateRecordGateAsync`). Dead wrapper `IsRootRecordReadableAsync` removed. |
| `WorkforceRoleCeilingGate.cs` | `CreateMatterEvent`, `CreateWorkAssignmentEvent` (sprk_event Create + root AppendTo); `AdmitsAsync(principal, privileges, required, ct)` for list-side evaluation. |
| `ExternalDataService.cs` | `CreateEventAsync(TodoRootKind, rootId, …)` / `BuildEventCreatePayload(rootKind, …)` bind via `TryGetRootBinding`. Corrected the wrong remark about ADR-024 resolver fields (see defer D-1). |
| `ExternalProjectDtos.cs` | `CanCreate` doc. |
| `DocumentOperationsEndpoints.cs` | `DocumentLocked(DocumentLockedError, correlationId)` builder used by the checkout 409 (no behaviour change) and the delete 409 (#2271 residual: now `status`, `title`, `error`, `detail`, `checkedOutBy`, `checkedOutAt`, `correlationId`). `.Produces<DocumentLockedError>(409)` kept. |

## Decisions and deviations

1. **The workforce ceiling is part of `canCreate`.** Without it a workforce contact sees `canCreate: true` and the POST answers 403 (FR-07 "no action the UI offers may answer 403"). Cost: one extra cached role read per list for workforce contacts.
2. **Documents-list `canCreate` fixed too** (`UploadCeiling`) — same defect class, found while modelling on it.
3. **Signature change, not an overload**, for `CreateEventAsync` — `RecordOwnerAssignmentCensus` parses method names; an overload would split the census.
4. **Project route equivalence.** Its first gate moved from `HasProjectAccess` to `RightsForRoot` Read; they are the same predicate and `DenyRoot` gives the same body. Status, body, reasonCode and Location unchanged (pinned by the existing project tests, all green).
5. **400 responses carry no `errorCode`**, matching the project route (FR-08 "same errorCodes").
6. **Message refactor:** the read core calls `EvaluateRecordGateAsync` directly so `canCreate` reuses the gate (no second read); ledger hops updated.
7. **Escalation trigger 1 checked:** `sprk_event` has `sprk_regardingmatter` / `sprk_regardingworkassignment` (data-model doc) — not fired. Message `canCreate` is one probe, the POST's — not fired. Size < 60 MB, no CVE — not fired.
8. **Deviation: `current-task.md` not written** — task 001's agent shares the checkout; the main session owns it.
9. **Extra test files touched** beyond the POML list (signature-only updates): `ExternalTodoScopeTests`, `SecureChildOwnershipExternalTests`, `ExternalDataServiceTests`, `EventReadPathTests`, `EventDateOnlyTests`, `EventRoutesLiveTests`; `ExternalRootEventReadTests`' "not a route" (405) test replaced by a ViewOnly-403 test now that the route exists.

## Tests

New:
- `tests/integration/contract/Api/ExternalAccess/ExternalRootEventCreateContractTests.cs` — 201 (bind, owner, Location, payload via real `BuildEventCreatePayload`); ViewOnly 403 `insufficient_rights`; outside-the-set 403 with nothing written; matter grant ≠ WA with the same id; 401; 400 ×3; 409 owner refused; Dataverse-decided 201 / 403 / 503; ceiling 403 ×4 + OK; list `canCreate` = POST (8); workforce list ceiling (8, incl. EventTable vs TodoTable); unreadable role → own ceiling denial.
- `tests/integration/regression/Issue2271_DeleteLockedDocumentProblemDetailsTests.cs` — 409 parses as ProblemDetails naming the holder, exact member set, not-checked-out → 200 deleted, no delete right → 403 with no lock disclosed.

Extended: `ExternalMessageEndpointsTests` (partner rights 6, workforce ceiling 4, no ceiling checker, Dataverse-decided over the wire 9, non-Dataverse caller asks nothing), `ExternalRecordDocumentRoutesTests` (upload ceiling), `WorkforceCeilingRouteFamilyTests` (2 rows), `DocumentDestroyAuthorizationTestFixture` (`CheckedOutLocks`), route ledger + `AuthenticationSchemeCensusTests`.

Results (2026-11-11, final source):
- `Sprk.Bff.Api.Tests` filtered (ExternalMessageEndpoints, ExternalRootEventCreate, Issue2271_, ExternalRootEventRead, ExternalRecordDocumentRoutes, WorkforceCeiling*, ExternalDataService, DocumentDestroy*, SecureChildOwnership*, ExternalTodoScope, EventReadPath, EventDateOnly): **1656 / 1656 passed**.
- `Spaarke.ArchTests` full: **2145 / 2145 passed**; route-ledger filter (RouteAuthorizationGuard, AuthenticationSchemeCensus, DeleteRoute, RecordOwnerAssignmentCensus) re-run after the ledger line-ref edit: **143 / 143 passed**.
- Full BFF unit suite not run (machine shared; R3 gotcha).

## BFF hygiene

- **Placement:** in BFF — extends existing external-access endpoints; no new service, DI registration, package or background work (NFR-05).
- **Publish size** (fresh worktrees at merge-base `56d3463c1a`, `dotnet publish -c Release`, `Compress-Archive -CompressionLevel Optimal`, 191 files each): master 41,304,760 B vs branch 41,307,219 B → **+2,459 B (~+0.002 MB)**. Worktrees `C:\wt002m`/`C:\wt002b` removed via `Remove-WorktreeSafe.ps1`.
- **CVE:** `dotnet list package --vulnerable --include-transitive` — none.

## Conflict check (hard warn — for the main session)

- Open PR #2615 (DO-NOT-MERGE spike) touches `RouteAuthorizationGuardTests.Ledger.cs`.
- Master `a7d7aad31f` (uac-r2 188) touched `ExternalDataService.cs` in another region.
- **`origin/work/ebilling-r1` (task-130 batch, unmerged) overlaps heavily**: ExternalProjectDataEndpoints (78 hunks), ExternalDataService (72), ExternalMessageEndpoints (27), WorkforceRoleCeilingGate (3), ExternalProjectDtos (3). Merge ordering needs coordination.

## Reviews

- **code-review / adr-check (inline):** ADR-001 Minimal API ✓; ADR-008 endpoint filters, same as project route ✓; ADR-010 no DI added ✓; ADR-019 ProblemDetails + stable codes on every new/changed response ✓; ADR-028 indeterminate → 503 not 403, no secrets ✓; ADR-038 KEEP paths, no banned mocks ✓; ADR-052 no background work ✓; NFR-04 logs carry Dataverse ids only (no tenant/oid/UPN/email/token) ✓.
- **Adversarial pass 1 (authorization, Opus):** no authorization defect. F3 message `canCreate` not tested through wiring → fixed (over-the-wire core tests). K2 dead wrapper → removed.
- **Adversarial pass 2 (contract/tests, Opus):** F1 resolver fields (pre-existing) → filed as defer D-1 (out of scope; remark corrected). F3 event-list ceiling indistinguishable from to-do ceiling → fixed (EventTable/TodoTable cases). K4 test name → renamed.

## Known limits

- K1 to-do/event `canCreate` omit `CanStampCreator` (the POST's 500 path pre-exists; no resolver produces such a caller).
- K1 `canCreate: true` can still meet 409 when the owner is refused (ownership is resolved only on write).
- K1 the create bypasses `RecordCreationService` (I-16 filled by the L4 reconciler, as for projects).
- K1 no data-mutation test for matter/WA events (contract tests assert payload and nothing-written on refusal).
- K4 the ceiling re-read relies on the role cache.
- K1 the message GET handler's reader pass-through is not HTTP-tested (no HTTP fixture serves the message GET); the core is tested over the wire.
- Defer D-1/D-2/D-3 (`notes/defer-issues.md`) — **GitHub issues PENDING, main session to file**.

## For uac-r2 (review — PASS 2026-11-12, after F4-1)

**Result:** uac-r2 PASS (FR-08, FR-24), 2026-11-12, after F4-1. **F4-1:** `WorkforceRoleCeilingGate.AdmitsAsync` admitted an
empty requirement set for callers the ceiling does not apply to (systemuser, CIAM partner), so the three
`_ => Array.Empty<…>()` deny fallbacks in `ExternalProjectDataEndpoints.cs` gave `canCreate: true`. Fixed: an empty set
returns false before the ceiling read. Test pair in `WorkforceCeilingEndpointFilterTests` (`AdmitsAsync_EmptySetForASystemuser_ReturnsFalse`
must-fire, `AdmitsAsync_NonEmptySetForASystemuser_ReturnsTrue` must-not-fire); mutation-checked (removing the guard fails
the must-fire test). Re-run: external-access unit tests 6373/6373, ceiling/route-ledger arch tests 104/104.

Items reviewed:

1. The two new routes `POST /api/v1/external/{matters|workassignments}/{id}/events`: filters, `RightsForRootWriteAsync` with `CreateEventPrivilege`, ceilings `CreateMatterEvent` / `CreateWorkAssignmentEvent` (sprk_event Create + root AppendTo), owner resolution, 503 path.
2. `canCreate` for every caller on the to-do, event, message and documents lists — including the workforce-ceiling read on each list and that each list uses its own create route's ceiling.
3. The message gate refactor (`EvaluateRecordGateAsync` reused for read and `canCreate`; wrapper removed).
4. The delete-409 body shape (lock holder disclosed only after the destroy gate admits).

rework: none required by reviews beyond the fixes above; D-1 is a candidate follow-up task.
