# Prompt-Assembly Baseline — Measured Per-Slice Token Counts (Task AIR2-002)

> **Purpose**: Fixes the FR-B-05 ContextEnvelope token budgets against a MEASURED baseline
> of r1's as-built prompt assembly, per spec FR-P0-02 / design D-M2. Consumed by task 054
> when it sets the binding ContextEnvelope budgets.
>
> **Status**: Measured (see "Measurement method" below) — not a source-only estimate.
> **Date**: 2026-07-08. **Task**: AIR2-002.

---

## 0. Critical deviation from the task's anchors — read this first

The POML anchored this task on `OrchestratorPromptBuilder.cs:44` as "the natural
measurement seam." **That class is dead code in the live chat path.**
`IOrchestratorPromptBuilder.BuildSystemPrompt` is registered in DI
(`AiChatModule.cs`) and unit-tested (`OrchestratorPromptBuilderTests.cs`,
`OrchestratorPromptBuilderBudgetTests.cs`), but **no production call site invokes it**
(verified: zero matches for `.BuildSystemPrompt(` outside its own definition and tests).
It appears to have been built for `chat-routing-redesign-r1` task 141 / FR-22 and never
wired into the live turn.

The REAL single per-turn composition seam — per `PlaybookChatContextProvider.cs`'s own
FR-27 binding docstring (line ~132: *"this method is the single per-turn system-prompt
composition seam for chat... NO other component composes the per-turn prompt"*) — is:

1. **`PlaybookChatContextProvider.GetContextAsync`** — persona/Action system prompt →
   knowledge-scope enrichment (inline knowledge, skill instructions) → entity enrichment
   (host-record identity) → matter-memory append → returns `ChatContext.SystemPrompt`.
2. **`SprkChatAgentFactory.CreateAgentAsync`** — appends suffix blocks onto that
   `SystemPrompt`: Active Capabilities, Session Files manifest, Compact-formatting
   directive, Workspace State, FR-24 dedup directive, Action-Honesty directive,
   Current-Date directive.
3. **`ChatEndpoints.cs` (SendMessageAsync)** — separately appends
   `ChatHistoryManager.BuildLedgerOutputsContext` as a `[System]` message onto the
   `history` list (NOT part of `SystemPrompt`, NOT under the shared budget tracker).

Per `<steps mode="directional">`, this task measured the REAL seam (above), not the
dead-code anchor. This is the single most load-bearing finding of the task — task 054
and any future prompt-assembly work should treat `PlaybookChatContextProvider` +
`SprkChatAgentFactory` + `ChatHistoryManager.BuildLedgerOutputsContext` as the
composition surface, not `OrchestratorPromptBuilder`.

**Component-justification note**: r1 already built `IPromptBudgetTracker` /
`PromptBudgetTracker` (`Services/Ai/Memory/`, R6 Pillar 7 task 068) — a shared, scoped,
per-turn 8K token-budget tracker with no-content (ADR-015-compliant) granted/truncated
telemetry, already wired into most of `PlaybookChatContextProvider`'s and
`SprkChatAgentFactory`'s blocks (`persona`, `knowledge-inline`, `skill-instructions`,
`entity-enrichment`, `active-capabilities`, `session-files-manifest`,
`workspace-state`, `matter-memory`). **No new instrumentation was added** — the existing
tracker already satisfies NFR-07 for every layer it covers, and extending it further was
out of scope for a measurement task. Three blocks bypass the tracker entirely (see §3).

---

## 1. Measurement method

**Measured, not source-only estimated**, via a temporary harness (xUnit test, deleted
immediately after the run — not retained in `tests/**`, not classified at `/test-diet`)
that called the REAL production methods directly with representative fixtures:

- `PlaybookChatContextProvider.GetContextAsync(...)` — constructed with mocked
  `IScopeResolverService` / `IPlaybookService` / `IDataverseService` / `IMatterMemoryService`
  (same fixture pattern as the existing `PlaybookChatContextProviderTests.cs`), a
  representative record-context `ChatHostContext` (`EntityType=matter`,
  `EntityName="Acme Corp v. Beta LLC"`, `PageType=entityrecord`), one inline knowledge
  fragment, one skill fragment, and a matter-memory fragment shaped exactly like the
  example in `IMatterMemoryService.ToSystemPromptFragmentAsync`'s own doc-comment.
  Isolated each sub-block's real contribution by re-invoking the SAME method with
  successively more fixture data and diffing output lengths (no parallel assembly path
  — same method, different inputs, per ADR-040).
