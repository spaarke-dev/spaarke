# Task Index — Spaarke Teams App (R1)

> **Generated**: 2026-08-03 by `/project-pipeline` (INITIALIZE-ONLY).
> **Execution is owner-gated** — run waves deliberately via `task-execute`; the pipeline did NOT auto-execute (owner directive `notes/pipeline-run-guidance.md`).
> **25 tasks · 9 phases.** All POMLs pass the completeness lint (`scripts/Validate-TaskPoml.ps1`).

## Legend
Status: 🔲 not-started · 🔄 in-progress · ✅ completed · ⛔ blocked · 🔁 needs-retry
Tier: model-tier @ effort (dispatch each subagent at these per root CLAUDE.md §8.5)

> **Wave 0 status (2026-08-03)**: Task **002 ✅ COMPLETE** — ADR-028 Amendment A2 applied to the canonical concise `.claude/adr/ADR-028` (workforce-plane MUST/MUST NOT + generalized pluggable-authority exemption; all A1 invariants preserved; internal Xrm surfaces unaffected; ADR-034 Path-C cross-ref). Deviation: `docs/adr/` full copy does not exist → applied concise-only, mirroring A1 (documented in the ADR note + CHANGELOG + draft). The governing rule is now in place for Phase-1 auth code. Task **001** autonomous portion complete — code-path verification of both membership planes done (findings: `notes/spikes/foundation-spike-findings.md`); systemuser plane code-GO, contact plane code-CONDITIONAL-GO (no architectural blocker; = tasks 020/021), SPA no-regression. **Live Teams desktop/web SSO go/no-go remains OPERATOR-GATED** (spike scaffold at `notes/spikes/teams-tab-spike/`). 001 stays 🔄 until the operator records an overall GO in findings §5. **🚦 GATE: Do NOT start Wave 1 until findings §5 records an overall GO.**

## Task Registry

