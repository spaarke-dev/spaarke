# Current Task State — Spaarke AI Platform Chat Routing Redesign (R1)

> **Last Updated**: 2026-06-25 (Task 114b complete — FE `StructuredOutputStreamWidget` reworked to consume section-name-keyed SSE events per FR-54. Added 3 new `workspace.*` discriminants (`section_started`/`section_data`/`section_completed`) + 9 new optional fields to `PaneEventTypes.WorkspacePaneEvent` (ADR-030 additive). Widget extended with `SectionState` exported type; reducer accepts 3 new actions; new `SectionRenderer` component renders sections in `sectionIndex` order with insertion-order fallback; section-mode detection via `streamState.sections.size > 0`; new `data-render-mode="sections"|"fields"` attribute. **Backward-compat invariant**: legacy `FieldDelta`-only streams render via unchanged schema-position path — verified by anchor test `BACKWARD-COMPAT: legacy FieldDelta events render via schema-position path UNCHANGED (118R anchor)` in `StructuredOutputStreamWidget.sections.test.tsx`. **19 new tests**; all 23 legacy widget tests + 8 integration tests pass unchanged (50/50 widget suites green); SpaarkeAi downstream Vite build clean (3,955 kB / gzip 1,080 kB). Public props / widgetData / WorkspaceWidgetRegistry contract UNCHANGED. Coordination point count reduced 5→2 per FR-54 design intent. Earlier: Task 114a complete — per-section SSE streaming wired. Approach A (orchestrator-emits, ADR-013 separation of concerns): `PlaybookOrchestrationService.EmitDeliverCompositeSectionEventsAsync` re-iterates the `CompositeOutputPayload.Sections` list when a `ActionType.DeliverComposite` node completes and emits `section_started` → `section_data` → `section_completed` for each section via `ChannelWriter<PlaybookStreamEvent>`. New SSE event types in `Services/Ai/Chat/SseEventTypes/SectionStreamSseEvents.cs` (`SectionStartedSseEventData`, `SectionDataSseEventData`, `SectionCompletedSseEventData`); three factory methods on `ChatSseEventFactory`. Three new `PlaybookEventType` enum values (`SectionStarted` / `SectionData` / `SectionCompleted`) + `PlaybookStreamEvent.SectionPayload` discriminated wrapper. **9 new tests** (3-section emission + ordering + uniqueness + empty-sections + backward-compat invariant `SchemaPositionPlaybook_DeliverOutputPath_EmitsZeroSectionEvents` + ADR-015 tier-1 safety + 3 factory smoke). 119 related regression tests + 7918 full-BFF test suite all green. BFF publish compressed: **47.93 MB** (≈+0.01 MB vs 47.92 MB baseline — under NFR-01 60 MB ceiling). 17-warning baseline preserved. ADR-033 invariant verified: chat-summarize `delta`-typed `AnalysisChunk` stream surface is untouched; section events flow on a distinct envelope with non-colliding event-type names. Earlier: Task 117b FE `playbook_options` consumer; Task 114R `NodeType.DeliverComposite` (47.92 MB compressed); Task 116 dead-code removal; Task 117a SSE event contract; Task 111R hybrid intent reranker; Task 115 `intentHint` vector-query bias.)
> **Recovery**: READ "Quick Recovery" section FIRST. Tasks 112 + 113R + 115 + 111R + 117a + 116 + 114R + 117b + 114a + 114b ✅ shipped on `work/spaarke-ai-platform-chat-routing-redesign-r1`. Next active tasks: 114c (ADR; main session only because `.claude/` touch — likely in progress in parallel). 117 (telemetry, blocked-by 117b) now unblocked. 118R (per-playbook data migration) is the next FR-54 follow-up — backward-compat anchor test in `StructuredOutputStreamWidget.sections.test.tsx` MUST continue to pass after any 118R migration.

---

## Quick Recovery (READ THIS FIRST — <30 seconds)

