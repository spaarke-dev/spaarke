# BFF Remediation — Phase 2 Candidate Categorization

> **Project**: `sdap-bff-api-remediation-fix`
> **Compiled**: 2026-05-24
> **By**: Phase 2 tasks 020 (SAFE), 021 (MEDIUM), 022 (HIGH + REJECT + this file)
> **Inputs**: `inventory/INVENTORY.md` + 6 critical findings + Phase 0 decisions
> **Gate**: Owner sign-off (operator-only model per NFR-08 revised); without sign-off Phase 3 cannot start
> **Status**: Awaiting operator gate sign-off via commit

---

## Executive summary

After Phase 1 inventory + 6 critical findings + Phase 0 owner decisions:

| Tier | Count | Project impact |
|---|---|---|
| **SAFE** | 3 | All of Outcome A's size goal achievable; FR-A3 already-resolved no-op |
| **MEDIUM** | 1 | Partial Outcome B (System.Security.Cryptography.Xml HIGH ×2 patchable) |
| **HIGH** | 0 | None — all naturally-HIGH items are either REJECTed by scope OR accepted-risk per Decision C.1 |
| **REJECT** | 10 | Per spec §Out of Scope + Phase 0 Decisions |
| **OUTCOME E TRACK** | (separate) | Facade migration runs as its own Phase 4 track; not a tier-classified candidate |

**Phase 4 calendar projection**:
- SAFE-1, SAFE-2: 2 × 24-48h bake each = 2-4 days
- SAFE-3: no-op verification, ~30 min
- MEDIUM-1: 24-48h bake = 1-2 days
- Outcome E (tasks 046-051): independent parallel track, ~3-5 days
- **Total Phase 4 wall-clock**: 4-6 days (bake-window-dominated as designed)

---

## SAFE-Tier — 3 candidates

See [`inventory/candidates-safe.md`](./inventory/candidates-safe.md) for full per-candidate details.

| # | Title | FR | Savings | Phase 4 task |
|---|---|---|---|---|
| **SAFE-1** | Publish with `--runtime linux-x64` | FR-A1 | ~67 MB uncompressed, ~15-20 MB compressed | 040 |
| **SAFE-2** | Exclude `*.js.map` from publish | FR-A2 | ~5-7 MB uncompressed, ~2-3 MB compressed | 041 |
| **SAFE-3** | Cosmos ServiceInterop dedup | FR-A3 | NO-OP (already resolved upstream — Cosmos SDK 3.47.0) | 042 (verify-only) |

**Outcome A projected end state**:
- Compressed: 75.2 → ~53 MB (under 60 MB target ✅)
- Uncompressed: 212 → ~140 MB (under 150 MB target ✅)

**Critical enabler (resolved 2026-05-24)**: Task 019 migrated dev BFF from Windows to Linux (`spaarke-bff-dev`). FR-A1 now applies cleanly across all 3 envs (dev, demo, prod all Linux).

---

## MEDIUM-Tier — 1 candidate

See [`inventory/candidates-medium.md`](./inventory/candidates-medium.md) for full details.

| # | Title | FR | CVE | Phase 4 task |
|---|---|---|---|---|
| **MEDIUM-1** | Patch `System.Security.Cryptography.Xml 8.0.1` HIGH ×2 via Microsoft.IdentityModel.* bump | FR-B1 | GHSA-37gx-xxp4-5rgx + GHSA-w3x6-4m5h-cxqf | 044 |

**Outcome B projected end state**: 1 of 2 HIGH CVEs remediated (50%). The other HIGH (Kiota NU1903) is accepted-risk per Phase 0 Decision C.1 — see REJECT section.

---

## HIGH-Tier — 0 candidates

After Phase 0 decisions + Phase 1 findings, there are **no HIGH-tier candidates** for this project:

- The only natural HIGH-tier item (Kiota NU1903 CVE patch via Graph SDK 6.x upgrade) is REJECTed by spec §Out of Scope binding (forbidden Graph SDK / Kiota major-version bumps)
- All MEDIUM-1 + SAFE-tier candidates are dual-probe-verified safe; no removal of transitive-only-deps via parent-direct-removal is in scope
- Outcome E (facade migration) is its own Phase 4 track per design §6 Phase 4; not tier-categorized

---

## REJECT-Tier — 10 items (per spec §Out of Scope + Phase 0 decisions)

| # | Item | Source of REJECT | Notes / follow-up |
|---|---|---|---|
| R-1 | `<PublishTrimmed>true</PublishTrimmed>` | spec §Out of Scope | Graph SDK + Identity.Web + EF + DI + JSON serializers reflection-hostile — silent breakage |
| R-2 | `<PublishAot>true</PublishAot>` | spec §Out of Scope | Same reasoning, more aggressive |
| R-3 | .NET 8.0 → 9.0 SDK upgrade | spec §Out of Scope | Separate concern, separate risk profile |
| R-4 | Microsoft.Graph 5.101.0 → 6.x major bump | spec §Out of Scope + Phase 0 Decision C.1 | Follow-up project: "Graph SDK 6.x + Kiota 2.0 upgrade" (separate calendar ~3-4 weeks) |
| R-5 | Microsoft.Kiota.* 1.21.2 → 2.0 major bump | spec §Out of Scope + Phase 0 Decision C.1 | Chain-locked with R-4; same follow-up project |
| R-6 | Microsoft.Kiota.Abstractions NU1903 HIGH patch | spec §Out of Scope (via R-5) + Phase 0 Decision C.1 | **Accepted risk**. Document in LESSONS-LEARNED.md task 090 with CVE link + mitigation plan reference |
| R-7 | `Azure.AI.Projects 1.0.0-beta.8` version change | spec §Out of Scope (pre-release pin) + Phase 0 task 012 (rationale still valid) | Agent Framework chain-locked |
| R-8 | `Microsoft.Agents.AI 1.0.0-rc1` version change | spec §Out of Scope (pre-release pin) + Phase 0 task 012 | Agent Framework chain-locked |
| R-9 | `Azure.AI.OpenAI 2.8.0-beta.1` version change | spec §Out of Scope (pre-release pin) + Phase 0 task 012 | MissingMethodException avoidance documented |
| R-10 | ADR-010 DI minimalism fix (99+ → ≤15 registrations) | spec §Out of Scope | Separate architectural project. Task 038 captures baseline; task 054 verifies Phase 4 delta within +4 to +8 (Outcome E facade adds; consumer-side dependencies go down separately) |
| R-11 | Wider audit of `Spaarke.Core` / `Spaarke.Dataverse` publish outputs | spec §Out of Scope + Phase 0 Decision UQ-06 (inventory-only) | Wider audit is follow-up project |
| R-12 | `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` | spec §Out of Scope | Separate project |
| R-13 | OpenMcdf Moderate ×2 transitive patch | Severity below Outcome B's HIGH threshold | Defer to weekly Dependabot triage |
| R-14 | OpenTelemetry.Api Moderate transitive patch | Severity below Outcome B's HIGH threshold | Dependabot PR #175 may address; deferred per plan.md PR-1 |
| R-15 | 33+ other outdated transitives | Not security-driven; not Outcome A/B drivers | Deferred to weekly Dependabot triage per plan.md PR-1 |

---

## Outcome E — separate Phase 4 track (NOT tier-classified)

Per design §6 Phase 4, Outcome E (Internal AI Hygiene — `Services/Ai/PublicContracts/` facade migration + AI-coupled job handler relocation) runs as a parallel track and bundles per **plan.md PR-2** as a single squash-merged PR. Tasks:

- 046: Create facade interfaces
- 047-050: Migrate inventory-derived CRUD→AI consumers (per Phase 0 PF-3 deferral; Phase 1 task 014 produces authoritative consumer list)
- 051: Relocate AI-coupled job handlers (per G1 reconciliation; Phase 1 inventory produces authoritative handler list)
- 052-053: Test + grep verification
- 054: Gate review

