# Task 079 — one contact's stored home tenant; the grant notifier and the Assign Task gate decide the home link from it

Issues: #2458 (ISS-024, DI-137) and #2491. Owner decision 2026-10-31: fix in R3, option 2 (a new one-contact read; `ContactSelect` is not widened).

## The change

- **New read** `IContactIdentityStore.ReadHomeTenantAsync(Guid contactId, CancellationToken ct) → Task<HomeTenantLookup>`.
  - Path: `contacts({id})?$select=contactid,sprk_externaltenantid`. The query is its own, and `ContactSelect` stays unchanged (a test pins it exactly).
  - Parse: `DataverseContactIdentityStore.InterpretHomeTenantRead` (pure), built on the `BuildInviteMarkerPath` / `InterpretMarkerLeg` pattern. The contact id is checked.
  - `HomeTenantLookup(LookupStatus Status, Guid? HomeTenantId)` carries a tenant only for `Read` on this contact's row with a non-empty GUID. `HomeTenantLookup.FromStored` is the one parse rule; the in-memory double and the plane tests use it too.
  - Every other answer means "could not tell". It is never read as "no tenant, so NotLinked":

    | Answer | Status | Tenant |
    |---|---|---|
    | null / absent / empty / non-GUID / empty GUID / non-string (a masked column reads null) | `Read` | none |
    | 404 | `Read` | none |
    | another contact's id, no id, not JSON, not an object, no body | `Failed` | none |
    | 400 "Could not find a property named …" | `ColumnMissing` (logged on every call, like the neighbouring home reads; the message text is the sign-in one and names `Set-WorkforceHomeIdentitySchema.ps1`) | none |
    | any other failure | `Failed` | none |

  - Its `ToString` never prints the tenant.
- **`GrantAccessNotifier.NotifyContactGrantAsync`**: on the external branch (`people.Count == 0`, nobody represents the contact), the new `HomeLinkBarsExternalEmailAsync` runs inside `EmailExternalContactGrantAsync`, after its own onboarded-with-email and caller-is-the-contact checks and before the link is built or anything is sent, and only for a binding that reads Bound + WorkforceHome. It reads the stored tenant, then `FindHomeLinkedSystemUsersAsync(tenant, bound oid)` → `ContactBindingDecision.DecideHomeLink`, the same read and decision the portal's home sign-in makes.
  - NotLinked → emailed as today.
  - Enabled → no email, `NotApplicable`. The user's own rights apply in the portal, so the internal notice is not sent either.
  - Disabled → no email, `NotApplicable`.
  - Ambiguous, could not tell, ColumnMissing, a failed link read, or a read that throws → no email, `Failed`. The grant stands; nothing throws.
- **`AssignTaskRecipientGate.HomeLinkAsync`**: the tenant now comes from `ReadHomeTenantAsync` instead of `contact.RawExternalTenantId`. That is the `GetContactAsync` row, which on the real store never carries the tenant. This change is inside the existing try. Nothing else in the gate changed: the oid still comes from the row, and the switch and every other branch are untouched. An unreadable tenant is `Denied` → `Unverified`, never `NotLinked`.
- **Doubles**: `InMemoryContactIdentityStore.ReadHomeTenantAsync` answers from `Contact.ExternalTenantId` through `FromStored`. It answers null under `MaskHomeColumns`, and has fault knobs `HomeTenantReadStatus` and `ThrowOnHomeTenantRead`, both cleared by `Reset`. `HeaderDrivenIdentityStore` (contract tests) delegates.

## Outcome-code choice

Enabled and Disabled → `NotApplicable`, whose meaning is "Nobody is to be told". Could-not-tell → `Failed`: "who they are could not be read". The route then reports `notificationFailed`, as for every other unreadable identity on this path. This follows the POML default, and the existing enum semantics agree with it.

## Component justification (CLAUDE.md §11)

