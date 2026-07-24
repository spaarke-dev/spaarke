# Task 061 — Post-Cutover Acceptance Evidence (NFR-01/04/05, §10 BFF Hygiene)

> **Date**: 2026-07-23
> **Task**: `tasks/061-corpus-proof-size-cve.poml`
> **Rigor**: FULL · directional step mode · sonnet/high
> **Prerequisite**: Task 060 done-with-exception (§6.5 Path-A — mammoth retained for transient/Browse
> mounts only; the stored-document Load/Save write path is fully on `ComposeShadowPatchEngine`; see
> `notes/060-BLOCKED-projection-less-transient-mounts.md`).

**Verdict: ALL SIX CHECKS GREEN. No release blocker. No escalation required.**

---

## 1. Corpus byte-diff harness (NFR-01) — PASS, no regression

Re-ran the round-trip byte-diff harness (task 004, extended by task 034) against the full fidelity
corpus post-cutover:

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter \
  "FullyQualifiedName~ComposeNoOpRoundTripByteDiffSeamTests|FullyQualifiedName~ComposePatchEngineSaveSeamTests|FullyQualifiedName~ComposeShadowPatchEngineByteDiffSeamTests"

Passed! - Failed: 0, Passed: 28, Skipped: 0, Total: 28
```

Breakdown:

| Test class | Cases | Result |
|---|---|---|
| `ComposeShadowPatchEngineByteDiffSeamTests` (NoOpApply x3 corpus docs + InteriorInsert x3 corpus docs) | 6 | 6/6 PASS |
| `ComposePatchEngineSaveSeamTests` (6 theory slices × 3 corpus docs = 18, + 1 negative unknown-paraId Fact) | 19 | 19/19 PASS |
| `ComposeNoOpRoundTripByteDiffSeamTests` (task 004 original no-op proof × 3 corpus docs) | 3 | 3/3 PASS |
| **Total** | **28** | **28/28 PASS, 0 failed** |

Every corpus doc's untouched OOXML subtrees (styles, numbering, headers/footers, theme, media —
excluding the one legitimately-new `word/comments.xml` part created by the anchored-comment case,
per task 034's documented adjustment) are byte-identical post-cutover, for both the no-op case and
the non-empty-operation-log case (split/merge/insert/delete/interior-insert/anchored-comment).

**Pre-cutover baseline comparison (task 034, commit `006c40d94`)**: 545/545 total Compose suite green,
with the byte-diff-specific harness at 21/21 new cases (18 theory + 1 negative + 2 audit) plus the
pre-existing 3 no-op cases from task 004 — all passing. Post-cutover, the byte-diff-specific harness
set has grown to 28 (an additional `ComposeShadowPatchEngineByteDiffSeamTests` class, 6 cases, landed
between 034 and 061) and remains 100% green. **No byte-identity regression on any corpus doc.**

## 2. Full Compose test suite (server) — PASS; client suite — PASS (after dependency build)

### Server (dotnet)

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~Compose"

Passed! - Failed: 0, Passed: 515, Skipped: 0, Total: 515
```

515/515 green. This is **515, not the task-034 baseline of 545** — a net delta of -30, fully
accounted for by task 036 (retire push-annotations + `DocxAnnotationWriter`, ✅ done, commit
`bae44955b`, which lands chronologically between 034 and 061 on the critical path), which deleted 5
test files covering the retired legacy writer/push-annotations surface
(`ComposePushSaveEndpointContractTests.cs` -520 LOC, `ComposePushSavePreviewCalculatorTests.cs` -78,
`ComposePushSaveStatusStoreTests.cs` -84, `ComposeServicePushSaveTests.cs` -376,
`DocxAnnotationWriterTests.cs` -552). This is the expected, intentional test-diet for retired
production code — **not a regression**.

### Client (jest) — `src/client/shared/Spaarke.Compose.Components`

