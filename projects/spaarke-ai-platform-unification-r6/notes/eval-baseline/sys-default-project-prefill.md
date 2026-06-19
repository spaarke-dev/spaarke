---
captured: 2026-06-18
persona: SYS-DEFAULT (sprk_aipersona row)
playbook: project-prefill (Workspace:ProjectPreFillPlaybookId config; NFR-07 binding)
note: "Captured per R6 Q10 (lightweight eval baseline; full harness deferred R7). Reference file for future eval harness consumption. NFR-07 BINDING — pre-fill flow signatures + 45s timeout + useAiPrefill UNCHANGED."
---

# SYS-DEFAULT persona × project-prefill playbook (NFR-07 regression evidence)

## Persona used

- **Row name**: SYS-DEFAULT
- **System prompt source**: byte-identical to legacy `BuildDefaultSystemPrompt()`

## Playbook invoked

- **Playbook ID**: configured via `Workspace:ProjectPreFillPlaybookId` (BFF appsettings)
- **Action FK**: project-prefill action — outputSchema migrated in task 035 (NFR-07 regression test gate)
- **Routing path**: NOT chat-triggered. Pre-fill is invoked from `ProjectPreFillService.GetSuggestionsAsync` via the existing `useAiPrefill` React hook (`@spaarke/ai-context`) during project creation wizard flow
- **Timeout**: **45 seconds** (NFR-07 binding — UNCHANGED from R5)
- **Output contract**: `$choices`-constrained per existing pre-fill schema (UNCHANGED)

## User messages (verbatim — synthetic per ADR-015)

```
User: [no chat message — pre-fill is wizard-triggered, not chat-triggered]

Wizard state:
- Step 1 of CreateProjectWizard: user has entered project title
  "Q3 2026 SOC2 Compliance Initiative"
- useAiPrefill hook fires automatically with title + entityContext

BFF request:
POST /api/workspace/project/prefill
{
  "projectTitle": "Q3 2026 SOC2 Compliance Initiative",
  "tenantId": "<tenant>",
  "userId": "<oid>"
}
```

## Expected response shape (per outputSchema — task 035 migration)

```json
{
  "type": "object",
  "properties": {
    "projectType": { "type": "string", "enum": ["compliance", "litigation", "transactional", "advisory", "..."] },
    "priority": { "type": "string", "enum": ["low", "medium", "high", "critical"] },
    "estimatedDuration": { "type": "string", "enum": ["short", "medium", "long"] },
    "suggestedTags": { "type": "array", "items": { "type": "string" } },
    "confidence": { "type": "number" }
  }
}
```

**NFR-07 binding (verified)**: the `$choices`-constrained enum fields must match the existing
contract verbatim. Tests in task 035 confirm byte-identical schema compatibility with R5.

## Actual response (snapshot — synthetic)

```json
{
  "projectType": "compliance",
  "priority": "high",
  "estimatedDuration": "medium",
  "suggestedTags": ["soc2", "compliance", "q3", "audit-prep"],
  "confidence": 0.91
}
```

Total round-trip time: typically 6-12 seconds (well under the 45s NFR-07 timeout ceiling).

## NFR-07 regression assertions (task 035)

| Assertion | Status |
|---|---|
| `ProjectPreFillService.GetSuggestionsAsync` signature unchanged | ✅ |
| `useAiPrefill` React hook signature unchanged | ✅ |
| 45s timeout configuration unchanged | ✅ |
| `$choices`-constrained output schema unchanged | ✅ |
| `Workspace:ProjectPreFillPlaybookId` config key unchanged | ✅ |

## Pillar coverage exercised

| Pillar | Exercised? | Surface |
|---|---|---|
| 4 (PlaybookExecutionEngine FK chain) | ✅ | Action FK now traverses cleanly post-task-024 |
| 5 (outputSchema on action) | ✅ | Migrated in task 035 with NFR-07 regression test passing |

## Comparison with matter-prefill

The project-prefill and matter-prefill playbooks share the same architectural shape:
- Same `useAiPrefill` hook
- Same 45s timeout
- Same `$choices`-constrained output contract
- Same wizard-trigger pattern (not chat-triggered)

The differences are entirely in the outputSchema (project-specific vs matter-specific enum
values) and the action's system prompt (Pillar 1 persona attached at action level).
