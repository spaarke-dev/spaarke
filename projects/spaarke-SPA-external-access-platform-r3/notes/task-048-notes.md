# Task 048 — `POST /api/v1/external/workforce/link` (ADR-028 A9, dual-token proof)

Branch `work/spaarke-SPA-external-access-platform-r3` (base `55daa9e8a`). The design is `notes/a9-bff-design.md` §5; the owner decision is §11 item 3. Rigor FULL; Opus 5.5 @ high; steps directional; two adversarial passes (tags `auth`, `security`).

The escalation trigger ("validating the body token with the default scheme's parameters requires changing that scheme's configuration") did **not** fire. The validator reads the default scheme's `JwtBearerOptions` and clones its `TokenValidationParameters`; it never writes them.

## Placement (CLAUDE.md §10)

The route goes in the BFF. It authenticates the stamp's WorkforceHome bearer, validates a Spaarke-tenant token for the same BFF app with the default scheme's own parameters, and writes the stamp's Dataverse app-only through the existing `ContactIdentityBinder` / `DataverseContactIdentityStore`. There is no OBO, no Graph call, no token acquisition and no email.

New surface:
- one endpoint file and one DTO file;
- one validator class (one DI registration, a singleton);
- two store methods, one binder method, one pure decision;
- one pure eviction-tenant helper.

There is no new package.

## §11 justification

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ExternalWorkforceLinkEndpoints.cs` (POST `/workforce/link`) | `ExternalRegistrationEndpoints` (047), the reference | It cannot sit on the `/api/v1/external` group: `CallerPrincipalAuthorizationFilter` would refuse the caller before the handler runs. So it is its own small group with the same policy, exactly as 047. | A licensed user signing in with their company account can never reach their systemuser (A9 §5 has no other way to prove the link). |
| `ExternalWorkforceLinkDtos.cs` | `ExternalRegistrationDtos` (closed-DTO precedent) | It follows that precedent; no existing DTO has this field. | Without a closed body, an extra field such as `systemUserId` could be smuggled in and silently ignored. |
| `SpaarkeGuestTokenValidator` (+ DI singleton) | The default `JwtBearer` scheme (`AuthorizationModule`), `WorkforceCallerClaims`, `WorkforceMembershipTest.TryParseHomeTenant` | It reuses the scheme's parameters and metadata, and the existing idp parser. A second authentication scheme would authenticate the *request*, but the guest token is in the body and must be validated alongside a different bearer. | The body token could not be proven to be a valid Spaarke-tenant token for this app. |
| `ContactIdentityBinder.LinkHomeSystemUserAsync` | `RegisterHomeContactAsync` (047): its probe → read → decide → If-Match write → 412 retry loop | Same loop shape, same store, same probe. The 047 method writes contacts, not systemusers. | No once-only, version-checked link write. |
| `ContactBindingDecision.DecideHomeLinkWrite` (+ action enum, decision record) | `DecideHomeLink` (046, the read side) | A pure sibling of the read decision, over the same `HomeLinkLookup`. | The never-re-point and collision rules would have no testable decision. |
| `IContactIdentityStore.FindSystemUsersByGuestObjectIdAsync` | `GetSystemUserAsync` (by id), `FindHomeLinkedSystemUsersAsync` (by pair) | Neither filters by `azureactivedirectoryobjectid`, and the link must target the guest token's own systemuser. | The target systemuser could not be found from the guest token. |
| `IContactIdentityStore.WriteHomeLinkAsync` | `BindHomeOidAsync` (contact), `WriteAsync` | It reuses `WriteAsync`, `RowVersionPrecondition` and `NoRowVersion`; only the payload and the entity set are new. | No version-checked write of the pair and its mirrors in one request. |
| `ImpersonatedRootSetSource.EvictionTenants` (pure) | `ExternalParticipationService.GrantCacheTenantIds` (the POML's reference) | That is an instance method that reads the request's `tid` claim and raw configuration strings. The remover runs outside a request and takes no participation service, so the same rule is a pure static beside the cache key it addresses. It uses the parsed `D` form, which is the form a token's `tid` carries. | 046 review K1: a record turned Restricted stays visible to a linked user's home sign-in for up to `CacheTtl`. |

## How the proof works

1. **WorkforceHome bearer only.** `WorkforcePrincipalResolver.WorkforceHomeIdentity(user)` returns null for a `Bearer` or `Ciam` identity → 403 `home_identity_required`. Anonymous is 401 at the policy.
2. **Closed body.** `{guestAccessToken}`, at most 12 KB, no control characters or spaces. Any other property → 400 (`JsonUnmappedMemberHandling.Disallow`). The whole body is capped at 16 KB.
3. **Guest token, default-scheme validation.** The validator clones the default scheme's TVP and hands it the scheme's `BaseConfigurationManager`, as `JwtBearerHandler` does. It fails closed when the cloned parameters do not require signature, lifetime, audience and issuer validation. Freshness is the token's own `exp` with the scheme's own clock skew.
4. **A9 conditions** (`DenyReason`, pure):
   - `aud` is the stamp BFF app in either form (bare id or `api://`); the Copilot audience the default scheme also accepts is refused.
   - `idtyp`, when present, is `user`; `scp` contains `user_impersonation`.
   - `tid` = `AzureAd:TenantId`; if that setting is unset, nothing is accepted.
   - `acct` = `1`.
   - The idp's home tenant = the bearer's `tid`.
   - `oid` is a non-empty GUID.
   - The UPN check (defense in depth, refuse-only): the guest's home UPN equals the bearer's `preferred_username`, case-insensitively.
