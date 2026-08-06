# Task Index — Email Communication Intelligence R2

> **Generated**: 2026-08-05 via `/project-pipeline` · **40 tasks** across 7 phases (0–6)
> **Revised 2026-08-05**: spikes 001/002 retired (gate-after-write + Tier-2-deferred decisions); see Resolved decisions below.
> **Status legend**: 🔲 not-started · 🔄 in-progress · ✅ completed · ⛔ blocked · ⏸️ deferred
> **Execution**: via `task-execute` per task (Sonnet 5 @ high default; `<model-tier>`/`<effort>` per POML).
> **⚠️ Execution intentionally NOT started** — operator review gate (per pipeline run choice).

## Registry

| # | Task | Phase | Status | deps | model·effort | parallel-safe |
|---|---|---|---|---|---|---|
| 003 | R1 close-out — reconcile 013 + pin golden misfile emails | 0 | ✅ | — | sonnet·high | ✅ true |
| 004 | Entra NAA app-registration verify/provision | 0 | 🔲 | — | sonnet·med | ✅ true |
| 010 | HMAC footer/token signing helper (Key Vault) | 1·A | 🔲 | — | **opus·xhigh** | ❌ false |
| 011 | Footer config (operator app setting, per-tenant) | 1·A | ✅ | — | sonnet·high | ❌ false |
| 012 | Inject signed footer on outbound send path | 1·A | 🔲 | 010,011 | sonnet·high | ❌ false |
| 013 | `TrackingTokenRung` (reuse RungKind.ExplicitReference) | 1·A | 🔲 | 010,011 | **opus·high** | ✅ true (A-rungs) |
| 014 | `RecipientAliasRung` + Bcc plumbing | 1·A | ✅ | 011 | sonnet·high | ✅ true (A-rungs) |
| 015 | Formalize external-reply self-association + test | 1·A | ✅ | — | sonnet·high | ❌ false |
| 016 | `AffinityRung` + `sprk_affinity` store | 1·A | 🔲 | 011 | **opus·high** | ✅ true (A-rungs) |
| 017 | Pillar A BFF deploy (size/CVE) | 1·A | 🔲 | 010–016 | sonnet·med | ❌ false |
| 020 | Alternate key on `sprk_communication.sprk_internetmessageid` | 2·C | 🔲 | — | sonnet·high | ✅ true (schema-c) |
| 021 | Canonical message-id dedup — race-proof create + SB idempotency | 2·C | 🔲 | 020 | **opus·xhigh** | ❌ false |
| 022 | Context-merge on duplicate | 2·C | 🔲 | 021 | sonnet·high | ❌ false |
| 023 | Indexed `sprk_document.sprk_canonicalhash` column (forward-only) | 2·C | 🔲 | — | sonnet·high | ✅ true (schema-c) |
| 024 | SPE content dedup Tier-1 — **gate-after-write** (quickXorHash detector) | 2·C | 🔲 | 023 | **opus·high** | ❌ false |
| 025 | Cross-path reconciliation (comm ↔ document via message-id) | 2·C | 🔲 | 021 | sonnet·high | ❌ false |
| 026 | Pillar C BFF deploy (size/CVE) | 2·C | 🔲 | 021,022,023,024,025 | sonnet·med | ❌ false |
| 030 | Fix FR-06 RAG grounding — ParentEntity tagging (both sites) | 3·D | ✅ | — | sonnet·high | ✅ true (D-indep) |
| 031 | Batched identifier query (≈175→≤7) | 3·D | ✅ | — | sonnet·high | ✅ true (D-indep) |
| 032 | Golden regression suite (+ absorbs A3 test) | 3·D | ✅ | 015 | sonnet·high | ✅ true (D-indep) |
| 033 | Job B allow-list seed (`sprk_emailupdatefield`) | 3·D | 🔲 | — | sonnet·med | ✅ true (D-indep) |
| 034 | Job C apply endpoint + create-task queue-feed discriminator | 3·D | 🔲 | — | **opus·high** | ❌ false |
| 035 | Pillar D BFF deploy (size/CVE) | 3·D | 🔲 | 030,031,034 | sonnet·med | ❌ false |
| 040 | Add-in realignment (FR-B0 a–d) | 4·B | 🔲 | 004 | sonnet·high | ✅ true (PB-a) |
| 041 | Real Spaarke intake folder (both mechanisms) | 4·B | 🔲 | — | sonnet·high | ✅ true (PB-a) |
| 042 | Drag-to-matter + engine pre-select + ribbon quick-save | 4·B | 🔲 | 041 | sonnet·high | ✅ true (PB-b) |
| 043 | Unify user-upload with capture (engine + dedup) | 4·B | 🔲 | 021,024 | sonnet·high | ❌ false |
| 044 | Deploy Pillar B add-in (Azure SWA) | 4·B | 🔲 | 040,042 | sonnet·med | ❌ false |
| 045 | Pillar B BFF deploy (size/CVE) | 4·B | 🔲 | 041,043 | sonnet·med | ❌ false |
| 050 | Reconciliation grid — enhance DataGrid + Needs-review config | 5·E | 🔲 | — | sonnet·high | ❌ false (shared lib) |
| 051 | Triage as grid columns | 5·E | 🔲 | 050 | sonnet·high | ❌ false |
| 052 | Related-to card-picker (reuse EmailConnectionsReview) | 5·E | 🔲 | 050 | sonnet·high | ❌ false |
| 053 | Browse shell + one normalized reader (attachment folding) | 5·E | 🔲 | 050 | **opus·xhigh** | ❌ false |
| 054 | Citation navigation — ParaIdMap + resolveCitation (reuse Compose) | 5·E | 🔲 | 053 | **opus·xhigh** | ❌ false |
| 055 | Field-update reconcile tab (Job B, editable, apply-under-audit) | 5·E | 🔲 | 052,053 | sonnet·high | ❌ false |
| 056 | Task/deadline reconcile tab (Job C, create-and-complete + ad-hoc) | 5·E | 🔲 | 034,052,053 | sonnet·high | ❌ false |
| 057 | Reconciliation routing (category→team + per-team views) | 5·E | 🔲 | 050 | sonnet·high | ❌ false |
| 058 | r5 coordination contract (record R2 ownership D/E/F) | 5·E | ✅ | — | sonnet·med | ❌ false |
| 059 | Deploy Pillar E — code page + SpaarkeAi widget | 5·E | 🔲 | 050–057 | sonnet·med | ❌ false |
| 090 | Project wrap-up (test-diet, lessons, doc-drift, size) | 6 | 🔲 | all | sonnet·high | ❌ false |

