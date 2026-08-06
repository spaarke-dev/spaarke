# Email Communication Intelligence — R2

> **Last Updated**: 2026-08-05
>
> **Status**: In Progress (tasks generated; execution pending)

## Overview

R2 hardens the **trusted-capture** layer beneath R1's email association/triage engine: every email is matched to the right record **once** (deduped across mailboxes and users), filed through **one** intelligent path (mailbox or by hand), tracked by a transparent **signed footer**, and finally made **visible + actionable** through a reconciliation UI. Every item **extends** the R1 engine, capture path, Outlook add-in, or shared component library — no re-architecture (ADR-045 "extend, never fork").

## Quick Links

| Document | Description |
|----------|-------------|
| [Implementation Plan](./plan.md) | Phased WBS, dependencies, model tiers |
| [Design Spec](./spec.md) | AI-optimized spec (24 FRs / 11 NFRs / 5 pillars) |
| [Design Charter](./design.md) | Human design charter (source) |
| [Task Index](./tasks/TASK-INDEX.md) | Task tracker + parallel groups + hot-path coordination |
| [Project CLAUDE.md](./CLAUDE.md) | AI context for this project |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Development (tasks generated) |
| **Progress** | 0% (execution not started) |
| **Target Date** | — |
| **Completed Date** | — |
| **Owner** | ralph.schroeder |

## Problem Statement

R1 taught Spaarke to *understand* an email but left the capture layer soft: the same message in two mailboxes (or saved by two users) creates duplicate records; there is no content-level de-duplication of attachments; the "Save to Spaarke" upload path bypasses the intelligence engine entirely; threads Spaarke originates aren't recognized on return with high trust; and the extraction backend (triage, Job B field-updates, Job C tasks) feeds a queue **no UI renders** — the intelligence is stored but dark.

## Solution Summary

Five pillars, all additive to R1: **(A)** a signed, transparent tracking footer + new `TrackingTokenRung`, per-record recipient aliases, formalized thread self-association, and a deterministic learning loop; **(B)** a real unified intake folder + drag-to-matter add-in that runs the same engine as capture; **(C)** race-proof internet-message-id de-duplication (Dataverse alternate key) plus SPE content-hash dedup (absorbing `sdap-file-duplication-detector-r1`); **(D)** R1 carry-over fixes (RAG grounding, batched query, golden regression, Job B seed, Job C apply endpoint); and **(E)** a prototype-validated **reconciliation UI** — a DataGrid-enhanced grid over `sprk_communication` with a browse-shell + three tabs (Related-to / Fields / Tasks), one normalized reader with citation navigation, reusing `EmailConnectionsReview` + `SprkModal` + Compose's `CitationResolver`.

## Graduation Criteria

The project is **complete** when:

- [ ] The same email across N mailboxes + M users yields exactly **one** `sprk_communication` (race-proof; integration test + alternate-key)
- [ ] ~100% recognition of threads Spaarke originates (token + thread seam test)
- [ ] Drag-to-file and mailbox-capture produce **identical** engine output (association + triage + provenance parity test)
- [ ] Byte-identical file upload (any path incl. email-attachment) does **not** create a second canonical document (dedup seam test, post-spike)
- [ ] A matter-scoped RAG query returns that matter's correspondence (currently zero — FR-D1 test)
- [ ] Baseline + trend for the deterministic-resolution-rate (T0/P0) metric
- [ ] Add-in signs in and files against the BFF at runtime (manual UAT post Entra registration)
- [ ] Golden R1 UAT regression suite green (CI)
- [ ] Pillar E: a triaged email reconciled end-to-end (associate → edit field → Accept under audit → task with status → Save & confirm) on both the code-page and the SpaarkeAi widget
- [ ] BFF publish size ≤ 60 MB compressed; no new HIGH CVE

## Scope

### In Scope
- **Pillar A** — signed tracking footer + `TrackingTokenRung`; `RecipientAliasRung` + Bcc; formalized external-reply self-association; deterministic affinity learning loop
- **Pillar B** — Outlook add-in realignment; real Spaarke intake folder (both mechanisms); drag-to-matter + engine suggestions; unify user-upload with capture
- **Pillar C** — internet-message-id dedup + Dataverse alternate key; SPE content dedup Tier-1; cross-path reconciliation
- **Pillar D** — RAG grounding fix; batched identifier query; golden regression suite; Job B allow-list seed; Job C apply endpoint + create-task queue-feed kind
- **Pillar E** — reconciliation grid (enhance DataGrid); triage display; Related-to card-picker; field + task reconcile modals; one-reader/citation navigation; reconciliation routing; r5 coordination

### Out of Scope
- IP docketing / email→dated-obligations (recommended standalone R3)
- SprkChat over mail; Daily Briefing 7th channel; policy-based auto-apply of the confident band
- M365 group-mailbox capture; visible subject-line token; hidden `X-Spaarke-Regarding` header; ML ranker for the learning loop
- SPE dedup **Tier-2** (near-dup) — validated fast-follow gated on spike 2
- Firm-self-service Dataverse config surface for the footer (later enhancement)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| Footer config = operator-managed App Service app setting | Matches `AutoFileOptions` ADR-018 pattern; deployment config, changes rarely | ADR-018 |
| HMAC signing key in Key Vault, never in config | Secret hygiene | ADR-028 (Path A) |
| Affinity store is separate new state | Filing-history metadata, not session disposition | ADR-040 (Path A) |
| Reconciliation unit = `sprk_communication` (no new entity); routing via category→team config + filtered views | Extend, never fork | ADR-045 / ADR-024 |
| Pillar E enhances the shared `DataGrid`; reuses `EmailConnectionsReview`, `SprkModal`, `CitationResolver` | Default to reuse | §11 / ADR-050 / ADR-012 |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| SPE `quickXorHash` not stable immediately post-upload | High | Med | Spike-gated (P0-a) before building Tier-1 dedup |
| Heavy shared-lib contention (r5, dataset-grid-framework-r2, compose, messaging) | High | High | `parallel-safe:false` on shared writers; `/conflict-check` before every shared PR; coordination contract update |
| Footer injection triggers DLP/anti-spam | Med | Low | Transparent (non-hidden) footer + per-firm opt-out; deployment-checklist validation |
| Citation anchoring drift vs Compose | Med | Med | Reuse `CitationResolver` (no second mechanism); parity seam tests |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| Two SPE spikes (quickXorHash timing; Tier-2 threshold) | Internal | Pending | Gate Pillar C |
| Entra NAA app registration for add-in | External | Pending | FR-B0 runtime prereq |
| `sdap-file-duplication-detector-r1` | Internal | Absorbed | Design lifted into Pillar C |
| `email-communication-solution-r5` | Internal | Active | Owns shared components; coordination contract update (FR-E6) |
| `spaarke-dataset-grid-framework-r2` | Internal | Active | Shared DataGrid; conflict-check before FR-E2 PR |
| Exchange admin (per client) | External | Optional | Mail-flow rule for FR-A2 addresses |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-08-05 | 1.0 | Project initialized via /project-pipeline; tasks generated | ralph.schroeder |

---

*Based on the Spaarke development lifecycle. Source of truth for scope: [spec.md](./spec.md).*
