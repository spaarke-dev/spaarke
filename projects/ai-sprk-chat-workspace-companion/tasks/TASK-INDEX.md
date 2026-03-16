# TASK-INDEX.md — ai-sprk-chat-workspace-companion

> **Last Updated**: 2026-03-16
>
> **Total Tasks**: 29 | **Completed**: 0 | **In Progress**: 0 | **Pending**: 29

---

## Task Registry

| ID | Title | Phase | Status | Deps | Blocks | Rigor | Parallel |
|----|-------|-------|--------|------|--------|-------|----------|
| 001 | Extend SprkChatPane Launcher with Analysis Launch Context | 2A | 🔲 | — | 002 | FULL | Group A |
| 002 | Update AnalysisWorkspace Context Launch to pass SprkChatLaunchContext | 2A | 🔲 | 001 | 030, 031 | FULL | Group B |
| 003 | Remove Global SprkChat Auto-Injection from Non-Analysis Pages | 2A | 🔲 | — | — | FULL | Group A |
| 010 | Create InlineAiToolbar Types and Constants | 2B Library | 🔲 | — | 011, 012 | FULL | Group A |
| 011 | Create InlineAiToolbar Components (Toolbar + Actions) | 2B Library | 🔲 | 010 | 013 | FULL | Group B |
| 012 | Create InlineAiToolbar Hooks (useInlineAiToolbar + useInlineAiActions) | 2B Library | 🔲 | 010 | 013 | FULL | Group B |
| 013 | InlineAiToolbar Barrel Exports and Unit Tests | 2B Library | 🔲 | 011, 012 | 030 | STANDARD | Group C |
| 020 | Create AnalysisChatContextResolver Service with Redis Caching | 2C | 🔲 | — | 021 | FULL | Group A |
| 021 | Register GET /api/ai/chat/context-mappings/analysis/{analysisId} Endpoint | 2C | 🔲 | 020 | 022, 060 | FULL | Group B |
| 022 | Seed Analysis Playbook Test Data and Endpoint Tests | 2C | 🔲 | 021 | — | STANDARD | Group C |
| 030 | Create useInlineAiToolbar App-Level Hook | 2B-Wiring | 🔲 | 013 | 031 | FULL | Group D |
| 031 | Wire InlineAiToolbar into EditorPanel and SprkChat inline_action Subscription | 2B-Wiring | 🔲 | 030 | 060, 080 | FULL | Group E |
| 040 | Create SlashCommandMenu Component and useSlashCommands Hook | 2E | 🔲 | 013 | 061 | FULL | Group D |
| 041 | Create QuickActionChips Component | 2E | 🔲 | 013 | 060 | FULL | Group D |
| 042 | Create SprkChatMessageRenderer Component | 2E | 🔲 | 011 | 062 | FULL | Group B |
| 043 | Create PlanPreviewCard Component | 2E | 🔲 | 011 | 062 | FULL | Group B |
| 050 | Insert Button on AI Messages and Broadcast document_insert Event | 2D | 🔲 | 031 | 051 | FULL | Group F |
| 051 | Lexical Editor Insert-at-Cursor Handler for document_insert Events | 2D | 🔲 | 050 | 080 | FULL | Group G |
| 060 | Wire QuickActionChips and Context Fetch into SprkChat.tsx | 2E-Wiring | 🔲 | 041, 021 | 080, 081 | FULL | Group E |
| 061 | Wire SlashCommandMenu into SprkChatInput.tsx | 2E-Wiring | 🔲 | 040 | 081 | FULL | Group E |
| 062 | Wire SprkChatMessageRenderer and PlanPreviewCard into Message Pipeline | 2E-Wiring | 🔲 | 042, 043 | 080, 081 | FULL | Group E |
| 070 | Investigate Plan Preview Session State Design | 2F | 🔲 | 031 | 071 | MINIMAL | Group F |
| 071 | Implement plan_preview SSE Event and Compound Intent Detection | 2F | 🔲 | 070 | 072 | FULL | Group G |
| 072 | Implement POST /plan/approve Endpoint and Session Approval Flow | 2F | 🔲 | 071 | 073, 080 | FULL | Group H |
| 073 | Implement Write-Back to sprk_analysisoutput.sprk_workingdocument | 2F | 🔲 | 072 | 080 | FULL | Group I |
| 080 | Integration Tests: BFF Endpoints + Manual E2E Verification | 2G Testing | 🔲 | 072, 073, 060, 062 | 081 | STANDARD | Group J |
| 081 | Deploy Code Pages: AnalysisWorkspace and SprkChatPane to Dataverse | 2G Deploy | 🔲 | 080, 031, 060, 061, 062 | 090 | STANDARD | Group K |
| 082 | Deploy BFF API to Azure App Service | 2G Deploy | 🔲 | 080 | 090 | STANDARD | Group K |
| 090 | Project Wrap-Up: Quality Gates, Cleanup, and Completion | 2G Wrap-Up | 🔲 | 081, 082 | — | FULL | — |

**Status Legend**: 🔲 Pending · 🔄 In Progress · ✅ Completed · ⛔ Blocked

---

## Rigor Level Distribution

| Level | Count | Tasks |
|-------|-------|-------|
| **FULL** | 20 | 001-003, 010-012, 020-021, 030-031, 040-043, 050-051, 060-062, 071-073, 090 |
| **STANDARD** | 8 | 013, 022, 080, 081, 082 |
| **MINIMAL** | 1 | 070 |

---

## Parallel Execution Groups

Tasks in the same group can run **simultaneously** once their prerequisites are complete. Each parallel task MUST still use the `task-execute` skill — invoke multiple Skill tool calls in a **single message** to run them in parallel.