Initial run: **22 of 50 suites failed to RUN** (343/343 tests passed in the 28 suites that did run —
zero assertion failures). Root cause (confirmed by reading the stack traces, not guessed): this is a
**fresh worktree** where three sibling `file:`-dependency packages
(`Spaarke.SdapClient`, `Spaarke.UI.Components`, `Spaarke.DocumentOperations`) had never had
`npm install` run, so their `node_modules` were absent/stale and Node's module resolution couldn't
find `@fluentui/react-icons` (required by `Spaarke.UI.Components/dist/icons/SprkIcons.js`) or later
`@spaarke/auth` (required by `Spaarke.DocumentOperations/dist/hooks/useDocumentActions.js`). This is
an **environmental, not a logic** failure — pre-existing to R4, unrelated to any task-061 or task-060
change.

Per the task instruction, the dependency chain was built to unblock the suite (build artifacts only —
`node_modules` and `dist/` are gitignored; `git status --porcelain` confirms zero tracked-file changes
from this):

1. `src/client/shared/Spaarke.SdapClient`: `npm install --legacy-peer-deps --no-audit --no-fund` +
   `npm run build` (had no `dist/` at all — first build in this worktree)
2. `src/client/shared/Spaarke.UI.Components`: `npm install --legacy-peer-deps --no-audit --no-fund` +
   `npm run build` (no `build:prod` script; `npm run build` = `tsc`) — now resolves `@spaarke/sdap-client`
3. `src/client/shared/Spaarke.DocumentOperations`: `npm install --legacy-peer-deps --no-audit --no-fund`
   (dist already present; only `node_modules`/`@spaarke/auth` link was missing)

Re-run after dependency build:

```
> @spaarke/compose-components@0.2.0 test
> jest --ci

Test Suites: 50 passed, 50 total
Tests:       531 passed, 531 total
```

**50/50 suites, 531/531 tests, 0 failures.**

Task-038 guardrail tests specifically confirmed green:

```
jest --ci --testPathPatterns="ComposeFormatToolbar|saveOpLogPreservation|stepOperationInterceptor"

Test Suites: 3 passed, 3 total
Tests:       64 passed, 64 total
```

ComposeFormatToolbar gating, `ComposeWorkspace.saveOpLogPreservation`, and `stepOperationInterceptor`
all pass (64/64 cases across the 3 files).

## 3. Publish size (NFR-04) — PASS, well under ceiling

```
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/
```

Build succeeded (0 errors; pre-existing nullable/obsolete warnings only, none in `Services/Compose/`
beyond 2 pre-existing `ComposeBaselineParaIdStamper.cs` nullable warnings).

| Measure | Value |
|---|---|
| Compressed, **incl. 4 PDBs** (convention matches the ~49.63 MB baseline cited in root CLAUDE.md §10 and the task-034/POML baseline) | **46.11 MB** |
| Compressed, **excl. PDBs** | 45.31 MB |
| Uncompressed publish dir | 144 MB, 248 files |
| PDB files present | `Spaarke.Core.pdb`, `Spaarke.Dataverse.pdb`, `Spaarke.Scheduling.pdb`, `Sprk.Bff.Api.pdb` |
| Baseline (incl. PDBs) | ~49.63 MB |
| **Delta** | **-3.52 MB (decrease)** |
| Ceiling | ≤60 MB HARD |

Delta is a **decrease**, not an increase — no ≥+5 MB single-task escalation trigger fires. Well under
the 60 MB hard ceiling. Measured via `Compress-Archive` (PowerShell) over the full `deploy/api-publish/`
output, consistent with the "compressed publish output" convention used in prior baselines.

## 4. CVE scan (§10) — PASS, no new HIGH-severity CVE

```
dotnet list src/server/api/Sprk.Bff.Api/ package --vulnerable --include-transitive
```

Only pre-existing advisory reported:

```
Project `Sprk.Bff.Api` has the following vulnerable packages
   [net8.0]:
   Top-level Package                       Requested   Resolved   Severity   Advisory URL
   > System.Security.Cryptography.Xml      8.0.3       8.0.3      High       GHSA-g8r8-53c2-pm3f
                                                                  High       GHSA-8q5v-6pqq-x66h
                                                                  High       GHSA-cvvh-rhrc-wg4q
                                                                  High       GHSA-23rf-6693-g89p
                                                                  High       GHSA-mmjf-rqrv-855v
```

This is the known, pre-existing `System.Security.Cryptography.Xml` transitive advisory set — **no new
package or new HIGH-severity CVE introduced.**

