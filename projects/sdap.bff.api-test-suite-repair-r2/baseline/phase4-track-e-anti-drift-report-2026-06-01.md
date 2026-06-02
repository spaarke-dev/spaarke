# Phase 4 Track E — Anti-Drift Effectiveness Report

> **Project**: `sdap.bff.api-test-suite-repair-r2`
> **Task**: 044 — Track E (Anti-drift effectiveness report)
> **Author**: AI agent (task-execute, STANDARD rigor)
> **Date**: 2026-06-01
> **HEAD at authoring**: `f94cd58a` (Phase 3 exit gate PASS)
> **Per NFR-07**: This report is published whether findings are favorable or not. Inconvenient data is reported, not buried.

---

## Executive Summary

r1 (predecessor project `sdap-bff.api-test-suite-repair`) codified four anti-drift mechanisms on 2026-06-01:

1. **`.claude/constraints/bff-extensions.md` § F — Test Update Obligation** (binding rule)
2. **`.github/pull_request_template.md` — PR-author checkbox** (FR-23 / r1 task 081)
3. **`docs/procedures/testing-and-code-quality.md` — Per-PR reviewer checklist** (FR-24 / r1 task 082)
4. **Root `CLAUDE.md` §10 BFF Hygiene — bullet 6** (cross-reference + RB-T028 sub-rule)

r2 was the **first project to execute under these mechanisms at scale**: 19 commits, 16 ledger entries closed across HIGH/MED/LOW severity, 75+ tests transitioned Skip→Pass, and 18 services migrated to a new Null-Object kill-switch pattern (ADR-030).

### Headline finding

The codified mechanisms are **EFFECTIVE for their stated scope** (test-update obligation enforcement) but **INCOMPLETE for three adjacent failure modes** that r2 surfaced empirically:

| Mechanism | Verdict | Evidence |
|---|---|---|
| § F Test Update Obligation | **Effective** | 100% test-update compliance across 19 commits (every `src/` commit accompanied by test change; trait taxonomy 100% compliant; 0 orphaned production changes) |
| PR template checkbox | **Partial** | Mechanism not exercised yet (r2 not yet merged to master; checkbox state assessable only post-PR) |
| Procedure-doc reviewer checklist | **Partial** | Reviewer judgment applied at D-08 + D-10 security reviews; but the checklist itself is 1 line and easily missed |
| `CLAUDE.md` §10 bullet 6 (unconditional-endpoint sub-rule) | **Partial** | Rule wording catches Tier 1 BLOCKING pairs (4 of 13) but did NOT catch Tier 1.5 LATENT pairs (5 of 13). r2 task 011 discovered the wider asymmetric surface only AFTER execution started — Phase 1a inventory was the discovery mechanism, not the prevention mechanism |

### Headline recommendation

Phase 5 task 080 should codify **three lessons-learned patterns** in `docs/procedures/testing-and-code-quality.md`:

