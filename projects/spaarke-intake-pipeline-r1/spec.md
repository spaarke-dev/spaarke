# Spaarke Intake Pipeline R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-11-25
> **Source**: design.md (v0.3, owner design review complete 2026-11-25)

## Executive Summary

Spaarke Intake Pipeline R1 builds **one request pipeline**:
- doors (Front Door, Console upload, email, Copilot agent) create a request and a run;
- one orchestrator in the BFF classifies the request and routes it to a **harness** for its kind of work;
- an attorney approves the output;
- the result goes back through the door it came in by, and every decision is recorded.

**NDA review** is the first harness. It produces a marked-up Word file plus a clause table by running the accelerator's tested Python redline engines in a Container Apps Job. **Invoice intake** becomes the second harness after eBilling's UAT. The project also clears the code-review cleanup in design §10.1.

## Scope

### In Scope

r1 = design Phases 0–3 (owner clarification, 2026-11-25).

**Phase 0: spikes and cleanup**
- Container Apps Job hosting spike for the accelerator's engines.
- App-only `agreement-classify` / `agreement-review` writing an edit plan.
- One cleanup PR (design §10.1 Phase 0 items).
- ADR-004 Amendment A2 merged.

**Phase 1: core and NDA through the Front Door and the Console**
- Request and run records.
- The orchestrator.
- The `requestType` routing key and the harness registry.
- The NDA harness and the manual-triage path.
- Approval: team Work Item, claim, approve/reject from the Console and the model-driven app.
- Status updates and opt-in status emails.
- The Front Door and Console doors.
- The run admin view with dead-letter re-drive.
- The run-status API.

