# D-10 — Security review approval for RB-T028-03/04/05/06 cluster (task 011)

**Date**: 2026-06-01
**Reviewer**: `dev@spaarke.com` (per NFR-03 binding)
**Status**: **Approved**
**Cleared NFR**: NFR-03 for task 011 / RB-T028-03/04/05/06 cluster (covered under D-02 cluster exception for the shared root cause)

---

## Decision

The task 011 production fix (18-service Null-Object kill-switch migration per ADR-030) is approved for the eventual master merge (Phase 5 task 083 / FR-16). All 4 ledger entries (RB-T028-03/04/05/06) clear NFR-03 under D-03 + D-02 cluster exception.

## Reviewer findings (verbatim)

1. **Auth posture preserved.** Endpoint filters (`.RequireAuthorization()`, `AddAiAuthorizationFilter()`, `AddEndpointFilter<DocumentAuthorizationFilter>()`) fire at the endpoint-filter layer before any handler body — `FeatureDisabledException` only fires inside handler bodies. Unauthorized requests still get 401/403, never 503. The 4 `AuthorizationIntegrationTests` Skip→Pass transitions empirically confirm this on RB-T028-06.

2. **The iterative residual discovery was correct discipline.** Phase 1a inventory → 3 Phase 1c residuals → 2 Step 9.5 latent-bug scan residuals = 5 services that the initial static inventory missed. Each was caught BEFORE merge via the right mechanism (test failure / proactive scan). ADR-030 now codifies the prevention pattern + static-scan recipe so future PRs flush this preemptively. Phase 5 task 081 will codify the PR-review checklist into `docs/procedures/testing-and-code-quality.md`.

3. **B8 `IRagService` refactor is incidental ADR-007 cleanup, not scope creep.** `KnowledgeBaseEndpoints` no longer injects Azure SDK `SearchIndexClient` directly — the 3 affected handlers (`GetIndexHealth`, `GetIndexedDocuments`, `DeleteIndexedDocument`) now delegate to `IRagService` (which has a Null-Object). Code is verbatim move, no behavior change. This was the right moment to do it.

4. **`FeatureDisabledException` cannot be triggered by user input.** The exception is thrown ONLY by Null-Object service implementations, which are registered ONLY when feature flags are off. No user-controlled path can cause it. The 503 response is request-global (not per-request-state) which matches the kill-switch semantic in ADR-018.

5. **ADR-030 is canonical.** The concise `.claude/adr/` + full `docs/adr/` + INDEX.md updates in commit `85258885` are the right governance artifact. The pattern correctly extends ADR-018 (kill-switch outcome) with the composition-layer binding requirement that ADR-018 was missing. The 3 patterns (P1/P2/P3) and the explicit `errorCode` convention (`ai.<feature>.disabled`) give future maintainers a clear playbook.

## Implications

- **Merge gate**: NFR-03 cleared for task 011 cluster (4 ledger entries under D-02 cluster exception). The eventual master merge (task 083, FR-16) is unblocked for this work.
- **Task 013** (Phase 1 P1-S3 exit triple-run validation gate) is unblocked and may dispatch.
- **Phase 2 entry** (P2-W1) is unblocked once task 013 completes — the 7 MEDIUM-severity ledger entries can dispatch in parallel waves.
- **Audit trail**: PR #318 issue comments `4596627823` (security-review request) + `4596658441` (approval reply) capture the full review history.
- **Pattern set**: this is the second HIGH-severity security review under r2 (after D-08 for task 010). Same approval format used. Sets the precedent for the remaining Phase 2/3/4/5 HIGH+MED gates as Phase 2 begins.
- **ADR-030 stabilized**: with the security review approving the pattern's auth posture explicitly, ADR-030 is now production-canonical and may be referenced by future BFF additions per `bff-extensions.md` § F.

## Reference

- Production fix commits (10 total on this branch, 33c5a0ba..b00328be):
  - `d207ae93` — Tier 1 (6 promote-to-unconditional)
  - `1cfac08c` — Tier 2 (7 P3 Null-Objects + FeatureDisabledException + 16 endpoint catches)
  - `5613b8ad` — Tier 3 (unseal SprkChatAgentFactory + PendingPlanManager + B8 IRagService refactor)
  - `d932f355` — Tier 1.5 r1 (ChatContextMappingService residual)
  - `43ca4f9b` — Tier 1.5 r2 (DocxExportService residual)
  - `dbd3888e` — Tier 1.5 r3 (IWorkingDocumentService residual)
  - `08343e32` — Phase 1c (Skip→Pass + ledger + triple-run)
  - `56e74b84` — Tier 1.5 r4 (NullVisualizationService + NullFileIndexingService)
  - `85258885` — ADR-030 promotion (`.claude/adr/` + `docs/adr/` + INDEX.md)
  - `b00328be` — project state files
- Ledger entries (now `repaired`): RB-T028-03, RB-T028-04, RB-T028-05, RB-T028-06 (transitions in commit `08343e32`)
- Task POML: `projects/sdap.bff.api-test-suite-repair-r2/tasks/011-fix-rb-t028-cluster.poml`
- Triple-run report: `projects/sdap.bff.api-test-suite-repair-r2/baseline/per-fix-triple-run-rb-t028-cluster-2026-06-01.md`
- ADR-030 concise: `.claude/adr/ADR-030-bff-nullobject-kill-switch.md`
- ADR-030 full: `docs/adr/ADR-030-bff-nullobject-kill-switch.md`
- Per-service design (D-09): `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-09-nullobject-design.md`
- Initial escalation (E-01): `projects/sdap.bff.api-test-suite-repair-r2/escalations/E-01-rb-t028-cluster-scope-expansion.md`
- PR #318: https://github.com/spaarke-dev/spaarke/pull/318
- Comparable prior approval (task 010): `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-08-security-review-rb-t044-01.md`
