# Lessons learned — reuse-governance-tightening-r1

> One lesson per entry. `doc-drift-audit` / `ai-procedure-maintenance` decide which ones get promoted to `.claude/FAILURE-MODES.md`, a skill or an ADR. This file does not edit those.

## L1 — Check an outside assessment's premise before scoping the work

The external synopsis framed the risk as under-reuse. Measured here, the risk was over-promotion: 15 shared packages, 11 of them unsanctioned under ADR-012's "etc.". The existing `<justification>` gate already worked. Scoping to the measured risk is what kept the project to lightweight mechanisms (index, one scan, an observation-only sweep, package decisions) instead of a blocking duplication gate.
**Promote?** Already covered by the feedback memory "independent architecture review before fix". No new rule.

## L2 — A low-effort read-only scan is enough when a dry-run proves it

`code-mapper` at sonnet/`low` caught both seeded duplicates in the task 010 dry-run, so the scan shipped at `low` with no recurring agent cost. Gating the effort level on a measured dry-run, not a guess, avoided paying for `medium` by default.

## L3 — Retiring a package means regenerating every consumer's lock file

Task 080 first recorded the stale `@spaarke/document-operations` edge in `src/solutions/LegalWorkspace/package-lock.json` as a known limit with "no failing path". That was wrong. The "Client lint (type-aware ratchet)" job regenerates every lock with `npm install --package-lock-only --legacy-peer-deps` and fails on any diff, so PR #2843 failed. Lesson: when a package is removed, regenerate the lock of every solution that depended on it, directly or transitively, in the same change.
**Promote candidate:** a checklist line in the package-consolidation guidance (ADR-012 / `ai-procedure-maintenance` decides).

## L4 — A jest module-mock factory replaces the whole module

Moving `useDocumentActions` into `@spaarke/ui-components` broke `ComposeWorkspace.search.test.tsx`. Its existing `jest.mock('@spaarke/ui-components', factory)` replaced the whole module, so the hook, previously unmocked from another package, vanished. The fix was `jest.requireActual(...)` for the moved export. Lesson: when consolidating packages, grep the consumers' tests for `jest.mock('<target package>'` and add the moved exports to each factory.

## L5 — Closing an issue does not put the fix on master

ISS-001 (#2683) was closed on 2026-11-15 when its fix (`71df6a2f44`) was committed to the project branch. The fix reached master only with the project's final PR. Lesson: close a docs/defect issue when its fix merges to master, or say "fixed on branch X, not yet on master" in the closing comment.

## L6 — Re-arming a blanket warning suppression can be free; measure first

The repo-wide `CS0618` downgrade hid zero errors: a clean Release build with it re-armed had 0 `CS0618` errors. The `Directory.Build.props` comment justifying it (14 `DemoProvisioningOptions` callers) was stale. Lesson: a blanket suppression's stated reason decays; re-measure it before assuming removal is expensive.

## L7 — Per-session language servers: measure against Grep, with a warm-up, on the real model

The C# LSP trial (task 043) passed all gates but gave no gain over Grep (10/10 each) at 1.6× the cost, 6× the time and about 1.9 GB per session. Traps found: `csharp-ls` needs about 3 minutes to load `Spaarke.sln` while the plugin's request limit is 60 s, so an agent not told to retry silently falls back to Grep (one run did). Memory must be summed over the process tree, because the MSBuild BuildHost child holds most of it. The global claude CLI 2.1.6 could not see marketplace plugins; the desktop app's bundled CLI could. Full record and re-run harness: `notes/csharp-lsp-trial.md`.

## L8 — After squash merges, keep the branch's own project notes

Merging master back into the project branch after #2843 and #2873 conflicted only on this project's notes (`current-task.md`, `080-review.md`, `decisions.md`, `TASK-INDEX.md`). Master held the PR-time snapshots and the branch held newer versions. Resolving with the branch side is safe, but only after diffing both sides to confirm master has nothing newer.

## L9 — An advisory check that is always red hides real findings

`Test-InstructionPaths.ps1` exited 1 on master from two false positives (a comma line-list suffix, a runtime folder), so a real new missing path would have looked like the usual noise. It was filed as ISS-002 and the checker now runs against a baseline ("new 0"). Lesson: an advisory check is only useful while it is green on master.
