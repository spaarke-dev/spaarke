# Task 010 — Ciam audience forms + default-scheme CIAM guard (FR-22, FR-23)

Date: 2026-10-10. Rigor FULL (auth). Implemented by a parallel-wave implementer agent; the adversarial review passes are run by the main session.

## Changes

- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs`
  - **FR-22.** The `Ciam` scheme sets `TokenValidationParameters.ValidAudiences = BuildCiamValidAudiences(Ciam:Audience)`, replacing `options.Audience`. The list holds the configured value plus its other form: `{appId}` ⇄ `api://{appId}`.
  - **FR-23.** A new `PostConfigure<JwtBearerOptions>` on the default scheme wraps `OnTokenValidated`. It calls `context.Fail` (→ 401) when `iss` contains `ciamlogin.com` (OrdinalIgnoreCase), or when `tid` equals `Ciam:TenantId` and that setting is configured. It reads both the validated `SecurityToken` and the principal, accepting either form of the `tid` claim through `TenantResolution`. When the guard fires it logs `sdap.auth.deny.ciam_token_on_workforce_scheme`.
  - New internal helpers: `BuildCiamValidAudiences`, `IsCiamIssuer` and `IsCiamTenant`.
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/AuthorizationModuleCiamTests.cs` (new, 17 test cases). These run the real schemes behind a `TestServer` and assert HTTP status codes.

## Decisions

1. **The guard is its own `PostConfigure`.** It is not inside the existing Copilot-merge delegate, because that delegate sits behind a process-wide once-flag (`_jwtPostConfigureApplied`). Any later `JwtBearerOptions` instance would be built without the guard, and a security guard must apply to every instance.
2. **The guard uses `OnTokenValidated`, not an `IssuerValidator`.** The `tid` rule needs claims, and replacing Microsoft.Identity.Web's `AadIssuerValidator` would change how internal routes authenticate, which is an escalation trigger. The guard chains to the existing handler (Microsoft.Identity.Web's) only when it does not fire.
3. **The other audience form is derived only for a GUID app id.** A non-GUID identifier URI, or an unreplaced `#{…}#` placeholder, keeps today's single audience. This is the "do not widen" rule applied to the derivation.
4. **Empty `Ciam:Audience` gives an empty `ValidAudiences`,** so no audience is accepted. This is unchanged from today.
5. **Effect on the shared `ExternalCollaboration` policy (Ciam + default).** A CIAM token still authenticates there through the Ciam scheme, which the test `ExternalCollaborationPolicy_AssumedS1Shape_CiamToken_StillReturns200` proves.

## Unproven token-shape assumptions (T240d S1 (iv) pending)

- A CIAM user access token has `iss = https://{ciamTid}.ciamlogin.com/{ciamTid}/v2.0` and `tid = {ciamTid}`.
- Its `aud` is the stamp's BFF app id, either as a bare GUID (v2) or as `api://{appId}`. These are the spec's two forms.

Tests that rely on these shapes have `AssumedS1Shape` in their names. When S1 (iv) arrives, swap its shape into those tests, and escalate if it contradicts the two forms.

## Test harness notes

