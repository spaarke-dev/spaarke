# Task 010 — Deviations from literal POML wording

> Task: `010-widen-xrmcontext-typings.poml` — completed 2026-08-13.
> No escalation trigger fired (no breaking change). This note documents the one
> place implementation went slightly beyond the literal step-2 wording, per
> `task-execute` "directional" step-mode guidance (adapt when the codebase state
> warrants it, note the deviation).

## Deviation: `PageInput.data` widened to `Record<string, any> | string`

**Literal ask (step 2):** "Fix the `PageInput` type so the webresource pane
input uses `webresourceName` (not `webresource`)."

**What was also done:** `PageInput.data` was widened from `Record<string, any>`
to `Record<string, any> | string`.

**Why:** The canonical reference file cited in the task's own
`<relevant-files role="canonical-reference">` —
`notes/retired-sidepane-code/SidePaneManager.ts` — calls
`pane.navigate({ pageType: 'webresource', webresourceName: config.webResource,
data: data })` where `data` is a URL-encoded query string
(`getContextData()` returns `params.toString()`), not a `Record`. Without
widening `data`, that real-world call shape would not typecheck against the
newly-typed `PageInput`. Since `data` was already optional and the widening
only ADDS an allowed shape (no existing caller's `Record`-shaped `data` stops
compiling), this is additive/non-breaking — same class of change the task
explicitly authorizes for `webresourceName`.

**Verification:** confirmed via `tsc` full-project build (0 errors, no
consumer edits needed) and covered by a new test case
(`PageInput webresource contract > still supports Record-shaped data for
entityrecord/entitylist page inputs`) proving the `Record` shape still
type-checks alongside the new `string` shape.

**Resolution path:** Path C (pivot to comply / straightforward extension of
the same additive-widening approach the task already sanctioned) — not
escalated, since no ADR or breaking-change concern applies. Flagged in
task-execute Step 9.5 code-review as a Suggestion-level visibility note, not
a Warning or Critical finding.
