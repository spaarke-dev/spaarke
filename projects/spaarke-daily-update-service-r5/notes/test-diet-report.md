# Test diet report — spaarke-daily-update-service-r5

**Run date**: 2026-07-10
**Branch**: work/spaarke-daily-update-service-r5 (merged to master via PR #611)
**Scope**: tests touched between project start (`c037a28de`) and HEAD, **restricted to R5's own test surface** — the `c037a28de..HEAD` range spans two master merges that pulled in the entire spaarke-ai-architecture-redesign-r2 test suite (Compose/*, Chat/*, Gate/*, seam/*, dispatch/context-binder/semantic-scope, most Eval/*). Those are NOT this project's tests and are excluded. R5 ownership confirmed via `git log c037a28de..HEAD --no-merges -- <file>`.

## Summary

| Class | Count (files) | Action |
|---|---|---|
| MAINTAIN (behavioral / regression / contract / guardrail) | 11 files | confirmed — keep |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| PATH-VIOLATION (wrong KEEP path) | 0 | — |
| **Total R5 test files touched** | **11** | — |

**Total R5 test methods: ~130** (119 C# `[Fact]`/`[Theory]` + 11 client jest) — **all MAINTAIN. Zero deletes, zero moves.**

## Delete commands

**None.** No scaffolding-class tests were introduced by this project.

## Path-move commands

**None.** The BFF unit tests sit under `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/**` — the repo's canonical BFF unit-test tree (the entire BFF unit suite lives there). They are behavioral service tests, not `tests/unit/domain/**`-only material; per ADR-038's integration-heavy pyramid they are legitimately MAINTAIN at this location. Contract/Eval tests are already at `tests/integration/contract/**`; the guardrail test is an ArchTest. No relocation warranted.

## Maintain — confirmed (no action)

| File | Methods | KEEP category | Why maintain |
|---|---|---|---|
| `tests/integration/contract/Api/Ai/DailyBriefingEmailEndpointContractTests.cs` | 11 | contract | Endpoint contract: 200 self-send/colleague, 400 external/malformed/no-claim, 401 unauth — real request→response behavior + egress-guard enforcement |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/MembershipResolverServiceTests.cs` | 25 | behavioral + **regression** | Incl. `ResolveAsync_GeneratedFetchXml_MustNotUseDistinct_SoRecordIdsAreReturned` — the guard for the 0→49-matter completeness bug (every bug = regression) |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Membership/IdentityNormalizationServiceTests.cs` | 11 | behavioral | `sprk_primarycontact` contact resolution + 6-path identity, incl. no-contact / query-throws degradation |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingCollectorTests.cs` | 22 | behavioral | Collector de-dup, resolver-bypass re-flip; carries an explicit ADR-038 anti-pattern-ban header |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingCompositeServiceTests.cs` | 10 | behavioral | Composite render orchestration |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingNarratorEntityLinkTests.cs` | 5 | behavioral | Entity-link resolution in deterministic bullets |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Narrators/DailyBriefingNarratorItemRefsTests.cs` | 5 | behavioral | Item-ref projection |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Nodes/UpdateRecordNodeExecutorTests.cs` | 21 | behavioral + **regression** | `CoerceFieldValue` String→Choice fix (the Choice-write 500 defect) |
| `tests/integration/contract/Eval/BriefingAccuracyEvalSuiteTests.cs` | 7 | contract (Eval) | Mixed-item accuracy corpus + gate; module-boundary mocks only |
| `tests/Spaarke.ArchTests/DailyBriefingGroundednessGuardrailTests.cs` | 2 | governance guardrail | Enforces `GroundednessCheckService` stays OUT of the briefing path (project rule #2) |
| `src/client/shared/Spaarke.DailyBriefing.Components/test/emailShareDraft.test.ts` | 11 | behavioral (client) | Pure-helper unit tests for `buildItemEmailDraft` / deep-link / activity record — no DOM mount, deterministic |

## B1–B17 ban scan result

- **B1/B2 (Mock<HttpMessageHandler> / Mock<IServiceClient>)**: 0 real occurrences. The 2 grep hits are **comments documenting the ban's absence** (`// NO Mock<HttpMessageHandler>`), i.e. evidence of compliance.
- **B3/B4 (DI-registration / ctor-null assertion)**: 0. (2 `GetRequiredService` hits in the contract test are `WebApplicationFactory` fixture plumbing, not registration assertions.)
- **B13 (name-without-scenario)**: 0 — every method follows `{Method}_{Scenario}_{ExpectedResult}`.
- **B10 (coverage-filler NotThrow/NotNull-only)**, **B6 (mirror)**, **B9 (pass-through)**, **B11/B14/B16 (language-feature)**, **B17 (field-by-field mapper)**: none observed on inspection.

## Count delta

- R5 test files touched: 11
- Classified MAINTAIN: 11
- Classified SCAFFOLDING: 0
- Classified AMBIGUOUS: 0
- Net post-diet expected count: **unchanged** (no deletions)

## Verdict

**Clean diet — no reconciliation required.** The project authored behavioral / regression / contract / guardrail tests exclusively, followed the ADR-038 §7 build-vs-maintain discipline (behavioral names, module-boundary mocks, explicit ban-avoidance comments), and introduced no scaffolding. Nothing to delete or move before wrap-up.

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17.
