# ✅ RESOLVED — the LegalWorkspace standalone Vite build was RED from 2026-07-02 to 2026-09-04

> **Found** 2026-09-04 by `unified-access-control-r2` while verifying the CloseProjectDialog
> consolidation. **NOT caused by that change** — confirmed by `git log -S` and `merge-base`.
> **FIXED 2026-09-04, same day, by owner direction** ("addressed here and not deferred or pushed to
> another project"). An earlier revision of this note deferred it; that deferral is withdrawn.
> **See § Resolution at the bottom** — including the one prediction in this note that turned out
> to be wrong.

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
and the tsc signal actively said "fine". (The rule this generalizes to, and the second instance of the
same class hit inside this task, are in § Resolution below.)

## Impact while it was broken

LegalWorkspace as a **standalone deployable code page** could not be built from a clean checkout. It
remained consumable as a **component library** by SpaarkeAi (which has the aliases and deps), so the
embedded path was never affected — which is very likely why this stayed invisible. Per
[`LEGALWORKSPACE-RETIREMENT.md`](../../../docs/architecture/LEGALWORKSPACE-RETIREMENT.md) the standalone
page is retired, so the operational urgency was low — but the CI gap that hid it was not LW-specific.

---

## ✅ Resolution (2026-09-04)

### What was changed

| # | Change | File |
|---|---|---|
| 1 | `@spaarke/document-operations` alias pair, mirroring SpaarkeAi | `src/solutions/LegalWorkspace/vite.config.ts` |
| 2 | Same path added to `sharedLibPaths` (bare-import redirect) + to the react transpile `include` | same |
| 3 | 15 TipTap packages at **SpaarkeAi's exact versions** — pinned identically so the two solutions cannot drift into resolving different TipTap builds of the same Compose source | `src/solutions/LegalWorkspace/package.json` |
| 4 | `Build LegalWorkspace solution` step + `LegalWorkspace_solution` size baseline | `.github/workflows/nightly-health.yml`, `.github/baseline-bundle-sizes.json` |

### ⚠️ The prediction in this note that was WRONG

> *"There are likely more behind #2 — the chain was not walked to the end."*

There were not. The chain ended at TipTap: alias + deps produced a **green build, first try**. Recorded
because this note's own speculation was the stated reason for deferring the fix, and the speculation
did not survive contact with the build. **Walking the chain would have been cheaper than reasoning
about how long it was.**

### How the fix was verified (not "it built once")

The failure mode this whole note is about is *a signal that reports green without verifying anything*,
so the fix was checked by **removing it and confirming the build breaks**:

| Test | Result |
|---|---|
| Build with the alias | **exit 0**, artifact 5,471,366 bytes |
| Alias deleted, rebuild | **exit 1** — `Rollup failed to resolve "@spaarke/document-operations" from ComposeToolbar.tsx`; `dist/` **not** regenerated |
| Alias restored, rebuild | **exit 0** |
| Clean room: `rm -rf node_modules dist` → install → build | **exit 0**, 335 packages, same byte count |

**A trap inside the trap**: the first attempt read the exit code as `0` and nearly recorded the alias
as *not* load-bearing. `npm run build 2>&1 | tail -12` reports **`tail`'s** exit status, not npm's — a
pipeline hides the failure of every stage but the last. This is the same class of error as the one
being fixed: a green that was never measuring the thing. Re-run without the pipe to get the real code.

### CI placement, and why it is nightly rather than PR-time

`.github/workflows/**` has a declared owner (`ci-cd-unit-test-remediation-r1`) and the three tier files
(`ci-router`, `ci-tier1-blocking`, `ci-tier2-advisory`) are **frozen** for the shadow window; the freeze
carve-out covers repairing a gate that is *present but not enforcing*, which is not this case — there
was no LW gate at all. `projects/INDEX.md` names `nightly-health.yml` as explicitly **not frozen**, and
it already ran exactly this job shape for two other front-end surfaces, so LW joined it there.

That is enough to close the recurrence risk: a build failure fails the step, so the next dependency
Compose acquires goes red **within a day** instead of hiding for two months. PR-time coverage is a
wider question — see below — and belongs to the CI owner, not to this note.

### The gap that is still open (measured, not assumed)

**30 solutions under `src/solutions/` declare a build script. Not one of them is built on `pull_request`
by any workflow.** Verified 2026-09-04. Solution builds appear only in deploy workflows and in this
nightly job (which, before today, covered 2 of 30). `client-tests.yml` documents the same reasoning for
jest and stays nightly on purpose — 40 packages with no workspace root means 40 `npm install`s, which
would contend with the shadow window's PR throughput.

So LW is fixed and now watched, but **28 of 30 solutions still have no build signal of any kind**, and
any one of them may be red right now for exactly the reason LW was. Sizing and designing that gate is
`ci-cd-unit-test-remediation-r1`'s call. What is settled here is the underlying rule, which cost two
months to learn twice in one session:

> **A green typecheck is not evidence that a Vite solution builds.** TypeScript resolves through
> tsconfig paths; the bundler resolves through Vite aliases. The two lists drift, and nothing reconciles
> them. Only running the build reconciles them.

A second instance of the same class was hit and fixed inside this task: importing
`@spaarke/ui-components/src/components/…` **typechecks** (tsconfig paths) but **fails the bundle** with
a doubled `src/src/`, because the Vite alias already points *at* `src`. Same root cause, different
spelling — two resolution systems, one of them unchecked.