## Parallel Execution Groups (waves)

| Wave | Tasks | Prerequisite | Notes |
|---|---|---|---|
| **W0 — prereqs** | 003, 004 | — | Independent; parallel. *(Spikes 001/002 retired 2026-08-05 — gate-after-write + Tier-2-deferred.)* |
| **W1a — A foundation** | 010 → 011 → 012 | — | `parallel-safe:false` (shared `CommunicationModule`/`Configuration`/send path) — **sequential**. 015 (test-only) any time. |
| **W1b — A-rungs** | 013, 014, 016 | 010,011 | Parallel *within this project* (distinct rung files) — but `/conflict-check` on shared `CommunicationModule.cs`/`RungKind.cs`/`AssociationStatusMapper.cs`. |
| **W2-schema — C schema** | 020, 023 | — | Parallel (disjoint schema surfaces). |
| **W2-code — C dedup** | 021 → 022, 025 · 024 | 021←020; 024←023 | `parallel-safe:false` (contended `Services/Communication` / `SpeFileStore`) — **sequential**. |
| **W3 — D independent** | 030, 031, 033 (+032←015) | — | Parallel. **Goal-eligible candidate** (machine-verifiable, low-ambiguity, non-security) — operator may run under `/goal`; Step 9.5 authority unchanged. |
| **W3-code — D5** | 034 | — | Sequential (contract). Backs 056. |
| **W4 — B** | 040, 041 (PB-a) → 042 (PB-b) · 043 | 040←004; 042←041; 043←021,024 | 040/041 parallel; 043 gated on C1/C3. |
| **W5 — E (sequential)** | 050 → {051,052,053,057} → {054,055,056} | 050 first; 056←034 | **All `parallel-safe:false`** (shared `Spaarke.Communication.Components` + `DataGrid`) — **strictly sequential, main-session**. |
| **Deploys** | 017, 026, 035, 044, 045, 059 | per-phase | After their phase's code lands; each reports publish-size ≤60 MB. |
| **W6 — wrap-up** | 090 | all | `/test-diet`, lessons, doc-drift, coordination + INDEX, size report. |

