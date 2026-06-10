# Wave B-G10 — Second Walkthrough Fixes (B10 + B12 + B11)

> **Date**: 2026-06-10
> **Trigger**: User re-walkthrough on Spaarke Dev after Wave B-G9 deploy surfaced 3 more issues
> **Status**: Partial (B-G10a + B-G10b landed; B11 in investigation)

---

## Issues found

### B10 — PDF hallucination on workspace destination

**Symptom**: Upload PDF → workspace summary tab renders correctly (Wave B-G9a + c1 fixes
worked) BUT the Assistant chat shows a two-message hallucination:

> "I see you have uploaded the document '22874_11420624_10-23-2008_CTNF.PDF.' I will analyze
> it and provide a summary. Please give me a moment.
> I attempted to retrieve the content from the document '...PDF,' but it appears the content
> is not currently accessible for extraction. Could you please try uploading the document
> again or provide some details about the document's content or type?"

**Root cause**: Wave B-G9b's `BuildChatDestinationAckDirective` explicitly forbids
extractability speculation, but it fires ONLY for chat-destination playbooks. The
PDF→workspace summarize path resolves to a workspace-destination playbook, so the OTHER
directive (`BuildDedupDirective`) fires. That directive only said "don't emit analysis
content inline" — it did NOT forbid speculation about extraction.

**Fix (B-G10a)**: Extended `BuildDedupDirective` with the same "do NOT speculate about
extractability / readability" wording from `BuildChatDestinationAckDirective`. Both
directives now forbid the hallucination pattern. NFR-01 preserved (single-sentence
acknowledgment still allowed).

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (~line 1820)

---

### B11 — Summary tab still single + replaced (Wave B-G9c2 NOT effective in production)

**Symptom**: User cleared `DevTools → Application → Storage → Clear site data` AND tried
InPrivate Chrome. Still: summarizing two different files produces ONE Summary tab; second
file's content replaces first's.

**Status**: Under investigation by sub-agent. Code review of the diff shows:
- `executeSummarizeIntent.ts` Step 3a dispatches `widget_load` with unique `correlationId =
  streamId` and per-run `tabDisplayName`
- `ConversationPane.tsx` line 1169 changed `streamId: chatSessionId` → `streamId: undefined`
  so each invocation generates a fresh UUID via `generateStreamId()`
- `WorkspacePane.tsx` `widget_load` handler at line 619 calls `manager.addTab(...)` which
  creates a NEW tab with unique id

Code path looks correct on paper. Sub-agent is verifying:
1. Whether the deployed bundle actually contains the B-G9c2 source markers
2. Whether `executeSummarizeIntent` is the actual execution path for the user's workflow
3. Whether `widget_load` is actually firing with unique correlationId per invocation
4. Whether a downstream layer collapses tabs of same widgetType

**Possible diagnoses** (sub-agent will narrow):
- (A) Bundle didn't pick up source — needs hard rebuild + redeploy
- (B) Different execution path bypasses B-G9c2 fix
- (C) Downstream layer suppresses duplicate widget_load events
- (D) Some other root cause

---

### B12 — Chat-pane LLM output formatting too verbose + followup-card orchestration

**Symptom (12a)**: When user clicked the followup card "Explain the main conclusions",
the LLM returned heavily-structured markdown with many ## / ### headings, deep bullet
nesting, and generous spacing between paragraphs/sections. User wants something more
compact "like the playbook output" with TL;DR first.

**Symptom (12b architectural)**: User raised the question:
> "for the follow-up cards, how are these determined and what playbook or how are these
> constructed? they're good but should be within our orchestration particularly when they
> are substantive (versus reverting to a general LLM output)"

**Root cause (12a)**: Followup cards are SprkChat `predefinedPrompts` — clicking sends
the prompt text as a regular user message. The LLM responds via the normal chat path
with no formatting guidance, defaulting to verbose multi-level markdown.

