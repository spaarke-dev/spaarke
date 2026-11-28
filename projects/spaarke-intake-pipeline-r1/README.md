# Spaarke Intake Pipeline R1 (`spaarke-intake-pipeline-r1`)

> **Portfolio**: [Project #3128](https://github.com/spaarke-dev/spaarke/issues/3128) · Epic: [#421](https://github.com/spaarke-dev/spaarke/issues/421) · [Portfolio board view](https://github.com/users/spaarke-dev/projects/2)
> **Status:** Planned (2026-11-25). Spec and tasks are written; execution starts with Phase 0. Live state: [current-task.md](current-task.md), [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md).
> **Branch:** `work/spaarke-intake-pipeline-r1` · **Worktree:** `C:/code_files/spaarke-wt-spaarke-intake-pipeline-r1`
> **Origin:** split out of `specialist-legal-agents-r1` (owner, 2026-11-25).

## Quick Links

| File | Purpose |
|---|---|
| [spec.md](spec.md) | AI implementation spec: 22 FRs, 10 NFRs, ADR tensions, owner clarifications (the source for tasks) |
| [plan.md](plan.md) | Implementation plan: phases, critical path, discovered resources, risks |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | Task list, dependencies, parallel waves |
| [CLAUDE.md](CLAUDE.md) | Project operating manual: binding rules, owner decisions, coordination, gotchas |
| [design.md](design.md) | Design v0.3: architecture and the decided §13 questions (unchanged source) |
| [notes/reuse-scan.md](notes/reuse-scan.md) | Reuse scan: 14 capabilities and the existing component each extends |
| [notes/section10-verification-2026-11-25.md](notes/section10-verification-2026-11-25.md) | Evidence for the 8 §10 cleanup findings |
| [notes/code-review-evidence-2026-11-24.md](notes/code-review-evidence-2026-11-24.md) | Three Fable code reviews of master `b005279c4d` (intake paths, invoice pipeline, routing, run records, outputs, return) |

## Overview

This project provides one process model for **requests**: someone sends content to Legal (an NDA for review, an invoice, a Front Door request) and expects an outcome back.

## Problem Statement

Requests reach Legal through several unconnected paths today: inbound email, the External Access Front Door, Console uploads and the invoice routes. Each path either only files content (profile and index) or runs its own one-off pipeline. There is no common record of a request's progress, no common place to plug in a new kind of work, no attorney approval step before anything goes back out, and the reply path does not thread back to the sender. The design review (2026-11-24/25) also found eight concrete defects in these paths, listed in design §10.

## Proposed Solution

- **Doors:**
  - Front Door
  - Console submit
  - intake mailboxes, plus a suggest-only "Submit as request" proposal at other mailboxes
  - Copilot (MCP tools)
  - later, the invoice routes
- **Records:** each door creates a request (`sprk_servicerequest`). One BFF orchestrator (`IJobHandler`) tracks each pass through the process as a **run** (`sprk_processingjob`).
- **Routing:** a `requestType` routing key selects a **harness**. Content with no matching harness, or a low-confidence classification, goes to manual triage.
- **NDA harness (first):**
  1. An app-only AI Action writes an edit plan.
  2. The accelerator's Python redline engines, in a Container Apps Job, produce a redline, an external copy, a clean copy and a clause table.
  3. Every existing redline and comment is preserved.
- **Approval:** an attorney claims the team Work Item and approves or rejects it, from the Console or the model-driven form. Both decisions go into the Decision Record.
- **Return:** the result goes back through the door the request came in by. Email returns are a threaded reply from the shared legal mailbox. The requester can opt in to a status email at each change.
- **Invoices** become the second harness after eBilling UAT.

Content filing (profile and index) stays a separate, simpler level.

## Scope

**In scope (r1 = Phases 0–3):**
- Phase 0:
  - engine-hosting spike;
  - app-only edit plan;
  - one cleanup PR (design §10.1);
  - ADR-004 Amendment A2.
- Phase 1:
  - request and run records;
  - orchestrator;
  - routing key and harness registry;
  - NDA harness;
  - manual triage;
  - approval;
  - status and status emails;
  - Front Door and Console doors;
  - run admin with DLQ re-drive;
  - status API.
- Phase 2:
  - intake mailbox;
  - Job B "Submit as request" proposal;
  - the #1920 classification producer;
  - threaded email return;
  - MCP submit and status tools.
- Phase 3: invoice harness, after eBilling UAT sign-off.

**Out of scope:**
- Phase 4 harnesses (invention, trademark, policy) → r2.
- Forcing filing-only paths onto runs.
- Compose as the NDA front end.
- New review AI or attorney profiles (specialist-legal-agents).
- Auto-send, or sending as the attorney.
- Email joining the ingestor seam (#3162).
- Editing or ratifying the NDA standard (accelerator).
- Durable Task.

## Graduation Criteria

- [ ] **Phase 0:**
  - the engine container's output matches the accelerator golden fixtures byte for byte;
  - three synthetic NDAs give schema-valid edit plans;
  - the cleanup PR is merged with its regression tests;
  - ADR-004 A2 is merged.
- [ ] **Phase 1:** on dev, a Front Door NDA request returns marked up after attorney approval, and both approval and rejection are written to the Decision Record. When the requester opted in, a status email goes out at each change.
- [ ] **Phase 2:** the same flow works through an intake mailbox (as a threaded reply), through an accepted Job B suggestion, and through the MCP submit tool. No request is ever created at an ordinary mailbox without confirmation.
- [ ] **Phase 3:** invoices run as a harness, and the eBilling test suite and E2E are unchanged.
- [ ] **Quality:**
  - all new handlers and endpoints have unit tests, including negative and authorization cases;
  - the BFF publish size stays ≤ 60 MB with no new HIGH CVE;
  - the engine image is CVE-clean;
  - `/adr-check` and `/code-review` pass on each PR.
