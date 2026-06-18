# Current Task State — R6 (Wave C-G3 partial closeout — task 063 only outstanding)

> **Last Updated**: 2026-06-11 (post C-G3 gap-fill agent dispatch)
> **Mode**: Wave C-G3 4-of-5 tasks closed; task 063 outstanding
> **Branch**: `work/spaarke-ai-platform-unification-r6`

---

## Wave C-G3 closeout summary

| Task | Status | Title | Tests | Evidence note |
|------|--------|-------|-------|---------------|
| 057 | ✅ | User affordances (Send / AddToAssistant / PinToMatter) (D-C-08/09/10) | 27 / 27 passing | [task-057-evidence.md](notes/task-057-evidence.md) |
| 058 | ✅ | Conflict resolution implementation (Q8 USER WINS) (D-C-11) | 3 / 3 integration + 5 / 5 unit regression | [task-058-evidence.md](notes/task-058-evidence.md) |
| 062 | ✅ | Register trace widget with ContextWidgetRegistry (D-C-15) | 5 / 5 passing (narrow contract); 110-case serialize-restore blocked on pre-existing d3-force ESM infra gap | [task-062-evidence.md](notes/task-062-evidence.md) |
| 063 | 🔲 | Emit context.* events from chat agent + playbook (D-C-16) | NOT STARTED — punted | [task-063-partial-evidence.md](notes/task-063-partial-evidence.md) (handoff brief) |
| 066 | ✅ | Selective recall via embedding similarity (D-C-19) | 21 / 21 passing | [task-066-evidence.md](notes/task-066-evidence.md) (Verification section appended) |

**Build status**: BFF clean (0 errors, 16 pre-existing warnings). Integration
tests project clean. Unit tests clean.

**Wave C-G3 status**: 4 of 5 tasks closed; task 063 outstanding and BLOCKS the
end-to-end Pillar 6c trace pipeline (widget infra exists client-side; BFF
emissions are the missing half).

## Outstanding: task 063

See [`notes/task-063-partial-evidence.md`](notes/task-063-partial-evidence.md)
for the verbatim next-agent handoff brief. Summary:

- 4 emission categories to wire: tool-call events (SprkChatAgent), knowledge
  retrieval + decision events (CapabilityRouter), playbook node lifecycle
  (PlaybookExecutionEngine wrapper — NFR-08 BINDING: NOT inside the 11
  executors).
- Unit test file: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Telemetry/ContextEventEmissionTests.cs` (NEW).
- Audit note: `notes/task-063-adr015-emission-audit.md` (NEW; per-site
  payload-construction audit).
- ADR-015 BINDING: deterministic IDs ONLY in every emission payload. Never
  user message text, tool body content, or LLM response text.
- POML reference: `tasks/063-emit-context-events-from-agent-and-playbook.poml`.

Recommended dispatch: fresh sub-agent with the partial-evidence note as primary
brief + the POML + the 057/058/062/066 evidence notes for pattern reference.

## Wave C-G3 test-infrastructure heals (in scope, completed)

Two pre-existing SpaarkeAi-workspace test-infra blockers were fixed as a
prerequisite to verifying the 057 tests. Both are documented in
`task-057-evidence.md`:

1. Added `@spaarke/sdap-client` mock (`src/solutions/SpaarkeAi/src/__mocks__/sdap-client.ts`)
   + moduleNameMapper entry in `jest.config.ts`.
2. Rewired the 3 affordance components + their tests from the
   `@spaarke/ai-widgets` barrel to the `@spaarke/ai-widgets/events` subpath
   to avoid pulling in the workspace-widget side-effect chain that needs
   `@spaarke/ui-components/components/CreateMatterWizard` (a heavier
   dependency).

These heals are net-positive for the SpaarkeAi workspace test suite — they
unblock any new component test that only needs the PaneEventBus surface.

## Reminders for resume

- The repo-wide `widget-serialize-restore.test.ts` (110 cases, in
  `Spaarke.AI.Widgets`) is blocked on a pre-existing d3-force ESM gap. NOT
  introduced by R6 or this gap-fill. The narrow `register-execution-trace-widget.test.ts`
  is the authoritative FR-36 verification.
- The 058 persona snippet is DATA — `scripts/Seed-AiPersonaDefault.ps1` was
  extended in this pass but NOT executed against any Dataverse environment.
  User to run the script when ready (idempotent: PATCHes on drift).
- The 5 affordance / event-type tasks (057/058/062/066) closed in this pass do
  NOT modify any production node executor. NFR-08 binding preserved.
- All changes compile clean and tests pass (or pre-existing infra gap
  documented). No CVE / publish-size deltas measured in this pass (the changes
  are pure-BCL telemetry adds + persona-data extensions + frontend component
  additions); the 063 follow-up agent should run `dotnet publish` + CVE check
  per POML 063 step 9.
