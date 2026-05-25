# BASELINE.md — sdap-bff-api-remediation-fix

> **Phase 3 gate artifact.** Authoritative reference point for every Phase 4 regression check.
> **Captured**: 2026-05-25
> **Target environment**: `spaarke-bff-dev` (Linux App Service, `rg-spaarke-dev`, subscription `484bc857-3802-427f-9ea5-ca47b43db0f0`)
> **Source HEAD at baseline**: `7fb1776f` (task 023 — DI-singleton TokenCredential refactor)

---

## Phase 4 acceptance rule (binding)

For every Phase 4 candidate (tasks 040–054), the post-change measurement MUST be:
- **≤ baseline** for size, file count, DI registration count, error count
- **= baseline** for SHA-256 of unchanged components (drift signals an unintended change)
- **≥ baseline** for test pass count (currently 0 — see §1)
- **≤ baseline + 10%** for compressed publish size (the CI ceiling per Phase 0 task 006)
- **No new 404 routes** beyond the 2 documented in §3 below

If a candidate violates any rule, STOP and reconcile before continuing the bake.

---

## 1. Test suite (task 030)

🚨 **Status: BUILD FAILED — test runner never reached.**

| Metric | Baseline value |
|---|---|
| Test executions | 0 (build failed) |
| Pass / Fail / Skipped | 0 / 0 / 0 |
| Wall-clock | 8s (build-only) |
| Compile errors (unique) | ~69 across 17 test files |
| Error code distribution | CS7036 x ~31 · CS1503 x ~24 · CS1061 x ~6 · CS0618 x ~4 · CS1739 x ~3 · CS8625 x ~1 |

**Origin of breakage** (rough attribution):
- ~50% from **task 023** (this project): added `TokenCredential` param to 19 service constructors; test setups never updated. Files: `ScopeResolverServiceTests.cs`, `SessionRestoreServiceTests.cs`, `RecordSyncJobTests.cs`, plus likely 2–4 others in `Services/Ai/Tools/`.
- ~50% **pre-existing** from other in-flight projects: `EmailProcessingOptions.WebhookSecret` obsolete (comment cites "task 044" from a different project), `InviteExternalUserRequest.ContactId` removed (CS1739), other model-shape drift.

**Phase 4 implication — FR-E4 is unusable**:
- The "±5% test tolerance" Outcome E acceptance falls back to:
  - Task 053 (FR-E2 grep acceptance: zero direct CRUD→AI deps in production scope) — primary signal
  - Per-candidate manual smoke test on dev
  - `bff-deploy` skill's hash-verify + healthz check after every deploy

**Phase 4 regression rule for tests**: do NOT introduce additional test compile errors beyond the 69 baseline. Phase 6 wrap-up (task 090) flags test project repair as a separate sub-project — out of scope here.

**Source**: [`baseline/tests.txt`](baseline/tests.txt) (152 KB, full MSBuild log + extracted header)

---

## 2. Build warnings (task 031)

| Metric | Baseline value |
|---|---|
| Errors | 0 |
| Warnings | **17** (matches Phase 0 and Phase 1 — no drift from task 023) |
| Build time | 10.02 s |

**Warning distribution (unique after MSBuild dedup)**:

| Code | Count | Source |
|---|---|---|
| NU1903 | 1 | Microsoft.Kiota.Abstractions 1.21.2 HIGH CVE (accepted per Phase 2 Decision C.1) |
| CS1998 | 6 | async-no-await: AgentConfigurationService:100, PlaybookInvocationService:88/284/353, ChatEndpoints:1671 |
| CS0618 | 7 | DemoProvisioningOptions.* obsolete: RegistrationEndpoints:458–461, DemoExpirationService:347–348 |
| CS8601 | 2 | null-ref assign: AgentEndpoints:274, AgentEndpoints:297 |
| CS8604 | 1 | null-ref arg: ChatEndpoints:1153 |

**Phase 4 regression rule**: warning count must NOT exceed 17. Exception: task 043 (Kiota vuln patch) will REMOVE the NU1903 entry, reducing the ceiling to 16 after that bake.

**Source**: [`baseline/build-warnings.txt`](baseline/build-warnings.txt)

---

## 3. Endpoint smoke test (task 032)

| Metric | Baseline value |
|---|---|
| Total routes enumerated | **323** |
| Auth-required (401) | **305** |
| Open (200) — `/healthz`, `/ping`, `/status`, `/healthz/dataverse*` | 6 |
| Unexpected 200 (no `RequireAuthorization` — code-review flag) | 3 |
| 400 (Bad Request — model-binding rejects before auth; route IS registered) | 5 |
| **500 (server-error — route registered, internal failure)** | **2** |
| **404 (route missing or conditional)** | **2** (only 1 is a candidate regression) |

