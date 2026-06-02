# sdap.bff.api-test-suite-repair-r2 — Design

> **Project**: `sdap.bff.api-test-suite-repair-r2`
> **Status**: 🟡 Design (drafted 2026-06-01)
> **Owner**: ralph.schroeder@hotmail.com
> **Predecessor**: `sdap-bff.api-test-suite-repair` (r1, closed 2026-06-01)
> **Target end date**: **2026-08-31** (3 months; beat the September fix-by-date cliff)

---

## 1. Why this project exists

The r1 predecessor (`sdap-bff.api-test-suite-repair`) was deliberately **test-only**: NFR-01 forbade any changes under `src/`. r1 succeeded at its scope — `Failed: 0` across both test projects, CI gate restored, anti-drift codified — but the test repair **revealed** a backlog of real production-side problems and downstream test-quality work that r1 could not address.

**r2 is the closure project**: it takes the work r1 surfaced (20 real-bug ledger entries, 5 unresolved sibling-coordination items, integration-suite stability validation gap, anti-drift-effectiveness measurement gap) PLUS the structural test-quality improvements I identified in the post-r1 synopsis (PCF/Code Pages coverage rot, mutation testing pilot, deterministic test data, environment parity) and ships them as a **single project that finishes by 2026-08-31**.

The owner principle (2026-06-01):
> *"single project so that we can address these issues once and for all and not drag it out until September 2026."*

This document operationalizes that principle.

---

## 2. Embedded context — the post-r1 synopsis

This synopsis was authored 2026-06-01 at the close of r1. It's the starting state for r2.

### 2.1 What r1 fixed

| Area | Fix | Outcome |
|---|---|---|
| Unit test failures | All 342 cleared via factory-config keys (task 018), Communications ISP mock swap (task 011), assertion drift (tasks 030+050+054+060+061+071+072), trait tagging | `Failed: 0` |
| Integration test failures | All 198 cleared via `IntegrationTestFixture` Cosmos key (task 062) + 8 sibling-fixture additive edits (task 027) + Workspace fixture (task 060) | `Failed: 0` |
| Compile health | `Spe.Integration.Tests` 4 × CS1739 in `ExternalAccessIntegrationTests.cs` (task 024); unit project was already clean at r1 start | 0 errors both projects |
| AsyncEnumerable helper gap | Hand-rolled `Mocks/AsyncEnumerableHelpers.cs` + `FakeChatClient` per D-01 BUILD LOCAL verdict (no `Microsoft.Extensions.AI.Testing` package exists) | Available for future streaming tests |
| CI gate | `enforce_admins: true` + `skip-tests` removed + emergency procedure + `sdap-ci.yml` duplicate-YAML-key fix | Gate now operationally blocks |
| Anti-drift governance | `.claude/constraints/bff-extensions.md` § F + PR template + procedure doc + root `CLAUDE.md` §10 bullet 6 | 3 mechanisms codified |

**r1 reduction**: 540 → 0 Failed (−100%). Build clean. Triple-run validated stable (unit only).

### 2.2 What r1 did NOT fix — the 20 real-bug ledger entries

These are the production bugs r1's test suite revealed. Tests are currently Skip + `[Trait("status","real-bug-pending-fix")]` with documented fix-by dates. **r2 fixes the production code; tests then flip from Skip → `repaired`.**

