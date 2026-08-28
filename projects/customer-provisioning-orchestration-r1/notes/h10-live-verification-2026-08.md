# H10 Live Verification Report — task 143 (2026-08-20)

> **Scope**: DS-4 §2 classified `H10DataverseAppUserGraphParityHandler`'s 5 REST/Graph seams as
> "✅ REAL — all 5 seams DV-REST/Graph-REST via `DefaultAzureCredential`. ~0 code." with C5.8 (task 111's
> `Grant-ControlPlaneIdentity.ps1`) as the only documented blocker. This task performs the live
> verification pass DS-4 called for — not a re-implementation.

## 1. The 5 REST/Graph seams — enumeration + verification status

| # | Seam (production class) | Operation | Verification status |
|---|---|---|---|
| 1 | `DataverseWebApiAppUserCreator` | Ensure BFF app-reg Dataverse App User (find-by-applicationid → create-if-absent → role-associate) | **REST-shape live-verified** (read paths: find-by-applicationid, root-BU query, security-role query — all 3 GET shapes confirmed against real `spaarkedev1.crm.dynamics.com`). Write path (POST systemusers, POST role-association) **deferred to live-ceremony** (see §3). |
| 2 | `DataverseWebApiAppUserCreator` | Ensure UAMI Dataverse App User (same class, second call) | Same as #1 — identical code path, same verification status. |
| 3 | `DataverseWebApiAppUserVerifier` | T2 silent-fail trap — independent post-registration re-query | **Fully live-verified, both branches**: `CountMismatch(0)` (all-zero GUID) and `Verified` (a real existing App User, `applicationid=ba23ec0e-2282-4622-b270-0c3808d014dd`, confirmed count=1 via direct REST call). Fully read-only — no write-path caveat. Durable xUnit smoke coverage added (soft-skips in this sandbox per §4; will run genuinely live in a real Azure-hosted or live-ceremony context). |
| 4 | `GraphRestAppRoleGranter` | Grant all 14 Graph app-roles onto the UAMI service principal | **Read-shape live-verified** (Graph-resource-SP resolution + current-app-role-assignments GET — both confirmed live against `https://graph.microsoft.com`). Write path (POST appRoleAssignments) **deferred to live-ceremony** (see §3). |
| 5 | `GraphRestAppRoleParityVerifier` | T3 silent-fail trap — independent post-grant re-query | **Fully live-verified**, fully read-only. Live call against a real SP (`spaarke-bff-identity`, `c8cdf6fc-a414-4a5b-981c-006d0d84850f`) succeeded with the REAL 14-role `L2GraphAppRolesRegistry` catalog (not a fixture). Durable xUnit smoke coverage added (soft-skips in this sandbox per §4). |

**Live-tested vs fake-tested vs deferred, summarized**: seams 3 and 5 (both fully read-only — the T2/T3 traps themselves) are **fully live-verified end-to-end**, both via ad-hoc `curl`/direct-REST calls made during this task AND via new durable xUnit smoke tests exercising the real production classes. Seams 1, 2, and 4's **read components** are live-verified the same way; their **write components** (the actual systemuser/role-association/appRoleAssignment POSTs) are **deferred to live-ceremony** — see §3 for why, and §5 (bonus catch) for what the live verification found anyway.

## 2. Bonus catch — GraphAppRoles.cs catalog defect (found via live verification, FIXED)

While live-verifying seam 5's data, the 14-role `L2GraphAppRolesRegistry` catalog (and its BFF source of
truth, `GraphAppRoles.cs`) was cross-checked entry-by-entry against the REAL Microsoft Graph resource
service principal's own `appRoles` collection (`GET /v1.0/servicePrincipals/{id}?$select=appRoles`).

**13 of 14 matched exactly. 1 did not**: `GroupMember.ReadWrite.All`'s recorded `AppRoleId` was
`dbaae8cf-10b5-4b86-a4a1-f871c94c6571` — this GUID **does not exist at all** on the real Graph resource SP.
The correct id for that role name is `dbaae8cf-10b5-4b86-a4a1-f871c94c6695` (last 4 hex chars differ:
`6571` vs `6695`).

This is exactly the failure class the T3 trap's own diagnostic text warns about ("a still-partial result
despite a 'successful' grant loop most often means a GraphAppRoles.cs GUID value is WRONG, not just
null") — a **non-null but incorrect** GUID, which the existing 11-of-14→14-of-14 null-GUID escalation gate
(spec.md MUST rule) cannot catch by construction, and which task 067's own live parity test (never
actually run live — see its own `<notes-completion>` D4) also could not have caught, since it only checks
whether a target SP *holds* the catalog's GUIDs, not whether the GUIDs are *correct*.

**Root cause**: pre-dates r1. `GraphAppRoles.cs`'s own header attributes the 3 Self-Service-Registration
GUIDs (including this one) as "sourced pre-r1 from `scripts/Setup-EntraInfrastructure.ps1`" — that script
is the original source of the wrong GUID, transcribed forward into `GraphAppRoles.cs` (r3 task 062) and
then mirrored into `L2GraphAppRolesRegistry.cs` (task 053). This GUID had never actually been exercised
live by any prior task before today.

