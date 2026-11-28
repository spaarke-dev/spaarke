# One intake process model: proposal (2026-11-24)

> **Status:** proposal for owner discussion. Nothing decided beyond the owner decisions in §0. Not a spec.
> **Evidence:** three read-only Fable code reviews of origin/master `b005279c4d` (invoice intake; content intake paths; routing, run records, outputs and return), with the key claims re-checked by hand. Paths are relative to `src/server/api/Sprk.Bff.Api/` unless stated.
> **Evidence record** for `../design.md` (copied from `specialist-legal-agents-r1` on 2026-11-25).
> **Related:** `notes/nda-current-state-2026-11-24.md` §5 and §8.

## 0. Owner decisions this builds on (2026-11-24)

- **`sprk_servicerequest` is the request entity.** `spaarke-legal-front-door-r1` is not going forward, and no `sprk_legalrequest` will be created. In the ontology, an Inquiry is a different concept from a Service Request.
- **One process model** for content intake, as far as possible.
- **A separate processing record** is probably needed for routing and orchestration, and because one request can have several runs (an NDA negotiation: each revised draft is a new run).
- For NDA, the output is a **Word file plus a clause table**. Compose is not the front end.

## 1. What exists today: six intake paths, no shared model

| Path | Arrives | Stores | Chooses the AI work by | Analyses | Run record |
|---|---|---|---|---|---|
| **Email** | Graph webhook + polling + delta backup (`IncomingCommunicationJobHandler`) | `sprk_communication`, a `sprk_document` per attachment, `.eml` archive | Association rungs decide the *regarding*; the AI work is always the default profile | `AppOnlyDocumentAnalysis` + RAG per document | None |
| **ACS messaging** | Event Grid → `IncomingMessaging` → `ICommunicationChannelIngestor` | `sprk_communication` | — | None | None |
| **Console chat upload** | Authenticated POST (`ChatDocumentEndpoints`) | Chat session (Redis); optional SPE file; no row | **Event bindings** (`document_uploaded → chat-classify`) | Inline, SSE | None |
| **Office add-in save** | `POST /api/office/save` → `office-upload-finalization` | `sprk_document` | Default profile | Profile + RAG + Insights | **`sprk_processingjob`** |
| **External SPA upload** | `POST /api/v1/external/{…}/{id}/documents` | `sprk_document` | Default profile | Profile + RAG | None |
| **Front Door** | `POST /api/v1/external/servicerequests` | `sprk_servicerequest` (type + answers) | — | **None. Nothing runs after creation** | None |
| **Invoice submission** (separate domain) | SPA / internal / from-document / from-email routes | `sprk_invoicesubmission`, then the invoice | The route itself says "invoice" | Content Understanding read + line coding (Actions via `IInvoiceAi`) | The submission row (one run each) |

**What the paths already share in code:**
- the job framework (`IJobHandler`, `JobContract`, `JobOutcome`, `JobSubmissionService`);
- `IIdempotencyService` (a lock owned by the job);
- the app-only profile tail (`AppOnlyDocumentAnalysisJobHandler` → `AppOnlyAnalysisService`, with four producers);
- RAG indexing (`IPostUploadIndexingEnqueuer`);
- routing (`IConsumerRoutingService`, the Binding table, `ConsumerTypes`);
- ownership (`IRecordOwnershipResolver`, `RecordContainerResolver`).

**Where they diverge:**
- five queues;
- two profile idempotency-key formats (`DocAnalysis:{id}` for email, `analysis-{id}-documentprofile` for the others);
- different retry ceilings;
- only one path has a run record (Office);
- only one path chooses its AI work from content (chat);
- email bypasses the channel seam that ACS messaging uses.

**So today there is no single process model.** There is a shared *tail* (profile and index a document) behind several separate *front halves*.

## 2. Can the invoice machinery be reused? Mostly as a pattern, not as code

The invoice pipeline (`InvoiceSubmissionProcessingJobHandler`, `Services/Finance/Submission/`) is well built but written against invoice types throughout.
- **No generic types.** There is no generic base class, interface or type parameter. The processor, persister and after-steps interfaces take and return invoice types. The commit gate undoes by deleting invoice rows.
- **Size.** `Submission/` is about 20,000 lines; roughly 5–10% of it is reusable orchestration logic, and none of that is packaged as a reusable type.
- **It does not fit our model in three ways:**
  - one submission is one run (a re-attempt is a new row; the one in-place rerun is a purpose-built job that keeps its state in JSON);
  - there is no classify → route step;
  - approval is a later invoice lifecycle, not a pipeline stage.
- **Generalising live eBilling code is risky.** Its invariants are tied to the invoice link ("a linked submission is never Failed", "resume on link"). Guard tests pin it by name, and it has been through many adversarial fix rounds.

