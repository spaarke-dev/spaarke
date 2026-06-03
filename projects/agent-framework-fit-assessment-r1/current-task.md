# Current Task — Agent Framework Fit Assessment R1

> Tracks ACTIVE task only. History lives in `TASK-INDEX.md` and per-task `.poml` files.

---

**Active task**: task 001 complete (2026-06-03); main session to handle task transition
**Next task**: task 002 (parallel sibling) — main session tracks; task 003 follows after Phase 1 finishes

---

## Last completed task

**Task 001** — Inventory Spaarke AI code surfaces (S1-S4 + S8 catch) — completed 2026-06-03

### Files produced
- `projects/agent-framework-fit-assessment-r1/notes/01-spaarke-ai-surfaces-inventory.md` (top-level finding + 5 surface sections + 7 cross-cutting observations)

### Critical findings to carry forward to task 004 (decision matrix)
1. **`Microsoft.Agents.AI` package is referenced in `Sprk.Bff.Api.csproj` but ZERO source files use it** (grep verified) — clean evidence for the "half-adopted" SPEC §2 framing
2. **S1 SprkChat is the ONLY surface using Extensions.AI primitives** (`IChatClient`, `AIFunction`, `ChatResponseUpdate`, `FunctionCallContent`). S2/S3/S4 use `IOpenAiClient` wrapper or `OpenAI.Chat` SDK directly
3. **Middleware decorates `ISprkChatAgent` not `IChatClient`** — Spaarke missed the canonical `chatClient.AsBuilder().Use(...).Build()` idiom; this is the biggest single migration vector for S1
4. **Two-client pattern (default + "raw")** drives Spaarke's compound-intent gate; partially subsumed by upstream Tool Approval (task 000 baseline §4 P5/P9/P12)
5. **Two S8 surfaces discovered**: `SessionSummarizationService` and `CapabilityRouter` — both use `IChatClient` outside `Chat/Builder/Jobs`; recommend folding into S1 perimeter for task 004
6. **GitHub Issue #6268** carried forward as RED FLAG on S1 lift-to-ChatClientAgent recommendation

### Prior completed task

**Task 000** — Refresh primary sources baseline — completed 2026-06-03

### Files produced (task 000)
- `projects/agent-framework-fit-assessment-r1/notes/00-primary-source-baseline.md` (9 sections, 34 citable primary sources)

### Critical findings to carry forward
1. New upstream SHA: `afa7834e` (2026-06-03, "1.9 release" — shipped TODAY at BUILD 2026)
2. Agent Framework 1.0 GA April 2026 → platform is production-ready (not preview)
3. **GitHub Issue #6268** (.NET ChatClientAgent multi-tool streaming bug) — RED FLAG for S1 SprkChat adoption; assessment must condition S1 ADOPT on resolution
4. Sample catalog massively expanded vs. 2026-05-14 curated (4 → 50+ samples across 5 categories)
5. Tool Approval is now a framework feature (Pages 5 + 9 + 12) — partly subsumes Spaarke's CompoundIntentDetector + UseFunctionInvocation/raw-client split
6. Workflow HITL (`RequestPort`/`RequestInfoEvent`) is in agent-framework itself, not exclusively Foundry — affects S5 Foundry-overlap analysis

### Citation discipline for tasks 001-007
- Every feature claim cites a URL from notes/00 §4 (Learn pages) or §5 (Devblogs) with fetched date
- No claims citing only the curated `knowledge/agent-framework/` snapshot — that's orientation only
- §10 Sources appendix in the final assessment document is mandatory

---

## Next task (Phase 1 — parallel-group A)

Both tasks 001 and 002 depend only on 000 and run in parallel:

- **Task 001**: Inventory Spaarke AI code surfaces (S1-S4 + S8 catch) — reads `src/server/api/Sprk.Bff.Api/Services/Ai/` and adjacent paths
- **Task 002**: Inventory non-BFF AI touchpoints (S5-S7) — reads `knowledge/foundry-agent-service/`, `projects/ai-m365-copilot-integration/`, `projects/ai-spaarke-insights-engine-r1/`

Both produce structured notes files (`notes/01-*` and `notes/02-*`) for task 004's per-surface decision matrix.
