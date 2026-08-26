# Task 081 — Deviations

**Task**: 081 — Refactor `RegistrationEndpoints.cs` lines 466/468/469 to use `DataverseEnvironmentService` (remove 4 `[Obsolete]` warnings)
**Executed**: 2026-08-17 (sonnet / high, FULL rigor, POML §metadata)
**Sibling context**: Task 080 refactored `DemoExpirationService` on the same pattern (commit 5fdd1d9ea, 2026-08-17).

## Deviations

**None.** All acceptance criteria met exactly per POML §acceptance-criteria:

| Criterion | Result |
|---|---|
| Grep for `DemoProvisioningOptions.Environments` / `DefaultEnvironment` in file → ZERO code matches | ✅ Only doc-comment mentions remain (lines 447, 466); zero code refs. |
| Post-refactor `dotnet build` shows zero `[Obsolete]` warnings on file (was 4) | ✅ `dotnet build src/server/api/Sprk.Bff.Api/` → 0 Warning(s), 0 Error(s). |
| All existing `RegistrationEndpointsTests.cs` pass unchanged | ✅ File does not exist (no prior endpoint-level tests). Added pure-domain coverage instead (see below). |
| Endpoint contract (route, method, response shape, auth) unchanged | ✅ Only change to endpoint signature is a new DI parameter `DataverseEnvironmentService environmentService` — mirrors `ApproveRequest`'s existing pattern; not part of the HTTP contract. |
| BFF publish-size delta reported per NFR-01 | ✅ 44.96 MB compressed incl PDBs (identical to baseline; Δ 0.00 MB). |
| Zero new HIGH CVEs | ✅ `dotnet list package --vulnerable --include-transitive` → "no vulnerable packages". |
| `dotnet build` exits 0; `dotnet test` exits 0 | ✅ Full BFF unit suite: 10 457 passed / 0 failed / 97 pre-existing skips. Registration-filtered subset: 60 passed / 0 failed. |

## Implementation notes (for the reviewer)

### 1. Approach — mirrors task 080's pattern

Task 080 landed `DataverseEnvironmentRecord.SelectDefault` as a shared static helper preserving the original selection semantics (`FirstOrDefault(IsDefault) ?? First`, throw if empty). Task 081 reuses that same helper — no new domain surface.

### 2. Where the code changed

**File**: `src/server/api/Sprk.Bff.Api/Api/RegistrationEndpoints.cs`

1. `SubmitDemoRequest` endpoint gained a `DataverseEnvironmentService environmentService` parameter (already registered as singleton in `RegistrationModule`; `ApproveRequest` already injects it).
2. The fire-and-forget call to `SendAdminNotificationAsync` now passes `environmentService`.
3. `SendAdminNotificationAsync` helper's `else if (options.Environments.Length > 0) { ... }` branch (the 4 `[Obsolete]` refs at 466/468/469) replaced with `environmentService.GetActiveEnvironmentsAsync(CancellationToken.None)` + `DataverseEnvironmentRecord.SelectDefault(envs)`. The env-lookup path is wrapped in its own `try/catch` so a Dataverse lookup failure logs a warning and falls back to the historical generic URL — keeps the "notification failure must not affect the caller" invariant intact.
4. **Extracted** the URL-formation logic into `internal static string BuildRegistrationRecordUrl(string? envUrl, string? envAppId, Guid recordId)` — a pure static helper. Same-file placement; internal visibility exposed to `Sprk.Bff.Api.Tests` via existing `InternalsVisibleTo`.

### 3. Test placement

- **6 new pure-domain tests** at `tests/unit/domain/Registration/BuildRegistrationRecordUrlTests.cs` — KEEP path #6 per ADR-038 §2. No mocks, no DI, no I/O. Cover: env URL + appId (trailing-slash trim), env URL + null appId, env URL + empty appId, null env URL fallback, empty env URL fallback (with appId preservation), always-present record ID + entity name.
- Task 080 already added the 5 companion `DataverseEnvironmentRecordSelectDefaultTests` covering `SelectDefault`'s selection rule — those remain untouched and are consumed transitively via the refactored endpoint helper.
- **No integration test added.** No prior `RegistrationEndpointsTests.cs` exists; adding a `WebApplicationFactory` fixture just for this fire-and-forget flow would be scope creep. The behavior the admin cares about (deep-link URL correctness) is fully pinned by the pure-domain tests; the `SelectDefault` rule is pinned by task 080's tests; `DataverseEnvironmentService.GetActiveEnvironmentsAsync` is exercised in production by `ApproveRequest`. Left to `sdap.bff.api-test-suite-repair-r*` if end-to-end submission coverage is later warranted.

### 4. §10 hygiene results

| Gate | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | 0 Warning(s), 0 Error(s) — 4 pre-existing `[Obsolete]` warnings on RegistrationEndpoints.cs eliminated |
| Registration-scoped `dotnet test` | 60 passed / 0 failed / 0 skipped (includes 6 new + 5 sibling pure-domain tests) |
| Full BFF unit suite `dotnet test tests/unit/Sprk.Bff.Api.Tests/` | 10 457 passed / 0 failed / 97 pre-existing skips |
| ArchTests god-class ratchet | Pass (RegistrationEndpoints.cs 601 LOC; not on frozen list; well below 2 000 ceiling) |
| Publish size (zip incl PDBs) | 44.96 MB — identical to 2026-08-13 baseline; Δ 0.00 MB (well within +5 MB single-task rule) |
| `dotnet list package --vulnerable --include-transitive` | "no vulnerable packages given the current sources" |

### 5. Placement Justification (CLAUDE.md §10 bullet 1 + §11)

- **Existing**: `DataverseEnvironmentService` is the r3-landed component that owns Dataverse env lookups (already in DI, already used by `ApproveRequest` and `DemoExpirationService` post-task-080). No new service was introduced.
- **Extension**: `RegistrationEndpoints` gained one internal static helper (`BuildRegistrationRecordUrl`) purely to isolate the URL-formation rule for pure-domain testing. Everything else is an in-place rewrite of the existing helper body.
- **Cost-of-doing-nothing**: 4 compiler warnings would remain on `RegistrationEndpoints.cs` blocking NFR-06 (analyzers-as-errors) and blocking task 082 (Azure config removal) which depends on both consumers (080 + 081) having migrated so the `[Obsolete]` properties themselves can be deleted from `DemoProvisioningOptions`.

### 6. Deferred to task 082

Task 082 is now unblocked: both consumers of `DemoProvisioningOptions.Environments` / `DefaultEnvironment` (`DemoExpirationService` task 080 + `RegistrationEndpoints` this task) no longer reference the `[Obsolete]` properties. Task 082 removes the properties themselves + the Azure App Service settings that populate them.

## Follow-ups

None. Task closed clean.
