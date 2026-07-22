# Task 021 — Opaque-Atom Node (Client, FR-02 client half) — Decisions

> **Task**: `tasks/021-opaque-atom-node-client.poml` (FR-02, client half)
> **Files changed**: `opaqueAtomNode.ts` (new), `opaqueAtomNode.test.ts` (new), `ComposeEditor.tsx`
>   (import + schema registration + `useStyles` CSS — minimal, localized additions only)
> **Depends on**: task 012 (server projection — `ComposeDocxProjectionBuilder.EmitBlockAtom` / `AppendAtom`)

## 1. Two node types, not one — mirroring the server's two atom shapes exactly

Task 012's server projection emits opaque atoms in TWO distinct HTML shapes (verified by reading
`ComposeDocxProjectionBuilder.cs` `EmitBlockAtom` + `BuildContext.AppendAtom`):

- **Block atom** (`EmitBlockAtom`) — a whole-construct SDT standing BETWEEN paragraphs:
  `<div class="compose-atom" data-atom-kind="sdt" data-atomid="…" contenteditable="false"></div>`.
- **Inline atom** (`AppendAtom`) — a field / inline SDT / complex object living INSIDE a paragraph's
  own run flow: `<span class="compose-atom" data-atom-kind="…" contenteditable="false">{displayText}</span>`.

These are structurally different ProseMirror node kinds (`group: 'block'` vs `group: 'inline'`), so
`opaqueAtomNode.ts` defines `ComposeBlockAtomNode` + `ComposeInlineAtomNode` (both `atom: true` leaves,
no `content` expression), registered together as `COMPOSE_R4_OPAQUE_ATOMS`.

## 2. Identity-model tension: "carries its paraId" vs. task-012's actual descriptor — resolved, not escalated

The task POML's acceptance criteria say "Each atom node is non-editable and carries its `paraId`
attribute." Task 012's own decisions (`notes/task-012-opaque-atoms-decisions.md` §3) deliberately keep
BLOCK atoms **out of** `ParaIdMap` and give them their OWN minted `atomId` instead — a block atom has
no containing paragraph, so it literally has no `paraId` to carry. Only INLINE atoms have a paraId
(inherited from their containing paragraph).

This looked at first like the POML's own `<escalation>` trigger ("the task-012 atom descriptor cannot
express a construct... with no stable paraId... STOP and surface it"). On inspection it is NOT that
case:

- The trigger describes a construct with **no stable identity at all** — an unresolvable ambiguity.
- Here, EVERY atom has a stable, addressable identity — just not always named `paraId`. Block atoms
  have `atomId` (server-minted, collision-checked, exposed via `data-atomid`); inline atoms have the
  inherited `paraId`. Task 012 §3 documents this as a deliberate, already-reasoned design choice (to
  preserve the `ParaIdMap` "one entry per body paragraph" invariant and its F-01 single-walk test),
  not an oversight or a gap.
- Document order is never at risk either way — both atom shapes parse at the exact DOM position the
  server emitted them (a single top-to-bottom HTML walk), so order is structural, not attribute-driven.
- "Addressability by the patch model" is moot for an atom's OWN content — atoms are NEVER edited (I-1/
  I-4); the thing that needs paraId-based addressing is the *surrounding* paragraphs, which already
  carry their own real paraIds via `COMPOSE_R3_PARAID`.