| ID | Severity | Bug | Current fix-by | r2 phase |
|---|---|---|---|---|
| **RB-T044-01** | **HIGH** 🔥 | `ConversationHistorySanitizer.StripRetrievedContent` `fromTurnIndex` inverted → **cross-matter privilege leak** (5 tests skipped) | 2026-07-31 | Phase 1 (security) |
| **RB-T028-03** | **HIGH** | Endpoint metadata aborts: `INotificationService` registered conditionally but endpoints map unconditionally (13 KnowledgeBase tests affected) | 2026-07-31 | Phase 1 |
| **RB-T028-04** | **HIGH** | Same root cause as T028-03 (11 ChatEndpoints tests affected) | 2026-07-31 | Phase 1 |
| **RB-T028-05** | **HIGH** | Same root cause as T028-03 (8 ReAnalysisFlow tests affected) | 2026-07-31 | Phase 1 |
| **RB-T028-06** | **HIGH** | Same root cause as T028-03 (5 Auth tests affected) | 2026-07-31 | Phase 1 |
| RB-T044-02 | MED | `CitationExtractor.NormalizeCaseLaw` `TrimEnd('.')` over-strips reporter period (4 InlineData) | 2026-07-31 | Phase 2 |
| RB-T044-04 | MED | `NormalizePatent` EP/WO branches double-prefix country code → `EPEP3456789` (2 InlineData) | 2026-07-31 | Phase 2 |
| RB-T053-01 | MED | `CapabilityRouter` substring-match Layer-1 classifier produces false-positives ("case" → legal_research, "brief" → summarize_content) | 2026-07-31 | Phase 2 |
| RB-T070-03 | MED | `AnalysisChatContextResolver` stub path removed in production; 7 tests assert dead behavior | 2026-09-30 | Phase 2 |
| RB-T028-01 | MED | `AnalysisContextBuilder.BuildContinuationPrompt` `OrderByDescending(m => m.Timestamp)` non-deterministic on tied ticks | 2026-07-31 | Phase 2 |
| RB-T028-02 | MED | `Services.Ai.Insights.Layer2.Layer2OutcomeExtractionTests` (3 tests) — HOLD pending `ai-spaarke-insights-engine-r1` owner sign-off | 2026-09-30 | Phase 1 (sibling resolution) |
| RB-T028-07 | MED | Upload endpoint binding gap (9 tests) | 2026-07-31 | Phase 2 |
| RB-T012-01 | LOW | `SessionRestoreService.NormaliseETag` uses `Trim('"')` which over-strips embedded quotes; `ExtractODataETag` stops at JSON-escaped `\"` | 2026-07-31 | Phase 3 |
| RB-T034-01 | LOW | `AgentConfigurationService.GetExposedPlaybookIdsAsync` doesn't honor `CancellationToken` | 2026-07-31 | Phase 3 |
| RB-T044-03 | LOW | `NormalizeStatute` doesn't trim subsections from canonical key (1 Fact) | 2026-07-31 | Phase 3 |
| RB-T044-05 | LOW | `RegulationPattern` doesn't accept documented `CFR` (no-period) form (1 Fact) | 2026-07-31 | Phase 3 |
| RB-T050-01 | LOW | `SourcePaneSseEventData.CitationId` missing `JsonIgnoreCondition.WhenWritingNull`; emits `citationId: null` instead of omitting | 2026-07-31 | Phase 3 |
| RB-T070-01 | LOW | `AgentConversationService` 3 methods don't honor `CancellationToken` (same pattern as RB-T034-01) | 2026-07-31 | Phase 3 |
| RB-T070-02 | LOW | `R2SseEventEmitter.CapabilityChangePayload.RetryAfterSeconds` not omitted when null | 2026-07-31 | Phase 3 |
| RB-T028-08 | LOW | PrecedentAdmin endpoint binding (1 test) | 2026-09-30 | Phase 3 |

**Summary**: 5 HIGH (4 share root cause T028-03 pattern), 7 MED, 8 LOW. **r2 fixes ALL 20 by 2026-08-31** (one month earlier than the latest fix-by date).

### 2.3 Other items the synopsis flagged

**Near-term (2-4 weeks)**:
1. Land `github-actions-rationalization-r1` (separate project, in flight)
2. Assign owners for the 5 HIGH real-bugs — r2 IS the owner; this is resolved by r2 existing
3. Sibling-owner outreach for FR-04 (Action Engine, Insights, Communications owners)
4. Insights Layer 2 HOLD resolution (RB-T028-02)

