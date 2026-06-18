# Task 080 — D-D-01 CommandRouter Parser — Evidence

> **Status**: ✅ Complete
> **Wave**: D-G1 (Phase D launch)
> **Date**: 2026-06-18
> **POML**: `tasks/080-command-router-parser.poml`
> **Rigor**: FULL
> **Author**: sub-agent (R6 Wave D-G1)

---

## Scope

Pillar 8 foundation — build the `CommandRouter` parser that classifies user
chat input into a structured `Intent { command, references[], rawText,
isHardSlash, isSoftSlash }` shape **before** agent invocation per spec FR-48.

The parser is pure, synchronous, side-effect-free. It does NOT execute
commands or resolve references — execution lands in tasks 081 (hard slashes),
082 (soft slashes), and 083 (references resolver). This task is the foundation
the other Phase D Wave 1 tasks fan out from.

---

## Acceptance criteria (POML)

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1 | `CommandRouter.ts` exports `Intent` type + `parse(rawText: string): Intent` function | ✅ | Module exports `Intent`, `Reference`, `SlashCommand`, `HardSlashCommand`, `SoftSlashCommand`, `ReferenceKind`, `parse`, `HardSlashes`, `SoftSlashes` |
| 2 | Tests cover every hard slash, every soft slash, every reference shape, composition, unrecognized slash passthrough, NL passthrough — all green | ✅ | 36 tests / 36 passing in 8.3s |
| 3 | Parser is synchronous + pure | ✅ | Three explicit purity tests (no Promise, deterministic, regex state reset); zero awaits, zero fetch, zero React, zero state writes |
| 4 | `ConversationPane.tsx` calls `parse()` at send-message boundary; no behavior change | ✅ | Import + `const commandIntent = parseCommandIntent(messageText)` + `void commandIntent` inside `handleBeforeSendMessage` BEFORE existing `matchIntent` call |
| 5 | NFR-11 backward compat verified — NL → `{ command: null, ... }` shape | ✅ | Explicit shape-assertion regression test using `toEqual<Intent>` |
| 6 | `npm run build` succeeds | ⚠ See "Build verification" — pre-existing failures disclosed; surface-owned errors = 0 |
| 7 | BFF publish-size delta = 0 MB | ✅ | Frontend-only; zero `.cs` files modified |
| 8 | `code-review` + `adr-check` quality gates pass | ✅ | See "Quality gates" section |
| 9 | Downstream tasks (081, 082, 083, 084, 086) unblocked | ✅ | Intent shape contract published + 3 export tables for downstream consumers |

---

## Files

### Created

- `src/solutions/SpaarkeAi/src/components/conversation/CommandRouter.ts` (~290 lines, includes docstrings)
- `src/solutions/SpaarkeAi/src/components/conversation/__tests__/CommandRouter.test.ts` (~340 lines, 36 tests)

### Modified

- `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx` (+18 lines: import + 11-line comment + `parse()` call + `void` suppression)

### Diff at the send-message boundary (excerpt)

```tsx
// Around line 1087 of ConversationPane.tsx (handleBeforeSendMessage):
//
// ── R6 task 080 / D-D-01 (Pillar 8 foundation) ──────────────────────
// Capture the structured CommandRouter Intent at the send-message
// boundary. The Intent is currently CAPTURE-ONLY — there is NO
// behavior branch here. Downstream Phase D tasks (081 hard-slash
// executor, 082 soft-slash agent routing, 083 reference resolver)
// will read this Intent and dispatch. NFR-11 binding: when the user
// typed natural language (no slash), `commandIntent.command === null`
// and the existing R5-task-036 matcher + SprkChat send funnel runs
// UNCHANGED.
const commandIntent = parseCommandIntent(messageText);
void commandIntent;

const readyChips = attachmentChips.filter(c => c.status === "ready");
const intent = matchIntent(messageText, readyChips.length > 0, undefined);
// ... existing R5 task 036 logic UNCHANGED below
```

---

## Closed vocabulary (Q6 / FR-49 / FR-50 / FR-51)

| Kind | Tokens | Count |
|------|--------|-------|
| Hard slashes (deterministic, bypass LLM) | `/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter`, `/pin` | 6 |
| Soft slashes (intent shortcuts via agent) | `/summarize`, `/draft`, `/extract-entities`, `/analyze` | 4 |
| Reference shapes | `#scope`, `@<entity>`, `#<filename>` | 3 |

