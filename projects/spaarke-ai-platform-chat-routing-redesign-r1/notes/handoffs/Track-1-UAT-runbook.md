# Track 1 — Production-Smoke UAT Runbook

> **Project**: `spaarke-ai-platform-chat-routing-redesign-r1`
> **Date authored**: 2026-06-25
> **Authored by**: main session (Track 1 production-smoke unblocker work)
> **Status**: Ready for UAT after the deploy sequence below
> **Supersedes**: §"Track 1 — Production smoke unblockers" of `119-phase-5r-exit-gate.md`
> **Related**: ADR-037, FR-46…FR-59, plan.md Phase 7

This runbook is the operational counterpart to the Track-1 deliverables shipped by this
session: BFF orchestrator wiring (FR-49 / FR-50), `Deploy-Playbook.ps1` NodeTypeMap
extension, and the new `Add-NodeTypeChoiceOption.ps1` Dataverse schema script. Once the
three deploy steps below are executed in order, Phase 7 task 146 (UAT) is unblocked.

---

## Deploy sequence (DEV first; same steps for QA / PROD)

Pre-flight: `pac auth select` set to the target environment; `az login` complete;
`DATAVERSE_URL` env var set to the target environment URL (e.g.
`https://contoso.crm.dynamics.com`).

### Step 1 — Apply the Dataverse schema delta

Adds option value `100000004 → "DeliverComposite"` to the
`sprk_playbooknode.sprk_nodetype` choice column. **Idempotent — safe to re-run.**

```powershell
# Dry-run preview (no Dataverse changes)
pwsh ./scripts/dataverse/Add-NodeTypeChoiceOption.ps1 -DryRun

# Live apply
pwsh ./scripts/dataverse/Add-NodeTypeChoiceOption.ps1
```

Expected output:
- Reads current options (3 pre-existing: `100000000 AIAnalysis`, `100000001 Output`,
  `100000002 Control`; if Track-1 step 2 already deployed, `Workflow` may be present).
- If `100000004` already exists → emits `IDEMPOTENT: option value 100000004 already exists`
  and exits 0.
- Otherwise → POSTs `InsertOptionValue`; re-reads option list; prints the new option
  marked with `<-- NEW`.

Verification: re-run with `-DryRun` and confirm `100000004 → DeliverComposite` is in
the option list.

### Step 2 — Deploy the 118R multi-node migration

Apply
`infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json` to
Dataverse. Now unblocked because (a) the schema script in Step 1 added the new
option value AND (b) `Deploy-Playbook.ps1` `$NodeTypeMap` now includes
`'Workflow' = 100000003` and `'DeliverComposite' = 100000004` (Track-1 deliverable 2).

```powershell
# Force overwrite the existing summarize-document-for-workspace playbook with the
# multi-node DeliverComposite version.
pwsh ./scripts/Deploy-Playbook.ps1 `
    -DefinitionFile ./infra/dataverse/playbooks/summarize-document-for-workspace-v1-multinode.json `
    -Force
```

Expected output:
- Schema validation gate passes for every node's `sprk_configjson`.
- Existing playbook (if any) is deleted; new playbook + nodes + scope associations
  + canvas layout are created.
- Final summary line shows the new playbook GUID + node count.

Verification: in Dataverse Maker, open the new playbook record; confirm a Workflow
node + DeliverComposite terminal node exist; confirm the canvas wiring matches the
JSON definition.

### Step 3 — Deploy the BFF

Build + publish + deploy. The Track-1 BFF changes are:

1. `Api/Ai/ChatEndpoints.cs` — FR-49 wiring (emits `playbook_options` SSE event when
   user turn has attachments) + new `/api/ai/playbook-dispatch/execute` endpoint
   (FR-50).
2. `Services/Ai/Chat/PlaybookDispatcher.cs` — new public method
   `BuildDispatchResultForPlaybookAsync` used by the new endpoint.
