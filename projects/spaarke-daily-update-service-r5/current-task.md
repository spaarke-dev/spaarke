# Current Task State — spaarke-daily-update-service-r5

> **Last Updated**: 2026-07-09 (by context-handoff, pre-compaction)
> **Recovery**: Read "Quick Recovery" first. Branch `work/spaarke-daily-update-service-r5`, pushed to origin @ `ed6c513ae`.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|---|---|
| **Project** | spaarke-daily-update-service-r5 (26 tasks; **11 done + full UI redesign done**) |
| **Branch** | `work/spaarke-daily-update-service-r5` — pushed to origin (HEAD `ed6c513ae`) |
| **Status** | Design loop complete; **awaiting operator go/no-go on `/merge-to-master`** |
| **Next Action** | Operator decides: run `/merge-to-master` (see ⚠️ caveats below) OR continue remaining tasks (012/016/031/032/036/037/038/002/017/024/090) |

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