Closed — do NOT extend without spec FR sign-off.

### Reference disambiguation rule

- `#scope` (literal token) → `kind: 'scope'`
- `#<anything-else>` → `kind: 'filename'`
- `@<value>` → `kind: 'entity'` (always)

Permissive token chars: `[A-Za-z0-9][A-Za-z0-9._\-]*` — tokenizer is
deliberately permissive; downstream resolver (task 083) validates against
actual data.

---

## Intent shape

```ts
export interface Intent {
  command: SlashCommand | null;     // null = no slash → NFR-11 passthrough
  references: Reference[];          // empty array if none; never null
  rawText: string;                  // input AS-RECEIVED (untrimmed)
  isHardSlash: boolean;
  isSoftSlash: boolean;
}

export interface Reference {
  kind: 'scope' | 'entity' | 'filename';
  value: string;                    // sigil-stripped identifier
  raw: string;                      // literal token as appeared (e.g., '#scope')
}
```

### Invariants (enforced via tests)

- `isHardSlash` and `isSoftSlash` are mutually exclusive
- `command === null` ⇔ `isHardSlash === false && isSoftSlash === false`
- `command !== null` ⇒ exactly one of `isHardSlash` / `isSoftSlash` is true
- `references[]` is always an array, never null
- `rawText` is the input AS-RECEIVED

---

## Test breakdown (36 tests / 36 passing)

| Category | Count | Coverage |
|----------|-------|----------|
| Vocabulary registry (`HardSlashes` / `SoftSlashes`) | 2 | Q6 closed (6 + 4) |
| Hard slashes (one test per command, FR-49) | 6 | `/clear`, `/new-session`, `/help`, `/export`, `/save-to-matter`, `/pin` |
| Soft slashes (one test per command, FR-50) | 4 | `/summarize`, `/draft`, `/extract-entities`, `/analyze` |
| Case insensitivity | 5 | `/Summarize`, `/SUMMARIZE`, `/Clear`, `/Save-To-Matter`, `/New-Session` |
| Leading whitespace tolerance | 1 | `"   /summarize   "` |
| References (one test per kind, FR-51) | 3 | `#scope`, `@<entity>`, `#<filename>` |
| Composition (FR-52) | 4 | command + 1 ref / + 2 refs / + 3 refs / hard slash + ref |
| NFR-11 passthrough regression (BINDING) | 6 | NL / unrecognized slash / unknown command / empty / whitespace-only / NL with refs |
| Purity invariants | 5 | sync return / determinism / regex state reset / mutual exclusion (hard ↔ soft) / null ⇒ both false |
| **TOTAL** | **36** | |

### Critical NFR-11 regression test (verbatim)

```ts
test('natural language → command: null (full Intent shape)', () => {
  const intent = parse('summarize this for me');
  expect(intent).toEqual<Intent>({
    command: null,
    references: [],
    rawText: 'summarize this for me',
    isHardSlash: false,
    isSoftSlash: false,
  });
});
```

If this shape ever changes, every existing natural-language send regresses
because `handleBeforeSendMessage`'s subsequent `matchIntent` + SprkChat send
funnel reads through the unchanged path. This test is BINDING.

---

## Build verification

### Module-isolated typecheck

```bash
npx tsc --noEmit --strict --jsx react-jsx --skipLibCheck \
  src/components/conversation/CommandRouter.ts
```

→ **0 errors** (clean)

### Test runner

```bash
npm test -- --testPathPatterns=CommandRouter
```

→ **Test Suites: 1 passed, 1 total / Tests: 36 passed, 36 total** in 8.3s

### Full project build (`npm run build`)

```
✓ check-html-css-reset: index.html has the universal box-sizing reset.
⚠ tsc-surface-gate: 98 pre-existing error(s) in shared libs (deferred to Phase B).
                    Surface-owned: 0. ✓
vite v5.4.21 building for production...
✓ 687 modules transformed.
x Build failed in 2.54s
error during build:
[vite]: Rollup failed to resolve import "@spaarke/sdap-client"
from ".../Spaarke.UI.Components/src/services/EntityCreationService.ts"
```

