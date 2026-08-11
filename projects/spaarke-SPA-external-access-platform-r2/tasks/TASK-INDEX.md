# Task Index — Spaarke External Access Platform (R2)

> **Status**: INITIALIZED + re-decomposed for the WORKSPACE-SHELL foundation (P0 signed off 2026-08-06).
> Execution owner-gated wave-by-wave.
> **Foundation**: workspace shell (branded portal + pinned Quick Start tab + tabbed role-defaulted widgets
> + widget library + dockable assistant) — NOT the card-launcher (superseded). See `design.md` §12.
> **Total**: 45 tasks (P0:4 ✅ · P1:10 · P2:9 · P2b:4 · P3:9 · P4:4 · P5:4 · wrap-up:1). Includes 2 spikes.
> **Amendment (2026-08-10, P2b wave)**: Tasks **070–073** added — "Polymorphic access-write (grant authoring)",
> the WRITE/admin companion to task 028's polymorphic reads. Generalizes the teams-app-r1 grant surface (BFF
> grant-write + TrackingFieldTrio PCF + AccessGrantModal) from PROJECT-only to Project/Matter/WorkAssignment,
> adopts the shared side-pane Advanced Lookup (INavigationService.openLookup), and wires Tier-1 entitlement
> **Option B** (owner-created `sprk_approlemodulemap`; CIAM blanket-entitled). Owner already completed the schema
> (020 ✅), `sprk_accesspermission` on Project/WA, and TrackingFieldTrio form placement. Binding design:
> notes/polymorphic-grant-authoring-enhancement.md.
> **Amendment (2026-08-10)**: Task **028** added — polymorphic Tier-2 scoping across roots (Project/Matter/Work
> Assignment) + internal-only Service Requests tab, discovered via R2 grid-widget UAT. It **amends completed
> tasks 015 + 016** (single-parent framework + registrations) and **supersedes** the partial documents-by-project
> fix (commit `bff7e82e5`). No Dataverse schema change. Binding spec: `notes/external-access-polymorphic-scoping-design.md`.

Legend: 🔲 not-started · ✅ completed · Tier S=sonnet O=opus · Eff h=high x=xhigh m=medium · 🔬=spike