- `SprkChatAgentFactory.BuildCurrentDateDirective(...)`, `SideEffectHonestyDirective`,
  `BuildCompactFormattingDirective()` — called/read directly (pure/constant production
  members, zero DI required).
- `ChatHistoryManager.BuildLedgerOutputsContext(...)` — called directly with one
  representative `SessionOutput` (a classification TL;DR, ~230 chars payload).

**Token conversion**: chars/4 heuristic — the SAME heuristic already used throughout
this codebase for token budgeting (`OrchestratorPromptBuilder.EstimateTokens`,
`PlaybookChatContextProvider`'s own doc comments, `PromptBudgetTracker`'s callers). No
real tokenizer (e.g. tiktoken) is used anywhere in this pipeline today for budget
purposes, so chars/4 is "consistent with the deployed model" in the sense of matching
the codebase's existing convention — not a claim of tokenizer-exact precision.

**What is measured vs. representative-fixture vs. estimated**:

| Component | Status |
|---|---|
| `BuildCurrentDateDirective` output | **Measured exactly** — real production text, fixed sample date |
| `SideEffectHonestyDirective` | **Measured exactly** — real production constant string |
| `BuildCompactFormattingDirective` output | **Measured exactly** — real production constant string |
| `BuildLedgerOutputsContext` output | **Measured exactly** for the fixed template + one representative payload |
| Entity-enrichment block | **Measured exactly** for the real template + a representative record name/id |
| Persona / knowledge-inline / skill-instructions | Measured via the real rendering code, but the SOURCE TEXT is a representative fixture (playbook-authored content varies per Action; not pulled from a live Dataverse Action record) |
| Matter-memory fragment | Measured via the real rendering code, but the SOURCE TEXT mirrors the interface's own documented example (not a live Cosmos read) |
| User-turn message, persisted conversation history (`session.Messages`) | **Estimated** (see §5) — not exercised by the harness; too variable for a single representative run and out of scope for a static-fixture harness |

---

## 2. Per-slice measured table (representative record-context turn)

| ContextEnvelope slice | Component | Tokens (chars/4) | Source |
|---|---|---:|---|
| **Environment** | `BuildCurrentDateDirective` | 111 | Measured (exact) |
| **Environment total** | | **111** | |
| **User** | current-turn message | ~20–55 (est.) | Estimated |
| **User total** | | **~40 (est.)** | |
| **Business** | Persona / standing instructions (Action.SystemPrompt) | 76 | Measured, representative fixture |
| **Business** | Knowledge-inline (RAG inline fragment) | 47 | Measured, representative fixture |
| **Business** | Skill instructions | 27 | Measured, representative fixture |
| **Business** | `SideEffectHonestyDirective` (unconditional, tool-bearing sessions) | 779 | Measured (exact) |
| **Business** | `BuildCompactFormattingDirective` (unconditional) | 189 | Measured (exact) |
| **Business total** | | **1,118** | (+ tracker-gated Active Capabilities / dedup-directive / session-files-manifest when present, each typically small — not separately measured, see §3) |
| **Record-memory** | Entity enrichment (host-record identity) | 93 | Measured (exact template + representative name) |
| **Record-memory** | Matter-memory fragment (mid-density fixture) | 64 | Measured, representative fixture |
| **Record-memory total** | | **157** | |
| **Conversation** | `BuildLedgerOutputsContext`, ONE representative output | 220 | Measured (exact template + representative payload) |
| **Conversation** | Persisted `session.Messages` history | ~400–750 (est., 3–5 turn-pairs) | Estimated |
| **Conversation total (normal turn)** | | **~620–970 (mixed measured+estimated)** | |
| **TOTAL (normal turn, this fixture)** | | **~2,025–2,410** | vs. ceiling ≤4,200 |

---

## 3. FR-B-05 budgets — estimate vs. measured

