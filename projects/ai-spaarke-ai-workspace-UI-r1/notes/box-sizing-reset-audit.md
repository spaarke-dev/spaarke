# Box-Sizing Reset Audit — Code Page index.html Files

> **Date**: 2026-06-09 (iter-2 round 11 debrief)
> **Owner**: TBD (follow-up — backlog item for next UI hardening pass)
> **Context**: The 11-round DataGrid horizontal-scroll saga in this project
> uncovered that **17 of 23** Code Page `index.html` files lacked the
> `*, *::before, *::after { box-sizing: border-box }` reset. The reset is
> documented as REQUIRED in `docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md`
> §2 since 2026-06-04 (task 035 UAT iteration 3), but it was never propagated
> back to existing surfaces.

## The audit (run from repo root)

```bash
for f in src/solutions/*/index.html; do
  if grep -q "box-sizing" "$f"; then
    echo "OK   $(basename $(dirname $f))"
  else
    echo "MISS $(basename $(dirname $f))"
  fi
done | sort
```

## Results as of 2026-06-09

### OK (6 surfaces — have the reset)

- DocumentUploadWizard
- EventsPage
- LegalWorkspace
- SpaarkeAi  *(added 2026-06-09 round 11; this project's primary fix)*
- sprk_invoicespage
- sprk_kpiassessmentspage

### MISS (17 surfaces — REQUIRE the reset before hosting DataGrid or wide content)

| Surface | Hosts DataGrid? | Priority |
|---|---|---|
| AllDocuments | yes (planned) | HIGH |
| Reporting | yes (planned/likely) | HIGH |
| SmartTodo | indirectly (Kanban → no DataGrid; refactor in smart-todo-r4 may add) | MEDIUM |
| DailyBriefing | no (digest UI) | LOW — fix preemptively |
| CalendarSidePane | no (side pane) | LOW |
| EventDetailSidePane | no (side pane) | LOW |
| TodoDetailSidePane | no (side pane) | LOW |
| FindSimilarCodePage | no (similarity dialog) | LOW |
| PlaybookLibrary | no (card grid) | LOW |
| SpeAdminApp | no (admin UI) | LOW |
| CreateEventWizard | no (wizard) | LOW |
| CreateMatterWizard | no (wizard) | LOW |
| CreateProjectWizard | no (wizard) | LOW |
| CreateTodoWizard | no (wizard) | LOW |
| CreateWorkAssignmentWizard | no (wizard) | LOW |
| SummarizeFilesWizard | no (wizard) | LOW |
| WorkspaceLayoutWizard | no (wizard) | LOW |

## The fix per surface

Add to the `<style>` block at the top of each `index.html`:

```html
*, *::before, *::after {
  box-sizing: border-box;
}
```

Reference: `src/solutions/SpaarkeAi/index.html` (round 11 implementation).

## Why not fixed in this PR

This project (ai-spaarke-ai-workspace-UI-r1) focused on brittleness +
iter-2 testing-feedback work. Modifying 17 unrelated surfaces' index.html
would:

1. Expand the PR scope far beyond the project charter.
2. Trigger re-deploys of 17 Code Pages (production-touching).
3. Risk introducing regressions in surfaces that work today.

The proper fix is a dedicated hardening pass that:
- Adds the reset to each surface
- Rebuilds + redeploys each via its existing `Deploy-*.ps1` script
- Smoke-tests each surface post-deploy

## Suggested follow-up backlog item

> **Title**: Backfill box-sizing reset on 17 Code Page index.html files
>
> **Description**: Per the audit in
> `projects/ai-spaarke-ai-workspace-UI-r1/notes/box-sizing-reset-audit.md`,
> 17 Code Page surfaces are missing the `box-sizing: border-box` reset
> required by `DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §2. The DataGrid
> framework now has a defensive `visibleColumns.length × 24px` reserve
> (round 11) that masks the issue in many cases, but the reset is still
> the correct primary fix. Add the reset to each, rebuild, redeploy, smoke-
> test. HIGH-priority surfaces first (those that host DataGrid).
>
> **Acceptance**: Re-running the audit script prints `OK` for all 23 hosts.

## Long-term prevention

Two options worth considering when capacity allows:

1. **CI gate** — extend the `tsc-surface-gate.mjs` script to also check that
   `src/solutions/*/index.html` contains the reset. Block the surface's
   build if missing.
2. **Template enforcement** — the
   `templates/spaarke-codepage-with-datagrid/` template already has the
   reset. Wire a `npm create` / `npx degit` workflow that scaffolds new
   surfaces from this template instead of copy-pasting an existing one.