3. No DI changes (`PlaybookOptionsEventBuilder` already registered by task 117a).
4. No new packages, no schema changes outside Step 1.

```powershell
# Verify BFF builds clean
dotnet build src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj

# Publish and measure size (NFR-01 binding ceiling 60 MB)
dotnet publish -c Release src/server/api/Sprk.Bff.Api/ -o deploy/api-publish/

# Deploy via the standard script (resolves to dev/qa/prod by env-name).
# The exact command varies per environment; use whichever you used for the 118b deploy.
pwsh ./scripts/Deploy-BffApi.ps1 -Environment dev   # adjust to actual script name
```

Expected: publish size in the same neighborhood as the Phase 5R final measurement
(46.32 MB compressed per the 119 exit gate). Track-1 adds ~250 LOC + no new package
references — expected delta well under the +5 MB per-task escalation threshold.

---

## UAT smoke scenarios

After the three deploy steps, exercise these scenarios against the SpaarkeAi
LegalWorkspace dashboard chat. All scenarios assume an authenticated tenant user
with access to the workspace + at least one matter record.

### Scenario A — FR-49 / FR-50 happy path (PDF attachment → link buttons → execute)

Steps:

1. Navigate to a matter row → open LegalWorkspace dashboard.
2. Open SprkChat (the conversation pane).
3. Upload a small PDF via the attachment button (e.g. an NDA — anything that the
   `summarize-document-for-workspace` playbook can handle).
4. In the chat input box, type: `summarize this for me` and submit.

Expected outcome:
- The chat stream emits a `playbook_options` SSE event (FR-49). Inline link buttons
  for the top-N candidates render (FR-50 FE shipped in task 117b).
- An "Open Library" CTA renders alongside (FR-51 invariant — always on).
- **No auto-execution** (FR-48 invariant).
- BFF logs (Application Insights / Log Analytics) show:
  `FR-49 playbook_options emitted — session={SessionId}, candidateCount=N, rerankInvoked=…, attachmentCount=1, sessionFileIdCount=1`
  (ADR-015: NO message text, NO filename in any log line).

5. Click the highest-confidence candidate link button (typically `summarize-document-for-workspace`).

Expected outcome:
- The FE POSTs to `/api/ai/playbook-dispatch/execute` with
  `{ playbookId, sessionAttachmentIds: ["..."], originalMessage: "summarize this for me", sessionId }`.
- BFF emits the playbook output via SSE — for the workspace destination, the
  output_pane event flows + the workspace tab renders the composite output
  (multi-section structured output — FR-52/53/54 from Wave 5-C).
- BFF logs show:
  `FR-50 playbook-dispatch/execute: handled — session=…, playbookId=…, outputType=Text, destination=Workspace, handled=True`
- Section_started → section_data → section_completed events fire in section-name order
  (Summary → Key Terms → Action Items — section names per ADR-037 / 118R definition).

### Scenario B — Backward-compatibility (no attachment → existing single-match flow)

Steps:

1. Same setup as Scenario A — open SprkChat fresh (or in a new session).
2. WITHOUT uploading any file, type: `/summarize` (or natural language equivalent).
3. Submit.

Expected outcome:
- The chat flow takes the existing R6 dispatcher path (Stage 1 vector match → Stage 2
  LLM refinement → PlaybookOutputHandler streams the matched playbook output).
