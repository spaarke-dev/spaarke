# Task 191e: the flagged-user placement job and the placement-drift census

> 2026-11-11. Branch `task/uac-191e` off origin/master `654e39c685`. Rigor FULL, opus @ xhigh. Nothing was applied live:
> the dev reads below are metadata-only (attribute and ownership metadata), as the operator's az CLI identity.

## 1. What was built

| File | What it is |
|---|---|
| `Services/ExternalAccess/FlaggedUserPlacementJob.cs` (new) | The `IScheduledJob`, every 2 minutes. Reads every systemuser whose stored `sprk_isexternal` reads true, classifies each (pure `Classify`), and with writes on moves it into the unit (PATCH `businessunitid`), associates the role copy that lives in the unit, and reads both back. Report-only by default. |
| `Services/ExternalAccess/ExternalCollaboratorPlacement.cs` (new) | The shared reads (unit, role, System Administrator copies, flagged users, direct roles, memberships, team roles, unflagged users in the unit) and the placement-drift census grader. Static, like `SecureRecordIsolationCensus`. |
| `Services/ExternalAccess/SecureRecordIsolationCensusJob.cs` | Runs the placement census beside the secure census, in its own try; CRITICAL per finding; `placement` section in `ResultJson`; heartbeat gains `placementStatus`, `placementFindings`, `flaggedUsers`. |
| `Services/ExternalAccess/SecureRecordIsolationCensus.cs` | `Query`, `RetrieveAllAsync`, `OptionalGuid`, `RequiredGuid` made `internal` (reused, not copied). No behaviour change. |
| `Infrastructure/DI/ExternalAccessModule.cs` | One `AddScheduledJob<FlaggedUserPlacementJob>` line with its comment. |
| `tests/unit/.../Services/ExternalAccess/FlaggedUserPlacementJobTests.cs` (new) | The job's tests over an in-memory directory that behaves as spike S2 found, plus the shared condition evaluator (`FakeConditions`). |
| `tests/integration/auth/UnifiedAccessControl/SecureRecordIsolationCensusJobTests.cs` | The census directory evaluates every condition (shared evaluator); 16 placement cases. |

## 2. Behaviour (the rules the tests pin)

**The job.**
- Candidate: stored flag reads `true` (filtered by Dataverse AND re-checked in code; an absent or masked value is internal).
- Never moved: application users; disabled users; System Administrator held directly or through a team (matched by role
  template id, never by name); the Administrative access mode; any access mode but Read-Write (0) or Read (2); a user whose
  unit, disabled state or access mode was not returned.
- Never moved OUT: the only business-unit write targets the unit, for a user outside it. A user in the unit whose flag no
  longer reads true is reported (`unflaggedInUnit`, Warning) for an administrator (round 115).
- Before each write the user is re-read; if its decision changed (flag cleared, disabled, moved), nothing is written.
- Success = in the unit, holding THE role, no other direct role. Anything else (write threw, accepted PATCH that did not
  move, roles kept contrary to S2, read-back failed) = one Critical `ALERT` line for that user, run unsuccessful, retried
  next tick. A user moved but left without the role is picked up as "assign the role" next run.
- Unit or role not exactly one: nobody is written (a move without the role leaves the user with none). With writes on, one
  Critical `ALERT` line naming the waiting ids; report-only lists them as `unplaceable`.
- Any read failure before the write phase: nothing written, heartbeat `status=error`, throw (ADR-036 A1 rule 4).
- At most 25 users written per run; the rest are `deferred` to the next tick.
- Setting (positively named; absent, empty or unparseable = report-only): `ExternalAccess:FlaggedUserPlacement:WritesEnabled`
  (App Service: `ExternalAccess__FlaggedUserPlacement__WritesEnabled`). Report-only lists `wouldMove` and
  `wouldAssignRole` ids.

**The census** (CRITICAL per finding, one line each, by id; enabled human flagged users only — the secure census's
`IsHumanPrincipal`):

