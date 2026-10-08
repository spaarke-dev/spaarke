# Task 124: SpeDocumentViewer restored, and the other deployed controls with no source (2026-10-08)

Part of #1453 (B2B guest sign-in). Dataverse was only read in this task (spaarkedev1). Nothing was deployed, imported, published or edited in a form.

## Decision: restore and fix (not replace)

SpeDocumentViewer **is** the Document main form's preview. No maintained control or code page offers a form-bound replacement:

- **What it does.** The control is `sprk_Spaarke.SpeDocumentViewer`, version 1.0.27, a virtual control.
  - It sits on the **Document main form** (`9088d6a4-4cf2-f011-8406-7c1e520aa4df`) in section `overview_section_documentviewer`. It is bound to `sprk_documentname` at `controlHeight` 1200, with `showToolbar=false`; the ribbon's `sprk_DocumentOperations.js` supplies the commands.
  - It calls `GET /api/documents/{id}/view-url`, which returns an iframe URL and the checkout status. It also supports the checkout, check-in and discard edit modes, and `open-links`.
  - The form passes these static values:
    - `clientAppId=b36e9b91…` (the "SPE File Viewer PCF" app);
    - `bffAppId=1e40baad…`;
    - `tenantId=a221a95e…`.
- **The deployed bundle matches the restored source.** The baseline backup of the deployed bundle (`pcf-orphan-cleanup-r1/backups-2026-06-22/baseline-SpaarkeSpeDocumentViewer…zip`) is **byte-identical** to the bundle committed at `5b4cca898^`. The restored source is therefore the true source of what runs today.
- **No alternative fits.** The maintained preview surfaces are all dialogs or workspace widgets, not form-bound controls:
  - `RichFilePreview` and `RichFilePreviewDialog` in `@spaarke/ui-components`;
  - the LegalWorkspace `FilePreviewDialog`;
  - `DocumentViewerWidget`;
  - the SemanticSearch `DocumentPreviewDialog`.

  Replacing the control would mean building a **new** PCF and editing the form. That form is registered in 72 MDA app-module components and ships in SpaarkeMaster.
- **Restoring keeps the control name and id (`49b0cecd…`).** Deployment is then a plain version import with **no form edit**.
- **The control was not an orphan.** pcf-orphan-cleanup-r1 judged it unused by searching the source tree for importers, with "owner confirmation". The 2026-08-20 SpaarkeMaster audit (`pcf-legacy-retirement-r1/notes/audit-findings-from-r1.md`) found it live (RED) on the Document form. However, nobody restored the source or corrected `client-resources-inventory.md`.

### The fix (version 1.0.28)

**`control/authInit.ts`** was rewritten:
- **Tenant is passed explicitly.** It is passed as `tenantId`, so the library builds `https://login.microsoftonline.com/{tenant}` (INV-3, INV-6). It is no longer discarded with `void tenantId`.
- **Every value is validated as a GUID.** Values such as `"undefined"`, `"null"` and empty strings are rejected, so the 2026-05-13 malformed-authority bug class (`ef57fc3f35`) cannot return.
- **Missing configuration fails closed.** The control shows an error and never falls back to `/organizations`.
- **Only per-environment sources are used** (revised after independent review, finding F1):
  - Tenant: `sprk_TenantId`, else the Xrm `organizationSettings.tenantId`, else the control fails closed.
  - Client: `sprk_MsalClientId`, else it fails closed.
  - BFF app: `sprk_BffApiAppId`, else it fails closed.
  - **The form's `tenantId`, `clientAppId` and `bffAppId` are ignored.** They are dev ids (`a221a95e`, `b36e9b91`, `1e40baad`) typed into the form and **shipped in SpaarkeMaster**. My first version fell back to them whenever an environment variable was empty or unreadable (`getEnvironmentVariable` swallows errors). In any non-dev environment that fallback would have signed **every** user, members included, in to the dev tenant or app.
  - The Xrm organization tenant is the environment's own tenant, not the user's home tenant, so it is a safe second source for the tenant. Neither app id has an equivalent per-environment source.
  - The properties stay in the manifest as optional and ignored (the descriptions say so), so existing forms remain valid.
  - **Why fail closed instead of leaving `tenantId` out** and letting `@spaarke/auth` discover the tenant: that discovery is changing in flight (task 122). In the library this control builds against today it still ends at `/organizations`, which is the defect being fixed. An explicit error is deterministic and does not depend on the library version.
  - **Known limit:** if a guest cannot read `environmentvariabledefinition`, the client and BFF app ids fail closed, which shows an error. RelatedDocumentCount has the same dependency. This is unverified (note 122, "Not verified").
- **Scope** is `api://{bffAppId}/user_impersonation`, not `SDAP.Access`.
- **Redirect URI** is `Xrm…getClientUrl()`, the same rule RelatedDocumentCount uses.

