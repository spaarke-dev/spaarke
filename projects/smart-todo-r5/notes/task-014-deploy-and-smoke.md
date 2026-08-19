# Task 014 — Deploy schema+form + real-DV smoke (FR-04/FR-20)

> **Date**: 2026-08-16 · sonnet/high (run on opus) · FULL rigor (TEST-MODIFYING override). Environment: **spaarkedev1** (`https://spaarkedev1.crm.dynamics.com`, org `0c3e6ad9-ae73-f011-8587-00224820bd31`). Verified MCP + `pac` active profile both target this org before any write.
> **Status**: **DEPLOY COMPLETE + verified. Real-DV interactive smoke (steps 3–7) PENDING — requires a human in the MDA UI** (subgrid-create, PolymorphicPicker, visual glyph). Script handed to operator; records will be verified via MCP once created.

## Deploy delta discovered
- `sprk_priority` / `sprk_effort` choice columns (010): already live (confirmed via `describe sprk_todo`).
- `sprk_todo_regarding_presave` webresource (013): already deployed (`2ae21d81-…`).
- **`sprk_todo_score_onchange` webresource (011): NOT deployed** → deployed this task.
- To Do main form: RegardingResolver control + all regarding fields already present (013 verification); **presave + score OnLoad handlers NOT registered** → registered this task.

## What was deployed (steps 1–2) — all verified live
1. **Created webresource `sprk_todo_score_onchange`** (JScript, id `3f7f07ab-b799-f111-b8de-7ced8ddc4a05`) via Web API create, base64 content from `src/client/webresources/js/sprk_todo_score_onchange.js` (task 011). Entry point `Spaarke.SmartTodo.ScoreOnChange.onLoad`.
2. **Registered two OnLoad handlers on the To Do main form** (`eca59df4-1364-f111-ab0c-7ced8ddc4cc6`), added alongside the existing `RegardingRecordNumberHyperlink.onLoad` (preserved — no regression):
   - `Spaarke.SmartTodo.RegardingPreSave.onLoad` (lib `sprk_todo_regarding_presave`) — **closes task 013 criterion 4**.
   - `Spaarke.SmartTodo.ScoreOnChange.onLoad` (lib `sprk_todo_score_onchange`).
   Both `passExecutionContext="true" enabled="true"`; each self-wires its downstream events (OnSave / field OnChange) per the webresource contracts.
3. **Published** all customizations.

### ⚠️ Deploy mechanism note (important for future form tasks 030/031/032)
**Direct Web API PATCH of `systemform` is a silent no-op in spaarkedev1** — confirmed empirically: PATCH returns HTTP 200 but echoes the unchanged record (verified with both `formxml` and a `description` control test). Form edits MUST go through **solution import**. The working path used here:
1. Create temp unmanaged solution (Spaarke publisher `6aeef721-…`), add the form (componenttype 60) + both webresources (componenttype 61) via `AddSolutionComponent`.
2. `pac solution export --managed false` → unzip → literal-insert handlers/libraries into `customizations.xml` (anchor = existing hyperlink handler/library line, uniqueness asserted) → `Compress-Archive` repack.
3. `pac solution import --publish-changes --force-overwrite`.
4. Verify live `formxml` via Web API GET; delete temp solution (components stay applied).
Rollback artifacts in scratchpad: `customizations.original.xml`, `todo-form.original.xml`.

## Live verification (post-publish)
`GET systemforms(eca59df4…)?$select=formxml` shows the `<events>` block with **3 handlers** (hyperlink + presave + score) and `<formLibraries>` with **3 libraries**. `presave=True score=True hyperlink=True`.

## Real-DV smoke — PENDING (operator, in browser) — acceptance criteria 1–5,7 of task 014
Run in the MDA against spaarkedev1, then tell me the created To Do record name(s)/GUID(s) and I verify each via MCP:

**Path A — subgrid auto-detect**: open a **Matter** record → To Dos subgrid → **+ New To Do** → set Name, then **Priority=Urgent, Effort=Low** → **Save**. Expect: RegardingResolver auto-detects the parent Matter.

**Path B — manual pick**: open the To Do grid/app → **New** To Do (not from a subgrid) → in the RELATED RECORD (RegardingResolver) control, pick a **different** parent type (e.g. a Project) → set **Priority=Low, Effort=Very High** → **Save**.

**On-form score check** (both): after picking Priority/Effort, confirm `Priority Score`/`Effort Score` fields update live (Urgent→100, Low-effort→25; Low-priority→25, Very-High-effort→100) — proves the ScoreOnChange handler fired.

**Regression**: open an existing pre-R5 To Do record → form loads with no script error (check browser console for `[SmartTodo.*]` logs, no red errors).

I will then MCP-query each created record to confirm `sprk_regardingrecordtype/id/name/number/url` + the correct `sprk_regarding{Entity}` lookup + `sprk_priorityscore/sprk_effortscore`, and check field-mapping inheritance. Evidence appended here on completion.

## Quality gates (Step 9.5)
No NEW repo source authored by 014 (the deployed JS was authored+reviewed in task 011; the form is live config, not in-repo). ADR spot-check done inline: ADR-024 resolver pattern intact (canonical RegardingResolver, entity="sprk_todo", no AssociationResolver introduced); score OnChange handler is the ADR-006 **Path A** exception already documented in spec.md ADR Tensions (task 011). No new BFF surface (BFF=N).
