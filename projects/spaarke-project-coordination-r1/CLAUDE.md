# Spaarke Project Coordination R1 — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

This project makes cross-session messaging reliable **without constraining it**: configuration, stable project addresses, a version floor, and measurement. It then adds a minimal deterministic record layer for what messaging can't reach: the deploy ancestry guard and dev log, the worktree write boundary, the triage prompt and issue hygiene, README-frontmatter owned areas, a report-only detector, the start-up banner, and a critical-area `ack` in Router.

Out of scope: any guidance on when or how to message; BFF, Dataverse or product UI; the status digest (FR-24, deferred).

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md` (rev 2, governs). Design: `design.md` (rev 3). Plan: `plan.md`.

## 2. Binding rules for this project

- **No text about when, whether, how often or in what form to message** — no encouragement, examples, limits or templates — in any file this project creates or edits (spec MUST NOT, FR-05 negatives, NFR-09, success criterion 11). Before committing any `.claude/**`, hook or skill edit, grep the diff for messaging guidance.
- **Phase 0 is proved first.** Phases 3–5 (tasks 030+) don't start until task 005's checkpoint passes. Phases 1–2 may run during the 14-day wait (spec Scope).
- Every new hook or guard: `ask`, never `deny`; under 5 s; no network calls; must-fire and must-not-fire fixtures; "Enforced by:" recorded (NFR-02, root §16).
- Never deny `SendMessage` / `ListAgents`; never set `isolatePeerMachines`; no master ruleset change.
- Never block on a paused or closed project (FR-20, FR-22).
- Read the live version from the deployed DLL via Kudu; never add a version to an anonymous endpoint (K-1).
- Guard every shared .NET App Service deploy path, `Deploy-WorkspaceBff.ps1` included (K-2, FR-02).
- `.claude/**` edits are main-session-only (root §3): those tasks are `parallel-safe:false`.
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub issue — never one without the other.

ADR tensions approved for this project (root CLAUDE.md §6.5): none.

## 3. Owner directives and standing decisions

- 2026-10-17 — No messaging guidance text; out-of-the-box judgment works, and guidance is a nudge — see `notes/decisions.md#no-guidance`
- 2026-10-17 — Phase 0 measured checkpoint gates phases 3–5 — `notes/decisions.md#phase0-gate`
- 2026-10-17 — Registry = README frontmatter; `projects/INDEX.md` not edited for it — `notes/decisions.md#registry`
- 2026-10-17 — `ack` applied by the owning project's session (autonomous) — `notes/decisions.md#ack`
- 2026-10-15 — Deploy guard on all .NET App Services; dev log is a pinned issue; critical areas = access control, auth, ownership + secure records, Dataverse write path; ack folded into Router; detector also reads local worktrees — spec C-3 to C-7
- 2026-10-18 — Parent Epic #429 (DevOps & Code Quality)

## 4. Coordination

- **Every active project** uses the skills this project edits (`task-execute`, `task-create`, `project-pipeline`, `design-to-spec`, `conflict-check`, `devops-*`, `project-defer-issue-tracking`). Land each as a small PR after `/conflict-check` (NFR-06).
- **`ci-cd-unit-test-remediation-r1`** — Router's `docs_only` skip stays with that project. Agree the FR-22 change (D-1) before task 051 merges.
- **PR #2043** (`feat/context-checkpoint-hooks`) edits `.claude/settings.json` and root `CLAUDE.md`. Rebase onto it, or merge after it, for tasks 003, 014, 020 and 041.
- **uac-r2** — pilot source. Its 208 pending decisions are uac-r2's work, not ours.

## 5. Environment and live actions

- **User settings** (`~/.claude/settings.json`: `crossSessionInbound`, `dialogExpiry`) are the owner's machine settings. Show the exact JSON, and the owner applies or approves it (task 004).
- **Live dev**: task 016 runs the guard against `spaarke-bff-dev`. The read-only refusal check is fine; any actual deploy needs explicit owner OK and goes through `/bff-deploy`.
- **GitHub writes** (pinned dev-log issue, labels, Router change) are outward-facing. Each is part of its task's approved scope; nothing else.

## 6. Gotchas — do not re-learn

- 2026-10-17 — The `claude` on `PATH` is an npm global at **2.1.6** (can't message). The VS Code extension's bundled binary is at `~/.vscode/extensions/anthropic.claude-code-<ver>-win32-x64/resources/native-binary/claude.exe`. Use that for spikes.
- 2026-10-17 — A `SessionStart` hook's `sessionTitle` sets the **title**, not the messaging **address** (headless 2.1.300). `--name` sets the address.
- 2026-10-17 — `claude -p ... --allowedTools "X"` swallows a following prompt argument (variadic). Put the prompt right after `-p`, and add `< /dev/null`.
- 2026-10-17 — `jq` isn't installed in git-bash. Use `gh ... --jq` or Python.
- 2026-10-17 — Foreground `sleep` is blocked by the harness. Use `run_in_background`.
- 2026-10-18 — Project label = the bare project name (`gh label create {name}`, `/project-pipeline` Step 4.2), not `project:<name>`.

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/decisions.md` · `notes/defer-issues.md`
- `notes/naming-spike-2026-10-17.md` · `notes/pilot-issue-triage-2026-10-15.md` · `revision-instructions-coordination-r1.md`
- Applicable ADRs: ADR-038 (testing), ADR-052 (placement: CI-hosted), ADR-028 (untouched)
- Related projects: `ci-cd-unit-test-remediation-r1` (Router), `unified-access-control-r2` (pilot)
