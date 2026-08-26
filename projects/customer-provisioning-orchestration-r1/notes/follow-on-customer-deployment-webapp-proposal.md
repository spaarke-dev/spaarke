# Follow-On Project Proposal: Customer Deployment Web App

> **Filed 2026-08-22 during Model 1 Prod first-live stand-up session.**
> **Origin**: Owner-directed vision. Verbatim owner statement: *"Ultimately the best solution is we have a 'Customer Deployment' web app that allows the user to input whatever information/setting choices and Claude Code / the scripts run everything."*
> **Proposed slug**: `customer-deployment-webapp-r1`
> **Prerequisites**: r1 delivery must complete (all F1-F9 gaps in `provision-environment` skill absorbed into automation)

---

## Vision

Replace the operator-invoked `/provision-environment` skill with a self-service web UI. A Spaarke ops user (or eventually a prospect via marketing self-service) fills out a form with customer details + tenancy model + region + budget. The web app queues a provisioning job through L2 control-plane. All handlers H0-H14 execute end-to-end. SSE stream provides real-time progress to the user.

**Goal**: complete E2E customer provisioning with **zero human interaction beyond the initial form fill + explicit "start" click**.

---

## Why now (context)

The 2026-08-22 session that provisioned Model 1 Prod surfaced the exact automation gaps that this web app depends on being closed. Owner's directive: *"the expectation for the final delivered solutions is that this process will run E2E with no human interaction."* That's not achievable today — 9 fresh-sub gotchas (F1-F9 in the lessons-learned doc) still require manual intervention.

The web app is the NATURAL evolution once those gaps close:
- The skill layer already has the "operator UX" role
- Absorbing F1-F9 makes that UX fully automated
- Web app is just replacing the CLI operator UX with a browser UX; the underlying automation is the same

## Deliverables

### Core deliverables

1. **React/PCF UI** — form-based customer intake
   - Customer name (validates format: `[a-z0-9]{3,10}`)
   - Tenancy model (Model1Shared vs Model2Dedicated dropdown; explains cost/isolation tradeoff)
   - Primary contact (email, phone)
   - Region preference (dropdown with sane defaults: WestUS2 platform + WestUS3 OpenAI)
   - Budget/tier (SMB / Standard / Enterprise)
   - Compliance requirements (GlobalStandard vs DataZoneStandard for AI)
   - Estimated cost preview (compute + reserve)
   - "Start Provisioning" button

2. **Backend endpoint** — new BFF surface `/api/customer-provisioning/start`
   - Validates intake
   - Auth: OBO from web app user (must have `Operator` role on L2 REST API)
   - Enqueues job via existing L2 `POST /api/runs` (no new state machine needed)
   - Returns `{ runId, statusUrl, sseUrl }`

3. **Progress UI** — real-time SSE stream
   - New BFF surface `/api/customer-provisioning/runs/{runId}/stream` (server-sent events)
   - UI shows handler-by-handler progress (H0 preflight → H1..H14 provisioning steps)
   - Manual gate handling: if a handler hits `WaitingOnGate`, UI shows the gate + resolution steps + "Refresh" button
   - Completion: renders env URL, sign-in link, first-user creation prompt

4. **Completion UI** — success + next steps
   - Env URL (link)
   - Sign-in flow (opens in new tab)
   - First-user creation form (email + role)
   - Handoff report download (the `runs/{runId}.md` artifact)
   - "Onboard another customer" button

### Advanced deliverables (optional)

5. **Cost dashboard** — live per-customer spend tracking
6. **Bulk onboarding** — CSV upload for multiple customers
7. **Audit log** — who provisioned what, when, from where
8. **Prospect self-service** — public landing page → trial provisioning without ops touch

## Prerequisites (MUST land in r1 first)

| Gap | Owner | Status |
|---|---|---|
| F1-F9 absorbed into `provision-environment` Step 2.5 automation | r1 delivery | MVP informational only; automation TODO |
| L2 control-plane `/api/runs` API surfaced via BFF (currently direct) | r1 delivery | Direct today; BFF proxy TODO |
| SSE progress stream on `/api/runs/{id}` | r1 delivery | Poll-based today; SSE TODO |
| All handlers H0-H14 live-validated | r1 delivery | H1/H4/H5/H6/H7/H8/H10/H11/H12b live; others pending |
| Auto-quota detection and deployment-set composition | r1 delivery | Manual today; automation TODO (F5 gap) |
| Region auto-selection | r1 delivery | Manual today (this session pivoted eastus → westus2); automation TODO (F3+F4 gaps) |
| Sub-level Support Plan requirement + fallback | r1 delivery | Not implemented (F9 gap) |

## Design tensions

- **Where does the web app live?**
  - Spaarke tenant only (ops-only interface)? — Simple, low risk
  - Per Model 1 shared env (self-service tenant)? — More complex; auth model gets tricky
  - Recommendation: Spaarke tenant only for MVP; consider per-Model-1 later

- **Who can operate it?**
  - Spaarke ops only for MVP (Ops team members with `Operator` role on L2)
  - Later: prospect self-service via marketing landing page (with heavier vetting + rate limiting)

- **What's the failure story?**
  - When Microsoft auto-approver denies quota: does the web app queue a support ticket automatically (F8 gap resolved) or block for ops?
  - When a handler `WaitingOnGate` sits > 24h: notify ops via email? Cancel run automatically?

- **Handler observability**:
  - Real-time SSE feed of handler status is essential
  - Handler-level metrics: p50/p95 latency per handler for capacity planning

- **Cost transparency**:
  - Show pre-provisioning cost estimate
  - Show post-provisioning actual spend (billing API integration)

## Effort estimate (rough)

- **MVP scope** (ops-only, no advanced features): 4-6 weeks
  - Assuming r1 delivery has closed all prerequisite gaps
  - React UI: 1-2 weeks
  - BFF endpoints: 1 week
  - SSE stream: 1 week
  - End-to-end testing + auth wiring: 1-2 weeks

- **Full scope** (prospect self-service + billing integration): 3-4 months

## Kickoff conditions

Do NOT start `customer-deployment-webapp-r1` until:

1. r1 delivery is at "Ready for prod" status
2. `/provision-environment` skill's Step 2.5 is fully automated (F1-F9 absorbed)
3. Ops has successfully onboarded at least 3 Model 1 customers using the CLI-driven skill (validates the underlying automation is production-worthy)
4. Owner sign-off on the scope decisions above (per Model 1 vs cross-env location, who-can-operate, failure story)

---

## Meta

**Filing route**: This is currently a NOTE, not a project. When ready to promote to a full project:

1. `mkdir projects/customer-deployment-webapp-r1`
2. Copy this doc → `spec.md` (initial requirements)
3. Run `/design-to-spec` → `/project-pipeline`
4. Task decomposition → task-execute

**Related current-project artifacts**:
- `projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md` — the "why now" evidence
- `.claude/skills/provision-environment/SKILL.md` — the CLI skill that will become the webapp's automation core
- `infrastructure/bicep/stacks/model1-shared.bicep` — the Bicep the automation invokes

---

*Filed 2026-08-22 by Claude (Opus 4.7) with Ralph Schroeder as owner. Do not lose this file — it captures the owner's stated ultimate vision for the customer provisioning experience.*
