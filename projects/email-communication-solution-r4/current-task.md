# Current Task State — email-communication-solution-r4

> **Last Updated**: 2026-07-18 (by context-handoff)
> **Recovery**: Read "Quick Recovery" first. Project is COMPLETE + MERGED + DEPLOYED; this is a live **UAT bug-fix cycle** focused on the **Association Engine matching + review UI**.

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Phase** | Post-ship UAT iteration + **W6 task 060 CLOSED** (2026-07-18) — last substantive code task done; only wrap-up (090) + owner PCF import remain |
| **Branch** | `work/email-communication-solution-r4` · HEAD = origin/master + **uncommitted task-060 diff** (3 files) awaiting commit |
| **Tree** | task-060 changes staged-in-working-tree; `.claude/worktrees/` untracked scaffold |
| **BFF** | LIVE on `spaarke-bff-dev` (healthz 200) — all matching fixes deployed + hash-verified (last deploy `7ea2fea02`, 47.03 MB). Task 060 is client-only (no BFF touch). |
| **Task 060 (W6) — DONE** | SummarizeFilesDialog + FilePreviewDialog migrated to canonical `sendCommunication()`/`SendEmailDialog`; DocumentEmailWizard NO-CHANGE (retracted premise). Shared-lib tsc+eslint clean, 27+5 tests green, ADR-045/019/021/028 clean, body-format regression caught+fixed. See TASK-INDEX row 060. |
| **Next action** | (1) commit task-060 diff + merge to master; (2) run **task 090 wrap-up** (`/test-diet`, README→Complete, lessons-learned, archive); (3) OWNER still imports PCF `CommunicationConnectionsSolution_v1.2.1.zip` + hard-refresh (UAT verify — unchanged) |

### PCF ZIP ready for owner import (Dataverse)
- `src/client/pcf/CommunicationConnections/Solution/bin/CommunicationConnectionsSolution_v1.2.1.zip` (v1.2.1 — display fix)
- Import via `pac solution import --path <zip> --publish-changes`. Footer must read **v1.2.1** after hard-refresh; the instant tell it loaded = button says **"Confirm N suggestions"** (not "Accept all").
- NOTE: v1.2.0 is ALREADY imported/deployed (confirmed via Dataverse `customcontrols` = 1.2.0). v1.2.1 is the follow-up that shows the match reason on Primary/Filed rows (v1.2.0 wrongly gated it to `suggested` only) + surfaces reason/number on the collapsed card.

### Critical context — this session's arc (association matching + review UI)
The project shipped (W0–W8) earlier. This UAT cycle iterated the **Association Engine matching** end-to-end. Everything is committed + on master (`9062ef34b`); only owner-side PCF v1.2.1 import remains. Key fixes, newest first — see the numbered "What was fixed" log below for detail:
1. **Match-ranking by location + review-UI legibility** (`7ea2fea02` + PCF v1.2.0/v1.2.1): confidence tiered by WHERE a name matched — number 0.97 / subject 0.95 / body 0.88 / attachment 0.75 — so the exact subject match wins over incidental attachment noise (which drops below the 0.85 conflict floor, clearing spurious Ambiguous). Provenance carries `where=/matched=/name=/number=/reason=`. PCF shows the reason + record number per row + on the card, groups duplicate-named candidates, relabels "Accept all"→"Confirm N".
2. **Phase 2** (`52457f442`): attachment text as a match signal (ITextExtractor, bounded/non-fatal, before association) + `sprk_invoice` added to `RecordSyncJob` index coverage.
3. **Deterministic record-NAME/number match rung 3.5** (`cb018a12f`): the core gap — no deterministic rung matched on record NAMES. New `RecordNameMatchRung` (retrieve via keyword index then verify exact name/number in subject/body/attachment); surface-for-review, never auto-files; runs in the deterministic pass but excluded from auto-file eligibility.
4. **No-user tenant fallback** (`3af4571e9`): the inbound engine had no user `tid` → record search filtered `tenantId eq 'system'` while records are indexed under `AzureAd:TenantId` → 0 matches. Fixed the fallback.

**Verified working in prod (owner screenshots):** Smith v Smith matter → 95% Primary (subject match); a second email's matter → 97% (reference-number match). Engine correctness confirmed; the only open item is the v1.2.1 display import.

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