**Phase 2: email and Copilot doors**
- Intake mailboxes.
- Suggest-only "Submit as request" at other mailboxes (a Job B proposal).
- The `AttachmentClassification` producer (#1920).
- The threaded email return.
- MCP submit and status tools.

**Phase 3: invoice harness**
- Invoice orchestration moves onto the pipeline. Gated on eBilling UAT sign-off.

### Out of Scope

- Phase 4 harnesses (invention, trademark, policy) → r2.
- Creating a run for filing-only content (an email filed to a matter, an Office save, an External SPA upload that is not a request).
- Compose as the NDA front end. The attorney edits the output in Word.
- New review AI, attorney profiles or the NDA specialist (owned by `specialist-legal-agents-r1`).
- Auto-send. A human approves before anything leaves.
- Sending as the attorney's own mailbox.
- Email joining the `ICommunicationChannelIngestor` seam (#3162).
- Editing, versioning or ratifying the NDA standard (owned by the accelerator; this project pins a version).
- Durable Task orchestration.
- Porting the redline engines to C#.
- Automatic request creation from mail at ordinary (non-intake) mailboxes.

### Affected Areas

**BFF** (`src/server/api/Sprk.Bff.Api/`):
- `Services/Jobs/`: new orchestrator handler; DLQ re-drive wiring.
- `Services/Ai/PublicContracts/`: `IConsumerRoutingService.cs` (`RoutingContext`), `ConsumerTypes.cs`, an app-only agreement facade.
- `Services/Communication/`: `EmailChannelSender.cs` (reply threading), `IncomingCommunicationProcessor.cs` (intake mailbox, idempotency key, #1920 producer), `CommunicationService.cs`, `CommunicationProposalApplyService.cs` (new proposal kind), `CommunicationAccountService.cs`.
- `Api/ExternalAccess/ExternalIntakeEndpoints.cs` (Front Door enqueue) and `ExternalMessageEndpoints.cs` (return message).
- `Api/Mcp/LegalOperations/`: new submit and status tools.
- `Api/Admin/`: run admin view.
- `Infrastructure/DI/`: module registrations; `StartupDiagnostics.cs`.
- `Infrastructure/Storage/BlobGateway.cs`: staging.
- `Services/Finance/**`: **Phase 3 only**, after eBilling UAT sign-off.

**Other code:**
- `src/server/shared/`: only if envelope types are shared (optional, ADR-004 A2).
- Container image built from `src/accelerators/core/engines/` on master (after PR #3163). New container entry point; engines are not copied.

**Dataverse:**
- `sprk_servicerequest`: new columns.
- `sprk_processingjob`: request and output lookups, job type, stages.
- `sprk_communicationaccount`: intake flag.
- `sprk_backgroundjob`: description text.
- MDA service-request form and command.

**Client:**
- Console: `src/solutions/SpaarkeAi/**` (Work Item approve/reject, "submit as a request").
- External SPA (Front Door): run status and return message.

**Infra and CI:**
- Bicep: Container Apps environment and Job, KEDA Service Bus scaler, staging storage lifecycle rule, queues.
- CI: image build and CVE scan.
- Provisioning step per stamp.

**Docs:**
- `docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md`
- `docs/adr/ADR-036-background-job-infrastructure.md`
- `docs/data-model/` (`sprk_backgroundjobrun`, `sprk_backgroundjob`, `sprk_servicerequest`, `sprk_processingjob`)
- ADR-004 (A2)

## Requirements

### Functional Requirements

**Phase 0: spikes and cleanup**

1. **FR-01 Engine hosting (Container Apps Job).**
   - **What:** a Container Apps Job runs `segment_clauses`, `ooxml_redline`, `summary_table` and `check_outputs` unchanged.
     - It is built from `src/accelerators/core/engines/` on master.
     - It runs on Python 3.11. The engines use the standard library and `lxml` only, as the accelerator ships them. The entrypoint (wrapper) layer alone uses the hash-pinned Azure SDKs (`azure-identity`, `azure-servicebus`, `azure-storage-blob`) for managed-identity auth, peek-lock with lock renewal, explicit dead-lettering with a reason, and conditional blob writes (owner decision 2026-11-25).
     - It consumes a Service Bus queue as an ADR-004 A2 consumer.
     - It reads its inputs (source `.docx`, edit plan) from, and writes its outputs (redline, external and clean `.docx`, check report) to, stamp-private blob storage, using the stamp's managed identity only.
   - **Acceptance:**
     - (a) On the accelerator's golden fixtures, the job's outputs equal a local run of the same engine version, and `check_outputs` passes.
     - (b) A message that is not a valid Job Contract envelope goes to the DLQ, and no outputs are written.
     - (c) Delivering the same message twice gives one set of outputs at the same deterministic paths, and the marker is created once.
     - (d) The image has no HIGH or CRITICAL CVE.
     - (e) Cold-start and run latency are measured and recorded.
     - (f) The job has no secrets and makes no network calls other than blob storage and Service Bus.
     - (g) A refused edit (anchor not unique, or crossing a revision) is reported in the check report, never applied.
2. **FR-02 App-only agreement review writes an edit plan.**
   - **What:** `agreement-classify` and `agreement-review` run app-only from a job, through a PublicContracts facade, outside any chat session. The review step writes an edit plan that is valid against `edit-plan.schema.json`. Topic ids come from the pinned NDA standard.
   - **Acceptance:**
     - (a) On 3 synthetic NDAs, the plan validates against the schema and every `topic` id exists in the pinned `topics.yaml`.
     - (b) A non-agreement document is classified as not an NDA and produces no plan.
     - (c) No real NDA and no `answer-key/` content is used.
     - (d) `ConsumerTypes` has `agreement-classify` and `nda-review` in `All`, and `/healthz/catalog` reports no drift for them.
3. **FR-03 Cleanup PR** (design §10.1 Phase 0 items, one PR together with the real fixes).
   - **What:**
     - (1b) Delete `DocumentProcessing`, `ProfileSummary` and `EmailAnalysis`: handler, DI line, tests and diagnostics entry.
     - (2) The `StartupDiagnostics` expected list equals the registered handlers.
     - (4) The profile doc describes the direct-Action spine, with the node path as the null-seam fallback.
     - (5a) Both `DocAnalysis:{id}` keys use `AppOnlyDocumentAnalysisJobHandler.ProfileIdempotencyKey(documentId)`, and `MaxAttempts` is 3.
     - (6) The `ConsumerTypes` constants (shared with FR-02), with `nda-standard-summary` checked as well.
     - (7) A reply with `InReplyToMessageId` is sent through Graph `createReply`/`createReplyAll` on the parent message.
     - (8) The ADR-036:104 text and the `sprk_backgroundjobrun` data-model note are fixed.
   - **Acceptance:**
     - (a) Build and all tests pass, and no startup warning names a missing handler.
     - (b) Regression test: the same document enqueued by the email path and the upload path produces one profile.
     - (c) A test send of a reply shows `In-Reply-To` and `References` and threads under the original in Outlook.
     - (d) A send without `InReplyToMessageId` is unchanged (new message).
     - (e) If the parent message can't be found, the send fails with a ProblemDetails error code. It never silently sends unthreaded.
     - (f) ArchTests (including `WorkloadPlacementDocDriftTests`) pass.
4. **FR-04 ADR-004 Amendment A2 merged.**
   - **What:** the A2 text drafted on this branch (concise and full ADR, `jobs.md`, the ADR-052 row, CHANGELOG) merges before or with the first FR-01 code.
   - **Acceptance:** A2 is on master no later than the PR that adds the container consumer.

**Phase 1: core and NDA through the Front Door and the Console**

5. **FR-05 Request record.**
   - **What:** add to `sprk_servicerequest`:
     - channel (Front Door, Console, Email, Copilot);
     - stage (Submitted → In review → Needs triage → Awaiting approval → Returned → Closed);
     - assigned attorney;
     - current run;
     - "send email updates" (boolean).
   - **Acceptance:**
     - (a) The columns are deployed through the schema scripts.
     - (b) Stage is written only by the BFF write path (ADR-002); a client write of stage is refused.
     - (c) The columns don't collide with the ontology project's additions (direction, disposition, response-due date).
6. **FR-06 Run record.**
   - **What:** extend `sprk_processingjob`:
     - a request lookup to `sprk_servicerequest` (and `sprk_invoicesubmission` in Phase 3);
     - an output-document lookup;
     - job type "Intake run";
     - the intake stage values.

     One request can have several runs. Update the `sprk_backgroundjob` "Office-scoped" description (`Entity.xml:13`, `Create-BackgroundJobEntity.ps1:233`, `docs/data-model/sprk_backgroundjob.md`).
   - **Acceptance:**
     - (a) Office runs are unaffected: the Office status API and tests pass unchanged.
     - (b) An intake run is never returned by the Office job-status endpoints, and an Office run never by intake status.
     - (c) The description no longer says Office-only.
7. **FR-07 Orchestrator job.**
   - **What:** one ADR-004 `IJobHandler` runs a request run through these stages:
     1. Receive
     2. Store
     3. Classify
     4. Route
     5. Analyse (harness)
     6. Build (harness)
     7. Persist (one transaction)
     8. Approve (wait as a status)
     9. Return
     10. Record

     It applies the invoice pipeline's step-0 rules (design §3.3): payload → lock → re-read → terminal-first → flag → resume from stored state. It writes "Processing" before any AI call, uses deterministic ids, writes a marker before any send, and writes the terminal status last.
   - **Acceptance:**
     - (a) A crash injected after each stage resumes at that stage, and AI is never re-run for a stage that persisted its result.
     - (b) A duplicate delivery creates no duplicate documents, analyses, Work Items, messages or emails.
     - (c) A run that is already terminal is a no-op on redelivery.
     - (d) An exhausted run lands in the DLQ with admin-attention text and never shows as "Completed".
     - (e) No document content appears in any queue payload or log.
8. **FR-08 Routing key and harness registry.**
   - **What:** `RoutingContext` gains a `requestType` match key (`IConsumerRoutingService.cs:364`). A DI-registered harness registry resolves a harness by request type plus document type (pattern: `ToolHandlerRegistry`, `NodeExecutorRegistry`). The harness contract takes request, run and content, and returns documents, results, an approval-required flag, a return payload and a persist hook.
   - **Acceptance:**
     - (a) A Binding row with `requestType = nda` routes to the NDA harness.
     - (b) Existing `mimeType`/`documentType` routing is unchanged (existing tests pass).
     - (c) A request type with no registered harness routes to manual triage (FR-10), not an exception.
     - (d) Adding a harness needs only a harness class, a DI line and a Binding row.
9. **FR-09 NDA harness.**
   - **What:** classify (FR-02) → review into an edit plan → engine job (FR-01) → redline, external and clean `.docx` plus the page-1 clause table → `check_outputs` passes → persist the outputs as `sprk_document`s on the request and the results as `sprk_analysis`/`sprk_analysisoutput` linked to the run. The pinned NDA standard version is stamped on the run and on every output.
   - **Prior markup (owner):** existing tracked changes and comments in the submitted draft are preserved. They are never accepted, rejected or removed. An edit whose anchor crosses an existing revision becomes a comment-only note, and the clause table lists it as a "manual" row.
   - **Acceptance:**
     - (a) A clean synthetic NDA produces three outputs and a clause table, and `check_outputs` passes.
     - (b) A synthetic NDA with counterparty revisions and comments keeps every one of them in the redline and external outputs, and refused edits appear as comments plus manual rows.
     - (c) A `check_outputs` failure holds the run with admin attention and sends nothing for approval.
     - (d) The version stamp equals the pinned standard version.
     - (e) The Clean output follows the resolution of Unresolved Question 1.
10. **FR-10 Manual-triage path.**
    - **What:** when a request is not an NDA, no harness is registered for its type, or classification confidence is below the harness threshold:
      - the request moves to "Needs triage";
      - a Work Item goes to the owning legal team;
      - no AI output is produced and no automatic reply is sent;
      - the run ends Completed (no-op).
    - **Acceptance:**
      - (a) A non-agreement document creates exactly one triage Work Item.
      - (b) The run is not Failed.
      - (c) The requester gets only the opted-in status email (FR-12).
11. **FR-11 Approval.**
    - **What:**
      - An approval Work Item (`IActionSeam.CreateTaskAsync`) goes to the request's owning legal team (ownership resolver). Claiming it or approving it sets the assigned attorney.
      - One BFF approve/reject endpoint, with endpoint-filter authorization, is reachable from the Console Work Item and from the MDA service-request form.
      - **Approve:** a Decision Record is written; the approved SPE version of the output is captured; the run moves to Approved; Return is enqueued.
      - **Reject:** a Decision Record is written; the run closes; the request returns to "In review". The attorney starts a new run or replies manually.
    - **Acceptance:**
      - (a) A user outside the owning team gets 403, and nothing changes.
      - (b) A second approve of the same run is idempotent (one Decision Record, one Return).
      - (c) The returned file is the version current at approval, including the attorney's Word edits.
      - (d) Approving a run that isn't AwaitingApproval returns 409.
      - (e) Both surfaces call the same endpoint.
12. **FR-12 Status and status emails.**
    - **What:** every stage change updates the request's stage. When "send email updates" is on, exactly one email goes to the requester per stage transition, from the shared legal mailbox. Every decision (approve, reject) is in the Decision Record.
    - **Acceptance:**
      - (a) A redelivered stage transition sends no second email.
      - (b) With the option off, no status email is sent.
      - (c) The email contains status only, no document content beyond what the stage returns.
      - (d) The default follows the Assumptions.
13. **FR-13 Front Door door.**
    - **What:** a Front Door submit (`POST /api/v1/external/servicerequests` plus document upload) creates the run and enqueues it. On Return, the outputs are filed on the request (already visible to the requester) and a message is posted regarding the request, so the bell fires.
    - **Acceptance:**
      - (a) A submitted NDA reaches AwaitingApproval on dev.
      - (b) After approval, the requester sees the returned documents and a bell notification.
      - (c) The requester never sees an unapproved output.
      - (d) A submit with no document creates the request and a triage path, not a failed run.
14. **FR-14 Console door.**
    - **What:** a "Submit as a request" action on an uploaded document in the Console creates the request and the run. The outputs come back on the request.
    - **Acceptance:**
      - (a) The action creates exactly one request and run per click (idempotent on double-click).
      - (b) Console chat upload without the action stays interactive and creates no run.
15. **FR-15 Run admin view.**
    - **What:** an admin view lists runs by stage and status and shows held and dead-lettered runs with their admin-attention text. It re-drives dead-lettered messages through `DeadLetterQueueService` (extending `Api/Admin`).
    - **Acceptance:**
      - (a) Only admins can access it.
      - (b) Re-driving a run resumes it from stored state without re-running completed AI stages.
      - (c) Re-drive of a terminal run is refused.
16. **FR-16 Run-status API.**
    - **What:** an authorized read of a request's runs and stages (ADR-017) for the Console, the Front Door and the MCP status tool.
    - **Acceptance:**
      - (a) A caller without access to the request gets 403/404 and no data.
      - (b) An external requester sees only their own requests.
      - (c) Errors are ProblemDetails with stable error codes (ADR-019).

**Phase 2: email and Copilot doors**

17. **FR-17 Intake mailbox.**
    - **What:**
      - An intake flag on `sprk_communicationaccount`.
      - Every mail to an intake mailbox, from **any** sender (owner), creates a request with channel Email and a run. Classification sets the type.
      - A reply on an existing request's thread creates a new run on that request, not a new request.
      - The per-sender daily cap applies (NFR-04).
    - **Acceptance:**
      - (a) A new mail creates one request and one run.
      - (b) A reply on the thread creates a second run on the same request.
      - (c) The sender's mail over the cap is filed with no run and gets admin attention.
      - (d) A mail at a non-intake mailbox never creates a request without FR-18 confirmation.
      - (e) Normal filing (communication, documents, archive) still happens for every mail.
18. **FR-18 Suggest-only "Submit as request".**
    - **What:** at non-intake mailboxes, the triage signal (`CommunicationTriageAi` output, no extra AI call) produces a new **Job B proposal kind**, "Submit as request (type)", in the existing review queue (`CommunicationProposalApplyService`, queue feed `CommunicationEndpoints.cs:382`). Accept creates the request and run under the confirming user. Dismiss writes the audit row.
    - **Acceptance:**
      - (a) No request is ever created without an Accept.
      - (b) Accept is idempotent.
      - (c) Dismiss leaves no request and one Dismissed audit row.
      - (d) Existing field-update proposals behave unchanged.
19. **FR-19 `AttachmentClassification` producer** (#1920).
    - **What:** the email door's classify step enqueues `AttachmentClassification` for invoice-like attachments.
    - **Acceptance:**
      - (a) An invoice attachment at an intake mailbox enqueues the job once.
      - (b) No edits to `Services/Finance/Submission/` before eBilling UAT sign-off.
      - (c) #1920 is updated or closed.
20. **FR-20 Email return.**
    - **What:** the Return stage replies from the shared legal mailbox, app-only (`SendMode.SharedMailbox`), with the approved outputs attached (`AttachmentDocumentIds`, 35 MB cap). It threads under the requester's mail (FR-03 item 7), names the approving attorney in the signature and Cc's them.
    - **Acceptance:**
      - (a) The reply threads in the requester's client.
      - (b) The attachments are the approved versions.
      - (c) Outputs over 35 MB hold the run with admin attention and send no partial mail.
      - (d) A redelivered Return sends no second email (marker before send).
21. **FR-21 Copilot door (MCP).**
    - **What:** new `spaarke_*` MCP tools on the existing server: submit a document for review (creates the request and run as the caller), and request status (links to the outputs). They follow the `mcp-tool-handler` conventions (OBO caller-only, app-only visibility for writing tools, `McpTools` ledger, tests).
    - **Acceptance:**
      - (a) Submit creates one request and run attributed to the caller.
      - (b) Status returns only requests the caller can read.
      - (c) The writing tool is not model-visible where conventions require app-only visibility.
      - (d) Conformance, size and auth tests pass.

**Phase 3: invoice harness**

22. **FR-22 Invoice harness.**
    - **What:** invoice orchestration moves onto the pipeline as the second harness. The invoice-specific work stays domain code: Content Understanding read, line coding, rules and holds, the invoice transaction and commit gate, tracking and resubmission numbers, reason codes, firm notices and the approval lifecycle. `sprk_invoicesubmission` keeps the request role.
    - **Gate:** starts only after eBilling owner UAT (eBilling task 163) signs off.
    - **Acceptance:**
      - (a) eBilling's existing tests and end-to-end run pass unchanged.
      - (b) "A linked submission is never Failed" and resume-on-link hold under the run record.
      - (c) No `Services/Finance/Submission/` edit lands before sign-off.

### Non-Functional Requirements

- **NFR-01 At-least-once safety:** every stage and handler is idempotent per unit of work. Deterministic ids and keys, atomic per-message markers (ADR-004 A1 §4 / A2), and the terminal status written last.
- **NFR-02 Identity and authorization:**
  - background work is app-only, using the stamp's user-assigned managed identity;
  - no user token leaves its request (ADR-052:56);
  - approve, reject, status and admin endpoints use endpoint filters and fail closed (ADR-008, ADR-002);
  - MCP tools are OBO and caller-only.
- **NFR-03 Data governance (ADR-015):**
  - no document content in queue payloads, logs or errors;
  - staged blobs are stamp-private, readable only by the managed identity, and deleted by a lifecycle rule (TTL in Assumptions);
  - outputs follow `sprk_document` retention.
- **NFR-04 Abuse and cost guard:** a per-sender daily request cap at intake mailboxes (the ADR-039 Event-path budget). Mail over the cap is filed with no run and gets admin attention. Exchange spam filtering still applies.
- **NFR-05 Latency:** time from submission to AwaitingApproval for a typical NDA meets the Assumptions target (p95). The engine job has a hard timeout, after which the run is held with admin attention.
- **NFR-06 Observability:** `correlationId` runs end to end (BFF → queue → container job → completion). Stage transitions and outcomes (Completed, Failed, Poisoned) go to telemetry, and the DLQ is monitored.
- **NFR-07 BFF hygiene (root §10):**
  - publish-size delta measured against a fresh master build on every BFF-touching task (thresholds +5 MB / 55 MB / 60 MB);
  - no new HIGH CVE (BFF packages);
  - the container image is CVE-scanned in CI;
  - tests updated.
- **NFR-08 Kill switches:** each door and each harness can be disabled per environment without a redeploy (ADR-032 Null-Object; disabled → 503 ProblemDetails or a no-op enqueue).
- **NFR-09 Standard version:** the NDA standard is consumed at a pinned version, read from its stamp. Moving to a new version is a deliberate change in this project, and every run and output records the version.
- **NFR-10 Test-only data:** dev and CI use synthetic NDAs only. Real NDAs and accelerator `answer-key/` content are never readable by an agent or a test.

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API BFF; no Functions or Durable Task packages in the BFF assembly.
- **ADR-002**: No plugins; one server-side owner per record invariant (stage, current run, assigned attorney); clients preview only; security fails closed.
- **ADR-004** (+A1, **+A2 drafted**): Job Contract for all queue work; `IJobHandler` inside the BFF; A2 guarantees for the container consumer; no document bytes in payloads.
- **ADR-007**: SPE access only through `SpeFileStore`.
- **ADR-008**: Endpoint-filter authorization.
- **ADR-010**: DI minimalism; register concretes; interfaces only as real seams.
- **ADR-013**: AI through the PublicContracts facade; rate limiting on AI endpoints.
- **ADR-015**: AI data governance (minimum text, identifiers-only logs, retention, versioned prompts).
- **ADR-017**: Job status persisted; idempotent status updates; authorized status reads.
- **ADR-019**: ProblemDetails with stable `errorCode` and correlation id.
- **ADR-028**: Managed identity for outbound; A4 app-only row; no secrets; A5 impersonation only under its conditions (Job B Accept already impersonates the confirming user).
- **ADR-032**: Null-Object kill switches for conditional services.
- **ADR-036**: Only the `sprk_backgroundjob` description and docs are touched; no new scheduled job.
- **ADR-039**: Closed catalogues; harnesses call catalogued Actions only; Event-path budget; one confirmation gate for side effects.
- **ADR-043**: Execution spine for the app-only Actions.
- **ADR-045**: Server-side send through `CommunicationService`; the Association Engine on the normalized envelope; enrichment on both directions.
- **ADR-049**: Compose shadow-document rules (scope confirmed in ADR Tensions).
- **ADR-052**: Orchestrator in the BFF with the written reason; engines in an owner-approved Container Apps Job under Function-equivalent guardrails.

### MUST Rules (from ADRs)

- ✅ MUST implement BFF queue handlers as `IJobHandler`. The container consumer MUST meet ADR-004 A2: envelope, deterministic key plus atomic marker, `correlationId`, outcome, `MaxDeliveryCount` → DLQ.
- ✅ MUST mark receive-side idempotency atomically (SET NX or a conditional create/upsert); `IIdempotencyService` alone is not enough (#984).
- ✅ MUST persist status transitions and the final outcome for every run (ADR-017), with the terminal status written last.
- ✅ MUST record the host decision in the Placement Justification (ADR-052) and in design.md.
- ✅ MUST access SPE through `SpeFileStore`, and AI through PublicContracts facades.
- ✅ MUST use endpoint filters for approve, reject, status, admin and MCP endpoints; security fails closed.
- ✅ MUST return ProblemDetails with stable error codes; no content in errors.
- ✅ MUST register Null-Object kill switches for conditional services behind unconditional endpoints.
- ✅ MUST route harness AI only through catalogued Actions and Bindings (ADR-039), with Binding rows and `ConsumerTypes.All` in the same change.
- ❌ MUST NOT put document bytes, attachment bytes or email bodies in any queue payload, or log them.
- ❌ MUST NOT let a user token leave its request; MUST NOT call BFF endpoints from the container job; the container MUST NOT reference `Sprk.Bff.Api`.
- ❌ MUST NOT add Dataverse plugins or enforce invariants only in client code.
- ❌ MUST NOT add a hand-rolled timer `BackgroundService`.
- ❌ MUST NOT edit eBilling code (`Services/Finance/Submission/**`) before eBilling UAT sign-off.
- ❌ MUST NOT copy the accelerator engines. Build the image from `src/accelerators` on master. The engines keep Python 3.11, the standard library and `lxml` only. Only the entrypoint layer adds the hash-pinned Azure SDKs (FR-01).
- ❌ MUST NOT add a `.WithClientSecret(...)` site (ArchTests).

### Existing Patterns to Follow

- **Staged, resumable job:** `Services/Jobs/Handlers/InvoiceSubmissionProcessingJobHandler.cs` (lock, staged-blob read, final-delivery mapping) and `Services/Ai/Jobs/InvoiceSubmissionRerunJobHandler.cs` (resume from stored state). Copy the rules, not the invoice types.
- **Job framework:** `Services/Jobs/ServiceBusJobProcessor.cs`, `Services/Jobs/JobSubmissionService.cs`, `.claude/patterns/api/background-workers.md`.
- **Keyed handler registry:** `Services/Ai/ToolHandlerRegistry.cs:27`, `Services/Ai/Nodes/NodeExecutorRegistry.cs:29`.
- **Routing:** `Services/Ai/PublicContracts/IConsumerRoutingService.cs` (`RoutingContext` at `:364`), Binding rows in `infra/dataverse/sprk_playbookconsumer-rows.json`, and the drift check in `RoutingConsumerTypeHealthCheck.cs`.
- **App-only Action:** `Services/Ai/AppOnlyAnalysisService.cs` (`ProfileViaLinearActionAsync`) and the facade `Services/Ai/PublicContracts/InvoiceAi.cs`.
- **Task and Decision Record:** `Services/Ai/PublicContracts/IActionSeam.cs:44` (`CreateTaskAsync`); `DecisionRecordWriter` (`Infrastructure/DI/SignalsModule.cs:70`; used in `Services/Signals/GateDecisionRecordJob.cs`).
- **Proposal accept/dismiss:** `Services/Communication/CommunicationProposalApplyService.cs`, `Api/CommunicationEndpoints.cs:382,437`.
- **Shared-mailbox send:** `Services/Communication/CommunicationService.cs` (`SendMode.SharedMailbox`, `AttachmentDocumentIds`).
- **Front Door:** `Api/ExternalAccess/ExternalIntakeEndpoints.cs:110` and `ExternalMessageEndpoints.cs`.
- **MCP tools:** `Api/Mcp/LegalOperations/Decisions/*Tool.cs` and the `mcp-tool-handler` skill.
- **Blob access:** `Infrastructure/Storage/BlobGateway.cs` (managed-identity endpoint rules).
- **Kill switch:** `.claude/adr/ADR-032-bff-nullobject-kill-switch.md` patterns P1–P3.

## Placement & New Components (per CLAUDE.md §10 / §11)

### Hot-Path Declaration

Canonical copy: design.md §8.5 (added 2026-11-25). Skill directives = Y because ADR-004 A2 edits `.claude/constraints/jobs.md` (task 007).

```xml
<hot-path-declaration>
  <bff>Y</bff>
  <spaarkeai>Y</spaarkeai>
  <ci-workflows>Y</ci-workflows>
  <skill-directives>Y</skill-directives>
  <root-claude-md>N</root-claude-md>
</hot-path-declaration>
```

The External SPA (Front Door) is also touched (FR-13, FR-16).

**Placement Justification** (`.claude/constraints/bff-extensions.md`; ≤ 60 MB publish-size ceiling per task):
- **Orchestrator, harness registry, NDA harness (C# side), approval, status, run admin, doors:** in the BFF.
  - **ADR-052 B2:** every stage uses BFF domain code (routing, AI dispatch, write path, ownership, Decision Record).
  - **B3:** low volume.
  - **F5 is weak:** the only wait is one approval, persisted as a status.
  - **Written reason for a hand-rolled state machine:** design §4.
  - **Exit path:** ADR-052:79 behind an ADR-032 kill switch.
- **Redline engines:** in a Container Apps Job, owner-approved 2026-11-25 (ADR-052:76–77).
  - Heavy, custom runtime (Python + `lxml`); isolation for untrusted `.docx` (F4); no BFF code needed.
  - Guardrails: stamp managed identity, app-only, no BFF calls, per-stamp Bicep.
- **Status emails and email return:** in the BFF, through the existing `CommunicationService`.
- **MCP tools:** in the BFF's existing MCP server.

### New Components (§11 three-question gate)

| New component | Existing overlap (`notes/reuse-scan.md`) | Can extend instead? | Cost-of-doing-nothing (concrete failure) |
|---|---|---|---|
| Columns on `sprk_servicerequest` (channel, stage, assigned attorney, current run, send email updates) | Front Door request entity (`scripts/Set-ChildRecordCreatorContactSchema.ps1` adds request type / intake data) | **Yes**: extend the entity | Requests have no stage, attorney, run or notification choice; the status and approval FRs fail |
| `sprk_processingjob` request/output lookups, "Intake run" job type, stages | `Models/ProcessingJob.cs`, `Services/Office/JobStatusService.cs:42` | **Yes**: extend (owner decision) | No record per run; can't resume, re-drive or show status |
| Intake flag on `sprk_communicationaccount` | `sprk_autocreaterecords`, `sprk_accounttype` (`CommunicationAccountService.cs:72,165`) | No: neither means "takes requests"; overloading `sprk_accounttype` conflates owner with purpose | Mail to the legal mailbox never becomes a request |
| Orchestrator `IJobHandler` | `InvoiceSubmissionProcessingJobHandler.cs:71` (invoice-typed throughout) | No: the invoice code has no generic types and is frozen until UAT; copy its rules on the shared framework | Front Door, Console and email requests are never processed |
| `requestType` key on `RoutingContext` | `IConsumerRoutingService.cs:364` (only `MimeType`, `DocumentType`) | **Yes**: add a property | Can't route by request type |
| Harness contract and registry | Keyed-registry patterns (`ToolHandlerRegistry.cs:27`, `NodeExecutorRegistry.cs:29`); none for request work | No: different concept; copy the pattern | Each request type needs its own pipeline |
| Approve/reject endpoint | `IActionSeam.CreateTaskAsync` (`IActionSeam.cs:44`), `DecisionRecordWriter`; domain-specific approvals (`InvoiceApprovalEndpoints.cs:58`) | Partly: reuse the task seam and writer; the endpoint is new (the invoice approval is domain-bound) | Nothing can wait for an attorney and continue |
| Job B "Submit as request" proposal kind | `CommunicationProposalApplyService.cs` (field-update proposals, Accept/Dismiss/audit) | **Yes**: extend (owner reuse decision) | Requests sent to ordinary mailboxes never reach the pipeline unless re-sent |
| MCP submit and status tools | `Api/Mcp/LegalOperations/Decisions/*Tool.cs` (no MCP tool starts background work today) | No: new tools on the existing server, following the Decisions pattern | The Copilot/Teams door doesn't exist |
| Run admin view and DLQ re-drive | `Api/Admin/JobsEndpoints.cs:54` (scheduled jobs only), `DeadLetterQueueService.cs:14` (no caller) | **Yes**: extend `Api/Admin` and wire the existing service | Held or dead-lettered runs can't be seen or re-driven |
| Run-status API | `JobStatusService` (Office-only) | Partly: same record, intake-scoped read with request authorization | Doors and MCP can't show status |
| Blob staging (claim check) | `Infrastructure/Storage/BlobGateway.cs:148`; `StoredFileService` (finance-only) | **Yes**: use `BlobGateway`; finance staging stays separate | No ids-only hand-off to the engine job (ADR-004) |
| Engine Container Apps Job, Bicep, CI image | None: no Container Apps resource in `infra/` or `infrastructure/` | No: first of its kind (owner-approved) | No server-side marked-up NDA without a C# port |
| Status-email sender (per transition) | `CommunicationService` shared-mailbox send; `NotificationService` | **Yes**: compose existing send | Opted-in requesters get no updates (owner requirement) |

## ADR Tensions (per CLAUDE.md §6.5)

| ADR | Rule challenged | Conflict | Path | Rationale |
|---|---|---|---|---|
| ADR-052 | "Hand-rolled Service Bus + state machine needs a written reason"; the first Container Apps Job needs owner approval | A human-gated multi-step flow in the BFF; a Container Apps Job for the engines | **C** | Written reason recorded (design §4; Placement Justification above). Container Apps Job owner-approved 2026-11-25 under Function-equivalent guardrails |
| ADR-004 | "MUST NOT place document bytes or large blobs in payload" | The engine job needs the `.docx` and the plan | **C** | Claim check: ids-only messages; content in stamp-private, lifecycle-deleted blob storage |
| ADR-004 | "MUST implement handlers as `IJobHandler`" (A1 makes every Service Bus consumer ADR-004 work) | The container consumer can't reference `Sprk.Bff.Api`, where `IJobHandler` lives (ADR-052:71). The same gap applies to every future Function | **B** | Amendment **A2** (owner-approved 2026-11-25, drafted on this branch): `IJobHandler` scoped to the BFF; equivalent guarantees for ADR-052 hosts. Merges before or with FR-01 code (FR-04) |
| ADR-049 | I-5 "route ALL save/annotation writing through `ComposeShadowPatchEngine`"; I-7 "no text-search in the write path" | The NDA engines author tracked changes and comments and anchor on exact text | **C** | ADR-049 governs the Compose shadow-document save path. The NDA outputs are new documents that are never authored through Compose. The engine's anchoring (text must occur exactly once, on word boundaries, never across a revision, else refused) is not the fuzzy placement I-7 forbids. Confirm in `/adr-check` (Unresolved Question 2) |
| ADR-028 A5 / ADR-052 identity | Acting with no user present | Background runs and returns | **C** | App-only through the stamp's managed identity, attributed to the request and the assigned attorney. Sends only after human approval. Job B Accept impersonates the confirming user, as today |
| ADR-039 | Closed catalogues; Event-path budget; one confirmation gate for side effects | Background AI runs with no chat turn; an outbound send | **C** | Harnesses call catalogued Actions only. The per-sender cap is the Event-path budget. Attorney approval is the confirmation before the send |
| ADR-045 | "All programmatic send through `sendCommunication()`" | Server-originated status emails and returns | **C** | Server sends go through `CommunicationService` (the server side of the canonical send, as eBilling's notices do); enrichment runs on outbound |
| ADR-002 | One server-side owner per invariant; no client-only enforcement | Stage, current run and assigned attorney must stay consistent | **C** | The orchestrator and approve endpoint own those writes. Clients (Console, MDA, SPA) only call the BFF |

## Success Criteria

1. [ ] Engines run in the Container Apps Job with output identical to a local run on the golden fixtures; image CVE-clean; latency recorded. Verify by: the FR-01 spike report plus CI image-scan output.
2. [ ] App-only review writes schema-valid edit plans on 3 synthetic NDAs. Verify by: schema validation output and topic-id check (FR-02).
3. [ ] The Phase 0 cleanup PR is merged, #3162 filed, ADR-004 A2 on master. Verify by: PR links, tests green, threaded test send (FR-03, FR-04).
4. [ ] An NDA submitted on the Front Door on dev comes back marked up after attorney approval, with status emails when opted in and Decision Records for the approval. Verify by: dev end-to-end run with screenshots, Decision Record rows, received emails (FR-07–FR-13).
5. [ ] Resume and redelivery are safe: crash injection at each stage and duplicate delivery produce no re-run AI and no duplicates. Verify by: integration tests (FR-07, NFR-01).
6. [ ] The same NDA round-trips through an intake mailbox (threaded reply), through an accepted Job B suggestion, and through the MCP agent; no request is created at an ordinary mailbox without Accept. Verify by: dev end-to-end runs and negative tests (FR-17–FR-21).
7. [ ] The invoice harness passes eBilling's existing tests and end-to-end run unchanged, after UAT sign-off. Verify by: eBilling suite plus end-to-end report (FR-22).
8. [ ] Every BFF-touching task reports the publish-size delta against a fresh master build, with no new HIGH CVE. Verify by: PR descriptions (NFR-07).

## Dependencies

### Prerequisites

- **PR #3163** (accelerator: `src/accelerators` engines, schemas and standards on master) merged before the FR-01 image build.
- **ADR-004 A2** merged before or with FR-01 code (FR-04).
- **eBilling owner UAT sign-off** (eBilling task 163) before any Phase 3 task.
- **Ontology r1** agreement on its `sprk_servicerequest` columns (direction, disposition, response-due date) before FR-05.
- **Provisioning:** a Container Apps environment and Job, registry and staging storage per stamp; Exchange RBAC for Applications scoping of `Mail.Send`/`Mail.Read` on the shared legal mailbox.

### External Dependencies

- **Accelerator r1:** engine JSON in/out CLI contract (it will flag changes before they merge); the pinned NDA standard "Spaarke NDA – Mutual" v1.2 and its version stamp.
- **specialist-legal-agents-r1:** later supplies the NDA specialist and attorney profiles; T3 master of the standard.
- **External Access (Front Door):** request record, document upload, message thread and bell.
- **mcp-r1 / Spaarke MCP server:** host for the new tools.
- **Customer provisioning r1:** per-stamp provisioning step.
- **Counsel:** ratification of the NDA standard before any customer rollout (not a dev-phase blocker).
- **Azure:** Container Apps (Jobs, KEDA Service Bus scaler), Service Bus, Storage lifecycle management, Microsoft Graph `createReply`.

## Owner Clarifications

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Orchestration host (design Q2) | BFF jobs plus run record, or Durable Task? | BFF ADR-004 jobs with `sprk_processingjob` as the state machine; written ADR-052 reason | FR-07; Placement Justification |
| Engine host (Q3) | Python engines in a Container Apps Job or a C# port? | Container Apps Job, with conditions; ADR-052:77 approval given | FR-01; Bicep and CI work; ADR-004 A2 |
| Invoice timing (Q4) | When does invoice move? | Phase 3, after eBilling UAT, before customer rollout; no eBilling edits until then | FR-22 gate; MUST NOT rule |
| Sending identity (Q5) | Shared legal mailbox or attorney's mailbox? | Shared legal mailbox, app-only, as the queued Return stage; attorney named and Cc'd | FR-20, FR-12 |
| NDA standard (Q6) | Which standard, owned by whom? | Accelerator "Spaarke NDA – Mutual" v1.2, consumed at a pinned version; owned by the accelerator; counsel ratification before customer rollout | NFR-09; FR-02 topic ids |
| Email door scope (Q7) | Intake mailbox only, or also existing mailboxes? | Both: intake mailboxes create requests; elsewhere suggest-only | FR-17, FR-18 |
| §10 cleanup | Fix or file each finding? | Phase 0 fixes in one PR (1b, 2, 4, 5a, 6, 7, 8-docs); Phase 1: 3, 8-description; Phase 2: 1a; 5b filed as #3162 | FR-03, FR-15, FR-06, FR-19 |
| ADR-004 | Amend for non-BFF consumers? | Path B: A2 drafted now | FR-04; ADR Tensions |
| r1 scope | Which phases does r1 cover? | Phases 0–3; Phase 4 → r2 | In/Out of Scope |
| Approve UI | Where does the attorney approve? | Both: Console Work Item and MDA service-request form, one endpoint | FR-11; SpaarkeAi = Y; MDA command |
| Attorney assignment | How does a request get its attorney? | Team queue, then claim: the owning legal team's Work Item; claim or approve sets the assigned attorney | FR-05, FR-11 |
| No harness / low confidence | What happens? | Manual triage: Needs triage stage, Work Item to the legal team, no AI output, run Completed (no-op) | FR-10 |
| Intake senders | Whose mail creates a request? | Anyone | FR-17; NFR-04 abuse guard |
| Prior markup | Drafts with tracked changes? | Preserve all existing redlines and comments; never accept, reject or clobber them | FR-09; Unresolved Question 1 |
| Rejection / status | What does the requester get? | Every step updates status; decisions go in the Decision Record; a per-request "send email updates" option emails each status change | FR-05, FR-11, FR-12 |
| Reuse: suggestion surface | Triage hint plus command, or Job B proposal queue? | Job B proposal queue (new proposal kind; triage supplies the signal) | FR-18; §11 row |
| Accelerator coordination | Ownership and engine source | Standard stays accelerator-owned, pinned here; engines used from master after #3163, never copied; Python 3.11, stdlib + `lxml` | NFR-09; MUST NOT rule; FR-01 |
| Engine wrapper dependencies (2026-11-25) | Wrapper on stdlib REST, or Azure SDKs? | Take the correct technical approach: the wrapper layer uses hash-pinned `azure-identity`, `azure-servicebus`, `azure-storage-blob`. Explicit dead-letter with reason, lock renewal past the 5-minute maximum lock for runs up to the 10-minute timeout, and maintained token and retry handling are A2 requirements that hand-written REST would re-implement in security-sensitive code. The engines stay stdlib + `lxml`; the image is CVE-gated (task 003) | FR-01; task 001 |

## Assumptions

- **Latency:** p95 ≤ 10 minutes from submission to AwaitingApproval for an NDA ≤ 30 pages and ≤ 5 MB; engine job timeout 10 minutes. Affects NFR-05, FR-01 measurements, the job timeout.
- **Volume:** ≤ 200 requests per day per stamp. Affects KEDA scaling limits and queue sizing.
- **Per-sender cap:** 20 requests per sender per day per stamp, configurable. Over the cap, mail is filed with no run and gets admin attention. Affects NFR-04, FR-17.
- **Staging TTL:** 7 days (past any retry or dead-letter window). Affects the Bicep lifecycle rule, NFR-03.
- **"Send email updates" default:** ON for email-door requests, OFF for Front Door (which has the portal and bell) and Console; changeable on the request. Affects FR-05, FR-12.
- **Triage threshold:** below 0.85 classification confidence the request goes to manual triage (the `agreement-classify` confirm threshold). Affects FR-10.
- **Status emails** contain status and a link, never document content; returned documents go only through the Return stage. Affects FR-12.

## Unresolved Questions

- [ ] **Clean copy with prior markup.** The engine's Clean output "accepts all changes", which would accept the counterparty's revisions; the owner requires that they are never accepted. Options: the engine accepts only our revisions in Clean (an accelerator engine change), or Clean is omitted for drafts with prior markup. Blocks: FR-09 acceptance (e); settle in the Phase 0 spike with the accelerator.
- [ ] **ADR-049 scope confirmation.** Confirm in `/adr-check` that I-5/I-7 govern only the Compose save path. Blocks: FR-09 code review sign-off.
- [ ] **Ontology columns on `sprk_servicerequest`.** Agree names and ownership of direction, disposition and response-due date with ontology r1 so FR-05 doesn't collide. Blocks: FR-05 schema task.

---

*AI-optimized specification. Original design: design.md*
