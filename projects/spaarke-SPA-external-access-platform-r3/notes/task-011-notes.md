# Task 011 — Default modules by user type + partner service-request switch (FR-19, FR-06 module half, #1568)

Date: 2026-10-10. Rigor FULL (auth, entitlement). Implemented by a parallel-wave implementer agent; the two adversarial review passes are run by the main session. Basis: ADR-028 Amendment A7 (owner-approved 2026-10-10).

## Changes

- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ExternalAccessOptions.cs` (new). Section `ExternalAccess`, one property `PartnerServiceRequestsEnabled` (bool, default `false`). It binds only that key; the section's existing keys (`PortalUrl`, `Reconciliation:WritesEnabled`, `AssignedAccess:JobRevokeOnChangeEnabled`) stay read by key where they are today.
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/ExternalAccessModule.cs`
  - Binds the options in the existing `AddExternalAccess` (`AddOptions<ExternalAccessOptions>().BindConfiguration(...).ValidateOnStart()`); no new DI module (ADR-010).
  - The `service-requests` descriptor moved into `internal static ServiceRequestsModule(bool partnerServiceRequestsEnabled)` and is registered through `services.AddSingleton(sp => ServiceRequestsModule(IOptions<ExternalAccessOptions>.Value.PartnerServiceRequestsEnabled))`. Name, entity, `RecordIdAttribute = sprk_requestedby`, column list and saved-query set are unchanged.
  - Predicate: `ContactId != Empty && (Plane == Workforce || (switch && Plane == CiamContact)) ? { ContactId } : empty`. Still keyed by `sprk_requestedby` = the caller's own contact.
- `src/server/api/Sprk.Bff.Api/Infrastructure/ExternalAccess/ModuleEntitlementResolver.cs`
  - Takes `IOptions<ExternalAccessOptions>`.
  - Workforce (systemuser or workforce contact): `legal-front-door` + `policy-library` always, ∪ the `sprk_approlemodulemap` extras for the caller's App-Role claims. No roles → the defaults, and the map is not read.
  - CIAM: `assigned-work`; `assigned-work` + `legal-front-door` when the switch is on. CIAM still never reads the map.
- `src/server/api/Sprk.Bff.Api/appsettings.template.json`: `ExternalAccess:PartnerServiceRequestsEnabled: false` plus a comment.
- `tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ModuleEntitlementResolverTests.cs`: 3 old `ResolveAsync` tests replaced, 24 new test cases (34 in the class).

## Decisions

1. **The descriptor is built from options through a factory registration**, not by threading `IServiceProvider` into `ExternalModuleDescriptor`. `AddExternalModule` takes a built instance, and the registry enumerates `GetServices<ExternalModuleDescriptor>()`, which includes factory registrations. No change to the framework (`ExternalModuleRegistry.cs`) was needed. The binding test proves the factory-registered descriptor reaches the registry.
2. **`IOptions<T>` (read once per process), not `IOptionsMonitor<T>`**, for both readers. The registry is a startup singleton, so a monitor in the resolver alone would let the two disagree after a reload. An App Service setting change restarts the app anyway.
3. **`ValidateOnStart` with no custom validator.** A bound `bool` has no invalid value, but a non-boolean string fails the bind. Without `ValidateOnStart`, that failure would surface on the first external module read (when the registry singleton is built) as a 500 on every module. With it, startup fails instead. `IOptionsDriftTests` needs no Census entry (no custom validation, no demanded key) and passes.
4. **Order of the workforce result** is ordinal-sorted (as `ResolveWorkforceEntitlements` already was); the no-roles path returns the static default list in its declared order. Consumers treat the list as a set.
5. **CIAM switch-on never adds `policy-library`.** A7's table gives partners only the Front Door.

## Escalation check (POML trigger: workforce systemuser with no ContactId)

Not triggered. `WorkforcePrincipalStrategy` sets `ContactId = principal.ContactId ?? Guid.Empty` (`CallerPrincipalResolver.cs:672`), and `WorkforcePrincipalResolver` derives a systemuser's contact from the oid binding or links it inline (task 141, `WorkforcePrincipalResolver.cs:130-160`). A licensed systemuser therefore carries its linked contact on this path. `Guid.Empty` occurs only for a systemuser that is not linked yet (link writes off or the link failed); that caller already saw 0 service requests before this task, and this task does not change it. The test `ServiceRequests_CallerWithNoContact_AccessibleSetEmpty` pins that edge on both planes and both settings.

## Fail-before / pass-after

