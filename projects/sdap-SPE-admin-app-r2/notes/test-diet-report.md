# Test diet report — sdap-SPE-admin-app-r2

**Run date**: 2026-08-30
**Branch**: `work/sdap-SPE-admin-app-r2`
**Scope**: this project's test delta **relative to master** — `git diff origin/master HEAD -- 'tests/**/*.cs'`

> ⚠️ **Scoping correction — read this before comparing to any earlier count.**
> The skill's default scope (`{start-commit}..HEAD`) returns **354 test files** here, because this
> branch has merged master **three times** and that range sweeps in every other project's test
> changes. Dieting those would mean this project adjudicating other teams' tests. The correct scope
> is the branch's delta **against master**, which is **6 files / 41 test methods**.

---

## 🔴 Material change since the last inventory

`notes/test-retirement-inventory.md` held **~104 classified scaffolding methods across 6 files** plus
the 20 `SecurityEndpointTests` for this pass. **They are gone** — deleted by
`ci-cd-unit-test-remediation-r1` and pulled in via the master merge:

| File held for this diet | Status now |
|---|---|
| `SecurityEndpointTests.cs` | ✅ deleted upstream |
| `ContainerTypeEndpointsTests.cs` | ✅ deleted upstream |
| `SpeAdminGraphServiceTests.cs` | ✅ deleted upstream |
| `ContainerEndpointsTests.cs` | ✅ deleted upstream |

Those methods were adjudicated by another project's cleanup, not by this one. **This project's
remaining obligation is far smaller than the handoff implies** — and the handoff's "~104 held" line
is now stale and should not be carried into the wrap-up PR.

---

## Summary

| Class | Count | Action |
|---|---|---|
| **MAINTAIN** (keep at canonical path) | **33** | confirmed — no action |
| **SCAFFOLDING** (delete candidate) | **0** | none |
| **AMBIGUOUS** (reviewer judgment) | **1** | listed below |
| **PATH-VIOLATION** (wrong KEEP path) | **7** | `git mv` proposed below |
| **Total test methods in scope** | **41** | — |
| Support files (0 test methods) | 1 | `GraphWireMockFixture.cs` — load-bearing, keep |

**Zero scaffolding.** Every test this project wrote lives at `tests/integration/contract/**` — a KEEP
path — asserts observable behaviour, and carries a `{Method}_{Scenario}_{ExpectedResult}` name. That
is not a flattering accident: these were written *after* the defects they guard, so each one has a
concrete production behaviour it protects.

---

## Delete commands

**None.** No test in this project's delta matches any of B1–B17.

---

## Path-move commands (reviewer judgment required)

```bash
# HTTP contract tests sitting at a non-KEEP path.
# Content is maintain-class; only the location is wrong (heuristic 1).
git mv tests/unit/Sprk.Bff.Api.Tests/SpeAdmin/SearchItemsTests.cs \
       tests/integration/contract/SpeAdmin/SpeAdminSearchItemsContractTests.cs
```

`tests/unit/Sprk.Bff.Api.Tests/**` is **not** one of the eight KEEP paths. All seven methods assert
route + status code — the definition of a contract test:

| Method | Assertion |
|---|---|
| `SearchItems_WithoutAuthentication_Returns401` | 401 |
| `SearchItems_MissingConfigId_Returns401WithoutToken` | 401 precedence over 400 |
| `SearchItems_WithToken_MissingConfigId_Returns400` | 400 |
| `SearchItems_WithToken_EmptyQuery_Returns400` | 400 |
| `SearchItems_WithToken_WhitespaceQuery_Returns400` | 400 |
| `SearchItems_WithToken_InvalidConfigId_Returns400` | 400 |
| `SearchItems_WithToken_ValidConfigIdNotFound_Returns400` | 400 — ⚠️ see below |

### 🔴 One of the seven needs more than a move

`SearchItems_WithToken_ValidConfigIdNotFound_Returns400` **makes a real outbound Dataverse call**. It
has timed out at ~100 s repeatedly this project (and passed on other runs — it is network-flaky, not
consistently broken).

