# R2-052: Playbook Dispatch End-to-End Integration Findings

## Date: 2026-03-17

## Issues Found and Fixed

### 1. dialog_open SSE Parameter Format Mismatch (Critical)

**Issue**: The BFF `ChatSseDialogOpenData` record used `CodePage` and `Fields` as field names.
With `JsonNamingPolicy.CamelCase` serialization, these became `codePage` and `fields` on the wire.
The frontend `IChatSseEventData` expected `targetPage` and `prePopulateFields`.

**Impact**: dialog_open events from PlaybookDispatcher would arrive at the frontend but
`handleDialogOpenEvent` would see `data.targetPage` as undefined and ignore the event entirely.
The Code Page dialog would never open.

**Fix**: Renamed `ChatSseDialogOpenData` record parameters from `CodePage`/`Fields` to
`TargetPage`/`PrePopulateFields` so camelCase serialization produces `targetPage`/`prePopulateFields`
matching the frontend contract.

**Files changed**:
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` (record definition)
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs` (constructor call)

### 2. Missing `navigate` SSE Event Type on Frontend

**Issue**: The BFF `PlaybookOutputHandler` emits `navigate` events for `OutputType.Navigation`
playbooks, but the frontend had no handler for this event type. The `ChatSseEventType` union
did not include `'navigate'`, and `useSseStream` did not capture it.

**Fix (prior session)**: Added `'navigate'` to:
- `ChatSseEventType` union in `types.ts`
- `pendingActionEvent` type union in `types.ts` and `useSseStream.ts`
- SSE event capture logic in `useSseStream.ts` main loop
- `SprkChat.tsx` useEffect dispatch switch

Added new `INavigatePayload` type and `navigateToTarget()` function in `useActionHandlers.ts`
that handles both URL-based navigation (`Xrm.Navigation.openUrl`) and Code Page navigation
(`Xrm.Navigation.navigateTo` with `pageType: 'webresource'`).

**Fix (this session)**: The fallback/buffer-flush SSE parser block in `useSseStream.ts`
(secondary parsing loop around line 339) was still missing `'navigate'` from both the
condition check and the type union. Added `event.type === 'navigate'` to the condition
and `'navigate'` to the `as` type assertion to match the main parser block.

**Files changed**:
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/types.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useSseStream.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/useActionHandlers.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/hooks/index.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx`

### 3. Autonomous Dialog Path Missing action_success Event

**Issue**: The autonomous dialog path in `PlaybookOutputHandler` (requiresConfirmation=false)
emitted text + `plan_step_complete` events. The frontend `handleActionSuccessEvent` listens
for `action_success` events to show a Fluent v9 success toast. Without it, autonomous
playbook actions would complete silently with only a text message in the chat.

**Fix**: Added `action_success` SSE event emission in the autonomous path after the text
response. The event carries `actionId` and `message` fields matching the frontend
`IChatSseEventData` contract.

**Files changed**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookOutputHandler.cs`

### 4. Missing Action Confirm Endpoint

**Issue**: The frontend `dispatchConfirmedAction()` POSTs to
`/api/ai/chat/sessions/{sessionId}/actions/{actionId}/confirm` after the user clicks Confirm
in the ActionConfirmationDialog. This endpoint did not exist on the BFF.

**Fix**: Added stub endpoint `POST /sessions/{sessionId}/actions/{actionId}/confirm` with
`ActionConfirmRequest` and `ActionConfirmResult` records. Currently returns a success stub.
Future iterations will execute the action tool associated with the playbook.

**Files changed**:
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` (endpoint mapping + handler + records)

## End-to-End Flow Verification

### HITL Dialog Path (requiresConfirmation=true)
1. User sends: "send this analysis by email to John"
2. PlaybookDispatcher Stage 1: vector search matches email playbook
3. PlaybookDispatcher Stage 2: LLM extracts {recipient: "John"}
4. PlaybookOutputHandler: emits `dialog_open` SSE with targetPage="sprk_emailcomposer", prePopulateFields={recipient: "John"}
5. Frontend useSseStream: captures as pendingActionEvent
6. SprkChat useEffect: dispatches to handleDialogOpenEvent
7. openCodePageDialog: calls Xrm.Navigation.navigateTo with webresource + URL params

### Autonomous Path (requiresConfirmation=false)
1. User sends: "summarize this for the matter notes"
2. PlaybookDispatcher matches summarize playbook
3. PlaybookOutputHandler: emits text response + action_success SSE
4. Frontend: text appears in chat + success toast shown

### Navigate Path
1. User sends: "open the matter detail page"
2. PlaybookDispatcher matches navigation playbook
3. PlaybookOutputHandler: emits navigate SSE with targetPage or URL
4. Frontend: navigateToTarget opens via Xrm.Navigation

## NFR-04 Timing Assessment

The PlaybookDispatcher enforces a 2-second total budget:
- Stage 1 (vector search): 1.5s budget
- Stage 2 (LLM refinement): 0.5s budget
- High-confidence bypass: skips Stage 2 when single candidate scores >= 0.85

Timing validation requires live Dataverse + AI Search infrastructure. The code
enforces the budget via `CancellationTokenSource.CancelAfter()` at each stage
with fallback behavior on timeout.
