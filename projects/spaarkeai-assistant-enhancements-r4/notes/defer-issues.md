# Deferred Issues — spaarkeai-assistant-enhancements-r4

> Tracks deferred work + defects surfaced during the project. Per the deferral protocol (`/project-defer-issue-tracking`), each entry is ALSO filed as a GitHub Issue at PR time for team visibility. Entries with a `{URL}` placeholder are NOT yet filed.

---

## D-024-01 — No end-to-end contract test for the typed SSE `suggestions` event

| Field | Value |
|---|---|
| **Filed by** | task 024 (E2 eval cases), 2026-08-17 |
| **Surface** | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` (the `suggestions` SSE emission, ~:973–1039) |
| **What** | There is no end-to-end contract/integration test asserting the `ChatEndpoints` SSE `suggestions` event is emitted in the **typed two-kind** shape (`ChatSseSuggestionsData`/`ChatSseFollowupItem[]`) rather than the retired ungrounded free-string generator (`GenerateAndEmitSuggestionsAsync`, deleted in 021a). |
| **Why deferred** | Testing the streaming `suggestions` frame end-to-end requires the live-agent streaming harness that the deterministic golden-utterance eval suite deliberately avoids (the eval gate is mechanical — no live LLM). The typed shape is currently guarded structurally at three layers instead: the Action contract (`SuggestFollowupsAction_IsGroundedTypedTwoKindProposer_NoDeadEndFreeString`, task 024), the service (`AssistantSuggestionServiceTests`, 021a), and the client parse/render (SprkChat suggestion suites, 021b). The residual is the endpoint *wiring* (that ChatEndpoints calls the grounded proposer + emits the typed frame). |
| **Concrete failure guarded (§11 cost-of-doing-nothing)** | A regression that re-wired `ChatEndpoints` to emit an untyped free-string `suggestions` payload (or bypass `SuggestForConversationAsync`) would not be caught by a BFF endpoint test — only by the client parser dropping it at render time. |
| **Recommended fix** | An SSE contract test (or a focused unit over the extracted emission helper) asserting the `suggestions` frame carries `ChatSseFollowupItem[]` with `kind` set, emitted from the grounded proposer. Consider extracting the inline merge/emit block (`ChatEndpoints.cs` ~:983–1039) into a testable helper first. |
| **GitHub Issue** | {URL} |
