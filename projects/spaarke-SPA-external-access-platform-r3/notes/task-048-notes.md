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

- **The shared test host's audience.** It sets `AzureAd:Audience = api://test-app-id`. Identity Web's `RegisterValidAudience.ValidateAudience` then checks that value for v1 and v2 tokens alike. The validator also sets `AudienceValidator = null` on the parameters it is given, so it must only ever see a clone (the validator clones; production `JwtBearerHandler` does too).
- `BuildCiamValidAudiences` derives the second audience form only for a GUID app id. So the link fixture uses a GUID app and the audience shape `AuthorizationModule` leaves when Copilot is configured (`ValidAudiences = [api://{app}, {copilot}]`).
- **Fixture overrides.** The fixture pins the default scheme's issuer validator and signing metadata, because the shared host's `AzureAd:TenantId` is not a GUID. Identity Web's audience validator and the AAD signing-key issuer check (IDX40004) stay in the path.

## Known limits

- **K1, the UPN check on live tenants.** A B2B guest's `#EXT#` UPN derives from the *invited email*, which can differ from the person's home UPN (alias, changed UPN, email ≠ UPN). The check can then refuse a genuine person (`upn_mismatch`). It can never admit a wrong one. Task 074 (live verification) must confirm the claim shapes on dev guests before the route is relied on.
- **K2 (046 review, accepted).** The external notification last-seen marker is keyed by (token `tid`, principal id), so a linked user's unread state differs between their company sign-in and their guest sign-in. No change, per the POML.
- **K3, Forbidden on the write.** A 403 from the PATCH maps to `DenyBindingColumnMasked` → 500 `environment`. The app user lacks write rights on the secured columns, which is an environment fault, not a caller fault.

## Open

- **NoAccessShareEnforcer eviction (046 K1, second site).** UAC PR #2016 edits `NoAccessShareEnforcer.cs` next to the eviction loop (~1159). Agreed with UAC: this change lands on top of #2016 after it merges (UAC will message), with its own test. `RestrictedExternalShareRemover` is done here.

## Counts

(filled at completion)

## Publish size

(filled at completion)
