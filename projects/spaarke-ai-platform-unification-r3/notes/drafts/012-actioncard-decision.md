# Task 012 — ActionCard Lift Decision Memo

> **Project**: spaarke-ai-platform-unification-r3
> **Task**: 012 — Verify ActionCard reusability and lift to `@spaarke/ui-components` if context-agnostic
> **Date**: 2026-05-20
> **Decision**: **STAY** (in LegalWorkspace) + **REUSE EXISTING shared `ActionCard`** for task 041

---

## Decision Summary

**ActionCard is already in `@spaarke/ui-components`.** A prior project shipped a context-agnostic `ActionCard` at `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/ActionCard.tsx` and exposed it via the top-level barrel.

Therefore:

1. **Task 041 (`GetStartedCardsWidget`) consumes the existing shared `ActionCard`** directly:
   ```typescript
   import { ActionCard, ActionCardProps } from "@spaarke/ui-components";
   ```
   No new lift is required. No new files are created in the shared lib.

2. **LegalWorkspace's local `ActionCard` STAYS as-is** (no changes) to preserve FR-25 / NFR-10 (standalone LegalWorkspace functions identically). The LegalWorkspace variant uses `IActionCardProps` (with the `I` prefix per LegalWorkspace convention) and is wired into `GetStartedRow.tsx`, `GetStartedExpandDialog.tsx`, and the `GetStarted/index.ts` barrel. Replacing those imports introduces risk to the standalone surface for zero functional benefit (the shared and local components are visually equivalent — minor 100px vs 120px `minWidth` aside).

3. **`ActionCardHandlers.ts` stays in LegalWorkspace** (platform-bound — calls `window.Xrm.Navigation.navigateTo` and references the `sprk_playbooklibrary` web resource). Task POML anticipated this explicitly.

---

## Evidence — Import Categorization

### `ActionCard.tsx` (the LegalWorkspace visual primitive)

| Line | Import | Category |
|------|--------|----------|
| 1 | `import * as React from "react";` | (a) React/generic — agnostic-safe |
| 2-8 | `Text, makeStyles, shorthands, tokens, mergeClasses` from `@fluentui/react-components` | (a) Fluent v9 — agnostic-safe |
| 9 | `type { FluentIcon } from "@fluentui/react-icons"` | (a) Fluent v9 — agnostic-safe |

**No** (b) shared-lib internal imports, **no** (c) LegalWorkspace paths, **no** (d) Dataverse/Xrm/routing dependencies.

The LegalWorkspace `ActionCard` would meet the ADR-012 lift criteria — **except that the equivalent has already been lifted** under a slightly different name and is exported from the shared lib.

### Existing shared `ActionCard` discovery

While preparing the lift, the build surfaced a TypeScript module-name collision:

```
src/components/index.ts(131,1): error TS2308: Module './WorkspaceShell' has already
  exported a member named 'ActionCard'. Consider explicitly re-exporting to resolve
  the ambiguity.
```

Inspection of `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/ActionCard.tsx` confirmed: a context-agnostic `ActionCard` is already in the shared lib, exported via `WorkspaceShell/index.ts` → `components/index.ts`. Its `ActionCardProps` is a strict superset of LegalWorkspace's `IActionCardProps` (it adds an optional `className` prop).

| Feature | LegalWorkspace `IActionCardProps` | Shared `ActionCardProps` |
|---|---|---|
| `icon: FluentIcon` | yes | yes |
| `label: string` | yes | yes |
| `ariaLabel: string` | yes | yes |
| `onClick?: () => void` | yes | yes |
| `disabled?: boolean` | yes | yes |
| `className?: string` | — | yes (extra) |
| Style — `minWidth` | `"100px"` | `"120px"` |

Both renderings use Fluent v9 tokens exclusively (zero hex / rgba). Both are functionally drop-in interchangeable.

### `ActionCardHandlers.ts` (NOT relevant to a lift)

| Concern | Evidence |
|---------|----------|
| Calls platform API | Line 86: `(window as any).Xrm?.Navigation?.navigateTo(...)` |
| Hard-codes solution-local web resource | Line 87: `webresourceName: "sprk_playbooklibrary"` |
| Maps intents to LegalWorkspace playbook flows | Lines 33-36: `CARD_INTENT_MAP` references LegalWorkspace-specific intents |

Per the ADR-012 "Service Portability Tiers" table, this is **Platform-bound** — stays in the consumer. No lift considered.

---

## Action Taken

### Source changes — none (net)

After discovering the existing shared `ActionCard`, all in-progress edits were reverted:

