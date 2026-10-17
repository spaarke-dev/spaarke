# Task 047 — `POST /api/v1/external/registration` (ADR-028 A9)

Branch `task/047-registration` (base `1d9a06fbf`). The design is `notes/a9-bff-design.md` §4 ("Registering to an existing contact") and §6. The owner decisions are §11 items 1–2 and those of 2026-10-15.

Commits:
- `c100adc99`: the plane, the decision, the store and the binder.
- `3e260086a`: the route, its tests and the ArchTests.
- The commit that adds this file.

## Placement (CLAUDE.md §10)

The route goes in the BFF. It validates the stamp's WorkforceHome user token and writes the stamp's Dataverse app-only, next to the existing create path (`ContactIdentityBinder` / `DataverseContactIdentityStore`). There is no OBO and no Graph call, and no email is sent.

New surface:
- one endpoint file;
- one DTO file;
- four store methods;
- one binder method;
- one options property.

There is no new DI registration and no new package.

## §11 justification (three questions per new surface)

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ExternalRegistrationEndpoints.cs` (POST `/registration`) | `ExternalIntakeEndpoints` (the reference); the `/api/v1/external` group in `ExternalAccessEndpoints` | It cannot sit on the existing group: `CallerPrincipalAuthorizationFilter` refuses an unresolved caller (`registration_required`) before the handler runs. So it is a separate small group with the same policy (the POML requires this). | A9 forbids the silent create. Without the route, no workforce contact can register, and existing contacts would be duplicated. |
| `ExternalRegistrationDtos.cs` | `ContactGrantDtos` (closed-DTO precedent) | It follows that precedent; no existing DTO has these fields. | Without a closed body, a body `email` could bind. |
| `ContactIdentityBinder.RegisterHomeContactAsync` (+ private helpers) | `RunUnlinkedAsync` (the A8 create/bind loop) | It reuses that loop's store, flags (`WriteFlagsAsync`, `FlagKeyMirrorHoldersAsync`), probe and 412/key pattern. A8's `Decide` cannot be reused as-is: it creates silently, binds without confirmation, and knows no licensed-user, tenant or owner step. | There would be no ordered decision for §6 steps 2–4. |
| `ContactBindingDecision.DecideLicensedUserEmail` / `DecideRegistrationEmail` (+ row/lookup records) | `Decide` E3–E10, `DecideInvite` | They are pure siblings of `DecideInvite`, with the same reference-lookup (E10) shape. | §6 step 2 and step 3 would have no testable rule. |
| `IContactIdentityStore.FindEnabledSystemUsersByEmailAsync` | `GetSystemUserAsync` (by id), `ScanInteractiveSystemUsersAsync` (by page) | Neither filters by email. | The licensed-user refusal could not be decided. |
| `IContactIdentityStore.FindDefaultOwnerTeamsAsync` | `RecordOwnershipResolver.ResolveDefaultOwnerTeamAsync` (private, SDK, five resolver implementations) | Widening `IRecordOwnershipResolver` would touch five implementations, for a BU-only read the ownership context does not model. This is the same filter, on the store the binder already writes through. | There would be no owner, which the POML requires (`ownerid` = BU default team). |
| `IContactIdentityStore.ProbeHomeReadabilityAsync` | `ProbeBindingReadabilityAsync` (binding column only) | Same pattern, extended to the A9 secured columns. | A masked `sprk_externaltenantid` or secured pair would make a registered user look unregistered, and the route would create a duplicate (046 review K). |
| `IContactIdentityStore.BindHomeOidAsync` | `BindOidAsync` | `BindOidAsync` has no tenant parameter, and the home bind must write the tenant in the same request. | A bound WorkforceHome contact without its tenant never resolves (046 reads match on the tenant). |
| `NewContactDetails` init props (Department, ExternalTenantId, OwningTeamId); `OwnerTeamLookup` | `NewContactDetails` | Extended, not duplicated. | The create could not carry its tenant or owner. |
| `DataverseContactIdentityStore.BuildWriteRequest` (pure) | Was inline in `WriteAsync` | An extraction, with no behaviour change. | The "never `MSCRM.SuppressDuplicateDetection`" rule could not be pinned without an HTTP double (ADR-038 B1). |
| `ExternalAccessOptions.CustomerBusinessUnitId` | `ExternalAccessOptions` (the POML says not to add an options class) | A new property on the existing options. | There would be no owner configuration. |
| `IdentityPlaneMarker.WorkforceHome` | The raw `100000002` in `DecideHomeContact` | The enum member replaces the raw value. | `ReadBinding` would read every WorkforceHome contact as Unreadable (see below). |

## Files changed

- **src:**
  - `Api/ExternalAccess/ExternalRegistrationEndpoints.cs` (new)
  - `Api/ExternalAccess/Dtos/ExternalRegistrationDtos.cs` (new)
  - `Api/ExternalAccess/ExternalAccessEndpoints.cs` (mount)
  - `Api/ExternalAccess/GrantAccessNotifier.cs` (doc remark only)
  - `Infrastructure/ExternalAccess/ContactBindingDecision.cs`
  - `Infrastructure/ExternalAccess/ContactIdentityBinder.cs`
  - `Infrastructure/ExternalAccess/ContactIdentityStore.cs`
  - `Infrastructure/ExternalAccess/ExternalAccessOptions.cs`
  - `appsettings.template.json` (comment only: the setting is deliberately absent)
- **tests:**
  - `tests/unit/.../Api/ExternalAccess/ExternalRegistrationEndpointsTests.cs` (new)
  - `DataverseContactIdentityStoreTests.cs`
  - `ContactIdentitySchemaAgreementTests.cs` (it adds the A9 script's `$WorkforceHomeValue` to the plane options)
  - `GrantAccessNotifierExternalTests.cs`
  - `ContactBindingDecisionTests.cs`
  - `InMemoryContactIdentityStore.cs`
  - `HeaderDrivenIdentityStore` (in `ExternalAccessContractTests.cs`)
  - `tests/Spaarke.ArchTests/AuthenticationSchemeCensusTests.cs`
  - `RouteAuthorizationGuardTests.Ledger.cs` (125 → 126)

## Acceptance criterion → test

All tests are in `ExternalRegistrationEndpointsTests` unless another class is named. They run through the real route, the real 046 resolver, the binder and the decision, over the in-memory row store.

| Criterion | Test |
|---|---|
| 201: email, tid, oid, plane, owner, no parent | `NoMatch_Creates_OneWorkforceHomeContact_OwnedByTheBusinessUnitDefaultTeam`; payload keys (no `parentcustomerid`): `DataverseContactIdentityStoreTests.TheWorkforceHomeCreate_CarriesTenantOwnerAndDepartment_InLowercaseD_AndNoParentCustomer` |
| Already resolved → 200, no write | `AlreadyResolved_ByItsWorkforceHomeContact_…`, `AlreadyResolved_ByAStoredSystemUserLink_…` |
| Resubmit / 412 race → 200, same id, one contact | `Resubmit_Returns200_WithTheSameContact_…`, `ConcurrentLoser_TheKeys412_ReReads_…` |
| Licensed: internal / domainname → 403; disabled user does not trigger | `TokenEmail_OfAnEnabledSystemUser_InEitherField_…` (theory: internal, domainname), `TokenEmail_OfADisabledSystemUser_IsNotRefused_AndRegisters` |
| Bind with confirm → 200; without → 409, no write | `OneBindableContact_WithConfirmation_IsBound_NoNewContact`, `OneBindableContact_WithoutConfirmation_Is409ExistingRecordFound_NoWrite` |
| Two / bound / linked / External → 403, flagged EmailAmbiguous, nothing bound or created | `UnbindableMatch_Is403RegistrationContactConflict_Flagged_NothingBoundOrCreated` (theory ×4) |
| No email (even with `preferred_username`) → 403; body email → 400; names → 400 | `TokenWithoutEmail_EvenWithPreferredUsername_…`, `InvalidBody_Is400_NoWrite` (×9: body email, unknown prop, missing first, blank first, missing last, 51-char first, 51-char last, BEL, newline), `OverlongDepartment_Is400` |
| BU absent → 500, nothing created | `ExternalRegistrationOwnerUnconfiguredTests.NoMatch_WithoutTheBusinessUnitSetting_…` |
| Spaarke-tenant / CIAM → 403; anonymous → 401 | `NonHomePrincipal_Is403_AndNothingIsRead` (Bearer, Ciam), `Anonymous_Is401` |
| Never `MSCRM.SuppressDuplicateDetection` | `DataverseContactIdentityStoreTests.TheWriteRequest_NeverOptsInToDuplicateDetection` (create and bind) |
| ArchTest only-caller | `AuthenticationSchemeCensusTests.RegistrationIsTheOnlyCallerOfTheWorkforceHomeWrites` (must-fire control: `TheOnlyCallerGuard_SeesACallerItDoesNotAllow`) |

Tests beyond the stated criteria, one line each:
- **`UnreadableBindingOrA9Columns_Denies_AndNeverCreates`** (×3: binding masked, A9 masked, probe failed). This is the carried 046 constraint: "if unreadable, deny; never create".
- **`ClassifyHomeProbe_…`.** The probe's own rule.
- **`TheLicensedUserRead_…`, `TheOwnerTeamRead_…`, `TheHomeBind_…`, `TheWorkforceHomeCreate_Refuses…`.** These pin the Dataverse queries and payloads that the in-memory double does not exercise (ADR-038 B1).
- **ReadBinding row `100000002 → Bound`, `Invite_AWorkforceBoundContact_IsRefused(WorkforceHome)`, `ContactBoundOnAPortalPlane_IsEmailed(WorkforceHome)`.** These are the ReadBinding-caller decisions below.

## Seeding proofs

Each proof ran against the committed `3e260086a`, then was restored with `git checkout -- <file>`, then `git diff --stat -- src/` came back empty.

| Seed | Result |
|---|---|
| (a) Drop the `domainname` leg in `DecideLicensedUserEmail` | 1 failed / 31 passed: `TokenEmail_OfAnEnabledSystemUser_InEitherField_…(field: "domainname")` |
| (b) Bind without the `ConfirmExistingRecord` check (`if (false && …)`) | 1 failed / 31 passed: `OneBindableContact_WithoutConfirmation_Is409ExistingRecordFound_NoWrite` |
| (c) `IsWorkforcePlane` back to Workforce-only | 1 failed / 132 passed: `Invite_AWorkforceBoundContact_IsRefused(plane: WorkforceHome)` |

**Before → after (route tests).** With `app.MapExternalRegistrationEndpoints()` commented out, 31 of 32 failed. The one that passed is `Anonymous_Is401`: an unmapped route still gets 401 from the fallback policy. With the route mounted, 32 of 32 pass.

The new store, payload and probe tests cannot compile against the base, because they test new surface.

## Counts

- **Filtered affected suites:** 634 / 634 pass. Filter: Registration, ContactBindingDecision, DataverseContactIdentityStore, SchemaAgreement, GrantAccessNotifier, WorkforcePrincipalResolver, ContactIdentityBinder, IdentityBinding, ExternalInvite, CallerPrincipalResolver, ExternalAccessContract.
- **ArchTests (census + route ledger):** 93 / 93.
- **Full unit suite:** see the final report.
- **`dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`:** 0 warnings, 0 errors.
- **`dotnet list … package --vulnerable --include-transitive`:** no vulnerable packages. No new package.

## Publish size

`dotnet publish -c Release -r linux-x64 --self-contained false`, run on the branch and on a fresh `git archive` of base `1d9a06fbf` (scratch directories under `C:\tmp`, deleted afterwards).

| | zip bytes | files | raw bytes |
|---|---|---|---|
| base `1d9a06fbf` | 38,931,487 | 192 | 130,034,384 |
| branch | 38,952,928 | 192 | 130,103,236 |
| delta | **+21,441** | 0 | +68,852 |

## ReadBinding callers and the home plane

`IdentityPlaneMarker.WorkforceHome = 100000002` is now a known plane, so `ReadBinding` reads such a contact as **Bound**. Before this change it read as Unreadable.

| Caller | Before (Unreadable) | Now (Bound, plane WorkforceHome) | Decision |
|---|---|---|---|
| `Decide` E5–E7 (A8 workforce and CIAM binder; same-email WorkforceHome contact) | E5 collision `BindingUnreadable` | E7 collision `BoundToDifferentOid` | Still refused and flagged; only the reason is truer. |
| `DecideExistingLink` L7/L8 (systemuser linked to a WorkforceHome contact) | L7 `BindingUnreadable` | L8 `LinkedContactBoundToDifferentOid` | Still refused and flagged (048 owns the home link). |
| `DecideInvite` | `InviteContactBindingUnreadable` (refused) | `InviteWorkforceBoundContact` (refused, flagged) | Explicit: `IsWorkforcePlane` includes WorkforceHome; an employee is never re-invited as a partner. |
| `CollisionStillHolds` (`InviteMatchesWorkforceContact`) | — | Holds for WorkforceHome | Same rule as `DecideInvite`. A `BindingUnreadable` flag on a WorkforceHome row now clears (the row is readable). |
| `GrantAccessNotifier.IsOnboardedWithEmail` | Never emailed | **Emailed** | A WorkforceHome contact registered through, and signs in to, the same external portal the email links to. **Owner may wish to confirm.** |
| `GrantAccessNotifier` self-skip (~290) | `Binding.Oid` null | Compares the home oid | Correct: a home grantee who is the caller is skipped. |
| `ExternalInviteService.AssessAsync` | `Failed(InviteContactBindingUnreadable)` | `NotNeeded` | Correct: no CIAM invite for a home-registered contact. |
| `ContactIdentityBinder` :341 (expanded linked contact), :473 (`BindInvitedContactAsync` refuses non-Unbound) | — | Unchanged semantics | — |
| `MirrorHeldWithoutBinding` | Mirror looked squatted | Not squatted when bound to that oid | Correct. |
| `DecideOnOid` / `DecideBoundContact` | Plane-agnostic | Plane-agnostic | **Known limit:** an A8-plane caller whose oid equals a WorkforceHome contact's oid resolves to it. This needs a GUID collision across directories; it is pre-existing behaviour, not widened here. |
| `DecideHomeContact` | Raw-value match | Unchanged | — |

## Escalation triggers (no live reads; per the brief)

- **Systemuser email field.** The owner decided this on 2026-10-15. In dev, 9 of 11 enabled users have `internalemailaddress = domainname` and 2 differ, so both fields are compared case-insensitively against ENABLED users only. Not re-read live.
- **Duplicate detection.** The owner decided this on 2026-10-15. Dev has 3 published OOB contact rules (name, phone, email). They do not run on Web API writes unless the caller opts in. The store never sends `MSCRM.SuppressDuplicateDetection` (pinned by a test). Not re-read live.

## Known limits / open questions

1. **EmailAmbiguous flag on a single match.** Resolved in fix round 1 (see below): a single unbindable match is flagged by its cause.
2. **The email-claim rule.** The design (§6 line 128) says "no `email` and no `preferred_username` → `registration_email_missing`". The POML and the brief say the `email` claim only. The POML/brief rule was followed: `preferred_username` and `upn` are never read.
3. **Reason-code strings** use the `sdap.access.` prefix (the `registration_required` precedent), for example `sdap.access.licensed_user_link_required`. SPA task 058 must match the full string.
4. **Codes beyond the closed set, one line each:**
   - `registration_home_identity_required` (403): a Spaarke-tenant or CIAM principal, which the POML requires to be 403.
   - `registration_invalid` (400 `errorCode`): a validation failure.
   - `registration_unavailable` (503): a read or write did not land; retryable, nothing registered.
   - `registration_environment` (500): masked or missing binding/A9 columns, or no alternate key; an operator fix.
5. **Bound to the caller's oid but not resolved.** If the email's one contact is bound to the caller's oid but the (tid, oid) re-read does not resolve it (for example, bound on another plane), the answer is 403 conflict without a flag.
6. **Two default owner teams.** If the business unit has 2 default owner teams (or 0), the route answers 500 `registration_owner_unconfigured`. A failed team read answers 503.
7. **Test host.** It sets `RouteHandlerOptions.ThrowOnBadRequest = false`. The base host is Development, where Minimal API throws on an unbindable body, which production does not.
8. **Plain-text oid in shared logs.** The shared binder helpers (`RecordPartyAsync`, `FlagKeyMirrorHoldersAsync`) log the colliding oid in plain text when a registration flags. The new registration log lines hash tid/oid and never log the email or names.
9. **What the only-caller IL rule cannot see.** It cannot see reflection or dynamic invocation. The WorkforceHome create is fingerprinted by setting `NewContactDetails.ExternalTenantId`/`OwningTeamId`, which `CreatePayload` requires for that plane.
10. **Not tested:** two simultaneously authenticated identities on one principal.

## PR overlaps (`gh pr list --state open`)

- **#1982** ("Task 154: worklist …") also edits `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs`. Expect a conflict on `ExpectedEndpointFileCount` and its changelog when both merge.
- No other open PR touches the files changed here.

## Fix round 1

Two adversarial passes (pass 1 and pass 2) produced these findings. All are fixed in this commit.

| Id | Finding | Fix | Tests |
|---|---|---|---|
| F-A | A single unbindable match was flagged `EmailAmbiguous`, but `CollisionStillHolds` keeps `EmailAmbiguous` only while more than one active contact carries the email. IdentityLinkReconciliationJob therefore cleared the flag on its next run (a WorkforceHome party's home oid is never a systemuser's or another binding's oid). | `DecideRegistrationEmail` returns `FlagReason` by cause: bound to another oid on any plane = `BoundToDifferentOid`; unreadable binding = `BindingUnreadable`; linked to a systemuser = `LinkedToOtherUser`; two or more matches = `EmailAmbiguous`. The binder writes that reason. | `ContactBindingDecisionTests`: `DecideRegistrationEmail_UnbindableMatch_IsFlaggedByCause_...` (6 shapes), `CollisionStillHolds_ASingleMatchReason_HoldsForAWorkforceHomeParty_...` (3 reasons, with a clears-when-cause-gone control), `CollisionStillHolds_EmailAmbiguousForAWorkforceHomeParty_StillClearsWhenOnlyOneCarrierRemains`. Route: `UnbindableMatch_Is403RegistrationContactConflict_FlaggedByCause_...` (6 shapes, including a WorkforceHome contact bound to a different oid, which was untested). |
| F-B | `BindingReadability.Failed` (a transient 429/5xx) mapped to `DenyBindingColumnMasked` (500 `registration_environment`, "contact your administrator", FLS error log). `ProbeHomeReadabilityAsync` folded a missing column into `Failed`. | New `BindingReadability.ColumnMissing`, reported by `ClassifyHomeProbe` / `ProbeHomeReadabilityAsync` (a missing column wins over a transient failure on the other leg). The binder maps `Failed` to `DenyContactLookupFailed` (503 `registration_unavailable`, warning log); masked stays `DenyBindingColumnMasked` and a missing column maps to `DenyBindingColumnMissing`, both 500. | Route: the old `probe-failed` case moved to `TransientProbeFailure_Is503Unavailable_Retryable_AndNeverCreates` (home and binding probe); `MaskedBindingOrMissingA9Columns_Is500Environment_AndNeverCreates` gained `home-column-missing`. Store: `ClassifyHomeProbe_...` asserts `ColumnMissing` vs `Failed`. |
| F-C1 | A 403 (or a 400 privilege error) from Dataverse on the CREATE (owner team without a role with contact read; app user without Assign) was a 503, so callers retried a permanent fault. | New `StoreWriteStatus.Forbidden` from `DataverseContactIdentityStore.IsPermissionFault` (403, or 400 naming a privilege / `0x80040220`; 429, 5xx and network faults stay `Failed`). The binder maps it to `DenyContactCreateUnavailable` (500 `registration_environment`). | Store: `IsPermissionFault_...` (7 cases). Route: `Create_RefusedForAMissingPrivilege_Is500Environment_NotRetryable`, control `Create_FailedTransiently_StaysA503Unavailable`. |
| F-C2 | `ExternalAccess:CustomerBusinessUnitId` could name the Secure Record business unit. | The create reads the business unit named by `SecureRecordOwnerTeam.BusinessUnitName(configuration)` (new `FindBusinessUnitsByNameAsync`, `SecureRecordOwnerTeam.BusinessUnitFilter`, top 2) and refuses by ID, as `RecordOwnershipResolver` does: the customer unit is the Secure Record unit, or the name is ambiguous = 500 `registration_owner_unconfigured`; an unreadable answer = 503; no such unit = proceeds. The check runs before the owner team is read. | Route: `CustomerBusinessUnit_IsTheSecureRecordBusinessUnit_...`, `SecureRecordBusinessUnit_Ambiguous_...`, `SecureRecordBusinessUnitRead_Failed_...`, control `CustomerBusinessUnit_DifferentFromTheSecureRecordBusinessUnit_Registers`. Store: `TheSecureBusinessUnitRead_...`. |
| Pass 1 | Two route tests were missing. | Added `FailedLicensedUserRead_Is503Unavailable_NothingWritten` and `FailedOwnerTeamRead_Is503Unavailable_NothingCreated`. | Same. |

Section 11 for the one new store method (`FindBusinessUnitsByNameAsync`). Existing: `RecordOwnershipResolver.ResolveSecureBusinessUnitAsync` and `RegistrationDataverseService.EnsureNotSecureRecordBusinessUnitAsync`, which use the SDK client and a different raw-HTTP client. Extension: neither is reachable from this store's HTTP seam without a new dependency, so the same name rule and filter (`SecureRecordOwnerTeam`) are reused through the store's own `GetAsync`. Cost of doing nothing: a registrant's contact could be owned by a team in the Secure Record business unit.

Before / after (filtered run `ExternalRegistration|ContactBindingDecision|DataverseContactIdentityStore`, with the new behavior reverted in the binder, decision and `IsPermissionFault`): before, 16 failed of 275; after, 0 failed. Full filtered set (ExternalRegistration, ContactBindingDecision, IdentityLink, ContactIdentityBinder, DataverseContactIdentityStore, WorkforcePrincipalResolver): 412 passed, 0 failed. ArchTests `AuthenticationSchemeCensus|RouteAuthorizationGuard`: 93 passed, 0 failed. `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors.