| Kind | Fires when |
|---|---|
| `FlaggedUserOutsideExternalUnit` | business unit ≠ the unit |
| `RoleFromAnotherUnit` | a direct role (or a role of the unit's default team) assigned from another unit, or unreadable |
| `UnexpectedRole` | a role from the unit that is not THE External Collaborator role (direct, or on the default team) |
| `LineageDepthAboveBasic` | any held role (direct or through a team, THE role included) has Local/Deep/Global on any privilege of any lineage-registry table |
| `WriteUserPrivilege` | any held role has `prvWriteUser` at any depth |
| `OtherOwnerTeamMembership` | member of a team other than the unit's default team (access teams, type 1, excluded) |
| `LineageRowOwnedByFlaggedPrincipal` | a lineage-registry row owned by a flagged user or by the unit's default team |
| `FlaggedManagerUnderHierarchySecurity` | hierarchy security on and a flagged user manages someone (manager model) or holds a position (position model) |
| `UnitAmbiguous` | more than one unit of the name, or not directly under the root |

No unit yet: `inert`, one Warning with the flagged-user count, not a finding, run success unchanged (decision D-3).

## 3. Decisions and deviations

- **D-1 One adversarial pass, not two.** The POML step 4 says two (auth, security); the operator's brief for this run says
  one Opus pass. Operator instruction followed; recorded here.
- **D-2 POML step 5 (TASK-INDEX) not done; `current-task.md` untouched.** Reserved for the uac-r2 session by the operator.
  The POML status is left for that session too, so the drift check stays consistent.
- **D-3 Inert placement is not a census failure.** Before task 191i creates the unit, every environment would otherwise
  turn the 15-minute census red for the documented pre-migration state (design section 1). The secure census treats a
  missing secure BU as inert-and-failing; here the unit's absence is the expected state until an owner go, so it is a
  Warning with the count and leaves success unchanged. Once the unit exists every drift kind is graded.
- **D-4 "Only the role" is the rule the census grades, not just "a role from another unit".** Design 3.15 names a role
  from another unit; a second role copy assigned FROM the unit (every root role has a copy there) would pass that check
  and could carry Global on a Procedure table, which is outside the lineage registry the depth check reads. `UnexpectedRole`
  closes it without a second table list. Same for roles on the unit's default team (191d's `-Verify` checks the team too,
  but only when run).
- **D-5 Hierarchy, position model.** Design says "hierarchy security on with a flagged manager". In the position model a
  flagged user holding any position is reported (fail toward reporting; the census cannot cheaply tell whether a position
  has children).
- **D-6 No config override for the unit and role names.** They are compiled constants pinned to
  `config/external-collaborator-role.json` by a test (the script has no override either).

## 4. Known limits (one line each)

- K-1 The ownership check reads at most 500 rows per table and owner chunk; past that it says "at least N".
- K-2 Access-team memberships (type 1) are not graded: they are per-record shares, the share writers' concern (191c).
- K-3 The job does not remove roles a move left behind (S2 says there are none); it alerts and the census keeps reporting
  them. An S2 deviation is an escalation trigger, not something the job repairs.
- K-4 Disabled, application, support and non-interactive flagged users are not graded by the census (they cannot sign in
  interactively), matching what the job never moves. The reverse is not symmetric: a flagged System Administrator or a
  flagged user in access mode 1 or 5 IS graded (so stays a CRITICAL `FlaggedUserOutsideExternalUnit` every run) but is
  never moved by the job. That is deliberate drift for an administrator to resolve (clear the flag or the admin role).
- K-5 Before 191i creates the unit, placement is inert (Warning with the count), not a census failure (D-3).
- K-6 Ownership: one 500-row page per owner chunk. An owner whose rows all fall after that page gets no finding until the
  first owner's rows are fixed (the run is red either way, never clean).
- K-7 The run bound (25) takes users in id order: 25 or more users that keep failing would hold every slot each tick.
  Each failure is its own Critical line, so this is visible.
- K-8 The pre-write re-read covers the user and its direct roles, not its team roles; the role association after a move
  does not re-read the flag. The windows are seconds; the next tick and the census see the result.
- K-9 A re-read failure for one user is `unverified` (Error, run unsuccessful) and does not stop the other users in the
  run: each write rests on that user's own fresh read. Literal ADR-036 A1 rule 4 is "throw on read failure"; every read
  that decides the run's candidate list still throws.
- K-10 Lineage tables the environment does not have (no privilege returned) are listed as `ungradedTables` at Error and
  skipped for ownership (fix-round 1, finding 1).

## 4.5 Review (one Opus adversarial pass, 2026-11-11)

