# 078 — FR-45 line invariant + FR-27 single-pipeline audit

**Task**: 078 — Unify MemoryCompositionService with PlaybookChatContextProvider
**Mode**: MVP-cut (Q5b) — peer wave 4-E tasks 076, 077, 079 DEFERRED; this task reduced to audit + minimal-clarification refactor + invariant lock-in.
**Date**: 2026-06-23
**Author**: Claude (sub-agent invoked by main session)
**Consumed by**: task 080 (FR-45 binding-invariant regression test)

---

## 1. FR-45 line invariant — current location

The `_matterMemoryService.ToSystemPromptFragmentAsync(...)` invocation in `PlaybookChatContextProvider.cs`:

| Pre-task-078 | Post-task-078 (after XML-doc additions) |
|---|---|
| **line 627** (matches architecture §11.1) | **line 679** (within `AppendMatterMemoryAsync` try-block) |

The line number **shifted by 52 lines** purely from the XML doc-block added to `GetContextAsync` in step 4 below. The invocation site, signature, and call shape are otherwise unchanged.

**Line number is NOT load-bearing**. Architecture §11.1 references line 627 as a historical pin; the test (added in step 5) asserts the call EXISTS, not its position. Future XML-doc edits or refactors that shift the line MUST NOT break the test.

Verify locally:
```
Grep "_matterMemoryService\.ToSystemPromptFragmentAsync" src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs
```

---

## 2. FR-27 single-pipeline audit — findings

**Result**: ZERO duplicate composition pipelines found in production code.

| Code path | Composer | Status |
|---|---|---|
| `PlaybookChatContextProvider.GetContextAsync` | Persona → knowledge → entity → matter-memory → document summary | Sole per-turn composer ✅ |
| `SprkChatAgentFactory.CreateAgentAsync` | Resolves `IChatContextProvider` from per-turn scope, calls `GetContextAsync`, then appends suffix blocks (Active Capabilities, Session Files manifest, formatting directive, Workspace State) | Consumer of provider, NOT a parallel composer ✅ |
| `MemoryCompositionService.ComposeAsync` | R6 Pillar 7 4-layer composer (recent/compressed/retrieved/pinned) | **DEFINED but NOT WIRED in production** — only invoked from `MemoryCompositionServiceTests.cs`. No production caller exists today. Awaiting task 079 integration (deferred in MVP). |

**Grep evidence**:
- `ComposeAsync` invocations across `src/server/api/Sprk.Bff.Api/`: 0 (only the definition in `MemoryCompositionService.cs`)
- `ComposeAsync` invocations across tests: 26 (all in `MemoryCompositionServiceTests.cs` — verifies the composer in isolation)
- `BuildPromptAsync` / `BuildSystemPromptAsync` / `ComposePromptAsync`: 0 hits anywhere
- `GetContextAsync` invocations in `SprkChatAgentFactory.cs`: 1 (line 208, the single seam)

**Conclusion**: the FR-27 single-pipeline invariant holds today. No remedial refactor needed.

---

## 3. Scope picked: **A) Minimum change**