Not changed in this round (carried): a missing column on the BINDING probe (`ClassifyProbe`) still reads as `Failed` and so answers 503 on this route; the 046 resolver's step 1 already answers a missing A9 column before registration. A 403 on the BIND (PATCH) of an existing contact still maps to `DenyContactBindFailed` (503); the brief covers the CREATE only.

## Review known limits

Filed or carried from review; one line each.

- K1: `removeUnverifiedEmailClaim` is not enforced in provisioning (to be filed by the main session).
- K1: RESOLVED in fix round 2 - a deactivated contact carrying the email now refuses the registration and is flagged `InactiveContactEmailMatch` (owner 2026-10-15). The schema option must be applied before 047 deploys.
- K2: There is a systemuser-link race between the reference lookup and the bind.
- K2: A pending CIAM invitee can be bound.
- K2: Two directories with the same mail registering at the same time can create two contacts.
- K2: Raw tid/oid appear in the shared binder flag logs and in the 046 resolver logs (to be filed).
- K2: The only-caller ArchTest pins the interface, not the concrete store method or the generic `BindOidAsync(WorkforceHome)`.

## Fix round 2

Owner decision 2026-10-15, "Refuse, flag for admin": when the token email matches no ACTIVE bindable contact but an INACTIVE contact (`statecode` 1) carries it, registration is refused (403 `registration_contact_conflict`, the SPA shows "contact your administrator"), nothing is created or bound, and the inactive contact is flagged. Reactivation stays an operator decision. Second owner decision, same day: the flag reason is a NEW choice value (option 1).

