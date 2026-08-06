# Current Task State - spaarke-modal-system

> **Last Updated**: 2026-08-05 (by context-handoff — project CLOSED, final checkpoint)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | none — **PROJECT CLOSED (2026-08-05)**: original 28/29 ✅ + ALL follow-ons resolved (#712–#717, #719, #720, #713 all closed) |
| **Step** | Terminal. Worktree = branch = origin = master = main repo, all @ `f114f68f9`. Zero uncommitted / unpushed / unmerged / behind. |
| **Status** | closed — merged + deployed + verified |
| **Next Action** | NONE in this project. Sole open issue anywhere: **#730** (scheduled briefing email needs per-item deep links + link back to the Daily Briefing — SERVER-side template, email/briefing owner; requirements on the issue). This worktree can be retired or reused. |

### Critical Context
Six PRs carried the full arc to master: **#718** (modal system, 26 commits), **#722** (#713: EmailComposer `hostOwnsChrome` seam + SendEmailDialog on SprkModal + legacy dialog deleted + DailyBriefing on canonical process + linked briefing share), **#731** (hotfix: committed conflict markers ×5 files from shared-branch racing), **#734** (#712 LW `@spaarke/ai-outputs` dep + #716 DocumentOperations copy reconciliation), **#736** (#717-1: file-preview Open-record → Layout-1 record modal), **#737** (#714 FindSimilar viewer→`FindSimilarViewerDialog` + #715/#717-2 dead-folder deletions + LW vite alias/react-icons fixes). Master CI auto-deploys `sprk_spaarkeai` on every merge (verified success at `0cdc67c5a` + `f114f68f9`) — the clobber loop is permanently self-healing.

---

## Full State (Detailed)

### Deployed on SPAARKE DEV 1 (`spaarkedev1.crm.dynamics.com`) — all org-verified
- **PCFs**: CommunicationConversationPanel **1.16.0** · SemanticSearchControl **1.1.80** · CommunicationConnections **1.6.4** · RegardingResolver **1.4.8** · VisualHost **1.4.38** · RelatedDocumentCount **1.21.4**
- **Pages/WRs**: `sprk_spaarkeai` (via CI from master now), `sprk_emailpage`, `sprk_dailyupdate`, `sprk_smarttodo`, `sprk_speadmin`, `sprk_eventdetailsidepane`, `sprk_findsimilar`, `sprk_documentuploadwizard`, all 8 Create/Summarize wizards, `sprk_wizard_commands`, `sprk_DocumentOperations.js` v1.28.0 (org runs the src lineage; repo copies byte-identical per #716)
- Solution ZIPs for re-import elsewhere: each PCF's `Solution\bin\`

### What the system is now
One `SprkModal` shell + 6 presets everywhere (WizardShell = sole intentional exception, §11-G). Email composer fully on the shell (`hostOwnsChrome` seam; legacy `components/SendEmailDialog` DELETED; briefing share = prefilled canonical composer with per-item record deep links). Expand glyph = `ExpandUpRight`/`ContractDownLeft` (matches OOB). OOB sizing via `oobModalSizes.ts` + two hubs. Browse "1 of N" = `BrowseModal` (shell `nav` prop; `onBeforeNavigate` seam per amended ADR-050). `FindSimilarViewerDialog` (viewer) vs `FindSimilar/` wizard family — name collision ended. Display-size toggle removed from embedded SpaarkeAi (auto-4K breakpoint kept; `DisplaySizeMenu` dormant for standalone).

### Known residuals (cosmetic, unscheduled — noted on closed issues)
1. Viewer's internal dead `embedded` render path (zero callers) — `FindSimilarViewer/FindSimilarViewerDialog.tsx`.
2. RelatedDocumentCount `hooks/__tests__/useRelatedDocumentCount.test.ts` pre-existing failure (needs a `@spaarke/auth` manual mock; stale vs current auth contract).
3. LW dead code beyond what was deleted may remain (LW = library for SpaarkeAi; standalone page retired).

### Environment facts (for any future session)
- Master CI deploys `sprk_spaarkeai` on EVERY master merge — deploy-from-branch gets clobbered; merge first, CI carries it.
- LW now builds in fresh worktrees (#712 fixed: dep + vite alias; react-icons pinned ^2.0.326).
- Fresh installs: `npm install --legacy-peer-deps --no-audit --no-fund` (NEVER npm ci); PCF deploys: `npm run build:prod` + 5-location version bump + pack.ps1 + `pac solution import` (pac auth: spaarkedev1).
- Shared-branch racing hazard: other sessions pushed to `work/spaarke-modal-system` during 08-04/08-05 (caused the marker hotfix + a dropped-commit recovery). One session per worktree.
- Portfolio: project never board-registered — `/devops-project-sync` no-ops by contract.

### Docs/ledger
`notes/success-criteria-verification.md` · `notes/defer-issues.md` (DEF-001–008, all issue-linked, all closed except #730) · `notes/lessons-learned.md` · `tasks/TASK-INDEX.md` · ADR-050 (authored + Path-B amended) · `docs/standards/MODAL-DESIGN-SYSTEM.md`.
