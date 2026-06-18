# Current Task State — R6 (Wave D-G1 task 080 — done)

> **Last Updated**: 2026-06-18 (Phase D launch — Pillar 8 CommandRouter parser foundation landed)
> **Mode**: Wave D-G1 (task 080 — CommandRouter parser) — COMPLETE
> **Branch**: `work/spaarke-ai-platform-unification-r6`

---

## Task 080 — closeout

| Task | Scope | Status | Evidence note |
|------|-------|--------|---------------|
| 080 | Pillar 8 foundation. `CommandRouter.ts` pure parser implementing closed Q6 vocabulary (6 hard slashes + 4 soft slashes + 3 reference shapes); 36 unit tests green (each command + each reference + composition + NFR-11 passthrough + purity invariants); wired into `ConversationPane.handleBeforeSendMessage` at send-message boundary in capture-only mode (NO behavior branch — gates 081/082/083). | ✅ | `notes/task-080-evidence.md` |

### What landed

**Files created:**
- `src/solutions/SpaarkeAi/src/components/conversation/CommandRouter.ts` — pure parser; exports `parse(rawText): Intent`, types (`Intent`, `Reference`, `SlashCommand`, `HardSlashCommand`, `SoftSlashCommand`, `ReferenceKind`), read-only vocabulary tables (`HardSlashes`, `SoftSlashes`).
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/CommandRouter.test.ts` — 36 tests covering Q6 closed vocabulary + NFR-11 regression + purity invariants.

**Files modified:**
- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` — added `import { parse as parseCommandIntent } from "./CommandRouter"` + `const commandIntent = parseCommandIntent(messageText)` inside `handleBeforeSendMessage` BEFORE the existing R5 `matchIntent` call. `void commandIntent` suppresses unused-var lint until tasks 081/082/083 wire branching behavior. NO behavior change.

### NFR-11 binding regression test

Locked in `CommandRouter.test.ts`:

```ts
test('natural language → command: null (full Intent shape)', () => {
  const intent = parse('summarize this for me');
  expect(intent).toEqual<Intent>({
    command: null, references: [], rawText: 'summarize this for me',
    isHardSlash: false, isSoftSlash: false,
  });
});
```

This shape is THE contract that lets `handleBeforeSendMessage` fall through to the existing `matchIntent` + SprkChat send funnel UNCHANGED when the user types natural language.

### Build + tests

- **Tests**: `npm test -- --testPathPatterns=CommandRouter` → **36/36 passing** in 8.3s
- **Module typecheck**: `tsc --noEmit CommandRouter.ts` → **0 errors** (isolated)
- **`npm run build`**: `tsc-surface-gate` reports **0 surface-owned errors** (98 pre-existing errors in unrelated shared libs deferred to Phase B per gate); Vite build fails on PRE-EXISTING `@spaarke/sdap-client` rollup resolution in `EntityCreationService.ts` (NOT introduced by my changes — confirmed by `git stash` rerun)
- **ConversationPane regression check**: Stash-and-rerun confirmed 2 pre-existing test-suite failures in `ConversationPane.r5.test.tsx` + `ConversationPane.slash-nl-rewire.test.tsx` exist BEFORE my changes (CreateMatterWizard module resolution); my code adds zero new failures.
- **BFF publish-size delta**: **0 MB** (frontend-only; no `.cs` files modified)

### Downstream unblocked

- 081 (hard-slash executor — 6 commands)
- 082 (soft-slash agent routing — 4 commands)
- 083 (reference resolver — 3 shapes)
- 084 (composition integration tests)
- 086 (additional Phase D Wave 2 work that consumes Intent)

TASK-INDEX 080 flipped 🔲 → ✅.

---

## Next task

Per TASK-INDEX, Wave D-G1 has three parallel-safe siblings all gated by task 080:

- **task 081** — Hard slashes executor (`/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter`, `/pin`)
- **task 082** — Soft slashes agent routing (`/summarize`, `/draft`, `/extract-entities`, `/analyze`)
- **task 083** — References resolver (`#scope`, `@<entity>`, `#<filename>`)

These can be dispatched as a parallel wave (3 sub-agents in one message) per project-pipeline pattern.