8. **Match-ranking-by-location + review-UI legibility** (merged `7ea2fea02`, BFF deployed + PCF v1.2.0 packed): fixes the UAT where a reused test attachment's "Test New Matter via Workspace" text crowded out the exact subject match "Smith v Smith". **BFF** (`RecordNameMatchRung`): confidence now tiered by WHERE the name matched — number 0.97, name-in-subject 0.95, body 0.88, attachment 0.75 (subject/body/attachment verified as separate corpora). Subject match outranks attachment noise, which drops below the 0.85 conflict floor (clears the spurious Ambiguous). Provenance carries a parseable `where=/matched=/name=/number=/reason=` payload. **PCF** (CommunicationConnections 1.1.4→**1.2.0**, ZIP `Solution/bin/CommunicationConnectionsSolution_v1.2.0.zip` — OWNER imports): shows the human match reason + record number per row; groups duplicate-named ambiguous candidates into one expandable `Name · N records` row (display-only, no record dedup); "N possible matches — choose one" reflects distinct names; "Accept all" → "Confirm N suggestions" (tooltip; only shown for safely-acceptable non-ambiguous suggestions). Tests: RecordNameMatch/mapper/semantic 46 green. BFF 47.03 MB (§10 ✓).

7. **Phase 2 — attachment-text match signal + invoice index coverage** (merged `52457f442`, BFF deployed): completes the owner matching spec (subject OR body OR **attachment text** → match record number/name/description, across matter/project/**invoice**). (a) `NormalizedMessage.AttachmentText` — the inbound processor extracts bounded plain text from attachments via `ITextExtractor` (PDF/DOCX/TXT) BEFORE association (best-effort/non-fatal, bounded by `AttachmentMatchOptions`: 5 attachments, 10 MB each, 4000 chars), so a record named only in an attachment (e.g. engagement letter) still matches; both rungs include it in their query (RecordNameMatch cap raised to 6000, keyword-only so no embedding cost). (b) `RecordSyncJob` now indexes `sprk_invoice` (name=`sprk_name`, number=`sprk_invoicenumber`) alongside matter/project/contact/account. Tests: Communication 548 green. Package 47.02 MB (§10 ✓). **Operational note**: verify the BFF managed identity has read access to `sprk_invoice` in Dataverse — if not, invoice sync fails non-fatally (logged, other entities continue). Invoices appear in the index within 15 min of the next RecordSync cycle.

6. **Deterministic record-NAME/number match rung (rung 3.5)** (merged `cb018a12f`, BFF deployed): closes the gap where exact-name records (matter/project literally named in the email) fell through to the fuzzy semantic rung and got mis-ranked (the reranker floated "Test New Matter via Workspace" above the exact "Smith v Smith"). New `RecordNameMatchRung` retrieves candidates from `spaarke-records-index` by **keyword (BM25) ranking** (`RecordSearchOptions.PreferKeywordRanking` — skips the Azure semantic reranker) then **deterministically verifies** the record's name (normalized token subsequence) or reference number (normalized alphanumeric) appears verbatim in subject/body. Surfaces EVERY verified type (matter AND project AND invoice). Per owner spec (2026-07-17): **surface-for-review, never auto-file** — excluded from the mapper's auto-file-eligible set + not an AI rung (runs in the deterministic pass but lands Suggested); never auto-dedups. Mapper improvement: a conflict on ONE field (duplicate same-named records) no longer suppresses a clean association on ANOTHER field (Ambiguous now writes non-conflicting winners). `SemanticMatchRung` also switched to keyword-first (fuzzy fallback). Tests: Communication 367 + RecordSearch 30 (+ merged messaging = 535 total green). Package 47.02 MB (§10 ✓). Owner matching spec captured in auto-memory `association-matching-expectation`. **Phase 2 pending**: attachment-text matching + add `sprk_invoice` to RecordSyncJob index coverage.

5. **Semantic rung found no existing records — tenant-filter mismatch** (`RecordSearchService`, merged `3af4571e9`, BFF deployed): the inbound engine (rung 4) runs from a background/job caller with **no user `tid` claim**, so record search filtered on `tenantId eq 'system'`, but `RecordSyncJob` stamps every indexed record with `AzureAd:TenantId` → **0 matches** for all inbound emails. Only rung 5's metadata-only "looks like a new matter" survived. Empirically reproduced vs live `spaarke-records-index`: `system` filter → 0 hits; real-tenant filter → "Smith v Smith" matter @ score 15.5. **Fix**: no-user tenant fallback is now `AzureAd:TenantId` (symmetric with sync side). Regression test `SearchAsync_WhenNoUserContext_FiltersOnConfiguredTenant_NotSystemLiteral`. Tests: RecordSearch 28/28, Communication 355. Package 46.68 MB (§10 ✓). NOTE: RecordSyncJob syncs `sprk_matter`/`sprk_project`/`contact`/`account` only — **invoices are NOT yet indexed**, so invoice semantic-matching remains a known gap.

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