| Use | Pieces |
|---|---|
| **Reuse as is** | `IJobHandler` / `JobContract` / `JobOutcome`; `IIdempotencyService`; `IGenericEntityService` (transactions, row-version-conditional writes); `NotificationService`; the Signals and Decision Record writers; `SourceDocumentFileRead` |
| **Reuse with a change** | `StoredFileService` staging + scan verdict. The mechanism is general, but its purpose and parent enums are finance-only and it lives under `Services/Finance/Files` |
| **Copy the pattern** | Step 0: payload → lock → re-read → terminal-first → feature flag → resume from stored state. Also copy the following rules: <ul><li>"Processing" is written before any AI call;</li><li>the work is split into analysis that writes nothing, one transaction, then idempotent after-steps with the terminal status written last;</li><li>deterministic ids;</li><li>AI is never re-run on a resume;</li><li>the final-delivery outcome is mapped;</li><li>the requester gets exactly one notice;</li><li>a marker is written before any send;</li><li>intake order is stage → row → enqueue;</li><li>admin-attention text is written for anything held</li></ul> |
| **Leave alone** | Everything invoice-specific (processor, persister, after-steps, commit gate, tracking numbers, rerun, `IInvoiceAi`, reason codes, firm notices) |

**Answer to the owner's question:** build the shared orchestrator new, on the shared framework pieces, copying the invoice pipeline's rules. Don't refactor eBilling into it now. Later, invoice intake can become one *harness* of the shared model, if it's ever worth the re-proof.

## 3. The proposed model

### 3.1 Four records

| Record | Entity | Holds |
|---|---|---|
| **Request** (the business object) | `sprk_servicerequest` (existing) | Requester, type, intake answers, direction (exists). Add: channel, stage (submitted → in review → awaiting approval → returned → closed), assigned attorney, current run |
| **Run** (one pass through the process) | **Proposed: extend `sprk_processingjob`** (§3.3) | Request lookup, input document, status, stages JSON, current stage, payload, result, idempotency key, correlation id, error, retries, initiated by |
| **Content** | `sprk_document` (existing) | The submitted file(s) and every generated output (the redlined `.docx`). Filed against the request, so the requester can already see and download them in the Front Door |
| **Results** | `sprk_analysis` + `sprk_analysisoutput` (existing) | Structured AI results per analysis (classification, review findings, clause table data), linked to the run |

A negotiation is one request with several runs: each revised draft is a new `sprk_document` and a new run.

### 3.2 One process, ten stages

```
 Doors (adapters)                 email · Front Door · Copilot agent (Teams via the agent) · Console upload
   │  create or find the Request, file the content, create a Run, enqueue ONE job
   ▼
 Run orchestrator (one ADR-004 job type, invoice step-0 rules)
   1 Receive   2 Store   3 Classify   4 Route ──► harness for (request type + document type)
                                                   │  e.g. NDA → the NDA specialist
   5 Analyse   6 Build outputs   7 Persist (one transaction)
   8 Approve   ── Work Item to the assigned attorney; run waits (status AwaitingApproval)
                  attorney approves → endpoint resumes the run at stage 9
   9 Return    ── through the door it came in by
  10 Record    ── Decision Record (approval), run Completed, request stage updated, requester notified once
```

- **Doors** stay thin. They identify the requester, file the content and enqueue the run. They hold no AI.
- **Routing** reuses the Binding table. `RoutingContext` today matches only `mimeType` and `documentType`, so it gets a `requestType` key, and the classification supplies `documentType`.
- **A harness** is a registered handler for stages 5–6 for one kind of work (NDA review, later invention, trademark, policy, maybe invoice). The orchestrator owns everything else, so a new request type adds a harness and a Binding row, not a pipeline.
- **Approval** is new. No "approve, then resume" mechanism exists today.
  - The Work Item uses the existing task seam (`IActionSeam.CreateTaskAsync`, `sprk_event` tasks) regarding the request.
  - The approve action writes the Decision Record (`DecisionRecordWriter`) and re-enqueues the run at stage 9.
  - The attorney edits the `.docx` in Word before approving; the approved version is the one returned.

### 3.3 The run record: extend `sprk_processingjob` (needs an owner decision)

`sprk_processingjob` is already a generic staged run record:
- columns: status (Pending, InProgress, Completed, Failed, Cancelled), `sprk_stages` JSON, `sprk_currentstage`, `sprk_stagestatus`, `sprk_progress`, `sprk_payload`, `sprk_result`, `sprk_idempotencykey`, `sprk_correlationid`, error code and message, retry count, `sprk_document`, `sprk_initiatedby`;
- a status read and stream API used by the Office add-in.

It lacks a request lookup, and its job-type choice lists only Office jobs.

**One caution.** The Dataverse description of `sprk_backgroundjob` says `sprk_processingjob` "is Office-scoped — do NOT overload that entity". That note separates scheduler definitions from Office runs, but it is a recorded guard, so overriding it is the owner's call.

| Option | For | Against |
|---|---|---|
| **A. Extend `sprk_processingjob`** (recommended): add `sprk_servicerequest`, an output-document lookup, job type "Intake run" and the stage values | Reuse (§11); the status API and the Office job already prove the shape | Overrides the "Office-scoped" note; Office and intake runs share one table, separated by job type |
| B. A new run entity | Clean separation | Fails the §11 "extend instead" test while A is viable |
| C. Use `sprk_analysis` as the run | Exists | It is one AI analysis, anchored on a document, with ownership derived from that document; a run has several analyses and an approval |

