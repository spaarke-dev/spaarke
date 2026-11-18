# Task 035 — DEV smoke test of the item-4 package changes

> Owner OK for the DEV deploy: "Yes, deploy 035" (2026-11-13). Owner choice for SemanticSearch: "Script it like SpaarkeAi".
> Environment: DEV only (`https://spaarkedev1.crm.dynamics.com`, BFF `spaarke-bff-dev`). No hotfix was made in DEV.

## Deployments

| Surface | Build | Web resource | Method | Result |
|---|---|---|---|---|
| SpaarkeAi (Console) | Clean build from `5e50dde611` (`dist/spaarkeai.html`, 6,407,675 bytes) | `sprk_spaarkeai` (`5206a442-3451-f111-bec7-7ced8d1dc988`) | `scripts/Deploy-SpaarkeAi.ps1 -DataverseUrl https://spaarkedev1.crm.dynamics.com` | Updated + published |
| SemanticSearch | Webpack build + inline (`src/client/code-pages/SemanticSearch/out/sprk_semanticsearch.html`, 1,109,435 bytes) | `sprk_semanticsearch` (`695031d5-6412-f111-8342-7c1e520aa4df`) | Web API `webresourceset` PATCH (base64 `content`) + `PublishXml`, the same calls `Deploy-SpaarkeAi.ps1` makes | Updated + published, `modifiedon` 2026-11-14 03:37 UTC |

The SemanticSearch upload is **outside `code-page-deploy`'s documented steps** (that skill names no script for SemanticSearch). It was done on the owner's choice and used the token from `az account get-access-token --resource <env url>`; nothing else in DEV was changed.

## UI tests (POML 035), run 2026-11-14 in the in-app browser

| # | Test | What was done | Result |
|---|---|---|---|
| 1 | Console document operations | Console → Compose → Open Document → "Mutual Nondisclosure Agreement V3" → toolbar Word menu → **Open in web**. In the Console the moved hook (`useDocumentActions`) is used only by Compose (Open in web / Open in desktop). | **Pass.** `GET /api/documents/{id}/open-links` → 200; the page opened `spaarke.sharepoint.com` (the pane blocks script-opened tabs, so the Word page itself was not viewed). Download and Delete are not Console actions; they run in test 3. |
| 2 | Assistant context + LegalWorkspace section | Console load; Worklist section; Manage Workspaces panel (opened and cancelled, nothing saved); document added to the Assistant via Compose. | **Pass.** Worklist section rendered with live counts (228 notifications); the workspace registry listed 17 system layouts; the Assistant reported "I've added Mutual Nondisclosure Agreement V3.docx to our conversation" and offered document-specific suggestions (`ai-context` types path). All BFF calls 200/202/204. |
| 3 | SemanticSearch document actions | Search "mutual nondisclosure agreement" (SPE Files index, Hybrid) → 15 results → select "b01 - Kestrel Mutual Non-Disclosure Agreement.docx" → command-bar menu. | **Pass.** Menu shows Email a Link, Open in Web, Open in Desktop, Download, Send to Index; Delete enabled on selection. **Open in Web**: `open-links` → 200, page opened SharePoint. **Download** (2026-11-15, owner OK "proceed as recommended" = Download only): `GET /api/documents/{id}/download` → 200, no error banner, no new console error. Delete: not run (see below). |
| 4 | Dark mode | `spaarke-theme = dark` in the pane's localStorage (SpaarkeAi follows the Spaarke theme setting, not the OS scheme — ADR-021), reload, repeat test 1. | **Pass.** Console, Worklist, Compose editor, toolbar and the Word menu render with dark theme tokens; no light-coloured islands. Open in web → `open-links` 200. Setting removed afterwards (key was unset before). |

## Console errors

No error from Spaarke code. Errors seen, all pre-existing and unrelated to the item-4 changes:
- `[storage] Entity 'sprk_invoice' / 'sprk_document' With Id = … Does Not Exist` + matching 404s (8 records): the Navigator side pane's existence check on the user's recent items (`sprk_navitems`) for records since deleted. Data, not code; neither changed package is involved.
- `Failed to load resource … 401`: one per Console load. Every request from the Spaarke frames (Console, Navigator) returned < 400; the 401 comes from a cross-origin Microsoft platform frame (`apps.powerapps.com` authflow/apphost, OCV survey or Copilot card service).

## Not run

- **Delete** (`deleteDocuments`): the owner chose not to delete a DEV document (2026-11-15). The hook body moved unchanged and its unit tests cover the delete path (`useDocumentActions.test.tsx`, "deleteDocuments", 17/17 in task 033), so the untested risk is the wiring only, which Download and Open in Web exercise through the same hook instance in SemanticSearch.
- **Open in Desktop**: not clicked; it navigates to an `ms-word:` URL the browser pane cannot hand to Word. It uses the same `open-links` call verified above.

## Environment notes (for the next DEV test)

- The in-app browser pane may sign in with a personal Microsoft account that shares the work address; DEV rejects it (AADSTS50020). Sign in choosing **Work or school account**.
- SpaarkeAi follows the Spaarke theme setting (`spaarke-theme` localStorage / Dark Mode command), not the OS colour scheme, so dark-mode tests set the Spaarke setting.

## Result

All four POML ui-tests pass in DEV; no new console error from Spaarke code; no hotfix made. Runtime behaviour of the three package retirements is verified for task 080.

rework: none task 035