- The new `src/client/shared/Spaarke.UI.Components/src/components/ActionCard/` folder was **removed** (would have been a duplicate).
- The newly-added `export * from './ActionCard';` line in `src/client/shared/Spaarke.UI.Components/src/components/index.ts` was **removed** (caused the TS2308 collision).
- The in-progress LegalWorkspace re-export shim was **reverted** back to the original full implementation, byte-for-byte.

### Files created

- **`projects/spaarke-ai-platform-unification-r3/notes/drafts/012-actioncard-decision.md`** — this memo.

### Files preserved (verified unchanged from task start)

- `src/solutions/LegalWorkspace/src/components/GetStarted/ActionCard.tsx` (matches the byte-for-byte original)
- `src/solutions/LegalWorkspace/src/components/GetStarted/ActionCardHandlers.ts`
- `src/solutions/LegalWorkspace/src/components/GetStarted/GetStartedRow.tsx`
- `src/solutions/LegalWorkspace/src/components/GetStarted/GetStartedExpandDialog.tsx`
- `src/solutions/LegalWorkspace/src/components/GetStarted/index.ts`
- `src/client/shared/Spaarke.UI.Components/src/components/index.ts` (only the unrelated PaneHeader export from task 010 remains, untouched)
- All `WorkspaceShell/ActionCard*` files in the shared lib (untouched)

---

## Impact on Task 041 (GetStartedCardsWidget) — IMPORT PATH

**Concrete import for task 041:**

```typescript
import { ActionCard } from "@spaarke/ui-components";
import type { ActionCardProps } from "@spaarke/ui-components";
```

Symbol provenance: `src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/ActionCard.tsx` (re-exported via `WorkspaceShell/index.ts` and the top-level `components/index.ts`).

Do **NOT**:
- Import from LegalWorkspace (`src/solutions/LegalWorkspace/...`) — that violates cross-solution coupling and would break if LegalWorkspace's local variant evolves.
- Re-lift the LegalWorkspace component — it would duplicate the existing shared one.
- Use the deep import path `@spaarke/ui-components/dist/components/WorkspaceShell/ActionCard` — the barrel import is safe for Code Pages per ADR-012 ("Barrel is safe — React 19 has jsx-runtime").

Task 041 supplies its own widget-routed `onClick` handlers — the SpaarkeAi welcome state cards have different intents than LegalWorkspace's playbook flows, so `createPlaybookHandlers` is not reused.

---

## Follow-up Notes

- **Naming inconsistency tolerated**: The shared `ActionCardProps` lacks the `I` prefix used by LegalWorkspace's `IActionCardProps`. This is consistent with the broader shared-lib convention (e.g. `SprkButtonProps`, `WizardShellProps` — no `I` prefix). Task 041 should use `ActionCardProps`.
- **No deduplication of the LegalWorkspace local copy in this project**: Although the two components are functionally equivalent, replacing the LegalWorkspace local `ActionCard` with the shared one is *not* in scope for r3. Per FR-25 / NFR-10, the standalone LegalWorkspace must function identically; mass import rewrites there are out of scope and add risk. A future cleanup task may consolidate.
- **The 100px vs 120px `minWidth` difference is non-functional**: the LegalWorkspace `GetStartedRow` already wraps cards in its own grid sizing wrapper; the new SpaarkeAi widget will do likewise. Both surfaces compute card width from their layout container, so the local minimum has no visible effect once the row is constrained.
- **Snapshot test**: Not added for the shared `ActionCard` here — that component pre-dates this task and any test would belong to its owning project, not r3.

---

## Acceptance Criteria Verification

| Criterion | Status |
|-----------|--------|
| Decision memo exists with LIFT/STAY + evidence + task 041 import path | ✅ This file |
| Task 041 has a concrete documented import path (file path + symbol name) | ✅ `import { ActionCard, ActionCardProps } from "@spaarke/ui-components"`; provenance `WorkspaceShell/ActionCard.tsx` |
| Blocking coupling named (if STAY in LegalWorkspace) | ✅ N/A in the usual sense — the LegalWorkspace local variant is itself context-agnostic; the *reason* it stays is that an equivalent already exists in the shared lib, making a new lift redundant + duplicative |
| Standalone LegalWorkspace still builds + renders unchanged (FR-25 / NFR-10) | ✅ Local `ActionCard.tsx` byte-for-byte unchanged from project start; build verified — see execution log |
| Zero hex / rgba literals in any shared `ActionCard` | ✅ Verified in both the existing shared `WorkspaceShell/ActionCard.tsx` and the LegalWorkspace local copy — tokens only |
| Both packages build clean | ✅ See build verification at end of execution |
