# Lessons Learned - Spaarke AI Platform Unification R2

> **Project**: spaarke-ai-platform-unification-r2
> **Completed**: 2026-05-17
> **Tasks**: 86/86 across 7 phases

---

## What Went Well

### Architecture Decisions

- **PaneEventBus multi-subscriber pattern**: The unified event bus with multi-subscriber support enabled clean cross-pane communication without tight coupling. Citation clicks, text selection, playbook changes, and tab switches all flow through a single bus with type-safe event contracts. This pattern is an ADR candidate.

- **Widget registry pattern**: Separating WorkspaceWidgetRegistry and ContextWidgetRegistry with a shared WorkspaceWidget interface made widget migration systematic. The registry pattern enabled dynamic widget instantiation from SSE events without hard-coding widget types.

- **Data-refreshed restore (D-08)**: Choosing to restore widgets by re-fetching fresh data rather than replaying stale snapshots was the right call. It adds a small latency cost but eliminates an entire class of stale-data bugs and keeps restore logic simple.

- **Three-pane lifecycle stages**: The 4-stage lifecycle (Welcome, Active, Review, Complete) gave each pane clear rendering rules per stage, reducing conditional logic and making the shell predictable.

- **Parallel wave execution**: Organizing 86 tasks into 18 parallel waves with explicit dependency tracking allowed backend and frontend streams to run concurrently. Phase 4 (frontend) started independently of Phases 2-3 (backend), significantly reducing total elapsed time.

### Process

- **Task decomposition at 86 tasks**: The granularity was right. Tasks were small enough to complete in single sessions but large enough to be meaningful units of work.

- **POML task format**: Machine-readable task definitions with explicit dependencies, acceptance criteria, and file targets made execution systematic and recoverable after context compaction.

---

## What Was Harder Than Expected

### SSE Event Routing Migration (R1 to R2)

Migrating from R1's SSE event handling to R2's 23-type structured event contract required careful coordination. The existing SprkChat component had deeply embedded SSE parsing logic that needed to be preserved while the new AiSessionProvider took over stream management. The dual-provider period during migration was error-prone.

### Widget Serialize/Restore Testing

Testing serialize/restore across 18+ widgets revealed edge cases in widget state that were not obvious from the interface definitions. Widgets with async data dependencies (e.g., document viewers, entity info) required careful handling of the restore-then-refresh sequence. The test harness needed to mock both serialization and the subsequent data refresh.

### Pre-existing Test Project Build Errors

The test projects had 182 pre-existing build errors in unrelated test files. This made it impossible to run `dotnet test` for validation during development. Test verification had to rely on compilation of the main projects and manual inspection rather than automated test runs.

---

## Key Decisions That Should Inform Future Work

### Single-LLM-Call Invariant (D-01)

The decision to always make exactly one LLM call per user turn (with the capability router pre-selecting tools) was a strong architectural constraint that simplified reasoning about latency, cost, and error handling. Future R3 agent work should preserve this invariant or explicitly document why it was relaxed.

### Write-Through Cosmos Persistence (D-06)

Write-through (persist on every state change) was chosen over idle-flush (persist after inactivity). This adds per-turn write latency (~10-20ms) but guarantees no data loss on unexpected disconnection. The tradeoff is worth it for a legal operations platform where conversation history has compliance value.

### Safety Pipeline Pre/Post-LLM Split

Splitting safety into pre-LLM (prompt shields, privilege check) and post-LLM (groundedness, citation verification, confidence scoring) stages was essential. Pre-LLM checks prevent prompt injection before it reaches the model. Post-LLM checks annotate responses without blocking delivery (stream + retroactive annotation per D-03).

### ISprkAgent Abstraction

The ISprkAgent interface was designed to be R3-ready. When multi-agent orchestration arrives, DirectOpenAiAgent can be replaced or composed without changing the chat endpoint or session management layers.

---

## Technical Debt

### Pre-existing Test Project Build Errors

182 build errors exist in unrelated test files across the test projects. These pre-date R2 and were not addressed because they are outside the project scope. They should be triaged and fixed in a dedicated cleanup effort before R3.

### WelcomePanel Partially Deprecated

The original WelcomePanel component was replaced by the Stage 1 welcome experience in the three-pane shell. The old component file still exists but is no longer referenced from the main shell. It should be removed or archived in a cleanup pass.

---

## ADR Candidates

### PaneEventBus Pattern

The PaneEventBus multi-subscriber pattern with typed event contracts should be formalized as an ADR. It establishes how cross-pane communication works in the Spaarke frontend and constrains future pane additions to use the same bus rather than introducing ad-hoc callbacks or context drilling.

**Proposed**: ADR-025 (or next available number)
**Scope**: Frontend cross-component communication in Code Pages

### Three-Pane Lifecycle Stages Pattern

The 4-stage lifecycle (Welcome, Active, Review, Complete) with per-pane rendering rules should be documented as an ADR. It defines how the shell transitions between states and what each pane shows at each stage.

**Proposed**: ADR-026 (or next available number)
**Scope**: Frontend shell state management