| Slice | FR-B-05 a priori estimate | Measured (representative normal turn) | Delta | Verdict |
|---|---:|---:|---:|---|
| Environment | ≤50 | **111** | **+61 (+122%)** | **EXCEEDS estimate on every turn** (deterministic, unconditional — not a corner case) |
| User | ≤300 | ~40 (est.) | −260 (est.) | Comfortable margin for typical short turns; long dictated messages could approach the ceiling (not measured) |
| Business | ≤1,200 | **1,118** (measured subtotal; realistic total with tracker-gated blocks likely **1,150–1,300+**) | ~0 to +100 | **At or over ceiling on realistic turns** — two unconditional directives (Honesty 779 + CompactFormatting 189 = 968) alone consume 81% of the entire budget before any playbook content |
| Record-memory | ≤600 | 157 (this fixture; interface docs target 200–500 for memory alone at higher fact density) | comfortable margin | Under budget; richer matters could approach ~590–650 |
| Conversation | ≤2,000 | ~620–970 on THIS normal turn; **structurally unbounded up to ~8,000** (see §4) | **normal-turn: under; worst-case: +6,000 (+300%)** | **Structural risk** — see escalation below |
| **Ceiling** | ≤4,200 | ~2,025–2,410 (this turn); **could reach ~9,000+ combined with worst-case Conversation** | normal: under; worst-case: **+4,800 (+114%)** | Structural risk mirrors Conversation |

---

## 4. Escalation — per the task's own trigger (surfaced, not silently resolved)

Per this task's `<escalation>` clause: *"If the measured baseline shows any slice
already exceeding its FR-B-05 estimate by a wide margin... STOP and surface it."*

🔔 **Two findings meet this bar; a third is a structural risk requiring task 054's
attention before the budgets are finalized.**

1. **Environment already exceeds its estimate on every turn, unconditionally.**
   `BuildCurrentDateDirective` (111 measured tokens) is appended to every tool-bearing
   session's system prompt with zero variability. The FR-B-05 estimate of ≤50 was
   evidently written before this directive existed (added G-P3 UAT round-5, 2026-07-07).
   **Recommendation for task 054**: raise the Environment budget to ~150 (headroom above
   the measured 111), or trim the directive text if a smaller Environment slice is a hard
   constraint.

