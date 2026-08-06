# Task Index — Spaarke External Access Platform (R2)

> **Status**: INITIALIZED (tasks generated; execution owner-gated wave-by-wave)
> **Generated**: 2026-08-06 via `/project-pipeline` → `task-create`
> **Total tasks**: 34 (P0: 4 · P1: 10 · P2: 8 · P3: 7 · P4: 4 · wrap-up: 1)

Legend: 🔲 not-started · 🔄 in-progress · ✅ completed · ⛔ blocked · Tier: S=sonnet O=opus · Eff: h=high x=xhigh m=medium

| ID | Title | Phase | Status | Deps | Tier/Eff | Rigor | Parallel |
|----|-------|-------|--------|------|----------|-------|----------|
| 001 | Scaffold module-host prototype (shell + launcher + realm chooser) | P0 | 🔲 | none | S/h | STANDARD | — (base) |
| 002 | Extend prototype seed (servicerequest + entitlement factories + 3-persona preset) | P0 | 🔲 | 001 | S/m | STANDARD | — |
| 003 | Prototype Legal Front Door screens (intake/my-requests/NDA/upload) | P0 | 🔲 | 002 | S/h | STANDARD | — |
| 004 | Owner visual-approval gate + component map | P0 | 🔲 | 003 | S/m | MINIMAL | — (gate) |
| 010 | Author ADR-028 Amendment A3 | P1 | 🔲 | none | O/h | FULL | — (.claude/ main-session) |
| 011 | Module-host shell scaffold (extract from external-spa) | P1 | 🔲 | 004,010 | S/h | FULL | — (base) |
| 012 | Module registry + card launcher + /me-driven visibility | P1 | 🔲 | 011 | S/h | FULL | Group B |
| 013 | Dual-plane auth bootstrap (CIAM + realm discovery) | P1 | 🔲 | 011 | O/h | FULL | — (auth-sensitive) |
| 014 | Teams app packaging (manifest + CSP + theme) | P1 | 🔲 | 011 | S/h | STANDARD | Group B |
| 015 | FR-22 module-framework generalization (CallerPrincipalResolver) | P1 | 🔲 | 010 | O/x | FULL | — (core BFF surface) |
| 016 | Outside Counsel as first registered module | P1 | 🔲 | 012,015 | S/h | FULL | — |
| 017 | Cleanup dead Power Pages proxy/config | P1 | 🔲 | 011 | S/h | STANDARD | Group B |
| 018 | Cleanup inert filter + /api/v1/collab group | P1 | 🔲 | 015,016 | O/x | FULL | — (BFF deletion) |
| 019 | Deploy P1 (shell + Teams, SWA) | P1 | 🔲 | 012,013,014,016,017 | S/h | STANDARD | — (deploy) |
| 020 | Module-entitlement Dataverse schema | P2 | 🔲 | 015 | S/h | FULL | — (schema; blocks 021+) |
| 021 | Module-entitlement resolver (App-Role + Contact strategies) | P2 | 🔲 | 020 | O/h | FULL | — (auth core) |
| 022 | GET /me entitlement endpoint (Redis-cached) | P2 | 🔲 | 021 | O/h | FULL | Group C |
| 023 | Lazy Contact attribution (oid resolve-or-create) | P2 | 🔲 | 021 | S/h | FULL | Group C |
| 024 | Workforce-plane external-app auth policy | P2 | 🔲 | 015 | O/h | FULL | — (auth policy) |
| 025 | D1 workforce role→level grading | P2 | 🔲 | 024 | S/x | FULL | — |
| 026 | Core-user admin UI (grant/revoke; reuse AccessGrantModal) | P2 | 🔲 | 021 | S/h | FULL | Group C |
| 027 | Deploy P2 (BFF + entitlement schema) | P2 | 🔲 | 022,023,024,025,026 | S/h | STANDARD | — (deploy) |
| 030 | Extend sprk_servicerequest intake schema | P3 | 🔲 | 020 | S/h | FULL | — (schema; blocks 031+) |
| 031 | Generic typed-intake framework | P3 | 🔲 | 022,030 | O/h | FULL | — (framework base) |
| 032 | NDA module (review/approval → ready-for-signature) | P3 | 🔲 | 031 | S/h | FULL | Group D |
| 033 | Policy & Procedures module | P3 | 🔲 | 031 | S/h | STANDARD | Group D |
| 034 | Submitter authz (Tier-2) + app-only SPE upload | P3 | 🔲 | 030,031 | O/x | FULL | — (auth + SPE) |
| 035 | My-requests list + status UI | P3 | 🔲 | 031,034 | S/h | FULL | — |
| 036 | Deploy P3 (SWA + BFF + schema) | P3 | 🔲 | 032,033,034,035 | S/h | STANDARD | — (deploy) |
| 040 | Provisioner self-healing (CIAM 409) | P4 | 🔲 | none | O/x | FULL | — (brownfield) |
| 041 | Live-E2E external auth (wrong-issuer→401; no email-hijack) | P4 | 🔲 | 019,027 | S/h | STANDARD | — (live) |
| 042 | SSPR first-run verification + doc | P4 | 🔲 | 019 | S/m | STANDARD | — |
| 043 | Legal handoff — MDA routing/assignment | P4 | 🔲 | 036 | S/h | STANDARD | — |
| 090 | Project wrap-up (gates + test-diet + close-out) | Wrap-up | 🔲 | all | S/h | FULL | — (serial) |

