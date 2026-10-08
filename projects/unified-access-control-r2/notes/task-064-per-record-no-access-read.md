# Task 064: the per-record No Access read

> Executor notes, 2026-10-08. Branch `feat/uac-r2-064-no-access-read` (worktree `C:\wt064`, from `origin/master`
> 37d0c944c). Rigor FULL. Scope as narrowed by owner round 59 item 3 (POML amendments of 2026-10-05): ONE route for
> project, matter and work assignment. No live change was made.

## Step 0

- **Did Phase 1 ship deny-list management routes?** No. Nothing maps a deny-list add/remove route. Round 59 cut them
  anyway: authoring is 154's entry form, enforcement is 143's `POST /api/v1/external-access/no-access/enforce`
  (`NoAccessEnforceEndpoint.cs`). The provenance report, the per-entry enforcement state, a per-record method on
  `NoAccessListReader` and organization / contact records are also cut (amendment 3).
- **154 has landed** (PR #1393): the canonical-id rule `NoAccessListReader.TryParseObjectRecordId` is on master, so the
  string-equality record clause (`NoAccessEnforcementStore.CoveringObjectFilters`) matches what the form stores
  (amendment 4's soft dependency is met).
- **O2** (2026-10-01, accepted as recommended; 154 applies the role change): only an access-administrator role reads
  `sprk_noaccessentry`; every other reader of a record sees the banner; Write holders see the record's entries through
  Manage Access, with the BFF reading on their behalf. This task is that BFF read.
- **Live check (read-only, spaarkedev1):** every column the entry read selects exists, including the two added here
  (`sprk_name`, `modifiedon`); a bogus column is refused (`0x80060888`), so the probe is real. The table has 0 rows.

## What was built

1. **One lookup, lifted out of the enforcer** (amendment 1). `NoAccessShareEnforcer.ReadCoverageAsync(type, id)` is the
   ONE answer to "which active entries cover this record": the record's own entries, walls over every organization it
   references (B-10), and the same for every secure record it is filed under (round 61 item 1, through the ONE parent walk
   `SecureRootInheritance.ReadSecureParentsAsync`). It returns `NoAccessCoverage` (entries, each with the record it
   covers through; truncated; fault) and never throws a read fault. `EnforceForRecordAsync` ("Update Access") now calls
   it, so the enforcer and the read cannot disagree.
2. **One well-formedness rule** shared by both: `NoAccessShareEnforcer.TryClassify` (Business Rule 1 plus 154's canonical
   id), extracted from `EnforceEntryAsync` unchanged.
3. **The route** (amendment 2): `GET /api/v1/records/{sprk_project|sprk_matter|sprk_workassignment}/{recordId}/no-access`
   (`Api/ExternalAccess/RecordNoAccessEndpoint.cs`). Every Read caller gets `secure` and `noAccess`, each
   applies / doesNotApply / unknown; a caller who also holds Write gets the entry rows, read with the existing
   `ReadEntryAsync` per id and capped at `MaxEntriesPerRecord` (100). Contract frozen in
   [`phase4-access-report-contract.md`](phase4-access-report-contract.md).
4. **The gate**: the existing `RecordRouteAccessAuthorizationFilter`, fixed-entity-set form, existing `read` key (no new
   key, no new filter). The filter now publishes the rights its probe returned for the record it checked
   (`TryGetAuthorizedRights`), so the handler's Write decision reuses the same probe call.
5. **Entry display values**: `ReadEntryAsync` asks Dataverse for formatted lookup values and selects `sprk_name` and
   `modifiedon` (platform columns), so the list shows names, not GUIDs. Parsing is the pure `EntryFrom`. The reader's
   `RowSelect` is untouched (153's constraint: a missing column there would fail every deny read closed).

## Deviations from the POML text (directional mode)

| POML says | Done | Why |
|---|---|---|
| Amendment 1: the lifted lookup is "ONE store method" | A public method on `NoAccessShareEnforcer` | The lookup needs the parent walk (round 61, added after the amendment's line references were taken) and the participation service; the store has neither, and adding them to its constructor would put two more services into a typed-HttpClient wire class. The enforcer already held every dependency. The binding goal, one source of truth that both callers use, is met. |
| Amendment 2: the filter's route-value form "with an operation-specific refusal text in place of the 'add content' text at :309" | The fixed-entity-set form, three literal routes | That form already exists (task 159), uses an operation-neutral text, and gives the uniform 404 for no Read and for an absent record, so an unknown id and a denied id are indistinguishable. The upload form and its text are untouched. |
| Amendment 2: "for project, matter and work assignment only" | Same; no route exists for any other type | The shared entity-set map is not widened. |
| (implicit) | Secure-parent entries count toward `noAccess` and are listed with `viaSecureParent` | The enforcer (round 58/61) and the share-time guard (round 39 item 2) apply a secure parent's list to every secure record filed below it; the read reports what they enforce. |
| UX amendment (d): a read fault returns ProblemDetails | A 200 whose signal is `unknown` and whose entries are `unavailable` | Round 59 amendment 2 defines each signal as applies / does not apply / unknown. A fault is never a negative answer and never an empty list. The gate's refusals are ProblemDetails with stable codes (ADR-019). |

## Authorization (ADR-008; FR-07 read-side requirement)

- **Read** for the signals, **Write** for the entries. The signals carry no identity and no Reason; anyone who can open
  the record may see that it is Secure or walled (task 153's banner). The entries name who is walled off, so they follow
  O2: Write holders only, the same rule as Manage Access (`DelegationRuleFilter`'s Write).
- The route is NOT on `/api/v1/external-access`: that group's `DelegationRuleFilter` demands Write for every route, and
  the banner must answer Read-only callers.
- No Read, no bearer token, an absent record and a probe fault are all the uniform 404; nothing else is read for a
  refused caller. 401 when not signed in.
- Known limit (K3): a walled internal user who still holds Write through a team or role (owner N2) sees the entry list,
  including the entry naming them. No Reason is shown, and that user already knows they are walled (they cannot be shared
  the record). Not changed.

## CLAUDE.md §11 (new surface)

- **The route.** Existing: nothing returns the No Access state of one record; `INoAccessListReader` answers the inverse
  (a subject's denied records) and the enforce route only removes. Extension: one route file using the existing filter,
  key, enforcer lookup and store read; no new service, filter, key or DI registration. Cost of doing nothing: task 153's
  banner and task 067's list have nothing to read, and Write holders cannot see who is walled off a record (O2).
- **`ReadCoverageAsync` / `NoAccessCoverage` / `TryClassify`.** Existing: the same logic, private, inside the enforcer.
  Extension: it IS the extraction (amendment 1). Cost of doing nothing: a second copy in the route, the "two evaluators"
  defect this project exists to remove.
- **`TryGetAuthorizedRights`.** Existing: none (the filter discarded the rights). Extension: three lines in the existing
  filter. Cost of doing nothing: a second RetrievePrincipalAccess round trip per request, or a Write decision taken on
  something other than the gate's own answer.
- **`NoAccessEntryDisplay` + two selected columns.** Existing: the entry read returned ids only. Cost of doing nothing:
  067's list shows GUIDs, or every client re-reads contacts, organizations and users itself.

## Placement justification (`.claude/constraints/bff-extensions.md` §A)

In the BFF: a synchronous per-request read on a form load (latency budget), composed from BFF-owned authorization code
(the caller-rights gate, the enforcer's lookup); no background work, so ADR-052 does not apply. ADRs: 001 (Minimal API,
`Map{Feature}` extension), 008 (endpoint filter before the handler), 010 (no DI change), 019 (ProblemDetails on refusals),
003 (fail closed: unknown, never negative). No package added (no CVE change), no AI dependency, no config field.

## Tests

- `tests/integration/auth/UnifiedAccessControl/RecordNoAccessEndpointTests.cs`: 29 cases through the real pipeline
  (authentication, the route filter, the production enforcer lookup and handler; only module boundaries substituted).
  Covers acceptance (a)-(h) as narrowed, the three root types, secure-parent coverage and its fault, every read fault,
  truncation both ways, and the authorization negatives.
- `NoAccessEnforcementStoreTests`: 3 cases for the entry read's columns and display parsing.
- The existing enforcer suites (`NoAccessShareEnforcerTests`, `NoAccessEnforceEndpointTests`, the reconciliation job)
  pass unchanged: `EnforceForRecordAsync` behaves as before.
- Perturbations (each committed state restored; all caught): unreadable Secure flag read as not secure; entries returned
  to a Read-only caller; a truncated prefix read as "does not apply"; the parent walk dropped; the filter's rights
  honoured for another record; a malformed entry counted; an unreadable coverage read as "does not apply".
- `RouteAuthorizationGuardTests`: the new file is a governed `RouteLevelGate` file; census 117 → 118 with a history line.

## Gates

- Builds: `Spaarke.sln` green.
- Suites: `Sprk.Bff.Api.Tests` 18,590 passed / 0 failed (54 skipped, pre-existing); `Sprk.Bff.Api.IntegrationTests`
  88 / 0; `Spe.Integration.Tests` 350 / 0 (25 skipped, pre-existing); `Spaarke.ArchTests` 811 / 0.
- Publish size (compressed, `dotnet publish -c Release`, short path): fresh `origin/master` 36.225 MB, this branch
  36.236 MB, delta +0.011 MB. No package change; `dotnet list package --vulnerable --include-transitive`: none.
- Quality gates (own pass; the adversarial verifier is the main session's): no F-class finding left. K-class: the entry
  re-read after the covering query could in principle see an entry edited to another object in between (counted once,
  toward "applies", the safe direction for a banner); entries are read one by one (at most 100, typically 0-3).
  ADR check: 001, 003, 008, 010, 019, 038 complied with; no tension.

## Defects found

- **Out of scope, filed #1410 (F1, fail-open, narrow):** the Teams/SPA read-time veto ignores a secure PARENT's entries.
  `AccessibleRecordSetService.ResolveSystemUserDenyVetoAsync` builds each candidate from the record's own id and
  organizations only, while round 61 (binding) says a parent's list reaches every secure record filed below it. A walled
  user who still reaches a filed secure work assignment through a team (N2) is told by the enforcer "hidden on Teams/SPA"
  but sees it there. `AccessibleRecordSetService.cs` is the exclusive zone where 036 is in flight, so it was not fixed here.

## Verifier pass 1 (main session, 2026-10-08): fixes

| Finding | Fix |
|---|---|
| F3 (pre-existing; breaks NFR-01 "not distinguishable in any channel" on this route): timing oracle. The probe re-asks only on 404 (400 ms + 1,200 ms, `CallerRecordAccessProbe.NotFoundRetryDelay` and its retry loop). Dataverse answers 404 `0x80040217` only for a record that does not exist; a record the caller cannot see answers 403 `0x80048306` at once. So an unknown id reached the uniform 404 ~1.6 s after a denied one. The probe remark claiming Dataverse answers "not found" for a record the caller cannot see was false. | `CallerRecordAccessProbe.GetCallerRightsForAuthorizationGateAsync` (non-virtual; scopes the schedule with an `AsyncLocal` and calls the virtual `GetCallerRightsAsync`, so every test double is still the one asked). On that path ANY 403 follows the not-found schedule (`FollowsNotFoundSchedule`, pure), so both denials make the same asks and waits. The not-found retry itself is kept; every other probe use is unchanged; an allowed caller pays nothing. `RecordRouteAccessAuthorizationFilter`'s fixed-entity-set form (task 159's events `/{id}`, `/complete`, `/filing`, and these `/no-access` routes) and declared-target form (events create) use it. Every 403 is equalised, not only `0x80048306`, because a missing-privilege 403 is as fast and would reopen the oracle. Remark corrected. Wire seams `SendPrincipalAccessRequestAsync` / `DelayAsync` (protected virtual) let the test run the real loop with no transport double and no sleep. |
| F3 follow-on (pre-existing, out of scope): the same oracle on the other caller-rights gates (`DelegationRuleFilter.cs:183`, the upload form `RecordRouteAccessAuthorizationFilter.cs:276`, `EntityAccessFilter.cs:276`, `TodoSourceAccessFilter.cs:184`, `QuickCreateSourceAccessFilter.cs:260/:332`, `CommunicationRecordAuthorizationFilter.cs:735/:745`) | Filed **#1414** (a one-line switch per gate). Not changed here: the verifier scoped the fix to the route-record gate, and `DelegationRuleFilter` is the exclusive zone. |
| F4: a systemuser-subject entry counted toward `noAccess` on a NON-secure record, though user walls bind only secure records (Q4; `AccessibleRecordSetService.ResolveSystemUserDenyVetoAsync` removes nothing for it; the enforcer acts only on secure records) | `RecordNoAccessEndpoint.InForce`: on `secure: doesNotApply` such an entry is listed `inForce: false`, `notInForceReason: "userWallOnNonSecureRecord"` and does not count; on `secure: unknown` it is `inForce: null` (`secureStateUnknown`) and makes `noAccess` `unknown` unless another entry is in force; reached through a secure parent it stays in force. Contract note updated. |
| Test gap: Write-tier shape of an organization wall over what the secure parent references | Added. |

Tests added: `AuthorizationGateTimingTests` (13: same asks and waits for unknown vs denied on the gate, any 403 equalised,
allowed caller unaffected, off-gate behaviour unchanged, no leak to the next question, the pure rule, and both filter forms
end to end); `RecordNoAccessEndpointTests` +7 (user wall non-secure / secure / unknown / unknown-but-another-in-force,
contact wall on non-secure, parent org-wall shape, parent user wall vs the child's own flag). Perturbations, all caught: the
gate not equalising; each filter form using the plain probe; equalising every probe use; dropping the not-found retry; a
user wall counted on a non-secure record; an unknown Secure flag read as "not in force"; a parent's user wall judged by
the child's flag.

Gates after the fixes (rebased on `origin/master` 11dc9b0da): `Spaarke.sln` builds; `Sprk.Bff.Api.Tests` 18,662 passed,
4 failed on a loaded machine (Compose/Document contract tests timing out at ~2 m 47 s with "client aborted"; all 27 tests
of those four classes pass on a rerun; none touches this change); `Sprk.Bff.Api.IntegrationTests` 87 / 0 (3 live tests
skipped); `Spe.Integration.Tests` 350 / 0; `Spaarke.ArchTests` 811 / 0. Publish 36.254 MB compressed (base 36.225 MB at
37d0c944c; +0.029 MB including master's #1359). No package change, no CVE.

**Found in passing and fixed (master build break):** `tests/integration/Sprk.Bff.Api.IntegrationTests/Events/EventRoutesLiveTests.cs:407`
did not compile on `origin/master` (CS1061): #1359 called `.Single` on `ExternalDataService.GetEventsAsync`, whose result
#1408 (task 105) had just changed to `ExternalCollectionResponse<T>`. Fixed to `.Value.Single(...)`. Without it
`Spaarke.sln` and the integration suite the project requires before a PR do not build.

Known limits recorded (no change): **K1** an organization a secure PARENT references becomes visible (id and name) to a
Write holder on a filed child through the parent's organization-wall entries. **K2** `sprk_name` is free text and reaches
Write holders as typed (an administrator could put sensitive text there; Reason stays out). **K3** a walled internal user
who still holds Write through a team or role (N2) sees the entry naming them (see Authorization).

## Verifier pass 2 (main session, 2026-10-08): fix

| Finding | Fix |
|---|---|
| F3: an entry reaching the record two ways was judged on its direct path only. `ReadCoverageAsync` dropped a parent-path hit already listed directly, so a user wall over an organization that BOTH a not-yet-secure filed record and its secure parent reference read `inForce: false` / `doesNotApply`, while the share-time guard (`SecureShareNoAccessGuard.CheckRecordAndSecureParentsAsync`) says Walled. Non-monotonic: without the direct reference it counted. | `NoAccessCoveringEntry.AlsoViaSecureParent`: the duplicate is no longer dropped but MARKS the listed entry; the endpoint ORs it into the in-force decision, and the row carries `alsoViaSecureParent`. The enforcer is unchanged (it enforces distinct entry ids). The read-time veto path is untouched (#1410 is fixed separately). Contract note updated. |

Tests: the overlap case (applies, `inForce: true`, listed once with `alsoViaSecureParent: true`), its negative (only the
non-secure record references the organization: not in force, not marked), and a cancellation during the gate's wait (a
cancellation, never an answer; nothing re-asked). Perturbations: the endpoint ignoring the mark, and the coverage dropping
the parent path again, both caught.

## Owner questions

1. **Reason for Write holders?** O2's answer says Write holders see a record's entries; the 067 amendment's "(Reason
   included, confirm)" was never confirmed. Shipped WITHOUT the Reason (task 143's rule). Recommendation: keep it out;
   access administrators see it on the entry form. Adding it later is one column.

## Live steps after merge

1. Deploy the BFF to dev (standard `scripts/Deploy-BffApi.ps1`, fresh short-path worktree of `origin/master`).
2. Smoke (live session D): as a Read-only user, `GET /api/v1/records/sprk_project/{id}/no-access` on a walled project
   answers `noAccess: applies`, `entriesState: notShown`; as a Write user the same record lists the entry; a user without
   Read gets 404. No PCF or web resource changes in this task (153 and 067 consume the route).
