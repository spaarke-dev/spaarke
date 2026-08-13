# Test diet report — spaarkeai-compose-r6

**Run date**: 2026-08-13
**Branch**: `work/spaarkeai-compose-r6`
**Scope**: union of test files across ALL merged R6 PRs (#745 Phase 1+2, #747 Phase 3+4+5+6, #748 042-arc) — the project's full life, since deltas merged to master incrementally.
**Gate**: CLAUDE.md §7 project-close test-diet (ADR-038 §7, 17-ban classifier B1–B17).

## Summary

| Class | Count (files) | Test methods | Action |
|---|---|---|---|
| MAINTAIN (KEEP at canonical path) | 27 `.cs` | 181 | confirmed — no action |
| MAINTAIN-conditional (retire WITH component) | *(1 of the 27, noted below)* | *(13 of the 181)* | no action now; see note |
| Client Jest suites (ADR-038 explicitly out of scope) | 6 TS/TSX | 100 | colocated per convention — behavioral; keep |
| Non-test artifacts (fixtures ×2, corpus manifest, OOXML comparer helper) | 4 | — | infrastructure — keep |
| SCAFFOLDING (DELETE candidate) | **0** | 0 | none |
| AMBIGUOUS (reviewer judgment) | **0** | 0 | none |
| PATH-VIOLATION (wrong KEEP path) | **0** | 0 | none |
| **Total files touched** | **37** | **281** | — |

## Why zero SCAFFOLDING deletions

1. **Path discipline** — every server test file lives at an ADR-038 KEEP path or the repo-mandated BFF suite:
   - `tests/integration/seam/**` (19 files, 107 methods) — vertical-slice-seam KEEP category; the project's core deliverable IS this suite (fidelity round-trip, hard-tier degradation, PDF intake, version history, template chrome provenance, the CI fidelity gate harness).
   - `tests/integration/contract/**` (1 file, 6) — endpoint-contract KEEP (create-on-save contract incl. B-MED-3 association-inheritance facts).
   - `tests/integration/regression/**` (1 file, 2) — `NdaSaveNo422RegressionTests` — the "every bug = regression test" rule applied to THE founding bug of this project.
   - `tests/unit/Sprk.Bff.Api.Tests/Services/{Compose,Ai}/**` (6 files, 66) — the BFF §10 **test-update obligation** suite (precedent: assistant-r2 diet report, same ruling); behavioral engine tests (projector slot/grid invariants, part-merge, layout mapping), not wiring.
2. **Ban-shape scan clean** — zero non-comment `Mock<HttpMessageHandler>` (B1; the only textual hits are the suites' own "NO Mock<HttpMessageHandler>" compliance comments), zero DI-registration asserts (B3), zero ctor-null tests (B4), zero `Test1`/`Foo_Works` names (B13). `GetRequiredService` appearances are WebApplicationFactory slice resolution (legitimate), not registration assertions. `NotBeNull()` occurrences are intermediate guards inside deeper behavioral asserts, not sole assertions (B10 clear).
3. **Already gated at merge** — every suite passed the Step 9.5 TEST-MODIFYING gates when authored (all PASS-WITH-FINDINGS records in `notes/020-canonical-hub-design.md`, `notes/040-pdf-intake.md`, `notes/042-052-close.md`); the reviews specifically hunted vacuous assertions (042 MED-1 chrome assertion was caught + fixed there).
4. **Client Jest suites** — ADR-038 line 11: "Does NOT apply to React/PCF Jest tests." The 6 suites (100 tests: reducer lifecycle, banner honesty copy, save routing, template dialog, version-history modal, docxBridge imported-model) are behavioral, scenario-named, colocated per the shared-lib convention.

## MAINTAIN-conditional note (the one honest flag)

`tests/unit/Sprk.Bff.Api.Tests/Services/Compose/ComposeBaselineParaIdStamperTests.cs` (13 methods) tests the
count-gate/stamper that task 012 **retired from the save path**. The component is NOT dead: the ADR-049
Path-B amendment clause 4 retains it ONLY for the transitional op-log path (`ContentModel`-null saves).
Classification: **MAINTAIN while the transitional path lives.** When the `TRANSITIONAL op-log save shape`
Warning decays to zero and the engine + count-gate are deleted (defer register §E,
`projects/spaarkeai-compose-r7/notes/r6-defer-register-consolidated.md`), these tests delete **with** the
component in the same PR — that future deletion is component-retirement, not scaffolding-diet.

## Delete commands

None.

## Path-move commands

None.

## Count delta

- Test files touched during R6: 37 (27 `.cs` test classes + 6 TS/TSX + 4 non-test artifacts)
- Test methods: 181 server + 100 client = 281
- MAINTAIN: all · SCAFFOLDING: 0 · AMBIGUOUS: 0 · PATH-VIOLATION: 0
- Net post-diet expected count: unchanged (no deletes)

## Industry citation

Build-vs-maintain per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior;
Google test-sizes; DHH less-tests). 17-ban classifier B1–B17. R6's test population is the project's
*product* (the fidelity harness + seam suites ARE Success Criterion 5), not scaffolding around it.
