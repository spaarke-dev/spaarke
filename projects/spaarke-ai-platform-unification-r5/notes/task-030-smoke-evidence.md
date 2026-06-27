# Task 030 — Smoke Evidence: Insights tool integration (D2-20)

> **Status**: SCAFFOLD COMPLETE — SME walkthrough PENDING (operator decision 2026-06-04 to defer)
> **Date**: 2026-06-04 (scaffold authored); SME walkthrough date — TBD
> **Task**: D2-20 — `projects/spaarke-ai-platform-unification-r5/tasks/030-insights-tool-smoke-tests.poml`
> **Rigor**: FULL
> **Pure-test task** — BFF publish-size delta = **0 MB** (no BFF code changed). Zero new NuGet / npm dependencies.
> **POML status**: `complete-code-scaffold-pending-sme` (custom non-terminal status)

---

## 1. Scaffold deliverables (this session)

| Artifact | Path | LOC |
|---|---|---|
| Smoke matrix (JSON, 15 questions) | `tests/integration/Spe.Integration.Tests/fixtures/insights-smoke-matrix.json` | ~145 |
| Integration test class (xUnit theory) | `tests/integration/Spe.Integration.Tests/InsightsToolIntegrationTests.cs` | ~330 |
| Smoke evidence template (this file) | `projects/spaarke-ai-platform-unification-r5/notes/task-030-smoke-evidence.md` | — |
| SME walkthrough template (companion) | `projects/spaarke-ai-platform-unification-r5/notes/task-030-sme-walkthrough.md` | — |

The smoke test class:
- Lives in `Spe.Integration.Tests` (R5 CLAUDE.md §3.1 reuse — no parallel test project)
- Consumes `IntegrationTestFixture` via `IClassFixture` (same pattern as `PlaybookExecutionIntegrationTests`, `EventEndpointsTests`)
- Uses `[Trait("Category", "Integration")]` + `[Trait("Feature", "InsightsTool")]` for independent filterability
- xUnit `[SkippableTheory]` + `[MemberData(nameof(LoadSmokeMatrix))]` — fixture-JSON-driven
- **Imports NOTHING from `Sprk.Bff.Api.Services.Ai.Insights` or `Sprk.Bff.Api.Models.Insights`** (Zone B boundary; ADR-013 §3.5 + R5 CLAUDE.md §3.5). Local DTOs at file bottom.

---

## 2. 15-question matrix breakdown (binding per integration brief §8)

### Per practice area: 5 questions each

- **CTRNS (contracts/transactional)**: `ctrns-001` … `ctrns-005`
- **IPPAT (IP/patents)**: `ippat-001` … `ippat-005`
- **BNKF (banking/finance)**: `bnkf-001` … `bnkf-005`

### Diversification across Wave D7 synthetic GUIDs

| Subject | Count | IDs |
|---|---|---|
| `matter:da116923-d65a-f111-a825-3833c5d9bcb1` | 8 | ctrns-001, 002, 003, 005; ippat-001; bnkf-002, 003, 004 |
| `project:27845394-8e5f-f111-a825-70a8a59455f4` | 5 | ctrns-004; ippat-002, 003, 004, 005 |
| `invoice:05c8ef8d-8e5f-f111-a825-70a8a59455f4` | 2 | bnkf-001, 005 |

All three synthetic entities exercised; matter weighted heaviest (most question-topic surface area on the synthetic dataset).

### Path/mode mix (drives both code paths + exercises the classifier vs forceMode branches)

| `forceMode` value | Count | Purpose |
|---|---|---|
| `null` (classifier path) | 10 | Default LLM-routing; exercises intent classifier branch |
| `"playbook"` | 3 | Force playbook path (cost prediction); exercises forced-playbook branch |
| `"rag"` | 2 | Force RAG path; exercises forced-RAG branch (skips classifier) |

| `expectedPathHint` (informational) | Count |
|---|---|
| `rag` | 12 |
| `playbook` | 3 |

Note: `expectedPathHint` is **informational only** in the JSON matrix — the test does NOT assert it. The contract guarantees `path ∈ {"playbook", "rag"}`; the smoke test asserts that invariant + downstream FR-04 consequences (empty answer ⇔ empty citations on RAG; `playbookId` present on playbook). The hint records authoring intent for the SME walkthrough to compare against observed behavior. Decline + empty-results are expected naturally on a subset of synthetic-entity questions (e.g., tail-policy, prosecution-strategy, fee-disputes) — those are legitimate per integration brief §4.4 + §4.5, not failures.