| ID | Title | Phase | Status | Deps | Tier | Rigor | Parallel-safe | Group |
|----|-------|-------|--------|------|------|-------|---------------|-------|
| 001 | Foundation spike — workforce SSO → membership (both planes) + SPA unchanged | 0 | 🔄 | none | opus@xhigh | FULL | ❌ (foundation gate, main-session) | — |
| 002 | Apply ADR-028 Amendment A2 (workforce auth) — Path B | 0 | ✅ | none | opus@high | FULL | ❌ (.claude/ + docs/adr, main-session) | — |
| 010 | Shared standalone-MSAL module, pluggable authority | 1 | ✅ | 001,002 | opus@xhigh | FULL | ❌ (serial client-auth chain) | A |
| 011 | Teams SSO/NAA client strategy | 1 | ✅ | 010 | opus@high | FULL | ❌ (serial; shared auth files) | A |
| 012 | Teams host adapter + host-detection seam | 1 | ✅ | 011 | sonnet@high | FULL | ❌ (serial; shared bootstrap) | A |
| 020 | Workforce→principal resolver (collaboration endpoints) | 2 | ✅ | 001,002 | opus@xhigh | FULL | ❌ (serial BFF auth spine) | B |
| 021 | Contact-anchored membership entry (role-allowlist filtered) | 2 | ✅ | 020 | opus@xhigh | FULL | ❌ (serial; membership engine) | B |
| 022 | Accessible-record-set composition + enforcement gate | 2 | ✅ | 020,021 | opus@xhigh | FULL | ❌ (serial; enforcement) | B |
| 025 | **Principal-agnostic collab endpoints (Option A, dual-scheme /external) — R2 FR-22** | 2 | 🔄 | 020,021,022,030,051,065 | opus@xhigh | FULL | ❌ (serial; shared auth endpoints) | B |
| 030 | Broker-only SPE download gated by accessible-set | 3 | ✅ | 022 | opus@high | FULL | ❌ (serial; BFF download path) | B |
| 040 | TrackingFieldTrio two-icon governance toolbar | 4 | ✅ | 021 | sonnet@high | FULL | ❌ (serial PCF chain; shared component) | C |
| 041 | Access-grant modal (candidates + named users → grant + invite) | 4 | ✅ | 040 | sonnet@xhigh | FULL | ❌ (serial; shared component) | C |
| 042 | Email-members action → SendEmailDialog (ADR-045) | 4 | ✅ | 040 | sonnet@high | FULL | ❌ (serial; shared component) | C |
| 043 | Access-Permission Option-A sharing-gate on the modal | 4 | ✅ | 041 | sonnet@high | FULL | ❌ (serial; modifies 041 modal) | C |
| 045 | Deploy TrackingFieldTrio PCF to Dataverse | 4 | ✅ | 043,065 | sonnet@high | STANDARD | ❌ (deploy; shared env) | C |
| 050 | Standing-grant field on `contact` + form toggle | 5 | ✅ | 001 | sonnet@high | STANDARD | ✅ | D |
| 051 | Standing-grant runtime union into accessible-set | 5 | ✅ | 022,050 | opus@high | FULL | ❌ (modifies 022 composition) | D |
| 060 | BFF `tid`→environment routing | 6 | ✅ | 020 | opus@xhigh | FULL | ✅ | E |
| 061 | Multitenant workforce Entra + admin-consent onboarding | 6 | ✅ | 002 | sonnet@high | STANDARD | ✅ | E |
| 062 | Teams framing headers (CSP frame-ancestors) on SWA host | 6 | ✅ | 012 | sonnet@high | STANDARD | ✅ | E |
| 065 | Deploy BFF (resolver, membership, routing, download) | 6 | ✅ | 030,051,060 | sonnet@high | STANDARD | ❌ (deploy; shared spaarke-bff-dev) | E |
| 070 | Teams manifest v1.29 + M365 Agents Toolkit packaging | 7 | ✅ | 061,062 | sonnet@high | STANDARD | ✅ | F |
| 071 | New Teams-app CI deploy workflow | 7 | ✅ | 070 | sonnet@high | FULL | ✅ (⚠️ ci-workflows hot-path — /conflict-check) | F |
| 072 | Org-catalog distribution + Publisher Attestation prep | 7 | ✅ | 070 | sonnet@high | MINIMAL | ✅ | F |
| 080 | End-to-end integration verification (graduation criteria) | 8 | 🔲 | 045,065,072,051 | opus@high | FULL | ❌ (needs all phases) | G |
| 090 | Project wrap-up (gates, cleanup, docs, test-diet) | 8 | 🔲 | 080 | sonnet@high | FULL | ❌ (final; edits README/plan + .claude gates) | G |

## Critical Path (longest dependency chain)

```
001/002 → 020 → 021 → 022 → 030 → 065 → 045 → 080 → 090
                    └→ 040 → 041 → 043 ────┘
```
The **auth spine** (020 → 021 → 022 → 030) is the load-bearing serial chain; the PCF chain (040 → 041 → 043) and the standing-grant/enterprise streams run concurrently and re-converge at the BFF deploy (065) and integration verification (080).

## Streams (run concurrently; serial internally)

| Group | Stream | Tasks (serial within) | Head prereq |
|-------|--------|-----------------------|-------------|
| A | Client auth + Teams host | 010 → 011 → 012 | 001,002 |
| B | BFF auth spine | 020 → 021 → 022 → 030 | 001,002 |
| C | PCF access-management | 040 → 041 → (042) → 043 → 045 | 021 |
| D | Standing grant | 050 ∥ (051 after 022) | 001 |
| E | Enterprise / routing / deploy | 060, 061, 062 → 065 | 002/012/020 |
| F | Teams package + CI | 070 → (071 ∥ 072) | 061,062 |
| G | Verify + wrap-up | 080 → 090 | all |