**Fix (B-G10b)**: Added `BuildCompactFormattingDirective()` to `SprkChatAgentFactory`,
appended to ALL chat-turn system prompts. Specifies:
- Prefer short paragraphs over headings
- At MOST one heading level (no ###)
- Bullet nesting capped at 2 levels
- TL;DR (2-3 sentences) for substantive responses (>150 words)
- No blank lines between adjacent bullets in same list
- Skip bold inside bullets unless naming a defined term

Presentation-only directive; does NOT change content. NFR-01 preserved.

**File**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs`

**B12b — DEFERRED TO R7 (per user decision 2026-06-10)**:
Routing substantive followup cards through playbook orchestration (instead of raw LLM
chat) is non-trivial — needs a "when is a followup substantive enough to route to a
playbook" decision layer. Per Pillar 8 (Command Router) design + the soft-slash concept,
this is conceptually compatible but architectural work. Tracked as R7 backlog.

---

## Wave B-G10 dispatch outcome

- **B-G10a (B10 workspace directive extractability)**: ✅ Edit applied, build clean
- **B-G10b (B12a compact formatting)**: ✅ Edit applied, build clean
- **B-G10c (B11 per-run tabs)**: 🔄 Under sub-agent investigation
- **B12b (followup-card orchestration)**: ⏭️ Deferred to R7

Phase B exit-gate still HELD pending B11 root cause + fix + re-deploy + clean re-walkthrough.

## Files modified (B-G10a + B-G10b)

- `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs`
  - `BuildDedupDirective`: appended extractability-speculation prohibition (~+8 lines)
  - NEW `BuildCompactFormattingDirective` method (~+25 lines)
  - Call site appended to `context.SystemPrompt` after Session Files manifest (~+12 lines)

## Build/test
- BFF build: 0 errors, 16 baseline warnings
- Unit tests: not yet run (will run after B11 fix consolidates)

## R7 backlog (this session)
- B12b: substantive followup-card playbook orchestration (Pillar 8 alignment)
- Pillar 7 / Memory UI revisits (Q7 scope expansion)
- Persona seed-row content polish (these directives should eventually move to the SYS-
  persona seed row's `sprk_systemprompt` per Pillar 1 data-driven design; in B-G10 they
  live in factory code for hotfix expediency)

---

## B-G10c — Widget schema-aware fallback (post-stream-completion-but-not-JSON)

### Third-walkthrough finding

After Wave B-G10 a+b deployed + SpaarkeAi rebuilt with B-G9c2, user re-walked and got:
- `tldr: Malformed JSON: Unexpected token 'T', "The intern"... is not valid JSON`
- `entities: Malformed JSON: Unexpected token 'o', "organizati"... is not valid JSON`

### Root cause

The server emits VALUE content per field via `field_delta` SSE events (per R5 task 006
spike — Azure OpenAI streams properties in declaration order). The widget receives the
VALUE content NOT the JSON syntax wrapping it. So at streaming_complete, the accumulated
`tldr` content is the plain bullet text (e.g., "The international..."), not the JSON
literal `["The international...", "..."]`. Strict `JSON.parse` fails.

R5's `splitListContent` was forgiving — JSON-array first, newline next, comma next,
single-item last. R6 tasks 040 + 041's strict schema-aware path was added in parallel,
so the strict path is now hit but the streamed content doesn't match.

### Fix

`StructuredOutputStreamWidget.tsx`: when schema-aware `parseArrayOfString` returns an
error, fall back to legacy `splitListContent` (same lenient behavior R5 had). When
schema-aware `parseObject` returns an error, try `{...content}` wrap-and-retry; on final
failure, render the raw content as a paragraph (`data-display-hint=schema-object-raw-fallback`)
so the user sees the content rather than an error.

Existing 23 unit tests for 040 + 041 valid-JSON paths still pass. 5 tests that
SPECIFICALLY verified error-surface behavior were updated to verify the new graceful
fallback (filename: `StructuredOutputStreamWidget.test.tsx` lines 240+ and 530+).

### B-G10d — Skip empty followup suggestions when dedup-ack fires

Workspace-destination playbooks (B-G9b/B-G10a directive) constrain the chat-side LLM to
a single-sentence ack. The follow-up `GenerateAndEmitSuggestionsAsync` LLM then has no
substantive text to base suggestions on, so it generates nothing useful (or empty).

**R6 fix**: Skip suggestion generation when `fullResponse.Length < 150`. Honest empty
state instead of meaningless suggestions.

**R7 architectural home** (added 2026-06-10 per user feedback):
1. Add `sprk_followups` JSON column on `sprk_analysisaction` declaring the action's natural
   followup affordances: `[{label, playbookId, parameterMapping}]`.
2. `DeliverOutput` node executor emits a `followups` SSE event alongside the widget.
   SprkChat already has chip-rendering infrastructure (`SprkChatSuggestions`).
3. Followup click → `invoke_playbook(playbookId, parameters)` via existing Pillar 3
   dispatch. Click becomes a proper orchestrated playbook execution, not a generic LLM
   chat turn. Aligns with Pillar 8 "card-as-intent".

This R7 design supersedes B12a's "tighten the formatting" patch — once followups route
through playbooks, the playbook handles output styling per its outputSchema and the
generic chat-side `BuildCompactFormattingDirective` becomes the FALLBACK style for
non-orchestrated turns only.

### B13 — Followup cards missing — SAME root cause as B-G10d

User's "no followup cards displayed" is the same root cause: dedup-ack + suggestion-LLM
empty signal → no suggestions emitted. B-G10d makes the silence explicit (skips the call
rather than producing useless output).
