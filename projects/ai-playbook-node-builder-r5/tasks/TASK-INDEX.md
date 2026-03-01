# Task Index — AI Playbook Node Builder R5

> **Last Updated**: 2026-02-28
> **Total Tasks**: 25
> **Completed**: 3 / 25

## Status Legend

| Symbol | Status |
|--------|--------|
| 🔲 | Not Started |
| 🔄 | In Progress |
| ✅ | Completed |
| 🚫 | Blocked |

---

## Phase 1: Scaffold (Serial — Foundation)

| # | Task | Status | Dependencies | Est. |
|---|------|--------|-------------|------|
| 001 | [Project Scaffold and Build Pipeline](001-project-scaffold-and-build-pipeline.poml) | ✅ | none | 3h |
| 002 | [AuthService and DataverseClient](002-auth-service-and-dataverse-client.poml) | ✅ | 001 | 3h |
| 003 | [Entry Point, FluentProvider, Theme Detection](003-entry-point-theme-detection.poml) | 🔲 | 001, 002 | 2h |

## Phase 2: Canvas Migration (Parallel — after Phase 1)

| # | Task | Status | Dependencies | Est. |
|---|------|--------|-------------|------|
| 010 | [Canvas Migration to @xyflow/react v12](010-canvas-migration-xyflow-v12.poml) | 🔲 | 001, 002, 003 | 4h |
| 011 | [Migrate 7 Node Components to v12 Generics](011-migrate-node-components-v12.poml) | 🔲 | 001, 002, 003 | 3h |
| 012 | [Migrate ConditionEdge to v12 EdgeProps](012-migrate-condition-edge-v12.poml) | 🔲 | 001, 002, 003 | 1h |
| 013 | [Migrate canvasStore to v12 Types](013-migrate-canvas-store-v12.poml) | 🔲 | 001, 002, 003 | 2h |

## Phase 3: Scope Resolution (Parallel — after Phase 1)

| # | Task | Status | Dependencies | Est. |
|---|------|--------|-------------|------|
| 020 | [Rewrite scopeStore with Real Dataverse Queries](020-rewrite-scope-store.poml) | 🔲 | 001, 002, 003 | 3h |
| 021 | [Rewrite modelStore with Real Dataverse Queries](021-rewrite-model-store.poml) | 🔲 | 001, 002, 003 | 2h |
| 022 | [Build ActionSelector Component](022-build-action-selector.poml) | 🔲 | 001, 002, 003 | 2h |
| 023 | [Rewrite playbookNodeSync with DataverseClient](023-rewrite-playbook-node-sync.poml) | 🔲 | 001, 002, 003 | 4h |

## Phase 4: Node Config Forms (Parallel — after Phase 1)

| # | Task | Status | Dependencies | Est. |
|---|------|--------|-------------|------|
| 030 | [DeliverOutputForm Configuration Form](030-deliver-output-form.poml) | 🔲 | 001, 002, 003 | 2h |
| 031 | [SendEmailForm Configuration Form](031-send-email-form.poml) | 🔲 | 001, 002, 003 | 2h |
| 032 | [CreateTaskForm Configuration Form](032-create-task-form.poml) | 🔲 | 001, 002, 003 | 2h |
| 033 | [AiCompletionForm Configuration Form](033-ai-completion-form.poml) | 🔲 | 001, 002, 003 | 2h |
| 034 | [WaitForm Configuration Form](034-wait-form.poml) | 🔲 | 001, 002, 003 | 1h |
| 035 | [VariableReferencePanel Component](035-variable-reference-panel.poml) | 🔲 | 001, 002, 003 | 2h |
| 036 | [NodeValidationBadge Component](036-node-validation-badge.poml) | 🔲 | 001, 002, 003 | 2h |
| 037 | [Wire NodePropertiesForm to Type-Specific Forms](037-wire-node-properties-form.poml) | 🔲 | 030-036 | 2h |

## Phase 5: AI Assistant & Templates (Parallel — after Phase 1)

| # | Task | Status | Dependencies | Est. |
|---|------|--------|-------------|------|
| 040 | [Migrate AiAssistantModal and Sub-Components](040-migrate-ai-assistant.poml) | 🔲 | 001, 002, 003 | 3h |
| 041 | [Migrate aiAssistantStore](041-migrate-ai-assistant-store.poml) | 🔲 | 001, 002, 003 | 1h |
| 042 | [Migrate templateStore and ExecutionOverlay](042-migrate-template-store-execution.poml) | 🔲 | 001, 002, 003 | 2h |

## Phase 6: Integration & Polish (Serial — requires Phases 2-5)

| # | Task | Status | Dependencies | Est. |
|---|------|--------|-------------|------|
| 050 | [Wire BuilderLayout with All Panels](050-wire-builder-layout.poml) | 🔲 | 010-013, 020-023, 037, 040-042 | 3h |
| 051 | [Keyboard Shortcuts and Auto-Save](051-keyboard-shortcuts-autosave.poml) | 🔲 | 050 | 2h |
| 052 | [Dark Mode Verification (ADR-021)](052-dark-mode-verification.poml) | 🔲 | 050 | 2h |
| 053 | [Build and Deploy as Web Resource](053-build-deploy-web-resource.poml) | 🔲 | 050, 051, 052 | 2h |

## Phase 7: Verification & Cleanup (Serial — final)

| # | Task | Status | Dependencies | Est. |
|---|------|--------|-------------|------|
| 060 | [End-to-End Execution Test](060-end-to-end-execution-test.poml) | 🔲 | 053 | 3h |
| 061 | [Remove PCF from Solution](061-remove-pcf-from-solution.poml) | 🔲 | 060 | 1h |
| 062 | [Update Form to Open Code Page](062-update-form-to-code-page.poml) | 🔲 | 061 | 1h |

## Wrap-Up

| # | Task | Status | Dependencies | Est. |
|---|------|--------|-------------|------|
| 090 | [Project Wrap-Up](090-project-wrap-up.poml) | 🔲 | 060, 061, 062 | 1h |

---

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|--------------|-------|
| A | 010, 011, 012, 013 | Phase 1 complete | Canvas Migration — owns canvas/, nodes/, edges/, canvasStore |
| B | 020, 021, 022, 023 | Phase 1 complete | Scope Resolution — owns scopeStore, modelStore, ActionSelector, nodeSync |
| C | 030, 031, 032, 033, 034, 035, 036 | Phase 1 complete | Node Config Forms — each agent owns one form file |
| D | 040, 041, 042 | Phase 1 complete | AI Assistant — owns ai-assistant/, aiAssistantStore, templateStore |

**Groups A, B, C, D can all run in parallel** — they have no inter-group dependencies.

## Critical Path

```
001 → 002 → 003 → [A,B,C,D parallel] → 037 → 050 → 051/052 → 053 → 060 → 061 → 062 → 090
```

## Estimated Total Effort

| Phase | Tasks | Hours |
|-------|-------|-------|
| Phase 1 | 3 | 8h |
| Phase 2 | 4 | 10h |
| Phase 3 | 4 | 11h |
| Phase 4 | 8 | 15h |
| Phase 5 | 3 | 6h |
| Phase 6 | 4 | 9h |
| Phase 7 | 3 | 5h |
| Wrap-Up | 1 | 1h |
| **Total** | **25** | **65h** |

**With parallel execution**: Phases 2-5 run simultaneously, reducing wall clock time to ~30h.

---

*Generated by project-pipeline skill. Updated by task-execute skill during execution.*