| Field | Value |
|-------|-------|
| **Project** | `spaarke-ai-platform-chat-routing-redesign-r1` |
| **Branch** | `work/spaarke-ai-platform-chat-routing-redesign-r1` (worktree at `c:\code_files\spaarke-wt-spaarke-ai-platform-chat-routing-redesign-r1\`) |
| **Branch state** | After Phase 1R POML commit: 5 commits ahead of origin/master `8579d6536`. **Working tree clean.** Pushing now. |
| **Phase status** | Phase 0/1/2-partial/3/4-MVP ✅ shipped to bff-dev. **Phase 5R + Phase 1R spec + TASK-INDEX + 18 new POMLs locked in. Ready for W1 parallel execution.** |
| **Deployed state** | BFF master deployed; SpaarkeAi Code Page master deployed; Dataverse `RECALL-SESSION-FILE` row seeded; `Workspace__MatterPreFillPlaybookId` env var set |
| **Phase 1R STATUS** | **✅ CLOSED 2026-06-24** — All 8 FRs covered. 124/124 tests pass cumulatively. Cumulative BFF publish 49.22 MB (+2.94 MB vs 46.28 MB baseline; 10.78 MB headroom under NFR-01 — flag for ops monitoring; delta exceeds raw code volume, likely environmental drift). Phase 2 unblocked. |
| **Parallel Phase 5R progress** | Wave 5-A foundation seeded: **task 110** ✅ (commit `9a0632470`; `PlaybookDispatcher.DispatchAsync` accepts attachments; 5 tests; FR-15 invariant) + **task 110a** ✅ (commit `97bc64325`; Library modal `toLowerCase` defensive guard; root cause unsafe Dataverse cast) + **task 112** ✅ (`PlaybookDispatcher.RunPhaseBVectorMatchAsync` per-file vector match; manifest-present `documentTypes` pre-filter + manifest-absent parallel fan-out; 11 unit tests; BFF 44.94 MB) + **task 113R** ✅ (`PlaybookCandidateSelector` confidence-based top-N; 14 tests; BFF 47.89 MB) + **task 115** ✅ (`intentHint` vector-query bias on Phase B; `"Intent: {hint} | "` prefix on both paths; cache-key intent segmentation; ADR-015 tier-1 `intentHintProvided` flag only; 12 tests; BFF 47.91 MB — +0.02 MB delta; FR-20 invariant verified — same message + different intent produces different embed input). |
| **Owner action pending** | Optional: set `sprk_environment = '*'` explicitly on the 2 user-created records (null handled defensively). |
| **Next Action** | Phase 1R closed; **next priorities**: (a) reconcile + push all parallel work; (b) Phase 5R execution beginning at task 111R (hybrid intent reranker) per spec FR-46. |

### Critical context (3-sentence version)

User did UAT today and exposed that the chat-routing-redesign-r1 project's **deferred Phase 5+7 work IS what makes Spaarke AI feel impressive vs competition** — the project shipped infrastructure but not the user-visible convergence behavior. We merged R6 PR #401 (which had been blocking Phase 7), redeployed BFF + SpaarkeAi Code Page, and ran a detailed design conversation that REVISED Phase 5 scope substantially (was 6 tasks, now ~13-15 tasks) including: LLM-in-the-loop intent detection, multi-node Output composition rework, chat link-buttons UX, session continuity verification. Phase 7 stays as originally scoped (slash dict deletion + CapabilityRouter retirement). **Pre-compaction checkpoint is to enable resumption of W0 (spec+tasks update) without losing the design decisions.**

---

## What happened this session (full chronology)

### Session start state
- Phase 4 MVP completed and merged via PR #409 + PR #418 (cherry-pick) — both already in master
- Deployed BFF + Dataverse seed earlier in session
- User began UAT against the deployed system

### Critical events in chronological order

1. **UAT-1A fail**: User got HTTP 400 `tools[0].function.name` pattern violation on `/summarize`. I diagnosed root cause: `sprk_name` field used as OpenAI function name; spaces fail OpenAI regex `^[a-zA-Z0-9_\.-]+$`.

2. **UAT-2 fail**: Create Matter wizard pre-fill didn't execute. Diagnosed: bff-dev had legacy `Workspace__PreFillPlaybookId` but Phase 1 migration code reads `Workspace__MatterPreFillPlaybookId`. Fix: `az webapp config appsettings set` → `Workspace__MatterPreFillPlaybookId=2d660cad-d418-f111-8343-7ced8d1dc988`.

3. **UAT-3 fail**: Create Project wizard pre-fill failed. Diagnosed: config OK (`Workspace__ProjectPreFillPlaybookId` set), but playbook itself has no scopes/action (Dataverse data gap; user observed).

4. **Document Email Wizard 404**: FE calling `/by-name/Summarize New File(s)` instead of `/by-id/`. Phase 1 migration didn't fully land. Separate cleanup item.

5. **Identifier standardization design discussion**: User and I agreed on `sprk_{entity}id` as universal runtime lookup mechanism. User flagged: `sprk_toolid` already exists in `sprk_analysistool`; needs backfill script to populate (since GUID isn't easy to copy/paste from views/exports).

6. **`sprk_playbookconsumer` table design**: User WILL create the table themselves; wants `sprk_consumertype` + `sprk_consumercode` + scope filter columns + JSON `sprk_matchconditions` for complex matches. PCF rule-builder using JSONForms-style pattern. Service: `IConsumerRoutingService` reads it; replaces all `Workspace__*PlaybookId` env vars.

7. **User question**: "Have R6 deliverables been deployed? R6 was a massive project with little to show." → I audited R6 via `r6-deliverables-audit.md`. Found: R6 shipped substantial code; ~50% gated on 3 data scripts (already run); remaining gaps tracked as tasks 091-098. **CRITICAL FIND: PR #401 (R6 hotfixes) was OPEN, MERGEABLE, CLEAN, ALL CI GREEN — never merged. Contained "OpenAI tool-name sanitization" — the exact UAT-1A fix.**

8. **Merged PR #401** (8579d6536) → fast-forwarded worktree → redeployed BFF → redeployed SpaarkeAi Code Page (PR #401 also touched ConversationPane.tsx).

9. **UAT post-redeploy**: User retested. `/summarize` no longer crashed (good). BUT: summary went to Assistant chat (not Workspace); workspace tab opened blank; "summarize this document" said "document not in session" on retry; `/summarize` and NL behaved differently.

10. **User's sharp pushback**: "I thought we removed slash commands as a separate function. I thought we built playbook matching that dynamically matches upload file + chat instruction, derives intent, matches playbook embeddings, and presents user with options for playbooks to confirm. What happened to that?"

11. **HONEST acknowledgment**: I confirmed user's memory was correct. Quoted spec FR-20 ("`SoftSlashIntentToCapabilityName` dict removed; slash + NL flows produce identical routing"). Confirmed Phase 5+7 (the deferred work) IS exactly what user designed. **The project shipped infrastructure; the user-visible convergence was scope-cut and never built.**

12. **User identified architectural frailty**: The 5-coordination-point `StructuredOutputStreamWidget` model (schema-on-action + schema-aware widget) is brittle. User proposed: **multi-node Output composition** — each section/field has its own Action node feeding into an Output Node that composes for the consumer.

13. **User confirmed direction**: Option C — execute R6 task 095 + Phase 5 + Phase 7 in parallel where no risk. Asked me to review/extract Phase 5+7 spec/tasks (Option B from triage). Wants me as technical expert to recommend on multi-node design + LLM latency tradeoff.

14. **My recommendations (user accepted)**:
    - **Multi-node composition**: Yes do it. Pilot ONE playbook (summarize-document-for-workspace@v1); migrate others incrementally; chat sibling stays single-action; add ADR.
    - **LLM intent detection**: Hybrid — vector match primary (fast ~150ms); LLM fallback only when ambiguous (~500-800ms added); total worst case ~1s.
    - **Always show top 3 in chat link-buttons**, or all >=0.8 if multiple. User explicitly confirmed.
    - **Parallel-safe work to start NOW**: R6 task 095, Phase 7 task 144 (publish-size baseline), Phase 7 task 145 (Insights regression baseline), Phase 5 task 110 (DispatchAsync signature), Library modal `toLowerCase` fix.

---

## REVISED Phase 5 Scope (this is what to author POML files for)

### User UX intent (NEW, more sophisticated than original spec)

```
1. User uploads file
2. User types /summarize OR "summarize this document"
3. BFF: vector-match against playbook-embeddings (~150ms)
   - IF top-1 confidence >= 0.85: use as default, surface top 3 to user
   - ELSE: ambiguous → LLM picks best 3 from top 5 candidates (~500-800ms)
4. Chat: Assistant responds "Which playbook would you like me to use?"
   - Renders top 3 (or all >=0.8) as link buttons
   - + "Open Library Modal" link
5. User clicks → playbook executes
6. Output via Output Node config → workspace widget renders per-section
```

### Phase 5 tasks (revised, ~13 tasks)

**5-A: Intent detection + matching**
- 110: `PlaybookDispatcher.DispatchAsync` accepts attachments (existing FR-15)
- 112: Phase B vector match with manifest pre-filter (existing FR-17)
- **NEW**: LLM-in-the-loop intent detection (gpt-4o-mini)
- **NEW**: Confidence threshold + top-N returning logic
- 115: `commandIntent` as vector-query bias (existing FR-20)
- 116: Remove `SoftSlashIntentToCapabilityName` dict (BFF) (existing)

**5-B: Output composition rework (THE BIG ONE — ~5-6 days)**
- **NEW**: Add `NodeType.DeliverComposite` extension to PlaybookExecutionEngine
- **NEW**: Output Node accepts N input bindings; composes per section_key
- **NEW**: Per-section SSE streaming (`section_data` events keyed by name)
- **NEW**: `StructuredOutputStreamWidget` rework — listens by section name (not schema position)
- **NEW**: ADR for the design pattern (1-page)

**5-C: Chat link-buttons UX**
- **NEW**: Chat SSE event type for "playbook options"
- **NEW**: Frontend renders options as link buttons (clickable → invoke playbook)
- **NEW**: "Open Library Modal" link in chat options
- **NEW**: Library modal `Cannot read properties of null (reading 'toLowerCase')` bug fix

**5-D: Session continuity (user NOTEs 2 + 3)**
- **NEW**: Verify `ChatSession.UploadedFiles[]` retains files across turns (test if missing)
- **NEW**: Workspace output → AI memory (output sections accessible in subsequent turns via T2)

**5-E: Migration + telemetry + exit**
- **NEW**: Migrate `summarize-document-for-workspace@v1` to multi-node (proof point)
- 117: Routing telemetry (existing)
- 119: Acceptance test + Phase 5 exit gate (update test list)

### Phase 5 tasks DROPPED per Q5a cut (confirmed cut)
- 111 (Phase A fingerprint <50ms)
- 113 (Phase C reconciliation logic — REPLACED by LLM intent detection)
- 114 (gpt-4o-mini decider — REPLACED with hybrid LLM-on-ambiguous pattern)
- 118 (Load test verification)

### User OUT-OF-SCOPE confirmations
- Cards UI → user explicitly DOES NOT want cards; just link buttons
- Draft document playbook + WorkingDocument widget → Gap 3 punted
- Edit summary capability → "next, next round"
- Phase 6 specialized playbook authoring → not in scope

---

## REVISED Phase 7 Scope (unchanged from original)

| Task | What | Status |
|---|---|---|
| 140 | Verify R6 PR #401 merged | ✅ DONE TODAY (8579d6536) |
| 141 | Delete `CapabilityRouter` + 10 files + per-playbook tool filtering | Active |
| 142 | Remove `SoftSlashRouter.SOFT_SLASH_TO_INTENT` dict (frontend) | Active |
| 143 | Q20 dedup preservation test | Active |
| 144 | BFF publish-size net reduction | Active |
| 145 | Insights regression suite | Active |
| 146 | Full UAT regression | Active |
| 147 | Final code review | Active |
| 148 | Final adr-check | Active |
| 150 | Project wrap-up | Active |

**Note**: Phase 7 task 141/142 must wait until Phase 5 routing is working.

---

## Execution sequence (agreed with user)

| Wave | Work | Duration | Parallel-safe? |
|---|---|---|---|
| **W0 — NEXT** | Update `spec.md` + `TASK-INDEX.md` + author new POML files | I drive | — |
| **W1 — parallel** | R6 task 095 (ExecutionTraceWidget) + Phase 5 task 110 + Phase 7 baselines (144/145) + Library modal bug fix | ~1 day | ✅ Yes |
| **W2 — Phase 5 core** | LLM intent detection + vector match + confidence + chat link buttons | ~5 days | Bounded |
| **W3 — Phase 5 output rework** | Multi-node composition + section streaming + widget rework + ADR | ~6 days | Bounded |
| **W4 — Phase 5 migration** | Migrate `summarize-document-for-workspace@v1` to multi-node | ~1 day | Bounded |
| **W5 — Phase 5 close** | Session continuity + workspace→memory + telemetry + exit gate | ~2 days | None |
| **W6 — Phase 7** | Delete CapabilityRouter + remove FE dict + dedup test + reviews + wrap-up | ~3-5 days | Low risk |

**Total**: ~18-22 days (3.5-4.5 weeks)

---

## Files Modified This Session (all committed)

| Commit | What |
|---|---|
| Various during BFF redeploy | None — script changes were transient |
| `8579d6536` | PR #401 R6 hotfixes merge (CapabilityRouter.cs, SprkChatAgentFactory.cs, ToolHandlerToAIFunctionAdapter.cs, ConditionNodeExecutor.cs, ConversationPane.tsx, etc.) |
| App Service config | `Workspace__MatterPreFillPlaybookId=2d660cad-...` set on bff-dev |
| **`272dcce47`** | **W0 batch 1**: Phase 5+7 revised scope amendment in `spec.md` (FR-46 through FR-59); `TASK-INDEX.md` Phase 5 wave section revised (5-A through 5-F); 12 new task rows; Phase Summary + Materialization Plan + Audit History updated. |
| **`312385f2f`** | **W0 batch 2**: 12 new POML task files authored by 4 parallel sub-agents (UX 3 + Intent 2 + Composition 4 + Migration 3). All valid XML; task-id/filename match verified; FR mappings verified. |
| `a20eef3eb` | Checkpoint: W0 complete |
| **`3d342e9f9`** | **Phase 1R spec + index**: `sprk_playbookconsumer` routing table FR-1R-01..08; 6 new task rows (028, 028a–028e); audit history entry. |
| **(pending)** | **Phase 1R POMLs**: 6 new POML task files authored by 2 parallel sub-agents (Foundation 3 + Migration 3). All valid XML. |
| `f5a5568c7` | Pre-compaction checkpoint write to `current-task.md` |

**Working tree is currently clean.** Branch is 5 commits ahead of origin/master `8579d6536` after this commit; pushing now.

---

## Key DECISIONS made this session

| # | Decision | Rationale | Status |
|---|---|---|---|
| 1 | Use `sprk_{entity}id` as universal runtime lookup standard | Admin-stable, env-portable, mirrors PK; user verified playbook IDs match between Dev and Demo | Agreed |
| 2 | `sprk_playbookconsumer` table replaces all `Workspace__*PlaybookId` env vars | Scales to 100s; admin-editable; auditable; Dataverse-cached | Agreed — user creating the table |
| 3 | **Merge PR #401 immediately** | OpenAI tool-name sanitization fixes UAT-1A; was MERGEABLE/CLEAN/CI green | ✅ Done today |
| 4 | **Phase 5+7 = the actual project** | Original Q5b cut deferred user-visible work; those phases are the impressive UX | Agreed |
| 5 | **Multi-node Output composition** (user proposal, I confirmed as right architecturally) | Each section as own Action+Output; cleaner than schema-aware widget; ~5 days code work | Confirmed |
| 6 | **Hybrid LLM intent detection** | Vector match primary; LLM fallback only when ambiguous; ~150-1000ms latency | Confirmed |
| 7 | **Always show top 3 in chat link-buttons** (or all >=0.8) | User wants confirmation always — instruction may be vague | Confirmed |
| 8 | **Library modal already exists**; no new "cards" UI; chat options are just link buttons | User screenshot confirmed modal exists with `toLowerCase` bug | Confirmed |
| 9 | **Pilot multi-node on summarize-document-for-workspace@v1**; chat sibling stays single-action | Reduces migration risk; chat doesn't need multi-node | Confirmed |
| 10 | **Run R6 task 095 + Phase 7 baselines + Phase 5 task 110 in parallel** with Phase 5 main work | No risk; saves wall-clock | Confirmed |
| 11 | **OUT OF SCOPE**: draft playbook + WorkingDocument widget; edit-summary; cards UI; Phase 6 playbook authoring | User explicitly confirmed | Locked |

---

## Open follow-ups (NOT this project's scope)

| Item | Where it belongs |
|---|---|
| Editable widget framework (Issue 3 — "cannot edit summary") | Next-next project (R7+) |
| Draft document → WorkingDocument widget | Future project (after R7) |
| `sprk_toolid` backfill script | This project — Phase 5 W1 small task |
| Library modal `toLowerCase` null fix | This project — Phase 5 W1 |
| ADR for identifier standard (`sprk_{entity}id` rule) | This project — Phase 5 W0 deliverable |
| ADR for multi-node Output composition | This project — Phase 5 W3 deliverable |
| Document Email Wizard `/by-name/` migration | This project — Phase 5 carryover |
| R6 closeout tasks 096, 097b, 098 (not 095) | Defer to R6 wrap-up; not in our project scope |

---

## Resumption instructions for next session

When the new Claude instance starts after compaction:

1. **Read THIS Quick Recovery section first** (< 30 seconds)
2. **Confirm worktree is clean**: `git status --porcelain` (should be empty)
3. **Confirm master sync**: `git log -1 --oneline` should show `8579d6536` (or newer if master moved)
4. **Begin W0 work**: Update `spec.md` Phase 5 section + Phase 7 section per REVISED scope above; update `tasks/TASK-INDEX.md` with revised task list; author new POML files for new tasks (LLM intent detection, multi-node composition, chat link buttons, session continuity, output rework, ADRs)
5. **DO NOT touch** `spec.md`/`design.md`/`architecture/`/`README.md`/`plan.md`/`CLAUDE.md` *content* beyond what's authorized for Phase 5+7 revision — those are otherwise frozen. (Spec amendment for Phase 5+7 is explicitly authorized this session.)
6. **After W0 commits**, kick off W1 parallel work
7. **Continue updating current-task.md** as work progresses

User explicitly said: "we need to get this done!" — execution urgency is high.

---

## Branch state numbers (at handoff time)

- HEAD: `8579d6536` (PR #401 merge commit)
- Local = origin/work = origin/master = `8579d6536`
- Working tree: **clean**
- Cumulative project commits in master: 94+ (verified per-commit reachability earlier in session)
- BFF baseline: 46.28 MB compressed (last redeploy)
- bff-dev: master code running, hash-verified, `/healthz` returning 200
- SpaarkeAi Code Page: deployed at web resource `sprk_spaarkeai` (id `5206a442-3451-f111-bec7-7ced8d1dc988`)

---

## Critical guards (unchanged)

- DO NOT push to master directly
- Phase 5+7 spec/task amendments ARE authorized this session
- Other spec/design/architecture/README/plan/CLAUDE.md content remains frozen
- DO NOT use `--no-verify`, `--force`, or `git reset --hard`
- DO checkpoint after every wave
- DO NOT close out chat-routing-redesign-r1 project yet

---

*This file is the primary source of truth for active work state. Updated by `/context-handoff` 2026-06-24 after design conversation finalized revised Phase 5+7 scope.*
