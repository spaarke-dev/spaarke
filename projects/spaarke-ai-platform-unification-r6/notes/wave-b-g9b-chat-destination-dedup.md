# Hotfix Wave B-G9b — Chat-Destination Dedup (PDF Hallucination Fix)

**Severity**: HIGH
**Date**: 2026-06-10
**Owner**: R6 BFF
**File touched**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (1 file, +~75 lines, 0 deletions)
**Tests touched**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterDedupTests.cs` (+7 test methods, 8 cases incl. theory)

---

## 1. Symptom + Root Cause

### Symptom (R6 Phase B walkthrough on Spaarke Dev)

When a user uploads a **PDF** and asks the chat to summarize it:

1. The LLM responds with a hallucinated message like *"It appears the attached document does not contain extractable text content for me to summarize directly..."*
2. A few seconds later, the structured summary appears in chat (and in some cases also in the workspace Summary tab — duplicate fire pattern).
3. For `.doc` / `.txt` uploads: NO hallucinated message — clean single response.

### Root cause

The R6 task 042 (FR-30) CapabilityRouter dedup directive in `SprkChatAgentFactory.cs` applies a system-prompt suppression directive **ONLY when the playbook destination is NON-chat** (workspace / form-prefill / side-effect). The reasoning at task-042 time: chat-destination playbooks render INLINE in chat, so the LLM's parallel free-form text was treated as a benign / sometimes-useful narration.

That reasoning breaks for **async-text-extracted formats** (PDF, scanned images):

- The Document Intelligence extraction pipeline is async — at LLM-invocation time, the document body is empty / partial.
- The LLM, seeing no extractable text, hallucinates *"I can't extract this PDF"*.
- For `.doc` / `.txt` the text is synchronously available, so no hallucination.

This is the **chat-destination side of the same R5 Gap A duplicate-fire pattern** task 042 closed for non-chat destinations.

---

## 2. Fix — Condition Extension + New Directive Builder

### Before (factory call site, line ~450)

```csharp
if (destination.HasValue && destination.Value != Models.Ai.NodeDestination.Chat)
{
    var directive = BuildDedupDirective(destination.Value);
    if (!string.IsNullOrEmpty(directive))
    {
        context = context with { SystemPrompt = context.SystemPrompt + directive };
    }
}
```

### After

```csharp
if (destination.HasValue && destination.Value != Models.Ai.NodeDestination.Chat)
{
    var directive = BuildDedupDirective(destination.Value);
    if (!string.IsNullOrEmpty(directive))
    {
        context = context with { SystemPrompt = context.SystemPrompt + directive };
    }
}
else if (destination.HasValue && destination.Value == Models.Ai.NodeDestination.Chat)
{
    // Hotfix Wave B-G9b — apply chat-destination ack directive so the LLM emits a
    // brief acknowledgment instead of hallucinating for async-extracted formats (PDF).
    var chatAckDirective = BuildChatDestinationAckDirective();
    if (!string.IsNullOrEmpty(chatAckDirective))
    {
        context = context with { SystemPrompt = context.SystemPrompt + chatAckDirective };
    }
}
```

### Telemetry adjustment

The pre-existing log line emitted `directiveApplied = (destination != Chat)`. After the hotfix, a directive IS applied for chat destinations too, so the boolean now reads `destination.HasValue` (true whenever any destination — chat or otherwise — resolved).

### New directive builder

A new `BuildChatDestinationAckDirective()` method was added (internal static, sibling to `BuildDedupDirective`). It is intentionally a **separate method** so:

- `BuildDedupDirective(NodeDestination)` keeps its established contract (returns empty for Chat; non-empty for workspace / form-prefill / side-effect) — pre-existing tests 7-10 / 13-14 / 16-18 stay valid verbatim.
- The chat-destination ack lives behind a method whose name (`ChatDestinationAck`) makes the intent obvious at the call site.
- Future refactors that want to widen / narrow the chat-destination case only touch one method.

The directive wording (key differences from the non-chat directive):

| Aspect | Non-chat directive (`BuildDedupDirective`) | Chat ack directive (`BuildChatDestinationAckDirective`) |
|---|---|---|
| Where playbook renders | "to {target} ({surface})" — elsewhere | "inline in this chat conversation" — same surface |
| What LLM should do | "respond with a SINGLE-SENTENCE acknowledgment ONLY" | Same: "SINGLE-SENTENCE acknowledgment ONLY" |
| What LLM must NOT do | "Do NOT emit the analysis content inline" | "Do NOT attempt to analyze, summarize, extract, or describe the document content yourself" + "Do NOT speculate about whether the document is extractable / readable / contains text" |
| NFR-01 framing | "follow-up turns are unaffected — respond conversationally as normal" | Same (verbatim) |

---

## 3. NFR-01 Conversational-Primacy Justification

NFR-01 binds: *"The chat surface is conversationally primary; do not silence the LLM."*

The chat-destination ack directive **does NOT silence** the LLM — it instructs a **single-sentence acknowledgment** (e.g., *"Working on that now…"* or *"I'll summarize that for you now."*). Per the SKILL.md framing of NFR-01 (and the existing non-chat directive's wording), a single-sentence ack IS still conversational — just terse and **not hallucinating about content the LLM hasn't seen yet**.

Further NFR-01 preservation:

- **Directive applies ONLY when `SelectedPlaybookId != null`** — the router is confident. Free-form / ambiguous / refinement turns do NOT trigger the directive (this is enforced upstream by the existing `IsConfident && SelectedPlaybookId.HasValue` gate at line 430-432).
- **Directive does not stick across turns** — each turn re-evaluates router resolution; the next free-form turn after a playbook turn responds normally.
- **Test 21 (`PreservesNFR01ConversationalPrimacy`)** asserts the directive contains "SINGLE-SENTENCE acknowledgment" + "follow-up" (so the LLM doesn't continue silencing).

---

## 4. Test Coverage Added

Tests 19-25 added to `CapabilityRouterDedupTests.cs` (7 new test methods):

| # | Test name | Coverage |
|---|---|---|
| 19 | `BuildChatDestinationAckDirective_EmitsNonEmptyDirective` | Smoke: chat case is no longer no-op |
| 20 | `BuildChatDestinationAckDirective_NamesInvokePlaybookTool` | Directive references `invoke_playbook` tool name (task 023 / D-A-15 binding) |
| 21 | `BuildChatDestinationAckDirective_PreservesNFR01ConversationalPrimacy` | Directive contains "SINGLE-SENTENCE acknowledgment" + "follow-up" |
| 22 | `BuildChatDestinationAckDirective_ForbidsExtractabilityHallucination` | Directive contains "extract" + "Do NOT" — the explicit symptom this hotfix targets |
| 23 | `BuildChatDestinationAckDirective_WordingDiffersFromNonChatDirective` | Chat ack wording is distinct from non-chat directive; mentions "chat conversation" |
| 24 | `BuildDedupDirective_Chat_StillReturnsEmpty_AfterHotfix` | Legacy contract of `BuildDedupDirective(Chat)` preserved (separate method) |
| 25 | `EndToEnd_ChatDestination_AckDirectiveAppliedToPreventPdfHallucination` | End-to-end: router resolves chat-destination playbook + ack directive is non-empty + mentions "extract" |

Pre-existing tests 1-18 still pass unchanged.

---

## 5. Build + Test Outputs

### Build

```
dotnet build src/server/api/Sprk.Bff.Api/ -nologo -v q
```

Result: **0 errors**, 16 warnings (all pre-existing, unrelated). Time: 16.20s.

### Tests

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~CapabilityRouterDedup" --logger "console;verbosity=minimal"
```