**Why a new value.** `ContactBindingDecision.CollisionStillHolds` returns `false` for every flagged inactive contact before its per-reason switch ("the person was removed, the collision is moot"), and `KeyMirrorConflict` holds only on a mirror carrying the party's oid. The job scans flagged contacts in every state, so any existing reason would be cleared on the next run although nothing changed. Control test: `CollisionStillHolds_Control_EveryOtherReason_IsClearedOnAnInactiveContact` and `Control_AnotherReasonOnAnInactiveContact_IsClearedByTheJob`.

| Id | Change | Where |
|---|---|---|
| R2-1 | `IdentityCollisionReason.InactiveContactEmailMatch = 100000012` (100000012 was unused in the enum and in the schema script; the other 100000012 hits in the repo are unrelated choices). Deny code `sdap.access.deny.contact_inactive_email_match` in `DenyCodeFor`. | `ContactBindingDecision.cs` |
| R2-2 | `CollisionStillHolds`: a case BEFORE the inactive shortcut. The flag holds while the flagged contact is inactive and still carries an email; it clears when the contact is reactivated or its email is blank. The party records no email (it holds oid, plane, reason, time), so a change to ANOTHER non-blank address cannot be detected; storing an email (or a hash) in the parties JSON would change the flag contract and was not done. | `ContactBindingDecision.cs` |
| R2-3 | `DecideInactiveRegistrationEmail` (pure): inactive rows = Conflict over every inactive row with the new reason; none = Create; a failed read = Deny (503, never a create). | `ContactBindingDecision.cs` |
| R2-4 | `IContactIdentityStore.FindInactiveContactsByEmailAsync` + `BuildInactiveEmailLookupPath` (`statecode eq 1`, same columns and literal escaping, top 2). Binder: when `DecideRegistrationEmail` says Create (no ACTIVE match), the inactive read decides before the create; active matches decide as before and the inactive read is not reached. The Conflict handling moved into `RefuseAndFlagAsync`, shared by both paths. | `ContactIdentityStore.cs`, `ContactIdentityBinder.cs` |
| R2-5 | Fail safe: the flag write failing (the option is not provisioned yet, a 400 invalid option value) does not change the answer: 403, nothing created or bound. `RecordPartyAsync` logs a warning naming `scripts/Set-ContactIdentityBindingSchema.ps1` for this reason. | `ContactIdentityBinder.cs` |
| R2-6 | Schema script: option `100000012 = 'Inactive contact email match'` in `$ReasonOptions`; its existing add-missing-option path covers an environment whose choice predates it. **NOT run against any environment; the main session runs it with owner approval, and it must be applied BEFORE 047 deploys** (until then the flag cannot be written and the operator sees only the warning log). | `scripts/Set-ContactIdentityBindingSchema.ps1` |
| R2-7 | Round-1 leftover: a `Forbidden` (403, or a 400 naming a privilege) on the BIND PATCH of an existing contact was 503 `registration_unavailable`; it now logs an error and answers `DenyContactCreateUnavailable` = 500 `registration_environment`, like the create. | `ContactIdentityBinder.cs` |

