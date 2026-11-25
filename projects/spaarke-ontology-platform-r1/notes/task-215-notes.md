# Task 215 notes: Assign Task recipient gate uses the shared ContactUserRepresentation (#2457, DI-136)

Branch `work/ontology-215-gate-contact-representation`, cut from origin/master `b005279c4d`. Rigor FULL, opus @ high.

## Finding: the swap already landed with uac-r2 193

`AssignTaskRecipientGate.DecideCoreAsync` on origin/master already calls
`new ContactUserRepresentation(_identities, _links, _participations).ResolveAsync(contactId, rootIsRestricted: false,
BindingColumnMissingPolicy.DecideOnLink, ct)`. uac-r2 made the swap in their task 193 master merge (`b4130cc5ea`, #2700,
2026-11-11), which deleted the copied contact read, link read, oid confirmation and `Represents` lines and closed K-1 in the
gate's header. #2457 stayed open. Task 207 (`20129e985c`) later added the Administrative refinement on top.

The gate still had one leftover copy. It re-ran `InternalShareEndpoints.ClassifyEligibility(..., rootIsRestricted: false)`
on each represented user, although `ContactUserRepresentation` already returns that answer as `RepresentedUser.Eligibility`,
computed by the same call with the same five arguments.

## Change

- `DecideInternalAsync` takes `IReadOnlyList<RepresentedUser>` and keeps the users whose `Eligibility == Eligible`, then
  unwraps `.User`. It no longer calls `ClassifyEligibility` before the count. Order and de-duplication are unchanged: both come
  from `ContactUserRepresentation`'s `GroupBy(SystemUserId).First()`.
- The Restricted bar after the count is unchanged: `RestrictedAsync` is read once, only when the one eligible user is
  flagged external, and `ClassifyEligibility(..., restricted)` decides as before.
- The comment over the representation call now says that eligibility comes with the representation and that the Restricted
  bar is applied after the count (D-199).
- Not edited (uac-r2 owns them): `ContactUserRepresentation`, `GrantAccessNotifier`, `CallerRecordAccessProbe`,
  `InternalShareEndpoints.ClassifyEligibility`.

## Equivalence, branch by branch

"Pre-193" is the gate's copied code (`b4130cc5ea^`). "Master" is origin/master `b005279c4d`. "Branch" is this change. The
warning is the commit warning (`AssignTaskRecipient.Warning`); "emailed" means no warning.

| Gate branch / input | ContactUserRepresentation outcome | Pre-193 | Master | Branch |
|---|---|---|---|---|
| Contact read fails (`Failed` / other status) | `Unreadable` | unverified | unverified | unverified |
| Contact read throws | propagates | unverified (gate catch) | unverified | unverified |
| Contact row missing | `NotFound` | no_access | no_access | no_access |
| Contact inactive | `Inactive` | no_access | no_access | no_access |
| Binding column missing, contact active | `ColumnMissing`, users by link | as by link | as by link | as by link |
| Binding column missing, contact inactive | `Inactive` | no_access | no_access | no_access |
| Binding column missing, state unreadable or read throws | `Unreadable` | unverified | unverified | unverified |
| Link read throws | `Unreadable` | unverified (gate catch) | unverified | unverified |
| Oid-only user, oid lookup not `Read` (or throws) | `Unreadable` | unverified | unverified | unverified |
| Oid-only user, oid resolves to this contact alone | `Read`, user represented | as one user | as one user | as one user |
| Oid-only user, oid resolves elsewhere, or two contacts carry it | `Read`, no users, `OidUnconfirmed` set | no_access | no_access | no_access |
| No users, no oid-only candidate, contact row present | `Read`, no users | external path | external path | external path |
| No users, binding column missing (no row) | `ColumnMissing`, no users | no_access | no_access | no_access |
| No eligible user (disabled, not a person) | users, none Eligible | no_access | no_access | no_access |
| Two or more eligible users | users, more than one Eligible | several_users | several_users | several_users |
| Two users, one disabled | one Eligible | as one user | as one user | as one user |
| Two eligible, one flagged external, record Restricted | two Eligible (classified as not Restricted) | several_users | several_users | several_users |
| One eligible, flagged external, record Restricted | one Eligible | no_access | no_access | no_access |
| One eligible, flagged external, Restricted unreadable | one Eligible | unverified | unverified | unverified |
| One eligible, then wall / task Read / Administrative | unchanged gate steps | unchanged | unchanged | unchanged |

There is one textual difference between pre-193 and master: the "external only when" test. Pre-193 skipped candidates with an
empty `SystemUserId`; `OidUnconfirmed` does not. The two disagree only for an oid-only candidate whose `SystemUserId` is
`Guid.Empty`. That input cannot reach either code from Dataverse: `AssignedAccessStore.ReadLinkCandidatesAsync` drops empty ids
(`AssignedAccessStore.cs:873` at `b005279c4d`). Where they could disagree, master refuses (no_access) where pre-193 would have
tried the external path, so master is the closed side. This is uac-r2's merged behaviour, not this task's change.

