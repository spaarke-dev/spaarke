# Spaarke Project Coordination R1

> **Portfolio**: [Project #2089](https://github.com/spaarke-dev/spaarke/issues/2089) · [Board #2](https://github.com/users/spaarke-dev/projects/2) · Parent [Epic #429](https://github.com/spaarke-dev/spaarke/issues/429)

> **Last Updated**: 2026-10-18
>
> **Status**: Initialized — tasks generated, execution not started (owner review first)

## Overview

Spaarke sessions already message each other unprompted, and this project keeps it that way. It fixes the mechanical reasons messages fail (held messages expiring, addresses changing on restart, truncated names colliding, an outdated `claude` binary). It then adds a small durable layer for what messaging can't reach: projects with no session running, deploys that overwrite another project's live build, writes into another session's worktree, and findings that turn into unbounded tasks.

## Quick Links

| Document | Description |
|----------|-------------|
| [Spec](./spec.md) | Governing specification (rev 2) |
| [Design](./design.md) | Design rev 3, with change log |
| [Plan](./plan.md) | Phases, critical path, risks |
| [Tasks](./tasks/TASK-INDEX.md) | Task registry and parallel groups |
| [Naming spike](./notes/naming-spike-2026-10-17.md) | Session-address evidence behind FR-00 |
| [Pilot](./notes/pilot-issue-triage-2026-10-15.md) | uac-r2 triage pilot behind FR-05 – FR-12 |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Initialized — phase 0 next |
| **Progress** | 0% (see `tasks/TASK-INDEX.md`) |
| **Target Date** | — |
| **Completed Date** | — |
| **Owner** | Spaarke owner (single operator) |

## Problem Statement

Cross-session coordination works when sessions are running and messages get through. Evidence from 2026-10-15 shows where it doesn't:
- three messages were lost (two to restarted sessions, one held until it expired);
- a redeploy removed another project's live fix (E1);
- dev settings changed with no shared record (E3);
- a session deleted files in a peer's worktree (E4);
- PRs edited another project's files unseen (E5);
- one project's findings grew it from about 75 to 184 tasks (E6).

## Solution Summary

Phase 0 fixes messaging configuration only and **proves it by measurement** before anything else touches messaging. The project adds no text anywhere about when or how to message. Later phases add deterministic records for sessions that aren't running: a deploy ancestry guard and dev log, a worktree write boundary, a six-line finding-triage prompt, per-project owned areas, a report-only detector, a start-of-session banner, and a critical-area acknowledgement in `Router` that the owning project's session applies itself.

## Graduation Criteria

The project is **complete** when (spec success criteria 1–11):

- [ ] A deploy that would remove a live build not contained in the new one is refused (E1 replay), on every .NET App Service deploy path
- [ ] Every shared deploy leaves one parseable dev-log entry
- [ ] Messaging is reliable: project-addressed, no held, expired or misaddressed deliveries, and reachable after a restart
- [ ] Cross-session messaging volume doesn't drop at the phase 0, phase 2 and phase 5 measurements, and doesn't shift to item-prompted messages
- [ ] Triage on a project-labelled issue set respects on-issue rulings; tracking issues are skipped
- [ ] A restarted session sees coordination records made while it was down
- [ ] Router blocks an un-acked critical-area PR and passes once the owning project's session applies `ack`
- [ ] All new hooks and guards ship with must-fire and must-not-fire tests, and the tests pass
- [ ] At close: zero undisposed owned issues, zero owned areas, and no messaging-guidance wording in any file the project changed

## Scope

### In Scope

- Phase 0: messaging configuration (FR-00) and measurement (FR-26), proved by a 14-day checkpoint
- Phase 1: deploy ancestry guard, dev log, raw-command hook, worktree write boundary (FR-01 – FR-04, FR-25)
- Phase 2: constraint file and triage prompt; issue hygiene (FR-05 – FR-12)
- Phase 3: README-frontmatter registry; detector signals 1–2, report-only (FR-13 – FR-15)
- Phase 4: pre-code coordination issues; `SessionStart` banner; optional live notice (FR-17, FR-18, FR-18b)
- Phase 5: Step 0 / Step 9 checks; Router acknowledgement; `/conflict-check` reuse; owned-area handover (FR-16, FR-20 – FR-23)

### Out of Scope

- Any rule or guidance about when, whether, how often or in what form to message
- BFF, Dataverse schema or product UI changes
- The owner's status digest (FR-24, deferred)
- Detector signal 3 enabled by default; Channels; cross-machine messaging
- Applying uac-r2's pending pilot decisions

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| No messaging guidance text anywhere (2026-10-17) | Out-of-the-box judgment works; any guidance is a nudge | — |
| Phase 0 proved before phases 3–5 (2026-10-17) | Show that configuration improves delivery without suppressing messaging | — |
| README frontmatter is the registry (2026-10-17) | Per-project, parseable, no shared-file contention | — |
| `ack` applied by the owning project's session (2026-10-17) | Autonomous; no owner orchestration | — |
| Live version read from the DLL through Kudu, not `/ping` | `/ping` returns `pong`; no anonymous endpoint change | [ADR-028](../../.claude/adr/ADR-028-spaarke-auth-architecture.md) untouched |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| Written process suppresses unprompted messaging | High | Low | No guidance text; FR-26 measurement blocks the next phase on a drop |
| VS Code rename doesn't set the messaging address | Medium | Medium | FR-00 mechanism 3 (lookup fallback); task 002 decides |
| Router change blocks the wrong PRs | Medium | Medium | Globs proved by replay (task 020); owner approves before the task 051 merge (D-1) |
| Shared skill edits collide with other projects | Medium | High | Small PRs after `/conflict-check` (NFR-06); `.claude/**` tasks run sequentially |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Board #2 triage fields | Internal | Ready | Added 2026-10-15 |
| `/project-pipeline` registration MUST (PR #1983) | Internal | Ready | Merged 2026-10-16 |
| `gh` authenticated; Kudu management-token access | External | Ready | Same access `Deploy-BffApi.ps1` uses |
| D-1: owner approves the `ci-router.yml` change | Internal | Pending | Blocks the task 051 merge only |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Spaarke owner | Decisions, phase checkpoints, user settings, live deploy approval |
| Developer | Claude Code sessions | Implementation per `tasks/` |
| Reviewer | `code-review` / `adversarial-reviewer` | Per task rigor |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-10-18 | 1.0 | Initialized by `/project-pipeline` | Claude Code |