Moving it to a contract path does **not** fix that; it relocates a network dependency into a KEEP
path, which is worse than leaving it. Two acceptable resolutions:

1. **Offline Dataverse double** — the config lookup returns "not found" without a network call. Preferred: keeps the 400-on-unknown-config contract covered.
2. **Delete this one method**, move the other six. Loses the coverage but removes the flake.

**Not acceptable**: tightening the assertion or adding it to
[`tests/.reliability-registry.json`](../../../tests/.reliability-registry.json) for automatic retries.
It is **not currently registered** (checked), and it should not be — the registry buys retries for
*timing-dependent* tests, and using it here would mask a real outbound call from a unit path rather
than remove it.

---

## Ambiguous — reviewer judgment

| File:Method | Ambiguity | Suggestion |
|---|---|---|
| `SpeAdminCustomPropertyContractTests.cs`:`UpdateCustomProperties_WithSeveralProperties_SendsThemAllInOneWrite` | Mixes a **behavioural** assertion (both properties appear in the body — caller-visible) with an **implementation** assertion (`ContainSingle` on request count — the caller cannot observe whether it was one PATCH or two, except through atomicity on partial failure) | **Lean keep, but narrow it.** The body assertion earns its place. If the request-count assertion ever fails a legitimate refactor (e.g. chunking for large sets), delete that line rather than the test. Flagged here rather than defended because I wrote it — the honest classification is ambiguous |

---

## Maintain — confirmed (33)

All at `tests/integration/contract/SpeAdmin/**`. Each names a production behaviour that breaks if deleted.

| File | Methods | What they protect |
|---|---|---|
| `SpeAdminRecycleBinItemContractTests.cs` | 15 | Restore/delete have **opposite** failure semantics. Notably `…WhenIdWasNeverInTheBin_ReportsNoPurgeRatherThanSuccess` (fabricated success on an irreversible op) and `…WhenTheBinCannotBeReRead_ReportsUnverifiedRatherThanAssumingSuccess` |
| `SpeAdminSecurityContractTests.cs` | 13 | A security screen must not confuse "nothing wrong" with "couldn't check". `…WhenAccessDenied_ThrowsRatherThanReportingNoAlerts`, `…ReturnsNullRatherThanAZeroScore`, plus the 2 pinning the corrected access-denied wording |
| `SpeAdminContainerTypeCreateContractTests.cs` | 3 | `owningAppId` present in the body; `trial` sent as trial; unmappable classification throws before any request |
| `SpeAdminCustomPropertyContractTests.cs` | 2 | PATCH targets the sub-resource, not the container; body root unwrapped |

Support file: `GraphWireMockFixture.cs` — 0 test methods, but four contract files depend on it.
Deleting it breaks compilation. **KEEP.**

---

## Count delta

- Test methods added/modified by this project: **41**
- MAINTAIN: **33**
- SCAFFOLDING (delete): **0**
- AMBIGUOUS: **1**
- PATH-VIOLATION (move): **7**
- Net post-diet expected count: **41** (or **40** if the flaky method is deleted rather than doubled)

---

## Out of scope — noted, not actioned

Five SpeAdmin test files sit at the non-KEEP path `tests/unit/Sprk.Bff.Api.Tests/Api/SpeAdmin/`
(`BulkOperationTests`, `CustomPropertyTests`, `RegisterContainerTypeTests`, `SearchContainersTests`,
`UpdateContainerTypeSettingsTests`). **This project did not touch them**, so they are outside the
touch radius and are not adjudicated here. They are legitimate candidates for a future pass.

---

## Industry citation

Build-vs-maintain criteria per [ADR-038 §7](../../../docs/adr/ADR-038-testing-strategy.md#7-build-vs-maintain-criteria-scaffolding-test-bans--added-2026-06-26-per-spec-fr-b08)
(Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test sizes; DHH
less-tests). 17-ban classifier B1–B17.
