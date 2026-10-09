# Live batch A — Dataverse results (dev, read-only)

> Captured 2026-10-09 via the Dataverse MCP (`read_query`, SELECT only) against the dev environment
> (`spaarkedev1`). Covers T-P1-10, T-P1-12 (steps 1–3), T-P3-05 (FLS part) and T-P3-04 (steps 1–2) of
> `live/live-test-plan.md`. Nothing was changed. True for dev at capture time only.

## T-P1-10 — Guest users and `sprk_isexternal` — **FAIL**

| Guest (`domainname`) | `sprk_isexternal` | disabled | Entra object id |
|---|---|---|---|
| `8fbf7c84…eyal_quantumfoundry.net#EXT#@spaarke.onmicrosoft.com` | Yes | **Disabled** | `8fbf7c84-c694-4f14-ab13-01bfca7202fe` |
| `ralph.schroeder_hotmail.com#EXT#@spaarke.onmicrosoft.com` | Yes | Enabled | `ad268fcd-ac34-4e40-b63f-dacdc849fcbb` |
| `ralph_deweycheatham.onmicrosoft.com#EXT#@spaarke.onmicrosoft.com` | **No** (explicitly false, not blank) | Enabled | `bc596ecd-b61c-43f7-8664-0f27a2267a67` |

- Pass criterion was "every guest `true`". The Model 1 test guest used by word-add-in-r1 (`ralph_deweycheatham…`,
  object `bc596ecd…` — the same guest object x04/x05 and the add-in notes cite) has `sprk_isexternal = No`.
- Consequence on dev: that guest is treated as internal on Restricted-record membership, internal shares,
  BU-container writer and internal-only messages (`SystemUserIdentityResolver.cs:302-316`). **x03 H-2 confirmed live.**
- The value is an explicit `false`, not null: something wrote "No" (the attribute default, or a manual edit). Code
  never writes the field (a07 P4 re-run at `8a9ecaac1` = 0 writers).

## T-P1-12 — BFF application users: roles, impersonation privilege, field security — **PASS with findings**

### Roles held

| Application user | Application id | Roles |
|---|---|---|
| `# mi-bff-api-dev` (the **active** BFF identity; `Graph__ManagedIdentity__Enabled=true`) | `5967251e-171c-46fe-a6c2-ef843c90309d` | **System Administrator** |
| `SDAP-BFF-SPE-API` | `1e40baad-e065-4aea-a8d4-4b7ab273458c` | **System Administrator**, Delegate, Spaarke Ontology Service |
| `# spaarke-bff-api-prod` (the **production** BFF app) | `92ecc702-d9ae-492d-957e-563244e93d8c` | **System Administrator** (in the **dev** environment) |

### `prvActOnBehalfOfAnotherUser`
Held at global depth (mask 8) by System Administrator and Delegate (plus 23 Microsoft platform roles). The active
app user therefore **can** impersonate: `MSCRMCallerID` reads succeed on dev. **Pass.**

### Field-security profile membership

| Profile | Members |
|---|---|
| Spaarke BFF-Managed Field **Writers** | `# mi-bff-api-dev`, `SDAP-BFF-SPE-API` (exactly the two BFF app users — **pass**) |
| Spaarke Identity Link **Writers** | `# mi-bff-api-dev`, `SDAP-BFF-SPE-API` (**pass**) |
| Spaarke BFF-Managed Field Readers | teams: Secure Record, Spaarke, Spaarke Business Unit 1, Spaarke Demo, Spaarke Dev 1, Spaarke Test 1 |
| Spaarke Identity Link Readers | teams: Secure Record, Spaarke, Spaarke Business Unit 1, Spaarke Demo, Spaarke Dev 1, Spaarke Test 1 |
| **Standing Grant Administrators** | **no users and no teams** |
| System Administrator | all System Administrator role holders, incl. the three BFF app users above |

### Findings
1. **Dev passes only because the BFF app users are System Administrators.** The impersonation privilege and every
   secured-field read (incl. `contact.sprk_standinggrant`) succeed through the System Administrator role and its
   field-security profile, not through the least-privilege configuration a provisioned stamp gets from H7b.
   Dev therefore does **not** exercise the stamp configuration.
2. **Standing Grant Administrators has no members.** It holds read/create/update on `contact.sprk_standinggrant`, but on any
   environment where the BFF identity is not System Administrator, standing grants would read as not held
   (`ContactStandingGrantReader` fails closed). This is the same gap as x03 M-17 (H7b does not add the profile).
3. **The production BFF app (`92ecc702…`) is an enabled System Administrator in dev.** A production identity holding the
   top role in a non-production environment needs an owner decision (intended, or remove).
4. **Least privilege:** all three BFF app users hold System Administrator, the broadest Dataverse role.
5. Step 4 (App Insights counts) is in `x06-live-batch-a.md`.

