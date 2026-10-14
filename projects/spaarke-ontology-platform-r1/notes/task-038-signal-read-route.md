# Task 038: the Signal read route and Do-lane scoping

> Branch `task/ontology-038-signal-read-route` (worktree `C:\wts-038`), cut from `origin/docs/ontology-platform-design` at
> `9e61305ae`. Rigor **FULL** (the route decides record access: D-15, uac-r2 coordination). Two adversarial review passes (opus).

## What was built

`GET /api/v1/signals/worklist?set=lanes|closedToday|suppressed|closedEarlier` is the one route the Console worklist reads Signals
through.
- **Handler:** `Api/Signals/SignalReadEndpoints.cs`.
- **Service:** `Services/Signals/SignalReadService.cs`, registered unconditionally in `SignalsModule`.

`ReadAsync` steps:

1. WhoAmI as the caller. Unresolved: #1312's single 403.
2. The caller's linked contact. A contact that cannot be READ is 503 (review F1).
3. Task 050's grid row (`...5010`), read app-only. Every FetchXML must be a `sprk_signal` query.
4. The Signals, read AS THE CALLER with paging-cookie paging (`HasMoreRecords`). More than 5 pages is 503.
5. One catalog query.
6. The core-record check: one batched caller read per core type per 100 ids (`In()`). A type outside
   `CoreAncestorResolver.CoreRecordEntities`, or a half pair, is dropped (K2 as 036).
7. Subjects:
   - **Do lane:** the caller's own work, read as the caller with the "mine" clause.
   - **Decide lane:** the flag, plus the subject's Assigned To for the closed-set day.
8. The closed sets: each item's today comes from its assignee's zone, else UTC (D-25 as amended by D-129).
9. The read-time witness rule (D-123, below).
10. D-42 order: groups at their best item.

Each Do-lane table's "mine" clause ORs these columns:

| Table | Owner | Assignee column (`ForPersonColumns`) | Creator |
|---|---|---|---|
| event | yes | `sprk_assignedto` | yes |
| To Do | yes | `sprk_assignedto` | no |
| work assignment | no | `sprk_assignedtointernal` | yes |

The event rule matches `EventOwnershipScope` (097 decision B) exactly; a test pins this.

**Placement (CLAUDE.md §10).** The BFF is the only reader of `sprk_signal` for the Console (FR-24). Signal read depth is Parent: Child BU, so a browser read cannot apply the core-record check. No AI-internal type is used, and nothing is registered asymmetrically.

**§11 for the new surface:**
- **Route:** extending 036's per-Signal check was not possible, because it judges one Signal id and the list needs batched reads. Without the route, the Console has no compliant way to list Signals.
- **`SignalSuppressionService.WitnessDetailShownOnReadAsync`:** a thin call over `PartitionsAsync` / `ReadSubjectRowsAsync`. Its rationale is in the remarks.

## uac-r2 coordination

- Re-read `origin/work/unified-access-control-r2` @ `b04b5b37f`. This branch's base is newer for every file that matters:
  - `SecureChildLineage` @ `e4f147828`
  - `RecordRouteAccessAuthorizationFilter` @ `c5f71487f4`
  - `CoreAncestorResolver.CoreRecordEntities`
  - `SecureChildShareSynchronizer.SecureRootsAboveAsync` / `ResolveSecureOwnerTeamAsync`
- uac-r2's PR #1861 does not overlap.
- **For uac-r2 to review:** `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs` (their file). It sets `ExpectedEndpointFileCount` 121 → 122, adds a GovernedFile `Api/Signals/SignalReadEndpoints.cs`, and adds a HandlerDecision `GET /api/v1/signals/worklist`.
- The census was mutation-proven: with the original ledger 3 tests fail; with the new entry all 86 pass.

## Read-time witness rule (task 145 review F1, option B; main session 2026-10-14)

- **The rule:** a stored evidence line keeps `text` / `source` only when its witness row's secure family is KNOWN and EQUAL to the Signal's own family.
- **Otherwise:** both are removed and the line carries `"withheld": true`. `kind`, `ref`, `tier`, `asOf` and `clause` stay.
- **A fault** in the family read withholds every detail. This is fail closed: the list itself is unchanged and the result is logged at Warning.
- **Evidence that is not a JSON array of objects** is returned as null.
- **Implementation:** `SignalSuppressionService.WitnessDetailShownOnReadAsync` places the returned Signals with `PartitionsAsync`, reading app-only the rows the caller's reads already admitted. Its three private helpers mirror task 145's (unmerged) helpers:

  | This branch | Task 145 |
  |---|---|
  | `ReadWitnessFamiliesOnReadAsync` | `WitnessFamiliesAsync` |
  | `WitnessDetailWithinFamily` | `KeepsDetail` |
  | `WitnessOnRead` | `WitnessOf` |

- **When 145 merges:** fold these three into 145's helpers (one rule, one helper). The names differ on purpose so the merge does not collide.
- **DTO:** `WorklistItemRow.EvidenceRefs` carries the `withheld` flag per line.
- **UI** (`ConsoleKit/EvidenceLine.tsx:82-87` renders a line with no `text` as "No value recorded") is **outside 038**. Route it to 059: render `withheld: true` as "Details withheld (restricted record)".
- **For 145:** its job-side redaction (`StoredEvidenceToRedactAsync`) removes `text` / `source` without writing a marker. After that pass, the route can no longer tell withheld from absent. Suggest 145 also writes `"withheld": true` when it clears a line.
- **Live:** dev has the Secure Record BU, but no `sprk_signal` row carries evidence detail yet (read-only check 2026-10-14). The rule is proven in-process only:
  - the route: re-filed → withheld, same family → kept, fault → all withheld, not-an-array → null;
  - the service: re-filed, same family, undetermined, no secure team, fault, Signal missing.

