# Current Task State — spaarke-daily-update-service-r5

> **Last Updated**: 2026-07-09 (by context-handoff, pre-compaction)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarke-daily-update-service-r5`, pushed to origin @ `ed6c513ae`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | spaarke-daily-update-service-r5 (26 tasks; **19 done**) |
| **Branch** | `work/spaarke-daily-update-service-r5` — merged to master once (PR #590, e5e43c97c); **6 post-merge commits local, NOT pushed**: `c3b49e46a` `3390067b3` `585d9ceab` `e76ee15ce` `634bf64a9` `78ccb9c5b` |
| **Status** | ALL executable code/eval/data tasks DONE. This session: 002, 012 (+live MCP retirement), 036, 037, 015, 031, 032 (MCP restore), 016. Dataverse MCP confirmed authorized. |
| **Next Action** | Only operator/live-env tasks remain: **017/024/038** (Azure deploy + browser UAT on spaarkedev1), **022** (operator harness sign-off), **021/023** (design — done via live loop, formalize at wrap), **090** (wrap, deps deploys). None doable autonomously here. Decide: push the 6 commits to origin? Re-merge to master? |

### ✅ Done ledger (TASK-INDEX): 001,002,010,011,012,013,014,015,016,020,030,031,032,033,034,035,036,037,040 (19). 🔲 remaining: 017,021,022,023,024,038,090 (all need operator/live env).

### Session progress (post-merge hardening)
- **002 ✅** (@odata.bind audit): report `notes/odata-bind-audit.md`; 0 fixes (only provable violation is operator-deferred EventDetailSidePane), 1 deferred, 10 needs-verification (recorded not guessed — no live Dataverse metadata). Escalation: casing inconsistencies (`sprk_outputtypeid` vs `sprk_OutputTypeId`) need metadata pass → suggest /defer at 090.
- **012 ✅** (retire BRIEF-NARRATE-CHANNEL): const + comment cleared (grep-zero under src/), scope-index entry removed, composite description fixed. Build 0 err; 61+223 tests green; publish 45.13 MB. **DEFERRED**: live Dataverse Action-row retirement (id dc3533c0-…) → Phase A deploy task 017 (MCP unauthorized this session). Notes: `notes/012-channel-action-retirement.md`.
- Committed `c3b49e46a` (local; not pushed).

### ⚠️ Pending decision — `/merge-to-master`
Operator asked to commit → push (both DONE) → merge. Before merging, they must weigh: **master auto-deploys**, and this branch is a **mid-project checkpoint** (11/26 tasks) with a **partially-tested-only redesign** (reviewed in the `/prototype` harness with MOCK data, **no browser UAT on spaarkedev1** — which the project's own gate rule requires). Merge is defensible (accuracy verified, redesign cohesive) but ships un-UAT'd. **Get explicit go before running `/merge-to-master`.**

### Critical context
- **Accuracy headline COMPLETE + verified**: deterministic item rows (011), deterministic TL;DR facts (013, proven by prompt-capture test), binary anchor resolution (014, ADR §6.5 Path-C: server-side itemRefs), collector de-dup (034), collaborator-scope fix (033). 58/58 DailyBriefing server tests.
- **Standalone-build fix LANDED ON MASTER** (PR #584, superseded #506) — re-armed the CI gate; permanent. Our branch synced from master.
- **UI redesign DONE** (task 021/023, live-iterated in harness, operator-approved each step): StatTiles KPI row, "Today's summary" (was TL;DR) above Critical Today, Critical Today cards w/ severity left-accent + soft tint word-only pills, merged "Tasks" section (Overdue+Upcoming) with per-row status pills, clean hover rows, Add-to-ToDo moved into ⋮ menu (position 4), 20px MDA-matching title. Committed `80d7b42b2` + `ed6c513ae`.

---

## Full State

### Tasks (11/26 done — see tasks/TASK-INDEX.md)
✅ 001, 010, 011, 013, 014, 020, 030, 033, 034, 035, 040
🔲 Remaining: **002** (odata grep audit, dep 001✅), **012** (retire BRIEF-NARRATE-CHANNEL — narrator.cs + catalog), **016** (eval family — mixed-item corpus, opus, dep 011/013/014✅), **031** (jps-validate Step 7.7 — `.claude/` MAIN-SESSION only), **032** (fieldmapping sweep, dep 030✅), **036** (collapse QueryHighPriority*, collector), **037** (primary-contact cache, collector, dep 036), **038** (Phase B deploy/UAT), **017** (Phase A deploy/UAT), **024** (Phase D deploy/UAT), **090** (wrap-up: /test-diet + /defer). Design tasks 021/022/023 effectively done via the live loop — formalize status at wrap-up.
- Serialization: collector chain 036→037; 013 already done. `.claude/` tasks (031) main-session only.

### Design redesign — files changed (committed, pushed)
`src/client/shared/Spaarke.DailyBriefing.Components/src/components/`: StatTiles.tsx (NEW), DailyBriefingApp.tsx (order flip + deterministic tile counts + wiring), DigestHeader.tsx (20px title, date+items, news icon), TldrSection.tsx ("Today's summary", hero card, top-action callout — preserves 014 anchor logic), HighPrioritySection.tsx (Critical Today cards, severity accent, word-only tint pills), ChannelHeading.tsx (count chip), ActivityNotesSection.tsx (merge Overdue+Upcoming → "Tasks" + dueStatus), NarrativeBullet.tsx (clean rows, dueStatus pill, Add-to-ToDo → menu pos 4), components/index.ts (StatTiles export). Tests: HighPrioritySection.badges.test.ts, NarrativeBullet.test.tsx (updated to menu UX).

### Verification
- Client jest: **187 pass**, 7 skipped, **1 pre-existing fail** (`ActivityNotesSection.callbacks` onKeep TTL fixture) + `legalWorkspaceSectionRegistry` suite module error — BOTH pre-existing/unrelated. My change FIXED 2 pre-existing fails (menu now matches canonical FR-18 order).
- Typography audit: Fluent v9 tokens only = Power Apps design system (Segoe UI, type ramp, semantic colors #242424=colorNeutralForeground1, weight tokens). No hard-coded fonts/colors.

### Open caveats / follow-ups (address before production)
1. **StatTiles Overdue/New-matters counts** derived via category regex (`/overdue/`, `/matter/`) against render data — VERIFY the real server's channel category names match before production (Open items + Critical are always accurate). Ideal: server provides explicit counts. Flag at deploy/UAT (017).
2. **"Tasks" per-row pill** status derived from SOURCE channel (overdue→Overdue, upcoming→Due soon) — finer "Due today" needs a server per-bullet `dueStatus` field (deterministic, like ClassifyAction). Harness demonstrates the design.
3. **Live BRIEF-NARRATE-TLDR Dataverse row** prompt not PATCHed (mirror JSON updated by 013) → bundle with task 016 UAT.
4. **Harness** lives in `spaarke-prototype` repo, branch `feature/uat-harness-framework` (projects/daily-briefing-r5-uat) — files uncommitted there. Run: `cd c:/code_files/spaarke-prototype/projects/daily-briefing-r5-uat; $env:SPAARKE_REPO_ROOT="c:/code_files/spaarke-wt-spaarke-daily-update-service-r5"; npm run dev` → localhost:5174.

### Coordination
r2-core (`spaarke-ai-architecture-redesign-r2`) owns `Services/Ai/` engine internals; r5 owns `Narrators/DailyBriefing*` + `Nodes/UpdateRecordNodeExecutor`. Run `/conflict-check` before BFF waves. Registered in `projects/INDEX.md`.
