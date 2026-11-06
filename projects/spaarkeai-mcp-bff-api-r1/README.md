# Spaarke in Microsoft Copilot and Cowork: MCP Server in the BFF (r1)

> **Portfolio**: [Project #2608](https://github.com/spaarke-dev/spaarke/issues/2608) · [Board #2](https://github.com/users/spaarke-dev/projects/2) · Epic [#421](https://github.com/spaarke-dev/spaarke/issues/421) — _registered 2026-11-06 by /project-pipeline Step 4.2_

> **Last Updated**: 2026-11-06
>
> **Status**: In Progress (project pipeline: artifacts generated, task decomposition next)

## Overview

This project makes Spaarke a tool provider inside the user's normal Microsoft 365 Copilot Chat and Cowork flow, alongside Work IQ. One MCP server in the BFF (`/mcp/legal-operations`) exposes the user's **Decide lane** (the Work Items waiting on their decision) and the **Decision wizard** as four tools plus one MCP App widget. Every tool runs as the signed-in user over the same server-side services the Console uses. v1 proves the pattern end to end in the dev tenant; later capabilities (billing review, then Patents and Trademarks) become small slices that add tools.

## Quick Links

| Document | Description |
|----------|-------------|
| [Project Plan](./plan.md) | Phases, WBS, discovered resources, risk register |
| [Task Index](./tasks/TASK-INDEX.md) | Task breakdown and status (created by task-create) |
| [Spec](./spec.md) | AI implementation specification: FR-01–FR-19, NFR-01–NFR-09, MUST rules |
| [Design](./design.md) | Owner-approved design: decisions O-1–O-26, Phase 0 tests, risks |
| [Project CLAUDE.md](./CLAUDE.md) | Operating manual: binding rules, standing directives, coordination, gotchas |
| [Decisions](./notes/decisions.md) | Decision log |
| [Naming and ownership](./notes/coordination/2026-11-06-copilot-naming-and-ownership.md) | Naming conventions and cross-project agreements |

## Current Status

| Metric | Value |
|--------|-------|
| **Phase** | Planning (pipeline Step 3: task-create next) |
| **Progress** | 0% (see `tasks/TASK-INDEX.md` once created) |
| **Target Date** | — (owner sets) |
| **Completed Date** | — |
| **Owner** | Spaarke product owner (Global Admin in the dev tenant) |

## Problem Statement

Spaarke users already live in Microsoft 365 Copilot Chat and Cowork, where Work IQ gives them mail, calendar, files and people in one flow. Spaarke is not there: the April 2026 attempt (a declarative agent over a 28-function OpenAPI plugin) forced a context switch, could not carry a UI, and routed poorly. Users who must resolve Work Items have to leave Copilot and open the Console. Each new Spaarke capability would otherwise need its own Copilot integration, with its own auth and its own copy of the decision rules.

## Solution Summary

One stateless MCP server in the BFF, authenticated per ADR-028 A10 (OAuth at the Spaarke-tenant authority, OBO as the caller), exposes `spaarke_show_decisions` (model-visible) and three app-only tools (plan, lookup search, complete) plus an app-only paging call. The BFF decision slice is reworked so REST and MCP are two thin adapters over one transport-free decision capability (B-1 to B-10), with `DecisionCommitService` unchanged as the single owner of the completion invariant. A single-file widget (`ui://spaarke/decisions.html`) reuses the Console's `WorklistLanePanel` and `DecisionWizardHost` over MCP fetch shims. The project registers it in the dev tenant as a federated connector "Spaarke" plus a plugin package with the `spaarke-decisions` skill, and builds the Spaarke AI agent fallback only if Phase 0 test T1 requires it.

## Graduation Criteria

The project is considered **complete** when (spec Success Criteria):

- [ ] Phase 0 recorded: T5, T0 and T1 have dated results and decisions; E1–E5 and T13–T14 recorded — `notes/phase0-results.md` reviewed by the owner
- [ ] A licensed non-admin dev-tenant user lists their Decide lane and resolves a Work Item end to end **in Cowork** via `/spaarke-decisions`, and a Decision Record is written — record id captured; Console shows the item resolved
- [ ] The same in **Copilot Chat**, through the connector widget or (per T1) the Spaarke AI agent fallback — record id captured
- [ ] A direct prompt with no skill ("what decisions are waiting on me in Spaarke?") invokes `spaarke_show_decisions` — E3 plus a 20-prompt routing eval with its rate recorded
- [ ] Privileged detail never reaches model-visible output (FR-04) — negative integration test with a marker string passes
- [ ] REST contracts unchanged — contract tests on the signal, plan and decision routes pass before and after the extraction
- [ ] Console worklist and wizard unchanged — existing shared-lib tests pass and a Console smoke against dev is clean
- [ ] Auth negatives hold: anonymous, CIAM, A9 home-directory and no-`systemuser` callers are refused — `tests/integration/auth/**` green
- [ ] MCP protocol conformance (`--spec-version 2026-07-28`) and the `MapMcp` ArchTest guard are green in CI
- [ ] NFR-01 to NFR-04 measured within budget (p95 3 s / 1.5 s / 15 s; every result < 64 KiB at 200 items; widget < 3 MB; publish-size delta recorded per BFF task, no new HIGH CVE)
- [ ] Runbooks reproduce the connector, plugin and client registration from scratch — a second admin follows them in the dev tenant
- [ ] Docs and terminology updated (FR-18) — `doc-drift-audit` clean

## Scope

### In Scope

- MCP endpoint `/mcp/legal-operations`: stateless, A10 auth, `mcp` rate-limit policy, size limit, `Mcp:Enabled` kill switch (FR-01, FR-02)
- Tools `spaarke_show_decisions`, `spaarke_get_decision_plan`, `spaarke_search_decision_lookup`, `spaarke_complete_decision`, plus an app-only paging call (FR-03–FR-08)
- BFF rework B-1 (capability), B-2 (result mapper), B-3 (workforce lookup search + REST route), B-4 (`ICallerContext`), B-5 (slim projection), B-6 (placement), B-7 (tool policies), B-8 (route-guard coverage), B-10 (`because`)
- Widget `ui://spaarke/decisions.html` in `src/client/widgets/spaarke-decisions/`: Decide lane and Decision wizard with every parameter kind (FR-12, FR-13); text path when no widget renders (FR-14)
- Dev-tenant registration: A10 connector client, federated connector "Spaarke", plugin package with `spaarke-decisions`, runbooks (FR-16); Spaarke AI agent fallback only if T1 requires it (FR-17)
- Phase 0 platform tests T0–T14, E1–E5 (FR-19); docs, terminology and skill rewrites (FR-18)

### Out of Scope

- Do and Know lanes, Decided history, the full Console, `LegalWorkspaceApp`, matter workspace
- Inquiry outcome recording and the worklist narrative briefing
- A model-callable complete tool (O-14); organisational prompts (O-18)
- Connectors Gallery / Partner Center, customer stamps, B2B guests in practice, Model 2 SSO, Teams, mobile
- Recording the channel on the Decision Record; switching the Console lookup picker to the server route
- Removing the April 2026 surface (#2575, #1523) except the OpenAPI action if the fallback is adopted; the DEBUG token logger (#1574)
- The billing review slice (next project)

## Key Decisions

| Decision | Rationale | ADR |
|----------|-----------|-----|
| MCP server lives in the BFF, not a separate deployable | Tools are a second front door to BFF-only services (caller OBO, Redis ledger, `DecisionCallerRights`) | ADR-013 (Path C) |
| Connector auth is OAuth at the Spaarke-tenant authority (A10) | Production route for Model 1 guests from day one | ADR-028 A10 (Path B, done) |
| `DecisionCommitService` stays the only completion owner | One invariant owner; widget previews, server decides | ADR-002 (Path C) |
| Federated connector primary, plugin package also, agent only as T1 fallback | No context switch (O-3, O-23) | — |
| Route per product area `/mcp/{area}`; v1 `/mcp/legal-operations` | Connector URL is hard to change per customer (O-19, O-25) | — |

Full log: [notes/decisions.md](./notes/decisions.md); owner decisions O-1–O-26 in [design.md §0](./design.md).

## Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|------------|------------|
| No widget through the connector in Copilot Chat | High | Med | T1 decides; FR-17 agent fallback; text path (FR-14) |
| OAuth through a connector to the Spaarke-tenant authority fails (T5) | High | Low | T5 before any build; stop and amend ADR-028 if it fails |
| Dev deploy blocked until ontology's #2580/#2579 pair deploys | Med | High | Phase 0 runs on a master-based spike branch; Phase A–C build and test locally |
| Widget bundle exceeds init or size budget | Med | Med | Deep imports, bundle-size test, `decisionDraft.ts` re-render fallback |

Full register: [plan.md §8](./plan.md).

## Dependencies

| Dependency | Type | Status | Notes |
|------------|------|--------|-------|
| ADR-028 A10 | Internal | Ready | On this branch |
| Ontology #2580 / #2579 (D-207 billing refusal, `invoiceReview`, Invoice decisions card) | Internal | Merged into this branch 2026-11-06 | Deploy gate: no dev BFF deploy from this branch until the pair deploys |
| Dev BFF, dev tenant with Copilot licences and Cowork | External | Ready | Owner per-SHA go for any deploy |
| Owner as Global Admin for admin-center steps | External | Ready | Second admin may be designated |
| `ModelContextProtocol.AspNetCore` 2.3.1, `@modelcontextprotocol/ext-apps` 2.0.4, conformance suite | External | GA | Check ext-apps 1.x → 2.x API drift |
| MC1493223 (skills in Chat) | External | Rolling out | Cowork already supports `/` skills |

## Team

| Role | Name | Responsibilities |
|------|------|------------------|
| Owner | Spaarke product owner | Decisions, admin-center steps, deploy go |
| Developer | Claude Code sessions | Implementation via task-execute |
| Reviewer | ontology-platform-r1; unified-access-control-r2 | `Services/Signals/**` + `Api/Signals/**` + worklist route; auth diffs |

## Changelog

| Date | Version | Change | Author |
|------|---------|--------|--------|
| 2026-11-05 | 0.1 | Folder created (pre-design stub) | Claude Code |
| 2026-11-06 | 1.0 | Generated by project-setup from spec.md and the approved pipeline plan | Claude Code |

---

*Template version: 1.0 | Based on Spaarke development lifecycle*
