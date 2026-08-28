# Task 053 (H10) — Deviations from POML literal wording

Per root CLAUDE.md §6.5 ADR Conflict Resolution Protocol + task-execute Step 8's
"directional vs prescriptive" guidance (POML `<steps mode="prescriptive">` — the
sequence binds, but deviations are documented, not silently improvised).

## 1. Test file location — Path C (pivot to comply with established repo convention)

**POML said**: `Handlers/H10DataverseAppUserGraphParityHandler.Tests.cs` (colocated
with the handler under `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/`).

**Actual**: `src/server/services/Sprk.Provisioning.ControlPlane.Tests/Handlers/H10DataverseAppUserGraphParityHandlerTests.cs`
— a separate test project, matching ALL 8 prior Wave-C4 handler test files
(H0PreflightHandlerTests.cs, H1SubscriptionReadinessHandlerTests.cs,
H2aBicepInfraDeployHandlerTests.cs, H2bAiSearchIndexHandlerTests.cs,
H3EntraAppRegHandlerTests.cs, H5DataverseEnvCreationHandlerTests.cs,
H6SolutionImportHandlerTests.cs, H12aAiSeedChainHandlerTests.cs,
H12bAppConfigSeedHandlerTests.cs).

**Rationale**: the POML's `<file role="new">...Tests.cs</file>` path was a
task-create approximation, not a literal directive contradicted by the actual
codebase. Following the real convention (no `.Tests.cs` suffix, separate
project, `Handlers/` subfolder) keeps discoverability + `dotnet test` project
targeting consistent with the other 8 handler test suites.

## 2. InterStepState controlled schema extension

**Added**: `InterStepState.BffAppRegSystemUserId` (string?, JSON key
`bffAppRegSystemUserId`).

**Rationale**: design.md §6.2's enumerated `interStepState` keys name a single
`systemUserId` slot, but H10 registers TWO Dataverse App Users (BFF app-reg +
UAMI). The pre-existing `systemUserId` field's doc comment already scoped it to
"the MI-Dataverse App User" (the T2 trap subject), so `SystemUserId` is used for
the UAMI's systemuserid and the new `BffAppRegSystemUserId` field holds the BFF
app-reg's. This follows the CONTROLLED SCHEMA EXTENSION precedent established
by task 049 (`ImportedSolutions`) and task 050 (`SpeContainerId`) — a
deliberate, documented type extension, not an ad-hoc dictionary insert.

## 3. NFR-09 "Graph SDK calls catch ODataError" — Path C (pivot to comply in spirit)

**POML constraint**: `spec.md NFR-09: Graph SDK calls catch ODataError (not
ServiceException); ResponseStatusCode is int; ResponseHeaders is dict.`

**Actual**: H10's two Graph collaborators (`GraphRestAppRoleGranter`,
`GraphRestAppRoleParityVerifier`) use raw `HttpClient` + `DefaultAzureCredential`
against the Graph REST surface directly — NOT the `Microsoft.Graph` SDK package
(which L2 does not reference).

**Rationale** (full detail in `H10DataverseAppUserGraphParityHandler.cs` file
header "NFR-09 IMPLEMENTATION NOTE"): NFR-09 describes the BFF's
Microsoft.Graph SDK v6 / Kiota 2.0 error-handling contract. Every other
Wave-C4 L2 collaborator that calls Graph or Dataverse (H5's
`DataverseWebApiHealthProbe`, H2b's `RestApiAiSearchIndexVerifier`) uses raw
HttpClient + DefaultAzureCredential, not the Graph SDK — and design.md §9.2's
tool-selection table explicitly lists "`az rest` against Graph endpoints" /
"Direct Graph SDK invocation via a script" as the L2-tooling options, not a
first-class Microsoft.Graph SDK dependency. H10's REST collaborators catch
non-success HTTP status codes and surface the status code + response body in
the failure diagnostic — the same protection NFR-09 asks for (distinguish
HTTP-shaped errors from generic faults; carry the status code + payload for
diagnosis) — without adding a new SDK dependency to a project none of its 8
prior handlers needed.

**Alternative considered and rejected**: add `Microsoft.Graph 6.5.0` to L2
purely for this handler. Rejected as inconsistent with the established L2
pattern (raw REST + DefaultAzureCredential) and unnecessary complexity for a
2-GET/1-POST REST surface already validated end-to-end by
`scripts/Grant-GraphAppRoles.ps1` (task 015).

**Reviewer note**: if this pivot is judged insufficient at PR review, Path A
(project-scoped exception documented in spec.md's ADR Tensions section) or a
follow-up task adding the SDK are the two escalation options — flagged here
for explicit reviewer awareness per CLAUDE.md §6.5 (do not silently accept
Path C without visibility).

## 4. Dataverse App User creation mechanism — followed POML literally over an ambiguous design.md footnote

**Tension observed**: design.md §9.3's R2 finding note reads: *"v3.2 (M-10)
interim: PPAC UI + Graph SDK for role sync"* — which could be read as "App
User registration itself stays a manual Power Platform Admin Center action in
r1, only the Graph role sync is automated." The task POML's `<prompt>` and
step 3, however, are unambiguous: *"Register BFF app-reg AND UAMI as
Dataverse System Administrator App Users via Dataverse Web API systemusers
upsert on target env"* — an automated Web-API-based creator, not a manual
PPAC-UI step.

**Resolution**: followed the POML literally (`<steps mode="prescriptive">` —
the exact sequence binds; a needed deviation is an escalation, not a silent
improvisation). `DataverseWebApiAppUserCreator` performs a real Dataverse Web
API upsert (find-by-applicationid → create-if-absent against the root
business unit → resolve + associate the "System Administrator" role),
producing a genuinely useful automation instead of a no-op placeholder for
the exact silent-fail trap (T2) this handler exists to close. If Phase F
(task 089) live E2E acceptance testing surfaces a Dataverse Web API semantics
issue with this approach (e.g., environments that reject programmatic
App User creation), that is expected interim-refinement territory — the same
posture every other Wave-C4 "Null placeholder → Wave C5 real impl" collaborator
already carries (H1's `NullSubscriptionReadinessProbe`, H3's
`NullAdminConsentVerifier`), except H10's implementation is real from Wave C4
because T2 is this handler's entire reason to exist.

## Quality gates (Step 9.5) summary

- `dotnet build src/server/services/Sprk.Provisioning.ControlPlane/`: 0 warnings, 0 errors.
- `dotnet test src/server/services/Sprk.Provisioning.ControlPlane.Tests/`: 352 passed, 0 failed (17 new H10 tests).
- `dotnet list package --vulnerable --include-transitive` (L2 project): no vulnerable packages (no new NuGet packages added — H10 reuses the already-referenced `Azure.Identity`).
- code-review: found + fixed 1 real Warning (configured `DataverseRequestTimeout`/`GraphRequestTimeout` options were never wired to `HttpClient.Timeout` in the 4 REST collaborators — fixed). Applied 2 Suggestion-level hardening fixes (GUID validation before OData `$filter` interpolation; `using Microsoft.Extensions.Options;` cleanup). Noted-but-accepted: ADR-010 interface-per-collaborator pattern (5 interfaces, each single production impl) and an 8-parameter handler constructor — both exceed strict literal thresholds but are consistent with all 8 prior Wave-C4 handlers' established, already-reviewed pattern (testability seams against live Azure/Graph/Dataverse APIs).
- adr-check: 0 Critical violations. Same ADR-010 Warning as above (not a new tension — precedent-consistent).
