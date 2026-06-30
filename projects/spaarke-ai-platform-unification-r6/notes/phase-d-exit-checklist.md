# R6 Phase D Exit-Gate Checklist

> **Project**: spaarke-ai-platform-unification-r6
> **Phase**: D — Command Router + Integration + Closeout
> **Signed off**: 2026-06-29 by task 089
> **Evidence base**: TASK-INDEX.md rows for tasks 080–088 (all ✅) + spec.md §Phase D Exit Criteria

---

## 5 Exit Criteria (verbatim from `spec.md` §294-300)

### 1. `/help` works and is discoverable ✅

**Verify by** (per spec): typing `/help` shows command reference; UI affordance shows available commands.

**Evidence**:
- **Task 081** (D-D-02) — `HardSlashExecutor.ts` + `CommandHelpPanel.tsx`; 53 tests green (43 executor + 10 help panel); all 6 hard slashes measured <100ms in mocked-BFF; `notes/task-081-evidence.md`
- **Task 085** (D-D-06) — `HelpAffordance.tsx` (subtle Fluent v9 Button + Tooltip + `QuestionCircleRegular`); 10/10 tests green (aria-label, tooltip, click, Enter/Space keyboard, light/dark theme, disabled state); `ConversationPane.tsx` minimal additive change (import + render + wire `setHelpPanelOpen(true)`); Option A absolute-positioned overlay top-right of chat region; ADR-021 semantic tokens only; NFR-11 additive UX preserved; `notes/task-085-evidence.md`

**Status**: ✅ SIGNED OFF — `/help` opens the drawer; affordance is a persistent overlay top-right of chat.

---

### 2. Hard slashes bypass LLM (<100ms latency; no Azure OpenAI request) ✅

**Verify by** (per spec): `/clear` resets session without LLM call; latency <100ms; instrumentation confirms no Azure OpenAI request.

**Evidence**:
- **Task 081** (D-D-02) — `HardSlashExecutor.ts` for all 6 hard slashes (`/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter`, `/pin`); 43 unit tests green; <100ms verified in mocked-BFF; reuses existing REST endpoints (no LLM dispatch path involved); ADR-015 telemetry audit PASS (no user text in payloads); BFF publish-size delta = 0 MB (frontend-only)
- **Task 087** vertical-slice integration tests (Pillar8ToPlaybookEngineTests.cs, 13 tests passed in 23ms) confirm closed-vocabulary integrity and that hard-slash dispatch never reaches the playbook engine codepath.

**Status**: ✅ SIGNED OFF — all 6 hard slashes deterministic, sub-100ms, zero LLM round-trip.

---

### 3. Soft slashes route via agent with prioritized intent ✅

**Verify by** (per spec): `/summarize` triggers Summarize playbook intent via CapabilityRouter; agent confirms before action.

