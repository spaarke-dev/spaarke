# Pillar B (Outlook add-in) — live UAT findings, 2026-08-31

> Surfaced during operator live UAT of the deployed add-in (Success Criterion 7). Root-caused via code + **App Insights** (`app 6a76b012-46d9-412f-b4ab-4905658a9559`, BFF `spaarke-bff-dev`). Several are **pre-existing on master**, not introduced by R2.

## Findings

| # | Symptom | Root cause | Kind | Status |
|---|---|---|---|---|
| 1 | Sign-in failed: `AADSTS700046 Invalid Reply Address … brk-9199bf20-…` | Add-in Entra reg (`c1258e2d-…`) lacked the NAA broker **SPA** redirect for the deployed origin (had only `brk-multihub://localhost` under publicClient). | Config | ✅ **Fixed** — added `brk-9199bf20-…://icy-desert-…` + `brk-multihub://icy-desert-…` (SPA) via `az rest`. Needs folding into task 004 / `auth-deployment-setup.md`. |
| 2 | "File to" shows fabricated records (e.g. "Smith Foundation Audit — Phase: Planning") even in incognito | **BFF `OfficeService.SearchEntitiesAsync` is a STUB** — returns `GenerateStubResults` hardcoded data; *"For now, return stub data for testing the endpoint structure"* (line 663). **Never wired to Dataverse.** Tracked GitHub #229 / task 026. | **Unimplemented feature** | 🔴 **Open** — client wiring fixed (below), but server endpoint must be implemented. |
| 3 | Save "completes" but no `.eml` Document / no SPE file | `UploadFinalizationWorker` wrote `sprk_priority=192350001-3` to `sprk_emailartifact`, but the option set is `Urgent(100000000)/High(100000001)/Medium(100000002)/Low(100000003)`. Dataverse rejected every EmailArtifact create → job aborted. App Insights job `237a5c8d`. | **Pre-existing master bug** | ✅ **Fixed** (`c0bd37fdc`) — correct option values. Needs deploy + master PR. |
| 4 | Selected attachments never become child Documents | `OfficeJobQueue` derived `HasAttachments` from the unpopulated `Attachments` array; the add-in sends `SelectedAttachmentFileNames`. Gate on `ProcessEmailAttachmentsAsync` was always false. | Bug | ✅ **Fixed** (`cfae9cdc1`) — derive from `SelectedAttachmentFileNames`. Entangled with #3 (artifact failure aborted job before attachments). Needs deploy + re-verify. |
| 5 | Email `.eml` archives never AI-indexed | Every `.eml` archive is `sprk_searchindexed=false` (regular docs are `true`). Worker DOES queue AI analysis + RAG indexing when `triggerAiProcessing`+`ragIndex` (both set); failure is in the **downstream** analysis/indexing job for email archives. | Needs investigation | 🟡 **Open** — not yet root-caused (downstream job). |
| 6 | Recurring `Dataverse 400 for contacts` every ~15 min | `RecordSyncJob` contact `SelectFields` used `parentcustomerid`; Web API needs the lookup form `_parentcustomerid_value` → `0x80060888 Could not find a property named 'parentcustomerid'`. Contacts sync failed every run. | **Pre-existing master bug** | ✅ **Fixed** — `_parentcustomerid_value`. Needs deploy + master PR. |

## Client fix for #2 (necessary, not sufficient)
`SaveFlow.tsx` rendered `<EntityPicker>` without `searchOptions`, so `useEntitySearch` fell back to its own **client** mock. Fixed (`cfae9cdc1`) to pass `searchOptions={{ apiBaseUrl, getAccessToken }}` → the picker now calls the real `/api/office/search/entities`. This **exposed** that the server endpoint itself is the stub (#2). Real records won't appear until the server endpoint is implemented.

## Deploy state (dev)
- SWA add-in redeployed from branch `cfae9cdc1` (auth + client search wiring live).
- BFF fixes #3/#4/#6 committed on branch, **not yet deployed** as of this note.

## Master-bug callout
#3 (priority) and #6 (contacts sync) are **pre-existing on master** and affect every environment — they must land on master, not just this branch. #2 is a long-standing unimplemented feature (task 026 / #229).

## Recommended sequencing (operator-approved 2026-08-31: 3 → 1 → 2)
1. **(this doc)** document + file issues.
2. Deploy #3/#4/#6 to dev; verify a fresh save (email + attachments → `.eml` + attachment child docs) via Dataverse.
3. Implement real Dataverse entity search (#2 / task 026): FetchXML across Matter/Project/Invoice/Account/Contact, security-trimmed, paged, + tests. Then investigate #5 (AI-index of archives).
