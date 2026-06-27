---
captured: 2026-06-18
persona: SYS-DEFAULT (sprk_aipersona row)
playbook: summarize-document-for-workspace@v1 (SUM-WS@v1)
note: "Captured per R6 Q10 (lightweight eval baseline; full harness deferred R7). Reference file for future eval harness consumption."
---

# SYS-DEFAULT persona × summarize-document-for-workspace playbook

## Persona used

- **Row name**: SYS-DEFAULT
- **System prompt source**: byte-identical to legacy `BuildDefaultSystemPrompt()`

## Playbook invoked

- **Playbook ID**: `summarize-document-for-workspace@v1`
- **Action FK**: Shares `SUM-CHAT@v1` action (per task 033 Option A decision — workspace playbook references shared chat action; differs only in node config `destination: "workspace"`)
- **Routing path**: Triggered from `SummarizeFilesWizard` (LegalWorkspace surface) → BFF `/api/workspace/files/summarize` endpoint → PlaybookExecutionEngine (Pillar 4)
- **Node config destination**: `"workspace"` (vs `"chat"` for SUM-CHAT) — the same action's output stream goes to a workspace tab via `DeliverOutput` node executor (Pillar 5 dedup at CapabilityRouter ensures one render per intent)

## User messages (verbatim — synthetic per ADR-015)

```
User: [click] "Summarize selected file(s)" — UI affordance in SummarizeFilesWizard
```

(The wizard is a UI surface; no slash-command input. The trigger is a structured
request: `POST /api/workspace/files/summarize` with `{ fileIds: [...], matterId, playbookId }`.)

## Expected response shape (per outputSchema)

Same `sprk_analysisaction.outputSchema` as SUM-CHAT (shared action per task 033):

```json
{
  "type": "object",
  "properties": {
    "tldr": { "type": "array", "items": { "type": "string" } },
    "summary": { "type": "string" },
    "entities": { "type": "object", "...": "..." },
    "openQuestions": { "type": "array", "items": { "type": "string" } }
  }
}
```

Schema-aware rendering (Pillar 5):
- `tldr` → bullet list in workspace tab
- `summary` → paragraph block
- `entities` → labeled key-value section
- `openQuestions` → bullet list
- `hasUserEdits` flag tracked on `WorkspaceTab.WidgetData.HasUserEdits` (task 057 affordance + task 058 Q8 conflict resolution)

## Actual response (snapshot — workspace tab)

The output renders as a new `WorkspaceTab` with `widgetType: "Summary"` and:

```typescript
{
  id: "<tab-guid>",
  widgetType: "Summary",
  widgetData: {
    kind: "Summary",
    tldr: [
      "Three-year MSA between Acme Corp and Beta LLP",
      "Scope: tech consulting; hourly rate fixed; expense pass-through capped",
      "Termination: 60 days notice; immediate for material breach"
    ],
    summary: "This engagement letter ...",
    entities: { people: [...], organizations: [...], dates: [...] },
    openQuestions: [...],
    hasUserEdits: false
  },
  sessionId: "<session-id>",
  tenantId: "<tenant-id>",
  visibleToAssistant: true,  // user-initiated → defaults true for wizard
  sourceProvenance: { source: "playbook", createdBy: "summarize-document-for-workspace@v1", createdAt: "..." },
  matterContext: { matterId: "<matter-id>", matterName: "Acme v. Beta" },
  isPinned: false,
  canEdit: true,  // user-created (vs canEdit:false for agent-dispatched)
  lastUserEditAt: null,
  createdAt: "...", updatedAt: "..."
}
```

## Pillar coverage exercised

| Pillar | Exercised? | Surface |
|---|---|---|
| 4 (PlaybookExecutionEngine) | ✅ | `ExecuteAsync(summarize-document-for-workspace@v1)` |
| 5 (output schema + node destination) | ✅ | Same action's outputSchema; node config `destination: "workspace"`; dedup ensures single render |
| 6a (workspace tab persistence) | ✅ | Result written to `WorkspaceTab` via `IWorkspaceStateService.PersistTabAsync` (Q4 hybrid: Redis hot + Cosmos durable on pin) |
| 6b (canEdit + provenance) | ✅ | `canEdit: true` for user-initiated; provenance captured |

## NFR-07 binding note

The wizard pre-fill flow (NFR-07) is **independent** of this playbook and was NOT modified in R6. The pre-fill hook signatures + 45s timeout + `useAiPrefill` hook remain unchanged per the binding.
