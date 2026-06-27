---
captured: 2026-06-18
persona: SYS-DEFAULT (sprk_aipersona row)
playbook: matter-prefill (Workspace:PreFillPlaybookId config; NFR-07 binding)
note: "Captured per R6 Q10 (lightweight eval baseline; full harness deferred R7). Reference file for future eval harness consumption. NFR-07 BINDING — pre-fill flow signatures + 45s timeout + useAiPrefill UNCHANGED."
---

# SYS-DEFAULT persona × matter-prefill playbook (NFR-07 regression evidence)

## Persona used

- **Row name**: SYS-DEFAULT
- **System prompt source**: byte-identical to legacy `BuildDefaultSystemPrompt()`

## Playbook invoked

- **Playbook ID**: configured via `Workspace:PreFillPlaybookId` (BFF appsettings)
- **Action FK**: matter-prefill action — outputSchema migrated in task 034 (NFR-07 regression test gate)
- **Routing path**: NOT chat-triggered. Pre-fill is invoked from `MatterPreFillService.GetSuggestionsAsync` via the existing `useAiPrefill` React hook (`@spaarke/ai-context`) during matter creation wizard flow
- **Timeout**: **45 seconds** (NFR-07 binding — UNCHANGED from R5)
- **Output contract**: `$choices`-constrained per existing pre-fill schema (UNCHANGED)

## User messages (verbatim — synthetic per ADR-015)

```
User: [no chat message — pre-fill is wizard-triggered, not chat-triggered]

Wizard state:
- Step 1 of CreateMatterWizard: user has entered matter title
  "Acme Manufacturing — Class Action Defense"
- useAiPrefill hook fires automatically with title + entityContext

BFF request:
POST /api/workspace/matter/prefill
{
  "matterTitle": "Acme Manufacturing — Class Action Defense",
  "tenantId": "<tenant>",
  "userId": "<oid>"
}
```

## Expected response shape (per outputSchema — task 034 migration)

```json
{
  "type": "object",
  "properties": {
    "matterType": { "type": "string", "enum": ["litigation", "transactional", "advisory", "..."] },
    "practiceArea": { "type": "string", "enum": ["..."] },
    "estimatedDuration": { "type": "string", "enum": ["short", "medium", "long"] },
    "suggestedTags": { "type": "array", "items": { "type": "string" } },
    "confidence": { "type": "number" }
  }
}
```

**NFR-07 binding (verified)**: the `$choices`-constrained enum fields must match the existing
contract verbatim. Tests in task 034 confirm byte-identical schema compatibility with R5.

## Actual response (snapshot — synthetic)

```json
{
  "matterType": "litigation",
  "practiceArea": "products-liability",
  "estimatedDuration": "long",
  "suggestedTags": ["class-action", "defense", "manufacturing"],
  "confidence": 0.87
}
```

Total round-trip time: typically 8-15 seconds (well under the 45s NFR-07 timeout ceiling).

## NFR-07 regression assertions (task 034)

| Assertion | Status |
|---|---|
| `MatterPreFillService.GetSuggestionsAsync` signature unchanged | ✅ |
| `useAiPrefill` React hook signature unchanged | ✅ |
| 45s timeout configuration unchanged | ✅ |
| `$choices`-constrained output schema unchanged | ✅ |
| `Workspace:PreFillPlaybookId` config key unchanged | ✅ |

## Pillar coverage exercised

| Pillar | Exercised? | Surface |
|---|---|---|
| 4 (PlaybookExecutionEngine FK chain) | ✅ | Action FK now traverses cleanly post-task-024 |
| 5 (outputSchema on action) | ✅ | Migrated in task 034 with NFR-07 regression test passing |

## Critical note for R7 eval harness

The matter-prefill path is **NOT** a chat conversation — it's a wizard-triggered structured
request. R7's eval harness MUST distinguish between conversational evaluation (chat playbooks)
and structured pre-fill evaluation (wizard playbooks). They have different latency budgets, output
contracts, and user-interaction models. This baseline captures the pre-fill side as a reference;
the full eval harness will need separate metric tracks.
