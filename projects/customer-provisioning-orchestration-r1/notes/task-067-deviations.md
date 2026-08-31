# Task 067 — Deviations & Design Notes

> **Task**: 067 — Author nightly Graph app-role parity ArchTest
> **Rigor**: FULL (TEST-MODIFYING unconditional override + T3 silent-fail-trap parity)
> **Landed**: 2026-08-18

## What landed

- `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/Sprk.Provisioning.ControlPlane.NightlyTests.csproj` — new nightly-cadence integration test project (added to `spaarke.sln`).
- `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/GraphAppRoleParityTest.cs` — two `[Fact]`s: live UAMI-SP ↔ `GraphAppRoles.cs` parity (`[SkippableFact]`) + BFF ↔ L2 mirror drift-guard (unconditional `[Fact]`).
- `projects/customer-provisioning-orchestration-r1/notes/graph-app-role-parity-coord-pr.md` — coord-PR spec for ci-cd-r1 to apply `.github/workflows/**` wiring.
- **Build**: `dotnet build -c Release tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/` → **0 Warning(s), 0 Error(s)**. Acceptance met.

## Deviations from POML

### D1 — Bonus test method: BFF ↔ L2 mirror drift-guard (added, not in POML acceptance list)

**POML surface**: goal + acceptance criteria enumerate only the live UAMI SP ↔ `GraphAppRoles.cs` parity check.

**Deviation**: I added a second `[Fact]` — `L2GraphAppRolesRegistry_MirrorsBffGraphAppRolesConstant` — that asserts `Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity.L2GraphAppRolesRegistry.GetAll()` byte-matches `Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles.All` (Value + AppRoleId + GraphResourceAppId).

**Rationale (Path A per CLAUDE.md §6.5, documented + narrow)**: The L2 mirror's own file header at `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/DataverseAppUserGraphParity/IGraphAppRolesRegistry.cs` explicitly names task 067 as the drift-guard mechanism:

> DRIFT GUARD: task 067 ("Nightly Graph app-role parity ArchTest", depends on this task 053) is the mechanism that keeps this mirror in sync with the BFF source of truth.

Not delivering the drift-guard would leave that L2 comment structurally unsatisfied and re-open the exact silent-drift class the two-copy architecture accepts by design. The bonus test is cheap (~1 s runtime; no live creds; pure reflection + structural comparison) and lives in the SAME test project the POML mandated, so no scope leak.

**Impact**: adds one method to the deliverable. Tests count = 2 instead of 1. No change to project layout, no additional project references, no additional GH Actions secrets.

### D2 — L2 mirror drift-guard runs nightly-only (not per-PR)

**Consequence of D1**: because the L2 drift-guard lives in the nightly project, per-PR runs won't catch a BFF-only OR L2-only edit until the next nightly. § 4.b of the coord-PR spec offers ci-cd-r1 an OPTIONAL per-PR add-on (`--filter` invocation, ~10 s per PR) if they want tighter feedback for that specific check.

**Rationale for the default**: keeping the mirror-drift test in the nightly project preserves scope-per-POML (one new project, not two) and keeps all "task 067" outputs colocated. If ci-cd-r1 declines the per-PR add-on, the drift signal remains 24 h — same posture as the live UAMI parity check.

### D3 — Test path not in ADR-038's 7 canonical KEEP paths

**POML surface**: names path `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/GraphAppRoleParityTest.cs`.

**ADR-038 constraint**: the 7 KEEP paths are `integration/{auth,regression,data-mutation,tenant,contract,seam}` + `unit/domain`. The nightly project's path matches none of them.

**Resolution (Path A)**: POML explicitly names the path — it is the primary constraint. The test would arguably fit `integration/auth` (Graph app-role permissions ARE auth) OR `integration/tenant` (tenant-scoped Graph query), but "nightly" is orthogonal to those categories and the project-level structure keeps the nightly-only invariant clear (via project name + coord-PR wiring). Documenting here so the `/test-diet` pass at project wrap-up doesn't flag this as a scaffolding candidate.

### D4 — Did NOT run the live test (POML step 6 partial)

**POML step 6**: `dotnet build + dotnet test the nightly project against dev tenant/UAMI — verify parity check works.`

**Actual**: `dotnet build` **PASSED (0/0)**. Did NOT run `dotnet test` against live tenant — per the task orchestration message:

