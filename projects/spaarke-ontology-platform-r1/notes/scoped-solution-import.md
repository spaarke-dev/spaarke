# Scoped solution import (no tenant-wide publish) - task 121, for tasks 114 and 129

Status: DRAFT, input to task 130 (D-83). The procedure is documented from platform behaviour and repo precedent; steps marked UNVERIFIED have not been run in spaarkedev1 and need an owner-approved import to confirm (task 121 step 4). dev (spaarkedev1) only.

## Why
`pac solution import --publish-changes` and `pac solution publish-all` publish every customization in the environment (D-81, project CLAUDE.md section 5). The scoped path publishes only what the solution carries.

## Procedure
1. Import WITHOUT publish:
   `pac solution import --environment https://spaarkedev1.crm.dynamics.com --path <ZIP> --force-overwrite`
   (no `--publish-changes`; `--async` optional for large solutions).
2. List that solution's components:
   `GET /api/data/v9.2/solutions?$select=solutionid&$filter=uniquename eq '<Name>'`
   `GET /api/data/v9.2/solutioncomponents?$select=componenttype,objectid&$filter=_solutionid_value eq <solutionid>`
   componenttype: 1 entity, 9 optionset, 61 web resource, 62 sitemap, 66 custom control, 80 model-driven app. Resolve entity ids to logical names via `EntityDefinitions(<id>)?$select=LogicalName`.
3. Publish exactly those components with `POST /api/data/v9.2/PublishXml` body `{"ParameterXml":"<importexportxml>...</importexportxml>"}`:
   ```
   <importexportxml>
     <webresources><webresource>{guid}</webresource></webresources>
     <entities><entity>sprk_example</entity></entities>
     <optionsets><optionset>sprk_example_choice</optionset></optionsets>
     <sitemaps><sitemap>{guid}</sitemap></sitemaps>
   </importexportxml>
   ```
   Repo precedent for web resources: scripts/Deploy-SpaarkeAi.ps1 line ~104. Entity precedent: Publish-Table in scripts/Add-RegardingFilingPickerToForms.ps1. UNVERIFIED: whether `<appmodules>` is accepted by PublishXml in this environment (model-driven apps have their own publish action); if a component type cannot be published by PublishXml, STOP and escalate - never fall back to PublishAllXml.
4. Read each component back (webresource `content` hash, entity form/view `modifiedon`, customcontrol `version`) and record it in deploy-log.md.

## PCF finding (UNVERIFIED - needs one owner-approved scoped PCF import, e.g. task 129)
Question: does an imported PCF custom control serve its new version with no publish? Test: record `customcontrols?$filter=name eq '<ctrl>'` version before; import without publish; re-read version and fetch the control bundle web resource; then open a form hosting it. If the new version is served, PCF imports need no PublishXml; if not, include the host entity (`<entities>`) of each form using the control in step 3. Observed so far: task 111 imported SemanticSearch 1.1.82 with `--publish-changes` (not a valid test).

## Existing violations (inventory for task 130; not fixed by task 121)
Scripts: Deploy-DataverseSolutions.ps1:392, Deploy-NoAccessEntryRibbon.ps1:144, Deploy-SecureChildNewCommands.ps1:247-249 (also PublishAllXml), Publish-ThemeIcons.ps1:89 (PublishAllXml), Remove-SprkEventTodoFields.ps1:9 (PublishAllXml), Generate-CreateTodoRibbonXmlForTenEntities.ps1:85 (instruction text). Package-LegalWorkspace.ps1 was deleted in PR #1455.
Skills (main session applies): dataverse-deploy lines 99,169,437,467,470,499,512-513,597; pcf-deploy 298,391; ribbon-edit 216.
Replacement text: "pac solution import --path <zip> --force-overwrite (no --publish-changes), then PublishXml for exactly the solution's components per notes/scoped-solution-import.md; never PublishAllXml / publish-all."
Remaining unverified (needs an owner-approved import): PCF-without-publish; PublishXml acceptance of app modules.
