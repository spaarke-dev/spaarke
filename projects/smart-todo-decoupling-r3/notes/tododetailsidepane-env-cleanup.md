# TodoDetailSidePane — Target Environment Cleanup Procedure

> **Status**: Manual procedure for the deploy team
> **Date**: 2026-06-08
> **Author**: smart-todo-decoupling-r3 / task 081
> **Companion to**: `tododetailsidepane-decision.md` (task 080 audit + RETIRE recommendation)
> **Purpose**: Document the Dataverse-side cleanup steps required to fully retire the `TodoDetailSidePane` solution and `sprk_tododetailsidepane` web resource in each target environment. **Task 081 deletes the source tree + build manifests** — Dataverse-side cleanup is a manual step the deploy team performs per environment.

---

## 1. Scope

Per the audit (task 080), the `TodoDetailSidePane` solution has **zero hard consumers** in `src/`. No ribbon JS, no sitemap entry, no Code Page launcher, no plugin, no BFF endpoint references it. The R2 SmartTodo consolidation already replaced the launch path with an inline `TodoDetailPanel`. The source tree was deleted in task 081; this doc lists the per-environment cleanup steps so the deployed artefacts disappear too.

**Applies to every environment where the solution was previously imported**:
- Dev
- Test / QA / UAT (whichever apply)
- Production

---

## 2. Pre-cleanup verification (per environment)

Before deleting anything, confirm the artefacts exist and capture a defensive export.

### 2.1 Check whether the solution is present

Using PAC CLI (replace `<env>` with the target environment URL):

```powershell
pac auth create --environment "<env>"
pac solution list | Select-String -Pattern "TodoDetailSidePane"
```

- If **no match**: solution is already gone — skip to §4.
- If a match is found: continue with §2.2.

### 2.2 Defensive export

Export the current state of the solution before deleting. This preserves a snapshot in case rollback is ever needed (it should not be — there are no consumers).

```powershell
pac solution export --name "TodoDetailSidePane" --path ".\backups\TodoDetailSidePane_$(Get-Date -Format yyyyMMdd_HHmmss).zip" --managed false
```

Store the resulting zip in a safe location (e.g., team OneDrive or the deployment artefacts bucket). Tag it with the environment name and date.

### 2.3 Confirm no managed consumers in this environment

In the maker portal (or via `pac solution online-list`), check:

- Open the target environment's **Solutions** list
- Filter for any solution that has `sprk_tododetailsidepane` as a dependency
- Expected: only the `TodoDetailSidePane` solution itself
- If another solution depends on it (unexpected per audit): **STOP** and escalate before proceeding. File a finding in `tododetailsidepane-decision.md` §8 (Unexpected findings).

---

## 3. Cleanup steps (per environment)

### 3.1 Uninstall the solution

The `TodoDetailSidePane` solution wraps the `sprk_tododetailsidepane` web resource. Deleting the solution (managed: uninstall; unmanaged: delete) removes the web resource record automatically if the solution owns it.

#### Managed install path

```powershell
pac solution delete --solution-name "TodoDetailSidePane"
```

#### Unmanaged install path

In the maker portal:
1. Navigate to **Solutions**.
2. Locate `TodoDetailSidePane`.
3. Click the row's overflow menu → **Delete**.
4. Confirm the prompt.

This removes the solution from the environment.

### 3.2 Verify the web resource is gone

After §3.1, the `sprk_tododetailsidepane` web resource should no longer be referenced by any solution in the environment. Confirm:

```powershell
pac solution online-list --solution-name "TodoDetailSidePane"
# Should return: solution not found.
```

In the maker portal under **Default Solution** → **Web resources**:
- Search for `sprk_tododetailsidepane`
- Expected: **no result**.

If the web resource lingers in the Default Solution (can happen if it was added to Default Solution by a maker at some point), delete it manually:
1. Open the web resource record.
2. Confirm no active consumers (formula references, ribbon JS imports).
3. Delete.

### 3.3 Verify no orphan ribbon / sitemap references

The audit confirmed zero ribbon or sitemap references at the source level. Re-verify at the environment level:

- **Ribbons**: Open any entity that historically might have launched the side pane (`sprk_event`, `sprk_todo` once created). Confirm no ribbon command references `sprk_tododetailsidepane`. Expected: none, per audit §3.1.
- **Sitemap**: Open the model-driven app sitemap editor. Confirm no area / group / subarea references the web resource. Expected: none.

If any reference is found (would be unexpected): file as a finding in `tododetailsidepane-decision.md` §8 before deleting, then update this doc to reflect the new fact.

---

## 4. Post-cleanup verification

Run the following confirmations after §3:

1. **Solution gone**: `pac solution list | Select-String "TodoDetailSidePane"` returns nothing.
2. **Web resource gone**: maker portal search for `sprk_tododetailsidepane` returns no result.
3. **Smoke test SmartTodo Code Page**: open `sprk_smarttodo` in the environment. Confirm the Kanban board loads, cards render, clicking a card opens the inline `TodoDetailPanel` (not a side pane). No console errors referencing `sprk_tododetailsidepane` or `todoDetailPane`.
4. **Smoke test LegalWorkspace** (if applicable in the environment): open a legal workspace, confirm the SmartToDo widget loads. No console errors.
5. **Smoke test EventsPage** (if applicable): open the Events Code Page. No "missing web resource" errors.

If any smoke test fails: do **not** roll back automatically. The defensive export from §2.2 can be re-imported if needed, but per the audit, the side pane had no production opener — a failure here likely indicates an environment-specific stale ribbon or sitemap edit, which §3.3 should have caught. Diagnose first.

---

## 5. Order of operations across environments

Recommended order:

1. **Dev** — execute §2–§4. Smoke-test SmartTodo + LegalWorkspace + EventsPage thoroughly.
2. **Test / QA / UAT** — execute §2–§4 in each. Smoke-test.
3. **Production** — execute §2–§4. Smoke-test during low-traffic window if possible.

Each environment is independent — the source tree was already deleted in task 081, so subsequent solution imports will no longer include `TodoDetailSidePane`. The per-environment cleanup is a one-time catch-up to remove the legacy deployed artefact.

---

## 6. Rollback (informational only — should not be needed)

If a rollback is ever required (per audit, no scenario should trigger this):

1. Locate the defensive export from §2.2.
2. Re-import: `pac solution import --path "<backup-path>" --activate-plugins`.
3. File a P0 incident — this indicates a missed consumer the audit did not catch.

Note: per **OS-1** (no compat shims) and the R3 schema cut, the side pane's `services/todoService.ts` will fail at runtime once Phase 1 completes (`sprk_eventtodo` deleted, `_sprk_regardingevent_value` legacy meaning gone, `sprk_todoflag` removed). Re-import would yield a guaranteed-runtime-error component. Rollback is **not viable post-Phase-1** — diagnose forward instead.

---

## 7. Tracking

Each deploy-team operator should:

1. Tick off §2 + §3 + §4 per environment.
2. File the defensive export filename in this section.
3. Note the date + operator initials.

| Environment | Date | Operator | Defensive export filename | Notes |
|---|---|---|---|---|
| Dev | _(fill in)_ | _(initials)_ | _(filename)_ | |
| Test | _(fill in)_ | _(initials)_ | _(filename)_ | |
| Prod | _(fill in)_ | _(initials)_ | _(filename)_ | |

---

*End of env-cleanup procedure. Task 081 source-side work is complete; the per-environment cleanup is the deploy team's manual step per the order in §5.*