The new tests use the new constructor and the new `ExternalAccessOptions`/`ServiceRequestsModule` surface, so the original files do not compile against them. The proof restores the **pre-task behaviour** inside the new signatures (scratch script; restored afterwards, diff verified identical):
- resolver: CIAM → `OutsideCounselModules` regardless of the switch; workforce with no roles → empty; with roles → map codes only;
- descriptor: `Workforce && ContactId != Empty` only.

Result: **12 failed, 22 passed** (34). The failures are exactly the change-encoding cases: workforce systemuser defaults (no roles ×2, unmapped role ×2), workforce contact defaults ×2, map extras on top of defaults ×2, CIAM switch-on modules, CIAM switch-on accessible set, CIAM switch-on own-rows-only, and the binding test with `"true"`. The 22 that already pass are the preserved behaviours and negatives (switch-off CIAM, workforce descriptor unchanged by the switch, no-contact empty set, the untouched pure-function tests).

After the change: **34 passed, 0 failed.**

## Seeding proof (POML step 6)

Descriptor predicate edited to ignore the switch: `|| (partnerServiceRequestsEnabled && p.Plane == CiamContact)` → `|| (p.Plane == CiamContact)`. Run: **3 failed, 31 passed**:
- `ServiceRequests_CiamCaller_SwitchOff_AccessibleSetEmpty_AndNoRowSurvives`;
- `PartnerSwitch_IsBoundFromTheExternalAccessSection_AndDrivesTheRegisteredDescriptor(configured: null, expectedOn: False)`;
- `PartnerSwitch_IsBoundFromTheExternalAccessSection_AndDrivesTheRegisteredDescriptor(configured: "false", expectedOn: False)`.

Predicate restored; 34/34 pass.

## Test scope beyond the stated criteria (one line each)

- `ServiceRequests_WorkforceCaller_IsUnchangedByTheSwitch_OwnRequestsOnly` (4 cases): guards the constraint "keep Workforce && ContactId != Empty" for both workforce kinds under both settings.
- `ServiceRequests_CallerWithNoContact_AccessibleSetEmpty` (4 cases): the POML escalation edge (no ContactId) stays fail-closed on both planes and both settings.
- `ResolveAsync_WorkforceCaller_MapRowsOverlappingDefaults_AreNotDuplicated`: the seeded dev map grants exactly the defaults; the union must not repeat codes.
- `PartnerSwitch_IsBoundFromTheExternalAccessSection_AndDrivesTheRegisteredDescriptor` (3 cases): the goal says the switch is *bound* with default off; this proves the section/key name, the default and the factory registration end to end.

## Build, tests, publish size (BFF hygiene §10, NFR-01)