Result: **Passed! - Failed: 0, Passed: 32, Skipped: 0, Total: 32, Duration: 24 ms**

(Total = 18 pre-existing + 7 new test methods; new `[Theory]` cases yield 32 total assertions.)

Additional run — broader SprkChatAgentFactory tests (sanity check no regression):

```
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~SprkChatAgentFactory" --logger "console;verbosity=minimal"
```

Result: **Passed! - Failed: 0, Passed: 41, Skipped: 0, Total: 41, Duration: 140 ms**

---

## 6. ADR / Constraint Compliance

- **NFR-01 conversational primacy** — directive instructs SINGLE-SENTENCE ack, NOT silence. Applies only to confident playbook-routed turns; free-form / refinement turns unaffected. ✅
- **NFR-13 / NFR-07 / NFR-08** — safety pipeline, pre-fill flows, and node executors are NOT touched. The hotfix is a system-prompt enrichment only. ✅
- **ADR-013 facade boundary** — directive logic stays inside `Services/Ai/Chat/`. The new method is `internal static` on `SprkChatAgentFactory` (same class as the existing dedup builder). No public-contracts widening. ✅
- **ADR-015 telemetry** — log line continues to emit playbookId + destination + directiveApplied boolean only; no user content. ✅
- **R5 Gap A** — the hotfix closes the chat-destination half of the duplicate-fire pattern task 042 closed for non-chat destinations. ✅

