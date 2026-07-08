# FR/NFR Acceptance Reconciliation — spaarke-ai-architecture-redesign-r1

> Produced at task 090 wrap-up, 2026-07-08. Sources: `spec.md` (42 FR + 11 NFR) × `tasks/TASK-INDEX.md` (51 task annotations).
> **Result: every FR and NFR has at least one completed task mapping — no requirement has zero coverage.** Six flagged items below, all operator-ruled or carried as deferrals.

## Functional Requirements

| Requirement | Task(s) | Status | Evidence |
|---|---|---|---|
| FR-P0-01 | 001 | ✅ | Ledger round-trip incl. file refs; 5 new + 815 existing tests green |
| FR-P0-02 | 002 | ✅ | Digest generalized to outputs; 22 targeted + 768 ns tests green |
| FR-P0-03 | 003, 004 | ✅ | 3 tables extended on spaarkedev1; ConsumerRoutingService returns full Binding contract |
| FR-P0-04 | 005 | ✅ | 6 drift classes → Unhealthy at startup; 13 tests |
| FR-P0-05 | 006 | ✅ | FinanceModule exit + 3 Null peers + compound gate; 12 gating tests |
| FR-P0-06 | 007 | ✅ | ICodedWorkflow assembly-scan; DailyBriefing retrofit, narrator tests unchanged |
| FR-P0-07 | 008, 009 | ✅ | dataverse.* READ+WRITE handlers, GA-frozen names, 6↔6 bijection, user-OBO fail-closed |
| FR-P0-08 | 010 | ✅ | OBO spike documented FAIL-with-path (consent missing); native handlers stay runtime path |
| FR-P0-09 | 011 | ✅ | 34-case/14-family eval scaffold runs in CI |
| FR-P0-10 | 012 | ⚠️ | Audit note produced; found F-1 CRITICAL app-only engine writes (accept-until-cutover) |
| FR-P0-11 | 013 | ✅ | R7 #501 closed w/ absorption map; R4/ActionEngine/insights-r3 triggers re-pointed |
| FR-P1-01 | 020 | ✅ | chat-summarize via catalog; orchestrator dual-path grep-zero (703→405) |
| FR-P1-02 | 021 | ✅ | Store-precedes-render proven; {bindingId}@t{n} addressable |
| FR-P1-03 | 022 | ✅ | Event path: 4 bounds + precondition + M4 gate; classify+summary+chips |
| FR-P1-04 | 023 | ✅ | ONE dispatchConsumer helper; executeSummarizeIntent/intentMatcher grep-zero |
| FR-P1-05 | 024 | ✅ | E-2 adapter — insights run writes addressable SessionOutput |
| FR-P1-06 | 025 | ✅ | r7 closed w/ 4 keepers; linear_dispatch/regex grep-zero (492 lines) |
| FR-P1-07 | 026 | ✅ | UC-A-1 11/11 green; eval-gate merge-blocking; ADR-039 Accepted both copies |
| FR-P2-01 | 030 | ✅ | Budget 8 + pre-filter + citation + ToolChain persist; factory −772 lines |
| FR-P2-02 | 031 | ✅ | ONE pending store; tool-name lists deleted grep-zero; SessionGate markers |
| FR-P2-03 | 032 | ✅ | Missing-args→clarify; capture_mode=modal; mid-elicitation deterministic; 28 tests |
| FR-P2-04 | 033 | ✅ | REF-CHAT + no_match_handler Binding; dispatch_refused counter |
| FR-P2-05 | 034 | ⚠️ | Hard cutover done, intentHint grep-zero — soft-slash determinism PARTIAL (only /summarize bound; operator deferral at gate 038) |
| FR-P2-06 | 035 | ✅ | 4 dispatcher services + PlaybookEmbedding deleted; 27 symbols grep-zero |
| FR-P2-07 | 036 | ✅ | Chat/Tools deleted (11 classes); rerun/refine → typed handlers; bijection holds |
| FR-P2-08 | 037 | ✅ | 55 cases/20 families incl. 5 injection; found+fixed ungated-write defect |
| FR-P3-01 | 040 | ✅ | 8 consumers→Bindings; 3 config keys deleted grep-zero (universal-ingest honest-errors, intentionally not seeded) |
| FR-P3-02 | 041 | ✅ | draft-correspondence catalog + EmailDraftToolHandler; DRAFT-only, communicate-gated |
| FR-P3-03 | 042 | ✅ | create-task writes sprk_event(type=task)+ledger refs; typed-handler confirm-resume live |
| FR-P3-04 | 043 | ✅ | Daily Briefing coded composite; narrate flag deleted grep-zero (live email at 048) |
| FR-P3-05 | 044 | ⚠️ | Engine shells −11,849 lines grep-zero — residual F-1 analysis.rerun app-only leg accept-with-note (operator ruling) |
| FR-P3-06 | 045 | ✅ | ConversationPane 3,172→300 lines; ONE SSE path; −5,300 lines |
| FR-P3-07 | 046 | ✅ | ONE register-context-widgets; ExecutionTraceWidget renders real ToolChain; FieldDelta grep-zero |
| FR-P3-08 | 047 | ✅ | work_product persister → sprk_aitopicregistry → host record, store-precedes-persist |
| FR-P4-01 | 050 | ✅ | Track-B audit 62 rows, ZERO unexplained survivors (G-P4 MET) |
| FR-P4-02 | 051 | ✅ | ONE scope-model-index regenerated; seed round-trip clean |
| FR-P4-03 | 052 | ✅ | Docs → v0.5; ERD replaced; doc-drift-audit CLEAN |
| FR-P4-04 | 053 | ✅ | Canvas −24,942 lines; BA Actions+Bindings editor; jest 103/103 (live walkthrough deferred → G-M) |
| FR-P4-05 | 054 | ✅ | Per-tenant metering meter + KQL pack w/ live dev rollup; 12 tests |
| FR-P4-06 | 055 | ⚠️ | Size+CVE reported, no new HIGH, ceiling OK — net-reduction NOT met (+3.98 MB, G-P4 amber) |
| FR-P4-07 | 090 | ⚠️ | Wrap-up executed 2026-07-08 with G-M DEFERRED-WITH-EVIDENCE (operator ruling; #555) |
| FR-TB-01 | 070–073 (+050 audit) | ✅ | 4 batches grep-zero per batch + green builds; 050 closes audit |

## Non-Functional Requirements

| Requirement | Task(s) | Status | Evidence |
|---|---|---|---|
| NFR-01 publish size | 001, 055, G-P4 | ⚠️ | Ceiling ≤60 MB OK (49.63 MB) — "net reduction" NOT met absolutely (+3.98 MB); operator sign-off at G-P4 |
| NFR-02 eval merge gate | 011, 026, 037 | ✅ | Eval-gate CI merge-blocking from P1; 34→55 cases |
| NFR-03 injection threat model | 034, 037 | ✅ | 5 injection cases, no ungated side effects |
| NFR-04 latency | 030 | ⚠️ | Prompt-cache fingerprint implemented; latency evidence QUALITATIVE only (no recorded p50/p95 vs targets) |
| NFR-05 metering | 054 | ✅ | Counters + App Insights + KQL pack w/ live dev rollup |
| NFR-06 output quality | 026, 037 | ✅ | Schema-conformance + citation-integrity eval assertions |
| NFR-07 data governance | 001, 046 | ✅ | Ledger Tier 3; ToolChain identifiers-only proven at widget bridge |
| NFR-08 grep-zero cutovers | 034/035/036/044/070–073 | ✅ | Every retirement grep-zero with shown output; no compat shims |
| NFR-09 budgets | 022, 030 | ✅ | Per-turn tool cap 8 (CAS) + per-user daily Event budget telemetered |
| NFR-10 /goal wave conditions | all wave headers | ✅ | Every wave pre-authored; gates never wrapped |
| NFR-11 browser rule | 027, 038, 048 | ⚠️ | G-P0..G-P3 operator UAT satisfied; G-M browser walkthrough deferred (#555) |

## Flagged items (all ruled/dispositioned)

1. **FR-P4-07 / 090**: wrap-up completed 2026-07-08; G-M carried as DEFERRED-WITH-EVIDENCE (`notes/g-m-evidence.md`, #555).
2. **Success Criterion 6 / G-M**: operator ruling 2026-07-07 — post-r2 walkthrough.
3. **NFR-01 net reduction**: G-P4 amber (`notes/g-p4-evidence.md` item 6) — operator sign-off requested; recommend accept.
4. **NFR-04 latency**: qualitative evidence only — quantified TTFB measurement is an r2 candidate (r2 design §10).
5. **FR-P2-05 soft-slash determinism**: PARTIAL by operator deferral at gate 038; capability-discovery endpoint filed in DEF-006 bundle (#557).
6. **F-1 residual (FR-P0-10/FR-P3-05)**: `analysis.rerun` app-only engine leg accept-with-note (operator ruling, task 048 rulings set); bounded + FR-P4-01 re-verified.
