# Task 082 — D-D-03 Soft Slashes — Evidence

> **Status**: ✅ Complete (frontend module + BFF Layer 0.5 pre-pass + tests). ConversationPane.tsx integration deferred to MAIN SESSION (parallel coordination with tasks 081/083).
> **Wave**: D-G1 (Phase D Wave 1 — parallel with 081 + 083)
> **Date**: 2026-06-18
> **POML**: `tasks/082-soft-slashes-intent-shortcuts.poml`
> **Rigor**: FULL
> **Author**: sub-agent (R6 Wave D-G1)

---

## Scope

Pillar 8 soft slashes (Q6 closed at 4): `/summarize`, `/draft`, `/extract-entities`, `/analyze`. Soft slashes do NOT bypass the LLM — they route THROUGH the agent with a prioritized intent signal so `CapabilityRouter` matches the correct playbook / tool on FIRST try (spec FR-50 + Phase D exit criterion 3).

Parallel coordination constraint: tasks 081/082/083 all touch `ConversationPane.tsx`. This sub-agent BUILT the SoftSlashRouter module + tests + BFF Layer 0.5 pre-pass + tests, but did **NOT** modify `ConversationPane.tsx`. The main session performs the unified integration after all three Wave D-G1 agents return.

---

## Files

### Created (frontend)

- `src/solutions/SpaarkeAi/src/components/conversation/SoftSlashRouter.ts` (~190 lines; pure synchronous decorator; exports `decorateBody`, `toCommandIntent`, `SoftSlashIntents`, `type CommandIntent`, `type DecoratedChatBody`)
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/SoftSlashRouter.test.ts` (~220 lines; 38 tests, all green)

### Created (BFF)

- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Capabilities/CapabilityRouterSoftSlashTests.cs` (~200 lines; 18 tests, all green)

### Modified (BFF — minimal Layer 0.5 pre-pass per POML)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/ICapabilityRouter.cs` — extended `RouteSync` + `RouteAsync` with optional `commandIntent` parameter (default null; backward compatible). Not in `PublicContracts/` (ADR-013 honored).
- `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs` — added `TryClassifySoftSlash` (Layer 0.5) + `SoftSlashIntentToCapabilityName` closed-vocabulary dictionary + 4 synthetic capability name constants + emits `context.decision_made` event per match (ADR-015 compliant).
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` — extended `CreateAgentAsync` with optional `commandIntent` param; forwards to `RouteAsync`.
- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/NullSprkChatAgentFactory.cs` — added same optional param for override compliance.
- `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` — added optional `CommandIntent` field to `ChatSendMessageRequest` DTO; `SendMessageAsync` plumbs through to `CreateAgentAsync`.
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryTests.cs` — updated 4 Moq setups + 3 Verify calls to add `It.IsAny<string?>()` for new optional param (signature compatibility).

### NOT modified (by design)

- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — main session integrates after all 3 Wave D-G1 agents return. See "Integration handoff" below.

---

## 4 Soft Slashes Wired

| Command | `commandIntent` value | Synthetic capability name (BFF) | Routes to |
|---|---|---|---|
| `/summarize` | `summarize` | `invoke_playbook_summarize` | invoke_playbook(SUM-CHAT@v1) via Pillar 3 + 4 |
| `/draft` | `draft` | `invoke_playbook_draft` | draft-intent flag; agent uses TextRefinement + WorkingDocument tools |
| `/extract-entities` | `extract-entities` | `invoke_handler_extract_entities` | EntityExtractorHandler (Pillar 2 Wave 2) |
| `/analyze` | `analyze` | `invoke_playbook_analyze` | analysis-intent flag; agent picks from analysis playbook list |

---

## BFF Payload Composition

`ChatSendMessageRequest` (the JSON body of `POST /api/ai/chat/sessions/{id}/messages`) now carries:

```
{
  "message": "/summarize #engagement-letter.docx",
  "documentId": "...",
  "attachments": [ ... ],          // existing FR-07 field — passthrough
  "commandIntent": "summarize"      // NEW — frontend-decorated by SoftSlashRouter (R6 FR-50)
}
```

Composition with references (FR-52) verified by frontend test `'/summarize #engagement-letter.docx'`:
- Parsed intent: `{ command:'/summarize', isSoftSlash:true, references:[{kind:'filename', value:'engagement-letter.docx', raw:'#engagement-letter.docx'}] }`
- After `SoftSlashRouter.decorateBody()`: body has `commandIntent: 'summarize'` AND original message text (which contains the `#filename` reference for task 083's resolver to consume in parallel).

The two decorations (082 commandIntent + 083 references) target DISJOINT fields on the body — they compose without conflict.

