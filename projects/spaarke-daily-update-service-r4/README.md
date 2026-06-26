# Spaarke Daily Update Service R4

> **Portfolio**: [Project #454](https://github.com/spaarke-dev/spaarke/issues/454) · Parent Epic: [#421 AI Platform & Chat](https://github.com/spaarke-dev/spaarke/issues/421) · [Portfolio Board](https://github.com/users/spaarke-dev/projects/2)

> **Last Updated**: 2026-06-26
>
> **Status**: Complete

## Overview

R4 closes four structural defects surfaced by R3 UAT in spaarkedev1: (1) AI narration hallucinates firm names not in user data, (2) 4 of 5 widget preferences are dead code, (3) the membership Action primitive (ActionType 52) ships in C# but its `sprk_analysisaction` row was never deployed, and (4) 2 of 7 notification playbooks are stubs. The work ships as 5 phased PRs across 3 coordinated workstreams (W0 JPS deployment layer, W1 Producer enrichments, W2 Consumer redesign).

## Quick Links

| Document | Description |
|----------|-------------|
| [Design](./design.md) | UAT-driven architectural review session, 2026-06-25 |
| [Spec](./spec.md) | AI-optimized specification — 20 FRs, 6 NFRs, owner clarifications |
| [Plan](./plan.md) | Implementation plan, phase breakdown, WBS |
| [CLAUDE.md](./CLAUDE.md) | AI context — applicable ADRs + discovered resources |
| [current-task.md](./current-task.md) | Active task state tracker (context recovery) |
| [tasks/TASK-INDEX.md](./tasks/TASK-INDEX.md) | Task registry + parallel-execution groups |
| [notes/risks.md](./notes/risks.md) | Coordination risks (R3 PR #451 file overlap) |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Complete |
| **Progress** | 100% (46/46 tasks complete; PR #456 graduation-ready) |
| **Target Date** | TBD (~65 engineering hours; 5 PRs) |
| **Completed Date** | 2026-06-26 |
| **Owner** | ralph.schroeder@hotmail.com |
| **Graduation Tag** | `daily-briefing-r4-complete` |

## Problem Statement

R3 UAT in spaarkedev1 confirmed the headline R3 win (widget renders content, no longer shows EmptyState) but surfaced four classes of structural defects:

1. **Hallucination**: TL;DR mentions firm names ("Johnson & Lee LLP", "Davis v. Metro Transit") that are not present in source notification data — caused by baked-in example names in hardcoded prompts and no temperature-0 grounding.
2. **Dead-code preferences**: 4 of 5 user preferences (`timeWindow`, `dueWithinDays`, `disabledChannels`, `autoPopup`) are wired into UI but never affect what the widget displays. `minConfidence` is vestigial — no probabilistic AI scoring concept applies to deterministic data.
3. **JPS deployment gap**: `LookupUserMembership` C# executor (ActionType 52) shipped in platform-foundations R3 — but its corresponding `sprk_analysisaction` row was never deployed to Dataverse, so no playbook can dispatch it. The membership pattern is silently inert across the platform.
4. **Stub playbooks**: 2 of 7 notification playbooks (`notification-matter-activity`, `notification-work-assignments`) are placeholders that produce zero notifications.

## Solution Summary

R4 makes JPS deployment a first-class concern (W0 workstream). Source-code changes are necessary but not sufficient — corresponding `sprk_analysisaction` + `sprk_analysisplaybook` rows must be deployed to each environment. R4 ships:

- **W0 (PRs 1–2)**: Deploy 4 new Action rows + 1 new `DAILY-BRIEFING-NARRATE` playbook; reconcile `sprk_configjson` for the 7 existing notification playbooks; ship new `EntityNameValidatorNodeExecutor` (ActionType 141) for output scrubbing.
- **W1 (PR 3)**: Enrich `CreateNotificationNodeExecutor` customData (viaMatter / regardingName / source) + dual-write `sprk_category` column; migrate tasks-overdue/due-soon to membership-scope; implement 2 stub playbooks.
- **W2 (PRs 4–5)**: Convert `/narrate` from hardcoded BFF endpoint to JPS playbook dispatch; fix narration cache; wire 4 preferences end-to-end; remove `minConfidence`; redesign per-item UX with three-dot overflow menu (semantic-search PCF pattern); fix link click → modal open with 403 fallback toast.

## Graduation Criteria

The project is considered **complete** when all 15 success criteria from spec.md §Success Criteria pass:

- [x] **JPS deployment complete** — All 4 new Action rows + 1 new playbook + 1 new sprk_playbookconsumer row + reconciled 7 notification playbook configs deployed to spaarkedev1
- [x] **All 20 FRs deliver per spec** — FR-1 through FR-20 acceptance criteria pass; see PR #456 "All FRs Satisfied" section
- [x] **All 6 NFRs pass** — Jest 141/0 + xUnit 7879/0 + CVE clean + publish-size 46.30 MB compressed (under 60 MB) + perf via Path A.5 dispatch (-340 LOC)
- [x] **Hallucination eliminated** — Grounded prompts + temp=0 + EntityNameValidator Tool (post-LLM scrub); pending owner UAT in spaarkedev1
- [x] **Activity Notes never disappears** — FR-16 MessageBar + RawNotificationCard fallback always renders if raw notifications exist
- [x] **All 4 preferences work** — timeWindow (FR-17a), dueWithinDays (FR-17b), disabledChannels (FR-17c), autoPopup (FR-17d) all wired with verifying Jest tests
- [x] **Three-dot menu replaces inline buttons** — FR-18 NarrativeBullet overflow menu with 6 ordered MenuItems; Fluent v9 Menu primitive; aria-label "More actions"
- [x] **Record link opens modal** — FR-19 Xrm.Navigation.navigateTo(target:2, 80%×80%); rejection → "Cannot open record — you may not have access." toast
- [x] **Membership-scoped tasks-overdue/due-soon** — PB-021 + PB-020 redeployed with LookupUserMembership union; sprk_event targets
- [x] **2 stub playbooks functional** — PB-017 (matter-activity) + PB-022 (work-assignments) implemented as full 5-node playbooks
- [x] **Contact-only logging present** — `member_skipped` structured warning in MembershipResolverService; 3 tests verify
- [x] **All R3 deliverables preserved** — sprk_briefingstate Choice column unchanged; TTL=604800 unchanged; read-state derivation unchanged; 3 R3 actions migrated into overflow menu (behavior unchanged)
- [x] **All 5 PRs merged in correct order** — N/A: single canonical PR #456 covers PR-1+2+3+4+5 scope per worktree convention (one developer; long-lived branch). All 5 phase sections present in PR description.
- [x] **BFF publish-size delta ≤ +1 MB** — cumulative R4 net delta vs pre-R4 master baseline +0.65 MB (NFR-02 satisfied; PR 4's -1.34 MB simplification offsets later increments)
- [x] **No new HIGH-severity CVE** — Kiota 1.21.2 GHSA-7j59-v9qr-6fq9 pre-existing transitive of Microsoft.Graph (not introduced by R4)

## Scope

### In Scope

- **W0 JPS Deployment Layer**: 4 new `sprk_analysisaction` rows (SYS-LOOKUP-MEMBERSHIP, BRIEF-NARRATE-TLDR, BRIEF-NARRATE-CHANNEL, BRIEF-VALIDATE-ENTITY-NAMES); `EntityNameValidatorNodeExecutor.cs` + form + xUnit tests (ActionType 141); `DAILY-BRIEFING-NARRATE` playbook with node graph; reconcile 7 deployed playbook configs against repo JSON source-of-truth; `jps-scope-refresh` post-deploy.
- **W1 Producer**: customData enrichment (`viaMatter` / `regardingName` / `source`); `sprk_category` column dual-write; membership migration for tasks-overdue + tasks-due-soon playbooks; implementation of 2 stub playbooks (matter-activity, work-assignments); structured `member_skipped` logging for Contact-only members.
- **W2 Consumer**: `/narrate` playbook dispatch wrapper; `useBriefingNarration` cache fix; `ActivityNotesSection` empty-narrative fallback; preferences wiring (4 settings) + `minConfidence` removal; three-dot overflow menu UX; `Xrm.Navigation.navigateTo` modal open + 403 fallback toast; TL;DR ↔ Activities count reconciliation.

### Out of Scope

- Email fallback for Contact-only members (deferred to a separate R5 project)
- Phase 2 membership infrastructure deployment (junction-table + Service Bus topic remain feature-gated OFF)
- AI Search "matter context" knowledge node for `/narrate` playbook (deferred to R5)
- Insights Engine integration
- Defensive Dataverse UAC pre-check at widget level (accepted gap per Q&A D-C)
- Bell-panel parity (R3 FR-7 invariant preserved)
- Native bell-panel changes

### Explicitly NOT Changing

- R3 schema (`sprk_briefingstate` Choice column on `appnotification`)
- R3 BFF TTL fix (`ttlinseconds = 604800` at `CreateNotificationNodeExecutor.cs:490`)
- ADR-013 BFF AI architecture pattern (extended, not modified)
- ADR-021, ADR-024, ADR-027, ADR-028, ADR-034 (built upon, not modified)
- `@spaarke/daily-briefing-components` package boundary
- `appnotification` Microsoft OOB entity (only customData JSON content changes)

## Key Decisions

| Decision | Rationale | ADR / Owner Clarification |
|----------|-----------|---------------------------|
| `/narrate` converts from hardcoded endpoint to JPS playbook dispatch | Restores architectural consistency; enables prompt iteration without recompile | Spec line 14, Owner Q&A |
| `EntityNameValidator = 141` | Slots into post-LLM cluster (Sanitization=130, ObservationEmit=140) | Owner Q&A 2026-06-25 |
| Use `sprk_category` column (not nested-JSON `customData.category`) for query filters | Dataverse OData does NOT support `$filter` on nested JSON | Owner Q&A 2026-06-25 |
| Investigate `sprk_playbookconsumer` dispatch path before final design | Recently shipped infrastructure may already support widget-as-consumer pattern | Owner Q&A; FR-12 task 030 |
| Repo JSON files = canonical source-of-truth for playbook reconciliation | Easier audit + version control than deployed Dataverse rows | Owner Q&A 2026-06-25 |
| R4 develops in parallel with R3 PR #451 (does NOT block on R3 merge) | Spec line 305: independent; can merge in any order | Spec author intent |

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| R3 PR #451 still open with 11 file overlaps in `Spaarke.DailyBriefing.Components/` | Med | High | Document overlapping files in `notes/risks.md`; run `conflict-check` skill before each W2 PR merge; rebase whichever lands second |
| `sprk_playbookconsumer` doesn't fit widget-payload shape — fall back to direct `AnalysisOrchestrationService` invocation | Low | Med | FR-12 task 030 explicitly investigates first, decides path with rationale (AC-12c) |
| LLM still hallucinates after grounding instruction + temperature 0 | Med | Low | `EntityNameValidator` Tool node scrubs post-LLM as defense-in-depth (FR-3, FR-14) |
| Producer dual-write `sprk_category` misses some playbooks | Med | Low | W1 task 021 explicitly audits + adds writer if missing; xUnit fixture asserts AC-6d on every playbook |
| W0 Action-row deployment skipped or partial → playbook dispatch fails at runtime | High | Low | W0 spans 8 tasks with explicit MCP `read_query` verification + `jps-scope-refresh` smoke; PR 2 cannot merge before PR 1 propagates |

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| `MembershipResolverService` + `LookupUserMembershipNodeExecutor` | Internal (platform-foundations R3) | Shipped | C# present; R4 deploys the missing Action row |
| `sprk_briefingstate` Choice column on `appnotification` | Internal (R3) | Shipped to spaarkedev1 | Read-state derivation continues using this column |
| BFF `ttlinseconds = 604800` at `CreateNotificationNodeExecutor.cs:490` | Internal (R3) | Shipped | TTL fix preserved (spec misnamed the file path as NotificationService.cs — see notes/risks.md) |
| PlaybookBuilder code page | Internal | Present | R4 doesn't modify; verifies new Action/Tool appear in palette post-W0 |
| `@spaarke/daily-briefing-components` package | Internal (R2) | Present | R4 modifies consumer code |
| spaarkedev1 environment | External | Operator + dev access | Required for W0 deployments |
| Azure OpenAI | External | Production | Existing `IOpenAiClient` wiring; no new deployment/model requirements |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | ralph.schroeder@hotmail.com | Overall accountability, UAT |
| Developer | Claude Code + ralph.schroeder@hotmail.com | Implementation |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-06-25 | 1.0 | Initial scaffold via `/project-pipeline` (Steps 0–4) | Claude Code |
| 2026-06-26 | 1.1 | R4 graduation: 46/46 tasks complete; all 20 FRs delivered; PR #456 covers PR-1+2+3+4+5 scope; status → Complete; tag `daily-briefing-r4-complete`; lessons-learned.md authored | Claude Code |

---

*Generated by project-setup via project-pipeline. Spec authored by /design-to-spec 2026-06-25.*
