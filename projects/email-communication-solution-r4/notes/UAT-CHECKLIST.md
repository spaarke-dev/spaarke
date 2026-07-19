# email-communication-solution-r4 — UAT Checklist

> **Authored**: 2026-07-16 · **Environment**: spaarkedev1 (dev) / `spaarke-bff-dev`
> **Scope**: everything R4 shipped that a tester can exercise — the 6-rung Association Engine + BFF endpoints (deployed), the Connections PCF (multi-association review), the Actions PCF (Reply/Forward/Send/Save/Archive), direction-symmetric enrichment, kill-switches, and auth.
> **How to use**: work top-to-bottom. Each row has a **Pass/Fail** box and a **Notes/feedback** column — fill those in and hand back. Anything you can't run because a prerequisite isn't in place, mark **Blocked** and say why.

---

## 0. Prerequisites & sequencing (READ FIRST)

UAT splits into two tiers. **Tier 1 (API) can run now** — the BFF is deployed to `spaarke-bff-dev`. **Tier 2 (PCF UI) requires the owner-side 043 form config to be done first** (import both PCF solutions, place them on the OOB `sprk_communication` form, retire the Send ribbon, pack the Awaiting-Association view). Do the P-rows below before starting Tier 2.

| ID | Prerequisite | Done? | Notes |
|----|--------------|:---:|-------|
| P-1 | BFF deployed to `spaarke-bff-dev`; `GET /healthz` → 200 | ✅ | Verified 2026-07-18 — healthz 200 |
| P-2 | App settings set: `Communication__SemanticMatch__Enabled=true`, `Communication__AiClassification__Enabled=true`, `Communication__AutoFile__Enabled=true` / `__Threshold=0.85`, tokenized `AiSearch` index names populated | ✅ | Verified 2026-07-18 — SemanticMatch/AiClassification=true; AutoFile set explicit (was code-default true/0.85); index tokens resolve (`spaarke-records-index` @2, `spaarke-invoices-index` @6). Webhook secrets moved to Key Vault (mirror prod) |
| P-3 | Connections PCF imported | ✅ | Verified in Dataverse `customcontrol` — **v1.2.1** (supersedes the v1.0.1 named here) |
| P-4 | Actions PCF imported | ✅ | Verified in Dataverse `customcontrol` — **v1.1.1** (supersedes the v1.0.1 named here) |
| P-5 | Both PCFs placed on OOB `sprk_communication` form (Connections in accessories column bound to `sprk_associationprovenance`+`sprk_associationstatus`; Actions bound to `sprk_communicationtype`) | ☐ | OWNER to confirm (maker UI — not machine-verifiable from here) |
| P-6 | Legacy `sprk_communication_send.js` web resource + Send button removed; **Create-To-Do button KEPT** | ☐ | OWNER to confirm |
| P-7 | "Communications Awaiting Association" system view published | ☐ | OWNER to confirm |
| P-8 | Dataverse env vars present: `sprk_MsalClientId`=`170c98e1…`, `sprk_BffApiAppId`=`1e40baad…`, `sprk_BffApiBaseUrl` | ☐ | OWNER to confirm |
| P-9 | A test mailbox is monitored by a live Graph subscription (for inbound tests) | ☐ | OWNER to confirm (needed for D-1 inbound + the definitive webhook-KV resolution proof) |

---

## Tier 1 — BFF / Association Engine (API-level; runnable now)

