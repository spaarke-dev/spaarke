# 🔴 FINDING — the LegalWorkspace standalone Vite build has been RED since 2026-07-02

> **Found** 2026-09-04 by `unified-access-control-r2` while verifying the CloseProjectDialog
> consolidation. **NOT caused by that change** — confirmed by `git log -S` and `merge-base`.
> **Not fixed here**: the fix is more than one line and belongs to whoever owns the Compose→LW
> section wiring, not to a dead-code-removal task.

---

## What is broken

`npm run build` in `src/solutions/LegalWorkspace/` fails at Rollup module resolution. It is a **chain**,
not one gap — fixing the first exposes the second:

| # | Unresolved | Imported by | Why LW cannot resolve it |
|---|---|---|---|
| 1 | `@spaarke/document-operations` | `Spaarke.Compose.Components/src/widgets/ComposeToolbar.tsx` | LW's `vite.config.ts` has **no alias** for it. SpaarkeAi has one (`vite.config.ts:210-211`) |
| 2 | `@tiptap/core` | `Spaarke.Compose.Components/src/widgets/hooks/useComposeDocumentStyles.ts` | A real npm dependency **absent from LW's `package.json`** |

There are likely more behind #2 — the chain was not walked to the end, because each step is a
scope-expanding decision that is not this task's to make.

## Why LW is dragged into Compose's dependency graph at all

`sections/composeEditor.registration.ts` (spaarkeai-compose-r1 task 093, 2026-07-01) swapped a
Skeleton placeholder for the real TipTap editor, so LW now transpiles and bundles
`Spaarke.Compose.Components` (`vite.config.ts:53`, `:125-126`). LW therefore inherits every dependency
Compose acquires — but LW's alias list and `package.json` were not kept in step.

## When, and proof it is pre-existing

`@spaarke/document-operations` entered `ComposeToolbar.tsx` in **`0420562e6`, 2026-07-02**
(*"refactor(spaarkeai-compose-r1): retire Compose AI dispatch client (Phase A)"*), which
`git merge-base --is-ancestor … origin/master` confirms is **on master**. So the break is ~2 months old
and unrelated to this project.

## ⚠️ Why nobody noticed — the part worth internalising

**`tsc --noEmit` passes.** TypeScript resolves `@spaarke/document-operations` through `tsconfig` path
mappings; the **bundler** cannot, because it resolves through Vite aliases, and the two lists had
drifted apart. A typecheck-only gate reports green on a build that cannot produce an artifact.

That is also why this went unseen for two months: nothing in CI runs `npm run build` for this solution,
and the tsc signal actively said "fine". **A green typecheck is not evidence that a Vite solution
builds.**

A second instance of the same class was hit and fixed inside this task: importing
`@spaarke/ui-components/src/components/…` **typechecks** (tsconfig paths) but **fails the bundle** with
a doubled `src/src/`, because the Vite alias already points *at* `src`. Same root cause — two
resolution systems, one of them unchecked.

## What was NOT done, and why

A one-line alias for #1 was written and then **reverted**. It is correct in isolation (it mirrors
SpaarkeAi exactly), but it does not make the build green — #2 still fails — so it would have been an
**unverifiable build-config change riding along in a dead-code-removal commit**, and would have read
as "the LW build was fixed" when it was not.

## Recommended fix (for whoever owns it)

1. Add the `@spaarke/document-operations` alias pair to `src/solutions/LegalWorkspace/vite.config.ts`,
   mirroring `src/solutions/SpaarkeAi/vite.config.ts:210-211`.
2. Add the TipTap dependencies Compose needs to LW's `package.json` (align with SpaarkeAi's set).
3. Walk the chain to the end — expect more.
4. **Then add `npm run build` for LegalWorkspace to CI**, or this recurs silently the next time Compose
   takes a dependency. The build is the only check that would have caught any of this.

## Impact today

LegalWorkspace as a **standalone deployable code page** cannot be built from a clean checkout. It is
still consumed as a **component library** by SpaarkeAi (which has the aliases and deps), so the embedded
path is unaffected — which is very likely why this stayed invisible. Per
[`LEGALWORKSPACE-RETIREMENT.md`](../../../docs/architecture/LEGALWORKSPACE-RETIREMENT.md) the standalone
page is retired, so the operational urgency is low — but the CI gap that hid it is not LW-specific.