| # | Finding | Class | Disposition |
|---|---|---|---|
| 1 | Census queried every lineage table; a table the environment lacks throws, so after 191i the whole census (secure half included) fails every run | F4 fix-now | Fixed: only tables with a returned privilege are queried; the rest are `ungradedTables` (Error line, ResultJson). Test `Placement_ALineageTableTheEnvironmentLacks_IsListedAsUngraded_AndTheRestIsStillGraded` |
| 2 | `Classify` said Placed for in-unit + role + other roles; read-back said failure, so one alert then silence | F1 fix-now | Fixed: new `PlacedWithOtherRoles` decision, Critical every run in both modes, run unsuccessful, `otherRoles` in result and heartbeat. Test extended to a second run, plus a report-only test and rule-table rows |
| 3 | Census job resolved the Dataverse seam outside every try | K | Fixed (resolved inside each try) |
| 4 | Ownership first page only | K | K-6 |
| 5 | Run bound starvation | K | K-7 |
| 6 | Team roles and flag not re-read before association | K | K-8 |
| 7 | Per-user re-read failure does not throw | K | K-9 |
| 8 | Census grades admins/mode 1/5 that the job never moves | K | K-4 (documented) |
| 9 | Role missing or ambiguous gave no census finding | K | Fixed: one `RoleUnresolved` finding; test added |
| 10 | Census double ignores ColumnSet; position-model branch untested; job-level app/admin-mode cases overlap the access-mode check | K | Position-model test added. ColumnSet: the job's double projects columns (privacy test pins the job's column list); census columns are not pinned (left as known limit). App/admin-mode: pinned by the pure `Classify` rule table |

rework: fix-round-1 task 191e (findings 1, 2 fixed; 3, 9, 10-part fixed as cheap hardening).

## 5. Dev metadata read (2026-11-11, read-only)

- `systemuser.sprk_isexternal`: Boolean, not secured; `parentsystemuserid`, `positionid`, `accessmode`, `applicationid`,
  `isdisabled`, `businessunitid` exist; `organization.ishierarchicalsecuritymodelenabled` and `usepositionhierarchy` exist;
  `systemuserroles_association` exists.
- Every lineage-registry table (and the three roots) is UserOwned, so the `ownerid` query is valid on each.

## 6. Results

At head `abe533e365` (fix-round 1) unless stated.

| Gate | Result |
|---|---|
| Targeted (`FlaggedUserPlacementJobTests` + `SecureRecordIsolationCensusJobTests`) | 87/87 passed (82/82 before fix-round 1) |
| ArchTests (full) | 2488/2488 passed. First run failed 1 (`3d: every lookup-column read ... is a reviewed site`: `FlaggedUser.From`'s three SDK reads); reviewed as `FailsClosed` in `ExternalLookupAbsentReadGuardTests.ReviewedSites` |
| `tests/integration/Sprk.Bff.Api.IntegrationTests` | 212 passed, 8 skipped, 0 failed |
| `tests/integration/Spe.Integration.Tests` | 353 passed, 23 skipped, 0 failed |
| Full BFF unit suite | Did NOT finish on the shared machine (stopped after ~95 min, hung with no output for 15 min). It had logged 319 failures, scattered one or two per class across 124 classes (HTTP-host, Compose seam, timeout tests), none in the touched namespaces. A re-run of just those 124 classes failed a DIFFERENT set; six sampled classes run alone passed 114/114. Read as load-induced flakiness on the shared machine, not this change; CI runs the full suite |
| Mutation checks (each reverted) | Both definite-true checks removed (read filter + `Classify`): 5 fail, incl. the `#EXT#` must-not-fire. `Classify` check alone removed: 2 fail (the read filter still protects: defence in depth). Other-roles counted as placed: 3 fail. In-unit users given Move: 11 fail (incl. never-moved-out). Ownership on every table: the ungraded-table test fails |
| Format | `dotnet format whitespace --folder` on every changed C# file (CI Tier 1 check) |
| Publish size | master `654e39c685` 41,848,380 B zipped / 191 files; branch `abe533e365` 41,880,679 B / 191 files; delta +32,299 B (+0.03 MB); 4 PDBs each |
| CVE | `dotnet list ... --vulnerable --include-transitive`: no vulnerable packages; no package change |
| adr-check | ADR-036 (AddScheduledJob, heartbeat per attempt, throw on read failure, idempotent fresh reads), ADR-052 (BFF, schedule; B2/B3), ADR-028 (no new right), ADR-032 (unconditional registration), ADR-002 (no plugin), ADR-010 (no new interface or DI service): comply. PII log guard (`UacPiiLogTemplateGuardTests`) passes |
| Overlap with 191f (`C:\wt191f`) | None: 191f changes scripts only |

Class grep (fix-round 1, finding 2's class: "placed" decided differently in two places): the read-back and `Classify` now share
one rule (in unit + THE role + no other direct role); no other place decides "placed".
