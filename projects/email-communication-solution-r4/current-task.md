# Current Task State — email-communication-solution-r4

> **Last Updated**: 2026-07-17 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Project is COMPLETE + MERGED + DEPLOYED; this is a live **UAT bug-fix cycle**.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | Post-ship UAT iteration (owner testing on spaarkedev1) |
| **Branch** | `work/email-communication-solution-r4` · HEAD = **`2ce3ed416`** = origin/master (everything merged) |
| **Tree** | clean (only `.claude/worktrees/` untracked scaffold) |
| **BFF** | LIVE on `spaarke-bff-dev` (healthz 200) — all UAT fixes deployed + hash-verified |
| **Next action** | OWNER imports the 2 PCF ZIPs + re-sends the "Smith v Smith" test email; verify status=Suggested, contact Filed, "Create Matter (AI)" row surfaces |

### PCF ZIPs ready for owner import (Dataverse)
- `src/client/pcf/CommunicationConnections/Solution/bin/CommunicationConnectionsSolution_v1.1.4.zip`
- `src/client/pcf/CommunicationActions/Solution/bin/CommunicationActionsSolution_v1.1.1.zip`
- Import via `pac solution import --path <zip> --publish-changes` (temp-rename `Directory.Packages.props` if CPM blocks).

### Critical context
The project shipped (W0–W8, merged to master, BFF + SpaarkeAi deployed) earlier. This session was an **owner UAT loop** that found + fixed real bugs. All code is committed + on master (`2ce3ed416`). Only remaining = owner-side PCF import + re-test.

---

## What was fixed this session (all on master + BFF deployed)

**BFF (Sprk.Bff.Api) — merged + deployed:**
1. **Body-blind classifier** (`GraphMessageNormalizer`): HTML emails set `BodyText=null` → classifier saw only the subject. Now strips HTML → plain text into `BodyText`.
2. **Attachments not materialized** (`IncomingCommunicationProcessor`): inline `$expand=attachments` omits `contentBytes` → 0 `sprk_document`. Now re-fetches the attachments collection when bytes are missing.
3. **Multi-association — AI pass always runs** (`IncomingAssociationResolver`): removed the `!decision.AutoFiled` gate. Semantic (rung 4) + classification (rung 5) now always run so the engine finds matter/project/invoice even when a contact matched.
4. **Contact is a fallback, not a match** (`AssociationStatusMapper`, owner Option A): auto-file/Resolved now requires a **substantive** target (matter/project/invoice/servicerequest/event/workassignment). `FallbackFields = {sprk_regardingperson, sprk_regardingorganization, sprk_regardingaccount}` are still written (filed) but don't clear the auto-file bar → contact-only email = **Suggested** (stays in the review queue). Tests: Communication suite **325 green** (+2 mapper contract tests, +1 ladder test updated).

**PCF Connections (v1.1.0 → v1.1.4):**
- Collapsed **primary card** (number+name from `sprk_regardingrecordname/number`) + **Open** icon → **modal** (80vw×80vh, X close top-right + Close lower-left).
- Review surface is a **grid** (Type│Record│Confidence│Status│Actions) with per-row **icon** actions (Confirm/Change/Set-Primary; File-here on sub-rows).
- **Authoritative filed list**: reads the record's real `sprk_communication` regarding lookups via `COMMUNICATION_REGARDING_FIELDS` (NOT the sprk_todo `TODO_REGARDING_CATALOG` names — that was the "card says not filed / modal says filed" bug; wrong field names threw the `$select`).
- Per-slot filed state from each candidate's **`written`** flag (not global Resolved) → filed contact + unfiled matter-suggestion coexist.
- **"Create Matter (AI)"** row (`deriveAiSuggestedTypes` parses `types=[...]` from the classification signal) + AI suggestions count toward "to review".
- `Link another` type-first menu; Create-from-email removed (moved to Actions).

**PCF Actions (v1.0.1 → v1.1.1):**
- Toolbar: **Reply · Reply All · Forward · New** (left) + spacer + **right-justified** icon-only group (Save-to-SharePoint · Create Event/To-Do/Invoice, ✨ when engine-suggested). Dropped Send/Save-Draft. OOB-matched typography. Composer gained `initialCc` for Reply All.

---

## Engine behavior model (post-change — for anyone reasoning about associations)
- Deterministic rungs 0–3 always run; **AI rungs 4–5 now ALSO always run** (bounded by kill-switches + ADR-016 budget + ADR-014 cache).
- **Auto-file → Resolved** requires a SUBSTANTIVE deterministic winner ≥ 0.85 (ADR-018 kill-switch). AI never auto-files.
- **Fallback** (contact/org/account) matches are written but land **Suggested**, not Resolved.
- PCF reads status + per-candidate `written` + live regarding lookups; surfaces filed vs to-review per slot.

## Deploy facts
- BFF: `spaarke-bff-dev` / RG `rg-spaarke-dev`; deploy = `pwsh scripts/Deploy-BffApi.ps1` (hash-verify + healthz). App settings for the rungs already set (`Communication__SemanticMatch__Enabled=true`, `Communication__AiClassification__Enabled=true`, AutoFile 0.85).
- Dataverse org: `spaarkedev1.crm.dynamics.com`. Monitored mailboxes: `testuser1@spaarke.com`, `mailbox-central@spaarke.com` (active Graph subs, auto-renewing via `GraphSubscriptionManager`). Inbound poll backstop = 5 min (`EmailProcessing__PollingIntervalMinutes`).
- Live-query Dataverse: `TOKEN=$(az account get-access-token --resource https://spaarkedev1.crm.dynamics.com --query accessToken -o tsv)` then curl the Web API. Note: `sprk_communication` regarding lookup for Contact is **`sprk_regardingperson`** (not sprk_regardingcontact).

## Open / not-blocking
- PCF commits are on master already (part of the merged branch); ZIPs still need owner import to Dataverse.
- UAT helpers: `notes/uat-mock-provenance.md` (console script to seed multi-slot provenance), `notes/UAT-CHECKLIST.md`, `notes/DEPLOYMENT-CHECKLIST.md`.
- No pending decisions; owner approved Option A (fallback no-auto-file).
</content>
