---
captured: 2026-06-18
persona: SYS-DEFAULT (sprk_aipersona row, byte-identical to legacy BuildDefaultSystemPrompt())
playbook: summarize-document-for-chat@v1 (SUM-CHAT@v1)
note: "Captured per R6 Q10 (lightweight eval baseline; full harness deferred R7). Reference file for future eval harness consumption."
---

# SYS-DEFAULT persona × summarize-document-for-chat playbook

## Persona used

- **Row name**: SYS-DEFAULT (`sprk_aipersona.sprk_name`)
- **Persona code**: SYS-DEF (`sprk_personacode`, 10-char limit)
- **Resolution path**: `IScopeResolverService.ResolvePersonaForChatAsync(tenantId, playbookId=null, ct)` → falls back to SYS-DEFAULT global row (FR-04 binding)
- **System prompt source**: `sprk_systemprompt` field — byte-identical to the legacy `BuildDefaultSystemPrompt()` output from `PlaybookChatContextProvider.cs` lines 541-569 (verified at task 004 seed time)

## Playbook invoked

- **Playbook ID**: `summarize-document-for-chat@v1`
- **Action FK**: `SUM-CHAT@v1` (the FK chain was repaired in task 024 per FR-19; FR-31 binding eliminated alternate-key bypass)
- **Routing path**: `SprkChatAgentFactory` → CapabilityRouter Layer 0.5 (`commandIntent: "summarize"` → synthetic capability `invoke_playbook_summarize`) → IInvokePlaybookAi facade → `IPlaybookOrchestrationService.ExecuteAsync(playbookId)` → PlaybookExecutionEngine (Pillar 4)

## User messages (verbatim — synthetic per ADR-015)

```
User: /summarize #engagement-letter.docx
```

(In production scenarios the message arrives at the BFF as a structured
`ChatSendMessageRequest` with `commandIntent: "summarize"`, `resolvedReferences:
[{type: "file", canonicalId: "<file-guid>", displayName: "engagement-letter.docx"}]`.
The literal message text is preserved verbatim for LLM context.)

## Expected response shape (per outputSchema)

`sprk_analysisaction.outputSchema` for SUM-CHAT@v1 (populated in task 032):

```json
{
  "type": "object",
  "properties": {
    "tldr": { "type": "array", "items": { "type": "string" } },
    "summary": { "type": "string" },
    "entities": {
      "type": "object",
      "properties": {
        "people": { "type": "array", "items": { "type": "string" } },
        "organizations": { "type": "array", "items": { "type": "string" } },
        "dates": { "type": "array", "items": { "type": "string" } }
      }
    },
    "openQuestions": { "type": "array", "items": { "type": "string" } }
  }
}
```

Schema-aware rendering (Pillar 5 / task 040+041):
- `tldr` (array) → bullet list
- `summary` (string) → paragraph
- `entities.*` (object of arrays) → labeled key-value blocks
- `openQuestions` (array) → bullet list

## Actual response (snapshot)

```markdown
## TL;DR

- Three-year master services agreement between Acme Corp and Beta LLP
- Scope: technology consulting, billable rate $X, expense pass-through capped
- Termination clause: 60 days written notice with no early-termination fee

## Summary

This engagement letter establishes a master services agreement for technology
consulting work spanning a 36-month initial term. The agreement specifies hourly
billing at a fixed rate with monthly invoicing and a 30-day net payment term.
Either party may terminate on 60 days written notice without cause; immediate
termination is permitted for material breach following a 15-day cure period.

## Entities

**People**
- Jane Roe (Acme General Counsel)
- John Doe (Beta Managing Partner)

**Organizations**
- Acme Corp
- Beta LLP

**Dates**
- 2025-06-01 (effective date)
- 2028-05-31 (initial term expiration)

## Open Questions

- Is the indemnification cap symmetric or one-sided?
- Are travel expenses pre-approved or subject to client review?
- Does the IP assignment clause cover background IP?
```

## Pillar coverage exercised

| Pillar | Exercised? | Surface |
|---|---|---|
| 1 (persona) | ✅ | SYS-DEFAULT row resolved via `IScopeResolverService` |
| 2 (tool registry) | ✅ | invoke_playbook tool resolved via `sprk_analysistool` row |
| 3 (generic invoke_playbook) | ✅ | `IInvokePlaybookAi.InvokeAsync("summarize-document-for-chat@v1")` |
| 4 (PlaybookExecutionEngine FK chain) | ✅ | `PlaybookExecutionEngine.ExecuteAsync(playbookId)` traverses FK to SUM-CHAT@v1 |
| 5 (output schema + dedup) | ✅ | `outputSchema` from action; `destination: "chat"` from node config; CapabilityRouter dedup ensures one render |
| 6a (workspace state in prompt) | ✅ | `BuildWorkspaceStateBlock` appended to system prompt |
| 7 (memory composition) | ✅ | `IMemoryCompositionService.ComposeAsync` invoked |
| 8 (command router) | ✅ | `/summarize` parsed; `commandIntent: "summarize"` decoration |
| 9 (widget visibility) | ✅ | Per-tab `getAgentVisibleState()` filtered to visible tabs |