Not categorized as SAFE/MEDIUM/HIGH because the work is structurally different (refactor, not configuration/removal). Approved per Phase 0 Decision UQ-07 (small focused interfaces).

---

## Phase 4 execution ordering

Per design §6 Phase 4, one change per deploy with 24-48h dev bake between each:

### Outcome A track (sequential by bake)
1. SAFE-1 (FR-A1 `--runtime linux-x64`) → 24h bake on new Linux dev → task 040
2. SAFE-2 (FR-A2 sourcemap exclusion) → 24h bake → task 041
3. SAFE-3 (FR-A3 verification no-op) → task 042 (no bake needed; verify-only)

### Outcome B track (sequential after A-track stable in dev)
4. MEDIUM-1 (IdentityModel.* bump for System.Security.Cryptography.Xml) → 24-48h bake → task 044
   - Tasks 043, 045 in TASK-INDEX become unused placeholders; remove or repurpose during task 044 execution

### Outcome E track (parallel to A + B tracks)
5. Tasks 046-051 + 052-054 (separate squash-merge PR)

### Phase 4 gate (task 054)
- All tracks stable; Phase 4 EXECUTION-LOG.md complete; ready for Phase 5 promotion

---

## Phase 2 Gate — Owner sign-off

Per design.md §6 Phase 2: "Owner reviews `CANDIDATES.md`; SAFE/MEDIUM/HIGH ordering approved."

Per **NFR-08 revised (2026-05-24)**: Operator-only model — owner sign-off is recorded via this commit on the project branch (operator authorship = sign-off).

### Pre-Phase-3 acknowledgments

- [x] All 3 SAFE candidates documented with cost / evidence / action / test plan / rollback
- [x] 1 MEDIUM candidate documented with dual-probe agreement (static + DI/deps.json)
- [x] 0 HIGH candidates (intentional — explained above)
- [x] 15 REJECT items enumerated with source-of-reject citation
- [x] Outcome E execution model preserved (separate Phase 4 track)
- [x] Phase 4 ordering proposed (SAFE first, then MEDIUM after A-track stable, Outcome E parallel)
- [x] Owner gate sign-off via commit (operator-only per NFR-08 revised)

**Phase 3 is AUTHORIZED to begin** upon completion commit of this file.

---

## Phase 3 input

Phase 3 (tasks 030-038) baselines current behavior on the NEW Linux dev (per task 019 cutover):
- Test suite results (`dotnet test tests/unit/Sprk.Bff.Api.Tests/`) — task 030
- Build warning count (no `--warnaserror` per Phase 0 Decision 7) — task 031
- Endpoint smoke-test pass — task 032
- **App Insights 48h baseline metrics** (post-cutover; sparse traffic expected since dev workload is light) — task 033
- Deployed file SHA-256s via Kudu VFS — task 034
- Current publish + zip metrics on Linux env — task 035
- Reflection-load probe baseline (already done via deps.json pragmatic alternative — task 036 will re-capture for Phase 3 comparison baseline) — task 036
- DI registration count baseline (ADR-010 measurable binding) — task 038
- Archive Phase 1 extraction-assessment + commit `BASELINE.md` — task 037

Phase 4 candidates from this file feed directly into Phase 4 task execution.

---

## Residual items (NOT blocking Phase 3)

These are tracked elsewhere and don't block Phase 3 start:

1. **Operator post-cutover monitoring** — weekly grep of worktree branches for new BFF-touching work (task 004 followup)
2. **G4 Insights Engine facade adoption agreement** — operator action before Phase 4 task 046
3. **Old Windows dev decommission** — after operator confirms cutover spot-check OK
4. **Graph email subscriptions auto-expire** — ~3 days to migrate webhook delivery to new dev (or operator PATCH manually for faster cutover)