---

## 3. Information-leakage discipline confirmation (ADR-018 + integration brief §5.2)

Confirmed for this scaffold:

| Artifact | Verbatim response content? |
|---|---|
| `insights-smoke-matrix.json` | NO — only question text (OK per discipline; questions are synthetic, written by R5 team) and rubric hints (paraphrase of expected structure) |
| `InsightsToolIntegrationTests.cs` | NO — structural assertions only; logs `path`, confidence BUCKET (not value to 2dp leak), citation COUNT, `correlationId`; never logs `Answer`, citation `Excerpt`, or `StructuredResult` content |
| `task-030-smoke-evidence.md` | TEMPLATE — placeholders for per-question structural outcomes; explicit instructions throughout to paraphrase observations |
| `task-030-sme-walkthrough.md` | TEMPLATE — placeholders for SME categorical verdicts (usable / partial / not usable + paraphrased rationale); explicit no-verbatim-content reminder at top |

Test-method-level logging examples (acceptable; structural only):
```
[PASS-STRUCTURE] ctrns-001 [CTRNS]: path=rag, confidence=mid(<0.8), citationCount=3, playbookId=(none), correlationId=r5-smoke-ctrns-001-<guid>
[FAIL-STRUCTURE] ctrns-002 [CTRNS]: HTTP 503 ServiceUnavailable; errorCode=ai.insights.disabled; correlationId=<guid>.
```

What is NEVER logged or recorded:
- The `Answer` field text
- Citation `Excerpt` text
- Document content fragments
- Prompt text
- `StructuredResult.Envelope` content (only "present"/"absent" markers)

---

## 4. Pre-flight checklist (to be completed at execution time)

When the operator runs the smoke matrix against Spaarke Dev:

| Check | Status | How verified |
|---|---|---|
| (a) Spaarke Dev BFF `InsightsAssistantQuery` endpoint live | ☐ pending | `GET https://spaarke-bff-dev.azurewebsites.net/swagger` shows `Insights` tag → `InsightsAssistantQuery` |
| (b) Wave F (Insights v1.1) deployment status | ☐ pending | Read `notes/insights-r2-coordination.md` §8 changelog at execution time; record v1.0 vs v1.1 in §5 below |
| (c) Wave D7 synthetic entities accessible | ☐ pending | Dataverse spot-check at operator's discretion |
| (d) No competing smoke / load testing in flight on Spaarke Dev | ☐ pending | Operator confirms; aggregate rate budget = 60/min/oid (`ai-context`) |
| (e) `SPAARKE_DEV_BFF_URL` env var set | ☐ pending | Smoke test SKIPs without it |
| (f) `SPAARKE_DEV_BEARER_TOKEN` env var set | ☐ pending | Smoke test SKIPs without it; token must have `tid` + `oid` claims |
| (g) All 6 dependency tasks (024 + 025 + 026 + 027 + 028 + 029) confirmed ✅ in `TASK-INDEX.md` | partially — 024, 025, 029 ✅; 026, 027, 028 still 🔲 at scaffold time (2026-06-04) | Re-verify before execution; smoke tests cannot validate an incomplete integration stack |

> **Note (2026-06-04)**: tasks 026, 027, 028 are `🔲 not-started` at the time of scaffold authoring. The smoke test scaffold itself doesn't require them — it tests the BFF endpoint contract directly via HTTP — but the SME walkthrough (operator-led) consumes the rendered output via the SpaarkeAi UI, which depends on tasks 026 (renderer) + 027 (citations) + 028 (badge). The operator should re-verify the TASK-INDEX before scheduling the SME walkthrough.

---

## 5. Wave F deployment status (to be recorded at execution time)

| Field | Value |
|---|---|
| Wave F deployed to Spaarke Dev as of execution date? | TBD |
| v1.1 SSE consumed? | TBD |
| v1.1 `citations[].href` exercised? | TBD |
| Fallback path tested (v1.0 single-shot)? | TBD |

Either state (v1.0 only OR v1.1) is contract-acceptable per NFR-11 graceful fallback. Record actual at execution time.

---

## 6. 15-question structural matrix — RESULTS (to be filled at execution time)

Operator runs:
```powershell
$env:SPAARKE_DEV_BFF_URL = "https://spaarke-bff-dev.azurewebsites.net"
$env:SPAARKE_DEV_BEARER_TOKEN = "<OBO JWT with tid + oid>"

dotnet test tests/integration/Spe.Integration.Tests/Spe.Integration.Tests.csproj `
  --filter "Category=Integration&Feature=InsightsTool" `
  --logger "console;verbosity=detailed" `
  --logger "trx;LogFileName=insights-smoke-results.trx"