2. **Business is at or over its ceiling on realistic turns, dominated by two
   unconditional directives that bypass the shared budget tracker.**
   `SideEffectHonestyDirective` (779) + `BuildCompactFormattingDirective` (189) = 968
   tokens land on every tool-bearing turn regardless of `IPromptBudgetTracker` state —
   they are NOT gated like Active Capabilities / Workspace State / Session Files
   manifest are. Add any nontrivial playbook persona + knowledge + skills (this
   fixture's representative content alone: 150 tokens) and Business is already at 1,118;
   realistic production playbooks with richer instructions will exceed 1,200.
   **Recommendation for task 054**: either raise the Business budget to ~1,400–1,500, or
   treat the two unconditional directives as a separate reserved "protocol floor"
   sub-budget (~1,000 tokens) that is NOT expected to leave headroom for playbook content
   within the same 1,200 ceiling.

3. **Conversation has NO enforced ceiling and can structurally reach ~4x its budget.**
   `ChatHistoryManager.BuildLedgerOutputsContext` windows up to `MaxContextOutputs = 8`
   ledger outputs, each capped at `MaxContextPayloadChars = 4,000` chars (~1,000 tokens
   at chars/4) — a theoretical ceiling of **~8,000 tokens** for this ONE block alone,
   appended as a `[System]` history message OUTSIDE `IPromptBudgetTracker`'s 8K
   accounting entirely (it is not a `SystemPrompt` fragment; it never calls
   `TryReserve`). Combined with persisted `session.Messages` history, a session with
   several large stored outputs (e.g. multiple long classification/summary results from
   event-path automation) could push Conversation well past 2,000 and the overall
   ceiling well past 4,200 — with **zero telemetry or truncation feedback**, since this
   path is invisible to the tracker that everything else in the pipeline uses.
   **This is the most consequential finding of the task.** On the ONE representative
   turn measured here (single small output), Conversation came in comfortably under
   budget (~720–970) — the risk is structural/worst-case, not present on every turn,
   which is why this is flagged as a required task-054 input rather than a hard block
   on this task's completion.
   **Recommendation for task 054**: either (a) wire `BuildLedgerOutputsContext`'s
   caller into `IPromptBudgetTracker` (a `"conversation-ledger"` layer, consistent with
   every other block) so it participates in the shared budget and produces truncation
   telemetry, or (b) tighten `MaxContextOutputs` / `MaxContextPayloadChars` so the
   structural ceiling for this block cannot exceed the Conversation slice budget on its
   own. This also bears on **FR-P0-03 / NFR-04 (cache stability)** — task 003's
   determinism check should be read alongside this finding.

None of these findings required this task to modify production code, and none was
"resolved" here — per the escalation clause, they are surfaced for task 054's binding
budget-setting decision (and, for finding 3, possibly an ADR-040 follow-up on the ledger
context path).

---

## 5. What was NOT measured (explicitly estimated) and why

- **User-turn message** and **persisted `session.Messages` history**: genuinely
  variable content dependent on real user behavior; no representative single value is
  defensible without live traffic data. Estimated from typical short-question /
  turn-pair lengths seen elsewhere in this codebase's fixtures and comments. Task 054
  should treat these as the least-certain inputs to the Conversation/User budgets.
- **Active Capabilities, Session Files manifest, Workspace State, FR-24 dedup
  directive**: all four ARE gated by `IPromptBudgetTracker` in production (verified by
  reading `SprkChatAgentFactory.cs`), so they self-limit against the shared 8K ceiling
  and were not separately harnessed (would have required standing up
  `IWorkspaceStateService`, `DynamicCommandResolver`, and the dedup-directive lookup —
  disproportionate DI surface for a 1-day measurement task). Each is individually small
  (tens to ~150 tokens) based on their source templates; they are already
  self-truncating by construction, unlike the three unconditional directives and the
  ledger-outputs-context block flagged in §4.

---

## 6. BFF hygiene / governance (per `.claude/constraints/bff-extensions.md`)

- **Placement Justification**: no new component was added. This task measured the
  existing `Services/Ai/Chat/` + `Services/Ai/Memory/` pipeline via a temporary,
  deleted-after-use test harness. Zero production `.cs` files were modified.
- **Publish-size delta**: measured via `dotnet publish -c Release src/server/api/Sprk.Bff.Api/`
  → **44.34 MB compressed (excl. PDBs)**, vs. the ~45.87 MB excl.-PDBs baseline stated in
  root CLAUDE.md §10 (2026-07-08). Delta is effectively **0** (the ~1.5 MB variance is
  build-condition noise, not a code change — this task made zero production `.cs` edits).
  Well under the ≤60 MB ceiling and the ≥+5 MB single-task escalation threshold.
  `dotnet build src/server/api/Sprk.Bff.Api/ -c Release` also succeeds (0 errors, 0
  warnings from this task's scope).
- **New HIGH-severity CVE**: none — no package references were added or changed.
- **NFR-07 (no-content telemetry)**: no NEW instrumentation was added to production
  code. The existing `PromptBudgetTracker` (already covering most measured layers)
  was independently verified (by reading its source) to log identifiers/counts only
  (`layer`, `requestedTokens`, `grantedTokens`, `usedBudget`, `remaining`,
  `totalBudget`, `sessionId`, `tenantId`, `decision` — all enum/numeric/GUID fields,
  never fragment bodies) per its own ADR-015 BINDING doc-comment. No prompt content
  is logged anywhere touched by this task.
- **ADR-040 (no parallel assembly path)**: confirmed — the measurement harness called
  the REAL production methods (`PlaybookChatContextProvider.GetContextAsync`,
  `SprkChatAgentFactory`'s static directive builders, `ChatHistoryManager.
  BuildLedgerOutputsContext`) directly, with representative fixture inputs. No second
  composer, cache, or assembly path was introduced. The harness file was deleted
  immediately after the measurement run; `git status` shows zero residual diff from
  this task.

---

## 7. Anchors used (superseding the POML's stated anchors)

| Anchor | File:line | Role |
|---|---|---|
| Single per-turn composition seam | `Services/Ai/Chat/PlaybookChatContextProvider.cs:129` (`GetContextAsync`, FR-27 docstring ~line 132) | Business + Record-memory slice source |
| Suffix directive appends | `Services/Ai/Chat/SprkChatAgentFactory.cs:65` (`SideEffectHonestyDirective`), `:129` (`BuildCurrentDateDirective`), `:1390` (`BuildCompactFormattingDirective`) | Environment + Business (unconditional) |
| Conversation-ledger source | `Services/Ai/Chat/ChatHistoryManager.cs:302` (`BuildLedgerOutputsContext`) | Conversation slice source (per POML's original anchor — confirmed correct) |
| Shared budget tracker (existing, reused, NOT extended) | `Services/Ai/Memory/PromptBudgetTracker.cs`, `IPromptBudgetTracker.cs` | Covers most Business/Record blocks; does NOT cover the 3 unconditional directives or the ledger-outputs-context |
| Dead-code anchor (POML's stated seam — NOT the live path) | `Services/Ai/Chat/OrchestratorPromptBuilder.cs:44` | Registered + tested, zero production call sites |