## Critical Path

`020 → 021 → 043 → 045` and `023 → 024 → 043` (dedup foundation → unify upload → deploy) **and** the Pillar E spine `050 → 053 → 054 / 055 / 056 → 059` (with `056` gated on `034`). `090` is terminal (deps all). Longest chains are Pillar C→B backend and the Pillar E reader/citation glue. *(No spike gate — 023/024 start on their own deps.)*

## 🚨 Hot-path coordination (BINDING — run `/conflict-check` before every shared PR)

`parallel-safe:false` on all shared-Communication / DataGrid / Compose writers. Contending active worktrees:
- **email-communication-solution-r5** — owns `Spaarke.Communication.Components` (Pillar E); update its coordination contract (task 058).
- **spaarke-dataset-grid-framework-r2** — shared `DataGrid` (task 050 enhancement).
- **messaging-communication-app r1/r2/r3**, **spaarke-notification-spine-r1**, **email-communication-solution-r4** — shared `Services/Communication` persist/emit path (Pillars A/C/B/D).
- **spaarkeai-compose-r5 / -fidelity-r4.5** — `CitationResolver` reused by task 054 (do NOT fork — NFR-11).
- **spaarke-ai-architecture-redesign-r2** — `Services/Ai` owner; reach AI only via `PublicContracts/` (task 034 — NFR-05/ADR-013).

## ✅ Resolved decisions (owner, 2026-08-05)

1. **SPE content dedup → gate-after-write** (tasks 023/024): read `quickXorHash` from the driveItem metadata **after** upload, reconcile + notify (never silently suppress a document); accept a brief transient blob. **Spikes 001/002 retired.**
2. **SPE Tier-2 (near-dup) → deferred out of R2** — exact-hash Tier-1 only (task 024); near-dup is a follow-up.
3. **FR-E5 task fields vs `IActionSeam.CreateTaskAsync` → Path B "add fields"** (task 034): create via the seam, then PATCH status/completed-date/**base-date/final-due-date** via impersonated `UpdateRecordAsync` under the same audit row; **add `base-date` + `final-due-date` as new task-entity fields** (schema step in 034). Facade unchanged (ADR-013); full FR-E5 field set structured in R2. 056 consumes it.
4. **Backfill → forward-only** (tasks 023/030): no historical reprocessing.
5. **Browse shell → `BrowseModal` preset** (`@spaarke/ui-components`, `SprkModal/presets`; ADR-050 / MODAL-DESIGN-SYSTEM / MODAL-DECISION-CRITERIA) — task 053.

## High-risk items
- 021 (race-proof structural dedup), 024 (SPE detector), 010 (HMAC signing), 034 (Job C audit + PATCH), 053/054 (citation-map glue) — the `opus`/`xhigh` tasks; highest blast radius.
- Gate-after-write (024): the detector notifies + links on a hit, **never silently suppresses** a document (data-loss guard).

---

*Execute via `task-execute`. Update this table's status column (🔲→✅) as the last step of each task.*