5. **Write once.** `LinkHomeSystemUserAsync`:
   - Probes the A9 columns. Masked or missing → 500 `environment`, decided before any read the mask could fool.
   - Reads the guest's systemusers (by `azureactivedirectoryobjectid`, enabled first) and the pair's holders (`FindHomeLinkedSystemUsersAsync`, the secured pair, any state).
   - Decides, then writes the secured pair and both key mirrors in one PATCH with If-Match.
   - A 412 retries once. A duplicate-key fault (another writer took the pair) → `link_collision`.

## Acceptance criterion → test

| Criterion | Test |
|---|---|
| 200, link written, 046 resolution returns the systemuser | `ValidHomeBearer_AndValidGuestToken_ForTheSamePerson_Returns200_WritesTheLink_AndResolutionFindsTheSystemUser` |
| Repeat → 200, no write | `RepeatCall_Returns200AlreadyLinked_AndWritesNothing` |
| Expired, bad signature, wrong aud, acct 0/missing, idp tenant ≠ bearer tid, UPN mismatch → 403 + reason, no read or write | `GuestToken_FailingACheck_Is403WithItsReasonCode_AndWritesNothing` (theory) |
| tid not Spaarke → 403 | Over HTTP, the theory's `tid-not-issuer-tenant` → 403 `guest_token_invalid`: the default scheme's AAD signing-key issuer check (IDX40004) refuses a token whose `tid` is not its issuer's tenant before any A9 condition runs. The A9 `guest_tenant_not_spaarke` branch is covered in `SpaarkeGuestTokenValidatorTests.DenyReason_OneConditionFailing_…` (`tid-other`, `spaarke-tenant-unset`). |
| No systemuser / disabled → 403 | `GuestOid_WithNoSystemUser_…`, `GuestOid_OfADisabledSystemUser_…` |
| Linked to a different pair → 403, not re-pointed | `SystemUser_LinkedToADifferentPair_Is403LinkCollision_AndIsNotRePointed` |
| Pair on another systemuser → 403 | `Pair_AlreadyLinkedToAnotherSystemUser_…` (secured pair read) and `Pair_LinkedToAnotherSystemUserConcurrently_IsRefusedByTheKey_AsLinkCollision` (the alternate-key backstop) |
| Bearer / CIAM scheme → 403; anonymous → 401; missing token or extra property → 400 | `NonHomeBearer_Is403_…`, `Anonymous_Is401`, `MissingToken_OrAnyOtherProperty_Is400_NoWrite` |
| No log line or response contains the guest token | `TheGuestToken_AppearsInNoLogLine_AndNoResponse_OnSuccessOrRefusal` |
| 046 K1: evict under every customer tenant, with a test | `RestrictedExternalShareRemoverTests.OnARestrictedRecord_TheRemovedUsersCache_IsAlsoClearedUnderEveryCustomerHomeTenant` (NoAccessShareEnforcer: see "Open" below) |

### Tests beyond the named cases (one-line justification each)

- `copilot-aud` (route and unit): the default scheme accepts this audience, so only the A9 audience check stands between a Copilot token and a link.
- `MaskedHomeColumns_Is500Environment_…`: a masked pair reads as "no link", so without this fail-closed path the route would write over a link it cannot see.
- `DecideHomeLinkWrite_*` (5 tests): the write decision's branches that the route cannot reach directly. These are the ambiguous guest oid, an unreadable `isdisabled`, a half-written link, and a failed holders read.
- `ValidateAsync_DefaultSchemeWithout…` (with a positive control): pins the fail-closed guard against a future weakening of the default scheme.
- `GuestHomeUpn_*`: the `#EXT#` decoding is the only parsing step in the UPN check, and its last-underscore rule is non-obvious.
- `DenyReason_TheIdpInV1OrV2Form_…`: real guest tokens carry either form.