**Mid-term (1-3 months)**:
5. Production fixes for all 20 real-bugs by their documented fix-by dates
6. PCF + Code Pages test coverage (different stack, similar rot risk)
7. `Spe.Integration.Tests` runtime stability — triple-run was unit only; integration suite needs the same
8. Test-update-obligation effectiveness measurement (anti-drift mechanisms exist but aren't measured)

**Long-term (3-12 months)**:
9. CI-side coverage gating (Codecov / Coverlet thresholds tied to required-status-checks)
10. Reduce integration-test surface (some likely duplicate unit coverage)
11. Mutation testing pilot for high-value paths (Stryker.NET on `Services/Ai/Safety/*`)
12. Test environment parity (ephemeral-Azure-resources test tier, run nightly not per-PR)
13. Deterministic test data migration (`TestClock` + seeded `Guid` provider)

**r2 absorbs items 3-8 in full and pilots items 9-13** (proof-of-concept depth, not full implementation) so the long-term direction is set without dragging the project envelope.

---

## 3. Project goals

### 3.1 Primary outcome (must ship)

Every test that is currently `Skip` + `real-bug-pending-fix` in the predecessor's `ledgers/real-bug-ledger.md` is **either**:
- Flipped to `[Trait("status","repaired")]` (production code fixed; test now passes), OR
- Re-classified to a long-term ticket with explicit owner + acceptance criteria (if production fix is genuinely out of scope for a 3-month project)

The 4-month cliff (September 2026) is closed: zero entries left at the LATEST fix-by date.

### 3.2 Secondary outcomes (ship in r2)

- Insights Layer 2 (RB-T028-02) is resolved with the sibling project owner; HOLD lifted
- `Spe.Integration.Tests` runtime stability validated via the same triple-run + flake-quarantine protocol r1 used for unit tests
- Test-update-obligation effectiveness measured: from r1 close (2026-06-01) to r2 close, what % of BFF-touching PRs had the obligation checkbox AND a corresponding test addition/update? Report the number; adjust mechanisms if rubber-stamping is detected
- Sibling-project sign-offs completed: `priority-order.md`'s TBD slots populated for Action Engine, Insights, Communications owners

### 3.3 Forward-looking outcomes (pilot in r2; full scope in r3+)

- **PCF/Code Pages test rot audit** — Phase 4 audits the client-side test surfaces using the same factory-config / sibling-fixture / endpoint-registration patterns that r1 surfaced. Produces a delta report + a fix-vs-defer recommendation. Does NOT execute the fixes (those are r3).
- **Mutation testing pilot** — Phase 4 runs Stryker.NET against `Services/Ai/Safety/*` (where the HIGH severity privilege leak originated). Produces a mutation-score baseline + a list of top 10 weak assertions. Does NOT remediate (those are r3).
- **Deterministic test data pattern** — Phase 4 adopts `TestClock` + seeded `Guid` provider in ONE high-leverage subsystem (Workspace, the largest pure-logic surface) as proof-of-concept. Does NOT migrate the entire suite (that's r3).
- **CI coverage gating pilot** — Phase 4 enables Coverlet measurement (already partially in `sdap-ci.yml`) and reports baseline % per project. Does NOT add a coverage threshold to required-status-checks yet (waits for `github-actions-rationalization-r1` to land before any new required checks; coordination via that project).

### 3.4 Out of scope (deferred to r3)

- Full PCF/Code Pages test suite repair (r2 audits + recommends only)
- Full mutation testing remediation across all AI services (r2 pilots one area)
- Full deterministic test data migration (r2 proves the pattern in one subsystem)
- Coverage gate as a required status check (r2 measures + reports; r3 enforces, after `github-actions-rationalization-r1` is done)
- New test types (property-based, fuzzing, etc.)
- Integration-test surface reduction (audit yes, reduction no — that's its own project)

---

## 4. Scope

### 4.1 In scope

| # | Item | Sub-items |
|---|---|---|
| A | **20 real-bug production fixes** | All 20 entries from `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md`. Each fix: production code change in `src/` + Skip→Pass transition in tests + ledger entry resolution |
| B | **Sibling-coordination cleanup** | RB-T028-02 (Insights Layer 2 HOLD) resolved + `projects/sdap-bff.api-test-suite-repair/priority-order.md` TBD slots populated |
| C | **Integration suite stability** | Triple-run validation on `Spe.Integration.Tests` (same protocol as r1 task 084 for unit); any flake → quarantine + flaky-ledger entry |
| D | **Anti-drift effectiveness measurement** | From 2026-06-01 to r2 close: per-PR analysis of BFF-touching PRs; did the test-update obligation get followed? Produce a quarterly compliance report |
| E | **PCF/Code Pages test rot audit** | Read-only audit of `tests/unit/*` and `src/client/pcf/**`, `src/client/code-pages/**`. Delta report + recommendation; NO fixes applied |
| F | **Mutation testing pilot** | Stryker.NET run against `Services/Ai/Safety/*`. Score + top-10 weak assertions list. NO remediation |
| G | **Deterministic test data PoC** | Adopt `TestClock` + seeded `Guid` provider in `Services/Workspace/*` test surface. Other surfaces unchanged |
| H | **CI coverage measurement** | Enable Coverlet output in `sdap-ci.yml`; baseline % per project measured + published in r2 final ledger. NO threshold enforcement |
| I | **Real-bug ledger continuation pattern** | Document the lifecycle (open → assigned → in-progress → repaired → closed) and which artifacts move when |

### 4.2 Out of scope

Same exclusions as §3.4. Also:
- Anything covered by `github-actions-rationalization-r1` (don't duplicate work)
- Anything in the `sdap-bff-api-remediation-fix` predecessor's domain (Phase 4 facade — already shipped)
- New features (this is a quality project, not feature work)

### 4.3 Affected areas

- **`src/server/api/Sprk.Bff.Api/`** — production code; 20+ files touched for real-bug fixes
- `tests/unit/Sprk.Bff.Api.Tests/` — Skip→Pass transitions; new TestClock/Guid PoC for Workspace tests
- `tests/integration/Spe.Integration.Tests/` — triple-run validation; potentially quarantine entries
- `src/client/pcf/`, `src/client/code-pages/`, `src/client/shared/` — read-only for the PCF audit (item E)
- `projects/sdap-bff.api-test-suite-repair/ledgers/*` — entries transition; r2's own ledgers continue
- `projects/sdap.bff.api-test-suite-repair-r2/` — this project's artifacts
- `.github/workflows/sdap-ci.yml` — Coverlet measurement enable (item H; minimal change)
- `docs/procedures/testing-and-code-quality.md` — updated with TestClock/Guid pattern + lifecycle documentation (item I)

---

## 5. Phased delivery — 3-month timeline

### Phase 0 — Project setup + baseline (1 week; 2026-06-01 → 2026-06-08)

- Capture r1 close-out state as r2 baseline (real-bug count, ledger entries, current test suite state)
- Confirm all 20 real-bug entries are reproducible (re-run the Skipped tests; confirm Skip is still appropriate; no regression-disguised-as-Skip)
- Author project artifacts (this design.md → spec.md → CLAUDE.md → ~30-40 task POMLs)
- Sibling-owner outreach: send the FR-04 priority-order.md sign-off requests to Action Engine, Insights, Communications owners. Capture responses in `decisions/`
- Confirm `github-actions-rationalization-r1` status — if Phase 1 of r1-followup is done, coordinate the CI changes that this project requires; if not, sequence around it

**Phase 0 exit**: all 20 entries verified reproducible; sibling outreach sent; artifacts in place

### Phase 1 — HIGH severity + Insights HOLD (3 weeks; 2026-06-09 → 2026-06-29)

Concrete work, by ledger entry:

1. **RB-T044-01 (HIGH security)** — `ConversationHistorySanitizer.StripRetrievedContent` fix. Production change: invert the `fromTurnIndex` slicing logic per the test's documented contract. Code review with security lens (this is a privilege-leak fix). Add a regression test that specifically exercises cross-matter scenarios beyond what the 5 currently-Skipped tests cover.

2. **RB-T028-03/04/05/06 (HIGH × 4, shared root cause)** — Endpoint metadata generation aborts because services registered conditionally but endpoints map unconditionally. Two-step production fix:
   - **a.** Make service registration unconditional for any service whose endpoint maps unconditionally. Audit pattern: `services.Add{Service}()` is inside a feature-flag `if` block but `MapXXX()` is not.
   - **b.** Alternative for genuinely-conditional services: make the endpoint mapping conditional on the same flag. Pick per-case based on whether the flag is product-meaningful or just an internal kill switch.
   - 37 tests across 4 ledger entries flip Skip → Pass.

3. **RB-T028-02 (Insights Layer 2 HOLD)** — Coordinate with `ai-spaarke-insights-engine-r1` owner. Two outcomes possible:
   - Owner takes the bug (it's their domain) → 3 tests transferred to their backlog; we close T028-02 with cross-reference.
   - We take the bug → production fix here; 3 tests flip Skip → Pass.

**Phase 1 exit**: 5 HIGH entries closed (4 in our code, 1 via sibling transfer or our fix). +37 tests pass.

### Phase 2 — MEDIUM severity (3 weeks; 2026-06-30 → 2026-07-20)

7 entries: RB-T044-02, RB-T044-04, RB-T053-01, RB-T070-03, RB-T028-01, RB-T028-07, plus T028-02 if not already done in Phase 1.

Each is a focused production fix in the noted code path:

1. **RB-T044-02**: `CitationExtractor.NormalizeCaseLaw` — change `TrimEnd('.')` to a more precise regex that preserves the reporter period in canonical citations.

2. **RB-T044-04**: `NormalizePatent` EP/WO branches — remove the second prefix application.

3. **RB-T053-01**: `CapabilityRouter` Layer-1 classifier — three options ranked in the ledger (word-boundary regex / negative-evidence scoring / confidence-saturation guard). Owner decision required at Phase 2 start.

4. **RB-T070-03**: `AnalysisChatContextResolver` — either restore the stub path (if the tests' contract is current) OR delete the 7 tests as dead-target. Owner decision.

5. **RB-T028-01**: `AnalysisContextBuilder.BuildContinuationPrompt` — add `.ThenByDescending` tiebreaker OR switch to `TakeLast(N)` directly.

6. **RB-T028-07**: Upload endpoint binding — same DI/binding pattern as the HIGH cluster; may already be resolved by Phase 1 work.

**Phase 2 exit**: 7 MED entries closed. +30-40 tests pass (cumulative ~70 since Phase 1 start).

### Phase 3 — LOW severity + integration stability (2 weeks; 2026-07-21 → 2026-08-03)

8 LOW entries: RB-T012-01, RB-T034-01, RB-T044-03, RB-T044-05, RB-T050-01, RB-T070-01, RB-T070-02, RB-T028-08.

Each is small (mostly 1-line production fixes); batch-execution feasible (4 in parallel waves of 2 each).

Also in this phase:
- **Integration suite triple-run** — same protocol as r1 task 084 for unit. Captures `Spe.Integration.Tests` stability across 3 consecutive full runs.
- Any flake surfaces → `flaky-quarantined` Skip + ledger entry. Target ≤2 flakes (anything more triggers a Phase 4 audit task).

**Phase 3 exit**: all 20 real-bug entries closed. Integration stability validated.

### Phase 4 — Quality lift + audits + pilots (3 weeks; 2026-08-04 → 2026-08-24)

Parallel work tracks (sub-team or one focused agent per track):

- **Track A: PCF/Code Pages test rot audit** (item E) — apply r1's diagnostic playbook (factory config keys, sibling fixtures, endpoint vs service registration alignment) to `src/client/pcf/*` and `src/client/code-pages/*`. Output: `audits/pcf-codepages-test-rot-2026-08-XX.md` with per-control disposition + r3 scope recommendation.

- **Track B: Mutation testing pilot** (item F) — Stryker.NET against `Services/Ai/Safety/*`. Output: baseline mutation score, top-10 weak assertions, recommendation on whether to expand to `Services/Ai/Capabilities/*` and `Services/Ai/Chat/*` in r3.

- **Track C: TestClock/Guid PoC** (item G) — adopt the pattern in `Services/Workspace/*` test surface. Document the pattern in `docs/procedures/testing-and-code-quality.md`. Output: PoC working; pattern documentation; r3 migration plan.

- **Track D: Coverage measurement** (item H) — enable Coverlet output in `sdap-ci.yml`; capture baseline % per project; publish in r2 exit ledger.

- **Track E: Anti-drift effectiveness report** (item D) — analyze BFF-touching PRs from 2026-06-01 → 2026-08-15. Per PR: was the test-update checkbox checked? Were tests actually added/updated? Was the reviewer comment present? Report compliance rate. If <80%, propose corrective action.

**Phase 4 exit**: 5 tracks each produce a deliverable; 0 of them ship as required-status-checks (those wait for `github-actions-rationalization-r1` to land first).

### Phase 5 — Governance + close (1 week; 2026-08-25 → 2026-08-31)

- Update `docs/procedures/testing-and-code-quality.md` with: TestClock/Guid pattern, ledger lifecycle (item I), measurement findings from Track E
- Update `.claude/constraints/bff-extensions.md` § F if any HIGH-severity finding warrants a new binding rule (e.g., "MUST register services unconditionally when endpoints are unconditionally mapped" — directly addresses the RB-T028-03 root cause)
- Triple-run final validation on both suites (mirrors r1 task 084)
- Final ledgers: r2's own repair-ledger, exit-ledger; r1's real-bug-ledger annotated with which entries r2 closed and which were transferred
- PR + admin-merge cycle (mirrors r1's close)
- Lessons-learned authored
- /merge-to-master invocation

**Project end**: 2026-08-31. Target: zero real-bug entries with fix-by dates ≤ 2026-09-30.

---

## 6. Locked Decisions

### D-01: NFR-01 is RELAXED for r2 — production code IS in scope
The r1 NFR-01 (no `src/` changes) was load-bearing for r1's scope (test-only repair). r2's purpose is fixing the production bugs r1 surfaced. Production changes are explicitly in scope under careful review (Step 9.5 quality gates: code-review + adr-check per FULL rigor, plus security review for HIGH severity). The opposite NFR (NFR-01') applies: **MUST NOT** modify tests beyond the Skip→Pass transitions for the resolved real-bug entries; **MUST NOT** start a new repair-not-rewrite cycle on tests.

### D-02: One production fix = one ledger entry closed = one or more tests flipped
Each production change MUST close a specific ledger entry. No grab-bag commits. This makes per-fix attribution clean and lets owner audit progress. Exception: the RB-T028-03/04/05/06 cluster shares root cause — ONE production fix may close all 4 entries simultaneously, captured in one commit message.

### D-03: HIGH severity gets security review; MED gets code review; LOW gets standard review
- HIGH severity (RB-T044-01 + the 4 RB-T028 cluster) — production fix PRs go to owner + a security-aware reviewer. Code-review skill + adr-check + manual security pass.
- MED and LOW — standard FULL rigor (code-review + adr-check at Step 9.5).
- No bypassing review for "obvious" fixes — RB-T044-01 was an inverted index that "looked obvious" but cascaded into cross-matter leak.

### D-04: Phase 4 tracks are pilot-grade; full execution is r3 scope
The PCF audit, mutation testing, TestClock PoC, and coverage measurement are PROOFS of approach in r2. r2 ships recommendations, not full implementations. This protects the 2026-08-31 end date; r3 scopes are derived from r2's audit findings.

### D-05: Real-bug ledger is the source of truth for what closes when
Each ledger entry's `Status` field transitions: `open` → `assigned-to-r2` → `in-progress` → `repaired` → `closed`. Task POMLs reference ledger entry IDs explicitly. TASK-INDEX shows ledger-entry-closed count alongside task-complete count. This makes auditability cheap.

### D-06: r3 is NOT planned (resolved 2026-06-01)
**Updated 2026-06-01**: r3 is **NOT planned**. r2 is the comprehensive closure project — all 20 real-bug ledger entries are resolved here. Phase 4 forward-looking pilots (PCF audit, mutation testing, TestClock PoC, Coverlet baseline, anti-drift effectiveness) produce recommendations that inform future quality investments **without a formal r3 project budgeted**. The project is treated as an urgent BFF-development blocker; no delays. Phase 4 pilot recommendations may be picked up by ad-hoc future work as priorities allow, but no r3 follow-on is scoped or scheduled.

**Original (2026-06-01 pre-resolution)**: "The 2026-08-31 close lands a measured + actionable r2. Decisions on r3 (PCF coverage repair, mutation remediation, TestClock migration, coverage gate enforcement) happen at r2 close based on r2's audit findings. Owner makes the r3-or-not decision then." — superseded by the resolution above.

---

## 7. Binding Rules (NFRs)

- **NFR-01 (r2)**: Production code changes ARE in scope; tests are NOT (apart from Skip→Pass transitions for resolved ledger entries)
- **NFR-02**: Each production code change <50% line replacement per file; >50% requires escalation OR delete-and-rewrite with explicit decision record
- **NFR-03**: HIGH severity ledger entries require security review approval in the PR; cannot merge without it
- **NFR-04**: Every ledger entry closure includes a commit message citing the entry ID + the resolution mode (`repaired` / `transferred-to-sibling` / `archived-as-dead-target`)
- **NFR-05**: Triple-run validation (mirrors r1 task 084) is mandatory before Phase 3 exit
- **NFR-06**: Each phase produces a delta artifact in `baseline/` so progress is reproducible
- **NFR-07**: Anti-drift effectiveness report (Track E) is published whether or not its findings are favorable — no burying inconvenient data
- **NFR-08**: Project CLAUDE.md is loaded by every task agent (predecessor pattern)
- **NFR-09**: Task POMLs declare `<repair-not-rewrite>true</repair-not-rewrite>` for test changes; `<production-fix-per-ledger>true</production-fix-per-ledger>` for production changes (NEW metadata field for r2)

---

## 8. Risks

| Risk | Severity | Mitigation |
|---|---|---|
| HIGH severity privilege-leak fix (RB-T044-01) introduces a new bug | HIGH | Mandatory security review; regression-test additions; deploy to dev environment + manual cross-matter scenario test before merge |
| RB-T028-03/04/05/06 root-cause fix breaks other endpoint registrations | HIGH | Per-fix triple-run; staged rollout (one feature flag at a time); automated rollback via revert PR if any post-merge regression |
| Mutation testing finds 50+ weak assertions in `Services/Ai/Safety/*` — too many for r2 | MED | D-04 explicitly limits r2 to scoring + top-10 list; remediation defers to r3 |
| PCF audit reveals worse rot than BFF (the hypothesis exists) | MED | r2's audit IS the deliverable; r3 scope derives from the findings; doesn't expand r2 |
| Sibling project blocks RB-T028-02 transfer | LOW | Either we take the bug ourselves OR archive the 3 tests as `archived-pending-sibling-engagement` |
| Triple-run on `Spe.Integration.Tests` surfaces >2 flakes | LOW | Phase 4 audit task added; doesn't block Phase 3 close |
| Anti-drift effectiveness report shows <80% compliance | MED | NFR-07 requires publishing anyway; report includes corrective-action proposals |
| 2026-08-31 deadline slips | MED | Each phase has a hard end date; if a phase goes 1 week over, the next phase is descoped by an equal amount; Phase 4 tracks are independent and can drop tracks individually |

---

## 9. Success Criteria (preview — formalized in spec.md)

1. ✅ All 20 real-bug ledger entries closed (`repaired` / `transferred-to-sibling` / `archived-as-dead-target`)
2. ✅ Zero ledger entries with fix-by dates ≤ 2026-09-30 remain open
3. ✅ RB-T028-02 Insights Layer 2 HOLD resolved (one of three paths)
4. ✅ `Spe.Integration.Tests` triple-run shows ≤2 flakes; flakes quarantined + ledgered
5. ✅ Anti-drift effectiveness report published (whatever the finding)
6. ✅ `priority-order.md` TBD sibling sign-off slots populated
7. ✅ PCF/Code Pages test rot audit document published with r3 recommendation
8. ✅ Mutation testing pilot report published with top-10 weak assertions
9. ✅ TestClock/Guid PoC working in `Services/Workspace/*` tests; pattern documented
10. ✅ Coverlet baseline % published per project
11. ✅ Project closes by 2026-08-31
12. ✅ Both test projects: `Failed: 0` post-r2 (continuous from r1 close)

---

## 10. Reference context

Files the r2 agent MUST read before starting work:

### r1 predecessor artifacts (in r1's project directory)

- `projects/sdap-bff.api-test-suite-repair/CLAUDE.md` — binding rules
- `projects/sdap-bff.api-test-suite-repair/spec.md` — what r1 set out to do
- `projects/sdap-bff.api-test-suite-repair/design.md` — locked decisions D-01..D-06 (some inherited)
- `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` — the 20 entries r2 closes
- `projects/sdap-bff.api-test-suite-repair/ledgers/exit-ledger.md` — r1's authoritative close
- `projects/sdap-bff.api-test-suite-repair/ledgers/repair-ledger.md` — ~478 tests r1 repaired; useful for understanding what's already touched
- `projects/sdap-bff.api-test-suite-repair/notes/lessons-learned.md` — what worked / what didn't
- `projects/sdap-bff.api-test-suite-repair/baseline/post-phase23-authoritative-2026-05-31.md` — authoritative measurement at r1 close
- `projects/sdap-bff.api-test-suite-repair/decisions/D-01..D-06.md` — locked decisions r1 captured

### Repository-level context (always-load)

- Root `CLAUDE.md` — including §10 BFF Hygiene (now extended with bullet 6 on test-update obligation)
- `.claude/constraints/bff-extensions.md` § F — codified test-update obligation
- `docs/procedures/testing-and-code-quality.md` — updated procedure including Per-PR reviewer checklist
- `.github/pull_request_template.md` — test-update obligation question

### ADRs applicable to r2 (production-side work)

- ADR-001 (Minimal API) — endpoint patterns
- ADR-007 (SpeFileStore facade) — file operations
- ADR-008 (endpoint filters) — authorization
- ADR-010 (DI minimalism) — service registration discipline (DIRECTLY relevant to RB-T028-03/04/05/06)
- ADR-013 refined (AI extends BFF) — AI service patterns
- ADR-018 (kill switches) — feature flag handling (DIRECTLY relevant to the conditional-registration root cause)
- ADR-028 (Spaarke Auth v2) — auth patterns
- ADR-029 (BFF Publish Hygiene) — publish size impact

### Sibling project coordination

- `projects/ai-spaarke-action-engine-r1/` (if exists) — Action Engine
- `projects/ai-spaarke-insights-engine-r1/` (if exists) — Insights Engine (for RB-T028-02 HOLD resolution)
- `projects/x-email-communication-solution-r2/` (if exists) — Communications
- `projects/github-actions-rationalization-r1/` — coordinate any CI workflow changes through this project

### Existing patterns to follow

- r1's task-execute protocol invocation (every task uses the skill; not freestyle)
- r1's wave-based parallel dispatch (6-agent cap; disjoint file sets)
- r1's ledger lifecycle (entries transition with documented state)
- r1's commit message style (clear scope; ledger entry IDs referenced)
- r1's triple-run validation pattern (Phase 5 for r2)

---

## 11. Resolved Questions (2026-06-01)

(All 5 open questions resolved by owner; integrated into spec.md "Owner Clarifications" + this design's locked decisions.)

1. **r3 commitment**: **NO — r3 is NOT planned.** r2 is the comprehensive closure. See D-06 (updated 2026-06-01).
2. **Security reviewer for HIGH severity**: `dev@spaarke.com`. Resolves NFR-03 for task 010 (RB-T044-01) merge gate.
3. **Insights Layer 2 owner identity**: `dev@spaarke.com`. Task 002 outreach + task 012 follow-up go to this contact.
4. **Phase 4 staffing**: Parallel — 5 tracks in 1 wave, 5 agents simultaneous. TASK-INDEX P4-W1 plan is correct.
5. **CI gate sequencing**: `github-actions-rationalization-r1` Phase 1 is complete or imminent (lands before 2026-08-04). Phase 4 Track D (Coverlet) runs as planned in Phase 4; no slip to Phase 5 expected.

---

## 12. Status indicators

The lifecycle marker progresses through:
- 🟡 Design (this state)
- 🟢 Spec ready
- 🔄 In execution (Phase X / Y)
- ✅ Complete

This document moves through these states as the pipeline advances. The status indicator at the top of this file is the authoritative source.

---

*Authored 2026-06-01 by Claude during the close-out of the predecessor r1 project. Inherits the predecessor's lessons-learned, the synopsis from the post-r1 conversation, and the 20 real-bug ledger entries the predecessor produced. Ready for owner review.*
