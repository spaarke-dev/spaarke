# Task 041 — LiveIntegration suite: teardown proof, guard proof, and findings

> 2026-08-27 · FR-D02 / NFR-07 · Run live against Spaarke Dev (`spaarkedev1`, "Spaarke PAYGO 1"
> container type, `8a6ce34c-6055-4681-8f87-2f4f9f921c06`). **No secret value appears in this file.**

---

## 1. Step 2 HARD STOP — teardown-on-failure, proven live

**Procedure**: temporarily added an env-var-gated forced failure
(`SPE_LIVE_FORCE_FAILURE_PROOF=1`) immediately after
`ThrowawayContainer_DeleteRestorePermanentDelete_SucceedsAndLeavesPreExistingContainersUntouched`
confirmed the fixture's container was **active** — i.e. before the test's own delete/restore/purge
steps ran, so the container was in its least-torn-down state when the test threw.

```
[TASK-041 TEARDOWN PROOF] fixture container id = b!zsLR7nkMI0Oeb6VjopeE0c5RFHtaaUZCi0Jm-xs-hDQV_6QuLuKmR4jrMdC6UgMm
Failed ...ThrowawayContainer_DeleteRestorePermanentDelete... [FAIL]
  System.InvalidOperationException : TASK-041 HARD-STOP STEP 2 PROOF: intentional forced failure.
```

`IClassFixture<T>.DisposeAsync` ran anyway (xUnit-guaranteed). A follow-up query for that exact
container id, run in a separate process afterward:

```
in active listing: False
in deleted(recycle-bin) listing: False
```

**Fully gone, not just recycled** — `LiveIntegrationFixture.DisposeAsync`'s two independently-guarded
steps (soft-delete, then permanent-delete) both executed despite the test having thrown mid-run. This
is disposal, not end-of-test cleanup a failure would skip — the property NFR-07 and the HARD STOP
require.

The forced-failure scaffolding (the env-var check, the `Console.WriteLine`, and the temporary
verification test) was removed immediately after this proof. It is not part of the committed suite —
committing a test that deliberately throws would make the suite permanently red, which is a worse
failure mode than the one this proof defends against.

## 2. Step 3 HARD STOP — the guard, proven live

`ThrowawayContainerGuard.EnsureProvisionedByFixture` is exercised two ways in the committed suite:

