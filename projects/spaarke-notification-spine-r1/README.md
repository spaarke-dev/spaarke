# Spaarke Notification & Action Spine — R1

> **Last Updated**: 2026-07-20
>
> **Status**: In Progress (planning complete; execution gated on FR-01 spike)

## Overview

Builds the platform's **server-initiated, typed-signal → grounded-action → delivery spine once**: a session-agnostic domain-action seam (Layer A), a durable per-user `kind`-typed outbox (Layer B), Azure SignalR real-time delivery with a host-agnostic client subscriber (Layer C), and per-source producer policy (Layer D). Proven in R1 by two consumers of deliberately different shapes — Communication Responsive Intelligence (assessed communication → auto-created Event/Task/Notification) and proactive Daily-Briefing suggestions (`kind=suggestion` cards). Doctrine: **no second push/delivery mechanism**; the spine is dumb transport — every "should we act/push?" decision stays grounded + gated in per-source producer policy.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Implementation plan, WBS, and wave order |
| [Design Spec](./spec.md) | AI-optimized specification (source of truth for requirements) |
| [Design Doc](./design.md) | Original human design (preserved verbatim) |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status |
| [AI Context](./CLAUDE.md) | Session-start context for Claude Code |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development (Phase 0 — gate-zero spike is the first task) |
| **Progress** | 0% (planning artifacts + tasks generated) |
| **Target Date** | — (set after FR-01 go/no-go) |
| **Completed Date** | — |
| **Owner** | Spaarke platform |

## Problem Statement

Three sibling projects (`email-communication-solution-r4`, `messaging-communication-app-r1/r2/r3`, `spaarkeai-assistant-enhancements-r1`) each independently need server-initiated push and grounded auto-actions. Without a single shared spine, each forks its own push channel, gate, and delivery store — the exact "forked consumer" failure mode the design forbids. Concretely: comms RI cannot auto-create records without a chat session; proactive suggestions have no delivery path; messaging-r3's release gate (its task 045) is blocked with no `communication-arrived` contract to consume; and offline users lose every signal because nothing durable backs the push.

## Solution Summary

A four-layer spine built **once** in the BFF and shared by all consumers: **Layer A** promotes the existing node executors (`CreateNotification`/`CreateTask`/`UpdateRecord`) to a session-agnostic action seam behind the executors, exposed via the `PublicContracts` facade. **Layer B** is a thin `sprk_` durable `kind`-typed outbox (per-user pending rows). **Layer C** is Azure SignalR delivery (mode decided by the FR-01 spike) plus a host-agnostic shared client subscriber library and a `kind`-generic poll fallback. **Layer D** is per-source producer policy (comms tenant/matter rules + confidence gate; Daily-Briefing grounding + ADR-041 gate). The outbox is the durable source of truth; live push is acceleration only. Grounding and gates live in producers — the spine never carries ungrounded/ungated content.

## Graduation Criteria

The project is considered **complete** when:

- [ ] **FR-01 gate-zero spike recorded**: SignalR mode chosen, compressed publish size measured vs 55/60 MB bands, cold-start + CVE + CSP `connect-src` verified (spike notes + go/no-go doc).
- [ ] **Comms-RI E2E**: an assessed inbound communication auto-creates configured Event/Task/Notification records and surfaces in Daily Briefing (matched tenant/matter rule).
- [ ] **`communication-arrived` producer**: a persisted communication (email AND message) yields a `communication-arrived` outbox row + live ping (seam test both channels).
- [ ] **Layer-A extraction is behavior-neutral**: chat/playbook dispatch behavior unchanged after extraction (pre-extraction characterization + seam tests pass unmodified).
- [ ] **Degrade path**: with SignalR disabled (null-object), all signals still deliver via the pending endpoint + `appnotification` mirror.
- [ ] **Fan-out security**: a private thread's event reaches ONLY shared participants; internal-only never reaches external users (negative-access seam tests + named security sign-off).
- [ ] **Suggestion consumer**: a grounded, gated suggestion renders as a card; acting on it re-enters `SurfaceLaunch`/dispatch identically to the reactive equivalent; ungrounded candidates never reach the outbox.
- [ ] **Notification leg flip**: `DispositionRoutability.Notification` is `Routable=true`, preceded by the reviewed "what lights up" audit.
- [ ] **R3 unblock**: shared subscriber library consumed by the workspace AND documented for R3's PCF/code-page hosts; messaging-r3's task 045 unblocked via the contract-lock note (acknowledged at their P1).
- [ ] **ADR-047 authored** (concise `.claude/adr/` + full `docs/adr/`) with CHANGELOG entry.
- [ ] **BFF hygiene**: every BFF task reports publish size; final ≤60 MB (55 MB review band respected); 0 new HIGH CVE.