---

## Tests

### Frontend — `SoftSlashRouter.test.ts`

```
Test Suites: 1 passed, 1 total
Tests:       38 passed, 38 total
Time:        0.87 s
```

Coverage:
- Vocabulary integrity (Q6 closed at 4)
- 4 soft slashes → 4 intent values (table-driven)
- Case insensitivity (`/SUMMARIZE` → `summarize`)
- 11 non-soft-slash inputs → null (NFR-11)
- 4 soft slashes → body decorated with commandIntent
- 8 non-soft-slash inputs → body UNCHANGED (no commandIntent field present)
- FR-52 composition: `/summarize #engagement-letter.docx` + `/draft response to @opposing-counsel about #motion-to-dismiss` + attachments passthrough
- Purity: input body NOT mutated; new object returned every call; synchronous
- Defensive invariants: null command, mismatched isSoftSlash

### BFF — `CapabilityRouterSoftSlashTests.cs`

```
Passed!  - Failed: 0, Passed: 18, Skipped: 0, Total: 18, Duration: 15 ms
```

Coverage:
- 4 soft slashes → Layer 0.5 short-circuits to synthetic capability (RouteSync, table-driven)
- 4 soft slashes → Layer 0.5 short-circuits Layer 2 + Layer 3 too (RouteAsync, table-driven)
- null/empty/whitespace commandIntent → falls through to Layer 1 (NFR-11)
- 4 unrecognised commandIntent values → falls through to Layer 1 (defensive)
- Voice memory (Layer 0) takes priority over soft slash (Layer 0.5)
- Backward compat: 2-arg `RouteSync(msg, playbook)` still works (existing tests rely on this)
- Vocabulary integrity: exactly 4 mappings (`Q6` closed)

### Regression — existing CapabilityRouter + SprkChatAgentFactory tests

```
Passed!  - Failed: 0, Passed: 109, Skipped: 3, Total: 112
```

(3 skipped were pre-existing: `Layer1_DoesNotFalsePositive_OnNonKeywordMessages`, `Layer1_FullCorpus_DistributionSummary`, `RouteSync_FutureTightening_NonAnchoredRemember`.)

---

## ADR / NFR audit

| ADR / NFR | Status | Evidence |
|---|---|---|
| **ADR-013** (no new public-contract surface) | ✅ PASS | `ICapabilityRouter` is in `Services/Ai/Capabilities/`, NOT in `Services/Ai/PublicContracts/`. The `IInvokePlaybookAi` facade is unchanged. The BFF change is a deterministic internal lookup, not a new facade. |
| **ADR-015** (telemetry hygiene) | ✅ PASS | `context.decision_made` event carries `capabilityName = <synthetic config identifier>`, `decision = "soft_slash"`, `layer = "layer1"`. Zero user message content. `commandIntent` itself is closed-vocabulary (4 strings), set by client `SoftSlashRouter`, NEVER raw user text. Logger uses `commandIntent` in debug log too — closed vocabulary, Tier-1 safe. |
| **ADR-029** (BFF publish size) | ✅ PASS | Compressed publish ~44.7 MB vs ~45.65 MB baseline = ~-1 MB delta (well within ≤+5 MB R6 budget AND ≤60 MB ceiling). |
| **NFR-01** (conversational primacy) | ✅ PASS | Soft slash only adds an intent HINT; the agent still receives the full message + history and produces a conversational response. Layer 0.5 selects ONE synthetic capability; tool filtering downstream still leaves room for conversational answers when the LLM judges appropriate. |
| **NFR-11** (natural language still works) | ✅ PASS | When `commandIntent` is null (the common path), Layer 0.5 returns null and Layer 1 keyword scoring runs UNCHANGED. Frontend `decorateBody` returns the body verbatim for non-soft-slash inputs. Verified by 11 non-soft-slash test cases on the frontend + 7 null/unrecognised test cases on the BFF. |
| **Q6** (vocabulary closed at 4) | ✅ PASS | Frontend `SoftSlashIntents` array + `SOFT_SLASH_TO_INTENT` record + BFF `SoftSlashIntentToCapabilityName` dictionary all have exactly 4 entries. Closed-vocabulary tests assert this. |

---

## Integration handoff (FOR MAIN SESSION)

When the main session unifies `ConversationPane.tsx` after Wave D-G1 returns, apply the following:

### 1. Import statement

Add alongside the existing `parseCommandIntent` import (task 080):

```typescript
import { decorateBody } from './SoftSlashRouter';
```

(No type import needed unless ConversationPane explicitly types the chat body — the existing `Record<string, unknown>` shape is structurally compatible.)

