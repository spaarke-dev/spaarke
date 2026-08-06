# Pipeline Run Guidance — teams-app-r1 (for the next session)

> Written 2026-08-03 at project handoff. Read before running `/project-pipeline`.

## How to run

1. **Enter Plan Mode first** (Shift+Tab twice) — `/project-pipeline` requires it for Steps 0–3.
2. Run `/project-pipeline projects/teams-app-r1`.
3. **Scope = INITIALIZE-ONLY.** Generate plan + task files + `TASK-INDEX.md` + branch, then **PAUSE** — do **NOT** auto-execute code (Step 5). The owner will run task execution deliberately, wave by wave, in a later session. (Owner directive, 2026-08-03.)
4. **Structure for parallelism (owner directive).** Ensure `task-create` assigns `<parallel-group>` + `<parallel-safe>` on every task so waves can run with **parallel agents + autonomous where safe**. Keep `.claude/`-touching tasks sequential (main-session-only per root CLAUDE.md Sub-Agent Write Boundary). Group independent BFF endpoints, the PCF work, and the manifest/packaging as separate parallel-safe streams; serialize the auth-resolver → membership → enforcement chain.

## Confirmed decisions (do not re-litigate — see spec.md)

- **Auth**: workforce SSO (Option 2) for Teams; CIAM for SPA; shared standalone-MSAL, pluggable authority. **Extend `external-spa` in place** (host adapter).
- **AuthZ**: accessible-record-set — systemuser→ADR-034 membership (auto) ∪ contact→`sprk_externalrecordaccess` grants ∪ contact→standing-grant runtime membership. Non-systemuser workforce users supported (Option B) via workforce→contact + contact-anchored membership.
- **Access-Permission posture** = **Option A** (record-level sharing gate: Restricted=off / Limited=named-only / Standard=all incl. standing). Distinct from per-grant `sprk_accesslevel`.
- **Role allowlist** = **convention-based** (`sprk_assigned*` contact lookups via metadata discovery) + exclusion list; new fields auto-qualify. R1 `sprk_project` set: `sprk_assignedattorney1/2`, `sprk_assignedparalegal1/2`, `sprk_assignedtoexternal`, `sprk_assignedtointernal`.
- **Standing grants (FR-12) + email icon (FR-13)** = **R1**.
- **Grantor UX** = extend `TrackingFieldTrio` PCF (person + email icons + grant modal).
- Documents = broker-only SPE. AI = out of R1. Native Teams-channel bridge = out of R1.

## Governance / prerequisites

- **Hot-path**: BFF=Y, SpaarkeAi=N, ci-workflows=Y → pipeline registers the `projects/INDEX.md` row; run `/conflict-check` before every BFF PR (13+ active BFF worktrees).
- **ADR-028 A2 amendment** (`adr-028-amendment-draft.md`) must merge before/with the Teams-host auth code (Path B).
- **External prerequisites (admin-owned, NOT project tasks)**: `systemuser.sprk_primarycontact` linkage; go-live readiness verification of those links on the target org.
- **BFF publish ≤60 MB** (ADR-029); no M365 Agents SDK / Bot packages.
