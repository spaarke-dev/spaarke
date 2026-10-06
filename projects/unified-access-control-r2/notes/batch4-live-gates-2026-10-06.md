# Batch 4: dev live gates after the deploy (2026-10-06)

Run against spaarke-bff-dev at master `891cfd9a3`, then `c339d6920` (hotfix #1319). The runs were five parallel gate groups (G1 to G5) plus one investigation. Identities: admin (`az`), and testuser1@spaarke.com, the non-admin BU1 user, which played every non-admin role. uac.child was used only for impersonated reads. Every throwaway record was deleted; the SPE containers they created were kept for the owner's decision.

## Defects found and their status

| # | Defect | Class | Status |
|---|---|---|---|
| 1 | The No Access list filtered its lookups by logical name, so every deny-list read was a 400. Provisioning returned 500, and No Access checks and shares refused. | (a) | Fixed in #1319 and deployed |
| 2 | Event create returned 500 after writing the event, because the event log wrote `sprk_description`, a column that doesn't exist. A retry made a duplicate. | (a) | Fixed in #1319 and deployed; 159 (d) re-run PASS |
| 3 | `/no-access/enforce` returned 403 for every caller, because RPA is refused on an organization-owned table. | (a) | Fixed in #1320 (CI) |
| 4 | Dataverse refuses to revoke a share held by the record's current owner (0x80040223). Unsecure by the creator always reports `sweepComplete:false` and leaves a stale share; provisioning's undo hits the same refusal. | (a) | Fix in progress (revoke as the owner) |
| 5 | Upload into a secure container is a Graph 403. Internal document routes still call Graph as the user, and secure containers have no members (the owner's 2026-08-25 broker-only decision). | (a) | Owner decision pending (new task) |
| 6 | The provisioning field-mapping seeder has the same lookup-filter bug. | (a), other project | Issue #1318 |
| 7 | Insert-template returns raw XSLT for templates authored in the Dynamics editor. | pre-existing, email-r5 | Issue #1317 |

## Task outcomes from this run

- **Completed:**
  - 003, via 130's finance gate;
  - 132, gates (a) to (c);
  - 159, gates (a) to (f), with (d) re-run after #1319;
  - 160, the probes.
- **Gates passed, waiting on something else:**
  - 133: (e) waits for defect 4.
  - 148: G148-2 waits for defect 4.
  - 149: owner decision on whether a Web API GrantAccess stands in for the model-driven Share dialog.
  - 146: owner decision on G146-3, the alert app setting.
  - 156: AC8 passed. The upload landing is defect 5.
  - 158: round 58 re-runs through `/enforce` after #1320.
- **Remaining by-hand, CIAM or second-user gates:**
  - 137: observations.
  - 140: G-140-2.
  - 142: G-7 and the UX checks.
  - 143: gate 14, after task 154.
  - 147: G147-4 and G147-6.
  - 150: G-10 and the UI tests.
  - 157: the grid walk.
  - 162: (h), (i), (j) and (l).
- **Not yet inventoried:** 163 to 169.

The per-group records follow, verbatim.


---

# G1

## G1 results — verify scripts and read-only checks (2026-10-06)
Run from C:\wtG @ 891cfd9a3. Admin = az default login. Raw output under scratchpad\gates\g1out\.

### 133 — Set-RecordCreatorPersonSchema.ps1 -Verify — **PASS** (exit 0)
`-EnvironmentUrl https://spaarkedev1.crm.dynamics.com -BffApplicationIds 5967251e…,1e40baad… -Verify`. All (a)–(d) OK, incl. the rootcomponentbehavior=0 fix: the 3 columns + 3 relationships now report "in SpaarkeCore (its table includes subcomponents)" (the 2026-10-03 false negative is gone). Writers profile = only `# mi-bff-api-dev` + `SDAP-BFF-SPE-API`; Readers = all 6 BU default teams. INFO: sprk_project 0, **sprk_matter 2**, sprk_workassignment 0 app-created rows with no person recorded (informational; a resume of one refuses — guide 7a). Raw: g1out\133.txt.

### 143 — Set-NoAccessSystemUserSubjectSchema.ps1 -Verify — **PASS** (exit 0)
Column (lookup → systemuser), cascade (Delete RemoveLink only), column + relationship in SpaarkeCore via table subcomponents, data query selecting `_sprk_subjectsystemuser_value` answers. Raw: g1out\143.txt.

### 140 — Deploy-ExternalRecordAccessContactGrantor.ps1 -Verify — **PASS** (exit 0)
Script: lookup → contact, relationship `sprk_contact_sprk_externalrecordaccess_grantedbycontact` (nav sprk_GrantedByContact, no Assign/Share cascade, Delete RemoveLink); text(100); both field-secured with Readers+Writers permissions; in SpaarkeCore; backfill check 0 rows. Raw: g1out\140.txt.
Independent metadata read-back (admin Web API):
- `sprk_grantedbycontact`: AttributeType Lookup, Targets [contact], **IsSecured true**, MetadataId 65ecb659-be5d-4c0d-9c89-ea62aa5c7d37.
- `sprk_grantedbycontactid`: AttributeType String, MaxLength 100, **IsSecured true**, MetadataId 16816968-3fc1-f111-a05c-3833c5e9614d.
- SpaarkeCore: table sprk_externalrecordaccess (fbeb1369-…) is a solution component type 1 with **rootcomponentbehavior 0** (include subcomponents); the columns have no rows of their own → included implicitly. In SpaarkeCore: yes.

### 142/158 — Set-AssignedAccessLedgerSchema.ps1 -Verify — **PASS** (exit 0)
Table (OrganizationOwned), 6 columns, 8 lookups, alternate key `sprk_AssignedAccessLedgerKey` Active, table + every column/relationship/key in SpaarkeCore, write privileges only on Service Writer/Deleter + SysAdmin/SysCustomizer, BFF ledger select answers. Raw: g1out\142.txt.

### 150 — Set-SecureFlagFieldSecurity.ps1 -Verify — **PASS** (exit 0)
p1–p4, p6 OK; sprk_issecure field-secured on project/matter/work assignment/invoice; Readers read=4 create=0 update=0; Writers read/create/update=4; writers = the 2 BFF app users explicitly. INFO list of System Administrator holders (owner decision F4, accepted): TEAM 'Spaarke Demo' (every member), Ralph Schroeder, Delegated Admin, and platform/app users. Raw: g1out\150.txt.
#### 150 G-5 — dotnet test live field-security assertion — **PASS**
From C:\wtG: `SPAARKE_NFR05_DATAVERSE_URL=https://spaarkedev1.crm.dynamics.com SPAARKE_BFF_APPLICATION_IDS=<2 ids> AZURE_TOKEN_CREDENTIALS=AzureCliCredential SPAARKE_NFR05_REQUIRED=true dotnet test tests/unit/Sprk.Bff.Api.Tests --filter "FullyQualifiedName~SecureFlagFieldSecurity_InTheTargetEnvironment"` (REQUIRED=true added so an unconfigured run would fail instead of silently returning). Passed 1/1: "census … 4 of 4 table(s) secured, 12 field permission(s), 6 default team(s), 2 BFF application user(s) … PASS". Same F4 residual-writer list. Raw: g1out\150-G5.txt. (Build output created bin/obj under C:\wtG — no tracked files changed.)

### 148 — Invoke-SecureChildBackfill.ps1 -Verify + report-only trigger — **PASS**
- `-Verify` (BffBaseUrl https://spaarke-bff-dev.azurewebsites.net, ApiScope api://1e40baad…/.default): Run 1 secure records 1-1 of 1, passComplete=True; examined 0, changed 0, would change 0, refused 0, failed 0 → "VERIFY: PASS", exit 0. Reports g1out\148-verify\.
- Report-only dry run (default mode) trigger: run 8a968fc8-8113-4306-a2cb-1b828ed4b9c5, `mode report-only, rootsTotal 1, startPosition 1, passComplete true, wouldChange 0, changesTotal 0, needsF3 0, refused 0, failed 0`. Embedded write-mode sub-passes (by design: 147 recent-changes net + Make Secure relocation backstop): `recentChanges.rowsChanged 0, rootsFound 0`; `makeSecureRelocations.moved 0, stranded 0`. **wouldChange = 0** → no records to list. Reports g1out\148-dry\.

### 137 criterion 14 — external-access-reconciliation report-only run — **PASS**
- Status before (admin GET /api/admin/jobs/external-access-reconciliation/status): 200, `enabled true, cronSchedule "0 5 * * *", recentRuns [] , nextScheduledOn 2026-10-07T05:00Z`.
- Admin POST …/trigger → **202** runId `7657fc20-dcba-4772-888d-bef86dd2ba91`, started 2026-10-06T17:14:15Z.
- Status after: run trigger ManualAdmin, status **Succeeded**, 237 ms, processedItems 0, correlationId c174218bece94744942d9485d7c5bbf6.
- ResultJson: `status ok, mode "report-only", writesEnabled false, today 2026-10-06`. **Full per-rule report (for the owner's WritesEnabled decision):**

| Rule | Entity | Scanned | Planned | Changed | Failed | Other |
|---|---|---|---|---|---|---|
| R1-stamp-default-expiry | sprk_externalrecordaccess | 5 | 0 | 0 | 0 | contactIssued: stampedDefault 0, cappedByIssuer 0, deactivated 0, issuerDeleted 0, unresolved 0; claimHeld 0, alreadyApplied 0, markFailed 0, no truncation, no scan failure, sampleIds [] |
| R2-deactivate-grant-of-inactive-organization | sprk_externalrecordaccess | 5 | 0 | 0 | 0 | all other counters 0, sampleIds [] |
| R3-deactivate-ended-membership | sprk_contactorganization | 0 | 0 | 0 | 0 | all other counters 0 |

  Matches the note's hand classification (5 scanned grants, R1=R2=R3=0). With writes on, this run would have changed nothing.
- App Insights corroboration: trace 2026-10-06T17:14:15.30992Z `[EXT-ACCESS-RECON] heartbeat status=ok mode=report-only … r1Scanned=5 r1Planned=0 r1Changed=0 … r2Scanned=5 r2Planned=0 … r3Scanned=0`.
- App setting check (names only): no `*WritesEnabled*` setting for ExternalAccess (absent = false). (Only `IdentityLink__Reconciliation__WritesEnabled` exists.)
- Raw: g1out\137-a.txt, g1out\137-status.txt.

### 146 G146-3 — Communication__OwnershipHoldAlertUserIds__0 — **NOT SET (needs owner)**
`az webapp config appsettings list -g rg-spaarke-dev -n spaarke-bff-dev` filtered by name: **no setting whose name contains `OwnershipHold`** (count 0). Not in any appsettings*.json in the repo either. Per note §G146-3 the gate step is to SET it (`…__0=<admin systemuserid>`); setting app settings is an owner action (restarts the app) — not done. Effect while unset (per note): an ownership hold still dead-letters and logs Critical, but no `appnotification` is sent to anyone.

### 159 precondition — BFF application users hold prvActOnBehalfOfAnotherUser — **PASS**
privilege id ae5c41f0-e823-4cb9-b25a-8ef020201973.
- app 5967251e… → systemuser `# mi-bff-api-dev` 8793f4b0-01db-f011-8406-7c1e520aa4df (enabled, BU Spaarke): role System Administrator holds it (depth mask 8 = Global); RetrieveUserPrivileges: Global.
- app 1e40baad… → systemuser `SDAP-BFF-SPE-API` bb5a90e5-4ca8-f011-bbd3-7c1e5215b8b5 (enabled, BU Spaarke): roles Spaarke Ontology Service, System Administrator, Delegate; System Administrator and Delegate both hold it (mask 8); RetrieveUserPrivileges: Global.
Raw: g1out\159-pre.txt.

### 162 (m) — Repair-AnalysisAnchors.ps1 -Classify / -Verify — **PASS**
- `-Classify` (dry run): Active analyses 1009, anchorless 221, audit 452 deleted documents; per-writer/class breakdown recorded in g1out\162-classify.txt; **Derivable anchors (the -Apply plan): 0 row(s)** (one note: e8f49896's chat session names a document that no longer exists); "DRY RUN - nothing written", exit 0. (Matches note: 221 anchorless, 0 derivable.)
- `-Verify`: 0 derivable; "VERIFY PASSED: no derivable anchor is left unwritten…", **exit 0**. `-Apply` NOT run.

### 160 optional probes — deleted Dataverse proxy routes — **PASS**
As testuser1 (workforce BFF token):
- POST /api/dataverse/fetch → **404** (empty body)
- GET /api/dataverse/record/sprk_project/e070fa52-e662-f111-ab0c-000d3a4d8152 (a real project) → **404**
- GET /api/dataverse/record/sprk_project/00000000-0000-0000-0000-000000000001 → **404**
- (admin POST /api/dataverse/fetch → 404 too.) Token sanity: testuser1 on GET /api/admin/jobs/…/status → 403 (token admitted, so the 404s are route-not-mapped, not 401). Raw: g1out\160.txt.

### 132 (c) — task-132-live-gate.ps1 -Step Preflight — **RECORDED** (exit 0)
- (1) systemuser.sprk_primarycontact: **PRESENT**
- (2) spaarke-bff-dev `Redis__Enabled` = **true**; `Membership__CacheInvalidator__Enabled` = **not set** (appsettings default applies); `ExternalAccess__ImpersonatedRootSets__Enabled` = **not set** (appsettings default applies) — expected off; no code on master 891cfd9a3 references `ImpersonatedRootSets` (grep of src/server/api/Sprk.Bff.Api: 0 hits), i.e. 036 not merged, the flag is inert.
- (3) /healthz = 200; site last modified 2026-10-06T15:38:38Z.
Raw: g1out\132-preflight.txt.

---
### Records created + cleanup
None. Every step was read-only against Dataverse, except: two report-only `secure-child-reconciliation` runs (148 -Verify + dry run; in-memory run history, writes nothing in report-only; their write-mode sub-passes changed 0 rows) and one report-only `external-access-reconciliation` run (in-memory run record; no Dataverse write). No app settings changed, no restart, no -Apply. `dotnet test` created bin/obj build output under C:\wtG (untracked; no tracked file changed).

### Defects found
None.

### Needs the owner
1. **G146-3**: `Communication__OwnershipHoldAlertUserIds__0` is NOT set on spaarke-bff-dev (0 matching names). Setting it (an admin systemuserid, plus `__1…`) is an app-setting change → restart → owner decision.
2. **137 criterion 14**: report above (R1/R2 scanned 5, R3 scanned 0, all planned 0 / changed 0) is ready for the `ExternalAccess:Reconciliation:WritesEnabled` decision; turning writes on today would change nothing.
3. Informational: 133 `-Verify` reports 2 app-created `sprk_matter` rows with no `sprk_createdbyperson` (a resume of either refuses; guide 7a administrator recovery). 150/G-5 list the System Administrator residual writers incl. TEAM 'Spaarke Demo' (owner decision F4, already accepted).

---

# G2

## G2 results — BFF route authorization as testuser1 (2026-10-06, dev spaarkedev1 / spaarke-bff-dev @ 891cfd9a3)

Identities: U1 = testuser1 (systemuser 8d7bad7a, BU "Spaarke Business Unit 1"); admin = ralph.schroeder (1d02f31c).
Access confirmed with admin `RetrievePrincipalAccess` before each assertion. Helpers: scratchpad `g2/g2.py` (+ `s*.py` per gate).

### 003 -> 130 (d): GET /api/finance/matters/{M}/summary — **PASS**

| Matter | U1 RPA | U1 result | admin result |
|---|---|---|---|
| 2444af6d "Real estate transaction analysis" (BU1, has snapshots/invoices) | Read..Assign (full) | **200** currentSpend 1800, budget 1500, recentInvoices[INV-002 …] | 200 |
| a333c338 "Canadian Application" (BU1, no finance rows) | full | **404** "Financial Data Not Found" (documented no-data 404) | — |
| d14d79f4 "ONTOLOGY DEV SEED 005" (root BU, HAS finance data: admin 200 currentSpend 12000) | **None** | **403** `sdap.access.deny.insufficient_rights` "Access denied" | 200 |
| 0ee64da4 "NDA Review - Corner3" (root BU) | None | **403** insufficient_rights | — |
| random GUID | — | 403 insufficient_rights (same body) | — |

The strongest negative is d14d79f4: data exists (admin 200) but U1 without Read gets 403, no data in body.

### 159 Events API (U1 plays A and B)

Precondition — **PASS**: both BFF application users hold `prvActOnBehalfOfAnotherUser` at Global (`# mi-bff-api-dev` 8793f4b0, System Administrator; `SDAP-BFF-SPE-API` bb5a90e5, System Administrator).
Setup: E = 7f3fa397 "UAC gate 159 E 2026-10-06", created by admin (root BU "Spaarke", owner Ralph), Open; U1 RPA on E = **None**.
Matters: M = a333c338 "Canadian Application" (BU1, PAT-191111; U1 RPA full incl. AppendTo); MC = d14d79f4 (root BU; U1 RPA None).

- **(a) PASS** — `GET /api/v1/events?pageSize=100` as U1: 200, E absent (TotalCount 0 at the time; later 3, all other-agent G3 events owned by U1). Admin's list (66) contains E (control).
  `GET /api/v1/events/{E}`, `POST …/{E}/complete`, `GET …/{random}`, `POST …/{random}/complete` all 404 with the byte-identical body (minus correlationId): `{"title":"Not Found","status":404,"detail":"The requested record was not found.","reasonCode":"sdap.access.deny.record_unavailable"}`.
  E before/after: modifiedon 2026-10-06T17:15:57Z, statuscode 659490001, statecode 0, sprk_eventlog rows 0 → unchanged.
  Note: the list is owner-narrowed (`ownerUserId = caller`), so (a)'s list half is satisfied by owner narrowing as well as by the as-caller query.
- **(a′) PASS** — `PUT /{E}` 405, `DELETE /{E}` 405 (the path still has GET/complete/filing), `POST /{E}/cancel` 404, `GET /{E}/logs` 404.
- **(c) PASS** — `POST /api/v1/events` as U1 `{subject:"UAC gate 159 c 2026-10-06", regardingRecordType:1, regardingRecordId:MC}`: 403 `sdap.access.deny.insufficient_rights` "You do not have the rights this operation requires on a record it names."; 0 `sprk_event` rows with that subject. App Insights: `[DELEGATION] Caller holds prvCreatesprk_Event: True` then `[DELEGATION-RPA-UNAVAILABLE] RetrievePrincipalAccess returned 403 for sprk_matters(d14d79f4…)` → `[RECORD-ROUTE-AUTH] Denied … Holds None; requires AppendTo` (fail closed).
- **(d) FAIL — DEFECT D-1 (see Defects)** — `POST /api/v1/events` as U1 `{subject:"UAC gate 159 d 2026-10-06", regardingRecordType:1, regardingRecordId:M}` → **500** `{"title":"Internal Server Error","detail":"An error occurred while creating the event"}`, BUT the event row WAS created: E2 = 66a0b4b0. Read-back of E2 (the regarding shape itself is correct): `_sprk_regardingmatter_value` = a333c338 ✔, `_sprk_regardingrecordtype_value` = e8547bb4 (Matter) ✔, `sprk_regardingrecordid` = a333c338 ✔, `sprk_regardingrecordname` "Canadian Application" ✔, `sprk_regardingrecordurl` "/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=a333c338…" ✔, `sprk_regardingrecordnumber` PAT-191111 ✔, statuscode 659490001 / statecode 0 ✔, owner = BU1 default team cf15f587, `sprk_createdbyperson` = U1. Only the response status is wrong (and no event-log row was written).
- **(b) PASS** (on E2 from (d)) — U1 RPA on E2 = Read..Assign. `GET /api/v1/events/{E2}` 200 (subject, regardingRecordType 1 "Matter", statusCode 659490001 "Open"). `POST /{E2}/complete` 200 `{previousStatus:"Open", newStatus:"Completed"}`; read-back statuscode **659490002**, statecode **0**, modifiedon 2026-10-06T17:18:26Z.
- **(f) PASS** — `GET /api/v1/events?regardingRecordId=x%27%20or%20sprk_regardingrecordid%20ne%20%27zz` → 400 `errors.regardingRecordId: ["Regarding record id must be a GUID."]`.

### 162 AI analysis routes (U1 = testuser1)

Reference 404 (random GUID, U1 and admin identical): `{"title":"Not Found","status":404,"detail":"The requested record was not found.","reasonCode":"sdap.access.deny.record_unavailable"}`. "identical" below = same status and body minus correlationId/traceId.

- **(a) PASS** — analysis b571ad99 ("Document Profile - 2026-07-30", only anchor `sprk_documentid` = e9fb4575 "Invoice-10044725.pdf", root BU; U1 RPA on the doc **None**): U1 `GET /api/ai/analysis/{id}` → 404 identical to the random GUID. Control: admin → 200.
- **(b) PASS** — analysis a66f3cae (only anchor doc 8e6f3cae, BU1; U1 RPA Read..Assign): U1 → 200 (id, documentId, workingDocument, finalOutput, chatHistory…).
- **(c) PASS** — anchorless (221 rows today: Ralph 155, SDAP-BFF-SPE-API 65, mi-bff-api-dev 1; none has `sprk_createdbyperson`):
  - f46b4105 "Analysis Project" (createdby Ralph): U1 → 404 identical; admin (creator) → **200**.
  - 0e995a06 "Document Profile - 2026-01-27" (createdby SDAP-BFF-SPE-API, no person): U1 → 404 identical; admin → **404** identical. (Existing app-created rows were used — no simulation needed.)
- **(d) PASS** — U1 created its own session S1 9708483b (`POST /api/ai/chat/sessions {documentId: 8e6f3cae}` → 201), then `POST /api/ai/analysis/promote {sessionId:S1, name:"UAC gate 162 d 2026-10-06", regardingEntityType:"sprk_matter", regardingEntityId: d14d79f4 (U1 RPA None)}` → 403 `sdap.access.deny.insufficient_rights` "Access denied"; 0 `sprk_analysis` rows with that name.
- **(e) PASS** — admin session SA 27540afb (aichatsummary 4a6e4860, `_sprk_analysis_value` null). U1 `POST /promote {sessionId:SA, name:"UAC gate 162 e 2026-10-06"}` → 404 `{"error":"Session not found"}` — the same body as an unknown session id; 0 analysis rows with that name; SA's row unchanged (`_sprk_analysis_value` null, etag 27086352, modifiedon 17:21:43Z before and after).
- **(f) PASS** — throwaway doc d0449d75 "UAC gate 162 f" created by admin (root BU) and shared to U1 with **ReadAccess only** (`GrantAccess` 204; RPA = ReadAccess). U1 `POST /api/ai/analysis/execute {documentIds:[d0449d75], playbookId: 18cf3cc8 (Document Profile)}` → 403 `insufficient_rights`. App Insights: `[AI-AUTH] Access check PASSED … AccessRights=Read` then `Finance authorization DENIED … sprk_documents(d0449d75…) [body.documentIds] operation write reason sdap.access.deny.insufficient_rights` — refused before any execution. Doc modifiedon 17:22:13Z unchanged; 0 analyses on it. (Playbook not run.)
- **(g) PASS** — `POST /api/ai/analysis/fork` → **405** (Allow: GET — no POST handler on that path; route gone), `POST /{a66f3cae}/save` → 404, `POST /{a66f3cae}/export` → 404; same for U1 and admin.
- **(k) PASS** — U1 created its own doc 042c3592 via its own Dataverse token (RPA Read/Write/Append/AppendTo/Delete…). `POST /api/ai/analysis/create {name:"UAC gate 162 k 2026-10-06", documentId:042c3592, playbookId: Document Profile, skillIds:[62518925], knowledgeIds:[19552b15], toolIds:[d3fab651]}` (U1 ReadAccess on each scope row) → **201** analysisId a575f296; row owner = BU1 default team, `sprk_createdbyperson` = Test User 1, `sprk_documentid` = 042c3592. Same body on doc e9fb4575 (U1 RPA None) with name "…k-neg…" → **403** `insufficient_rights`; 0 rows with that name.
- **Delete probe — PASS** — cascade precondition read: `sprk_document_analysis_document` and `sprk_analysis_analysisoutput` both **Delete = Cascade** (Archive RemoveLink, others NoCascade) — already applied on dev. Admin added output 983d19a4 under analysis a575f296. U1 `DELETE sprk_documents(042c3592)` with its own Dataverse token → **204**; afterwards doc 404, analysis a575f296 404, output 983d19a4 404 (BFF GET of the analysis as admin 404). Unrelated anchorless analysis f46b4105 modifiedon 2026-08-18T03:07:39Z unchanged.
- (h)(i)(j)(l)(m): NOT RUN — not in G2 scope ((i)/(j) need uac.child token; (h)/(l) UI; (m) script gate).

### 157 (API part) — workforce token IS admitted on the external plane

`GET /api/v1/external/me` with U1's workforce BFF token → 200 `{contactId: ac6d7b68…, email: testuser1@spaarke.com, projects: []}` (dual-scheme group resolved U1 to its linked contact). So no CIAM token was needed.
- **(2) PASS** — `POST /api/v1/external/api/dataverse/fetch {entityName:"sprk_workassignment", fetchXml:"<fetch top='5'><entity name='sprk_workassignment'><attribute name='sprk_name'/><attribute name='sprk_assignedto'/></entity></fetch>"}` → **400** `errorCode DV_FETCHXML_COLUMN_NOT_PERMITTED`, "Refused: attribute 'sprk_assignedto'." Control (sprk_name only) → 200 `{entities:[]}` (U1 has no external grants, so empty).
- **(3) PASS** — `GET /api/v1/external/api/dataverse/savedqueries/sprk_project` → **404** `DV_SAVEDQUERY_NOT_FOUND`; `GET …/savedquery/195ab203-233e-4285-a34d-4acb5c0869ef` ("Active Projects", an MDA sprk_project view) → **404** `DV_SAVEDQUERY_NOT_FOUND`.
- Caveat: run with a workforce principal that has no project grants. The CIAM-contact variant (and the SPA/UI half, item 1) remains for the CIAM session.

### 142 — direct sync by a Read-only user — **PASS**

Throwaway matter d1e31fd3 "UAC gate 142 2026-10-06" (admin, root BU) shared to U1 with **ReadAccess only** (`GrantAccess` 204; RPA = ReadAccess; U1's finance summary on it → 404 no-data, i.e. Read accepted).
- U1 `POST /api/v1/external-access/assigned-access/sync {recordType:"matter", recordId:d1e31fd3}` → **403** `sdap.access.deny.delegation_write_required` "You must have Write access to this record to change who else can access it."
- Unknown record id → the identical 403 (enumeration-safe).
- Positive control: `ModifyAccess` to Read+Write (RPA ReadAccess, WriteAccess), waited 70 s (access-cache TTL), same call → **200** `{assignedAccess:{status:"evaluated", entries:[], writes:0, complete:true}, noAccess:[]}` — so the 403 is the Write rule, not a broken route. No ledger row was written for the matter.

### Records created + cleanup (all deleted; read-back 404 for each)

| Record | Created by | Purpose | Cleanup |
|---|---|---|---|
| sprk_event 7f3fa397 "UAC gate 159 E" | admin (Web API) | 159 (a) | deleted 204, read-back 404 |
| sprk_event 66a0b4b0 "UAC gate 159 d" (E2) | U1 via BFF POST /api/v1/events (the 500) | 159 (d)/(b) | deleted 204, read-back 404 |
| sprk_aichatsummary 3d6e4860 (session 9708483b) | U1 via POST /api/ai/chat/sessions | 162 (d) | deleted 204 |
| sprk_aichatsummary 4a6e4860 (session 27540afb) | admin via POST /api/ai/chat/sessions | 162 (e) | deleted 204 (the Redis/Cosmos session copies expire on their own TTL; not touched) |
| sprk_document d0449d75 "UAC gate 162 f" (+ Read share to U1) | admin | 162 (f) | deleted 204, read-back 404 |
| sprk_document 042c3592 "UAC gate 162 k+delete" | U1 (own Dataverse token) | 162 (k) + delete probe | deleted BY U1 in the probe (204), read-back 404 |
| sprk_analysis a575f296 "UAC gate 162 k" | U1 via BFF /create | 162 (k) + delete probe | removed by cascade, read-back 404 |
| sprk_analysisoutput 983d19a4 | admin | delete probe | removed by cascade, read-back 404 |
| sprk_matter d1e31fd3 "UAC gate 142" (+ U1 share) | admin | 142 | deleted 204, read-back 404 |

Final sweeps: 0 events named "UAC gate 159 *", 0 analyses / documents "UAC gate 162*", 0 matters "UAC gate 142". No existing record was modified (E, the unrelated analysis f46b4105, admin session row all verified unchanged where relevant). Not created: no sprk_eventlog rows (see D-1).

### Defects found

**D-1 — `POST /api/v1/events` returns 500 on every successful create; the event IS written. Class (a) runtime defect (broken real path), NOT security (fails after a correctly authorized write; no access widened).**
- Evidence: request as U1 `{subject:"UAC gate 159 d 2026-10-06", regardingRecordType:1, regardingRecordId:a333c338}` at 2026-10-06T17:16:47Z → 500 "An error occurred while creating the event"; row 66a0b4b0 created 17:16:48Z with the correct regarding set. App Insights operation b4798ba46ebd9294d6e00145d5fe45cb: `System.Net.Http.HttpRequestException 400` at `DataverseWebApiService.CreateEventLogAsync` (DataverseWebApiService.cs:769) ← `EventEndpoints.CreateEventInDataverseAsync` (EventEndpoints.cs:966) ← `CreateEventAsync` (:641).
- Root cause (confirmed by an admin Web API probe with the same payload shape, which created nothing): `CreateEventLogAsync` writes `sprk_description`, which **does not exist on `sprk_eventlog`** — Dataverse 0x80048d19 "Invalid property 'sprk_description' was found in entity 'Microsoft.Dynamics.CRM.sprk_eventlog'". Live custom columns on sprk_eventlog: sprk_action, sprk_createdbyperson, sprk_event, sprk_eventlogname, sprk_eventname (no description column). Dev has **0 sprk_eventlog rows in total** — the log write has never succeeded.
- Why only create breaks: create calls `dataverseService.CreateEventLogAsync` directly (no catch); complete goes through the `EventEndpoints.CreateEventLogAsync` helper that catches and logs a warning — so complete returns 200 (verified in (b)) and also silently writes no log.
- Impact: every Copilot `createEvent` (the published consumer) gets a 500 for a create that succeeded → user/agent retry makes duplicate events. Latent before task 159 (the payload line dates from b3f168184 "Events and Workflow Automation R1"); it became reachable when 159 fixed the regarding bind that used to fail first.
- Fix direction (not applied — repo edits forbidden here): drop `sprk_description` from the event-log payload (or add the column — schema decision), and decide whether a log-write failure after the event exists should fail the create.

**Observation O-1 (not a defect of the gates; for the owner):** `GET /api/v1/events` keeps the owner narrowing (`ownerUserId = caller`), and API-created events are owned by the BU default team (task 146). So an event U1 just created (E2, owner team cf15f587) does **not** appear in U1's own list, although U1 can GET it. Behaviour predates 159 ("owner narrowing kept"); flag only in case "my events" should include team-owned ones.

**Observation O-2 (minor):** in 159 (c) the AppendTo check logged `[DELEGATION-RPA-UNAVAILABLE] RetrievePrincipalAccess returned 403` for the unreadable matter — the OBO RetrievePrincipalAccess itself is refused for a record the caller cannot read; it fails closed into the correct 403, but the "unavailable" warning will appear for every ordinary no-access denial (log noise, not a behaviour issue).

### Needs the owner
- D-1 fix decision (payload column vs. schema; whether a post-create log failure should fail the request).
- O-1 only if team-owned events should show in a user's event list.

---

# G3

## G3 results — secure children: ownership, shares, reconciliation (dev, 2026-10-06)

Agent: gates-g3. BFF master 891cfd9a3 on spaarke-bff-dev. Evidence files: `gates/g3/` (logs, JSON).
Identities: admin (ralph.schroeder), T1 = testuser1 8d7bad7a-e39e-f011-bbd3-7c1e5217cd7c (creator),
N = uac.child d6f8f439-40bf-f111-a05b-3833c5e9614d (non-sharee, BU1), A = chelsea.friez c46d44ca-33bd-f111-aaaf-0022482913fc,
B = lori.witkin 2dddc606-42bd-f111-aaaf-0022482913fc, D = demo 849beec2-2b30-f111-88b5-7c1e520aa4df (A/B/D: root BU, Spaarke Core User, not admin).
Impersonated reads = admin token + `MSCRMCallerID`.

Jobs at start (17:16Z): `secure-child-reconciliation` enabled `*/2`, last run Succeeded, sweep report-only, recentChanges mode write, rootsTotal 1;
`secure-child-share-reconciliation` enabled `*/2`, last run Succeeded (Completed, inScope 0).

### 147 G147-3 — core-ancestor stamp backfill: PASS (with a deviation from the note's prediction, explained)

Run from C:\wtG (891cfd9a3), logs `g3/stamp-dry1.log`, `stamp-apply.log`, `stamp-dry2.log`. Run 17:13–17:16Z, BEFORE any G3 test data existed.
- Dry run 1: **0 to write**, 8 already correct, 0 conflicts, **4 unresolvable**, 0 errors; escalation banner (communication 2/7 = 28.6% unresolvable).
  The note predicted 4 to write + 2 unresolvable. Differences:
  - The 4 writes the note planned (to-do 2, communication 1, event 1) now read **SKIP-ALREADY-CORRECT** (to-dos b856b1ed / 15d2a80b → matter 2444af6d;
    communications 3b7b5825 / a0686224 → matter b68299c6; events 08021954, edfef460, 7300ed8f, 9e48e2b0) — already stamped since the note, most likely by task 156's
    5-minute core-ancestor job. Nothing left for the backfill to write.
  - 2 EXTRA unresolvable rows: to-dos `736a6eb7-9570-f111-ab0e-7ced8ddc4cc6` and `bc19baa5-9870-f111-ab0e-7ced8ddc4cc6` ("Overdue: Event", 2026-06-25), both regarding
    event `b52562e5-992e-f111-88b5-7ced8d1dc988`. Read live: that event has `_sprk_regardingrecordtype_value` = matter type but **no regarding lookup of any kind and no stamp**
    (every `_sprk_regarding*_value` null, `sprk_regardingrecordid` null). So its lineage holds no core ancestor and the to-dos' correct stamp is EMPTY — the same class as the note's
    two root-less communications (8428d06b → analysis 8128d06b; a36784ef → analysis 206fed82), which are unchanged. No write is owed for any of the four.
  - Every listed write (none) is a correct stamp per the note's rule, so -Apply was run.
- `-Apply -AcknowledgeEscalation`: "Apply complete: 0 written, 0 failed" (`APPLY-COMPLETE written=0 failed=0 deferred=0`), exit 0.
- Dry run 2: **0 to write**, same 4 unresolvable root-less rows, exit 0.
- For the owner (not a defect): the event b52562e5 is a data-quality residue (record type set, no regarding) — a Daily Briefing-era row; the two to-dos under it cannot be stamped until the event is filed.

### Environment finding that shapes every share test (read before G149)

Root-BU users read Secure-team-owned rows WITHOUT any share. Probe 17:22Z (`g3/g149_1.log`): root-BU users A (chelsea.friez) and B (lori.witkin) had
full rights (RetrievePrincipalAccess) on team-owned documents with **no POA row**, and on the probe root beyond its Read share; jake.schroeder (root BU,
NO direct role — only the root team's role) got Read+AppendTo on them. The Secure Record BU `d9ec0b6f…` is a child of the root BU `Spaarke`, and the
root team holds Spaarke Basic User — the deep-depth root reach the 146 note records as the **accepted dev finding #1081** (owner round 5). Not a new
defect. Consequence: root-BU users cannot serve as sharee subjects in dev. The only non-admin users outside the root BU are testuser1 and uac.child
(both BU1, both correctly DENIED on every team-owned row). So **T1 plays A and N plays B/D**, one at a time, and "non-shared user denied" is
shown with N in the phases where N holds no share. Owner awareness: in dev every root-BU user (7 enabled) reads every secure record and child.

### 149 G149-1 — Part A platform probes (admin Web API; reads impersonated): PASS

Run 2 (`g3/g149_1b.log`, 17:26:07–17:26:21Z, started 7 s after a job tick so no BFF job ran in the window). Probe roots (admin-created,
`sprk_issecure=true`, PATCHed to Secure Record Owners): project R `b5cf6b6e-aac1-f111-a05a-7c1e520a989f`, matter Mx `d6e67b78-aac1-f111-a05c-0022482913fc`,
work assignment Wx `d9449d75-aac1-f111-a05c-7c1e52154cae`. `organization.sharetopreviousowneronassign` = **false** (guide §7 5b holds).
1. GrantAccess R → T1 (Read): T1 reads R (200, RPA ReadAccess, POA mask 1).
2. Team-owned doc C1b `6fedb600…` (`sprk_Project` → R): T1 → **HTTP 403 0x80048306**, RPA none, no POA row. No platform cascade. ✔
3. GrantAccess R → N, THEN team-owned C2b `74edb600…`: N → **403**; RevokeAccess R → N: C2b still 403, R now 403 for N (POA row left at mask 0). ✔
4. **(g)** C3b `fb937d04…` owned by T1, GrantAccess → N (Read): N 200 (POA N mask 1). PATCH owner → Secure team: **N's share SURVIVES the Assign** (POA N mask 1
   unchanged, N reads 200, RPA ReadAccess); previous owner T1 gets **no** share (403) — consistent with sharetopreviousowneronassign=false.
   Task 148 consumes this: shares are not lost by an Assign (so re-own → mirror ordering is safe either way).
5. Same as step 2 for: matter child CMb `1a947d04…` (`sprk_Matter`) → 403; work assignment child CWb `28ff8908…` (`sprk_WorkAssignment`) → 403;
   `sprk_relatedproject`-only child CRb `33947d04…` → 403. ✔
(Run 1, 17:22Z, used root-BU users and is void as access evidence — see above; its POA facts agree: no child POA written by the platform, B's share on C3
survived its Assign. Run-1 probe ids: C1 f2dbb470, C2 7f449d75, C3 b311ab76, CM e1e67b78, CW f6e67b78, CR d658f07b.)

### BLOCKED: the BFF mirror is blocked by the coordinator's NoAccessListReader defect (confirmed independently)

Share job run 17:24:00Z (`runId 28e733e4-76e5-43ea-913d-23bce9ce2144`): **Failed / Incomplete** — inScope 5, notUpdated 5, granted 0 ("5 child(ren) not updated
and 0 held; the next run retries"). App Insights: `[NO-ACCESS] Deny-list query FAILED: BadRequest. Failing CLOSED` → `[NO-ACCESS-GUARD] Could not read the
deny-list needed to decide whether c46d44ca… is walled off sprk_project b5cf6b6e…; refusing the share (fail closed)` → `[SECURE-CHILD-SHARES] … not verifiable
against the No Access list … nothing is added for them there`. Fail-closed (under-share), as designed; same root cause as the main session's provisioning
500 `creator_no_access_unverifiable`. Every gate step that needs a child mirror, `/share-user` or provisioning waits for the fix.

**Hotfix #1319 (c339d6920) live:** share job run 18:02:00Z Succeeded (inScope 10, granted 20, notUpdated 0) — and that run mirrored the G149-1 probes: every child of
R / Mx / Wx (C1, C2, CR, C1b, C2b, CRb, CM/CMb, CW/CWb) now carries exactly R's/Mx's/Wx's Web API sharees at mask 1 (T1 and A, Read), N (revoked) nothing; C3b
(unfiled, outside any secure root) keeps N's own share untouched. App Insights: 164 "Deny-list query FAILED" traces 17:50–17:58:07Z, **none after the deploy**.

### 148 G148-1 — existing children follow the record on provisioning: PASS

Throwaway roots, created AS testuser1 (direct Web API, owner = T1, BU1), 17:18Z: project P `db0e65ec-a9c1-f111-a05c-3833c5e9614d`, matter M `b5bb11ed-a9c1-f111-a05a-7c1e520a989f`,
work assignment W `c01a70ec-a9c1-f111-a05c-0022482913fc`; under EACH, created as T1 (user-owned): document via the direct lookup (`sprk_Project`/`sprk_Matter`/`sprk_WorkAssignment`),
document via the related lookup (`sprk_relatedproject`/`sprk_relatedmatter`/`sprk_RelatedWorkAssignment`), event, to-do regarding the first document (grandchild),
communication, memo — ids in `g3/ids.json`. Baseline (`g3/baseline.txt`): every row owned by T1 and N (same BU1) held FULL rights on every one.
`POST /api/v1/external-access/provision-project {recordType, recordId}` as T1 (`g3/g148_1.log`, `prov_*.json`):
- P 18:02:58Z **200**, M 18:03:25Z **200**, W 18:03:48Z **200** — each `children: {status: Completed, reowned: 6, remaining: 0}` (communication 1, document 2, event 1,
  memo 1, to-do 1; refused/failed/needsF3 0), `sharedToCreatorSystemUserId` = T1, `resumed:false`, own containers: P `b!ZxwACqn6…`, M `b!HkqHE0XE…`, W `b!DuRySBK9…`.
- Read-back, all 18 children: `_owningteam_value` = **6eabc7f9 (Secure Record Owners)**; POA = exactly **T1 mask 23** (no Share); roots: T1 mask 262167 (Collaborate incl. Share).
- N (uac.child): `RetrievePrincipalAccess` = **none** and `GET` with MSCRMCallerID → **403** on all 3 roots and all 18 children. (The to-do filed only under a document — the
  grandchild — moved too.)

### 146 G146-4 — children created under the secure records through the BFF writer families: PASS

As T1 (non-admin, Collaborate+Share on each root), 18:04:55–18:05:43Z, ids `g3/ids146.json`, log `g3/g146_4.log`:
| Writer family | P (project) | M (matter) | W (work assignment) |
|---|---|---|---|
| Events API `POST /api/v1/events` (regardingRecordType 0/1/6) | 201 | 201 | 201 |
| Office `POST /api/office/todo` (regardingEntityType Project/Matter) | 201 | 201 | n/a (route takes Matter/Project/Invoice only) |
| `POST /api/v1/child-records/sprk_document` (direct lookup bind) | 201 | 201 | 201 |
| `POST /api/v1/child-records/sprk_memo` / `sprk_todo` / `sprk_event` (regarding bind) | 201 ×3 | 201 ×3 | 201 ×3 |
Read-back, all 17: `_owningteam_value` = **Secure Record Owners**, `sprk_createdbyperson` = **T1**, `createdby` = the BFF application user (A1 / G5), N: RetrievePrincipalAccess
**none** (non-sharee denied). Mirror: the child-records rows carried T1 at **mask 23** at once (inline mirror); the Events API and Office to-do rows had **no** POA right
after the create (T1 itself could not read them) and got T1 at mask 23 from the 18:06:00Z share run (Completed, inScope 51, granted 8) — ≤ 70 s, inside the ≤ 2-minute window
owner round 11 item 2 accepted for NEW children. (Observation for the owner, not a defect: on those two routes the CREATOR cannot open the row it just created for up to
2 minutes — the "Create then open" UX would 403 in that window; task 147's child-records route mirrors inline and has no such gap.)
Communications were not created through a BFF writer (the communication writers send/ingest mail — not run on dev); communications ARE covered under each root by G148-1.
Not run: §17i's "probe user can move THEIR app-created child out while a second Collaborate user cannot" — needs a second non-admin BU1 user with a token (only uac.child, no token).

### 149 G149-2 — sharees see exactly the secure record's children: PASS (Web API stand-in for the MDA Share dialog → OWNER DECISION)

Project P (provisioned above) and its 12 children (6 seeded families + 6 created through G146-4's writer families). Sharee = N (uac.child, the only non-admin non-root-BU user
besides the creator; sprk_isexternal false) in each role A/B/D in turn — so "non-shared user denied throughout" = N denied in every phase where N held no share. Log `g3/g149_2.log`.
- Baseline 18:06:57Z: N 403 on P and all 12 children, no POA.
- **Step 1** `/share-user` N Collaborate (as T1) 18:07:10Z → 200 (`outcome created`, root mask 262167). Immediately (18:07:17Z): N **200 on all 12 children, child mask 23** (no Share).
- **Step 2** `/share-user` N View → 200 (`updated`, root mask 1): every child **mask 1**, readable — immediate. `/share-user` N Full **as T1** → 200 `narrowed: true`,
  stays Collaborate (262167; children back to 23) — task 139's rule: T1 holds only Collaborate+Share, so it cannot grant Full (expected, not a FAIL). Repeated **as admin**:
  200 `created`, root mask 327703 → every child **mask 65559** (Delete, no Share) immediately.
- **Step 3** `/unshare-user` N → 200 `removed: true` (both as T1 and, after the admin Full, as admin): every child **403** immediately (POA rows left at mask 0).
- **Step 4 (MDA Share stand-in — owner decision whether equivalent):** Web API `GrantAccess` P → N (R/W/A/AT, root mask 23) 18:09:03Z: children 403 until the share job's
  18:10:00 run (Completed 18:10:10.5Z, granted 13) → N 200 on all children at mask 23 — **67 s**. `RevokeAccess` 18:10:17Z (just after that run): children readable until
  the 18:12:00 run (Completed 18:12:08Z, revoked 13) → all 403 — **1 min 51 s**. Both inside the ≤ 2-minute window (owner round 11 item 2). The worst case is one tick interval
  plus the run's duration (8–10 s here).
- **Step 5** N (no share) denied in the baseline and after each unshare/revoke — above.
- **Step 6** `/api/admin/jobs/secure-child-share-reconciliation/status`: enabled, `*/2 * * * *`, lastRunStatus **Succeeded**; App Insights heartbeats `[SECURE-CHILD-SHARES] heartbeat
  status=Completed` (runIds 577b84a9…, f21a76db…).
- Owner decision needed: is a Web API `GrantAccess`/`RevokeAccess` on the root an acceptable stand-in for the model-driven Share dialog? (It writes the same POA row the dialog
  writes; the dialog itself was not driven.)

### 147 G147-2 — out-of-product create under a secure record: PASS

Precondition: the 148 backfill was not re-run by me. Job status shows `recentChanges.catchUp {ran:false, complete:true}` (the catch-up already finished on this instance);
schedule `*/2`, enabled. Probe (`g3/g147_2.log`): T1, shared on P, `POST /api/data/v9.2/sprk_todos` WITH ITS OWN Dataverse token, `sprk_RegardingProject` → P, 18:13:13Z
→ 201 `04192e96-b1c1-f111-a05c-7c1e52154cae`. Before the run: owned by **user T1**, no POA; N (same BU1) had full rights. That is the out-of-product exposure window the
L4 net bounds. Run 18:14:00Z (Succeeded): `recentChanges {mode: write, corrected: 1, rowsChanged: 4, rootsFound: 1, roots: [sprk_project:db0e65ec…]}`,
`changes: [{pass: recent, table: sprk_todo, id: 04192e96…, previousOwner: systemusers(8d7bad7a…), targetTeam: 6eabc7f9…, outcome: Changed}]`. After: owner **Secure Record
Owners**, POA **T1 mask 23**, N: RetrievePrincipalAccess none, GET-as-N **403**. Probe deleted 18:14:3xZ (204, read-back 404).

### 148 G148-2 — unsecure: FAIL-by-defect D1 (the child pass PASSES; the root share sweep fails)

`POST /api/v1/external-access/unsecure-project {recordType: matter, recordId: M}` as T1 (creator → F3 `Creator`), 18:14:43Z → **200**: `newOwnerSystemUserId` T1,
`children {status: Completed, reowned: 13, remaining: 0}` (communication 1, document 3, event 3, **eventlog 1** (the Events API's log row), memo 2, to-do 3), **`sharesRevoked: 0`,
`sweepComplete: false`**. Log `g3/g148_2.log`, App Insights `g3/ai_unsecure.json` (TraceId `0HNP3QEEAB77J:0000004B`).
- Children ✔: all 12 tracked children (+ the eventlog) owned by the **BU1 default team** `cf15f587-baa0-f111-aaac-000d3a99d1d7`; **no child POA row with a mask > 0** (the 13 mirrors
  revoked). `sprk_issecure` **false** ✔; root owned by user T1, container kept.
- Order ✔: `[SECURE-CHILD-RECONCILE] reassign:` ×13 18:14:50.3–18:14:54.1Z → child mirror RevokeAccess ×13 18:14:54.9–18:15:02.7Z → reconcile summary (`unsecuring=True
  mayRelease=True status=Completed changed=13 mirrorRevoked=13`) 18:15:02.75Z → root RevokeAccess 18:15:02.85Z → flag PATCH 18:15:03.08Z → `[UNSECURE] … sharesRevoked=0,
  sweepComplete=False` 18:15:03.23Z. No privilege refusal (POML trigger 2 did not fire).
- **Root's shares gone ✘**: T1's share (262167) SURVIVES on the matter. The one root RevokeAccess → **400**. Reproduced by hand: **`0x80040223 "Only owner can revoke access to the
  owner. CallerId: <admin>, OwnerId: 8d7bad7a…"`**. Step 3 assigns the record to the caller (T1), and Step 4's `RevokeAllSharesAsync` then tries to revoke the new OWNER's own
  share, which Dataverse allows only the owner to do.
- **Defect D1 (class: low-severity runtime defect, also maintainability; not fail-open):** every creator-driven unsecure (the creator is ALWAYS shared at provisioning) returns
  `sweepComplete: false` and leaves the new owner's explicit share. No over-share now (the owner reads it anyway). Latent: if the record is later reassigned, the stale share
  keeps the former owner Collaborate+Share; and an operator reading `sweepComplete:false` cannot tell this benign case from a real stranded share. Fix shape (not applied):
  exclude the new owner's principal from the Step 4 sweep (ownership covers it), or revoke it before the Step 3 assign. Reported to the main session (~18:17Z); main confirmed the same 0x80040223 root cause G4 hit in 133(e), now fixing it (revoke the owner's own share AS the owner via MSCRMCallerID).

### 148 G148-4 — cleanup: PASS

18:17–18:18Z (`g3/cleanup.log`), as admin, children first, then roots; every DELETE 204 and read-back **404 — 56 of 56**, plus the G147-2 probe to-do (204/404) = 57 rows:
3 G148 roots (P db0e65ec, M b5bb11ed, W c01a70ec) + their 18 seeded children; 17 G146-4 writer-family children + 3 `sprk_eventlog` rows the Events API wrote
(f9880b68, 154d2574, ba86ea80); G149-1 probe roots R b5cf6b6e / Mx d6e67b78 / Wx d9449d75 + 12 probe documents. No `sprk_assignedaccess` or `sharepointdocumentlocation` rows
referenced the roots. The roots were deleted while still secure (P, W) or after unsecure (M); no unsecure was needed for deletion. After cleanup, the 18:20Z runs:
share job Completed inScope 0, sweep rootsTotal 1 (only 65a3fab2, which I did not touch).
**Residue (not deleted — Graph writes not in scope; owner decision):** the 3 SPE containers provisioning created: P `b!ZxwACqn6EEKP99-lPgOiBhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`,
M `b!HkqHE0XETUCPMjmQJXU3YBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN`, W `b!DuRySBK9FkKk2wSDxRigwhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN` (empty: files NoFile ×2 each). Now orphaned.

---

### Summary

| Gate | Result |
|---|---|
| 147 G147-3 stamp backfill (dry → -Apply -AcknowledgeEscalation → dry) | **PASS**: 0 written (the note's 4 already stamped); 4 root-less unresolvable rows, 2 more than the note predicted, both correctly empty |
| 149 G149-1 platform probes (a)-(g), matter / WA / relatedproject | **PASS**: no platform cascade; a share survives an Assign; sharetopreviousowneronassign false |
| 148 G148-1 provisioning re-owns existing children | **PASS**: 3 × 200, 18/18 Secure-team-owned, creator mask 23, N denied |
| 146 G146-4 writer families under secure roots | **PASS** (17 rows, 3 routes); communications covered by G148-1; "move-out by a 2nd Collaborate user" NOT RUN (no 2nd BU1 user token) |
| 149 G149-2 share fan-out | **PASS**: 23 / 1 / 65559 / denied, all immediate; Web API share 67 s, revoke 1 min 51 s; job Succeeded / Completed. MDA-dialog equivalence = owner decision |
| 147 G147-2 out-of-product probe | **PASS**: user-owned → Secure team at the next run, `recentChanges.corrected 1` with previousOwner; N denied |
| 148 G148-2 unsecure | **FAIL-by-defect D1**: children / order / flag PASS; the new owner's root share cannot be revoked → `sweepComplete:false` |
| 148 G148-4 cleanup | **PASS**: 57 rows deleted and verified; 3 SPE containers left (residue) |

**Defects**
- **D0** (main session's; confirmed independently; fixed by #1319 c339d6920 at ~18:00Z): NoAccessListReader filters → 400 → every child grant refused fail-closed
  (share job runs 17:24–17:58Z Failed, granted 0). Class (a) broken real path, fail-closed.
- **D1** (`UnsecureProjectEndpoint` Step 4): Dataverse 0x80040223 "Only owner can revoke access to the owner" when the unsecure's new owner (the caller / creator) holds a share.
  Class: low-severity runtime defect (normal path reports `sweepComplete:false`; a stale explicit share is left for the new owner, which becomes a latent access path after a later
  reassign). Not fail-open. Main session is fixing it.

**For the owner**
1. In dev, every root-BU user (7 enabled: chelsea.friez, lori.witkin, demo, final.test, eyal, jake, e2e.test2, plus the hotmail guest) reads every Secure-team-owned record and child
   without a share: the root team's Spaarke Basic User reaches the child Secure Record BU (the accepted #1081 dev finding). It is why root-BU users could not be used as sharees.
   It also means dev cannot exercise multi-sharee isolation beyond the two BU1 users.
2. G149-2 step 4 used Web API GrantAccess/RevokeAccess as the stand-in for the model-driven Share dialog. Accept as equivalent, or run the dialog by hand.
3. The Events API (`POST /api/v1/events`) and Office `POST /api/office/todo` create Secure-team-owned rows WITHOUT an inline mirror, so the creator cannot open its own new row for up to
   2 minutes (within the accepted window; the child-records route mirrors inline). Decide whether that UX gap matters.
4. The 3 orphan SPE containers above: delete, or keep.
5. Data residue: event b52562e5 (record type set, no regarding) keeps two to-dos unstampable.

**Re-run after the D1 fix deploys:** **G148-2** only. Use a fresh throwaway matter (or project) created as testuser1 with a few children → provision as T1 → unsecure as T1; expect
`sweepComplete: true`, `sharesRevoked` ≥ 1, and NO POA row left on the root (the owner's own share gone), plus the child checks as above. Then G148-4 cleanup for those probes.
Nothing else in G3 depends on the Step 4 sweep.

---

# G4

## G4 results — 132 (a)(b), 133 (a)-(j), 047 API half (2026-10-06)
Run from C:\wtG @ 891cfd9a3 (scripts), BFF dev @ 891cfd9a3. Admin = az default login; testuser1 = 8d7bad7a (BU1, non-admin).
Helpers: scratchpad\gates\g4\ (g4.py + per-step scripts, raw outputs *.out).

### Baseline (read-only, 2026-10-06 ~17:15Z)
- Business-unit containers: Spaarke (root) = b!vzGDfDpd7km_-_H38Q6Zfbot… ; Spaarke Business Unit 1 = b!vzGDfDpd7km_… (same); Spaarke Demo = b!yLRdWEOAdkaWXskuRfByIRiz…; Secure Record / Spaarke Dev 1 / Spaarke Test 1 = null.
- App setting SharePointEmbedded__DefaultContainerId = b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh (container id, not a secret).
- Secure Record Owners team 6eabc7f9 (non-default, BU Secure Record d9ec0b6f). organization.sharetopreviousowneronassign = False.
- testuser1 team memberships: only "Spaarke Business Unit 1" (cf15f587). uac.child = d6f8f439-40bf-f111-a05b-3833c5e9614d (BU1).
- Observation: testuser1's Teams/SPA lists (GET /api/v1/external/projects, /me) are EMPTY — the workforce composition is accessConferringOnly (FR-24), and ownerid/owningbusinessunit are not conferring roles; [WF-AUTH] "0 project / 0 matter / 0 work-assignment accessible roots". GET /api/users/me/memberships/{entity} (membership-resolved cache, the cache §9 row 3 bounds) lists 59 matters / 18 projects. 132 polling therefore uses the memberships route (+ RetrievePrincipalAccess), recorded as the instrument.

### 132 (C12 access caches)
#### (a) ReassignMatter — PASS
- Throwaway matter M132 = af0d7197-a9c1-f111-a05a-7c1e520a989f "UAC gate 132a 2026-10-06" (admin create, owner team Spaarke Business Unit 1 cf15f587).
- Warm: testuser1 GET /api/users/me/memberships/sprk_matter 17:16:25Z → 60 ids, M132 present (it was absent at 17:16:08, i.e. the pre-create entry lived its 2-min TTL — consistent with §7). RPA before = Read/Write/Append/AppendTo/Create/Delete/Share/Assign.
- First -Apply to "Spaarke Test 1" team refused by Dataverse (0x80042f0a, team holds no prvReadsprk_Matter) — harness choice, nothing written; second -Apply kept the recorded original owner (round 48 (d) bookkeeping worked live: "original owner already recorded … KEPT").
- Reassigned to team Spaarke Demo (9471b764, other BU) at 17:17:10.81Z. Poll every ~15 s: present at 17:17:20…17:18:24, ABSENT at 17:18:40Z (count 60→… M132 gone) = **~1.5 min** after the reassign (bound ≤ 4 min). RPA(testuser1) after = None.
- -Verify: owner Spaarke Demo; RPA = None. RestoreMatter -Apply 17:18:55Z → owner cf15f587; -Verify "restored: YES".
- Minor harness defect (class f): -Verify printed "minutes since the reassign = -238.3" — the state's ISO timestamp is re-parsed as local time (UTC−4 offset ≈ 240 min). The real elapsed time is from the timestamps above. Script-only, no product impact.
- Instrument note: the Teams/SPA lists are empty for testuser1 (see baseline), so the membership route was polled; it reads the same membership-resolved cache the 4-min bound covers.

### 133 (C11 provisioning lock-out)
#### (a)(b)(e) ShareFirstProof — (a) PASS, (b) PASS, (e) FAIL on the share half (platform refusal; see defect D1)
- The repo script as written threw before its first Dataverse call after the create: `Cannot convert "System.Object[]" to System.Guid` (line 103 — under PowerShell 7 `$resp.Headers['OData-EntityId']` is a string[]; `-replace` returns an array). Harness defect (class f). The probe had been created: P1 = 97ee9215-aac1-f111-a05c-7c1e52154cae "TASK133-PROBE-20261006131931" (owner testuser1, issecure True, no container). Continued on P1 with a scratch copy (`g4\t133.ps1`: only change = take -RecordId as the existing probe, `@(...)[0]` on the header, repoRoot C:\wtG) — the call sequence is byte-for-byte the repo script's.
- CreatorAccessRights (from source) = ReadAccess,WriteAccess,AppendAccess,AppendToAccess,ShareAccess; sharetopreviousowneronassign = False.
- pre-call: owner user 8d7bad7a, no POA rows.
- **(a)** GrantAccess to the CURRENT owner: **ACCEPTED** → POA 8d7bad7a mask 262167 (= CreatorAccessMask).
- **(b)** owner PATCH → team 6eabc7f9 (read back): POA 8d7bad7a **still mask 262167** — the share survives the move.
- **(e)** compensating move back → owner user 8d7bad7a (read back) ✔; then RevokeAccess of the share → **HTTP 400 0x80040223 "Only owner can revoke access to the owner. CallerId: 1d02f31c (admin), OwnerId: 8d7bad7a"**. After-restore state: owner 8d7bad7a, issecure True, **POA 8d7bad7a mask 262167 still present** → does NOT equal pre-call. Reproduced the same call standalone (same 400). The same RevokeAccess sent with `MSCRMCallerID: 8d7bad7a` (impersonating the owner) → 204, mask 0.
- P1 deleted 17:2xZ (DELETE 204, GET → 404).
- **Disposition (main session, 2026-10-06):** recorded as a KNOWN LIMIT, class (e) — fail-safe; the endpoint answers honestly (`sharesRestored: false`, "could not be confirmed removed; it shows under Manage Access"); only the owner keeps a redundant share of their own record. No code change now; owner to decide. Impact as read from the code: `ProvisionProjectEndpoint.RestoreCreatorShareAsync` with targetMask 0 on a record the creator owns is hit on (1) the NotMoved path (owner move refused/ignored after share-first) and (2) compensation after a move back — both end with the creator's self-share at 262167 left in place. Residual: that self-share survives a later reassignment of the record (sharetopreviousowneronassign=False would otherwise remove the previous owner's access). Possible fixes (not applied): revoke impersonating the owner (MSCRMCallerID, shown working above), or treat a self-share on a creator-owned record as equivalent to mask 0.

#### Provisioning-dependent gates — BLOCKED pending redeploy
Main session confirmed (2026-10-06 ~17:20Z) a dev defect: NoAccessListReader filters on lookup logical names → Dataverse 400 → every deny-list read fails closed → provision-project returns 500 `sdap.provision.creator_no_access_unverifiable`. Main is fixing + redeploying. Records prepared and kept for the post-fix run:
- PB = ae525172-aac1-f111-a05c-0022482913fc "UAC gate 133b 2026-10-06" (testuser1 Web API create, owner testuser1, container null at create, externalaccount null).
- PI = 83449d75-aac1-f111-a05c-7c1e52154cae "UAC gate 133i 2026-10-06" (testuser1 create; testuser1 then PATCHed sprk_containerid = BU1/root/Default container b!vzGD… — the wizard-cascade shape).
- PK = 0d29ab75-aac1-f111-a05a-7c1e520a989f "UAC gate 132b 2026-10-06" (admin create, owner team BU1 cf15f587).
- PG = 53f34066-aac1-f111-a05c-0022482913fc "UAC gate 133g 2026-10-06" — see (g) below.

#### (g) part 1 (no provisioning needed) — Office quick-create as testuser1: PASS
- testuser1 POST /api/office/quickcreate/project {"name":"UAC gate 133g 2026-10-06"} + X-Idempotency-Key → 201, id 53f34066-aac1-f111-a05c-0022482913fc.
- Inspect: owner team cf15f587 (BU1 default), container null, issecure False, createdby 8793f4b0 (BFF app user — app-only create), **sprk_createdbyperson = 8d7bad7a (testuser1)** ✔; POA 8d7bad7a mask 262167; RPA(testuser1) full.

### 047 (API half) — instrument check (pre-provisioning)
- Admin Graph token (az CLI, tenant a221a95e): GET /storage/fileStorage/containers/{id} → 400 "Invalid hostname for this tenancy" / list → 403 accessDenied (no FileStorageContainer scope) — **unusable**, as the POML predicted.
- BFF SPE-admin routes (configId c3a25b9a "Spaarke PAYGO 1", type 8a6ce34c): GET /api/spe/containers/{BU1 container} → 200 (active, "Spaarke Dev Container 2"); random id → 404; **the secure container b!MVasATu… (65a3fab2) and orphan b!HBRbo… → 503 `spe.admin.deny.scope_unverifiable` "Try again shortly"** (App Insights: "SPE Admin container scope UNVERIFIABLE … refusing", deterministic, repeated); list route returns 5 containers, secure ones absent (scope-filtered). So the SPE-admin route cannot confirm a secure container either. Observation for the owner / the 165 lane (not this group's gate): a secure-record container (bound to no business unit) gets a permanent 503 with retry advice on the admin get-by-id route — class (e)/(f): fails closed, but the "try again shortly" copy is wrong for a deterministic state.
- Plan for existence: after provisioning, testuser1 POST /api/obo/records/sprk_project/{id}/upload-session (gated, creates an empty Graph upload session in the record's container; no bytes uploaded).

### Post-hotfix run (BFF #1319 c339d6920; app start in App Insights 17:59:47Z)
- Preconditions: last `[NO-ACCESS] Deny-list query FAILED` = 17:58:07Z (2,015 since 15:35Z), **none after the 17:59:47 start**; first provisioning (below) 200.

#### 133 (b) — test user provisions a fresh secure project on the API path: PASS
- PB ae525172 created by testuser1 (Web API, own token), unflagged, owner testuser1, no POA, container null.
- testuser1 POST /api/v1/external-access/provision-project {recordType:project, recordId:PB} at 18:00:45Z → **200** (26 s): ownerTeamId 6eabc7f9, speContainerId **C1 = b!ECpwsE3P2Em14S3B84uYOxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN**, sharedToCreatorSystemUserId 8d7bad7a, resumed false. App Insights: [DELEGATION] ALLOWED → issecure set (read back) → share-first ([ACCESS-EVICT] share change) → owner moved (read back) → [ACCESS-EVICT] owner change → container created → recorded.
- Inspect: owner team 6eabc7f9, issecure True, container C1; POA exactly one row: 8d7bad7a mask **262167** (= CreatorAccessRights R/W/Append/AppendTo/Share); RPA(testuser1) = Read, Write, Append, AppendTo, Share (exactly the creator rights; Create/Delete/Assign gone).
- Side note: sprk_createdbyperson stays null on a Web-API-created row (only BFF create paths stamp it) — as designed.

#### 047 (API half) on PB: PASS except Graph existence (PARTIAL)
- C1 ≠ every business-unit container (root/BU1 b!vzGD…, Demo b!yLRd…, others null) and ≠ SharePointEmbedded__DefaultContainerId (b!vzGD…) ✔. Recorded on exactly 1 project, 0 matters, 0 WAs.
- _owningteam_value = 6eabc7f9 (Secure Record Owners) ✔. sprk_externalaccount null before and after (unchanged) ✔. Business units 6 before/after; accounts created today 0 ✔.
- Re-post (= 133 (d)): testuser1 18:01:26Z → **409 `sdap.provision.already_provisioned`**, speContainerId C1; admin 18:01:32Z → same 409. Log "already provisioned … Refusing" before any write; record after: modifiedon 18:01:05 unchanged, container C1 unchanged, POA unchanged (mask 262167) → no second container ✔.
- Graph existence: **not independently verified (instrument gap).** Evidence: the endpoint's own log "Created SPE container C1 ('Secure Project – UAC gate 133b 2026-10-06')" (Graph returned the id). Read-back routes: admin az Graph token unusable (403/400); SPE-admin get-by-id answers 503 scope_unverifiable for every secure container; testuser1 OBO POST /api/obo/records/sprk_project/PB/upload-session → **403** "api identity lacks required container-type permission" (Graph accessDenied on drives/C1/createUploadSession — not itemNotFound). Another lane (156) hit the same Graph 403 on a PUT into secure container b!8qdU… at 18:02:44Z. → **Observation for the owner (not classed as a G4 defect, it is 047's stated "document isolation unproven" area): OBO upload into a freshly provisioned secure container is refused by Graph for its creator.** Likely a container-type/permission gap on secure containers; needs the owner/doc-isolation lane.

#### 133 (d): PASS (see the re-post above; Inspect before/after identical: modifiedon 18:01:05, POA 8d7bad7a 262167).

#### 133 (c) StrandForResume + admin provisioning: PASS
- StrandForResume -Apply on PB 18:03:46Z: owner team 6eabc7f9, tu1 share revoked (POA mask 0), container cleared (C1 now referenced by nothing — orphan, recorded).
- admin POST provision-project 18:03:49Z → **200, resumed:true**, sharedToCreatorSystemUserId 8d7bad7a (createdby, not the admin caller — F8 ✔), new container **C2 = b!IRjcuov6dU-QV2n8wNvGgxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN**.
- Inspect: owner team, container C2, POA 8d7bad7a 262167, RPA(tu1) = creator rights ✔.

#### 133 (f) strand WITHOUT revoke + the "Try securing again" call: PASS
- Cleared sprk_containerid only (18:04:21Z; C2 orphaned, recorded); creator share kept (262167).
- testuser1 POST provision-project with the client's body shape {projectId: PB} (provisioningService.ts `provisionSecureProject`) 18:04:26Z → **200, resumed:true**, sharedToCreatorSystemUserId 8d7bad7a, new container **C3 = b!_0H4eQgAXkm7AAo66_apUBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN** (≠ BU / default; on 1 record). POA unchanged 262167.

#### 133 (g) Office quick-create → strand → admin resume: PASS
- PG 53f34066 (quick-created by testuser1, app-only createdby 8793f4b0, sprk_createdbyperson = testuser1). testuser1 provisioned it 18:05:13Z → 200 (creator rule satisfied through sprk_createdbyperson), container C4 = b!UhVkyYyZrEKopsCjLi3dfxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN.
- StrandForResume -Apply 18:05:25Z (owner team, tu1 share → 0, container cleared; C4 orphaned, recorded).
- admin POST provision-project 18:05:28Z → **200, resumed:true, sharedToCreatorSystemUserId = 8d7bad7a (testuser1)** — the human from sprk_createdbyperson, not the app user, not the admin caller ✔. New container C5 = b!_yZXxbMiOESIRAX0EKNSlhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN.
- Inspect: owner team 6eabc7f9, container C5, POA 8d7bad7a 262167, RPA(tu1) = creator rights, sprk_createdbyperson = 8d7bad7a.

#### 133 (h) record with its own non-BU container: PASS
- PH = 5365f28d-b0c1-f111-a05c-3833c5e9614d "UAC gate 133h 2026-10-06" (testuser1 create), testuser1 PATCHed sprk_containerid = C1 (the orphan from (c) — a real SPE container referenced by no other record, no BU, not configured).
- testuser1 provision 18:05:57Z → **200, speContainerId = C1** (resumed false). Read back: sprk_containerid = C1 unchanged; C1 on exactly 1 project; owner team; POA tu1 262167. No new container (response names C1; nothing else recorded). [App Insights PROVISION lines for PH/PI had not ingested at write time — not used as evidence.]

#### 133 (i) record whose container is a BU's container: PASS
- PI 83449d75 (testuser1 create, sprk_containerid = b!vzGD… = BU1/root BU container = DefaultContainerId).
- testuser1 provision 18:06:16Z → **200**, new container C6 = b!UhxDf-Qp9UKCLyeglKKvdhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN (≠ any BU / default; on 1 record). Business units after: Spaarke Business Unit 1 and Spaarke still b!vzGDfDpd7km_-_H38Q6ZfbotQXLPXF9Ci71VoQmIOHUKlvxOqBsHQLrROZ5KySLh, Demo unchanged ✔. (Also the 047 step-8 shape: a cascaded shared container at create time still provisioned to its own.)

#### 133 (j) document-location probe: PASS
- PJ = 89be3c0b-abc1-f111-a05c-3833c5e9614d "UAC gate 133j 2026-10-06" (admin create) + location LOC = 8ebe3c0b-abc1-f111-a05c-3833c5e9614d (probe-133c1, https://probe.invalid/133c1).
- admin provision 18:06:44Z → **500 `sdap.provision.cascade_children_unreadable`, childTable `sharepointdocument`, cascadeChildState `refused`**; PJ modifiedon 17:26:29 unchanged, owner unchanged (admin), issecure still False, container null ✔.

#### 132 (b) provisioning evicts a BU colleague's warm cache: PASS
- PK 0d29ab75 (admin create, owner BU1 team cf15f587). testuser1 (the BU colleague) WARM 18:07:01Z: /api/users/me/memberships/sprk_project 21 ids, PK present; GET /api/v1/external/projects 21 (PK not listed there — not a conferring membership); Dataverse direct read 200; BFF /api/v1/external/projects/PK 200; RPA full.
- admin POST provision-project 18:07:23Z → 200 (container C7 = b!1jQ90fRNREqIkel3wEl2txG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN, shared to its creator, the admin).
- testuser1 FIRST requests after (18:07:24–28Z, 1 s after the response, well inside the 2-min TTL): memberships 20 ids, **PK absent**; external/projects PK absent; Dataverse direct read **403** "does not have ReadAccess"; BFF /api/v1/external/projects/PK **403** "You do not have access to this project".
- VerifyProvision (run with -ResourceGroupName spe-infrastructure-westus2 -AppInsightsName spe-insights-dev-67e2xz, since the default RG holds no App Insights): owner Secure Record Owners; colleague RPA = None; `[ACCESS-EVICT] Evicted 2 cache entries for sprk_project 0d29ab75 (owner change) across 3 pattern(s), 0 failed` at 18:07:14 (+ share-change eviction 18:07:13).
- Note: the GET /api/v1/external/projects count went 0 (17:14Z) → 21 (18:07Z) for testuser1 — consistent with main's sprk_isexternal change, not investigated further.

### Records created + cleanup
| Record | Id | Cleanup |
|---|---|---|
| matter M132 | af0d7197-a9c1-f111-a05a-7c1e520a989f | deleted (204, GET 404) — restored to its original owner first |
| project P1 (TASK133-PROBE-20261006131931) | 97ee9215-aac1-f111-a05c-7c1e52154cae | deleted 17:2xZ (204/404); its leftover self-share revoked impersonating the owner first |
| project PB (133b/c/d/f, 047) | ae525172-aac1-f111-a05c-0022482913fc | deleted (204/404) |
| project PI (133i) | 83449d75-aac1-f111-a05c-7c1e52154cae | deleted |
| project PK (132b) | 0d29ab75-aac1-f111-a05a-7c1e520a989f | deleted |
| project PG (133g, Office quick-create) | 53f34066-aac1-f111-a05c-0022482913fc | deleted |
| project PH (133h) | 5365f28d-b0c1-f111-a05c-3833c5e9614d | deleted |
| project PJ (133j) | 89be3c0b-abc1-f111-a05c-3833c5e9614d | deleted (never provisioned) |
| document location LOC | 8ebe3c0b-abc1-f111-a05c-3833c5e9614d | deleted before PJ |
Secure records were deleted directly as admin (no note asked for an unsecure first). No other agent's records were touched. 65a3fab2 not touched. Orphan b!HBRbok… not touched.

**SPE containers created by these provisionings — NOT deleted (owner decides); all now referenced by no record:**
- C1 b!ECpwsE3P2Em14S3B84uYOxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN (PB first run; reused by PH in (h))
- C2 b!IRjcuov6dU-QV2n8wNvGgxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN (PB, (c) resume)
- C3 b!_0H4eQgAXkm7AAo66_apUBG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN (PB, (f) resume)
- C4 b!UhVkyYyZrEKopsCjLi3dfxG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN (PG first run)
- C5 b!_yZXxbMiOESIRAX0EKNSlhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN (PG, (g) resume)
- C6 b!UhxDf-Qp9UKCLyeglKKvdhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN (PI)
- C7 b!1jQ90fRNREqIkel3wEl2txG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN (PK)
All empty (no file was ever uploaded; the one upload-session attempt was refused by Graph).

### Defects / findings (classified)
1. **D1 — RevokeAccess of the current owner's own share is refused by Dataverse (0x80040223)** → provisioning's share undo (`RestoreCreatorShareAsync`, targetMask 0) cannot succeed on a creator-owned record (NotMoved path + compensation). Class **(e)** — fail-safe, honest `sharesRestored:false`. Main session: recorded as a known limit for the owner, no code change now.
2. **OBO upload into a freshly provisioned secure container refused by Graph for its creator** (403 "api identity lacks required container-type permission", drives/C1/createUploadSession; another lane saw the same on b!8qdU… PUT). Not classified as a G4 gate defect — 047 explicitly leaves document isolation unproven — but it is a likely **(a) broken real path** (a secure project's creator cannot upload) for the owner / doc-isolation lane to confirm.
3. SPE-admin GET /api/spe/containers/{id} answers **503 scope_unverifiable "Try again shortly"** for every secure-record container (deterministic). Class (f) (fails closed; the retry copy is misleading). Means no BFF route can independently confirm a secure container exists in Graph.
4. Harness (class f): task-133-live-gate.ps1 ShareFirstProof line 103 throws under PowerShell 7 (`OData-EntityId` header is string[]); task-132-live-gate.ps1 -Verify prints negative "minutes since the reassign" (parses the stored UTC ISO time as local). No product impact.
5. Gate-instrument note: the Teams/SPA lists (/api/v1/external/projects, /me) do not list team/BU-owned records (FR-24 accessConferringOnly), so 132 (a)/(b) polled /api/users/me/memberships/{entity} (the membership-resolved cache that §9 row 3 bounds) plus the direct reads.

### For the owner
- D1 disposition (known limit vs fix: revoke as the owner, or treat a self-share on a creator-owned record as mask 0).
- Finding 2 (OBO upload to secure containers 403): needs the doc-isolation lane.
- Container cleanup: C1–C7 above (plus pre-existing orphan b!HBRbok…).
- 047 criterion "container exists in Graph": only the endpoint's create log is evidence; needs an app-token read (Test-SpeContainerPermissionPaging-style, BFF identity) if it must be shown independently. 047 step 7 (secure_bu_not_found) needs an app-setting flip — not run (owner decision; settings changes forbidden here).

---

# G5

## G5 results — UAC-r2 batch-4 dev live gates (2026-10-06)

Agent: gates-g5. Environment: spaarkedev1 / spaarke-bff-dev (master 891cfd9a3). Tasks: 158-a, 158-b, 156 AC8, 150 G-6..G-9.
Helper: scratchpad/gates/g5lib.py (own); state: g5state.json; raw log: g5log.txt.

### DEFECT D-G5-1 (class (a): broken real path, fails closed). Reported to main 17:18Z. FIXED by hotfix #1319 (c339d6920), deployed about 17:59:47Z: provisioning returns 200 and no deny-list failures after the deploy.
- Every secure provisioning on dev fails: `POST /api/v1/external-access/provision-project` returns 500 `sdap.provision.creator_no_access_unverifiable`
  and nothing is changed. Seen on P1 549136a4-a9c1-f111-a05a-7c1e520a989f (trace 0HNP3P8179ACB:00000003, op 97f951df8011a983321ff1f7f9488b76) and M1 12cc34a2-a9c1-f111-a05c-3833c5e9614d (…:00000004), both at 17:16Z.
- App Insights: `GET sprk_noaccessentries … 400`, then "[NO-ACCESS] Deny-list query FAILED: BadRequest", then "[NO-ACCESS-GUARD] Could not read the deny-list … refusing the share (fail closed)", then the provisioning refusal.
- Root cause: `NoAccessListReader.BuildSubjectFilter` / `BuildOrganizationObjectFilter` filter on lookup LOGICAL names (`sprk_subjectsystemuser eq {id}`, `sprk_subjectcontact eq`, `sprk_subjectorganization eq`, `sprk_objectorganization eq`).
  Dataverse rejects them: 400 0x80060888 "Could not find a property named 'sprk_subjectsystemuser'". The `_sprk_subjectsystemuser_value eq {id}` form returns 200 (reproduced as admin).
  The contact form has existed since task 038 (30dd5f397).
- Volume: 812 "Deny-list query FAILED" traces between 16:20Z (deploy) and 17:16Z. It affects every deny-list reader (provisioning, shares, jobs), not only these gates.

### Note N-G5-1 (data, not a 158/150 FAIL: the rule is task 114's in batch 5)
- `/share-user` to testuser1 on ordinary P1 returned 422 `sdap.access.user_share.user_not_internal` because testuser1.sprk_isexternal was NULL (fail closed).
  NULL also on the admin, demo, final.test, eyal, jake, e2e.test2 and the #EXT# guest. main set testuser1 to false at ~17:21Z.
- Observation (R5 / #1081 dev artifact): root-BU user lori.witkin (2dddc606, Spaarke Core User) holds Read/Write/Delete/Share/Assign on secure project 65a3fab2 with no share (RetrievePrincipalAccess).

### Task 150
#### G-6 (Web API part) — PASS
- testuser1 is in no "Spaarke Demo" team (its only team is BU1's default team). Roles: Reporting Access Viewer, AI Analysis User, Office Add In User, Core User.
- testuser1 created its own project a7ee9215-aac1-f111-a05c-7c1e52154cae, matter 8aa74217-aac1-f111-a05c-0022482913fc and WA f4efc917-aac1-f111-a05a-7c1e520a989f, each 201; each reads sprk_issecure=false.
- Write control: testuser1 PATCH of the name on the project and the WA returned 204.
- testuser1 PATCH `{"sprk_issecure":true}` on each of the three returned 403 0x8004f507 ("does not have update permissions to a Is Secure secured field on entity Project/Matter/Work Assignment"). Admin read-back: false on all three.
- testuser1 create naming `sprk_issecure:true` on project, matter and WA returned 403 0x8004f502 (no create permission on the secured field). No row was created (a name query returns 0 on each table).
#### G-7 (App Insights part) — PASS, with a caveat
- Searched traces and exceptions over 24 h for `flag_unreadable` / `SecureFlagUnreadable`: 0 hits. Caveat: the earliest trace in the workspace's 24 h window is 16:20:29Z (the deploy), so the search covers about 1 h of the new BFF.

### Task 156 AC8 — PASS (the container check covers the resolver's decision only; see below)
Test data (admin, Web API): ordinary matter M2 e6792240-aac1-f111-a05a-7c1e520a989f; communication C1 210f1d3e-aac1-f111-a05c-7c1e52154cae, filed under M2 (typed lookup plus untyped pair: dev has no sprk_recordtype_ref row for sprk_communication);
to-dos T1 1b7ba93f-aac1-f111-a05c-3833c5e9614d and T2 e838dc41-aac1-f111-a05c-0022482913fc, each sprk_regardingcommunication=C1 with stamp sprk_regardingmatter=M2.
#### Stamp repair by the 5-minute job — PASS
- 17:21:14Z: cleared T1's sprk_regardingmatter out of band (PATCH 204; read back null).
- 17:25:04Z, job run 6d8bebfa: `[CORE-ANCESTOR-RECON] stale mode=write entity=sprk_todo id=1b7ba93f… source=sprk_communication 210f1d3e… before=[sprk_regardingmatter=(null)] after=[sprk_regardingmatter=e6792240…]`,
  then `[CORE-ANCESTOR-RESTAMP] Re-stamped`, then heartbeat `status=ok mode=write scanned=57 stale=1 repaired=1 repairFailures=0 scanFailures=0`. T1 reads M2 again (modifiedon 17:25:04Z). Repaired 3 min 50 s after the break, within one cycle.

#### Re-file of the communication into the throwaway secure project — PASS (after hotfix #1319, c339d6920, deployed about 17:59:47Z)
- 18:02:34Z: admin `PATCH /api/communications/210f1d3e…/filing` (body: sprk_RegardingMatter@odata.bind null, sprk_RegardingProject@odata.bind /sprk_projects(P1), pair id/name/type=project) returned 204.
- Read-back: C1 is regardingproject=P1, regardingmatter=null, owned by Secure Record Owners 6eabc7f9. T1 and T2 are each **sprk_regardingproject=P1, sprk_regardingmatter=null**, owned by 6eabc7f9, modifiedon 18:02:26Z (inline re-stamp in the same request).
- Container: `PUT /api/obo/records/sprk_todo/{T1}/files/uac-gate-156-G5-2026-10-06.txt` (admin). The trace shows `[SECURE-CONTAINER] Resolved sprk_todo 1b7ba93f… to the OWN container of the secure sprk_project 549136a4… (sprk_regardingproject; not a business-unit container)`,
  then "Uploading file as user to container b!8qdU0gEu…" = P1's own container **b!8qdU0gEubUSu29rQVtpvehG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN**. The derived value is correct.
  The Graph PUT itself returned 403 Access denied: an OBO upload as admin, who is not a member of the new container. So no file landed; the BFF answered **401** (see observation O-3). Op eadcd0032cc1c0842c6c3180263a79d7.

### Task 158
#### 158-a — PASS (throwaway secure project P1, not 65a3fab2)
- P1 549136a4-a9c1-f111-a05a-7c1e520a989f was provisioned by admin at 18:01:11Z: 200, own container b!8qdU0gEubUSu29rQVtpvehG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN, owner team 6eabc7f9, creator shared 262167.
  Shared through /share-user: testuser1 Collaborate (mask 262167) and lori.witkin View (mask 1). Both 200.
- WA_A 4ba60dfb-afc1-f111-a05c-3833c5e9614d "UAC gate 158 WA_A 2026-10-06 (G5)" was created out of band (Web API, sprk_RegardingProject bind) at 18:01:50Z.
- Job run 0b76c240 at 18:05:10Z: "[SECURE-INHERIT] sprk_workassignment 4ba60dfb… is filed under a secure record and is now secure (provisioned for its creator)".
  Heartbeat: `secureParents=8 filed=2 secured=2 refused=0 failed=0 deferred=0 sharesWritten=3`. 3 min 20 s after the create.
- Read-back: sprk_issecure=true; _owningteam_value=6eabc7f9 (Secure Record Owners); own container **b!Q93-JrZ7fUmviHObJDHmyhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN** (not P1's, not b!MVasATu…).
  Creator (admin, createdby) is shared 262167. uac.child RetrievePrincipalAccess = none (no ReadAccess): DENIED.
- Ledger: 3 rows, sourcefield `inherited:sprk_project:549136a4…`:
  - testuser1: state 100000001 Shared, level 23, sprk_reason empty
  - lori: 100000001 Shared, level 1, reason empty
  - admin: 100000002 CoveredByExisting, level 262167, reason "covered-by-existing"
  These match the WA's POA (testuser1 23, lori 1, admin 262167).

#### 158-b — PASS (throwaway secure matter M1 12cc34a2-a9c1-f111-a05c-3833c5e9614d)
- M1 was provisioned at 18:01:25Z: 200, container b!4ltjdXaQMEGKlLr47q0m6RG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN. testuser1 shared View through /share-user (200, mask 1).
- WA_B 4da60dfb-afc1-f111-a05c-3833c5e9614d was created out of band under M1 (sprk_RegardingMatter). The same job run secured it at 18:05:24Z: issecure=true, team 6eabc7f9, own container b!qZ9Qj5EIfkKIBIESyeRL1xG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN.
  testuser1 has Read through inherited row `inherited:sprk_matter:12cc34a2…` (Shared, level 1).
- **Round 58** (direct share on the WA, walled on the MATTER):
  - /share-user uac.child View on WA_B: 200, ReadAccess.
  - No Access entry 2df69fa5-b0c1-f111-a05c-0022482913fc created (subject systemuser uac.child; object recordtype sprk_matter + id M1).
  - `POST /no-access/enforce` returned **403 delegation_write_required — DEFECT D-G5-2** (below).
  - The job path then enforced the same entry. Run e293531b at 18:10:05Z: `[NO-ACCESS-ENFORCE] Entry 2df69fa5… (SystemUser): 1 user(s) x 1 secure record(s); removed 1, … failures 0`; heartbeat removed=1.
  - uac.child rights on WA_B are now none (POA mask 0). WA_B is still issecure=true and team 6eabc7f9.
  - Entry deactivated (statecode 1 / statuscode 2), then deleted.
  - So round 58's behaviour holds through the job. The on-save enforce route is blocked by D-G5-2.
- **Unsecure the matter without alsoUnsecure** (admin = creator; F3 log "(Creator, F3)"): 200.
  - Response: `relatedSecureRecords=[{workassignment 4da60dfb… "UAC gate 158 WA_B 2026-10-06 (G5)"}]`, sharesRevoked 1, sweepComplete **false** (O-1).
  - M1 is now issecure=false, owned by admin in the root BU.
  - WA_B **stays secure** (issecure=true, team 6eabc7f9).
- **Round 39**: testuser1's share on WA_B is **gone** (RetrievePrincipalAccess none; POA mask 0). Its ledger row `inherited:sprk_matter:12cc34a2…` reads **sprk_state 100000007 (Revoked), sprk_reason "access-removed"**. The admin row reads Revoked / "kept-direct".
- WA_B then unsecured alone (admin creator): 200, issecure=false, owner admin, sweepComplete false (O-1).

### Task 150 (continued, after the hotfix)
- **G-6 secure half — PASS.** testuser1 holds Write on secure P1 (Collaborate) and on secure WA_A (inherited 23).
  `PATCH {"sprk_issecure":false}` returned 403 0x8004f507 on both. Read-back: true on both.
  Matter: testuser1 held only View on secure M1, so the matter case is covered by its own ordinary matter (403 above).
- **G-7 read part — PASS.** testuser1 (BU1, own Dataverse token) `GET sprk_projects(P1)?$select=sprk_issecure,sprk_containerid` returned 200 with **sprk_issecure=true present** and container b!8qdU0gEu….
  Also WA_A issecure=true. App Insights: 0 `flag_unreadable` hits (above).
- **G-8 — PASS.** Root-BU user lori.witkin (2dddc606, BU Spaarke 06fbf21c, Spaarke Core User; shared View on P1) read P1 through `MSCRMCallerID`: 200, sprk_issecure=true.
  BU1 user testuser1: true (G-7). Caveat: lori also reaches secure records through root-BU role depth (N-G5-1); the FLS read is what this gate checks.
- **G-9 — PASS.**
  - testuser1 at Collaborate (rights R/W/Append/AppendTo/Share, no Delete) `POST /unsecure-project` on P1: **403 `sdap.unsecure.not_permitted`** ("Only someone with Full Access … or the person who created it …"). P1 is unchanged (modifiedon 18:01:05Z, still secure).
  - testuser1 raised to Full Access (/share-user 100000002, mask 327703, now incl. Delete), then the same call: **200**. Log "(FullAccess, F3)". P1 issecure=false, owner testuser1.
    children reowned 3 (C1, T1, T2 to BU1's default team cf15f587); relatedSecureRecords lists WA_A, which stays secure.
  - Creator rule: admin unsecure of M1, WA_B and WA_A returned 200, log "(Creator, F3)". admin is also a System Administrator, so this shows the creator branch is taken first. It does not isolate the creator rule from role rights.

### DEFECT D-G5-2 (class (a): broken real path; a security removal is delayed, with a minutes-bound backstop). Reported to main 18:07Z.
- `POST /api/v1/external-access/no-access/enforce` returns 403 `sdap.access.deny.delegation_write_required` for EVERY caller, including a System Administrator.
- Cause: DelegationRuleFilter checks Write on `sprk_noaccessentries({id})` with RetrievePrincipalAccess, but sprk_noaccessentry is **OrganizationOwned**.
  Dataverse: 400 0x80040800 "The 'RetrievePrincipalAccess' method does not support entities of type 'sprk_noaccessentry'" (reproduced as admin).
  App Insights 18:06:34Z: `[DELEGATION-RPA-UNAVAILABLE] RetrievePrincipalAccess returned 400 for sprk_noaccessentries(2df69fa5…) … Denying`, then `[DELEGATION] DENIED … caller holds None`.
- Effect: the No Access form's on-save enforcement (round 3 R3 "immediate") never runs. The walled user keeps access until `no-access-share-reconciliation` (removed it 3.5 min later here).

### Observations (not gate failures)
- O-1 (f): `/unsecure-project` reports `sweepComplete:false` whenever the new owner already held a share.
  The sweep tries to RevokeAccess the new owner's (the creator's) own POA row, Dataverse answers 400, and the sweep is marked incomplete ("Could not revoke the SystemUser share for 1d02f31c…", exception at 18:11:43Z and 18:11:59Z).
  Only the owner's own row survives, so no stale access remains, but the response tells the operator a share "may survive" every time the creator is the fallback owner.
- O-2 (f/b): deleting a work assignment leaves its `sprk_assignedaccess` ledger rows with a null target (5 rows, state Revoked). I deleted them by hand as cleanup.
- O-3 (f): the OBO record-keyed upload maps a Graph 403 "Access denied to container" to HTTP **401**, which tells the client the user is not authenticated.
- O-4: at 18:05Z the secure-inherit heartbeat showed secureParents=8. Other gates were provisioning concurrently.

### Records created + cleanup
All deleted 18:14:18–18:14:48Z (each GET returns 404 afterwards). Before delete, every secure one was unsecured through the API.
- P1 549136a4-a9c1-f111-a05a-7c1e520a989f (project): secure, unsecured (G-9), deleted
- M1 12cc34a2-a9c1-f111-a05c-3833c5e9614d (matter): secure, unsecured, deleted
- WA_A 4ba60dfb-afc1-f111-a05c-3833c5e9614d: secured by job, unsecured, deleted
- WA_B 4da60dfb-afc1-f111-a05c-3833c5e9614d: secured by job, unsecured, deleted
- M2 e6792240-aac1-f111-a05a-7c1e520a989f (matter), C1 210f1d3e-aac1-f111-a05c-7c1e52154cae (communication), T1 1b7ba93f-aac1-f111-a05c-3833c5e9614d and T2 e838dc41-aac1-f111-a05c-0022482913fc (to-dos): deleted
- No Access entry NA1 2df69fa5-b0c1-f111-a05c-0022482913fc: deactivated, then deleted
- testuser1-created G6P a7ee9215-aac1-f111-a05c-7c1e52154cae, G6M 8aa74217-aac1-f111-a05c-0022482913fc, G6W f4efc917-aac1-f111-a05a-7c1e520a989f: deleted
- Orphan ledger rows deleted: 8e160475-…, 9a160475-…, a9160475-…, 963d2081-…, ab3d2081-… (all -b0c1-f111-a05c-0022482913fc)
- **SPE containers left in place (not deleted, per the rules):**
  - P1 b!8qdU0gEubUSu29rQVtpvehG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN
  - M1 b!4ltjdXaQMEGKlLr47q0m6RG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN
  - WA_A b!Q93-JrZ7fUmviHObJDHmyhG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN
  - WA_B b!qZ9Qj5EIfkKIBIESyeRL1xG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN
  No file was written to any of them (the upload probe got a Graph 403).
- Not changed by me: 65a3fab2 (untouched), any app setting, testuser1's sprk_isexternal (main set it).

### For the owner
1. D-G5-1: fixed by hotfix #1319 and confirmed live (provisioning 200; 0 deny-list failures after 17:59:47Z).
2. D-G5-2: the No Access enforce route needs a write check that works on an organization-owned table. Owner/main to decide the fix.
3. O-1: should the unsecure sweep skip the new owner's own share, so sweepComplete is not falsely false?
4. N-G5-1: NULL `sprk_isexternal` refuses internal shares for 8 enabled users (task 114, batch 5).
5. Four SPE containers listed above are pending the owner's orphan-container decision.

---

# upload403

## Upload 403 into secure containers — investigation (2026-10-06, dev)

Agent: upload403. Code read: C:\wtG @ 891cfd9a3 (read-only). Dev BFF @ 891cfd9a3 (+ hotfix #1319 c339d6920). Helpers: `gates\u403\` (u.py, perms2.py, secure.py, docs.py, control.py).

### Verdict
**Class (a) — broken real path, fails closed (no disclosure).** This is a **code/design defect**. It is not a container-type registration gap or missing config, and no Entra or owner config step fixes it.
- Provisioning creates each secure container **app-only** and grants **no** SPE container permission to anyone. That is by design: under the owner's broker-only decision, no user is ever granted one.
- The workforce byte paths still call Graph **OBO**. Under OBO, SharePoint Embedded requires the *user* to hold a role on the container.
- Business-unit containers only work because users were **hand-granted** roles on them. A secure container has 0 roles, so every OBO call into it is denied, for every user.

### Q1 — Container TYPE: the same for all containers (not the cause)
- `SharePointEmbedded__ContainerTypeId` = **8a6ce34c-6055-4681-8f87-2f4f9f921c06** ("Spaarke PAYGO 1", owning app 170c98e1).
  - Read via `az webapp config appsettings list`; values were printed for non-secret names only.
- Provisioning path:
  - `ProvisionProjectEndpoint.cs:855` reads that setting.
  - `:1183 → CreateSpeContainerAsync :3615` calls `SpeFileStore.CreateContainerAsync`, which goes to `ContainerOperations.CreateContainerAsync`.
  - That method uses `_factory.ForApp()` (`ContainerOperations.cs:58`): an **app-only** create with that `ContainerTypeId` (`:63`). Then it writes the business-unit stamp.
- BFF SPE-admin list (`GET /api/spe/containers?configId=c3a25b9a…`, admin) reports `containerTypeId 8a6ce34c…` for all of these:
  - the BU1/root container b!vzGD… "Spaarke Dev Container 2"
  - the Demo container b!yLRd… "Spaarke Inc"
  - today's secure containers C1 b!ECpw…, b!8qdU… (158a), b!4ltj… (158b), b!Zxw… and b!HkqH… (G3 148)
- The other dev config, "Spaarke SPE Model 1 Owner" (type fb3817a8), is not used by the BFF's provisioning or uploads.
- The 166 migration (gate 24, "367 moved") targeted the records' derived containers. Every pointered `sprk_document` in dev now sits in one of the two BU containers: **455 → b!vzGD…, 87 → b!yLRd…, 33 → no pointer** (aggregate FetchXML). Both are the same type 8a6ce34c.

### Q2 — Identity and token on the upload path: OBO through the BFF app
- Route: `POST /api/obo/records/{entity}/{id}/upload-session` (`OBOEndpoints.cs:361`).
  - Filter: `RecordRouteAccessAuthorizationFilter(AssociateContentOperation)`.
  - Then `RecordContainerResolver.ResolveForRecordAsync`, then `SpeFileStore.CreateUploadSessionAsUserAsync(ctx, …)` (`:410`).
  - That is **OBO** through `GraphClientFactory.ForUserAsync`, using the CCA ClientId = API_APP_ID **1e40baad**.
- The small-file route `PUT …/files/{*path}` is OBO too: `UploadSmallAsUserAsync`, `OBOEndpoints.cs:169`.
- App Insights, the failing request (op `243a3efa312d120a500a6d36a68795c6`, 18:02:46–47Z, testuser1 on PB ae525172):
  - inbound token: aud api://1e40baad, appid 04b07795 (az CLI), scp SDAP.Access
  - `[DELEGATION] Caller rights … Read, Write, Append, AppendTo, Share`, then `[RECORD-ROUTE-AUTH] Allowed`
  - `[SECURE-CONTAINER] Resolved secure sprk_project … to its OWN container`
  - OBO exchange 200, with scopes incl. FileStorageContainer.Selected / Files.ReadWrite.All
  - Graph `POST /v1.0/drives/b!ECpw…/root:/uac-gate-047-probe.txt:/createUploadSession` → **403, ODataError message "Access denied"**
  - Then `SpaarkeStorageException "obo.upload.session.create: Access denied"`.
- **Positive control** (18:16:08–12Z): same user, same route, same app, same container type, same code path.
  - testuser1 created throwaway project 8bcdbbfc-b1c1-f111-a05a-7c1e520a989f (non-secure, BU1).
  - `POST …/upload-session?path=uac-upload403-control.txt` → **200**, with an uploadUrl on drive b!vzGD… (the BU1 container).
  - The only variable that changed is the container.

### Q3 — Container membership: nobody is added, and the design says nobody should be
- **BU1/root container b!vzGD…** (`GET /api/spe/containers/{id}/permissions?configId=c3a25b9a…`, admin, 200) has these roles:
  - Ralph Schroeder: **owner**
  - Eyal Iffergan: **writer**
  - **Test User 1: writer**
  - No code grants these, so they were added by hand. That is why uploads into BU containers "work today".
- **Secure containers** (same route, 200): **`items: []`, count 0** on C1 b!ECpw…, b!Zxw… (G3 148 project) and b!DuRy… (G3 148 work assignment).
- Code: provisioning grants no container permission anywhere.
  - `SpeContainerMembershipService.GrantMembershipAsync` still has **zero callers** (notes 017/020/071/073/075).
  - Provisioning shares only the **Dataverse** record (creator share 262167, named principals).
  - `docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md:5-6` explicitly scopes SPE out ("Container/SPE provisioning is code").
- Design: `projects/unified-access-control-r2/SECURE-DOCUMENTS-BUILD-PLAN.md` §1 (owner decision 2026-08-25, broker-only for workforce AND contacts) says:
  - *"No user — workforce or contact — is ever granted a SharePoint Embedded container permission … The BFF authorizes, then streams app-only."*
  - The per-project container is *"Blast-radius containment … NOT the live ACL. No user ACLs are granted on it."*
  - So, by design, **nobody** (creator, sharees, Secure Record Owners team) is a container member.
- The defect is that the workforce byte paths were never moved to app-only.
  - Earlier notes (071 §0, `finding-compose-create-on-save…` §4, route sweep 2026-10-02) cite "it's OBO, so SPE denies without a container ACL, and no user has one" as a *security comfort*.
  - None of them noticed that the same fact makes the **legitimate** OBO path dead for every container without hand-granted ACLs, which includes every secure container.

### Q4 — Is "api identity lacks required container-type permission" about the app's container-type registration? No.
- That text is the BFF's own **blanket translation of every Graph 403** that is not `Authorization_RequestDenied`:
  - `Infrastructure/Graph/GraphErrorTranslator.cs:133`
  - `Infrastructure/Errors/ProblemDetailsHelper.cs:111`
- Graph's actual error was plain **"Access denied"**, which is what SPE returns for a delegated caller with no container role.
- The app's registration on type 8a6ce34c is fine. Evidence:
  - the positive control (delegated createUploadSession → 200 on the same type)
  - the topology doc's verified state (2026-10-04): *"the BFF managed identity already holds `full` app-only and delegated"* on 8a6ce34c
  - app-only create, BU stamp and permission listing all succeed on these containers
- Secure and BU containers use the same type, so no registration change is involved. **No Entra or container-type action is needed (and none was taken).**
- Side defect, class **(f)**: the misleading 403 copy sends diagnosis toward the app registration. It should pass through Graph's code/message (e.g. "SharePoint Embedded denied the caller access to this container").

### Q5 — Reproduction
- No new provisioning was needed. G4's PB/C1 run plus App Insights already reproduce the failure, and each provisioning makes a permanent container.
- The minimal discriminating repro is the positive control above:
  - same everything → BU container 200
  - secure container 403 (App Insights 18:02:47Z)
  - plus the permission lists (3 roles vs 0)

### Impact (what is broken for secure records, from code)
- **Every workforce OBO byte path** fails for every user on a secure record's container, because none of them holds a role there:
  - **Uploads:** `OBOEndpoints.cs:169` (PUT small) and `:410` (upload-session). There is no working workforce upload into any secure record.
  - **Reads:** `FileAccessEndpoints` preview-url / preview / content / office / open-links / view-url / share-link (`:330, :458, :522, :587, :665, :825, :907, :925`) and `DocumentVersionEndpoints :118, :175`.
  - **Compose:** `ComposeAnnotationEndpoints :143/:242`, `ComposeSyncEndpoints :266`, and the save `ReplaceFileContentAsUserAsync`.
  - **AI:** `AnalysisDocumentLoader :137/:152`, `DocumentContextService :702`, `FileIndexingService :76`, `AiAuthorizationFilter :787`, `ChatDocumentEndpoints :1307`, `ChatWordExportEndpoints :179`.
- These **still work** because they are app-only:
  - `/api/documents/{id}/download` and `/eml-render` (`DownloadFileAsync`, `:1058/:1145`)
  - email/communication archive (`UploadSmallAsync`)
  - `DocumentContainerRelocator` (migration / Make Secure copy)
  - the external-contact broker (`ExternalProjectDataEndpoints :365`)
- **Existing records:**
  - **65a3fab2** (container b!MVasATu_GE6Lqs6JOGaeghG_EVjnHABFpxLm1FYg2Ah7gIBcg3lNSbiHX5dqt_eN): **0 documents point to it**. Its permission list cannot be read (503 `spe.admin.deny.scope_unverifiable`, the pre-165 unbound container, #1313). It was created by the same app-only path, so it is almost certainly role-less and affected the same way. No data is at risk; it just cannot take workforce uploads or previews.
  - **The 166-migrated 367 documents: NOT affected.** All are in the BU containers (b!vzGD… / b!yLRd…), and OBO keeps working there for users who hold the hand-granted roles.
  - **Zero** documents sit in any secure container in dev, so no upload into a secure container has ever succeeded there.
- **Latent, larger impact:**
  - Task 150's Make Secure relocation moves an existing record's documents app-only into its new secure container.
  - From that moment, preview, Office open, content, versions, Compose and AI reads of those documents fail for **everyone** on the workforce side. Only /download still works.
  - Watch this as soon as Make Secure is used on a record that has documents.
- Not a security hole: every failure is a denial.

### Smallest correct fix (code — needs a dev-lane task; owner should confirm direction, §6.5)
Consistent with the binding broker-only decision (§1, "BFF authorizes, then streams app-only"):
1. **Uploads (the reported symptom), smallest change:**
   - The two record-keyed routes already authorize against the owning record at the route filter (`AssociateContentOperation`) and derive the container server-side.
   - Switch `OBOEndpoints.cs:169` from `UploadSmallAsUserAsync` to the existing app-only `SpeFileStore.UploadSmallAsync(driveId, path, stream, conflictBehavior, ct)` (`SpeFileStore.cs:91`).
   - Give `:410` an app-only `CreateUploadSessionAsync`: `UploadSessionManager` has only the OBO variant today (`:625`), so add the app-only twin on the same `ForApp()` client.
   - Tests: route-level, for app-only on a secure-resolved container, plus the existing deny cases.
2. **Reads:** the document-gated read routes listed above (`DocumentAuthorizationFilter` already applied) need the same conversion, or secure documents can be uploaded but not previewed or opened.
   - App-only equivalents exist for preview (`GetPreviewUrlAsync`, `SpeFileStore.cs:209`) and download.
   - The **Office edit links** (`/office`, `/open-links`) are the hard case. A browser or desktop Office edit session runs as the user, so it needs a per-user SPE role, which broker-only forbids. The owner must decide between view/download-only for secure documents and an explicit, lifecycle-managed exception.
3. **Alternative the owner already rejected (needs ADR/decision amendment, path B):**
   - Grant the creator and sharees a role on the secure container, kept in sync with share / unshare / No Access / close / reassign.
   - This is the "two mechanisms diverge" class the 2026-08-25 decision avoided. Not recommended.
   - A one-off manual grant through the SPE-admin permission route would unblock a test, but it is an owner decision and was **not** done here.
4. Class (f) companion fix: stop mapping every 403 to "api identity lacks required container-type permission" (`GraphErrorTranslator.cs:133`, `ProblemDetailsHelper.cs:111`).

Who: a BFF code task (dev lane) after the owner confirms direction (1+2, and the Office-edit question). No Entra admin, container-type registration or app-setting step is involved.

### Records created + cleanup
| What | Id | Cleanup |
|---|---|---|
| project (testuser1 Web API create, non-secure, BU1) "UAC gate upload403 control 2026-10-06" | 8bcdbbfc-b1c1-f111-a05a-7c1e520a989f | deleted (DELETE 204, GET 404) |
| Graph upload session on BU1 container b!vzGD… (path uac-upload403-control.txt, **no bytes sent**) | session guid a7e5b60a-d3f9-4142-a297-14afcaf3bfe6 | not cancellable without the token-bearing URL; Graph expires an unused session automatically, and no file was committed |
No provisioning was run, no container was created, and no container permission was granted or changed. No app-setting, Entra or Key Vault action was taken. 65a3fab2 was not touched.

### Defects (classified)
1. **U403-1 (a)**: workforce OBO byte paths (uploads + reads) are dead on secure containers. Provisioning creates role-less containers by design (broker-only §1), but those paths were never converted to app-only. Fails closed. Files and lines are listed above; the primary ones are `OBOEndpoints.cs:169, :410`. Owner direction needed on reads and Office edit.
2. **U403-2 (f)**: the blanket 403 → "api identity lacks required container-type permission" copy (`GraphErrorTranslator.cs:133`, `ProblemDetailsHelper.cs:111`) misattributes a membership denial to the app's registration.
3. Observation: BU-container access depends on **hand-granted** per-user SPE roles (Ralph owner, Eyal writer, testuser1 writer on b!vzGD…). Nothing in code grants them, so a new user in a BU gets the same 403 on BU uploads and previews. The same root cause applies, latent outside dev.
