# Spaarke Notification & Action Spine — R2 - AI Context

> **Purpose**: Context for Claude Code working on spaarke-notification-spine-r2.
> **Always load this file first** when working on any task in this project.

---

## Project Status

- **Phase**: **Pre-spec** (investigation hydrated; no spec/plan/tasks yet).
- **Last Updated**: 2026-08-20
- **Next Action**: Run `design-to-spec` using [`notes/INVESTIGATION-AND-ASSESSMENT.md`](notes/INVESTIGATION-AND-ASSESSMENT.md) as the input, resolving its §9 open questions; then `project-pipeline`.

---

## What this project is

Rebuild the proactive-suggestion **surface** removed after r1 (the in-Assistant renderer, deleted by `spaarkeai-assistant-enhancements-r2` FR-E1) as **OOB Dataverse notifications**: a **scheduled BFF job** → grounded+gated items → deduped against `sprk_notificationoutbox` on a **7-day** window → writes an outbox ledger row **and** a native `appnotification` whose action opens the record in a **modal** (`navigationTarget:"dialog"`).

**The full investigation is in [`notes/INVESTIGATION-AND-ASSESSMENT.md`](notes/INVESTIGATION-AND-ASSESSMENT.md)** — read it before authoring the spec. It captures the finding, gap analysis, the six owner decisions (2026-08-20), the researcher's OOB `appnotification` findings, the proposed architecture, component-justification pre-work, and open questions.

---

## Key constraints (carried from r1 + owner decisions)

- **OOB bell is the surface** — NOT a custom Assistant card. Do NOT re-add `useSuggestionCards.tsx` or any SpaarkeAi suggestion renderer.
- **No second push/delivery channel** — one spine (ADR-047). Reuse the outbox + `NotificationService` + `@spaarke/notifications` client models.
- **Ledger-first dedup** — write the `sprk_notificationoutbox` row and dedup on it BEFORE the `appnotification` write. `appnotification` has no dismiss state (dismiss = delete), so it cannot self-dedup — the outbox is the ledger. 7-day window; a dismissed nudge must NOT reappear in-window.
- **Modal-on-click** — `appnotification.Data.Actions[].data`: `type:"url"` + `navigationTarget:"dialog"` + same-origin `?pagetype=entityrecord&etn=<type>&id=<id>` URL (bare-relative / js / data URLs are blocked → silent no-op).
- **Reuse, don't fork** (CLAUDE.md §11) — share the r1 grounding/gating (`DailyBriefingSuggestionProducer`), don't duplicate it. Extend `OutboxService` for the dedup query rather than a parallel store.
- **BFF hygiene** (CLAUDE.md §10) — Placement Justification + Hot-Path Declaration (BFF=Y) in `design.md`; publish ≤60 MB; 0 new HIGH CVE; `/conflict-check` before BFF PRs.
- **Scheduler** — reuse a sanctioned BFF background-work host (`ServiceBusJobProcessor` / hosted `BackgroundService`), don't invent a new scheduler (open question §9-Q1).

---

## Task Execution Protocol

Once tasks exist, all task work MUST use the `task-execute` skill (root CLAUDE.md §4). Until then this project is investigation-only.

---

## Deferrals & Issues

Track via `/project-defer-issue-tracking` (writes `notes/defer-issues.md` + a GitHub issue). None yet.

---

## Resources

- **Predecessor**: `projects/spaarke-notification-spine-r1/` (backend spine, ADR-047).
- **Researcher memo**: `.claude/agent-memory/researcher/appnotification-modal-click-schema-2026-08-20.md`.
- **Corrected docs**: `docs/architecture/SPAARKE-NOTIFICATION-SPINE-ARCHITECTURE.md`, `docs/guides/NOTIFICATIONS-AND-SUGGESTIONS-USER-GUIDE.md`, `docs/data-model/sprk_notificationoutbox.md`.
- **Applicable ADRs**: ADR-047 (spine), ADR-039 (grounding), ADR-041 (gate), ADR-024 (regarding), ADR-028 (auth/owner-scope), ADR-032 (null-object), ADR-038 (seam tests DoD), ADR-027 (per-customer provisioning).

---

*Keep this file updated through the project lifecycle.*
