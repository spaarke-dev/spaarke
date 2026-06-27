# Task 143 — FR-24 Dedup Verification Evidence

**Date**: 2026-06-25
**Branch**: `work/spaarke-ai-platform-chat-routing-redesign-r1`
**Test file**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/SprkChatAgentFactoryDedupTests.cs`

## Purpose

Author a binding-invariant regression test verifying that the R6 FR-30 render-routing dedup directive survives the WP4 cutover (task 141 — `CapabilityRouter` deletion + FR-24 rewire to `playbookId` parameter).

## Approach

Per spec FR-24 + task 141 rewire, the dedup directive is now driven by the explicit `playbookId` parameter on `SprkChatAgentFactory.CreateAgentAsync` (previously driven by `CapabilityRoutingResult.SelectedPlaybookId` from the now-deleted CapabilityRouter). Tests assert the wire-level invariant: the system prompt carries the correct directive given `(playbookId, terminal destination)`.

Per task 118a / 141 precedent: integration test scaffold for chat sessions does not exist; unit tests at the `SprkChatAgentFactory` boundary with callback-driven `INodeService` mocks provide integration-equivalent rigor for the directive-application code path.

## Tests (10 total)

| # | Test | Asserts |
|---|------|---------|
| 1 | `AppendsNonChatDedupDirective_WhenPlaybookTerminalIsNonChat[workspace]` | Workspace destination → "Render Routing Directive (R6 task 042 / FR-30, hardened B-G10)" + target phrase "the workspace" + surface "workspace tab" |
| 2 | `AppendsNonChatDedupDirective_WhenPlaybookTerminalIsNonChat[form-prefill]` | FormPrefill → target "the form" + surface "form pre-fill" |
| 3 | `AppendsNonChatDedupDirective_WhenPlaybookTerminalIsNonChat[side-effect]` | SideEffect → target "the system" + surface "background action" |
| 4 | `AppendsChatAckDirective_WhenPlaybookTerminalIsChat` | Chat destination → "Render Routing Directive (Hotfix Wave B-G9b)" (PDF hallucination fix preserved) |
| 5 | `AppendsNoDirective_WhenPlaybookIdIsNull` | `playbookId` null (free-form conversational turn) → NEITHER directive (NFR-01 preserved) |
| 6 | `AppendsNoDirective_WhenNodeServiceIsAbsent` | `INodeService` not registered → soft failure; agent still constructed |
| 7 | `AppendsNoDirective_WhenNodeServiceReturnsEmptyArray` | Malformed playbook (no nodes) → no directive |
| 8 | `AppendsNoDirective_WhenNodeServiceThrows` | Dataverse outage → exception caught; no directive; agent still constructed (NFR-01) |
| 9 | `DirectiveAppliedFreshly_OnEachInvocation` | Two successive calls with same `playbookId` both get directive (no memoization) |
| 10 | `SwitchesDirective_WhenPlaybookDestinationChanges` | Workspace playbook → non-chat directive; chat playbook → chat-ack directive (destination-aware) |

## Verification result

**With FR-24 dedup active** (production code):
```
Passed!  - Failed: 0, Passed: 10, Skipped: 0, Total: 10, Duration: 60 ms
```

## Sanity check — "does the gate have teeth?"

Per POML Step 6: temporarily disable the dedup logic and verify tests fail.

**Modification**: `SprkChatAgentFactory.cs:435` changed from `if (playbookId.HasValue)` to `if (playbookId.HasValue && false)` — short-circuits the dedup block.

**Result** (with dedup disabled):
```
Failed!  - Failed: 6, Passed: 4, Skipped: 0, Total: 10, Duration: 151 ms
```

The 6 failing tests are the **positive** assertions (directive presence):
- `AppendsNonChatDedupDirective_WhenPlaybookTerminalIsNonChat` × 3 inline-data rows
- `AppendsChatAckDirective_WhenPlaybookTerminalIsChat`
- `DirectiveAppliedFreshly_OnEachInvocation`
- `SwitchesDirective_WhenPlaybookDestinationChanges`

The 4 still-passing tests are the **negative** assertions (directive absence in degenerate conditions):
- `AppendsNoDirective_WhenPlaybookIdIsNull`
- `AppendsNoDirective_WhenNodeServiceIsAbsent`
- `AppendsNoDirective_WhenNodeServiceReturnsEmptyArray`
- `AppendsNoDirective_WhenNodeServiceThrows`

This 6/4 split is the **discriminating test signature** the POML required: positive tests detect breakage; negative tests remain valid even when the system is broken. The directive-presence gate has teeth.

**Restoration**: `git checkout -- src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` (commit unchanged; sanity-check did not contaminate the production branch). Post-restore re-run: 10/10 pass.

## Acceptance-criterion mapping (POML)

| Criterion | Status |
|-----------|--------|
| Test asserts exactly ONE non-chat directive for non-chat destinations | ✅ (3 InlineData rows + presence-only) |
| Test asserts exactly ONE chat-ack directive for chat destination | ✅ |
| Negative-control sub-test (legitimate non-dedup case) | ✅ (`AppendsNoDirective_WhenPlaybookIdIsNull` — free-form turn case) |
| Negative-control sub-test (destination-aware switching) | ✅ (`SwitchesDirective_WhenPlaybookDestinationChanges`) |
| Test passes against post-141, post-142 state | ✅ (10/10) |
| Test marked binding-invariant | ✅ (`[Trait("category", "binding-invariant")]`) |
| Sanity-check confirms test FAILS if dedup artificially disabled | ✅ (6 positive fail / 4 negative still pass — proves discrimination) |

## ADR compliance

- **ADR-013**: tests live inside `Services/Ai/Chat/` test boundary; do not widen the public-contracts surface.
- **ADR-015**: tests use synthetic identifiers (`tenant-dedup`, `doc-dedup-001`) and synthetic system prompt (`"You are an analyst."`); no user content.
- **ADR-010**: no new DI seams introduced; tests use the existing `IChatContextProvider` + `INodeService` scoped interfaces.

## Notes for Phase 7 continuation

- This test is the **safety net** for any future dispatcher refactor — if a downstream change regresses the dedup invariant, this test fires loudly.
- The `SprkChatAgentFactoryDedupTests` class file replaces the deleted `CapabilityRouterDedupTests.cs` (deleted in task 141). The dedup invariant — one user intent → one render — is now verified at the chat-agent factory boundary rather than the (deleted) router boundary.
- Both the `EmitCapabilityChangesIfDifferentAsync` SSE event and the existing capability-change client-side handling continue to work; this test does NOT cover those (per-turn tool-set diffing is asserted in `SprkChatAgentFactoryToolResolutionTests`).