- Signing metadata comes from a local RSA key through a `StaticConfigurationManager`, so the tests make no network calls.
- On the default scheme only, the test issuer validator accepts every issuer. This models the worst case FR-23 is written for, so the 401s come from the guard alone.
- Workforce tokens use the deployed audience form (`AzureAd:Audience = api://{API_APP_ID}`) and set `ver` (Microsoft.Identity.Web's audience validator keys off it).
- **Copilot case.** The module merges the Copilot audience only once per process (the static flag), and ADR-038 B8 forbids resetting it by reflection. The test host therefore re-adds the same configured Copilot audience. What this task can break is the guard rejecting a Copilot token, and the test still exercises that end to end.

## Fail-before / pass-after

- New tests against the original `AuthorizationModule.cs` (from `git show HEAD`): **6 failed, 11 passed**.
  - Failures: the two cross-form audience cases, both CIAM-issuer cases, the CIAM-tid case, and the unset-tid-still-rejects-iss case.
  - The 11 that pass already are the preservation and negative cases: same-form audiences, another customer's audience, empty audience, non-GUID not widened, workforce, Copilot, unset tid behaving as before, and ExternalCollaboration.
- After the change: **17 passed, 0 failed.**

## Seeding proof (security guard)

The guard was disabled with `if (denyReason is not null && false)` and the new tests were run. **4 failed:**

- `DefaultScheme_CiamTenantIdUnset_StillRejectsCiamIssuer_Returns401`
- `DefaultScheme_CiamTenantIdToken_Returns401`
- `DefaultScheme_AssumedS1Shape_CiamIssuerToken_Returns401` ×2

The other 13 passed. The guard was then restored and confirmed by grep.

## Test scope beyond the named criteria (one line each)

- `api://` configured × either token form: POML step 2 says "strip or add", so both directions are part of the contract.
- Non-GUID identifier URI not widened: an edge case of the derivation, applying the "do not widen" constraint.
- Uppercase `CIAMLOGIN.COM` issuer: mirrors `DeterminePlane`'s OrdinalIgnoreCase match.
- A CIAM token on `ExternalCollaboration` still returns 200: it proves the guard does not break the CIAM plane on the shared policy (escalation trigger 1).

## BFF hygiene (§10)

- **Placement:** in the BFF. Inbound token validation for the BFF's own schemes can only live in `AuthorizationModule` (`.claude/constraints/bff-extensions.md`).
- **New surface:** no endpoint, service, package or DI service type. One additional `PostConfigure` delegate and three internal static helpers.
- **Publish size:**
  - Command: `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent linux-x64, Release.
  - Compression: `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, PDBs included (4 PDBs).
  - Fresh worktrees: `C:\wt010m` at `origin/master` 1b7ea7f48, and `C:\wt010b` at branch HEAD 6e5984449 plus this change.
  - File counts: 192 / 192.
  - Size: master 38,204,214 B (36.43 MB); branch 38,205,388 B (36.44 MB); **delta +1,174 B**. This delta also includes the branch's other R3 commits. Well under 60 MB.
- **CVE:** no package was added or changed, so there is no new CVE surface.

## Review fixes (2026-10-10)

Two independent adversarial reviews found no F-class defects. Three known-limit hardenings were applied in `AuthorizationModule.cs`.

1. **Startup refusal (K3).** `AddAuthorizationModule` calls `ThrowIfCiamTenantEqualsWorkforceTenant` before it registers anything. It throws `InvalidOperationException` when `Ciam:TenantId` and `AzureAd:TenantId` both parse as GUIDs and are equal. The comparison is on `Guid`, so case, `N` format and padding do not matter. Identical values would make the FR-23 guard reject every workforce token, a whole-stamp 401 outage. The message names both keys and no value. An unset, blank, placeholder or non-GUID value on either side never triggers it.
   - The check runs at registration, not inside the `[CRITICAL]` audience `PostConfigure`. That delegate is behind the process-wide once-flag and runs at options resolution, so a throw there would be a first-request 500 rather than a refusal to start.
   - Limitation: the check reads configuration as it stands when `AddAuthorizationModule` runs. A config source added to the builder after that call is not seen.
2. **Issuer-tenant rule (K4).** The guard now also denies when the first path segment of the `iss` URL parses as a GUID equal to `Ciam:TenantId`. Both the validated token issuer and the principal `iss` claim are checked. This is a strict superset of the previous rules. It closes a tid-less token issued via `login.microsoftonline.com/{ciamTid}/v2.0` and CIAM custom URL domains with no `tid`. Behaviour is unchanged when `Ciam:TenantId` is unset or not a GUID. A custom CIAM URL domain relies on `Ciam:TenantId` being set; provisioning H4b always sets it.
3. **Deny-log properties (K4).** `sdap.auth.deny.ciam_token_on_workforce_scheme` now carries `IssuerHost` (the `Uri.Host` of the issuer) and `Tid` (capped at 64 chars) as structured properties. The token and headers are never logged.

### Tests added (16 new cases; file total 33)

- `DefaultScheme_NoTidClaim_CiamTenantInIssuerPath_Returns401`
- `DefaultScheme_NoTidClaim_CustomDomainIssuerWithCiamTenantInPath_Returns401`
- `DefaultScheme_NoTidClaim_V1IssuerWithCiamTenantInPath_Returns401`
- `DefaultScheme_NoTidClaim_WorkforceIssuerPath_Returns200`
- `DefaultScheme_CiamTenantIdUnset_IssuerPathRuleDoesNotFire_Returns200`
- `DefaultScheme_CiamToken_DenyWarningCarriesIssuerHostAndTid_NeverTheToken`
- `AddAuthorizationModule_CiamTenantEqualsWorkforceTenant_RefusesToStart` (3 cases)
- `AddAuthorizationModule_CiamTenantEqualsWorkforceTenant_UppercaseGuid_RefusesToStart`
- `AddAuthorizationModule_CiamTenantDistinctOrUnsetOrPlaceholder_Starts` (5 cases)
- `AddAuthorizationModule_NonGuidTenantsThatAreEqualStrings_Starts`

### Fail-before / pass-after

Against the code before these fixes: 8 failed, 25 passed. The 8 failures were the 3 equal-tenant startup cases, the uppercase equal case, the 3 tid-less issuer-path cases and the deny-log properties case. The preservation cases passed already. After the fixes: 33 passed, 0 failed.

### Fixtures

No fixture sets `Ciam:TenantId` equal to `AzureAd:TenantId`. None was changed.
