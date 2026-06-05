# Task 009 — Deviations

> **Task**: 009-storybook-coverage-and-visual-diff-gate
> **Status**: in-progress (sub-agent scope done; HUMAN-loop pending)
> **Date**: 2026-06-01
> **Sub-agent**: Phase A acceptance gate sub-agent

---

## D-1: Storybook is NOT installed; `.storybook/` config authored as a "drop-in spec"

### What the task asked for
Step 3 of the POML: "Configure Storybook a11y addon; ensure axe runs on every
story." Step 4: "Set up zoom-level testing in Storybook viewport addon:
75/100/125/150%."

### What I found
The `@spaarke/ui-components` package does not currently have Storybook installed
as a devDependency. The `storybook/*.stories.tsx` files exist as CSF (Component
Story Format) modules but no `@storybook/*` packages are listed in
`package.json` devDependencies. There was no `.storybook/main.ts` or
`.storybook/preview.ts` config directory either.

### What I did (deviation)
1. Created `.storybook/main.ts` and `.storybook/preview.ts` that author the
   EXPECTED Storybook configuration per task 009 NFR-04 (a11y) + NFR-01
   (viewport zoom). When Storybook is actually installed in a follow-up task,
   the config loads verbatim.
2. Added `parameters.a11y` + `parameters.viewport.viewports` blocks to the new
   stories (`FluentV9NativeFeatures.stories.tsx`, `EdgeStates.stories.tsx`)
   directly so per-story configuration is self-contained even if the global
   `.storybook/preview.ts` is missed.
3. Documented the install steps + axe-core CI invocation in
   `phase-a-acceptance-gate.md` section 5.

### Why this is acceptable
The task POML treats step 3 + 4 as wiring tasks ("configure" / "set up"). The
sub-agent CANNOT install npm packages without triggering downstream concerns
(transitive CVE check per ADR-029, lockfile churn, mismatched peer deps). The
appropriate scope for this sub-agent is to author the wiring spec and document
the install command for the human / main session. Acceptance criterion 2
("axe-core CI: zero serious/critical") is marked PARTIAL in the parity report
to make this hand-off explicit.

### Follow-up
- Main session or a dedicated task should run:
  ```
  cd src/client/shared/Spaarke.UI.Components
  npm install --save-dev \
    @storybook/react-vite @storybook/addon-essentials \
    @storybook/addon-a11y @storybook/addon-viewport \
    @storybook/test-runner
  ```
- Then run the headless axe scan per section 5 of the parity report.

---

## D-2: `DensityExtraSmall` story documents a wrapper limitation, not an exercised feature

### What the task asked for
Step 1 of the POML: "Each story shows the DataGrid using Fluent v9 native prop,
not a custom impl." For density specifically: "3 sizes: extra-small, small,
medium."

### What I found
The Fluent v9 `<DataGrid>` natively supports `size: 'extra-small' | 'small' |
'medium'`. The Spaarke wrapper's configjson schema only exposes two
`densityDefault` values: `'comfortable'` → wrapper maps to `size="medium"`, and
`'compact'` → wrapper maps to `size="small"`. There is no schema knob that
maps to `size="extra-small"`.

### What I did (deviation)
The `DensityExtraSmall` story renders with `densityDefault: 'compact'` (i.e.,
`size="small"` at runtime) and includes an inline doc comment explaining the
mismatch:

```tsx
// The wrapper maps densityDefault: 'compact' → size: 'small'. The story name
// tracks Fluent v9's smallest size; when the wrapper grows a third density tier
// (NFR-07 future), bump this story's config to set size="extra-small" directly.
```

The parity report (`phase-a-acceptance-gate.md` section 2) documents the
"Wrapper note on density" so reviewers don't expect three visually distinct
densities in the story strip.

### Why this is acceptable
The Phase A scope ships the framework with 2-tier density per the `_version
'1.0'` configjson schema (task 001). Adding a third tier expands the schema —
out of Phase A scope. The story preserves Fluent v9 feature visibility
(reviewer can read the wrapper code in `DataGrid.tsx` line ~636 and see the
mapping); a future enhancement task can:
1. Add `'comfortable' | 'compact' | 'tight'` to the schema's `densityDefault`,
2. Update `DataGrid.tsx` to map `'tight'` → `size="extra-small"`,
3. Bump the `DensityExtraSmall` story config to use the new value.

### Follow-up
None for R1. Density enhancement is a Phase F+ candidate (track in
`spec.md` enhancement backlog if not already there).

---

## D-3: Step 5 (capture MDA reference screenshots) deferred to human

### What the task asked for
Steps 5–7 of the POML: capture MDA native screenshots, capture matching
framework screenshots, perform visual diff comparison.

### What I did (deviation)
Captured the protocol in `phase-a-acceptance-gate.md` section 7 (Manual MDA
visual-diff capture — HUMAN-loop instructions). No actual screenshot capture
occurred because:
1. The sub-agent runs in a non-browser environment (no live MDA tenant access,
   no display, no screenshot tooling).
2. The task brief explicitly lists "manual MDA visual diff screenshot capture
   (requires browser + live MDA env)" as OUT OF SCOPE for the sub-agent.

The protocol is fully documented; the human reviewer can pick up the work from
the directions in section 7.

### Why this is acceptable
The task POML's notes section explicitly states: "in R1 we accept manual
review (Ralph eyeballs side-by-side screenshots) since installing Chromatic
adds CI complexity." This is the agreed R1 approach. Acceptance criterion 3
in the parity report is marked PARTIAL pending the human-loop step.

### Follow-up
Human reviewer runs section 7 of `phase-a-acceptance-gate.md` against a live
MDA tenant and the framework's `DataGrid → DefaultSprkEvent` story.

---

## D-4: `/code-review` and `/adr-check` skill invocations deferred to main session

### What the task asked for
Step 12 of the POML: "Run `/code-review` and `/adr-check` on all Phase A
code."

### What I did (deviation)
Programmatic grep-based code-review gate (POML step 8) ran clean — all 6
checks passed. Full skill invocation deferred to main session per the task
brief's "OUT OF SCOPE for sub-agent" list:
> "Invoking /code-review and /adr-check skills (those run best in main session)"

Acceptance criterion 5 in the parity report is marked PARTIAL pending the
main-session skill runs.

### Follow-up
Main session invokes both skills on the Phase A code surface (tasks 001–008):
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/**/*.{ts,tsx}`
- `src/client/shared/Spaarke.UI.Components/src/hooks/useDataGridContext.ts`
- `src/client/shared/Spaarke.UI.Components/src/services/IDataverseClient.ts`
- `src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts`

---

*Open items resolved or routed; sub-agent scope complete.*
