# Current Task State — spaarke-auth-v4-dataverse-MI

> **Last Updated**: 2026-08-19 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Everything needed to continue is in this file.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | `spaarke-auth-v4-dataverse-MI` — zero-secret BFF confidential credential (OBO → MI-FIC) |
| **Branch** | `work/spaarke-auth-v4-dataverse-MI` · worktree `c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI` |
| **Task** | **none — project initialized, execution NOT started (owner-gated)** |
| **Step** | Pipeline complete: spec → plan → 29 POMLs → lint PASS → registered on the board |
| **Status** | ready-to-start |
| **Next Action** | Run `task-execute` on **`tasks/001-create-dev-deployment-slot.poml`** |
| **Portfolio** | [Project #800](https://github.com/spaarke-dev/spaarke/issues/800) under Epic [#426](https://github.com/spaarke-dev/spaarke/issues/426) · Active / Planning · 0 of 29 |

### Repo state — all clean as of handoff

| Check | Value |
|---|---|
| Working tree | clean |
| Pushed through | `88784e7d4` (merge of origin/master) |
| Behind `origin/master` | **0** |
| Build | `dotnet build src/server/api/Sprk.Bff.Api/` → **0 errors**, 7 warnings (pre-existing obsolete-API) |
| POML lint | `pwsh scripts/Validate-TaskPoml.ps1 projects/spaarke-auth-v4-dataverse-MI/tasks` → **PASS**, 29 scanned, 0 errors, 6 explained warnings |

### Critical Context

The client secret survived three prior audits because of **one false sentence** in
`.claude/constraints/auth.md:108` ("OAuth spec requires confidential client + secret"). OAuth requires a
confidential *credential*. That premise is now corrected (ADR-028 **A4** + **E-3**, 2026-08-17) and the dev
**MI-FIC already exists** (`mi-bff-api-dev-assertion`, created 2026-08-19, verified still present at handoff).

**OBO fails CLOSED** — a bad change locks every user out immediately and totally. Staged slot rollout is
mandatory; there are still **0 slots** on `spaarke-bff-dev`, which is exactly what task 001 creates.

Read [`CLAUDE.md`](CLAUDE.md) before any task — it carries the non-negotiables, the credential-seam
architecture, and the two live CI gates.

---

## Full State

### What was completed this session (2026-08-19)

1. **Live Azure verification + Phase 0 unblocked** — created the dev MI-FIC, removing the external Azure-AD-admin
   dependency permanently. Resolved the `signInAudience` conflict (live = `AzureADMultipleOrgs`; the inventory
   said `AzureADMyOrg`). → [`notes/PHASE-0-LIVE-VERIFICATION.md`](notes/PHASE-0-LIVE-VERIFICATION.md)
2. **Cross-project coordination** — sent the provisioning change request; **they accepted and applied it**, and
   answered the app-registration question as a Model 1 / Model 2 split. Sent the unification interlock.
3. **`/design-to-spec`** — [`spec.md`](spec.md), 23 FRs across 6 workstreams.
4. **`/adr-check`** — 9 compliant, 4 warnings, 1 violation. All folded in.
5. **`/project-pipeline`** — [`plan.md`](plan.md), [`CLAUDE.md`](CLAUDE.md), **29 POMLs**,
   [`tasks/TASK-INDEX.md`](tasks/TASK-INDEX.md). Lint PASS.
6. **`/devops-project-register`** — Project #800 under Epic #426, 9 board fields populated.
7. **Master sync** — merged 66 commits from `origin/master`; clean, build green, and master touched **none** of
   our task-referenced files.

### Decisions

| Date | Decision | Rationale |
|---|---|---|
| 2026-08-19 | Rollout is **dev only** | `spaarke-bff-prod` is Stopped; prod/demo decommissioned |
| 2026-08-19 | Power BI → **UAMI-as-principal** | Microsoft's documented model; leaves the shared provider seam |
| 2026-08-19 | Group 2 non-Entra keys = **parallel workstream** | Independent of the OBO migration |
| 2026-08-19 | Provider seam = **injected named interface** | `Spaarke.Core` placement is circular and fails `LayerDependencyTests` FR-14 |
| 2026-08-19 | **FR-C4 (FIC automation) in scope** | Provisioning task 130 is soft-blocked on it — the one item outside dev-only |
| 2026-08-19 | Pipeline stops **before** task execution | OBO's fail-closed blast radius; no autonomous agent dispatch |
| 2026-08-19 | 6 lint warnings **left flagged, not silenced** | They fire on new *test* files; boilerplate justification would be hollow rationale |

### Two live CI gates that will bite

1. **`ADR010_DITests.cs:164`** — 1:1-interface ceiling is **153** (re-verified post-merge).
   `IClientAssertionProvider` makes 154. **Task 020 must raise it in the same PR** with the FR-14 justification.
2. **`LayerDependencyTests.cs:43`** — fails if `Spaarke.Dataverse` gains a `ProjectReference`. It must not.

### Cross-project obligations

| Direction | Item |
|---|---|
| **We owe** `customer-provisioning-orchestration-r1` | **Task 030** (`Register-EntraAppRegistrations.ps1` FIC extension) before their **Wave G-3**, else their task 130 builds a duplicate. It only depends on 020 — **pull it forward**, don't let phase order carry it |
| **They owe us** | Confirm Model 2's FIC issuer is not cross-tenant — [`notes/PROVISIONING-CHANGE-REQUEST.md`](notes/PROVISIONING-CHANGE-REQUEST.md) §9.2. Silent failure mode |
| **Interlock** `dataverse-access-unification-r1` | 4 shared files; `DataverseServiceClientImpl.cs` needs real sequencing (tasks 010, 011, 022) |

### Open questions carried into execution

1. **Power BI service-principal profiles under a managed identity** — unverified; **gates all of Phase 4**. Task 040.
2. **`Analysis:PromptFlowKey`** — still in use? Task 055.
3. **Model 2 FIC issuer tenancy** — with provisioning.

### Files modified this session

All committed and pushed. No uncommitted work.

- `projects/spaarke-auth-v4-dataverse-MI/` — spec.md, plan.md, CLAUDE.md, README.md, design.md, current-task.md,
  4 notes files, 29 task POMLs, TASK-INDEX.md
- `config/spaarke-resources.yaml` — corrected `signInAudience`; added the new FIC + password-credential inventory

### Recovery commands

```bash
cd c:/code_files/spaarke-wt-spaarke-auth-v4-dataverse-MI
git log --oneline -3                     # expect 88784e7d4 at or near HEAD
cat projects/spaarke-auth-v4-dataverse-MI/tasks/TASK-INDEX.md
# then: task-execute on tasks/001-create-dev-deployment-slot.poml
```

### Blockers

**None.** Task 001 is startable. Its only prerequisite (live Azure verification) is complete and re-verified at
handoff: both FICs present, 1 secret / 0 certs, UserAssigned identity, 0 slots.