**MSAL client: use the shared `sprk_MsalClientId` (dev: `170c98e1`), not `b36e9b91`.**
- RelatedDocumentCount on the **same form** and the Document ribbon already use the shared client. One client means one token cache and one consent (INV-7). A second client on the same form means a second cache and a second popup chain.
- The shared client is also the one every environment's runbook provisions.
- The control reads only the environment variable, so **no form or app-registration change is required**. Deleting the static form values (now ignored) is an optional clean-up (see the task 125 plan, step 6).
- `b36e9b91` can be retired once no form or ribbon references it. The ribbons `sprk_registrationribbon.js` and `sprk_aichatcontextmap_ribbon.js` still hardcode it (note 122, defect 7).

**Other changes in the restored source:**
- The three auth properties are now `required="false"` and **ignored**, with their descriptions updated. They are kept, not removed, so existing form customizations stay valid.
- The dead `accessToken` prop and parameters are removed (ADR-028 MUST NOT).
- The dead `AuthService.ts`, a direct `PublicClientApplication`, is **not** restored (INV-7).
- `BffClient` builds every URL through `buildBffApiUrl`, so there are no `${base}/api/...` literals.
- The `@spaarke/ui-components` dependency is dropped. The control used it for a logger only, and it forced a full shared-library build chain.
- The unused `@azure/msal-browser@^4` dependency is dropped (the library's peer is v3).
- The host's error and design-mode panels use Fluent tokens and `MessageBar` instead of hard-coded hex colours (ADR-021).
- `pack.ps1` reads the version from `solution.xml`, and its zip path is absolute. Before, `[ZipFile]::Open` resolved the relative path against the .NET process directory, which broke the script.
- The stale `solution/src/**` and `.cdsproj` copies are not restored.
- The version is bumped to 1.0.28 in the manifest, the UI footer, `solution.xml`, the built `ControlManifest.xml` and `package.json`.

### Build and tests

- `scripts/Invoke-PcfBuildProd.ps1 -PcfPath src/client/pcf/SpeDocumentViewer` → `PASS SpeDocumentViewer: [build] Succeeded`. This was re-run after the F1 fix.
  - The bundle is 438,291 B. The deployed 1.0.27 bundle is 426,332 B.
  - The bundle contains no `SDAP.Access`, and it contains `user_impersonation`.
- `npx jest` → **25 passed, 2 suites.**
  - `authInit.test.ts`:
    - `sprk_TenantId` wins over the Xrm tenant;
    - an empty or malformed `sprk_TenantId` falls back to the Xrm tenant;
    - a failed environment-variable read never yields the form's dev tenant (the resolver takes no form input);
    - no valid tenant fails closed, and `initAuth` is not called;
    - a missing client or BFF app id fails closed, with no form fallback;
    - configuration errors never match the host's "blocked popup" pass-through;
    - explicit `tenantId`, no `authority`, no `SDAP.Access` or `organizations`.
  - `BffClient.test.ts`: URLs built through `buildBffApiUrl`, and an empty base refused.
- The output was copied into `solution/Controls/sprk_Spaarke.SpeDocumentViewer/`. `pack.ps1` was run locally to validate the 7-entry zip, and the zip was then deleted.
- **The build bundles the pre-task-122 `@spaarke/auth` dist**, because task 122 is still in flight. The control does not depend on 122: it passes the tenant itself. INV-8 still requires a rebuild after 122 merges (task 125, step 1).

## The orphan-cleanup gate gap

1. **The source was deleted without the gate.** Task 002 (source deletion, PR #412) ran in parallel with task 001 (preflight). The hard gate, the "D-02 mandatory FormXML grep", was attached only to task 003 (Dataverse deletion), and task 003 never ran.
2. **The preflight form check could not see the form.** It grepped only each control's **dedicated** solution `customizations.xml`. The preflight note admits this was "necessary-but-not-sufficient". Forms live in the entity-owner and master solutions, so the Document main form was invisible to it.
3. **The importer grep is the wrong test.** "No external importers in the source tree" is always true for a PCF: forms bind controls by name in Dataverse, never by import.
4. **The correction was never fed back.** The 2026-08-20 six-layer audit reversed the verdict to RED (live). Nobody restored the source or fixed the docs. `client-resources-inventory.md` kept listing it as an orphan; that line is corrected in this task.

**Rule to adopt:** before deleting a PCF's **source**, query live `systemform.formxml LIKE '%<name>%'` and `savedquery.layoutxml LIKE '%<name>%'` (both are single read-only queries; this task used them), check canvas-app hosts, and check the SpaarkeMaster inclusion lists.

## Audit of the other deployed controls with no source

Rows of `customcontrol` named `sprk_%` in spaarkedev1, compared with `src/client/pcf/*`, show 12 controls with no source. They are SpeDocumentViewer plus the 11 below; the list in the brief is correct. Form bindings come from live `systemform.formxml` and view bindings from `savedquery.layoutxml`; `userquery` and sitemap checks were clean. "Signs in / BFF" comes from the deployed bundle (the 2026-06-22 baseline backups, which predate every `modifiedon` except AssociationResolver's), or from the last source in git (`ded4e037c2^`, or `e61550fe8a^` for AssociationResolver).

| Control | Deployed | Bound to | Signs in / calls BFF | Recommendation |
|---|---|---|---|---|
| AnalysisWorkspace | 1.3.5 | Nothing. Its canvas host `sprk_analysisworkspace_8bc0b` is already deleted, and the owner overrode DEV-001 on 2026-08-20 ("only launch is SpaarkeAI now"). | **Yes.** MSAL (`PublicClientApplication`, 170c98e1, `SDAP.Access`, `organizations` fallback) and the BFF. | Remove and uninstall (the host is gone). Excluded from SpaarkeMaster. |
| AnalysisBuilder | 2.9.2 | No form. Hosted by canvas `sprk_analysisbuilder_40af8`, which no sitemap or source references. Superseded by the `sprk_analysisbuilder` code page. | **Yes, broken.** Hardcoded retired BFF `spe-api-dev-67e2xz`, plus msal-browser. | Remove with its canvas app. Only in the Default solution: **no backup** (accepted in the orphan project). |
| PlaybookBuilderHost | 2.25.0 | No form. Hosted by canvas `sprk_playbookbuilder_eb5ad`, with no sitemap or source reference. Superseded by the `sprk_playbookbuilder` code page. | **Yes.** Raw `fetch` with `Authorization: Bearer ${accessToken}` to `/api/ai/playbook-builder/*`, plus SSE. | Remove with its canvas app (pcf-legacy-retirement §1). |
| AssociationResolver | 1.2.0 (2026-07-02) | Nothing. The owner removed it from the Matter form on 2026-08-20. | No. It resolves `sprk_BffApiBaseUrl` but never calls the BFF (`_apiBaseUrl` is unused). | Remove. **Back it up first:** there is no 2026-06-22 backup. |
| FieldMappingAdmin | 1.1.0 | Nothing. | Hardcoded retired BFF URL; no token code found. Otherwise `Xrm.WebApi`. | Remove (backup exists). |
| LegalWorkspace PCF | 1.0.1 | Nothing. | **Yes.** Raw `fetch` with `Bearer ${accessToken}` to `/api/workspace/*`, and a hardcoded retired BFF URL. | Remove (backup exists). Superseded by the code page. |
| RegardingLink | 1.1.6 | **6 active `sprk_event` views:** My Events Open, All Tasks, All Tasks Open, Matter All Tasks Open 7 Days, My Tasks Open, All Tasks Open 7 Days. | No (no auth or BFF strings in the bundle). | **Leave it until the views migrate** to SpaarkeGridCustomizer's `RegardingLinkRenderer`, then remove. |
| EventFormController | 1.0.6 | **Event modal form** `90d2eff7…` (opened by `EventDetailSidePane/sidePaneService.ts:92` through a hardcoded form id) and the **Event Assign Work main form** `835b8ee8…`. | No (`Xrm` only). | **Leave it.** Restore its source from `ded4e037c2^` before it ever needs a change (in SpaarkeMaster). |
| EventAutoAssociate | 1.0.0 | **Event quick create form** `642e1a65…`. | No (`webAPI` only). | Leave, or remove it from the quick create form if RegardingResolver now covers it (owner check). Restore its source if kept. |
| DueDatesWidget | 1.0.8 | Nothing. | No (`webAPI` only). | Remove (backup exists). |
| EventCalendarFilter | 1.0.5 | Nothing. | No. | Remove (backup exists). |

**For #1453:** none of the 11 is reached by a guest today. The four that sign in or call the BFF (AnalysisWorkspace, AnalysisBuilder, PlaybookBuilderHost, LegalWorkspace PCF) are bound to no form, view or navigated page. They violate ADR-028 and point at a retired BFF, so they should go, and that goes in a filed issue rather than this task.

Found in passing:
- **`sprk_MsalClientId` defaults to `1e40baad…`, the BFF API app.** Only spaarkedev1's override value makes it `170c98e1`. Any environment that imports the definition without an override value would sign in with the BFF's own app id.
- **VisualHost's Storybook stories are broken.** `stories/DueDatesWidget.stories.tsx` and `EventCalendarFilter.stories.tsx` import from the deleted `src/client/pcf/DueDatesWidget|EventCalendarFilter` folders.
- **`scripts/Build-SpaarkeMaster.ps1:80` is stale.** It lists EventAutoAssociate as `1d93fc9e…`, but dev1's id is `21dd8ded…`.

## Task 125 deploy plan (owner approval required)

1. **Prerequisite.** Wait for task 122 to merge. Then rebuild `@spaarke/auth` (`cd src/client/shared/Spaarke.Auth && rm -rf dist && npm run build`), and then run `pwsh -File scripts/Invoke-PcfBuildProd.ps1 -PcfPath src/client/pcf/SpeDocumentViewer -Install`. Expect `PASS`. Run `npx jest` in the control folder and expect 22 passed. Copy `out/controls/control/{bundle.js,ControlManifest.xml,css/*,strings/*}` into `solution/Controls/sprk_Spaarke.SpeDocumentViewer/`.
2. **Read-only pre-checks** in spaarkedev1.
   - Already confirmed 2026-10-08: `sprk_TenantId=a221a95e…`, `sprk_MsalClientId=170c98e1…` (override value), `sprk_BffApiAppId=1e40baad…`, `sprk_BffApiBaseUrl=https://spaarke-bff-dev.azurewebsites.net/api`.
   - In Entra, confirm that app `170c98e1` has the SPA redirect URI `https://spaarkedev1.crm.dynamics.com` and a delegated permission to `api://1e40baad…/user_impersonation`. RelatedDocumentCount already depends on both.
3. **Pack and import.** Run `pwsh -File src/client/pcf/SpeDocumentViewer/solution/pack.ps1`, then `pac solution import --path src/client/pcf/SpeDocumentViewer/solution/bin/SpaarkeSpeDocumentViewer_v1.0.28.zip --publish-changes`. That imports the unmanaged solution `SpaarkeSpeDocumentViewer` into spaarkedev1 (the pcf-deploy skill path). **No form edit is needed:** the control name and id are unchanged, and the new optional properties accept the existing static values.
4. **Verify.**
   - `customcontrol` `sprk_Spaarke.SpeDocumentViewer` reports version `1.0.28`.
   - After a hard refresh of a Document record, the footer reads `v1.0.28 - Built 2026-10-08`.
   - The console shows `tenant a221a95e… (environment-variable), client 170c98e1…`.
   - A member sees the preview load.
   - For the B2B guest: no AADSTS700016, and `/view-url` returns 200. The latter also needs note 122's guest prerequisites: an enabled systemuser, Dataverse OBO, and Read on the document.
5. **Customer environments.** SpeDocumentViewer is in SpaarkeMaster (`Build-SpaarkeMaster.ps1` `IncludedPcfIds`), so the next SpaarkeMaster release carries 1.0.28. Those environments need the three `sprk_*` environment variables set, as RelatedDocumentCount already requires.
6. **Optional form clean-up** (a separate owner decision; not required). On the Document main form's SpeDocumentViewer parameters, delete the static `clientAppId`, `bffAppId` and `tenantId` values, so dev ids stop shipping in SpaarkeMaster's form XML.
7. **Rollback.** Do not import the 2026-06-22 baseline (1.0.27) over 1.0.28: a control of a lower version is not reliably applied. Rebuild the previous behaviour as 1.0.29 instead, or revert the auth change and re-import.

## Issues to file (not filed by this task)

1. **Retire the source-less PCFs that sign in or call a retired BFF.** Covers AnalysisWorkspace, AnalysisBuilder (plus canvas `40af8`), PlaybookBuilderHost (plus canvas `eb5ad`) and LegalWorkspace PCF. Evidence is the table above. All four are unbound, raw-Bearer or MSAL, and two point at `spe-api-dev-67e2xz`. Belongs in pcf-legacy-retirement-r1, workstream 1.
2. **Remove the unbound non-auth orphans:** DueDatesWidget, EventCalendarFilter, FieldMappingAdmin and AssociationResolver. Back up AssociationResolver first.
3. **Migrate RegardingLink off the 6 `sprk_event` views, then retire it.**
4. **Restore the source of EventFormController and EventAutoAssociate** (from `ded4e037c2^`), or migrate their three Event forms. They are form-bound with no source of record.
5. **Fix the orphan-cleanup gate.** Gate source deletion on live `systemform` and `savedquery` queries plus the SpaarkeMaster lists, and correct the procedure's Check 2 (it inspected only the dedicated solution).
6. **Fix the `sprk_MsalClientId` default value**, which is the BFF app id `1e40baad`. Set it to the SPA client, or leave it empty so the control fails loudly.
7. **Fix the broken VisualHost stories** (DueDatesWidget and EventCalendarFilter imports), and the stale EventAutoAssociate id in `Build-SpaarkeMaster.ps1`.
8. **Retire app `b36e9b91`.** Do it after the ribbons stop hardcoding it (note 122, defect 7) and step 6 above is done.