## 5. NetArch / Tier-1 NetArchTest facade suite (NFR-05) — PASS (ADR-013 green; ADR-007 pre-existing-only)

### ADR-013 (no AI internals in `Services/Compose/`)

```
dotnet test tests/Spaarke.ArchTests/ --filter "FullyQualifiedName~ADR013"

Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2
```

**2/2 GREEN.** `Services/Compose/` injects no `IOpenAiClient`/executor/routing type.

### ADR-007 (no `Microsoft.Graph` type above `SpeFileStore`)

```
dotnet test tests/Spaarke.ArchTests/ --filter "FullyQualifiedName~ADR007"

Passed: 2, Failed: 1, Total: 3
```

1 failure — `GraphTypesMustBeIsolatedToInfrastructure`, failing types:

- `Sprk.Bff.Api.Services.Communication.GraphAttachment...`
- `Sprk.Bff.Api.Services.Communication.GraphMessageTo...`
- `Sprk.Bff.Api.Services.Communication.Engine.GraphMe...`
- `Sprk.Bff.Api.Infrastructure.Errors.ProblemDetailsH...`
- `Sprk.Bff.Api.Api.Office.Errors.OfficeProblemDetail...`

**Confirmed**: the failure set is exactly `Services.Communication.*` / `Api.Office.*` /
`Infrastructure.Errors.*` — matching the task brief's named pre-existing, out-of-R4-scope exception
list verbatim. **Zero `Compose` types appear in the failure set.** No regression; the other two
ADR-007 sub-tests (`Controllers must not reference Graph SDK`, `Endpoints must not reference Graph
SDK`) are green.

## 6. Client build (`build:prod`) — PASS

`src/client/shared/Spaarke.Compose.Components/package.json` has no `build:prod` script; fell back to
`npm run build` (= `tsc`):

```
> @spaarke/compose-components@0.2.0 build
> tsc

EXIT CODE: 0
```

Clean compile, 0 errors.

---

## Summary table

| # | Check | Result | Numbers |
|---|---|---|---|
| 1 | Corpus byte-diff (NFR-01) | ✅ PASS | 28/28, 0 failed; byte-identical on every corpus doc; no regression vs 034 |
| 2a | Full Compose suite (server) | ✅ PASS | 515/515 (delta -30 vs 034's 545, fully explained by task 036 legacy-writer test deletion) |
| 2b | Compose client suite (jest) | ✅ PASS | 50/50 suites, 531/531 tests (after building 3 sibling `file:` deps — env-only issue, zero assertion failures at any point) |
| 2c | Task-038 guardrail tests | ✅ PASS | 64/64 (ComposeFormatToolbar, saveOpLogPreservation, stepOperationInterceptor) |
| 3 | Publish size (NFR-04) | ✅ PASS | 46.11 MB incl. PDBs (45.31 MB excl.), delta -3.52 MB vs ~49.63 MB baseline, ≤60 MB ceiling |
| 4 | CVE scan (§10) | ✅ PASS | Only pre-existing `System.Security.Cryptography.Xml` High advisories; no new package |
| 5a | NetArch ADR-013 | ✅ PASS | 2/2 green |
| 5b | NetArch ADR-007 | ✅ PASS (pre-existing-only) | 2/3 green; 1 failure = pre-existing `Services.Communication.*`/`Api.Office.*`/`Infrastructure.Errors.*` only, zero Compose types |
| 6 | Client build:prod | ✅ PASS | No `build:prod` script; `npm run build` (tsc) exit 0 |

**No escalation trigger fired.** Task 062 (deploy + UAT) and 063 (flagship gate) are unblocked by this
evidence.

## Side effects / repo hygiene

- No production code changed. Only test/verification commands were run.
- Three sibling client packages had `npm install` (+ `npm run build` for two of them) run to resolve
  a pre-existing, environment-only module-resolution gap in this fresh worktree
  (`Spaarke.SdapClient`, `Spaarke.UI.Components`, `Spaarke.DocumentOperations`). All resulting
  `node_modules/` and `dist/` output is gitignored — `git status --porcelain` confirms zero tracked-file
  changes from these installs/builds.
- `deploy/api-publish/` publish output is gitignored; temporary size-measurement zip files were created
  and deleted in the same session.
