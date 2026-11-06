# Test diet report - spaarke-SPA-external-access-platform-r3

**Run date**: 2026-11-03
**Branch**: work/spaarke-SPA-external-access-platform-r3 (HEAD d1c8557ed9 plus uncommitted notes)
**Scope**: the 168 C# test files changed by R3's merged PRs #1795, #1799, #1880, #2086, #2141, #2175, #2279, #2360, #2386, #2450, #2461, #2500 (union list in the session scratchpad `r3-tests.txt`). 6 of the 168 are `src/server/services/Sprk.Provisioning.ControlPlane.Tests/**`. 18 files contain no test methods (fixtures, doubles, builders; plus the live-test file whose custom attribute the scan does not see): see "Support files".
**Method**: skill `.claude/skills/test-diet/SKILL.md`; ADR-038 section 7 (B1-B17) plus Amendments A1-A3; `.claude/constraints/testing.md`. Every test method was enumerated mechanically (`[Fact]` / `[Theory]` / `[SkippableFact]`; a theory counts once, whatever its rows) and screened for B1, B3, B4, B6, B8, B10, B13, B15 and B17 signals, constant-mirror shape, reflection, DI-resolution, assertion-free bodies and DETACHED shape. Roughly 90 flagged or sampled bodies were then read. The remaining methods were not read one by one: a MAINTAIN verdict means "no ban signal found and the name states the behaviour", not "reviewed by hand". Reclassification by a reviewer is expected (skill: the reviewer's call is final).
**R3 attribution**: a method counts as "touched by R3" when any of its non-trivial lines appears among the added lines of R3's PR patches (`gh api .../pulls/<n>/files`). When unsure it is counted as touched.

## Summary

| Class | Count | of which touched by R3 | swept only (not touched) | Action |
|---|---:|---:|---:|---|
| FITNESS FUNCTION (`tests/Spaarke.ArchTests/**`, heuristic 0) | 120 | 18 | 102 | KEEP; heuristics 2-12 not applied |
| MAINTAIN (behaviour test at a KEEP path, grandfathered, or in the ControlPlane layout) | 2966 | 1658 | 1308 | confirmed (per-file table below) |
| SCAFFOLDING (DELETE candidate) | 18 | 4 | 14 | review the method-level list below; 2 are under KEEP paths (PATH-VIOLATION-PROTECTED) |
| AMBIGUOUS (reviewer judgment) | 51 | 13 | 38 | listed below |
| ORPHAN (subject deleted) | 0 | 0 | 0 | none found |
| DETACHED (no production call) | 2 | 1 | 1 | rewrite against production code or delete (A3) |
| PATH-VIOLATION (file added after 2026-10-12 outside a KEEP path) | 872 | 816 | 56 | git mv commands below |
| **Total test methods in the touched files** | **4029** | **2510** | **1519** | - |

Files: 168 (150 with test methods, 17 support-only). The PATH-VIOLATION figure counts behavioural methods that would be MAINTAIN if their file stood at a KEEP path; moving the files converts them. SCAFFOLDING/AMBIGUOUS/DETACHED methods inside those same files are counted in their own class, and the file is still a path violation.

The SPA's vitest tests (`src/client/external-spa/tests/**`) are out of the skill's C# scope and were not classified.

Untouched-method note: "swept only" are methods in a touched file that R3 did not add or change (touch-radius rule). They are reported separately below; they are not R3's to defend.

### Path rules applied

- `git cat-file -e e91218e1f:<path>` (master on 2026-10-12): 24 test files under `tests/unit/Sprk.Bff.Api.Tests/**` existed then and are grandfathered (heuristic 1 skipped; heuristics 2-12 applied to their methods). 37 did not exist then and are PATH-VIOLATION.
- Of those, files that R3 itself added (status `added` in an R3 PR) versus files another project added after 2026-10-12 and R3 only modified are separated in the move list. Several R3-added files date from 2026-10-10 and 10-11, before the cut-off date but after the master snapshot the clause names: the clause is keyed on presence on master, so they count as new.
- `src/server/services/Sprk.Provisioning.ControlPlane.Tests/**` follows the ControlPlane project's own layout (its own xunit project beside the service). ADR-038 section 2 and the skill list KEEP paths only under `tests/`; no path verdict is given for these six files and no `git mv` is proposed. Behaviour classification still applies.
- `tests/integration/Sprk.Bff.Api.IntegrationTests/Events/EventRoutesLiveTests.cs` (uses a custom `[LiveEventsFact]`, so the method scan counts 0): outside the KEEP set and not named by the grandfather clause. See the Ambiguous table.
- `tests/Spaarke.ArchTests/**`: all methods FITNESS FUNCTION. The scan counts only test-method declarations at class-member indentation; source fixtures embedded in raw strings are not counted.

## Delete commands (DO NOT auto-execute - reviewer judgment required)

```bash
# No whole-file deletions: every file in scope contains behavioural tests.
# Method-level deletions only (remove the specific [Fact]/[Theory] with the Edit tool).
# src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/ContactIdentityBindingSchemaPackagedTests.cs
#   - DELETE method: Scans_FindWhatIsThere_AndNotWhatIsNot (DETACHED (A3); swept, not R3) - exercises the test file's private XML helpers `IsSecured` / `KeyAttributes` / `Permissions` over seeded XML; no production code is called. Claimed behaviour: the packaged-schema scans are not vacuous. Rewrite only if the helpers move into production
# tests/integration/auth/UnifiedAccessControl/IdentityBinding/ContactBindingDecisionTests.cs   [KEEP path: PATH-VIOLATION-PROTECTED, needs same-PR replacement or A3 evidence]
#   - DELETE method: TheIntentLookup_CarriesNoGrant (B8; swept, not R3) - reflects over `BindIntentLookup` properties incl. NonPublic to prove no member contains "Grant"; a structural guard that belongs in tests/Spaarke.ArchTests (fitness function), not here
# tests/integration/auth/UnifiedAccessControl/IdentityBinding/ContactIdentityBinderOwnerTeamTests.cs   [KEEP path: PATH-VIOLATION-PROTECTED, needs same-PR replacement or A3 evidence]
#   - DELETE method: WithOwner_CopiesEveryPropertyOfNewContactDetails_AndSetsTheOwner (B8+B17; R3-touched) - invokes the private static `ContactIdentityBinder.WithOwner` by reflection and checks field-by-field that a copy preserved every property (B17). Replace with a behaviour test through the public binder create path (the OwnerTeam tests already assert the owner lands) or an ArchTest on the record
#   - DELETE method: TheCopyCheck_FiresOnACopyThatDropsAProperty_AndNotOnACompleteOne (DETACHED (A3); R3-touched) - tests the test file's own `NotCopied` detector over hand-built `NewContactDetails` DTOs; no production member is called. Goes with the test above
# tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/DataverseContactIdentityStoreTests.cs
#   - DELETE method: TheBindIntentRead_HasNoGrantLeg (B8; swept, not R3) - first assertion reflects over every method name (NonPublic) to prove two names are absent; the second assertion (path has no grant table) is behavioural: split it out and keep that half
# tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalAccessAuditTests.cs
#   - DELETE method: TheCategory_IsUnderSprkBffApi_AndDedicatedToTheAudit (B6; R3-touched) - two assertions on one constant string; `TheAuditCategory_IsEnabledAtInformation_UnderTheProductionSettings` already proves the category passes the production filter
# tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ModuleEntitlementResolverTests.cs
#   - DELETE method: OutsideCounselEntitlements_IsTheBlanketOutsideCounselSet (B6; swept, not R3) - asserts a static set equals its one-element literal (1:1 mirror); the resolver behaviour tests cover its use
# tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseDescribeHandlerTests.cs
#   - DELETE method: HandlerType_IsRegisteredInDi (B3; swept, not R3) - DI-registration test; the tool-framework assembly scan is verified by app start / the FR-P0-04 bijection health check
#   - DELETE method: Handler_IsDiscoverableByHandlerClassName (B6; swept, not R3) - asserts HandlerId == nameof(Handler): the member is `nameof(...)` (1:1 mirror)
#   - DELETE method: SupportedToolTypes_IsNonEmpty (B10; swept, not R3) - only asserts NotBeNullOrEmpty on a constant list
# tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseReadQueryHandlerTests.cs
#   - DELETE method: HandlerType_IsRegisteredInDi (B3; swept, not R3) - DI-registration test; the tool-framework assembly scan is verified by app start / the FR-P0-04 bijection health check
#   - DELETE method: Handler_IsDiscoverableByHandlerClassName (B6; swept, not R3) - asserts HandlerId == nameof(Handler): the member is `nameof(...)` (1:1 mirror)
#   - DELETE method: SupportedToolTypes_IsNonEmpty (B10; swept, not R3) - only asserts NotBeNullOrEmpty on a constant list
# tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseSearchDataHandlerTests.cs
#   - DELETE method: HandlerType_IsRegisteredInDi (B3; swept, not R3) - DI-registration test; the tool-framework assembly scan is verified by app start / the FR-P0-04 bijection health check
#   - DELETE method: Handler_IsDiscoverableByHandlerClassName (B6; swept, not R3) - asserts HandlerId == nameof(Handler): the member is `nameof(...)` (1:1 mirror)
#   - DELETE method: SupportedToolTypes_IsNonEmpty (B10; swept, not R3) - only asserts NotBeNullOrEmpty on a constant list
# tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/EmailDraftToolHandlerTests.cs
#   - DELETE method: HandlerType_IsRegisteredInDi (B3; swept, not R3) - DI-registration test; the tool-framework assembly scan is verified by app start / the FR-P0-04 bijection health check
#   - DELETE method: Handler_IsDiscoverableByHandlerClassName (B6; swept, not R3) - asserts HandlerId == nameof(Handler): the member is `nameof(...)` (1:1 mirror)
# tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/DataverseWebApiClientCreateFailureTests.cs
#   - DELETE method: TheMessageKeepsTheShapeEnsureSuccessStatusCodeGave (B12+B13; R3-touched) - compares against the framework default `HttpRequestException` text; name has no scenario/outcome split. Keep only if a caller parses that text
#   - DELETE method: ASuccessfulCreate_StillReturnsTheIdFromTheEntityIdHeader (B8; R3-touched) - sets the private `_httpClient` field by reflection (NonPublic) and builds an unused HttpClient. REWRITE via `ClientAnswering` with the entity-id header rather than delete: the success-path id parse is worth a test
```

KEEP-path candidates need more than a delete: `ContactBindingDecisionTests.TheIntentLookup_CarriesNoGrant` (move to an ArchTest), `ContactIdentityBinderOwnerTeamTests.WithOwner_CopiesEveryPropertyOfNewContactDetails_AndSetsTheOwner` and its DETACHED control `TheCopyCheck_FiresOnACopyThatDropsAProperty_AndNotOnACompleteOne` (replace with a public-path test, then both go together).

Whole-class pattern outside R3's files: the `Handler_IsDiscoverableByHandlerClassName`, `SupportedToolTypes_IsNonEmpty` and `HandlerType_IsRegisteredInDi` trio exists in about 25 other handler test files under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/` that R3 never touched. They are outside this diet's scope; one sweep would remove them.

## Path-move commands

Moves cross test projects, so each needs its namespace/using/csproj reference adjusted and a `dotnet build` of both test projects before the next move. Helper files (`ReadProofHttp.cs`, `AppOnlySelectProjection.cs`, `TestInfrastructure/*`) are shared by the moved files and the tests that stay; move them last or into `tests/integration/Shared/`.

```bash
# --- files R3 added (or created within R3 PRs) ---
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalDeleteOwnTests.cs tests/integration/contract/Api/ExternalAccess/ExternalDeleteOwnTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalDeleteServiceRequestTests.cs tests/integration/contract/Api/ExternalAccess/ExternalDeleteServiceRequestTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalIntakeEndpointsTests.cs tests/integration/contract/Api/ExternalAccess/ExternalIntakeEndpointsTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageEndpointsTests.cs tests/integration/contract/Api/ExternalAccess/ExternalMessageEndpointsTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageSendTests.cs tests/integration/contract/Api/ExternalAccess/ExternalMessageSendTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMultiRootDocumentRouteTests.cs tests/integration/contract/Api/ExternalAccess/ExternalMultiRootDocumentRouteTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMultiRootListReadTests.cs tests/integration/contract/Api/ExternalAccess/ExternalMultiRootListReadTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalNotificationFeedTests.cs tests/integration/contract/Api/ExternalAccess/ExternalNotificationFeedTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalRecordDocumentRoutesTests.cs tests/integration/contract/Api/ExternalAccess/ExternalRecordDocumentRoutesTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalRegistrationEndpointsTests.cs tests/integration/contract/Api/ExternalAccess/ExternalRegistrationEndpointsTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalRootEventReadTests.cs tests/integration/contract/Api/ExternalAccess/ExternalRootEventReadTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/GrantAccessNotifierExternalTests.cs tests/integration/auth/UnifiedAccessControl/GrantAccessNotifierExternalTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/AuthorizationModuleCiamTests.cs tests/integration/auth/ExternalAccess/AuthorizationModuleCiamTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/AuthorizationModuleCopilotAudienceTests.cs tests/integration/auth/ExternalAccess/AuthorizationModuleCopilotAudienceTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/DI/AuthorizationModuleWorkforceHomeTests.cs tests/integration/auth/ExternalAccess/AuthorizationModuleWorkforceHomeTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ContactIdentityBinderFactoryOwnEnvironmentTests.cs tests/integration/auth/UnifiedAccessControl/IdentityBinding/ContactIdentityBinderFactoryOwnEnvironmentTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ContactIdentityBinderFactoryReadProofTests.cs tests/integration/auth/UnifiedAccessControl/IdentityBinding/ContactIdentityBinderFactoryReadProofTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/DataverseContactIdentityStoreInviteStampTests.cs tests/integration/auth/UnifiedAccessControl/IdentityBinding/DataverseContactIdentityStoreInviteStampTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalAccessAuditTests.cs tests/unit/domain/ExternalAccess/ExternalAccessAuditTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalAccessOptionsValidatorTests.cs tests/unit/domain/ExternalAccess/ExternalAccessOptionsValidatorTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalDataServiceServiceRequestCreateTests.cs tests/integration/data-mutation/ExternalAccess/ExternalDataServiceServiceRequestCreateTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalMultiRootRuleTests.cs tests/integration/auth/ExternalAccess/ExternalMultiRootRuleTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/InviteEmailStampTests.cs tests/integration/auth/UnifiedAccessControl/IdentityBinding/InviteEmailStampTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/SdkColumnReadPermissionSourceTests.cs tests/integration/auth/ExternalAccess/SdkColumnReadPermissionSourceTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/SecuredColumnReadProofTests.cs tests/integration/auth/ExternalAccess/SecuredColumnReadProofTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/OptionsValidation/SystemSenderMailboxConfigurationTests.cs tests/unit/domain/OptionsValidation/SystemSenderMailboxConfigurationTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationOpenThreadGateTests.cs tests/integration/auth/Communication/CommunicationOpenThreadGateTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Communication/InboundInternalOnlyFlagTests.cs tests/integration/data-mutation/Communication/InboundInternalOnlyFlagTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Dataverse/DataverseWebApiClientCreateFailureTests.cs tests/unit/domain/Dataverse/DataverseWebApiClientCreateFailureTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Registration/PortalNotificationRequestTests.cs tests/unit/domain/Registration/PortalNotificationRequestTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Registration/RegistrationEnvironmentComparisonTests.cs tests/unit/domain/Registration/RegistrationEnvironmentComparisonTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Registration/WorkAccountNoticeTests.cs tests/unit/domain/Registration/WorkAccountNoticeTests.cs
# --- files another project added after 2026-10-12 that R3 only modified (path violation inherited, not R3-authored) ---
git mv tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalDocumentNarrowingRouteTests.cs tests/integration/contract/Api/ExternalAccess/ExternalDocumentNarrowingRouteTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Registration/DemoExpirationJobRetryTests.cs tests/unit/domain/Registration/DemoExpirationJobRetryTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Registration/DemoExpirationJobTargetEnvironmentTests.cs tests/unit/domain/Registration/DemoExpirationJobTargetEnvironmentTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Registration/DemoExpirationReadTests.cs tests/unit/domain/Registration/DemoExpirationReadTests.cs
git mv tests/unit/Sprk.Bff.Api.Tests/Services/Registration/DemoSpeGrantLifecycleTests.cs tests/unit/domain/Registration/DemoSpeGrantLifecycleTests.cs
```

Proposed destinations follow the mirror-`src/` rule (testing.md 2): HTTP route/endpoint tests to `contract`, authorization and identity-binding to `auth`, write/stamp paths to `data-mutation`, pure domain logic to `unit/domain`. `ExternalAccessAuditTests` and `ExternalAccessOptionsValidatorTests` are config/logging checks with no better KEEP home than `unit/domain`.

Path-violation methods by file (behavioural methods that become MAINTAIN after the move):

| File (tests/unit/Sprk.Bff.Api.Tests/...) | Added by | Methods affected | R3-touched |
|---|---|---:|---:|
| Api/ExternalAccess/ExternalDeleteOwnTests.cs | R3 | 78 | 78 |
| Api/ExternalAccess/ExternalDeleteServiceRequestTests.cs | R3 | 35 | 35 |
| Api/ExternalAccess/ExternalDocumentNarrowingRouteTests.cs | other project | 19 | 5 |
| Api/ExternalAccess/ExternalIntakeEndpointsTests.cs | R3 | 41 | 41 |
| Api/ExternalAccess/ExternalMessageEndpointsTests.cs | R3 | 60 | 60 |
| Api/ExternalAccess/ExternalMessageSendTests.cs | R3 | 52 | 50 |
| Api/ExternalAccess/ExternalMultiRootDocumentRouteTests.cs | R3 | 17 | 17 |
| Api/ExternalAccess/ExternalMultiRootListReadTests.cs | R3 | 55 | 55 |
| Api/ExternalAccess/ExternalNotificationFeedTests.cs | R3 | 56 | 56 |
| Api/ExternalAccess/ExternalRecordDocumentRoutesTests.cs | R3 | 58 | 58 |
| Api/ExternalAccess/ExternalRegistrationEndpointsTests.cs | R3 | 43 | 43 |
| Api/ExternalAccess/ExternalRootEventReadTests.cs | R3 | 17 | 17 |
| Api/ExternalAccess/GrantAccessNotifierExternalTests.cs | R3 | 34 | 34 |
| Infrastructure/DI/AuthorizationModuleCiamTests.cs | R3 | 33 | 33 |
| Infrastructure/DI/AuthorizationModuleCopilotAudienceTests.cs | R3 | 6 | 6 |
| Infrastructure/DI/AuthorizationModuleWorkforceHomeTests.cs | R3 | 27 | 27 |
| Infrastructure/ExternalAccess/ContactIdentityBinderFactoryOwnEnvironmentTests.cs | R3 | 3 | 3 |
| Infrastructure/ExternalAccess/ContactIdentityBinderFactoryReadProofTests.cs | R3 | 6 | 6 |
| Infrastructure/ExternalAccess/DataverseContactIdentityStoreInviteStampTests.cs | R3 | 8 | 8 |
| Infrastructure/ExternalAccess/ExternalAccessAuditTests.cs | R3 | 6 | 6 |
| Infrastructure/ExternalAccess/ExternalAccessOptionsValidatorTests.cs | R3 | 4 | 4 |
| Infrastructure/ExternalAccess/ExternalDataServiceServiceRequestCreateTests.cs | R3 | 15 | 15 |
| Infrastructure/ExternalAccess/ExternalMultiRootRuleTests.cs | R3 | 34 | 34 |
| Infrastructure/ExternalAccess/InviteEmailStampTests.cs | R3 | 7 | 7 |
| Infrastructure/ExternalAccess/SdkColumnReadPermissionSourceTests.cs | R3 | 10 | 10 |
| Infrastructure/ExternalAccess/SecuredColumnReadProofTests.cs | R3 | 10 | 10 |
| OptionsValidation/SystemSenderMailboxConfigurationTests.cs | R3 | 15 | 15 |
| Services/Communication/CommunicationOpenThreadGateTests.cs | R3 | 32 | 32 |
| Services/Communication/InboundInternalOnlyFlagTests.cs | R3 | 33 | 33 |
| Services/Dataverse/DataverseWebApiClientCreateFailureTests.cs | R3 | 5 | 5 |
| Services/Registration/DemoExpirationJobRetryTests.cs | other project | 13 | 0 |
| Services/Registration/DemoExpirationJobTargetEnvironmentTests.cs | other project | 8 | 0 |
| Services/Registration/DemoExpirationReadTests.cs | other project | 5 | 0 |
| Services/Registration/DemoSpeGrantLifecycleTests.cs | other project | 14 | 0 |
| Services/Registration/PortalNotificationRequestTests.cs | R3 | 4 | 4 |
| Services/Registration/RegistrationEnvironmentComparisonTests.cs | R3 | 5 | 5 |
| Services/Registration/WorkAccountNoticeTests.cs | R3 | 4 | 4 |

## Ambiguous - reviewer judgment

| File:Method | R3-touched | Ambiguity reason | Suggestion |
|---|---|---|---|
| ControlPlane.Tests/Handlers/ContactIdentityBindingSchemaPackagedTests.cs:H7bVerifiesExactlyThePackagedIdentityLinkLock | no (swept) | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| ControlPlane.Tests/Handlers/H7bContactLocksPackagedTests.cs:ThePackageSecuresEveryS19AndS22Column | yes | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| ControlPlane.Tests/Handlers/H7bContactLocksPackagedTests.cs:H7bAcceptsThePackagedLocks | yes | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| ControlPlane.Tests/Handlers/H7bContactLocksPackagedTests.cs:H7bRefusesAPackageThatDeviatesFromTheLock | yes | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| integration/auth/UnifiedAccessControl/IdentityBinding/CiamContactBindingTests.cs:TheCiamPlaneNeverCreatesAContact | no (swept) | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| integration/auth/UnifiedAccessControl/IdentityBinding/CiamContactBindingTests.cs:TheBindRefusesAContactBoundOnTheWorkforcePlane | no (swept) | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| integration/auth/UnifiedAccessControl/IdentityBinding/CiamContactBindingTests.cs:TheBindRefusesAContactASystemUserLinksTo | no (swept) | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| integration/auth/UnifiedAccessControl/IdentityBinding/CiamContactBindingTests.cs:OnlyActiveContactsResolve | no (swept) | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| integration/auth/UnifiedAccessControl/IdentityBinding/ContactBindingDecisionTests.cs:TheWindowAndTheSkew_AreTheOwnersNumbers | no (swept) | B6/B16: asserts two constants equal literals (30 days, 5 min); pins an owner decision ("owner round 97") | Keep if the owner wants the pin; otherwise delete |
| integration/auth/UnifiedAccessControl/IdentityBinding/ContactBindingDecisionTests.cs:TheDecisionAsksForEachLookupOnlyWhenItNeedsIt | no (swept) | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| integration/auth/UnifiedAccessControl/IdentityBinding/ContactIdentityBinderOwnerTeamTests.cs:TheRefusalReasonCode_IsRegistrationsOwn | yes | B6: asserts two constants equal each other and a literal; purpose is "one code, not a second copy" | Convert to a compile-time shared constant, or keep as an ArchTest |
| integration/auth/UnifiedAccessControl/IdentityBinding/InviteOrganizationResolverTests.cs:TheDomainGrammar | yes | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| integration/auth/UnifiedAccessControl/IdentityBinding/WorkforceMembershipTests.cs:MembershipIsNeverInferredFromTheEmailDomainOrAnExtUpn | yes | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| integration/auth/UnifiedAccessControl/RestrictedExternalShareRemoverTests.cs:TheLeaseIsReleasedAfterThePass | yes | B13: prose name without the `_Scenario_Expected` split (heuristic 2); assertion is behavioural and non-trivial | Rename to `{Method}_{Scenario}_{ExpectedResult}`; no deletion |
| integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs:TheEntitySetAndPrivilegeNames_AreTheLiveVerifiedValues | no (swept) | B6: 8 constants equal literals; pins live-verified Dataverse names (a wrong privilege name is never held) | Keep if drift against the live names is the point; the filter behaviour tests would not catch a typo |
| integration/data-mutation/ChildAccessPermission/AccessTableAppOnlyWriterTests.cs:TheAccessTableSet_IsExactlyTheseTables | no (swept) | B6: set equality of a production static against the test's own literal list | Keep as the access-table census pin or fold into an ArchTest |
| integration/data-mutation/ChildAccessPermission/AccessTableAppOnlyWriterTests.cs:TheAccessColumnMap_IsBuiltFromTheReadersOwnConstants | yes | B6/B15: 77-line list of constants asserted to equal the production map built from the same constants | Likely an ArchTest/lock-step guard; reviewer decides |
| unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalDocumentNarrowingRouteTests.cs:TheAssistantsNarrowing_IsRegistered_OverTheAppOnlyReader | no (swept) | Heuristic 4 (B3) fires: resolves the narrowing from a built provider. It does assert behaviour (visibility + reasons + one key read), which is the B3 replacement | Likely MAINTAIN once the file is at a KEEP path |
| unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalDocumentNarrowingRouteTests.cs:TheInternalRoutesNarrowing_IsRegistered_OverTheAppOnlyReader | no (swept) | Same as above | Same |
| unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalMessageSendTests.cs:Fr20_TheMessageCreatePrivilege_IsTheLiveName | no (swept) | B6: one constant equals a literal; reason given: wrong name denies every post | Same as above; consider a live-verification script instead |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalAccessAuditTests.cs:TheGuard_SeesAnOverride_ThatRaisesTheAuditCategory | yes | Control for a config guard: exercises only the test's own `EffectiveMinimumLevel` over the framework logging filter plus the `Category` constant (close to DETACHED) | Keep as the guard's must-fire control or fold into an ArchTest |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ModuleEntitlementResolverTests.cs:PartnerInventionIntakeEnabled_DefaultsOff | yes | B16: asserts a POCO default (false); the default is a fail-closed switch | Keep if the default is security-relevant (it is), else delete |
| unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseToolNameFreezeTests.cs:WriteHandlers_AreChatOnly | no (swept) | B6/B16: same as ReadHandlers_AreChatOnly (untouched, swept) | Same |
| unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseToolNameFreezeTests.cs:ReadHandlers_AreChatOnly | yes | B6/B16: asserts a metadata property on three constructed handlers | Part of the ADR-039 frozen surface; reviewer decides |
| integration/auth/UnifiedAccessControl/ExistingCiamAccountInviteTests.cs: 25 methods calling `InviteRoute()` (0 R3-touched) | no (swept) | B8: private route handler `InviteExternalUserAsync` reached by reflection (`BindingFlags.NonPublic`); assertions on the returned IResult are behavioural | Drive the public `/invite` route through the host; then the helper and B8 go. Methods: InviteRoute_OfAPersonWithACiamAccount_AnswersLikeANewAccount_OnlyTheLogSaysExistingAccount, TheInviteResponses_NeverCarryTheCode, InviteRoute_WhenTheInviteEmailFails_ReportsEmailNotSent_IdenticallyOnBothBranches, InviteRoute_WhenTheInviteEmailIsSent_ReportsNoFailure_IdenticallyOnBothBranches, ARequestSuppliedTenant_IsRefused400, Invite_ACustomerTenantDomain_IsAWorkAccountInvite_AndAWorkforceSignInBindsItOnce, Invite_WorkAccount_SendsTheFixedNotice_ToTheStampedAddress_WithNoCodeOrIds, Invite_WorkAccount_ANoticeThatFails_ClearsTheStamp_AndIsReportedEmailNotSent, APortalAccountInvite_SendsNoWorkAccountNotice, NoCiamHost_AWorkAccountInvite_IsSent_OnBothRoutes, NoCiamHost_AMalformedRequest_Is400_BeforeAnyLookup, NoCiamHost_APortalAccountInvite_IsStill503CiamNotConfigured, EveryOtherDefiniteAnswer_IsAPortalAccountInvite, ALookupWithNoDefiniteAnswer_Is503_AndNothingIsWritten, ASuppliedSendAsPortalAccount_IsRefused400_WhateverItsValue, NoCustomerTenantsConfigured_MakesNoLookup_AndInvitesAPortalAccount, AWorkAccountInvite_ToAContactBoundToAPortalAccount_IsRefused409, AnInvite_ToARemovedContact_IsRefused_SetMarker409_UnprovenMarker503, AnInvite_ToAnUnboundContact_WhoseEmailARemovedInactiveContactCarries_IsRefused, AnInvite_ToAnUnmarkedContact_IsNotRefused, AWorkAccountInvite_ToAWorkforceBoundContact_IsRefused_WithTheExistingCode, Invite_ThePersonIsRemovedWhileTheInviteIsInProgress_IsNotBound_AndNothingIsSent, Invite_ThePersonIsNotRemoved_IsBoundAndEmailed, InviteRoute_ToAPersonWhoAlreadyHasAnAccount_SendsNothing_WritesNothing_AndSaysSo, InviteRoute_ToAnUnboundContact_IsInvitedAsBefore |
| integration/auth/UnifiedAccessControl/GrantSendsInviteTests.cs: 2 methods calling `InviteRoute()` (2 R3-touched) | partly | B8: private route handler `InviteExternalUserAsync` reached by reflection (`BindingFlags.NonPublic`); assertions on the returned IResult are behavioural | Drive the public `/invite` route through the host; then the helper and B8 go. Methods: Invite_AfterAGrantWhoseInviteFailed_SendsTheInvite_AndWritesNoGrant, Invite_AfterAGrantWhoseWorkAccountNoticeFailed_SendsTheNotice_AndWritesNoGrant |
| tests/integration/Sprk.Bff.Api.IntegrationTests/Events/EventRoutesLiveTests.cs (all `[LiveEventsFact]` methods) | yes | outside the KEEP set; not named by the grandfather clause; live-environment tests skipped without credentials | Accept as a live-environment project or move; no deletion |

## Maintain - confirmed (no action)

Per-file counts (one row per method would be ~3,600 rows). "R3" = methods R3 added or changed; "swept" = present before, untouched. Counts exclude the methods listed in the Delete and Ambiguous sections.

| File | KEEP path / basis | MAINTAIN methods | R3 | swept |
|---|---|---:|---:|---:|
| ControlPlane.Tests/Handlers/ContactIdentityBindingSchemaPackagedTests.cs | ControlPlane layout (not a tests/ KEEP path) | 3 | 2 | 1 |
| ControlPlane.Tests/Handlers/H7bContactLocksPackagedTests.cs | ControlPlane layout (not a tests/ KEEP path) | 3 | 3 | 0 |
| ControlPlane.Tests/Handlers/SecureRecordSetup/DataverseWebApiSecureRecordSetupTests.cs | ControlPlane layout (not a tests/ KEEP path) | 16 | 3 | 13 |
| ControlPlane.Tests/Handlers/SecureRecordSetup/H7bSecureRecordSetupHandlerTests.cs | ControlPlane layout (not a tests/ KEEP path) | 23 | 1 | 22 |
| ControlPlane.Tests/Handlers/SecureRecordSetup/SecureRecordSetupProcedureTests.cs | ControlPlane layout (not a tests/ KEEP path) | 50 | 50 | 0 |
| integration/auth/ExternalAccess/ExternalMultiRootSecureRootsTests.cs | integration/auth | 14 | 0 | 14 |
| integration/auth/ExternalAccess/ExternalWorkforceLinkEndpointsTests.cs | integration/auth | 20 | 20 | 0 |
| integration/auth/ExternalAccess/SpaarkeGuestTokenValidatorTests.cs | integration/auth | 8 | 8 | 0 |
| integration/auth/UnifiedAccessControl/AccessCacheFaultCachingTests.cs | integration/auth | 19 | 0 | 19 |
| integration/auth/UnifiedAccessControl/AccessCacheInvalidationTests.cs | integration/auth | 7 | 0 | 7 |
| integration/auth/UnifiedAccessControl/AssignedAccessMaterializerTests.cs | integration/auth | 96 | 84 | 12 |
| integration/auth/UnifiedAccessControl/AssignedAccessSendsInviteTests.cs | integration/auth | 11 | 0 | 11 |
| integration/auth/UnifiedAccessControl/AutomaticInviteOrganizationTests.cs | integration/auth | 9 | 0 | 9 |
| integration/auth/UnifiedAccessControl/ChildRecordContainerResolutionTests.cs | integration/auth | 111 | 0 | 111 |
| integration/auth/UnifiedAccessControl/ContactGrantAuthorizationTests.Invite.cs | integration/auth | 11 | 0 | 11 |
| integration/auth/UnifiedAccessControl/ContactGrantAuthorizationTests.cs | integration/auth | 68 | 0 | 68 |
| integration/auth/UnifiedAccessControl/DocumentDestroyAuthorizationTests.cs | integration/auth | 31 | 21 | 10 |
| integration/auth/UnifiedAccessControl/DocumentPointerDeleteVerdictTests.cs | integration/auth | 18 | 18 | 0 |
| integration/auth/UnifiedAccessControl/ExistingCiamAccountInviteTests.cs | integration/auth | 42 | 0 | 42 |
| integration/auth/UnifiedAccessControl/ExternalAccessAdministratorAuthorityTests.cs | integration/auth | 13 | 0 | 13 |
| integration/auth/UnifiedAccessControl/ExternalModuleColumnAllowListTests.cs | integration/auth | 51 | 28 | 23 |
| integration/auth/UnifiedAccessControl/ExternalTodoScopeTests.cs | integration/auth | 78 | 0 | 78 |
| integration/auth/UnifiedAccessControl/GrantCoreRemovedPersonTests.cs | integration/auth | 12 | 0 | 12 |
| integration/auth/UnifiedAccessControl/GrantExternalContactEmailTests.cs | integration/auth | 8 | 0 | 8 |
| integration/auth/UnifiedAccessControl/GrantLifecycleCharacterizationTests.cs | integration/auth | 51 | 0 | 51 |
| integration/auth/UnifiedAccessControl/GrantRowOwnershipTests.cs | integration/auth | 25 | 0 | 25 |
| integration/auth/UnifiedAccessControl/GrantSendsInviteTests.cs | integration/auth | 18 | 18 | 0 |
| integration/auth/UnifiedAccessControl/GrantorCeilingTests.cs | integration/auth | 41 | 0 | 41 |
| integration/auth/UnifiedAccessControl/IdentityBinding/CiamContactBindingTests.cs | integration/auth | 39 | 24 | 15 |
| integration/auth/UnifiedAccessControl/IdentityBinding/ContactBindingDecisionTests.cs | integration/auth | 104 | 30 | 74 |
| integration/auth/UnifiedAccessControl/IdentityBinding/ContactIdentityBinderOwnerTeamTests.cs | integration/auth | 29 | 29 | 0 |
| integration/auth/UnifiedAccessControl/IdentityBinding/ContactIdentityBinderWorkforceGuestTests.cs | integration/auth | 12 | 3 | 9 |
| integration/auth/UnifiedAccessControl/IdentityBinding/CreateAroundRemovalBindingTests.cs | integration/auth | 13 | 13 | 0 |
| integration/auth/UnifiedAccessControl/IdentityBinding/ExternalAccessContactSecuritySchemaAgreementTests.cs | integration/auth | 8 | 8 | 0 |
| integration/auth/UnifiedAccessControl/IdentityBinding/IdentityColumnsReadProofBindingTests.cs | integration/auth | 8 | 8 | 0 |
| integration/auth/UnifiedAccessControl/IdentityBinding/IdentityLinkReconciliationTests.cs | integration/auth | 45 | 31 | 14 |
| integration/auth/UnifiedAccessControl/IdentityBinding/InviteOrganizationResolverTests.cs | integration/auth | 13 | 13 | 0 |
| integration/auth/UnifiedAccessControl/IdentityBinding/InviteStampBindingTests.cs | integration/auth | 26 | 26 | 0 |
| integration/auth/UnifiedAccessControl/IdentityBinding/RemovedMarkerBindingTests.cs | integration/auth | 24 | 24 | 0 |
| integration/auth/UnifiedAccessControl/IdentityBinding/WorkforceMembershipTests.cs | integration/auth | 12 | 7 | 5 |
| integration/auth/UnifiedAccessControl/ImpersonatedRootSetSourceTests.cs | integration/auth | 16 | 3 | 13 |
| integration/auth/UnifiedAccessControl/InternalUserShareTests.cs | integration/auth | 79 | 21 | 58 |
| integration/auth/UnifiedAccessControl/InviteAndGrantExternalContactEmailTests.cs | integration/auth | 4 | 4 | 0 |
| integration/auth/UnifiedAccessControl/InviteContactOwnerTests.cs | integration/auth | 11 | 11 | 0 |
| integration/auth/UnifiedAccessControl/InviteCreateAroundRemovalTests.cs | integration/auth | 5 | 5 | 0 |
| integration/auth/UnifiedAccessControl/PersonAccessAdministrationTests.cs | integration/auth | 38 | 38 | 0 |
| integration/auth/UnifiedAccessControl/PersonOrganizationGrantSpeCleanupTests.cs | integration/auth | 8 | 8 | 0 |
| integration/auth/UnifiedAccessControl/ProjectClosureCascadeTests.cs | integration/auth | 64 | 57 | 7 |
| integration/auth/UnifiedAccessControl/RegistrationSecureRecordPlacementTests.cs | integration/auth | 9 | 0 | 9 |
| integration/auth/UnifiedAccessControl/RemovalSweepTests.cs | integration/auth | 21 | 21 | 0 |
| integration/auth/UnifiedAccessControl/RestrictedExternalShareRemoverTests.cs | integration/auth | 21 | 20 | 1 |
| integration/auth/UnifiedAccessControl/RevokeRouteDescriptionTests.cs | integration/auth | 2 | 2 | 0 |
| integration/auth/UnifiedAccessControl/SpeRevokeMatcherTests.cs | integration/auth | 49 | 30 | 19 |
| integration/auth/UnifiedAccessControl/StandingGrantWriterInvariantScriptTests.cs | integration/auth | 11 | 11 | 0 |
| integration/auth/UnifiedAccessControl/UnsentInviteRetractTests.cs | integration/auth | 9 | 9 | 0 |
| integration/auth/UnifiedAccessControl/WorkforceEmailNoHijackTests.cs | integration/auth | 30 | 5 | 25 |
| integration/contract/Api/Communication/CommunicationRecordAuthorizationContractTests.cs | integration/contract | 63 | 42 | 21 |
| integration/contract/Api/Communication/CommunicationRenameThreadContractTests.cs | integration/contract | 4 | 0 | 4 |
| integration/contract/Api/Communication/CommunicationSetThreadPinnedContractTests.cs | integration/contract | 4 | 0 | 4 |
| integration/contract/Api/ExternalAccess/ExternalAccessContractTests.cs | integration/contract | 28 | 0 | 28 |
| integration/contract/Api/ExternalAccess/ExternalMessagePostContractTests.cs | integration/contract | 5 | 5 | 0 |
| integration/contract/Api/ExternalAccess/ExternalProjectEventContractTests.cs | integration/contract | 13 | 12 | 1 |
| integration/data-mutation/ChildAccessPermission/AccessTableAppOnlyWriterTests.cs | integration/data-mutation | 36 | 12 | 24 |
| integration/data-mutation/ChildAccessPermission/ContactAccessColumnGuardTests.cs | integration/data-mutation | 6 | 6 | 0 |
| integration/data-mutation/ExternalAccess/NoAccessShareEnforcerTests.cs | integration/data-mutation | 58 | 34 | 24 |
| integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.RegardingPair.cs | integration/data-mutation | 26 | 2 | 24 |
| integration/data-mutation/RecordOwnership/SecureChildOwnershipAiToolTests.cs | integration/data-mutation | 36 | 2 | 34 |
| integration/data-mutation/RecordOwnership/SecureChildOwnershipExternalTests.cs | integration/data-mutation | 8 | 8 | 0 |
| integration/data-mutation/Signals/AssignTaskNotifierTests.cs | integration/data-mutation | 1 | 1 | 0 |
| integration/data-mutation/Signals/AssignTaskRecipientGatePlaneTests.cs | integration/data-mutation | 9 | 0 | 9 |
| integration/regression/Issue963_ExternalDataPagingTests.cs | integration/regression | 18 | 5 | 13 |
| integration/seam/Communication/CommunicationListThreadsSeamTests.cs | integration/seam | 13 | 1 | 12 |
| integration/seam/Communication/CommunicationPrivilegePrivacySeamTests.cs | integration/seam | 5 | 2 | 3 |
| integration/seam/Communication/CommunicationWorkspaceReadSeamTests.cs | integration/seam | 7 | 1 | 6 |
| integration/seam/ExternalAccess/UnifiedEvaluatorSeamTests.cs | integration/seam | 53 | 47 | 6 |
| unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/Tier2ScopeFilterInjectorTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 14 | 6 | 8 |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/CallerPrincipalResolverTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 33 | 27 | 6 |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/CallerPrincipalTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 23 | 15 | 8 |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ContactIdentitySchemaAgreementTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 5 | 2 | 3 |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/DataverseContactIdentityStoreTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 109 | 83 | 26 |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ExternalDataServiceTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 35 | 32 | 3 |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ModuleEntitlementResolverTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 31 | 26 | 5 |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/SubjectStandingGrantReaderTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 29 | 29 | 0 |
| unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/WorkforcePrincipalResolverTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 55 | 55 | 0 |
| unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseDescribeHandlerTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 47 | 42 | 5 |
| unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseReadQueryHandlerTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 56 | 49 | 7 |
| unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseSearchDataHandlerTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 40 | 37 | 3 |
| unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/DataverseToolNameFreezeTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 9 | 3 | 6 |
| unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/EmailDraftToolHandlerPerItemToolsTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 19 | 16 | 3 |
| unit/Sprk.Bff.Api.Tests/Services/Ai/Handlers/EmailDraftToolHandlerTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 12 | 0 | 12 |
| unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationAttachmentTextServiceTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 20 | 17 | 3 |
| unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationByRegardingReadTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 9 | 5 | 4 |
| unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationQueueFeedServiceTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 13 | 11 | 2 |
| unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationServiceMessageSendTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 28 | 27 | 1 |
| unit/Sprk.Bff.Api.Tests/Services/Communication/CommunicationThreadReadServiceTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 39 | 31 | 8 |
| unit/Sprk.Bff.Api.Tests/Services/Communication/DataverseRecordCreationTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 49 | 28 | 21 |
| unit/Sprk.Bff.Api.Tests/Services/Communication/ThreadResolverTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 101 | 69 | 32 |
| unit/Sprk.Bff.Api.Tests/Services/Dataverse/DocumentIndexParentResolverTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 24 | 24 | 0 |
| unit/Sprk.Bff.Api.Tests/Services/Registration/RegistrationContactLinkTests.cs | unit/Sprk.Bff.Api.Tests (grandfathered, existed on master 2026-10-12) | 13 | 9 | 4 |
| unit/domain/Dataverse/RecordOwnershipResolverTests.cs | unit/domain | 193 | 118 | 75 |

FITNESS FUNCTION files (all methods KEEP): ADR010_DITests.cs, AuthenticationSchemeCensusTests.cs, ContactCreateAroundRemovalGuardTests.cs, ContactCreateOwnerPolicyGuardTests.cs, IOptionsDriftTests.cs, RecordOwnerAssignmentCensusTests.cs, RemovalSweepSharedRemovalGuardTests.cs, RouteAuthorizationGuardTests.Runtime.cs, RouteAuthorizationGuardTests.cs, SecureRecordOwnerRoleSetParityTests.cs, SpeWriteSinkContainerProvenanceGuardTests.cs, StandingGrantProfileNamePinTests.cs, StandingGrantVerifyAgreementTests.cs. 120 methods.

Notes on MAINTAIN verdicts that a reader may expect to see flagged:

- Prose-style names such as `TheCiamPlaneNeverCreatesAContact` are the repo's house style and mostly state scenario and outcome; only the 12 where the name carries no split at all are listed as B13 ambiguities.
- Heuristic 4 (B3, `GetRequiredService<>`) fires on ~35 methods. Most resolve a fixture or the system under test from a built host and then assert behaviour (`ExternalDeleteOwnTests`, `AuthorizationModule*Tests`, `ModuleEntitlementResolverTests`). Only the pure registration checks (`HandlerType_IsRegisteredInDi` x4) are SCAFFOLDING.
- `ExternalAccessOptionsValidatorTests.Validate_NeverFails_SoStartupIsNotBlocked` and the `...DoesNotThrow` ThreadResolver/Inbound tests carry an outcome assertion beside the no-throw check, so B10 does not apply.
- `DataverseToolNameFreezeTests` pins GA MCP tool and argument names (ADR-039 external contract); kept as contract freezes except the two `*Handlers_AreChatOnly` tests listed as ambiguous.
- Methods whose assertions run through a helper (`AssertDenied`, `Assert403`, `EndpointTable.AssertMapped`) show zero `Should()` calls in the scan; the sample read shows real assertions, not B10.

### Support files (no test methods; keep with their consumers)

- ControlPlane.Tests/Handlers/SecureRecordSetup/FakeSecureRecordSetupDataverse.cs
- tests/Spaarke.ArchTests/BootedBff.cs
- tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Ledger.cs
- tests/integration/Shared/Dataverse/TestRecordContainerResolver.cs
- tests/integration/auth/UnifiedAccessControl/AssignedAccessTestDoubles.cs
- tests/integration/auth/UnifiedAccessControl/DocumentDestroyAuthorizationTestFixture.cs
- tests/integration/auth/UnifiedAccessControl/ExternalCollaborationTestFixture.cs
- tests/integration/auth/UnifiedAccessControl/IdentityBinding/IdentityBindingTestKit.cs
- tests/integration/auth/UnifiedAccessControl/IdentityBinding/InMemoryContactIdentityStore.cs
- tests/integration/auth/UnifiedAccessControl/IdentityBinding/InviteOrganizationTestKit.cs
- tests/integration/auth/UnifiedAccessControl/NoAccessEnforcementTestDoubles.cs
- tests/integration/contract/Api/Communication/CommunicationRecordAuthorizationHost.cs
- tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/AppOnlySelectProjection.cs
- tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/AccessibleRecordSetTestFactory.cs
- tests/unit/Sprk.Bff.Api.Tests/Infrastructure/ExternalAccess/ReadProofHttp.cs
- tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/CommunicationVisibilityFixtures.cs
- tests/unit/Sprk.Bff.Api.Tests/TestInfrastructure/IdentityModelLoggerIsolation.cs

## Count delta

- Test methods in files R3 added: 1218 (in 65 files). Methods R3 added or changed across all 168 files: 2510.
- Methods classified MAINTAIN: 2966 (+ 120 FITNESS FUNCTION kept as-is, + 872 PATH-VIOLATION that become MAINTAIN once moved).
- Methods classified SCAFFOLDING: 18, of which 4 R3-touched and 14 swept; DETACHED: 2; AMBIGUOUS: 51; ORPHAN: 0.
- Net post-diet expected count (touched files): 4029 - 20 = 4009 methods, before any reviewer-approved ambiguous deletions. Deleting R3-touched ones only: 2510 - 5 = 2505.

## ORPHAN check

None. R3's twelve PRs removed or renamed no `.cs` file under `src/` or `tests/` (checked on the PR file lists: no `removed` or `renamed` entries), and every non-ArchTest file references at least three production types that still exist under `src/server`. No test targets a deleted route, type or member.

## Reliability-registry findings (`tests/.reliability-registry.json`)

- Registry holds 7 `TimingSensitive` entries and 0 `ConcurrencySensitive`. None names a method in the R3 touched files (they are `JobsEndpointsTests.Trigger_RunsJobOutOfBand_RecordsRun`, `JobStatusSseIntegrationTests.PublishStatusUpdate_LatencyUnder1Second_*`, `ContextResolverIntegrationTests.ContextResolver_InitializesWithinThreeSeconds`, three `ScorecardCalculatorPerformanceTests` and `ScopeResolverServiceTests.SearchScopesAsync_PerformanceUnder1Second_*`).
- **Stale entries to delete in this PR: none.** R3 removed no timing or concurrency dependence from a listed test.
- Advisory (not a required action): `tests/Spaarke.ArchTests/ContactCreateOwnerPolicyGuardTests.cs` `Control_ManyAssignmentsToOneLocal_ResolveInBoundedTime` (R3-added fitness test) asserts `Stopwatch` elapsed under 5 s. `Stopwatch` is banned in tests (testing.md 2) and the test is wall-clock dependent but unregistered, so a loaded CI runner fails it with no retry. Prefer a step-count bound on the resolver; if a wall-clock bound is kept, register it as `TimingSensitive`.
- Other timing-API hits in scope are not timing-dependent assertions: `DateTime(Offset).UtcNow` used to build token expiries and fixture timestamps (`SpaarkeGuestTokenValidatorTests`, `ExternalWorkforceLinkEndpointsTests`, `AccessCache*Tests`, `EventRoutesLiveTests`, `H7bSecureRecordSetupHandlerTests`), and `Task.Delay(30s, ct)` inside stub handlers (`AccessCacheFaultCachingTests`), which waits for cancellation.

## Industry citation

Build-vs-maintain criteria per ADR-038 section 7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1-B17; Amendments A1 (ArchTests KEEP), A2 (B8 scope), A3 (ORPHAN/DETACHED).

## Reviewer decisions (task 090, 2026-11-03)

**Path moves:** done for the 32 files R3 added (`git mv`, history kept). Every destination glob (`integration/contract`, `integration/auth`, `integration/data-mutation`, `unit/domain`) compiles into `Sprk.Bff.Api.Tests` (see its csproj), so no namespace, `using` or project-reference change was needed. The note above that "moves cross test projects" does not apply to this repo. **Not moved:** the 5 files another project added (`ExternalDocumentNarrowingRouteTests`, `DemoExpiration*` ×3, `DemoSpeGrantLifecycleTests`). Their owners decide. The shared helper files stay where they are.

**SCAFFOLDING (R3-touched):**
- `ExternalAccessAuditTests.TheCategory_IsUnderSprkBffApi_AndDedicatedToTheAudit`: **kept**. The category string is the key operators filter the audit logs on, so pinning it guards a contract rather than restating a name.
- `DataverseWebApiClientCreateFailureTests.TheMessageKeepsTheShapeEnsureSuccessStatusCodeGave`: **kept**. Operators and `docs/guides/DATAVERSE-AUTHENTICATION-GUIDE.md` read this message text, and `DataverseForbiddenException` uses the same shape.
- `DataverseWebApiClientCreateFailureTests.ASuccessfulCreate_StillReturnsTheIdFromTheEntityIdHeader`: **rewritten**. It now uses the file's `ClientAnswering` builder (with an `entityId` parameter) instead of a second reflection injection. The file's one injection seam remains: `DataverseWebApiClient` has no `HttpClient` constructor.
- `ContactIdentityBinderOwnerTeamTests` (2 reflection/detached tests): **kept**. They are under a KEEP path, so removing them needs a replacement test in the same PR (A3), which is out of scope here.

**SCAFFOLDING not R3's (14 methods):** left to their owning projects (ai-dataverse MCP handlers, UAC). R3 deletes no other project's tests.

**AMBIGUOUS (51):** kept. The 27 tests that call the `/invite` handler by reflection are UAC-r2's surface.

**Advisory, Stopwatch:** fixed. `Control_ManyAssignmentsToOneLocal_ResolveInBoundedTime` became `Control_ManyAssignmentsToOneLocal_Resolve`, with no wall-clock bound. It had already failed once under load (task 079 notes). An unbounded resolver would hang the test, and the CI job timeout reports that.
