# Communication Architecture Assessment (Email Send + Receive)

> **Date**: 2026-06-05
> **Authors**: ai-assessment-r1 (3 parallel auditor agents synthesized)
> **Trigger**: Recurring "small" bugs in email send (latest: Summarize wizard sending `bodyFormat: 'Text'` which the BFF rejects). Investigation surfaced 6 distinct client-side implementations of the same BFF call, two parallel server subsystems, and undocumented duplicate component forks in `src/solutions/LegalWorkspace/`.
> **Scope**: Client-side send/receive callers; server-side Communication subsystem; documentation landscape. **Outbound is primary focus**; inbound covered for context.
> **Status**: Assessment — not yet a decision. Recommendations in §7 are for review.

---

## 1. Executive Summary

The Spaarke platform has a **documented canonical pattern** for email send/receive — and the code does not follow it. The pattern is centered on:

- **One client wrapper**: `sendCommunication()` in [`src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts`](../../src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts)
- **One server pipeline**: `CommunicationService.SendAsync()` in [`src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs)
- **One Dataverse entity**: `sprk_communication` (with `sprk_communicationaccount` for sender config)

In practice:

| Layer | Documented canonical | Reality |
|---|---|---|
| Client | 1 typed wrapper (`sendCommunication`) | **6 distinct implementations**, only 1 uses the wrapper |
| Server | 1 subsystem (`Services/Communication/`) | **2 parallel subsystems** (`/api/communications/*` R2 + `/api/v1/emails/*` R1), with an orphaned job type registered but no handler |
| Solutions | Single shared library (`@spaarke/ui-components`) | LegalWorkspace solution has **stale duplicate forks** of 8+ wizards, 6–85 days behind shared lib |
| Docs | R2 canonical (`communication-service-architecture.md`) | R2 + R1 docs both live; readers can't tell which is current |
| ADRs | — | **No Communication ADR exists.** Every other major subsystem has one. |

**Cost of the gap**: today's Summarize wizard bug, the earlier `FilePreviewDialog` URL bug (FAILURE-MODES.md AP-4), the `attachmentDocumentIds` semantics drift, all 3 different error-handling contracts callers must reason about — these are the same systemic root cause repeating.

**This assessment recommends a phased consolidation** (§7) and a Communication ADR (§8). It does NOT recommend a big-bang refactor.

---

## 2. The Canonical Pattern (per docs)

The clearest statement is [`.claude/patterns/api/send-email-integration.md:24`](../../.claude/patterns/api/send-email-integration.md):

> **Three patterns**:
> - UI → `POST /api/communications/send`
> - AI Playbook → `send_communication` tool
> - Server → inject `CommunicationService`

Reinforced by:

- [`.claude/FAILURE-MODES.md:265`](../../.claude/FAILURE-MODES.md) AP-4 — *"Prefer the typed `communicationApi.ts` wrapper for any communications endpoint — typed wrappers cannot accidentally omit `/api`."*
- [`docs/architecture/communication-service-architecture.md:231-236`](../architecture/communication-service-architecture.md) — MUST/MUST NOT constraints (Graph send is the critical path; tracking is best-effort; register via `CommunicationModule`).
- [`.claude/adr/ADR-024-polymorphic-resolver-pattern.md:103`](../../.claude/adr/ADR-024-polymorphic-resolver-pattern.md) — `IncomingAssociationResolver` is the canonical server-side resolver.
- [`.claude/constraints/jobs.md:131`](../../.claude/constraints/jobs.md) — `Communication:{messageId}:Process` is the canonical idempotency key.

**No contradicting statement exists in any doc**. The drift is purely in code.

---

## 3. Current State Inventory

### 3.1 Client send-email implementations (6 distinct)

| # | Implementation | Location | Callers | bodyFormat | Error handling |
|---|---|---|---|---|---|
| **1** | `sendCommunication()` typed wrapper (**canonical**) | [`communicationApi.ts:171`](../../src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts) | `DocumentEmailWizard.tsx:499` (only) | `'html'` default → maps to `'HTML'` | Throws; parses ProblemDetails; extracts `detail`/`title` |
| **2** | `EntityCreationService.sendEmail()` (inline fetch) | [`EntityCreationService.ts:335-379`](../../src/client/shared/Spaarke.UI.Components/src/services/EntityCreationService.ts) | All 5 create-record wizards (Matter, Project, Event, Todo, WorkAssignment) — both `@spaarke/ui-components` AND LegalWorkspace forks | `'HTML'` default | Returns `{success, warning?}`; **never throws**; swallows errors as warnings |
| **3** | Inline fetch in `SummarizeFilesDialog.tsx` (shared lib) | [`Spaarke.UI.Components/.../SummarizeFilesDialog.tsx:436`](../../src/client/shared/Spaarke.UI.Components/src/components/SummarizeFilesWizard/SummarizeFilesDialog.tsx) | Summarize wizard via shared lib | Hardcoded `'PlainText'` | Inline try/catch; pushes string to `warnings[]` |
| **4** | Inline fetch in `SummarizeFilesDialog.tsx` (LegalWorkspace duplicate) | [`LegalWorkspace/.../SummarizeFiles/SummarizeFilesDialog.tsx:458`](../../src/solutions/LegalWorkspace/src/components/SummarizeFiles/SummarizeFilesDialog.tsx) | LegalWorkspace bundle | Hardcoded `'PlainText'` | Same as #3 |
| **5** | Inline fetch in `FilePreviewDialog.tsx` (LegalWorkspace) | [`LegalWorkspace/.../FilePreview/FilePreviewDialog.tsx:155`](../../src/solutions/LegalWorkspace/src/components/FilePreview/FilePreviewDialog.tsx) | LegalWorkspace `SendEmailDialog.onSend` callback | Hardcoded `'PlainText'` | Throws plain `Error` |
| **6** | Webresource `sprk_communication_send.js` (ribbon) | [`webresources/js/sprk_communication_send.js:548`](../../src/client/webresources/js/sprk_communication_send.js) | Dataverse ribbon on `sprk_communication` form | Hardcoded `'HTML'` | Parses ProblemDetails; `setFormNotification`. **Self-bootstraps MSAL — does NOT use `@spaarke/auth`** |

**Direct hidden cost — three different error-handling contracts for the same endpoint**:
- #1 throws structured errors
- #2 silently returns `{success:false}` (warnings get hidden in UI completion flows)
- #3/#4 push strings to a warnings array
- #5 throws plain `Error`
- #6 form notification

Callers cannot reason about email-send errors consistently.

### 3.2 LegalWorkspace solution: stale duplicate forks

The `src/solutions/LegalWorkspace/` solution contains its own copies of 8+ wizard components that ALSO exist in `@spaarke/ui-components`. Shared-lib copies are 6–85 days NEWER. Today's `'Text'` → `'PlainText'` fix landed 2026-05-26 in both — only because someone happened to fix both. Past fixes have not been so lucky.

| Component family | Shared lib mtime | LegalWorkspace mtime | Gap |
|---|---|---|---|
| `SendEmailStep` (CreateMatter) | 2026-06-02 | 2026-03-23 | **+71 days** |
| Create Project wizard | 2026-06-04 | 2026-03-20 | **+76 days** |
| Create Event wizard | 2026-06-01 | 2026-03-20 | **+73 days** |
| Create Todo wizard | 2026-06-01 | 2026-03-20 | **+73 days** |
| Create Work Assignment dialog | 2026-06-01 | 2026-03-24 | **+69 days** |
| Create Work Assignment service | 2026-06-01 | 2026-05-26 | +6 days |
| SummarizeFiles dialog (the broken one) | 2026-06-01 | 2026-05-26 | +6 days |
| Summarize SendEmail step | 2026-06-02 | 2026-03-09 | **+85 days** |
| Create Matter — `matterService` | 2026-06-01 | 2026-04-05 | **+57 days** |

**Architectural smell**: `LegalWorkspace/.../WorkAssignmentWizardDialog.tsx:31` cross-package source-path imports the shared-lib `SendEmailStep` via `../../../../../client/shared/Spaarke.UI.Components/src/components/CreateRecordWizard/steps/SendEmailStep` — i.e., it bypasses the npm package boundary entirely.

**The duplicate-fork situation is not documented anywhere**. `SPAARKEAI-COMPONENT-MODEL.md` describes LegalWorkspace's structure but does not flag the duplicates as drift or specify a canonical choice.

### 3.3 Server subsystem (Communication = R2; Email = R1; both still live)

The BFF has **two parallel email-domain subsystems**:

**`Services/Communication/` (R2 canonical)** — 13 services per [`communication-service-architecture.md`](../architecture/communication-service-architecture.md):
- `CommunicationService` — outbound send pipeline (SharedMailbox + User OBO)
- `ApprovedSenderValidator` — sender allowlist
- `CommunicationAccountService` — `sprk_communicationaccount` queries + Redis cache + daily-send counters
- `MailboxVerificationService`, `EmlGenerationService`, `GraphMessageToEmlConverter`, `GraphAttachmentAdapter`, `IncomingCommunicationProcessor`, `IncomingAssociationResolver`, `CommunicationJobProcessor`, `GraphSubscriptionManager`, `InboundPollingBackupService`, `DailySendCountResetService`

**`Services/Email/` (R1 parallel)** — operates on Dataverse standard `email` activity entities (NOT `sprk_communication`):
- Triggered by Dataverse Service Endpoint webhook `POST /api/v1/emails/webhook-trigger` ([`EmailEndpoints.cs:75`](../../src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs))
- Enqueues `JobType="ProcessEmailToDocument"` ([`EmailEndpoints.cs:240`](../../src/server/api/Sprk.Bff.Api/Api/EmailEndpoints.cs))
- **No `ProcessEmailToDocumentJobHandler` exists** in `Services/Jobs/Handlers/` (confirmed via Glob). `StartupDiagnostics.cs:60` lists the handler as "expected." Webhook enqueues jobs no consumer processes — a latent dead-letter generator.
- Synchronous save path (`SaveEmailAsDocumentAsync` at `EmailEndpoints.cs:287`) **bypasses `sprk_communication` entirely** — creates `sprk_document` directly with `sprk_emaillookup=emailId`. This produces documents that are NOT linked through the canonical communication record.

**Three `.eml` generators** (all use MimeKit):
1. `EmlGenerationService` — outbound (takes `SendCommunicationRequest`)
2. `GraphMessageToEmlConverter` — inbound from Graph webhook (takes `Message`)
3. `EmailToEmlConverter` — R1 parallel path (takes Dataverse `email` activity)

**Two webhook signing keys**:
- `Communication:WebhookSigningKey` — Graph subscriptions (HMAC `X-Hub-Signature-256`)
- `EmailProcessing:WebhookSigningKey` — Dataverse Service Endpoint (HMAC `X-Dataverse-Signature`)
- `EmailProcessing:WebhookSecret` retained as deprecated; AUTH-7 retires it.

**`GraphMessageId` is faked from `correlationId`** ([`CommunicationService.cs:350,659`](../../src/server/api/Sprk.Bff.Api/Services/Communication/CommunicationService.cs)) because Graph `sendMail` does not return a message ID. Same value lands in `sprk_graphmessageid`. **Reply thread matching is broken for replies to messages we sent** — `IncomingAssociationResolver.cs:171` looks up by `sprk_internetmessageid` (null for outbound).

### 3.4 Inbound email surfaces

Server-side has full inbound infrastructure (webhook → SB job → `IncomingCommunicationProcessor` → archive + associate). Client-side has essentially **none**:

- No component renders inbound `sprk_communication` records as threads/messages.
- `EmailProcessingMonitor` PCF reads `/api/admin/email-processing/stats` (operational stats only).
- `sprk_emailactions.js` ribbon "Save to Document" calls `/api/emails/convert-to-document` (the R1 path — bypasses `sprk_communication`).
- Calendar/EventDetailSidePane render cached email metadata on `sprk_event` records (display-only).

**No "inbox" surface, no thread/reply UI, no read-back of communications. The R2 inbound pipeline produces records that the client UI cannot show.**

### 3.5 Configuration drift

| Configuration source | What it controls | Mutually exclusive with |
|---|---|---|
| `Communication:ApprovedSenders[]` (appsettings) | Tier 1 sender allowlist | — |
| `sprk_communicationaccount` (Dataverse) | Tier 2 sender allowlist with verification, daily limit, archive opt-in | — |
| `RegistrationEmailService` hard-codes `"demo@demo.spaarke.com"` as sender | Registration emails | Must be present in approved senders |

The deployment guide ([`COMMUNICATION-DEPLOYMENT-GUIDE.md:249-272`](../guides/COMMUNICATION-DEPLOYMENT-GUIDE.md)) requires setting 9 `Communication__*` + 8 `EmailProcessing__*` config keys per env. The admin guide ([`COMMUNICATION-ADMIN-GUIDE.md:32`](../guides/COMMUNICATION-ADMIN-GUIDE.md)) says R2 is "unified configuration" with "no code deployments needed." These framings are inconsistent.

---

## 4. Divergence Analysis — Why "Same Endpoint, 6 Implementations" Matters

The 6 client implementations don't just look duplicated — they DIVERGE in behavior in ways that produce distinct bugs:

| Axis | Variation across the 6 |
|---|---|
| `bodyFormat` | Default differs (`'HTML'` vs hardcoded `'PlainText'` vs `'HTML'`). Today's bug was driven by one path hardcoding the wrong string. |
| `sendMode` / `fromMailbox` | Only #1 and #6 support choosing. The other 4 silently use BFF default (`SharedMailbox`). **No wizard offers user-mode OBO sending today.** |
| `attachmentDocumentIds` semantics | The wrapper docstring at [`communicationApi.ts:8-19`](../../src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts) explicitly says the BFF expects driveItem IDs — but `DocumentEmailWizard.tsx:494` passes `sprk_document` GUIDs. **This is a confirmed latent bug in the CANONICAL path.** Not documented anywhere in `docs/`. |
| `associations` | Each impl sends a different shape. #1 sends 4-field `{entityType, entityId, entityName?, entityUrl?}`. #2 omits `entityUrl`. #3 + #4 send NONE. #5 sends `[{entityType:'sprk_document', entityId}]`. #6 collects 8 fields. **The "always set associations" rule from `send-email-integration.md:26` is violated in 3 of 6 impls.** |
| Recipient normalization | 5 different implementations of "split string on `;,` and trim." |
| Retry behavior | None of the 6 retry on transient failure. `authenticatedFetch` does ONE 401 retry, but transient 5xx is unhandled at every layer. |
| Auth bootstrapping | #1–#5 use injected `authenticatedFetch` (good). #6 self-bootstraps MSAL — drift risk if `@spaarke/auth` evolves. |

**The drift is the bug surface**. Every new caller picks an implementation, and each implementation has its own subset of features and bugs.

---

## 5. Documentation Gaps to Fill Before/During Consolidation

The docs auditor confirmed: a canonical pattern IS documented. But specific gaps prevent the docs from preventing future drift:

1. **No Communication ADR.** Every major subsystem has one in `.claude/adr/` (ADR-007 SPE, ADR-013 AI tools, ADR-028 Auth). Communication has only the pattern pointer + architecture doc. There is no concise `MUST / MUST NOT` constitutional doc.
2. **`attachmentDocumentIds` semantics gap.** The field name in the BFF request says "DocumentIds" but it actually expects SPE driveItem IDs. Documented ONLY in a code comment at [`communicationApi.ts:8-19`](../../src/client/shared/Spaarke.UI.Components/src/services/communicationApi.ts). Not in `docs/`. This is the latent bug in the canonical client path.
3. **LegalWorkspace duplicate-component policy.** Not documented anywhere. The componentization audit doc exists but doesn't flag these forks or specify which is canonical.
4. **SendMode decision guidance.** Admin guide explains the two modes; no doc tells a feature developer when to default to `user` vs `sharedMailbox`.
5. **Attachment-policy single source of truth.** `CHAT-ATTACHMENT-POLICY.md` is the canonical doc for chat — explicitly scopes itself away from email. The 150 / 35 MB Communication caps live only in the architecture doc and the user guide; no analogous policy doc.
6. **Caller-side retry contract.** Architecture doc says "callers handle retries" but never explains what callers must do (idempotency? Polly? what timeouts?).
7. **R1 docs still live.** `email-to-document-architecture.md` and `email-to-document-automation.md` self-mark "retained for historical context" but their continued presence is a navigation hazard.
8. **R1 field-name drift in `sdap-overview.md`.** [`sdap-overview.md:357-363`](../architecture/sdap-overview.md) lists `sprk_emailsubject`, `sprk_emailfrom`, etc. on `sprk_document` — fields from the R1 era. Reader cannot tell whether these are still in use alongside the R2 `sprk_communication` fields.
9. **`bff-extensions.md` doesn't flag Communication as a sensitive surface.** New `/api/communications/*` routes don't get the same review pressure as new AI surfaces.

---

## 6. Adjacent / Compounding Problems

These are NOT inside the email subsystem but they make consolidation harder:

- **The LegalWorkspace duplicate-component pattern is broader than email.** Same forks exist for non-email components (FilePreview, dataset viewer, etc.). Solving the email duplication touches the meta-question: is LegalWorkspace a fork or an embedder of `@spaarke/ui-components`?
- **The PCF compiled bundles include shared-lib email components.** `SemanticSearchControl` bundles `EntityCreationService`, `DocumentEmailWizard`, `SendEmailDialog`, AND the inline-fetch `SummarizeFilesDialog`. Same for `SpeDocumentViewer`. Fixing the shared lib requires rebuilding/redeploying ALL surfaces that bundle it, every time. (See backlog #11 on build determinism.)
- **The orphaned `ProcessEmailToDocument` job type is silently broken.** It enqueues to a queue with no consumer; messages go to DLQ. Nobody noticed because the parallel synchronous save path works.
- **`@spaarke/auth` adoption is incomplete (backlog #9).** The webresource path (`sprk_communication_send.js`) self-bootstraps MSAL. Aligning email send with `@spaarke/auth` is part of consolidation.

---

## 7. Recommended Consolidation Plan (Phased)

The user's stated preference is "minimize friction; don't paper over root issues" — so the recommendation balances those.

### Phase 1 — Unblock (today, ~30 min)
- **Deploy the SummarizeFilesWizard solution** with the existing `'PlainText'` fix. Unblocks today's user testing without code changes.
- No consolidation work yet; just unblock.

### Phase 2 — Stop the bleeding (1 PR, 1 day)
- **Add a Communication ADR** in `.claude/adr/` codifying:
  - Client MUST use `sendCommunication()` for any new send path; document `attachmentDocumentIds` semantics; document the association rule; document the 3 error contracts as a drift to be eliminated.
  - Server MUST register Communication-touching services via `CommunicationModule` only.
  - LegalWorkspace solution MUST NOT fork shared-lib email components; if forked, MUST be marked deprecated with a target retirement date.
- **Add the missing pattern docs**: `attachmentDocumentIds` semantics doc, SendMode decision criteria, attachment policy doc analogous to `CHAT-ATTACHMENT-POLICY.md`.
- **Mark R1 docs** (`email-to-document-architecture.md`, `email-to-document-automation.md`) as `**RETIRED — see communication-service-architecture.md**` at the top, and remove from any pointer table.

### Phase 3 — Consolidate clients (1 wave, ~1 day per wizard)
For each of the 6 implementations, replace with `sendCommunication()`:
- **Wave A**: SummarizeFilesDialog (shared lib + LegalWorkspace) → use `sendCommunication`. Drops impls #3 + #4.
- **Wave B**: FilePreviewDialog (LegalWorkspace) → use `sendCommunication`. Drops impl #5.
- **Wave C**: EntityCreationService.sendEmail → re-implement as a thin adapter that calls `sendCommunication`. Migrates all 5 create-record wizards transparently. Drops impl #2.
- **Wave D**: Webresource `sprk_communication_send.js` → align with `@spaarke/auth`. Drops impl #6's MSAL self-bootstrap.

Each wave is independent; can ship one PR per wave.

### Phase 4 — Resolve LegalWorkspace duplicates (decision + execution)
**Decision needed first**: is LegalWorkspace meant to embed `@spaarke/ui-components` (in which case retire the forks) or fork it (in which case codify the fork lifecycle)? This is outside the email scope but blocks Phase 3 Wave A/B from cleanly closing the duplication.

Recommendation: retire the forks. The shared-lib copies are 6-85 days newer and authoritative. Migration is per-component (each wizard's LegalWorkspace fork → re-export from `@spaarke/ui-components`).

### Phase 5 — Server cleanup (1 PR)
- **Retire `Services/Email/` R1 subsystem** OR document why it coexists with R2 permanently. If retiring: migrate `RegistrationEmailService` and the Office add-in `/api/v1/emails/{id}/save-as-document` to call `CommunicationService.SendAsync`/inbound pipeline. Remove the orphaned `ProcessEmailToDocument` job type. Consolidate to one webhook signing key (`Communication:WebhookSigningKey`).
- **Fix the `GraphMessageId` fake** — capture the real Internet-Message-Id from the sent message via `graphClient.Me.Messages[id]` lookup post-send, OR generate a deterministic one and store it on `sprk_internetmessageid` so reply-matching works.
- **Fix the `attachmentDocumentIds` semantics**: either rename the BFF field to `AttachmentDriveItemIds` OR translate from `sprk_document` GUIDs server-side. Either way, the wrapper docstring should match reality.

### Phase 6 — Inbound surface (project-sized)
Building a client UI to read `sprk_communication` records is a project, not a phase of this consolidation. It's worth a follow-on referral.

### What this assessment does NOT recommend
- A big-bang refactor that touches all 6 impls + all duplicate forks + both server subsystems in one PR. The risk surface is too large.
- Disabling any current path without a migration target. Each impl has users; consolidate forward, don't break first.
- Documenting status quo as "intentional." It isn't. The docs already say what should be canonical; the code needs to catch up.

---

## 8. Recommended Backlog Referrals

Add to `projects/_backlog/needs-a-project.md`:

- **#12 Communication architecture consolidation** — Execute Phase 2–5 above. Track per-phase as separate PRs.
- **#13 LegalWorkspace solution fork lifecycle** — Decision: retire or codify. Prerequisite to #12 Wave A/B closing cleanly.
- **#14 Build a Communication client surface (inbox / thread view)** — Currently zero client code reads `sprk_communication`. The server pipeline produces records nobody can browse.
- **#15 Retire `Services/Email/` R1 subsystem or document permanent coexistence** — Decision needed before removing the parallel webhook + orphan job.

---

## 9. Appendices — Source Inventories

The full inventories from the three auditor agents are captured below for reference. These are unedited audit outputs.

### Appendix A — Client-side inventory

> See agent A output in the assessment workspace. Headline findings: 6 distinct implementations, 3 error contracts, LegalWorkspace forks 6–85 days behind shared lib, `attachmentDocumentIds` semantics drift, recipient-normalization implemented 5 different ways.

### Appendix B — Server-side inventory

> See agent B output. Headline findings: 2 parallel subsystems (`Services/Communication/` R2 + `Services/Email/` R1), orphaned `ProcessEmailToDocument` job, 3 `.eml` generators, 2 webhook signing keys, `GraphMessageId` faked breaking reply-matching.

### Appendix C — Documentation inventory

> See agent C output. Headline findings: canonical pattern documented in `.claude/patterns/api/send-email-integration.md` + `docs/architecture/communication-service-architecture.md`; no Communication ADR; LegalWorkspace duplicate policy undocumented; `attachmentDocumentIds` semantics gap; R1 docs still live alongside R2.

---

*This assessment is a precursor to a consolidation decision, not a self-executing plan. See §7 for the recommended phased approach.*