- No `playbook_options` event is emitted (FR-49 only fires when attachments are present).
- The chat-summarize playbook (or the dispatcher's single best match) executes
  directly. Behavior identical to pre-Track-1.
- BFF logs continue to show `PlaybookDispatcher: dispatch completed in N ms — Match (...)`
  exactly as before.

### Scenario C — Library modal pre-filter (FR-51 link)

Steps:

1. Same setup as Scenario A; emit a `playbook_options` event with the PDF upload.
2. Click "Open Library" instead of a candidate button.

Expected outcome:
- `sprk_playbooklibrary` Code Page opens in a modal (Xrm.Navigation.navigateTo with
  target:2).
- The modal receives the `sessionAttachmentIds` data envelope and pre-filters the
  library to playbooks plausibly relevant to the attachment classification (when
  available upstream).
- Selecting a playbook in the modal → invokes the same
  `/api/ai/playbook-dispatch/execute` endpoint with the chosen `playbookId`.

### Scenario D — Tier-1 telemetry hygiene spot-check

Within 15 minutes of running scenarios A–C, query Application Insights / Log Analytics:

```kusto
// ADR-015 binding check: NO user message content, NO filename in log lines
traces
| where timestamp > ago(15m)
| where message contains "FR-49" or message contains "FR-50"
| project timestamp, message, severityLevel
| limit 50
```

Expected: every captured log line carries ONLY:
- Deterministic IDs: sessionId, tenantId, playbookId (GUIDs)
- Counts: candidateCount, attachmentCount, sessionFileIdCount, sessionAttachmentCount
- Flags: rerankInvoked, handled
- Outcome tags: outputType, destination

FAIL conditions:
- Any log message containing the verbatim user message text (e.g. "summarize this for me")
- Any log message containing the attachment filename (e.g. "NDA.pdf")
- Any log message containing extracted text content

If a FAIL surfaces, do NOT continue UAT — file an ADR-015 violation issue and
investigate the offending log statement.

---

## Rollback procedure

If Step 2 (118R migration) breaks production playbook execution:

1. Revert the playbook via the prior snapshot:
   `infra/dataverse/playbooks/summarize-document-for-workspace-v1-snapshot.json`
   (or whichever pre-118R snapshot exists). Force-deploy it:
   ```powershell
   pwsh ./scripts/Deploy-Playbook.ps1 -DefinitionFile <snapshot-path> -Force
   ```
2. The schema option value `100000004` can REMAIN — it's additive and inert without
   the migrated playbook.
3. If Step 3 (BFF deploy) breaks the chat flow: redeploy the prior BFF release.
   The Track-1 BFF changes degrade gracefully — the new endpoint returns 404 if not
   present (the FE already handles this per ConversationPane.tsx:1576).

---

## Phase 7 hand-off

Once UAT scenarios A–D pass:

1. Mark `current-task.md` action item: `Phase 7 unblocked`.
2. Proceed with plan.md Phase 7 sequence (per 119 exit gate recommended order):
   - Task 141 — CapabilityRouter retirement (atomic deletion of 10 files)
   - Task 142 — FE SoftSlashRouter dict removal verification
   - Tasks 143–148 + 150 per the plan

Track 1 itself adds no new dependencies on Phase 7 — the deferred items 3–7 from the
119 exit gate (118a P2/P3, 117a flag, 118R lock, full UAT) are independent of these
unblockers.

---

## File index (Track 1 deliverables produced 2026-06-25)

| Deliverable | Path | Purpose |
|---|---|---|
| BFF wiring | `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | FR-49 emit + FR-50 endpoint |
| BFF dispatcher | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs` | `BuildDispatchResultForPlaybookAsync` direct-execute path |
| Deploy script | `scripts/Deploy-Playbook.ps1` | NodeTypeMap +Workflow +DeliverComposite |
| Schema script | `scripts/dataverse/Add-NodeTypeChoiceOption.ps1` | Idempotent option-value insert for `100000004` |
| Direct-execute unit tests | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/PlaybookDispatcherDirectExecuteTests.cs` | Covers `BuildDispatchResultForPlaybookAsync` |
| Wire-contract unit tests | `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/PlaybookDispatchExecuteRequestTests.cs` | Locks the FE-BE request shape |
| Runbook | `projects/spaarke-ai-platform-chat-routing-redesign-r1/notes/handoffs/Track-1-UAT-runbook.md` | This document |

*Track 1 commits remain uncommitted — main session will batch + commit.*