> Note: DO NOT run the test itself (requires live Graph + UAMI creds). Compile-clean is the acceptance for this task's landing.

This is Path A per the orchestration override. The nightly workflow (coord-PR spec §3.a) will exercise the live path; ci-cd-r1's PR completes the loop.

### D5 — Reflection over direct static access (POML letter compliance)

**POML wording**: `reads GraphAppRoles.cs constant reflectively` / `assembly-level scan for the constant type + fields`.

**Actual**: `typeof(GraphAppRoles).Assembly` bootstraps the assembly discovery (so a class RENAME breaks the compile — fastest possible feedback), then `Assembly.GetType(...)` + `FieldInfo.GetValue(null)` reads `All` and `GraphResourceAppId` reflectively (so a FIELD rename fails at test time with a clear diagnostic naming the missing field).

**Rationale**: this hybrid captures both refactor-classes the POML asked to catch — class-shape drift (compile-time) AND field-shape drift (test-time-with-diagnostic). A pure `Assembly.LoadFrom(...)` scan (no `typeof()` anchor) would remove the compile-time signal and gain nothing.

## Compliance verifications

| Requirement | Where enforced |
|---|---|
| Explicit tenantId (§4D I5) | `new DefaultAzureCredential(new DefaultAzureCredentialOptions { TenantId = tenantId })` — line ~130 of the test file. No `.default`-only ambient credential. |
| Graph SDK v6 / Kiota 2 error type (NFR-09) | `catch (ODataError ex)` — line ~180. Surfaces `ex.ResponseStatusCode` (int) + `ex.Error?.Message` + `ex.Error?.Code`. No `ServiceException`. |
| Diff-formatted failure (POML constraint) | `FormatParityDiff(...)` — emits `Missing roles (N) — must be GRANTED onto the UAMI SP:` + per-role `- {DisplayName} ({AppRoleId}) [Value={Value}]` lines; `Extra roles (N) — assigned on the UAMI SP but NOT in GraphAppRoles.cs (drift):` + per-GUID lines; remediation footer. |
| Coord-PR spec, not workflow edits (POML constraint) | `notes/graph-app-role-parity-coord-pr.md` describes the diff for ci-cd-r1 to apply; **zero** `.github/workflows/**` edits in this task. |
| Nightly cadence, not per-PR (POML constraint) | Test project name (`NightlyTests`) + coord-PR spec § 2 rationale + coord-PR spec § 3.a `graph-app-role-parity` job in `nightly-health.yml`. The default per-PR test glob (`tests/unit/**` + specific integration projects) does NOT include this project. |
| ADR-028 21 MUSTs — Graph role parity | H10 handler owns provisioning-time parity (T3 post-condition); this test catches the drift post-provisioning window per FR-13 acceptance sub-clause. |
| Sub-agent write boundary (root CLAUDE.md §3) | This task edits only `tests/**` + `projects/customer-provisioning-orchestration-r1/notes/**` + `spaarke.sln` + `projects/.../TASK-INDEX.md`. NO `.claude/**` edits. |

## Files touched

**New**:
- `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/Sprk.Provisioning.ControlPlane.NightlyTests.csproj`
- `tests/integration/Sprk.Provisioning.ControlPlane.NightlyTests/GraphAppRoleParityTest.cs`
- `projects/customer-provisioning-orchestration-r1/notes/graph-app-role-parity-coord-pr.md`
- `projects/customer-provisioning-orchestration-r1/notes/task-067-deviations.md` (this file)

**Modified**:
- `spaarke.sln` (via `dotnet sln add` — solution + `Configuration` block auto-populated)
- `projects/customer-provisioning-orchestration-r1/tasks/TASK-INDEX.md` (row 067 → ✅)

**Read only (context)**:
- `src/server/api/Sprk.Bff.Api/Infrastructure/Auth/GraphAppRoles.cs`
- `src/server/services/Sprk.Provisioning.ControlPlane/Handlers/DataverseAppUserGraphParity/*.cs`
- `projects/code-quality-and-assurance-r3/notes/task-042-063-ci-gate-wiring-deferral.md`
- `projects/customer-provisioning-orchestration-r1/spec.md` (FR-13, FR-33, NFR-09, §4D I5)
- `projects/customer-provisioning-orchestration-r1/tasks/067-*.poml`
