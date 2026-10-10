# x09d — Broken buttons (record §11 H-4/H-5, §13a) and §11 Medium/Low triage

> Read-only research, 2026-10-09. Code is the authority; every claim cites `path:line`. Live state is taken
> only from the batch-A readouts (`working/x06-live-batch-a.md`, `working/x07-live-batch-a-dataverse.md`,
> `working/x04-live-entra-readout.md`, `working/x05-live-dataverse-readout.md`) and is true for **dev** at
> capture time only. Nothing was changed; no az/Graph/Dataverse/network/git-write command was run.

## 0. Roots and method

| Root | Id | Note |
|---|---|---|
| Worktree `src/` | `8a9ecaac1` (worktree HEAD `844dd1e5d` adds project docs only) | all unqualified `path:line` cites are this root |
| `origin/master` per brief | `231c5ab2b` (#1538) | used for every "changed since?" check below (`git diff 8a9ecaac1 231c5ab2b`, `git show 231c5ab2b:<path>`) |
| `origin/master` actually fetched | `ebacd2e15` (#1519, on top of `7e0291299` #1391) | `231c5ab2b` is an ancestor (`git merge-base --is-ancestor` = yes). The two newer commits add **Create-privilege display rules** to the Email, Document, Matter, Project, WorkAssignment, analysis ribbons and touch no auth path — noted per row where relevant |

Method: for each item, (a) the ribbon/form XML that binds the user action, (b) the script function and its auth + URL code, (c) the server route and its policy, (d) which copy x07 found deployed on dev, (e) the `231c5ab2b` diff.

Two corrections to the record surfaced while doing this (see §3): `/api/insights/ask` **is** mapped — the insight scripts fail for a different reason than "unmapped" — and the `/api/api/` double-prefix affects **all six** KPI/rollup copies, not three.

---

## 1. Task 1 — Broken-button table

Legend for "Live": **CONFIRMED LIVE ON DEV** = x07 T-P3-04 decoded the deployed web resource and/or read `systemform.formxml` / `ribboncommand` on dev; **CODE-ONLY** = repo evidence only (no live read of that surface). "Fix kind": **PKG** = solution-packaging only (copy the fixed source into `src/dataverse/solutions/SpaarkeMaster/WebResources/` and re-import, or script-deploy); **CODE** = code change needed; **DEL** = delete.

### 1.1 Email — ribbon "Archive Email" (form command bar, web client only)

| Field | Finding |
|---|---|
| Where the user clicks | Email main form, `Mscrm.Form.email.MainTab.ExportData` → button **"Archive Email"** (`sprk.Email.ArchiveEmail.Button`, tooltip "Archive this email as an SDAP document… AI analysis will be triggered automatically") — `src/dataverse/solutions/SpaarkeMaster/Entities/Email/RibbonDiff.xml:4-6,60-67`. Shown only on Web client and when `Spaarke.Email.canArchiveEmail` says not archived (`:19-20,32-39`); enabled when `canSaveToDocument` (`:41-46`). |
| What runs | `$webresource:sprk_emailactions.js` → `Spaarke.Email.saveToDocument(PrimaryControl)` (`RibbonDiff.xml:23-25`). Source `src/client/webresources/js/sprk_emailactions.js`: acquires a BFF token (`:500`), then `POST {bffApiUrl}/api/emails/convert-to-document` with `Authorization: Bearer` (`:505-520`). |
| Which copy is live on dev | **Source copy (task-123 fixed, v1.2.1, `user_impersonation` at `:49`)** — deployed 2026-10-08 22:51, byte-identical to source (x07 T-P3-04). The packaged copy at `8a9ecaac1` is v1.2.0 / `SDAP.Access` (`SpaarkeMaster/WebResources/sprk_emailactions.js:46`). |
| What the user sees | Progress indicator "Converting email to document…", then the error dialog **"Email not found or you don't have permission to access it."** — the script maps HTTP 404 to that text (`src/client/webresources/js/sprk_emailactions.js:566-569`). |
| Why it fails | **Unmapped route.** No `api/emails` / `convert-to-document` literal exists under `src/server` at `8a9ecaac1` or `231c5ab2b` (`git grep` empty); `MapEmailEndpoints` is recorded as removed at `src/server/api/Sprk.Bff.Api/Infrastructure/DI/EndpointMappingExtensions.cs:219-223` (same at `231c5ab2b:219`). With a valid bearer the fallback policy yields 404 (a02 §1.2.6; `AuthorizationModule.cs:235,415-419`). Per a02 D-25 the server **never** mapped this exact literal (pickaxe empty; the retired file mapped `/api/v1/emails/{id}/save-as-document`). Token acquisition itself works on dev (fixed copy, scope `user_impersonation`). |
| Fix | **CODE** (not packaging): either remove the button + script, or re-point `saveToDocument` at the Graph-based communication pipeline (`IncomingCommunicationProcessor`, per the removal comment). Repackaging alone changes nothing — the dead path is in both copies. |
| Live | **CONFIRMED LIVE ON DEV** (x07: ribbon command `sprk.Email.ArchiveEmail.Command` → fixed copy → 404). |
| Changed on `231c5ab2b`? | Packaged copy updated to v1.2.1 (`+user_impersonation`, `git diff 8a9ecaac1 231c5ab2b -- …/sprk_emailactions.js`) but **still posts to `/api/emails/convert-to-document`** (`git show 231c5ab2b:…/sprk_emailactions.js:506`). Ribbon XML unchanged at `231c5ab2b`; `ebacd2e15` (#1519) adds a `sprk_document` Create-privilege display rule only. **Still broken.** |

### 1.2 Matter — main form OnLoad: insight pre-warm and insight card

| Field | Finding |
|---|---|
| Where | Matter main form `{4fa382f2-c273-f011-b4cb-6045bdd6a665}` form-level `onload`: `Spaarke.MatterInsight.onLoad` ← `sprk_matter_insight_onload.js`; `Spaarke.MatterInsightCard.onLoad` ← `sprk_matter_insight_card_mount.js` (`…/sprk_Matter/FormXml/main/{4fa382f2…}.xml:1455-1456,1466-1467`; the same pair is also registered at `:355-356`). Nothing to click: it runs on every Matter form open; the card's fetch also runs on first popover open / manual refresh (`src/dataverse/forms/sprk_matter/insightCardMount.ts:510-519`). |
| What runs | Source `src/dataverse/forms/sprk_matter/insightCardMount.ts:275` sets `_invocationEndpoint = '/api/insights/ask'` and `_invokeInsight` does `fetch(url, { method:'POST', headers: Content-Type/Accept, credentials:'include' })` with **no `Authorization`** and a **relative URL** (`:318-334`); `insightWidgetOnLoad.ts:156,297-312` does the same fire-and-forget pre-warm. Packaged copies identical in substance: `SpaarkeMaster/WebResources/sprk_/scripts/matter_insight_onload.js:63,188`, `…/matter_insight_card_mount.js:153,210`, and the root-named duplicates `sprk_matter_insight_onload.js:63,188` (both name sets are root components: `Other/Solution.xml:367-368,410-412`). |
| Which copy is live | Packaged (x07: `sprk_matter_insight_onload.js` and `sprk_/scripts/matter_insight_onload.js` deployed 2026-06-11/16). The form binds the **root-named** files (`sprk_matter_insight_onload.js`, `sprk_matter_insight_card_mount.js`), not the `sprk_/scripts/` pair. |
| What the user sees | Pre-warm: nothing (errors swallowed by design, `insightWidgetOnLoad.ts:308-312`). Card: `onFetchInsight` rejects with `Insight invocation failed: HTTP <status>` (`insightCardMount.ts:336-338`) → the Spaarke.AI.Widgets card shows its failure state (exact widget text not verified here). |
| Why it fails | **Not** an unmapped route: `POST /api/insights/ask` is mapped (`src/server/api/Sprk.Bff.Api/Api/Insights/InsightEndpoints.cs:81-95`, wired at `EndpointMappingExtensions.cs:402`, same at `231c5ab2b`) behind `AddInsightsAskAuthorizationFilter`. The client posts a **relative** path from a Dataverse form, so the browser sends it to `https://<org>.crm.dynamics.com/api/insights/ask` — the Dataverse origin, never the BFF — and even if the base were right there is **no bearer token** (`credentials:'include'` carries no BFF cookie). Both scripts assume "same-origin / reverse-proxy" (`insightWidgetOnLoad.ts:155` comment) which does not exist in this topology (a02 D-3). **Correction to record §13a / x07, which say "the unmapped `/api/insights/ask`".** |
| Fix | **CODE**: resolve `sprk_BffApiBaseUrl` (host-only normalised) and acquire a bearer (`Spaarke.BffAuth.getToken`, already loaded on this form at `:1457`) before posting; then **PKG** both root-named and `sprk_/scripts/` copies (or drop one name set). |
| Live | **CONFIRMED LIVE ON DEV** (x07: Matter main form OnLoad loads `matter_insight_onload`). |
| Changed on `231c5ab2b`? | Form XML gains `sprk_/scripts/accessstatus_banner.js` library + `Spaarke.AccessStatus.onLoad` handler only; insight handlers unchanged; scripts unchanged. **Still broken.** |

### 1.3 Matter — main form OnLoad: KPI grade recalculation (`sprk_matter_kpi_refresh.js`)

| Field | Finding |
|---|---|
| Where | Matter main form `onload` → `Spaarke.MatterKpi.onLoad` ← `sprk_matter_kpi_refresh.js` (`…/sprk_Matter/FormXml/main/{4fa382f2…}.xml:1453,1463`). No button: the handler watches the KPI subgrid row count and, when it changes (e.g. after a Quick Create save), POSTs a recalculation (`SpaarkeMaster/WebResources/sprk_matter_kpi_refresh.js:233-244`). **Only the Matter main form loads it**; the Project main form loads no KPI script (`…/sprk_Project/FormXml/main/{5aa00242…}.xml:1371-1372` lists only `bff_auth` and `assignedaccess_postsave`). |
| What runs | **Packaged** copy v1.0.0 (`:71`): `fetch(_apiBaseUrl + "/api/matters/" + id + "/recalculate-grades", { method:"POST", headers: Content-Type/Accept })` — **no `Authorization`, no credential at all** (`:269-278`); `_apiBaseUrl` is the raw `sprk_BffApiBaseUrl` value (`:91,152`). **Source** copy `src/solutions/webresources/sprk_matter_kpi_refresh.js` v2.0.0 (`:78`) calls `Spaarke.BffAuth.getToken(_apiBaseUrl)` and sends a Bearer (`:284-301`) but still concatenates `_apiBaseUrl + "/api/matters/…"` (`:276`) with **no `/api` stripping** (grep for `/api$`/`normalizeBaseUrl` = 0 hits in both copies). |
| Which copy is live | **Packaged** (x07: deployed 2026-04/05, not the `src/solutions/webresources` copies). |
| Why it fails | Two independent causes on dev: (1) **no bearer** → `FallbackPolicy = RequireAuthenticatedUser` → 401 (route is `RequireAuthorization()` + `FinanceAuthorizationFilter`, `src/server/api/Sprk.Bff.Api/Api/ScorecardCalculatorEndpoints.cs:34-45`); (2) **`/api/api/` double prefix** — dev `sprk_BffApiBaseUrl` = `https://spaarke-bff-dev.azurewebsites.net/api` (x05 §A `:15,33`), so the URL is `…/api/api/matters/{id}/recalculate-grades`, which no route matches (404 even with a token). The route itself is mapped (`EndpointMappingExtensions.cs:242`). |
| What the user sees | Nothing explicit: console log "Calling calculator API", the non-`ok` branch logs; the form does not refresh, so **grades stay stale after a KPI assessment is added** until something else recalculates. |
| Fix | **PKG** the v2.0.0 source copy (restores the bearer) **and CODE**: strip a trailing `/api` as `assignedaccess_postsave.js` does (`src/solutions/webresources/sprk_assignedaccess_postsave.js:63-65,88-93`, #1488) or set the env var host-only. Repackaging alone still yields `/api/api/` on dev. |
| Live | **CONFIRMED LIVE ON DEV** (x07: packaged copy, no `Authorization`; form binding read from the repo form XML, which x07 did not separately query — "which forms load them was not separately queried"). |
| Changed on `231c5ab2b`? | No (`sprk_matter_kpi_refresh.js` absent from the 8a9ecaac1..ebacd2e15 file list). **Still broken.** |

### 1.4 Matter — main form OnLoad: finance subgrid rollup (`sprk_subgrid_parent_rollup.js`, ×2)

| Field | Finding |
|---|---|
| Where | Matter main form `onload` → `Spaarke.SubgridRollup.onLoad` twice, parameters `subgridName=subgrid_invoice` and `subgrid_budgets`, `apiPathTemplate=/api/finance/{entityPath}/{entityId}/recalculate` (`…/sprk_Matter/FormXml/main/{4fa382f2…}.xml:1454,1464-1465`). Triggers when the invoice/budget subgrid row count changes. |
| What runs | **Packaged** v1.0.0 (`SpaarkeMaster/WebResources/sprk_subgrid_parent_rollup.js:46`): `fetch(inst.apiBaseUrl + template, { method:"POST", headers: Content-Type/Accept })` — **no credential, no MSAL at all** (`:271-283`). **Source** v2.0.0 (`src/solutions/webresources/sprk_subgrid_parent_rollup.js:50`) carries its own MSAL 2.38.4 via `/api/config/client` (`:199-204`) and sends a Bearer (`:461-466`); no `/api` stripping in either copy. |
| Which copy is live | **Packaged** (x07). |
| Why it fails | Same pair as 1.3: no bearer → 401 on `RequireAuthorization()` + `FinanceAuthorizationFilter` (`src/server/api/Sprk.Bff.Api/Api/Finance/FinanceRollupEndpoints.cs:40-50`, wired `EndpointMappingExtensions.cs:368`), and `/api/api/finance/matters/{id}/recalculate` on dev's `/api`-suffixed base. |
| What the user sees | Nothing explicit; finance rollups on the Matter header do not refresh after invoice/budget edits. |
| Fix | **PKG** v2.0.0 + **CODE** `/api` strip (or host-only env value). Note the source v2.0.0 copy inherits a02 D-8/D-9 (`accounts[0]` + display-name `loginHint`, authority taken verbatim from anonymous `/api/config/client`) — functional for U1 on dev, fragile for guests. |
| Live | **CONFIRMED LIVE ON DEV** (x07 packaged copy; binding from repo form XML). |
| Changed on `231c5ab2b`? | No. **Still broken.** |

### 1.5 KPI quick-create and KPI subgrid scripts — packaged, no loader found

| Field | Finding |
|---|---|
| Files | `sprk_kpi_subgrid_refresh.js` (packaged v1.1.0, `SpaarkeMaster/WebResources/…:77`, POST `/api/{entityPath}/{id}/recalculate-grades` with no credential `:294-298`) and `sprk_kpiassessment_quickcreate.js` (packaged, `credentials:"include"` only, `:271-273`); both root components (`Other/Solution.xml:407-408`). Source v2.0.0 copies use `Spaarke.BffAuth` (`src/solutions/webresources/sprk_kpi_subgrid_refresh.js:84,309-326`; `…/sprk_kpiassessment_quickcreate.js:75,282-302`); no `/api` stripping in any copy. |
| Loader | **None in the repo**: no `FormXml`/`RibbonDiff` under `src/dataverse` or `src/solutions` references either file (grep: only `Solution.xml`); the KPI Assessment quick-create form (`…/Entities/sprk_KPIAssessment/FormXml/quickCreate/`) declares no form libraries; `sprk_KPIAssessment/RibbonDiff.xml` binds only the `sprk.SecureChild.*` New command. x07 did not query the live forms for these. |
| Status | **CODE-ONLY** (packaged and deployed on dev per x07's decoded-content table, but no binding found; live binding **NEEDS LIVE TEST**). If a live form binds them they fail exactly as 1.3. Fix = PKG + CODE as 1.3, or DEL if unbound. Unchanged on `231c5ab2b`. |

### 1.6 Registration request — "Approve Demo Access" / "Reject Request" (form and home grid)

| Field | Finding |
|---|---|
| Where | `sprk_registrationrequest` main form command bar and home-page grid: **Approve Demo Access** (`Sprk.Registration.Approve.Form.Button`, `.Grid.Button`) and **Reject Request** (`.Reject.Form.Button`, `.Grid.Button`) — `src/dataverse/solutions/SpaarkeMaster/Entities/sprk_registrationrequest/RibbonDiff.xml:4-21`. |
| What runs | `$webresource:sprk_/js/registrationribbon.js` → `Sprk.RegistrationRibbon.approveRequestFromForm` / `approveRequest` / `rejectRequestFromForm` / `rejectRequest`; enable rules from the same file (`RibbonDiff.xml:35,46,57,68,79,87`). |
| Which copy is live | **Packaged** `sprk_/js/registrationribbon.js` (x07: deployed 2026-04-06, byte-identical to the packaged copy; root component `Other/Solution.xml:362`). The task-123 fixed source is `src/client/webresources/js/sprk_registrationribbon.js` (env-var driven, `user_impersonation` at `:36-43`, tenant validation `:71-151`) — **a different schema name that is in no `Solution.xml` and no `.data.xml`** (`ls SpaarkeMaster/WebResources | grep registration` = nothing; no deploy script references it). The fix has no deploy path. |
| Why it fails | Packaged copy hard-codes: client `b36e9b91-…` (the legacy "SPE File Viewer PCF" app — **exists live, `AzureADMyOrg`, SPA redirect `spaarkedev1.crm.dynamics.com`**, x04 A4), BFF app `1e40baad-…`, tenant `a221a95e-…`, scope `SDAP.Access`, redirect `https://spaarkedev1.crm.dynamics.com` (`SpaarkeMaster/WebResources/sprk_/js/registrationribbon.js:27,34-44`) and a host map whose dev/demo branches and default all point at **`https://spe-api-dev-67e2xz.azurewebsites.net`** (`:27,79-93`), the retired App Service (record M-19; only its MI app user survives, x05 §B). So on dev: MSAL may well mint a token (client and `SDAP.Access` both exist live), then the `fetch` goes to a dead host. The real route is `POST /api/registration/requests/{id}/approve|reject`, `RequireAuthorization()` + `RegistrationAuthorizationFilter` (Admin/SystemAdmin app role; `src/server/api/Sprk.Bff.Api/Api/RegistrationEndpoints.cs:18,34-51`; a05 `:235`). On a non-dev environment the hard-coded tenant/redirect make sign-in itself fail (AADSTS redirect mismatch) before any call. |
| What the user sees | Progress indicator, then `_sprkReg_showError` → `Xrm.Navigation.openErrorDialog` with the caught fetch error (network failure to the retired host) (`…/registrationribbon.js:242,290-292,435-438`); exact message text depends on the browser's `TypeError`. |
| Fix | **PKG only** — but it requires a **rename**: copy `src/client/webresources/js/sprk_registrationribbon.js` over `SpaarkeMaster/WebResources/sprk_/js/registrationribbon.js` (keep the `sprk_/js/` schema name the ribbon binds) and re-import, or change the four `RibbonDiff.xml` `Library=` values to a newly packaged `sprk_registrationribbon.js`. The source copy's own-MSAL/`sessionStorage` pattern (a02 D-6) remains an ADR-028 deviation after the fix. Note the route needs an `Admin`/`SystemAdmin` app role (record M-4): on dev only `Admin` exists (x04 A1). |
| Live | **CONFIRMED LIVE ON DEV** (x07: `Sprk.Registration.*.Command` → old packaged copy). |
| Changed on `231c5ab2b`? | **No** — neither copy differs (`git diff --stat 8a9ecaac1 231c5ab2b` empty for both; also absent from the ebacd2e15 file list). **Still broken; fix still unpackaged.** |

### 1.7 AI Chat Context Map — "Refresh Cache" (form and grid) — **fixed on dev, fixed in package on origin/master**

| Field | Finding |
|---|---|
| Where | `sprk_aichatcontextmap` main form and home grid, button **"Refresh Cache"** / "Refresh Context Mapping Cache" (`…/Entities/sprk_AIChatContextMap/RibbonDiff.xml:4-11,41-51`) → `$webresource:sprk_aichatcontextmap_ribbon.js` → `refreshMappings` (`:25`), enable rule `Spaarke_EnableRefreshMappings` (`:34`). |
| What runs | `DELETE {bffApiUrl}/api/ai/chat/context-mappings/cache` (`src/client/webresources/js/sprk_aichatcontextmap_ribbon.js:274-278`); route mapped `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs:340-341` (`:341-342` at `231c5ab2b`). |
| Which copy is live | **Source (fixed, v1.1.0)** deployed 2026-10-08 22:51 (x07). The `8a9ecaac1` packaged copy was the broken one (client `b36e9b91`, tenant `a221a95e`, `SDAP.Access`, `spe-api-dev-67e2xz` host map — `SpaarkeMaster/WebResources/sprk_aichatcontextmap_ribbon.js:17-33,57-58`). |
| Status | **Not broken on dev** (fixed copy + mapped route). **Packaged copy fixed on `231c5ab2b`** (`git show 231c5ab2b:…/sprk_aichatcontextmap_ribbon.js:10,32,52-112` — env-var driven, `user_impersonation`, tenant validation). Residual: own MSAL 2.38.3 + `sessionStorage` (a02 D-6, ADR-028 deviation) — HYGIENE, not a break. |
| Live | **CONFIRMED LIVE ON DEV** (fixed). |

### 1.8 Document — ribbon "Delete" and the orphan `sprk_DocumentDelete.js`

| Field | Finding |
|---|---|
| Where | `sprk_document` ribbon Delete command → `Spaarke.Document.deleteDocument` ← `$webresource:sprk_DocumentOperations.js` (`…/Entities/sprk_Document/RibbonDiff.xml:179-181`); every other document command (checkin/checkout/discard/download/open/refresh/sendToIndex) also binds `sprk_DocumentOperations.js` (`:153-290`). |
| Which copy is live | **Source (fixed, v1.28.1)** deployed 2026-10-08 (x07) — env-var driven, fails closed on a non-GUID `sprk_TenantId` (`src/client/webresources/js/sprk_DocumentOperations.js:330-334`), `user_impersonation`. Delete calls `DELETE /api/documents/{id}` (`:1080` ff.). **Works on the fixed path** (x07). |
| `sprk_DocumentDelete.js` | Packaged root component (`Other/Solution.xml:392`; `:393` at `231c5ab2b`), deployed 2026-04-06 (x07), **loaded by nothing** (grep of all `Entities/**` for `DocumentDelete` = empty, x07 ribboncommand read agrees). Hard-codes client `ed44fb14-…`, BFF app `8c60b44e-…`, tenant `e2e89f3a-…` — none exists; the tenant is not found (x04 A21/A22) (`SpaarkeMaster/WebResources/sprk_DocumentDelete.js:27-49`), `sessionStorage` MSAL (`:140-146`). **Latent hazard not in the record:** it defines the **same namespace object** `Spaarke.Document.Config` (`:27`) that `sprk_DocumentOperations.js` reads (`src/client/webresources/js/sprk_DocumentOperations.js:632,758,896,1007`); if any form ever loaded both, the later-loaded file would clobber the other's config. |
| Status | Delete button: **not broken on dev**; packaged `sprk_DocumentOperations.js` updated on `231c5ab2b` (27-line task-123 diff applied). `sprk_DocumentDelete.js`: **DEL** (remove the root component and the file; solution-packaging only). **CONFIRMED LIVE ON DEV** (x07: orphan). Unchanged on `231c5ab2b`. |

### 1.9 ScopeConfigEditor PCF — Tool editor "handler class" dropdown

| Field | Finding |
|---|---|
| Where | Inside the ScopeConfigEditor PCF, `ToolEditor` loads the handler-class dropdown on mount: `GET {apiBaseUrl}/api/ai/tools/handlers` with only `Content-Type` (`src/client/pcf/ScopeConfigEditor/ScopeConfigEditor/components/ToolEditor.tsx:153-158`); no `@spaarke/auth` import anywhere in the control (grep empty). |
| Server | Route mapped and `RequireAuthorization()` (`src/server/api/Sprk.Bff.Api/Api/Ai/HandlerEndpoints.cs:26-30`, wired `EndpointMappingExtensions.cs:261`) → **401 always**. |
| What the user sees | Inline error "Unable to load handler list (HTTP 401). Enter the handler class manually." and the dropdown degrades to a text box (`ToolEditor.tsx:175-181`). The control otherwise works (Dataverse writes are via `webAPI`). |
| Fix | **CODE**: `initAuth` + `authenticatedFetch` (pattern: any of the ten `authInit.ts` consumers, e.g. `src/client/pcf/SemanticSearchControl/SemanticSearchControl/authInit.ts:52`). |
| Packaging / live | **CODE-ONLY.** Not in `SpaarkeMaster/Controls/` and not referenced by any `FormXml` in `src/dataverse` or `src/solutions`; it has its own `Solution/` folder (`src/client/pcf/ScopeConfigEditor/Solution/{solution.xml,customizations.xml,pack.ps1}`) — a separately imported solution; whether it is placed on a live form was not read in batch A. Unchanged on `231c5ab2b`. |

### 1.10 UpdateRelatedButton PCF and `sprk_updaterelated_commands.js`

| Field | Finding |
|---|---|
| PCF | `UpdateRelatedButtonApp.tsx` posts `{apiBaseUrl}/api/v1/field-mappings/push` with `credentials:'include'` and **no `Authorization`** (`src/client/pcf/UpdateRelatedButton/UpdateRelatedButtonApp.tsx:161-168`); `apiBaseUrl` is a bound form column, no env-var fallback (`index.ts:54`; manifest `usage="bound"`, a02 §1.2.1). Route is `RequireAuthorization()` (`src/server/api/Sprk.Bff.Api/Api/FieldMappings/FieldMappingEndpoints.cs:29-32`, wired `EndpointMappingExtensions.cs:228`) → 401; the UI shows `HTTP 401` from the `!response.ok` branch (`:171-179`). |
| Ribbon script | `src/client/webresources/js/sprk_updaterelated_commands.js` (Event grid/form/subgrid "Update Related") hard-codes **`apiBaseUrl: "https://spe-api-dev-67e2xz.azurewebsites.net"`** (`:17`) — the retired host — and posts with no bearer (`:180-187`). **Not packaged** (`Other/Solution.xml` has no `updaterelated`), **not bound** by any `RibbonDiff.xml` or `src/client/webresources/ribbon/*.xml`. |
| The live path instead | Matter and Project ribbons bind `$webresource:sprk_fieldmapping_push` (4 bindings; `…/sprk_Matter/RibbonDiff.xml`, `…/sprk_Project/RibbonDiff.xml`; root component `Other/Solution.xml:402`), which has its own env-var-driven MSAL and sends a Bearer (`src/client/webresources/js/sprk_fieldmapping_push.js:679,692-728,806-838,874-900`) — functional. |
| Status | **CODE-ONLY**, both artefacts: PCF not in `SpaarkeMaster/Controls/`, own `Solution/Controls/sprk_Spaarke.Controls.UpdateRelatedButton/` folder, no form binding found; commands script unpackaged. Fix = **DEL** both (superseded by `sprk_fieldmapping_push`) or **CODE** (`@spaarke/auth`). Unchanged on `231c5ab2b`. Live placement **NEEDS LIVE TEST** (record a02 D-5). |

### 1.11 `sprk_communication_send` — packaged, deployed, referenced by nothing

| Field | Finding |
|---|---|
| File | Root component `Other/Solution.xml:378`; `.data.xml` describes it as "Web resource for the sprk_communication entity Send command bar button". Own MSAL 2.38.0 from CDN (`SpaarkeMaster/WebResources/sprk_communication_send:622`), reads all four env vars (`:159-185`), **strips a trailing `/api`** (`:207-208`), scope `user_impersonation`, Bearer to `POST {base}/api/communications/send` (`:548-570`); route mapped `src/server/api/Sprk.Bff.Api/Api/CommunicationEndpoints.cs:58-62`. |
| Binding | None: `sprk_Communication/RibbonDiff.xml` binds no `communication_send` function (its Send/compose is the `CommunicationActions` PCF, whose manifest says it "Replaces the ribbon sprk_communication_send.js (W4 pivot, task 044)" — `src/client/pcf/CommunicationActions/package.json:4`; `…/index.ts:8`). Grep of all `Entities/**` = empty. |
| Status | **CONFIRMED LIVE ON DEV as deployed-but-orphan** (x07: deployed 2026-08-14, packaged). Would work if invoked (correct scope, mapped route, `/api` strip). Fix = **DEL** (solution-packaging only). Record L-1 stands. Unchanged on `231c5ab2b`. |

### 1.12 Summary (one line each)

| # | Surface | Live | Root cause | Fix kind | On `231c5ab2b` |
|---|---|---|---|---|---|
| 1.1 | Email → "Archive Email" | CONFIRMED LIVE | unmapped `/api/emails/convert-to-document` (404 shown as "not found / no permission") | CODE | still broken (scope fixed only) |
| 1.2 | Matter form OnLoad → insight pre-warm + card | CONFIRMED LIVE | relative URL hits Dataverse origin + no bearer (route **is** mapped) | CODE + PKG | still broken |
| 1.3 | Matter form OnLoad → `sprk_matter_kpi_refresh.js` | CONFIRMED LIVE | packaged v1.0.0: no bearer (401) **and** `/api/api/` on dev | PKG + CODE | still broken |
| 1.4 | Matter form OnLoad → `sprk_subgrid_parent_rollup.js` ×2 | CONFIRMED LIVE | packaged v1.0.0: no credential (401) **and** `/api/api/` on dev | PKG + CODE | still broken |
| 1.5 | `sprk_kpi_subgrid_refresh.js`, `sprk_kpiassessment_quickcreate.js` | CODE-ONLY (no loader found) | as 1.3 if ever bound | PKG + CODE or DEL | unchanged |
| 1.6 | Registration Approve/Reject (form + grid) | CONFIRMED LIVE | packaged copy: legacy client, `SDAP.Access`, hard-coded tenant/redirect, retired host `spe-api-dev-67e2xz`; fix exists under an unpackaged name | PKG (rename) | still broken |
| 1.7 | AI Chat Context Map "Refresh Cache" | CONFIRMED LIVE — **fixed** | — | — | package fixed |
| 1.8 | Document "Delete" / `sprk_DocumentDelete.js` | CONFIRMED LIVE — Delete works; DocumentDelete orphan | orphan with nonexistent tenant; shares `Spaarke.Document.Config` namespace | DEL | package of DocumentOperations fixed; orphan remains |
| 1.9 | ScopeConfigEditor handler dropdown | CODE-ONLY | no bearer to `RequireAuthorization()` route (401 → manual entry) | CODE | unchanged |
| 1.10 | UpdateRelatedButton PCF + `sprk_updaterelated_commands.js` | CODE-ONLY | no bearer; script hard-codes retired host; superseded by `sprk_fieldmapping_push` | DEL (or CODE) | unchanged |
| 1.11 | `sprk_communication_send` | CONFIRMED LIVE — orphan | packaged, no binding; would work | DEL | unchanged |

---

## 2. What `231c5ab2b` / `ebacd2e15` changed in this area (vs `8a9ecaac1`)

From `git diff --name-only 8a9ecaac1 origin/master -- src/client/webresources src/solutions/webresources src/dataverse …` and per-file `git show`:

- **Packaged copies updated to the task-123 source:** `sprk_DocumentOperations.js` (27 lines), `sprk_aichatcontextmap_ribbon.js` (129 lines), `sprk_emailactions.js` (9 lines; scope only — dead route kept). This closes record **D-27 for three of four files**.
- **Not updated:** `sprk_/js/registrationribbon.js` (D-27's fourth file; still legacy ids/`SDAP.Access`/retired host), the four KPI/rollup packaged scripts (D-23), `sprk_DocumentDelete.js` (still a root component), ScopeConfigEditor and UpdateRelatedButton.
- Matter main form: `+ sprk_/scripts/accessstatus_banner.js` library and `Spaarke.AccessStatus.onLoad` handler; the insight and KPI handlers are untouched.
- `ChatEndpoints.cs` (32 lines): `ProblemDetailsHelper` refactor only; `DELETE /context-mappings/cache` remains (`:341-342`).
- `EndpointMappingExtensions.cs`: `MapInsightsAskEndpoint` still at `:402`; `MapEmailEndpoints` still absent (`:219` comment).
- `ebacd2e15` (#1519, after the brief's `231c5ab2b`): Create-privilege `EntityPrivilegeRule` display rules on the Email/Document/Matter/Project/WorkAssignment/analysis ribbons. No script or auth change.
- Of the §11 M/L evidence files, the only drift to `231c5ab2b` is: `JobsEndpoints.cs` (`Results.Problem` refactor, M-4 gating unchanged), office add-in messageId work (`OutlookAdapter.ts`, `SaveFlow.tsx`, `useSaveFlow.ts` — not the H-7/M-3/M-15 lines), and a new `src/client/shared/Spaarke.Auth/src/errorGuards.ts` (guards for `authenticatedFetch` throws; not a fix for M-14).

---

## 3. Corrections and additions to the record (facts only)

1. **§13a / x07 "the unmapped `/api/insights/ask`"** — the route is mapped (`InsightEndpoints.cs:81-95`, `EndpointMappingExtensions.cs:402`). The scripts fail because they post a **relative** URL (lands on the Dataverse origin) **without a bearer** (a02 D-3 already has this right).
2. **§11 H-4 "three KPI scripts build `/api/api/...` on dev"** — all **six** copies examined (packaged + source of `sprk_matter_kpi_refresh.js`, `sprk_subgrid_parent_rollup.js`; source of `sprk_kpi_subgrid_refresh.js`, `sprk_kpiassessment_quickcreate.js`) have zero `/api` stripping; only `assignedaccess_postsave`/`noaccessentry_postsave`/`accessstatus_banner`/`sprk_communication_send` strip it. Repackaging the v2.0.0 KPI sources therefore does **not** make them work on dev while `sprk_BffApiBaseUrl` ends in `/api`.
3. **§11 H-5 / D-27 "Task 123 fixed the source copies only"** — on `231c5ab2b` three of the four packaged copies are now fixed; **only `sprk_/js/registrationribbon.js` remains**, and its fix lives under a schema name (`sprk_registrationribbon.js`) that no solution or deploy script carries.
4. **New latent hazard:** `sprk_DocumentDelete.js` and `sprk_DocumentOperations.js` both own `Spaarke.Document.Config`; today harmless (DocumentDelete has no loader).
5. **Brief's `origin/master = 231c5ab2b`** is two commits behind the fetched `ebacd2e15`; nothing in the newer two affects the rows above beyond display rules.

---

## 4. Task 2 — Triage of §11 Medium, Low, H-8 and H-10

Classes (one per item): **FUNCTIONAL** = a user-visible feature is broken or a user gets wrong access today (dev or a provisioned Model 1 stamp); **LATENT-SECURITY** = no visible break, but an access/credential weakness that could be exploited or would mis-authorize; **HYGIENE** = dead code, docs, naming, drift. "Model 1 stamps" = does it affect a provisioned Model 1 customer stamp (**yes** / **no** / **dev-only**). Evidence lines are the record's unless re-opened here (marked †).

| # | Class | Why (one line) | Model 1 stamps |
|---|---|---|---|
| **H-8** | **FUNCTIONAL** | `ReportingAuthorizationFilter` requires a `roles` claim `sprk_ReportingAccess` (†`src/server/api/Sprk.Bff.Api/Api/Reporting/ReportingAuthorizationFilter.cs:81-87,154-169`); the dev BFF app defines only `Admin` (x04 A1) → every reporting caller is 403 on dev with `sprk_ReportingModuleEnabled=yes` (x05). | **yes** — no code path in the record defines `sprk_Reporting*` roles on any registration (H3 stamp apps expose `user_impersonation` only, §12a item 5); not re-verified here |
| **H-10** | **HYGIENE** (with a latent note) | Bot template is dead-but-deployable: `SpaarkeAgentHandler` injects only a logger, all services are TODO (†`…/Api/Agent/SpaarkeAgentHandler.cs:24-32`); the bot app `f257a0a9…` has no service principal yet is pre-authorized on the BFF app (x04 A9) and the template grants a KV read with no consumer — an unused grant/pre-auth, not a reachable path. | **no** (dev template; not in the stamp pipeline per record §8.2 — not re-verified) |
| M-1 | **LATENT-SECURITY** | CIAM plane gets the blanket `OutsideCounselModules` set with no map read (†`…/Infrastructure/ExternalAccess/ModuleEntitlementResolver.cs:94-98`) — contradicts A3; mis-authorizes any CIAM contact whose map would have narrowed Tier-1, but nothing visibly breaks. | **no** (CIAM plane only; stamps write no `Ciam__*`, a09 §2.5) |
| M-2 | **FUNCTIONAL*** (conditional) | Contact-only (U3) and guest outcomes hinge on `WorkforceIdentity:CustomerTenantIds`: dev lists Spaarke's own tenant (x04 §2), L2 refuses it on stamps (`CustomerWorkforceTenantsRule.cs:22-23,100-105`), and the Teams/SPA plane signs in on `/organizations` with no tenant pin (`msal-config.ts:157`) — the same Model 1 guest resolves to different member-test outcomes per environment and per IdP session. *Which token shape arrives is NEEDS LIVE TEST (x03 A2–A4); the divergence itself is code fact. | **yes** |
| M-3 | **FUNCTIONAL** (intermittent, add-in) | `OfficeNaaStrategy` ignores `requireSilentOnly` and the eager startup acquire can pop up without a gesture (`OfficeNaaStrategy.ts:218-256`; `AuthService.ts:100-104`) → popup-blocked silent sign-in failures on Office on the web; `accounts[0]` with no tenant filter picks the wrong account for multi-account guests. | **yes** (add-in is a Model 1 surface) |
| M-4 | **LATENT-SECURITY** | `/api/agent/*` validates no audience/app role — any default-scheme token with `oid`+`tid` passes (†`…/Api/Agent/AgentAuthorizationFilter.cs:80-81` TODO); one `Admin` app role unlocks `/api/spe/**`, `/api/admin/*`, registration approval (creates Entra users + licences), RAG admin, with no Dataverse identity or actor record (†`AuthorizationModule.cs:375-383`). | **yes** |
| M-5 | **LATENT-SECURITY** | Warning-level token logger on every `/api*` request ("remove before production"); the exception handler re-emits `Access-Control-Allow-Origin: <any Origin>` (†`MiddlewarePipelineExtensions.cs:88-94`); `*.powerappsportals.com` suffix allowed with credentials (`CorsModule.cs:101-116`). Log leakage + CORS widening, no visible break. | **yes** |
| M-6 | **LATENT-SECURITY** | ACS ingress accepts an optional `?sig=` + body `topic`; `disableLocalAuth` unset; a 24 h chat token per message; the stamp template assigns no UAMI role on the ACS resource (`acs-communication.bicep:70-77,97-128`) — the last would be a stamp **functional** gap if ACS chat is used there. | **yes** |
| M-7 | **LATENT-SECURITY** | `DataverseWebApiService` (the impersonation writer) builds its own un-pinned `DefaultAzureCredential` with inverted key precedence; 17 direct `TokenCredential` consumers bypass the `Graph:ManagedIdentity:Enabled` flag (`DataverseWebApiService.cs:56-64,106-117`) — identity selection is environment-dependent, not pinned; works on dev. | **yes** |
| M-8 | **FUNCTIONAL** (on a template redeploy / value-less import) | `SpaarkeMaster` env-var *definitions* default to Spaarke dev values (`environmentvariabledefinition.xml:2` ×4) → an import without value rows signs users into Spaarke's tenant against the dev BFF; `appsettings.template.json:37-42` carries one audience, so a redeploy from template drops the Teams-SSO and bare-GUID `aud` values that dev only accepts by hand-set `AzureAd__ValidAudiences__0..2` (x04 §2). The rest of M-8 (registry contradictions, GitHub FIC split, scope drift) is hygiene. | **yes** |
| M-9 | **HYGIENE** (design gap) | Model 2 is not provisionable (per-tenant `DefaultAzureCredential { TenantId }` for a Spaarke UAMI; keyless-proof role unassigned; H4 grant names a foreign principal) — documented in comments, not as an ADR tension; no user on a shipped path. | **no** (Model 2 only) |
| M-10 | **LATENT-SECURITY** | Service Bus as SAS string; soft-deleted KV names still referenced (x04 §4); Power BI client secret with no MI path; SignalR shared key; default credential order includes `ClientSecret` when the section is absent (`AuthorizationModule.cs:486-490`) — key-shaped credentials where ADR-028 A4 wants MI/FIC. Batch A adds: four secrets stored as plain App Service settings on dev (x06). | **yes** |
| M-11 | **LATENT-SECURITY** | `infra/insights/**`: per-tenant UAMI with KV Secrets User + Storage Blob Data Owner, account-key `AzureWebJobsStorage` (live: `allowSharedKeyAccess: true`, x06 T-P3-12), AI Search admin key handed to a script; no owner, no ArchTest. | **dev-only** (live objects are `insights-spaarkedev-*`; no stamp channel found in the record) |
| M-12 | **HYGIENE** (dead code; would mis-authorize if wired) | 23 `can*` policies with a wrong-domain extractor, `TenantRouting` deny-all router attached to nothing, `GrantMembershipAsync` uncalled, `AgentTokenService` unused (would cache raw tokens in Redis) — no live route reaches them. | **no** (dead) |
| M-13 | **LATENT-SECURITY** | Three `oid`→`systemuser` resolvers with different disabled-user semantics; notification pings resolve without `isdisabled` (`SystemUserIdentityResolver.cs:144-176,223`); RPA failures silently degrade every Write+ gate to deny with no metric — a disabled user can still be pinged, and outages look like permission denials. | **yes** |
| M-14 | **HYGIENE** | Library gaps (401 retry re-sends the same bearer; `VERSION` not bumped by #1453; `bffApiScope` unvalidated; `initAuth` has no `strategy` field so add-ins construct `SpaarkeAuthProvider` directly and `authenticatedFetch`/`useAuth` throw `not_initialized` there) — quality defects, no access impact; `231c5ab2b` adds `errorGuards.ts` but none of these. | n/a |
| M-15 | **FUNCTIONAL** (error shape, add-in) | `/api/office/communications/*` turns an OBO 401/403 into **500 "Lookup Failed"** (`CommunicationsEndpoints.cs:82-84,267-279`) so an auth failure is shown as a server error; the only deploy ships the token-decoding Diagnostics panel and the Email tab enabled (`deploy-office-addins.yml:76-87`); two raw `fetch` sites lack 401 retry. | **yes** (add-in) |
| M-16 | **LATENT-SECURITY** | RAG API-key caller chooses its own tenant (no `oid`/`tid`, `ApiKeyAuthenticationHandler.cs:89-97`); `TenantAuthorizationFilter` passes when no tenant is named (`:79-84`); `MembershipEndpoints.ExtractTenantId` defaults to `"anonymous"` (`:378-381`) — tenant-isolation seams that pass by omission. | **yes** |
| M-17 | **FUNCTIONAL** (stamps) | H7b provisions neither the Standing Grant Administrators FLS Read on `contact.sprk_standinggrant` nor the Access Administrator / Core User privilege edits (`SecureRecordSetupProcedure.cs:221,313,527-540,642-682`) → standing grants read as not held on a fresh stamp; **dev passes only because the BFF app users are System Administrator** (x07 T-P1-12 finding 1–2; the profile has no members live). | **yes** |
| M-18 | **LATENT-SECURITY** | Office JIT writer grant, SPE membership sync and the secure-child share synchroniser still read the record's own `sprk_accesspermission` after task 174 folded every other reader to effective flags (`OfficeEditAccessService.cs:231-239`; `SpeContainerMembershipSync.cs:442-443`; `SecureChildShareSynchronizer.cs:1025`) — mis-authorizes an external user on a child whose effective flag differs from its own; no live case verified. | **yes** |
| M-19 | **HYGIENE** | Dev Dataverse holds application users with no repo owner (`spe-api-dev-67e2xz` MI, `mi-ontology-writer-dev`), a disabled app user for a deleted app, and `github-actions-spe-infrastructure` on dev (x05 §B) — orphans; batch A adds the production BFF app `92ecc702…` as an enabled System Administrator in dev (x07), which is an owner decision rather than a break. | **dev-only** |
| M-20 | **LATENT-SECURITY** | `DataverseAccessDataSource.GetUserAccessAsync` sets the caller's OBO token on the shared `HttpClient.DefaultRequestHeaders` on the one path that decides document rights (`DataverseAccessDataSource.cs:298-300,453-457`); the registration is per-scope today (`SpaarkeCore.cs:62,107-115`) so the race needs concurrent use within one request — a latent identity race, not a visible break. | **yes** |
| L-1 | **HYGIENE** | `sprk_communication_send` packaged and deployed with no ribbon/form reference (§1.11); would work if invoked. | **yes** (ships in `SpaarkeMaster`, dead) |
| L-2 | **HYGIENE** | Dead/misleading client surface (`__SPAARKE_BFF_API_SCOPE__` unread, `createDataverseTokenProvider`/`ODataDataverseClient` uncalled, `usePlaybookOptions` posts to a deleted route behind an event nothing emits, SpaarkeAi standalone dev-host fallback outside Dataverse) — dormant. | **no** (dormant) |
| L-3 | **HYGIENE** | Teams/SPA polish (deep links not restored, sanity build omits `VITE_TEAMS_*`, stale comments, README dev-proxy guidance, `Deploy-PowerPages.ps1` still targets the SPA, single `FakeAuth` fixture) — pre-production surfaces with no production deploy (H-6). | **no** (no production deploy exists) |
| L-4 | **LATENT-SECURITY** | Anonymous demo registration creates rows and sends mail behind only a rate limit; `api-key-admin` policy on a JWT route degrades to per-IP; orphan `Email:WebhookSigningKey` / live `EmailProcessing__Webhook*` for a deleted route family (x04 §2); MIW pin drift; `/api/obo/*` and `*AsUser` names contradict the app-only identity. | **yes** (same code) |
| L-5 | **LATENT-SECURITY** (design) | External-surface writes carry no creator attribution; container-type grants are `full` for both identities; two delegation surfaces with different gates; context-free AI chat open to any default-scheme principal with `oid`+`tid`. | **yes** |
| L-6 | **HYGIENE** | Add-in packaging: legacy XML manifests still published; `outlook/manifest.json` conflates package and client id; `Access-Control-Allow-Origin: *` on static SWA responses; sign-out without server-side OBO cache invalidation. | **yes** (add-in), hygiene |
| L-7 | **HYGIENE** | ~120 STALE/CONTRADICTED auth claims across ADR-028, `.claude/constraints/auth.md`, `.claude/patterns/auth/*`, architecture docs (x02) — §14 disposition. | n/a |
| L-8 | **HYGIENE** (conditional FUNCTIONAL) | Sign-in identity precedence differs per PCF after #1453 (env-var-first in three, manifest-first in six; `environmentVariables.ts:340-350`); five `Communication*` manifests declare an unread `tenantId`. Becomes a wrong-app sign-in on a stamp **only if** a shipped form carries stale dev `clientAppId`/`bffAppId` values — `[LIVE?]`, not verified. | conditional (NEEDS LIVE TEST) |
| L-9 | **HYGIENE** | `ExternalParticipationService` takes a nullable filing reader; a null reader fails **closed** silently (`ExternalParticipationService.cs:333-348`) — safe direction, poor observability. | **no** |
| L-10 | **HYGIENE** (operational) | L2 hosts refuse to start without `ReservedTenants__*`; CIAM tenant id hand-copied; the T7 Spaarke-tenant guard is a silent no-op on a KV-reference `AzureAd__TenantId` (`ReservedTenantsOptions.cs:33-86`; `CustomerIdentityT7Probe.cs:255-272`). | **yes** (provisioning), hygiene |
| L-11 | **FUNCTIONAL** (dormant feature) | The admin-consent callback is gated on `Onboarding:Enabled`, which no repo producer sets and which is absent on dev (x04 §2) → the Microsoft redirect lands on a silent 401 (`EndpointMappingExtensions.cs:458-463`; `OnboardingModule.cs:58-62`); only matters when the consent flow is used. | **no** (not on the Model 1 path; stamp value unknown) |
| L-12 | **HYGIENE** | `config/spaarke-resources.yaml` still lists `5175798e…` as live; both `5175798e…` and `fd1325aa…` are gone from Entra (x04 A3, A5). | n/a |
| L-13 | **LATENT-SECURITY** | The GitHub OIDC app `8c85a481…` holds a password credential beside its four federated credentials (x04 A20; batch A: expired `rbac` secret, x06 T-P3-09) — an unneeded secret on a CI identity. | **dev-only** (CI) |
| L-14 | **LATENT-SECURITY** (low) | `spaarke-external-access-SPA` `f306885a…` is pre-authorized on the dev BFF app yet appears in no code, config or workflow (only a skill doc and retired notes) — a client that can mint BFF tokens without consent and without an owner. | **dev-only** |
| L-15 | **LATENT-SECURITY** (low) | Dev BFF app redirect URIs include two `oauth.pstmn.io` entries and `…/webresources/sprk_spaarkeai`; the PCF client lists `http://localhost`, `:8181` and Postman (x04 A1, A2) — redirect-URI hygiene on live apps. | **dev-only** |
| L-16 | **HYGIENE** (evidence gap) | `AgentToken__*`, `Rag__ApiKey`, `Onboarding__Enabled`, `PowerBi__*`, `TenantRouting__*`, `Notifications__SignalR__*`, `ServiceBus__*`, `Redis__Endpoint`, `AZURE_CLIENT_ID` not captured — §8.3 incomplete (batch A later confirmed `Onboarding__EnableDevBypass=true`, x06). | n/a |

### 4.1 Triage counts (38 items: H-8, H-10, M-1…M-20, L-1…L-16)

| Class | Count | Items |
|---|---|---|
| FUNCTIONAL | 7 | H-8, M-2* (conditional on an unproven token shape), M-3, M-8, M-15, M-17, L-11 |
| LATENT-SECURITY | 16 | M-1, M-4, M-5, M-6, M-7, M-10, M-11, M-13, M-16, M-18, M-20, L-4, L-5, L-13, L-14, L-15 |
| HYGIENE | 15 | H-10, M-9, M-12, M-14, M-19, L-1, L-2, L-3, L-6, L-7, L-8 (conditional FUNCTIONAL), L-9, L-10, L-12, L-16 |

Total 7 + 16 + 15 = 38.

Model 1 stamp impact: **yes** — H-8, M-2, M-3, M-4, M-5, M-6, M-7, M-8, M-10, M-13, M-15, M-16, M-17, M-18, M-20, L-1, L-4, L-5, L-6, L-10 (20); **conditional** — L-8; **dev-only** — M-11, M-19, L-13, L-14, L-15; **no / n-a** — the rest.

---

## 5. Evidence index (files opened in this pass)

Record: `auth-system-of-record.md` §11, §12, §13, §13a. Working: `a02-dataverse-surfaces.md` §1.2.5, §1.2.6, P4, §8 (D-3…D-29); `a05-bff-inbound.md:235,296`; `x04-live-entra-readout.md` A4; `x05-live-dataverse-readout.md` §A; `x06-live-batch-a.md` (findings); `x07-live-batch-a-dataverse.md` (all).
Code (worktree): `src/client/webresources/js/{sprk_emailactions,sprk_registrationribbon,sprk_aichatcontextmap_ribbon,sprk_DocumentOperations,sprk_updaterelated_commands,sprk_fieldmapping_push}.js`; `src/solutions/webresources/{sprk_matter_kpi_refresh,sprk_kpi_subgrid_refresh,sprk_subgrid_parent_rollup,sprk_kpiassessment_quickcreate}.js`; `src/dataverse/forms/sprk_matter/{insightCardMount,insightWidgetOnLoad}.ts`; `src/dataverse/solutions/SpaarkeMaster/WebResources/{sprk_emailactions.js,sprk_/js/registrationribbon.js,sprk_aichatcontextmap_ribbon.js,sprk_DocumentDelete.js,sprk_matter_kpi_refresh.js,sprk_kpi_subgrid_refresh.js,sprk_subgrid_parent_rollup.js,sprk_kpiassessment_quickcreate.js,sprk_communication_send,sprk_/scripts/matter_insight_*.js,sprk_matter_insight_*.js}`; `src/dataverse/solutions/SpaarkeMaster/Entities/{Email,sprk_registrationrequest,sprk_AIChatContextMap,sprk_Document,sprk_KPIAssessment,sprk_Communication,sprk_Matter,sprk_Project}/…`; `src/dataverse/solutions/SpaarkeMaster/Other/Solution.xml`; `src/client/pcf/ScopeConfigEditor/**`, `src/client/pcf/UpdateRelatedButton/**`; `src/server/api/Sprk.Bff.Api/Api/{Insights/InsightEndpoints,Ai/HandlerEndpoints,Ai/ChatEndpoints,RegistrationEndpoints,ScorecardCalculatorEndpoints,Finance/FinanceRollupEndpoints,FieldMappings/FieldMappingEndpoints,CommunicationEndpoints,Reporting/ReportingAuthorizationFilter,Agent/AgentAuthorizationFilter,Agent/SpaarkeAgentHandler}.cs`; `src/server/api/Sprk.Bff.Api/Infrastructure/{DI/EndpointMappingExtensions,DI/AuthorizationModule,DI/MiddlewarePipelineExtensions,ExternalAccess/ModuleEntitlementResolver}.cs`.
Git (read-only): `git diff --stat/--name-only 8a9ecaac1 231c5ab2b|origin/master -- …`, `git show 231c5ab2b:<path>`, `git grep <pat> 231c5ab2b -- src/server`, `git merge-base --is-ancestor 231c5ab2b origin/master`.