**Evidence**:
- **Task 082** (D-D-03) — `SoftSlashRouter.ts` (38 tests green) for the 4 soft slashes (`/summarize`, `/draft`, `/extract-entities`, `/analyze`); CapabilityRouter Layer 0.5 pre-pass on the BFF side (18 BFF tests green); BFF publish-size delta ≈ 0 MB; `notes/task-082-evidence.md`
- **Note on successor delta**: chat-routing-redesign-r1 retired CapabilityRouter wholesale (their Phase 7 / PR #509 merged 2026-06-27); the Layer 0.5 prioritization invariant is preserved by their replacement (FR-23 tool-filtering path in `SprkChatAgentFactory.CreateAgentAsync`). R6 Phase D exit-gate signs off on the soft-slash UX contract — successor maintains it through a different routing mechanism.

**Status**: ✅ SIGNED OFF — soft slashes route to playbook intents; UX contract preserved.

---

### 4. References resolve at parse time ✅

**Verify by** (per spec): `@matter` resolves to matter record; `#contract.docx` resolves to session file; resolved entities appear in agent prompt.

**Evidence**:
- **Task 083** (D-D-04) — `ReferenceResolver.ts` + 27 tests green; 3 resolver types wired (scope/entity/file); ADR-014 tenantId cache keys; NFR-01 non-blocking; in-flight de-dup; `notes/task-083-evidence.md`
- **Task 084** (D-D-05) — composition integration tests (12 tests green): FR-52 `/summarize #engagement-letter.docx` (5 tests) + `/draft response to @opposing-counsel about #motion-to-dismiss` (4 tests); end-to-end parse → resolve → decorate → send chain with stubbed adapters (fileLookup / scopeFetch); ADR-014 tenantId cache keys + NFR-01 non-blocking degradation both exercised; `notes/task-084-evidence.md`

**Status**: ✅ SIGNED OFF — `@entity`, `#scope`, `#filename` references resolve at parse time and decorate the outgoing chat body.

---

### 5. All R6 changes have integration test coverage (vertical-slice per §6) ✅

**Verify by** (per spec): `tests/integration/` has end-to-end test for the Vertical-Slice Validation Target.

**Evidence**:
- **Task 087** (D-D-08) — **vertical-slice integration test (all 9 pillars per spec §6)**. COMPOSED EVIDENCE per task 078 precedent: 9-pillar evidence map at `notes/vertical-slice-evidence.md` + new `Pillar8ToPlaybookEngineTests.cs` (13 tests PASSED in 23 ms) at the Pillar 8 → Pillar 3 → Pillar 4 → Pillar 5 → Pillar 6c BFF chain. Covers: ADR-015 audit, NFR-11 fall-through, voice-memory vs soft-slash ordering, FR-30 playbook ID propagation, Q6 closed-vocabulary integrity. BFF publish-size 46.06 MB compressed (+0.41 MB cumulative R6 — well within ≤+5 MB NFR-02 and 60 MB ADR-029 hard limit). NFR-08 invariant verified (`git diff src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` empty). `notes/task-087-evidence.md`
- **Task 086** (D-D-07) — natural-language regression test (NFR-11 backward compat): `natural-language-regression.test.ts` + 50 tests green; 4 NL inputs verified for (a) `Intent.command === null` (b) `decorateBody` no-op no `commandIntent` field (c) input body purity (d) NFR-01 conversational primacy preserved for refinement / follow-up; `notes/task-086-evidence.md`
- **Task 088** (D-D-09) — lightweight eval baseline (Q10 markdown transcripts): 4 markdown transcripts at `notes/eval-baseline/`: SYS-DEFAULT × summarize-chat (vertical-slice snapshot) + summarize-workspace (Pillar 5 shared-action) + matter-prefill (NFR-07 evidence) + project-prefill (NFR-07 evidence); each cites Q10 + synthetic user messages per ADR-015; full eval harness deferred R7 per spec Owner Clarifications.

**Status**: ✅ SIGNED OFF — vertical-slice integration test exists + NFR-11 regression coverage + Q10 eval transcripts seeded.

---

## Bonus closeout follow-on (out of Phase D criteria, captured for R7 backlog)

The 4 DEF items pulled back into R6 scope on 2026-06-28 by user direction were shipped to master on 2026-06-29 (merge commit `ecb650e44`):

| Item | Commit | What |
|---|---|---|
| DEF-001 (#471) | `229f30ef9` | BFF `context_event` SSE emission via `IContextSseRelay` per-request bridge; ExecutionTraceWidget receives live trace frames |
| DEF-002 (#473) | `e16677beb` | Fix broken playbook-attached persona precedence layer — was stub that filtered by `scopetype` only; now reads `sprk_analysisplaybook.sprk_playbookpersona` FK |
| DEF-003 (#474) | `c98fe7f85` | `DeliverOutputForm` destination + widgetType fields; persists `NodeRoutingConfig` wire format into existing `sprk_configjson` |
| DEF-004 (#476) | n/a — user did via maker portal | Removed vestigial `sprk_capabilities` column from `sprk_analysisplaybook`; surfaces sister-project ISS-003 (#510) bug |

These are NOT Phase D exit criteria — they were post-Phase-D surface gaps. Phase D exit gate signs off on the 5 spec-bound criteria above.

---

## Acceptance Criteria (from task 089 POML)

| Criterion | Status |
|---|---|
| `phase-d-exit-checklist.md` exists with 5 criteria, each ✅ + evidence file cited | ✅ This document |
| `plan.md` Phase D milestone marked ✅ | ✅ Updated same commit |
| All tasks 080–088 marked ✅ in TASK-INDEX.md (verified) | ✅ Confirmed at TASK-INDEX.md status sync rows |
| BFF publish-size delta = 0 MB | ✅ Documentation-only task |

---

**Phase D exit gate: SIGNED OFF 2026-06-29.** R6 ready for project wrap-up (task 090).
