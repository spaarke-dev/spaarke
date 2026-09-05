# Task Index — `spaarkeai-word-add-in-r1`

> **Generated**: 2026-09-04 by `/project-pipeline` (initialize-only)
> **Total**: 35 tasks across 5 phases
> **Status legend**: 🔲 not started · 🔄 in progress / needs retry · ✅ complete · ⛔ blocked · ⏭️ deferred

**Execute via `task-execute` only.** Never read a POML and implement manually (root CLAUDE.md §4).

---

## Phase 0 — De-risk and baseline

Gates most of Phases 1–3. Do not size Phase 1 until this closes.

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 001 | Worktree bootstrap and true typecheck baseline | 🔲 | MINIMAL | sonnet / medium | — | none |
| 002 | **Spike-1**: `document.url` shape for SPE files in Word desktop | 🔲 | STANDARD | opus / high | P0-spikes | none |
| 003 | **Spike-2**: Office Dialog API for opening a record | 🔲 | STANDARD | sonnet / high | P0-spikes | none |
| 004 | **Spike-3**: can a task pane open the Copilot pane (timeboxed) | 🔲 | MINIMAL | sonnet / medium | P0-spikes | none |
| 005 | **Spike-4**: does the add-in save path share the shipped collision semantics | 🔲 | STANDARD | opus / high | P0-spikes | none |
| 006 | FR-18: clear typecheck debt in `shared/taskpane` | 🔲 | FULL | sonnet / high | P0-typecheck | 001 |
| 007 | FR-18: clear typecheck debt in `shared/adapters` + `shared/services` | 🔲 | FULL | sonnet / high | P0-typecheck | 001 |
| 008 | FR-18: clear typecheck debt in `word/` + `outlook/` | 🔲 | FULL | sonnet / high | P0-typecheck | 001 |

**Gate**: typecheck clean · four spike reports in `notes/spikes/` · FR-01, FR-12 and FR-20 scope decided.

---

## Phase 1 — Foundation

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 010 | FR-04: consolidate onto one Word adapter via `HostAdapterFactory` | 🔲 | FULL | opus / xhigh | P1-a | 006, 007, 008 |
| 011 | FR-05: migrate Word to the unified JSON manifest | 🔲 | STANDARD | sonnet / high | P1-a | none |
| 012 | FR-01 server: document-identity resolver extending `/api/documents` | 🔲 | FULL | opus / high | — | 002 |
| 013 | FR-01 client: `getDocumentUrl` capability and identity threading | 🔲 | FULL | sonnet / high | — | 010, 012 |
| 014 | FR-02: server-side custom XML part GUID stamp (forward-only) | 🔲 | FULL | opus / high | P1-b | 012 |
| 015 | FR-03: Save\|Find tab shell, enable navigation in Word | 🔲 | FULL | sonnet / high | P1-b | 010 |
| 016 | Un-skip `/api/office/save` contract tests + cover the identity route | 🔲 | FULL | sonnet / high | — | 012 |

**Gate**: a Spaarke-sourced document is identified end-to-end · both hosts render both tabs · `/api/office/save` has executing tests.

---

## Phase 2 — Save flow

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 020 | FR-06: filename defaults to Document Name, editable in-pane | 🔲 | FULL | sonnet / high | P2-a | 015 |
| 021 | FR-07: Description becomes Profile, populated from the record | 🔲 | FULL | sonnet / high | P2-a | 013, 015 |
| 022 | FR-08: Generate Profile button and BFF trigger | 🔲 | FULL | sonnet / high | P2-a | 013 |
| 023 | FR-11 server: make `ExistingDocumentId`/`IsNewVersion` real | 🔲 | FULL | opus / xhigh | — | 012 |
| 024 | FR-11 client: default to version; override routes link/graduate | 🔲 | FULL | opus / high | — | 023, 013 |
| 025 | FR-12: surface collision handling per the Spike-4 outcome | ⛔ | FULL | sonnet / high | — | **005** |
| 026 | FR-09: related-to record card honoring the two-slot model | 🔲 | FULL | sonnet / high | P2-b | 013 |
| 027 | FR-10: open the related record and the Document record | 🔲 | FULL | sonnet / high | P2-b | 003, 026 |