Methodology: the agent first probed each route with empty `application/json` POST, then RETRIED any 404s using `multipart/form-data` to avoid false-negatives from file-upload endpoints. This brought the initial 9 raw 404s down to 2 after retry — 7 of the original 404s were file-upload endpoints that correctly returned 401 once multipart content-type was used.

### 3a. The 2 server-errors (investigate during Phase 4)

| Method | Path |
|---|---|
| POST | `/api/finance/matters/{matterId:guid}/recalculate` |
| POST | `/api/finance/projects/{projectId:guid}/recalculate` |

Both expected 401 but returned 500 (returns ProblemDetails 500). Route IS registered; the failure happens internally on unauth probe — likely an auth filter throws before the standard 401 response, or model binding fails harder. Phase 4 task 047 (Finance facade migration) should re-verify these post-change.

### 3b. The 3 unexpected-200 routes (intentionally anonymous? — flag for code review)

| Method | Path |
|---|---|
| GET | `/api/admin/builder-scopes/status` |
| GET | `/api/config/client` |
| GET | `/api/office/health` |

Likely intentional status/health endpoints without auth. Phase 4 should not change this behavior. Documented for traceability.

### 3c. The 2 route-missing 404s (1 known non-regression, 1 candidate)

| Method | Path | Status |
|---|---|---|
| POST | `/api/office/save-debug` | **CONFIRMED non-regression** — registered only when `env.IsDevelopment()` (`OfficeEndpoints.cs:112`). App Service runs as Production. Expected behavior. |
| POST | `/api/office/quickcreate/{entityType}` | **CANDIDATE REGRESSION** — returns Kestrel 404 with empty body even with `Idempotency-Key` header. Source shows the route is supposed to be registered with `.AddIdempotencyFilter()` (`OfficeEndpoints.cs:1119`). Worth investigating in Phase 4. |

**Phase 4 regression rule**: no NEW 404s beyond these 2. The `quickcreate` route may turn into a real fix-target during Phase 4, but is documented baseline state for now. Investigation/repair is OUT OF SCOPE for this remediation project unless it blocks an Outcome E migration.

**Source**: [`baseline/endpoints-smoke.json`](baseline/endpoints-smoke.json) (75 KB, 323 routes with method/path/status/expected/file)

---

## 4. App Insights 48h baseline (task 033) — IN-PROGRESS

**Window start**: 2026-05-25 (UTC, post old-dev decommission)
**Earliest completion**: 2026-05-27 (UTC, after 48h elapses)

This is the **calendar gate** for Phase 4. Phase 4 task 040 (first Outcome A SAFE candidate) cannot start until `baseline/app-insights-48h.json` is committed with the 5 metric categories per task 033 acceptance.

**Source**: [`baseline/app-insights-baseline-start.md`](baseline/app-insights-baseline-start.md) — full operator runbook for the T+48h capture.

---

## 5. Deployed file SHA-256 hashes (task 034)

10 files captured from `spaarke-bff-dev` Kudu VFS (10,080,301 bytes total).

### Core rollback set (4 hashes — MUST match after Phase 4 rollback)

| File | SHA-256 |
|---|---|
| Sprk.Bff.Api.dll | `e7b8c16548ee60d8a3fa04e7cc96d3c6530b34891cd119f3599db8426a3f9a0b` |
| Sprk.Bff.Api.deps.json | `b57ab3294d43d80be5e5d9180d0a4cf7b3462d1ed4c8bee4dd0c8a34fc569bd8` |
| Spaarke.Core.dll | `9810319495282b5bab109a4d505e67780553f37046f09ae91130ff9de67d7ee2` |
| Spaarke.Dataverse.dll | `1b7bf95f46dd1fd97846964f5e9ec17c30fe644e68e2133c0259abe704ad77da` |

**Notable**: `appsettings.json` is NOT deployed on Linux — configuration is supplied entirely via Azure App Service Application Settings (env-var injection). Documented so Phase 4 doesn't expect a file that doesn't exist. PDBs included for completeness but rebuild-sensitive (timestamps embedded).

**Source**: [`baseline/deployed-sha256.txt`](baseline/deployed-sha256.txt)

---

## 6. Publish + zip metrics (task 035)

| Metric | Baseline value | Phase 4 target (after Outcome A) |
|---|---|---|
| Uncompressed | **212.5 MB** | ≤150 MB |
| File count | **287** | ≤240 |
| DLL count | **216** | (no explicit target — informational) |
| Compressed zip | **72.9 MB** | ≤60 MB (CI ceiling = baseline + 10% = 80.2 MB) |
| Drift vs Phase 1 (2026-05-24) | +0.5 MB / 0 files / -2.3 MB zip | n/a (negligible) |

