# Repo-Cleanup Audit — Task 090

> **Project**: spaarke-daily-update-service-r4
> **Date**: 2026-06-26
> **Run by**: Task 090 wrap-up
> **Mode**: AUDIT ONLY — DO NOT auto-remove. Owner's call.

This audit reports ephemeral files within `projects/spaarke-daily-update-service-r4/notes/`. Removal is an owner decision; this task does NOT auto-clean per the constraint in the task 090 dispatch protocol.

---

## `notes/debug/` (8 files)

Per-task publish-size + smoke notes — produced by §10 BFF Hygiene gates during execution:

| File | Source task | Recommended action |
|------|-------------|--------------------|
| `001-mcp-smoke-deferred.md` | task 001 (MCP smoke deferred) | KEEP — explains why a smoke was deferred; closed by task 030 |
| `002-publish-size.md` | task 002 (enum addition) | DELETE — superseded by later measurements |
| `003-publish-size.md` | task 003 (executor authoring) | DELETE — superseded |
| `020-publish-size.md` | task 020 (customData enrichment) | DELETE — superseded by PR 3 wrap (task 029) |
| `021-publish-size.md` | task 021 (sprk_category audit) | DELETE — superseded |
| `027-publish-size.md` | task 027 (member_skipped warning) | DELETE — superseded |
| `031-publish-size.md` | task 031 (HandleNarrate dispatch) | DELETE — superseded by PR 4 wrap (task 036) |
| `034-test-infrastructure.md` | task 034 (fallback test infra) | KEEP — has reusable Jest test-infra notes |

**Recommended action**: Owner reviews; auto-clean is safe for the 6 `*-publish-size.md` files (superseded by cumulative PR wrap measurements in the PR #456 body). The 2 KEEPs retain pedagogical value.

---

## `notes/drafts/` (empty)

No files. No action.

---

## `notes/spikes/` (empty)

No files. No action.

---

## `notes/handoffs/` (empty)

No files. No action. (R4 was a single-developer single-worktree project; no inter-session handoffs were needed.)

---

## Retained directories (intentional preservation)

These directories contain durable artifacts and should NOT be cleaned:

| Directory | Purpose | Files |
|-----------|---------|-------|
| `notes/audit/` | Playbook + entity audits (PR 2 Wave C) | Retained |
| `notes/conflict-check/` | R3 PR #451 overlap analyses | Retained |
| `notes/decisions/` | Task 030 dispatch path decision (FR-12 / AC-12c) | Retained |
| `notes/deployments/` | Per-task Dataverse deployment records (4 Action rows + 1 playbook + 1 consumer + 7 redeployed) | Retained |
| `notes/design/` | Per-task design notes | Retained |
| `notes/risks.md` | Live risks log | Retained |
| `notes/samples/` | R3 narrate response golden sample (task 032 schema-conformance) | Retained |
| `notes/smoke/` | Per-task smoke evidence | Retained |
| `notes/lessons-learned.md` | R4 lessons (this wrap-up task) | **NEW** (task 090) |
| `notes/repo-cleanup-audit-090.md` | This file | **NEW** (task 090) |

---

## Summary

- 6 files RECOMMENDED for cleanup (per-task publish-size notes superseded by cumulative PR-body measurements)
- 2 files RECOMMENDED for retention (pedagogical value)
- 0 spikes / drafts / handoffs to clean (all empty)
- All durable folders preserved

**Owner action**: review the 6 RECOMMENDED-DELETE files and either confirm auto-clean or decide otherwise. R4 task 090 will NOT auto-delete.