Per the MVP rationale in `notes/handoffs/032-loader-gap-and-036-bundling.md` (and the user's Q5b cut decision):

- Tasks 076 (LayeredContextCardBuilder), 077 (TrustFrameInstructionInjector), 079 (composition target) are DEFERRED. Their service types do not exist in the repo.
- Without those peers, task 078's coordinator role collapses to: **document the seam + lock in the invariants for the future plug-in tasks**.

The minimum-change implementation:
1. XML doc-block on `GetContextAsync` declaring the FR-27 single-pipeline contract + FR-45 binding invariant + future plug-in points for 076/077/079.
2. Two source-text regression tests pinning both invariants.

This is consistent with the POML's binding invariant clause: *"Binding invariant: `PlaybookChatContextProvider.cs:627` MatterMemoryService invocation continues to fire. Task 080 enforces this via regression test."* Task 080 escalates with stricter assertions; task 078 ships the baseline invariant pin.

---

## 4. Files changed

### `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs`
- **Lines 83–139** (new XML `<remarks>` block on `GetContextAsync`): documents the FR-27 single-pipeline contract, the FR-45 binding invariant, and the three future plug-in points (trust frame after persona / cards after knowledge enrichment / dynamic suffix after matter-memory).
- No behavior change. No new service injection. No removed call. Pure doc-comment addition.

### `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookChatContextProviderTests.cs`
- **Lines 329–404** (new region): two FR-27 + FR-45 binding-invariant regression tests + a helper for resolving repo-relative source paths.
  - `GetContextAsync_PreservesMatterMemoryServiceInvocation_FR45` — asserts the source contains `_matterMemoryService.ToSystemPromptFragmentAsync(` (FR-45 binding).
  - `GetContextAsync_IsSinglePerTurnCompositionSeam_FR27` — asserts `SprkChatAgentFactory.cs` resolves `IChatContextProvider` from the per-turn scope and calls `GetContextAsync` — pins the consumer wiring so a parallel composer in the factory fails the test.
- Helper `LocateBffSource(params string[])` mirrors the canonical pattern in `MatterPreFillServiceTests` (NFR-07 source-text invariants).
- No existing test was modified or relocated.

---

## 5. Future plug-in point notes (deferred tasks 076 / 077 / 079)

When the deferred peers ship, they MUST plug into `GetContextAsync` at these insertion points (documented inline in the XML `<remarks>`):

| Task | Service | Insertion point in `GetContextAsync` | Tier (architecture §6.2) |
|---|---|---|---|
| **077** | `ITrustFrameInstructionInjector` | After persona resolution (line ~270 today, AFTER `systemPrompt = action.SystemPrompt;`), BEFORE `EnrichSystemPrompt`. | Static prefix (cacheable) |
| **076** | `ILayeredContextCardBuilder` | After `EnrichSystemPrompt`, BEFORE `AppendEntityEnrichment`. | Static prefix (cacheable) |
| **079** | `IMemoryCompositionService.ComposeAsync` | After `AppendMatterMemoryAsync` (the FR-45 point), AFTER document-summary load, BEFORE final `ChatContext` return — formatted as the per-turn dynamic suffix. FR-42 pinned-never-drops invariant is owned by the composer itself. | Dynamic suffix (per-turn) |

**Critical**: when these peers wire in, the FR-27 test must still pass — meaning the integration MUST keep the single seam in `PlaybookChatContextProvider` and MUST NOT introduce a parallel composer in `SprkChatAgentFactory`. The FR-45 test must still pass — meaning the matter-memory call MUST remain (it can move within the file, but it CANNOT be deleted).

---

## 6. Surprises / observations

1. **MemoryCompositionService is not yet wired into production.** It has 26 unit tests but zero production callers. The "two parallel pipelines" framing in the POML prompt (line 19) assumes the composer was already wired somewhere — it isn't. The single-pipeline state therefore exists by default of non-integration, not by deliberate construction. Task 079 (deferred) will wire it in; this task only declares the contract that task 079 must honor.

2. **The XML `<inheritdoc />` previously on `GetContextAsync` is now followed by a sibling `<remarks>` block.** Per C# XML-doc conventions this is supported: the inherited summary applies, and the new `<remarks>` is additive. No tooling change needed.

3. **`MatterPreFillServiceTests` is the established pattern for source-text invariant tests in this codebase.** This task mirrored that pattern (locate source via assembly path walk → `File.ReadAllText` → `.Should().Contain(...)`). Task 080's stricter regression test should follow the same pattern.

4. **The FR-27 test asserts on `SprkChatAgentFactory.cs` AS WELL AS** `PlaybookChatContextProvider.cs`. This is intentional: the FR-27 invariant is a relationship between two files (one composer, one consumer), not a property of one file. The test fails loudly if anyone adds a competing composer to the factory.

---

## 7. Build / test status

**Not run by this sub-agent** per task instructions ("Do NOT build, test, publish, commit, or push — the main session does that"). Main session should run:
- `dotnet build src/server/api/Sprk.Bff.Api/` to verify the XML doc addition compiles cleanly.
- `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --filter "FullyQualifiedName~PlaybookChatContextProvider"` to verify the new regression tests pass (and existing tests don't regress).