1. **Pure-logic, always-on** (`Guard_RefusesDestructiveOperation_WhenIdDoesNotMatchTheFixtureContainer`) —
   negative control (a foreign id throws) + positive control (the fixture's own id never throws). Runs
   in every `dotnet test`, live or not — it makes no Graph call, so gating it behind
   `SPE_LIVE_INTEGRATION_ENABLED` would only remove regression coverage.
2. **Structural, live** (`DestructiveHelper_NeverCallsGraph_WhenTargetIsNotTheFixtureContainer`) — a
   destructive helper (`SoftDeleteThroughGuard`) that calls the guard BEFORE issuing the Graph request.
   Run live 2026-08-27: throws `InvalidOperationException` for a random foreign id, confirming the
   refusal happens before any Graph call is reached in the actual call path a real test uses — not just
   in the pure-function proof above.

## 3. Live run results (2026-08-27, all against Spaarke Dev)

| Test | Result |
|---|---|
| `Guard_RefusesDestructiveOperation_WhenIdDoesNotMatchTheFixtureContainer` | ✅ Pass |
| `DestructiveHelper_NeverCallsGraph_WhenTargetIsNotTheFixtureContainer` | ✅ Pass |
| `ThrowawayContainer_DeleteRestorePermanentDelete_SucceedsAndLeavesPreExistingContainersUntouched` | ✅ Pass (includes the automated pre-existing-containers-unchanged assertion) |
| `ContainerTypePermissionGrant_ReadPath_ShowsTheOwningAppsRealGrant` | ✅ Pass |
| `ConsumingAppRegistration_WritePath_ReturnsApiNotFound_ADocumentedGraphDefect` | ✅ Pass (characterizes a real defect — see §4) |
| `ContainerTypeOwnerGrant_RoleFlow_RequiresADelegatedToken_SkippedWithoutOne` | ✅ Pass (no-op — no delegated token available; see §5) |

6/6 green. Re-run with no env vars set (default `dotnet test` baseline): all 6 pass in <1ms each with
zero network calls — the suite is a true no-op without `SPE_LIVE_INTEGRATION_ENABLED`.

## 4. 🔴 New finding — the Graph-based consuming-app registration WRITE path is broken

`RegisterConsumingTenantAsync` (`POST .../containerTypeRegistrations/{id}/applicationPermissionGrants`,
wired to `POST /api/spe/containertypes/{typeId}/consumers` via `ConsumingTenantEndpoints.cs`) returns
`400 invalidRequest / apiNotFound` **on both API versions**, confirmed live:

```
POST https://graph.microsoft.com/beta/storage/fileStorage/containerTypeRegistrations/8a6ce34c.../applicationPermissionGrants
  => 400 BadRequest: {"error":{"code":"invalidRequest","message":"API not found","innerError":{"code":"apiNotFound",...}}}
POST https://graph.microsoft.com/v1.0/storage/fileStorage/containerTypeRegistrations/8a6ce34c.../applicationPermissionGrants
  => 400 BadRequest: {"error":{"code":"invalidRequest","message":"API not found","innerError":{"code":"apiNotFound",...}}}
```

**A GET on the identical URL succeeds on both versions** and returns the real, live grant for the
owning app (`170c98e1`, `delegatedPermissions: ["readContent","writeContent","manageConte...`). So the
collection is readable but not writable through Graph as this code calls it — the resource genuinely
exists (both GET calls return `200` with real data), it just refuses the write shape this method sends.

**Not investigated further in this task** (out of scope — task 041 builds test infrastructure, it does
not chase production fixes): whether the fix is a different request body shape, a different HTTP verb,
or whether Graph simply does not support writing this collection at all and the SPE Admin app's actual
"Register" button — which calls a SEPARATE, SharePoint-REST-based code path
(`POST /api/spe/containertypes/{typeId}/register` → `SpeAdminGraphService.RegisterContainerTypeAsync`,
`PUT {sharePointAdminUrl}/_api/v2.1/storageContainerTypes/{id}/applicationPermissions`) — is the only
real way to register a consuming app. That second path was **not** exercised live in this task: it has
no confirmed, proven-reversible undo (no corresponding REST DELETE was located), and mutating it
against the shared "Spaarke PAYGO 1" container type without a proven revert would violate the same
reversibility bar this suite holds every other mutating call to.

`ConsumingTenantEndpoints.cs`'s POST/PUT/DELETE handlers (`RegisterConsumerAsync`, `UpdateConsumerAsync`,
`RemoveConsumerAsync`) are therefore suspected non-functional in production today. **FILED as issue #834** (2026-08-27) — see https://github.com/spaarke-dev/spaarke/issues/834. Recommend a
follow-up task to determine the correct write shape (or to retire the Graph-based write endpoints in
favor of the SharePoint-REST `/register` path, which IS wired to the SPE Admin app's UI). Flagged to the
orchestrator in the task completion report; not fixed here.

**Secondary finding**: `RegisterConsumingTenantAsync` only wraps Graph's 404 into
`SpaarkeStorageException` (the ADR-007 facade boundary); every other status — including this 400 —
propagates as a raw `Microsoft.Graph.Models.ODataErrors.ODataError`, which is itself an ADR-007
boundary gap (Graph SDK types are not supposed to escape `Infrastructure.Graph`/`SpeFileStore`). Also
out of scope for this task; recorded for the same follow-up.

## 5. Auth findings recorded during fixture construction

- **`DefaultAzureCredential` does not work as the Key-Vault credential on this workstation.** Its
  `ManagedIdentityCredential` probe throws `AuthenticationFailedException` (IMDS unreachable) rather
  than the `CredentialUnavailableException` the chain is built to fall through on, so the whole chain
  aborts before ever trying `AzureCliCredential`. Switched `LiveIntegrationFixture` to
  `AzureCliCredential` directly — consistent with the BFF module's own documented local-dev answer
  ("az login covers everything except OBO", `src/server/api/Sprk.Bff.Api/CLAUDE.md`).
- **`containerTypeRegistrations` reads work on both v1.0 and beta** — `GraphClientV1` was added to the
  fixture to give the registration/permission tests a v1.0-based sibling client alongside the
  beta-based `GraphClient` container CRUD needs (task 020's finding, unchanged).
- **Container-type OWNER grants remain delegated-and-beta-only**, confirmed unchanged from task 027's
  2026-08-25 finding (403 app-only, both versions). No delegated token was available in this
  (non-interactive, automated) task-execution session — `ContainerTypeOwnerGrant_RoleFlow_*` ran as
  its documented no-op. The test is fully implemented and will run for real the first time an operator
  exports `SPE_LIVE_DELEGATED_TOKEN` from a manual device-code sign-in
  (`notes/delegated-diagnostics.py`'s pattern) before invoking
  `dotnet test --filter Category=LiveIntegration`.

## 6. Pre-existing containers — confirmed unchanged

`ThrowawayContainer_DeleteRestorePermanentDelete_SucceedsAndLeavesPreExistingContainersUntouched`'s
final assertion (`finalActive.Should().BeEquivalentTo(_fixture.PreExistingContainerIds, ...)`) is an
automated, every-run proof — not a manual before/after diff — that the set of containers under
"Spaarke PAYGO 1" is byte-identical to what it was before the fixture created its throwaway container.
Passed on every live run in this task, including the forced-failure run (§1) and the final clean run
(§3).

## 7. Step 9.5 quality gates (unconditional — TEST-MODIFYING override, root CLAUDE.md §8)

Both `code-review` and `adr-check` were run against
`tests/integration/seam/SpeAdmin/LiveIntegrationFixture.cs` and
`tests/integration/seam/SpeAdmin/ContainerLifecycleLiveTests.cs`.

**ADR-038**: compliant. Correct KEEP path (`tests/integration/seam/**`), `[Trait("Category",
"LiveIntegration")]` (not `[Category(...)]`), no banned mock patterns (no `Mock<HttpMessageHandler>`,
no DI-registration tests, no ctor null-check tests), live tier mocks nothing real — the only
intentionally-unreachable dependency (`DataverseWebApiClient`) mirrors the pre-existing WireMock
contract-test convention and is orthogonal to what these tests exercise, not a mocked collaborator of
the code under test.

**ADR-007**: not implicated for test code — the facade-isolation rule scopes to `src/`
(`Infrastructure.Graph`/`SpeFileStore`). `ConsumingAppRegistration_WritePath_...` catches
`Microsoft.Graph.Models.ODataErrors.ODataError` directly to observe a real facade gap in production's
own error handling (§4's secondary finding) — the test is diagnosing the gap, not repeating it.

**ADR-028 / credential guards**: not implicated. `tests/Spaarke.ArchTests/CredentialCensusTests.cs` and
`CredentialGuardTests.cs` scan `src/server/**` only (confirmed by reading `SourceScan.ServerSourceFiles()`
usage) — this task's `ClientSecretCredential`/`AzureCliCredential` usage is test-only code exercising the
already-sanctioned E-1 owning-app credential category, not a new production confidential-client site.

**Findings applied before finalizing** (self-review, three refinements):
1. `InitializeAsync` originally did 8 sequential things in one method (AI-smell "methods with too many
   responsibilities") — extracted into `BuildGraphService`, `BuildV1GraphClientAsync`,
   `ProvisionThrowawayContainerAsync`.
2. Three separate `HttpClient` instances were being constructed with no disposal — consolidated to one
   shared, fixture-owned, disposed-in-`DisposeAsync` instance reused by `LiveHttpClientFactory`,
   `GraphClientV1`, and the delegated client builder.
3. Two `_fixture.ContainerId!` null-forgiving sites got one-line comments explaining why they're safe
   (both are behind the `if (!_fixture.IsLive) return;` gate that guarantees `InitializeAsync` already
   populated the value).

Re-ran the full live suite (6/6 green) and the default-mode suite (6/6 green, <1ms each) after applying
these refinements to confirm they were behavior-preserving.

No Critical or unresolved Warning findings remain. `dotnet build Spaarke.sln`: 0 errors, 0 warnings
(post-refinement).