Branch against master: `RepresentedUser.Eligibility` is
`ClassifyEligibility(c.IsDisabled, c.AccessMode, c.ApplicationId, c.IsExternal, rootIsRestricted: false)`, the same call the
gate made, so no input changes who is emailed or which warning is given.

## The issue's "read the Restricted flag once up front": not applied

#2457 suggested reading the Restricted flag up front for every assignee and passing it into `ResolveAsync`. Done literally,
that changes outcomes, which the brief forbids:

| Input | Master and branch | Up-front read passed into `ResolveAsync` |
|---|---|---|
| Two eligible users, one flagged external, record Restricted | several_users | the flagged one drops before the count, so the other is emailed |
| One user flagged external, Restricted unreadable | unverified | the user drops before the count: no_access |

It would also add a Restricted read for every assignee, plus a warning log for every task filed under a table without access
flags. The criterion "Restricted read once" already holds: the flag is read at most once per decision, and only for the one
eligible user flagged external. The new table test pins both differences, so the up-front variant fails it (mutation check
below).

## Tests

- New: `tests/integration/data-mutation/Signals/AssignTaskRecipientGateRepresentationTableTests.cs`, 24 rows. For each
  representation outcome it checks the verdict, the warning, the user emailed, how many times Restricted was read, and that the
  external path was never entered. It uses the real gate decision, the real `ContactUserRepresentation`, the real binder oid
  decision and the real eligibility rule. Rows: none, one active, one disabled, one not a person, several, several with one
  disabled, several with one flagged external on Restricted, one flagged external on Restricted / Standard / unreadable, one not
  flagged on Restricted, contact inactive / missing / unreadable, links unreadable, column missing (active, no user, inactive,
  state unreadable), bound oid resolving here / elsewhere / on two contacts / lookup unreadable, bound oid plus a linked user.
- All 24 rows pass on master's gate, unchanged, before the edit. That run is the origin/master reference for AC3.
- Existing tests: none modified.

### Mutation checks (gate swapped in, run, restored; `--filter FullyQualifiedName~AssignTask`)

| Gate variant | Result |
|---|---|
| origin/master `b005279c4d` (the old file) | 195 / 195 pass: every table row matches master |
| This branch | 195 / 195 pass |
| Restricted read up front and passed into `ResolveAsync` (the issue's literal suggestion) | 40 fail, 23 of them table rows. "several-one-flagged-external-on-restricted" is emailed instead of several_users |
| SeveralUsers refusal removed | 3 fail: the "several" rows, plus the existing `AContactMappingToSeveralUsers_GetsNoEmail_AndNoBell_MustNotFire` |

### Runs (branch, `tests/unit/Sprk.Bff.Api.Tests`, filtered)

- Baseline on unmodified master, `FullyQualifiedName~AssignTask`: 171 / 171.
- `FullyQualifiedName~AssignTask`: 195 / 195 (171 existing, unmodified, plus 24 new).
- `FullyQualifiedName~Signals`: 5507 pass, 6 skipped (live-only), 0 failed. This includes the contract baselines.
- `FullyQualifiedName~ExternalAccess`: 6916 / 6916.
- `FullyQualifiedName~ContactUserRepresentation`: 15 / 15.
- `tests/Spaarke.ArchTests` (built explicitly first): 2793 / 2793.
- BFF build: 0 warnings, 0 errors.

## BFF hygiene (root CLAUDE.md §10, §11)

- Publish size: `dotnet publish -c Release` from fresh short-path worktrees `C:\wm215` (master `b005279c4d`) and `C:\wb215`
  (branch `2ad6ecd8aa`), each zipped with `Compress-Archive`. Master 43,578,744 B (194 files); branch 43,578,464 B (194 files);
  delta -280 B. Both worktrees were removed with `Remove-WorktreeSafe.ps1`.
- No package change, so no new CVE. No new service, interface, endpoint, DI registration or column. §11 does not fire: this is
  a pure modification.
- Placement: unchanged; the gate already lives in the BFF (Services/Signals).

## Review (Step 9.5, self)

- code-review and adr-check, run inline: no F-class findings. One K2 (found in passing, historical): the pre-193 vs master
  `Guid.Empty` difference above. It cannot be reached, it fails closed, and it is uac-r2's merged behaviour. Nothing to file.
- Class search: the other `ContactUserRepresentation` callers (`GrantAccessNotifier.cs:165`, `ContactInternalUserShare.cs:172`)
  already read `RepresentedUser.Eligibility`. `InternalUserShareCore.cs:129` classifies a `SystemUserRow` for /share-user,
  which is not a copy of this rule.
- Not run here: the adversarial reviewers (the main session runs two, plus uac-r2's review).

rework: none task 215
