# Task 057 - Console UI kit: notes

PR: shipped as its own PR to master (branch `feat/console-ui-kit-057`, worktree `C:\wts-b`). Shared `@spaarke/ui-components`.

## Step 0 - rigor and B.6 pre-start check
Rigor FULL (frontend, new shared components, modifies .tsx). Checked against HANDOFF @ ae1cc9f: section 1.3 (one shape per verb:
Show evidence = EvidenceLine; Show state = StatusBar + chip; Point = AggregateCard; Record entry = RecordRow, "no edit or delete
affordance"), section 1.4 L117-118 (evidence shown but not tested is labelled "context, not tested"; missing/stale shows as
Missing, never zero, never omitted) and L144 (Decision Record append-only, no edit/delete anywhere). No prototype code ported;
only the contract and the nine state names were read.

## Dependency decision (StatusBadge)
Task 012's `StatusBadge` exists only on the ontology branch; master has none. 057 ships to master as its own PR, so the PR carries
`StatusBadge/{StatusBadge.tsx,index.ts,__tests__}` copied byte-for-byte from the ontology branch (commit 1c56cd29b), then adds the
`success` tone, plus the barrel export. Why: the alternative (a PR that cannot compile on master) is unmergeable, and a second badge
is forbidden (R-15). Consequence for the ontology branch: when it merges master it will see an add/add conflict on the three
StatusBadge files; resolve by taking master's (a strict superset: the same code plus `success`).

## Section 11 justification (new components)
| Component | Existing (grep: no EvidenceLine/StatusBar/RecordRow/AggregateCard under src/client/shared at 0ea74d1c3f) | Extension? | Cost of doing nothing |
|---|---|---|---|
| EvidenceLine | `CitationBadge` is legal-citation verification; no tiered/sourced/clause-linked line | No: no component takes tier + source + as-of + clause | Wizard "What was found" is hand-rolled in SpaarkeAi (FR-28 forbids) and a null fact would render 0 or blank (H-5) |
| StatusBar | Fluent MessageBar + StatusBadge | Yes: it IS a composition (MessageBar), only the state-to-tone table and meta line are new | Each surface re-derives which closure maps to which tone; Routine shown as Authorized (R-4) |
| RecordRow | `DocumentRowMenu` dropped (D-46); no Decision Record row exists | No: a row with no menu and an Open link is a different contract | Audit trail list gets a bespoke row with an edit/delete menu, violating FR-21/R-13 |
| AggregateCard | `MetricCard` (WorkspaceShell): square count tile, no link sentence; 052's non-square option has not landed | Not yet: composing needs 052; revisit then | Email Review aggregate is hand-rolled in the worklist, or is counted in lane counts |

## Decisions and small deviations from the POML
- Nine states (R-14/R-15): Open ("To decide"), Decided, Done, Dismissed, ClearedItself, Superseded, RuleRetired, Authorized, Denied;
  one exported table `CONSOLE_STATE_BADGES` and `resolveConsoleState(resolutionType, recordClass)` (Acted+Routine = Done).
- `EvidenceTier` accepts the stored `Observation` and displays it as Interpretation (the built `SignalEvidenceRef` uses Fact/Observation).
- No confidence prop anywhere (#5). `AggregateCard` null count reads Missing.
- Not built: Mode-2 harness (optional; the fixtures + jest cover the contract), so no visual dark-mode eyeballing; dark mode is
  asserted by rendering under `webDarkTheme` and by a no-colour-literal source scan. Step 9.7 browser test skipped (no deployed env).
- `tsc` on the package reports 9 errors, all unresolved `@spaarke/auth` / `@spaarke/sdap-client` (unbuilt siblings) and none in
  these files; the files typecheck clean. No `Xrm`/`getXrm` used, so the Tier 1 guard does not apply.

## Quality gates (Step 9.5)
Tests: 36 pass (26 kit + 10 StatusBadge); coverage 100% stmts / 97% branch on the touched files. code-review: no F-class finding;
K4 only (`toLocaleDateString` is locale-dependent, display-only). adr-check (ADR-012, 021, 022-compat): clean; no React 18/19-only API
used (no useId/useTransition), no Xrm, no hard-coded colour, tokens only. Prettier applied; eslint clean.

## D-50 issue
https://github.com/spaarke-dev/spaarke/issues/1381 (also ISS-007 in `defer-issues.md`).