```

Then paste the structural-only outcome per row. Verbatim content NEVER recorded.

| ID | Practice area | Subject scheme | Observed path | Confidence bucket | Citation count | CorrelationId | Categorical verdict |
|---|---|---|---|---|---|---|---|
| ctrns-001 | CTRNS | matter | TBD | TBD | TBD | TBD | TBD |
| ctrns-002 | CTRNS | matter | TBD | TBD | TBD | TBD | TBD |
| ctrns-003 | CTRNS | matter | TBD | TBD | TBD | TBD | TBD |
| ctrns-004 | CTRNS | project | TBD | TBD | TBD | TBD | TBD |
| ctrns-005 | CTRNS | matter | TBD | TBD | TBD | TBD | TBD |
| ippat-001 | IPPAT | matter | TBD | TBD | TBD | TBD | TBD |
| ippat-002 | IPPAT | project | TBD | TBD | TBD | TBD | TBD |
| ippat-003 | IPPAT | project | TBD | TBD | TBD | TBD | TBD |
| ippat-004 | IPPAT | project | TBD | TBD | TBD | TBD | TBD |
| ippat-005 | IPPAT | project | TBD | TBD | TBD | TBD | TBD |
| bnkf-001 | BNKF | invoice | TBD | TBD | TBD | TBD | TBD |
| bnkf-002 | BNKF | matter | TBD | TBD | TBD | TBD | TBD |
| bnkf-003 | BNKF | matter | TBD | TBD | TBD | TBD | TBD |
| bnkf-004 | BNKF | matter | TBD | TBD | TBD | TBD | TBD |
| bnkf-005 | BNKF | invoice | TBD | TBD | TBD | TBD | TBD |

**Categorical verdict legend**: `usable` / `partial` / `not-usable` / `inconclusive` (operator + AI agent assign collaboratively post-run; SME refines during walkthrough).

---

## 7. Aggregate per practice area (to be filled at execution time)

| Practice area | Structural pass rate | Categorical usable rate (target ≥ 4/5) | Notable patterns (paraphrased) |
|---|---|---|---|
| CTRNS | TBD / 5 | TBD / 5 | TBD |
| IPPAT | TBD / 5 | TBD / 5 | TBD |
| BNKF | TBD / 5 | TBD / 5 | TBD |
| **Aggregate** | **TBD / 15** | **TBD / 15** | Target: 15/15 structural; ≥ 12/15 categorical usable |

---

## 8. Correlation-id end-to-end trace (FR-17 / SC-16; ≥ 3 representative requests)

Pick one question per practice area with structurally-successful response; record correlationId + (if accessible) App Insights / Kusto query reference. Trace BODY is NEVER captured here — only the lookup query + match count.

| Practice area | Question ID | CorrelationId | App Insights/Kusto query (reference only) | Trace result-count | Status |
|---|---|---|---|---|---|
| CTRNS | TBD | TBD | `requests \| where customDimensions.correlationId == "<id>"` | TBD | ☐ pending |
| IPPAT | TBD | TBD | (same as above) | TBD | ☐ pending |
| BNKF | TBD | TBD | (same as above) | TBD | ☐ pending |

---

## 9. Rate-limit observations (ADR-016 / integration brief §11)

The test paces inter-request gap at ~3s (`InterRequestPace`). 15 questions × 3s ≈ 45s for the matrix — well under the 60/min aggregate budget (60/oid across `/ask` + `/search` + `/assistant/query`).

| Observation | Status |
|---|---|
| Any 429 observed during execution? | TBD |
| If yes — `Retry-After` honored per task 029? | TBD (test SKIPs the case + logs positive-validation note) |
| Aggregate request count within budget? | TBD |

A 429 during smoke is recorded as **positive end-to-end validation** of task 029's retry handling — NOT a smoke-test failure (per task 030 POML constraint citing brief §11).

---

## 10. Defects / Sev-1 findings

| Sev | Finding | Cross-link | Status |
|---|---|---|---|
| TBD | TBD | TBD | ☐ pending execution |

**Sev-1 blocks**: contract violation, no-leakage failure (any forbidden content leaked into rendered DOM or evidence files), SC-11 or SC-18 not met → STOP, escalate, do not close task.

**Sev-2/3**: UX rough edges, calibration observations → feed task 044 R6 backlog (cross-link to existing project artifacts in notes).

---

## 11. Quality gate confirmations

| Gate | Status |
|---|---|
| **ADR-013 §3.5 (Zone B boundary)** | ✅ `InsightsToolIntegrationTests.cs` imports nothing from `Sprk.Bff.Api.Services.Ai.Insights` / `Sprk.Bff.Api.Models.Insights`; verified by file inspection (local DTOs at bottom). |
| **ADR-016 (rate-limit honoring)** | ✅ Test paces requests at 3s/case; 429 path SKIPs (positive validation, not failure). |
| **ADR-018 (no information leakage)** | ✅ Test asserts/logs STRUCTURAL only; evidence template enforces paraphrase rule; explicit reminders throughout. |
| **ADR-019 (ProblemDetails parsing)** | ✅ Local `InsightsProblemDetailsDto` mirrors `errorCode` + `correlationId` extensions; reuses contract envelope. |
| **R5 CLAUDE.md §3.1 (reuse mandate)** | ✅ Consumes existing `IntegrationTestFixture` + `IClassFixture`; matrix via xUnit `[Theory]` + `[MemberData]`; no parallel test infrastructure. |
| **R5 CLAUDE.md §3.5 (Insights consumer governance)** | ⚠️ partial — tasks 026/027/028 still 🔲 at scaffold time (2026-06-04). Re-verify before SME walkthrough execution. |
| **R5 CLAUDE.md §3.6 (BFF publish-size)** | ✅ delta = 0 MB (no BFF code changed). |
| **R5 CLAUDE.md §3.7 (test obligation)** | ✅ this task IS the integration test obligation for P2-G6/G7. |
| **spec SC-11 (registered tool + both paths render)** | ☐ pending execution — depends on tasks 026/027/028 ship + SME walkthrough. |
| **spec SC-18 (SME walkthrough signoff)** | ☐ PENDING (operator decision 2026-06-04 to defer SME to future session). See `task-030-sme-walkthrough.md`. |

---

## 12. Zero-new-dependencies confirmation

- New NuGet packages: **NONE**
- New npm packages: **NONE**
- Test class uses already-present `xunit`, `FluentAssertions`, `Xunit.SkippableFact`, `Microsoft.AspNetCore.Mvc.Testing` (all in `Spe.Integration.Tests.csproj` since prior tasks)
- Fixture JSON is data-only (no transitive code dependency)

Verified: `Spe.Integration.Tests.csproj` unchanged by this task.

---

## 13. SME walkthrough deferred (operator decision 2026-06-04)

Per task 030 POML acceptance criteria, SME walkthrough is BINDING per spec SC-18. **Operator decision (2026-06-04)**: defer SME walkthrough to a future session.

**For this task scaffold**:
- Code scaffold + 15-question matrix + evidence templates are READY for execution
- POML `<status>` = `complete-code-scaffold-pending-sme` (custom non-terminal status)
- `TASK-INDEX.md` shows `🔄 in-progress (scaffold complete; SME walkthrough pending)`
- Task does NOT close until SME signoff captured in `task-030-sme-walkthrough.md`

**When the operator schedules + completes the SME walkthrough**:
1. Run the 15-question structural matrix (Section 6 above; populate columns)
2. Fill correlation-id trace section (Section 8 above)
3. Compute aggregate per practice area (Section 7 above)
4. Coordinate SME session per `task-030-sme-walkthrough.md` Section 2 protocol
5. Record SME categorical verdicts + signoff
6. Update POML `<status>` = `complete`
7. Update TASK-INDEX 030 `🔄 → ✅`
8. Reset `current-task.md` to next pending task (likely 031)
9. Phase 2 close gate clears once both 030 + 031 are ✅

---

## 14. Final-summary placeholder (to be filled at SME-walkthrough close)

| Metric | Target | Actual |
|---|---|---|
| Structural pass rate | 15/15 | TBD |
| Categorical usable rate | ≥ 12/15 | TBD |
| Practice areas with `usable` aggregate | 3/3 | TBD |
| Sev-1 findings | 0 | TBD |
| SME signoff captured | yes | TBD (DEFERRED) |
| `code-review` + `adr-check` gates | pass | TBD (run at SME close, not scaffold time) |
| BFF publish-size delta | 0 MB | ✅ confirmed |
| New dependencies | 0 | ✅ confirmed |

---

*Authored 2026-06-04 by R5 task-execute (task 030 sub-agent). Scaffold complete; SME walkthrough deferred per operator decision. POML status = `complete-code-scaffold-pending-sme`. Re-opens on operator schedule.*