**Disclosure**: this Vite/rollup failure is **pre-existing**, not introduced
by task 080. Verified via `git stash` + rerun → same failure reproduced with
my changes stashed. The `EntityCreationService.ts` file is unmodified by this
task. The `tsc-surface-gate` confirms **Surface-owned: 0** errors — task 080
adds zero TypeScript errors to surface-owned code.

### ConversationPane regression check

```bash
npm test -- --testPathPatterns=ConversationPane
```

→ 2 test suites failing on PRE-EXISTING `@spaarke/ui-components/components/CreateMatterWizard` jest module resolution error. Reproduced via `git stash` rerun with my changes removed → identical failure. **Zero regression from task 080.**

### BFF publish-size delta

**0 MB**. Frontend-only task; no `.cs` files modified. Per CLAUDE.md §10 +
ADR-029 + R6 NFR-02 — no BFF-side verification required.

---

## ADR + constraint compliance

| Rule | Status | Evidence |
|------|--------|----------|
| **ADR-029** — BFF publish size ≤60 MB; per-task delta verified | ✅ | 0 MB delta (frontend-only) |
| **ADR-030** — PaneEventBus stays at 4 channels | ✅ | Parser is pure module, NOT an event emitter; emits zero events |
| **ADR-031** — Shell lifecycle stays at 4 stages | ✅ | No shell changes |
| **ADR-015** — No user message content logged | ✅ | Zero logging in parser; `rawText` is in-memory return value only |
| **ADR-013** — CRUD-side AI consumers route through PublicContracts | ✅ | N/A (frontend-only) |
| **NFR-01** — Conversational primacy preserved | ✅ | Parser does NOT block; only classifies. NL path UNCHANGED. |
| **NFR-03** — No new ADRs in R6 | ✅ | Zero new ADRs |
| **NFR-04** — Zero Microsoft Agent Framework references | ✅ | grep clean |
| **NFR-11** — NL still works alongside slashes | ✅ | Explicit regression test locks `{ command: null, ... }` shape |
| **Pillar 8 / Q6** — Closed vocabulary (6 + 4 + 3) | ✅ | Vocabulary registry tests assert exact counts + literal token lists |

---

## Quality gates (Step 9.5 — FULL rigor)

### code-review

Self-review against `.claude/skills/code-review/SKILL.md` criteria:

- **Naming**: `CommandRouter` matches Pillar 8 vocabulary in spec + CLAUDE.md. `parse` matches the POML pattern. Types follow string-literal-union convention used by `intentMatcher.ts`.
- **Purity**: Zero IO, zero React, zero async, zero state writes. Regex state reset between calls (tested).
- **Test coverage**: 36 tests across 8 describe blocks; per-command + per-reference + composition + edge cases + invariants + NFR-11 regression.
- **Docstrings**: Every export has JSDoc; module header cites spec FR-48..FR-54, Q6, NFR-01/11, ADR-029/030.
- **Comments at boundary**: ConversationPane wire-up has 11-line explanatory comment citing task 080 + downstream tasks + NFR-11.
- **No emoji / no decorative styling** per repo conventions.

### adr-check

- ✅ ADR-013 — N/A (frontend)
- ✅ ADR-015 — no message logging in parser
- ✅ ADR-029 — 0 MB delta verified
- ✅ ADR-030 — no 5th PaneEventBus channel; parser is pure module
- ✅ ADR-031 — no new shell stage
- ✅ NFR-03 — no new ADRs
- ✅ NFR-11 — explicit shape-assertion regression test

Both gates pass.

---

## Downstream unblocked

- **task 081** — Hard slashes executor (consumes `intent.command` when `isHardSlash === true`)
- **task 082** — Soft slashes agent routing (consumes `intent.command` when `isSoftSlash === true`)
- **task 083** — References resolver (consumes `intent.references[]`)
- **task 084** — Composition integration tests (exercises full Intent shape across 081/082/083)
- **task 086** — Phase D Wave 2 work that builds on Intent contract

All four downstream tasks have everything they need from the published Intent
shape + the two read-only vocabulary tables (`HardSlashes`, `SoftSlashes`).

---

## Tool call count

~14 tool calls (well under the 50-call stream-idle threshold).