> Tester note: authenticated calls need a bearer token for `api://1e40baad-…/SDAP.Access`. Unauthenticated calls to protected routes MUST return **401** (never 404 — 404 would mean the route didn't register).

### 1A. Endpoint smoke (auth + routing)

| ID | Test | Steps | Expected | Pass/Fail | Notes |
|----|------|-------|----------|:---:|-------|
| A-1 | Health | `GET /healthz` | 200 | ✅ | 200 (2026-07-18) |
| A-2 | Suggest — route registered | `POST /api/communications/{guid}/suggest-associations` **without** token | 401 (not 404) | ✅ | 401 → route registered |
| A-3 | Archive — route registered | `POST /api/communications/{guid}/archive` **without** token | 401 (not 404) | ✅ | 401 |
| A-4 | Send — route registered | `POST /api/communications/send` **without** token | 401 (not 404) | ✅ | 401 |
| A-5 | Status — route registered | `GET /api/communications/{guid}/status` without token | 401 | ✅ | 401 |
| A-6 | Webhook anonymous validation handshake | `POST /api/communications/incoming-webhook?validationToken=abc123` | 200, body echoes `abc123` as text/plain | ✅ | 200, body `abc123`, `text/plain` |
| A-7 | Webhook rejects bad HMAC | `POST /api/communications/incoming-webhook` with a change-notification body and a wrong/absent `X-Hub-Signature-256` | 401/400 (rejected; no job enqueued) | ✅ | 401 rejected. NOTE: proves rejection but not KV-key resolution — definitive proof = D-1 (real signed notification accepted) |

### 1B. Suggestion preview endpoint (read-only — the review surface's data source)

| ID | Test | Steps | Expected | Pass/Fail | Notes |
|----|------|-------|----------|:---:|-------|
| B-1 | Suggest returns candidates | Pick a real `sprk_communication` GUID; `POST …/{id}/suggest-associations` with token | 200 with target(s) + confidence + provenance rationale | ✅ | `6edc948a` → 200, `status:Suggested`, `autoFileEligible:false`, candidates = contact@0.7 (ParticipantCorrelation) + matter@0.97 (RecordNameMatch:number) — matches written provenance |
| B-2 | **Read-only invariant** | Before/after B-1, read the record's `sprk_associationprovenance` + `sprk_associationstatus` | **Unchanged** — suggest never writes | ✅ | `modifiedon`=2026-07-18T16:16:33 (original processing); multiple suggest calls a day later did NOT change it → read-only confirmed |
| B-3 | Unknown ID | Suggest with a random GUID | 404 ProblemDetails | ✅ | 404 RFC 7807: `type=.../COMMUNICATION_NOT_FOUND`, title/detail/status/correlationId present |
| B-4 | Auth scoping (NFR-07) | As a user WITHOUT access to the matter, suggest on a communication regarding that matter | Denied/empty per matter-level scope — no cross-matter leakage | ⬚ | OWNER — needs a restricted-access test user (my token identity is privileged; can't prove scoping alone) |
| B-5 | Privilege is flagged, not decided (ADR-015) | Use a communication whose content trips privilege signals | Response *flags* privilege as a signal; does not auto-decide/auto-file on it | ⬚ | Pending a privilege-content email (sample records = privilege None) |

### 1C. Association Engine — 6-rung ladder & status mapping

> The ladder: deterministic rungs 0–3 at ≥0.85 → **Resolved (auto-filed)**; 0.50–0.85 OR any AI rung (4 semantic / 5 classify) → **Suggested**; <0.50/none → **Pending Review**; conflicting high-confidence → **Ambiguous**. AI rungs (4–5) **never** auto-file.

| ID | Test | Steps | Expected | Pass/Fail | Notes |
|----|------|-------|----------|:---:|-------|
| C-1 | Rung 0 — explicit ref | Inbound/caller-supplied explicit regarding | Matched at rung 0; provenance names rung 0 | ⬚ | Not exercised in the 2026-07-18 sample (rung exists; no explicit-ref email in set) |
| C-2 | Rung 1 — thread continuity | Reply to a prior email already associated to a matter (`inReplyTo`/`references`/`conversationId`) | Same matter matched via thread rung; provenance cites thread | ✅ | Verified via real data (`048e7239`, `d58fd828`) — provenance cites `thread:ancestor:<msgid>→parent:{guid}:sprk_regardingperson`, confidence 1.0 |
| C-3 | Rung 2 — participant/domain | Sender is a known contact / sender domain matches `sprk_organization` (via `sprk_domain`) | Contact/org matched; **org writes `sprk_regardingorganization`→`sprk_organization`, NOT `account`** | ✅ | Participant→contact verified (`participant:sender:ralph.schroeder@spaarke.com→contact` @0.7). Org-by-domain not in sample |
| C-4 | Rung 3 — structural detector | Email that is a calendar invite / e-sign completion / has an invoice # / court-filing marker | Detector fires; correct target type surfaced | ⬚ | Not exercised in sample |
| C-5 | Rung 4 — semantic (AI) | Fuzzy matter/project/invoice reference (no deterministic hit) | Lands as **Suggested** with match reasons in provenance; **never auto-filed** | ⬚ | Deterministic RecordNameMatch hit on both sample records so semantic rung not the resolver; rung present |
| C-6 | Rung 5 — classify (AI) | Ambiguous email with no record match | Category/urgency/obligations surfaced as **Suggested/Ambiguous**; never auto-filed | ✅ | `AiClassification` signal present (metadata-only) in both records: category/urgency/obligations/types — never auto-filed |
| C-7 | Auto-file threshold | Deterministic match ≥0.85 | Status → **Resolved**, regarding auto-filed | ✅ | `048e7239` → Resolved, `autoFiled:true`, `autoFileThreshold:0.85`, `killSwitchEnabled:true` (confirms AutoFile config live) |
| C-8 | Suggest band | Deterministic match 0.50–0.85 | Status → **Suggested** (not auto-filed) | ✅ | `6edc948a` → Suggested (deterministic-eligible 0.7 in band). NOTE: reason string interpolates 0.97 not 0.7 — cosmetic |
| C-9 | Pending band | No/low match (<0.50) | Status → **Pending Review** | ✅ | 2 records at status Pending Review in the set (Mailbox Verification Test, Inbound Test) |
| C-10 | Ambiguous | Two conflicting high-confidence targets | Status → **Ambiguous** | ✅ | `d58fd828` → Ambiguous: two duplicate "Smith v. Smith" projects @0.95 conflict on `sprk_regardingproject` (written:false); clean siblings (matter, person) still written:true |
| C-11 | Provenance recorded | Any of the above | `sprk_associationprovenance` JSON records rung + per-attribute confidence for each match | ✅ | Rich JSON verified — version/direction/decision/rungsFired/candidates[field,target,confidence,written,conflict,contributors]/signals |
| C-12 | Per-rung telemetry | Trigger inbound processing; check App logs | `EventId 4501/4502` per-rung telemetry present | ⬚ | Needs App log access — not verified from Dataverse |

### 1D. Direction symmetry & enrichment (FR-08/09)

| ID | Test | Steps | Expected | Pass/Fail | Notes |
|----|------|-------|----------|:---:|-------|
| D-1 | Inbound enrichment | Send a test email INTO the monitored mailbox | New `sprk_communication` created; runs association → status + provenance | ✅ | Verified via the 2026-07-18 inbound records — each created with `sprk_associationstatus` + `sprk_associationprovenance` (see Tier 1C). **CAVEAT**: those inbounds predate today's (2026-07-19) webhook→Key-Vault migration, so the definitive *post-migration* webhook-KV resolution proof still needs ONE fresh inbound email today (a new record appearing ⇒ KV refs resolved). |
| D-2 | **Outbound enrichment** | Send an outbound email via the Actions PCF / `/send` | Outbound communication auto-associates AND is RAG-indexed | ⚠️ | **PARTIAL / expectation corrected.** `/send` → 200, real email sent from `mailbox-central@spaarke.com` (`97e5b972`, direction Outgoing). **(a) engine auto-associate = deferred BY DESIGN** — `CommunicationEnrichmentService.RunAssociationAsync` is a documented no-op for outbound (associations are client-supplied only; engine-over-outbound deferred to direction-symmetry). No `associations` passed → none written (correct). **(b) RAG-index = BLOCKED by F-1** — `archivedDocumentId:null`, `archivalWarning:"...archival failed: Access denied"` (same SPE-container 403). Outbound archival has NO mailbox fetch ⇒ definitively pins F-1 to the container write. |
| D-3 | Best-effort / non-fatal (NFR-06) | Force an enrichment sub-step to fail (e.g., temporarily flip a kill-switch mid-flow) | Send/inbound-capture still succeeds; failure is logged, not fatal | ✅ | Demonstrated organically — outbound send succeeded + `sprk_communication` created despite archival "Access denied" (surfaced as `archivalWarning`, non-fatal) |
| D-4 | Reply stamps thread cols | Reply from Actions PCF | `sprk_inreplyto` / `sprk_internetmessageid` populated | ⚠️ | `sprk_internetmessageid` populated (verified on inbound `048e7239`). Reply-specific `sprk_inreplyto` needs a reply via Actions PCF (owner/H-4) |

### 1E. Kill-switches (ADR-018 / ADR-032) — no redeploy

| ID | Test | Steps | Expected | Pass/Fail | Notes |
|----|------|-------|----------|:---:|-------|
| E-1 | Auto-file off | Set `Communication__AutoFile__Enabled=false` (restart/refresh config) | A ≥0.85 deterministic match now lands as **Suggested**, NOT auto-filed — no redeploy | ☐ | |
| E-2 | Semantic rung off | `Communication__SemanticMatch__Enabled=false` | Rung 4 no longer contributes; engine still runs 0–3 + 5 | ☐ | |
| E-3 | Classify rung off | `Communication__AiClassification__Enabled=false` | Rung 5 no longer contributes; no errors on unconditional endpoints (Null-Object) | ☐ | |
| E-4 | Restore | Re-enable all three | Behavior returns to baseline | ☐ | |

### 1F. Archive to SharePoint (FR — on-demand)

| ID | Test | Steps | Expected | Pass/Fail | Notes |
|----|------|-------|----------|:---:|-------|
| F-1 | Archive creates docs | `POST …/{id}/archive` on an un-archived communication with attachments | 200; `.eml` Document + one Document per attachment; `attachmentDocumentsCreated` reflects count | ❌ | **CONFIRMED REAL DEFECT (2026-07-19) — owner reproduced via H-8 in the Actions PCF (real MSAL user) → same 403.** ROOT-CAUSED from the live BFF log: the archive `.eml` upload runs as the **BFF managed identity** (`UploadSessionManager.UploadSmallAsync` → `GraphClientFactory.ForApp()` → `mi-bff-api-dev` / `5967251e`), and its Graph `PUT /beta/drives/{container}/root:/communications/{id}/{file}.eml:/content` → **403 `Microsoft.Graph ODataError: Access denied`**. Fails on BOTH the demo container AND the `DefaultContainerId` (retarget tested + reverted) ⇒ NOT the container, NOT the caller token (upload always runs as the MI regardless of caller). **The MI is not authorized on SPE container type `8a6ce34c-…`.** SPE writes that DO work go via the **SPE owning app** (`1e40baad` + `spe-app-cert`, `SpeAdminGraphService`, fetches `spe-owning-app-secret` — seen in the log). Stack: `UploadSessionManager:45 ← ArchiveToSpeAsync:1794 ← ArchiveExistingAsync:195 ← ArchiveCommunicationAsync:420`. **Blocks H-8 + outbound RAG (D-2).** FIX (dev + SPE admin): (1) grant MI `5967251e` write on container type `8a6ce34c-…`, OR (2) route the archive upload through the owning-app identity that already has container access. Dev to confirm whether attachment materialization currently succeeds via MI or owning-app to pick the cleaner fix. |
| F-2 | Idempotent | Archive the same communication again | 200 `alreadyArchived: true`; no duplicate Documents | ⬚ | Gated on F-1 fix |

---

## Tier 2 — Connections PCF (multi-association review; needs P-3/P-5/P-7)

> Opens on the OOB `sprk_communication` form's right accessories column. Renders provenance as typed connection slots.

| ID | Test | Steps | Expected | Pass/Fail | Notes |
|----|------|-------|----------|:---:|-------|
| G-1 | Renders + version | Open a `sprk_communication` with provenance | Slots render; footer reads **v1.0.1 • Built 2026-07-16** | ☐ | |
| G-2 | Confidence + rationale per slot | Inspect a Suggested slot | Shows confidence + provenance rationale for that target | ☐ | |
| G-3 | Confirm one slot (≤1 click) | Click Confirm on a slot | Regarding filed via shared resolver (ADR-024); form refreshes; typed lookup + denorm fields set | ☐ | |
| G-4 | Accept-all | Click Accept-all with multiple slots | All slots filed; **highest-priority slot (Matter first) owns the denormalized primary** | ☐ | |
| G-5 | Status advances to Resolved | Confirm every review slot | `sprk_associationstatus` → **Resolved** once all confirmed | ☐ | |
| G-6 | Change / override reason | Click Change on a slot, enter a reason, Save | Reason persisted into provenance JSON as feedback signal (NO auto-relearn); slot not silently re-filed | ☐ | |
| G-7 | Link another record | Click "Link another", pick a record across the regarding catalog | Picked record filed as regarding | ☐ | |
| G-8 | Set primary | On a confirmed slot, Set Primary | Denormalized Regarding fields point at that target; sibling associations untouched | ☐ | |
| G-9 | Create from this email | Click Create (Event / To Do / Invoice) | Correct target create/quick-create form launches (full create-and-link deferred to RI project — launch-only is expected) | ☐ | |
| G-10 | Read-only mode | Open on a read-only/disabled form | No confirm/change/create affordances; renders as filed | ☐ | |
| G-11 | Error surfacing | Force a write failure (e.g., revoke access mid-action) | Friendly error MessageBar; no silent data loss | ☐ | |

---

## Tier 3 — Actions PCF (Reply/Forward/Send/Save/Archive; needs P-4/P-5/P-8)

| ID | Test | Steps | Expected | Pass/Fail | Notes |
|----|------|-------|----------|:---:|-------|
| H-1 | Renders + auth bootstraps | Open a `sprk_communication` (email type) | Toolbar renders (Reply/Forward/Send · Save Draft · Save to SharePoint); footer reads **v1.0.1**; no sign-in popup on second tab (SSO) | ☐ | |
| H-2 | Zero-config auth fallback | With only env vars set (no PCF input overrides) | Auth still initializes from `sprk_MsalClientId`/`sprk_BffApiAppId`/`sprk_BffApiBaseUrl` | ☐ | |
| H-3 | Misconfig message | Unset an env var | Clear "not configured" MessageBar naming the missing vars (no crash) | ☐ | |
| H-4 | Reply pre-fill | Click Reply | Composer opens pre-filled from record (from/to/subject/body); never opens blank | ☐ | |
| H-5 | Forward | Click Forward | Composer opens in forward mode | ☐ | |
| H-6 | Send | Compose + Send | Email sends via `/api/communications/send`; success toast; form refreshes | ☐ | |
| H-7 | Save Draft | Click Save Draft | Host form saves; "Draft saved." | ☐ | |
| H-8 | Save to SharePoint | Click Save to SharePoint on a saved communication | Calls `/archive`; success msg (+N attachments); idempotent on repeat | ☐ | |
| H-9 | Save-before-archive guard | Click Save to SharePoint on an unsaved record | "Save the communication before archiving" (no call fired) | ☐ | |
| H-10 | Non-email channel | Open a non-email `sprk_communicationtype` record | "This channel is read-only — email actions unavailable" | ☐ | |

---

## Tier 4 — Views, ribbon retirement, cross-cutting

| ID | Test | Steps | Expected | Pass/Fail | Notes |
|----|------|-------|----------|:---:|-------|
| I-1 | Awaiting-Association view | Open the "Communications Awaiting Association" view | Lists communications with status in (Suggested, Pending Review, Ambiguous); matter-level auth-scoped | ☐ | |
| I-2 | Send button retired | Inspect the form ribbon | Legacy Send button/web resource gone; **Create-To-Do button still present & working** | ☐ | |
| I-3 | Send still works from all surfaces | Send from Actions PCF and any migrated caller (e.g., DocumentEmailWizard) | Sends succeed; attachments use SPE driveItem IDs (not `sprk_document` GUIDs) | ☐ | |
| I-4 | No OOB-email regression | General smoke of communication list/forms | No broken refs to the retired `Services/Email` / `/api/v1/emails/*` subsystem | ☐ | |

---

## Out of scope for this UAT (do NOT test — re-homed or deferred)

- **Responsive Intelligence auto-actions** (auto CreateEvent/Task/Notification + Triage summary/checklist) — re-homed to `spaarke-notification-spine-r1`. The Connections "Create from this email" button is **launch-only** here by design (G-9).
- **Outlook add-in** suggestion UI in the save pane — deferred pending the broader add-in strategy.
- Teams/Slack/Gmail/SMS channels — seams only; no channel impl shipped.

---

## Sign-off

| Role | Name | Date | Result (Pass / Pass-with-issues / Fail) |
|------|------|------|------------------------------------------|
| Tester | | | |
| Owner | | | |

> Return this file with Pass/Fail + Notes filled in. Log any Fail/Blocked as a defect (title, steps, expected vs actual, screenshot) so it can be triaged into a fix task or the 090 wrap-up.
</content>
</invoke>