1. **Existing**: `GetContactAsync` (`ContactSelect`, no tenant), `HomeContactSelect` (has the tenant, but only the sign-in's by-tenant search uses it) and `FindBindIntentAsync` (an own-query read of other field-secured columns) overlap. None of them returns one contact's stored tenant to a caller that already has the contact.
2. **Extension**: putting the tenant in `ContactSelect` would add it to every contact read, which `ContactBindingDecision.cs:268-272` forbids by design. Reusing the `HomeContactSelect` query would be a by-tenant search, not a by-id read. So this adds one method on the existing interface and store, following the `FindBindIntentAsync` pattern. There is no new service, endpoint, setting, package or DI registration.
3. **Cost of doing nothing**: a home-linked workforce contact is emailed a portal link for access that their portal view (the licensed user's) may not have (#2458). On the real store, every WorkforceHome contact is refused the Assign Task email, because the gate never sees the tenant (#2491).

**Placement**: in the BFF. This is a read on the existing identity store, called from two existing in-request paths. There is no background work (ADR-052 does not apply). ADR-010: no new DI registration.

## Seeding proofs

| Mutation | Result | Restored |
|---|---|---|
| Notifier: the `HomeLinkBarsExternalEmailAsync` call removed | 12 of 54 notifier tests fail, among them `AWorkforceHomeContact_WhoseHomePairAnEnabledLicensedUserCarries_IsNotEmailed_NorToldInApp` | yes |
| Gate: tenant read from the row again (`HomeTenantLookup.FromStored(contact.RawExternalTenantId)`) | `AWorkforceHomeAssignee_AsTheRealStoreReadsIt_NotLinked_WithAGrant_GetsOneEmail` fails (1 of 124) | yes |
| Red run (plumbing in place, gate and notifier not wired) | 13 fail: every new notifier case and the gate's NotLinked-with-grant case | — |

The gate's Enabled → Unverified and unreadable-tenant → Unverified cases already pass before the change, because the row has no tenant and the gate refused every WorkforceHome contact. They guard "never NotLinked" from now on. The store path and parse tests exercise new API, so before the change they did not compile.

## AssignTaskRecipientGatePlaneTests: one fixture line added (deviation)

This file uses a Strict `Mock<IContactIdentityStore>`. With the gate reading the tenant through the new read, the file as it stood fails 4 tests: the two derived-member emails, the removed-marker "workforce-home" control, and the Disabled → NoAccess case. The Strict mock refuses the unset call, and the gate fails closed.

The added setup in its `Identities(row, …)` helper (3 lines) answers the new read from the row's own `RawExternalTenantId` through `HomeTenantLookup.FromStored`. No test case or assertion changed, and every test there keeps its meaning. `WithNoReadableHomeTenant` ("not-a-guid") is now refused because the read answers "could not tell", not because the gate parses the row.

## Tests (all green)

| Filter | Passed |
|---|---|
| `GrantAccessNotifier*` | 56 / 56 |
| `AssignTaskRecipientGatePlaneTests` | 25 / 25 |
| `AssignTaskRecipientGateSeamTests` | 25 / 25 |
| `AssignTaskNotifierTests` (holds the gate's real-store cases) | 74 / 74 |
| `DataverseContactIdentityStore*` | 246 / 246 |
| `GrantExternalContactEmailTests` (integration, `/grant` endpoint) | 12 / 12 |
| `Spaarke.ArchTests`, filtered to ExternalAccess, Logging and Adr015 (after the review fixes) | 5 / 5 |
| `Spaarke.ArchTests`, before the review fixes | 1883 / 1884 in the full run. The one failure, the ISS-005 timing guard (`Control_ManyAssignmentsToOneLocal_ResolveInBoundedTime`, 5 s budget, took 9 s), ran while a BFF publish loaded the machine. Its class passes 129 / 129 in isolation. |

New tests:
- `DataverseContactIdentityStoreTests`:
  - `TheHomeTenantRead_SelectsTheContactIdAndTheHomeDirectoryOnly_ById_AndContactSelectIsUnchanged`
  - `TheHomeTenantRead_IsATenant_OnlyForThisContactsReadableGuid` (16 Dataverse-shaped bodies)
  - `TheHomeTenantRead_KeepsAFaultedRead_AndIgnoresItsBody`
  - `TheHomeTenantRead_OfAnErrorBody_IsColumnMissingOnlyForAMissingColumn` (real 400 bodies)
  - `AHomeTenantLookup_NeverPrintsTheTenant`
- `GrantAccessNotifierExternalTests`:
  - `AWorkforceHomeContact_NotLinked_IsEmailedExactlyAsACiamContactIs` (same subject, message and link as a CIAM contact)
  - `…EnabledLicensedUserCarries_IsNotEmailed_NorToldInApp`
  - `…OnlyADisabledUserCarries_IsNotEmailed`
  - `…WhoseHomeLinkCannotBeTold_IsNotEmailed_AndTheGrantStands_FailClosed` (9 cases: ambiguous; tenant empty, non-GUID, Failed, ColumnMissing, throws; link Failed, ColumnMissing, throws)
  - `ANonWorkforceHomeContact_IsEmailedAsToday_WithoutTheHomeReads`
  - `AContactNotEmailedToday_StaysUnemailed_WithoutTheHomeReads`
  - `AWorkforceHomeContact_ThatAnInternalUserRepresents_NeverReachesTheHomeCheck`

  Every WorkforceHome case also asserts that no log line carries the email, name or tenant. The test logger now records every level.

  The WorkforceHome row of `ContactBoundOnAPortalPlane_IsEmailed` moved into the NotLinked test. With the check in place it needs a home-link answer.
- `AssignTaskNotifierTests` (its harness's `GetContactAsync` row has no tenant, which is the real store's shape):
  - `AWorkforceHomeAssignee_AsTheRealStoreReadsIt_NotLinked_WithAGrant_GetsOneEmail`
  - `…WhoseHomePairALicensedUserCarries_IsUnverified_MustNotFire`
  - `AWorkforceHomeAssignee_WhoseStoredTenantCannotBeRead_IsUnverified_NeverNotLinked_MustNotFire` (5 cases)
  - `ACiamAssignee_IsDecidedAsToday_WithoutTheHomeReads`

The full BFF unit suite was not run (project gotcha 2026-10-11).

## Review fixes (two adversarial passes; both found item 1)

1. **Order of checks.** The home check ran before `EmailExternalContactGrantAsync`'s own onboarded-with-email and caller-is-the-contact checks. A Bound + WorkforceHome contact who would never be emailed (no address, or the granter themself) got two extra reads and, when they failed, `Failed` (`notificationFailed: true` on /grant and /invite-and-grant) instead of `NotApplicable`. The home check now runs inside `EmailExternalContactGrantAsync`, after those two checks and before `TryBuildLink`. Every other case is unchanged. Tests: `AWorkforceHomeContactWithNoEmailAddress_IsNotApplicable_WithoutTheHomeReads_EvenWhenThoseReadsWouldFail` and `AWorkforceHomeContactThatIsTheCaller_IsNotApplicable_WithoutTheHomeReads_EvenWhenThoseReadsWouldFail`; both failed before the move (`Failed` instead of `NotApplicable`) and pass after.
2. **Stale remark.** The `IsOnboardedWithEmail` doc said a WorkforceHome binding "IS emailed"; it now says it is emailed only when no licensed user carries its home pair.
3. **Store HTTP wiring.** `TheRealStore_ReadHomeTenant_DecidesFromTheContactReadsAnswer` runs the real store's `ReadHomeTenantAsync` over the HTTP harness: 200 with this contact's GUID tenant, 200 masked (null), 404, 400 missing-column, 403 and 500. It also asserts the one request is the read's own path, with no readability proof and no write.
4. **No tenant in a store log line.** The store's generic `GetAsync` already takes a fixed `logLabel` (used by the email lookups). The home link lookup (its path carries the home tenant and oid) and the new home tenant read now pass one, so a failed or throwing read logs the label and the status, never the path or body. No other read's log line changed. Test: `AFailingHomeLinkRead_LogsAFixedLabel_NeverTheTenantOrTheOid`; it failed before the label (the path with the tenant and oid was logged) and passes after. Its control shows the sign-in's home contact lookup still logs its path.

## Known limits (filed separately: #ISSUE)

- `InMemoryContactIdentityStore.GetContactAsync` rows still carry the tenant that the real `ContactSelect` never returns, so a test that reads it from the row can pass where the real store would not.
- `AssignTaskRecipientGate.ComposePortalPrincipalAsync` (~:496) still passes the row's empty tenant into the WorkforceHome principal. No consumer reads it today.
- Whether `sprk_externaltenantid` is readable by id under the BFF identity is to be confirmed on dev at deploy. A masked column reads null, which this read treats as could-not-tell, so it fails closed (no email, Unverified).
- The sign-in path's other home reads (the home contact lookup and the guest systemuser lookup) still log their paths, which carry the home tenant and oid. Left as is (outside this change).

## Publish-size delta

Measured to the convention in `.claude/constraints/azure-deployment.md`:
- Command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent (the project default), Release.
- Both sides came from fresh detached worktrees at short paths: master `d0c1c5dc3c` at `C:\wt079-base`, branch `fad3a81d6a` at `C:\wt079b`. Both were removed afterwards with `Remove-WorktreeSafe.ps1`.
- Zipped with `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included.

| Side | Files (PDBs) | Zip |
|---|---|---|
| master `d0c1c5dc3c` | 192 (4) | 40,985,171 B = 39.087 MB |
| branch `fad3a81d6a` | 192 (4) | 40,987,948 B = 39.089 MB |
| **delta** | 0 | **+2,777 B (+0.003 MB)** |

Both sides are far below the 55 MB review line and the 60 MB ceiling. No package was added, so no new CVE surface. BFF hygiene §10 and NFR-01 are verified.

## Open / observed

- `AssignTaskRecipientGate.ComposePortalPrincipalAsync` still builds the WorkforceHome principal with `contact.RawExternalTenantId`, which is empty on the real store. Neither composition (`AccessibleRecordSetService`) nor the events list reads `WorkforcePrincipal.TenantId`, so the verdict does not change. The brief scoped the gate to `HomeLinkAsync`, so this was left as is.
- `InMemoryContactIdentityStore.GetContactAsync` still returns the tenant on its rows, as before; other tests depend on it. The gate and notifier no longer read it.
- `notes/defer-issues.md` ISS-024 is not updated here; that belongs to the caller's close-out.
- No live change; the fix ships with a later dev BFF deploy.
