# Decisions — Reuse Governance Tightening R1

> Full decision log. The project `CLAUDE.md` keeps the one-line binding outcome; rationale lives here or in `design.md` §11.

| Date | Decision | Source |
|---|---|---|
| 2026-11-10 | OQ-1..OQ-16 decided | design.md §11 |
| 2026-11-10 | Fable ROI review trims applied; cuts recorded | design.md §10.7 |
| 2026-11-10 | SpaarkeAi hot-path corrected N → Y; XML declaration added to design.md | /design-to-spec Owner Clarifications |
| 2026-11-10 | PR shape: items 1–3 one PR; item 4 own PR(s); item-5 sub-items own PRs; one adversarial pass over 1–4 | spec.md NFR-05 |
| 2026-11-10 | Dry-run pass rule (both caught → go; one missed → owner decides on `medium`; both missed → no-go) | spec.md FR-03 |
| 2026-11-10 | Item 4 execution waits on owner approval per package, also in autonomous mode | spec.md FR-08 |
| 2026-11-12 | Dry-run case 1 = nav-prop discovery (field mapping run as unscored 1b); result go (both caught at `low`); owner answered "Go" — tasks 011 and 012 proceed at `low` | task 010, `notes/reuse-scan-dry-run.md` |
| 2026-11-13 | Item 4 decisions: owner approved all 9 recommendations ("Approve all") — consolidate sdap-client and document-operations into ui-components, ai-outputs into ai-widgets, ai-context types into ai-widgets (retire), retire the legal-workspace facade; sanction compose-components; sanction smart-todo / communication / daily-briefing through one ADR-012 Path B amendment for Pattern D feature packages (overturns design §6.2). r1 executes 031 ai-context, 032 legal-workspace, 033 document-operations; 034 files the rest | task 030, `notes/package-reviews.md` |