## Parallel Execution Groups

Tasks in the same group can run simultaneously once prerequisites are met. **MAX 6 agents/wave.**
Tasks marked main-session-only (`.claude/`) or on the shared external-access BFF surface are
`parallel-safe:false` and run sequentially. **Run `/conflict-check` before EVERY BFF PR** (external-access
surface shared with `teams-app-r1`).

| Group | Tasks | Prerequisite | Files Touched | Safe |
|-------|-------|--------------|---------------|------|
| B | 012, 014, 017 | 011 ✅ | registry/launcher (client components) · Teams manifest+staticwebapp.config · vite.config/powerpages — distinct | ✅ Yes |
| C | 022, 023, 026 | 021 ✅ | /me endpoint · lazy-contact service · admin UI (distinct files) | ✅ Yes |
| D | 032, 033 | 031 ✅ | NDA module · P&P module (separate module surfaces) | ✅ Yes |

Sequential / not parallel-safe: 010 (.claude/ main-session), 013 (auth bootstrap in main.tsx), 015
(core BFF surface), 018 (BFF deletion), 020/030 (schema, block downstream), 021/024/025/031/034/035
(shared BFF/auth or dependent), all deploy tasks, 040 (brownfield provisioner), 090 (serial close-out).

## Parallel Execution Plan (waves)

```
P0 (sequential — prototype builds up): 001 → 002 → 003 → 004 (owner visual-approval GATE)
   goal-eligible: NO (exploratory UX; no machine-verifiable end-state; owner gate)

P1:
  Wave 1 (sequential): 010 (ADR-028 A3, main-session)  ||  015 dispatched after 010 (core BFF surface, solo)
  Wave 2 (sequential): 011 (shell scaffold — needs 004 + 010)
  Wave 3 (parallel, 3 agents): 012, 014, 017 — prereq 011 — goal-eligible: NO (frontend visual + deploy-adjacent)
  Wave 4 (sequential): 013 (auth bootstrap), 016 (OC module — needs 012+015)
  Wave 5 (sequential): 018 (BFF cleanup deletion) → 019 (deploy P1)
     goal-eligible: NO (auth/security + deploy + irreversible deletion)

P2:
  Wave 1 (sequential): 020 (schema) ; 024 (auth policy — needs 015, can run alongside 020/021 region)
  Wave 2 (sequential): 021 (resolver — needs 020)
  Wave 3 (parallel, 3 agents): 022, 023, 026 — prereq 021 — goal-eligible: NO (auth entitlement)
  Wave 4 (sequential): 025 (role→level — needs 024) → 027 (deploy P2)

P3:
  Wave 1 (sequential): 030 (schema — needs 020)
  Wave 2 (sequential): 031 (intake framework — needs 022+030)
  Wave 3 (parallel, 2 agents): 032, 033 — prereq 031 — goal-eligible: NO (frontend + workflow)
  Wave 4 (sequential): 034 (submitter authz + SPE) → 035 (my-requests UI) → 036 (deploy P3)

P4 (sequential): 040 (provisioner) ; 041 (live-E2E — needs 019+027) ; 042 (SSPR — needs 019) ; 043 (legal handoff — needs 036)
   goal-eligible: NO (security/auth + live env + irreversible)

Wrap-up: 090 (serial) — goal-eligible: NO
```

**Goal-eligibility summary**: NO waves are `/goal`-eligible. Rationale: every wave is either
security/auth-touching, deployment/irreversible, frontend-visual (no machine-verifiable end-state),
or an owner-gated UX prototype. Execute owner-gated wave-by-wave with per-task `task-execute` + Step 9.5
gates.

## Critical Path

```
004 (prototype approval) → 011 → 012 → 016 → 019 → 041
010 (ADR-028 A3) → 015 → 020 → 021 → 031 → 034 → 036 → 043 → 090
```

Longest chain runs through the FR-22 generalization → entitlement schema → intake framework →
submitter authz → Front Door deploy → legal handoff → wrap-up.

## High-Risk Items

- **015 / 018** — core external-access BFF surface shared with `teams-app-r1`; `/conflict-check` mandatory; 018 deletion must confirm zero `/api/v1/collab` callers (escalation).
- **010** — ADR-028 A3 must NOT overwrite the existing A2; read A2 first; may pivot to Path C if A2 already covers R2.
- **020 / 030** — Dataverse schema shape + intake request-type/status need owner sign-off (escalation).
- **034 / 040** — auth + SPE broker-only (no OBO); brownfield 409 race — highest-reasoning tasks (xhigh).
- **teams-app-r1 operator-gated BFF redeploy + live Teams E2E** — P1 prerequisite (spec Dependencies).

## How to Execute

1. Confirm prerequisites (✅ in Status) + run `dotnet build src/server/api/Sprk.Bff.Api/` before any BFF wave.
2. `/conflict-check` before any BFF PR.
3. Invoke `task-execute` per task (parallel groups: one message, multiple Skill calls, ≤6 agents).
4. Build-verify between waves; checkpoint after each group.
5. `.claude/`-touching task (010) is main-session-only — never dispatch to a sub-agent.
