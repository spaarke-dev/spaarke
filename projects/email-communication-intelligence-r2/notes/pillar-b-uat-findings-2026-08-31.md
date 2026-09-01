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

---

## #7 Document-profile playbook fails — ROOT-CAUSED + FULLY DOCUMENTED 2026-09-01

> **Full write-up**: [`docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`](../../../docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md) — the authoritative reference (three AI execution models, all three document-profile entry paths, the failure mechanism, fix options, change-safety checklist). GitHub #919.

During UAT it emerged that indexing + association now work, but every saved Document shows `sprk_filesummarystatus = Failed (100000004)` — the **document-profile AI playbook is not completing**. The AI *analysis* node succeeds; the **Update Record** write-back node is what dies.

**Root cause (empirically confirmed 2026-09-01 — live Dataverse config pull + App Insights `0x0A` path):** the "Document Profile" **playbook**'s Update Record node (`sprk_playbooknode 0fa4e8db-…`, playbook `18cf3cc8-…`, graph = AI Analysis → Update Record → Deliver-To-Index) stores its config in the **Playbook-Builder wrapper format** — an outer object whose `configJson` property is the real config encoded as a **JSON string**. `fieldMappings[0]` is `sprk_filesummary` (the multi-line summary), value `{{output_aiAnalysis.output.sprk_filesummary}}`.

The Layer-1 renderer (`PlaybookOrchestrationService.RenderConfigJsonStructurally`) parses the **outer** wrapper (valid JSON), walks it, and hits the nested `configJson` **string**. Because that string starts with `{` (not `{{`), `IsPureTemplate` is false → it renders as a **mixed string** via the flat engine, injecting the multi-line summary's **raw newlines**. `WriteStringValue` escapes them at the *outer* level (outer stays valid) but **never descends into the nested JSON**, so the nested string's logical content keeps raw `0x0A`. Then `UpdateRecordNodeExecutor.ParseConfig` unwraps via `GetString()` (decodes → raw newline) and **re-parses** the nested string → `JsonException: '0x0A' is invalid within a JSON string. Path: $.fieldMappings[0].value` → `catch → null` → node validation fails → playbook stops → `filesummarystatus=Failed`.

**⚠️ Correction to the prior checkpoint note:** the earlier hypothesis — "orchestrator **falls back to flat string substitution at `:2284`**" — is **WRONG**. The outer wrapper *is* valid JSON, so the structural path runs and the `:2284` fallback never fires. The real defect is the **nested-string blind spot** in the structural walker (it escapes at the outer level, not inside the nested `configJson`-as-a-string). A fix aimed only at `:2284` would not touch the failing code path.

**Key architectural finding:** document profiling exists as **both** a node-based *playbook* AND a direct *Action* (ACT-011), using the SAME Action logic. The **wizard** and **Compose** paths use the direct-Action linear spine (`ActionRunner`) and are **structurally immune** (no config re-parse). Only the **Outlook / app-only background** path uses the node playbook and hits this bug. This is why the same feature "works here, fails there."

**Why it was masked:** the STORED `sprk_configjson` is a valid template (`{{…}}`), so `UpdateRecordParseConfigReproTests` (stored-config guard) is green. Only the RENDERED config (AI text with newlines) is invalid. `ParseConfig`'s `catch { return null; }` hid the `JsonException`; the precise path was recovered with temp instrumentation (since removed; `ParseConfig` left `internal` for the test).

**Fix options (owner to choose — NOT yet applied):** (1, recommended) make Layer 1 **wrapper-aware** — recurse `RenderConfigJsonStructurally` into a nested string that is itself JSON-containing-a-template, so newlines are escaped at the nested level (fixes Update Record / Create Task / Create Notification / Send Email at once); (2) defer substitution and let the executor render values (it already does at `UpdateRecordNodeExecutor.cs:222`); (3, strategic) converge the app-only path onto the direct-Action spine and retire the node playbook for this consumer. Whichever — add a **rendered-config** regression test (multi-line + embedded-quote through the wrapper format), not just the stored-config test. Full detail: the architecture doc above.

**Blast radius:** SHARED — any wrapper-format node config receiving multi-line/quoted upstream content. Owned by AI-orchestration (`spaarke-ai-architecture-redesign-r2`), NOT the Office save path.