**Phase 4 size-delta projections** (out of scope for baseline; recorded for Outcome A planning):
- Task 040 (`--runtime linux-x64` framework-dependent publish): est −54 to −67 MB native-binary savings
- Task 041 (exclude `wwwroot/**/*.js.map`): est −X MB (4 sourcemap files in `wwwroot/playbook-builder/assets/`)
- Task 042 (Cosmos `ServiceInterop.dll` dedup): **no-op** — Phase 1 confirmed not present in current publish

**Source**: [`baseline/publish-metrics.txt`](baseline/publish-metrics.txt)

---

## 7. Reflection-load probe (task 036)

Snapshot copy of Phase 1 task 015 output. The probe is `deps.json` + DI grep based (pragmatic alternative to runtime reflection per Phase 1 task 015 note).

**Source**: [`baseline/reflection-probe-baseline.txt`](baseline/reflection-probe-baseline.txt) — byte-for-byte identical to `inventory/reflection-probe.txt`.

Phase 4 candidate verification step: "verify reflection-load probe matches baseline (or differences are accounted for in the candidate's per-task notes)".

---

## 8. DI registration count (task 038 — ADR-010 measurable binding)

| Lifetime | Modules count | Program.cs | Total |
|---|---|---|---|
| AddSingleton | 126 | 1 (TokenCredential, task 023) | 127 |
| AddScoped | 117 | 2 (DocumentStorageResolver, ScorecardCalculatorService) | 119 |
| AddTransient | 1 (GraphModule only) | 0 | 1 |
| AddHostedService | 18 | 0 | 18 |
| **Total** | **262** (across 24 `*Module.cs`) | **3** | **265** |

**ADR-010 target**: ≤15 non-framework registrations. **Gap**: +250 (out of scope for this project; separate architectural project required).

**Phase 4 Outcome E expected delta**:
- Task 046 (create facade interfaces): +4 to +8 (depending on whether `PublicContractsModule` collects them or registers individually)
- Tasks 047–050 (consumer migrations): ±0 (consumer-side injections change but registrations stay put)
- Task 051 (handler relocation): ±0 (namespace-only change; JobType dispatch by name unchanged)

**Absolute Phase 4 ceiling for task 054 (gate)**: 273 registrations (265 + 8). If exceeded, investigate before continuing.

**Largest modules** (for Outcome E placement decisions):
- AnalysisServicesModule.cs — 61 (23% of all modules); candidate site for facade extension
- FinanceModule.cs — 22 (21 scoped + 1 singleton); heaviest scoped footprint
- AiModule.cs — 18

**Source**: [`baseline/di-registrations.txt`](baseline/di-registrations.txt)

---

## 9. Extraction assessment archive

`docs/assessments/bff-ai-extraction-assessment-2026-05-20.md` archived as `baseline/extraction-assessment-archive.md` per task 037 acceptance step 1. This is the immutable Phase 3 snapshot of the evidence base that drove the "keep AI in BFF" decision (refined ADR-013 + this project's scope).

**Source**: [`baseline/extraction-assessment-archive.md`](baseline/extraction-assessment-archive.md) (added by Phase 3 close commit)

---

## Phase 4 readiness checklist

- [x] Test suite baseline captured (task 030) — documented as "build fails; tests N/A; fall back to FR-E2 grep + manual smoke"
- [x] Build warning count baseline (task 031) — 17 warnings
- [x] Endpoint smoke baseline (task 032) — 323 routes; 305 ok 401s; 2 404s (1 dev-only + 1 candidate); 2 known 500s; 3 unexpected 200s
- [ ] App Insights 48h baseline (task 033) — **calendar gate IN PROGRESS until 2026-05-27**
- [x] Deployed file SHA-256s (task 034) — 10 files
- [x] Publish + zip metrics (task 035) — 212.5 MB / 287 files / 72.9 MB
- [x] Reflection-load probe baseline (task 036) — copied from Phase 1
- [x] DI registration count (task 038) — 265 total
- [x] Extraction assessment archive (task 037 step 1)
- [ ] Operator G4 facade adoption agreement with Insights Engine owner (UQ-04 residual) — **needed BEFORE Phase 4 task 046**
- [ ] Owner sign-off on this BASELINE.md (task 037 step 3)
- [ ] CLAUDE.md "Project Status" updated to "Phase 4 ready (pending 48h calendar gate + G4 agreement)" (task 037 step 4)

**Phase 4 cannot start until all checkboxes are ✅.**

---

## Cross-references

- Project CLAUDE: [`CLAUDE.md`](CLAUDE.md)
- Phase 1 inventory: [`inventory/INVENTORY.md`](inventory/INVENTORY.md)
- Phase 2 categorization: [`CANDIDATES.md`](CANDIDATES.md)
- Task index: [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md)
- Linux migration record: [`baseline/linux-dev-migration.md`](baseline/linux-dev-migration.md)
- Rollback drill record: [`baseline/rollback-drill.md`](baseline/rollback-drill.md)
- Auth refactor record: commit `7fb1776f` + this project's `current-task.md` decisions log