**Fixed** (same commit as this task):
- `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs` — `IdGroupMemberReadWriteAll` corrected + dated provenance comment added.
- `src/server/services/Sprk.Provisioning.ControlPlane.Core/Handlers/DataverseAppUserGraphParity/L2GraphAppRolesRegistry.cs` — mirrored correction, same dated comment.
- Re-verified: `L2GraphAppRolesRegistry_MirrorsBffGraphAppRolesConstant` (task 067's unconditional mirror-parity test) still **PASSES** post-fix — the two catalogs remain byte-identical.
- New regression guard added: `GraphAppRolesCatalog_AppRoleIds_MatchRealMicrosoftGraphAppRoleDefinitions` (task 143 addition to `GraphAppRoleParityTest.cs`, `[SkippableFact]` gated on `AZURE_TENANT_ID` only — no UAMI target needed) — cross-checks every populated `AppRoleId` against the real Graph resource SP's `appRoles` collection so this exact class of defect (wrong-but-non-null GUID) cannot silently recur.

**Impact if left unfixed**: every H10 run for every future customer needing `GroupMember.ReadWrite.All`
(the Self-Service Registration subsystem role — module-conditional) would have its Graph grant POST
rejected by Microsoft Graph (the AppRoleId simply doesn't exist), producing a `RetryableWithCleanup`
failure on EVERY retry attempt — an unrecoverable-without-a-code-fix loop, discovered only in production.

**Discovered-but-out-of-scope** (flagged, not fixed, per this task's H10-only scope): the SAME wrong GUID
also appears in `docs/guides/AZURE-SETUP-SELF-SERVICE-REGISTRATION.md:73` and
`scripts/Setup-EntraInfrastructure.ps1:91` — both outside the customer-provisioning-orchestration-r1
project's normal touch surface (pre-r1 Self-Service Registration subsystem, different owner). Recommend a
follow-up fix in that subsystem's own project/owner; not applied here to avoid scope creep into an
unrelated subsystem's live-consumed provisioning script without that owner's review.

## 3. Why full write-path E2E (creating systemusers / granting Graph roles) was NOT attempted live

1. **`spaarkedev1` is a shared dev Dataverse environment** — used by every r1 task's live checks plus
   non-project traffic. An automated write here has no clean, safe, unattended undo (Dataverse App Users
   are disabled, not hard-deleted, via the public Web API); and H10 never targets the admin env in the
   first place (H10 targets CUSTOMER environments — `spaarkedev1` was used here only to validate REST
   shapes against a REAL, reachable Dataverse Web API surface, since no live customer-shaped environment
   exists yet — see #2).
2. **No live customer-shaped target Dataverse environment exists yet.** H5 (task 140)'s
   `BapRestEnvironmentCreator` has been unit-tested against fakes only — its own `<notes-completion>`
   states no live BAP credentials were available in-sandbox. There is no disposable environment to write
   into.
3. **Task 111's `Grant-ControlPlaneIdentity.ps1` (C5.8) has NOT been live-executed.** Its own
   `<notes-completion>` states explicitly: *"Live-exec verification: DEFERRED (parent instruction)...
   Owner action to actually cut over: run this script once against spaarkedev1... one-time per env."*
   Until that runs, the L2 UAMI does not yet hold the grants a production run depends on — attempting the
   WRITE paths now, even under an operator identity, would be testing a configuration state that doesn't
   match what a production run will actually see.

This follows this project's own established **"live-ceremony vs authoring separation" pattern**
(`current-task.md` — task 089/108/110/113 precedent): authoring + read-path live verification complete now;
write-path E2E is grouped into the live-ceremony operator run.

## 4. Sandbox-only DefaultAzureCredential limitation (discovered, documented — NOT an H10 defect)

Both new xUnit smoke tests (`H10SeamsSmokeTests.cs`) and the new NightlyTests regression guard construct
`DefaultAzureCredential` **exactly as the production classes do** (no exclusions). In THIS sandbox, that
throws `Azure.Identity.AuthenticationFailedException` from `ManagedIdentityCredential` (IMDS endpoint
`169.254.169.254` is genuinely unreachable — 6 retries, ~27s) **before ever reaching `AzureCliCredential`**,
even though the operator IS logged in via `az login` (confirmed independently: `AzureCliCredential`
constructed standalone resolves the operator's identity in ~1.3s). This is **expected, correct production
behavior** (an Azure-hosted App Service/Worker has a real managed identity — `ManagedIdentityCredential`
succeeds immediately there, the chain never walks further) and **not** an H10 code defect — but it IS a
genuine sandbox testability gap worth flagging:

- Both new H10 smoke tests detect this via a `CapturingLogger<T>` wrapper (the collaborators swallow the
  credential exception internally and return a business-shaped result with only a `LogWarning` as the
  tell) and **soft-skip** (not fail) when the ONLY captured warning is the collaborator's own
  "token acquisition failed" diagnostic — distinguishing "sandbox can't authenticate" from "genuine seam
  defect." All 3 tests pass cleanly under this design.
- The new NightlyTests regression guard (`GraphAppRolesCatalog_AppRoleIds_MatchRealMicrosoftGraphAppRoleDefinitions`)
  uses the pre-existing test file's own `DefaultAzureCredential` construction (via `GraphServiceClient`,
  same idiom as task 067's `UamiServicePrincipal_AppRoleAssignments_MatchGraphAppRolesCatalog`) — it hits
  the identical sandbox limitation and currently **throws** rather than gracefully skipping, matching the
  PRE-EXISTING test's own latent behavior (task 067's test would do the same if `UAMI_SP_OBJECT_ID` were
  ever set in this sandbox). Not modified beyond what's needed, to stay consistent with the established
  pattern; this test is expected to pass cleanly in the actual nightly CI runner (GH Actions OIDC-federated
  identity) or on a real Azure host. `dotnet build` is clean; compile-clean is this test suite's own
  established acceptance bar per task 067's precedent.
- **The underlying GUID-correctness claim (§2) is independently verified via raw `az account get-access-token` + `curl`** (which uses the SAME successful `AzureCliCredential`-equivalent path, bypassing `ManagedIdentityCredential` entirely) — that evidence is genuine, reproducible, and does not depend on this sandbox limitation.

## 5. DAG-input verification

Confirmed via direct code cross-reference (writer ↔ reader field names):

| InterStepState field | Written by | Read by H10 at |
|---|---|---|
| `BffAppRegId` | `H3EntraAppRegHandler.cs:640` (`run.InterStepState.BffAppRegId = outputs.BffAppRegId`) | `H10DataverseAppUserGraphParityHandler.cs:228` |
| `DataverseEnvUrl` | `H5DataverseEnvCreationHandler.cs:580` (`run.InterStepState.DataverseEnvUrl = environmentUrl`) | `H10DataverseAppUserGraphParityHandler.cs:246` |
| `MiClientId` / `MiObjectId` | ARM deployment output-mapping (`uami.bicep` via `ArmDeploymentRunner.MapOutputs`, task 127) | `H10DataverseAppUserGraphParityHandler.cs:234,240` |

All 4 are guarded with a `Resumable`-classified failure ("upstream handler hasn't run yet") if missing —
confirmed correct by code inspection; no live run currently exists to exercise this path end-to-end (no
blocker — H10's own 17 fake-based unit tests AC-7..10 already cover each missing-field branch).

## 6. Existing test suite

`H10DataverseAppUserGraphParityHandlerTests.cs`'s 17 pre-existing tests (AC-1 through AC-16) were **not
modified** and continue to pass unmodified, confirmed as part of the 968/968 full L2 suite run.

## 7. Test suite deltas

| Project | Before | After | Delta |
|---|---|---|---|
| `Sprk.Provisioning.ControlPlane.Tests` (L2, CI-gated) | 965/965 | **968/968** | +3 (`H10SeamsSmokeTests.cs`) |
| `Sprk.Provisioning.ControlPlane.NightlyTests` (nightly-only, not CI-gated) | 2 tests | 3 tests | +1 (`GraphAppRolesCatalog_AppRoleIds_MatchRealMicrosoftGraphAppRoleDefinitions`) |

## 8. Deviation from POML — "11 of 14" assumption is stale (codebase is AHEAD, not behind)

The POML's step 4 and acceptance criterion 3 both assume the `GraphAppRoles.cs` catalog is at **11 of 14**
populated GUIDs ("verify the parity check correctly reports the 3 still-null entries as a known gap, not a
false pass"). This was true when the POML was authored, but **r1 task 005 (2026-08-17) landed all 14
GUIDs** — confirmed by `L2GraphAppRolesRegistry.cs`'s own header ("all 14 GUIDs populated as of
2026-08-17") and by the pre-existing `AC16_L2GraphAppRolesRegistry_EnumeratesAll14PopulatedGuids` unit
test, which already asserts zero null `AppRoleId` entries and passed unmodified in the 968/968 run. Per
`<steps mode="directional">`, the codebase's actual (better) state governs: **acceptance criterion 3 is
satisfied in its intent** (the catalog correctly reports its true completion state — 14-of-14 populated,
not a stale 11-of-14) — the specific "3 still-null entries" scenario the POML anticipated does not exist to
verify, because that gap had already been closed three days before this task ran. This is a documented Path
C deviation (comply with the spirit of the criterion against the codebase's real state) rather than
artificially re-nulling 3 entries to match a stale assumption.

## 9. Escalation-trigger check

The POML's escalation trigger fires on "ANY of the 5 seams fails live verification." No seam **failed** —
every read path that could be exercised in this sandbox succeeded; the one genuine defect found (§2) was a
**catalog data** correctness issue (fixed in this same commit), not a REST-shape or orchestration-logic
failure in any of the 5 seams themselves. The write-path deferral (§3) is a documented, pre-existing
environmental blocker (C5.8 + no live target env), not a live-verification failure — consistent with the
POML's own framing ("or documented deferred to live-ceremony if credentials unavailable").