| ID | Title | Phase | Status | Deps | Tier/Eff | Rigor | Parallel |
|----|-------|-------|--------|------|----------|-------|----------|
| 001 | Prototype scaffold (shell + launcher + realm chooser) | P0 | ✅ | none | S/h | STANDARD | — |
| 002 | Prototype seed factories + 3-persona preset | P0 | ✅ | 001 | S/m | STANDARD | — |
| 003 | Prototype Legal Front Door screens | P0 | ✅ | 002 | S/h | STANDARD | — |
| 004 | Owner visual-approval gate (workspace-shell signed off) | P0 | ✅ | 003 | S/m | MINIMAL | — |
| 010 | ADR-028 Amendment A3 | P1 | ✅ | none | O/h | FULL | — (.claude/ main-session) |
| 011 | Workspace-shell scaffold (portal header + tab host + pane layout + dockable assistant) | P1 | ✅ | 004,010 | S/h | FULL | — (shell base) |
| 012 | Widget registry + tabbed workspace + pinned Quick Start + entitlement-gated widget library | P1 | ✅ | 011 | S/h | FULL | Group B |
| 013 | Dual-plane auth bootstrap (CIAM + realm discovery) | P1 | ✅ | 011 | O/h | FULL | — (auth) |
| 014 | Teams app packaging (manifest + CSP + theme) | P1 | ✅ | 011 | S/h | STANDARD | Group B |
| 015 | FR-22 module/widget-data framework generalization | P1 | ✅ (amended by 028) | 010 | O/x | FULL | — (core BFF) |
| 016 | Outside Counsel widgets (Projects/Matters/Work Assignments/Documents/Invoices) | P1 | ✅ (Tier-2 scoping amended by 028) | 012,015 | S/h | FULL | — |
| 017 | Cleanup dead Power Pages proxy/config | P1 | ✅ | 011 | S/h | STANDARD | Group B |
| 018 | Cleanup inert filter + /api/v1/collab | P1 | ✅ | 015,016 | O/x | FULL | — (BFF deletion) |
| 019 | Deploy P1 (workspace shell + Teams, SWA) | P1 | ✅ | 012,013,014,016,017 | S/h | STANDARD | — (deploy; from wt, live auth E2E owner-pending) |
| 020 | Module/widget-entitlement Dataverse schema | P2 | ✅ (Option B; owner-created `sprk_approlemodulemap` 2026-08-10) | 015 | S/h | FULL | — (schema) |
| 021 | Entitlement resolver (App-Role + Contact strategies) | P2 | 🔲 (amended by 072 — Option B) | 020 | O/h | FULL | — (auth core) |
| 022 | GET /me entitlement endpoint (Redis-cached) | P2 | 🔲 (amended by 072 — Option B + tab config) | 021 | O/h | FULL | Group C |
| 023 | Lazy Contact attribution (oid resolve-or-create) | P2 | 🔲 | 021 | S/h | FULL | Group C |
| 024 | Workforce-plane external-app auth policy | P2 | 🔲 | 015 | O/h | FULL | — (auth) |
| 025 | D1 workforce role→level grading | P2 | 🔲 | 024 | S/x | FULL | — |
| 026 | Core-user admin UI (grant/revoke; reuse AccessGrantModal) | P2 | 🔲 | 021 | S/h | FULL | Group C |
| 027 | Deploy P2 (BFF + entitlement schema) | P2 | 🔲 | 022,023,024,025,026 | S/h | STANDARD | — (deploy) |
| 028 | Polymorphic Tier-2 scoping across roots (Project/Matter/WorkAssignment) + internal-only Service Requests tab (supersedes bff7e82e5; amends 015/016) | P2 | ✅ | 015,016 | O/x | FULL | — (auth boundary; own redeploy; deployed dev, live both-plane UAT owner-pending) |
| 070 | Polymorphic external grant-WRITE (BFF) — grant/revoke across Project/Matter/WorkAssignment + close-project lookup-name bug fix | P2b | ✅ (deployed + live-verified 2026-08-11; ALSO fixed the fully-broken grant path: PascalCase @odata.bind nav names, grantedby oid→systemuserid, expiry field, account→org removed) | 028,020 | O/x | FULL | — (auth boundary write; own redeploy) |
| 071 | Polymorphic grant UI — TrackingFieldTrio host + AccessGrantModal across roots; adopt shared side-pane Advanced-Lookup (INavigationService.openLookup) | P2b | ✅ (v1.0.12 built + imported to SPAARKE DEV 1 2026-08-11; host-entity-derived recordType, `_sprk_{root}_value` read-filter fix, side-pane pickContact + optional org picker; 25 modal tests; live matter-grant read verified. UI click-path smoke = task-073 UAT) | 070 | S/h | FULL | — (shared lib + PCF; solution import) |
| 072 | Tier-1 entitlement Option-B wiring — resolver reads sprk_approlemodulemap + blanket-entitle CIAM + widgetRegistry tab sets (amends 021/022) | P2b | 🔲 | 020 | O/h | FULL | — (auth; /me + widgetRegistry) |
| 073 | Deploy + both-plane UAT — polymorphic access-write wave (BFF + PCF + entitlement) | P2b | 🔲 | 070,071,072 | S/h | STANDARD | — (deploy; from wt; both-plane UAT) |
| 030 | Intake schema (servicerequest + FR-24 feedback + thread-on-request) | P3 | 🔲 | 020 | S/h | FULL | — (schema) |
| 031 | Generic typed-intake framework | P3 | 🔲 | 022,030 | O/h | FULL | — (framework base) |
| 032 | NDA AI 3-outcome assessment (FR-23) | P3 | 🔲 | 031 | O/x | FULL | — (auth+AI) |
| 033 | 🔬 NDA-redline-surface SPIKE (FR-23b) | P3 | 🔲 | 032 | S/h | STANDARD | — (spike) |
| 034 | P&P document architecture (sprk_documentcategory + type-routed index + Policy grid) (FR-25) | P3 | 🔲 | 020 | O/h | FULL | — (schema+index) |
| 035 | Review Policy Question + Policy Library browse (FR-25) | P3 | 🔲 | 031,034 | S/h | FULL | — |
| 036 | Submitter authz (Tier-2) + app-only SPE upload (FR-17) | P3 | 🔲 | 030,031 | O/x | FULL | — (auth+SPE) |
| 037 | My-requests + feedback (email + in-app) + thread-on-request (FR-24) | P3 | 🔲 | 031,036 | S/h | FULL | — |
| 038 | Deploy P3 (SWA + BFF + schema) | P3 | 🔲 | 032,033,034,035,036,037 | S/h | STANDARD | — (deploy) |
| 040 | Provisioner self-healing (CIAM 409) | P4 | 🔲 | none | O/x | FULL | — (brownfield) |
| 041 | Live-E2E external auth (wrong-issuer→401; no email-hijack) | P4 | 🔲 | 019,027 | S/h | STANDARD | — (live) |
| 042 | SSPR first-run verification + doc | P4 | 🔲 | 019 | S/m | STANDARD | — |
| 043 | Legal handoff — MDA routing/assignment | P4 | 🔲 | 038 | S/h | STANDARD | — |
| 050 | 🔬 External Assistant Access & Permission SECURITY SPIKE (gates 051) | P5 | 🔲 | 034 | O/x | FULL | — (security spike, human sign-off) |
| 051 | Ask Legal assistant (bounded: policy_search + wizard routing; no file ingest) (FR-26) | P5 | 🔲 | 050,012,034 | O/h | FULL | — (AI external plane) |
| 052 | Cross-boundary messaging (external thread endpoint + ConversationView) (FR-27) | P5 | 🔲 | 015 | O/h | FULL | — (shared Comm surface) |
| 053 | Deploy P5 (SWA + BFF) | P5 | 🔲 | 051,052 | S/h | STANDARD | — (deploy) |
| 090 | Project wrap-up (gates + test-diet + close-out) | Wrap-up | 🔲 | all | S/h | FULL | — (serial) |