Section 11 for `FindInactiveContactsByEmailAsync`. Existing: `FindActiveContactsByEmailAsync`, filtered `statecode eq 0`, used by the invite service, the binder's token planes and the reconciliation job. Extension: widening that read to every state would hand inactive rows to callers that rely on it being active-only (the invite service, the job's carrier counts), so the registration route gets its own minimal pair read instead. Cost of doing nothing: a deactivated person registering again gets a second contact beside the deactivated one, and the operator never learns of it.

**Tests.** `ExternalRegistrationEndpointsTests`: inactive-only match is 403 and flagged, nothing created or bound (3 shapes: confirm true/false, two inactive, upper-case email); inactive plus one active bindable behaves as before (409, then bind) and leaves the inactive contact alone; inactive plus an active bound match conflicts by the active cause; inactive read failed is 503; inactive match on a different email registers; flag option not provisioned still refuses 403 and warns naming the script; bind `Forbidden` is 500 `registration_environment`; control bind transient failure stays 503. `ContactBindingDecisionTests`: the three decision cases, `CollisionStillHolds_InactiveContactEmailMatch_...` (holds while inactive with an email; clears on reactivation and on a blank email; failed read keeps it) and the every-other-reason control. `IdentityLinkReconciliationTests`: the job keeps the flag across runs while inactive and clears it on reactivation; control that another reason is cleared. `DataverseContactIdentityStoreTests`: the inactive path is pinned (`statecode eq 1`, escaping, columns) and the active path is unchanged. `InMemoryContactIdentityStore` gained the inactive read (it already modelled `statecode`), `FailInactiveEmailLookup` and `BindStatusOverride`; the contract-test wrapper forwards the new method.

