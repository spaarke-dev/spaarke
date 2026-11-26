# Specialist Legal Agents R1 (umbrella), Phase 0 — project operating manual

> Read with `current-task.md` (current state). Repo-wide rules are in root `CLAUDE.md`; this file holds only what is specific to this project. Guidance: `.claude/skills/project-setup/references/claudemd-template.md`.

## 1. Scope and status

This folder is the umbrella program for attorney-owned specialist legal agents (design.md). **Phase 0 runs here** (OD-3, amended 2026-11-24). Phase 0 produces evidence and a go/no-go record that decide the runtime shape:
- G1, where the profile is applied (D-A7);
- G5, delivery shape (D-A12);
- G6, compose or recommend (D-A13);
- G3, profile resolution;
- the G2 remainder;
- a Cowork patent-drafting spike;
- desk items I-1 to I-4.

**Out of scope:**
- product code on master;
- the Phase 1 tables, compiler and Code Page;
- G4 Outlook, G10 (Copilot Studio) and G11 (SPFx). Owner: "do not invest in Copilot Studio or SPFx".

Phases 1–4 are child projects. Phase 1 is `specialist-legal-agents-patents-r1`.

Status: see `tasks/TASK-INDEX.md` and `current-task.md`. Spec: `spec.md`. Design: `design.md`. Plan: `plan.md`.

## 2. Binding rules for this project