## Seeding proofs

Each guard was broken in place, the named filter was run, and the guard was restored by a reverse string replacement (verified byte-identical). Script: the session scratchpad `seed.py`.

| Guard broken | Tests that failed |
|---|---|
| `DenyReason`: the idp home tenant = bearer tid comparison removed | `DenyReason_OneConditionFailing_…(idp-other-tenant)` and the route theory's `idp-other-tenant` (2 of 141) |
| `DecideHomeLinkWrite`: the enabled filter removed (disabled rows counted as enabled) | `DecideHomeLinkWrite_NoEnabledRow_…` and `GuestOid_OfADisabledSystemUser_…` (2 of 30) |
| `RestrictedExternalShareRemover`: customer tenants dropped from eviction | `OnARestrictedRecord_TheRemovedUsersCache_IsAlsoClearedUnderEveryCustomerHomeTenant` (1 of 23) |

## Deviations from the POML

- **Test placement.** The POML names `tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/`; `tests/CLAUDE.md` requires new test files under a KEEP path. The route and validator tests are in `tests/integration/auth/ExternalAccess/` (auth path; they compile into the same project).
- **Order.** Implementation came before the tests (POML step 1 says tests first; steps are directional). Each guard's test was then proven able to fail by the seeding proofs above.

## Test fixture facts worth keeping

- **The shared test host's audience.** It sets `AzureAd:Audience = api://test-app-id`. Identity Web's `RegisterValidAudience.ValidateAudience` then checks that value for v1 and v2 tokens alike. Identity Web's validator also sets `AudienceValidator = null` on the parameters it is handed. So `SpaarkeGuestTokenValidator` only ever passes it a clone, as production `JwtBearerHandler` does; the shared options are never mutated.
- `BuildCiamValidAudiences` derives the second audience form only for a GUID app id. So the link fixture uses a GUID app and the audience shape `AuthorizationModule` leaves when Copilot is configured (`ValidAudiences = [api://{app}, {copilot}]`).
- **Fixture overrides.** The fixture pins the default scheme's issuer validator and signing metadata, because the shared host's `AzureAd:TenantId` is not a GUID. Identity Web's audience validator and the AAD signing-key issuer check (IDX40004) stay in the path.

## Known limits

- **K1, the UPN check on live tenants.** It can never admit a wrong person, but it can refuse a genuine one (`upn_mismatch`):
  - A B2B guest's `#EXT#` UPN derives from the *invited email*, which can differ from the person's home UPN (an alias, a changed UPN, or email ≠ UPN).
  - In a v1 guest token, `unique_name` may carry the home UPN without `#EXT#`. `GuestHomeUpn` then falls back to `email`, which can differ from the UPN.

  Task 074 (live verification) must confirm the claim shapes on dev guests before the route is relied on.
- **K2 (046 review, accepted).** The external notification last-seen marker is keyed by (token `tid`, principal id), so a linked user's unread state differs between their company sign-in and their guest sign-in. No change, per the POML.
- **K3, Forbidden on the write.** A 403 from the PATCH maps to `DenyBindingColumnMasked` → 500 `environment`. It means the app user lacks write rights on the secured columns, which is an environment fault, not a caller fault.
- **K4, Identity Web's `AadIssuerValidator` is never run by a test.** The shared test host's tenant is not a GUID, so the fixture replaces the issuer validator. The audience validator and the AAD signing-key issuer check stay in the path. The live check is task 074.
- **K5, the only-caller pin is narrow.** It sees calls through `IContactIdentityStore.WriteHomeLinkAsync` and `ContactIdentityBinder.LinkHomeSystemUserAsync`. It does not see a direct call on the concrete store, or a raw write of `HomeLinkPayload` through another Dataverse client. This is the same shape as 047's `BindHomeOidAsync` pin.
- **K6, the share routes' options injection.** `/share-user` and `/unshare-user` take the customer tenants as an optional `[FromServices]` parameter. Minimal APIs inject it, because both are mapped as method groups and the options are registered. Only the 18 direct test callers pass null. If the handler is ever wrapped in a lambda, the customer-tenant eviction silently stops, and no host-level test would notice. The `/share-user` clean-up path has no customer-tenant test; it is a grant clean-up, not a removal (UAC review note 2).
- **K7, the two customer-tenant readers differ on a bad reload.** On an invalid reloaded configuration, the options reader (`CustomerTenantIdsOrNone(IOptionsMonitor)`) falls back to none: `CurrentValue` throws, and the throw is caught and logged. The configuration reader used by the materializer skips non-GUID entries and keeps the valid ones (UAC review note 1). Startup validation (`ValidateOnStart`) makes this a reload-only case.
- **K8, the "anonymous" key on `/share-user` and `/unshare-user`.** The new overload drops "anonymous", as `EvictionTenants` does everywhere. A default-scheme caller always carries `tid`, so nothing real was cleared there.