## Scope

### In Scope

- Gate-zero SignalR footprint spike (Serverless vs Default; go/no-go BEFORE Layer-C placement).
- Layer A — session-agnostic action seam behind the node executors, via `PublicContracts`.
- Layer B — `kind`-typed durable `sprk_` outbox table + write/read/expiry service.
- Layer C — Azure SignalR delivery + host-agnostic shared client subscriber library + `kind`-generic pending/poll fallback.
- Typed envelope contract (`kind` discriminator; IDs + minimal display metadata only).
- `communication-arrived` producer (spine emits at persistence time for ALL channels; R3 consumes only).
- `kind` taxonomy lock (`suggestion` | `communication-assessed` | `communication-arrived`; reserve `job-complete` | `share` | `system-alert`).
- Comms-RI proving producer + comms policy layer (re-homed email-r4 050–054).
- `DispositionRoutability.Notification` leg flip (after the "what lights up" audit).
- Suggestion consumer (absorbed assistant R1.5): Daily-Briefing `kind=suggestion` producer + Assistant renderer branch reusing the shipped dispatch/ack.
- ADR-047 (concise + full).
- R3-P1 contract-lock deliverable.

### Out of Scope

- Routing comms RI through `EventRulesService.FireAsync` (reuse gate *primitives* only).
- Any second push/delivery mechanism (per-consumer hubs, parallel proactive-action path) — hard MUST NOT.
- Messaging fan-out consumers (messaging-r3 builds its own consume-side badge/toast).
- The `Record` disposition leg flip (Notification only in R1).
- Presence/idle detection (consumer concern).
- Consumers for reserved kinds (`job-complete`/`share`/`system-alert`).
- SSE code rework (SSE stays chat-only presentation).
- Per-customer SignalR provisioning automation beyond wiring into the ADR-027 orchestrator.

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Spine built once in BFF, shared by all consumers | BFF is the sole policy/token point; three siblings independently designed the same need | [ADR-047](../../docs/adr/) (this project) |
| Outbox written BEFORE SignalR ping | Durable truth; push is acceleration; offline users must not lose signals | ADR-041/043 (store-before-render) |
| SignalR mode decided by spike, not assumed | Send-only topology favors Serverless; burden of proof on Default | FR-01 |
| Notification flip goes THROUGH the disposition registry | ADR-043 is the ONE disposition source of truth; never route around it | [ADR-043](../../.claude/adr/ADR-043-ai-capability-execution-spine.md) |
| Grounding + gates live in producers, not the spine | Spine is dumb transport; NFR-03 | ADR-039/041 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| SignalR SDK pushes BFF publish >60 MB | High | Medium | FR-01 spike measures BOTH modes first; if >60 MB, hub/negotiate moves out of BFF before Layer-C tasks start |
| Fan-out targeting leaks a private thread to external users | High (compliance incident) | Low | FR-08 derives targeting from Dataverse record security; test-verified negative-access cases; named security sign-off (R-5) |
| Layer-A extraction breaks chat/playbook dispatch | High | Medium | Characterization tests pin the chat path BEFORE extraction; seam tests are DoD |
| Merge collision on `Services/Communication/**` with email-r4 / messaging-r3 | Medium | High | email-r4 W10 must merge first; `/conflict-check` before every BFF PR; coordinate at messaging-r3 P1 |
| Second push channel accidentally built (assistant R1.5 SignalR design) | High | Medium | R1.5 absorbed into this project; single contract; renderer is a chip source on the shipped dispatch, not a new pipeline |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| email-r4 W10 merged | Internal | Blocked (pending) | Must merge before this project's enrichment/persist-path touches; `/conflict-check` per BFF wave |
| messaging-r3 Phase-1 coordination | Internal | Coordinate | Their tasks 002–005 serially edit the same persist/read path; merge-order agreement at their P1 (with FR-19 lock) |
| Azure SignalR Service instance | External | Ready | Mode + tier per FR-01 (~$49/mo/unit Standard); per-customer provisioning via ADR-027 |
| Target-env CSP `connect-src` | External | Verify | Must allow the SignalR endpoint (verified in FR-01; silent-fallback risk if missed) |
| New Dataverse outbox table | Internal | Not started | Schema applied per environment; `docs/data-model/` entry required |
| Shipped substrate (enrichment emit point, thread model, participant junction, create-flow vertical, appnotification path) | Internal | Ready | Verified on master 2026-07-20 |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-07-20 | 1.0 | Project artifacts generated via `/project-pipeline` (README, plan, CLAUDE.md, current-task) | project-pipeline |

---

*Generated by `/project-pipeline` from `spec.md`. Source of truth for requirements is `spec.md`; original design preserved in `design.md`.*