**Gate**: identified document saves as a version, not a duplicate row · override creates a linked copy · profile displays · record card opens the record.

---

## Phase 3 — Surfacing Spaarke

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 030 | FR-13: shared server-side creation service (**Matter**) | 🔲 | FULL | opus / xhigh | — | 012 |
| 031 | FR-13: Project creation completeness + QuickCreate routing | 🔲 | FULL | opus / high | — | 030 |
| 032 | **FR-16a: per-row authorization on the similarity surface** | 🔲 | FULL | opus / xhigh | — | none |
| 033 | FR-16b: Find view three-state gating and Run Index | 🔲 | FULL | sonnet / high | — | 032, 015, 013 |
| 034 | FR-16c: Find results, lazy-scroll, records bridge decision | 🔲 | FULL | sonnet / high | — | 033 |
| 035 | FR-14: Add To Do carrying document **and** related record | 🔲 | FULL | sonnet / high | P3-c | 013, 026 |
| 036 | FR-15: Send Email via Outlook with document + record links | 🔲 | FULL | sonnet / high | P3-c | 026 |
| 037 | FR-17: wire `quickSave` and `shareDocument` ribbon commands | 🔲 | FULL | sonnet / high | P3-c | 011, 010 |

**Gate**: a pane-created Matter is complete · Find returns permission-trimmed results with a **passing negative test**.

---

## Phase 4 — Parity, deploy, close

| # | Task | Status | Rigor | Tier / Effort | Group | Deps |
|---|---|---|---|---|---|---|
| 040 | FR-19: Outlook parity pass and capability-gating audit | 🔲 | FULL | sonnet / high | — | 033, 035, 036 |
| 041 | NFR-09: per-env Entra SPA redirects + deploy-workflow trigger | 🔲 | STANDARD | opus / high | — | 011 |
| 042 | Deploy the add-in and BFF; run UAT against the acceptance set | 🔲 | STANDARD | sonnet / high | — | 040, 041 |
| 090 | Project wrap-up, lessons learned, `/test-diet` gate | 🔲 | MINIMAL | sonnet / medium | — | 042 |

---

## Parallel execution groups

Dispatch each subagent at its POML's `<model-tier>` and `<effort>`. **Max 6 concurrent.** Verify the build between waves (root CLAUDE.md / project-pipeline Step 5): `dotnet build src/server/api/Sprk.Bff.Api/` if any `.cs` changed; `npm run build` in `src/client/office-addins` if any `.ts`/`.tsx` changed (**there is no `build:prod` here** — `build` is the production build).

| Wave | Tasks | Prerequisite | Notes |
|---|---|---|---|
| W0 | 001 | — | Serial. Sizes W2. |
| W1 | 002, 003, 004, 005 | — | 4 independent spikes. Can run alongside W0. |
| W2 | 006, 007, 008 | 001 | Disjoint directories. |
| W3 | 010, 011 | W2 · (011 needs nothing) | Disjoint: adapters vs manifest+webpack. |
| W4 | 012 | 002 | Serial — gated on Spike-1. |
| W5 | 013, 016 | 010, 012 | Disjoint: client wiring vs tests. |
| W6 | 014, 015 | 012 / 010 | Disjoint: server stamp vs tab shell. ⚠️ **014 appends to the same contract-test file as 016** — the W5→W6 ordering keeps them apart; do not co-schedule 014 and 016. |
| W7 | 020, 021, 022 | 013, 015 | Disjoint views. |
| W8 | 023 → 024 | 012 | **Strictly serial** — data-integrity change. |
| W9 | 026 → 027 | 013 / 003 | Serial. |
| W10 | 025 | **005** | ⛔ Blocked until Spike-4 closes. `parallel-safe=false`. |
| W11 | 030 → 031 | 012 | Serial. |
| W12 | 032 → 033 → 034 | — | **Strictly serial.** 032 is `parallel-safe=false`. |
| W13 | 035, 036, 037 | 013, 026, 011, 010 | Disjoint. |
| W14 | 040 | 033, 035, 036 | Serial. |
| W15 | 041 → 042 → 090 | 040 | Serial. Deploy is CI-only. |

