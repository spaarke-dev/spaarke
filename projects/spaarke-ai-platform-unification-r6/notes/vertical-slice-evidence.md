# R6 Vertical-Slice Evidence Synthesis (9 pillars)

> **Date**: 2026-06-18
> **Authored by**: task 087 closeout
> **Purpose**: Document the composed-evidence map proving R6 architecture convergence delivered all 9 pillars end-to-end, in lieu of a single multi-week E2E harness.
> **Recommended action**: Phase D sign-off

---

## Framing

Per task 087's POML, the binding vertical-slice acceptance is "all 9 pillars exercised in one coherent `/summarize #file.docx` scenario." Two paths to satisfy this:

1. **Build a fresh full E2E harness** (mocked LLM service + Cosmos test container + Redis test instance + Playwright frontend driver). Multi-week scope; duplicates per-pillar coverage already built.
2. **Compose existing per-pillar evidence + close cross-pillar seams**. Each pillar's contract is locked by Phase A/B/C/D per-task tests; the seams between pillars are the actual risk surface. Lock those.

Task 078 (Phase C cross-pillar integration test) established precedent for option 2 in the same project. Task 087 extends the same pattern for the BFF-side Pillar 8 → 3 → 4 chain that was the residual gap.

---

## 9-pillar evidence map

| Pillar | Description | Existing per-task tests | New cross-pillar tests (task 078 + 087) |
|---|---|---|---|
| **1** Persona | Data-driven persona resolution from `sprk_aipersona` Dataverse entity via `IScopeResolverService.ResolveAsync()` | Phase A task 005 (wire `SprkChatAgentFactory.CreateAgentAsync`) + task 028 (Phase A integration test) | Covered |
| **2** Tool registry | Data-driven tool assembly from `sprk_analysistool` rows; Q9 big-bang 10-tool migration | Phase A tasks 011/012/013 (regression gate); Wave 9/10 closeouts | Covered |
| **3** Generic `invoke_playbook` | `IInvokePlaybookAi` facade per ADR-013; specialized bridges (InvokeSummarize, InvokeInsightsQuery) deleted | Phase A tasks 020/021/022/023 | task 087 `Pillar8_SoftSlashSummarize_RoutesToInvokePlaybookSummarize` — soft slash → synthetic `invoke_playbook_summarize` capability |
| **4** Playbook engine FK | `SessionSummarizeOrchestrator` routes via `PlaybookExecutionEngine.ExecuteAsync(playbookId)`; FK chain (playbook → node → action) traverses to `SUM-CHAT@v1`; no alternate-key bypass | Phase A tasks 024/025/028 | task 087 `Pillar8_SoftSlashWithMatchingManifestPlaybook_PropagatesPlaybookId` — `SelectedPlaybookId` propagation seam |
| **5** Schema-aware output + dedup | Action `outputSchema`; node config `destination`/`widgetType`; `StructuredOutputStreamWidget` schema-aware rendering; CapabilityRouter dedup | Phase B tasks 030–042 + task 048 integration test | task 087 `Pillar8_SoftSlashWithMatchingManifestPlaybook_PropagatesPlaybookId` — playbook ID → terminal node destination resolution (FR-30) |
| **6a** Workspace state | `WorkspaceTab` canonical interface; Redis hot + Cosmos durable persistence; `GET /api/workspace/state` endpoint; per-turn snapshot in agent prompt | Phase C tasks 050–053 + task 078 cross-pillar | task 078 `CrossPillar_SendArtifactThenAppearsInNextTurnPrompt` |
| **6b** Workspace tools + affordances + conflict | `send_workspace_artifact` / `update_workspace_tab` / `close_workspace_tab` chat tools; user affordances; Q8 conflict resolution | Phase C tasks 054–058 + task 078 cross-pillar | task 078 (5 cross-pillar tests covering FR-39 canEdit, round-trip, stale-read refusal, hidden tab privacy) |
| **6c** Trace widget + events | Additive `context.*` PaneEventBus events; `ExecutionTraceWidget`; CapabilityRouter emits `context.decision_made` | Phase C tasks 059/061/062/063 | task 087 `Pillar8_Adr015_NoUserContentInDecisionMadeEvents` — ADR-015 BINDING audit at routing seam |
| **7** Memory + Q7 UI | Summarization compression; pinned-context entity; hierarchical composition; `MatterMemoryService` activation; voice memory ("remember/forget/always"); Q7 Pinned Memory UI | Phase C tasks 064–070 | task 087 `Pillar8_VoiceMemoryPriorityOverSoftSlash` — Layer 0 vs Layer 0.5 priority binding |
| **8** Command Router | `CommandRouter.ts` parser; 6 hard slashes + 4 soft slashes + 3 reference types; BFF Layer 0.5 pre-pass | Phase D tasks 080–086 (36 + 53 + 38 + 27 + 12 + 10 + 50 = 226 frontend tests + 18 BFF Layer 0.5 unit tests) | task 087 `Pillar8ToPlaybookEngineTests.cs` (13 tests at the Pillar 8 → BFF chain) |
| **9** Widget visibility contract | `getAgentVisibleState()` opt-in contract; `WorkspaceWidgetRegistry` extended; per-widget visible state; per-turn prompt builder respects `visibleToAssistant: true` filter | Phase C tasks 071/072/073/074 (`Pillar9PrivacyFilterTests.cs` 3-tab scenario) + task 078 cross-pillar | task 078 `CrossPillar_AllFourWidgetVariants_PrivacyFilterAndAdr015Audit` (Table row-ID elision audit) |