- `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors (SDK 10.0.101).
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~ModuleEntitlementResolverTests|FullyQualifiedName~ExternalModuleRegistry|FullyQualifiedName~MeEntitlements"`: 50 passed, 0 failed.
- `dotnet test tests/Spaarke.ArchTests --filter "FullyQualifiedName~IOptionsDriftTests|FullyQualifiedName~Adr032|FullyQualifiedName~ExternalSpa"`: 250 passed, 0 failed.
- Full `dotnet test tests/unit/Sprk.Bff.Api.Tests/`: **19,419 passed, 7 failed, 54 skipped (19,480; 40 min)**. The 7 are in areas this task does not touch (`DocumentIdentityByIdContractTests`, `ComposeTransientKeyDedupSeamTests`, `ComposePdfRefreshBaselineSeamTests`, `ComposeApplyTemplateSeamTests`, `CatalogHealthMemoContractTests`, `DocumentProfileContractTests`, `ComposePatchEngineSaveSeamTests`), with HTTP/timing stack traces; the run overlapped two Release publishes on the same machine. Re-run of those 7 classes alone on the same tree (`--no-build --filter ...`): **198 passed, 0 failed**. They are load-sensitive, not regressions.
- Publish size. Command `dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/`, framework-dependent linux-x64 (the project's RID), Release, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, **PDBs included**. Each from a fresh worktree at a short path:

  | Build | Commit | Compressed |
  |---|---|---|
  | Fresh master | `origin/master` 61c9e80a1 | 38,214,654 bytes (36.44 MB) |
  | Branch base (pre-task) | c9fa6db96 | 38,216,412 bytes (36.45 MB) |
  | Branch + task 011 | c9fa6db96 + this change | 38,217,244 bytes (36.45 MB) |

  Delta vs fresh master: **+2,590 bytes (+0.00 MB)**; of that, task 011 itself is +832 bytes. Well under the 60 MB ceiling. No package added.
- `dotnet list package --vulnerable --include-transitive` (branch + task): no vulnerable packages. No new HIGH CVE.

## Placement Justification (`.claude/constraints/bff-extensions.md`)

In the BFF, in the existing external-access module. Tier-1 entitlement and the Tier-2 module predicate already live there (`ModuleEntitlementResolver`, the `service-requests` descriptor), they run on every external request's authorization path, and the switch is per-stamp BFF configuration. No new endpoint, service, package or background work; one options class bound in the existing `AddExternalAccess`.

## Component justification (CLAUDE.md §11) — `ExternalAccessOptions`

1. **Existing:** no options class binds the `ExternalAccess` section; `WorkforceIdentityOptions` (section `WorkforceIdentity`) is the only options class in `Infrastructure/ExternalAccess`, and the section's other keys are read by key (`ExternalAccessReconciliationJob.WritesEnabledConfigKey`, `AssignedAccessReconciliationJob.RevokeOnChangeConfigKey`, `PortalUrl`).
2. **Extension:** `WorkforceIdentityOptions` is the workforce member-test configuration with its own validator; a module-availability switch is unrelated to the member test and would bind the wrong section.
3. **Cost of doing nothing:** without a bound per-customer setting there is no way to turn partner service requests on for one customer (owner requirement 2026-10-10, FR-06), and the descriptor and resolver could not agree on one value.

`ServiceRequestsModule(bool)` is a modification of existing surface (the existing descriptor moved into a factory method so it can read the switch), not new surface.

## Follow-ups noticed (not in this task's file list)

- Stale doc comments still describe the pre-A7 "workforce = App-Role map only" rule: `Api/ExternalAccess/MeEntitlementsEndpoint.cs:15-18`, `Api/ExternalAccess/ExternalAccessEndpoints.cs:79-89` (route summary/description), `Api/ExternalAccess/Dtos/MeEntitlementsResponse.cs:17`. Text only; behaviour is in the resolver.
- FR-06's other halves (the intake endpoint accepting the CIAM plane when the switch is on; 403 when off) are not in this task; they should read the same `IOptions<ExternalAccessOptions>`.
- Deployment: no stamp channel writes `ExternalAccess__PartnerServiceRequestsEnabled`; absent means off, which is the intended default. A customer that wants it on needs the App Service setting (provisioning / deployment guide entry).

## Review results and fixes (2026-10-10)

Two adversarial review passes; no F-class (fix-now) defects. Fixes applied in the review-fix commit (comments and tests only):

- Startup on a blank or non-boolean switch value: `ExternalAccess:PartnerServiceRequestsEnabled` = `""` / `"yes"` / `"1"` throws at startup (fail-closed, verified). Added `PartnerSwitch_BlankOrNonBooleanValue_FailsStartup` (`""`, `"yes"`) to `ModuleEntitlementResolverTests`. `ExternalAccessModule.cs` and `appsettings.template.json` comments now say any non-boolean value, INCLUDING EMPTY, fails startup; remove the setting to turn it off.
- Stale pre-A7 comments corrected to the A7 model (default Tier-1 modules by server-resolved principal kind: workforce -> legal-front-door + policy-library; CIAM -> assigned-work, + legal-front-door when the partner switch is on; `sprk_approlemodulemap` = extras; service requests = own `sprk_requestedby` rows for workforce, and for CIAM only when the switch is on): `MeEntitlementsEndpoint.cs`, `ExternalAccessEndpoints.cs` (route comment + description), `Dtos/MeEntitlementsResponse.cs`, `NoAccessListReader.cs` (dropped `ModuleEntitlementResolver` from the "no guards" precedent), `ExternalAccessModule.cs` (Service Requests label), and client comments in `widgetRegistry.ts`, `ServiceRequestsWidget.tsx`, `me-client.ts`. `planes: ['workforce']` is unchanged; widening the client plane is task 052.

Known limits NOT fixed here:

- (K1) App-only tokens that resolve as Workforce now receive default modules (no records are reachable). Fix planned after task 012 merges: deny when `CallerIdentity.FromPrincipal(user).Kind != UserDelegated` in `WorkforcePrincipalStrategy` / `CallerPrincipalAuthorizationFilter`.
- (K2) Tier-1 is not enforced per module route; a task 035 constraint was added for a shared `IsEntitled`.
- (K3) The client still hides service requests from CIAM; a task 052 constraint was added.
- (K5) A systemuser without a linked contact sees 0 service requests (pre-existing, pinned by test).

Provisioning F.5: `.claude/constraints/provisioning.md:290` per_env_settings step is N/A. `ExternalAccessOptions` demands no key (absent = off); an operator turns the switch on by adding the App Service setting `ExternalAccess__PartnerServiceRequestsEnabled=true`. No `ExternalAccess` key is catalogued anywhere today.