### Goal-eligibility (`/goal` wave loop)

| Wave | Eligible | Reason |
|---|---|---|
| W2 (006–008) | ✅ | Machine-verifiable end-state (`npm run typecheck` clean), 3 well-specified low-ambiguity tasks. Condition: *"`npm run typecheck` in `src/client/office-addins` exits 0 and `npm run build` is green."* |
| W7 (020–022) | ✅ | 3 well-specified view tasks with closed acceptance sets. |
| W13 (035–037) | ✅ | 3 disjoint adaptation tasks. |
| W1 (spikes) | ❌ | Investigation — no machine-verifiable end-state; outcomes need operator judgment. |
| W8, W10, W12 | ❌ | Data-integrity / security / blocked. Never auto-loop. |
| W15 | ❌ | Deploy and irreversible. |

> The `/goal` evaluator is a **stopping-condition check, not a quality gate.** Step 9.5 (`code-review` + `adr-check`) authority is unchanged, and tasks are never auto-completed on goal achievement.

---

## Critical path

`001 → 002 → 012 → 013 → 023 → 024 → 026 → 040 → 042`

**Spike-1 (002) is the keystone.** If `document.url` is unusable on Word desktop for SPE files, FR-01's primary path fails and the FR-02 stamp becomes the only identity mechanism — which does not work for documents saved before this release. That outcome invalidates the sizing of 012, 013, 021, 023, 024, 026 and 027.

---

## High-risk items

| Task | Risk |
|---|---|
| **002** | Negative result cascades through most of Phases 1–2. Run first. |
| **005** | May reveal the add-in save path has no collision protection at all — a live data-loss finding, not a UX gap. |
| **010** | `HostAdapterFactory` has zero call sites and the "tested" `WordAdapter` uses the broken `body.getOoxml()` path. **Prescriptive order is load-bearing**: port `getCompressedFile()` first, verify Outlook, delete last. |
| **023 / 024** | Data integrity. NFR-08: editable documents MUST use link/graduate, never the immutable suppress path — suppress-forever collapses two distinct drafts into one record. |
| **032** | Security. The similarity engine currently trims by `tenantId` alone. Gates 033 structurally; if hardening proves large, cut Find from r1 rather than ship it unsafe. |
| **030** | New server-side creation service; blast radius beyond the add-in. Existing `Create*Wizard` components MUST NOT be modified. |

---

## Cross-project coordination

Run `/conflict-check` before **every** BFF PR.

| Project | Overlap |
|---|---|
| `spaarkeai-compose-r8` | The other `.docx` write path; `parallel-safe:false` across the Compose spine. ADR-049 governs. Never delete `docxBridge.ts`. |
| `unified-access-control-r2` | Task 094 (collision pre-flight — task 005/025) · task 095 (two-slot association — task 026). **Do not duplicate.** |
| `email-communication-intelligence-r2` | Authored the current add-in surface + both handoff docs. |

⚠️ **`ci-router.yml`, `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml` are FROZEN** under the shadow-comparison window (open 2026-08-27). `deploy-office-addins.yml` is editable — and **does not currently trigger on this branch** (only `master` and `work/SDAP-outlook-office-add-in`).

---

## Findings bound to tasks

Six discovery findings modify spec assumptions. Full detail in [`../plan.md`](../plan.md) §3.

| ID | One-line | Owning task |
|---|---|---|
| **F-a** | The shipped collision handling is on an upload path the add-in does not use | 005 → 025 |
| **F-b** | FR-16's similarity engine has **no per-row authorization** | 032 (gates 033) |
| **F-c** | No single endpoint returns similar documents *and* records | 034 |
| **F-d** | FR-11's `ExistingDocumentId` hook is inert on both sides | 023, 024 |
| **F-e** | FR-04 as written would regress the `.docx` save | 010 |
| **F-f** | `POST /api/office/save` has zero executing contract coverage | 016 |
| **F-g** | `sprk_event` does not exist on `sprk_document` (only `sprk_relatedevent`), yet shipped code authorizes and writes it — the coordination doc's "mappable set" is wrong on `event`, `todo` and `contact` | 026, 035 (premise corrected) · **fix owned by UAC-r2** |