**Resolution (Path C — pivot to comply with design intent, per root CLAUDE.md §6.5's spirit)**: rather
than escalate a non-blocking naming mismatch, or fabricate a fake client-side paraId for block atoms
(which the escalation trigger explicitly forbids — "rather than inventing a client-side identity"),
`opaqueAtomNode.ts` exposes each atom's REAL server-provided identity:
- `ComposeInlineAtomAttributes.paraId` — the inherited containing-paragraph paraId (hidden attribute,
  mirrors `paraIdExtension.ts`'s FR-09 convention — never rendered to the DOM).
- `ComposeBlockAtomAttributes.atomId` — the server-minted atom id (visible `data-atomid`, since the
  server itself renders it visibly, unlike the hidden paragraph paraId).

Colocated tests (`opaqueAtomNode.test.ts`) assert the inline case's `paraId` attribute against the
literal acceptance-criterion wording ("the node's paraId attr equals the descriptor's paraId"), and
separately assert the block case's `atomId` attribute against its own descriptor. This is a documented
deviation from the POML's literal phrasing, not a silent one — flagged here for owner review, same
precedent task 012 set for its own boundary decisions.

## 3. Non-editability split across task 020 / task 021 — scope boundary

Both atom node types are `atom: true` leaves with NO `content` expression — there is no interior
ProseMirror cursor position inside an atom, so "typing inside" is structurally impossible (this task's
contribution, verified by the "non-editability" test group in `opaqueAtomNode.test.ts`: placing the
selection at `atomPos + 1` and inserting text never mutates the atom's own rendered content).

What this task does **NOT** attempt: guarding against a NodeSelection-then-type replacing the WHOLE
atom (standard ProseMirror behavior for any atom node, same as an image). That is a transaction-level
concern — the step-interceptor PLUGIN (task 020, a parallel task, a different file:
`stepOperationInterceptor.ts`) is where that guard belongs, per the task POML's own coordination note
("Confirm the task-020 interceptor refuses edits routed into it"). Duplicating transaction-filtering
logic here would cross the parallel-task file boundary the two POMLs establish.

## 4. Placeholder rendering — no NodeView/React; renderHTML + CSS classes only

The POML's step 2 says "Placeholder NodeView" (a TipTap/ProseMirror term of art). This module
implements the placeholder via `Node.create`'s `renderHTML` (a static DOM-output spec) + token-based
CSS classes (`compose-atom`, `compose-atom-block`, `compose-atom-inline`) in `ComposeEditor.tsx`'s
`useStyles()`, rather than a React `ReactNodeViewRenderer`. Rationale: every existing Compose schema
extension (marks: `InsertionMark`/`DeletionMark`/`CommentAnchorMark`; the paraId extension) uses this
same `renderHTML` + token-class pattern — no ReactNodeViewRenderer precedent exists anywhere in this
codebase yet, and the placeholder's needs (a label, a border, a background) don't require React
interactivity. This keeps the addition consistent with the file's established conventions and avoids
introducing a new architectural pattern for a display-only leaf.

## 5. Testing — Jest, not Vitest

The POML says "Add colocated Vitest tests." `Spaarke.Compose.Components/package.json` uses **Jest**
(`"test": "jest"`) — every existing colocated test in `src/widgets/` (`marks/marks.test.ts`,
`ComposeEditor.paraId.test.tsx`, etc.) is a Jest test. Per the task's `directional` step mode ("adapt
the sequence to the real codebase state... do the right thing and note the deviation"), the 17 new
tests in `opaqueAtomNode.test.ts` are Jest tests, following the exact `marks.test.ts` /
`ComposeEditor.paraId.test.tsx` headless-`Editor` pattern (StarterKit + the extension array under
test). All 17 pass; the full package suite is unaffected (see §6).

## 6. Verification

- `npm install --legacy-peer-deps --no-audit --no-fund` — up to date, no changes.
- `npx jest opaqueAtomNode` — **17/17 pass**.
- `npx jest` (full package suite) — 200 passed, 0 failed among suites that could load; 25 pre-existing
  suite-load failures (`Cannot find module '@spaarke/auth'` / `@spaarke/ai-widgets'` / etc.) are a
  **pre-existing environmental gap**: those sibling shared-lib packages (`Spaarke.Auth`,
  `Spaarke.AI.Widgets`, `Spaarke.UI.Components`, `Spaarke.DocumentOperations`) have no `dist/` built in
  this worktree (`Spaarke.Auth/package.json` `main: "dist/index.js"`, but no `dist/` directory exists).
  Confirmed unrelated to this task: none of the 25 failing suites import `opaqueAtomNode.ts`, and the
  failures are 100% `Cannot find module '@spaarke/*'` resolution errors, not assertion failures.
- `npx tsc --noEmit` — same pre-existing `@spaarke/*` module-resolution errors (in files this task
  never touched: `ComposeAiToolbar.tsx`, `ComposeWorkspace.tsx`, `useComposeWordShuttle.ts`, etc.).
  **Zero errors reference `opaqueAtomNode.ts` or `opaqueAtomNode.test.ts`.** `npm run build` (`tsc`)
  therefore also fails, for the same pre-existing reason — not a regression introduced by this task.
- `grep tiptap-pro package.json` — no `@tiptap-pro/*` dependency (NFR-03 confirmed; the new node types
  are built entirely on `@tiptap/core`'s MIT `Node.create`).
- `git diff` on `ComposeEditor.tsx` — confirmed minimal + localized (one import line, one comment
  block, one extension-array entry, one CSS block) and cleanly coexists with task 020's concurrent
  `stepOperationInterceptor` registration in the same file (verified via `git diff --stat`; no merge
  conflict, no interleaving).

## 7a. Step 9.5 quality gates

**code-review** (`opaqueAtomNode.ts`, `opaqueAtomNode.test.ts`, `ComposeEditor.tsx` diff):
- Security / performance: no findings (no secrets, no fetch, no loops/blocking calls).
- AI code smells (5-pattern scan): none found — no single-impl interfaces (the two `*Attributes`
  types are plain TS shape interfaces, not DI seams), no try/catch-log-rethrow, no null-checks-on-
  non-nullable, comments are rationale-bearing (not code-restating), no >3-responsibility methods.
- ADR-021 (Fluent v9 / semantic tokens): compliant — all new CSS in `ComposeEditor.tsx`'s
  `useStyles()` uses `tokens.*`; zero hex/inline `style=`.
- One Suggestion applied during self-review: simplified `atomKindLabel()` from a `key in Record` type-
  narrowing one-liner to a plain `?? ` fallback lookup (equivalent behavior, clearer). Re-ran the 17
  colocated tests after the fix — still 17/17 green.
- Component justification (CLAUDE.md §11 / Step 6.6): satisfied by the POML's own `<justification>`
  block (existing = none, verified by grep; extension = no existing non-editable leaf node to build
  on; cost-of-doing-nothing = fields/SDTs break the editor or drop on save).

**adr-check**:
- ADR-021 — Compliant (see above).
- ADR-028 — N/A (render-only node; no fetch/token code added, per the task's own constraint).
- ADR-013 / ADR-007 — N/A (no AI-internal or `Microsoft.Graph` types touched; this is a pure client
  schema addition).
- ADR-010 — N/A to the two TS attribute interfaces (ADR-010 targets C# DI service interfaces with a
  single implementation; `ComposeBlockAtomAttributes`/`ComposeInlineAtomAttributes` are structural
  type-only shapes, not runtime DI seams).
- NFR-03 (MIT licensing) — Compliant: `opaqueAtomNode.ts` imports only from `@tiptap/core`; confirmed
  no `@tiptap-pro/*` entry in `package.json`.
- No BFF-hygiene checklist applies (no `Sprk.Bff.Api`/`Spaarke.Core`/`Spaarke.Dataverse` files touched).
- **Result: 0 Critical, 0 Warning, 1 Suggestion (applied).**

## 7. Deferred to later tasks

- Step 9.7 browser/UI testing (dark-mode visual check, ADR-021) — this session has no `--chrome`
  integration; per `task-execute` Step 9.7's skip rule ("Claude Code not started with --chrome"), this
  is documented as skipped rather than run. The dark-mode-CORRECTNESS half is covered at the unit level
  (`opaqueAtomNode.test.ts` "ADR-021" group: no inline `style=`/hex ever leaks onto the rendered
  placeholder — only token-driven CSS classes). A full visual dark-theme check is deferred to task 024
  (the project's consolidated frontend test/verification task) or a future `--chrome` session.
- Enforcement of "no intra-atom operation" in the actual Patch Engine (task 030+, per task-012 §6) —
  out of scope for both task 012 and this task; this task only renders the atom + carries its identity.