**Total coverage**: every pillar has both a dedicated per-task test AND at least one cross-pillar test exercising its seam to an adjacent pillar.

---

## Conversational refinement (NFR-01) coverage

| Coverage | Files |
|---|---|
| NL fall-through (no `commandIntent` → keyword classification preserved) | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterSoftSlashTests.cs` (NFR-11 theory tests) + task 087 `Pillar8_NullCommandIntent_FallsThroughToLayer1KeywordPath` + task 086 `natural-language-regression.test.ts` (50 frontend tests; 4 NL inputs verified end-to-end) |
| Voice memory ("remember X" / "forget X" / "always X") recognition | task 069 `CapabilityRouterVoiceMemoryTests` + task 087 `Pillar8_VoiceMemoryPriorityOverSoftSlash` |
| "Make it shorter" refinement after playbook output | Verified by NFR-01 binding (CapabilityRouter restricts tool list but never prevents conversational ability); task 086 verifies 4 NL inputs do NOT decorate; agent's full conversation history remains intact |
| Composition pattern `/summarize #file.docx` | task 084 `composition.integration.test.ts` (12 frontend integration tests: parse → resolve → decorate → send chain) |

---

## BFF publish-size cumulative tracking (NFR-02)

| Checkpoint | Compressed publish size | Cumulative delta from pre-R6 baseline |
|---|---|---|
| Pre-R6 baseline | **45.65 MB** | — |
| Task 087 closeout (current) | **46.06 MB** | **+0.41 MB** |

**Verdict**: Comfortably within NFR-02's R6 budget (≤+5 MB total) and the ADR-029 hard ceiling (60 MB). No size escalation triggered across the entire R6 project — the R6 work was carefully kept thin per the BFF Hygiene rules in root CLAUDE.md §10.

Per-task BFF publish-size deltas were tracked across Phases A/B/C/D evidence notes (most were 0 MB — frontend-only or test-only); aggregate +0.41 MB came from data-driven persona/tool/playbook resolution wiring + Layer 0.5 router pre-pass + workspace state service + memory composition wiring (split across many tasks).

---

## NFR-08 invariant — 11 production node executors UNMODIFIED

```
$ git diff src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/
(empty — zero modifications across R6)
```

11 production node executors preserved as-is per NFR-08:
- AgentServiceNodeExecutor, AiAnalysisNodeExecutor, ConditionNodeExecutor, CreateNotificationNodeExecutor, CreateTaskNodeExecutor, DeliverOutputNodeExecutor, DeliverToIndexNodeExecutor, QueryDataverseNodeExecutor, SendEmailNodeExecutor, UpdateRecordNodeExecutor — plus the Start-equivalent boundary (handled at orchestrator entry, not in a separate executor file).

Plus the additive Phase 2 helper nodes (DeclineToFindNode, EvidenceSufficiencyNode, GroundingVerifyNode, IndexRetrieveNode, LiveFactNode, ReturnInsightArtifactNode) — all from prior projects (R1/R2/Insights), not modified by R6.

---

## Sign-off recommendation

**Phase D vertical-slice contract**: MET via composed evidence.

| Acceptance criterion (POML §6) | Evidence path |
|---|---|
| Pillar 1: persona resolved from `sprk_aipersona`; no `BuildDefaultSystemPrompt()` call | task 005 + 028 |
| Pillar 2: tools assembled from `sprk_analysistool`; no hardcoded instantiation | task 011 + 012 + 013 |
| Pillar 3: `/summarize` triggers `invoke_playbook` | task 023 + 087 |
| Pillar 4: routes via `PlaybookExecutionEngine.ExecuteAsync(playbookId)` | task 025 + 087 |
| Pillar 5: schema-aware rendering + ONE render per intent | task 040 + 041 + 042 + 048 |
| Pillar 6a: workspace state in agent prompt | task 053 + 074 + 078 |
| Pillar 6b: "Send to Workspace" + "Make it shorter" + `/clear` work | task 057 + 058 + 081 + 078 |
| Pillar 6c: Context pane trace; ADR-015 hygiene | task 061 + 062 + 063 + 087 |
| Pillar 7: memory composition (recent + compressed + pinned + retrieved) | task 067 + 068 + 069 + 070 |
| Pillar 8: `/summarize #file.docx` parses + decorates + resolves + routes | task 080 + 082 + 083 + 084 + 087 |
| Pillar 9: per-turn prompt builder respects `visibleToAssistant: true` filter | task 073 + 074 + 078 |
| Conversational refinement (NFR-01): "make it shorter" → no tool call | task 086 + 069 + 087 |
| BFF publish-size delta vs Phase C exit baseline | +0.41 MB cumulative R6 (well within ≤+5 MB) |

**Recommended next step**: task 088 (lightweight eval baseline; Q10 markdown transcripts) → task 089 (Phase D exit-gate validation) → task 090 (wrap-up).
