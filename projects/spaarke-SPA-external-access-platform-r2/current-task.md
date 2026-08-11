# Current Task State — spaarke-SPA-external-access-platform-r2

> **Last Updated**: 2026-08-10 (task 070 — code complete, deploy-gated)
> **Recovery**: read Quick Recovery. Task 070 BFF code+tests+quality-gates DONE; the Azure deploy +
> live matter/WA grant smoke test (escalation gate) is the one remaining, owner-gated step.

## Quick Recovery

| Field | Value |
|-------|-------|
| **Status** | Task **070 CODE-COMPLETE** — polymorphic external grant-WRITE (Project/Matter/WorkAssignment) + close-project cascade-revoke bug fix. Build clean; full BFF suite **10293 pass / 0 fail** (+34 new); publish **48.44 MB** (Δ0, ≤60); no CVE; code-review + adr-check **PASS**. NOT yet deployed. |
| **NEXT ACTION** | **Owner: gate the BFF deploy** — `scripts/Deploy-BffApi.ps1` → spaarke-bff-dev (from worktree, NOT CI), then the **escalation-gate live smoke test**: grant a Matter AND a Work Assignment to an external contact via `/api/v1/external-access/grant` `{recordType,recordId}` and confirm `_sprk_matter_value`/`_sprk_workassignment_value` populate (read_query). If a matter/WA grant write fails live → STOP + escalate (nav-property mismatch). Then mark 070 ✅ and proceed to 071. Say "deploy task 070" to proceed. |
| **Task** | 070 (code done; deploy + live-write verification pending) |
| **Branch/sync** | `work/spaarke-SPA-external-access-platform-r2` — 070 code committed locally (see git log). 40 behind / (10+) ahead of master; master integration still deferred. |
| **Escalation** | ARMED for Step 8 live-write: matter/WA nav-property names (`sprk_matterid`/`sprk_workassignmentid`) verified by convention (2 on-table data points) + owner + schema describe, but the definitive faithful check is the live grant write. |

## ✅ Task 070 — what shipped (code)
- **New** `ExternalGrantRoot.cs` (enum + `BindFor`/`TryParse`; no interface, ADR-010).
- `GrantAccessRequest`/`InviteExternalUserRequest` gained optional `{RecordType, RecordId}`; legacy `ProjectId` = back-compat shorthand.
- `GrantExternalAccessEndpoint`: `ResolveGrantRoot` (fail-closed) + polymorphic `BuildGrantPayload` (binds exactly one typed lookup; project grant byte-identical).
- `/invite-and-grant` threads the root (400 before onboarding side effects); `/revoke` + `/invite` no longer require `ProjectId`.
- `ProjectClosureEndpoint`: bug fix `_sprk_projectid_value` → `_sprk_project_value` (+ extracted `BuildActiveProjectGrantsFilter` for regression test).
- Tests: `PolymorphicGrantWriteTests.cs` (34) — per-root payload contract, fail-closed rejects, legacy ProjectId, root parse/bind, close-project filter regression. 3 existing tests updated for relaxed contracts.
- Docs: `notes/task-070-deviations.md` (incl. 2 discovered pre-existing latent bugs → recommend /defer: `sprk_expiresdate`, `sprk_accountid`).

## Remaining after 070 deploy
- **P2b wave**: 071 (PCF/modal polymorphism + side-pane Advanced Lookup), 072 (Tier-1 Option-B entitlement + widgetRegistry tab sets), 073 (wave deploy + both-plane UAT).
- **028 live both-plane UAT** (deployed dev, owner-pending).
- **Master integration** deferred (40 behind).

## Notes index
`notes/`: `task-070-deviations.md` (this task), `polymorphic-grant-authoring-enhancement.md` (P2b binding design), `task-028-deviations.md`, `external-access-polymorphic-scoping-design.md`, `module-entitlement-schema-decision.md`, `grid-widget-empty-diagnosis.md`.
