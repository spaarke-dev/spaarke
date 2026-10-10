# Task 011 (C-3) — deviations from the POML, recorded per step 6

> Disambiguate the two unrelated `MetricCard` components. Spec FR-41 (C-3).

## 1. Chose a real rename over documentation-plus-alias

The POML's constraint said: *"prefer documenting plus a barrel-level alias over a wide rename if the
rename would touch many consumers. Measure the consumer count first and state it."*

**Consumer count measured** (grep for every import of each component, by literal import specifier, not
symbol name — per the method note in `notes/reuse-verification-2026-10-02.md` §8.6):

| Component | Real consumers (excl. own barrel/own test) |
|---|---|
| `Spaarke.UI.Components/WorkspaceShell/MetricCard` | 1 — `MetricCardRow.tsx` (intra-package only; **zero** cross-package consumers found) |
| `Spaarke.Visuals/components/MetricCard` | 1 — `VisualHost/control/components/ChartRenderer.tsx` (cross-package, via deep relative import — confirms the X7 finding that nobody imports via the `@spaarke/visuals` package specifier) |

This is not "many consumers" by any reading — so instead of a header-comment-only fix, I did a real rename
of the lower-value side: `Spaarke.Visuals`'s `MetricCard` → `VisualMetricCard` (and `IMetricCardProps` →
`IVisualMetricCardProps`). The file's own backlog item (`notes/reuse-verification-2026-10-02.md` §8.7, C-3)
explicitly suggested "consider renaming the Visuals one," and the canonical, FR-27-reuse-target name
(`MetricCard`) was kept on the `WorkspaceShell` component, which is the one that must stay easy to find and
import correctly. Both files also got the acceptance-criterion header comment naming the counterpart.

**No behavior or prop change**: the rename touched only the exported identifier and its type name, never a
prop, a style, or JSX structure.

## 2. A third, unrelated `MetricCard` exists and was deliberately left untouched

`src/solutions/LegalWorkspace/src/components/PortfolioHealth/MetricCard.tsx` is a third, independent
component also named `MetricCard` (confirmed via `import { MetricCard } from "./MetricCard"` in
`PortfolioHealthStrip.tsx` and the local barrel `PortfolioHealth/index.ts`). It is not named in this task's
`<relevant-files>` or `<outputs>`, and the task's own scope constraint is disambiguation of "the two
components." Touching a third file not named in the POML would be scope creep per CLAUDE.md §11. **Flagging
it here rather than silently leaving a known third collision undocumented.** It does not create the
specific hazard this task targets (nobody would reach for `VisualHost`'s metric card and get this one, or
vice versa — the names are the same but there is no evidence of cross-package import pressure for this
third component), but a future cleanup item could extend C-3 to cover it.

## 3. A pre-existing, unrelated broken import was found and deliberately NOT fixed

`src/client/pcf/VisualHost/stories/MetricCard.stories.tsx` imports
`{ MetricCard, IMetricCardProps } from '../control/components/MetricCard'` — a path that does not exist.
`VisualHost/control/components/` has no `MetricCard.tsx` (confirmed by directory listing); the real
component lives at `shared/Spaarke.Visuals/src/components/MetricCard.tsx` and is imported by
`ChartRenderer.tsx` via a deep relative path, not from `control/components/`. This Storybook stories file
was already broken before this task (consistent with the `@spaarke/visuals` barrel's own comment: "moved
from the VisualHost PCF in VHVU-041" — the story file was never updated after that move). It is **not**
included in the PCF's `tsconfig.json` (`include: ["control/**/*"]` excludes `stories/**`), so it does not
affect `npm run build` / `build:prod`, only the separate `build-storybook` script. Left unfixed:
out of scope for a disambiguation task, and fixing it would require either resurrecting a duplicate file or
rewriting the story against the renamed `VisualMetricCard`, which is a larger judgment call than this task's
constraints allow ("disambiguation only"). Flagging for a future cleanup item.

## 4. Environment: this worktree had zero `node_modules` anywhere under `src/client`

To actually execute step 4 ("Build both packages and run their tests"), I ran
`npm install --legacy-peer-deps --no-audit --no-fund` in `Spaarke.Visuals` (and separately in
`Spaarke.UI.Components`, which already had `node_modules` from elsewhere) and in `VisualHost` (PCF). This
regenerated `package-lock.json` in `Spaarke.Visuals` (113 insertions / 112 deletions — resolved-metadata
churn from a fresh install, not a dependency change I made). Flagging so the main session can decide whether
to keep or discard that lockfile diff; I did not hand-edit it.

`Spaarke.UI.Components`'s `npm run build` (`tsc`) fails with 9 pre-existing errors, all in files I never
touched (`AccessGrantModal/types.ts`, `services/document-upload/FileUploadService.ts`,
`services/document-upload/types.ts`, `services/EntityCreationService.ts`, `utils/useWizardPageBootstrap.ts`),
all `TS2307: Cannot find module '@spaarke/auth'` / `'@spaarke/sdap-client'`. Root cause: both are local
`file:`-linked sibling packages (`../Spaarke.Auth`, `../Spaarke.SdapClient`) that have never been `npm
install`ed or built in this worktree (`dist/` and `node_modules` both absent for each). This is a pre-existing
environment gap, not something C-3 introduced — zero errors reference `MetricCard` or `WorkspaceShell`, and
there is no existing test for `WorkspaceShell/MetricCard.tsx` to regress (I grepped for one; none exists). I
did not bootstrap the two sibling packages to force a green `tsc` run — that is a different, unrelated piece
of work and well outside "disambiguation only."