## Gated / spike tasks (do NOT skip the gate)
- **033 🔬 NDA-redline-surface spike** → gates the outcome-(b) redline *build* (a follow-on; not in R2 unless the spike says so). Task 032 mocks the redline UX.
- **050 🔬 External-Assistant security spike** → **gates 051** (Ask Legal build). **Security-sensitive → human sign-off (§6).** Resolves P&P RAG trim + 2-tool-catalog escape-proofing + wizard-routing safety + audit.

## Parallel Execution Groups
| Group | Tasks | Prereq | Safe |
|-------|-------|--------|------|
| B | 012, 014, 017 | 011 | ✅ (distinct: widget-workspace / Teams manifest / vite cleanup) |
| C | 022, 023, 026 | 021 | ✅ (distinct: /me endpoint / lazy-contact / admin UI) |

Everything else sequential (schema-blocks, auth-sensitive, shared BFF surface, deploys, spikes, `.claude/` main-session-only for 010). **MAX 6 agents/wave.** No wave is `/goal`-eligible (all security/auth/deploy/irreversible or gated). **`/conflict-check` before every BFF PR** (external-access surface + `Services/Communication` for 052).

## Critical path
```
004 → 011 → 012 → 016 → 019 → 041
010 → 015 → 020 → 034 → 050(spike) → 051 → 053
              → 021 → 031 → 032 → 038 → 043 → 090
```

## High-risk / coordination
- **015 / 018 / 028 / 052** — external-access + `Services/Communication` shared BFF surface; `/conflict-check` per PR. teams-app-r1 is COMPLETE/merged (stable base, not concurrent).
- **028** — auth-boundary generalization (multi-dimension Tier-2 scoping + dual-plane accessible-root composition); FULL/O-x; own worktree redeploy + both-plane UAT; carries an `<escalation>` trigger for missing access-source schema. Supersedes the partial `bff7e82e5` documents fix (documents currently scoped project-only → hides matter/WA-linked docs).
- **010** — ADR-028 A3, main-session-only, reads existing A2 first, ordered before P1 auth.
- **020 / 030 / 034** — Dataverse schema (entitlement / intake option-set+status / documentcategory+governance) need owner sign-off (escalation).
- **032 / 036 / 040 / 051** — auth + SPE + app-only (no OBO) + AI-on-external-plane — highest reasoning; 051 gated by the 050 security spike.
- **032 redline (b)** — mocked; real build gated by spike 033.

## How to execute
1. Prereqs ✅ + `dotnet build src/server/api/Sprk.Bff.Api/` before any BFF wave; `/conflict-check` before any BFF PR.
2. `task-execute` per task; parallel groups = one message, multiple Skill calls, ≤6 agents.
3. `.claude/`-touching (010) is main-session-only — never a sub-agent.
4. Spikes (033, 050) produce decision docs in `notes/` and gate their downstream build; 050 needs human sign-off.