**Before / after (seeding proofs, each reverted afterwards; `git diff -- src` matched the finished change each time).**
- Inactive read dropped from the Create path: 4 failed of 429 (the three inactive-only route cases and the not-provisioned case); restored, 0 failed.
- Bind `Forbidden` branch disabled: 1 failed (`Bind_RefusedForAMissingPrivilege_...` got 503, expected 500); restored, 0 failed.
- `CollisionStillHolds` new case disabled: 2 failed (the decision test and the job-level test); restored, 0 failed.
- Final filtered set (ExternalRegistration, ContactBindingDecision, IdentityLink, ContactIdentityBinder, DataverseContactIdentityStore, WorkforcePrincipalResolver, plus ContactIdentitySchemaAgreement and ExternalAccessContract): 512 passed, 0 failed. ArchTests `AuthenticationSchemeCensus|RouteAuthorizationGuard`: 93 passed, 0 failed. `dotnet build src/server/api/Sprk.Bff.Api/ -warnaserror`: 0 warnings, 0 errors. `ContactIdentitySchemaAgreementTests` (the enum equals the script's option list) passes with the new value.

**Deployment prerequisite.** Run `scripts/Set-ContactIdentityBindingSchema.ps1` (owner approval) on every environment that serves the registration route before 047 deploys.

## Fix round 3

From the scoped re-check of round 2 (no F-class finding; two small fixes and four known limits).

- **Solution option set.** `src/dataverse/solutions/SpaarkeMaster/OptionSets/sprk_identitycollisionreason.xml` stopped at 100000011; it now carries `100000012` in the siblings' exact shape (the solution export leaves every label description empty, so the new entry does too). `ContactIdentitySchemaAgreementTests.TheSolutionOptionSet_CarriesEveryCollisionReason_AsTheScriptDoes` compares the enum, the schema script's `$ReasonOptions` and this XML. Seeding proof: with the XML entry removed the test fails (1 of 157); restored, it passes.
- **Schema hint only for an unprovisioned option.** The store's flag-write failure text is `"{status}: {body}"`. New pure `DataverseContactIdentityStore.IsOptionValueOutOfRange` recognises Dataverse's "The value N of 'col' ... is outside the valid range" (seen live on `sprk_event.sprk_priority`, task 097; the exact text for a collision-reason column was not captured live, so the match is on the phrase only). The binder emits the "apply `scripts/Set-ContactIdentityBindingSchema.ps1`" warning only for a plain `Failed` status carrying that text; any other failure (a throttle, a 5xx) gets the generic error log with its status and text. Tests: `InactiveMatch_WhenTheFlagOptionIsNotProvisioned_...` (hint present), `InactiveMatch_WhenTheFlagWriteFailsForAnotherReason_...` (no hint, error log kept), and `IsOptionValueOutOfRange_...` (6 cases). Seeding proof: relaxing the condition fails the no-hint test; restored, it passes.

Known limits (K, not fixed here):
- K1: An unclassified 403 on the business-unit read (`FindBusinessUnitsByNameAsync`) is a plain read failure, so it answers 503 (retryable) although a permission fault would be permanent.
- K2: The business-unit name is not URL-encoded in the filter (`FindBusinessUnitsByNameAsync`, and the same in `RegistrationDataverseService.cs:830`). Harmless for the default name "Secure Record"; a name with a reserved character would break the read.
- K2: The inactive-contact read is `$top=2`, so with three or more deactivated contacts on one email only two are flagged. The refusal itself is unaffected.
- K2: When the base binding column is missing, the schema hint names `Set-ContactIdentityBindingSchema.ps1`, but the A9 script is the one that adds the home columns; the operator may need both.


## Main session, after merge (2026-10-16)

- Merged into R3 as `176857143` (`--no-ff`; `git merge-tree` dry run clean); the BFF builds with `-warnaserror` and 0 warnings.
- K1 `removeUnverifiedEmailClaim` (fix-round-1 list): filed as ISS-013, #2001. No stamp sets or checks it (provisioning has only the coordination note, row 6). Dev is covered by task 059, which sets it in the same step that makes the dev BFF app multitenant, so 047 on dev is not exposed before 059.
- K2 raw tid/oid in logs: no change needed. `.claude/constraints/auth.md:173` requires every authenticated server log to carry `oid` and `tenantId`, so the values are logged by policy, not leaked.


## UAC review fixes (2026-10-16)

**F1 (security): the home bind now consumes the invite-code hash.** `BindHomeOidAsync` (registration BindExisting with ConfirmExistingRecord, its only caller: `ContactIdentityBinder.cs:659`) bound the home oid but left `sprk_externalinvitecodehash` in place, so a later operator unbind would let a forwarded invite link bind the contact through the CIAM E11 path (task 185's invariant: a bound contact never holds an unused hash).
- New pure `HomeBindAndConsumeInvitePayload(homeTenantId, oid)` = `HomeBindPayload` + `sprk_externalinvitecodehash = null` (task 185's `BindAndConsumeInvitePayload` pattern). `HomeBindPayload` is unchanged (its tests and the fallback use it). The marker `sprk_externalinvitedon` is not touched.
- `BindHomeOidAsync` sends it in the same `If-Match` PATCH through the new `HomeBindConsumingInviteAsync`, with `missingColumns` naming the hash column, so a write refused for that column answers `StoreWriteStatus.ColumnMissing`. On `ColumnMissing` it logs a warning and retries ONCE with the plain `HomeBindPayload` (same row version) and returns that result; every other status returns as is (one call). An environment without the column holds no hash, so the plain bind is correct there.
- `InMemoryContactIdentityStore.BindHomeOidAsync` clears the stored hash in the bind (detail ends `|invite-code-hash=null`); where the hash column is modelled missing it binds without clearing, as the real store's retry does.
- Tests: pure payload (hash null, tenant, oid, mirror, plane WorkforceHome, no `sprk_externalinvitedon`; plain payload has no hash key); fallback (first write names the hash column and is refused, the retry sends the plain payload with no missing-columns argument, result Written); five non-ColumnMissing statuses are sent once and returned as is; behaviour (`ExternalRegistrationEndpointsTests.OneBindableContact_WithAPendingInvite_WithConfirmation_IsBound_AndTheInviteCodeHashIsCleared`: contact with a pending hash bound by registration ends with the hash null, marker unchanged). ADR-038 bans HTTP doubles, so the fallback goes through a delegate seam (`write`) on the store rather than a mocked handler.
- Seeding proof: with the `= null` line removed from the consuming payload builders and the in-memory home bind set back to `consumeInvite: false`, 4 tests failed (the pure payload test, the fallback test, the behaviour test, and task 185's `TheConsumingBind_IsThePlainBindPlusTheHashCleared_InOneBody`, which shares the line); restored, all pass.

**K2: no email in read-failure logs.** `GetAsync` took a `logLabel`; when set, both its warnings (refused response and thrown) log it instead of the path. Set for the three reads whose OData filter carries the caller's email: `FindActiveContactsByEmailAsync` ("contacts by email"), `FindInactiveContactsByEmailAsync` ("inactive contacts by email"), `FindEnabledSystemUsersByEmailAsync` ("systemusers by email"). A grep of `src` shows no other read embeds an email; every other read still logs its path. Test `AReadThatCarriesTheEmail_LogsAFixedLabel_NeverTheEmail` drives the three reads through a throwing token source (reaches the same warning without an HTTP double) with a capturing logger: no message contains the email (plain, local part, domain or URL-escaped), each has its label, and a control read (`FindContactsByOidAsync`) still logs its path.

**Counts.** Filtered unit run (`ContactIdentity|ExternalRegistration|ContactBinding|Invite`): 551 passed, 0 failed (8 new tests/cases over the 543 before). BFF build `-warnaserror`: 0 warnings, 0 errors. ArchTests: 1051 passed, 1 failed, and the failure is not from this change: `RecordOwnerAssignmentCensusTests.EveryOwnerWriteIsCensused` reports `UNLISTED owner write in ContactIdentityStore.cs.CreatePayload` (the WorkforceHome create's `OwnerBindKey` write from the original 047 commit `c100adc99`; the census, which came in with master, has no entry for it). It needs a census classification (probably a Seam entry plus an `OwnerSeam` guard for the refusal of an empty team) by whoever owns that guard; not added here.

## Deploy prerequisites (UAC review K1, 2026-10-16)

- **removeUnverifiedEmailClaim = true** on the stamp's BFF app and on the multitenant SPA client is a stated prerequisite for 047 on every stamp other than dev, because registration binds by the token `email`. Dev: task 059 sets it in the same step that makes the BFF app multitenant. Other stamps: provisioning task 264 (ISS-013, #2001).
- Option 100000012 'Inactive contact email match' applied to the stamp's sprk_identitycollisionreason (`Set-ContactIdentityBindingSchema.ps1 -Apply`) before the 047 BFF.
- The owner-write census entry for `ContactIdentityStore.CreatePayload` (Seam) was added after master's census arrived with merge 344cb0cb3. UAC owns the census guard and should confirm the classification.

## UAC re-check fixes (2026-10-16)

UAC re-checked 344cb0cb3..9e860c692: F1 and K2 hold. One F remained.

- **F3:** the census Seam entry for `ContactIdentityStore.CreatePayload` had no OwnerSeam guard.
  - The empty-owning-team check now throws the standard owner-seam refusal (InvalidOperationException, "refusing to create an app-owned contact"). A missing tenant stays an ArgumentException.
  - `CreatePayload` is listed in `Seams` with the shared `Refusal` regex.
  - Seeding proof: with the wording removed, `EverySeamRefusesAnOwnerlessCreate` fails on CreatePayload; restored, it passes.
- **K2 residual** (UAC probed dev: Dataverse echoes the whole `$filter`, email included, in a syntax error's message):
  - A labelled read now logs the status and `error.code` only (the new pure `ErrorCodeOf`), never the body.
  - The theory uses the probed shape.
- **K3, open until the fold:** a fallback-wiring mutation survives (the tests drive `HomeBindConsumingInviteAsync` through a delegate). This closes when `BindHomeOidAsync` folds onto PR #2007's store-level fallback at the next master merge. #2007 has the same gap.
- **K1, open until the fold:** the fallback is duplicated with #2007, and the static column arrays will collide on merge. Resolved by the same fold.

Runs:
- the full suite on 9e860c692 (23,856 passed, 1 TaskCanceled; it passes alone);
- after this fix, 697/697 identity, registration and invite tests;
- ArchTests 1052/1052.

## Fold onto PR #2007 (2026-10-16)

Master was merged into R3 with UAC's #2007 (`96d5b16c2`). The home bind now shares #2007's consuming bind:
- **Fold:** a private `BindConsumingInviteAsync(contactId, version, consuming, plain, ct)` sends the consuming payload with the hash column named as a possible missing column. On ColumnMissing it sends the plain payload once, under the same row version.
  - `BindOidAndConsumeInviteAsync` and `BindHomeOidAsync` both call it.
  - R3's delegate seam `HomeBindConsumingInviteAsync` and its `InviteHashColumnOnly` array are gone. This closes K1.
- **K3 closed:**
  - The home fallback tests now drive `BindHomeOidAsync` through #2007's scripted HTTP fake, so the real `WriteAsync` classification is exercised.
  - Seeded: passing `missingColumns: null` in the helper fails both fallback tests (home and #2007's); restored, both pass.
- **Merge semantics:** R3 (047 fix round 1) reports a write refused for a missing privilege as `Forbidden`; #2007's theory expected `Failed` for its 403 case.
  - Every #2007 caller treats any non-Written status as a denial: binder :967, :1187, :1234, and the invite service via `BindInvitedContactAsync`.
  - So the case now expects `Forbidden`.
- **Runs:** 781/781 identity, binding, invite and contract tests; ArchTests 1052/1052.