## Deviations (recorded, not escalated)

1. **To Do "mine"** is owner OR `sprk_assignedto`. The POML says owner only; D-129 makes records team-owned with the person in Assigned To, so owner-only would empty the lane.
2. **Work assignment "mine"** uses `sprk_assignedtointernal` (D-129, `ForPersonColumns`). `SignalEvaluator` uses `sprk_assignedto` (DI-82, #1881).
3. **D-35** follows the amended rule: a no-core row is Do-only, and only when its subject is the caller's.
4. **No "fake fifth core type" test (POML D-36 constraint).** `CoreAncestorResolver.CoreRecordEntities` is static, the same deviation as task 037.
   - D-36 is honoured structurally: there is no core-type branch, the core table comes from the catalog row, and K2 is checked against the taxonomy.
   - Tests cover all four core types plus a non-core catalog type.
5. **No paging contract.** The route returns all lanes; more than 5 × 5000 Signal rows per query is 503.
6. **Decide items in the closed sets** take the subject's Assigned To zone (review F4). The POML is silent on this; D-129 names the individual.
7. **POML step 4** (TASK-INDEX / status) is left to the main session.

## Known limits

- **K2:** a 403 on the first Signal page is an empty list (no Read on `sprk_signal`). The other case that produces it is DI-83 (no Read on `sprk_policy`), which is filed.
- **F3 → known limit (scale):** each lane's FetchXML returns every Signal the caller's BU tree reads, before narrowing. Beyond 25k rows in one query the route is 503 permanently.
  - Below that, the cost is up to 250 caller reads per core type.
  - Fix when volumes approach this: a server-side Do-lane pre-narrowing link.
- **K1:** `MineDo` is keyed by subject id alone. A forged type can ride another row's id; it is still the caller's own work and nothing leaks.
- **K3:** the time zone is read twice per assignee user (once cached in `DataverseRecipientDays`).
- **K4:**
  - The primary key is assumed to be `{logical}id` (custom tables). An activity-type core would 400, then 503.
  - Ordering on a lookup relies on Dataverse returning a paging cookie; if one is missing, the result is a loud 503.
- **Witness rule cost:** one partition walk per distinct lineage, only for returned rows that still carry detail.
  - One unreadable lineage (a walk that does not complete) withholds detail on every line of that call. This fails closed; it is not a shorter list.

## Evidence

**In-process route tests** (`SignalReadRouteTests`, filter `SignalReadRoute`): 34 pass, plus 2 opt-in live legs.

**Live read-only legs** (`SignalReadRouteLiveTests`, spaarkedev1): 2/2 pass.
- Reader: Jessica Broyles `7c35232f-…`.
- Other BU: `d6f8f439-…`.

**Unit tests:** `SignalSuppressionTests` 88/88, including 6 new `T038_*`. The filter `Signal` passes 1522/1522.

**ArchTests:** 974/974, including `RouteAuthorizationGuard` 86/86.

**Mutation proofs:**

| Mutation | Tests failing |
|---|---|
| M0: route unmapped | 21 of 25 |
| M1: core check removed | 4 |
| M2: mine filter removed | 5 |
| Review fixes reverted (pre-`8024c9b92` service) | 6 |
| M3: assignee zone ignored | 2 |
| M4: Do rows skip the core check | 2 |
| Witness rule forced true | 2 unit, 1 route |

**Publish size (Release):** base is a fresh build of `9e61305ae` (the project branch base this task is cut from; the brief said master, while the project CLAUDE.md says the project branch). Head is `e428f728e`.

| | Files | Bytes |
|---|---|---|
| Base | 192 | 129,129,964 |
| Head | 192 | 129,220,488 |
| Delta | 0 | +90,524 (about +88 KB uncompressed) |

The delta is well under +5 MB. The `8024c9b92` additions are source only, with no package.

**CVE:** `dotnet list package --vulnerable --include-transitive` reports no vulnerable packages.

## Issues filed (D-106)

- **DI-82 [#1881](https://github.com/spaarke-dev/spaarke/issues/1881):** the evaluator's work-assignment assignee column.
- **DI-83 [#1882](https://github.com/spaarke-dev/spaarke/issues/1882):** the outer link to `sprk_policy` empties the worklist for users without policy Read. Task 050's note #8 assumption is wrong.
- **DI-84 [#1883](https://github.com/spaarke-dev/spaarke/issues/1883):** the per-Signal access check (036) and the worklist rule (038) disagree.

## Review record

- **Pass 1 (opus).** Fix-now findings, all fixed in `8024c9b92`:
  - F1: a contact fault shortened the Do lane.
  - F2: the Do-row core check was not isolated by a test.
  - F2: the zone partition was untested.
  - F4: Decide items in the closed sets used UTC.
  - F4: the fifth-core-type test is recorded as deviation 4.
  - F3: scale is recorded as a known limit.

  Known-limit findings (K1–K4) are recorded above. The page-2 refusal (K2) was fixed: it is now 503.
- **Pass 2 (opus):** see the commit that follows this note.
