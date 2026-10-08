# Batch 5 live record, 2026-10-08

## Task 154: No Access forms (PR #1393, `39ba73dbc`), applied to spaarkedev1 2026-10-08 ~04:07–04:30Z

1. **BFF:** deployed; healthz 200.
2. **Form library:** `sprk_/scripts/noaccessentry_postsave.js` deployed.
3. **`Deploy-NoAccessEntryForms.ps1 -Apply`:** applied the entry form, the view, the Organization NO ACCESS section (2 subgrids), the Contact NO ACCESS section (1 subgrid) and the site-map subarea. Snapshot `C:\wtR2\scripts\logs\noaccess-forms-snapshot-20261008001043.json`.
   - **Gap:** `AddAppComponents` answered 204 and added nothing. `-Verify` shows "sprk_noaccessentry is not a component of the app"; `ValidateApp` passes.
   - **For the owner:** check whether "No Access Entries" shows in the left nav; if not, add the table in the app designer. The script now warns instead of claiming success (PR #1407).
4. **`Deploy-NoAccessEntryRibbon.ps1 -Apply`:** a no-op the first time. `& pac` resolved to the `~/bin/pac` bash shim under Git Bash, so the post-import check failed. A manual `pac.cmd` import then worked, and `-Verify` PASSES (Add Existing hidden; 2 HideCustomAction diffs). Fixed in PR #1407.
5. **`Set-NoAccessEntryRolePrivileges.ps1`:**
   - The first `-Apply` created "Spaarke Access Administrator" with its set, then failed on `RemovePrivilegeRole`, because the payload shape was wrong. Fixed in PR #1407.
   - The re-run removed Read from Spaarke Core User and assigned the role to ralph.schroeder@spaarke.com. `-Verify` PASSES (O2).
   - The platform added 9 default privileges (SharePoint data, plugin/SDK reads at Global), which are reported only.
   - Microsoft's service roles were left as they are; per the main-session default, the owner did not object.
6. **Owner manual live gate:** checklist (a)–(p) in `notes/task-154-no-access-management-forms.md`.

## Task 105: external data paging (PR #1408, `37d0c944c`, closes #963), deployed 2026-10-08

- **Deployed:** the BFF (healthz 200) and the external SPA (workflow `deploy-external-spa.yml`, run 37741946955, success).
- **Live gate pending owner OK:** seed 250 `sprk_document` rows on a test project, then confirm the external `/documents` returns 250 with no `truncated` and the SPA shows no notice. No dev project has more than 200 children today.
- **Filed:** #1409 (an external to-do gets an empty regarding name on a transient read failure).