---

## 7. Commit-Message Fragment

```
fix(r6, B-G9b): chat-destination dedup ack directive — eliminates PDF hallucination

When a user uploads a PDF and a chat-destination playbook (e.g., summarize-
document-for-chat@v1) resolves, the LLM previously had no directive constraining
its parallel free-form generation. For async-text-extracted formats (PDF, scanned
images) the LLM saw an empty document body at invocation time and hallucinated
"I can't extract this PDF" before the playbook's structured summary arrived.

This hotfix extends the R6 task 042 (FR-30) CapabilityRouter dedup pattern to
chat-destination playbooks. When SelectedPlaybookId is set AND destination is
chat, the factory now applies a NEW BuildChatDestinationAckDirective() that
instructs the LLM to emit a single-sentence acknowledgment and explicitly forbids
speculation about extractability. The pre-existing BuildDedupDirective method is
unchanged; tests 1-18 pass verbatim. NFR-01 conversational primacy is preserved
(single-sentence ack is still conversational — just terse).

Coverage: 7 new test cases in CapabilityRouterDedupTests.cs (32 total cases pass).
Build: 0 errors. ADR-013 facade boundary intact (Services/Ai/Chat/ only).
```

---

## 8. Stop-and-Surface Findings

None of the four stop-and-surface triggers fired:

1. **Factory's destination determination complexity** — already simple: `ResolvePlaybookTerminalDestinationAsync` reads `INodeService` once per turn; existing logic preserved.
2. **NFR-01 silence risk** — directive uses SINGLE-SENTENCE ack wording + `IsConfident && SelectedPlaybookId.HasValue` gate ensures non-playbook turns see no directive. Tests 21 + 24 enforce.
3. **Surface outside OWN list** — only `SprkChatAgentFactory.cs` + `CapabilityRouterDedupTests.cs` modified.
4. **Upstream PDF text extraction issue** — DI flow runs asynchronously by design; the playbook executor (`AiAnalysisNodeExecutor` → `SUM-CHAT@v1`) handles async extraction transparently. The LLM's parallel free-form text was the symptom, not the missing extraction. Fix is correctly at the directive layer.

---

## 9. Outstanding (Not in This Hotfix's Scope)

- **Widget-side fix (Wave B-G9a)** — duplicate render in workspace Summary tab when chat-destination playbook fires: owned by sister hotfix wave (different file ownership).
- **Document Intelligence ready-signal** — making the chat-agent invocation block until extraction completes is a deeper architectural change. The directive layer is the correct minimal fix and matches the existing R6 task 042 pattern.