## Parallel Execution Plan (wave-by-wave — owner runs each wave deliberately)

> **Semantics**: `parallel-safe:false` prevents concurrency with **same-stream, file-sharing siblings** (already enforced by `deps`). The wave grouping below never co-schedules two file-sharing tasks, so the cross-stream tasks in each wave ARE safe to dispatch as concurrent `task-execute` agents. **Main-session-only** tasks (touch `.claude/`) must NOT be delegated to a sub-agent (root CLAUDE.md §3). **Max 6 agents/wave.** Build-verify between waves. Run `/conflict-check` before every BFF PR.

| Wave | Tasks | Concurrency | Notes | goal-eligible |
|------|-------|-------------|-------|---------------|
| 0 | **001** → **002** | sequential, main-session | 001 is the go/no-go gate; 002 edits `.claude/adr/`+`docs/adr/` (main-session-only). Do NOT start Wave 1 until the spike is GO. | NO (2 tasks, auth/ADR judgment) |
| 1 | 010 · 020 · 050 · 061 | 4 agents | Heads of streams A/B/D/E; different file surfaces. | NO (auth/security in the wave) |
| 2 | 011 · 021 · 060 | 3 agents | 060 after resolver (020) to avoid BFF DI churn. | NO (auth) |
| 3 | 012 · 022 · 040 | 3 agents | 022 = enforcement gate; 040 starts PCF. | NO (auth + frontend judgment) |
| 4 | 030 · 051 · 041 · 042 · 062 | 5 agents | 062 needs the host adapter (012). | NO (auth) |
| 5 | 043 · 065 · 070 | sequential-ish | 065 = **BFF deploy** (shared `spaarke-bff-dev`, coordinate; verify ≤60 MB). | NO (deploy) |
| 6 | 045 · 071 · 072 | 2–3 agents | 045 = **PCF deploy** (after BFF deploy); 071 ci-workflows hot-path. | NO (deploy) |
| 7 | 080 | sequential | End-to-end verification against all 7 graduation criteria. | NO (verification/judgment) |
| 8 | 090 | sequential, main-session | Wrap-up: code-review + adr-check + repo-cleanup + `/test-diet` + README/plan → Complete. | NO (gates) |

**No wave is `/goal`-eligible.** This project is auth/security/deploy-heavy end-to-end (Step 3.85 excludes security/auth/deploy/irreversible waves); every wave requires the orchestrator's accept/patch/escalate judgment. Run waves with the normal per-wave "continue".

## High-Risk Items

- **001 spike** — go/no-go for the whole project; a Teams SSO/NAA desktop failure or a contact-plane resolution failure is an escalation, not a workaround (FR-16).
- **002 ADR-028 A2** — must merge **before/with** Phase-1 auth code (Path B); main-session-only.
- **020/021/022** — the auth spine; contact-anchored membership MUST reuse `BuildFetchXml` (ADR-034 Path C) and filter to the `sprk_assigned*` allowlist (NFR-05) — adverse roles must never confer access.
- **060 `tid`→env routing** — misroute = cross-tenant data exposure; unmapped `tid` must be denied by construction.
- **065 BFF deploy** — publish ≤60 MB (baseline ~49.63 MB); no M365 Agents SDK/Bot; `/conflict-check` (13+ active BFF worktrees).
- **071 CI workflow** — `.github/workflows/**` is a contended hot-path.

## How to execute a wave

1. Confirm all prerequisites for the wave are ✅ in this table.
2. For a multi-agent wave, send ONE message with MULTIPLE `task-execute` Skill invocations (one per task), EXCEPT main-session-only tasks (002, 090 gates) which run in the main session.
3. Dispatch each subagent at its `Tier` (model + effort).
4. After the wave, build-verify (`dotnet build src/server/api/Sprk.Bff.Api/` for `.cs`; `npm run build:prod` for PCF) before the next wave.
5. Update this table's Status column (🔲 → ✅).