| Group | Tasks | Prerequisite | Files Touched | Safe to Parallelize |
|-------|-------|--------------|---------------|---------------------|
| **A** | 001, 003, 010, 020 | None (first tasks) | Separate files — launcher, HTML pages, shared library types, BFF resolver | ✅ Yes |
| **B** | 002, 011, 012, 021, 042, 043 | Group A tasks (respective deps) | Different modules — App.tsx, components, hooks, BFF endpoint, message renderer | ✅ Yes |
| **C** | 013, 022 | 011+012 (for 013), 021 (for 022) | Barrel exports + tests vs BFF test data | ✅ Yes |
| **D** | 030, 040, 041 | Group C (013 for all) | Different hooks and components in shared library | ✅ Yes |
| **E** | 031, 060, 061, 062 | Group D (respective deps) | Different wiring files — EditorPanel, SprkChat, SprkChatInput, SprkChatMessage | ✅ Yes |
| **F** | 050, 070 | Group E (031 for both) | Insert button (SprkChat.tsx) vs investigation notes — no conflict | ✅ Yes |
| **G** | 051, 071 | Group F (050 for 051, 070 for 071) | Lexical editor handler vs BFF SSE implementation | ✅ Yes |
| **H** | 072 | 071 | BFF plan approval endpoint | Single task |
| **I** | 073 | 072 | BFF write-back tool | Single task |
| **J** | 080 | 072, 073, 060, 062 | Integration tests | Single task |
| **K** | 081, 082 | 080 | Code pages (Dataverse) vs BFF (Azure) — completely separate targets | ✅ Yes |

### How to Execute Parallel Groups

1. **Check all prerequisites** are ✅ completed in this index
2. **Single message, multiple Skill tool calls** — invoke `task-execute` for each task in the group simultaneously:
   ```
   [Skill: task-execute, args: "projects/ai-sprk-chat-workspace-companion/tasks/001-extend-sprkchat-launch-context.poml"]
   [Skill: task-execute, args: "projects/ai-sprk-chat-workspace-companion/tasks/003-remove-global-sprkchat-injection.poml"]
   [Skill: task-execute, args: "projects/ai-sprk-chat-workspace-companion/tasks/010-create-inline-ai-toolbar-types.poml"]
   [Skill: task-execute, args: "projects/ai-sprk-chat-workspace-companion/tasks/020-create-analysis-chat-context-resolver.poml"]
   ```
3. **Wait for all to complete** before moving to the next group
4. **Never** bypass task-execute for "efficiency" — it ensures ADR loading, checkpointing, and quality gates

---

## Critical Path

The longest dependency chain (minimum sequential tasks):

```
010 (types) → 011/012 (components/hooks) → 013 (exports)
→ 030 (app hook) → 031 (editor wiring)
→ 070 (investigation) → 071 (plan preview SSE) → 072 (approval endpoint) → 073 (write-back)
→ 080 (integration tests) → 081/082 (deployment) → 090 (wrap-up)
```

**Total serial steps on critical path**: 12 tasks

---

## Dependency Graph (key relationships)

```
Group A (no deps):   001 ──→ 002 ──→ 030
                     003 (standalone)
                     010 ──→ 011 ──→ 013 ──→ 030 ──→ 031 ──→ 050 ──→ 051
                          └→ 012 ──→ 013        └→ 060 ──→ 080 ──→ 081 ──→ 090
                     020 ──→ 021 ──→ 022         └→ 061           └→ 082 ──┘
                                └──→ 060         └→ 062 ──→ 080

                                          040 ──→ 061
                                          041 ──→ 060
                                          042 ──→ 062
                                          043 ──→ 062

                     031 ──→ 070 ──→ 071 ──→ 072 ──→ 073 ──→ 080
```

---

## Phase Summary

| Phase | Tasks | Description |
|-------|-------|-------------|
| 2A: Contextual Launch | 001, 002, 003 | Decouple SprkChat from global auto-injection; add analysis context |
| 2B: Inline AI Toolbar (Library) | 010, 011, 012, 013 | New shared library components + types |
| 2B-Wiring | 030, 031 | Wire toolbar into AnalysisWorkspace editor |
| 2C: Context-Driven Actions BFF | 020, 021, 022 | New BFF endpoint for context-driven chips |
| 2D: Insert-to-Editor | 050, 051 | Insert AI content into Lexical editor |
| 2E: Enhanced SprkChat Pane (Library) | 040, 041, 042, 043 | New UI components for enriched pane |
| 2E-Wiring | 060, 061, 062 | Wire new components into SprkChat |
| 2F: BFF Plan Preview + Write-Back | 070, 071, 072, 073 | Plan preview gate + write-back |
| 2G: Testing + Deploy + Wrap-Up | 080, 081, 082, 090 | Quality gates, deployment, completion |

---

## High-Risk Items

| Task | Risk | Mitigation |
|------|------|------------|
| 073 (Write-Back) | Accidental SPE file modification | No SPE SDK calls allowed; integration test asserts SPE write = 0 |
| 070 (Session Design) | Wrong storage mechanism for pending plans | Investigation task produces design doc before implementation starts |
| 031 (Editor Wiring) | mousedown vs click breaks text selection | Spec explicitly requires mousedown; toolbar component uses mousedown handlers |
| 060 (SprkChat Wiring) | Auth token missing in context-mappings fetch | Must follow useChatPlaybooks.ts auth pattern exactly |
| 012 (InlineAiToolbar Hooks) | Hook calls BroadcastChannel on wrong channel name | Must match channel name used by SprkChatPane subscriber |