## T-P3-05 (FLS part) — Standing-grant field permission

`contact.sprk_standinggrant` field permissions: **System Administrator** (read/create/update) and **Standing Grant Administrators**
(read/create/update). No other profile. Combined with finding 2 above: today only System Administrator holders can read it.

## T-P3-04 — Which web-resource copies are live on dev (steps 1–2 of the test)

Deployed content was base64-decoded and compared byte-for-byte (whitespace-normalised) with every repo copy of the same
file name at `8a9ecaac1`.

| Deployed web resource | Last modified | Identical to | Auth-relevant markers in the deployed copy |
|---|---|---|---|
| `sprk_DocumentOperations.js` | 2026-10-08 22:51 | **source** `src/client/webresources/js/sprk_DocumentOperations.js` (task-123 fixed) | `user_impersonation`, `sprk_BffApiAppId`, `sprk_TenantId` env vars |
| `sprk_aichatcontextmap_ribbon.js` | 2026-10-08 22:51 | **source** `src/client/webresources/js/…` (fixed) | `user_impersonation`, env vars |
| `sprk_emailactions.js` | 2026-10-08 22:51 | **source** `src/client/webresources/js/…` (fixed) | `user_impersonation`, env vars; **still calls `/api/emails/convert-to-document`** (unmapped → 404, x03 H-4) |
| `sprk_/js/registrationribbon.js` | 2026-04-06 | **packaged** `SpaarkeMaster/WebResources/sprk_/js/registrationribbon.js` | **`SDAP.Access`, legacy client `b36e9b91`, hard-coded tenant `a221a95e`, retired host `spe-api-dev`** |
| `sprk_DocumentDelete.js` | 2026-04-06 | packaged (orphan) | `user_impersonation` against nonexistent tenant **`e2e89f3a`**, retired host `spe-api-dev` |
| `sprk_communication_send` | 2026-08-14 | packaged | `user_impersonation`, env vars |
| `sprk_matter_insight_onload.js` and `sprk_/scripts/matter_insight_onload.js` | 2026-06-11 / 06-16 | packaged | call **`/api/insights/ask`** (unmapped, x03 H-4) |
| `sprk_matter_kpi_refresh.js`, `sprk_kpi_subgrid_refresh.js`, `sprk_subgrid_parent_rollup.js` | 2026-04/05 | **packaged** (not the `src/solutions/webresources` copies) | **no `Authorization` header at all** (x03 H-4) |

Findings:
- **Partly fixed on dev:** task 123's fixes were deployed for three files.
- **The registration ribbon's fix was not deployed, and it was put under a different file name.** The live ribbon file is
  `sprk_/js/registrationribbon.js`, and it is the old packaged version with hard-coded dev identities and `SDAP.Access`.
  So x03 H-5 / a02 D-27 is **confirmed live on dev**.
- **Shipped callers that cannot succeed are live:** Archive Email (`/api/emails/convert-to-document`), the matter insight
  scripts (`/api/insights/ask`), and the KPI/rollup scripts with no bearer token. So x03 H-4 is **confirmed live on dev**.
### What actually loads them (T-P3-04 steps 2–3, `systemform.formxml` and `ribboncommand.commanddefinition`)

| Loader | Library wired | Consequence on dev |
|---|---|---|
| Email ribbon **Archive Email** (`sprk.Email.ArchiveEmail.Command`) | `sprk_emailactions.js` → `Spaarke.Email.saveToDocument` (fixed copy) | Signs in correctly but POSTs to the unmapped `/api/emails/convert-to-document` → **404**. **x03 H-4 is live and wired.** |
| Document ribbon **Delete** (`sprk.Document.Delete.Command`) | `sprk_DocumentOperations.js` → `Spaarke.Document.deleteDocument` (fixed copy) | Works on the fixed path. |
| Registration **Approve/Reject** (form + grid, `Sprk.Registration.*.Command`) | **`sprk_/js/registrationribbon.js`** (old packaged copy) | Signs in as the legacy client `b36e9b91` with `SDAP.Access` and a hard-coded tenant, and calls the **retired host `spe-api-dev`**. **x03 H-5 is live and wired: these buttons target a backend that no longer exists.** |
| **Matter main form** OnLoad | `matter_insight_onload` | Calls the unmapped `/api/insights/ask` (x03 H-4, live on the Matter form). |
| `sprk_DocumentDelete.js` | **not loaded by any form or ribbon command** | Orphaned file (dead config, nonexistent tenant); cleanup candidate, not a live path. |

Unmanaged ribbon customizations were checked through `ribboncommand` (the KPI "New" commands are the out-of-box
subgrid command and `sprk.SecureChild.*`). The KPI refresh scripts are form/subgrid-loaded; which forms load them was
not separately queried.