- MUST NOT merge the spike branch, or add `spike_*` tools to master's catalogue (FR-02). A PR for it, if opened, is titled "[DO NOT MERGE]".
- MUST resolve the caller with `ICallerSystemUserResolver` and fail closed: `NO_PROFILE` / `CALLER_UNRESOLVED`. Never fall back to another user's profile or to a default profile (FR-02).
- MUST keep spike tools read-only, model-visible, on `/mcp/legal-operations` behind the existing auth chain. No second MCP route, server or OAuth registration (FR-02, ADR-028 A10).
- MUST declare profile positions in the Action's input schema; undeclared inputs are dropped (ADR-043). MUST apply the profile's model tier explicitly (#1934) (FR-02).
- MUST log request header **names** only. `Authorization`, cookies and tokens are never logged or committed (FR-03, NFR-05).
- MUST send an R4 inventory row to COPILOT-PLATFORM-COORDINATION (Issue #3006) before **any** tenant upload. Install for the owner and the test user only (R7) (FR-01, NFR-01).
- MUST NOT put `answer-key/` or the real NDAs (Corteva, RPX/Helios) anywhere an agent can read. Reuse the accelerator's `FORBIDDEN_IN_PACKAGE` list. All content is synthetic or public (NFR-02).
- Patent test content is labelled `status: test`, drawn from **published** patents, with the publication number recorded (FR-09).
- Thresholds: G1 ≥95% adherence on both surfaces, 0 ratchet escapes, 0 bleed; G5 ≥95% routing, 0 functions on controls. Pilots of 10; a variant below 90% stops (FR-04, FR-06).
- Deferred work and newly found issues: `/project-defer-issue-tracking` writes `notes/defer-issues.md` AND a GitHub issue. Never one without the other.

ADR tensions approved for this project (root CLAUDE.md §6.5), all Path C (spec "ADR Tensions"):
- ADR-039 (closed catalogues): spike tools are cataloged on the existing route; a profile only narrows them (design T-4).
- ADR-028 A10: test agents are coded declarative agents on the same dedicated client and registration (design T-7(a)).
- ADR-040: the ledger is not the durable record. The run store is now decided: `sprk_playbookrun` through `PlaybookRunRecordStore`, from Phase 1 (design T-3).

## 3. Owner directives and standing decisions

- 2026-11-25: Phase 0 spike tools on a never-merged branch; staged, small test effort; G1 on NDA first, then patents.
- 2026-11-25: The run path is the action-based model (`IActionRunner` + `PublicContracts` facade; `ICodedWorkflow` for multi-step). The node-graph engine is frozen; playbooks as a concept are live (`docs/architecture/playbook-architecture.md`, ADR-039 amendment PR #3213).
- 2026-11-25: `sprk_playbookrun` ("AI Run") is the one AI-run record. One shared writer, `PlaybookRunRecordStore` (mcp-r2 task 005); Phase 1 adds its profile-version and gate columns to that store (`notes/playbook-concept-and-run-record-2026-11-25.md`).
- 2026-11-25 OD-2: the patent attorney is the first archetype. NDA stays a function; the intake harness is `spaarke-intake-pipeline-r1` (#3128).
- 2026-11-25: Spaarke AI is removed (mcp-r2 §4.5). Tenant steps E1/E2 are the owner's.
- Full decision log: design.md §12 and `notes/decisions.md`.

## 4. Coordination

- **COPILOT-PLATFORM-COORDINATION** (Issue #3006): R4 rows before uploads; inventory closure (FR-14).
- **spaarkeai-mcp-bff-api-r2** (session `local_d05d2f6b-4983-425e-8572-fa4d92b27338`):
  - owns `Api/Mcp/**`, McpModule, the catalogue and `PlaybookRunRecordStore`;
  - the spike branch is cut from master after its merged work;
  - its CAPABILITY-INDEX rows 34+ collide with #3213 and are folded on merge.
- **spaarke-legal-ai-accelerator-r1**: NDA positions, evals and the T-P2 prompt set (read only; never edit its worktree). G6 results go to it.
- **unified-access-control-r2**: owns the dev BFF deploy slot. Spike deploys go through its session, with the owner's go per SHA.
- **spaarke-intake-pipeline-r1** (#3128): the NDA submit-and-return harness. Not Phase 0 work.

## 5. Environment and live actions

- **Owner only:** tenant and admin-center clicks (uploads, installs, Legal for Cowork switch-on, removals).
- **Owner's go first:** any DEV Dataverse write (for example `agreement-review-spike`); every dev BFF deploy (per SHA, via uac-r2).
- **Never:** change the production OAuth registration A1; edit the production `agreement-review` row.
- **Test agents:** a fresh app id for each action change, or wait about 45 minutes after an update (BR-2/BR-3). Dynamic discovery by default (BR-5).
- **Monitoring:** App Insights queries use the PowerShell form in `phase0/g8/README.md`. BFF requests are sampled at about 25%, and the BFF logs no caller identity. Attribute runs by tester and time, and use several turns per check.

## 6. Gotchas — do not re-learn

- 2026-11-24: An Agent Builder agent cannot use the tenant federated connector. Agents reach MCP only through their own action. Fix: test agents are coded declarative agents with their own MCP action (`phase0/g8/README.md`).
- 2026-11-24: `az` rejects KQL with `name=` aliases. Fix: use the PowerShell query form in `phase0/g8/README.md`.
- 2026-11-25: "The playbook engine is frozen" does not mean "playbooks are unused" (FAILURE-MODES G-26). Fix: read `docs/architecture/playbook-architecture.md` first.
- 2026-11-25: `nda-review` was generalised and renamed to `agreement-review` (`infra/dataverse/actions/agreement-review.action.json`). Fix: use `agreement-review`.
- 2026-11-25: The linear Action path ignores Binding tier overrides (#1934). Fix: set the model tier on the Action explicitly.

## 7. Key documents

- `spec.md` · `design.md` · `plan.md` · `notes/decisions.md` · `notes/reuse-scan.md` · `notes/run-path-review-2026-11-25.md` · `notes/playbook-concept-and-run-record-2026-11-25.md` · `notes/agent-package-identity-and-distribution.md` · `phase0/g8/README.md`
- Applicable ADRs: `.claude/adr/ADR-028-spaarke-auth-architecture.md` (A10) · `.claude/adr/ADR-039-grounded-execution-closed-catalogs.md` · `.claude/adr/ADR-040-session-ledger.md` · `.claude/adr/ADR-013-ai-architecture.md` · `.claude/adr/ADR-043-ai-capability-execution-spine.md` · `.claude/adr/ADR-008-endpoint-filters.md` · ADR-038 (`docs/adr/ADR-038-testing-strategy.md`) · `.claude/adr/ADR-052-workload-placement.md`
- Related projects: `spaarkeai-mcp-bff-api-r2`, `spaarke-legal-ai-accelerator-r1`, `spaarke-intake-pipeline-r1`, `unified-access-control-r2`