## Reviews

### Pass 1 (adversarial, Opus)

No path links the wrong person. The fix-now findings and what was done:

| Finding | Class | Done |
|---|---|---|
| The new route was missing from the route ledger, the scheme census and the only-caller pin | F4 | Added: `GovernedFile` (RouteLevelGate), `Permanent` CallerScopedOnly `r3-048`, count 127, the census route, and `WorkforceHomeWrites` pins for `LinkHomeSystemUserAsync` and `WriteHomeLinkAsync` |
| 046 K1 also applies at `InternalShareEndpoints` (share and unshare), `AssignedAccessMaterializer` and `NoAccessShareEnforcer` | F1 | All four sites now go through `EvictionTenants`, after merging master with UAC #2016 (`2e4ffc654`, merge `1e5e51325`). UAC reviewed the three UAC-file edits: OK |
| The ambiguous guest systemuser answered 503 "try again" | F1 | Now 403 `sdap.access.link.systemuser_ambiguous` |
| A missing `sprk_hometenantidkey` got past the probe | F1 | The probe selects it |
| The test comment overstated coverage, and the token shape was unrealistic | F3 | Comment corrected. The test token is now v1 (`aud api://{app}`, `sts.windows.net` issuer), the shape the stamp app issues under `AzureAd:Audience = api://{app}` |
| A validator throw answered 403 (K) | K→fixed | 503 `unavailable` |
| An invalid options reload in the remover's `finally` would skip all eviction (K) | K→fixed | Reader that never throws |
| Already-linked in a non-canonical form, or with a second holder (K) | K→fixed | Both are now `Collision` |
| No test of the 412 retry (K3) | K→fixed | `AConcurrentChangeToTheSystemUser_Is412_ReReadOnce_ThenLinked` |

### Pass 2 (adversarial, Opus, whole change set)

No wrong-person, token-confusion, fail-open, shared-state or leakage defect was found. The eviction key format matches: `CacheTenantFor` uses the raw lowercase `tid`, and `EvictionTenants` emits `D`.

| Finding | Class | Done |
|---|---|---|
| A signing-metadata outage answered 403 (the token handler swallows the fetch failure) | F1 (plausible) | The validator fetches the metadata first; a throw is 503 `unavailable`. Test: `ValidateAsync_WhenTheSigningMetadataCannotBeFetched_IsUnavailable_NotARefusal` |
| Stale line references in the ledger | doc | Corrected (`:107`, `:121`, `:133`, `:147`) |
| Stale notes (this file) | doc | Corrected |
| K1–K5 | K | Recorded above (K1, K4, K5, K6, K8) |

### Seeding proofs added in the fix round

| Guard broken | Test that failed |
|---|---|
| `NoAccessShareEnforcer`: customer tenants dropped | `Enforce_ClearsTheWalledUsersCacheKey_AlsoUnderEveryCustomerHomeTenant` (1 of 63) |
| `ImpersonatedRootSetSource.InvalidateAsync` (the share routes): customer tenants dropped | `Unshare_ClearsTheUsersRootSet_AlsoUnderEveryCustomerHomeTenant` (1 of 130) |
| `SpaarkeGuestTokenValidator`: signing-metadata pre-fetch removed (pass 2) | `ValidateAsync_WhenTheSigningMetadataCannotBeFetched_IsUnavailable_NotARefusal` (1 of 35) |

## Counts

- **ArchTests:** 1052/1052 on `6e6fa656b`. After pass 2, the ledger and census tests: 93/93.
- **048-affected suites after pass 2:** 550/550. These are the validator, link endpoint, binding decision, enforcer, share, remover and materializer tests.
- **Full BFF unit suite (`6e6fa656b`, machine under load: 39 dotnet processes, 88 % CPU):**
  - About 40 failures, all host-based contract tests, each after 2–17 minutes (the load gotcha).
  - The 36 failing classes re-run in isolation: 541 passed, 7 skipped, 0 failed. None is real.

## Publish size

(filled at completion)