## 4. NDA as the first harness: what exists and what's new

| Stage | Reuse | New |
|---|---|---|
| 3 Classify | `agreement-classify` Action + `sprk_agreementtype` registry | **App-only path.** Today both NDA Actions run only through `SessionDispatchOrchestrator` (a chat session). The registry-token step must be callable from a job. No `ConsumerTypes` constant exists for `agreement-classify` or `nda-review` |
| 5 Analyse | `agreement-review` (`nda-review` binding); later the specialist with the attorney's profile | App-only dispatch. Its output is `{overallRisk, flaggedSections[{sectionRef, quotedText, riskLevel, flaggedClause, assessment, standardRef}]}`, with **no replacement wording and no paragraph anchors** |
| 6 Build | `ComposeShadowPatchEngine.Apply`, pure bytes in and out, session-free: it writes native tracked changes and comments from operations anchored by `(paraId, run, offset)`. `ComposeSummaryPageGenerator` (deterministic). `ComposeDocumentRenderer.AppendSection` (supports table blocks, per its comment) | **Findings → anchors on the server:** `ComposeAnchorResolver` exists but has no production caller, and ADR-049 forbids text search in the Compose write path; check whether that rule covers this path. A **propose-wording** pass. A **clause table** builder (none exists). **Policy for drafts that already carry redlines:** the engine refuses edits inside existing tracked changes |
| 7 Persist | `sprk_analysis` / `sprk_analysisoutput`, `sprk_document` | Link both to the run |
| 8 Approve | Task seam, `DecisionRecordWriter` | Approve-then-resume |
| 9 Return | `CommunicationService` shared-mailbox send. It is app-only and takes `AttachmentDocumentIds` (existing documents, 35 MB total). Front Door requesters already see documents filed on their request | Reply threading: the outbound mail may not carry In-Reply-To (unconfirmed). Front Door: the bell fires on *messages* regarding a request, not on documents, so post a message with the return |

**Sending identity, for the owner.** There are two options:
- the shared legal mailbox, app-only: exists and is the same for every door;
- the attorney's own mailbox: needs their sign-in at approval, and in-memory attachments only work on that path.

The shared mailbox is the simpler fit for one model.

## 5. Bringing the existing paths into the model

This proposal does **not** rewrite working paths.
- **New, request-based work runs on the model from the start:** NDA first, then the other Front Door types.
- **Existing paths join step by step:**
  - Email and the External upload gain a door that creates a request when the content is a request (for example an NDA sent to the legal mailbox).
  - Their profile and index tail stays as it is.
  - Office save keeps its run record, which becomes the shared one.
  - Invoice intake stays its own domain until a re-proof is worth it.

Whether ordinary filing (an email attachment that is just correspondence) should ever create a run is the owner's call. The recommendation is no: a run exists only for a request.

## 6. Findings to report (not caused by this work)

The reviews found these on master `b005279c4d`. None is fixed here. Each should be filed or confirmed by its owner (root CLAUDE.md §8.5).

1. **Four job handlers have no producer anywhere in `src`:** `AttachmentClassification`, `ProfileSummary`, `EmailAnalysis`, and `DocumentProcessing` (which calls itself a sample). Either dead code or producers outside the repo.
2. **`StartupDiagnostics.cs:84`** expects handlers `ProcessEmailToDocument` and `BatchProcessEmails`, which do not exist.
3. **`DeadLetterQueueService`** has list and re-drive methods and is registered, but nothing calls it.
4. **Doc drift.** `docs/architecture/DOCUMENT-PROFILE-AND-AI-EXECUTION-MODELS.md` (`:129`, `:177`, `:265`) still describes the app-only profile as the node playbook. The code (`AppOnlyAnalysisService.cs:639`) runs it as a direct Action.
5. **Email bypasses the `ICommunicationChannelIngestor` seam** that ACS messaging uses. The profile idempotency key also differs between email and the other paths.
6. **`agreement-classify` and `nda-review` have Binding rows but no `ConsumerTypes.All` entry.** Per `ConsumerTypes.cs:367-369`, the routing health check may report them Unhealthy (not checked).
7. **Outbound "reply" mail may not set In-Reply-To**, so recipients' clients may not thread it (inferred, not confirmed).
8. **`sprk_backgroundjobrun`** is deployed but unused, per its data-model doc.

## 7. Decisions for the owner

1. **Run record:** extend `sprk_processingjob` (A, recommended), a new entity (B), or `sprk_analysis` (C)?
2. **Sending identity for returns:** the shared legal mailbox (recommended) or the attorney's own mailbox?
3. **Scope of "one model":** new request-based work on the model now, existing paths step by step (recommended), or migrate the existing paths as part of the first project?
4. **Findings in §6:** file them as issues for their owning areas?
5. Still open from earlier:
   - which NDA standard is the master;
   - where this lives (this program's Phase 1, or its own platform project; the model is shared platform work, so it may deserve its own project with NDA as the first harness).
