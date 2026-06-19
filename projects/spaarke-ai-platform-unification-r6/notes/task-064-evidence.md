# Task 064 evidence — SummarizationCompressionService

**Pillar / Spec ref**: R6 Pillar 7 / D-C-17 / NFR-10 — sliding-window compression
primitive. Folds the oldest M chat turns into a single System-role summary message
when the conversation exceeds the 8K system-prompt token budget. Foundation for
task 067 (hierarchical memory composition); task 068 wires it into
`SprkChatAgentFactory`.
**Wave**: C-G2 gap-fill (source pre-existed; tests added here).
**Date**: 2026-06-11.

## Implementation

Source files (already existed pre-gap-fill):
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/ISummarizationCompressionService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/SummarizationCompressionService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Memory/SummarizationCompressionOptions.cs`
- DI registration in `Infrastructure/DI/AnalysisServicesModule.cs` (lines ~480-506).

Test file added in this gap-fill:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Memory/SummarizationCompressionServiceTests.cs`

## Key design decisions

- **Soft-failure (P2 Quiet) posture**: returns `null` (NOT a thrown exception) when
  kill switch is off, input is empty / too small, LLM circuit is broken, or any
  unexpected exception bubbles. Caller (task 068) treats null as "skip compression —
  use raw window".
- **Defensive token budget**: caller-supplied `maxSummaryTokens` clamped to
  `[128, 1024]`. Post-call response is truncated at `maxSummaryTokens × CharsPerToken`
  to enforce the NFR-10 ~512-token reserved slot.
- **Input cap**: 65,000-character ceiling on formatted LLM input (~16K tokens) so a
  pathological caller can't blow the cheap-tier model's per-call budget.
- **Canonical output prefix**: every output starts with `"Summary of earlier
  conversation: "` so downstream prompt-assembly can pattern-match compressed slots.
- **Cancellation re-raised**: `OperationCanceledException` propagates (chat turn
  teardown); all other exceptions become silent null returns.

## Governance

- **ADR-010**: interface seam justified — task 068 will choose between real impl,
  Null-Object peer (R5 kill-switch), and unit-test fake. Registered as
  `AddScoped<ISummarizationCompressionService, SummarizationCompressionService>()`
  via `AnalysisServicesModule`; ZERO new `Program.cs` line.
- **ADR-013**: lives under `Services/Ai/Memory/`; injects `IOpenAiClient` directly —
  AI-internal collaborator, NO PublicContracts facade needed per the refined
  2026-05-20 ADR-013 boundary rule.
- **ADR-014**: stateless; caller owns per-tenant session cache. Tenant isolation is
  transitive — only the caller's tenant messages enter this service.
- **ADR-015**: input messages contain raw user content, but the OUTPUT is the
  LLM-generated summary ONLY (raw text never echoed back). Caller (task 068)
  discards originals after substituting the returned summary.
- **§F.1 asymmetric-registration**: registration is INSIDE the compound
  `(Analysis:Enabled && DocumentIntelligence:Enabled)` gate — matches the consumer
  (task 068's `SprkChatAgentFactory` is inside the same compound gate via
  `NullSprkChatAgentFactory` peer). Kill-switch posture is intrinsic (returns null).

## Test coverage

6 tests (all pass):
1. `CompressAsync_ReturnsNull_WhenKillSwitchOff` — Enabled=false short-circuits before LLM.
2. `CompressAsync_ReturnsNull_WhenInputEmpty` — empty list → null.
3. `CompressAsync_ReturnsNull_WhenInputBelowMinimum` — single message below
   `MinMessagesToCompress (2)`.
4. `CompressAsync_ReturnsSummaryMessage_OnHappyPath` — asserts System-role + canonical prefix.
5. `CompressAsync_ReturnsNull_WhenLlmThrowsGenericException` — P2 Quiet posture.
6. `CompressAsync_ReturnsNull_WhenCircuitBroken` — `OpenAiCircuitBrokenException` → null.
7. `CompressAsync_TruncatesResponse_WhenExceedingTokenBudget` — NFR-10 budget enforcement.

## Build status

- Source build: clean.
- Test build: clean.
- Tests: 7/7 pass (count includes the truncation test).