### 2. Where the soft-slash decoration goes

The current `handleBeforeSendMessage` body (lines 1077-1107) parses the intent via `parseCommandIntent(messageText)` and currently void-suppresses it (capture-only per task 080). Task 081 will add the **hard-slash branch** (early-return after dispatch). The soft-slash branch goes **AFTER** the hard-slash branch (since hard slashes bypass the LLM and the soft slash needs the LLM call to proceed).

However — `handleBeforeSendMessage` is `onBeforeSendMessage` in `SprkChat.tsx`, which is INFORMATIONAL per the SprkChat contract (line 1086 of current ConversationPane). It cannot mutate the outbound body that SprkChat.tsx itself sends. The actual body is built inside `SprkChat.tsx` (lines 1024-1031: `const body: Record<string, unknown> = { message, documentId, ... }`).

**The soft-slash decoration must therefore live ALONGSIDE the body construction in `SprkChat.tsx`** — NOT in `handleBeforeSendMessage`. Two integration patterns are available; pick whichever the main session prefers:

**Pattern A (preferred, minimal):** Extend `SprkChat.tsx`'s body construction to call `decorateBody`. This requires touching shared `@spaarke/ui-components`. The diff:

```typescript
// In src/client/shared/Spaarke.UI.Components/src/components/SprkChat/SprkChat.tsx
// around line 1024:
import { decorateBody } from '<TBD-shared-location-or-pane-pass-through>';
// Or: pass commandIntent through a new prop `onBuildOutboundBody?: (body) => body`
//     that ConversationPane wires up.

const intent = parseCommandIntent(messageText);   // already exists in task 080 wiring path
const baseBody: Record<string, unknown> = { message: messageText, documentId };
if (chatAttachments.length > 0) {
  baseBody.attachments = chatAttachments.map(/* ... */);
}
const body = decorateBody(intent, baseBody as DecoratedChatBody);
startStream(`${baseUrl}/api/ai/chat/sessions/${session.sessionId}/messages`, body, getAccessToken);
```

**Pattern B (no shared lib change):** Expose `SoftSlashRouter` as a new optional `SprkChat` prop `onDecorateOutboundBody?: (body) => body` and pass `(body) => decorateBody(intent, body)` from `ConversationPane`. This pattern is cleaner from a shared-lib API standpoint but requires a small new prop on `SprkChat`.

Both patterns are minimal. Main session decides based on the parallel scope of task 083 (which may also need to decorate the body with resolved references).

### 3. handleBeforeSendMessage compatibility

The existing `handleBeforeSendMessage` (post-080) only OBSERVES the parsed intent. Tasks 081 (hard slash) + 082 (soft slash) + 083 (references) need different integration sites:

- **081 hard slash**: can intercept inside `handleBeforeSendMessage` (informational + side-effect host action — clear UI, navigate, etc.). May want to ALSO suppress the SprkChat send for some hard slashes; if so, see Pattern B above.
- **082 soft slash**: needs to decorate the OUTBOUND body, so requires `SprkChat.tsx`-level body construction (Pattern A or B above).
- **083 references**: similar to 082 — decorate the body. Likely shares Pattern A or B with 082.

### 4. Telemetry breadcrumb

The BFF Layer 0.5 pre-pass emits `context.decision_made` events with `decision: "soft_slash"`. The frontend execution-trace widget (Pillar 6c) will pick these up automatically — no extra wiring on the frontend.

---

## Build / publish

| Check | Result |
|---|---|
| `dotnet build src/server/api/Sprk.Bff.Api/` | PASS — 0 errors, 16 warnings (pre-existing) |
| `npx jest src/components/conversation/__tests__/SoftSlashRouter.test.ts` | PASS — 38/38 tests in 0.87s |
| `dotnet test --filter CapabilityRouterSoftSlashTests` | PASS — 18/18 tests in 15ms |
| `dotnet test --filter CapabilityRouter (existing) + SprkChatAgentFactoryTests` | PASS — 109/109 tests (3 pre-existing skips), no regressions |
| BFF publish-size delta | ≈ 0 MB (compressed ~44.7 MB vs ~45.65 MB baseline = ~-1 MB; well within ≤+5 MB R6 budget per NFR-02) |
| Frontend bundle | No change measured (module is small + only depends on existing CommandRouter types) |

---

## Downstream unblock

- 084 (composition integration test) — can now use both 081's hard slash + 082's soft slash + 083's references in the same outbound body.
- 087 (vertical-slice integration test) — the `/summarize` → Pillar 4 SUM-CHAT@v1 path is now BFF-deterministic on first try.