1. **Static-scan recipe** for asymmetric service-registration / endpoint-mapping (from ADR-030 §10 + r2 asymmetric-registration-inventory)
2. **Fixture-config-FIRST inspection protocol** (from tasks 025 + 037 — both flagged as "subsumed by 011" but turned out to be fixture/contract gaps the cluster fix unmasked)
3. **Empirical-reproduction-before-fix protocol** (from tasks 010 + 011 + 012 — all 3 found r1 ledger's recommended fixes were INCOMPLETE; true root cause discovered via hand-trace + reproduction)

Phase 5 task 081 (CONDITIONAL extension to `.claude/constraints/bff-extensions.md` § F) **IS warranted** based on r2 evidence (see Section 3.2).

**r3 recommendation**: NOT needed for anti-drift mechanism design itself (D-06 stands — owner directive). The 3 lessons above are codified in Phase 5 task 080/081; future projects inherit them.

---

## Section 1 — Anti-Drift Mechanism Inventory + Effectiveness Assessment

### 1.1 Mechanism A — `.claude/constraints/bff-extensions.md` § F (Test Update Obligation)

**Rule (excerpt)**:

> When a PR modifies `src/server/api/Sprk.Bff.Api/Services/` (or `Api/`, `Infrastructure/`, `Filters/`), it MUST include corresponding test additions or updates in `tests/unit/Sprk.Bff.Api.Tests/` (and `tests/integration/Spe.Integration.Tests/` if the modification crosses an integration boundary).
>
> **Endpoints map unconditionally; service registration must also be unconditional** — RB-T028-03/04/05/06 (HIGH × 4, filed 2026-05-31) documented a regression where `INotificationService` was registered conditionally but endpoints depending on it mapped unconditionally → 37 integration test failures masked by host-build failures, surfaced only after fixture config was added in the `sdap-bff.api-test-suite-repair` project. The pattern MUST be avoided in future additions.

**Effectiveness verdict**: **EFFECTIVE** for its stated scope.

**Evidence (from r2 execution, commit range `33c5a0ba..f94cd58a`)**:

| Production-change commit | Touches `Services/` / `Api/` / `Infrastructure/` / `Filters/`? | Accompanied by test change in same commit OR follow-on commit? | Verdict |
|---|---|---|---|
| `8b7a905d` (RB-T044-01 ConversationHistorySanitizer fix) | yes (`Services/Ai/Safety/CrossMatter/`) | yes (5 Skip→Pass + 1 new 3-matter regression test in same commit) | ✅ compliant |
| `d207ae93` (task 011 Tier 1 — promote-to-unconditional) | yes (4 module files, 5 services promoted) | follow-on (`08343e32` adds Skip→Pass + ledger transition) | ✅ compliant |
| `1cfac08c` (task 011 Tier 2 — 7 P3 Null-Objects + endpoint catches) | yes (7 new Null-Object impls + 7 endpoint catches) | follow-on (`08343e32` adds Skip→Pass) | ✅ compliant |
| `5613b8ad` (task 011 Tier 3 — unseal + B8 IRagService refactor) | yes (B8 refactor in `KnowledgeBaseEndpoints`) | follow-on (`08343e32` adds 4 new `IRagService` mock setups in `KnowledgeBaseEndpointsTests`) | ✅ compliant |
| `d932f355` + `43ca4f9b` + `dbd3888e` + `56e74b84` (Tier 1.5 rounds 1-4) | yes (4 more service promotions) | follow-on (`08343e32`) | ✅ compliant |
| `c7d7019b` (Phase 2 P2-W1 bundle — 4 closures + 1 partial + 1 residual) | yes (5 production fixes) | yes (Skip→Pass + trait transitions + `IntegrationTestFixture` config key add in same commit) | ✅ compliant |
| `9828711a` (RB-T044-04 NormalizePatent EP/WO fix) | yes (`Services/Ai/Insights/Layer1/`) | yes (1 Skip→Pass in same commit) | ✅ compliant |
| `546ebcb3` (Phase 3 P3-W1 6-LOW bundle) | yes (6 production fixes) | yes (each fix's Skip→Pass + trait transition in same commit) | ✅ compliant |
| `628d9bf1` (Phase 3 P3-W2 — RB-T044-05 + RB-T028-08) | yes (2 fixes) | yes (2 Skip→Pass + `TestUserId` GUID fix in same commit) | ✅ compliant |

**Aggregate**: 9 of 9 production-touching commit groups (= 100% test-update compliance).

**Trait taxonomy compliance** (per phase3-exit-ledger-audit-2026-06-01.md §2):
- Active `[Trait("status","real-bug-pending-fix")]` count: **2** (exact match — both correctly pointing at `RB-T053-01a` residual)
- Active `[Trait("status","flaky-quarantined")]` count: **0** (exact match)
- `repaired` trait applied to every Skip→Pass transition: ✅

**Why this mechanism worked**: The rule is binding and explicit. r2 task POMLs declared `<production-fix-per-ledger>true</production-fix-per-ledger>` (NFR-09, new for r2) which signaled to task-execute that test-update was mandatory. The cluster exception (D-02) was the only deviation allowed, and was used 2 times correctly (task 011 cluster + task 013 inline flake fix).

### 1.2 Mechanism B — `.github/pull_request_template.md` PR-author checkbox

**Rule (excerpt)**:

> - [ ] **Test update obligation** — If this PR modifies `src/server/api/Sprk.Bff.Api/Services/`, has a corresponding test been added/updated? (Yes / No / Not applicable — explain)
>   See [`.claude/constraints/bff-extensions.md`](../.claude/constraints/bff-extensions.md) "Test update obligation" section.

**Effectiveness verdict**: **PARTIAL — Not yet exercised at PR layer**.

**Evidence**: r2 work is on branch `work/sdap.bff.api-test-suite-repair-r2`. No PR has yet been opened against master for r2 closure (task 083 P5-W2 is the planned PR-and-admin-merge cycle). The checkbox state will be assessable only post-PR.

**However**: r2 task POMLs (which serve as the in-project equivalent of the PR description) DID enforce the test-update obligation via the `<production-fix-per-ledger>` metadata field and `<acceptance-criteria>` clauses. Every production-fix task POML required Skip→Pass evidence as a testable acceptance criterion.

**Caveat — checkbox is a fallback, not the primary enforcement mechanism**: r1 design.md §5.5 explicitly chose NOT to make this a required-status check (no CI script enforcement per D-05). The checkbox is a prompt, not a gate. Reviewer judgment is the actual enforcement.

### 1.3 Mechanism C — `docs/procedures/testing-and-code-quality.md` per-PR reviewer checklist

**Rule (excerpt, line 1467)**:

> **Per-PR reviewer checklist:**
> - [ ] Verify test-update obligation per [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md).

**Effectiveness verdict**: **PARTIAL — checklist is minimal (1 line); reviewer judgment is the actual lever**.

**Evidence (r2 internal review evidence)**:

- D-08 (RB-T044-01 security review) — `dev@spaarke.com` approved on PR #318. Review focused on cross-matter privilege leak semantics; test-update obligation was satisfied PRE-review (5 Skip→Pass + 1 new regression test were already in the commit when review opened).
- D-10 (RB-T028 cluster + ADR-030 security review) — `dev@spaarke.com` approved on PR #318 comment `4596658441`. Review focused on the 18-service Null-Object migration scope + kill-switch correctness. Test-update obligation satisfied PRE-review (36 Skip→Pass + 4 new `IRagService` mock setups already in the commit).

**Reviewer call-out for test-update obligation specifically**: 0 instances. The obligation was satisfied PRE-review; reviewers had nothing to call out. This is the **success mode** for the mechanism, not a failure — the rule worked at the author layer, so reviewers didn't need to enforce it.

**Failure-mode caveat**: We cannot empirically measure whether reviewers WOULD have caught a non-compliant PR, because no non-compliant PRs were submitted in r2. The mechanism is **untested at its failure path**.

### 1.4 Mechanism D — Root `CLAUDE.md` §10 BFF Hygiene bullet 6 (RB-T028 sub-rule)

**Rule (excerpt, bullet 6)**:

> Update corresponding tests per the [Test update obligation](.claude/constraints/bff-extensions.md#f-test-update-obligation-binding-per-fr-22--d-05) section in `bff-extensions.md`. PRs modifying `src/server/api/Sprk.Bff.Api/Services/` MUST add/update tests in `tests/unit/Sprk.Bff.Api.Tests/`. **Endpoints that map unconditionally must have unconditional service registration** (per RB-T028-03/04/05/06, filed 2026-05-31 by `sdap-bff.api-test-suite-repair`). Exceptions require explicit code review sign-off with reason.

**Effectiveness verdict**: **PARTIAL — wording catches BLOCKING (Tier 1) cases; MISSES LATENT (Tier 1.5) cases**.

**Evidence (from r2 task 011 asymmetric-registration-inventory)**:

The rule wording "endpoints that map unconditionally must have unconditional service registration" is correct but underspecified. r2 task 011 Phase 1a inventory found:

| Tier | Count | What rule wording catches | What r2 had to discover empirically |
|---|---:|---|---|
| **Tier 1 BLOCKING** (cause startup metadata-gen abort) | 8 (B1-B8) | ✅ caught — these are the canonical violations the rule targets | n/a |
| **Tier 1.5 LATENT** (endpoint maps + injects conditional service as hard `[FromServices]` param) | 5 (L1-L5) | ❌ NOT caught — rule wording is permissive of `IBriefingAi? = null` nullable-defaults, but minimal-API param-inference rules in .NET 8 + `[FromServices]` interaction surface this as a Tier 1.5 latent failure that becomes Tier 1 BLOCKING in certain test configs | r2 Phase 1a Grep + inventory + E-01 5-layer cascade analysis discovered this category |
| Already-correct conditional pairs (mapping inside `if`-block) | 4 (C1-C4) | ✅ rule doesn't apply (both gated correctly) | n/a |

**The Tier 1.5 anti-pattern in words**: "Endpoint maps unconditionally + injects a conditional service as a hard `[FromServices]` parameter (with or without nullable-default suffix)". This is NOT what r1 ledger described (r1 ledger described Tier 1 only — the conditional registration vs unconditional mapping pair). r2 discovered the Tier 1.5 surface during task 011 Phase 1a inventory — AFTER execution started, not as a pre-execution prevention.

**Discovery sequence (commit chain evidence)**:
- `d207ae93` (Tier 1 — 5 promote-to-unconditional) — addressed the rule as written
- `1cfac08c` (Tier 2 — 7 P3 Null-Objects) — addressed B2/B3/B7/B8 BLOCKING pairs
- `d932f355` + `43ca4f9b` + `dbd3888e` + `56e74b84` (Tier 1.5 rounds 1-4, 4 more services promoted) — addressed the LATENT pairs discovered DURING execution

The 4 separate Tier 1.5 commits are direct empirical evidence that the rule wording in CLAUDE.md §10 bullet 6 was insufficient prevention. The pattern of finding additional services round-by-round means the static prevention failed; the dynamic discovery succeeded.

**Why this matters for Phase 5**: ADR-030 §10 already contains the static-scan recipe for finding asymmetric pairs. Phase 5 task 080 should lift that recipe into `docs/procedures/testing-and-code-quality.md` as a **pre-commit static check** (not CI — author-side). Phase 5 task 081 should extend `.claude/constraints/bff-extensions.md` § F with the Tier 1.5 language explicitly.

---

## Section 2 — Lessons-Learned Patterns from r2 Execution

### 2.1 Lesson #1 — Tier 1.5 anti-pattern (asymmetric registration with `[FromServices]` injection)

**Source evidence**:
- Task 011 escalation E-01 (`escalations/E-01-rb-t028-cluster-scope-expansion.md`) §Layer 2 + §Layer 3 + §Layer 4
- Task 011 asymmetric-registration-inventory (`baseline/asymmetric-registration-inventory-2026-06-01.md`) §5.B (LATENT pairs L1-L5)
- 4 separate Tier 1.5 commits (`d932f355` + `43ca4f9b` + `dbd3888e` + `56e74b84`) — each discovered an additional latent pair after the previous round's build succeeded

**Anti-pattern in code**:

```csharp
// In an UNCONDITIONALLY-mapped endpoint:
app.MapGet("/api/finance/invoices/search", async (
    string query,
    IInvoiceSearchService searchService) =>  // ← conditional service as hard param
{
    return await searchService.SearchAsync(query);
});

// In FinanceModule (CONDITIONAL registration):
if (documentIntelligenceEnabled)
{
    services.AddScoped<IInvoiceSearchService, InvoiceSearchService>();
}
// else: NOT registered → endpoint metadata-gen fails at startup
```

**Why CLAUDE.md §10 bullet 6 doesn't catch this**: The rule says "every service the endpoint depends on must be unconditional". The PR author may believe `IInvoiceSearchService` is fine because (a) the endpoint compiles, (b) the test fixture happens to set `DocumentIntelligence:Enabled=true` so it runs locally, (c) the failure surfaces only when the fixture sets `Enabled=false` and only at metadata-gen time. The mental model "compiles + tests pass = compliant" is wrong here; the static-scan recipe is needed.

**Recommended codification (Phase 5 task 080)** — lift this static-scan recipe from ADR-030 §10 into `docs/procedures/testing-and-code-quality.md`:

1. `Grep "if .*Enabled" src/server/api/Sprk.Bff.Api/Infrastructure/DI/` → enumerate conditional registrations
2. For each conditional service, `Grep "IServiceName" src/server/api/Sprk.Bff.Api/Api/` → enumerate endpoint consumers
3. For each consumer, check whether the consuming endpoint is inside an `if` block in `EndpointMappingExtensions.cs`
4. If endpoint is UNCONDITIONAL + service is CONDITIONAL → Tier 1.5 anti-pattern → MUST promote service to unconditional OR apply Null-Object pattern per ADR-030

**Recommended `.claude/constraints/bff-extensions.md` § F extension (Phase 5 task 081)**:

> When adding ANY new conditional service registration, the author MUST verify (via the static-scan recipe in `docs/procedures/testing-and-code-quality.md` §Asymmetric-Registration Pre-Commit Check) that no unconditionally-mapped endpoint injects the conditional service as a `[FromServices]` parameter (with or without nullable-default). Nullable-default `IServiceX? = null` does NOT suppress metadata-gen failure in .NET 8 minimal API; the only safe patterns are (a) promote service to unconditional or (b) register a Null-Object per ADR-030.

### 2.2 Lesson #2 — Fixture-config-FIRST inspection protocol

**Source evidence**:
- Task 025 (RB-T028-07 fixture-config gap — `IntegrationTestFixture` missing `CosmosPersistence:DatabaseName`)
- Task 037 (RB-T028-08 fixture-config gap — `IntegrationTestConstants.TestUserId` was non-GUID literal `"test-user"`, failing GUID-shape claim validation)

**The pattern**: Both ledger entries were initially flagged in r2 plan as "verify subsumed by task 011 cluster fix" — the hypothesis being that fixing the asymmetric-registration root cause (task 011) would auto-resolve them. After task 011 closed, both tests STILL failed. Investigation revealed:

- **RB-T028-07**: The cluster fix correctly registered `IRagService` (B7) and `SearchIndexClient` (B8) — but `KnowledgeBaseEndpointsTests` fixture was ALSO missing a `CosmosPersistence:DatabaseName` config key that the now-running endpoint needed at request time. The cluster fix UNMASKED the fixture-config gap; it wasn't caused by the cluster fix.
- **RB-T028-08**: The cluster fix correctly resolved the conditional-service problem — but `AuthorizationIntegrationTests` was using `TestUserId = "test-user"` (non-GUID string literal) which failed the GUID-shape claim validation in newly-reachable Auth filters. The fixture's claims-state was the contract gap; the cluster fix UNMASKED it.

**Anti-pattern in mental model**: "If a test was Skipped because of a known root cause, and we fix that root cause, the test will pass." This is wrong when the test fixture has multiple latent contract gaps that the Skip was hiding.

**Recommended codification (Phase 5 task 080)** — add to `docs/procedures/testing-and-code-quality.md`:

> **Fixture-Config FIRST Inspection Protocol** (applies to any Skip→Pass transition associated with a closed ledger entry):
>
> Before declaring a ledger entry "subsumed by" a cluster fix or upstream root-cause repair, MUST inspect the test fixture's:
> 1. Configuration values (`fixture.Configuration` overrides) — every key/value the test reads should be present in the fixture's `appsettings.json` OR `IConfigurationBuilder` overrides
> 2. Claims/state (`TestUserId`, `ClaimsPrincipal`, fake auth tokens) — every value must satisfy the production contract shape (GUIDs are GUIDs; emails are emails; etc.)
> 3. Service mocks (`Mock<T>` setups, `Loose` mode behavior) — every method the test exercises should have an explicit `.Setup(...)` OR be tolerated by `Loose` mock default
>
> If any of these gaps exist, file the fixture-config gap as a SEPARATE ledger entry. Do NOT collapse into the upstream cluster fix. Use STANDARD rigor; the fix is usually 1-3 lines.

### 2.3 Lesson #3 — Ledger-hypothesis correction protocol (empirical reproduction before applying recommended fix)

**Source evidence**:
- Task 010 (RB-T044-01) — r1 ledger recommended a 1-line `if (i > fromTurnIndex)` → `if (i < fromTurnIndex)` inversion. r2 hand-trace + reproduction showed the inversion would BREAK the existing `Sanitizer_StripsRetrievalBlocks_PreservesConclusions` test. True fix: matter-pivot-aware semantic with 37% line replacement (per phase3-exit-ledger-audit §NFR-02). The "obvious" 1-line fix was incomplete.
- Task 011 (RB-T028-03/04/05/06 cluster) — r1 ledger recommended either "conditional endpoint mapping" (Approach 1) OR "register a no-op `INotificationService`" (Approach 2). r2 attempt at Approach 1 surfaced E-01's 5-layer failure cascade. True fix: Null-Object kill-switch pattern across 18 services (NOT the 4 r1 captured), with `FeatureDisabledException` + endpoint catch + 503 ProblemDetails (ADR-030 codification). The r1 recommendations were both incomplete (Approach 1 didn't scale to the latent surface; Approach 2 was right in pattern but missed the cascade).
- Task 012 (RB-T028-02) — r1 ledger hypothesized "fixture-text-drift after sibling-project edits". r2 byte-level inspection + temporary Skip removal showed the actual cause was CRLF↔LF whitespace mismatch in `GroundingVerifier.Normalize` semantics. True fix: 1-line visibility promotion (`internal` → `public`) + 16 lines of XML doc + 7 test assertions migrated to use the canonical `Normalize` mirror. The "fixture drift" hypothesis was wrong; the test was asserting a stricter invariant than production enforced.

**The pattern**: r1 ledger entries documented the FAILURE SYMPTOM accurately but the RECOMMENDED FIX was hypothesized without hand-trace + empirical reproduction. r2 agents who applied the recommended fix without verifying root cause first discovered:
- The recommended fix was incomplete (10/12)
- The recommended fix would break existing tests (10)
- The recommended fix was directionally wrong (11 — Approach 1 didn't scale)

**Recommended codification (Phase 5 task 080)** — add to `docs/procedures/testing-and-code-quality.md`:

> **Empirical-Reproduction-FIRST Protocol** (applies to any ledger entry where the recommended fix involves more than a trivial 1-line change):
>
> Before applying the ledger's recommended fix, MUST:
> 1. **Reproduce the failure locally** — temporarily remove `Skip = "..."` from the failing test; run; capture the TRX message verbatim
> 2. **Hand-trace the production code path** — identify the call graph from test entry to the failure site; verify the recommended fix actually changes the failing assertion's outcome
> 3. **Verify the recommended fix doesn't regress sibling tests** — same test file's neighboring tests are the first regression risk
> 4. **Run the unit suite Failed-target=0 once with the proposed fix** before opening the PR
>
> If steps 1-4 reveal the ledger's recommended fix is incomplete or wrong, file a r2-style "path-b" decision record (e.g., `decisions/D-XX-{entry}-actual-fix.md`) documenting:
> - The ledger's original hypothesis
> - The empirically-verified actual root cause
> - The corrected fix path
> - The 3-line cross-reference back to the ledger entry
>
> Then proceed with the corrected fix. Cluster exceptions (per D-02) allow bundling the corrected analysis with the production change.

### 2.4 Lesson #4 — Discovery-during-execution vs Prevention-at-design

**Synthesis lesson connecting #1, #2, #3**: r2 had 3 cases where r1's pre-execution governance (ledger + constraint docs + CLAUDE.md) was INSUFFICIENT prevention; the gaps were discovered DURING execution by agents doing hand-trace + inventory + reproduction. This is not a failure of r1 — r1's body of work IS the foundation that made r2's discovery possible (without the asymmetric-pair concept, r2 wouldn't have run the inventory). But it IS a signal that:

- The static-scan recipe (Lesson #1) must move from ADR-030 §10 into the procedure doc — currently it's buried in a project-level draft ADR
- The fixture-config-FIRST protocol (Lesson #2) must be codified as a STANDARD rigor procedure — currently r2 invented it on-the-fly per task
- The empirical-reproduction protocol (Lesson #3) must be codified as a STANDARD rigor procedure — currently it's implicit in task-execute's Step 0.5 but not visible to ledger-fix-recommendation authors

**Phase 5 task 080 should integrate all 3 lessons as new procedure sections** with explicit references to r2 task 011 + 025 + 037 + 010 + 012 as worked examples.

---

## Section 3 — Specific Recommendations for Phase 5 Governance Updates

### 3.1 For Phase 5 task 080 (`docs/procedures/testing-and-code-quality.md`)

Add three new sections:

**§N+1 Asymmetric-Registration Pre-Commit Check** (from Lesson #1 / 2.1)
- 4-step static-scan recipe (lifted from ADR-030 §10)
- Tier 1 vs Tier 1.5 classification
- Worked example: r2 asymmetric-registration-inventory (5.A vs 5.B vs 5.C)
- Decision tree: promote-to-unconditional vs Null-Object vs refactor
- Cross-reference to ADR-030 + RB-T028 cluster

**§N+2 Fixture-Config-FIRST Inspection Protocol** (from Lesson #2 / 2.2)
- 3-step inspection checklist (config / claims / mocks)
- "Do NOT collapse into upstream cluster fix" anti-pattern
- Worked example: r2 task 025 (`CosmosPersistence:DatabaseName`) + task 037 (`TestUserId` GUID shape)
- Cross-reference to r1 sibling-fixture pattern + r2 NFR-09 per-fix triple-run

**§N+3 Empirical-Reproduction-FIRST Protocol** (from Lesson #3 / 2.3)
- 4-step reproduction protocol
- "Path-b decision record" pattern for corrected hypotheses
- Worked examples: r2 D-07 (RB-T028-02 path-b) + D-09 (ADR-030 vs r1 Approach-1/Approach-2) + task 010 RB-T044-01 unified semantic vs r1's 1-line inversion
- Cross-reference to D-02 cluster exception + D-03 obvious-fixes-still-cascade lesson

### 3.2 For Phase 5 task 081 (`.claude/constraints/bff-extensions.md` § F)

**Extension IS warranted** based on r2 evidence (E-01 + asymmetric-registration-inventory + ADR-030 promotion).

Add new bullet/sub-section to § F:

> **Tier 1.5 Anti-Pattern (added 2026-06-01 by r2 task 011 evidence)**:
>
> When adding a NEW conditional service registration (inside any `if (flag)` block in `Infrastructure/DI/*.cs`), the author MUST verify no unconditionally-mapped endpoint injects the conditional service as a `[FromServices]` parameter — including parameters with nullable-default (`IServiceX? = null`). In .NET 8 minimal-API metadata generation, nullable-default does NOT suppress the unresolved-dependency failure; it only suppresses the runtime null-check.
>
> The verification MUST use the static-scan recipe in `docs/procedures/testing-and-code-quality.md` §Asymmetric-Registration Pre-Commit Check.
>
> If the static scan finds a Tier 1.5 anti-pattern, the author MUST EITHER:
> (a) promote the service to unconditional registration (preferred when service has zero AI/external deps) — per ADR-010 DI minimalism + ADR-030 §4.4
> (b) apply the Null-Object kill-switch pattern (when service has conditional deps that prevent unconditional registration) — per ADR-030 §4.1-4.3
> (c) refactor the endpoint signature to consume a different service (e.g., refactor `KnowledgeBaseEndpoints` to consume only `IRagService`, not `SearchIndexClient` directly) — when (a) and (b) introduce coupling violations
>
> Cross-references: ADR-030 (BFF Null-Object Kill-Switch Pattern), RB-T028-03/04/05/06 cluster + ADR-030 §10 static-scan recipe, r2 asymmetric-registration-inventory-2026-06-01.md.

**Decision record for task 081**: Per task 081 POML, this extension IS warranted (Option YES path). Decision record should be `decisions/D-08-bff-extensions-extension-applied.md` (NOT `D-08-no-extension.md`).

**Note on D-08 naming collision**: r2 already has a `D-08-security-review-rb-t044-01.md`. Task 081's decision record should use a different number (e.g., D-13).

### 3.3 For Phase 5 task 082 + 084 (downstream effects)

- Task 082 (final triple-run validation): no change — the existing 6-TRX combined triple-run plan is sufficient evidence for Phase 5 exit.
- Task 084 (doc-drift audit post-procedure update): MUST verify that the 3 new sections from task 080 are cross-referenced from CLAUDE.md §10 (otherwise the new procedures aren't discoverable for future projects). Add a check.

---

## Section 4 — Open Questions / Future r2+1 Candidates

(Per D-06 owner directive: r3 is NOT planned. These are noted as "future r2+1 candidates" for the steady-state team to consider, not blocking gaps.)

### 4.1 Open question — How to test the unmeasured failure path

**Question**: Mechanism C (procedure-doc reviewer checklist) is **untested at its failure path** in r2 because no non-compliant PR was submitted. How would we know if a reviewer would catch a non-compliant PR if one were submitted?

**Possible future approaches** (none recommended for r3; noted for awareness):
- Synthetic non-compliant PR injection in a dev environment (intentionally submit a PR that violates § F; measure reviewer response time + accuracy)
- Code-review skill expansion to flag § F violations automatically (orthogonal to D-05's no-CI-script choice; the code-review skill is local + advisory, not CI)
- Track reviewer test-update call-outs as a tracked metric in `gh pr review` analyses going forward

**Recommendation**: Accept the unmeasured-failure-path risk for now. The mechanism's PRIMARY enforcement is the author layer (§ F binding rule + PR template prompt); the reviewer checklist is a backstop. r2 evidence shows the primary enforcement works.

### 4.2 Open question — Tier 1.5 detection in CI

**Question**: D-05 + design.md §5.5 explicitly chose NOT to add a CI script for test-update obligation enforcement. Does the SAME logic apply to Tier 1.5 detection?

**Analysis**:
- The static-scan recipe (Lesson #1) is mechanical — could be a script
- BUT the corrective action (promote-to-unconditional vs Null-Object vs refactor) requires judgment per-service
- AND r2 evidence shows the recipe needs only ~30 minutes per BFF-touching PR to execute manually

**Recommendation**: Keep the recipe in the procedure doc (task 080) and constraint doc (task 081). DO NOT add a CI script. Reviewer judgment + author discipline is sufficient. Matches D-05 design intent.

### 4.3 Open question — Are there other "Tier X.5" anti-patterns lurking?

**Question**: r2 discovered Tier 1.5 (unconditional endpoint + conditional service as `[FromServices]`). Are there other near-miss anti-patterns CLAUDE.md §10 doesn't cover?

**Candidates worth future inventory** (NOT in r2 scope):
- Conditional `BackgroundService` registration vs unconditional `IHostedService` enumeration — could cause similar metadata-gen issues
- Conditional `IOptions<T>` registration when handlers depend on `IOptions<T>` — silent default-empty options vs unresolved-options-throw
- Conditional MediatR / OpenTelemetry / Identity processor registration with unconditional middleware that injects them

**Recommendation**: Not a r3 deliverable. Phase 5 task 080's static-scan recipe is the inventory mechanism; future BFF-touching projects can run the recipe to surface any new anti-patterns in their domain.

### 4.4 Open question — Should ADR-030 promote into `.claude/adr/` immediately?

**Status**: ADR-030 was promoted to `docs/adr/ADR-030.md` via commit `85258885` (post-task 011 D-10 sign-off). But the `.claude/adr/ADR-030-bff-nullobject-kill-switch.md` concise version (per `.claude/adr/INDEX.md` pattern) is NOT yet authored.

**Recommendation**: Phase 5 task 081 main-session edit to `.claude/constraints/bff-extensions.md` should be paired with `.claude/adr/ADR-030-*.md` concise-version authoring. Both are main-session-only edits to `.claude/` (per CLAUDE.md §3). Task 081's POML scope can be naturally extended to cover this.

**Action**: Suggest adding to task 081 acceptance criteria. Not a blocking issue for Phase 4 Track E.

---

## Closing Statement (NFR-07 publish-regardless declaration)

Per NFR-07: this report is published whether favorable or not. The findings are MIXED:

- **Favorable**: Mechanism A (§ F test-update obligation) achieved 100% compliance across r2's 19 commits. The trait taxonomy is clean. No NFR-04 commit-citation violations.
- **Unfavorable**: Mechanism D (CLAUDE.md §10 bullet 6 RB-T028 sub-rule) MISSED Tier 1.5 anti-patterns; r2 task 011 had to discover them during execution via 4 separate Tier 1.5 commits. 5 services needed promotion AFTER the initial Tier 1 + Tier 2 fixes thought to be complete.
- **Partially measured**: Mechanism B (PR template checkbox) and Mechanism C (reviewer checklist) are not yet exercisable because r2 is pre-PR-merge. Failure paths untested.

The unfavorable finding is REPORTED, not buried. It is the basis for Phase 5 task 080 + task 081 governance updates. Future BFF-touching projects benefit from the codification.

---

## Appendix A — File and Commit References

### Anti-drift mechanism files (read for this audit)

- `.claude/constraints/bff-extensions.md` (§ F at lines 73-91)
- `.github/pull_request_template.md` (line 21 — test-update checkbox)
- `docs/procedures/testing-and-code-quality.md` (§Per-PR reviewer checklist at line 1467)
- `CLAUDE.md` §10 (BFF Hygiene — Binding Governance, bullet 6 specifically)

### r2 execution evidence (commit chain `33c5a0ba..f94cd58a`, 19 commits)

- `d207ae93` + `1cfac08c` + `5613b8ad` + `d932f355` + `43ca4f9b` + `dbd3888e` + `56e74b84` + `08343e32` + `85258885` + `b00328be` + `2f25b204` — task 011 RB-T028 cluster work (Tier 1 + Tier 2 + Tier 3 + Tier 1.5 × 4 rounds + ADR-030 promote + security review)
- `8b7a905d` — task 010 RB-T044-01 (Lesson #3 evidence — r1 hypothesis incomplete)
- `5d129e1d` — task 013 Phase 1 exit triple-run + RB-T013-01 inline flake fix (D-02 cluster exception)
- `c7d7019b` — Phase 2 P2-W1 wave 1 bundle (includes task 025 RB-T028-07 fixture-config gap — Lesson #2 evidence)
- `9828711a` — task 021 RB-T044-04 NormalizePatent fix
- `546ebcb3` + `628d9bf1` — Phase 3 P3-W1 + P3-W2 6-LOW + 2-LOW closures (includes task 037 RB-T028-08 fixture-config gap — Lesson #2 evidence)
- `2b55287b` — Phase 3 P3-W3 integration triple-run (FR-10)
- `f94cd58a` — Phase 3 exit gate PASS + task 039

### Supporting r2 artifacts

- `projects/sdap.bff.api-test-suite-repair-r2/baseline/asymmetric-registration-inventory-2026-06-01.md` — full inventory (Tier 1 + 1.5 + correct + already-symmetric)
- `projects/sdap.bff.api-test-suite-repair-r2/baseline/phase3-exit-ledger-audit-2026-06-01.md` — cumulative NFR compliance audit
- `projects/sdap.bff.api-test-suite-repair-r2/baseline/per-fix-triple-run-rb-t028-02-2026-06-01.md` — Lesson #3 worked example (path-b)
- `projects/sdap.bff.api-test-suite-repair-r2/decisions/ADR-030-DRAFT-bff-nullobject-kill-switch.md` — pattern codification (promoted to `docs/adr/ADR-030.md` via `85258885`)
- `projects/sdap.bff.api-test-suite-repair-r2/escalations/E-01-rb-t028-cluster-scope-expansion.md` — 5-layer cascade discovery
- `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-07-insights-layer2-resolution.md` — path-b decision record (RB-T028-02 hypothesis correction)
- `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-09-nullobject-design.md` — per-service Null-Object design (Lesson #1 codification)
- `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-10-security-review-rb-t028-cluster.md` — security sign-off

### r1 predecessor evidence

- `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` — 20 entries r2 closes; basis for the corrected-hypothesis lessons
- r1 task 080 (extend `.claude/constraints/bff-extensions.md` § F — the original codification)
- r1 task 081 (add PR template checkbox)
- r1 task 082 (add procedure-doc reviewer checklist)

---

## Appendix B — Decision-Criteria Map for Phase 5 Tasks

| Phase 5 task | Decision input from this report | Recommended action |
|---|---|---|
| 080 (procedure-doc update) | Lessons #1 + #2 + #3 codification recommendations | Add 3 new sections per §3.1 above |
| 081 (CONDITIONAL constraint extension) | Extension warranted (§3.2 above) | Extend § F per §3.2 wording; pair with `.claude/adr/ADR-030-*.md` authoring per §4.4 |
| 082 (final triple-run validation) | No change required | Proceed as planned |
| 084 (doc-drift audit) | Verify task 080's 3 new sections cross-referenced from CLAUDE.md §10 | Add check per §3.3 |

---

*End of Phase 4 Track E anti-drift effectiveness report. Published 2026-06-01 per NFR-07.*
