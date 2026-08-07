# Task Index — spaarkeai-assistant-enhancements-r2

> **Generated**: 2026-08-05 via /project-pipeline
> **Total**: 27 tasks (23 implementation/deploy + 1 wrap-up across 5 workstream phases)
> **Phasing**: E → A → B → D → C (owner-accepted)

## Legend
Status: 🔲 not-started · 🔄 in-progress/needs-retry · ✅ completed (owner E2E for A+B cleared 2026-08-06) · ✅* deployed + smoke-verified, owner E2E verification pending (see notes/a-deploy-verify.md)
Tier: `sonnet` (default) · `opus` (high-blast/judgment) — effort: `high` (default) · `xhigh` (hard-but-specified)

## Tasks

| ID | Title | Phase | Status | Deps | Tier / Effort | Rigor | Parallel |
|----|-------|-------|--------|------|---------------|-------|----------|
| 001 | Remove Notifications suggestion surface (E) | 1 E | ✅ | none | sonnet / high | FULL | none (ConvPane spine) |
| 002 | Deploy + verify E | 1 E | ✅ | 001 | sonnet / high | STANDARD | none (deploy) |
| 010 | active_widget_changed subscriber + focus ref (FR-A1) | 2 A | ✅ | 001 | sonnet / high | FULL | none (ConvPane spine) |
| 011 | activeContext focus-stamp decorate (FR-A2) | 2 A | ✅ | 010 | sonnet / high | FULL | none (ConvPane spine) |
| 012 | Server: thread activeContext + prefer focus-stamp (FR-A3/A4) | 2 A | ✅ | 001 | **opus / xhigh** | FULL | Group A (BFF) |
| 013 | Deploy + verify A | 2 A | ✅ | 011,012 | sonnet / high | STANDARD | none (deploy) |
| 020 | Closed contextType set on widget metadata (FR-B1/C3) | 3 B | ✅ | 001 | sonnet / high | FULL | Group B1 (shared-lib) |
| 021 | Context-type tag column + BFF contract + seed + Reanalyze binding (FR-B2/D11) — **Option C** | 3 B | ✅ | 020 | sonnet / high | **FULL** | none (live schema + BFF deploy) |
| 022 | Grounded suggest turn (BFF suggest path + client), ≤3 content-specific chips per tab (FR-B3/B5) — **Option B** | 3 B | ✅ | 020,021 | **opus / xhigh** | FULL | none (BFF + ConvPane spine) |
| 023 | Manual refresh-suggestions affordance (FR-B4) | 3 B | ✅ | 022 | sonnet / high | FULL | none (ConvPane spine) |
| 024 | Dev-visible proactive-selection trace (FR-B6) | 3 B | ✅ | 022 | sonnet / high | STANDARD | none (ConvPane spine) |
| 025 | Deploy + verify B | 3 B | ✅ | 022,023,024 | sonnet / high | STANDARD | none (deploy) |
| 030 | Awaited messages[0] Cosmos write (FR-D2) | 4 D | ✅ | 001 | sonnet / **xhigh** | FULL | Group D-srv-a (persistence) |
| 031 | 404-on-missing history contract (FR-D3) | 4 D | ✅ | 001 | sonnet / high | FULL | Group D-srv-a (ChatEndpoints) |
| 032 | Stored title + rename endpoint + title-gen (FR-D4) | 4 D | ✅ | 031 | sonnet / high | FULL | none (ChatEndpoints, after 031) |
| 033 | Retention TTL spike + implement (FR-D10) | 4 D | ✅ (safe per-doc TTL path; filed→ttl=-1, warm-reload durability fix) | 030 | **opus / xhigh** | FULL | none (persistence, after 030) |
| 034 | "Set related record" rename + prompt (FR-D9) — **Path B** (owner 2026-08-06): server-side ADR-024 regarding write on `sprk_analysis` (runtime `DataverseServiceClientImpl`) + relax doc-anchor + self-contained client picker. Indep ADR-024 review (core write PASS); W1-W5 fixed (dropped non-deployed `sprk_regardingrecordurl` → reduced set, §6.5 Path A; 7 domain tests for the write; resolver-dup Path A doc'd; parity fail-fast) | 4 D | ✅ | 037 | **opus / high** | FULL | none (AnalysisEndpoints + Spaarke.Dataverse + HistoryOverlay) |
| 035 | Route History through rich restore + clear/remount (FR-D1) | 4 D | ✅ (clear-before-restore in WorkspacePane; overwrite-hazard + marker-leak fixed; 2 indep reviews) | 031 | **opus / xhigh** | FULL | none (ConvPane spine) |
| 036 | Rehydrate attachment chip on restore (FR-D5) — **re-scoped full paired slice** (owner 2026-08-06): BFF restore-DTO projection (`uploadedFiles`) + ConvPane host-owned rehydrate (SprkChat seam AVOIDED — parallel `FilesAttachedIndicator` render; shared lib untouched) | 4 D | ✅ done | 035 | sonnet / high | FULL | none (BFF restore DTO + ConvPane) |
| 037 | HistoryOverlay rebuild: menu/preview/grouping/search (FR-D6/7/8) | 4 D | ✅ (UI complete; FR-D7 data needs BFF projection → DI-01, fold into 039) | 032 | sonnet / high | FULL | Group D-client-b (HistoryOverlay) |
| 038 | Reanalyze chip on document context (FR-D11) | 4 D | ✅ (deterministic seed on document-tab focus + `getAppendedLocalChips` persistence, dispatch via `chips.dispatchBinding`; found+fixed a mid-dispatch clobber bug in review) | 021,022 | sonnet / high | FULL | none (ConvPane spine) |
| 039 | Deploy + verify D | 4 D | 🔲 | 030-038 | sonnet / high | STANDARD | none (deploy) |
| 040 | Email variant in SerializedWidgetState + guard (FR-C2 client) | 5 C | ✅ | 001 | sonnet / high | FULL | Group C1 (shared-lib) |
| 041 | Email variant in WorkspaceTabVisibleState + derive/format (FR-C2 server) | 5 C | ✅ (visible-state shape 041 + persisted `EmailTabWidgetData` carrier + `TryDeriveVisibleState` producer 041b — Path 1 per owner; committed 580cbda48) | 001 | sonnet / high | FULL | Group C1 (BFF) |
| 042 | getAgentVisibleState on email widget + eml-render (FR-C1/C4) | 5 C | ✅ **FR-C1** (carrier 042a/c @580cbda48 + producer 042b @94955e609 — email tab populates widgetData) · ✅ **FR-C4** via 042c-fr-c4 **B1** @95f936cdb (owner-chosen §6.5 Path A: additive SprkChat host-send seam + one-shot documentId decorate + email-summarize chip + focus-stamp fix) | 040 | **opus / xhigh** | FULL | Group C2 (email widget) |
| 043 | Deploy + verify C | 5 C | 🔲 | 040,041,042 | sonnet / high | STANDARD | none (deploy) |
| 090 | Project wrap-up (gates, test-diet, cleanup, docs) | 9 | 🔲 | 002,013,025,039,043 | sonnet / high | FULL | none (final) |

## Dependency notes / critical path

**The `ConversationPane.tsx` sequential spine** is the dominant constraint: 001 (E), 010/011 (A), 022/023/024 (B), 035/036/038 (D) all edit it and run **strictly sequentially** relative to each other. BFF concerns, `HistoryOverlay.tsx` (037), shared-lib types (020, 040), and catalog data (021) parallelize *alongside* that spine.

**Critical path** (longest chain): `001 → 031 → 032 → 037 → 034 → 039 → 090` (Phase 4 D is the largest workstream). Parallel BFF chain `001 → 030 → 033 → 039` and client chain `001 → 031 → 035 → 036 → 039` run alongside.

**BFF file-overlap serialization**: 031 & 032 both edit `ChatEndpoints.cs` → serial (032 after 031). 030 & 033 both edit `SessionPersistenceService.cs` → serial (033 after 030). 037 & 034 both edit `HistoryOverlay.tsx` → serial (034 after 037).

## High-risk items
- **035** (FR-D1) — tab-restore overwrite hazard; clear/remount first + regression test. opus/xhigh.
- **033** (FR-D10) — Cosmos retention/TTL; spike-first, data-loss blast radius. opus/xhigh.
- **012** (FR-A4) — ADR-015 Path A privacy boundary (active content-visible; background metadata-only). opus/xhigh.
- **042** (FR-C1) — email compact shape lives in `useEmailWorkspaceRecord`, not the widget wrapper. opus/xhigh.

## Cross-worktree coordination
- **No live overlaps.** `spaarke-notification-spine-r1` and `ai-advanced-capabilities-analysis-hub-r1` are **both fully merged into master** (verified 2026-08-05: 0 unmerged commits each; `projects/INDEX.md` rows are stale — worktrees not yet archived). R2 fast-forwarded to master, so their changes are already in this branch. Task 001 (E) removes the suggestion surface (`useSuggestionCards.tsx`, present in HEAD) from a **known static state** — no merge-order coordination needed.
- Run `/conflict-check` before BFF (`Services/Ai`) / `ConversationPane` PRs as normal hygiene, but no specific in-flight peer is expected.

## Parallel Execution Plan

Waves respect the ConversationPane spine + BFF file-overlaps. Most waves are 1–2 tasks (the spine + file-overlaps prevent large fan-out), so **no wave is `/goal`-eligible** (needs ≥3 well-specified, low-ambiguity, non-security/deploy tasks — this project's waves don't qualify; dispatch normally with a per-task "continue").

| Wave | Tasks | Prereq | Concurrency | goal-eligible |
|------|-------|--------|-------------|---------------|
| E1 | 001 | — | 1 | NO (single task; ConvPane spine) |
| E2 | 002 | 001 | 1 | NO (deploy) |
| A1 | 010, 012 | 001 | 2 (diff files: ConvPane ∥ BFF) | NO (2 tasks; 012 ADR-015 boundary) |
| A2 | 011 | 010 | 1 | NO (single; ConvPane spine) |
| A3 | 013 | 011,012 | 1 | NO (deploy) |
| B1 | 020 | 001 | 1 | NO (single; shared-lib) |
| B2 | 021 | 020 | 1 | NO (single; catalog data) |
| B3 | 022 | 020,021 | 1 | NO (single; ConvPane spine) |
| B4 | 023 | 022 | 1 | NO (single; ConvPane spine) |
| B5 | 024 | 022 | 1 | NO (single; ConvPane spine) |
| B6 | 025 | 022,023,024 | 1 | NO (deploy) |
| D1 | 030, 031 | 001 | 2 (persistence ∥ ChatEndpoints) | NO (2 tasks; BFF) |
| D2 | 032, 033 | 031 / 030 | 2 (ChatEndpoints ∥ persistence) | NO (2 tasks; 033 data-loss risk) |
| D3 | 035, 037 | 031 / 032 | 2 (ConvPane ∥ HistoryOverlay) | NO (2 tasks; 035 overwrite hazard) |
| D4 | 036 | 035 | 1 | NO (single; ConvPane spine) |
| D5 | 038 | 021,022,036 | 1 | NO (single; ConvPane spine) |
| D6 | 034 | 037 | 1 | NO (single; HistoryOverlay + AnalysisEndpoints) |
| D7 | 039 | 030-038 | 1 | NO (deploy) |
| C1 | 040, 041 | 001 | 2 (shared-lib ∥ BFF) | NO (2 tasks) |
| C2 | 042 | 040 | 1 | NO (single; email widget) |
| C3 | 043 | 040,041,042 | 1 | NO (deploy) |
| WRAP | 090 | all deploys | 1 | NO (final gate task) |

**How to execute a multi-task wave:** confirm prereqs are ✅, then send ONE message with multiple `task-execute` Skill invocations (one per task) dispatched at each task's `<model-tier>` + `<effort>`. Wait for all to complete, run a build check between waves (BFF: `dotnet build src/server/api/Sprk.Bff.Api/`; SpaarkeAi: `npm run build`), then advance.

## Success criteria trace (spec §Success Criteria)
1 → 010/011/012/013 · 2 → 020/021/022/025 · 3 → 040/041/042/043 · 4 → 035/036/039 · 5 → 030/039 · 6 → 037/039 · 7 → 034/033/039 · 8 → 001/002 · 9 → all BFF tasks (012,030,031,032,033,034,041 + deploys).
